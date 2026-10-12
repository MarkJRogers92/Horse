using System;
using System.Threading.Tasks;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>Live-test deadline in the same simulation clock as the bridge.
/// Slow software rendering can cap process delta, so wall time is only a watchdog.</summary>
internal static class TransitionCompletionWait
{
    internal sealed record Result(bool Completed, string StopReason, int Frames,
        double EngineSeconds, double WallSeconds);

    internal static async Task<Result> Wait(Node observer, HorsePresenter presenter,
        double engineSeconds = 3, double wallSeconds = 30)
    {
        if (!double.IsFinite(engineSeconds) || engineSeconds <= 0 ||
            !double.IsFinite(wallSeconds) || wallSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(engineSeconds));
        if (!presenter.IsTransitioning) return new(true, "already-complete", 0, 0, 0);
        ulong start = Time.GetTicksMsec();
        // SceneTreeTimer is updated after the nodes, unlike ProcessFrame which
        // fires before their _Process callbacks. It observes completed simulation.
        var timer = observer.GetTree().CreateTimer(engineSeconds, processAlways: false);
        int frames = 0;
        while (presenter.IsTransitioning && timer.TimeLeft > 0 &&
               (Time.GetTicksMsec() - start) / 1000.0 < wallSeconds)
        {
            await observer.ToSignal(observer.GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
        }
        double wall = (Time.GetTicksMsec() - start) / 1000.0;
        double elapsed = engineSeconds - Math.Max(0, timer.TimeLeft);
        bool completed = !presenter.IsTransitioning && timer.TimeLeft > 0 && wall < wallSeconds;
        return new(completed,
            completed ? "complete" : timer.TimeLeft <= 0 ? "engine-timeout" : "wall-watchdog",
            frames, elapsed, wall);
    }
}
