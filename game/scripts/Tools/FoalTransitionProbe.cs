using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Cloverbrook.Game.Scenes;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>Deterministic proof against actual imported skins and real viewer controls.</summary>
public partial class FoalTransitionProbe : Node
{
    private HorseStage _stage = null!;
    private readonly List<object> _measurements = new();
    private int _checks;
    private string _output = "/tmp/foal-transition-probe";

    public override void _Ready()
    {
        _output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? _output;
        CallDeferred(nameof(Run));
    }

    private async void Run()
    {
        try
        {
            _stage = new HorseStage(); AddChild(_stage);
            _stage.SetMode(World.LightingMode.Neutral);
            // Advance every phase explicitly, independent of machine/render speed.
            _stage.ProcessMode = ProcessModeEnum.Disabled;
            foreach (string family in new[] { "horse.standard.foal.v1", "horse.compact.foal.v1" })
            foreach (int fps in new[] { 60, 120 })
                CheckFamily(family, fps);
            CheckWalkStopPhases();
            CheckRestEvidenceGuards();
            if (OS.GetCmdlineUserArgs().Contains("--render")) await CaptureTransitions();
            await CheckViewer();
            await CheckLiveWaitClocks();
            System.IO.Directory.CreateDirectory(_output);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "transition-report.json"), JsonSerializer.Serialize(new
            {
                checks = _checks, failures = Array.Empty<string>(), measurements = _measurements,
                scope = "Corrected foal Idle/Walk rest-surface swaps, reversible shape/height bridges, real viewer selection and cancellation. Direct Present and adult semantics unchanged."
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[foal-transition] PASS {_checks} checks; report {_output}/transition-report.json");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("[foal-transition] FAILED " + error);
            GetTree().Quit(1);
        }
    }

    private void CheckFamily(string family, int fps)
    {
        var original = FixtureLibrary.Get("bay", true) with { MeshFamilyId = family };
        var weights = original.MorphWeights.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        weights["body_length"] = 2200; weights["chest_width"] = -1300;
        weights["neck_length"] = 1800; weights["muscle"] = 2400; weights["height"] = 900;
        original = original with { MorphWeights = weights };
        var neutral = Neutral(original);
        var p = _stage.Presenter;
        Check(p.Present(original, "idle").Supported, "original shaped foal loads");
        p.Pose("idle", 0.43);
        _stage.FrameView("q3");
        var start = Surface(p.Model!);
        Check(p.TransitionTo(neutral, "walk").Supported && p.IsTransitioning && !p.UsesWalkVariant,
            "Walk starts with outgoing static mesh");
        Check(Distance(start, Surface(p.Model!)) < 1e-8, "request does not change visible surface");
        CompleteBridge(family + " idle-to-walk", fps);
        Check(p.UsesWalkVariant && p.Animator?.IsPlaying() == true && ShapeMatches(neutral), "neutral Walk resumes only after complete bridge");
        p.Animator!.Advance(0.37);
        _stage._Process(0.37);
        float route = p.Position.X;
        Vector3 camera = _stage.Camera.Target;
        Check(route > 0.1, "route advanced before stopping");
        start = Surface(p.Model!);
        Check(p.TransitionTo(original, "idle").Supported && p.IsTransitioning, "Idle starts asynchronous restoration");
        Check(Distance(start, Surface(p.Model!)) < 1e-8, "Idle request does not pop current Walk surface");
        CompleteBridge(family + " walk-to-idle", fps);
        Check(!p.UsesWalkVariant && p.CurrentAnimation == "Idle" && ShapeMatches(original), "Idle restores all four component morphs and height");
        _stage._Process(0.4);
        Check(Math.Abs(p.Position.X - route) < 1e-7 && _stage.Camera.Target.DistanceTo(camera) < 1e-7,
            "Idle keeps route and camera placement");

        // Reverse repeatedly during both halves. Each request must start from the
        // current visible surface, with the latest destination winning.
        p.TransitionTo(neutral, "walk");
        for (int i = 0; i < 10; i++)
        {
            p._Process(i % 3 == 0 ? HorsePresenter.FoalTransitionHalfSeconds + 0.001 : 0.093);
            var before = Surface(p.Model!);
            bool walk = i % 2 == 1;
            p.TransitionTo(walk ? neutral : original, walk ? "walk" : "idle");
            Check(Distance(before, Surface(p.Model!)) < 1e-8 && p.IsTransitioning,
                "rapid reversal retains visible rig/shape");
            Check(p.LocomotionSpeedMetersPerSecond == 0 && p.Animator?.IsPlaying() != true,
                "rapid reversal never starts locomotion");
            float previousLift = p.Model!.Position.Y;
            var rebased = Surface(p.Model);
            p._Process(1.0 / 120);
            Check(Math.Abs(p.Model!.Position.Y - previousLift) < 0.002f && Distance(rebased, Surface(p.Model)) < 0.002f,
                "first reversal frame retains continuous clearance and shape");
        }
        p.TransitionTo(original, "idle");
        CompleteBridge(family + " latest-reversal-to-idle", fps);
        Check(ShapeMatches(original) && !p.UsesWalkVariant, "latest rapid-toggle destination wins");

        p.TransitionTo(neutral, "walk"); p._Process(0.11);
        var chestnut = FixtureLibrary.Get("chestnut", true) with { MeshFamilyId = family };
        p.Present(chestnut, "idle");
        var model = p.Model;
        p._Process(5);
        Check(!p.IsTransitioning && p.Model == model && p.Descriptor == chestnut && ShapeMatches(chestnut),
            "coat change cancels pending foal request");
        p.TransitionTo(Neutral(chestnut), "walk"); p._Process(0.31);
        var adult = Neutral(FixtureLibrary.Get("bay", false));
        p.Present(adult, "walk"); model = p.Model; p._Process(5);
        Check(!p.IsTransitioning && p.Model == model && p.Descriptor == adult && p.UsesWalkVariant,
            "age/family change cancels pending foal request");
        Check(!p.TransitionTo(original, "walk").Supported && p.Model == null && !p.IsTransitioning,
            "nonneutral Walk still fails closed");
        p.Present(original, "idle"); p.TransitionTo(neutral, "walk"); p._Process(0.08);
        var probe = FixtureLibrary.UnsupportedProbes().First();
        Check(!p.Present(probe, "walk").Supported && p.Model == null && !p.IsTransitioning,
            "unsupported selection cancels pending bridge");
        p._Process(5);
        Check(p.Model == null && p.Descriptor == probe, "cancelled callback cannot reload a hidden old mesh");
    }

    private void CompleteBridge(string label, int fps)
    {
        var p = _stage.Presenter;
        float maxStep = 0, maxSwap = 0, maxNormalizedSwap = 0;
        var precision = new List<object>();
        var stretch = new Dictionary<string, float>();
        float minimumBodyHeight = float.PositiveInfinity;
        float maximumModelLift = 0;
        int swaps = 0, frames = 0;
        Vector3 root = p.Position, camera = _stage.Camera.Target;
        while (p.IsTransitioning && frames < fps * 2)
        {
            Node3D outgoing = p.Model!;
            var before = Surface(outgoing);
            MeasureBridgeDeformation(before, Surface(outgoing, applySkin: false), stretch, ref minimumBodyHeight);
            _stage._Process(1.0 / fps);
            p._Process(1.0 / fps);
            if (p.Model != outgoing)
            {
                // QueueFree is deferred; the detached old rig is still available
                // at its exact final rest pose. Compare every indexed skin corner.
                float gap = Distance(Surface(outgoing), Surface(p.Model!));
                float normalizedGap = Distance(Surface(outgoing, true), Surface(p.Model!, true));
                maxNormalizedSwap = Math.Max(maxNormalizedSwap, normalizedGap);
                precision.Add(new { outgoing = RestPrecision(outgoing), incoming = RestPrecision(p.Model!), normalizedGapMeters = normalizedGap });
                maxSwap = Math.Max(maxSwap, gap); swaps++;
                // Different imported rigs quantize weights independently. Measure
                // that precision separately from exact source/rest geometry.
                Check(gap < 0.0001f, "actual old/new skin rest surfaces differ by less than 0.1 mm");
                Check(normalizedGap < 0.00001f, "normalizing imported weight sums removes the rest-swap discrepancy (<10 micrometres)");
            }
            var after = Surface(p.Model!);
            maxStep = Math.Max(maxStep, Distance(before, after));
            MeasureBridgeDeformation(after, Surface(p.Model!, applySkin: false), stretch, ref minimumBodyHeight);
            maximumModelLift = Math.Max(maximumModelLift, p.Model!.Position.Y);
            Check(p.Position.DistanceTo(root) < 1e-7 && _stage.Camera.Target.DistanceTo(camera) < 1e-7,
                "bridge holds route and camera");
            if (p.IsTransitioning)
                Check(p.Animator?.IsPlaying() != true && p.LocomotionSpeedMetersPerSecond == 0,
                    "bridge never runs clip or locomotion");
            frames++;
        }
        Check(!p.IsTransitioning && swaps == 1, "bridge terminates with one rest-only swap");
        Check(maxStep < 0.035f, "sampled surface step remains below 35 mm");
        Check(minimumBodyHeight >= -0.002f, "bridge retains unchanged 2 mm floor-penetration bound");
        Check(p.Model!.Position.Length() < 0.000001f, "bridge clears its temporary lift at the exact checked endpoint");
        var endpoint = Surface(p.Model);
        p.Animator?.Advance(0);
        Check(Distance(endpoint, Surface(p.Model)) < 0.00001f, "resuming frame zero does not pop the final bridge surface");
        foreach (var (component, maximum) in stretch)
            Check(float.IsFinite(maximum) && maximum <= 2, "bridge preserves unchanged 2x edge-stretch gate: " + component);
        _measurements.Add(new { label, fps, frames, maxSkinSwapGapMeters = maxSwap, maxNormalizedSkinSwapGapMeters = maxNormalizedSwap,
            maxFrameVertexStepMeters = maxStep, skinPrecision = precision, maxEdgeStretchByComponent = stretch, minimumBodyHeightMeters = minimumBodyHeight,
            maximumTemporaryModelLiftMeters = maximumModelLift,
            edgeStretchBaseline = "Corresponding unskinned corrected surface at the same current morph values and height; edges longer than 1 micrometre",
            contactScope = "Minimum actual Body height measured throughout; bridge repositions legs, with no steady Walk or locomotion until completion" });
        GD.Print($"[foal-transition] {label} {fps} fps: swap {maxSwap * 1000:F6} mm; max frame {maxStep * 1000:F3} mm; stretch {stretch.Values.Max():F6}x; floor {minimumBodyHeight * 1000:F3} mm");
    }

    private void CheckWalkStopPhases()
    {
        var original = FixtureLibrary.Get("bay", true);
        var neutral = Neutral(original);
        var p = _stage.Presenter;
        foreach (double phase in new[] { 0.0, 0.125, 0.25, 0.5, 0.625, 0.75, 0.875 })
        {
            p.Present(neutral, "walk");
            p.Pose("walk", phase * p.LocomotionPeriodSeconds);
            p.TransitionTo(original, "idle");
            CompleteBridge($"walk stop phase {phase}", 60);
            Check(ShapeMatches(original), "arbitrary Walk stop phase restores Idle shape");
        }
    }

    private async Task CheckViewer()
    {
        var viewer = GD.Load<PackedScene>("res://scenes/viewer.tscn").Instantiate<HorseViewer>();
        AddChild(viewer);
        var stage = viewer.GetNode<HorseStage>("Stage");
        var foal = All<CheckButton>(viewer).Single(n => n.Name == "FoalToggle");
        var animation = All<OptionButton>(viewer).Single(n => n.Name == "AnimationSelector");
        var coat = All<OptionButton>(viewer).Single(n => n.Name == "CoatSelector");
        foal.ButtonPressed = true;
        stage.Camera.Yaw = 71; stage.Camera.Distance = 4.17f;
        var initial = stage.Presenter.Descriptor!;
        Select(animation, 3);
        Check(stage.Presenter.IsTransitioning && stage.Camera.Yaw == 71 && Math.Abs(stage.Camera.Distance - 4.17f) < 1e-6,
            "real foal animation control opts in and preserves orbit/zoom");
        await WaitForBridge(stage.Presenter);
        Check(stage.Presenter.UsesWalkVariant, "real viewer reaches Walk");
        Select(animation, 0);
        await WaitForBridge(stage.Presenter);
        Check(!stage.Presenter.UsesWalkVariant && stage.Presenter.Descriptor!.MorphWeights.OrderBy(p => p.Key).SequenceEqual(initial.MorphWeights.OrderBy(p => p.Key)),
            "real viewer restores selected Idle weights");
        Select(animation, 3); Select(animation, 0); Select(animation, 3);
        Select(coat, 0);
        await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
        Check(!stage.Presenter.IsTransitioning && stage.Presenter.Descriptor!.CoatRecipe == FixtureLibrary.Get("chestnut", true).CoatRecipe && stage.Presenter.UsesWalkVariant,
            "real viewer coat change cancels bridge and retains selected animation");
        viewer.QueueFree();
    }

    private void CheckRestEvidenceGuards()
    {
        string source = FileAccess.GetFileAsString(VisualCatalog.Path);
        foreach (string family in new[] { "horse.standard.foal.v1", "horse.compact.foal.v1" })
        foreach (string field in new[] { "missing", "sourceGlbSha256", "candidateBlendSha256", "regionProofSha256", "consistencyProofSha256", "morphProofSha256", "importProofSha256" })
        {
            var json = JsonNode.Parse(source)!;
            var entry = json["meshFamilies"]![family]!;
            if (field == "missing") entry.AsObject().Remove("restGeometry");
            else entry["restGeometry"]![field] = "stale-or-missing";
            var catalog = VisualCatalog.Load(json.ToJsonString());
            Check(!catalog.Check(FixtureLibrary.Get("bay", true) with { MeshFamilyId = family }).Supported,
                "missing/stale corrected foal rest binding fails closed: " + field);
            Check(catalog.Check(FixtureLibrary.Get("bay", false)).Supported,
                "foal rest guard does not disable adults");
        }
    }

    private async Task CaptureTransitions()
    {
        Check(DisplayServer.GetName() != "headless", "transition captures require a rendered run");
        var p = _stage.Presenter;
        var layer = new CanvasLayer();
        var caption = new Label { Position = new Vector2(20, 20) };
        caption.AddThemeColorOverride("font_color", new Color(0.2f, 0.2f, 0.2f));
        layer.AddChild(caption); AddChild(layer);
        foreach (string coat in new[] { "bay", "cremello" })
        foreach (string view in new[] { "side", "front", "rear", "q3" })
        {
            var original = FixtureLibrary.Get(coat, true);
            var neutral = Neutral(original);
            string folder = System.IO.Path.Combine(_output, coat + "-" + view);
            System.IO.Directory.CreateDirectory(folder);
            p.Present(original, "idle");
            _stage.FrameView(view);
            for (int frame = 0; frame < 180; frame++)
            {
                if (frame is 15 or 95) p.TransitionTo(neutral, "walk");
                if (frame is 60 or 135) p.TransitionTo(original, "idle");
                if (p.IsTransitioning) p._Process(1.0 / 30);
                else
                {
                    p.Animator?.Advance(1.0 / 30);
                    _stage._Process(1.0 / 30);
                }
                _stage.Camera._Process(0);
                caption.Text = $"Corrected foal | {coat} | {view} | {(p.IsTransitioning ? "transition to " : "")}{p.RequestedAnimation}\nRepeated Idle / Walk; original Idle morphology returns gradually";
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, $"{frame:D4}.png")) == Error.Ok,
                    "saved rendered transition frame");
            }
        }
        layer.QueueFree();
    }

    private async Task CheckLiveWaitClocks()
    {
        int savedMaxFps = Engine.MaxFps;
        var viewer = GD.Load<PackedScene>("res://scenes/viewer.tscn").Instantiate<HorseViewer>();
        AddChild(viewer);
        var stage = viewer.GetNode<HorseStage>("Stage");
        var p = stage.Presenter;
        var body = All<OptionButton>(viewer).Single(n => n.Name == "BodyFamilySelector");
        var foal = All<CheckButton>(viewer).Single(n => n.Name == "FoalToggle");
        var animation = All<OptionButton>(viewer).Single(n => n.Name == "AnimationSelector");
        var coat = All<OptionButton>(viewer).Single(n => n.Name == "CoatSelector");
        try
        {
            // Match the CI sequence: already walking, switch to foal, change coat,
            // then select Idle. Use real process callbacks, never manual _Process.
            foreach (int bodyIndex in new[] { 0, 1 })
            {
                Engine.MaxFps = savedMaxFps;
                Select(body, bodyIndex); Select(animation, 3);
                foal.ButtonPressed = true; Select(coat, 0);
                Check(p.UsesWalkVariant && !p.IsTransitioning, "coat selection directly presents foal Walk");
                Engine.MaxFps = 1; // Reproduce software-renderer delta clamping.
                Select(animation, 0);
                Check(p.IsTransitioning, "low-FPS viewer starts asynchronous Idle bridge");
                var result = await TransitionCompletionWait.Wait(this, p);
                _measurements.Add(new { label = $"low-FPS viewer body {bodyIndex}", wait = result });
                Check(result.Completed && result.WallSeconds > 3 && result.EngineSeconds < 3,
                    "slow live bridge completes within three-second simulation deadline despite >3s wall time");
                Check(!p.UsesWalkVariant && p.CurrentAnimation == "Idle" &&
                    p.Descriptor!.MorphWeights.OrderBy(x => x.Key).SequenceEqual(
                        FixtureLibrary.Get("chestnut", true).MorphWeights.OrderBy(x => x.Key)),
                    "slow viewer restores selected chestnut shape for both foal families");
            }
            Engine.MaxFps = savedMaxFps;
            Select(animation, 3);
            p.SetProcess(false); // Genuine stuck state must still fail closed.
            var stuck = await TransitionCompletionWait.Wait(this, p);
            _measurements.Add(new { label = "disabled presenter rejection", wait = stuck });
            Check(!stuck.Completed && stuck.StopReason == "engine-timeout" && p.IsTransitioning,
                "three-second simulation deadline rejects disabled presenter");
            p.SetProcess(true);
            await WaitForBridge(p);
            Select(animation, 0);
            p.SetProcess(false);
            var watchdog = await TransitionCompletionWait.Wait(this, p, engineSeconds: 3, wallSeconds: 0.05);
            _measurements.Add(new { label = "wall watchdog rejection", wait = watchdog });
            Check(!watchdog.Completed && watchdog.StopReason == "wall-watchdog" && p.IsTransitioning,
                "independent wall watchdog rejects a stalled wait");
        }
        finally
        {
            Engine.MaxFps = savedMaxFps;
            p.SetProcess(true);
            viewer.QueueFree();
        }
    }

    private async Task WaitForBridge(HorsePresenter presenter)
    {
        var result = await TransitionCompletionWait.Wait(this, presenter);
        Check(result.Completed, "live process bridge completes within engine-time deadline");
    }

    private static void Select(OptionButton control, int index)
    {
        control.Select(index); control.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    private bool ShapeMatches(VisualDescriptor descriptor)
    {
        var p = _stage.Presenter;
        var c = VisualCatalog.Instance;
        if (p.Descriptor != descriptor || p.Model == null) return false;
        float scale = 1 + descriptor.MorphWeights.GetValueOrDefault("height") * c.HeightScalePerUnit;
        if (p.Model.Scale.DistanceTo(Vector3.One * scale) > 1e-6) return false;
        foreach (var (component, channels) in c.FamilyMeshBlendShapes[descriptor.MeshFamilyId])
        {
            var mesh = All<MeshInstance3D>(p.Model).Single(m => m.Name == component);
            foreach (var (channel, name) in channels)
            {
                float expected = Mathf.Clamp(descriptor.MorphWeights.GetValueOrDefault(channel) / c.BodyBlendShapeNormalization, -1, 1);
                if (Math.Abs(mesh.GetBlendShapeValue(mesh.FindBlendShapeByName(name)) - expected) > 1e-6) return false;
            }
        }
        return true;
    }

    private static VisualDescriptor Neutral(VisualDescriptor descriptor) => descriptor with
    {
        MorphWeights = descriptor.MorphWeights.ToDictionary(p => p.Key, _ => 0, StringComparer.Ordinal)
    };

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerable<T> All<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T value) yield return value;
            foreach (var nested in All<T>(child)) yield return nested;
        }
    }

    private static Dictionary<string, Vector3[]> Surface(Node3D model, bool normalizeWeights = false, bool applySkin = true)
    {
        var result = new Dictionary<string, Vector3[]>();
        foreach (var mesh in All<MeshInstance3D>(model))
        {
            var skeleton = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
            var skin = mesh.Skin;
            var transform = RelativeTransform((Node3D?)skeleton ?? mesh, model);
            var binds = !applySkin || skin == null || skeleton == null ? Array.Empty<Transform3D>() :
                Enumerable.Range(0, skin.GetBindCount()).Select(i =>
                {
                    int bone = skin.GetBindName(i).IsEmpty ? skin.GetBindBone(i) : skeleton.FindBone(skin.GetBindName(i));
                    if (bone < 0) throw new InvalidOperationException("Unresolved imported Skin bind");
                    return skeleton.GetBoneGlobalPose(bone) * skin.GetBindPose(i);
                }).ToArray();
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(s);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var targets = mesh.Mesh.SurfaceGetBlendShapeArrays(s);
                for (int m = 0; m < mesh.GetBlendShapeCount(); m++)
                {
                    float weight = mesh.GetBlendShapeValue(m);
                    if (weight == 0) continue;
                    var points = targets[m][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var basis = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    bool relative = mesh.Mesh is ArrayMesh am && am.BlendShapeMode == Mesh.BlendShapeMode.Relative;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] += weight * (relative ? points[i] : points[i] - basis[i]);
                }
                int width = vertices.Length == 0 ? 0 : weights.Length / vertices.Length;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = vertices[i];
                    if (binds.Length > 0 && width > 0)
                    {
                        point = Vector3.Zero;
                        float mass = 0;
                        for (int j = 0; j < width; j++)
                        {
                            float weight = weights[i * width + j];
                            if (weight != 0) point += weight * (binds[bones[i * width + j]] * vertices[i]);
                            mass += weight;
                        }
                        if (normalizeWeights && mass > 0) point /= mass;
                    }
                    vertices[i] = transform * point;
                }
                result[mesh.Name + ":" + s] = indices.Length > 0 ? indices.Select(i => vertices[i]).ToArray() : vertices;
            }
        }
        return result;
    }

    private static object RestPrecision(Node3D model)
    {
        float weightError = 0, bindPositionError = 0, bindBasisError = 0;
        foreach (var mesh in All<MeshInstance3D>(model))
        {
            var skeleton = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
            if (skeleton == null || mesh.Skin == null) continue;
            for (int b = 0; b < mesh.Skin.GetBindCount(); b++)
            {
                int bone = mesh.Skin.GetBindName(b).IsEmpty ? mesh.Skin.GetBindBone(b) : skeleton.FindBone(mesh.Skin.GetBindName(b));
                var bind = skeleton.GetBoneGlobalPose(bone) * mesh.Skin.GetBindPose(b);
                bindPositionError = Math.Max(bindPositionError, bind.Origin.Length());
                bindBasisError = Math.Max(bindBasisError, Math.Max(bind.Basis.X.DistanceTo(Vector3.Right),
                    Math.Max(bind.Basis.Y.DistanceTo(Vector3.Up), bind.Basis.Z.DistanceTo(Vector3.Back))));
            }
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(s);
                int count = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
                var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int width = weights.Length / count;
                for (int i = 0; i < count; i++)
                {
                    float sum = 0;
                    for (int j = 0; j < width; j++) sum += weights[i * width + j];
                    weightError = Math.Max(weightError, Math.Abs(1 - sum));
                }
            }
        }
        return new { maxWeightSumError = weightError, maxBindRestTranslationErrorMeters = bindPositionError, maxBindRestBasisError = bindBasisError };
    }

    private static void MeasureBridgeDeformation(Dictionary<string, Vector3[]> posed, Dictionary<string, Vector3[]> rest,
        Dictionary<string, float> maximums, ref float minimumBodyHeight)
    {
        foreach (var (component, points) in posed)
        {
            var baseline = rest[component];
            float max = maximums.GetValueOrDefault(component);
            for (int i = 0; i < points.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = i + e, b = i + (e + 1) % 3;
                    float length = baseline[a].DistanceTo(baseline[b]);
                    if (length > 0.000001f) max = Math.Max(max, points[a].DistanceTo(points[b]) / length);
                }
            maximums[component] = max;
            if (component.StartsWith("Body:", StringComparison.Ordinal))
                minimumBodyHeight = Math.Min(minimumBodyHeight, points.Min(v => v.Y));
        }
    }

    private static Transform3D RelativeTransform(Node3D node, Node3D model)
    {
        var result = node.Transform;
        for (var parent = node.GetParent(); parent != null; parent = parent.GetParent())
        {
            if (parent is Node3D spatial) result = spatial.Transform * result;
            if (parent == model) break;
        }
        return result;
    }

    private static float Distance(Dictionary<string, Vector3[]> a, Dictionary<string, Vector3[]> b)
    {
        if (a.Count != b.Count) throw new InvalidOperationException("Component surface count changed");
        float max = 0;
        foreach (var (key, points) in a)
        {
            if (!b.TryGetValue(key, out var other) || points.Length != other.Length)
                throw new InvalidOperationException("Indexed corner layout changed: " + key);
            for (int i = 0; i < points.Length; i++) max = Math.Max(max, points[i].DistanceTo(other[i]));
        }
        return max;
    }
}
