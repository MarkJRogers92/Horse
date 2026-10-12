using System;
using System.IO;
using System.Linq;
using Cloverbrook.Domain;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// First-session stable guide (P2-08 slice). Each step points at the next real action and completes from
/// saved progress, so it never fakes or skips simulation rules. Skipping is a per-slot UI preference stored
/// beside the save, not in the authoritative save.
/// </summary>
public partial class StableDay
{
    private const string GuideFileName = "guide.cfg";
    private Label _dailyNote = null!;
    private Control _guideRow = null!;
    private Label _guideText = null!;
    private bool _guideDismissed;
    private bool _guideSawDetails;
    private bool _guideSawCareChoice;

    private Control BuildGuidePanel()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var stack = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; panel.AddChild(stack);
        _dailyNote = Cloverbrook.Game.UI.CozyTheme.Title("Daily basics are covered for active-stable horses.", 17);
        stack.AddChild(_dailyNote);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); _guideRow = row; stack.AddChild(row);
        _guideText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(420, 0) };
        _guideText.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(_guideText);
        var skip = new Button { Text = "Skip guide", CustomMinimumSize = new Vector2(110, 36), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        skip.Pressed += DismissGuide;
        row.AddChild(skip);
        return panel;
    }

    private string GuidePath => Path.Combine(Path.GetDirectoryName(_store.SavePath) ?? ".", GuideFileName);

    private void LoadGuidePreference()
    {
        _guideDismissed = _foalingDemoMode;
        if (_guideDismissed) return;
        var config = new ConfigFile();
        if (config.Load(GuidePath) == Error.Ok) _guideDismissed = (bool)config.GetValue("guide", "dismissed", false);
    }

    private void DismissGuide()
    {
        _guideDismissed = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GuidePath) ?? ".");
            var config = new ConfigFile(); config.SetValue("guide", "dismissed", true);
            if (config.Save(GuidePath) != Error.Ok) SetStatus("The guide is hidden for now; its setting could not be saved.");
        }
        catch (Exception) { SetStatus("The guide is hidden for now; its setting could not be saved."); }
        RefreshGuide();
    }

    /// <summary>The current guide step, or null when the guide is finished or skipped.</summary>
    private string? GuideStep()
    {
        if (_guideDismissed || _state == null) return null;
        var firstDay = _state.Header.CalendarDay == 0;
        if (firstDay && !_guideSawDetails)
            return "Guide 1 of 5 · Meet your horses. Press Details on a horse to see its coat, family and care.";
        if (firstDay && !_guideSawCareChoice)
            return "Guide 2 of 5 · Plan today's care. Feed and bedding are automatic; turn on turnout or grooming to keep a horse comfortable.";
        if (firstDay)
            return "Guide 3 of 5 · End the day. Press Review day to check costs and the 50-coin club support, then End day.";
        var bredFoals = _state.Horses.Where(h => h.OriginCommitId != null && h.OwnerId.Equals(_state.PlayerOwnerId)).ToArray();
        if (bredFoals.Length == 0)
            return "Guide 4 of 5 · Try breeding. Pick a mare and a stallion, then Review pairing. You see the fee and due day before anything is saved.";
        if (bredFoals.All(h => h.LifeState.Stage == LifeStage.Unborn))
            return "Guide 5 of 5 · Keep ending days. The foal is born on its due day; the mare's card counts down.";
        if (bredFoals.Any(h => h.LifeState.Stage != LifeStage.Unborn && h.Name == "New foal"))
            return "Your foal has arrived! Give it a name under Your horses. That finishes the guide.";
        return null;
    }

    private void RefreshGuide()
    {
        if (!GodotObject.IsInstanceValid(_guideRow)) return;
        var step = GuideStep();
        _guideRow.Visible = step != null;
        _dailyNote.Visible = step == null;
        _guideText.Text = step ?? "";
    }
}
