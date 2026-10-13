using System;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// The weekly Willowbrook Cup (D-019), shown as the second tab of the gallop dialog. The race is decided and saved
/// (fee, prize and training) before it plays, so closing the dialog mid-race can never change the result.
/// </summary>
public partial class StableDay
{
    private HBoxContainer? _cupRivalRow;
    private ClubRaceOutcome? _cupOutcome;
    private ButtonGroup? _cupPickGroup;

    private void FillCupPicker()
    {
        if (_state == null) return;
        var prizes = $"1st {ClubRace.PrizeFor(1)} · 2nd {ClubRace.PrizeFor(2)} · 3rd {ClubRace.PrizeFor(3)} coins";
        var days = HorseFair.DaysUntilNextFair(_state.Header.CalendarDay);
        var won = ClubRace.PrizeThisWeek(_state);
        if (won != null)
        {
            _practiceIntro.Text = (won > 0 ? $"You won {won} coins in this week's cup!" : "You raced in this week's cup. Better luck next time!")
                + $" The next cup opens in {Days(days)}. Keep galloping to train for it.";
            _practiceStart.Disabled = true;
            _practiceOverlay.Visible = true;
            return;
        }
        _practiceIntro.Text = $"Race five visiting horses over {PracticeGallop.DistanceMeters} m. Prizes: {prizes}. "
            + $"Entry {ClubRace.EntryFee} coins, one horse per week (the cup closes in {Days(days)}). Racing counts as today's training.";

        // The visiting field, with their portraits and home stables.
        _cupRivalRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _cupRivalRow.AddThemeConstantOverride("separation", 8);
        foreach (var rival in ClubRace.RivalsThisWeek(_state))
        {
            _raceHorses[rival.Horse.HorseId] = rival.Horse;
            var card = new VBoxContainer(); card.AddThemeConstantOverride("separation", 0);
            var portrait = Portrait(rival.Horse, 72, Color.FromHtml("#efe7f7")); portrait.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            card.AddChild(portrait);
            var name = CozyTheme.Title(rival.Horse.Name, 13); name.HorizontalAlignment = HorizontalAlignment.Center; card.AddChild(name);
            var home = new Label { Text = rival.StableName, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(100, 0) };
            home.AddThemeFontSizeOverride("font_size", 10); home.AddThemeColorOverride("font_color", CozyTheme.SoftText);
            card.AddChild(home);
            _cupRivalRow.AddChild(card);
        }
        _practiceIntro.GetParent().AddChild(_cupRivalRow);
        _practiceIntro.GetParent().MoveChild(_cupRivalRow, _practiceIntro.GetIndex() + 1);

        // Exactly one of your horses runs.
        _cupPickGroup = new ButtonGroup();
        var first = true;
        foreach (var horse in _state.Horses.Where(h => h.OwnerId.Equals(_state.PlayerOwnerId) && !HorseMarket.IsSold(h) && h.LifeState.Stage == LifeStage.Adult)
                     .OrderByDescending(PracticeTraining.Skill).ThenBy(h => h.Name, StringComparer.Ordinal))
        {
            var block = ClubRace.BlockReason(_state, horse.HorseId);
            var skill = PracticeTraining.Skill(horse);
            var choice = new CheckBox
            {
                Text = horse.Name + (block != null ? $" · {block}" : $" · training {Percent(skill)}"),
                Disabled = block != null, ButtonGroup = _cupPickGroup, ButtonPressed = block == null && first,
            };
            if (DescriptorFor(horse) is { } look)
            {
                if (_portraits.TryGetCached(look, out var ready) && ready != null) choice.Icon = ready;
                else _ = SetIconAsync(choice, look);
                choice.AddThemeConstantOverride("icon_max_width", 36);
            }
            if (block == null) { _practiceChoices[horse.HorseId] = choice; first = false; }
            _practicePicks.AddChild(choice);
        }
        _practiceStart.Disabled = _practiceChoices.Count == 0;
        if (_practiceChoices.Count == 0) _practiceResults.Text = "No adult horse can race today.";
    }

    private async Task StartCupAsync()
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        var picked = _practiceChoices.Where(p => p.Value.ButtonPressed).Select(p => p.Key).ToArray();
        if (picked.Length != 1) { _practiceResults.Text = "Choose one horse to race."; return; }
        _busy = true; _practiceStart.Disabled = true;
        var bought = _state;
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision; var horseId = picked[0];
            var (state, outcome) = await Task.Run(() => _store.CommitClubRace(owner, horseId, revision));
            _state = state;
            if (outcome == null) { _practiceResults.Text = "This week's cup has already been raced."; return; }
            _cupOutcome = outcome;
            _practiceResult = outcome.Race;
            _practiceGains = new() { [horseId] = PracticeTraining.Skill(state.Horses.Single(h => h.HorseId.Equals(horseId)))
                - PracticeTraining.Skill(bought.Horses.Single(h => h.HorseId.Equals(horseId))) };
            BeginRacePlayback(outcome.Race);
            _practiceResults.Text = $"The {ClubRace.Name} is underway!";
            Audio.GameAudio.Play("race_bell", -4);
            Audio.GameAudio.Loop("hooves_gallop");
            _practiceElapsedMs = 0;
        }
        catch (Exception ex)
        {
            _practiceResults.Text = "The cup entry was not saved. " + PlainError(ex);
            _practiceStart.Disabled = false;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void FinishCupPlayback(PracticeGallopResult result)
    {
        var outcome = _cupOutcome;
        _practiceStart.Disabled = true; _practiceStart.Text = "See you next week";
        var shown = result.Entrants.OrderBy(e => e.Place).Where(e => e.Place <= 3 || e.HorseId.Equals(outcome?.EntrantId));
        var lines = shown.Select(e =>
        {
            var horse = RaceHorse(e.HorseId);
            var mine = horse.OwnerId.Equals(_state?.PlayerOwnerId);
            return $"{Ordinal(e.Place)} · {e.Name}{(mine ? " ♥" : "")} · {e.FinishMilliseconds / 1000.0:0.0} s";
        }).ToList();
        if (outcome != null)
        {
            var name = RaceHorse(outcome.EntrantId).Name;
            lines.Insert(0, outcome.PrizeCoins > 0
                ? $"{name} finished {Ordinal(outcome.Place)} and won {outcome.PrizeCoins} coins!"
                : $"{name} finished {Ordinal(outcome.Place)}. A good run; keep training for next week's cup.");
            lines.Insert(1, "");
            if (outcome.Place == 1) { Audio.GameAudio.Play("birth_fanfare", -6); _yard.Cheer(outcome.EntrantId.ToString()); }
            else if (outcome.PrizeCoins > 0) Audio.GameAudio.Play("coins", -4);
            SetStatus(lines[0]);
        }
        _practiceResults.Text = string.Join("\n", lines);
    }
}
