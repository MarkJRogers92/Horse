using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>"A foal is born!" celebration after a saved day (D-012). Presentation only: naming uses the existing rename save.</summary>
public partial class StableDay
{
    private Control _birthOverlay = null!;
    private Label _birthTitle = null!;
    private Label _birthText = null!;
    private Control _birthArt = null!;
    private LineEdit _birthName = null!;
    private HorseId? _birthFoal;
    private CpuParticles2D _confetti = null!;

    private static readonly string[] FoalNames =
    {
        "Clover", "Bramble", "Willow", "Hazel", "Poppy", "Maple", "Juniper", "Biscuit", "Honey", "Pippin", "Bluebell", "Saffron",
        "Thistle", "Marigold", "Pebble", "Nutmeg", "Rosie", "Sorrel", "Buttercup", "Fern", "Acorn", "Wren", "Daisy", "Caramel",
        "Primrose", "Toffee", "Meadow", "Sparrow", "Cinnamon", "Lark", "Muffin", "Starling", "Tansy", "Bonnie", "Magnolia", "Hickory",
        "Dandelion", "Sage", "Comet", "Velvet", "Ember", "Misty", "Briar", "Sunny", "Ginger", "Moonbeam", "Petal", "Dusty",
    };
    private readonly RandomNumberGenerator _nameRng = new();

    /// <summary>A small button that fills a name box with an unused, cosy horse name.</summary>
    private Button SuggestNameButton(LineEdit target)
    {
        var button = new Button { Text = "Idea", CustomMinimumSize = new Vector2(64, 38), TooltipText = "Suggest a name" };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += () =>
        {
            var taken = _state?.Horses.Select(h => h.Name).ToHashSet() ?? new System.Collections.Generic.HashSet<string>();
            var free = FoalNames.Where(n => !taken.Contains(n) && n != target.Text).ToArray();
            if (free.Length > 0) target.Text = free[_nameRng.RandiRange(0, free.Length - 1)];
        };
        return button;
    }

    private Control _nameStableOverlay = null!;

    private static readonly string[] StableNameIdeas =
    {
        "Cloverbrook Stables", "Willow Hollow Farm", "Bluebell Meadows", "Honeysuckle Stables", "Starlight Paddocks", "Rosewood Farm",
        "Maplewood Stables", "Buttercup Hill", "Moonbeam Ranch", "Primrose Pastures", "Thistledown Farm", "Sunny Vale Stables",
    };

    private void BuildNameStableOverlay(Control root)
    {
        _nameStableOverlay = CozyTheme.Dialog(root, "Name your stable", 560, out var body, out var buttons, out _);
        var speaker = new BunnySpeaker();
        speaker.Say("Welcome to your very own stable! What shall we call it? You'll see the name on the sign by the paddock.");
        body.AddChild(speaker);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; row.AddThemeConstantOverride("separation", 8);
        var input = new LineEdit { Text = StableName.Default, MaxLength = 28, CustomMinimumSize = new Vector2(300, 44) };
        row.AddChild(input);
        var idea = new Button { Text = "Idea", CustomMinimumSize = new Vector2(64, 44) };
        idea.Pressed += () =>
        {
            var options = StableNameIdeas.Where(n => n != input.Text).ToArray();
            input.Text = options[_nameRng.RandiRange(0, options.Length - 1)];
        };
        row.AddChild(idea);
        body.AddChild(row);
        var save = new Button { Text = "That's the one!", CustomMinimumSize = new Vector2(190, 46) };
        save.Pressed += () =>
        {
            var name = StableName.Set(_store.SavePath, input.Text);
            _stableNameLabel.Text = name; _yard.SetStableName(name);
            _nameStableOverlay.Visible = false;
            Audio.GameAudio.Play("care_cheer");
        };
        buttons.AddChild(save);
        CozyTheme.FinishDialog(body, buttons);
    }

    /// <summary>Live play only: a brand-new stable asks for its name once.</summary>
    private void MaybeAskStableName()
    {
        _yard.SetStableName(StableName.Get(_store.SavePath));
        if (_yard.Wandering && !_foalingDemoMode && !_hubPreviewRunning && !StableName.IsChosen(_store.SavePath)) _nameStableOverlay.Visible = true;
    }

