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

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
        _out = Arg("--out", _out);
        var sets = Arg("--sets", "sheet,detail,markings,turntable,pair,stable,anim,probes").Split(',');
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
                    case "sheet": await Sheet(); break;
                    case "detail": await Detail(); break;
                    case "markings": await MarkingSet(); break;
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
        });
    }

    private bool Show(VisualDescriptor d, string anim = "idle", double t = 0.0)
    {
        var s = _stage.Presenter.Present(d, anim);
        if (!s.Supported) { GD.PushError($"[capture] expected supported: {d.FixtureId}: {s.Summary}"); _failures++; return false; }
        _stage.Presenter.Pose(anim, t); // freeze so captures are deterministic
        return true;
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

    private async Task MarkingSet()
    {
        _stage.SetMode(LightingMode.Neutral);
        int i = 0;
        foreach (var d in FixtureLibrary.MarkingVariety("chestnut", false, 8))
        {
            if (!Show(d)) continue;
            _stage.FrameView("q3"); await Shot($"markings/test_{i}__q3.png", "markings", d, "q3", "TEST-ONLY marking key");
            _stage.FrameView("head"); await Shot($"markings/test_{i}__head.png", "markings", d, "head", "TEST-ONLY marking key");
            i++;
        }
        // same key, adult vs foal: markings must match
        var key = FixtureLibrary.MarkingVariety("chestnut", false, 3).Last();
        var foalSame = FixtureLibrary.Get("chestnut", true).WithTestMarkingKey(key.MarkingRealizationKey, key.DisplayLabel + " (foal)");
        if (Show(foalSame)) { _stage.FrameView("q3"); await Shot("markings/test_2__foal_q3.png", "markings", foalSame, "q3", "same key as test_2 adult"); }
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
            for (int i = 0; i < 6; i++)
            {
                if (!Show(d, anim, len * i / 6.0)) continue;
                _stage.FrameView("side");
                await Shot($"anim/{anim}_{i}.png", "anim", d, "side", $"{anim} t={len * i / 6.0:0.00}s (pose sample, not playback)");
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
}
