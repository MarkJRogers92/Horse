using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>Isolated diagnostic preview of the original Mesh2Motion horse and Walk.</summary>
public partial class TemporaryDonorWalk : Node3D
{
    private const string DonorScenePath = "res://assets/horse/temporary_mesh2motion/mesh2motion_horse_walk.glb";
    private const float RootHeightOffset = 0.032f;
    private const float SharedForwardSpeed = 0.903103f;
    private const string WalkName = "Walk";
    private Node3D _root = null!;
    private Camera3D _camera = null!;
    private AnimationPlayer _player = null!;
    private Skeleton3D _skeleton = null!;
    private Node3D _model = null!;
    private string _output = "/tmp/temporary-donor-preview";
    private bool _interactive;
    private bool _captureAutoTravel;
    private double _travelDistance;
    private double _interactiveRouteDistance;
    private double _processElapsed;
    private int _exitCode;
    private readonly Dictionary<string, object?> _report = new();
    private readonly List<Dictionary<string, object?>> _captures = new();
    private readonly List<string> _captureFailures = new();

    public override void _Ready()
    {
        _output = ReadOutputArgument();
        Directory.CreateDirectory(_output);
        _interactive = OS.GetCmdlineUserArgs().Contains("--interactive");
        CallDeferred(_interactive ? nameof(StartInteractive) : nameof(RunProof));
    }

    public override void _Process(double delta)
    {
        if ((!_interactive && !_captureAutoTravel) || _root == null) return;
        _processElapsed += delta;
        _travelDistance += SharedForwardSpeed * delta;
        var routeX = _interactive && _interactiveRouteDistance > 0 ? _travelDistance % _interactiveRouteDistance : _travelDistance;
        _root.Position = new Vector3((float)routeX, RootHeightOffset, 0);
        if (_interactive) AimCamera("side");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
            GetTree().Quit();
    }

