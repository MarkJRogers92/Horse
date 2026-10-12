using System;
using Godot;

namespace Cloverbrook.Game.UI;

/// <summary>
/// Pip, the stable's rabbit guide, drawn entirely in code (no art assets): a gentle bob, ear twitches,
/// blinks, and a mouth that moves while talking. Purely presentational.
/// </summary>
public partial class Bunny : Control
{
    private static readonly Color Fur = Color.FromHtml("#fffaf6");
    private static readonly Color Line = Color.FromHtml("#d9a9bd");
    private static readonly Color InnerEar = Color.FromHtml("#f7b6cb");
    private static readonly Color Eye = Color.FromHtml("#4a3050");
    private static readonly Color Cheek = new(0.97f, 0.62f, 0.72f, 0.55f);
    private double _time;
    private double _nextBlink = 2.5;
    private double _nextTwitch = 1.6;
    private double _twitchUntil;

    /// <summary>Only the head and ears, for peeking over a dialog card.</summary>
    public bool Peeking { get; set; }
    public bool Talking { get; set; }

    public Bunny() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_time > _nextTwitch) { _twitchUntil = _time + 0.25; _nextTwitch = _time + 2.2 + (_time * 7.3 % 2.0); }
        if (_time > _nextBlink + 0.14) _nextBlink = _time + 2.8 + (_time * 3.1 % 1.6);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var s = Mathf.Min(Size.X, Size.Y) / 100f;
        var bob = (float)Math.Sin(_time * 2.2) * 1.6f;
        Vector2 P(float x, float y) => new(x * s + (Size.X - 100 * s) / 2, (y + bob) * s);
        var twitch = _time < _twitchUntil ? (float)Math.Sin((_twitchUntil - _time) * 40) * 0.12f : 0f;

        if (!Peeking) Ellipse(P(50, 97), 30 * s, 15 * s, 0, Fur, Line);
        Ellipse(P(36, 24), 9 * s, 24 * s, -0.22f, Fur, Line);
        Ellipse(P(36, 25), 4.5f * s, 17 * s, -0.22f, InnerEar, null);
        Ellipse(P(64, 24), 9 * s, 24 * s, 0.22f + twitch, Fur, Line);
        Ellipse(P(64, 25), 4.5f * s, 17 * s, 0.22f + twitch, InnerEar, null);
        Ellipse(P(50, 62), 31 * s, 27 * s, 0, Fur, Line);
        Ellipse(P(31, 70), 6 * s, 4 * s, 0, Cheek, null);
        Ellipse(P(69, 70), 6 * s, 4 * s, 0, Cheek, null);

        var blinking = _time > _nextBlink && _time < _nextBlink + 0.14;
        foreach (var x in new[] { 39f, 61f })
        {
            if (blinking) DrawArc(P(x, 59), 4 * s, 0.2f, Mathf.Pi - 0.2f, 10, Eye, 2 * s, true);
            else
            {
                DrawCircle(P(x, 59), 4.6f * s, Eye);
                DrawCircle(P(x + 1.4f, 57.4f), 1.6f * s, Colors.White);
            }
        }
        DrawColoredPolygon(new[] { P(47, 66), P(53, 66), P(50, 69.5f) }, InnerEar);
        var open = Talking && Math.Sin(_time * 18) > -0.2;
        if (open) Ellipse(P(50, 74), 3.6f * s, 3 * s, 0, Color.FromHtml("#b45a7a"), null);
        else
        {
            DrawArc(P(47.5f, 71), 2.5f * s, 0.1f, Mathf.Pi - 0.3f, 8, Line, 1.6f * s, true);
            DrawArc(P(52.5f, 71), 2.5f * s, 0.3f, Mathf.Pi - 0.1f, 8, Line, 1.6f * s, true);
        }
    }

    private void Ellipse(Vector2 center, float rx, float ry, float angle, Color fill, Color? outline)
    {
        const int Segments = 32;
        var points = new Vector2[Segments];
        var (sin, cos) = ((float)Math.Sin(angle), (float)Math.Cos(angle));
        for (var i = 0; i < Segments; i++)
        {
            var a = Mathf.Tau * i / Segments;
            var x = Mathf.Cos(a) * rx; var y = Mathf.Sin(a) * ry;
            points[i] = center + new Vector2(x * cos - y * sin, x * sin + y * cos);
        }
        DrawColoredPolygon(points, fill);
        if (outline.HasValue)
        {
            var loop = new Vector2[Segments + 1]; Array.Copy(points, loop, Segments); loop[Segments] = points[0];
            DrawPolyline(loop, outline.Value, Mathf.Max(1.2f, rx / 14f), true);
        }
    }
}

/// <summary>Pip with a speech bubble. <see cref="Say"/> types the line out while Pip's mouth moves.</summary>
public partial class BunnySpeaker : HBoxContainer
{
    private const float CharactersPerSecond = 55f;
    private readonly Bunny _bunny = new() { CustomMinimumSize = new Vector2(78, 78) };
    private readonly Label _line = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
    private float _shown;

    public BunnySpeaker()
    {
        AddThemeConstantOverride("separation", 0);
        _bunny.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddChild(_bunny);
        AddChild(new BubbleTail { CustomMinimumSize = new Vector2(14, 0), SizeFlagsVertical = SizeFlags.Fill });
        var bubble = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var style = CozyTheme.Box(Colors.White, 18, 8); style.BorderColor = CozyTheme.Accent; style.SetBorderWidthAll(2);
        bubble.AddThemeStyleboxOverride("panel", style);
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 0); bubble.AddChild(stack);
        var name = CozyTheme.Title("Pip", 13); name.AddThemeColorOverride("font_color", CozyTheme.AccentDeep); stack.AddChild(name);
        _line.AddThemeFontSizeOverride("font_size", 15);
        stack.AddChild(_line);
        AddChild(bubble);
    }

    public string Text => _line.Text;

    public void Say(string text)
    {
        if (_line.Text == text) return;
        _line.Text = text; _shown = 0; _line.VisibleCharacters = 0; _bunny.Talking = true;
    }

    /// <summary>Shows the whole line immediately (used by captures and when the player is busy).</summary>
    public void Finish() { _shown = _line.Text.Length; _line.VisibleCharacters = -1; _bunny.Talking = false; }

    public override void _Process(double delta)
    {
        if (!_bunny.Talking) return;
        _shown += (float)delta * CharactersPerSecond;
        if (_shown >= _line.Text.Length) Finish();
        else _line.VisibleCharacters = (int)_shown;
    }

    private partial class BubbleTail : Control
    {
        public override void _Draw()
        {
            var mid = Size.Y / 2;
            DrawColoredPolygon(new[] { new Vector2(Size.X + 2, mid - 9), new Vector2(Size.X + 2, mid + 9), new Vector2(1, mid + 2) }, Colors.White);
            DrawPolyline(new[] { new Vector2(Size.X, mid - 9), new Vector2(1, mid + 2), new Vector2(Size.X, mid + 9) }, CozyTheme.Accent, 2, true);
        }
    }
}
