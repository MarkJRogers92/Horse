using System.Linq;
using Cloverbrook.Game.CameraControl;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Scenes;

/// <summary>
/// A horse on a set with lights and an orbit camera. Used by the viewer, the stable
/// scene and the CI capture runner so every venue shows the horse identically.
/// </summary>
public partial class HorseStage : Node3D
{
    public HorsePresenter Presenter { get; private set; } = null!;
    public OrbitCamera Camera { get; private set; } = null!;
    public LightingMode Mode { get; private set; }
    private Node3D? _set, _lights;
    private double _walkRouteElapsed;

    public override void _Ready()
    {
        Presenter = new HorsePresenter { Name = "Horse" };
        AddChild(Presenter);
        Presenter.Presented += OnHorsePresented;
        Camera = new OrbitCamera { Name = "Camera", Fov = 38, Current = true };
        AddChild(Camera);
    }

    public override void _Process(double delta)
    {
        if (Presenter == null || Camera == null) return;
        if (Presenter.IsTransitioning) return;
        double speed = Presenter.LocomotionSpeedMetersPerSecond;
        double period = Presenter.LocomotionPeriodSeconds;
        if (!Presenter.UsesWalkVariant || !string.Equals(Presenter.CurrentAnimation, "Walk", System.StringComparison.OrdinalIgnoreCase) ||
            !double.IsFinite(speed) || speed <= 0 || !double.IsFinite(period) || period <= 0)
        {
            if (!Presenter.PreserveRoutePlacement) ResetWalkRoute();
            return;
        }
        if (Presenter.Animator?.IsPlaying() != true) return;

        // Keep the walk route bounded to four checked clip cycles. The camera follows
        // exactly the same scene-root delta, so the horse stays framed as it advances.
        double routeDuration = period * 4.0;
        _walkRouteElapsed = (_walkRouteElapsed + delta) % routeDuration;
        float nextX = (float)(speed * _walkRouteElapsed);
        float deltaX = nextX - Presenter.Position.X;
        Presenter.Position = new Vector3(nextX, Presenter.Position.Y, Presenter.Position.Z);
        Camera.Target += Vector3.Right * deltaX;
    }

    private void OnHorsePresented(string summary, bool supported)
    {
        if (!Presenter.IsTransitioning || !supported) ResetWalkRoute();
    }

    private void ResetWalkRoute()
    {
        if (Presenter == null) return;
        _walkRouteElapsed = 0;
        float deltaX = -Presenter.Position.X;
        Presenter.Position = new Vector3(0, Presenter.Position.Y, Presenter.Position.Z);
        if (Camera != null) Camera.Target += Vector3.Right * deltaX;
    }

    public void SetMode(LightingMode mode)
    {
        Mode = mode;
        _set?.QueueFree(); _lights?.QueueFree();
        _set = mode == LightingMode.Neutral ? Sets.Studio() : Sets.Stable();
        _lights = Lighting.Build(mode);
        AddChild(_set); AddChild(_lights);
    }

    /// <summary>Standard presentation views. Horse faces +X; its right (mane) side is +Z.</summary>
    public void FrameView(string view)
    {
        bool foal = Presenter.Descriptor?.IsFoal == true;
        float s = foal ? 0.66f : 1f;
        var target = new Vector3(0.05f * s, 1.02f * s, 0) + Vector3.Right * Presenter.Position.X;
        switch (view)
        {
            case "side": Camera.SetView(0, 4, 5.4f * s, target); break;
            case "side_left": Camera.SetView(180, 4, 5.4f * s, target); break;
            case "front": Camera.SetView(90, 6, 4.6f * s, target); break;
            case "rear": Camera.SetView(-90, 8, 4.6f * s, target); break;
            case "head": Camera.SetView(55, 6, (Presenter.UsesWalkVariant ? 2.2f : 1.6f) * s, (Presenter.HeadViewTarget ?? new Vector3(1.2f * s, 1.75f * s, 0)) + Vector3.Right * Presenter.Position.X); break;
            case "legs": Camera.SetView(30, 2, 2.4f * s, new Vector3(0.0f, 0.45f * s, 0) + Vector3.Right * Presenter.Position.X); break;
            case "wide": Camera.SetView(38, 12, 10.5f * s, target + new Vector3(-1.6f, 0.6f, -1.2f)); break;
            default: Camera.SetView(42, 8, 5.0f * s, target); break; // q3: three-quarter front, mane side
        }
    }

    public static string[] Views => new[] { "q3", "side", "front", "rear", "head", "legs", "side_left", "wide" };
}