    private void StartInteractive()
    {
        BuildStage();
        var packed = ResourceLoader.Load<PackedScene>(DonorScenePath);
        if (packed == null) throw new InvalidOperationException($"Could not load imported donor GLB scene: {DonorScenePath}");
        _model = packed.Instantiate<Node3D>();
        _root.AddChild(_model);
        _model.Name = "OriginalDonorHorseAndRig";
        _model.RotationDegrees = new Vector3(0, 90, 0);
        _player = FindDescendant<AnimationPlayer>(_model) ?? throw new InvalidOperationException("Imported scene has no AnimationPlayer");
        _skeleton = FindDescendant<Skeleton3D>(_model) ?? throw new InvalidOperationException("Imported scene has no Skeleton3D");
        var animationName = _player.GetAnimationList().FirstOrDefault(n => string.Equals(n, WalkName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(animationName)) throw new InvalidOperationException("Imported scene has no Walk animation");
        var clip = _player.GetAnimation(animationName);
        clip.LoopMode = Animation.LoopModeEnum.Linear;
        _interactiveRouteDistance = SharedForwardSpeed * clip.Length * 4.0;
        _player.Play(animationName);
        _root.Position = new Vector3(0, RootHeightOffset, 0);
        AimCamera("side");
    }

    private static string ReadOutputArgument()
    {
        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--out") return Path.GetFullPath(args[i + 1]);
        return "/tmp/temporary-donor-preview";
    }

    private async void RunProof()
    {
        try
        {
            BuildStage();
            var packed = ResourceLoader.Load<PackedScene>(DonorScenePath);
            if (packed == null) throw new InvalidOperationException($"Could not load imported donor GLB scene: {DonorScenePath}");
            _model = packed.Instantiate<Node3D>();
            _root.AddChild(_model);
            _model.Name = "OriginalDonorHorseAndRig";
            _model.RotationDegrees = new Vector3(0, 90, 0); // donor Blender -Y -> Godot +Z; preview travel +X
            _player = FindDescendant<AnimationPlayer>(_model) ?? throw new InvalidOperationException("Imported scene has no AnimationPlayer");
            _skeleton = FindDescendant<Skeleton3D>(_model) ?? throw new InvalidOperationException("Imported scene has no Skeleton3D");
            var animationNames = _player.GetAnimationList();
            var animationName = animationNames.FirstOrDefault(n => string.Equals(n, WalkName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(animationName)) throw new InvalidOperationException($"Imported Walk animation missing; available: {string.Join(",", animationNames)}");
            var clip = _player.GetAnimation(animationName);
            if (clip.Length <= 0) throw new InvalidOperationException("Imported Walk clip has no positive duration");
            // Explicit presentation loop only; this is not a claim that the source authored a seamless loop.
            clip.LoopMode = Animation.LoopModeEnum.Linear;
            _report["donorScene"] = DonorScenePath;
            _report["donorSource"] = new Dictionary<string, object?>
            {
                ["rigBlendSha256"] = "585258436808e70a565d76ac493fb018b08c1087798a8c75ba13125f817c0bd9",
                ["walkBlendSha256"] = "eae76f96316e22b426e2b513db7a621a345fe336a8ac207458d4dbf42db2e5fa",
                ["exportedGlbSha256"] = Sha256(ProjectSettings.GlobalizePath(DonorScenePath)),
                ["previewSceneSha256"] = Sha256(ProjectSettings.GlobalizePath("res://scenes/tools/TemporaryDonorWalk.tscn")),
                ["previewScriptSha256"] = Sha256(ProjectSettings.GlobalizePath("res://scripts/Tools/TemporaryDonorWalk.cs")),
            };
            _report["presentation"] = new Dictionary<string, object?>
            {
                ["uniformScale"] = 1.0,
                ["rootYHeightOffsetMeters"] = RootHeightOffset,
                ["rootRotationDegreesY"] = 90.0,
                ["sharedForwardSpeedMetersPerSecond"] = SharedForwardSpeed,
                ["speedBasis"] = "0.873 m mean proxy excursion / (29/30 s imported source frame span) = about 0.903103 m/s; rough presentation calibration only, not a validated no-slip stance speed.",
                ["walkCycleForwardTravelMeters"] = SharedForwardSpeed * (29.0 / 30.0),
                ["loopModeForcedForPreview"] = clip.LoopMode.ToString(),
                ["floorContact"] = "Diagnostic only; source baseline has about 31.6 mm whole-mesh penetration and this shared 32 mm offset is a visual compromise."
            };
            var skinSurfaceEvidence = InspectSkinSurfaces(_model);
            _report["importedRuntime"] = new Dictionary<string, object?>
            {
                ["animationNames"] = animationNames,
                ["walkResolvedName"] = animationName,
                ["walkLengthSeconds"] = clip.Length,
                ["skeletonBoneCount"] = _skeleton.GetBoneCount(),
                ["skeletonBones"] = Enumerable.Range(0, _skeleton.GetBoneCount()).Select(i => _skeleton.GetBoneName(i).ToString()).ToArray(),
                ["meshSurfaces"] = skinSurfaceEvidence,
            };

            _root.Position = new Vector3(0, RootHeightOffset, 0);
            _travelDistance = 0;
            _processElapsed = 0;
            _captureAutoTravel = true;
            AimCamera("side");
            _player.Play(animationName);
            _player.Seek(0.04, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var advanceStart = _player.CurrentAnimationPosition;
            var rootStart = _root.Position;
            var processStart = _processElapsed;
            var travelStart = _travelDistance;
            var poseAtStart = ReadPoseSummary();
            await ToSignal(GetTree().CreateTimer(0.28), SceneTreeTimer.SignalName.Timeout);
            var advanceEnd = _player.CurrentAnimationPosition;
            var rootEnd = _root.Position;
            var processDelta = _processElapsed - processStart;
            var travelDelta = _travelDistance - travelStart;
            _captureAutoTravel = false;
            var poseAtEnd = ReadPoseSummary();
            var delta = PoseDelta(poseAtStart, poseAtEnd);
            _report["playbackAdvance"] = new Dictionary<string, object?>
            {
                ["positionSecondsBefore"] = advanceStart,
                ["positionSecondsAfter"] = advanceEnd,
                ["positionAdvanced"] = advanceEnd > advanceStart,
                ["maxBonePositionDeltaMeters"] = delta.Position,
                ["maxBoneRotationDeltaRadians"] = delta.Rotation,
                ["maxBoneScaleDelta"] = delta.Scale,
                ["poseChanged"] = delta.Position > 1e-5 || delta.Rotation > 1e-5 || delta.Scale > 1e-5,
                ["allPoseValuesFinite"] = poseAtStart.Finite && poseAtEnd.Finite,
                ["automaticRootMovement"] = new Dictionary<string, object?>
                {
                    ["startX"] = rootStart.X, ["endX"] = rootEnd.X,
                    ["deltaX"] = rootEnd.X - rootStart.X,
                    ["elapsedProcessSeconds"] = processDelta,
                    ["accumulatedTravelDeltaMeters"] = travelDelta,
                    ["expectedFromSharedSpeed"] = SharedForwardSpeed * processDelta,
                    ["source"] = "Same _Process route-advance code used by the interactive preview."
                },
            };

            var shots = new[]
            {
                ("walk_side_phase_00.png", 0.03, "side"),
                ("walk_side_phase_25.png", clip.Length * 0.25, "side"),
                ("walk_side_phase_50.png", clip.Length * 0.50, "side"),
                ("walk_front_phase_50.png", clip.Length * 0.50, "front"),
                ("walk_quarter_phase_75.png", clip.Length * 0.75, "quarter"),
                ("walk_quarter_phase_95.png", clip.Length * 0.95, "quarter"),
            };
            foreach (var (file, time, angle) in shots)
            {
                _player.Stop();
                _player.Play(animationName);
                var clipTime = Mathf.Clamp((float)time, 0.0f, clip.Length - 0.001f);
                _player.Seek(clipTime, true);
                _root.Position = new Vector3(SharedForwardSpeed * (float)clipTime, RootHeightOffset, 0);
                AimCamera(angle);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var pose = ReadPoseSummary();
                var actualAnimationPosition = _player.CurrentAnimationPosition;
                var pngPath = Path.Combine(_output, file);
                var image = GetViewport().GetTexture().GetImage();
                var error = image.SavePng(pngPath);
                if (error != Error.Ok) _captureFailures.Add($"{file}: PNG save returned {error}");
                if (!File.Exists(pngPath) || image.GetWidth() != 1280 || image.GetHeight() != 720)
                    _captureFailures.Add($"{file}: missing or wrong image dimensions {image.GetWidth()}x{image.GetHeight()}");
                if (!pose.Finite) _captureFailures.Add($"{file}: non-finite bone pose");
                _captures.Add(new Dictionary<string, object?>
                {
                    ["file"] = file,
                    ["sha256"] = File.Exists(pngPath) ? Sha256(pngPath) : null,
                    ["size"] = new[] { image.GetWidth(), image.GetHeight() },
                    ["phaseSeconds"] = clipTime,
                    ["requestedPhaseSeconds"] = clipTime,
                    ["actualAnimationPositionSecondsAtCapture"] = actualAnimationPosition,
                    ["elapsedTravelMeters"] = SharedForwardSpeed * clipTime,
                    ["cameraAngle"] = angle,
                    ["poseFinite"] = pose.Finite,
                    ["saveError"] = error.ToString(),
                });
            }

            _player.Stop();
            _player.Play(animationName);
            var nearEnd = Math.Max(0.0, clip.Length - 0.06);
            _player.Seek(nearEnd, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var wrapStart = _player.CurrentAnimationPosition;
            await ToSignal(GetTree().CreateTimer(0.16), SceneTreeTimer.SignalName.Timeout);
            var wrapEnd = _player.CurrentAnimationPosition;
            _report["playbackWrap"] = new Dictionary<string, object?>
            {
                ["startPositionSeconds"] = wrapStart,
                ["endPositionSeconds"] = wrapEnd,
                ["wrappedUnderForcedLoopMode"] = wrapEnd < wrapStart,
                ["interpretation"] = "Observed playback wrapped with linear loop mode forced by this preview; this does not establish valid skin deformation or authored seamless closure."
            };
            _report["captures"] = _captures;
            _report["claims"] = new[]
            {
                "Temporary visual preview of the original donor horse, rig, and linked Walk animation only.",
                "No Mesh2Motion fitting, skin/rig repair, retargeting, or production catalog integration was performed.",
                "No coat, marking, genetics, or four-component production-morph contract is claimed.",
                "Contact, floor agreement, foot slip, and gait quality remain unaccepted diagnostics."
            };
            var failures = CollectFailures(clip.Length, advanceEnd > advanceStart, delta, poseAtStart, poseAtEnd, wrapEnd < wrapStart, rootEnd.X - rootStart.X, travelDelta, processDelta, skinSurfaceEvidence);
            failures = failures.Concat(_captureFailures).ToArray();
            _report["failures"] = failures;
            await File.WriteAllTextAsync(Path.Combine(_output, "runtime_report.json"), JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true }));
            if (failures.Length > 0)
            {
                _exitCode = 1;
                GD.PushError("TemporaryDonorWalk proof checks failed: " + string.Join("; ", failures));
            }
        }
        catch (Exception ex)
        {
            _exitCode = 1;
            GD.PushError($"TemporaryDonorWalk failed: {ex}");
            _report["fatalError"] = ex.ToString();
            await File.WriteAllTextAsync(Path.Combine(_output, "runtime_report.json"), JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true }));
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(_exitCode);
    }

    private void BuildStage()
    {
        _root = new Node3D { Name = "DonorPresentationRoot" };
        AddChild(_root);
        var floor = new MeshInstance3D { Name = "GroundReference", Mesh = new PlaneMesh { Size = new Vector2(20, 8) } };
        floor.Position = Vector3.Zero;
        floor.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.23f, 0.26f), Roughness = 1.0f };
        AddChild(floor);
        for (var x = -2; x <= 10; x++)
        {
            var marker = new MeshInstance3D { Name = $"GroundMarker{x}", Mesh = new BoxMesh { Size = new Vector3(0.015f, 0.006f, 5.0f) } };
            marker.Position = new Vector3(x, 0.003f, 0);
            marker.MaterialOverride = new StandardMaterial3D { AlbedoColor = x % 2 == 0 ? new Color(0.78f, 0.48f, 0.18f) : new Color(0.40f, 0.44f, 0.48f), Roughness = 1.0f };
            AddChild(marker);
        }
        var light = new DirectionalLight3D { RotationDegrees = new Vector3(-42, -30, 0), LightEnergy = 1.6f, ShadowEnabled = true };
        AddChild(light);
        var world = new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.60f, 0.69f, 0.76f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.63f, 0.67f, 0.70f), AmbientLightEnergy = 0.8f } };
        AddChild(world);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 4.6f, Current = true };
        AddChild(_camera);
        var caption = new Label { Text = "TEMPORARY DONOR WALK · ORIGINAL MESH2MOTION HORSE · NOT PRODUCTION" };
        caption.Position = new Vector2(24, 18);
        caption.AddThemeColorOverride("font_color", new Color(0.08f, 0.10f, 0.12f));
        caption.AddThemeFontSizeOverride("font_size", 19);
        AddChild(caption);
    }

    private void AimCamera(string angle)
    {
        var focus = _root.Position + new Vector3(0, 1.0f, 0);
        var offset = angle switch
        {
            "front" => new Vector3(5.8f, 2.2f, 0.0f),
            "quarter" => new Vector3(4.2f, 2.1f, -4.6f),
            _ => new Vector3(0.0f, 2.2f, -5.6f),
        };
        _camera.Position = focus + offset;
        _camera.LookAt(focus, Vector3.Up);
    }

    private static T? FindDescendant<T>(Node root) where T : Node
    {
        if (root is T found) return found;
        foreach (var child in root.GetChildren())
        {
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private List<Dictionary<string, object?>> InspectSkinSurfaces(Node root)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var meshInstance in FindAll<MeshInstance3D>(root))
        {
            if (meshInstance.Mesh == null) continue;
            for (var s = 0; s < meshInstance.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = meshInstance.Mesh.SurfaceGetArrays(s);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var width = vertices.Length > 0 ? bones.Length / vertices.Length : 0;
                var activeHistogram = new SortedDictionary<int, int>();
                var sums = new List<float>();
                var finite = true;
                var positionsFinite = vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
                var legalBoneIndices = true;
                for (var i = 0; i < weights.Length; i++) finite &= float.IsFinite(weights[i]) && weights[i] >= -1e-7f;
                for (var v = 0; v < vertices.Length; v++)
                {
                    var active = 0; var sum = 0f;
                    for (var j = 0; j < width; j++)
                    {
                        var ix = v * width + j;
                        if (weights[ix] > 1e-7f) active++;
                        if (weights[ix] > 1e-7f && (bones[ix] < 0 || bones[ix] >= _skeleton.GetBoneCount())) legalBoneIndices = false;
                        sum += weights[ix];
                    }
                    activeHistogram[active] = activeHistogram.GetValueOrDefault(active) + 1;
                    sums.Add(sum);
                }
                result.Add(new Dictionary<string, object?>
                {
                    ["meshNode"] = meshInstance.GetPath().ToString(), ["surface"] = s,
                    ["vertexCount"] = vertices.Length, ["boneIndexWidthPerVertex"] = width,
                    ["weightWidthPerVertex"] = vertices.Length > 0 ? weights.Length / vertices.Length : 0,
                    ["activeInfluenceHistogram"] = activeHistogram,
                    ["minimumWeightSum"] = sums.Count > 0 ? (double)sums.Min() : 0.0,
                    ["maximumWeightSum"] = sums.Count > 0 ? (double)sums.Max() : 0.0,
                    ["allWeightsFiniteNonnegative"] = finite,
                    ["allPositionsFinite"] = positionsFinite,
                    ["allActiveBoneIndicesInRange"] = legalBoneIndices,
                    ["boneAndWeightWidthsMatch"] = width > 0 && bones.Length == weights.Length && bones.Length == vertices.Length * width,
                    ["weightsArrayLength"] = weights.Length, ["boneIndexArrayLength"] = bones.Length,
                });
            }
        }
        return result;
    }

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        if (root is T match) yield return match;
        foreach (var child in root.GetChildren())
            foreach (var nestedMatch in FindAll<T>(child)) yield return nestedMatch;
    }

    private sealed record PoseSnapshot(Vector3[] Positions, Quaternion[] Rotations, Vector3[] Scales, bool Finite);

    private PoseSnapshot ReadPoseSummary()
    {
        var positions = new Vector3[_skeleton.GetBoneCount()];
        var rotations = new Quaternion[positions.Length];
        var scales = new Vector3[positions.Length];
        var finite = true;
        for (var i = 0; i < _skeleton.GetBoneCount(); i++)
        {
            positions[i] = _skeleton.GetBonePosePosition(i);
            rotations[i] = _skeleton.GetBonePoseRotation(i);
            scales[i] = _skeleton.GetBonePoseScale(i);
            finite &= float.IsFinite(positions[i].X) && float.IsFinite(positions[i].Y) && float.IsFinite(positions[i].Z);
            finite &= float.IsFinite(rotations[i].X) && float.IsFinite(rotations[i].Y) && float.IsFinite(rotations[i].Z) && float.IsFinite(rotations[i].W);
            finite &= float.IsFinite(scales[i].X) && float.IsFinite(scales[i].Y) && float.IsFinite(scales[i].Z);
        }
        return new PoseSnapshot(positions, rotations, scales, finite);
    }

    private static (float Position, float Rotation, float Scale) PoseDelta(PoseSnapshot a, PoseSnapshot b)
    {
        var p = 0.0f; var r = 0.0f; var s = 0.0f;
        var count = Math.Min(a.Positions.Length, b.Positions.Length);
        for (var i = 0; i < count; i++)
        {
            p = Math.Max(p, a.Positions[i].DistanceTo(b.Positions[i]));
            r = Math.Max(r, a.Rotations[i].AngleTo(b.Rotations[i]));
            s = Math.Max(s, a.Scales[i].DistanceTo(b.Scales[i]));
        }
        return (p, r, s);
    }

    private string[] CollectFailures(double clipLength, bool advanced, (float Position, float Rotation, float Scale) delta, PoseSnapshot start, PoseSnapshot end, bool wrapped, float rootDelta, double travelDelta, double processDelta, List<Dictionary<string, object?>> surfaces)
    {
        var failures = new List<string>();
        if (clipLength <= 0) failures.Add("Walk clip length is not positive");
        if (!advanced) failures.Add("AnimationPlayer time did not advance");
        if (!start.Finite || !end.Finite) failures.Add("Bone pose values were non-finite");
        if (!(delta.Position > 1e-5 || delta.Rotation > 1e-5 || delta.Scale > 1e-5)) failures.Add("No sampled bone pose change was observed");
        if (!wrapped) failures.Add("Playback did not wrap under the forced linear loop mode");
        if (surfaces.Count == 0) failures.Add("No imported mesh skin surface was found");
        foreach (var surface in surfaces)
        {
            if (!(surface.TryGetValue("allWeightsFiniteNonnegative", out var weightsFinite) && weightsFinite is true)) failures.Add("Imported skin weights are non-finite or negative");
            if (!(surface.TryGetValue("allPositionsFinite", out var verticesFinite) && verticesFinite is true)) failures.Add("Imported vertex positions are non-finite");
            if (!(surface.TryGetValue("allActiveBoneIndicesInRange", out var boneIndicesValid) && boneIndicesValid is true)) failures.Add("Imported active bone indices are out of range");
            if (!(surface.TryGetValue("boneAndWeightWidthsMatch", out var widthsMatch) && widthsMatch is true)) failures.Add("Imported bone/weight surface widths do not match");
            if (surface.TryGetValue("minimumWeightSum", out var minSum) && minSum is double min && min < 0.998) failures.Add($"Imported skin minimum weight sum is too low: {min}");
            if (surface.TryGetValue("maximumWeightSum", out var maxSum) && maxSum is double max && max > 1.002) failures.Add($"Imported skin maximum weight sum is too high: {max}");
        }
        if (processDelta <= 0 || travelDelta <= 0 || rootDelta <= 0) failures.Add("Automatic root movement did not advance");
        var expectedTravel = SharedForwardSpeed * processDelta;
        if (Math.Abs(rootDelta - expectedTravel) > 0.002 || Math.Abs(travelDelta - expectedTravel) > 0.002) failures.Add("Automatic root movement did not match the shared speed route code");
        return failures.ToArray();
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
