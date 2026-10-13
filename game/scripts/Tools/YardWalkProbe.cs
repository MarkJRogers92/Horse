using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cloverbrook.Game.Stable;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// Simulates a few minutes of horses wandering the stable yard on a fixed time step and checks
/// what a player would see: feet stay matched to the ground (clip pace == ground pace), no
/// teleporting, and every Idle/Walk model swap is hidden inside the rest-pose bridge.
/// </summary>
public partial class YardWalkProbe : Node
{
    private const double Step = 1.0 / 60;
    private const double SimulatedSeconds = 240;
    private string _output = "/tmp/yard-walk-probe";
    private int _checks;

    public override void _Ready()
    {
        _output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? _output;
        CallDeferred(nameof(Run));
    }

    private void Run()
    {
        try
        {
            var yard = new StableYard { Wandering = true };
            AddChild(yard);
            // Advance everything explicitly so the result never depends on machine speed.
            yard.ProcessMode = ProcessModeEnum.Disabled;
            var horses = new List<(string, VisualDescriptor?)>
            {
                ("light", Neutral(FixtureLibrary.Get("bay", false) with { MeshFamilyId = "horse.standard.adult.v1" })),
                ("stock", Neutral(FixtureLibrary.Get("chestnut", false) with { MeshFamilyId = "horse.compact.adult.v1" })),
                ("foal", Neutral(FixtureLibrary.Get("bay", true) with { MeshFamilyId = "horse.standard.foal.v1" })),
                // Not neutral: has no checked Walk, so it must turn on the spot and never move or reload.
                ("shaped", FixtureLibrary.Get("palomino", false) with { MeshFamilyId = "horse.standard.adult.v1", MorphWeights = new Dictionary<string, int> { ["body_length"] = 900 } }),
            };
            yard.SetHorses(horses);
            Check(yard.HorseCount == horses.Count, "all four horses are shown");
            var presenters = yard.GetChildren().OfType<HorsePresenter>().ToArray();
            Check(presenters.Length == horses.Count, "one presenter per horse");

            var state = presenters.ToDictionary(p => p, p => new Track { Model = p.Model, Position = p.Position });
            double elapsed = 0;
            int walkFrames = 0, bridgeSwaps = 0, walkStarts = 0;
            double maxJump = 0, maxPaceError = 0, maxSpeed = 0;
            var shapedStart = presenters.Single(p => p.Descriptor!.FixtureId.Contains("palomino", StringComparison.Ordinal)).Position;
            while (elapsed < SimulatedSeconds)
            {
                // Same order the engine uses: scene logic first, then animation advance.
                yard._Process(Step);
                foreach (var p in presenters)
                {
                    bool bridgedBefore = p.IsTransitioning;
                    p._Process(Step);
                    var t = state[p];
                    if (p.Model != t.Model)
                    {
                        Check(bridgedBefore || p.IsTransitioning, $"{p.Name}: a model swap happened outside the rest-pose bridge (a visible pop)");
                        bridgeSwaps++; t.Model = p.Model;
                    }
                    if (p.Animator != null && !p.IsTransitioning) p.Animator.Advance(Step * p.Animator.SpeedScale);
                    var moved = (p.Position - t.Position).Length();
                    maxJump = Math.Max(maxJump, moved);
                    if (p.IsTransitioning)
                        Check(moved < 1e-7, $"{p.Name}: horse moved while its Idle/Walk bridge was running");
                    bool walking = !p.IsTransitioning && p.UsesWalkVariant &&
                                   string.Equals(p.CurrentAnimation, "Walk", StringComparison.OrdinalIgnoreCase);
                    if (walking)
                    {
                        walkFrames++;
                        if (!t.WasWalking) walkStarts++;
                        double clipPace = p.Animator!.SpeedScale;
                        // Ground speed this frame must equal the clip's checked speed times the clip's playback pace.
                        double groundSpeed = moved / Step;
                        maxPaceError = Math.Max(maxPaceError, Math.Abs(groundSpeed - p.LocomotionSpeedMetersPerSecond * clipPace));
                        maxSpeed = Math.Max(maxSpeed, groundSpeed);
                        Check(p.Animator.IsPlaying() || p.Animator.CurrentAnimation == p.CurrentAnimation, $"{p.Name}: Walk clip not playing while the horse travels");
                    }
                    else if (!p.IsTransitioning && p.Descriptor!.FixtureId.Contains("palomino", StringComparison.Ordinal))
                        Check(moved < 1e-7 && !p.UsesWalkVariant, "shaped horse without a checked Walk never travels or loads a Walk model");
                    t.WasWalking = walking; t.Position = p.Position;
                }
                elapsed += Step;
            }

            var walkers = presenters.Where(p => !p.Descriptor!.FixtureId.Contains("palomino", StringComparison.Ordinal)).ToArray();
            Check(walkFrames > 600, $"the walking horses really walked (frames {walkFrames})");
            Check(walkStarts >= 6, $"walkers started several walks (starts {walkStarts})");
            Check(bridgeSwaps >= walkStarts, "every walk start/stop used the bridge");
            Check(maxPaceError < 1e-3, $"clip pace matches ground pace every frame (max error {maxPaceError:F6} m/s)");
            double fastest = walkers.Max(p => p.LocomotionSpeedMetersPerSecond);
            Check(maxJump <= fastest * Step * 1.001 + 1e-6, $"no teleports (largest single-frame step {maxJump * 1000:F3} mm)");
            Check(presenters.All(p => p.Position.Length() < 12), "horses stay in the paddock");
            Check(presenters.Single(p => p.Descriptor!.FixtureId.Contains("palomino", StringComparison.Ordinal)).Position.DistanceTo(shapedStart) < 1e-6,
                "the shaped horse stayed where it was put");

            System.IO.Directory.CreateDirectory(_output);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "yard-walk-report.json"), JsonSerializer.Serialize(new
            {
                checks = _checks, simulatedSeconds = SimulatedSeconds, walkFrames, walkStarts, bridgeSwaps,
                maxPaceErrorMetersPerSecond = maxPaceError, maxGroundSpeedMetersPerSecond = maxSpeed, maxSingleFrameStepMeters = maxJump,
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[yard-walk] PASS {_checks} checks; {walkStarts} walks, {walkFrames} walking frames, max pace error {maxPaceError:E2} m/s");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("[yard-walk] FAILED " + error);
            GetTree().Quit(1);
        }
    }

    private sealed class Track { public Node3D? Model; public Vector3 Position; public bool WasWalking; }

    private static VisualDescriptor Neutral(VisualDescriptor descriptor) => descriptor with
    {
        MorphWeights = descriptor.MorphWeights.ToDictionary(p => p.Key, _ => 0, StringComparer.Ordinal)
    };

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
