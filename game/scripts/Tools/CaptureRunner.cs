using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Cloverbrook.Game.Scenes;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;
using IOException = System.IO.IOException;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// Headless-CI evidence generator. Run with a real renderer (Xvfb + lavapipe in CI):
///   godot --path game res://scenes/capture.tscn -- --out ../evidence [--sets sheet,turntable,...]
/// Writes PNGs plus capture_report.json describing every image and every refusal.
/// A still image proves materials/markings, NOT animation quality.
/// </summary>
public partial class CaptureRunner : Node
{
    private string _out = "user://evidence";
    private HorseStage _stage = null!;
    private readonly List<Dictionary<string, object?>> _report = new();
    private int _failures;
    private readonly List<string> _unavailableAnimations = new();
    private readonly List<Dictionary<string, object?>> _animationTrackEvidence = new();
    private readonly List<Dictionary<string, object?>> _foalIdleEvidence = new();
    private readonly List<Dictionary<string, object?>> _adultIdleImportEvidence = new();
    private readonly List<Dictionary<string, object?>> _foalRigEvidence = new();
    private readonly List<Dictionary<string, object?>> _viewerPathEvidence = new();
    private Dictionary<string, object?>? _appearancePerformance;
    private List<Dictionary<string, object?>> _meshLodEvidence = new();
    private readonly List<Dictionary<string, object?>> _signedDetailEvidence = new();
    private readonly List<Dictionary<string, object?>> _signedDetailFallbackEvidence = new();
    private readonly List<Dictionary<string, object?>> _candidateWalkEvidence = new();
    private string _candidateScene = "";

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
        _out = Arg("--out", _out);
        _candidateScene = Arg("--candidate-scene", "");
        var sets = Arg("--sets", "clay,sheet,detail,appearance,markings,turntable,pair,stable,anim,probes").Split(',');
        DirAccess.MakeDirRecursiveAbsolute(_out);
        GetWindow().Size = new Vector2I(1280, 720);

        _stage = new HorseStage { Name = "Stage" };
        AddChild(_stage);
        try
        {
            foreach (var set in sets)
            {
                GD.Print($"[capture] set {set}");
                switch (set)
                {
                    case "clay": await Clay(); break;
                    case "sheet": await Sheet(); break;
                    case "detail": await Detail(); break;
                    case "appearance": await Appearance(); break;
                    case "markings": await MarkingSet(); break;
                    case "material_probe": await MaterialProbe(); break;
                    case "neck_fit": await NeckFitProbe(); break;
                    case "foal_preview": await FoalPreviewSet(); break;
                    case "foal_rig_inventory": await FoalRigInventorySet(); break;
                    case "foal_idle": await FoalIdleSet(); break;
                    case "adult_idle_import": await AdultIdleImportSet(); break;
                    case "viewer_age_toggle": await ViewerAgeToggleProbe(); break;
                    case "adult_runtime": await AdultRuntimeSet(); break;
                    case "detail_baseline": await SignedDetailBaseline(); break;
                    case "detail_runtime": await SignedDetailRuntimeSet(); break;
                    case "stock_runtime": await StockRuntimeSet(); break;
                    case "candidate_walk": await CandidateWalkSet(); break;
                    case "turntable": await Turntable(); break;
                    case "pair": await Pair(); break;
                    case "stable": await StableShots(); break;
                    case "anim": await AnimFrames(); break;
                    case "probes": await Probes(); break;
                    default: GD.PushError($"unknown set {set}"); _failures++; break;
                }
            }
        }
        catch (Exception e) { GD.PushError("[capture] " + e); _failures++; }

