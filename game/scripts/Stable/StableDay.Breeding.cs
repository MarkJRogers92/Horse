using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

public partial class StableDay
{
    private readonly StableBreedingPlanner _breedingPlanner = new();
    private ConceptionRequest? _pendingBreedingRequest;
    private StableBreedingQuote? _reviewedBreedingQuote;
    private HorseId? _reviewedMareId, _reviewedStallionId;
    private ulong? _reviewedBreedingRevision;
    private HorseId? _selectedMareId, _selectedStallionId;
    private string? _breedingUnavailableReason;
    private OptionButton _marePicker = null!, _stallionPicker = null!;
    private Button _planBreedingButton = null!, _retryBreedingButton = null!, _confirmBreedingButton = null!;
    private Label _breedingStatus = null!, _breedingDetails = null!;
    private Control _breedingOverlay = null!;
    private bool _breedingConfirmationOpen;
    private bool _refreshingBreedingPickers;
    private bool? _lastBreedingReplayed;

    private void BuildBreedingUi(VBoxContainer parent, Control root)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(0, 112) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        panel.AddChild(box);
        box.AddChild(CozyTheme.Title("Breeding", 19));
        box.AddChild(SmallText("Choose a mare and a stallion. Pairing rules are checked during review."));
        var selectors = new HBoxContainer();
        selectors.AddThemeConstantOverride("separation", 8);
        _marePicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 38), ClipText = true };
        _stallionPicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 38), ClipText = true };
        _marePicker.ItemSelected += index => OnBreedingParentSelected(_marePicker, index);
        _stallionPicker.ItemSelected += index => OnBreedingParentSelected(_stallionPicker, index);
        selectors.AddChild(_marePicker);
        selectors.AddChild(_stallionPicker);
        box.AddChild(selectors);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        _planBreedingButton = new Button { Text = "Review pairing", CustomMinimumSize = new Vector2(145, 38) };
        _planBreedingButton.Pressed += ShowBreedingReview;
        actions.AddChild(_planBreedingButton);
        _retryBreedingButton = new Button { Text = "Retry saved breeding", Visible = false, CustomMinimumSize = new Vector2(170, 38) };
        _retryBreedingButton.Pressed += () => _ = CommitPendingBreedingAsync();
        actions.AddChild(_retryBreedingButton);
        box.AddChild(actions);
        _breedingStatus = SmallText("A confirmed pairing costs 50 coins and reserves one future home place.");
        box.AddChild(_breedingStatus);
        parent.AddChild(panel);
        BuildBreedingOverlay(root);
    }

    private void BuildBreedingOverlay(Control root)
    {
        _breedingOverlay = CozyTheme.Dialog(root, "Review breeding plan", 680, out var box, out var buttons, out _);
        _breedingPair = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _breedingPair.AddThemeConstantOverride("separation", 14);
        box.AddChild(_breedingPair);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(620, 230), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _breedingDetails = new Label { CustomMinimumSize = new Vector2(600, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _breedingDetails.AddThemeFontSizeOverride("font_size", 15);
        scroll.AddChild(_breedingDetails);
        box.AddChild(scroll);
        var cancel = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(140, 46) };
        cancel.Pressed += CancelBreedingReview;
        buttons.AddChild(cancel);
        _confirmBreedingButton = new Button { Text = "Confirm · 50 coins", CustomMinimumSize = new Vector2(190, 46) };
        _confirmBreedingButton.Pressed += ConfirmBreedingReview;
        buttons.AddChild(_confirmBreedingButton);
        CozyTheme.FinishDialog(box, buttons);
    }

    private HBoxContainer _breedingPair = null!;

    /// <summary>Mare ♥ stallion = ? foal, as portrait cards across the top of the review.</summary>
    private void ShowBreedingPair(HorseId mareId, HorseId stallionId)
    {
        foreach (var child in _breedingPair.GetChildren()) child.QueueFree();
        var mare = _state!.Horses.Single(h => h.HorseId.Equals(mareId));
        var stallion = _state.Horses.Single(h => h.HorseId.Equals(stallionId));
        _breedingPair.AddChild(NamedPortrait(Portrait(mare, 140, CardTint(mare)), mare.Name));
        _breedingPair.AddChild(Symbol("♥"));
        _breedingPair.AddChild(NamedPortrait(Portrait(stallion, 140, CardTint(stallion)), stallion.Name));
        _breedingPair.AddChild(Symbol("="));
        var foal = new PanelContainer { CustomMinimumSize = new Vector2(110, 110), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var foalCard = CozyTheme.Box(Color.FromHtml("#fbe7b0"), 28, 0); foalCard.BorderColor = Colors.White; foalCard.SetBorderWidthAll(5);
        foal.AddThemeStyleboxOverride("panel", foalCard);
        var mystery = CozyTheme.Title("?", 64); mystery.HorizontalAlignment = HorizontalAlignment.Center; mystery.VerticalAlignment = VerticalAlignment.Center;
        mystery.AddThemeColorOverride("font_color", new Color(CozyTheme.AccentDeep, 0.8f));
        foal.AddChild(mystery);
        _breedingPair.AddChild(NamedPortrait(foal, "Foal · a surprise!"));
    }

    private static Control NamedPortrait(Control portrait, string name)
    {
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 2);
        portrait.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        column.AddChild(portrait);
        var label = CozyTheme.Title(name, 15); label.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(label);
        return column;
    }

    private static Label Symbol(string text)
    {
        var label = CozyTheme.Title(text, 30); label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        return label;
    }

    private void RefreshBreedingUi()
    {
        if (!GodotObject.IsInstanceValid(_marePicker)) return;
        var owned = _state == null ? Array.Empty<PublicHorseSummary>() : _breedingPlanner.GetOwnedHorses(_state)
            .Where(h => h.Home == HomeKind.ActiveStable).ToArray();
        var mares = owned.Where(h => h.Role == ReproductiveRole.Mare).ToArray();
        var stallions = owned.Where(h => h.Role == ReproductiveRole.Stallion).ToArray();
        _refreshingBreedingPickers = true;
        _marePicker.Clear();
        _stallionPicker.Clear();
        foreach (var mare in mares) _marePicker.AddItem("Mare · " + mare.Name, _marePicker.ItemCount);
        foreach (var stallion in stallions) _stallionPicker.AddItem("Stallion · " + stallion.Name, _stallionPicker.ItemCount);
        _selectedMareId = SelectId(_marePicker, mares.Select(h => h.HorseId).ToArray(), _selectedMareId);
        _selectedStallionId = SelectId(_stallionPicker, stallions.Select(h => h.HorseId).ToArray(), _selectedStallionId);
        _refreshingBreedingPickers = false;

        if (_foalingDemoMode) _breedingUnavailableReason = "Breeding is available in Manage stable; the foaling demo remains separate.";
        _planBreedingButton.Disabled = _state == null || _busy || _recoveryRequired || _foalingDemoMode
            || _pendingRequest != null || _pendingBreedingRequest != null || _breedingUnavailableReason != null
            || mares.Length == 0 || stallions.Length == 0;
        _retryBreedingButton.Visible = _pendingBreedingRequest != null;
        _retryBreedingButton.Disabled = _busy || _recoveryRequired || _pendingRequest != null;
        _marePicker.Disabled = _busy || _recoveryRequired || _foalingDemoMode || _pendingRequest != null || _pendingBreedingRequest != null;
        _stallionPicker.Disabled = _marePicker.Disabled;
        if (_breedingUnavailableReason != null) _breedingStatus.Text = _breedingUnavailableReason;
        else if (_pendingBreedingRequest != null) _breedingStatus.Text = "The last save result was uncertain. Retry the exact same pairing to learn whether it committed.";
        else if (mares.Length == 0 || stallions.Length == 0) _breedingStatus.Text = "A saved mare and stallion in the active stable are needed before pairing.";
        else _breedingStatus.Text = "Saved stable breeding · 50 coins · foal due in 4 days · 4 protected home places.";
        if (_reviewedBreedingQuote != null && _breedingConfirmationOpen)
            _confirmBreedingButton.Disabled = _state == null || _busy || _recoveryRequired || _pendingRequest != null
                || SelectedId(_marePicker) != _reviewedMareId || SelectedId(_stallionPicker) != _reviewedStallionId
                || _state.Header.SaveRevision != _reviewedBreedingRevision || !_reviewedBreedingQuote.IsEligible;
    }

    private HorseId? SelectedId(OptionButton picker)
    {
        return picker == _marePicker ? _selectedMareId : _selectedStallionId;
    }

    private static HorseId? SelectId(OptionButton picker, HorseId[] ids, HorseId? previous)
    {
        if (ids.Length == 0) return null;
        var index = previous == null ? -1 : Array.FindIndex(ids, id => id.Equals(previous));
        index = index >= 0 ? index : 0;
        picker.Select(index);
        return ids[index];
    }

    private void OnBreedingParentSelected(OptionButton picker, long index)
    {
        if (_refreshingBreedingPickers) return;
        var options = GetBreedingParentOptions(picker);
        var selected = index >= 0 && index < options.Length ? options[index].HorseId : null;
        if (picker == _marePicker) _selectedMareId = selected;
        else _selectedStallionId = selected;
        InvalidateBreedingReview();
    }

    private PublicHorseSummary[] GetBreedingParentOptions(OptionButton picker)
    {
        if (_state == null) return Array.Empty<PublicHorseSummary>();
        var role = picker == _marePicker ? ReproductiveRole.Mare : ReproductiveRole.Stallion;
        return _breedingPlanner.GetOwnedHorses(_state)
            .Where(h => h.Home == HomeKind.ActiveStable && h.Role == role).ToArray();
    }

    private void InvalidateBreedingReview()
    {
        if (!_breedingConfirmationOpen) return;
        _reviewedBreedingQuote = null;
        _breedingConfirmationOpen = false;
        _breedingOverlay.Visible = false;
        SetStatus("The selected pairing changed. Review the current horses again before confirming.");
    }

    private void ShowBreedingReview()
    {
        if (_state == null || _busy || _recoveryRequired || _foalingDemoMode || _pendingRequest != null || _pendingBreedingRequest != null) return;
        try
        {
            var mareId = SelectedId(_marePicker);
            var stallionId = SelectedId(_stallionPicker);
            if (mareId == null || stallionId == null) throw new InvalidOperationException("Choose a saved mare and stallion first.");
            _reviewedBreedingQuote = _breedingPlanner.Preview(_state, mareId, stallionId, new BreedingCommitId(Guid.NewGuid()));
            _reviewedMareId = mareId;
            _reviewedStallionId = stallionId;
            _reviewedBreedingRevision = _state.Header.SaveRevision;
            _breedingConfirmationOpen = true;
            var quote = _reviewedBreedingQuote;
            var mare = _breedingPlanner.GetOwnedHorses(_state).Single(h => h.HorseId.Equals(mareId));
            var stallion = _breedingPlanner.GetOwnedHorses(_state).Single(h => h.HorseId.Equals(stallionId));
            var lines = new System.Collections.Generic.List<string>
            {
                $"Mare: {mare.Name}",
                $"Stallion: {stallion.Name}",
                $"Fee: {quote.CostCoins:N0} coins · wallet after: {_state.Coins - quote.CostCoins:N0} coins.",
                $"Conception: calendar day {quote.ConceptionDay + 1} · due: calendar day {quote.PlannedBirthDay + 1} (4 days).",
                "Growth schedule: youngster at conception +7 days · adult at +10 days · breeding age at +12 days.",
                $"Protected home places: now {quote.CurrentCapacityUsed}/{quote.CurrentCapacity}; after this pairing {quote.FutureCapacityUsed}/{quote.FutureCapacity}.",
                "The offspring keeps one permanent home allocation through pregnancy, growth, and adulthood.",
                "",
            };
            foreach (var warning in quote.Warnings)
                lines.Add(warning.Code == "unknown_ancestry"
                    ? "Family history is incomplete. These horses may be related."
                    : "Warning: " + warning.Message);
            if (!quote.IsEligible)
            {
                lines.Add("");
                lines.Add("This pairing is blocked:");
                foreach (var rejection in quote.Rejections) lines.Add("• " + rejection.Message);
            }
            _breedingDetails.Text = string.Join("\n", lines);
            ShowBreedingPair(mareId, stallionId);
            _confirmBreedingButton.Disabled = !quote.IsEligible;
            _breedingOverlay.Visible = true;
        }
        catch (Exception ex) { SetStatus("The pairing could not be reviewed. " + PlainError(ex)); }
    }

    private void CancelBreedingReview()
    {
        _breedingOverlay.Visible = false;
        _breedingConfirmationOpen = false;
        _reviewedBreedingQuote = null;
        _reviewedBreedingRevision = null;
        SetStatus("Pairing review canceled. No fee, hold, or random outcome was saved.");
    }

    private void ConfirmBreedingReview()
    {
        if (_state == null || !_breedingOverlay.Visible || _reviewedBreedingQuote?.IsEligible != true
            || _reviewedBreedingRevision != _state.Header.SaveRevision
            || SelectedId(_marePicker) != _reviewedMareId || SelectedId(_stallionPicker) != _reviewedStallionId)
        {
            CancelBreedingReview();
            SetStatus("The stable or selected pair changed after review. Review the current pairing again.");
            return;
        }
        _pendingBreedingRequest = _reviewedBreedingQuote.Request;
        _breedingOverlay.Visible = false;
        _breedingConfirmationOpen = false;
        _reviewedBreedingQuote = null;
        _ = CommitPendingBreedingAsync();
    }

    private async Task CommitPendingBreedingAsync()
    {
        if (_pendingBreedingRequest == null || _busy || _recoveryRequired || _pendingRequest != null) return;
        _busy = true;
        Refresh();
        SetStatus("Saving this pairing and its protected home place…");
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var request = _pendingBreedingRequest;
            var result = await Task.Run(() => _store.CommitStableBreeding(request, _breedingPlanner));
            if (!result.IsDurablyCommitted) throw new IOException("The save did not confirm a durable breeding result.");
            _state = result.State;
            _pendingBreedingRequest = null;
            _lastBreedingReplayed = result.Replayed;
            var child = _breedingPlanner.GetOwnedHorses(_state).Single(h => h.HorseId.Equals(result.Receipt.Commit.OffspringHorseId));
            SetStatus(result.Replayed
                ? $"That exact pairing was already saved. {child.Name} remains the same unborn horse; no second fee was charged."
                : $"Pairing saved. {child.Name} is expected in four days; the 50-coin fee and home place are protected.");
        }
        catch (ConceptionRejectedException ex)
        {
            _pendingBreedingRequest = null;
            if (ex.Code is "stale_save" or "stale_capacity" or "stale_terms" or "quote_mismatch")
                _breedingUnavailableReason = "This pairing review became stale. Reload the stable before reviewing a new pairing.";
            SetStatus($"The pairing was not committed ({ex.Code}). Review the current save and pair again. {PlainError(ex)}");
        }
        catch (StableCommandRejectedException ex)
        {
            _pendingBreedingRequest = null;
            if (ex.Code == "stale_save")
                _breedingUnavailableReason = "The stable changed before its starter upgrade. Reload the stable before reviewing a pairing.";
            SetStatus("The starter or saved state is not eligible for this operation. Reload and review again. " + PlainError(ex));
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
                    _recoveryDialog.DialogText = loaded.Notice + "\n\nReview this verified checkpoint before retrying the exact pairing.";
                    _recoveryDialog.PopupCentered();
                }
            }
            catch (Exception reloadError) { error = new AggregateException(ex, reloadError); }
            SetStatus("The save did not confirm. The exact pairing is kept so Retry saved breeding cannot draw or charge twice. " + PlainError(error));
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private async Task RunBreedingCaptureAsync(string output)
    {
        try
        {
            if (_foalingDemoMode || _state == null || _recoveryRequired || _state.Header.CalendarDay != 0
                || _state.Coins != 500 || _state.Horses.Count != 2)
                throw new InvalidOperationException("Capture requires a fresh isolated normal starter stable with two horses and 500 coins.");
            var directory = Path.GetFullPath(output);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidOperationException("Capture output must be a new empty directory.");
            Directory.CreateDirectory(directory);
            var start = _state;
            var startHash = start.Header.PayloadHash;
            var startingSaveBytes = File.ReadAllBytes(_store.SavePath);
            var mare = _breedingPlanner.GetOwnedHorses(start).Single(h => h.Role == ReproductiveRole.Mare);
            var stallion = _breedingPlanner.GetOwnedHorses(start).Single(h => h.Role == ReproductiveRole.Stallion);
            await CaptureFrame(Path.Combine(directory, "01_initial.png"));

            // Exercise cancel through the same selectors and review handlers as a player.
            _marePicker.Select(_breedingPlanner.GetOwnedHorses(start).Where(h => h.Role == ReproductiveRole.Mare).ToArray().Length == 0 ? -1 : 0);
            _stallionPicker.Select(0);
            ShowBreedingReview();
            if (!_breedingOverlay.Visible || _reviewedBreedingQuote?.IsEligible != true)
                throw new InvalidOperationException("The normal stable UI did not produce an eligible saved-horse plan.");
            if (_reviewedBreedingQuote.CostCoins != 50 || _reviewedBreedingQuote.PlannedBirthDay != 4
                || _reviewedBreedingQuote.CurrentCapacity != 4 || _reviewedBreedingQuote.CurrentCapacityUsed != 2
                || _reviewedBreedingQuote.FutureCapacity != 4 || _reviewedBreedingQuote.FutureCapacityUsed != 3
                || !_reviewedBreedingQuote.Warnings.Any(warning => warning.Code == "unknown_ancestry"))
                throw new InvalidOperationException("The saved-horse plan did not show the fixed fee, four-day due date, protected space, or unknown-ancestry warning.");
            await CaptureFrame(Path.Combine(directory, "02_plan.png"));
            CancelBreedingReview();
            if (_state.Header.PayloadHash != startHash || _state.Coins != 500 || _state.Conceptions.Count != 0
                || _state.Header.RngStreams.Breeding.EventCounter != start.Header.RngStreams.Breeding.EventCounter
                || !File.ReadAllBytes(_store.SavePath).SequenceEqual(startingSaveBytes) || File.Exists(_store.BackupPath))
                throw new InvalidOperationException("Cancel changed the saved wallet, conception history, or breeding stream.");
            await CaptureFrame(Path.Combine(directory, "03_canceled.png"));

            ShowBreedingReview();
            if (!_breedingOverlay.Visible || _reviewedBreedingQuote?.IsEligible != true)
                throw new InvalidOperationException("The actual pairing review could not be reopened after cancel.");
            _confirmBreedingButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitForBreedingSave();
            if (_busy || _pendingBreedingRequest != null || _state == null || _state.Coins != 450 || _state.Conceptions.Count != 1)
                throw new InvalidOperationException("Confirm did not durably save exactly one 50-coin conception.");
            var committed = _state.Conceptions.Single();
            var childId = committed.Commit.OffspringHorseId;
            var breedingCounter = _state.Header.RngStreams.Breeding.EventCounter;
            _pendingBreedingRequest = committed.Request;
            _lastBreedingReplayed = null;
            Refresh();
            _retryBreedingButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitForBreedingSave();
            if (_busy || _pendingBreedingRequest != null || _lastBreedingReplayed != true || _state.Coins != 450
                || _state.Conceptions.Count != 1 || _state.Header.RngStreams.Breeding.EventCounter != breedingCounter)
                throw new InvalidOperationException("Retry saved breeding did not recognize the exact durable command without a second fee or RNG advance.");
            var reloadButton = _reloadButton;
            reloadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_state.Coins != 450 || _state.Conceptions.Count != 1
                || _state.Horses.Single(h => h.HorseId.Equals(childId)).LifeState.Stage != LifeStage.Unborn)
                throw new InvalidOperationException("Reload did not retain the same canonical unborn offspring.");
            var replay = await Task.Run(() => _store.CommitStableBreeding(committed.Request, _breedingPlanner));
            if (!replay.Replayed || replay.State.Coins != 450 || replay.State.Conceptions.Count != 1
                || replay.State.Header.RngStreams.Breeding.EventCounter != breedingCounter
                || !replay.State.Horses.Any(h => h.HorseId.Equals(childId)))
                throw new InvalidOperationException("Exact duplicate confirmation changed the fee, offspring, or breeding stream.");
            _state = replay.State;
            await CaptureFrame(Path.Combine(directory, "04_saved_pregnancy.png"));

            for (var i = 0; i < 4; i++)
            {
                _reviewButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!_reviewOverlay.Visible || _preview?.Allowed != true) throw new InvalidOperationException("The saved pregnancy blocked a normal End day before birth.");
                _confirmDayButton.EmitSignal(BaseButton.SignalName.Pressed);
                for (var frame = 0; frame < 600 && _busy; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_busy || _pendingRequest != null) throw new InvalidOperationException("The actual daily transaction did not finish during breeding capture.");
            }
            var foal = _state.Horses.Single(h => h.HorseId.Equals(childId));
            if (_state.Header.CalendarDay != 4 || foal.LifeState.Stage != LifeStage.Foal || foal.BirthDay != 4
                || foal.Genome.GenomeHash != committed.UnbornAtCommit.Genome.GenomeHash
                || !foal.Parentage.ParentAId!.Equals(mare.HorseId) || !foal.Parentage.ParentBId!.Equals(stallion.HorseId))
                throw new InvalidOperationException("Birth did not preserve the committed child identity, canonical genetics, parents, and four-day date.");
            await CaptureFrame(Path.Combine(directory, "05_birth.png"));
            if (!await NameFoalThroughUiAsync(childId, "Clover")) throw new InvalidOperationException("The real naming controls did not save the newborn's name.");
            var named = _state.Horses.Single(h => h.HorseId.Equals(childId));
            if (named.Name != "Clover") throw new InvalidOperationException("The saved newborn name did not update.");
            _reloadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!_state.Horses.Any(h => h.HorseId.Equals(childId) && h.Name == "Clover"))
                throw new InvalidOperationException("The newborn's chosen name did not survive an actual save reload.");
            await CaptureFrame(Path.Combine(directory, "06_named_reload.png"));
            for (var i = 0; i < 6; i++)
            {
                _reviewButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!_reviewOverlay.Visible || _preview?.Allowed != true) throw new InvalidOperationException("Growth blocked a normal End day.");
                _confirmDayButton.EmitSignal(BaseButton.SignalName.Pressed);
                for (var frame = 0; frame < 600 && _busy; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_busy || _pendingRequest != null) throw new InvalidOperationException("The saved foal did not progress through the real End day handler.");
            }
            _reloadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var grown = _state.Horses.Single(h => h.HorseId.Equals(childId));
            var totalEvents = _state.DailyState.Receipts.SelectMany(r => r.LifecycleEvents).ToArray();
            if (_state.Header.CalendarDay != 10 || grown.Name != "Clover" || grown.LifeState.Stage != LifeStage.Adult
                || grown.BirthDay != 4 || totalEvents.Count(e => e.Kind.ToString() == "Birth") != 1
                || totalEvents.Count(e => e.Kind.ToString() == "FoalToYoungster") != 1
                || totalEvents.Count(e => e.Kind.ToString() == "YoungsterToAdult") != 1
                || _state.Conceptions.Count != 1 || _state.Coins != 554 || _state.Header.RngStreams.Breeding.EventCounter != breedingCounter)
                throw new InvalidOperationException("Reloaded birth, name, growth, daily fees, or breeding receipt was not stable.");
            if (_state.Ledger.Count(entry => entry.Kind == "breeding") != 1
                || _state.Ledger.Single(entry => entry.Kind == "breeding").DeltaCoins != -50)
                throw new InvalidOperationException("The saved wallet ledger does not contain exactly one 50-coin breeding debit.");
            await CaptureFrame(Path.Combine(directory, "07_reloaded_adult.png"));
            // D-010: sell the grown foal to the club through the real Details -> offer -> Sell controls.
            var coinsBeforeSale = _state.Coins;
            _inspectButtons[childId].EmitSignal(BaseButton.SignalName.Pressed);
            if (!_inspectSellButton.Visible || _inspectSellButton.Disabled) throw new InvalidOperationException("The grown foal was not offered for sale.");
            _inspectSellButton.EmitSignal(BaseButton.SignalName.Pressed);
            var offer = _reviewedOffer ?? throw new InvalidOperationException("Asking for an offer did not open the club's offer.");
            await CaptureFrame(Path.Combine(directory, "08_offer.png"));
            _confirmSaleButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 600 && _busy; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _reloadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_saleOverlay.Visible || _state.Coins != coinsBeforeSale + offer.PriceCoins || _inspectButtons.ContainsKey(childId)
                || !HorseMarket.IsSold(_state.Horses.Single(h => h.HorseId.Equals(childId))))
                throw new InvalidOperationException("The sale did not pay once, save, and move the horse to the club.");
            await CaptureFrame(Path.Combine(directory, "09_after_sale.png"));
            var checks = new
            {
                outcome = "ok",
                day = _state.Header.CalendarDay,
                coins = _state.Coins,
                salePrice = offer.PriceCoins,
                breedingFee = StableBreedingPlanner.CostCoins,
                conceptionCount = _state.Conceptions.Count,
                breedingEventCounter = _state.Header.RngStreams.Breeding.EventCounter,
                childId = childId.ToString(),
                childGenomeHash = grown.Genome.GenomeHash,
                parentAId = grown.Parentage.ParentAId?.ToString(),
                parentBId = grown.Parentage.ParentBId?.ToString(),
                birthDay = grown.BirthDay,
                stage = grown.LifeState.Stage.ToString(),
                name = grown.Name,
                lifecycleEvents = totalEvents.Select(e => e.Kind.ToString()).ToArray(),
                screenshots = Directory.GetFiles(directory, "*.png").Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray()
            };
            File.WriteAllText(Path.Combine(directory, "checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[stable-breeding-capture] OK screenshots=9 day={_state.Header.CalendarDay} coins={_state.Coins} child={childId} output={directory}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[stable-breeding-capture] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task WaitForBreedingSave()
    {
        for (var frame = 0; frame < 600 && _busy; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
