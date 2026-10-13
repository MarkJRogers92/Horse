using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// The weekly Cloverbrook Coat and Turnout Show (D-029). Judged on looks and care, not speed. The result is decided
/// and saved (fee and prize together) before it is shown, so closing the dialog can never change it.
/// </summary>
public partial class StableDay
{
    private Control _showOverlay = null!;
    private VBoxContainer _showContent = null!;
    private Button _showButton = null!;
    private Button _showEnter = null!;
    private Button _showClose = null!;
    private ButtonGroup? _showGroup;
    private readonly Dictionary<HorseId, CheckBox> _showChoices = new();
    private VBoxContainer? _showCard;
    private Label? _showTip;

    private Button ShowButton()
    {
        _showButton = CozyTheme.WithIcon(new Button { Text = "Horse show", CustomMinimumSize = new Vector2(170, 50) }, "grooming", 32);
        _showButton.Pressed += OpenShow;
        return _showButton;
    }

    private void BuildShowOverlay(Control root)
    {
        _showOverlay = CozyTheme.Dialog(root, HorseShow.Name, 880, out var body, out var buttons, out _);
        CozyTheme.DialogHeader(body, "fair", 836, 80);
        _showContent = new VBoxContainer(); _showContent.AddThemeConstantOverride("separation", 8);
        body.AddChild(_showContent);
        _showClose = new Button { Text = "Close", CustomMinimumSize = new Vector2(130, 46) };
        _showClose.Pressed += () => _showOverlay.Visible = false;
        buttons.AddChild(_showClose);
        _showEnter = CozyTheme.WithIcon(new Button { Text = $"Enter · {HorseShow.EntryFee} coins", CustomMinimumSize = new Vector2(220, 46) }, "coins", 26);
        _showEnter.Pressed += () => _ = EnterShowAsync();
        buttons.AddChild(_showEnter);
        CozyTheme.FinishDialog(body, buttons);
    }

    private IEnumerable<HorseRecord> ShowCandidates() => _state == null ? Enumerable.Empty<HorseRecord>()
        : _state.Horses.Where(h => HorseShow.IsEligibleHorse(_state, h)).OrderBy(h => h.Name, StringComparer.Ordinal);

    private void OpenShow()
    {
        if (_state == null) return;
        FillShow(null);
        _showOverlay.Visible = true;
    }

