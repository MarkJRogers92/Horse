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
/// D-020 visual review of cosmetic tack: every mesh family wearing a pad and a bow, at neutral shape and at the
/// combined body-shape extremes, idle and mid-walk, side and three-quarter views.
/// Usage: scenes/tools/TackPreview.tscn -- --out DIR
/// </summary>
public partial class TackPreview : Node3D
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var args = OS.GetCmdlineUserArgs();
        var outIndex = Array.IndexOf(args, "--out");
        var output = ProjectSettings.GlobalizePath(outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : "user://tack-preview");
        Directory.CreateDirectory(output);
        try
        {
            var stage = new HorseStage { Name = "Stage" };
            AddChild(stage);
            stage.SetMode(LightingMode.Neutral);
            var catalog = VisualCatalog.Instance;
            foreach (var (familyId, family) in catalog.MeshFamilies.OrderBy(f => f.Key))
            {
                bool foal = family.Age == "foal";
                var channels = catalog.FamilyMeshBlendShapes[familyId].TryGetValue("Body", out var body) ? body.Keys.ToArray() : Array.Empty<string>();
                var baseDescriptor = FixtureLibrary.Get("bay", foal) with { MeshFamilyId = familyId, TestOnly = true, CosmeticLayer = "bow=bow.rose;pad=pad.lavender" };
                int max = (int)catalog.BodyBlendShapeNormalization;
                foreach (var (label, value) in new[] { ("neutral", 0), ("all+", max), ("all-", -max) })
                {
                    var weights = baseDescriptor.MorphWeights.ToDictionary(p => p.Key, p => 0, StringComparer.Ordinal);
                    foreach (var channel in channels) weights[channel] = value;
                    var descriptor = baseDescriptor with { FixtureId = $"tack.{familyId}.{label}", MorphWeights = weights };
                    foreach (var animation in new[] { "idle", "walk" })
                    {
                        var support = stage.Presenter.Present(descriptor, animation);
                        if (!support.Supported) { GD.Print($"[tack-preview] {familyId} {label} {animation}: refused {support.Summary}"); continue; }
                        if (animation == "walk") stage.Presenter.Pose("walk", stage.Presenter.LocomotionPeriodSeconds * 0.3);
                        foreach (var view in new[] { "side", "q3" })
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
