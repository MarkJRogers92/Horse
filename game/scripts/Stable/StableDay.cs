using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>Text-based stable management prototype backed by the durable domain save.</summary>
public partial class StableDay : Node
{
    private const string DefaultSavePath = "user://stable-management/stable.clvr";
    private readonly StableDayService _service = new();
    private readonly Dictionary<HorseId, (bool Turnout, bool Grooming)> _optionalCare = new();
    private readonly List<CheckButton> _optionalToggles = new();
    private readonly Dictionary<HorseId, CheckButton> _turnoutButtons = new();
    private AtomicSaveStore _store = null!;
    private StableSnapshot? _state;
    private DailyPreview? _preview;
    private CarePlan? _reviewedPlan;
    private EndDayRequest? _pendingRequest;
    private ulong? _reviewedRevision;
    private int? _reviewedDay;
    private bool _recoveryRequired;
    private bool _busy;
    private string? _captureOutput;
    private Label _season = null!, _wallet = null!, _status = null!;
    private VBoxContainer _roster = null!, _care = null!;
    private Button _reviewButton = null!, _retryButton = null!, _reloadButton = null!, _backButton = null!;
    private Control _reviewOverlay = null!;
    private Label _reviewDetails = null!;
    private Button _confirmDayButton = null!;
    private ConfirmationDialog _recoveryDialog = null!;
    private SaveLoadResult? _recoveryCandidate;

    public override void _Ready()
    {
        _store = new AtomicSaveStore(ResolveSavePath(OS.GetCmdlineUserArgs()));
        BuildUi();
        var args = OS.GetCmdlineUserArgs();
        _captureOutput = args.FirstOrDefault(a => a.StartsWith("--stable-day-capture=", StringComparison.Ordinal))?["--stable-day-capture=".Length..];
        if (args.Contains("--stable-day-smoke"))
        {
            _ = RunSmokeAsync();
            return;
        }

        try
        {
            LoadOrCreate();
            Refresh();
            if (_captureOutput != null) _ = RunCaptureAsync(_captureOutput);
        }
        catch (Exception ex)
        {
            SetStatus("The stable could not be opened. " + PlainError(ex));
            SetControlsEnabled(false);
        }
    }

