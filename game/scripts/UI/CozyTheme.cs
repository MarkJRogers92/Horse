using Godot;

namespace Cloverbrook.Game.UI;

/// <summary>Soft rounded pastel UI theme built in code (no binary theme assets).</summary>
public static class CozyTheme
{
    public static readonly Color Text = Color.FromHtml("#4a3050");
    public static readonly Color Panel = Color.FromHtml("#fff7f3f0");
    public static readonly Color Accent = Color.FromHtml("#f3a6c0");
    public static readonly Color AccentHover = Color.FromHtml("#f7bfd2");
    public static readonly Color Warning = Color.FromHtml("#ffe1d6");

    public static Theme Build()
    {
        var t = new Theme();
        t.DefaultFontSize = 17;
        var panel = Box(Panel, 18, 14); panel.BorderColor = Color.FromHtml("#f1cfdc"); panel.SetBorderWidthAll(2);
        panel.ShadowColor = new Color(0.4f, 0.2f, 0.35f, 0.15f); panel.ShadowSize = 8;
        t.SetStylebox("panel", "PanelContainer", panel);
        foreach (var type in new[] { "Button", "OptionButton", "CheckButton" })
        {
            t.SetStylebox("normal", type, Box(Color.FromHtml("#fde4ec"), 14, 8));
            t.SetStylebox("hover", type, Box(AccentHover, 14, 8));
            t.SetStylebox("pressed", type, Box(Accent, 14, 8));
            t.SetStylebox("focus", type, Box(new Color(1, 1, 1, 0), 14, 8));
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Text);
            t.SetColor("font_pressed_color", type, Text);
        }
        t.SetColor("font_color", "Label", Text);
        t.SetStylebox("panel", "PopupMenu", Box(Panel, 12, 8));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Text);
        t.SetStylebox("hover", "PopupMenu", Box(AccentHover, 8, 4));
        return t;
    }

    public static StyleBoxFlat Box(Color bg, int radius, int pad)
    {
        var b = new StyleBoxFlat { BgColor = bg };
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
        return l;
    }
}
