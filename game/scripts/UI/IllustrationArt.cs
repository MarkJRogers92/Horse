using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Cloverbrook.Game.UI;

/// <summary>Decorative studies only. Never a renderer for a saved horse or a genetics result.</summary>
public static class IllustrationArt
{
    public const string Root = "res://assets/ui/illustrations/";
    public static List<JsonElement> ReadCatalog()
    {
        using var document = JsonDocument.Parse(FileAccess.GetFileAsString(Root + "catalog.json"));
        var result = new List<JsonElement>();
        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray()) result.Add(asset.Clone());
        return result;
    }

    public static TextureRect Image(string id, int size)
    {
        var path = Root + id + ".png";
        return new TextureRect { Name = "Illustration", Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null,
            CustomMinimumSize = new Vector2(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
            TooltipText = "Illustration study · not a saved horse or genetic prediction" };
    }

    private static Shader? _rounded;

    /// <summary>Gallery art: trims only the baked black frame bands (catalog frame_inset, source pixels),
    /// keeps the remaining picture uncropped, and rounds its corners to match the cozy cards.</summary>
    public static TextureRect Portrait(JsonElement asset, Texture2D texture, float height)
    {
        Texture2D shown = texture;
        float aspect = texture.GetWidth() / (float)texture.GetHeight();
        if (asset.TryGetProperty("frame_inset", out var inset))
        {
            // Runtime textures are import-capped (768), so scale source-pixel insets to the loaded size.
            float scale = texture.GetWidth() / (float)asset.GetProperty("width").GetInt32();
            float l = inset[0].GetInt32() * scale, t = inset[1].GetInt32() * scale;
            float w = texture.GetWidth() - l - inset[2].GetInt32() * scale, h = texture.GetHeight() - t - inset[3].GetInt32() * scale;
            shown = new AtlasTexture { Atlas = texture, Region = new Rect2(l, t, w, h), FilterClip = true };
            aspect = w / h;
        }
        _rounded ??= GD.Load<Shader>("res://shaders/rounded_image.gdshader");
        var size = new Vector2(Mathf.Round(height * aspect), height);
        var material = new ShaderMaterial { Shader = _rounded };
        material.SetShaderParameter("size", size);
        material.SetShaderParameter("radius", 16f);
        var rect = new TextureRect { Name = "Portrait", Texture = shown, CustomMinimumSize = size, Material = material,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.Resized += () => material.SetShaderParameter("size", rect.Size);
        return rect;
    }

    // Existing header heights are retained, so actions and gameplay previews do not get pushed offscreen.
    public static Control? Header(VBoxContainer body, string name, int height)
    {
        var entry = name switch
        {
            "sale" => ("57-marketplace", "A new chapter", "Review this horse's offer below."),
            "gallop" => ("56-covered-arena", "Time to practise", "A quiet moment before the run."),
            "day" => ("39-stable-courtyard", "A day at your stable", "Care, company and a little progress."),
            _ => ("", "", "")
        };
        if (entry.Item1.Length == 0) return null;
        var row = new HBoxContainer { Name = "IllustratedHeader", CustomMinimumSize = new Vector2(0, height) };
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(Image(entry.Item1, height));
        var text = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(text);
        text.AddChild(CozyTheme.Title(entry.Item2, 18));
        var caption = CozyTheme.Title(entry.Item3, 15); caption.AutowrapMode = TextServer.AutowrapMode.WordSmart; text.AddChild(caption);
        var note = CozyTheme.Title("ILLUSTRATION", 10); note.AddThemeColorOverride("font_color", CozyTheme.SoftText); text.AddChild(note);
        body.AddChild(row); body.MoveChild(row, 1);
        return row;
    }

    /// <summary>Small contextual artwork in existing dialog chrome; real horse previews stay intact.</summary>
    public static void AddDialogAccent(HBoxContainer header, string title)
    {
        var id = title switch
        {
            "Review breeding plan" => "60-breeding-planning",
            "A foal is born!" => "40-nursery",
            "Stable Shop" => "49-tack-room-b",
            "Dress up" => "58-customization",
            "Cooperative pasture" => "52-turnout",
            _ => ""
        };
        if (id.Length > 0) header.AddChild(Image(id, 54));
    }
}
