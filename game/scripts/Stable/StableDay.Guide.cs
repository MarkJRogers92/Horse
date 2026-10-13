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
        _dailyNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dailyNote.CustomMinimumSize = new Vector2(380, 0);
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
        if (firstDay && !_guideSawDetails && !_state.Horses.Any(h => HorseBond.Bond(h) > 0))
            return (1, "Hi, I'm Pip! Welcome to your stable. Click a horse in the yard to say hello. Try a pat or a carrot!");
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
        else _dailyNote.Text = DailyTip();
        RefreshDailyTasks();
    }

    /// <summary>After the guide: one useful, state-driven suggestion for today (presentation only).</summary>
    private string DailyTip()
    {
        if (_state == null) return "Daily basics are covered for active-stable horses.";
        var day = _state.Header.CalendarDay;
        var home = _state.Horses.Where(h => h.OwnerId.Equals(_state.PlayerOwnerId) && !HorseMarket.IsSold(h)
            && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn).OrderBy(h => h.Name, StringComparer.Ordinal).ToList();
        if (home.FirstOrDefault(h => h.OriginCommitId != null && h.Name == "New foal") is { } foal)
            return "Tip: your new foal still needs a name. Type one under Your horses.";
        foreach (var h in home)
        {
            var care = _state.DailyState.For(h.HorseId);
            var plan = _optionalCare.TryGetValue(h.HorseId, out var o) ? o : (false, false);
            if (care.Grooming < 4000 && !plan.Item2) return $"Tip: {h.Name} would love some grooming today.";
            if (care.Turnout < 4000 && !plan.Item1) return $"Tip: {h.Name} is longing for turnout in the paddock.";
        }
        if (home.FirstOrDefault(h => HorseBond.Interactions.All(k => !HorseBond.DoneToday(h, k, day))) is { } lonely)
            return $"Tip: click {lonely.Name} in the yard to give a pat, a brush or a carrot. It builds your bond!";
        var gallopReady = PracticeGallop.Eligibility(_state).Where(e => e.Eligible)
            .Select(e => home.FirstOrDefault(h => h.HorseId.Equals(e.HorseId))).Where(h => h != null && !PracticeTraining.TrainedToday(h, day)).ToList();
        if (!_foalingDemoMode && !ClubRace.EnteredThisWeek(_state) && home.Any(h => ClubRace.BlockReason(_state, h.HorseId) == null)
            && HorseFair.DaysUntilNextFair(day) <= 3)
            return $"Tip: the {ClubRace.Name} closes in {Days(HorseFair.DaysUntilNextFair(day))}. Enter a horse from Practice gallop!";
        if (!_foalingDemoMode && day % HorseFair.DaysPerWeek == 1 && !HorseShow.EnteredThisWeek(_state) && _state.Coins >= HorseShow.EntryFee
            && home.Any(h => HorseShow.IsEligibleHorse(_state, h)))
            return $"Tip: the {HorseShow.Name} is on this week. Groom and bond with a horse, then enter it from Horse show!";
        if (gallopReady.Count > 0) return $"Tip: {gallopReady[0]!.Name} hasn't galloped today. A practice gallop builds training.";
        var pregnant = home.FirstOrDefault(h => h.ReproductionState.PregnancyId != null);
        if (pregnant != null) return $"Tip: {pregnant.Name} is expecting. Keep her comfy until the foal arrives!";
        bool Ready(HorseRecord h) => h.LifeState.Stage == LifeStage.Adult && h.ReproductionState.PregnancyId == null && h.ReproductionState.RecoveryUntilDay <= day;
        var mare = home.FirstOrDefault(h => h.ReproductionState.Role == ReproductiveRole.Mare && Ready(h));
        var stallion = home.FirstOrDefault(h => h.ReproductionState.Role == ReproductiveRole.Stallion && Ready(h));
        if (mare != null && stallion != null && _state.Coins >= 50) return $"Tip: {mare.Name} and {stallion.Name} could have a foal together. Review the pairing to see.";
        if (!_foalingDemoMode && _state.Coins >= DnaTest.Price && home.Count(h => h.LifeState.Stage == LifeStage.Adult) >= 2 && DnaTest.TestedCount(_state) == 0)
            return $"Tip: the vet's DNA test ({DnaTest.Price} coins, under Details) shows a horse's true colour genes and makes foal odds exact.";
        if (!_foalingDemoMode && day % HorseFair.DaysPerWeek == 0 && HorseFair.Offers(_state).FirstOrDefault(o => HorseFair.BlockReason(_state, o) == null) is { } offer)
            return $"Tip: it's fair week! {offer.Name} and friends are waiting at the Horse fair.";
        if (!_foalingDemoMode && _state.Coins >= 300 && StableShop.OwnedDecor(_state).Count == 0)
            return "Tip: the Shop sells flower carts, benches and more to decorate your yard.";
        return "Everyone is fed and happy. End the day whenever you're ready!";
    }
}
