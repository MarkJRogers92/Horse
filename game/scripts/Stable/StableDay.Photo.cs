using System;
using System.IO;
using Godot;
using Cloverbrook.Game.UI;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// Photo mode (D-023): hide the interface, orbit the camera around a horse or the whole paddock, and save a
/// snapshot to Pictures/Cloverbrook Stables. Presentation only; nothing touches the save.
/// </summary>
public partial class StableDay
{
    private CanvasLayer? _photoLayer;
    private Label? _photoSubject;
    private bool _photoDragging;
    private Control? _photoBar;

    private Button PhotoButton()
    {
        var button = CozyTheme.WithIcon(new Button { Text = "", TooltipText = "Photo mode", CustomMinimumSize = new Vector2(52, 44) }, "camera", 30);
        button.Pressed += EnterPhotoMode;
        return button;
    }

    private void EnterPhotoMode()
    {
        if (_state == null || _busy || _photoLayer != null) return;
        CloseCareBubble();
        _uiLayer.Visible = false;
        _yard.EnterPhotoMode(_yard.PhotoNextSubjectFirst());
        _photoLayer = new CanvasLayer { Layer = 5 };
        AddChild(_photoLayer);
        var bar = new PanelContainer { Theme = CozyTheme.Build() };
        bar.AddThemeStyleboxOverride("panel", CozyTheme.Box(new Color(1, 0.98f, 0.96f, 0.92f), 22, 10));
        bar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); bar.AddChild(row);
        var exit = new Button { Text = "Done", CustomMinimumSize = new Vector2(110, 46) };
        exit.Pressed += ExitPhotoMode;
        row.AddChild(exit);
        var next = new Button { Text = "Next subject", CustomMinimumSize = new Vector2(150, 46) };
        next.Pressed += () => { _yard.PhotoNextSubject(); UpdatePhotoSubject(); };
        row.AddChild(next);
        _photoSubject = CozyTheme.Title("", 16); _photoSubject.VerticalAlignment = VerticalAlignment.Center; _photoSubject.CustomMinimumSize = new Vector2(150, 0);
        row.AddChild(_photoSubject);
        var hint = SmallText("Drag to turn · scroll to zoom"); hint.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(hint);
        var snap = CozyTheme.WithIcon(new Button { Text = "Snap", CustomMinimumSize = new Vector2(140, 50) }, "camera", 30);
        snap.Pressed += () => _ = SnapAsync();
        row.AddChild(snap);
        _photoLayer.AddChild(bar);
        bar.ResetSize();
        var screen = GetViewport().GetVisibleRect().Size;
        bar.Position = new Vector2((screen.X - bar.GetCombinedMinimumSize().X) / 2, screen.Y - bar.GetCombinedMinimumSize().Y - 18);
        _photoBar = bar;
        UpdatePhotoSubject();
    }

    private void UpdatePhotoSubject()
    {
        if (_photoSubject == null || _state == null) return;
        var key = _yard.PhotoSubject;
        _photoSubject.Text = key == null ? "Whole yard" : System.Linq.Enumerable.FirstOrDefault(_state.Horses, h => h.HorseId.ToString() == key)?.Name ?? "";
    }

    private void ExitPhotoMode()
    {
        _photoLayer?.QueueFree(); _photoLayer = null; _photoBar = null; _photoSubject = null;
        _yard.ExitPhotoMode();
        _uiLayer.Visible = true;
    }

    /// <summary>Photo-mode mouse handling; returns true when the event was used.</summary>
    private bool HandlePhotoInput(InputEvent @event)
    {
        if (_photoLayer == null) return false;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }: _yard.PhotoZoom(0.9f); return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }: _yard.PhotoZoom(1.1f); return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button: _photoDragging = button.Pressed; return true;
            case InputEventMouseMotion motion when _photoDragging: _yard.PhotoOrbit(-motion.Relative.X * 0.3f, motion.Relative.Y * 0.2f); return true;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }: ExitPhotoMode(); return true;
        }
        return true;
    }

    private async System.Threading.Tasks.Task SnapAsync()
    {
        if (_photoBar == null) return;
        _photoBar.Visible = false;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        if (IsInstanceValid(_photoBar)) _photoBar.Visible = true;
        string message;
        try
        {
            var folder = Path.Combine(OS.GetSystemDir(OS.SystemDir.Pictures), "Cloverbrook Stables");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Cloverbrook-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            image.SavePng(path);
            message = "Saved to Pictures › Cloverbrook Stables";
        }
        catch (Exception ex) { message = "The photo could not be saved: " + ex.Message; }
        Audio.GameAudio.Play("shutter", -4);
        if (_photoLayer == null) return;
        var flash = new ColorRect { Color = new Color(1, 1, 1, 0.85f), MouseFilter = Control.MouseFilterEnum.Ignore };
        flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _photoLayer.AddChild(flash);
        var toast = CozyTheme.Ribbon(message, 18);
        toast.Position = new Vector2(24, 24);
        _photoLayer.AddChild(toast);
        var tween = CreateTween();
        tween.TweenProperty(flash, "color:a", 0f, 0.35);
        tween.TweenCallback(Callable.From(flash.QueueFree));
        GetTree().CreateTimer(2.4).Timeout += () => { if (IsInstanceValid(toast)) toast.QueueFree(); };
    }
}
