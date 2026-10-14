using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Game.Stable;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// D-032 visual review: the yard dressed for each season's festival with horses wearing the four festival rewards, plus a
/// close-up of each horse. Usage: scenes/tools/FestivalPreview.tscn -- --out=DIR [--closeups]
/// </summary>
public partial class FestivalPreview : Node
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var args = OS.GetCmdlineUserArgs();
        var output = args.FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "/tmp/festival-preview";
        Directory.CreateDirectory(output);
        try
        {
            if (args.Contains("--tack")) await TackShots(output);
            if (!args.Contains("--no-yard"))
            {
                var yard = new StableYard { Wandering = false };
                AddChild(yard);
                var horses = new List<(string, VisualDescriptor?)>
                {
                    ("h0", FixtureLibrary.Get("bay", false) with { MeshFamilyId = "horse.standard.adult.v1", CosmeticLayer = "bow=bow.blossom;tail=tail.sunflower" }),
                    ("h1", FixtureLibrary.Get("palomino", false) with { MeshFamilyId = "horse.standard.adult.v1", CosmeticLayer = "braids=braids.harvest;halter=halter.fairy" }),
                };
                yard.SetHorses(horses);
                for (var s = 0; s < 4; s++)
                {
                    var id = Cloverbrook.Domain.Festivals.SeasonIds[s];
                    yard.SetSeason(s); yard.SetFestival(id);
                    await Wait(1.5);
                    await Capture(Path.Combine(output, $"yard_{id}.png"));
                }
            }
            GD.Print("[festival-preview] OK " + output);
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[festival-preview] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    /// <summary>Each festival reward on its own horse, side and three-quarter views (the same stage the tack review uses).</summary>
    private async Task TackShots(string output)
    {
        var stage = new Cloverbrook.Game.Scenes.HorseStage { Name = "Stage" };
        AddChild(stage);
        stage.SetMode(Cloverbrook.Game.World.LightingMode.Neutral);
        var items = new (string Layer, string Name, string Colour, string Family)[]
        {
            ("bow=bow.blossom", "crown", "bay", "horse.standard.adult.v1"), ("tail=tail.sunflower", "tailbow", "chestnut", "horse.standard.adult.v1"),
            ("braids=braids.harvest", "braids", "palomino", "horse.standard.adult.v1"), ("halter=halter.fairy", "halter", "bay", "horse.standard.adult.v1"),
            ("bow=bow.blossom;halter=halter.fairy", "crown_halter_compact", "chestnut", "horse.compact.adult.v1"),
        };
        foreach (var (layer, name, colour, family) in items)
        {
            var descriptor = FixtureLibrary.Get(colour, false) with { MeshFamilyId = family, TestOnly = true, CosmeticLayer = layer, FixtureId = "festival." + name };
            var support = stage.Presenter.Present(descriptor, "idle");
            if (!support.Supported) { GD.Print($"[festival-preview] {name}: refused {support.Summary}"); continue; }
            foreach (var view in new[] { "side", "q3" })
            {
                stage.FrameView(view);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().GetRegion(new Rect2I(260, 120, 760, 480)).SavePng(Path.Combine(output, $"tack_{name}_{view}.png"));
            }
        }
        RemoveChild(stage); stage.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Wait(double seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task Capture(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
    }
}
