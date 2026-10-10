using Godot;

namespace Cloverbrook.Game.CameraControl;

/// <summary>Reusable orbit camera: drag/arrow keys to orbit, wheel/pinch/+/- to zoom.</summary>
public partial class OrbitCamera : Camera3D
{
    [Export] public Vector3 Target = new(0, 1.1f, 0);
    [Export] public float Yaw = 35f;          // degrees; 0 = looking at the horse's left side
    [Export] public float Pitch = 8f;
    [Export] public float Distance = 4.6f;
    [Export] public float MinDistance = 1.2f;
    [Export] public float MaxDistance = 12f;
    [Export] public bool AutoRotate;

    private bool _dragging;

    public override void _Ready() => Apply();

    public void SetView(float yaw, float pitch, float distance, Vector3? target = null)
    {
        Yaw = yaw; Pitch = pitch; Distance = distance;
        if (target.HasValue) Target = target.Value;
        Apply();
    }

    public override void _Process(double delta)
    {
        float k = (float)delta;
        if (AutoRotate) Yaw += 12f * k;
        if (Input.IsActionPressed("ui_left") || Input.IsActionPressed("orbit_left")) Yaw -= 70f * k;
        if (Input.IsActionPressed("ui_right") || Input.IsActionPressed("orbit_right")) Yaw += 70f * k;
        if (Input.IsActionPressed("ui_up")) Pitch = Mathf.Clamp(Pitch + 40f * k, -5f, 70f);
        if (Input.IsActionPressed("ui_down")) Pitch = Mathf.Clamp(Pitch - 40f * k, -5f, 70f);
        Apply();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb when mb.ButtonIndex is MouseButton.Left or MouseButton.Right:
                _dragging = mb.Pressed; break;
            case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp:
                Distance = Mathf.Clamp(Distance * 0.9f, MinDistance, MaxDistance); break;
            case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown:
                Distance = Mathf.Clamp(Distance * 1.1f, MinDistance, MaxDistance); break;
            case InputEventMouseMotion mm when _dragging:
                Yaw += mm.Relative.X * 0.3f; Pitch = Mathf.Clamp(Pitch + mm.Relative.Y * 0.2f, -5f, 70f); break;
            case InputEventScreenDrag sd:
                Yaw += sd.Relative.X * 0.3f; Pitch = Mathf.Clamp(Pitch + sd.Relative.Y * 0.2f, -5f, 70f); break;
            case InputEventMagnifyGesture mg:
                Distance = Mathf.Clamp(Distance / mg.Factor, MinDistance, MaxDistance); break;
            case InputEventKey { Pressed: true, Keycode: Key.Equal or Key.KpAdd }:
                Distance = Mathf.Clamp(Distance * 0.9f, MinDistance, MaxDistance); break;
            case InputEventKey { Pressed: true, Keycode: Key.Minus or Key.KpSubtract }:
                Distance = Mathf.Clamp(Distance * 1.1f, MinDistance, MaxDistance); break;
        }
    }

    private void Apply()
    {
        float y = Mathf.DegToRad(Yaw), p = Mathf.DegToRad(Pitch);
        // Horse faces +X; yaw 0 puts the camera on the horse's left (+Z in Godot = -Y Blender... see TOOLCHAIN.md)
        var offset = new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p)) * Distance;
        GlobalPosition = Target + offset;
        LookAt(Target, Vector3.Up);
    }
}