    private static string ResolveSavePath(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--save-path=", StringComparison.Ordinal))
                return Path.GetFullPath(args[i]["--save-path=".Length..]);
            if (args[i] == "--save-path" && i + 1 < args.Count)
                return Path.GetFullPath(args[i + 1]);
        }
        return ProjectSettings.GlobalizePath(DefaultSavePath);
    }

    private void LoadOrCreate()
    {
        if (File.Exists(_store.SavePath) || File.Exists(_store.BackupPath))
        {
            ApplyLoadedState(_store.Load());
            return;
        }

        try
        {
            _state = _store.Initialize(StableDayBootstrap.Create(_service));
            SetStatus("Welcome. Your new stable is ready for its first day.");
        }
        catch (InvalidOperationException)
        {
            // Another instance may have created the same slot after the presence check.
            ApplyLoadedState(_store.Load());
        }
    }

    private void ApplyLoadedState(SaveLoadResult loaded)
    {
        _state = loaded.State;
        _recoveryRequired = loaded.NeedsRecoveryAcknowledgment;
        _recoveryCandidate = _recoveryRequired ? loaded : null;
        if (_recoveryRequired)
        {
            _recoveryDialog.DialogText = loaded.Notice + "\n\nContinue only if you want to restore this exact verified checkpoint.";
            _recoveryDialog.PopupCentered();
        }
        else SetStatus(loaded.Notice == "Primary save verified."
            ? "Your saved stable is loaded and ready."
            : loaded.Notice);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var root = new Control { Theme = CozyTheme.Build() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.OffsetLeft = root.OffsetTop = root.OffsetRight = root.OffsetBottom = 0;
        layer.AddChild(root);

        var page = new VBoxContainer { Position = new Vector2(20, 16), Size = new Vector2(1240, 688), AnchorRight = 1, AnchorBottom = 1 };
        page.OffsetRight = -40;
        page.OffsetBottom = -32;
        page.AddThemeConstantOverride("separation", 10);
        root.AddChild(page);

        var header = new HBoxContainer();
        header.AddChild(CozyTheme.Title("A Day at the Stable", 32));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var prototype = CozyTheme.Title("STABLE MANAGEMENT · PROTOTYPE", 13);
        prototype.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(prototype);
        _backButton = new Button { Text = "Back to menu", CustomMinimumSize = new Vector2(150, 42) };
        _backButton.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        header.AddChild(_backButton);
        page.AddChild(header);

        var summary = new HBoxContainer();
        summary.AddThemeConstantOverride("separation", 12);
        _season = SummaryCard("Season 1 · Day 1");
        _wallet = SummaryCard("500 coins");
        summary.AddChild(SummaryPanel(_season, "CALENDAR"));
        summary.AddChild(SummaryPanel(_wallet, "WALLET"));
        var dailyNote = CozyTheme.Title("Daily basics are covered for active-stable horses.", 17);
        var notePanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        notePanel.AddChild(dailyNote);
        summary.AddChild(notePanel);
        page.AddChild(summary);

        var columns = new HSplitContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SplitOffsets = new[] { 365 }, DraggerVisibility = SplitContainer.DraggerVisibilityEnum.Hidden };
        columns.AddThemeConstantOverride("separation", 12);
        page.AddChild(columns);

        var rosterPanel = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        columns.AddChild(rosterPanel);
        var rosterBox = new VBoxContainer(); rosterBox.AddThemeConstantOverride("separation", 8); rosterPanel.AddChild(rosterBox);
        rosterBox.AddChild(CozyTheme.Title("Your horses", 23));
        var rosterScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        rosterBox.AddChild(rosterScroll);
        _roster = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _roster.AddThemeConstantOverride("separation", 8);
        rosterScroll.AddChild(_roster);

        var carePanel = new PanelContainer(); columns.AddChild(carePanel);
        var careBox = new VBoxContainer(); careBox.AddThemeConstantOverride("separation", 8); carePanel.AddChild(careBox);
        careBox.AddChild(CozyTheme.Title("Today's care", 23));
        var instruction = new Label { Text = "Feed and fresh bedding are always included. Choose extra comfort care for each horse.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(450, 0) };
        instruction.AddThemeFontSizeOverride("font_size", 15);
        careBox.AddChild(instruction);
        var careScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        careBox.AddChild(careScroll);
        _care = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _care.AddThemeConstantOverride("separation", 8);
        careScroll.AddChild(_care);

        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 10);
        var leftActions = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _status = new Label { Text = "Loading your stable…", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size", 14);
        leftActions.AddChild(_status);
        var buttonRow = new HBoxContainer();
        _reloadButton = new Button { Text = "Reload save", CustomMinimumSize = new Vector2(150, 44) };
        _reloadButton.Pressed += ReloadState;
        buttonRow.AddChild(_reloadButton);
        _retryButton = new Button { Text = "Retry saved day", Visible = false, CustomMinimumSize = new Vector2(180, 44) };
        _retryButton.Pressed += () => _ = CommitPendingAsync();
        buttonRow.AddChild(_retryButton);
        _reviewButton = new Button { Text = "Review day", CustomMinimumSize = new Vector2(180, 48) };
        _reviewButton.Pressed += ShowReview;
        buttonRow.AddChild(_reviewButton);
        leftActions.AddChild(buttonRow);
        footer.AddChild(leftActions);
        page.AddChild(footer);

        BuildReviewOverlay(root);
        BuildRecoveryDialog(root);
    }

    private static Label SummaryCard(string value) => CozyTheme.Title(value, 24);

    private static Control SummaryPanel(Label value, string label)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(220, 58) };
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 0); panel.AddChild(box);
        var heading = CozyTheme.Title(label, 11); box.AddChild(heading); box.AddChild(value);
        return panel;
    }

    private void BuildReviewOverlay(Control root)
    {
        _reviewOverlay = new Control { Visible = false };
        _reviewOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_reviewOverlay);
        var dim = new ColorRect { Color = new Color(0.16f, 0.11f, 0.17f, 0.52f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _reviewOverlay.AddChild(dim);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect); _reviewOverlay.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(620, 0) }; center.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12); panel.AddChild(box);
        box.AddChild(CozyTheme.Title("Review today's plan", 26));
        _reviewDetails = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(560, 0) };
        box.AddChild(_reviewDetails);
        var buttons = new HBoxContainer(); buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var cancel = new Button { Text = "Keep planning", CustomMinimumSize = new Vector2(150, 44) };
        cancel.Pressed += () => { _reviewOverlay.Visible = false; ClearReviewedPlan(); }; buttons.AddChild(cancel);
        _confirmDayButton = new Button { Text = "End day", CustomMinimumSize = new Vector2(150, 44) };
        _confirmDayButton.Pressed += ConfirmReviewedDay;
        buttons.AddChild(_confirmDayButton); box.AddChild(buttons);
    }

    private void BuildRecoveryDialog(Control root)
    {
        _recoveryDialog = new ConfirmationDialog { Title = "Restore a verified save", OkButtonText = "Restore backup" };
        _recoveryDialog.GetOkButton().Text = "Restore backup";
        _recoveryDialog.Confirmed += AcknowledgeRecovery;
        root.AddChild(_recoveryDialog);
    }

    private void Refresh()
    {
        if (_state == null) return;
        var day = _state.Header.CalendarDay;
        var seasonNumber = day / StableCalendar.DaysPerSeason + 1;
        var dayNumber = day % StableCalendar.DaysPerSeason + 1;
        _season.Text = $"Season {seasonNumber} · Day {dayNumber}";
        _wallet.Text = $"{_state.Coins:N0} coins";

        _roster.QueueFreeChildren();
        _care.QueueFreeChildren();
        _optionalToggles.Clear();
        _turnoutButtons.Clear();
        foreach (var horse in _state.Horses.OrderBy(h => h.Name, StringComparer.Ordinal))
        {
            var care = _state.DailyState.For(horse.HorseId);
            var card = new PanelContainer();
            var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 3); card.AddChild(content);
            content.AddChild(CozyTheme.Title(horse.Name, 20));
            content.AddChild(SmallText($"{StageName(horse.LifeState.Stage)} · {HomeName(horse.HomeAssignment)}"));
            content.AddChild(SmallText($"Condition: nutrition {Percent(care.Nutrition)} · bedding {Percent(care.Bedding)}"));
            content.AddChild(SmallText($"Comfort: turnout {Percent(care.Turnout)} · grooming {Percent(care.Grooming)}"));
            _roster.AddChild(card);

            if (!horse.OwnerId.Equals(_state.PlayerOwnerId) || horse.HomeAssignment != HomeKind.ActiveStable || horse.LifeState.Stage == LifeStage.Unborn) continue;
            if (!_optionalCare.ContainsKey(horse.HorseId)) _optionalCare[horse.HorseId] = (false, false);
            var options = _optionalCare[horse.HorseId];
            var section = new PanelContainer(); var sectionBox = new VBoxContainer(); sectionBox.AddThemeConstantOverride("separation", 5); section.AddChild(sectionBox);
            sectionBox.AddChild(CozyTheme.Title(horse.Name, 19));
            var required = new HBoxContainer();
            required.AddChild(CareToggle("Feed", true, true)); required.AddChild(CareToggle("Fresh bedding", true, true));
            sectionBox.AddChild(required);
            var optional = new HBoxContainer();
            var turnout = CareToggle("Turnout", options.Turnout, false);
            turnout.Toggled += on => SetOptional(horse.HorseId, on, null);
            _optionalToggles.Add(turnout);
            _turnoutButtons[horse.HorseId] = turnout;
            optional.AddChild(turnout);
            var groom = CareToggle("Grooming", options.Grooming, false);
            groom.Toggled += on => SetOptional(horse.HorseId, null, on);
            _optionalToggles.Add(groom);
            optional.AddChild(groom);
            sectionBox.AddChild(optional);
            sectionBox.AddChild(SmallText("Optional care can be changed before you review the day."));
            _care.AddChild(section);
        }

        if (_recoveryRequired) SetStatus("A verified backup needs your acknowledgment before the stable can be changed.");
        SetControlsEnabled(!_busy && !_recoveryRequired);
        _reviewButton.Disabled = _busy || _recoveryRequired || _pendingRequest != null || _state.Horses.Count == 0;
        _retryButton.Visible = _pendingRequest != null;
        _retryButton.Disabled = _busy || _recoveryRequired;
        _reloadButton.Disabled = _busy || _pendingRequest != null;
        foreach (var toggle in _optionalToggles) toggle.Disabled = _busy || _recoveryRequired;
    }

    private void SetOptional(HorseId id, bool? turnout, bool? grooming)
    {
        (bool Turnout, bool Grooming) current = _optionalCare.TryGetValue(id, out var value) ? value : (false, false);
        _optionalCare[id] = (turnout ?? current.Turnout, grooming ?? current.Grooming);
    }

    private static CheckButton CareToggle(string text, bool value, bool required)
    {
        var toggle = new CheckButton { Text = text, ButtonPressed = value, Disabled = required, CustomMinimumSize = new Vector2(155, 40) };
        toggle.AddThemeColorOverride("font_disabled_color", CozyTheme.Text);
        return toggle;
    }

    private static Label SmallText(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 14);
        return label;
    }

    private static string Percent(int value) => $"{value / 100}%";
    private static string StageName(LifeStage stage) => stage switch
    {
        LifeStage.Adult => "Adult",
        LifeStage.Foal => "Foal",
        LifeStage.Youngster => "Young horse",
        LifeStage.Unborn => "Expecting",
        _ => "Horse"
    };

    private static string HomeName(HomeKind home) => home switch
    {
        HomeKind.ActiveStable => "Active stable",
        HomeKind.CooperativePasture => "Cooperative pasture",
        HomeKind.InstitutionalLease => "Leased care",
        _ => "Home unknown"
    };

    private CarePlan CurrentPlan() => new CarePlan(_state!.Horses
        .Where(h => h.OwnerId.Equals(_state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn).Select(h =>
    {
        (bool Turnout, bool Grooming) option = _optionalCare.TryGetValue(h.HorseId, out var care) ? care : (false, false);
        return new HorseCareAssignment(h.HorseId, feed: true, bedding: true, turnout: option.Turnout, grooming: option.Grooming);
    }));

    private void ShowReview()
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null) return;
        try
        {
            _reviewedPlan = CurrentPlan();
            _preview = _service.Preview(_state, _reviewedPlan);
            _reviewedRevision = _state.Header.SaveRevision;
            _reviewedDay = _state.Header.CalendarDay;
            var walletDebit = _preview.CareCostCoins + _preview.OperationsCostCoins - _preview.AssistanceCoins;
            var lines = new List<string>
            {
                $"Season {_state.Header.CalendarDay / StableCalendar.DaysPerSeason + 1}, day {_state.Header.CalendarDay % StableCalendar.DaysPerSeason + 1} → season {_preview.ToDay / StableCalendar.DaysPerSeason + 1}, day {_preview.ToDay % StableCalendar.DaysPerSeason + 1}.",
                $"Daily care: {_preview.CareCostCoins:N0} coins · stable operations: {_preview.OperationsCostCoins:N0} coins.",
                $"From your wallet: {walletDebit:N0} coins · assistance: {_preview.AssistanceCoins:N0} coins.",
                $"Wallet after the day: {_preview.ProjectedCoins:N0} coins.",
                "",
                "Care after today's plan:"
            };
            foreach (var outcome in _preview.HorseOutcomes)
            {
                var horse = _state.Horses.Single(h => h.HorseId.Equals(outcome.HorseId));
                lines.Add($"{horse.Name}: nutrition {Percent(outcome.Before.Nutrition)} → {Percent(outcome.After.Nutrition)}, bedding {Percent(outcome.Before.Bedding)} → {Percent(outcome.After.Bedding)}, turnout {Percent(outcome.Before.Turnout)} → {Percent(outcome.After.Turnout)}, grooming {Percent(outcome.Before.Grooming)} → {Percent(outcome.After.Grooming)}.");
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
            SetStatus(result.Replayed ? "That day was already saved. Your wallet and calendar are up to date." : "Day saved. Your horses' care and the calendar are up to date.");
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
        if (_busy || _pendingRequest != null) return;
        ClearReviewedPlan();
        try
        {
            var loaded = _store.Load();
            ApplyLoadedState(loaded);
            if (!loaded.NeedsRecoveryAcknowledgment) SetStatus("The current saved stable was reloaded.");
            Refresh();
        }
        catch (Exception ex)
        {
            SetStatus("The saved stable could not be reloaded. " + PlainError(ex));
        }
    }

    private async Task RunSmokeAsync()
    {
        try
        {
            LoadOrCreate();
            if (_state == null || _state.Header.CalendarDay != 0) throw new InvalidOperationException("Smoke requires a fresh temporary save slot.");
            var startCoins = _state.Coins;
            var plan = new CarePlan(_state.Horses.Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
            var preview = _service.Preview(_state, plan);
            if (!preview.Allowed) throw new InvalidOperationException("Fresh starter day was blocked: " + preview.BlockReason);
            var request = new EndDayRequest(new TransactionId(Guid.NewGuid()), _state.PlayerOwnerId, _state.Header.SaveRevision,
                _state.Header.CalendarDay, plan, _service.PolicyVersion);
            var committed = await Task.Run(() => _store.CommitEndDay(request, _service));
            var reloaded = _store.Load();
            var replay = await Task.Run(() => _store.CommitEndDay(request, _service));
            if (reloaded.State.Header.CalendarDay != 1 || committed.State.Header.CalendarDay != 1 || replay.State.Header.CalendarDay != 1 || !replay.Replayed)
                throw new InvalidOperationException("Commit, reload or retry did not retain exactly one day advancement.");
            if (reloaded.State.Coins != startCoins - (preview.CareCostCoins + preview.OperationsCostCoins - preview.AssistanceCoins)
                || committed.State.Coins != reloaded.State.Coins || replay.State.Coins != reloaded.State.Coins)
                throw new InvalidOperationException("Commit, reload or retry changed the wallet more than once.");
            GD.Print($"[stable-day-smoke] OK day={reloaded.State.Header.CalendarDay} coins={reloaded.State.Coins} replayed={replay.Replayed} path={_store.SavePath}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[stable-day-smoke] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task RunCaptureAsync(string output)
    {
        try
        {
            if (_state == null || _recoveryRequired || _state.Header.CalendarDay != 0)
                throw new InvalidOperationException("Capture requires a fresh, healthy temporary save slot.");
            var startCoins = _state.Coins;
            if (startCoins != 500 || _state.Horses.Count != 2)
                throw new InvalidOperationException("Capture expects the two-horse starter stable with 500 coins.");
            var directory = Path.GetFullPath(output);
            Directory.CreateDirectory(directory);

            await CaptureFrame(Path.Combine(directory, "01_stable_day.png"));
            var turnout = _turnoutButtons.First();
            SetOptional(turnout.Key, true, null);
            turnout.Value.ButtonPressed = true;
            _reviewButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!_reviewOverlay.Visible || _preview?.Allowed != true)
                throw new InvalidOperationException("The Review day button did not open an allowed day preview.");
            if (!_reviewedPlan!.For(turnout.Key)!.Turnout || _preview.HorseOutcomes.Single(o => o.HorseId.Equals(turnout.Key)).After.Turnout != 6200)
                throw new InvalidOperationException("The selected turnout choice did not appear in the reviewed care result.");
            await CaptureFrame(Path.Combine(directory, "02_review.png"));

            _confirmDayButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 600 && _busy; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_busy || _pendingRequest != null || _state?.Header.CalendarDay != 1)
                throw new InvalidOperationException("Confirm did not complete exactly one day advancement.");

            _reloadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_state == null || _state.Header.CalendarDay != 1 || _state.Coins != startCoins - 36
                || _state.DailyState.Receipts.Count != 1 || _state.Ledger.Count(e => e.Kind == "stable-day") != 1
                || _state.DailyState.For(turnout.Key).Turnout != 6200)
                throw new InvalidOperationException("Reload did not show exactly one saved day and one 36-coin debit.");
            await CaptureFrame(Path.Combine(directory, "03_day_2.png"));
            GD.Print($"[stable-day-capture] OK screenshots=3 day={_state.Header.CalendarDay} coins={_state.Coins} output={directory}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[stable-day-capture] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task CaptureFrame(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        if (error != Error.Ok) throw new IOException($"Could not save UI capture ({error}): {path}");
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (var button in new[] { _reviewButton, _retryButton, _reloadButton, _backButton })
            if (GodotObject.IsInstanceValid(button)) button.Disabled = !enabled;
        foreach (var toggle in _optionalToggles)
            if (GodotObject.IsInstanceValid(toggle)) toggle.Disabled = !enabled;
    }

    private void SetStatus(string text) { if (GodotObject.IsInstanceValid(_status)) _status.Text = text; }

    private static string PlainError(Exception ex)
    {
        var message = ex is AggregateException aggregate ? string.Join(" ", aggregate.InnerExceptions.Select(e => e.Message)) : ex.Message;
        return string.IsNullOrWhiteSpace(message) ? "Please reload and try again." : message;
    }
}

internal static class StableDayUiExtensions
{
    public static void QueueFreeChildren(this Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
