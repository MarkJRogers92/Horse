using System;
using System.IO;
using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// First-session guide (P2-08 slice), voiced by Pip the rabbit. Each step points at the next real action and
/// completes from saved progress, so it never fakes or skips simulation rules. Skipping is a per-slot UI
/// preference stored beside the save, not in the authoritative save.
/// </summary>
public partial class StableDay
{
    private const string GuideFileName = "guide.cfg";
    private const int GuideSteps = 6;
    private Label _dailyNote = null!;
    private Control _guideRow = null!;
    private BunnySpeaker _guideSpeaker = null!;
    private int _guideStep;
    private bool _guideDismissed;
    private bool _guideSawDetails;
    private bool _guideSawCareChoice;

    private Control BuildGuidePanel()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var stack = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; panel.AddChild(stack);
        _dailyNote = CozyTheme.Title("Daily basics are covered for active-stable horses.", 17);
        stack.AddChild(_dailyNote);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); _guideRow = row; stack.AddChild(row);
        _guideSpeaker = new BunnySpeaker { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(_guideSpeaker);
        var skip = new Button { Text = "Skip", CustomMinimumSize = new Vector2(84, 38), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "Pip will stop giving tips for this stable." };
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
            if (config.Save(GuidePath) != Error.Ok) SetStatus("Pip is resting for now; that setting could not be saved.");
        }
        catch (Exception) { SetStatus("Pip is resting for now; that setting could not be saved."); }
        RefreshGuide();
    }

    /// <summary>The current guide step (1-based) and Pip's line, or (0, null) when finished or skipped.</summary>
    private (int Step, string? Line) GuideLine()
    {
        if (_guideDismissed || _state == null) return (0, null);
        var firstDay = _state.Header.CalendarDay == 0;
        if (firstDay && !_guideSawDetails)
            return (1, "Hi, I'm Pip! Welcome to your stable. Press Details on a horse to meet them properly.");
        if (firstDay && !_guideSawCareChoice)
            return (2, "Feed and bedding are always done. Switch on turnout or grooming to keep someone extra comfy!");
        if (firstDay)
            return (3, "When you're happy, press End the day and check the plan. The riding club pays 50 coins every day.");
        var bred = _state.Horses.Where(h => h.OriginCommitId != null).ToArray();
        if (bred.Length == 0)
            return (4, "Fancy a foal? Pick a mare and a stallion, then Review pairing. You'll see the fee first.");
        if (bred.All(h => h.LifeState.Stage == LifeStage.Unborn))
            return (5, "Keep ending days. The foal arrives on its due day; the mare's card counts down.");
        if (bred.Any(h => h.LifeState.Stage != LifeStage.Unborn && h.Name == "New foal"))
            return (6, "A foal! Give them a name under Your horses. Later, Practice gallop trains adults, and the club buys horses you bred.");
        return (0, null);
    }

    private void RefreshGuide()
    {
        if (!GodotObject.IsInstanceValid(_guideRow)) return;
        var (step, line) = GuideLine();
        _guideStep = step;
        _guideRow.Visible = line != null;
        _dailyNote.Visible = line == null;
        if (line != null) _guideSpeaker.Say(line);
    }
}
