using System;
using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Game.UI;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Scenes;

/// <summary>Four curated appearance sets built only from supported domain fixtures.</summary>
public partial class HorseAppearanceDemo : Node
{
    private sealed record Preset(string Name, string First, string Second);

    private static readonly Preset[] Presets =
    {
        new("Light · athletic", "light_racer.chestnut", "light_racer.palomino"),
        new("Light · long-stride", "light_endurance.bay", "light_endurance.buckskin"),
        new("Stock · muscular", "stock_muscular.black", "stock_muscular.smoky_black"),
        new("Stock · cob-influenced", "stock_cob.red_dun", "stock_cob.bay_dun"),
    };

    private HorseStage _stage = null!;
    private Node3D _grid = null!;
    private OptionButton _preset = null!, _coat = null!;
    private Label _status = null!;
    private bool _overview = true;
    private readonly Dictionary<string, VisualSupport> _overviewSupports = new(StringComparer.Ordinal);
    private string _overviewSupportSummary = "";

    public override void _Ready()
    {
        _stage = new HorseStage { Name = "Stage" };
        AddChild(_stage);
        _stage.SetMode(LightingMode.Neutral);
        foreach (var sweep in FindAll<MeshInstance3D>(_stage).Where(mesh => mesh.Mesh is CylinderMesh)) sweep.Visible = false;
        _stage.AddChild(Sets.Mesh(new PlaneMesh { Size = new Vector2(200, 200) }, Sets.Mat(new Color(0.46f, 0.46f, 0.46f), 0.9f), new Vector3(0, -0.02f, 0)));
        BuildOverview();
        BuildUi();
        _preset.Select(0);
        PopulateCoats();
        ShowOverview();
    }

