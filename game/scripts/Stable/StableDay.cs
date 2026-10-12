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
    private const string FoalingDemoSavePath = "user://stable-management/foaling-demo.clvr";
    private const string FoalingDemoResourceName = "Cloverbrook.Game.Stable.FoalingDemo";
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
    private bool _foalingDemoMode;
    private string? _captureOutput;
    private string? _lifecycleCaptureOutput;
    private string? _breedingCaptureOutput;
    private Label _season = null!, _wallet = null!, _status = null!;
    private VBoxContainer _roster = null!, _care = null!;
    private Button _reviewButton = null!, _retryButton = null!, _reloadButton = null!, _backButton = null!, _foalingDemoButton = null!;
    private Control _reviewOverlay = null!;
    private Label _reviewDetails = null!;
    private Button _confirmDayButton = null!;
    private ConfirmationDialog _recoveryDialog = null!;
    private SaveLoadResult? _recoveryCandidate;
    private VBoxContainer _history = null!;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _foalingDemoMode = args.Contains("--foaling-demo") || args.Any(a => a.StartsWith("--stable-lifecycle-capture=", StringComparison.Ordinal));
        _store = new AtomicSaveStore(ResolveSavePath(args, _foalingDemoMode));
        LoadGuidePreference();
        BuildUi();
        _captureOutput = args.FirstOrDefault(a => a.StartsWith("--stable-day-capture=", StringComparison.Ordinal))?["--stable-day-capture=".Length..];
        _lifecycleCaptureOutput = args.FirstOrDefault(a => a.StartsWith("--stable-lifecycle-capture=", StringComparison.Ordinal))?["--stable-lifecycle-capture=".Length..];
        _breedingCaptureOutput = args.FirstOrDefault(a => a.StartsWith("--stable-breeding-capture=", StringComparison.Ordinal))?["--stable-breeding-capture=".Length..];
        if (args.Contains("--stable-day-smoke"))
        {
            _ = RunSmokeAsync();
            return;
        }

        try
        {
            if (_lifecycleCaptureOutput != null)
            {
                if (!HasExplicitSavePath(args))
                    throw new InvalidOperationException("Lifecycle capture requires an explicit fresh --save-path so it cannot advance a real demo slot.");
                if (File.Exists(_store.SavePath) || Directory.Exists(_store.SavePath) || File.Exists(_store.BackupPath))
                    throw new InvalidOperationException("Lifecycle capture save path already exists. Choose a new isolated path; existing saves are never replaced.");
            }
            if (_breedingCaptureOutput != null)
            {
                if (_foalingDemoMode) throw new InvalidOperationException("Breeding capture uses the normal starter stable, not the foaling demo.");
                if (!HasExplicitSavePath(args))
                    throw new InvalidOperationException("Breeding capture requires an explicit fresh --save-path so it cannot advance a real stable.");
                if (File.Exists(_store.SavePath) || Directory.Exists(_store.SavePath) || File.Exists(_store.BackupPath))
                    throw new InvalidOperationException("Breeding capture save path already exists. Choose a new isolated path; existing saves are never replaced.");
                var captureDirectory = Path.GetFullPath(_breedingCaptureOutput);
                if (Directory.Exists(captureDirectory) || File.Exists(captureDirectory))
                    throw new InvalidOperationException("Breeding capture output must be a fresh path that does not exist yet.");
            }
            LoadOrCreate();
            Refresh();
            if (_lifecycleCaptureOutput != null) _ = RunLifecycleCaptureAsync(_lifecycleCaptureOutput);
            else if (_breedingCaptureOutput != null) _ = RunBreedingCaptureAsync(_breedingCaptureOutput);
            else if (_captureOutput != null) _ = RunCaptureAsync(_captureOutput);
        }
        catch (Exception ex)
        {
            SetStatus("The stable could not be opened. " + PlainError(ex));
            SetControlsEnabled(false);
            // Automated runs must fail fast instead of waiting on an error screen until the CI timeout.
            if (_captureOutput != null || _lifecycleCaptureOutput != null || _breedingCaptureOutput != null)
            {
                GD.PushError("[stable-capture] FAILED during setup: " + ex);
                GetTree().Quit(1);
            }
        }
    }

    private static string ResolveSavePath(IReadOnlyList<string> args, bool foalingDemo)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--save-path=", StringComparison.Ordinal))
                return Path.GetFullPath(args[i]["--save-path=".Length..]);
            if (args[i] == "--save-path" && i + 1 < args.Count)
                return Path.GetFullPath(args[i + 1]);
        }
        return ProjectSettings.GlobalizePath(foalingDemo ? FoalingDemoSavePath : DefaultSavePath);
    }

    private static bool HasExplicitSavePath(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--save-path=", StringComparison.Ordinal))
                return args[i]["--save-path=".Length..].Trim().Length > 0;
            if (args[i] == "--save-path")
                return i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1])
                    && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        }
        return false;
    }

    private void LoadOrCreate()
    {
        if (File.Exists(_store.SavePath) || File.Exists(_store.BackupPath))
        {
            ApplyLoadedState(_store.Load());
            EnableStarterBreedingIfSafe();
            return;
        }

        if (_foalingDemoMode)
        {
            var fixtureBytes = ReadFoalingDemoFixture();
            var fixture = new StableSaveCodec().Decode(fixtureBytes);
            try
            {
                _state = _store.Initialize(fixture);
                SetStatus("Foaling demo loaded into its own sandbox save. Your regular stable is unchanged.");
            }
            catch (InvalidOperationException)
            {
                // A second game instance may have created this dedicated demo slot.
                // Load its exact primary/backup state rather than reseeding it.
                ApplyLoadedState(_store.Load());
            }
            return;
        }

        try
        {
            _state = _store.Initialize(StableDayBootstrap.Create(_service));
            SetStatus("Welcome. Your new stable is ready for its first day.");
            EnableStarterBreedingIfSafe();
        }
        catch (InvalidOperationException)
        {
            // Another instance may have created the same slot after the presence check.
            ApplyLoadedState(_store.Load());
            EnableStarterBreedingIfSafe();
        }
    }

    private void EnableStarterBreedingIfSafe()
    {
        if (_foalingDemoMode || _recoveryRequired || _state == null) return;
        try
        {
            _state = _store.EnableStarterBreeding(_state.PlayerOwnerId, _state.Header.SaveRevision);
            _breedingUnavailableReason = null;
        }
        catch (Exception ex)
        {
            // This one-time path only recognizes the exact starter founders. A
            // rejected candidate remains readable and is never replaced.
            _breedingUnavailableReason = "Starter breeding is unavailable for this saved stable. " + PlainError(ex);
        }
    }

    private static byte[] ReadFoalingDemoFixture()
    {
        using var source = typeof(StableDay).Assembly.GetManifestResourceStream(FoalingDemoResourceName)
            ?? throw new FileNotFoundException("The foaling demo sample is missing from this game build.");
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        if (bytes.Length == 0) throw new InvalidDataException("The foaling demo sample is empty.");
        return bytes.ToArray();
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

    private void OpenFoalingDemo()
    {
        if (_busy || _pendingRequest != null || _pendingBreedingRequest != null || _recoveryRequired) return;
        try
        {
            _foalingDemoMode = true;
            _store = new AtomicSaveStore(ProjectSettings.GlobalizePath(FoalingDemoSavePath));
            LoadGuidePreference();
            _state = null;
            ClearReviewedPlan();
            LoadOrCreate();
            Refresh();
            _foalingDemoButton.Disabled = true;
        }
        catch (Exception ex)
        {
            SetStatus("The foaling demo could not be opened. " + PlainError(ex));
        }
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
        _foalingDemoButton = new Button { Text = _foalingDemoMode ? "Foaling demo · active" : "Foaling demo", CustomMinimumSize = new Vector2(190, 42), Disabled = _foalingDemoMode };
        _foalingDemoButton.Pressed += OpenFoalingDemo;
        header.AddChild(_foalingDemoButton);
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
        summary.AddChild(BuildGuidePanel());
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
        _history = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _history.AddThemeConstantOverride("separation", 5);
        rosterBox.AddChild(CozyTheme.Title("Recent milestones", 18));
        var historyScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 96),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        historyScroll.AddChild(_history);
        rosterBox.AddChild(historyScroll);

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
        BuildBreedingUi(careBox, root);

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
        _practiceButton = new Button { Text = "Practice gallop", CustomMinimumSize = new Vector2(170, 44) };
        _practiceButton.Pressed += ShowPractice;
        buttonRow.AddChild(_practiceButton);
        leftActions.AddChild(buttonRow);
        footer.AddChild(leftActions);
        page.AddChild(footer);

        BuildReviewOverlay(root);
        BuildInspectOverlay(root);
        BuildPracticeOverlay(root);
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
        _inspectButtons.Clear();
        foreach (var horse in _state.Horses
                     .OrderBy(h => h.LifeState.Stage is LifeStage.Foal or LifeStage.Youngster ? 0 : h.LifeState.Stage == LifeStage.Adult ? 1 : 2)
                     .ThenBy(h => h.Name, StringComparer.Ordinal))
        {
            var care = _state.DailyState.For(horse.HorseId);
            var card = new PanelContainer();
            var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 3); card.AddChild(content);
            var header = new HBoxContainer();
            var title = CozyTheme.Title(horse.Name, 20); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            header.AddChild(title); header.AddChild(InspectButton(horse.HorseId)); content.AddChild(header);
            content.AddChild(SmallText($"{StageName(horse.LifeState.Stage)} · {HomeName(horse.HomeAssignment)}"));
            content.AddChild(SmallText($"Condition: nutrition {Percent(care.Nutrition)} · bedding {Percent(care.Bedding)}"));
            content.AddChild(SmallText($"Comfort: turnout {Percent(care.Turnout)} · grooming {Percent(care.Grooming)}"));
            var parents = ParentNames(horse);
            if (parents.Length > 0) content.AddChild(SmallText("Parents: " + parents));
            if (horse.ReproductionState.PregnancyId != null)
            {
                var unborn = _state.Horses.FirstOrDefault(candidate => candidate.LifeState.Stage == LifeStage.Unborn
                    && (Equals(candidate.Parentage.ParentAId, horse.HorseId) || Equals(candidate.Parentage.ParentBId, horse.HorseId)));
                var dueDay = unborn?.OriginCommitId == null ? (int?)null : _state.Conceptions
                    .Where(c => c.Commit.BreedingCommitId.Equals(unborn.OriginCommitId)).Select(c => (int?)c.Commit.PlannedBirthDay).SingleOrDefault();
                if (dueDay.HasValue)
                {
                    var remaining = Math.Max(0, dueDay.Value - _state.Header.CalendarDay);
                    content.AddChild(SmallText($"Foal due in {remaining} {(remaining == 1 ? "day" : "days")} · planned calendar day {dueDay.Value + 1}."));
                }
            }
            if (horse.ReproductionState.RecoveryUntilDay > _state.Header.CalendarDay)
            {
                var remaining = horse.ReproductionState.RecoveryUntilDay - _state.Header.CalendarDay;
                content.AddChild(SmallText($"Mare recovery: {remaining} {(remaining == 1 ? "day" : "days")} remaining."));
            }
            if (horse.BirthDay.HasValue && horse.LifeState.Stage is LifeStage.Foal or LifeStage.Youngster)
            {
                var currentCalendarDay = _state.Header.CalendarDay;
                var youngsterDay = horse.BirthDay.Value + 3;
                var adultDay = horse.BirthDay.Value + 6;
                var readyDay = horse.BirthDay.Value + 8;
                if (horse.LifeState.Stage == LifeStage.Foal)
                    content.AddChild(SmallText($"Youngster in {Math.Max(0, youngsterDay - currentCalendarDay)} days · adult in {Math.Max(0, adultDay - currentCalendarDay)} days · breeding-ready in {Math.Max(0, readyDay - currentCalendarDay)} days."));
                else
                    content.AddChild(SmallText($"Adult in {Math.Max(0, adultDay - currentCalendarDay)} days · breeding-ready in {Math.Max(0, readyDay - currentCalendarDay)} days."));
            }
            else if (horse.BirthDay.HasValue && horse.LifeState.Stage == LifeStage.Adult)
            {
                var daysUntilReadiness = Math.Max(0, horse.BirthDay.Value + 8 - _state.Header.CalendarDay);
                content.AddChild(SmallText(daysUntilReadiness == 0 ? "Breeding-age milestone reached." : $"Breeding-age milestone in {daysUntilReadiness} days."));
            }
            _roster.AddChild(card);

            if (horse.OwnerId.Equals(_state.PlayerOwnerId) && horse.OriginCommitId != null && horse.LifeState.Stage != LifeStage.Unborn)
            {
                var renameRow = new HBoxContainer();
                var nameInput = new LineEdit { PlaceholderText = "Choose a name", Text = horse.Name, MaxLength = 40, CustomMinimumSize = new Vector2(190, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                var rename = new Button { Text = horse.LifeState.Stage == LifeStage.Foal ? "Name foal" : "Rename horse", CustomMinimumSize = new Vector2(125, 38) };
                rename.Pressed += () => _ = RenameFoalAsync(horse.HorseId, nameInput.Text);
                renameRow.AddChild(nameInput); renameRow.AddChild(rename); _roster.AddChild(renameRow);
            }

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

        RefreshMilestoneHistory();

        if (_recoveryRequired) SetStatus("A verified backup needs your acknowledgment before the stable can be changed.");
        SetControlsEnabled(!_busy && !_recoveryRequired);
        _reviewButton.Disabled = _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null || _state.Horses.Count == 0;
        _retryButton.Visible = _pendingRequest != null;
        _retryButton.Disabled = _busy || _recoveryRequired;
        _reloadButton.Disabled = _busy || _pendingRequest != null || _pendingBreedingRequest != null;
        foreach (var toggle in _optionalToggles) toggle.Disabled = _busy || _recoveryRequired;
        _foalingDemoButton.Text = _foalingDemoMode ? "Foaling demo · active" : "Foaling demo";
        _foalingDemoButton.Disabled = _busy || _foalingDemoMode || _pendingRequest != null || _pendingBreedingRequest != null || _recoveryRequired;
        RefreshBreedingUi();
        RefreshGuide();
    }

    private string ParentNames(HorseRecord horse)
    {
        var names = new[] { horse.Parentage.ParentAId, horse.Parentage.ParentBId }
            .Where(id => id != null)
            .Select(id => _state!.Horses.FirstOrDefault(parent => parent.HorseId.Equals(id))?.Name ?? "Unknown")
            .ToArray();
        return string.Join(" × ", names);
    }

    private void RefreshMilestoneHistory()
    {
        _history.QueueFreeChildren();
        var events = _state!.DailyState.Receipts.SelectMany(receipt => receipt.LifecycleEvents)
            .OrderByDescending(item => item.EffectiveDay).Take(5).ToArray();
        if (events.Length == 0)
        {
            _history.AddChild(SmallText("No foal or growth milestones recorded yet."));
            return;
        }
        foreach (var item in events)
            _history.AddChild(SmallText($"Day {item.EffectiveDay + 1}: {LifecycleText(item, _state)}"));
    }

    private static string LifecycleText(StableLifecycleEvent item, StableSnapshot state)
    {
        var horse = state.Horses.FirstOrDefault(h => h.HorseId.Equals(item.HorseId));
        var name = horse?.Name ?? "Foal";
        var related = item.RelatedHorseId == null ? null : state.Horses.FirstOrDefault(h => h.HorseId.Equals(item.RelatedHorseId));
        return item.Kind.ToString() switch
        {
            "Birth" => $"{name} was born{(related == null ? "" : $" to {related.Name}")}." + (horse == null ? "" : " Parents: " + ParentNamesFor(horse, state) + "."),
            "FoalToYoungster" => $"{name} grew into a youngster.",
            "YoungsterToAdult" => $"{name} reached adulthood.",
            _ => $"{name} reached a new life stage."
        };
    }

    private static string ParentNamesFor(HorseRecord horse, StableSnapshot state) => string.Join(" × ",
        new[] { horse.Parentage.ParentAId, horse.Parentage.ParentBId }.Where(id => id != null)
            .Select(id => state.Horses.FirstOrDefault(parent => parent.HorseId.Equals(id))?.Name ?? "Unknown"));

    private void SetOptional(HorseId id, bool? turnout, bool? grooming)
    {
        (bool Turnout, bool Grooming) current = _optionalCare.TryGetValue(id, out var value) ? value : (false, false);
        _optionalCare[id] = (turnout ?? current.Turnout, grooming ?? current.Grooming);
        if (turnout == true || grooming == true) { _guideSawCareChoice = true; RefreshGuide(); }
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

    private void SetControlsEnabled(bool enabled)
    {
        foreach (var button in new[] { _reviewButton, _retryButton, _reloadButton, _backButton, _foalingDemoButton, _practiceButton })
            if (GodotObject.IsInstanceValid(button)) button.Disabled = !enabled;
        foreach (var button in new BaseButton[] { _marePicker, _stallionPicker, _planBreedingButton, _retryBreedingButton })
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
