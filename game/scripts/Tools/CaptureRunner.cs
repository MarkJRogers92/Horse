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
    private Dictionary<string, object?>? _appearancePerformance;
    private List<Dictionary<string, object?>> _meshLodEvidence = new();

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
        _out = Arg("--out", _out);
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
            appearancePerformance = _appearancePerformance,
            meshLodEvidence = _meshLodEvidence,
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
        foreach (var (component, channels) in catalog.MeshBlendShapes)
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
