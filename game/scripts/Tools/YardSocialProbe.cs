using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Cloverbrook.Domain;
using Cloverbrook.Game.Stable;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>Fixed-clock coverage for the presentation-only social layer, including swept clearance and trait reactions.</summary>
public partial class YardSocialProbe : Node
{
    private int _checks;
    public override void _Ready() => CallDeferred(nameof(Run));

    private async void Run()
    {
        try
        {
            Check(Math.Abs(StableYard.SegmentGap(Vector2.Zero, Vector2.Right, Vector2.Up, Vector2.Up + Vector2.Right) - 1f) < 0.001f, "parallel body segments");
            Check(StableYard.SegmentGap(Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down) < 0.001f, "crossing body segments");
            Check(Math.Abs(StableYard.SegmentGap(Vector2.Zero, Vector2.Zero, Vector2.Right, Vector2.Right) - 1f) < 0.001f, "zero-length body segments");
            var yard = new StableYard { Wandering = true };
            AddChild(yard);
            yard.ProcessMode = ProcessModeEnum.Disabled;
            var traits = Personality.Ids.ToArray();
            var descriptor = FixtureLibrary.Get("bay", false) with { MeshFamilyId = "horse.standard.adult.v1" };
            yard.SetHorses(traits.Select(t => (t, (VisualDescriptor?)descriptor)).ToArray());
            yard.SetPersonalities(traits.ToDictionary(t => t, t => t));
            var horses = yard.GetChildren().OfType<HorsePresenter>().ToArray();
            const double step = 1.0 / 60;
            int attentionFrames = 0, walkFrames = 0;
            double worstClearanceLoss = 0;
            var seenProfiles = new HashSet<string>();
            bool placedBlocker = false;
            for (int frame = 0; frame < 60 * 180; frame++)
            {
                if (frame % 900 == 60)
                {
                    string key = traits[(frame / 900) % traits.Length];
                    yard.Highlight(key);
                    seenProfiles.Add(yard.SocialReactionFor(key));
                }
                if (frame % 900 == 360) yard.Highlight(null);
                // A successful avoidance run need not yield naturally. Exercise the
                // blocked-step path explicitly by placing a stationary horse just
                // beyond a walker's reserved capsule before measuring this frame.
                if (!placedBlocker && frame > 60 * 90)
                {
                    int mover = Array.FindIndex(horses, p => p.UsesWalkVariant && !p.IsTransitioning);
                    if (mover >= 0)
                    {
                        int blocker = (mover + 1) % horses.Length;
                        horses[blocker].Position = horses[mover].Position + horses[mover].Basis.X.Normalized() * 2.36005f;
                        horses[blocker].Rotation = horses[mover].Rotation;
                        yard.Focus(traits[blocker]);
                        placedBlocker = true;
                    }
                }
                var gaps = new float[horses.Length, horses.Length];
                var beforeScales = horses.Select(p => p.Model?.Scale.Y ?? 1).ToArray();
                for (int a = 0; a < horses.Length; a++)
                    for (int b = a + 1; b < horses.Length; b++) gaps[a, b] = yard.HorseClearance(horses[a], horses[b]);
                yard._Process(step);
                foreach (var p in horses)
                {
                    p._Process(step);
                    if (p.Animator != null && !p.IsTransitioning) p.Animator.Advance(step * p.Animator.SpeedScale);
                    if (p.UsesWalkVariant) { walkFrames++; Check(p.IdleLife == null, "no head cues on Walk"); }
                    if (p.IdleLife is { } life)
                    {
                        if (life.AttentionWeight > 0.02f) attentionFrames++;
                        if (p.IsTransitioning) Check(life.Offset < 0.0001f, "head cue clears for neutral bridge");
                    }
                }
                for (int a = 0; a < horses.Length; a++)
                    for (int b = a + 1; b < horses.Length; b++)
                    {
                        float after = yard.HorseClearance(horses[a], horses[b]);
                        // Existing slot overlap may be unwound, never made worse by a new step or turn.
                        double loss = Math.Max(0, Math.Min(gaps[a, b], 0f) - Math.Min(after, 0f));
                        worstClearanceLoss = Math.Max(worstClearanceLoss, loss);
                        Check(loss < 0.0002, $"horses {a}/{b} keep body clearance at frame {frame}: {gaps[a, b]:F4} -> {after:F4}; scales {beforeScales[a]:F4}/{beforeScales[b]:F4} -> {horses[a].Model?.Scale.Y:F4}/{horses[b].Model?.Scale.Y:F4}; bridges {horses[a].IsTransitioning}/{horses[b].IsTransitioning}");
                    }
            }
            Check(seenProfiles.Count == 6, "all six personalities have distinct reactions");
            Check(attentionFrames > 300, "head reactions really ran");
            Check(yard.GreetingCount > 0, "resting horses greeted one another");
            Check(yard.CompanionVisitCount > 0, "horses chose familiar grazing company");
            Check(placedBlocker && yard.YieldCount > 0, "a walker yielded to the deliberately blocked route");
            Check(walkFrames > 600, "avoidance still allows travel");
            var output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "/tmp/yard-social-probe";
            System.IO.Directory.CreateDirectory(output);
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                yard.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CapturePersonalities(output, traits, descriptor);
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "yard-social-report.json"), JsonSerializer.Serialize(new
            {
                checks = _checks, seconds = 180, attentionFrames, walkFrames, yard.GreetingCount, yard.CompanionVisitCount, yard.YieldCount,
                worstClearanceLossMeters = worstClearanceLoss, profiles = seenProfiles.OrderBy(p => p),
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[yard-social] PASS {_checks} checks; {yard.GreetingCount} greetings, {yard.CompanionVisitCount} companion visits, {yard.YieldCount} yields");
            GetTree().Quit(0);
        }
        catch (Exception e) { GD.PushError("[yard-social] FAILED " + e); GetTree().Quit(1); }
    }

    // Exercise the actual hover/selection route, then let Godot's skeleton modifiers
    // render the resulting held pose. The fixed animation/atmosphere clocks make
    // these useful side-by-side review images instead of machine-speed snapshots.
    private async Task CapturePersonalities(string output, string[] traits, VisualDescriptor descriptor)
    {
        var yard = new StableYard { Wandering = true };
        AddChild(yard); yard.SetProcess(false);
        yard.SetSeason(1); yard.SetWeather(YardWeather.Sunny);
        yard.SetAtmospherePreviewTime(3.25f);
        foreach (var trait in traits)
        {
            yard.Focus(null); yard.Highlight(null);
            yard.SetHorses(new[] { (trait, (VisualDescriptor?)descriptor) }, new Dictionary<string, string> { [trait] = trait });
            yard.SetPersonalities(new Dictionary<string, string> { [trait] = trait });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var horse = yard.GetChildren().OfType<HorsePresenter>().Single();
            horse.SetProcess(false);
            Check(horse.IdleLife != null, trait + " has the real Idle skeleton modifier");
            horse.IdleLife!.MeasureEveryFrame = true;
            horse.Position = Vector3.Zero; horse.RotationDegrees = new Vector3(0, -25, 0);
            if (horse.Animator is { } animation)
            {
                animation.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
                animation.Seek(0, true);
            }
            yard.SetCameraHome(new Vector3(2.1f, 1.8f, 4.5f), new Vector3(0.35f, 1.05f, 0));
            await Capture(System.IO.Path.Combine(output, trait + "_rest.png"));
            yard.Focus(trait);
            for (int i = 0; i < 120; i++) { yard._Process(1.0 / 60); horse._Process(1.0 / 60); }
            Check(horse.IdleLife is { AttentionWeight: > 0.85f }, trait + " has a visible held head response");
            yard.SetCameraHome(new Vector3(2.1f, 1.8f, 4.5f), new Vector3(0.35f, 1.05f, 0));
            await Capture(System.IO.Path.Combine(output, trait + "_response.png"));
            GD.Print($"[yard-social-render] {trait}: {horse.IdleLife.MeasureMuzzle()}");
        }
        yard.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Capture(string path)
    {
        // Automatic sky radiance updates one roughness layer per frame after a
        // season/weather change. Let every layer settle before comparing pixels.
        int settleFrames = (int)ProjectSettings.GetSetting("rendering/reflections/sky_reflections/roughness_layers", 8) + 2;
        for (int i = 0; i < settleFrames; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(!image.IsEmpty() && image.SavePng(path) == Error.Ok, "rendered social screenshot saved");
    }

    private void Check(bool ok, string description) { _checks++; if (!ok) throw new InvalidOperationException(description); }
}
