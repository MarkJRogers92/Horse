using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Game.UI;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Scenes;

/// <summary>Interactive horse viewer (also used as the stable scene with StartInStable).</summary>
public partial class HorseViewer : Node
{
    [Export] public bool StartInStable;
    private static readonly string[] AnimationChoices = { "idle", "breathe", "look", "walk", "trot", "graze" };

    private HorseStage _stage = null!;
    private readonly List<VisualDescriptor> _entries = new();
    private OptionButton _coat = null!, _anim = null!, _bodyFamily = null!;
    private CheckButton _foal = null!, _stable = null!, _spin = null!;
    private Label _status = null!, _title = null!;
    private PanelContainer _warning = null!;
    private Label _warningText = null!;
    private string _currentView = "q3";

    public override void _Ready()
    {
        _stage = new HorseStage { Name = "Stage", FadeAtRouteWrap = true };
        AddChild(_stage);
        _stage.SetMode(StartInStable ? LightingMode.Stable : LightingMode.Neutral);

        foreach (var id in FixtureLibrary.ClassIds) _entries.Add(FixtureLibrary.Get(id, false));
        _entries.AddRange(FixtureLibrary.MarkingVariety("bay", false, 4));
        _entries.AddRange(FixtureLibrary.UnsupportedProbes());

        BuildUi();
        _coat.Select(1);
        Refresh();
        _stage.FrameView("q3");
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var root = new Control { Theme = CozyTheme.Build() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(root);

        var panel = new PanelContainer { Position = new Vector2(16, 16) };
        root.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        _title = CozyTheme.Title("Cloverbrook Stables", 24);
        box.AddChild(_title);
        box.AddChild(CozyTheme.Title(StartInStable ? "The paddock" : "Horse viewer", 15));

        _coat = new OptionButton { Name = "CoatSelector" };
        foreach (var e in _entries) _coat.AddItem(e.DisplayLabel + (e.TestOnly ? "  (test)" : ""));
        _coat.ItemSelected += _ => Refresh();
        box.AddChild(_coat);

        _bodyFamily = new OptionButton { Name = "BodyFamilySelector" };
        _bodyFamily.AddItem("Light");
        _bodyFamily.AddItem("Stock");
        _bodyFamily.ItemSelected += _ => Refresh();
        box.AddChild(_bodyFamily);

        _foal = new CheckButton { Name = "FoalToggle", Text = "Foal" }; _foal.Toggled += _ => Refresh();
        _stable = new CheckButton { Text = "Stable lighting", ButtonPressed = StartInStable };
        _stable.Toggled += on => { _stage.SetMode(on ? LightingMode.Stable : LightingMode.Neutral); };
        _spin = new CheckButton { Text = "Turntable" }; _spin.Toggled += on => _stage.Camera.AutoRotate = on;
        var row = new HBoxContainer(); row.AddChild(_foal); row.AddChild(_stable); row.AddChild(_spin);
        box.AddChild(row);

        _anim = new OptionButton { Name = "AnimationSelector" };
        foreach (var a in AnimationChoices) _anim.AddItem(a);
        _anim.AddItem("(still)");
        _anim.ItemSelected += _ => Refresh(resetView: false);
        _anim.Select(0);
        box.AddChild(_anim);

        var views = new HBoxContainer();
        foreach (var v in new[] { "q3", "side", "front", "head", "legs" })
        {
            var b = new Button { Text = v }; b.Pressed += () => { _currentView = v; _stage.FrameView(v); }; views.AddChild(b);
        }
        box.AddChild(views);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) };
        _status.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_status);

        var back = new Button { Text = "Back to menu" };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        box.AddChild(back);

        _warning = new PanelContainer { Visible = false };
        _warning.AddThemeStyleboxOverride("panel", CozyTheme.Box(CozyTheme.Warning, 16, 12));
        _warning.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _warning.Position = new Vector2(440, 20);
        _warningText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) };
        _warning.AddChild(_warningText);
        root.AddChild(_warning);

        var help = new Label { Text = "Drag / arrow keys: orbit    Wheel / +/-: zoom", Position = new Vector2(16, 690) };
        help.AddThemeFontSizeOverride("font_size", 13);
        root.AddChild(help);
    }

    private void Refresh(bool resetView = true)
    {
        var d = _entries[_coat.Selected];
        if (_foal.ButtonPressed)
            d = ResolveAgeVariant(d, foal: true);
        if (!d.FixtureId.StartsWith("probe.", System.StringComparison.Ordinal))
            d = d with { MeshFamilyId = $"horse.{(_bodyFamily.Selected == 1 ? "compact" : "standard")}.{(d.IsFoal ? "foal" : "adult")}.v1" };
        string requestedAnimation = _anim.Selected >= 0 && _anim.Selected < _anim.ItemCount - 1
            ? _anim.GetItemText(_anim.Selected)
            : "";

        bool walkShapeOverride = false;
        if (string.Equals(requestedAnimation, "walk", System.StringComparison.OrdinalIgnoreCase))
        {
            if (CanUseCheckedWalk(d))
            {
                d = WithNeutralWalkShape(d);
                walkShapeOverride = true;
            }
            else
            {
                // Keep the selected descriptor's default family/shape when Walk is not supported.
                int idleIndex = FindAnimationItem("idle");
                if (idleIndex >= 0) _anim.Select(idleIndex);
                requestedAnimation = "idle";
            }
        }

        // Only explicit animation changes opt into the corrected-foal bridge.
        // Coat/family/age changes retain immediate presentation and cancel stale work.
        var support = resetView
            ? _stage.Presenter.Present(d, requestedAnimation)
            : _stage.Presenter.TransitionTo(d, requestedAnimation);
        for (int i = 0; i < _anim.ItemCount - 1; i++)
            _anim.SetItemDisabled(i, !_stage.Presenter.IsAnimationAvailable(_anim.GetItemText(i)));
        if (_anim.Selected >= 0 && _anim.Selected < _anim.ItemCount - 1 && _anim.IsItemDisabled(_anim.Selected))
        {
            int fallback = FindAnimationItem("idle");
            _anim.Select(fallback >= 0 && !_anim.IsItemDisabled(fallback) ? fallback : _anim.ItemCount - 1);
        }
        _warning.Visible = !support.Supported;
        _warningText.Text = "This horse cannot be shown yet.\n" + support.Summary + "\n(No substitute coat is drawn.)";
        _status.Text = $"{d.DisplayLabel}\nclass {d.CoatClassId} · {d.MeshFamilyId}\nrecipe {d.CoatRecipe} · eyes {d.EyeRecipe} · skin {d.SkinRecipe}\n" +
                       (support.Supported ? "markings: " + _stage.Presenter.Markings!.Describe() + "\n" : "") +
                       (walkShapeOverride ? "Walk uses the checked neutral family shape.\n" : "") + support.Summary;
        if (resetView) _currentView = "q3";
        // Do not undo the user's orbit/zoom or teleport the route during a bridge.
        if (!_stage.Presenter.IsTransitioning) _stage.FrameView(_currentView);
    }

    private static int FindAnimationItem(string animation)
    {
        // The final option is the explicit still pose, not an animation.
        for (int i = 0; i < AnimationChoices.Length; i++)
            if (string.Equals(AnimationChoices[i], animation, System.StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static bool CanUseCheckedWalk(VisualDescriptor descriptor)
    {
        if (descriptor.FixtureId.StartsWith("probe.", System.StringComparison.Ordinal)) return false;
        var catalog = VisualCatalog.Instance;
        return catalog.Check(descriptor).Supported && catalog.MeshFamilies.TryGetValue(descriptor.MeshFamilyId, out var family) &&
               family.Age == (descriptor.IsFoal ? "foal" : "adult") && family.WalkVariant != null;
    }

    private static VisualDescriptor WithNeutralWalkShape(VisualDescriptor descriptor)
    {
        var neutralWeights = descriptor.MorphWeights.ToDictionary(pair => pair.Key, _ => 0, System.StringComparer.Ordinal);
        foreach (string channel in new[] { "body_length", "chest_width", "neck_length", "muscle", "height", "mane_density", "tail_density" })
            neutralWeights[channel] = 0;
        return descriptor with { MorphWeights = neutralWeights };
    }

    private static VisualDescriptor ResolveAgeVariant(VisualDescriptor selected, bool foal)
    {
        // Unsupported diagnostics are deliberately never converted into valid family fixtures.
        if (selected.FixtureId.StartsWith("probe.", System.StringComparison.Ordinal)) return selected;
        var classId = selected.FixtureId.Split('.')[0];
        if (!FixtureLibrary.ClassIds.Contains(classId, System.StringComparer.Ordinal)) return selected;

        var ageVariant = FixtureLibrary.Get(classId, foal);
        // Test-only marking variants still pair with their age-specific base, while
        // retaining the exact deterministic marking key selected in the dropdown.
        if (selected.FixtureId.Contains(".markings-", System.StringComparison.Ordinal))
            ageVariant = ageVariant.WithTestMarkingKey(selected.MarkingRealizationKey, selected.DisplayLabel);
        return ageVariant;
    }
}