        var json = JsonSerializer.Serialize(new
        {
            format = "cloverbrook.capture-report.v1",
            godot = Engine.GetVersionInfo()["string"].AsString(),
            renderer = RenderingServer.GetVideoAdapterName() + " / " + RenderingServer.GetVideoAdapterApiVersion(),
            catalog = VisualCatalog.Instance.Version,
            warning = "Stills prove materials, markings and composition only - not animation quality, not art approval.",
            failures = _failures,
            meshFamilies = VisualCatalog.Instance.MeshFamilies.ToDictionary(p => p.Key,
                p => new { scene = p.Value.Scene, placeholder = p.Value.Placeholder, age = p.Value.Age }),
            unavailableAnimations = _unavailableAnimations,
            animationTrackEvidence = _animationTrackEvidence,
            foalIdleEvidence = _foalIdleEvidence,
            adultIdleImportEvidence = _adultIdleImportEvidence,
            foalRigEvidence = _foalRigEvidence,
            viewerPathEvidence = _viewerPathEvidence,
            appearancePerformance = _appearancePerformance,
            meshLodEvidence = _meshLodEvidence,
            signedDetailEvidence = _signedDetailEvidence,
            signedDetailFallbackEvidence = _signedDetailFallbackEvidence,
            candidateWalkEvidence = _candidateWalkEvidence,
            images = _report,
        }, new JsonSerializerOptions { WriteIndented = true });
        using (var f = FileAccess.Open(_out.PathJoin("capture_report.json"), FileAccess.ModeFlags.Write)) f.StoreString(json);
        GD.Print($"[capture] wrote {_report.Count} entries, failures={_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Settle(int frames = 8)
    {
        for (int i = 0; i < frames; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string file, string set, VisualDescriptor? d, string view, string extra = "")
    {
        await Settle();
        var img = GetViewport().GetTexture().GetImage();
        var path = _out.PathJoin(file);
        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
        var err = img.SavePng(path);
        if (err != Error.Ok) { _failures++; GD.PushError($"save failed {path}: {err}"); }
        _report.Add(new()
        {
            ["file"] = file, ["set"] = set, ["view"] = view, ["lighting"] = _stage.Mode.ToString(), ["note"] = extra,
            ["fixtureId"] = d?.FixtureId, ["coatClassId"] = d?.CoatClassId, ["label"] = d?.DisplayLabel, ["cacheKey"] = d?.CacheKey,
            ["support"] = _stage.Presenter.Support?.Summary, ["markings"] = _stage.Presenter.Markings?.Describe(), ["testOnly"] = d?.TestOnly,
            ["meshFamilyId"] = d?.MeshFamilyId,
        });
    }

    private bool Show(VisualDescriptor d, string anim = "idle", double t = 0.0)
    {
        var s = _stage.Presenter.Present(d, anim);
        if (!s.Supported) { GD.PushError($"[capture] expected supported: {d.FixtureId}: {s.Summary}"); _failures++; return false; }
        _stage.Presenter.Pose(anim, t); // freeze so captures are deterministic
        return true;
    }

    private async Task Clay()
    {
        _stage.SetMode(LightingMode.Neutral);
        foreach (var foal in new[] { false, true })
        {
            var d = FixtureLibrary.Get("bay", foal);
            foreach (var v in new[] { "q3", "side", "front", "rear", "head", "legs" })
            {
                if (!Show(d)) continue;
                _stage.Presenter.ApplyClay();
                _stage.FrameView(v);
                await Shot($"clay/{(foal ? "foal" : "adult")}__{v}.png", "clay", d, v, "grey clay form review (no coat)");
            }
        }
    }

    private async Task Sheet()
    {
        foreach (var id in FixtureLibrary.ClassIds)
        {
            var adult = FixtureLibrary.Get(id, false); var foal = FixtureLibrary.Get(id, true);
            foreach (var mode in new[] { LightingMode.Neutral, LightingMode.Stable })
            {
                _stage.SetMode(mode);
                string m = mode == LightingMode.Neutral ? "neutral" : "stable";
                if (Show(adult)) { _stage.FrameView("q3"); await Shot($"sheet/{id}__adult_q3__{m}.png", "sheet", adult, "q3"); }
                if (mode == LightingMode.Neutral && Show(adult)) { _stage.FrameView("side"); await Shot($"sheet/{id}__adult_side__{m}.png", "sheet", adult, "side"); }
                if (Show(foal)) { _stage.FrameView("q3"); await Shot($"sheet/{id}__foal_q3__{m}.png", "sheet", foal, "q3"); }
            }
        }
    }

    private async Task Detail()
    {
        _stage.SetMode(LightingMode.Neutral);
        foreach (var (id, views) in new[] { ("bay", new[] { "head", "legs", "front", "rear" }), ("cremello", new[] { "head" }),
                     ("grullo", new[] { "side", "legs" }), ("non_dun1_marked", new[] { "side" }), ("smoky_black", new[] { "head" }) })
        {
            var d = FixtureLibrary.Get(id, false);
            foreach (var v in views) if (Show(d)) { _stage.FrameView(v); await Shot($"detail/{id}__{v}.png", "detail", d, v); }
        }
    }

    private async Task Appearance()
    {
        _stage.SetMode(LightingMode.Neutral);
        var descriptors = FixtureLibrary.AppearanceStudio;
        if (descriptors.Count != 8 || descriptors.Select(d => d.FixtureId.Split('.')[0]).Distinct(StringComparer.Ordinal).Count() != 4 ||
            descriptors.Select(d => d.MeshFamilyId).Distinct(StringComparer.Ordinal).Count() != 2 ||
            descriptors.Select(d => d.CoatClassId).Distinct(StringComparer.Ordinal).Count() != 8 ||
            descriptors.Select(d => d.MarkingRealizationKey).Distinct(StringComparer.Ordinal).Count() != 8 || descriptors.Any(d => !d.TestOnly))
        {
            _failures++;
            GD.PushError("[capture] appearance studio must contain four test-only presets, eight supported coats and eight distinct marking seeds across both adult masters");
        }
        foreach (var descriptor in descriptors)
        {
            if (!Show(descriptor)) continue;
            if (_animationTrackEvidence.Count == 0) RecordAnimationTracks(descriptor);
            VerifyMorphValues(descriptor);
            _stage.FrameView("q3");
            await Shot($"appearance/{descriptor.FixtureId}__q3.png", "appearance", descriptor, "q3", "canonical resolver test fixture; not live stock");
            _stage.FrameView("head");
            await Shot($"appearance/{descriptor.FixtureId}__head.png", "appearance", descriptor, "head", "close inspection of the same supported fixture");
        }

        var overview = new Node3D { Name = "AppearanceOverviewCapture" };
        _stage.AddChild(overview);
        overview.AddChild(Sets.Mesh(new PlaneMesh { Size = new Vector2(200, 200) }, Sets.Mat(new Color(0.46f, 0.46f, 0.46f), 0.9f), new Vector3(0, -0.02f, 0)));
        foreach (var sweep in FindAll<MeshInstance3D>(_stage).Where(mesh => mesh.Mesh is CylinderMesh)) sweep.Visible = false;
        _stage.Presenter.Visible = false;
        for (int i = 0; i < descriptors.Count; i++)
        {
            var horse = new HorsePresenter { Name = $"OverviewHorse{i + 1}" };
            overview.AddChild(horse);
            int column = i % 4, row = i / 4;
            horse.Position = new Vector3((column - 1.5f) * 3.2f, 0, (row - 0.5f) * 5.0f);
            var support = horse.Present(descriptors[i], "");
            if (!support.Supported) { _failures++; GD.PushError($"[capture] overview fixture unsupported: {descriptors[i].FixtureId}: {support.Summary}"); }
        }
        _stage.Camera.MaxDistance = 30;
        _stage.Camera.SetView(0, 27, 17, new Vector3(0, 0.95f, 0));
        await Shot("appearance/overview_all_eight.png", "appearance", null, "overview",
            "all eight canonical resolver art-test fixtures arranged as a 2 by 4 inspection grid");
        _appearancePerformance = await MeasureAppearancePerformance();
        overview.QueueFree();
        _stage.Presenter.Visible = true;
        _stage.Camera.MaxDistance = 12;
        _meshLodEvidence.Clear();
        foreach (var familyDescriptor in descriptors.GroupBy(d => d.MeshFamilyId, StringComparer.Ordinal).Select(group => group.First()))
            if (Show(familyDescriptor, "")) _meshLodEvidence.AddRange(MeasureMeshLods(familyDescriptor));

        // The canonical examples use moderate profile values. These renderer-only,
        // clearly test-only probes exercise the descriptor's full documented range
        // and neutral reset on each adult master without pretending to be genetics.
        foreach (var descriptor in descriptors.GroupBy(d => d.MeshFamilyId, StringComparer.Ordinal).Select(g => g.First()))
        foreach (var value in new[] { -2000, 0, 2000 })
        {
            var weights = descriptor.MorphWeights.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var morph in VisualCatalog.Instance.BodyBlendShapes.Keys) weights[morph] = value;
            var probe = descriptor with
            {
                FixtureId = $"renderer-boundary.{descriptor.MeshFamilyId}.{value}",
                DisplayLabel = $"TEST ONLY renderer morph probe {value}",
                MorphWeights = weights,
                TestOnly = true,
            };
            if (!Show(probe)) continue;
            VerifyMorphValues(probe);
            _stage.FrameView("q3");
            await Shot($"appearance/boundary__{probe.MeshFamilyId.Replace('.', '-')}_{value}.png", "appearance", probe, "q3",
                "renderer boundary probe only; synthetic descriptor weights, not a resolved horse or player save");
        }
    }

    private async Task<Dictionary<string, object?>> MeasureAppearancePerformance()
    {
        var frameIntervalsMs = new List<double>(120);
        ulong previousFrameAt = Time.GetTicksUsec();
        for (int i = 0; i < 120; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong frameAt = Time.GetTicksUsec();
            frameIntervalsMs.Add((frameAt - previousFrameAt) / 1000.0);
            previousFrameAt = frameAt;
        }
        frameIntervalsMs.Sort();
        double averageIntervalMs = frameIntervalsMs.Average();
        return new Dictionary<string, object?>
        {
            ["scene"] = "eight adult horses, two rows of four; production mesh and materials",
            ["sampleFrames"] = frameIntervalsMs.Count,
            ["averageFrameIntervalMs"] = averageIntervalMs,
            ["p95FrameIntervalMs"] = frameIntervalsMs[(int)Math.Ceiling(frameIntervalsMs.Count * 0.95) - 1],
            ["maxFrameIntervalMs"] = frameIntervalsMs[^1],
            ["averageObservedFps"] = 1000.0 / averageIntervalMs,
            ["fpsLastSecond"] = Performance.GetMonitor(Performance.Monitor.TimeFps),
            ["renderObjectsLastFrame"] = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame),
            ["renderDrawCallsLastFrame"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            ["renderer"] = RenderingServer.GetVideoAdapterName() + " / " + RenderingServer.GetVideoAdapterApiVersion(),
        };
    }

    private async Task MaterialProbe()
    {
        _stage.SetMode(LightingMode.Neutral);
        var descriptor = FixtureLibrary.MarkingVariety("chestnut", false, 4).Last();
        var zeroMorphs = descriptor.MorphWeights.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var morph in VisualCatalog.Instance.BodyBlendShapes.Keys) zeroMorphs[morph] = 0;
        descriptor = descriptor with { FixtureId = "renderer-probe.zero-morphs", DisplayLabel = "TEST ONLY neutral material probe", MorphWeights = zeroMorphs, TestOnly = true };
        if (!Show(descriptor, "")) return;
        foreach (var light in FindAll<DirectionalLight3D>(_stage)) light.ShadowEnabled = false;
        foreach (var geometry in FindAll<GeometryInstance3D>(_stage)) geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (var world in FindAll<WorldEnvironment>(_stage)) world.Environment.SsaoEnabled = false;
        var matte = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.48f, 0.48f, 0.48f),
            Roughness = 0.95f,
            Metallic = 0f,
        };
        foreach (var mesh in FindAll<MeshInstance3D>(_stage.Presenter.Model!))
        {
            mesh.MaterialOverride = matte;
            mesh.LodBias = 1000f; // Godot documents higher bias as retaining higher detail farther away.
        }
        _stage.FrameView("head");
        await Shot("material_probe/test_3__head__matte_full_detail_no_shadows.png", "material_probe", descriptor, "head",
            "synthetic zero-morph neutral standard material probe with directional shadows, cast shadows, and SSAO disabled; high LOD bias retains detail");
    }

    private async Task NeckFitProbe()
    {
        _stage.SetMode(LightingMode.Neutral);
        var descriptor = FixtureLibrary.AppearanceStudio.FirstOrDefault(d => d.MeshFamilyId == "horse.standard.adult.v1");
        if (descriptor == null)
        {
            _failures++;
            GD.PushError("[capture] no standard adult descriptor available for neck fit probe");
            return;
        }

        foreach (var value in new[] { -2000, 2000 })
        {
            var weights = descriptor.MorphWeights.ToDictionary(pair => pair.Key, _ => 0, StringComparer.Ordinal);
            if (!weights.ContainsKey("neck_length"))
            {
                _failures++;
                GD.PushError("[capture] descriptor does not expose neck_length");
                return;
            }
            weights["neck_length"] = value;
            var probe = descriptor with
            {
                FixtureId = $"renderer-neck-fit.{value}",
                DisplayLabel = $"TEST ONLY neck fit probe {value}",
                MorphWeights = weights,
                TestOnly = true,
            };
            if (!Show(probe, "")) continue;
            DisableDiagnosticShadows();
            VerifyMorphValues(probe);
            _stage.FrameView("head");
            await Shot($"appearance/neck_fit_{value}_head.png", "neck_fit", probe, "head",
                "synthetic neck_length-only descriptor at full supported boundary; shadows disabled for eye/head fit inspection");
            _stage.FrameView("q3");
            await Shot($"appearance/neck_fit_{value}_q3.png", "neck_fit", probe, "q3",
                "synthetic neck_length-only descriptor at full supported boundary; shadows disabled for silhouette inspection");
        }
    }

    private void RecordAnimationTracks(VisualDescriptor descriptor, string requestedAnimation = "idle",
        double positionBefore = 0, double positionAfter = 0, double clipLength = 0)
    {
        var player = _stage.Presenter.Animator;
        var tracks = player?.GetAnimationList() ?? Array.Empty<string>();
        var skeleton = _stage.Presenter.Model == null
            ? null
            : FindAll<Skeleton3D>(_stage.Presenter.Model).FirstOrDefault();
        _animationTrackEvidence.Add(new Dictionary<string, object?>
        {
            ["fixtureId"] = descriptor.FixtureId,
            ["meshFamilyId"] = descriptor.MeshFamilyId,
            ["animationPlayerFound"] = player != null,
            ["tracks"] = tracks,
            ["requestedAnimation"] = requestedAnimation,
            ["resolvedAnimation"] = _stage.Presenter.CurrentAnimation,
            ["isPlayingAfterFrames"] = player?.IsPlaying() ?? false,
            ["positionBeforeFrames"] = positionBefore,
            ["positionAfterFrames"] = positionAfter,
            ["clipLengthSeconds"] = clipLength,
            ["positionAdvanced"] = clipLength > 0 && (positionAfter > positionBefore + 0.0001 ||
                (positionAfter < positionBefore && clipLength - positionBefore + positionAfter > 0.0001)),
            ["hasExactLowercaseIdle"] = player?.HasAnimation("idle") ?? false,
            ["hasExactUppercaseIdle"] = player?.HasAnimation("Idle") ?? false,
            ["skeletonBoneCount"] = skeleton?.GetBoneCount() ?? 0,
            ["skeletonBoneNames"] = skeleton == null
                ? Array.Empty<string>()
                : Enumerable.Range(0, skeleton.GetBoneCount()).Select(index => skeleton.GetBoneName(index).ToString()).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            ["meshComponents"] = _stage.Presenter.Model == null
                ? Array.Empty<string>()
                : FindAll<MeshInstance3D>(_stage.Presenter.Model).Select(mesh => mesh.Name.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
        });
    }

    private void DisableDiagnosticShadows()
    {
        foreach (var light in FindAll<DirectionalLight3D>(_stage)) light.ShadowEnabled = false;
        foreach (var geometry in FindAll<GeometryInstance3D>(_stage)) geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (var world in FindAll<WorldEnvironment>(_stage)) world.Environment.SsaoEnabled = false;
    }

    private List<Dictionary<string, object?>> MeasureMeshLods(VisualDescriptor descriptor)
    {
        var evidence = new List<Dictionary<string, object?>>();
        var presenter = _stage.Presenter;
        if (presenter.Model == null) return evidence;
        foreach (var mesh in FindAll<MeshInstance3D>(presenter.Model))
        {
            if (mesh.Mesh == null) continue;
            for (int surfaceIndex = 0; surfaceIndex < mesh.Mesh.GetSurfaceCount(); surfaceIndex++)
            {
                var nativeSurface = RenderingServer.MeshGetSurface(mesh.Mesh.GetRid(), surfaceIndex);
                var lods = nativeSurface.ContainsKey("lods") ? nativeSurface["lods"].AsGodotArray() : new Godot.Collections.Array();
                var lodDetails = new List<Dictionary<string, object?>>();
                foreach (var lodVariant in lods)
                {
                    var lod = lodVariant.AsGodotDictionary();
                    int indexBytes = lod.ContainsKey("index_data") ? lod["index_data"].AsByteArray().Length : 0;
                    lodDetails.Add(new Dictionary<string, object?>
                    {
                        ["edgeLength"] = lod.ContainsKey("edge_length") ? lod["edge_length"].AsSingle() : 0f,
                        ["indexDataBytes"] = indexBytes,
                    });
                }
                evidence.Add(new Dictionary<string, object?>
                {
                    ["fixtureId"] = descriptor.FixtureId,
                    ["component"] = mesh.Name.ToString(),
                    ["surface"] = surfaceIndex,
                    ["baseIndexBytes"] = nativeSurface.ContainsKey("index_data") ? nativeSurface["index_data"].AsByteArray().Length : 0,
                    ["lodCount"] = lodDetails.Count,
                    ["lods"] = lodDetails,
                });
            }
        }
        return evidence;
    }

    private void VerifyMorphValues(VisualDescriptor descriptor)
    {
        var meshGroups = FindAll<MeshInstance3D>(_stage.Presenter.Model!)
            .GroupBy(mesh => mesh.Name.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var catalog = VisualCatalog.Instance;
        if (!catalog.FamilyMeshBlendShapes.TryGetValue(descriptor.MeshFamilyId, out var familyChannels))
        {
            _failures++;
            GD.PushError($"[capture] {descriptor.FixtureId}: no family morph contract for {descriptor.MeshFamilyId}");
            return;
        }
        foreach (var (component, channels) in familyChannels)
        {
            if (!meshGroups.TryGetValue(component, out var matches) || matches.Length != 1)
            {
                if (channels.Count > 0)
                {
                    _failures++;
                    GD.PushError($"[capture] {descriptor.FixtureId}: {component} mesh expected exactly once, found {(matches?.Length ?? 0)}");
                }
                continue;
            }
            var mesh = matches[0];
            foreach (var (morph, shape) in channels)
            {
                int index = mesh.FindBlendShapeByName(shape);
                float expected = descriptor.MorphWeights.TryGetValue(morph, out var weight)
                    ? Mathf.Clamp(weight / catalog.BodyBlendShapeNormalization, -1f, 1f)
                    : 0f;
                if (index < 0 || Mathf.Abs(mesh.GetBlendShapeValue(index) - expected) > 0.0001f)
                {
                    _failures++;
                    GD.PushError($"[capture] {descriptor.FixtureId}: {component}.{morph} expected {expected}, got {(index < 0 ? "missing" : mesh.GetBlendShapeValue(index).ToString())}");
                }
            }
        }
    }

    private async Task ViewerAgeToggleProbe()
    {
        var packed = GD.Load<PackedScene>("res://scenes/viewer.tscn");
        if (packed == null)
        {
            _failures++;
            GD.PushError("[capture] viewer age-toggle probe could not load the real viewer scene");
            return;
        }

        var viewer = packed.Instantiate<HorseViewer>();
        AddChild(viewer);
        await Settle(2);
        var stage = FindAll<HorseStage>(viewer).FirstOrDefault();
        var selector = FindAll<OptionButton>(viewer).FirstOrDefault(node => node.Name == "CoatSelector");
        var animationSelector = FindAll<OptionButton>(viewer).FirstOrDefault(node => node.Name == "AnimationSelector");
        var foalToggle = FindAll<CheckButton>(viewer).FirstOrDefault(node => node.Name == "FoalToggle");
        if (stage == null || selector == null || animationSelector == null || foalToggle == null)
        {
            _failures++;
            GD.PushError("[capture] viewer age-toggle probe could not find its real Stage, CoatSelector, AnimationSelector, and FoalToggle controls");
            viewer.QueueFree();
            return;
        }

        int idleIndex = Enumerable.Range(0, animationSelector.ItemCount)
            .FirstOrDefault(index => animationSelector.GetItemText(index) == "idle", -1);
        int grazeIndex = Enumerable.Range(0, animationSelector.ItemCount)
            .FirstOrDefault(index => animationSelector.GetItemText(index) == "graze", -1);
        int stillIndex = animationSelector.ItemCount - 1;
        if (idleIndex < 0 || grazeIndex < 0 || stillIndex < 0 || animationSelector.GetItemText(stillIndex) != "(still)")
        {
            _failures++;
            GD.PushError("[capture] viewer animation selector is missing idle, graze, or its final still option");
            viewer.QueueFree();
            return;
        }

        int markingIndex = Enumerable.Range(0, selector.ItemCount)
            .FirstOrDefault(index => selector.GetItemText(index).Contains("test markings #0", StringComparison.Ordinal), -1);
        if (markingIndex < 0)
        {
            _failures++;
            GD.PushError("[capture] viewer age-toggle probe could not find a real custom marking-variety entry");
            viewer.QueueFree();
            return;
        }

        selector.Select(markingIndex);
        selector.EmitSignal("item_selected", markingIndex);
        await Settle(2);
        var adult = stage.Presenter.Descriptor;
        var selectedKey = adult?.MarkingRealizationKey ?? "";
        bool adultIdleAvailable = !animationSelector.IsItemDisabled(idleIndex) && stage.Presenter.IsAnimationAvailable("idle");
        bool adultGrazeUnavailable = animationSelector.IsItemDisabled(grazeIndex) && !stage.Presenter.IsAnimationAvailable("graze");
        animationSelector.Select(idleIndex);
        animationSelector.EmitSignal("item_selected", idleIndex);
        double adultIdleBefore = stage.Presenter.Animator?.CurrentAnimationPosition ?? 0;
        await Settle(8);
        double adultIdleAfter = stage.Presenter.Animator?.CurrentAnimationPosition ?? 0;
        double adultIdleLength = stage.Presenter.Animator?.CurrentAnimationLength ?? 0;
        string adultResolvedIdle = stage.Presenter.CurrentAnimation;
        bool adultIdleAdvanced = stage.Presenter.Animator?.IsPlaying() == true && adultIdleLength > 0 &&
            (adultIdleAfter > adultIdleBefore + 0.0001 ||
             (adultIdleAfter < adultIdleBefore && adultIdleLength - adultIdleBefore + adultIdleAfter > 0.0001));

        foalToggle.SetPressedNoSignal(true);
        foalToggle.EmitSignal("toggled", true);
        await Settle(2);
        var foal = stage.Presenter.Descriptor;
        bool reachedFoal = foal?.MeshFamilyId == "horse.standard.foal.v1";
        bool markingKeyPreserved = !string.IsNullOrEmpty(selectedKey) && foal?.MarkingRealizationKey == selectedKey;
        bool foalIdleAvailable = !animationSelector.IsItemDisabled(idleIndex) && stage.Presenter.IsAnimationAvailable("idle");
        bool foalGrazeUnavailable = animationSelector.IsItemDisabled(grazeIndex) && !stage.Presenter.IsAnimationAvailable("graze");
        animationSelector.Select(idleIndex);
        animationSelector.EmitSignal("item_selected", idleIndex);
        double foalIdleBefore = stage.Presenter.Animator?.CurrentAnimationPosition ?? 0;
        await Settle(8);
        double foalIdleAfter = stage.Presenter.Animator?.CurrentAnimationPosition ?? 0;
        double foalIdleLength = stage.Presenter.Animator?.CurrentAnimationLength ?? 0;
        string foalResolvedIdle = stage.Presenter.CurrentAnimation;
        bool foalIdleAdvanced = stage.Presenter.Animator?.IsPlaying() == true && foalIdleLength > 0 &&
            (foalIdleAfter > foalIdleBefore + 0.0001 ||
             (foalIdleAfter < foalIdleBefore && foalIdleLength - foalIdleBefore + foalIdleAfter > 0.0001));

        foalToggle.SetPressedNoSignal(false);
        foalToggle.EmitSignal("toggled", false);
        await Settle(2);
        var restoredAdult = stage.Presenter.Descriptor;
        bool returnedToAdult = restoredAdult?.MeshFamilyId == "horse.standard.adult.v1";
        bool markingKeyStableOnReturn = restoredAdult?.MarkingRealizationKey == selectedKey;

        int probeIndex = Enumerable.Range(0, selector.ItemCount)
            .FirstOrDefault(index => selector.GetItemText(index).Contains("probe", StringComparison.OrdinalIgnoreCase), -1);
        string probeId = "";
        bool probeStayedUnsupported = false;
        if (probeIndex >= 0)
        {
            selector.Select(probeIndex);
            selector.EmitSignal("item_selected", probeIndex);
            foalToggle.SetPressedNoSignal(true);
            foalToggle.EmitSignal("toggled", true);
            await Settle(2);
            probeId = stage.Presenter.Descriptor?.FixtureId ?? "";
            probeStayedUnsupported = probeId.StartsWith("probe.", StringComparison.Ordinal) &&
                stage.Presenter.Support?.Supported == false && stage.Presenter.Model == null &&
                animationSelector.IsItemDisabled(idleIndex) && !stage.Presenter.IsAnimationAvailable("idle");
        }

        _viewerPathEvidence.Add(new Dictionary<string, object?>
        {
            ["selectedMarkingEntry"] = selector.GetItemText(markingIndex),
            ["selectedMarkingKey"] = selectedKey,
            ["adultFamily"] = adult?.MeshFamilyId ?? "",
            ["foalFamily"] = foal?.MeshFamilyId ?? "",
            ["returnedAdultFamily"] = restoredAdult?.MeshFamilyId ?? "",
            ["markingKeyPreservedOnFoalToggle"] = markingKeyPreserved,
            ["markingKeyStableOnAdultReturn"] = markingKeyStableOnReturn,
            ["stillOptionIsLast"] = stillIndex == animationSelector.ItemCount - 1,
            ["adultIdleAvailable"] = adultIdleAvailable,
            ["adultIdleResolvedAnimation"] = adultResolvedIdle,
            ["adultIdleClipLengthSeconds"] = adultIdleLength,
            ["adultIdlePlaybackAdvanced"] = adultIdleAdvanced,
            ["adultGrazeUnavailableAndDisabled"] = adultGrazeUnavailable,
            ["foalIdleAvailable"] = foalIdleAvailable,
            ["foalIdleResolvedAnimation"] = foalResolvedIdle,
            ["foalIdleClipLengthSeconds"] = foalIdleLength,
            ["foalIdlePlaybackAdvanced"] = foalIdleAdvanced,
            ["foalGrazeUnavailableAndDisabled"] = foalGrazeUnavailable,
            ["probeFixtureIdAfterFoalToggle"] = probeId,
            ["probeRemainsUnsupported"] = probeStayedUnsupported,
        });

        if (adult?.MeshFamilyId != "horse.standard.adult.v1" || !reachedFoal || !markingKeyPreserved ||
            !returnedToAdult || !markingKeyStableOnReturn || !probeStayedUnsupported || !adultIdleAvailable ||
            !adultIdleAdvanced || adultResolvedIdle != "Idle" || !adultGrazeUnavailable || !foalIdleAvailable ||
            !foalIdleAdvanced || foalResolvedIdle != "Idle" || !foalGrazeUnavailable || stillIndex != animationSelector.ItemCount - 1)
        {
            _failures++;
            GD.PushError($"[capture] viewer age-toggle/animation probe failed: adult={adult?.MeshFamilyId} Idle={adultResolvedIdle}/{adultIdleAdvanced}/{adultIdleAvailable}; foal={foal?.MeshFamilyId} Idle={foalResolvedIdle}/{foalIdleAdvanced}/{foalIdleAvailable}; graze={adultGrazeUnavailable}/{foalGrazeUnavailable}; adultReturn={restoredAdult?.MeshFamilyId}; markingKey={selectedKey == foal?.MarkingRealizationKey && selectedKey == restoredAdult?.MarkingRealizationKey}; probe={probeId}/{probeStayedUnsupported}");
        }

        viewer.QueueFree();
        await Settle(1);
    }

    private async Task FoalPreviewSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        var foal = FixtureLibrary.Get("chestnut", true);
        var adult = FixtureLibrary.Get("chestnut", false);
        if (foal.MeshFamilyId != "horse.standard.foal.v1")
        {
            _failures++;
            GD.PushError($"[capture] expected domain foal fixture on horse.standard.foal.v1, got {foal.MeshFamilyId}");
            return;
        }

        VerifyFamilyMorphCatalogContracts(foal);

        if (!Show(foal, "")) return;
        RecordAnimationTracks(foal, "");
        VerifyFoalRigInventory(foal);
        _meshLodEvidence.Clear();
        _meshLodEvidence.AddRange(MeasureMeshLods(foal));
        var componentNames = _stage.Presenter.Model == null
            ? Array.Empty<string>()
            : FindAll<MeshInstance3D>(_stage.Presenter.Model).Select(mesh => mesh.Name.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!new[] { "Body", "Eyes", "Mane", "Tail" }.All(componentNames.Contains))
        {
            _failures++;
            GD.PushError($"[capture] foal model missing required visual components; found [{string.Join(", ", componentNames)}]");
        }
        VerifyMorphValues(foal);
        foreach (var view in new[] { "head", "front", "side", "side_left" })
        {
            _stage.FrameView(view);
            await Shot($"foal/{foal.FixtureId}__{view}.png", "foal_preview", foal, view,
                "domain-exported phase-one foal descriptor shown on the production juvenile master");
        }

        var sameKeyAdult = adult.WithTestMarkingKey(foal.MarkingRealizationKey, "TEST ONLY adult with foal marking key");
        var sameKeyFoal = foal.WithTestMarkingKey(foal.MarkingRealizationKey, "TEST ONLY foal with adult marking key");
        if (Show(sameKeyAdult, ""))
        {
            _stage.FrameView("head");
            await Shot("foal/same_marking_key__adult_head.png", "foal_preview", sameKeyAdult, "head",
                "TEST-ONLY shared markingRealizationKey comparison; no genetics claim");
        }
        if (Show(sameKeyFoal, ""))
        {
            VerifyMorphValues(sameKeyFoal);
            _stage.FrameView("head");
            await Shot("foal/same_marking_key__foal_head.png", "foal_preview", sameKeyFoal, "head",
                "TEST-ONLY shared markingRealizationKey comparison; no genetics claim");
        }

        var compactShared = foal with
        {
            FixtureId = "foal_preview.compact_shared_base",
            DisplayLabel = "TEST ONLY compact family shared juvenile base",
            MeshFamilyId = "horse.compact.foal.v1",
            TestOnly = true,
        };
        if (Show(compactShared, ""))
        {
            VerifyMorphValues(compactShared);
            _stage.FrameView("q3");
            await Shot("foal/compact_shared_base__q3.png", "foal_preview", compactShared, "q3",
                "TEST-ONLY compact family uses shared juvenile master; catalog limitation discloses no distinct compact foal base");
        }

        foreach (var morph in VisualCatalog.Instance.BodyBlendShapes.Keys)
        foreach (var value in new[] { -2000, 2000 })
        {
            var weights = foal.MorphWeights.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            weights["height"] = 0;
            foreach (var key in VisualCatalog.Instance.BodyBlendShapes.Keys) weights[key] = 0;
            weights[morph] = value;
            var probe = foal with
            {
                FixtureId = $"foal_preview.morph.{morph}.{value}",
                DisplayLabel = $"TEST ONLY foal {morph} endpoint {value}",
                MorphWeights = weights,
                TestOnly = true,
            };
            if (!Show(probe, "")) continue;
            VerifyMorphValues(probe);
            _stage.FrameView(morph == "neck_length" ? "head" : "q3");
            await Shot($"foal/morph_{morph}_{value}.png", "foal_preview", probe, morph == "neck_length" ? "head" : "q3",
                "TEST-ONLY isolated descriptor morph endpoint; no offspring or genetics claim");
        }
    }

    private async Task FoalRigInventorySet()
    {
        _stage.SetMode(LightingMode.Neutral);
        var foal = FixtureLibrary.Get("chestnut", true);
        if (foal.MeshFamilyId != "horse.standard.foal.v1")
        {
            _failures++;
            GD.PushError($"[capture] expected domain foal fixture on horse.standard.foal.v1, got {foal.MeshFamilyId}");
            return;
        }

        if (!Show(foal, "")) return;
        await Settle();
        VerifyFoalRigInventory(foal);
        var model = _stage.Presenter.Model;
        var components = model == null
            ? Array.Empty<string>()
            : FindAll<MeshInstance3D>(model).Select(mesh => mesh.Name.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!new[] { "Body", "Eyes", "Mane", "Tail" }.All(components.Contains))
        {
            _failures++;
            GD.PushError($"[capture] foal model missing required visual components; found [{string.Join(", ", components)}]");
        }

        VerifyMorphValues(foal);
        _meshLodEvidence.Clear();
        _meshLodEvidence.AddRange(MeasureMeshLods(foal));
        if (_foalRigEvidence.Count > 0)
        {
            _foalRigEvidence[^1]["components"] = components;
        }
    }

    private async Task FoalIdleSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        var foal = FixtureLibrary.Get("chestnut", true);
        var support = _stage.Presenter.Present(foal, "idle");
        if (!support.Supported)
        {
            _failures++;
            GD.PushError($"[capture] foal_idle unsupported: {support.Summary}");
            return;
        }

        var skeletons = _stage.Presenter.Model == null
            ? Array.Empty<Skeleton3D>()
            : FindAll<Skeleton3D>(_stage.Presenter.Model).ToArray();
        VerifyFoalRigInventory(foal);
        var player = _stage.Presenter.Animator;
        if (player == null || string.IsNullOrEmpty(_stage.Presenter.CurrentAnimation))
        {
            _failures++;
            GD.PushError("[capture] foal_idle did not resolve an imported Idle animation");
            return;
        }

        double before = player.CurrentAnimationPosition;
        await Settle(8);
        double after = player.CurrentAnimationPosition;
        double length = player.CurrentAnimationLength;
        RecordAnimationTracks(foal, "idle", before, after, length);
        bool advanced = length > 0 && (after > before + 0.0001 || (after < before && length - before + after > 0.0001));
        if (!player.IsPlaying() || !advanced)
        {
            _failures++;
            GD.PushError($"[capture] foal Idle did not advance: {before}->{after}, length={length}");
        }
        if (skeletons.Length != 1 || length <= 0)
        {
            _failures++;
            GD.PushError($"[capture] foal Idle proof requires one skeleton and positive clip length; skeletons={skeletons.Length}, length={length}");
            return;
        }

        var skeleton = skeletons[0];
        var idleClip = player.GetAnimation(_stage.Presenter.CurrentAnimation);
        var importedTrackPaths = idleClip == null
            ? Array.Empty<string>()
            : Enumerable.Range(0, idleClip.GetTrackCount()).Select(index => idleClip.TrackGetPath(index).ToString()).ToArray();
        var importedTrackSamples = idleClip == null
            ? Array.Empty<Dictionary<string, object?>>()
            : Enumerable.Range(0, idleClip.GetTrackCount()).Select(index =>
            {
                int count = idleClip.TrackGetKeyCount(index);
                return new Dictionary<string, object?>
                {
                    ["path"] = idleClip.TrackGetPath(index).ToString(),
                    ["type"] = idleClip.TrackGetType(index).ToString(),
                    ["keyCount"] = count,
                    ["firstKeyTime"] = count > 0 ? idleClip.TrackGetKeyTime(index, 0) : 0,
                    ["middleKeyTime"] = count > 0 ? idleClip.TrackGetKeyTime(index, count / 2) : 0,
                    ["lastKeyTime"] = count > 0 ? idleClip.TrackGetKeyTime(index, count - 1) : 0,
                    ["firstKeyValue"] = count > 0 ? idleClip.TrackGetKeyValue(index, 0).ToString() : "",
                    ["middleKeyValue"] = count > 0 ? idleClip.TrackGetKeyValue(index, count / 2).ToString() : "",
                    ["lastKeyValue"] = count > 0 ? idleClip.TrackGetKeyValue(index, count - 1).ToString() : "",
                };
            }).ToArray();
        var animationRoot = player.GetNodeOrNull(player.RootNode);
        var boneIndices = Enumerable.Range(0, skeleton.GetBoneCount())
            .ToDictionary(index => skeleton.GetBoneName(index).ToString(), index => index, StringComparer.Ordinal);
        if (!boneIndices.TryGetValue("Root", out int rootIndex) || !boneIndices.TryGetValue("Neck", out int neckIndex) ||
            !boneIndices.TryGetValue("Head", out int headIndex))
        {
            _failures++;
            GD.PushError("[capture] foal Idle proof is missing Root, Neck, or Head bones");
            return;
        }

        // Playback advancement was verified above. Sample pose evidence at exact
        // clip times: screenshot I/O can span most of this subtle four-second loop.
        // Waiting for a wall-clock threshold after capture can miss both extrema.
        _stage.Presenter.Pose("idle", 0);
        // Pose seeks with update=true and pauses playback. Let the scene tree
        // flush deferred skeleton/skin updates without advancing that clip time.
        await Settle(2);
        var rootPositionAtStart = skeleton.GetBonePosePosition(rootIndex);
        var rootRotationAtStart = skeleton.GetBonePoseRotation(rootIndex);
        var neckPositionAtStart = skeleton.GetBonePosePosition(neckIndex);
        var neckRotationAtStart = skeleton.GetBonePoseRotation(neckIndex);
        var headPositionAtStart = skeleton.GetBonePosePosition(headIndex);
        var headRotationAtStart = skeleton.GetBonePoseRotation(headIndex);
        var samples = new List<Dictionary<string, object?>>();
        bool headOrNeckMoved = false;
        bool rootStayedFixed = true;
        float maxHeadNeckPositionDelta = 0f;
        float maxHeadNeckRotationDelta = 0f;
        foreach (var view in new[] { "head", "front", "side", "q3" })
        {
            _stage.FrameView(view);
            await Shot($"foal_idle/initial_{view}.png", "foal_idle", foal, $"initial_{view}",
                "neutral lighting at exact imported Idle time zero; real playback advancement was checked separately");
        }
        foreach (var (name, fraction) in new[] { ("quarter", 0.25), ("midpoint", 0.5) })
        {
            double targetTime = length * fraction;
            _stage.Presenter.Pose("idle", targetTime);
            await Settle(2);
            if (Math.Abs(player.CurrentAnimationPosition-targetTime)>0.0001)
            {
                _failures++;
                GD.PushError($"[capture] foal Idle exact pose sample failed: {name}, target {targetTime}, actual {player.CurrentAnimationPosition}");
                return;
            }
            var rootPosition = skeleton.GetBonePosePosition(rootIndex);
            var rootRotation = skeleton.GetBonePoseRotation(rootIndex);
            var neckPosition = skeleton.GetBonePosePosition(neckIndex);
            var neckRotation = skeleton.GetBonePoseRotation(neckIndex);
            var headPosition = skeleton.GetBonePosePosition(headIndex);
            var headRotation = skeleton.GetBonePoseRotation(headIndex);
            float rootPositionDelta = rootPosition.DistanceTo(rootPositionAtStart);
            float rootRotationDelta = rootRotation.AngleTo(rootRotationAtStart);
            float neckPositionDelta = neckPosition.DistanceTo(neckPositionAtStart);
            float neckRotationDelta = neckRotation.AngleTo(neckRotationAtStart);
            float headPositionDelta = headPosition.DistanceTo(headPositionAtStart);
            float headRotationDelta = headRotation.AngleTo(headRotationAtStart);
            maxHeadNeckPositionDelta = Math.Max(maxHeadNeckPositionDelta, Math.Max(neckPositionDelta, headPositionDelta));
            maxHeadNeckRotationDelta = Math.Max(maxHeadNeckRotationDelta, Math.Max(neckRotationDelta, headRotationDelta));
            rootStayedFixed &= rootPositionDelta <= 0.001f && rootRotationDelta <= 0.001f;
            headOrNeckMoved |= neckPositionDelta > 0.0001f || neckRotationDelta > 0.0001f ||
                               headPositionDelta > 0.0001f || headRotationDelta > 0.0001f;
            samples.Add(new Dictionary<string, object?>
            {
                ["sample"] = name,
                ["timeSeconds"] = player.CurrentAnimationPosition,
                ["rootPositionDelta"] = rootPositionDelta,
                ["rootRotationDeltaRadians"] = rootRotationDelta,
                ["neckPositionDelta"] = neckPositionDelta,
                ["neckRotationDeltaRadians"] = neckRotationDelta,
                ["headPositionDelta"] = headPositionDelta,
                ["headRotationDeltaRadians"] = headRotationDelta,
            });

            foreach (var view in new[] { "head", "front", "side" })
            {
                _stage.FrameView(view);
                await Shot($"foal_idle/{name}_{view}.png", "foal_idle", foal, $"{name}_{view}",
                    "neutral capture at the exact paused imported Idle sample time; real playback advancement checked separately");
            }
            if (name == "quarter")
            {
                _stage.FrameView("q3");
                await Shot("foal_idle/quarter_q3.png", "foal_idle", foal, "quarter_q3",
                    "neutral capture at the exact paused imported Idle sample time; real playback advancement checked separately");
            }
        }

        _foalIdleEvidence.Add(new Dictionary<string, object?>
        {
            ["fixtureId"] = foal.FixtureId,
            ["meshFamilyId"] = foal.MeshFamilyId,
            ["resolvedAnimation"] = _stage.Presenter.CurrentAnimation,
            ["tracks"] = player.GetAnimationList(),
            ["importedTrackPaths"] = importedTrackPaths,
            ["importedTrackSamples"] = importedTrackSamples,
            ["animatorRootNode"] = player.RootNode.ToString(),
            ["animatorRootResolvedPath"] = animationRoot?.GetPath().ToString() ?? "<missing>",
            ["modelPath"] = _stage.Presenter.Model?.GetPath().ToString() ?? "<missing>",
            ["animationPlayerPath"] = player.GetPath().ToString(),
            ["animationRootHasExpectedSkeleton"] = animationRoot?.GetNodeOrNull("Skeleton/Skeleton3D") is Skeleton3D,
            ["clipLengthSeconds"] = length,
            ["playbackAdvanced"] = advanced,
            ["rootStayedFixed"] = rootStayedFixed,
            ["headOrNeckPoseChanged"] = headOrNeckMoved,
            ["maximumHeadNeckPositionDelta"] = maxHeadNeckPositionDelta,
            ["maximumHeadNeckRotationDeltaRadians"] = maxHeadNeckRotationDelta,
            ["headNeckPoseWithinSmallMotionBounds"] = maxHeadNeckPositionDelta <= 0.12f && maxHeadNeckRotationDelta <= 0.30f,
            ["samples"] = samples,
        });
        bool smallPose = maxHeadNeckPositionDelta <= 0.12f && maxHeadNeckRotationDelta <= 0.30f;
        if (!rootStayedFixed || !headOrNeckMoved || !smallPose)
        {
            _failures++;
            GD.PushError($"[capture] foal Idle pose contract failed: rootStayedFixed={rootStayedFixed}, headOrNeckMoved={headOrNeckMoved}, smallPose={smallPose} (position={maxHeadNeckPositionDelta}, rotation={maxHeadNeckRotationDelta})");
        }
        VerifyMorphValues(foal);
        _meshLodEvidence.Clear();
        _meshLodEvidence.AddRange(MeasureMeshLods(foal));
    }

    private async Task AdultIdleImportSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        foreach (var familyId in new[] { "horse.standard.adult.v1", "horse.compact.adult.v1" })
        {
            var descriptor = FixtureLibrary.AppearanceStudio.FirstOrDefault(item => item.MeshFamilyId == familyId);
            if (descriptor == null)
            {
                _failures++;
                GD.PushError($"[capture] adult idle import probe has no descriptor for {familyId}");
                continue;
            }
            var support = _stage.Presenter.Present(descriptor, "idle");
            if (!support.Supported)
            {
                _failures++;
                GD.PushError($"[capture] adult idle import probe unsupported for {familyId}: {support.Summary}");
                continue;
            }
            var player = _stage.Presenter.Animator;
            var skeletons = _stage.Presenter.Model == null
                ? Array.Empty<Skeleton3D>()
                : FindAll<Skeleton3D>(_stage.Presenter.Model).ToArray();
            if (player == null || skeletons.Length != 1 || string.IsNullOrEmpty(_stage.Presenter.CurrentAnimation))
            {
                _failures++;
                GD.PushError($"[capture] adult idle import probe expected one skeleton and a resolved animation for {familyId}");
                continue;
            }

            var skeleton = skeletons[0];
            var names = Enumerable.Range(0, skeleton.GetBoneCount())
                .ToDictionary(index => skeleton.GetBoneName(index).ToString(), index => index, StringComparer.Ordinal);
            if (!names.TryGetValue("Root", out int rootIndex) || !names.TryGetValue("Head", out int headIndex))
            {
                _failures++;
                GD.PushError($"[capture] adult idle import probe missing Root or Head in {familyId}");
                continue;
            }

            var clip = player.GetAnimation(_stage.Presenter.CurrentAnimation);
            var headTrackCount = clip == null ? 0 : Enumerable.Range(0, clip.GetTrackCount())
                .Where(index => clip.TrackGetPath(index).ToString().EndsWith(":Head", StringComparison.Ordinal))
                .Select(clip.TrackGetKeyCount).DefaultIfEmpty(0).Max();
            var earTrackCount = clip == null ? 0 : Enumerable.Range(0, clip.GetTrackCount())
                .Where(index => clip.TrackGetPath(index).ToString().EndsWith(":Ear_L", StringComparison.Ordinal))
                .Select(clip.TrackGetKeyCount).DefaultIfEmpty(0).Max();
            double before = player.CurrentAnimationPosition;
            await Settle(1);
            var rootPositionStart = skeleton.GetBonePosePosition(rootIndex);
            var rootRotationStart = skeleton.GetBonePoseRotation(rootIndex);
            var headRotationStart = skeleton.GetBonePoseRotation(headIndex);
            double target = player.CurrentAnimationLength * 0.5;
            int frames = 0;
            float headRotationDelta = 0f;
            while (player.IsPlaying() && player.CurrentAnimationPosition + 0.0001 < target && frames < 180)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;
                headRotationDelta = Math.Max(headRotationDelta,
                    StableRotationDistance(headRotationStart, skeleton.GetBonePoseRotation(headIndex)));
            }
            var rootPositionEnd = skeleton.GetBonePosePosition(rootIndex);
            var rootRotationEnd = skeleton.GetBonePoseRotation(rootIndex);
            var headRotationEnd = skeleton.GetBonePoseRotation(headIndex);
            headRotationDelta = Math.Max(headRotationDelta, StableRotationDistance(headRotationStart, headRotationEnd));
            double after = player.CurrentAnimationPosition;
            double length = player.CurrentAnimationLength;
            RecordAnimationTracks(descriptor, "idle", before, after, length);
            bool rootFixed = rootPositionStart.DistanceTo(rootPositionEnd) <= 0.001f &&
                             rootRotationStart.AngleTo(rootRotationEnd) <= 0.001f;
            bool advanced = player.IsPlaying() && length > 0 && after > before + 0.0001;
            bool headMoved = headRotationDelta > 0.0001f;
            _adultIdleImportEvidence.Add(new Dictionary<string, object?>
            {
                ["fixtureId"] = descriptor.FixtureId,
                ["meshFamilyId"] = familyId,
                ["resolvedAnimation"] = _stage.Presenter.CurrentAnimation,
                ["clipLengthSeconds"] = length,
                ["headTrackKeyCount"] = headTrackCount,
                ["leftEarTrackKeyCount"] = earTrackCount,
                ["playbackAdvanced"] = advanced,
                ["rootStayedFixed"] = rootFixed,
                ["headRotationDeltaRadians"] = headRotationDelta,
                ["rotationMeasurement"] = "maximum throughout advancing half-cycle; relative-quaternion atan2",
                ["sampledFrames"] = frames,
                ["headPoseChanged"] = headMoved,
            });
            if (!advanced || !rootFixed || !headMoved || headTrackCount <= 1 || earTrackCount <= 1)
            {
                _failures++;
                GD.PushError($"[capture] adult Idle import check failed for {familyId}: advanced={advanced}, rootFixed={rootFixed}, headDelta={headRotationDelta}, headKeys={headTrackCount}, earKeys={earTrackCount}");
            }
        }
    }

    // acos(dot) rounds tiny Idle rotations to zero in single precision. The
    // vector part of the relative quaternion retains the same small movement.
    private static float StableRotationDistance(Quaternion a, Quaternion b)
    {
        var relative = a.Normalized().Inverse() * b.Normalized();
        double vectorLength = Math.Sqrt((double)relative.X * relative.X +
            (double)relative.Y * relative.Y + (double)relative.Z * relative.Z);
        return (float)(2.0 * Math.Atan2(vectorLength, Math.Abs(relative.W)));
    }

    private void VerifyFamilyMorphCatalogContracts(VisualDescriptor foal)
    {
        JsonNode ReadCatalog() => JsonNode.Parse(FileAccess.GetFileAsString(VisualCatalog.Path))!;
        const string familyId = "horse.standard.foal.v1";

        var malformed = ReadCatalog();
        malformed["morphSupport"]!["familyMeshBlendShapes"]![familyId]!["Body"]!.AsObject().Remove("neck_length");
        try
        {
            _ = VisualCatalog.Load(malformed.ToJsonString());
            _failures++;
            GD.PushError("[capture] family morph contract accepted an accessory neck_length channel without Body support");
        }
        catch (JsonException) { }

        var legacy = ReadCatalog();
        legacy["morphSupport"]!["familyMeshBlendShapes"]![familyId]!.AsObject().Clear();
        var legacyCatalog = VisualCatalog.Load(legacy.ToJsonString());
        var weights = foal.MorphWeights.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var key in weights.Keys.ToArray()) weights[key] = 0;
        weights["neck_length"] = 2000;
        var legacySupport = legacyCatalog.Check(foal with { MorphWeights = weights });
        if (!legacySupport.Supported || !legacySupport.Limitations.Any(limit => limit.Contains("morphs not applied: neck_length", StringComparison.Ordinal)))
        {
            _failures++;
            GD.PushError($"[capture] explicit empty legacy family contract expectation failed: supported={legacySupport.Supported}; limits=[{string.Join("; ", legacySupport.Limitations)}]; problems=[{string.Join("; ", legacySupport.Problems)}]");
        }
    }

    private void VerifyFoalRigInventory(VisualDescriptor foal)
    {
        var model = _stage.Presenter.Model;
        var skeletons = model == null ? Array.Empty<Skeleton3D>() : FindAll<Skeleton3D>(model).ToArray();
        var requiredBones = new[]
        {
            "Root", "Neck_Base", "Neck", "Head_Base", "Head",
            "Chest", "Front_L_Upper", "Front_L_Forearm", "Front_L_Cannon", "Front_L_Pastern", "Front_L_Hoof",
            "Front_R_Upper", "Front_R_Forearm", "Front_R_Cannon", "Front_R_Pastern", "Front_R_Hoof",
            "Pelvis", "Hind_L_Upper", "Hind_L_Gaskin", "Hind_L_Hock", "Hind_L_Cannon", "Hind_L_Pastern", "Hind_L_Hoof",
            "Hind_R_Upper", "Hind_R_Gaskin", "Hind_R_Hock", "Hind_R_Cannon", "Hind_R_Pastern", "Hind_R_Hoof",
        };
        var boneNames = skeletons.Length == 1
            ? Enumerable.Range(0, skeletons[0].GetBoneCount()).Select(index => skeletons[0].GetBoneName(index).ToString()).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var missing = skeletons.Length != 1
            ? new[] { $"expected exactly one Skeleton3D; found {skeletons.Length}" }
            : requiredBones.Where(name => !boneNames.Contains(name)).ToArray();
        var problems = missing.ToList();
        var headParentName = "<unavailable>";
        var headAncestry = Array.Empty<string>();
        var headAncestryReachesNeck = false;
        if (skeletons.Length == 1)
        {
            var skeleton = skeletons[0];
            if (skeleton.GetBoneCount() != 64)
                problems.Add($"expected final GLB's 64 skin joints; imported Skeleton3D has {skeleton.GetBoneCount()}");
            var boneIndices = Enumerable.Range(0, skeleton.GetBoneCount())
                .ToDictionary(index => skeleton.GetBoneName(index).ToString(), index => index, StringComparer.Ordinal);
            if (boneIndices.TryGetValue("Head", out var headIndex))
            {
                var parentIndex = skeleton.GetBoneParent(headIndex);
                headParentName = parentIndex >= 0 ? skeleton.GetBoneName(parentIndex).ToString() : "<root>";
                var ancestry = new List<string> { "Head" };
                var ancestorIndex = parentIndex;
                while (ancestorIndex >= 0 && ancestry.Count <= skeleton.GetBoneCount())
                {
                    var ancestorName = skeleton.GetBoneName(ancestorIndex).ToString();
                    ancestry.Add(ancestorName);
                    ancestorIndex = skeleton.GetBoneParent(ancestorIndex);
                }
                headAncestry = ancestry.ToArray();
                headAncestryReachesNeck = ancestry.Skip(1).Contains("Neck", StringComparer.Ordinal);
                if (!string.Equals(headParentName, "Head_Base", StringComparison.Ordinal))
                    problems.Add($"Head parent is {headParentName}, expected Head_Base");
                if (!headAncestryReachesNeck)
                    problems.Add($"Head ancestry [{string.Join(" -> ", headAncestry)}] does not reach Neck");
            }
            _foalRigEvidence.Add(new Dictionary<string, object?>
            {
                ["fixtureId"] = foal.FixtureId,
                ["meshFamilyId"] = foal.MeshFamilyId,
                ["skeletonCount"] = skeletons.Length,
                ["boneCount"] = skeleton.GetBoneCount(),
                ["expectedSkinJointCount"] = 64,
                ["headParent"] = headParentName,
                ["headAncestry"] = headAncestry,
                ["headAncestryReachesNeck"] = headAncestryReachesNeck,
            });
            var chains = new[]
            {
                new[] { "Chest", "Front_L_Upper", "Front_L_Forearm", "Front_L_Cannon", "Front_L_Pastern", "Front_L_Hoof" },
                new[] { "Chest", "Front_R_Upper", "Front_R_Forearm", "Front_R_Cannon", "Front_R_Pastern", "Front_R_Hoof" },
                new[] { "Pelvis", "Hind_L_Upper", "Hind_L_Gaskin", "Hind_L_Hock", "Hind_L_Cannon", "Hind_L_Pastern", "Hind_L_Hoof" },
                new[] { "Pelvis", "Hind_R_Upper", "Hind_R_Gaskin", "Hind_R_Hock", "Hind_R_Cannon", "Hind_R_Pastern", "Hind_R_Hoof" },
            };
            foreach (var chain in chains)
            for (int i = 1; i < chain.Length; i++)
            {
                if (!boneIndices.TryGetValue(chain[i - 1], out var parentIndex) || !boneIndices.TryGetValue(chain[i], out var childIndex))
                    continue; // Missing names are already reported above or by the component chain below.
                var actualParent = skeleton.GetBoneParent(childIndex);
                if (actualParent != parentIndex)
                    problems.Add($"{chain[i]} parent is {(actualParent < 0 ? "<root>" : skeleton.GetBoneName(actualParent).ToString())}, expected {chain[i - 1]}");
            }
        }
        if (problems.Count == 0) return;
        _failures++;
        GD.PushError($"[capture] {foal.FixtureId}: foal rig inventory/chain mismatch: {string.Join("; ", problems)}");
    }

    private async Task MarkingSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        var lightDescriptor = FixtureLibrary.AppearanceStudio.FirstOrDefault(d => d.MeshFamilyId == "horse.standard.adult.v1");
        if (lightDescriptor == null)
        {
            _failures++;
            GD.PushError("[capture] no standard adult descriptor available for Light asset evidence");
            return;
        }
        _meshLodEvidence.Clear();
        foreach (var familyDescriptor in FixtureLibrary.AppearanceStudio
                     .GroupBy(d => d.MeshFamilyId, StringComparer.Ordinal).Select(group => group.First()))
        {
            var support = _stage.Presenter.Present(familyDescriptor, "idle");
            if (!support.Supported)
            {
                _failures++;
                GD.PushError($"[capture] animation probe unsupported for {familyDescriptor.MeshFamilyId}: {support.Summary}");
                continue;
            }
            var player = _stage.Presenter.Animator;
            double positionBefore = player?.CurrentAnimationPosition ?? 0;
            await Settle(8);
            double positionAfter = player?.CurrentAnimationPosition ?? 0;
            double clipLength = player?.CurrentAnimationLength ?? 0;
            RecordAnimationTracks(familyDescriptor, "idle", positionBefore, positionAfter, clipLength);
            bool positionAdvanced = clipLength > 0 && (positionAfter > positionBefore + 0.0001 ||
                (positionAfter < positionBefore && clipLength - positionBefore + positionAfter > 0.0001));
            if (string.IsNullOrEmpty(_stage.Presenter.CurrentAnimation) || player?.IsPlaying() != true || !positionAdvanced)
            {
                _failures++;
                GD.PushError($"[capture] lowercase idle request did not advance for {familyDescriptor.MeshFamilyId}: resolved='{_stage.Presenter.CurrentAnimation}', playing={player?.IsPlaying() ?? false}, position {positionBefore}->{positionAfter}, length={clipLength}");
            }
            _meshLodEvidence.AddRange(MeasureMeshLods(familyDescriptor));
            _stage.Presenter.Pose("idle", 0.0); // deterministic stills after active-playback evidence
        }
        if (!Show(lightDescriptor, "")) return;
        // Capture the authored coat/markings without shadow-map noise so shape
        // boundary quality can be judged independently of the lighting probe.
        DisableDiagnosticShadows();
        int i = 0;
        foreach (var d in FixtureLibrary.MarkingVariety("chestnut", false, 8))
        {
            if (!Show(d)) continue;
            _stage.FrameView("q3"); await Shot($"markings/test_{i}__q3.png", "markings", d, "q3", "TEST-ONLY marking key");
            _stage.FrameView("head"); await Shot($"markings/test_{i}__head.png", "markings", d, "head", "TEST-ONLY marking key");
            if (i == 3)
            {
                _stage.FrameView("legs");
                await Shot("markings/test_3__legs.png", "markings", d, "legs", "targeted close inspection of the same marking key's leg socks");
            }
            i++;
        }
        // same key, adult vs foal: markings must match
        var key = FixtureLibrary.MarkingVariety("chestnut", false, 3).Last();
        var foalSame = FixtureLibrary.Get("chestnut", true).WithTestMarkingKey(key.MarkingRealizationKey, key.DisplayLabel + " (foal)");
        if (Show(foalSame))
        {
            _stage.FrameView("q3"); await Shot("markings/test_2__foal_q3.png", "markings", foalSame, "q3", "same key as test_2 adult");
            _stage.FrameView("head"); await Shot("markings/test_2__foal_head.png", "markings", foalSame, "head", "same key as test_2 adult; foal face check");
        }

        // Explicit art-only coordinates for a repeatable target: canonical face
        // star center s=.30/t=0 with radius .075, plus strip, snip and bald cases.
        // These bytes are marking-realizer test inputs, not genotype or save data.
        foreach (var (name, faceCode) in new[] { ("star_s030", 30), ("strip", 50), ("snip", 80), ("bald", 96) })
        {
            var keyText = DiagnosticMarkingKey(faceCode);
            var descriptor = lightDescriptor.WithTestMarkingKey(keyText, $"TEST ONLY diagnostic {name}");
            if (!Show(descriptor, "")) continue;
            DisableDiagnosticShadows();
            var realized = Markings.FromKey(keyText);
            var note = name == "star_s030"
                ? "synthetic TEST-ONLY MarkingRealizer key: star center s=.30/t=0, radius .075, near-zero offset; no genetics claim"
                : $"synthetic TEST-ONLY MarkingRealizer key for {realized.Face}; includes fore/hind sock checks; no genetics claim";
            foreach (var view in new[] { "front", "head", "q3" })
            {
                _stage.FrameView(view);
                await Shot($"markings/diagnostic_{name}__{view}.png", "markings", descriptor, view, note);
            }
            _stage.FrameView("legs");
            await Shot($"markings/diagnostic_{name}__legs.png", "markings", descriptor, "legs", note);
        }

        // A compact family gets a focused copy of the critical forehead-star test,
        // without repeating the entire marking matrix for the second adult mesh.
        var stockDescriptor = FixtureLibrary.AppearanceStudio.FirstOrDefault(d => d.MeshFamilyId == "horse.compact.adult.v1");
        if (stockDescriptor != null)
        {
            var stockStar = stockDescriptor.WithTestMarkingKey(DiagnosticMarkingKey(30), "TEST ONLY stock diagnostic forehead star");
            if (Show(stockStar, ""))
            {
                DisableDiagnosticShadows();
                const string note = "synthetic TEST-ONLY stock-family star center s=.30/t=0, radius .075; no genetics claim";
                _stage.FrameView("front");
                await Shot("markings/stock_diagnostic_star_s030__front.png", "markings", stockStar, "front", note);
                _stage.FrameView("head");
                await Shot("markings/stock_diagnostic_star_s030__head.png", "markings", stockStar, "head", note);
            }
        }
    }

    private async Task AdultRuntimeSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        // Deterministic art-only forehead star keeps the current face map visible.
        var lightBase = FixtureLibrary.AppearanceStudio.FirstOrDefault(d => d.MeshFamilyId == "horse.standard.adult.v1");
        if (lightBase == null)
        {
            _failures++;
            GD.PushError("[capture] adult_runtime has no Light descriptor in the appearance studio");
            return;
        }
        var descriptor = lightBase.WithTestMarkingKey(DiagnosticMarkingKey(30), "TEST ONLY Light forehead star");
        if (descriptor.MeshFamilyId != "horse.standard.adult.v1")
        {
            _failures++;
            GD.PushError($"[capture] adult_runtime expected Light family, got {descriptor.MeshFamilyId}");
            return;
        }

        var support = _stage.Presenter.Present(descriptor, "idle");
        if (!support.Supported)
        {
            _failures++;
            GD.PushError($"[capture] adult_runtime unsupported: {support.Summary}");
            return;
        }
        var player = _stage.Presenter.Animator;
        double positionBefore = player?.CurrentAnimationPosition ?? 0;
        await Settle(8);
        double positionAfter = player?.CurrentAnimationPosition ?? 0;
        double clipLength = player?.CurrentAnimationLength ?? 0;
        RecordAnimationTracks(descriptor, "idle", positionBefore, positionAfter, clipLength);
        bool positionAdvanced = clipLength > 0 && (positionAfter > positionBefore + 0.0001 ||
            (positionAfter < positionBefore && clipLength - positionBefore + positionAfter > 0.0001));
        if (player?.IsPlaying() != true || !positionAdvanced)
        {
            _failures++;
            GD.PushError($"[capture] Light Idle did not advance: {positionBefore}->{positionAfter}, length={clipLength}");
        }
        _meshLodEvidence.Clear();
        _meshLodEvidence.AddRange(MeasureMeshLods(descriptor));
        VerifyMorphValues(descriptor);
        _stage.Presenter.Pose("idle", 0.0);
        DisableDiagnosticShadows();

        foreach (var view in new[] { "head", "front", "side", "side_left", "q3" })
        {
            _stage.FrameView(view);
            await Shot($"adult_runtime/light_{view}.png", "adult_runtime", descriptor, view,
                "test-only forehead star on the appearance-studio coat and hair recipe; current Light GLB under neutral lighting; head target from manifest");
        }
        var target = _stage.Presenter.HeadViewTarget ?? new Vector3(1.04f, 1.66f, 0f);
        foreach (var (name, yaw) in new[] { ("front", 90f), ("side", 0f), ("side_left", 180f) })
        {
            _stage.Camera.SetView(yaw, 7f, 1.7f, target);
            await Shot($"adult_runtime/light_head_{name}.png", "adult_runtime", descriptor, $"head_{name}",
                "close view of ears, eye, muzzle, forehead star and mane attachment under neutral lighting");
        }
    }

    private async Task CandidateWalkSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        if (string.IsNullOrWhiteSpace(_candidateScene))
        {
            _failures++;
            GD.PushError("[capture] candidate_walk requires an explicit --candidate-scene res://path/to/candidate.glb input");
            _candidateWalkEvidence.Add(new Dictionary<string, object?>
            {
                ["status"] = "failed-missing-input",
                ["requiredArgument"] = "--candidate-scene res://...",
            });
            return;
        }
        if (!_candidateScene.StartsWith("res://", StringComparison.Ordinal) || !ResourceLoader.Exists(_candidateScene))
        {
            _failures++;
            GD.PushError($"[capture] candidate_walk scene '{_candidateScene}' is missing or is not a res:// resource");
            _candidateWalkEvidence.Add(new Dictionary<string, object?>
            {
                ["status"] = "failed-missing-candidate-scene",
                ["candidateScene"] = _candidateScene,
            });
            return;
        }

        var originalCatalog = VisualCatalog.Instance;
        var lightDescriptor = FixtureLibrary.AppearanceStudio.FirstOrDefault(d => d.MeshFamilyId == "horse.standard.adult.v1");
        var singletonField = typeof(VisualCatalog).GetField("_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (lightDescriptor == null || singletonField == null)
        {
            _failures++;
            GD.PushError("[capture] candidate_walk cannot find the Light appearance descriptor or VisualCatalog singleton");
            return;
        }

        try
        {
            var catalogRoot = JsonNode.Parse(FileAccess.GetFileAsString(VisualCatalog.Path))?.AsObject();
            var lightFamily = catalogRoot?["meshFamilies"]?["horse.standard.adult.v1"]?.AsObject();
            if (lightFamily == null)
                throw new InvalidOperationException("Light family is absent from the visual catalog JSON");
            lightFamily["scene"] = _candidateScene;
            var detail = lightFamily["bodySignedDetail"]?.AsObject();
            if (detail == null)
            {
                detail = new JsonObject();
                lightFamily["bodySignedDetail"] = detail;
            }
            // The accepted atlas is bound to the frozen Light GLB. Never carry it
            // into a Mesh2Motion candidate scene through this temporary catalog.
            detail["enabled"] = false;
            detail["strength"] = 0.0;
            var candidateCatalog = VisualCatalog.Load(catalogRoot!.ToJsonString());
            singletonField.SetValue(null, candidateCatalog);

            var descriptor = lightDescriptor.WithTestMarkingKey(
                DiagnosticMarkingKey(30), "TEST ONLY Mesh2Motion Walk candidate");
            var support = _stage.Presenter.Present(descriptor, "walk");
            if (!support.Supported || _stage.Presenter.Model == null)
            {
                _failures++;
                GD.PushError($"[capture] candidate_walk unsupported: {support.Summary}");
                _candidateWalkEvidence.Add(new Dictionary<string, object?>
                {
                    ["status"] = "unsupported-candidate-scene",
                    ["candidateScene"] = _candidateScene,
                    ["testOnly"] = descriptor.TestOnly,
                    ["detailExplicitlyDisabledInClone"] = true,
                    ["support"] = support.Summary,
                });
                return;
            }

            var presenter = _stage.Presenter;
            var player = presenter.Animator;
            string resolvedAnimation = presenter.CurrentAnimation;
            var clip = player != null && !string.IsNullOrEmpty(resolvedAnimation)
                ? player.GetAnimation(resolvedAnimation)
                : null;
            if (player == null || clip == null || !presenter.IsAnimationAvailable("walk") || clip.Length <= 0)
            {
                _failures++;
                GD.PushError($"[capture] candidate_walk has no positive-length, uniquely resolved Walk clip; resolved='{resolvedAnimation}'");
                return;
            }

            var components = FindAll<MeshInstance3D>(presenter.Model).ToArray();
            var componentGroups = components.GroupBy(mesh => mesh.Name.ToString(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var requiredComponents = new[] { "Body", "Eyes", "Mane", "Tail" };
            if (requiredComponents.Any(name => !componentGroups.TryGetValue(name, out var matches) || matches.Length != 1))
            {
                _failures++;
                GD.PushError("[capture] candidate_walk expected one Body, Eyes, Mane, and Tail component after Light-catalog presentation");
            }
            VerifyMorphValues(descriptor);

            bool finiteGeometry = true;
            var meshEvidence = new List<Dictionary<string, object?>>();
            foreach (var mesh in components)
            {
                var surfaces = new List<Dictionary<string, object?>>();
                for (int surfaceIndex = 0; mesh.Mesh != null && surfaceIndex < mesh.Mesh.GetSurfaceCount(); surfaceIndex++)
                {
                    if (mesh.Mesh is not ArrayMesh arrayMesh) continue;
                    var arrays = arrayMesh.SurfaceGetArrays(surfaceIndex);
                    var vertexValues = arrays.Count > (int)Mesh.ArrayType.Vertex
                        ? arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array()
                        : Array.Empty<Vector3>();
                    var boneValues = arrays.Count > (int)Mesh.ArrayType.Bones
                        ? arrays[(int)Mesh.ArrayType.Bones].AsInt32Array()
                        : Array.Empty<int>();
                    var weightValues = arrays.Count > (int)Mesh.ArrayType.Weights
                        ? arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array()
                        : Array.Empty<float>();
                    bool verticesFinite = vertexValues.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
                    bool weightsFinite = weightValues.All(float.IsFinite);
                    finiteGeometry &= verticesFinite && weightsFinite;
                    int influenceWidth = vertexValues.Length > 0 && boneValues.Length == weightValues.Length && boneValues.Length % vertexValues.Length == 0
                        ? boneValues.Length / vertexValues.Length
                        : 0;
                    surfaces.Add(new Dictionary<string, object?>
                    {
                        ["surface"] = surfaceIndex,
                        ["vertexCount"] = vertexValues.Length,
                        ["boneArrayCount"] = boneValues.Length,
                        ["weightArrayCount"] = weightValues.Length,
                        ["influenceWidth"] = influenceWidth,
                        ["verticesFinite"] = verticesFinite,
                        ["weightsFinite"] = weightsFinite,
                    });
                }
                var expectedMorphs = VisualCatalog.Instance.FamilyMeshBlendShapes[descriptor.MeshFamilyId]
                    .TryGetValue(mesh.Name.ToString(), out var channels)
                    ? channels.Values.ToArray()
                    : Array.Empty<string>();
                var availableMorphs = expectedMorphs
                    .Where(shape => mesh.FindBlendShapeByName(shape) >= 0)
                    .ToArray();
                meshEvidence.Add(new Dictionary<string, object?>
                {
                    ["component"] = mesh.Name.ToString(),
                    ["surfaceCount"] = mesh.Mesh?.GetSurfaceCount() ?? 0,
                    ["blendShapeCount"] = mesh.GetBlendShapeCount(),
                    ["expectedBlendShapes"] = expectedMorphs,
                    ["availableExpectedBlendShapes"] = availableMorphs,
                    ["surfaces"] = surfaces,
                });
            }
            if (!finiteGeometry)
            {
                _failures++;
                GD.PushError("[capture] candidate_walk found non-finite mesh vertices or skin weights");
            }

            var skeletons = FindAll<Skeleton3D>(presenter.Model).ToArray();
            if (skeletons.Length != 1)
            {
                _failures++;
                GD.PushError($"[capture] candidate_walk expected one imported Skeleton3D, found {skeletons.Length}");
            }
            var skeleton = skeletons.FirstOrDefault();
            var boneNames = skeleton == null
                ? Array.Empty<string>()
                : Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => skeleton.GetBoneName(i).ToString()).ToArray();
            (Vector3[] positions, Quaternion[] rotations, Vector3[] scales, bool finite) ReadPose()
            {
                if (skeleton == null) return (Array.Empty<Vector3>(), Array.Empty<Quaternion>(), Array.Empty<Vector3>(), false);
                var positions = new Vector3[skeleton.GetBoneCount()];
                var rotations = new Quaternion[skeleton.GetBoneCount()];
                var scales = new Vector3[skeleton.GetBoneCount()];
                bool finite = true;
                for (int i = 0; i < skeleton.GetBoneCount(); i++)
                {
                    positions[i] = skeleton.GetBonePosePosition(i);
                    rotations[i] = skeleton.GetBonePoseRotation(i);
                    scales[i] = skeleton.GetBonePoseScale(i);
                    var p = positions[i]; var q = rotations[i]; var s = scales[i];
                    finite &= float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) &&
                              float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W) &&
                              float.IsFinite(s.X) && float.IsFinite(s.Y) && float.IsFinite(s.Z);
                }
                return (positions, rotations, scales, finite);
            }
            float PoseDelta((Vector3[] positions, Quaternion[] rotations, Vector3[] scales, bool finite) a,
                (Vector3[] positions, Quaternion[] rotations, Vector3[] scales, bool finite) b, out float maxAngle, out float maxScale)
            {
                float maxPosition = 0f; maxAngle = 0f; maxScale = 0f;
                int count = Math.Min(a.positions.Length, b.positions.Length);
                for (int i = 0; i < count; i++)
                {
                    maxPosition = Math.Max(maxPosition, a.positions[i].DistanceTo(b.positions[i]));
                    maxAngle = Math.Max(maxAngle, a.rotations[i].AngleTo(b.rotations[i]));
                    maxScale = Math.Max(maxScale, a.scales[i].DistanceTo(b.scales[i]));
                }
                return maxPosition;
            }

            double clipLength = clip.Length;
            double startPosition = player.CurrentAnimationPosition;
            var poseBefore = ReadPose();
            await Settle(10);
            double progressedPosition = player.CurrentAnimationPosition;
            var poseAfter = ReadPose();
            bool playbackAdvanced = player.IsPlaying() && clipLength > 0 &&
                (progressedPosition > startPosition + 0.0001 ||
                 (progressedPosition < startPosition && clipLength - startPosition + progressedPosition > 0.0001));
            float maxPlaybackAngle, maxPlaybackScale;
            float maxPlaybackPosition = PoseDelta(poseBefore, poseAfter, out maxPlaybackAngle, out maxPlaybackScale);
            bool poseMoved = maxPlaybackPosition > 0.00001f || maxPlaybackAngle > 0.00001f || maxPlaybackScale > 0.00001f;
            bool skeletonPoseFinite = poseBefore.finite && poseAfter.finite;
            if (!playbackAdvanced || !poseMoved || !skeletonPoseFinite)
            {
                _failures++;
                GD.PushError($"[capture] candidate_walk playback/pose check failed: advanced={playbackAdvanced}, moved={poseMoved}, finite={skeletonPoseFinite}, delta={maxPlaybackPosition}/{maxPlaybackAngle}");
            }

            var phases = new List<Dictionary<string, object?>>();
            foreach (var (name, fraction) in new[] { ("early", 0.10), ("mid", 0.50), ("late", 0.85) })
            {
                double time = clipLength * fraction;
                presenter.Pose("walk", time);
                var phasePose = ReadPose();
                if (!phasePose.finite)
                {
                    _failures++;
                    GD.PushError($"[capture] candidate_walk phase '{name}' produced non-finite bone position/rotation/scale values");
                }
                foreach (var view in new[] { "side", "front", "q3" })
                {
                    _stage.FrameView(view);
                    await Shot($"candidate_walk/{name}_{view}.png", "candidate_walk", descriptor, $"{name}_{view}",
                        $"TEST ONLY imported candidate Walk clip at {time:F3}s ({fraction:P0}); neutral lighting");
                }
                phases.Add(new Dictionary<string, object?>
                {
                    ["phase"] = name,
                    ["fractionOfClip"] = fraction,
                    ["timeSeconds"] = time,
                    ["poseFinite"] = phasePose.finite,
                    ["bonePoseScale"] = phasePose.scales.Select(scale => new[] { scale.X, scale.Y, scale.Z }).ToArray(),
                });
            }

            player.Play(resolvedAnimation);
            player.Seek(Math.Max(0.0, clipLength - Math.Min(0.05, clipLength * 0.05)), true);
            double nearEndPosition = player.CurrentAnimationPosition;
            bool loopWrapped = false;
            int wrapWaitFrames = 0;
            while (player.IsPlaying() && wrapWaitFrames < 120)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                wrapWaitFrames++;
                if (player.CurrentAnimationPosition + 0.01 < nearEndPosition)
                {
                    loopWrapped = true;
                    break;
                }
            }
            if (!loopWrapped)
            {
                _failures++;
                GD.PushError($"[capture] candidate_walk did not wrap within {wrapWaitFrames} frames from {nearEndPosition:F4}s (mode={clip.LoopMode})");
            }

            var body = componentGroups.TryGetValue("Body", out var bodies) && bodies.Length == 1 ? bodies[0] : null;
            var bodyMaterial = body?.GetActiveMaterial(0) as ShaderMaterial;
            bool detailOff = bodyMaterial != null && !bodyMaterial.GetShaderParameter("use_signed_detail").AsBool() &&
                             Mathf.Abs(bodyMaterial.GetShaderParameter("signed_detail_strength").AsSingle()) <= 0.000001f;
            if (!detailOff)
            {
                _failures++;
                GD.PushError("[capture] candidate_walk clone did not keep Light Body signed detail explicitly disabled at false/zero");
            }

            _candidateWalkEvidence.Add(new Dictionary<string, object?>
            {
                ["status"] = "captured-test-only-candidate",
                ["candidateScene"] = _candidateScene,
                ["familySceneOverriddenInTemporaryCatalogClone"] = true,
                ["bodySignedDetailExplicitlyDisabledInClone"] = detailOff,
                ["fixtureId"] = descriptor.FixtureId,
                ["testOnly"] = descriptor.TestOnly,
                ["support"] = support.Summary,
                ["resolvedAnimation"] = resolvedAnimation,
                ["animationNames"] = player.GetAnimationList(),
                ["clipLengthSeconds"] = clipLength,
                ["clipLoopMode"] = clip.LoopMode.ToString(),
                ["playbackAdvanced"] = playbackAdvanced,
                ["playbackStartPosition"] = startPosition,
                ["playbackPositionAfterWait"] = progressedPosition,
                ["maximumPosePositionDelta"] = maxPlaybackPosition,
                ["maximumPoseRotationDeltaRadians"] = maxPlaybackAngle,
                ["maximumPoseScaleDelta"] = maxPlaybackScale,
                ["poseChangedWhilePlaying"] = poseMoved,
                ["skeletonPoseFinite"] = skeletonPoseFinite,
                ["skeletonCount"] = skeletons.Length,
                ["boneCount"] = skeleton?.GetBoneCount() ?? 0,
                ["boneNames"] = boneNames,
                ["meshComponents"] = meshEvidence,
                ["finiteMeshGeometryAndWeights"] = finiteGeometry,
                ["phaseSamples"] = phases,
                ["loopWrapObserved"] = loopWrapped,
                ["loopWrapWaitFrames"] = wrapWaitFrames,
                ["loopPositionNearEnd"] = nearEndPosition,
                ["loopPositionAfterWait"] = player.CurrentAnimationPosition,
                ["loopInterpretation"] = "Playback wrapped under HorsePresenter's forced loop mode; this does not validate skinning or prove authored seamless closure.",
                ["influenceWidthInterpretation"] = "Per-surface bone/weight width is inventory only; it does not prove skin validity or posed accuracy.",
            });
        }
        catch (Exception e)
        {
            _failures++;
            GD.PushError($"[capture] candidate_walk failed: {e}");
            _candidateWalkEvidence.Add(new Dictionary<string, object?>
            {
                ["status"] = "candidate-capture-exception",
                ["candidateScene"] = _candidateScene,
                ["error"] = e.ToString(),
            });
        }
        finally
        {
            singletonField.SetValue(null, originalCatalog);
            // Leave the shared stage back on its ordinary Light family if this
            // test-only set is combined with any other capture sets.
            _stage.Presenter.Present(lightDescriptor, "");
        }
    }

    private async Task SignedDetailBaseline()
    {
        _stage.SetMode(LightingMode.Neutral);
        const string familyId = "horse.standard.adult.v1";
        var descriptor = FixtureLibrary.AppearanceStudio.FirstOrDefault(d => d.MeshFamilyId == familyId);
        if (descriptor == null)
        {
            _failures++;
            GD.PushError("[capture] signed-detail baseline needs the Light appearance-studio descriptor");
            return;
        }

        var family = VisualCatalog.Instance.MeshFamilies[familyId];
        if (family.BodySignedDetail == null)
        {
            _failures++;
            GD.PushError("[capture] baseline comparison needs the accepted Light detail metadata");
        }
        VerifySignedDetailInvalidMetadataFailsClosed(family.Scene);
        if (!Show(descriptor, "")) return;
        var body = FindAll<MeshInstance3D>(_stage.Presenter.Model!).SingleOrDefault(mesh => mesh.Name == "Body");
        var material = body?.GetActiveMaterial(0) as ShaderMaterial;
        if (material == null || !material.GetShaderParameter("use_signed_detail").AsBool())
        {
            _failures++;
            GD.PushError("[capture] enabled baseline descriptor did not bind its accepted detail map");
            return;
        }
        var explicitlyDisabledMaterial = material.Duplicate() as ShaderMaterial;
        if (explicitlyDisabledMaterial == null || body == null)
        {
            _failures++;
            GD.PushError("[capture] could not construct the explicit detail-off comparison material");
            return;
        }
        explicitlyDisabledMaterial.SetShaderParameter("use_signed_detail", false);
        explicitlyDisabledMaterial.SetShaderParameter("signed_detail_strength", 0f);
        for (int surface = 0; surface < body.Mesh!.GetSurfaceCount(); surface++)
            body.SetSurfaceOverrideMaterial(surface, explicitlyDisabledMaterial);
        var disabledMaterial = body.GetActiveMaterial(0) as ShaderMaterial;
        if (disabledMaterial == null || disabledMaterial.GetShaderParameter("use_signed_detail").AsBool() ||
            Mathf.Abs(disabledMaterial.GetShaderParameter("signed_detail_strength").AsSingle()) > 0.000001f)
        {
            _failures++;
            GD.PushError("[capture] explicit disabled comparison did not set detail false/zero");
        }
        VerifyMorphValues(descriptor);
        _meshLodEvidence.Clear();
        _meshLodEvidence.AddRange(MeasureMeshLods(descriptor));
        _stage.FrameView("q3");
        await Shot("detail_baseline/light_body_detail_off.png", "detail_baseline", descriptor, "q3",
            "explicit cloned material comparison: accepted detail map disabled and strength zero; every other recipe input unchanged");
    }

    private void VerifySignedDetailInvalidMetadataFailsClosed(string sourceScene)
    {
        var sourceHash = Convert.ToHexString(SHA256.HashData(FileAccess.GetFileAsBytes(sourceScene))).ToLowerInvariant();
        VisualCatalog LoadWithBodyDetail(Action<JsonObject> mutate)
        {
            var root = JsonNode.Parse(FileAccess.GetFileAsString(VisualCatalog.Path))!.AsObject();
            var family = root["meshFamilies"]!["horse.standard.adult.v1"]!.AsObject();
            var detail = family["bodySignedDetail"]!.DeepClone().AsObject();
            detail["sourceGlbSha256"] = sourceHash;
            mutate(detail);
            family["bodySignedDetail"] = detail;
            return VisualCatalog.Load(root.ToJsonString());
        }

        const string wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";
        // Use an owned, deterministic fixture instead of relying on the optional fallback art.
        // This probe only reads raw bytes for the stale-hash path; no texture import is needed.
        var fixturePath = $"res://assets/horse/production/.capture-detail-hash-{Guid.NewGuid():N}.png";
        var fixtureAbsolutePath = ProjectSettings.GlobalizePath(fixturePath);
        var fixtureImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgb8);
        fixtureImage.Fill(new Color(0.5f, 0.5f, 0.5f));
        var fixtureBytes = fixtureImage.SavePngToBuffer();
        using (var fixtureFile = FileAccess.Open(fixturePath, FileAccess.ModeFlags.Write))
        {
            if (fixtureFile == null)
                throw new IOException("Could not create temporary signed-detail PNG fixture");
            fixtureFile.StoreBuffer(fixtureBytes);
        }

        try
        {
            const string lightFamilyId = "horse.standard.adult.v1";
            const string stockFamilyId = "horse.compact.adult.v1";
            var unknown = LoadWithBodyDetail(detail => detail["encoding"] = "future-unknown-format");
            var badStrength = LoadWithBodyDetail(detail => detail["strength"] = -0.01);
            var staleAtlas = LoadWithBodyDetail(detail =>
            {
                detail["path"] = fixturePath;
                detail["textureSha256"] = wrongHash;
            });
            var missingBinding = LoadWithBodyDetail(detail => detail.Remove("bindingPath"));
            var staleBinding = LoadWithBodyDetail(detail => detail["bindingSha256"] = wrongHash);
            var staleCorpus = LoadWithBodyDetail(detail => detail["corpusSha256"] = wrongHash);
            var incompatibleScope = LoadWithBodyDetail(detail => detail["uvReuseStatus"] = "whole-body");
            var staleSource = LoadWithBodyDetail(detail => detail["sourceGlbSha256"] = wrongHash);
            var wrongFamilyRoot = JsonNode.Parse(FileAccess.GetFileAsString(VisualCatalog.Path))!.AsObject();
            var lightDetail = wrongFamilyRoot["meshFamilies"]!["horse.standard.adult.v1"]!["bodySignedDetail"]!.DeepClone();
            wrongFamilyRoot["meshFamilies"]!["horse.compact.adult.v1"]!["bodySignedDetail"] = lightDetail;
            var wrongFamily = VisualCatalog.Load(wrongFamilyRoot.ToJsonString());
            var lightDescriptor = FixtureLibrary.AppearanceStudio.First(descriptor => descriptor.MeshFamilyId == lightFamilyId);
            var stockDescriptor = FixtureLibrary.AppearanceStudio.First(descriptor => descriptor.MeshFamilyId == stockFamilyId);
            var probes = new (string Label, VisualCatalog Catalog, string FamilyId, VisualDescriptor Descriptor, string Expected)[]
            {
                ("unknown encoding", unknown, lightFamilyId, lightDescriptor, "unknown encoding"),
                ("invalid strength", badStrength, lightFamilyId, lightDescriptor, "strength must be finite"),
                ("stale atlas hash", staleAtlas, lightFamilyId, lightDescriptor, "detail atlas SHA-256 does not match"),
                ("missing binding", missingBinding, lightFamilyId, lightDescriptor, "evidence paths or hashes are missing"),
                ("stale binding", staleBinding, lightFamilyId, lightDescriptor, "handoff or binding SHA-256 does not match"),
                ("stale corpus", staleCorpus, lightFamilyId, lightDescriptor, "handoff or binding content is malformed or incompatible"),
                ("incompatible UV scope", incompatibleScope, lightFamilyId, lightDescriptor, "UV reuse status is not the approved"),
                ("stale source GLB", staleSource, lightFamilyId, lightDescriptor, "source GLB SHA-256 does not match"),
                ("wrong family", wrongFamily, stockFamilyId, stockDescriptor, "supported only for the Light adult Body"),
            };

            var singletonField = typeof(VisualCatalog).GetField("_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (singletonField == null)
                     {
                _failures++;
                GD.PushError("[capture] cannot run signed-detail fallback probes because the catalog singleton is unavailable");
                return;
            }
            var originalCatalog = VisualCatalog.Instance;
            foreach (var (label, catalog, familyId, probeDescriptor, expected) in probes)
            {
                var family = catalog.MeshFamilies[familyId];
                var reason = family.BodySignedDetailDisabledReason ?? "";
                bool metadataDisabled = family.BodySignedDetail == null && reason.Contains(expected, StringComparison.Ordinal);
                VisualSupport runtimeSupport;
                bool detailOff = false;
                float detailStrength = float.NaN;
                bool phenotypeRenderable = false;
                try
                {
                    singletonField.SetValue(null, catalog);
                    runtimeSupport = _stage.Presenter.Present(probeDescriptor, "");
                    phenotypeRenderable = runtimeSupport.Supported && _stage.Presenter.Model != null;
                    var body = _stage.Presenter.Model == null
                        ? null
                        : FindAll<MeshInstance3D>(_stage.Presenter.Model).SingleOrDefault(mesh => mesh.Name == "Body");
                    var material = body?.GetActiveMaterial(0) as ShaderMaterial;
                    if (material != null)
                    {
                        detailOff = !material.GetShaderParameter("use_signed_detail").AsBool();
                        detailStrength = material.GetShaderParameter("signed_detail_strength").AsSingle();
                    }
                }
                finally
                {
                    singletonField.SetValue(null, originalCatalog);
                }

                bool fallbackPass = metadataDisabled && phenotypeRenderable && detailOff && Mathf.Abs(detailStrength) <= 0.000001f;
                _signedDetailFallbackEvidence.Add(new Dictionary<string, object?>
                {
                    ["case"] = label,
                    ["family"] = familyId,
                    ["disabledReason"] = reason,
                    ["detailDisabled"] = metadataDisabled,
                    ["detailEnabledShaderParam"] = !detailOff,
                    ["detailStrength"] = detailStrength,
                    ["proceduralPhenotypeRenderable"] = phenotypeRenderable,
                    ["passed"] = fallbackPass,
                });
                if (fallbackPass) continue;
                _failures++;
                GD.PushError($"[capture] {label} fallback failed: disabled={metadataDisabled}; supported={runtimeSupport.Supported}; renderable={phenotypeRenderable}; detailOff={detailOff}; strength={detailStrength}; reason='{reason}'");
            }
        }
        finally
        {
            var cleanup = DirAccess.RemoveAbsolute(fixtureAbsolutePath);
            if (cleanup != Error.Ok)
            {
                _failures++;
                GD.PushError($"[capture] could not remove temporary signed-detail PNG fixture ({cleanup})");
            }
        }
    }

    private async Task SignedDetailRuntimeSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        const string familyId = "horse.standard.adult.v1";
        var family = VisualCatalog.Instance.MeshFamilies[familyId];
        var detail = family.BodySignedDetail;
        if (detail == null)
        {
            _failures++;
            GD.PushError($"[capture] accepted Light detail was disabled: {family.BodySignedDetailDisabledReason ?? "no reason"}");
            return;
        }

        VerifySignedDetailInvalidMetadataFailsClosed(family.Scene);
        var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        var corpusPath = System.IO.Path.Combine(repoRoot, "art", "derived", "production", "evidence", "horse_light_uv0_provenance.json");
        if (!System.IO.File.Exists(corpusPath) ||
            Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(corpusPath))).ToLowerInvariant() != detail.CorpusSha256)
        {
            _failures++;
            GD.PushError("[capture] repository UV0 provenance corpus is missing or its SHA-256 differs from accepted binding");
        }

        var baseDescriptor = FixtureLibrary.GetAppearanceStudio("light_racer.chestnut");
        if (!Show(baseDescriptor, "")) return;
        var body = FindAll<MeshInstance3D>(_stage.Presenter.Model!).SingleOrDefault(mesh => mesh.Name == "Body");
        var material = body?.GetActiveMaterial(0) as ShaderMaterial;
        if (material == null || !material.GetShaderParameter("use_signed_detail").AsBool() ||
            Mathf.Abs(material.GetShaderParameter("signed_detail_strength").AsSingle() - 1f) > 0.000001f)
        {
            _failures++;
            GD.PushError("[capture] accepted Light Body detail did not bind with strength 1.0");
            return;
        }
        var boundMap = material.GetShaderParameter("signed_detail_map").AsGodotObject() as Texture2D;
        if (boundMap == null || !string.Equals(boundMap.ResourcePath, detail.Path, StringComparison.Ordinal))
        {
            _failures++;
            GD.PushError("[capture] Light Body shader is not bound to the accepted v5 atlas resource");
            return;
        }

        var importedImage = boundMap.GetImage();
        var pixels = importedImage.GetData();
        byte minCode = byte.MaxValue, maxCode = byte.MinValue;
        int neutralCodes = 0;
        bool equalRgb = importedImage.GetFormat() == Image.Format.Rgb8 && pixels.Length == 2048 * 2048 * 3;
        for (int i = 0; i + 2 < pixels.Length; i += 3)
        {
            if (pixels[i] != pixels[i + 1] || pixels[i] != pixels[i + 2]) equalRgb = false;
            minCode = Math.Min(minCode, pixels[i]);
            maxCode = Math.Max(maxCode, pixels[i]);
            if (pixels[i] == 128) neutralCodes++;
        }
        string importedPixelHash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        if (importedImage.GetWidth() != 2048 || importedImage.GetHeight() != 2048 || !equalRgb ||
            minCode != 90 || maxCode != 166 || importedPixelHash != detail.PixelSha256)
        {
            _failures++;
            GD.PushError($"[capture] imported v5 pixels differ from source proof: {importedImage.GetFormat()} {importedImage.GetWidth()}x{importedImage.GetHeight()}, range={minCode}-{maxCode}, rgbEqual={equalRgb}, pixelHash={importedPixelHash}");
        }

        var importSettingsPath = detail.Path + ".import";
        var importSettings = FileAccess.GetFileAsString(importSettingsPath);
        var shaderText = FileAccess.GetFileAsString("res://shaders/horse_coat.gdshader");
        var samplerDecl = shaderText.Split('\n').FirstOrDefault(line => line.Contains("uniform sampler2D signed_detail_map", StringComparison.Ordinal)) ?? "";
        bool importLossless = importSettings.Contains("compress/mode=0", StringComparison.Ordinal) &&
                              importSettings.Contains("detect_3d/compress_to=0", StringComparison.Ordinal);
        bool dataSampler = samplerDecl.Length > 0 && !samplerDecl.Contains("source_color", StringComparison.Ordinal);
        if (!importLossless || !dataSampler)
        {
            _failures++;
            GD.PushError($"[capture] detail data import/sampler contract failed (lossless={importLossless}, sampler='{samplerDecl}')");
        }

        var bodyDetailEvidence = new Dictionary<string, object?>
        {
            ["family"] = familyId,
            ["enabled"] = material.GetShaderParameter("use_signed_detail").AsBool(),
            ["bodyOnly"] = true,
            ["path"] = detail.Path,
            ["sourceGlbSha256"] = detail.SourceGlbSha256,
            ["atlasSha256"] = detail.TextureSha256,
            ["importedPixelSha256"] = importedPixelHash,
            ["handoffSha256"] = detail.HandoffSha256,
            ["bindingSha256"] = detail.BindingSha256,
            ["corpusSha256"] = detail.CorpusSha256,
            ["strength"] = material.GetShaderParameter("signed_detail_strength").AsSingle(),
            ["bakedStrength"] = detail.BakedStrength,
            ["decodedMultiplierBounds"] = new[] { 0.9640944881889764, 1.0359055118110236 },
            ["codeBounds"] = new[] { (int)minCode, (int)maxCode },
            ["neutralCode"] = 128,
            ["neutralCodePixelCount"] = neutralCodes,
            ["neutralRecoveredMaskPixelCount"] = detail.NeutralRecoveredPixelCount,
            ["rgbChannelsEqual"] = equalRgb,
            ["losslessImport"] = importLossless,
            ["nonColorSamplerNoSourceColor"] = dataSampler,
            ["uv"] = "UVMap / Godot UV; normal PNG orientation; no channel or image inversion",
            ["uvReuseScope"] = detail.UvReuseStatus,
            ["retainedTriangleCount"] = detail.RetainedTriangleCount,
            ["neutralRecoveredFaceCount"] = detail.NeutralRecoveredFaceCount,
            ["wholeBodyCompatibilityClaimed"] = false,
            ["semanticLayers"] = new[] { "points", "soft skin", "white markings", "hooves" },
        };
        _signedDetailEvidence.Add(bodyDetailEvidence);
        _stage.FrameView("q3");
        await Shot("detail_runtime/light_chestnut_q3_enabled.png", "detail_runtime", baseDescriptor, "q3",
            "accepted Light v5 detail enabled at strength 1; descriptor pigment and semantic layers remain authoritative");
        foreach (var (name, view, yaw) in new[] { ("front", "front", 90f), ("side", "side", 0f), ("side_left", "side_left", 180f) })
        {
            _stage.Camera.SetView(yaw, 7f, 1.7f, _stage.Presenter.HeadViewTarget ?? family.HeadViewTarget ?? Vector3.Zero);
            await Shot($"detail_runtime/light_chestnut_head_{name}.png", "detail_runtime", baseDescriptor, $"head_{name}",
                "accepted UV-matched Body detail; mane, face markings and skin remain separate descriptor/shader layers");
        }

        foreach (var (id, views) in new[]
                 {
                     ("bay", new[] { "q3", "legs" }),
                     ("cremello", new[] { "head", "legs" }),
                     ("bay_dun", new[] { "q3", "legs" }),
                 })
        {
            var descriptor = FixtureLibrary.Get(id, false);
            if (descriptor.MeshFamilyId != familyId || !Show(descriptor, ""))
            {
                _failures++;
                GD.PushError($"[capture] {id} semantic-layer comparison did not use the Light adult Body");
                continue;
            }
            var comparisonBody = FindAll<MeshInstance3D>(_stage.Presenter.Model!).SingleOrDefault(mesh => mesh.Name == "Body");
            var comparisonMaterial = comparisonBody?.GetActiveMaterial(0) as ShaderMaterial;
            if (comparisonMaterial == null || !comparisonMaterial.GetShaderParameter("use_signed_detail").AsBool() ||
                Mathf.Abs(comparisonMaterial.GetShaderParameter("signed_detail_strength").AsSingle() - 1f) > 0.000001f)
            {
                _failures++;
                GD.PushError($"[capture] {id} detail comparison did not use the accepted layer");
                continue;
            }
            _signedDetailEvidence.Add(new Dictionary<string, object?>
            {
                ["fixture"] = descriptor.FixtureId,
                ["coatRecipe"] = descriptor.CoatRecipe,
                ["skinRecipe"] = descriptor.SkinRecipe,
                ["coatLayers"] = descriptor.CoatLayers.ToArray(),
                ["detailEnabled"] = comparisonMaterial.GetShaderParameter("use_signed_detail").AsBool(),
                ["detailStrength"] = comparisonMaterial.GetShaderParameter("signed_detail_strength").AsSingle(),
                ["faceType"] = comparisonMaterial.GetShaderParameter("face_type").AsInt32(),
                ["pointsAmount"] = comparisonMaterial.GetShaderParameter("points_amount").AsSingle(),
                ["noseAmount"] = comparisonMaterial.GetShaderParameter("nose_amount").AsSingle(),
                ["primitiveStrength"] = comparisonMaterial.GetShaderParameter("primitive_strength").AsSingle(),
                ["legWhite"] = comparisonMaterial.GetShaderParameter("leg_white").AsVector4().ToString(),
            });
            foreach (var view in views)
            {
                _stage.FrameView(view);
                await Shot($"detail_runtime/light_{id}_{view}.png", "detail_runtime", descriptor, view,
                    $"accepted Light detail beside descriptor {descriptor.SkinRecipe} skin, coat points, white and hoof layers");
            }
        }

        const string stockFamilyId = "horse.compact.adult.v1";
        var stockFamily = VisualCatalog.Instance.MeshFamilies[stockFamilyId];
        if (stockFamily.BodySignedDetail != null)
        {
            _failures++;
            GD.PushError("[capture] Stock family must not inherit the Light-only detail metadata");
        }
        var stock = FixtureLibrary.AppearanceStudio.First(d => d.MeshFamilyId == stockFamilyId);
        if (Show(stock, ""))
        {
            var stockBody = FindAll<MeshInstance3D>(_stage.Presenter.Model!).SingleOrDefault(mesh => mesh.Name == "Body");
            var stockMaterial = stockBody?.GetActiveMaterial(0) as ShaderMaterial;
            bool stockDetailDisabled = stockMaterial != null && !stockMaterial.GetShaderParameter("use_signed_detail").AsBool() &&
                                       Mathf.Abs(stockMaterial.GetShaderParameter("signed_detail_strength").AsSingle()) < 0.000001f;
            if (!stockDetailDisabled)
            {
                _failures++;
                GD.PushError("[capture] Stock Body must remain detail-off at strength zero");
            }
            _signedDetailEvidence.Add(new Dictionary<string, object?>
            {
                ["family"] = stockFamilyId,
                ["fixture"] = stock.FixtureId,
                ["detailMetadataPresent"] = stockFamily.BodySignedDetail != null,
                ["detailEnabled"] = stockMaterial?.GetShaderParameter("use_signed_detail").AsBool() ?? false,
                ["detailStrength"] = stockMaterial?.GetShaderParameter("signed_detail_strength").AsSingle() ?? 0f,
            });
            _stage.FrameView("q3");
            await Shot("detail_runtime/stock_detail_off_guard.png", "detail_runtime", stock, "q3",
                "Stock adult guard: no Light atlas metadata, shader detail remains false/zero");
        }
    }

    private async Task StockRuntimeSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        var descriptor = FixtureLibrary.AppearanceStudio.FirstOrDefault(d => d.MeshFamilyId == "horse.compact.adult.v1");
        if (descriptor == null)
        {
            _failures++;
            GD.PushError("[capture] Stock smoke could not present its adult descriptor");
            return;
        }
        var support = _stage.Presenter.Present(descriptor, "idle");
        if (!support.Supported)
        {
            _failures++;
            GD.PushError($"[capture] Stock smoke unsupported: {support.Summary}");
            return;
        }
        var player = _stage.Presenter.Animator;
        double before = player?.CurrentAnimationPosition ?? 0;
        await Settle(8);
        double after = player?.CurrentAnimationPosition ?? 0;
        double length = player?.CurrentAnimationLength ?? 0;
        RecordAnimationTracks(descriptor, "idle", before, after, length);
        bool advanced = length > 0 && (after > before + 0.0001 || (after < before && length - before + after > 0.0001));
        if (player?.IsPlaying() != true || !advanced)
        {
            _failures++;
            GD.PushError($"[capture] Stock Idle did not advance: {before}->{after}, length={length}");
        }
        _stage.Presenter.Pose("idle", 0.0);
        VerifyMorphValues(descriptor);
        _meshLodEvidence.Clear();
        _meshLodEvidence.AddRange(MeasureMeshLods(descriptor));
        var skeletons = FindAll<Skeleton3D>(_stage.Presenter.Model!).ToArray();
        if (skeletons.Length != 1 || skeletons[0].GetBoneCount() != 58)
        {
            _failures++;
            GD.PushError($"[capture] Stock skeleton inventory expected one 58-bone skeleton, found {skeletons.Length} with {(skeletons.Length == 1 ? skeletons[0].GetBoneCount() : 0)} bones");
        }
        foreach (var view in new[] { "head", "front" })
        {
            _stage.FrameView(view);
            await Shot($"stock_runtime/stock_{view}.png", "stock_runtime", descriptor, view,
                "compact adult current manifest target; focused capture and Idle/morph/LOD inventory");
        }
    }

    private static string DiagnosticMarkingKey(int faceCode)
    {
        var bytes = new byte[32];
        bytes[0] = (byte)faceCode;
        bytes[1] = 95; // fore sock
        bytes[3] = 85; // hind sock
        bytes[9] = 64; // star radius ~= .075
        bytes[10] = 128;
        bytes[11] = 128;
        bytes[12] = 128;
        bytes[13] = 127; // face offset approximately zero
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task Turntable()
    {
        foreach (var (id, mode) in new[] { ("bay", LightingMode.Stable), ("palomino", LightingMode.Neutral) })
        {
            _stage.SetMode(mode);
            var d = FixtureLibrary.Get(id, false);
            if (!Show(d)) continue;
            _stage.FrameView("q3");
            for (int a = 0; a < 8; a++)
            {
                _stage.Camera.SetView(a * 45f, 8f, 5.2f, new Vector3(0.05f, 1.02f, 0));
                await Shot($"turntable/{id}_{mode.ToString().ToLower()}_{a * 45:000}.png", "turntable", d, $"yaw{a * 45}");
            }
        }
    }

    private async Task Pair()
    {
        _stage.SetMode(LightingMode.Stable);
        foreach (var id in new[] { "bay", "palomino", "grullo", "cremello" })
        {
            var mare = FixtureLibrary.Get(id, false); var foal = FixtureLibrary.Get(id, true);
            if (!Show(mare)) continue;
            var second = new HorsePresenter { Name = "Foal", Position = new Vector3(1.0f, 0, 1.25f), RotationDegrees = new Vector3(0, -25, 0) };
            _stage.AddChild(second);
            second.Present(foal, "idle"); second.Pose("idle", 0.7);
            _stage.Camera.SetView(40, 9, 6.0f, new Vector3(0.5f, 0.85f, 0.6f));
            await Shot($"pair/{id}__mare_and_foal.png", "pair", mare, "pair", "adult + foal of the same class (same marking key)");
            second.QueueFree();
            await Settle(2);
        }
    }

    private async Task StableShots()
    {
        _stage.SetMode(LightingMode.Stable);
        var d = FixtureLibrary.Get("chestnut", false);
        if (!Show(d)) return;
        foreach (var (name, yaw, pitch, dist, target) in new[]
                 {
                     ("establishing", 38f, 12f, 13.5f, new Vector3(-2.2f, 1.4f, -2.5f)),
                     ("barn", 20f, 8f, 11f, new Vector3(-4.5f, 2.0f, -5.5f)),
                     ("paddock_low", 70f, 3f, 7.5f, new Vector3(0, 1.0f, 0)),
                     ("overview", 45f, 32f, 20f, new Vector3(-2.5f, 0.5f, -3f)),
                 })
        {
            _stage.Camera.SetView(yaw, pitch, dist, target);
            await Shot($"stable/{name}.png", "stable", d, name);
        }
    }

    private async Task AnimFrames()
    {
        _stage.SetMode(LightingMode.Neutral);
        var d = FixtureLibrary.Get("bay", false);
        foreach (var (anim, len) in new[] { ("walk", 1.2), ("trot", 0.733), ("look", 5.0), ("idle", 4.0) })
        {
            if (!Show(d)) continue;
            if (_stage.Presenter.Animator == null || !_stage.Presenter.Animator.HasAnimation(anim))
            {
                _unavailableAnimations.Add(anim);
                GD.Print($"[capture] skipped unavailable animation {anim}");
                continue;
            }
            for (int i = 0; i < 6; i++)
            {
                if (!Show(d, anim, len * i / 6.0)) continue;
                _stage.FrameView("side");
                await Shot($"anim/{anim}_{i}.png", "anim", d, "side", $"{anim} t={len * i / 6.0:0.00}s (pose sample, not playback)");
            }
        }
    }

    private async Task Probes()
    {
        _stage.SetMode(LightingMode.Neutral);
        foreach (var p in FixtureLibrary.UnsupportedProbes())
        {
            var s = _stage.Presenter.Present(p);
            if (s.Supported) { _failures++; GD.PushError($"[capture] probe {p.FixtureId} should be unsupported"); }
            _stage.FrameView("q3");
            await Shot($"probes/{p.FixtureId}.png", "probes", p, "q3", "expected UNSUPPORTED - empty set proves no substitute coat");
        }
    }

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }
}