    private void BuildBirthOverlay(Control root)
    {
        _birthOverlay = CozyTheme.Dialog(root, "A foal is born!", 620, out var body, out var buttons, out _birthTitle);
        _birthArt = CozyTheme.DialogHeader(body, "birth", 576, 220) ?? new Control();
        _birthText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(560, 0), HorizontalAlignment = HorizontalAlignment.Center };
        _birthText.AddThemeFontSizeOverride("font_size", 17);
        body.AddChild(_birthText);
        var nameRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; nameRow.AddThemeConstantOverride("separation", 8);
        _birthName = new LineEdit { PlaceholderText = "Choose a name", MaxLength = 40, CustomMinimumSize = new Vector2(260, 44) };
        nameRow.AddChild(_birthName);
        nameRow.AddChild(SuggestNameButton(_birthName));
        body.AddChild(nameRow);
        var later = new Button { Text = "Later", CustomMinimumSize = new Vector2(120, 46) };
        later.Pressed += () => _birthOverlay.Visible = false;
        buttons.AddChild(later);
        var name = CozyTheme.WithIcon(new Button { Text = "Name foal", CustomMinimumSize = new Vector2(170, 46) }, "foal", 28);
        name.Pressed += () =>
        {
            if (_birthFoal == null || string.IsNullOrWhiteSpace(_birthName.Text)) return;
            _birthOverlay.Visible = false;
            _ = RenameFoalAsync(_birthFoal, _birthName.Text);
        };
        buttons.AddChild(name);
        CozyTheme.FinishDialog(body, buttons);

        _confetti = new CpuParticles2D
        {
            Emitting = false, OneShot = true, Amount = 140, Lifetime = 3.2, Explosiveness = 0.85f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(620, 10),
            Direction = new Vector2(0, 1), Spread = 35, Gravity = new Vector2(0, 260), InitialVelocityMin = 60, InitialVelocityMax = 220,
            AngularVelocityMin = -360, AngularVelocityMax = 360, ScaleAmountMin = 4, ScaleAmountMax = 8, Position = new Vector2(640, -20),
        };
        var colours = new Gradient();
        colours.SetColor(0, Color.FromHtml("#f7a1bd")); colours.SetColor(1, Color.FromHtml("#fbd36b"));
        colours.AddPoint(0.33f, Color.FromHtml("#9fd3f5")); colours.AddPoint(0.66f, Color.FromHtml("#c9a7ef"));
        _confetti.ColorInitialRamp = colours;
        _birthOverlay.AddChild(_confetti);
    }

    /// <summary>Show the celebration for the first foal born in a saved day, after the night card fades.</summary>
    private void CelebrateBirth(StableLifecycleEvent birth)
    {
        if (_state == null || !_yard.Wandering) return;
        var foal = _state.Horses.FirstOrDefault(h => h.HorseId.Equals(birth.HorseId));
        if (foal == null) return;
        var mother = birth.RelatedHorseId == null ? null : _state.Horses.FirstOrDefault(h => h.HorseId.Equals(birth.RelatedHorseId));
        _birthFoal = foal.HorseId;
        string coat;
        try { coat = HorseProfiles.For(_state, foal.HorseId).VisibleCoat; } catch { coat = "a lovely coat"; }
        _birthText.Text = $"{mother?.Name ?? "Your mare"} had a healthy foal!\nParents: {ParentNamesFor(foal, _state)} · Coat: {coat}\nWhat will you call them?";
        foreach (var old in _birthArt.GetChildren()) old.QueueFree();
        var portrait = Portrait(foal, 170, Color.FromHtml("#fbe7b0"));
        var centre = new CenterContainer(); centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.AddChild(portrait);
        _birthArt.AddChild(centre);
        _birthName.Text = "";
        var timer = GetTree().CreateTimer(3.4);
        timer.Timeout += () =>
        {
            _birthOverlay.Visible = true;
            _confetti.Restart();
            Audio.GameAudio.Play("birth_fanfare", -3);
            _birthName.GrabFocus();
        };
    }
}
