using System;
using Godot;

namespace Cloverbrook.Game.UI;

/// <summary>
/// The game's one visual language: rounded Nunito type, puffy pill buttons, cream cards with rose borders,
/// hand-drawn toggles/checkboxes and ribbon-titled dialogs. Every screen builds from this theme so no
/// Godot default (grey) control is left showing.
/// </summary>
public static class CozyTheme
{
    public static readonly Color Text = Color.FromHtml("#4a3050");
    public static readonly Color SoftText = Color.FromHtml("#7a5a80");
    public static readonly Color Panel = Color.FromHtml("#fff8f2f2");
    public static readonly Color Cream = Color.FromHtml("#fffaf5");
    public static readonly Color Accent = Color.FromHtml("#f3a6c0");
    public static readonly Color AccentHover = Color.FromHtml("#f7bfd2");
    public static readonly Color AccentDeep = Color.FromHtml("#e48aa9");
    public static readonly Color Blush = Color.FromHtml("#fde4ec");
    public static readonly Color Border = Color.FromHtml("#f1cfdc");
    public static readonly Color Mint = Color.FromHtml("#bfe6d2");
    public static readonly Color Warning = Color.FromHtml("#ffe1d6");

    private static FontFile? _regular, _bold;
    public static Font? RegularFont => _regular ??= Load("res://assets/fonts/nunito-latin-600-normal.woff2");
    public static Font? BoldFont => _bold ??= Load("res://assets/fonts/nunito-latin-800-normal.woff2");
    private static FontFile? Load(string path) => ResourceLoader.Exists(path) ? ResourceLoader.Load<FontFile>(path) : null;

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 17 };
        if (RegularFont != null) t.DefaultFont = RegularFont;

        var panel = Box(Panel, 22, 14); panel.BorderColor = Border; panel.SetBorderWidthAll(3);
        panel.ShadowColor = new Color(0.45f, 0.2f, 0.35f, 0.14f); panel.ShadowSize = 10; panel.ShadowOffset = new Vector2(0, 3);
        t.SetStylebox("panel", "PanelContainer", panel);

        foreach (var type in new[] { "Button", "OptionButton" })
        {
            t.SetStylebox("normal", type, Puffy(Blush, AccentHover));
            t.SetStylebox("hover", type, Puffy(AccentHover, Accent));
            t.SetStylebox("pressed", type, Pressed(Accent));
            t.SetStylebox("hover_pressed", type, Pressed(Accent));
            t.SetStylebox("disabled", type, Puffy(new Color("#f4ecef"), new Color("#e8dde2")));
            t.SetStylebox("focus", type, Focus());
            foreach (var colour in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
                t.SetColor(colour, type, Text);
            t.SetColor("font_disabled_color", type, new Color(Text, 0.45f));
            if (BoldFont != null) t.SetFont("font", type, BoldFont);
        }
        t.SetIcon("arrow", "OptionButton", Icons.Arrow());

        foreach (var type in new[] { "CheckButton", "CheckBox" })
        {
            t.SetStylebox("normal", type, Box(new Color(1, 1, 1, 0.55f), 16, 6));
            t.SetStylebox("hover", type, Box(Blush, 16, 6));
            t.SetStylebox("pressed", type, Box(AccentHover, 16, 6));
            t.SetStylebox("hover_pressed", type, Box(Accent, 16, 6));
            t.SetStylebox("disabled", type, Box(new Color(1, 1, 1, 0.3f), 16, 6));
            t.SetStylebox("focus", type, Focus());
            foreach (var colour in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
                t.SetColor(colour, type, Text);
            t.SetColor("font_disabled_color", type, new Color(Text, 0.5f));
        }
        t.SetIcon("checked", "CheckButton", Icons.Toggle(true)); t.SetIcon("unchecked", "CheckButton", Icons.Toggle(false));
        t.SetIcon("checked_disabled", "CheckButton", Icons.Toggle(true, 0.5f)); t.SetIcon("unchecked_disabled", "CheckButton", Icons.Toggle(false, 0.5f));
        t.SetIcon("checked", "CheckBox", Icons.Check(true)); t.SetIcon("unchecked", "CheckBox", Icons.Check(false));
        t.SetIcon("checked_disabled", "CheckBox", Icons.Check(true, 0.45f)); t.SetIcon("unchecked_disabled", "CheckBox", Icons.Check(false, 0.45f));

        var field = Box(Cream, 14, 6); field.BorderColor = Border; field.SetBorderWidthAll(2);
        var fieldFocus = Box(Color.FromHtml("#ffffff"), 14, 6); fieldFocus.BorderColor = Accent; fieldFocus.SetBorderWidthAll(3);
        t.SetStylebox("normal", "LineEdit", field); t.SetStylebox("focus", "LineEdit", fieldFocus); t.SetStylebox("read_only", "LineEdit", field);
        t.SetColor("font_color", "LineEdit", Text); t.SetColor("font_placeholder_color", "LineEdit", new Color(Text, 0.45f));
        t.SetColor("caret_color", "LineEdit", AccentDeep); t.SetColor("selection_color", "LineEdit", new Color(Accent, 0.45f));

        t.SetStylebox("background", "ProgressBar", Box(Warning, 10, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, 10, 0));

        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", type, Box(new Color(Blush, 0.6f), 6, 0));
            t.SetStylebox("grabber", type, Box(AccentHover, 6, 0));
            t.SetStylebox("grabber_highlight", type, Box(Accent, 6, 0));
            t.SetStylebox("grabber_pressed", type, Box(AccentDeep, 6, 0));
        }

        var popup = Box(Cream, 16, 8); popup.BorderColor = Border; popup.SetBorderWidthAll(2);
        t.SetStylebox("panel", "PopupMenu", popup);
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Text);
        t.SetStylebox("hover", "PopupMenu", Box(AccentHover, 10, 4));
        t.SetStylebox("panel", "TooltipPanel", popup);
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetColor("font_color", "Label", Text);
        // Sliders: rosy track and fill with a round white knob.
        var track = Box(Blush, 6, 0); track.ContentMarginTop = track.ContentMarginBottom = 5; track.BorderColor = Border; track.SetBorderWidthAll(1);
        var fill = Box(Accent, 6, 0); fill.ContentMarginTop = fill.ContentMarginBottom = 5;
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", fill);
        t.SetStylebox("grabber_area_highlight", "HSlider", fill);
        t.SetIcon("grabber", "HSlider", Icons.Knob(false));
        t.SetIcon("grabber_highlight", "HSlider", Icons.Knob(true));
        return t;
    }

    private static StyleBoxFlat Puffy(Color face, Color rim)
    {
        var b = Box(face, 22, 8); b.BorderColor = rim; b.BorderWidthBottom = 4; b.BorderWidthLeft = b.BorderWidthRight = b.BorderWidthTop = 1;
        b.ShadowColor = new Color(0.55f, 0.25f, 0.4f, 0.12f); b.ShadowSize = 4; b.ShadowOffset = new Vector2(0, 2);
        b.ContentMarginLeft = b.ContentMarginRight = 18;
        return b;
    }

    private static StyleBoxFlat Pressed(Color face)
    {
        var b = Box(face, 22, 8); b.BorderColor = AccentDeep; b.BorderWidthTop = 3; b.ContentMarginTop = 10; b.ContentMarginBottom = 6;
        b.ContentMarginLeft = b.ContentMarginRight = 18;
        return b;
    }

    private static StyleBoxFlat Focus()
    {
        var b = Box(new Color(1, 1, 1, 0), 22, 8); b.BorderColor = new Color(AccentDeep, 0.7f); b.SetBorderWidthAll(2); b.DrawCenter = false;
        return b;
    }

    /// <summary>Five ability stars, value 0-10000 (5000 = three stars); null shows five empty stars.</summary>
    public static Control StarRating(int? value)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 1);
        int filled = value is int v ? Math.Clamp((int)Math.Round(1 + v * 4.0 / 10000), 1, 5) : 0;
        for (var i = 0; i < 5; i++)
            row.AddChild(new TextureRect { Texture = i < filled ? Icons.StarOn : Icons.StarOff, CustomMinimumSize = new Vector2(20, 20),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        return row;
    }

    /// <summary>Five bond hearts (0-5 filled).</summary>
    public static Control HeartRating(int hearts, int size = 18)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 1);
        for (var i = 0; i < 5; i++)
            row.AddChild(new TextureRect { Texture = i < hearts ? Icons.HeartOn : Icons.HeartOff, CustomMinimumSize = new Vector2(size, size),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore });
        return row;
    }

    /// <summary>A generated UI icon (D-012, res://assets/ui/icons), or null if it is missing.</summary>
    public static Texture2D? Icon(string name)
    {
        var path = $"res://assets/ui/icons/icon_{name}.png";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>Put an icon on a button at a readable size.</summary>
    public static T WithIcon<T>(T button, string name, int size = 30) where T : Button
    {
        button.Icon = Icon(name);
        button.AddThemeConstantOverride("icon_max_width", size);
        return button;
    }

    public static StyleBoxFlat Box(Color bg, int radius, int pad)
    {
        var b = new StyleBoxFlat { BgColor = bg, AntiAliasing = true };
        b.SetCornerRadiusAll(radius);
        b.ContentMarginLeft = b.ContentMarginRight = pad + 4;
        b.ContentMarginTop = b.ContentMarginBottom = pad;
        return b;
    }

    public static Label Title(string text, int size)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Text);
        if (BoldFont != null) l.AddThemeFontOverride("font", BoldFont);
        return l;
    }

    /// <summary>A rosy pill with white bold text, used as a dialog or card heading.</summary>
    public static Control Ribbon(string text, int size = 22)
    {
        var pill = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        var style = Box(Accent, 20, 6); style.BorderColor = AccentDeep; style.BorderWidthBottom = 3; style.ContentMarginLeft = style.ContentMarginRight = 20;
        pill.AddThemeStyleboxOverride("panel", style);
        var label = Title(text, size); label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", AccentDeep); label.AddThemeConstantOverride("outline_size", 4);
        pill.AddChild(label);
        return pill;
    }

    /// <summary>
    /// Builds a hidden modal: dimmed backdrop, centred cream card with a ribbon title and Pip peeking in the corner.
    /// Add content to <paramref name="body"/> and actions to <paramref name="buttons"/> (right-aligned).
    /// </summary>
    public static Control Dialog(Control root, string title, int width, out VBoxContainer body, out HBoxContainer buttons, out Label titleLabel)
    {
        var overlay = new Control { Visible = false };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(overlay);
        var dim = new ColorRect { Color = new Color(0.27f, 0.15f, 0.27f, 0.45f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(dim);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect); overlay.AddChild(center);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        var style = Box(Cream, 30, 18); style.BorderColor = Accent; style.SetBorderWidthAll(4);
        style.ShadowColor = new Color(0.35f, 0.12f, 0.3f, 0.25f); style.ShadowSize = 18; style.ShadowOffset = new Vector2(0, 6);
        card.AddThemeStyleboxOverride("panel", style);
        center.AddChild(card);
        body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 12); card.AddChild(body);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 10); body.AddChild(header);
        var ribbon = Ribbon(title); header.AddChild(ribbon);
        titleLabel = (Label)ribbon.GetChild(0);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        header.AddChild(new Bunny { CustomMinimumSize = new Vector2(54, 54), Peeking = true });
        buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        // Callers add their content first, then the button row is moved to the end.
        body.AddChild(buttons);
        return overlay;
    }

    private static Shader? _roundedImage;

    /// <summary>Insert a wide illustration (res://assets/ui/headers/header_NAME.jpg) under a dialog's ribbon.</summary>
    public static TextureRect? DialogHeader(VBoxContainer body, string name, int width, int height)
    {
        var path = $"res://assets/ui/headers/header_{name}.jpg";
        if (!ResourceLoader.Exists(path)) return null;
        _roundedImage ??= GD.Load<Shader>("res://shaders/rounded_image.gdshader");
        var material = new ShaderMaterial { Shader = _roundedImage };
        material.SetShaderParameter("size", new Vector2(width, height));
        var art = new TextureRect
        {
            Texture = GD.Load<Texture2D>(path), CustomMinimumSize = new Vector2(width, height), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, Material = material, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        body.AddChild(art);
        body.MoveChild(art, 1);
        return art;
    }

    /// <summary>Moves a dialog's button row below content added after <see cref="Dialog"/>.</summary>
    public static void FinishDialog(VBoxContainer body, HBoxContainer buttons) => body.MoveChild(buttons, body.GetChildCount() - 1);

    /// <summary>Procedural, anti-aliased icons so no grey engine defaults remain.</summary>
    internal static class Icons
    {
        private static float Coverage(float distance) => Mathf.Clamp(0.5f - distance, 0f, 1f);
        private static float RoundRect(Vector2 p, Vector2 center, Vector2 half, float r)
        {
            var q = (p - center).Abs() - half + new Vector2(r, r);
            return new Vector2(Mathf.Max(q.X, 0), Mathf.Max(q.Y, 0)).Length() + Mathf.Min(Mathf.Max(q.X, q.Y), 0) - r;
        }
        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var pa = p - a; var ba = b - a; var h = Mathf.Clamp(pa.Dot(ba) / ba.Dot(ba), 0, 1);
            return (pa - ba * h).Length();
        }
        private static Color Over(Color under, Color over, float alpha)
        {
            var a = over.A * alpha; var outA = a + under.A * (1 - a);
            if (outA <= 0) return new Color(0, 0, 0, 0);
            return new Color((over.R * a + under.R * under.A * (1 - a)) / outA, (over.G * a + under.G * under.A * (1 - a)) / outA,
                (over.B * a + under.B * under.A * (1 - a)) / outA, outA);
        }
        private static ImageTexture Paint(int w, int h, Func<Vector2, Color> shade)
        {
            var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) image.SetPixel(x, y, shade(new Vector2(x + 0.5f, y + 0.5f)));
            return ImageTexture.CreateFromImage(image);
        }
        /// <summary>A five-pointed star (filled gold or an empty outline) for ability ratings.</summary>
        public static ImageTexture Star(bool filled) => Paint(22, 22, p =>
        {
            var d = p - new Vector2(11, 11.5f);
            float angle = Mathf.Atan2(d.Y, d.X) + Mathf.Pi / 2;
            float sector = Mathf.PosMod(angle, Mathf.Tau / 5) - Mathf.Tau / 10;
            float radius = Mathf.Lerp(4.6f, 10f, Mathf.Pow(Mathf.Abs(Mathf.Cos(sector * 2.5f)), 3)); // points at the sector centres
            float edge = d.Length() - radius;
            var c = new Color(0, 0, 0, 0);
            c = Over(c, filled ? Color.FromHtml("#e8a93a") : Border, Coverage(edge));
            c = Over(c, filled ? Color.FromHtml("#f7c95a") : Colors.White, Coverage(edge + 1.6f));
            return c;
        });
        /// <summary>A heart (filled pink or an empty outline) for bond (D-021).</summary>
        public static ImageTexture Heart(bool filled) => Paint(22, 22, p =>
        {
            // Implicit heart curve: (x²+y²-1)³ - x²y³ ≤ 0, scaled into the icon.
            float x = (p.X - 11f) / 8.6f, y = -(p.Y - 11.8f) / 8.6f;
            float f = Mathf.Pow(x * x + y * y - 1f, 3) - x * x * y * y * y;
            float edge = f * 9f; // approximate distance in pixels near the outline
            var c = new Color(0, 0, 0, 0);
            c = Over(c, filled ? Color.FromHtml("#e48aa9") : Border, Coverage(edge));
            c = Over(c, filled ? Color.FromHtml("#f7a8c4") : Colors.White, Coverage(edge + 1.4f));
            return c;
        });
        private static ImageTexture? _heartOn, _heartOff;
        public static ImageTexture HeartOn => _heartOn ??= Heart(true);
        public static ImageTexture HeartOff => _heartOff ??= Heart(false);
        private static ImageTexture? _starOn, _starOff;
        public static ImageTexture StarOn => _starOn ??= Star(true);
        public static ImageTexture StarOff => _starOff ??= Star(false);
        /// <summary>Free cached textures so none outlive the scene at shutdown.</summary>
        public static void Release()
        {
            _starOn?.Dispose(); _starOff?.Dispose(); _starOn = _starOff = null;
            _heartOn?.Dispose(); _heartOff?.Dispose(); _heartOn = _heartOff = null;
        }
        public static ImageTexture Knob(bool hot) => Paint(26, 26, p =>
        {
            var c = new Color(0, 0, 0, 0);
            c = Over(c, new Color(0.6f, 0.3f, 0.45f, 0.25f), Coverage((p - new Vector2(13, 14.5f)).Length() - 10f));
            c = Over(c, Colors.White, Coverage((p - new Vector2(13, 13)).Length() - 10f));
            c = Over(c, hot ? AccentDeep : Accent, Coverage((p - new Vector2(13, 13)).Length() - 4.5f));
            return c;
        });
        public static ImageTexture Toggle(bool on, float alpha = 1f) => Paint(48, 28, p =>
        {
            var c = new Color(0, 0, 0, 0);
            c = Over(c, on ? AccentDeep : Border, Coverage(RoundRect(p, new Vector2(24, 14.5f), new Vector2(22, 12), 12)) * alpha);
            c = Over(c, on ? Accent : Blush, Coverage(RoundRect(p, new Vector2(24, 14), new Vector2(20.5f, 10.5f), 10.5f)) * alpha);
            var knob = new Vector2(on ? 34 : 14, 14);
            c = Over(c, new Color(0.6f, 0.3f, 0.45f, 0.25f), Coverage((p - knob - new Vector2(0, 1.5f)).Length() - 8.5f) * alpha);
            c = Over(c, Colors.White, Coverage((p - knob).Length() - 8f) * alpha);
            if (on) c = Over(c, Accent, Coverage((p - knob).Length() - 3f) * alpha);
            return c;
        });
        public static ImageTexture Check(bool on, float alpha = 1f) => Paint(26, 26, p =>
        {
            var c = new Color(0, 0, 0, 0);
            c = Over(c, on ? AccentDeep : Accent, Coverage(RoundRect(p, new Vector2(13, 13), new Vector2(11, 11), 7)) * alpha);
            c = Over(c, on ? Accent : Colors.White, Coverage(RoundRect(p, new Vector2(13, 13), new Vector2(9, 9), 5.5f)) * alpha);
            if (on)
            {
                var tick = Mathf.Min(Segment(p, new Vector2(8, 13.5f), new Vector2(11.5f, 17)), Segment(p, new Vector2(11.5f, 17), new Vector2(18, 9)));
                c = Over(c, Colors.White, Coverage(tick - 1.6f) * alpha);
            }
            return c;
        });
        public static ImageTexture Arrow() => Paint(18, 12, p =>
            Over(new Color(0, 0, 0, 0), AccentDeep, Coverage(Mathf.Min(Segment(p, new Vector2(3, 3.5f), new Vector2(9, 8.5f)), Segment(p, new Vector2(9, 8.5f), new Vector2(15, 3.5f))) - 1.4f)));
    }
}
