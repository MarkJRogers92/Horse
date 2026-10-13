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
/// Coat review: every phase-1 fixture coat on the standard adult, side and head views, under neutral and yard
/// lighting. Writes one PNG per coat/view. Usage: scenes/tools/CoatGallery.tscn -- --out DIR [--lighting stable]
/// </summary>
public partial class CoatGallery : Node3D
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        var output = ProjectSettings.GlobalizePath(Arg("--out", "user://coat-gallery"));
        Directory.CreateDirectory(output);
        try
        {
            var stage = new HorseStage { Name = "Stage" };
            AddChild(stage);
            stage.SetMode(Arg("--lighting", "neutral") == "stable" ? LightingMode.Stable : LightingMode.Neutral);
            var variants = int.Parse(Arg("--variants", "1"));
            var views = Arg("--views", "side,q3,head").Split(',');
            foreach (var classId in FixtureLibrary.ClassIds)
            for (var v = 0; v < variants; v++)
            {
                var fixture = FixtureLibrary.Get(classId, false);
                // Variants re-roll the individual shade (D-024) by changing the realisation key.
                var descriptor = v == 0 ? fixture : fixture with { MarkingRealizationKey = fixture.MarkingRealizationKey + "-variant-" + v };
                var support = stage.Presenter.Present(descriptor, "idle");
                if (!support.Supported) { GD.Print($"[coat-gallery] {classId}: refused {support.Summary}"); continue; }
                if (stage.Presenter.Animator is { } animator && animator.IsPlaying()) { animator.Seek(0.4, true); animator.Pause(); }
                foreach (var view in views)
                {
                    stage.FrameView(view);
                    for (var i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var image = GetViewport().GetTexture().GetImage().GetRegion(new Rect2I(200, 60, 880, 600));
                    image.SavePng(Path.Combine(output, $"{classId}__v{v}__{view}.png"));
                }
            }
            GD.Print("[coat-gallery] OK " + output);
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError("[coat-gallery] FAILED " + ex); GetTree().Quit(1); }
    }
}
