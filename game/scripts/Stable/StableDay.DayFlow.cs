using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>Day review, End Day commit, recovery, reload and foal naming.</summary>
public partial class StableDay
{
    private CarePlan CurrentPlan() => new CarePlan(_state!.Horses
        .Where(h => h.OwnerId.Equals(_state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn).Select(h =>
    {
        (bool Turnout, bool Grooming) option = _optionalCare.TryGetValue(h.HorseId, out var care) ? care : (false, false);
        return new HorseCareAssignment(h.HorseId, feed: true, bedding: true, turnout: option.Turnout, grooming: option.Grooming);
    }));

    private void ShowReview()
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        try
        {
            _reviewedPlan = CurrentPlan();
            _preview = _service.Preview(_state, _reviewedPlan);
            _reviewedRevision = _state.Header.SaveRevision;
            _reviewedDay = _state.Header.CalendarDay;
            var lines = new List<string>
            {
                $"{StableYard.SeasonName(_state.Header.CalendarDay / StableCalendar.DaysPerSeason)} day {_state.Header.CalendarDay % StableCalendar.DaysPerSeason + 1} → {StableYard.SeasonName(_preview.ToDay / StableCalendar.DaysPerSeason)} day {_preview.ToDay % StableCalendar.DaysPerSeason + 1}.",
                $"Club support: +{_preview.IncomeCoins:N0} coins.",
                $"Daily care: {_preview.CareCostCoins:N0} coins · stable operations: {_preview.OperationsCostCoins:N0} coins.",
                $"From your wallet: {_preview.WalletDebitCoins:N0} coins · assistance: {_preview.AssistanceCoins:N0} coins.",
                $"Wallet after the day: {_preview.ProjectedCoins:N0} coins.",
                "",
                "Care after today's plan:"
            };
            foreach (var outcome in _preview.HorseOutcomes)
            {
                var horse = _state.Horses.Single(h => h.HorseId.Equals(outcome.HorseId));
                lines.Add($"{horse.Name}: nutrition {Percent(outcome.Before.Nutrition)} → {Percent(outcome.After.Nutrition)}, bedding {Percent(outcome.Before.Bedding)} → {Percent(outcome.After.Bedding)}, turnout {Percent(outcome.Before.Turnout)} → {Percent(outcome.After.Turnout)}, grooming {Percent(outcome.Before.Grooming)} → {Percent(outcome.After.Grooming)}.");
            }
            if (_preview.LifecycleEvents.Count > 0)
            {
                lines.Add("");
                lines.Add("Expected stable milestones:");
                foreach (var item in _preview.LifecycleEvents)
                    lines.Add("• " + LifecycleText(item, _state));
            }
            if (!_preview.Allowed) lines.Add("\nThis day cannot be completed: " + (_preview.BlockReason ?? "The plan is not allowed."));
            _reviewDetails.Text = string.Join("\n", lines);
            _confirmDayButton.Disabled = !_preview.Allowed;
            _reviewOverlay.Visible = true;
        }
        catch (Exception ex)
        {
            SetStatus("The day could not be reviewed. " + PlainError(ex));
        }
    }

    private void CreatePendingRequest(CarePlan? reviewedPlan)
    {
        if (_state == null || reviewedPlan == null) return;
        _pendingRequest = new EndDayRequest(new TransactionId(Guid.NewGuid()), _state.PlayerOwnerId,
            _state.Header.SaveRevision, _state.Header.CalendarDay, reviewedPlan, _service.PolicyVersion);
    }

    private void ConfirmReviewedDay()
    {
        if (_state == null || !_reviewOverlay.Visible || _preview?.Allowed != true || _reviewedPlan == null
            || _pendingBreedingRequest != null
            || _reviewedRevision != _state.Header.SaveRevision || _reviewedDay != _state.Header.CalendarDay)
        {
            _reviewOverlay.Visible = false;
            ClearReviewedPlan();
            SetStatus("The stable changed after that review. Review the current day before ending it.");
            return;
        }
        _reviewOverlay.Visible = false;
        if (_pendingRequest == null) CreatePendingRequest(_reviewedPlan);
        ClearReviewedPlan();
        _ = CommitPendingAsync();
    }

    private void ClearReviewedPlan()
    {
        _reviewedPlan = null;
        _reviewedRevision = null;
        _reviewedDay = null;
        _preview = null;
    }

