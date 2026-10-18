using System;
using System.Text.Json;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Scenes;

/// <summary>Original concept art for review only; never an owned horse or genetic forecast.</summary>
public partial class ConceptPortraitGallery : Control
{
    private GridContainer _cards = null!;
    private Button _back = null!;
    private bool _leaving;

    public override void _Ready()
    {
        Theme = CozyTheme.Build();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddBackdrop();
        var background = new ColorRect { Color = new Color(CozyTheme.Cream, 0.88f), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var edge in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride("margin_" + edge, 28);
        AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 16);
        margin.AddChild(content);
        var header = new HBoxContainer();
        content.AddChild(header);
        var title = CozyTheme.Title("Concept portraits", 30);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        _back = new Button { Name = "BackToMenu", Text = "Back to menu", CustomMinimumSize = new Vector2(156, 44) };
        _back.Pressed += ReturnToMenu;
        header.AddChild(_back);
        content.AddChild(Wrapped("AI-generated illustration studies. These do not depict your saved horses or predict a foal's appearance.", 18));
        var scroll = new ScrollContainer { Name = "PortraitScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.AddChild(scroll);
        _cards = new GridContainer { Name = "PortraitCards", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cards.AddThemeConstantOverride("h_separation", 18);
        _cards.AddThemeConstantOverride("v_separation", 18);
        scroll.AddChild(_cards);
        try
        {
            using var catalog = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/ui/concept_portraits/catalog.json"));
            foreach (var asset in catalog.RootElement.GetProperty("assets").EnumerateArray()) AddPortrait(asset);
        }
        catch (Exception ex)
        {
            GD.PushWarning("[concept-portraits] Could not load gallery: " + ex.Message);
            _cards.AddChild(Wrapped("The concept portraits could not be loaded. You can return to the menu safely.", 18));
        }
        content.AddChild(Wrapped("Higgsfield Soul 2.0 · Original downloads · Commercial-release rights not yet verified", 14));
        Resized += UpdateColumns;
        UpdateColumns();
        _back.GrabFocus();
    }

    private void AddBackdrop()
    {
        try
        {
            using var catalog = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/ui/concept_portraits/catalog.json"));
            var background = catalog.RootElement.GetProperty("background");
            var crop = background.GetProperty("display_region");
            var texture = GD.Load<Texture2D>("res://assets/ui/concept_portraits/" + background.GetProperty("file").GetString());
            var art = new TextureRect { Name = "StableBackdrop", MouseFilter = MouseFilterEnum.Ignore,
                Texture = new AtlasTexture { Atlas = texture,
                    Region = new Rect2(crop[0].GetInt32(), crop[1].GetInt32(), crop[2].GetInt32(), crop[3].GetInt32()),
                    FilterClip = true },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
            art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(art);
        }
        catch (Exception ex) { GD.PushWarning("[concept-portraits] Backdrop unavailable: " + ex.Message); }
    }

    private void UpdateColumns() => _cards.Columns = Size.X >= 1000 ? 3 : 1;

    private void AddPortrait(JsonElement asset)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);
        var file = asset.GetProperty("file").GetString()!;
        var texture = GD.Load<Texture2D>("res://assets/ui/concept_portraits/" + file);
        if (texture == null) throw new InvalidOperationException("Missing portrait " + file);
        body.AddChild(new TextureRect { Name = "Portrait", Texture = texture, CustomMinimumSize = new Vector2(240, 300),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore });
        body.AddChild(Wrapped(asset.GetProperty("title").GetString()!, 22));
        body.AddChild(Wrapped(asset.GetProperty("description").GetString()!, 16));
        var note = Wrapped(asset.GetProperty("quality_notes").GetString()!, 14);
        note.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        body.AddChild(note);
        _cards.AddChild(card);
    }

    private static Label Wrapped(string text, int size)
    {
        var label = CozyTheme.Title(text, size);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!input.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        ReturnToMenu();
    }

    private void ReturnToMenu()
    {
        if (_leaving) return;
        _leaving = true;
        _back.Disabled = true;
        if (GetTree().ChangeSceneToFile("res://scenes/main.tscn") == Error.Ok) return;
        _leaving = false;
        _back.Disabled = false;
        GD.PushWarning("[concept-portraits] Could not return to the main menu.");
    }
}