    private void BuildOverview()
    {
        _grid = new Node3D { Name = "EightHorseOverview" };
        // Leave the left third of the viewport clear for the compact control panel.
        _grid.Position = new Vector3(4f, 0, 0);
        _stage.AddChild(_grid);
        var descriptors = FixtureLibrary.AppearanceStudio;
        for (int i = 0; i < descriptors.Count; i++)
        {
            var horse = new HorsePresenter { Name = $"OverviewHorse{i + 1}" };
            _grid.AddChild(horse);
            int column = i % 4, row = i / 4;
            horse.Position = new Vector3((column - 1.5f) * 3.2f, 0, (row - 0.5f) * 5.0f);
            var support = horse.Present(descriptors[i], "");
            _overviewSupports[descriptors[i].FixtureId] = support;
        }
        _overviewSupportSummary = SummarizeOverviewSupport(descriptors);
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
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        box.AddChild(CozyTheme.Title("Appearance studio", 24));
        box.AddChild(CozyTheme.Title("Art test only · four body presets · eight supported coat/mark examples", 14));

        _preset = new OptionButton();
        foreach (var preset in Presets) _preset.AddItem(preset.Name);
        _preset.ItemSelected += _ => { PopulateCoats(); Refresh(); };
        box.AddChild(_preset);

        _coat = new OptionButton();
        _coat.ItemSelected += _ => Refresh();
        box.AddChild(_coat);

        var modeButtons = new HBoxContainer();
        var all = new Button { Text = "Show all eight" };
        all.Pressed += ShowOverview;
        modeButtons.AddChild(all);
        var inspect = new Button { Text = "Inspect selected" };
        inspect.Pressed += FocusSelected;
        modeButtons.AddChild(inspect);
        box.AddChild(modeButtons);

        var views = new HBoxContainer();
        foreach (var (view, label) in new[] { ("q3", "Three-quarter"), ("side", "Side"), ("front", "Front"), ("head", "Head"), ("legs", "Legs") })
        {
            var button = new Button { Text = label };
            button.Pressed += () => { FocusSelected(); _stage.FrameView(view); };
            views.AddChild(button);
        }
        box.AddChild(views);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(390, 0) };
        _status.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_status);

        var back = new Button { Text = "Back to menu" };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        box.AddChild(back);

        var help = new Label { Text = "Drag / arrow keys: orbit    Wheel / +/-: zoom", Position = new Vector2(16, 690) };
        help.AddThemeFontSizeOverride("font_size", 13);
        root.AddChild(help);
    }

    private void PopulateCoats()
    {
        _coat.Clear();
        var preset = Presets[Math.Clamp(_preset.Selected, 0, Presets.Length - 1)];
        _coat.AddItem(FixtureLibrary.GetAppearanceStudio(preset.First).DisplayLabel);
        _coat.AddItem(FixtureLibrary.GetAppearanceStudio(preset.Second).DisplayLabel);
        _coat.Select(0);
    }

    private void Refresh()
    {
        if (_coat.ItemCount != 2) return;
        if (_overview) UpdateOverviewStatus();
        else FocusSelected();
    }

    private void ShowOverview()
    {
        _overview = true;
        _stage.Presenter.Visible = false;
        _grid.Visible = true;
        _stage.Camera.MaxDistance = 30;
        _stage.Camera.SetView(0, 27, 17, new Vector3(4f, 0.95f, 0));
        UpdateOverviewStatus();
    }

    private void FocusSelected()
    {
        _overview = false;
        _grid.Visible = false;
        _stage.Presenter.Visible = true;
        _stage.Camera.MaxDistance = 12;
        var descriptor = CurrentDescriptor();
        var support = ShowDescriptor(descriptor);
        _status.Text = $"{descriptor.DisplayLabel}\n{DescribeSupport(support)}\n" +
                       "Compare the body shape, coat color, and deterministic white markings. This is an art-test fixture, not a player horse, save, or offspring.";
    }

    private VisualDescriptor CurrentDescriptor()
    {
        var preset = Presets[Math.Clamp(_preset.Selected, 0, Presets.Length - 1)];
        return FixtureLibrary.GetAppearanceStudio(_coat.Selected == 1 ? preset.Second : preset.First);
    }

    private void UpdateOverviewStatus()
    {
        var descriptor = CurrentDescriptor();
        var support = _overviewSupports.TryGetValue(descriptor.FixtureId, out var known) ? known : new VisualSupport(false, new[] { "overview support was not recorded" }, Array.Empty<string>());
        _status.Text = $"Eight art-test horses across four body profiles and eight supported coat/marking combinations.\n" +
                       $"Selected for close inspection: {descriptor.DisplayLabel}.\n" +
                       $"{_overviewSupportSummary}\n{DescribeSupport(support)}\n" +
                       "These examples are not player horses, saves, or offspring.";
    }

    private string SummarizeOverviewSupport(IReadOnlyList<VisualDescriptor> descriptors)
    {
        var failed = descriptors.Where(d => !_overviewSupports[d.FixtureId].Supported)
            .Select(d => $"{d.DisplayLabel}: {DescribeSupport(_overviewSupports[d.FixtureId])}").ToArray();
        if (failed.Length > 0) return $"Preview warning: {failed.Length} of {descriptors.Count} examples cannot be rendered. " + string.Join(" ", failed);

        var limitations = descriptors.SelectMany(d => _overviewSupports[d.FixtureId].Limitations
                .Where(limit => !string.Equals(limit, "test-only descriptor", StringComparison.Ordinal))
                .Select(limit => FriendlyLimitation(limit)))
            .Distinct(StringComparer.Ordinal).ToArray();
        return limitations.Length == 0
            ? $"Overview support: all {descriptors.Count} examples render with their declared appearance controls."
            : $"Overview support: all {descriptors.Count} examples render. Limitations: {string.Join(" ", limitations)}";
    }

    private static string DescribeSupport(VisualSupport support)
    {
        if (!support.Supported)
            return "Cannot preview: " + string.Join(" ", support.Problems.Select(FriendlyLimitation));
        var limitations = support.Limitations.Where(limit => !string.Equals(limit, "test-only descriptor", StringComparison.Ordinal))
            .Select(FriendlyLimitation).Distinct(StringComparer.Ordinal).ToArray();
        return limitations.Length == 0 ? "Visual support: all listed appearance controls are applied."
            : "Visual limitations: " + string.Join(" ", limitations);
    }

    private static string FriendlyLimitation(string limitation)
    {
        if (limitation.StartsWith("morphs not applied:", StringComparison.Ordinal))
        {
            var morphs = limitation[(limitation.IndexOf(':') + 1)..];
            var notes = new List<string>();
            if (morphs.Contains("mane_density", StringComparison.Ordinal) || morphs.Contains("tail_density", StringComparison.Ordinal))
                notes.Add("Mane and tail fullness controls have no visual effect.");
            if (morphs.Contains("body_length", StringComparison.Ordinal) || morphs.Contains("chest_width", StringComparison.Ordinal) ||
                morphs.Contains("neck_length", StringComparison.Ordinal) || morphs.Contains("muscle", StringComparison.Ordinal))
                notes.Add("Some body-shape controls are unavailable for this age.");
            return notes.Count == 0 ? "Some appearance controls have no visual effect." : string.Join(" ", notes);
        }
        if (limitation.StartsWith("adult mesh is missing required body blend shapes:", StringComparison.Ordinal))
            return "The adult model is missing a required body-shape control.";
        if (limitation.StartsWith("adult mesh is missing declared component blend shapes:", StringComparison.Ordinal))
            return "The adult model is missing required shape controls on one or more attached parts.";
        return limitation;
    }

    private VisualSupport ShowDescriptor(VisualDescriptor descriptor)
    {
        var support = _stage.Presenter.Present(descriptor, "");
        _stage.FrameView("q3");
        return support;
    }

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }
}
