using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// One drawn lane of the practice-gallop track (D-012): sand, rails, a chequered
/// finish line and the horse's portrait token galloping along it. Purely visual:
/// <see cref="Value"/> (0-1000 of the distance) comes from the domain's recorded frames.
/// </summary>
public partial class RaceLane : Control
{
    private static readonly Color Sand = Color.FromHtml("#ecd6ae");
    private static readonly Color SandDark = Color.FromHtml("#dcc092");
    private static readonly Color Rail = Colors.White;
    private float TokenSize => _compact ? 30f : 46f;
    private bool _compact;

    /// <summary>A slimmer lane for full six-horse fields (the cup).</summary>
    public bool Compact
    {
        get => _compact;
        set { _compact = value; CustomMinimumSize = new Vector2(0, value ? 34 : 54); }
    }
    private Control _token = new();
    private readonly Label _place;
    private double _bob;
    private float _value;

    public RaceLane()
    {
        CustomMinimumSize = new Vector2(0, 54);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _place = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _place.AddThemeFontSizeOverride("font_size", 15);
        if (UI.CozyTheme.BoldFont != null) _place.AddThemeFontOverride("font", UI.CozyTheme.BoldFont);
        AddChild(_place);
    }

    /// <summary>The galloping token, normally the horse's portrait card.</summary>
    public void SetToken(Control token)
    {
        _token.QueueFree();
        _token = token;
        _token.CustomMinimumSize = new Vector2(TokenSize, TokenSize);
        _token.Size = new Vector2(TokenSize, TokenSize);
        AddChild(_token);
        MoveChild(_token, 0);
        PlaceToken();
    }

    public bool Running { get; set; }

    public float Value
    {
        get => _value;
        set { _value = Mathf.Clamp(value, 0, 1000); PlaceToken(); }
    }

    /// <summary>Show the finishing place as a rosette at the line.</summary>
    public void ShowPlace(int place)
    {
        _place.Text = place switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => place + "th" };
        var colour = place switch { 1 => Color.FromHtml("#f5c542"), 2 => Color.FromHtml("#c9d1dc"), 3 => Color.FromHtml("#e0a46e"), _ => Color.FromHtml("#f3c6d6") };
        var style = UI.CozyTheme.Box(colour, 14, 2); style.BorderColor = Colors.White; style.SetBorderWidthAll(3);
        _place.AddThemeStyleboxOverride("normal", style);
        _place.AddThemeColorOverride("font_color", UI.CozyTheme.Text);
        _place.Size = new Vector2(52, 30);
        _place.Position = new Vector2(Size.X - TokenSize - 20f - 60f, (Size.Y - 30) / 2); // just behind the token at the line
        _place.Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Running) return;
        _bob += delta;
        PlaceToken();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) PlaceToken();
    }

    private void PlaceToken()
    {
        float start = 6f, end = Size.X - TokenSize - 20f;
        float x = start + (end - start) * (_value / 1000f);
        // A gallop bounce: two quick hops per stride while running.
        float hop = Running ? Mathf.Abs(Mathf.Sin((float)_bob * 9f)) * 6f : 0f;
        _token.Position = new Vector2(x, (Size.Y - TokenSize) / 2f - hop);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var lane = new Rect2(0, 6, Size.X, Size.Y - 12);
        DrawRect(lane, Sand);
        // Hoof-churned stripes behind the token.
        float behind = 6f + (Size.X - TokenSize - 26f) * (_value / 1000f);
        for (float x = 10; x < behind; x += 22) DrawRect(new Rect2(x, lane.Position.Y + lane.Size.Y * 0.45f, 10, 3), SandDark);
        DrawLine(new Vector2(0, lane.Position.Y), new Vector2(Size.X, lane.Position.Y), Rail, 3f);
        DrawLine(new Vector2(0, lane.End.Y), new Vector2(Size.X, lane.End.Y), Rail, 3f);
        // Chequered finish line.
        float fx = Size.X - 16f; const float cell = 6f;
        for (int row = 0; row * cell < lane.Size.Y; row++)
        for (int col = 0; col < 2; col++)
            DrawRect(new Rect2(fx + col * cell, lane.Position.Y + row * cell, cell, Mathf.Min(cell, lane.End.Y - (lane.Position.Y + row * cell))),
                (row + col) % 2 == 0 ? Colors.White : UI.CozyTheme.Text);
    }
}
