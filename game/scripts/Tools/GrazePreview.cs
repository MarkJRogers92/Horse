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
/// D-036 review of the graze pose: every mesh family with the graze held at full weight, side and three-quarter views, plus the measured
/// height of the muzzle tip above the ground. Usage: scenes/tools/GrazePreview.tscn -- --out DIR [--family text] [--views side,q3] [--weight 1]
/// </summary>
public partial class GrazePreview : Node3D
{
    public override void _Ready() => _ = RunAsync();

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static Skeleton3D? FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren()) if (FindSkeleton(c) is { } f) return f;
        return null;
    }

    private async Task RunAsync()
    {
        var args = OS.GetCmdlineUserArgs();
        var output = ProjectSettings.GlobalizePath(Arg(args, "--out") ?? "user://graze-preview");
        var familyFilter = Arg(args, "--family");
        var views = (Arg(args, "--views") ?? "side,q3").Split(',');
        float weight = float.Parse(Arg(args, "--weight") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
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
                var descriptor = FixtureLibrary.Get("bay", foal) with { MeshFamilyId = familyId, TestOnly = true, FixtureId = $"graze.{familyId}" };
                var support = stage.Presenter.Present(descriptor, "idle");
                if (!support.Supported) { GD.Print($"[graze-preview] {familyId}: refused {support.Summary}"); continue; }
                stage.Presenter.EnableIdleLife(7);
                var life = stage.Presenter.IdleLife;
                if (life == null) { GD.Print($"[graze-preview] {familyId}: no idle life"); continue; }
                if (Arg(args, "--bones") != null)
                {
                    var sk = FindSkeleton(stage.Presenter);
                    var toOwner = stage.Presenter.GlobalTransform.AffineInverse();
                    for (int b = 0; b < sk!.GetBoneCount(); b++)
                    {
                        var o = toOwner * (sk.GlobalTransform * sk.GetBoneGlobalRest(b).Origin);
                        int par = sk.GetBoneParent(b);
                        GD.Print($"[bones] {familyId} {sk.GetBoneName(b)} (parent {(par >= 0 ? sk.GetBoneName(par).ToString() : "-")}) x={o.X:0.00} y={o.Y:0.00} z={o.Z:0.00}");
                    }
                }
                life.MeasureEveryFrame = true;
                life.HoldGraze(weight);
                foreach (var view in views)
                {
                    stage.FrameView(view);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var image = GetViewport().GetTexture().GetImage().GetRegion(new Rect2I(260, 120, 760, 480));
                    image.SavePng(Path.Combine(output, $"{familyId}__graze_{view}.png"));
                }
                GD.Print($"[graze-preview] {familyId}: {life.MeasureMuzzle()}");
            }
            GD.Print("[graze-preview] OK " + output);
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[graze-preview] FAILED " + ex);
            GetTree().Quit(1);
        }
    }
}
