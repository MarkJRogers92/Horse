using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Game.Scenes;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// D-013 deformed visual review: every family with a checked Walk, at neutral and at the
/// extreme body-shape weights (each channel at -1 and +1, plus two combined extremes),
/// posed at four points of the stride, side and three-quarter views. Writes PNG sheets.
/// Usage: scenes/tools/WalkShapeAudit.tscn -- --out DIR
/// </summary>
public partial class WalkShapeAudit : Node3D
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var args = OS.GetCmdlineUserArgs();
        var outIndex = Array.IndexOf(args, "--out");
        var output = outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : "user://walk-shape-audit";
        output = ProjectSettings.GlobalizePath(output);
        Directory.CreateDirectory(output);
        try
        {
            var stage = new HorseStage { Name = "Stage" };
            AddChild(stage);
            stage.SetMode(LightingMode.Neutral);
            var catalog = VisualCatalog.Instance;
            var report = new List<string>();
            foreach (var (familyId, family) in catalog.MeshFamilies.Where(f => f.Value.WalkVariant != null).OrderBy(f => f.Key))
            {
                bool foal = family.Age == "foal";
                var channels = catalog.FamilyMeshBlendShapes[familyId].TryGetValue("Body", out var body) ? body.Keys.ToArray() : Array.Empty<string>();
                var baseDescriptor = FixtureLibrary.Get("bay", foal) with { MeshFamilyId = familyId, TestOnly = true };
                var variants = new List<(string Label, Dictionary<string, int> Weights)> { ("neutral", Weights(baseDescriptor, channels, _ => 0)) };
                int max = (int)catalog.BodyBlendShapeNormalization;
                foreach (var channel in channels)
                {
                    variants.Add(($"{channel}-", Weights(baseDescriptor, channels, c => c == channel ? -max : 0)));
                    variants.Add(($"{channel}+", Weights(baseDescriptor, channels, c => c == channel ? max : 0)));
                }
                variants.Add(("all+", Weights(baseDescriptor, channels, _ => max)));
                variants.Add(("all-", Weights(baseDescriptor, channels, _ => -max)));
                foreach (var (label, weights) in variants)
                {
                    var descriptor = baseDescriptor with { FixtureId = $"walk-audit.{familyId}.{label}", MorphWeights = weights };
                    var support = stage.Presenter.Present(descriptor, "walk");
                    if (!support.Supported || !stage.Presenter.UsesWalkVariant)
                    {
                        report.Add($"{familyId} {label}: REFUSED {support.Summary}");
                        continue;
                    }
                    double period = stage.Presenter.LocomotionPeriodSeconds;
                    foreach (var view in new[] { "side", "q3" })
                    for (var i = 0; i < 4; i++)
                    {
                        stage.Presenter.Pose("walk", period * i / 4.0);
                        stage.FrameView(view);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var image = GetViewport().GetTexture().GetImage();
                        // Keep the horse itself at full resolution: crop the central region.
                        image = image.GetRegion(new Rect2I(260, 120, 760, 480));
                        image.SavePng(Path.Combine(output, $"{familyId}__{label}__{view}_{i}.png"));
                    }
                    report.Add($"{familyId} {label}: rendered");
                }
            }
            File.WriteAllLines(Path.Combine(output, "report.txt"), report);
            GD.Print("[walk-shape-audit] OK " + output);
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[walk-shape-audit] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    private static Dictionary<string, int> Weights(VisualDescriptor descriptor, string[] channels, Func<string, int> value)
    {
        var weights = descriptor.MorphWeights.ToDictionary(p => p.Key, p => 0, StringComparer.Ordinal);
        foreach (var channel in channels) weights[channel] = value(channel);
        return weights;
    }
}