    private async Task CommitPendingAsync()
    {
        if (_pendingRequest == null || _busy || _recoveryRequired) return;
        _busy = true; Refresh();
        SetStatus("Saving this day…");
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var request = _pendingRequest;
            var result = await Task.Run(() => _store.CommitEndDay(request, _service));
            if (!result.IsDurablyCommitted) throw new IOException("The save did not confirm a durable result.");
            _state = result.State;
            _pendingRequest = null;
            ClearReviewedPlan();
            var lifecycleNotice = result.Receipt.LifecycleEvents.Count == 0 ? "" : " " + string.Join(" ", result.Receipt.LifecycleEvents.Select(item => LifecycleText(item, _state)));
            SetStatus(result.Replayed ? "That day was already saved. Your wallet and calendar are up to date." + lifecycleNotice
                : "Day saved. Your horses' care and the calendar are up to date." + lifecycleNotice);
            if (!result.Replayed)
            {
                PlayDayTransition(_state.Header.CalendarDay, result.Receipt.LifecycleEvents.Select(item => LifecycleText(item, _state)).FirstOrDefault());
                AnnounceFestival();
                if (result.Receipt.LifecycleEvents.FirstOrDefault(e => e.Kind == LifecycleEventKind.Birth) is { } birth) CelebrateBirth(birth);
            }
        }
        catch (Exception ex)
        {
            var error = ex;
            try
            {
                var loaded = _store.Load();
                _state = loaded.State;
                if (loaded.NeedsRecoveryAcknowledgment)
                {
                    _recoveryRequired = true;
                    _recoveryCandidate = loaded;
                    _recoveryDialog.DialogText = loaded.Notice + "\n\nRestore only after reviewing that this checkpoint may be one day behind.";
                    _recoveryDialog.PopupCentered();
                }
            }
            catch (Exception reloadError) { error = new AggregateException(ex, reloadError); }
            SetStatus($"The save did not confirm. Your exact day request is kept so Retry saved day cannot charge twice. {PlainError(error)}");
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void AcknowledgeRecovery()
    {
        if (_recoveryCandidate == null) return;
        try
        {
            var candidate = _recoveryCandidate.State;
            _state = _store.RecoverFromBackup(candidate.Header.SaveId, candidate.Header.SaveRevision, candidate.Header.PayloadHash).State;
            _recoveryRequired = false; _recoveryCandidate = null; _pendingRequest = null;
            EnableStarterBreedingIfSafe();
            SetStatus("The verified backup was restored. The stable is ready again.");
            Refresh();
        }
        catch (Exception ex)
        {
            SetStatus("The backup could not be restored. No new stable was created. " + PlainError(ex));
        }
    }

    private void ReloadState()
    {
        if (_busy || _pendingRequest != null || _pendingBreedingRequest != null) return;
        ClearReviewedPlan();
        try
        {
            var loaded = _store.Load();
            ApplyLoadedState(loaded);
            EnableStarterBreedingIfSafe();
            if (!loaded.NeedsRecoveryAcknowledgment) SetStatus("The current saved stable was reloaded.");
            Refresh();
        }
        catch (Exception ex)
        {
            SetStatus("The saved stable could not be reloaded. " + PlainError(ex));
        }
    }

    private async Task RenameFoalAsync(HorseId horseId, string requestedName)
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        var name = requestedName.Trim();
        if (name.Length == 0)
        {
            SetStatus("Enter a name for the foal first.");
            return;
        }
        var horse = _state.Horses.SingleOrDefault(item => item.HorseId.Equals(horseId));
        if (horse == null || !horse.OwnerId.Equals(_state.PlayerOwnerId) || horse.LifeState.Stage is LifeStage.Unborn)
        {
            SetStatus("Only a born horse in your stable can be named.");
            return;
        }
        _busy = true;
        Refresh();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var replacement = await Task.Run(() => _store.RenameHorse(_state.PlayerOwnerId, horse.HorseId,
                _state.Header.SaveRevision, horse.Revision, name));
            _state = replacement;
            SetStatus($"{name} is now recorded in the stable and pedigree.");
        }
        catch (Exception ex)
        {
            SetStatus("The foal's name was not saved. Reload the stable and try again. " + PlainError(ex));
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }
}
