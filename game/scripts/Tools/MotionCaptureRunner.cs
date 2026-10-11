using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cloverbrook.Game.Scenes;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// Reproducibly samples a real horse animation at 24 fps and saves the individual PNG
/// frames. These are frames of evaluated animation, not a screenshot presented as video.
/// Example: godot --path game res://scenes/motion_capture.tscn -- --out ../motion/adult_walk_side --age adult --animation walk --view side --loops 2
/// Blend check: godot --path game res://scenes/motion_capture.tscn -- --out ../motion/transitions --age both --animation transitions
/// Assemble with: ffmpeg -framerate 24 -i frame_%05d.png -c:v libx264 -pix_fmt yuv420p preview.mp4
/// </summary>
public partial class MotionCaptureRunner : Node
{
    private HorseStage _stage = null!;
    private string _out = "user://motion";
    private const int Fps = 24;

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        _out = Arg("--out", _out);
        string age = Arg("--age", "adult").ToLowerInvariant();
        string animation = Arg("--animation", "walk").ToLowerInvariant();
        string view = Arg("--view", "side").ToLowerInvariant();
        int loops = int.TryParse(Arg("--loops", "2"), out var parsedLoops) ? Math.Clamp(parsedLoops, 2, 10) : 2;
        int failures = 0;
        DirAccess.MakeDirRecursiveAbsolute(_out);
        GetWindow().Size = new Vector2I(960, 540);
        _stage = new HorseStage { Name = "Stage" };
        AddChild(_stage);
        _stage.SetMode(LightingMode.Neutral);

        if (animation == "transitions")
        {
            var ages = age == "both" ? new[] { "adult", "foal" } : new[] { age };
            var checks = new List<object>();
            foreach (string transitionAge in ages)
            {
                if (transitionAge is not ("adult" or "foal"))
                {
                    failures++;
                    continue;
                }
                var check = await CheckTransitions(transitionAge);
                checks.Add(check.Report);
                failures += check.Failures;
            }
            WriteReport(new
            {
                format = "cloverbrook.animation-transition-check.v1",
                sequence = new[] { "idle", "walk", "trot", "idle" },
                blendSeconds = 0.18,
                sampleRate = Fps,
                playback = "AnimationPlayer manual playback; each clip switch uses its actual Play blend and each sample advances 1/24s.",
                ageChecks = checks,
                failures,
            });
            GetTree().Quit(failures == 0 ? 0 : 1);
            return;
        }

        var descriptor = FixtureLibrary.Get("bay", age == "foal");
        var support = _stage.Presenter.Present(descriptor, animation);
        var animator = _stage.Presenter.Animator;
        if (age is not ("adult" or "foal") || !support.Supported || animator == null || !animator.HasAnimation(animation))
        {
            GD.PushError($"[motion] unavailable: age={age}, animation={animation}, supported={support.Supported}");
            WriteReport(new { format = "cloverbrook.motion-capture.v1", age, animation, failures = 1, error = "Requested age or animation is unavailable." });
            GetTree().Quit(1);
            return;
        }

        var clip = animator.GetAnimation(animation);
        if (clip.Length <= 0)
        {
            GD.PushError($"[motion] animation has invalid duration: {animation} ({clip.Length})");
            WriteReport(new { format = "cloverbrook.motion-capture.v1", age, animation, failures = 1, error = "Animation duration must be positive." });
            GetTree().Quit(1);
            return;
        }

        _stage.FrameView(view);
        // Keep motion-review side views large enough to judge joint deformation.
        // The public HorseStage/OrbitCamera default remains unchanged for the viewer.
        if (view == "side")
        {
            bool foal = descriptor.IsFoal;
            float scale = foal ? 0.66f : 1f;
            _stage.Camera.SetView(0, 4, 3.15f * scale, new Vector3(0.05f * scale, 1.02f * scale, 0));
        }
        var skeleton = FindSkeleton(_stage.Presenter.Model!);
        if (skeleton == null)
        {
            GD.PushError("[motion] no Skeleton3D in requested model");
            WriteReport(new { format = "cloverbrook.motion-capture.v1", age, animation, failures = 1, error = "No Skeleton3D found." });
            GetTree().Quit(1);
            return;
        }