    private static Label SmallLabel(string text, int size = 12, bool soft = true)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", size);
        if (soft) l.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        return l;
    }

    /// <summary>Builds the dialog body. With an outcome it shows the committed results instead of the picker.</summary>
    private void FillShow(HorseShowOutcome? outcome)
    {
        if (_state == null) return;
        _showContent.QueueFreeChildren(); _showChoices.Clear(); _showCard = null; _showTip = null;
        var days = HorseFair.DaysUntilNextFair(_state.Header.CalendarDay);
        var prizes = $"1st {HorseShow.PrizeFor(1)} · 2nd {HorseShow.PrizeFor(2)} · 3rd {HorseShow.PrizeFor(3)} coins";
        var won = HorseShow.PrizeThisWeek(_state);

        if (outcome != null) { _showContent.AddChild(ResultBanner(outcome)); }
        else if (won != null)
            _showContent.AddChild(SmallLabel((won > 0 ? $"You won {won} coins at this week's show!" : "You showed a horse this week. Better luck next time!")
                + $" The next show opens in {Days(days)}.", 15, false));
        else
            _showContent.AddChild(SmallLabel($"Show one adult horse to five visiting rivals. The judge looks at coat, grooming, bond and turnout. Prizes: {prizes}. "
                + $"Entry {HorseShow.EntryFee} coins, one horse a week (the show closes in {Days(days)}).", 14, false));

        if (outcome != null) _showContent.AddChild(ResultsTable(outcome));
        else if (won == null) FillShowPicker();

        _showContent.AddChild(SmallLabel("The visiting rivals", 13, false));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        foreach (var rival in HorseShow.RivalsThisWeek(_state))
        {
            var card = new VBoxContainer(); card.AddThemeConstantOverride("separation", 0);
            var portrait = Portrait(rival.Horse, 72, Color.FromHtml("#efe7f7")); portrait.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            card.AddChild(portrait);
            var name = CozyTheme.Title(rival.Horse.Name, 13); name.HorizontalAlignment = HorizontalAlignment.Center; card.AddChild(name);
            var coat = SmallLabel(rival.CoatLabel, 10); coat.HorizontalAlignment = HorizontalAlignment.Center; coat.CustomMinimumSize = new Vector2(140, 0); card.AddChild(coat);
            var total = CozyTheme.Title($"{rival.Score.Total} / 100", 13); total.HorizontalAlignment = HorizontalAlignment.Center;
            total.AddThemeColorOverride("font_color", CozyTheme.AccentDeep); card.AddChild(total);
            row.AddChild(card);
        }
        _showContent.AddChild(row);

        var open = outcome == null && won == null && _showChoices.Count > 0;
        _showEnter.Visible = outcome == null && won == null;
        _showEnter.Disabled = !open || _busy || _recoveryRequired;
        if (open) SelectShowHorse();
    }

    private void FillShowPicker()
    {
        if (_state == null) return;
        _showContent.AddChild(SmallLabel("Choose a horse to show", 13, false));
        var flow = new HFlowContainer(); flow.AddThemeConstantOverride("h_separation", 12);
        _showGroup = new ButtonGroup();
        var first = true;
        foreach (var horse in ShowCandidates())
        {
            var block = HorseShow.BlockReason(_state, horse.HorseId);
            var choice = new CheckBox
            {
                Text = horse.Name + (block != null ? " · " + block : $" · {HorseShow.ScoreHorse(_state, horse.HorseId).Total}/100"),
                Disabled = block != null, ButtonGroup = _showGroup, ButtonPressed = block == null && first,
            };
            if (DescriptorFor(horse) is { } look)
            {
                if (_portraits.TryGetCached(look, out var ready) && ready != null) choice.Icon = ready;
                else _ = SetIconAsync(choice, look);
                choice.AddThemeConstantOverride("icon_max_width", 44);
            }
            if (block == null) { _showChoices[horse.HorseId] = choice; first = false; choice.Pressed += SelectShowHorse; }
            flow.AddChild(choice);
        }
        _showContent.AddChild(flow);
        if (_showChoices.Count == 0) _showContent.AddChild(SmallLabel("No adult horse can be shown today.", 14, false));
        _showCard = new VBoxContainer(); _showCard.AddThemeConstantOverride("separation", 3);
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", CozyTheme.Box(Colors.White, 16, 8));
        panel.AddChild(_showCard);
        _showContent.AddChild(panel);
        _showTip = SmallLabel("", 13);
        _showContent.AddChild(_showTip);
    }

    private HorseId? PickedShowHorse() => _showChoices.Where(p => p.Value.ButtonPressed).Select(p => (HorseId?)p.Key).FirstOrDefault();

    /// <summary>Refreshes the judge's card and tips for the selected horse.</summary>
    private void SelectShowHorse()
    {
        if (_state == null || _showCard == null || PickedShowHorse() is not { } id) return;
        _showCard.QueueFreeChildren();
        var horse = _state.Horses.Single(h => h.HorseId.Equals(id));
        var score = HorseShow.ScoreHorse(_state, id);
        _showCard.AddChild(CozyTheme.Title($"The judge's card · {horse.Name}", 16));
        _showCard.AddChild(ScoreRow("Coat", score.Coat, 40));
        _showCard.AddChild(ScoreRow("Grooming", score.Grooming, 25));
        _showCard.AddChild(ScoreRow("Bond", score.Bond, 15));
        _showCard.AddChild(ScoreRow("Turnout & tack", score.Turnout, 15));
        _showCard.AddChild(ScoreRow("Judge", score.Judge, 5));
        var preview = HorseShow.Preview(_state, id);
        var total = CozyTheme.Title($"Total {score.Total} / 100 · would place {Ordinal(preview.Place)} today", 15);
        total.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        _showCard.AddChild(total);
        if (_showTip != null) _showTip.Text = string.Join("\n", ShowTips(horse, score));
    }

    private static Control ScoreRow(string label, int value, int max)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(130, 0) });
        var bar = new ProgressBar { MinValue = 0, MaxValue = max, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(360, 18), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(bar);
        var number = new Label { Text = $"{value} / {max}", CustomMinimumSize = new Vector2(70, 0) };
        number.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        row.AddChild(number);
        return row;
    }

    private IEnumerable<string> ShowTips(HorseRecord horse, HorseShowScore score)
    {
        var tips = new List<string>();
        if (score.Grooming < 20) tips.Add($"Tick Grooming for {horse.Name} in today's care and end the day to earn more grooming points.");
        if (score.Turnout < 9) tips.Add($"Turnout in the paddock and a saddle pad and bow (Details, Dress up) lift the turnout points.");
        if (score.Bond < 15) tips.Add($"Click {horse.Name} in the yard for a pat, a brush or a carrot to grow the bond.");
        if (tips.Count == 0) tips.Add($"{horse.Name} is looking show-ready!");
        return tips.Select(t => "• " + t);
    }

    private Control ResultBanner(HorseShowOutcome outcome)
    {
        var (headline, tint) = outcome.Place switch
        {
            1 => ("Blue ribbon! First place!", "#a8cdf0"),
            2 => ("Red ribbon! Second place!", "#f3a6a6"),
            3 => ("Yellow ribbon! Third place!", "#f7dc7a"),
            _ => ($"{Ordinal(outcome.Place)} place. A lovely day out!", "#e6e0d8"),
        };
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", CozyTheme.Box(Color.FromHtml(tint), 24, 10));
        var box = new VBoxContainer(); panel.AddChild(box);
        var title = CozyTheme.Title(headline, 24); title.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(title);
        var name = _state?.Horses.SingleOrDefault(h => h.HorseId.Equals(outcome.EntrantId))?.Name ?? "Your horse";
        var line = SmallLabel(outcome.PrizeCoins > 0 ? $"{name} scored {outcome.Score.Total} and won {outcome.PrizeCoins} coins!"
            : $"{name} scored {outcome.Score.Total}. Groom, bond and dress up for a better card next week.", 14, false);
        line.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(line);
        return panel;
    }

    private static Control ResultsTable(HorseShowOutcome outcome)
    {
        var grid = new GridContainer { Columns = 7 };
        grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 2);
        foreach (var head in new[] { "", "Horse", "Coat", "Groom", "Bond", "Turnout", "Total" }) { var h = SmallLabel(head, 12); grid.AddChild(h); }
        foreach (var p in outcome.Placings)
        {
            var colour = p.IsPlayer ? CozyTheme.AccentDeep : CozyTheme.Text;
            foreach (var cell in new[] { Ordinal(p.Place), p.Name + (p.IsPlayer ? " ♥" : ""), p.Score.Coat.ToString(), p.Score.Grooming.ToString(), p.Score.Bond.ToString(),
                         p.Score.Turnout.ToString(), p.Score.Total.ToString() })
            {
                var l = new Label { Text = cell }; l.AddThemeColorOverride("font_color", colour); grid.AddChild(l);
            }
        }
        return grid;
    }

    private async Task EnterShowAsync()
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        if (PickedShowHorse() is not { } horseId) { SetStatus("Choose one horse to show."); return; }
        _busy = true; _showEnter.Disabled = true;
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision;
            var (state, outcome) = await Task.Run(() => _store.CommitShowEntry(owner, horseId, revision, HorseShow.EntryFee));
            _state = state;
            if (outcome == null) { SetStatus("This week's show has already been entered."); FillShow(null); return; }
            FillShow(outcome);
            var name = state.Horses.Single(h => h.HorseId.Equals(horseId)).Name;
            Audio.GameAudio.Play("ribbon");
            if (outcome.Place == 1) { Audio.GameAudio.Play("birth_fanfare", -6); _yard.Cheer(horseId.ToString()); }
            else if (outcome.PrizeCoins > 0) Audio.GameAudio.Play("coins", -4);
            SetStatus(outcome.Place switch
            {
                1 => $"Blue ribbon! {name} won the {HorseShow.Name} and {outcome.PrizeCoins} coins!",
                2 or 3 => $"{name} placed {Ordinal(outcome.Place)} and won {outcome.PrizeCoins} coins!",
                _ => $"{name} placed {Ordinal(outcome.Place)} at the show. Keep grooming for next week!",
            });
        }
        catch (Exception ex)
        {
            SetStatus("The show entry was not saved. " + PlainError(ex));
            _showEnter.Disabled = false;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }
}
