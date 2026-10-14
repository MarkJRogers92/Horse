using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Game.Scenes;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// D-020/D-033 visual review of cosmetic tack: every mesh family wearing all six slots (pad, blanket, bow, halter, braids, tail bow), at neutral shape and at the
/// combined body-shape extremes, idle and mid-walk, side and three-quarter views.
/// Usage: scenes/tools/TackPreview.tscn -- --out DIR [--family text] [--shapes neutral,all+,all-] [--anims idle,walk] [--views side,q3,head,rear] [--layer "slot=item;…"] [--coat bay]
/// </summary>
public partial class TackPreview : Node3D
{
    public override void _Ready() => _ = RunAsync();

    private const string AllTack = "blanket=blanket.navy;bow=bow.rose;braids=braids.pink;halter=halter.sky;pad=pad.lavender;tail=tail.rose";

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private async Task RunAsync()
    {
        var args = OS.GetCmdlineUserArgs();
        var output = ProjectSettings.GlobalizePath(Arg(args, "--out") ?? "user://tack-preview");
        // Optional filters keep a review quick: --family text, --shapes neutral,all+,all-, --anims idle,walk,
        // --views side,q3,head,rear,front, --layer "slot=item;..." (default: every slot).
        var familyFilter = Arg(args, "--family");
        var shapes = (Arg(args, "--shapes") ?? "neutral,all+,all-").Split(',');
        var anims = (Arg(args, "--anims") ?? "idle,walk").Split(',');
        var views = (Arg(args, "--views") ?? "side,q3,head,rear").Split(',');
        var layer = Arg(args, "--layer") ?? AllTack;
        var bodyColour = Arg(args, "--coat") ?? "bay";
        Directory.CreateDirectory(output);
        try
        {
            var stage = new HorseStage { Name = "Stage" };
            AddChild(stage);
            stage.SetMode(LightingMode.Neutral);
            var catalog = VisualCatalog.Instance;
            foreach (var (familyId, family) in catalog.MeshFamilies.OrderBy(f => f.Key))
            {
                if (familyFilter != null && !familyId.Contains(familyFilter, StringComparison.OrdinalIgnoreCase)) continue;
                bool foal = family.Age == "foal";
                var channels = catalog.FamilyMeshBlendShapes[familyId].TryGetValue("Body", out var body) ? body.Keys.ToArray() : Array.Empty<string>();
                var baseDescriptor = FixtureLibrary.Get(bodyColour, foal) with { MeshFamilyId = familyId, TestOnly = true, CosmeticLayer = layer };
                int max = (int)catalog.BodyBlendShapeNormalization;
                foreach (var (label, value) in new[] { ("neutral", 0), ("all+", max), ("all-", -max) })
                {
                    if (!shapes.Contains(label)) continue;
                    var weights = baseDescriptor.MorphWeights.ToDictionary(p => p.Key, p => 0, StringComparer.Ordinal);
                    foreach (var channel in channels) weights[channel] = value;
                    var descriptor = baseDescriptor with { FixtureId = $"tack.{familyId}.{label}", MorphWeights = weights };
                    foreach (var animation in anims)
                    {
                        var support = stage.Presenter.Present(descriptor, animation);
                        if (!support.Supported) { GD.Print($"[tack-preview] {familyId} {label} {animation}: refused {support.Summary}"); continue; }
                        if (animation == "walk") stage.Presenter.Pose("walk", stage.Presenter.LocomotionPeriodSeconds * 0.3);
                        foreach (var view in views)
                        {
                            stage.FrameView(view);
                            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                            var image = GetViewport().GetTexture().GetImage().GetRegion(new Rect2I(260, 120, 760, 480));
                            image.SavePng(Path.Combine(output, $"{familyId}__{label}__{animation}_{view}.png"));
                        }
                    }
                }
            }
            GD.Print("[tack-preview] OK " + output);
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[tack-preview] FAILED " + ex);
            GetTree().Quit(1);
        }
    }
}