        var tracks = Enumerable.Range(0, clip.GetTrackCount()).Select(i => new
        {
            path = clip.TrackGetPath(i).ToString(),
            type = clip.TrackGetType(i).ToString(),
            keyCount = clip.TrackGetKeyCount(i),
        }).ToArray();
        var firstPose = new Vector3[skeleton.GetBoneCount()];
        var previousPose = new Vector3[skeleton.GetBoneCount()];
        var previousRotations = new Quaternion[skeleton.GetBoneCount()];
        var startRotations = new Quaternion[skeleton.GetBoneCount()];
        var endPositions = new Vector3[skeleton.GetBoneCount()];
        var endRotations = new Quaternion[skeleton.GetBoneCount()];
        var boneNames = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBoneName).ToArray();
        double longestDelta = 0;
        double largestRotationDelta = 0;
        double firstWrapTranslation = 0;
        double firstWrapRotation = 0;
        double firstWrapTime = -1;
        bool finite = true;
        // Include the first 24-fps sample at or beyond the requested loop count.
        int frameCount = (int)Math.Ceiling(clip.Length * loops * Fps) + 1;
        animator.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        animator.Play(animation, 0.0);
        animator.Seek(0.0, true);
        for (int frame = 0; frame < frameCount; frame++)
        {
            double seconds = (double)frame / Fps;
            double localTime = seconds % clip.Length;
            await WaitForDraw();
            var current = ReadPose(skeleton);
            finite &= current.All(p => p.Finite);
            bool crossedLoopBoundary = frame > 0 && Math.Floor(seconds / clip.Length) > Math.Floor(((double)frame - 1) / Fps / clip.Length);
            for (int bone = 0; bone < current.Length; bone++)
            {
                var position = current[bone].Position;
                var rotation = current[bone].Rotation;
                if (frame == 0)
                {
                    firstPose[bone] = position;
                    startRotations[bone] = rotation;
                }
                else
                {
                    longestDelta = Math.Max(longestDelta, position.DistanceTo(previousPose[bone]));
                    largestRotationDelta = Math.Max(largestRotationDelta, rotation.AngleTo(previousRotations[bone]));
                    if (crossedLoopBoundary)
                    {
                        firstWrapTranslation = Math.Max(firstWrapTranslation, position.DistanceTo(previousPose[bone]));
                        firstWrapRotation = Math.Max(firstWrapRotation, rotation.AngleTo(previousRotations[bone]));
                    }
                }
                previousPose[bone] = position;
                previousRotations[bone] = rotation;
            }
            if (crossedLoopBoundary && firstWrapTime < 0) firstWrapTime = seconds;
            var image = GetViewport().GetTexture().GetImage();
            string file = $"frame_{frame:00000}.png";
            Error saved = image.SavePng(_out.PathJoin(file));
            if (saved != Error.Ok)
            {
                GD.PushError($"[motion] failed saving {file}: {saved}");
                failures++;
            }
            if (frame == frameCount - 1)
                for (int bone = 0; bone < current.Length; bone++)
                {
                    endPositions[bone] = current[bone].Position;
                    endRotations[bone] = current[bone].Rotation;
                }
            // Advance the AnimationPlayer itself so sampling exercises real loop playback,
            // rather than repeatedly seeking and pausing a set of unrelated still poses.
            if (frame + 1 < frameCount) animator.Advance(1.0 / Fps);
            if (frame % Fps == 0) GD.Print($"[motion] {age}/{animation}: {frame}/{frameCount} at {seconds:0.000}s");
        }

        // Compare the loop's last 24-fps sample to its first sample to reveal a poor seam.
        double finalToStartPosition = 0, finalToStartRotation = 0;
        for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
        {
            finalToStartPosition = Math.Max(finalToStartPosition, endPositions[bone].DistanceTo(firstPose[bone]));
            finalToStartRotation = Math.Max(finalToStartRotation, endRotations[bone].AngleTo(startRotations[bone]));
        }
        if (!finite) failures++;
        bool changing = longestDelta > 0.0001 || largestRotationDelta > 0.0001;
        if (!changing)
        {
            failures++;
            GD.PushError("[motion] skeleton pose did not change across the sampled animation");
        }

        WriteReport(new
        {
            format = "cloverbrook.motion-capture.v1",
            age,
            meshFamilyId = descriptor.MeshFamilyId,
            animation,
            durationSeconds = clip.Length,
            loops,
            fps = Fps,
            frames = frameCount,
            view,
            resolution = new { width = 960, height = 540 },
            sampling = "AnimationPlayer.Play followed by Advance(1/24) between captures; every PNG waits for RenderingServer.FramePostDraw.",
            frameFiles = new { pattern = "frame_%05d.png", first = "frame_00000.png", last = $"frame_{frameCount - 1:00000}.png" },
            skeleton = new { boneCount = boneNames.Length, bones = boneNames },
            tracks,
            pose = new { coordinateSpace = "Skeleton3D local bone pose (GetBonePose)", allFinite = finite, changed = changing, maxPerFrameBoneTranslationMeters = longestDelta, maxPerFrameBoneRotationRadians = largestRotationDelta },
            loopSeam = new
            {
                lastSampleLocalTimeSeconds = (double)(frameCount - 1) / Fps % clip.Length,
                offsetFromClipEndSeconds = clip.Length - ((double)(frameCount - 1) / Fps % clip.Length),
                sampledWrapAtSeconds = firstWrapTime,
                sampledWrapStepSeconds = firstWrapTime < 0 ? (double?)null : 1.0 / Fps,
                maxBoneTranslationMetersAcrossWrapStep = firstWrapTranslation,
                maxBoneRotationRadiansAcrossWrapStep = firstWrapRotation,
                finalFrameToStartTranslationMeters = finalToStartPosition,
                finalFrameToStartRotationRadians = finalToStartRotation,
                exactExportedEndpointSeam = "Not measured here; check the animation export validation report for the t=0 and t=duration key poses."
            },
            videoAssembly = "ffmpeg -framerate 24 -i frame_%05d.png -c:v libx264 -pix_fmt yuv420p preview.mp4",
            failures,
        });
        GD.Print($"[motion] wrote {frameCount} frames; failures={failures}");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private async Task WaitForDraw()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private async Task<(object Report, int Failures)> CheckTransitions(string age)
    {
        int failures = 0;
        var descriptor = FixtureLibrary.Get("bay", age == "foal");
        var support = _stage.Presenter.Present(descriptor, "idle");
        var animator = _stage.Presenter.Animator;
        if (!support.Supported || animator == null)
            return (new { age, supported = false, error = support.Summary }, 1);

        var required = new[] { "idle", "walk", "trot" };
        var missing = required.Where(name => !animator.HasAnimation(name)).ToArray();
        if (missing.Length > 0)
            return (new { age, supported = true, missingClips = missing }, missing.Length);

        var skeleton = FindSkeleton(_stage.Presenter.Model!);
        if (skeleton == null) return (new { age, error = "No Skeleton3D found." }, 1);
        animator.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        var clipChecks = new List<object>();
        foreach (string clipName in required)
        {
            animator.Play(clipName, 0.0);
            animator.Seek(0.0, true);
            await WaitForDraw();
            var previousClipPose = ReadPose(skeleton);
            bool clipFinite = previousClipPose.All(p => p.Finite);
            bool clipChanged = false;
            double clipMaxTranslation = 0, clipMaxRotation = 0;
            int sampleCount = Math.Max(1, (int)Math.Ceiling(animator.GetAnimation(clipName).Length * Fps));
            for (int sample = 0; sample < sampleCount; sample++)
            {
                animator.Advance(1.0 / Fps);
                await WaitForDraw();
                var current = ReadPose(skeleton);
                clipFinite &= current.All(p => p.Finite);
                for (int bone = 0; bone < current.Length; bone++)
                {
                    double delta = current[bone].Position.DistanceTo(previousClipPose[bone].Position);
                    double rotation = current[bone].Rotation.AngleTo(previousClipPose[bone].Rotation);
                    clipMaxTranslation = Math.Max(clipMaxTranslation, delta);
                    clipMaxRotation = Math.Max(clipMaxRotation, rotation);
                    clipChanged |= delta > 0.0001 || rotation > 0.0001;
                }
                previousClipPose = current;
            }
            if (!clipFinite || !clipChanged) failures++;
            clipChecks.Add(new
            {
                clip = clipName,
                durationSeconds = animator.GetAnimation(clipName).Length,
                samples = sampleCount,
                allPoseValuesFinite = clipFinite,
                skeletonChanged = clipChanged,
                maxBoneTranslationMetersPerSample = clipMaxTranslation,
                maxBoneRotationRadiansPerSample = clipMaxRotation,
            });
        }

        animator.Play("idle", 0.0);
        animator.Seek(0.0, true);
        var transitions = new List<object>();
        var previous = ReadPose(skeleton);
        bool allFinite = previous.All(p => p.Finite);
        bool changed = false;
        foreach (var next in new[] { "walk", "trot", "idle" })
        {
            _stage.Presenter.Play(next);
            double maxDelta = 0;
            double maxRotation = 0;
            bool transitionChanged = false;
            const int transitionSamples = 12;
            for (int sample = 0; sample < transitionSamples; sample++)
            {
                animator.Advance(1.0 / Fps);
                await WaitForDraw();
                var current = ReadPose(skeleton);
                allFinite &= current.All(p => p.Finite);
                for (int bone = 0; bone < current.Length; bone++)
                {
                    double delta = current[bone].Position.DistanceTo(previous[bone].Position);
                    double rotation = current[bone].Rotation.AngleTo(previous[bone].Rotation);
                    maxDelta = Math.Max(maxDelta, delta);
                    maxRotation = Math.Max(maxRotation, rotation);
                    changed |= delta > 0.0001 || rotation > 0.0001;
                    transitionChanged |= delta > 0.0001 || rotation > 0.0001;
                }
                previous = current;
            }
            bool targetClipPlaying = animator.CurrentAnimation == next;
            if (!transitionChanged || !targetClipPlaying) failures++;
            transitions.Add(new { to = next, targetClipPlaying, clipDurationSeconds = animator.GetAnimation(next).Length, samples = transitionSamples, durationSeconds = (double)transitionSamples / Fps, skeletonChangedDuringBlend = transitionChanged, maxBoneTranslationMetersPerSample = maxDelta, maxBoneRotationRadiansPerSample = maxRotation });
        }
        if (!allFinite) failures++;
        if (!changed) failures++;
        return (new
        {
            age,
            supported = true,
            meshFamilyId = descriptor.MeshFamilyId,
            skeletonBones = skeleton.GetBoneCount(),
            poseCoordinateSpace = "Skeleton3D local bone pose (GetBonePose)",
            clipMotionChecks = clipChecks,
            allPoseValuesFinite = allFinite,
            skeletonChanged = changed,
            transitions,
        }, failures);
    }

    private void WriteReport<T>(T report)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        using var file = FileAccess.Open(_out.PathJoin("motion_report.json"), FileAccess.ModeFlags.Write);
        file?.StoreString(json);
    }

    private static Skeleton3D? FindSkeleton(Node root)
    {
        if (root is Skeleton3D skeleton) return skeleton;
        foreach (Node child in root.GetChildren())
            if (FindSkeleton(child) is { } found) return found;
        return null;
    }

    private static (Vector3 Position, Quaternion Rotation, bool Finite)[] ReadPose(Skeleton3D skeleton)
    {
        var pose = new (Vector3, Quaternion, bool)[skeleton.GetBoneCount()];
        for (int i = 0; i < pose.Length; i++)
        {
            var transform = skeleton.GetBonePose(i);
            var rotation = transform.Basis.GetRotationQuaternion();
            bool isFinite = Finite(transform.Origin) && Finite(rotation);
            pose[i] = (transform.Origin, rotation, isFinite);
        }
        return pose;
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Finite(Quaternion q) => float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W);
}
