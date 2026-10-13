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
    private CanvasLayer _uiLayer = null!;

    public override void _Ready()
    {
        Audio.GameAudio.Ensure(GetTree());
        Audio.GameAudio.Music("music_yard");
        var args = OS.GetCmdlineUserArgs();
        _foalingDemoMode = args.Contains("--foaling-demo") || args.Any(a => a.StartsWith("--stable-lifecycle-capture=", StringComparison.Ordinal));
        _store = new AtomicSaveStore(ResolveSavePath(args, _foalingDemoMode));
        LoadGuidePreference();
        BuildHub();
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
            MaybeAskStableName();
            if (_lifecycleCaptureOutput != null) _ = RunLifecycleCaptureAsync(_lifecycleCaptureOutput);
            else if (_breedingCaptureOutput != null) _ = RunBreedingCaptureAsync(_breedingCaptureOutput);
            else if (_captureOutput != null) _ = RunCaptureAsync(_captureOutput);
            else if (args.FirstOrDefault(a => a.StartsWith("--hub-preview=", StringComparison.Ordinal)) is { } hubPreview)
                _ = RunHubPreviewAsync(hubPreview["--hub-preview=".Length..]);
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
        _uiLayer = layer;
        AddChild(layer);
        var root = new Control { Theme = CozyTheme.Build() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.OffsetLeft = root.OffsetTop = root.OffsetRight = root.OffsetBottom = 0;
        layer.AddChild(root);

        root.MouseFilter = Control.MouseFilterEnum.Ignore; // clicks on open yard reach the horses

        // Top bar: game title, calendar and wallet pills, menu buttons.
        var header = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        header.AddThemeConstantOverride("separation", 10);
        Anchor(header, 0, 0, 1, 0, 16, 12, -16, 64);
        var ribbon = CozyTheme.Ribbon(StableName.Get(_store.SavePath), 24);
        _stableNameLabel = (Label)ribbon.GetChild(0);
        header.AddChild(ribbon);
        _season = SummaryCard("Season 1 · Day 1");
        _wallet = SummaryCard("500 coins");
        header.AddChild(HudPill(_season, "YEAR 1", "day", out _yearLabel));
        header.AddChild(HudPill(_wallet, "COINS", "coins", out _));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        // Compact icon button (tooltip names it) so every top-bar button fits a 1280-wide window, Menu included.
        _foalingDemoButton = CozyTheme.WithIcon(new Button { Text = "", CustomMinimumSize = new Vector2(52, 44), Disabled = _foalingDemoMode,
            TooltipText = _foalingDemoMode ? "Foaling demo (active)" : "Foaling demo: try a birth in a separate sandbox save" }, "foal", 30);
        _foalingDemoButton.Pressed += OpenFoalingDemo;
        header.AddChild(_foalingDemoButton);
        _backButton = new Button { Text = "Menu", CustomMinimumSize = new Vector2(100, 44) };
        _backButton.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        header.AddChild(PhotoButton());
        header.AddChild(ShopButton());
        header.AddChild(JournalButton());
        header.AddChild(Audio.GameAudio.MuteButton());
        header.AddChild(_backButton);
        root.AddChild(header);

        // Left: your horses with portraits.
        var rosterPanel = new PanelContainer();
        Anchor(rosterPanel, 0, 0, 0, 1, 16, 76, 352, -76);
        root.AddChild(rosterPanel);
        var rosterBox = new VBoxContainer(); rosterBox.AddThemeConstantOverride("separation", 6); rosterPanel.AddChild(rosterBox);
        rosterBox.AddChild(CozyTheme.Title("Your horses", 22));
        var rosterScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        rosterBox.AddChild(rosterScroll);
        _roster = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _roster.AddThemeConstantOverride("separation", 8);
        rosterScroll.AddChild(_roster);
        _history = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _history.AddThemeConstantOverride("separation", 4);
        rosterBox.AddChild(CozyTheme.Title("Recent milestones", 16));
        var historyScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 64),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        historyScroll.AddChild(_history);
        rosterBox.AddChild(historyScroll);

        // Right: today's care and breeding.
        var carePanel = new PanelContainer();
        Anchor(carePanel, 1, 0, 1, 1, -440, 76, -16, -76);
        root.AddChild(carePanel);
        var careBox = new VBoxContainer(); careBox.AddThemeConstantOverride("separation", 6); carePanel.AddChild(careBox);
        careBox.AddChild(CozyTheme.Title("Today's care", 22));
        var instruction = new Label { Text = "Feed and fresh bedding are always included. Add comfort care for each horse.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        instruction.AddThemeFontSizeOverride("font_size", 13);
        careBox.AddChild(instruction);
        var careScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        careBox.AddChild(careScroll);
        _care = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _care.AddThemeConstantOverride("separation", 8);
        careScroll.AddChild(_care);
        BuildBreedingUi(careBox, root);

        // Centre bottom: Pip, over the open yard.
        var guide = BuildGuidePanel();
        guide.SizeFlagsHorizontal = guide.SizeFlagsVertical = Control.SizeFlags.Fill;
        Anchor(guide, 0, 0, 1, 0, 368, 76, -456, 196);
        root.AddChild(guide);
        root.MoveChild(guide, 1); // stay beneath every dialog overlay
        BuildTasksPanel(root); // D-031 Pip's daily tasks, under the bubble

        // Bottom dock: status and the day's main actions.
        var footer = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; footer.AddThemeConstantOverride("separation", 10);
        Anchor(footer, 0, 1, 1, 1, 16, -66, -16, -12);
        _status = new Label { Text = "Loading your stable…", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center };
        _status.AddThemeFontSizeOverride("font_size", 14);
        // Long messages stay inside the dock (two lines, then an ellipsis) instead of pushing over the buttons.
        _status.MaxLinesVisible = 2; _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var statusPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipContents = true };
        statusPanel.AddThemeStyleboxOverride("panel", CozyTheme.Box(new Color(1, 1, 1, 0.82f), 18, 6));
        statusPanel.AddChild(_status);
        footer.AddChild(statusPanel);
        _reloadButton = new Button { Text = "Reload save", CustomMinimumSize = new Vector2(140, 50) };
        _reloadButton.Pressed += ReloadState;
        footer.AddChild(_reloadButton);
        _retryButton = new Button { Text = "Retry saved day", Visible = false, CustomMinimumSize = new Vector2(170, 50) };
        _retryButton.Pressed += () => _ = CommitPendingAsync();
        footer.AddChild(_retryButton);
        footer.AddChild(FairButton());
        footer.AddChild(ShowButton());
        _practiceButton = CozyTheme.WithIcon(new Button { Text = "Practice gallop", CustomMinimumSize = new Vector2(190, 50) }, "gallop", 32);
        _practiceButton.Pressed += ShowPractice;
        footer.AddChild(_practiceButton);
        _reviewButton = CozyTheme.WithIcon(new Button { Text = "End the day", CustomMinimumSize = new Vector2(190, 54) }, "end_day", 34);
        _reviewButton.Pressed += ShowReview;
        footer.AddChild(_reviewButton);
        root.AddChild(footer);

        BuildReviewOverlay(root);
        BuildInspectOverlay(root);
        BuildPracticeOverlay(root);
        BuildSaleOverlay(root);
        BuildFairOverlay(root);
        BuildShowOverlay(root);
        BuildTackOverlay(root);
        BuildShopOverlay(root);
        BuildBirthOverlay(root);
        BuildJournalOverlay(root);
        BuildNameStableOverlay(root);
        BuildRecoveryDialog(root);
    }

    private static Label SummaryCard(string value) => CozyTheme.Title(value, 20);

    private static void Anchor(Control control, float left, float top, float right, float bottom, float offsetLeft, float offsetTop, float offsetRight, float offsetBottom)
    {
        control.AnchorLeft = left; control.AnchorTop = top; control.AnchorRight = right; control.AnchorBottom = bottom;
        control.OffsetLeft = offsetLeft; control.OffsetTop = offsetTop; control.OffsetRight = offsetRight; control.OffsetBottom = offsetBottom;
    }

    private Label _yearLabel = null!;
    private Label _stableNameLabel = null!;
    private int _shownSeason = -1;

    private static Control HudPill(Label value, string label, string icon, out Label heading)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(150, 0) };
        panel.AddThemeStyleboxOverride("panel", CozyTheme.Box(new Color(1, 1, 1, 0.9f), 20, 4));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); panel.AddChild(row);
        row.AddChild(new TextureRect { Texture = CozyTheme.Icon(icon), CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", -4); row.AddChild(box);
        heading = CozyTheme.Title(label, 10); heading.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        box.AddChild(heading); box.AddChild(value);
        return panel;
    }

    private void BuildReviewOverlay(Control root)
    {
        _reviewOverlay = CozyTheme.Dialog(root, "Review today's plan", 640, out var box, out var buttons, out _);
        _reviewDetails = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(580, 0) };
        box.AddChild(_reviewDetails);
        CozyTheme.DialogHeader(box, "day", 596, 100);
        var cancel = new Button { Text = "Keep planning", CustomMinimumSize = new Vector2(160, 46) };
        cancel.Pressed += () => { _reviewOverlay.Visible = false; ClearReviewedPlan(); }; buttons.AddChild(cancel);
        _confirmDayButton = new Button { Text = "End day", CustomMinimumSize = new Vector2(150, 46) };
        _confirmDayButton.Pressed += ConfirmReviewedDay;
        buttons.AddChild(_confirmDayButton);
        CozyTheme.FinishDialog(box, buttons);
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
        _season.Text = $"{StableYard.SeasonName(seasonNumber - 1)} · Day {dayNumber}";
        if (_shownSeason != seasonNumber - 1) { _shownSeason = seasonNumber - 1; _yard.SetSeason(_shownSeason); }
        _yard.SetTrophies(_state.Ledger.Count(l => l.Kind == ClubRace.PrizeLedgerKind && l.DeltaCoins == ClubRace.PrizeFor(1)));
        var weather = WeatherFor(_state, day);
        if (_yard.Weather != weather) _yard.SetWeather(weather);
        Audio.GameAudio.Ambience(weather == YardWeather.Rain && _shownSeason != 3 ? "ambience_rain" : "ambience_yard");
        _yearLabel.Text = $"YEAR {(seasonNumber - 1) / 4 + 1} · {WeatherName(weather, _shownSeason)}";
        _wallet.Text = $"{_state.Coins:N0} coins";

        _roster.QueueFreeChildren();
        _care.QueueFreeChildren();
        _optionalToggles.Clear();
        _turnoutButtons.Clear();
        _inspectButtons.Clear();
        foreach (var horse in _state.Horses.Where(h => !HorseMarket.IsSold(h))
                     .OrderBy(h => h.LifeState.Stage is LifeStage.Foal or LifeStage.Youngster ? 0 : h.LifeState.Stage == LifeStage.Adult ? 1 : 2)
                     .ThenBy(h => h.Name, StringComparer.Ordinal))
        {
            var care = _state.DailyState.For(horse.HorseId);
            var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            var cardKey = horse.HorseId.ToString();
            card.MouseEntered += () => _yard.Highlight(cardKey);
            card.MouseExited += () => _yard.Highlight(null);
            var cardRow = new HBoxContainer(); cardRow.AddThemeConstantOverride("separation", 10); card.AddChild(cardRow);
            var portrait = Portrait(horse, 84, CardTint(horse));
            portrait.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            cardRow.AddChild(portrait);
            var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 1); cardRow.AddChild(content);
            var titleRow = new HBoxContainer(); titleRow.AddThemeConstantOverride("separation", 6);
            var title = CozyTheme.Title(horse.Name, 19);
            titleRow.AddChild(title);
            var hearts = CozyTheme.HeartRating(HorseBond.Hearts(horse), 13); hearts.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            hearts.TooltipText = "Bond: pat, brush and treat this horse in the yard";
            titleRow.AddChild(hearts);
            content.AddChild(titleRow);
            content.AddChild(SmallText($"{StageName(horse.LifeState.Stage)} · {HomeName(horse.HomeAssignment)}"));
            content.AddChild(CareBar("Fed", care.Nutrition)); content.AddChild(CareBar("Bedding", care.Bedding));
            content.AddChild(CareBar("Turnout", care.Turnout)); content.AddChild(CareBar("Grooming", care.Grooming));
            var details = InspectButton(horse.HorseId); details.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; details.CustomMinimumSize = new Vector2(96, 34);
            content.AddChild(details);
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
                var nameInput = new LineEdit { PlaceholderText = "Choose a name", Text = horse.Name, MaxLength = 40, CustomMinimumSize = new Vector2(90, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                var rename = CozyTheme.WithIcon(new Button { Text = horse.LifeState.Stage == LifeStage.Foal ? "Name foal" : "Rename", CustomMinimumSize = new Vector2(110, 38) }, "foal", 22);
                rename.Pressed += () => _ = RenameFoalAsync(horse.HorseId, nameInput.Text);
                renameRow.AddChild(nameInput); renameRow.AddChild(SuggestNameButton(nameInput)); renameRow.AddChild(rename); _roster.AddChild(renameRow);
            }

            if (!horse.OwnerId.Equals(_state.PlayerOwnerId) || horse.HomeAssignment != HomeKind.ActiveStable || horse.LifeState.Stage == LifeStage.Unborn) continue;
            if (!_optionalCare.ContainsKey(horse.HorseId)) _optionalCare[horse.HorseId] = (false, false);
            var options = _optionalCare[horse.HorseId];
            var section = new PanelContainer(); var sectionBox = new VBoxContainer(); sectionBox.AddThemeConstantOverride("separation", 5); section.AddChild(sectionBox);
            var sectionHeader = new HBoxContainer(); sectionHeader.AddThemeConstantOverride("separation", 8);
            sectionHeader.AddChild(Portrait(horse, 40, CardTint(horse)));
            var sectionTitle = CozyTheme.Title(horse.Name, 18); sectionTitle.VerticalAlignment = VerticalAlignment.Center;
            sectionHeader.AddChild(sectionTitle);
            sectionBox.AddChild(sectionHeader);
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
            _care.AddChild(section);
        }

        RefreshMilestoneHistory();
        RefreshYard();
        RefreshDecor();
        CheckRibbons();

        if (_recoveryRequired) SetStatus("A verified backup needs your acknowledgment before the stable can be changed.");
        SetControlsEnabled(!_busy && !_recoveryRequired);
        _reviewButton.Disabled = _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null || _state.Horses.Count == 0;
        _retryButton.Visible = _pendingRequest != null;
        _fairButton.Disabled = _busy || _recoveryRequired || _foalingDemoMode;
        _showButton.Disabled = _busy || _recoveryRequired || _foalingDemoMode;
        _retryButton.Disabled = _busy || _recoveryRequired;
        _reloadButton.Disabled = _busy || _pendingRequest != null || _pendingBreedingRequest != null;
        foreach (var toggle in _optionalToggles) toggle.Disabled = _busy || _recoveryRequired;
        _foalingDemoButton.TooltipText = _foalingDemoMode ? "Foaling demo (active)" : "Foaling demo: try a birth in a separate sandbox save";
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
        if (turnout == true || grooming == true) { _guideSawCareChoice = true; _yard.Cheer(id.ToString()); }
        RefreshGuide();
    }

    private static CheckButton CareToggle(string text, bool value, bool required)
    {
        var toggle = new CheckButton { Text = text, ButtonPressed = value, Disabled = required, CustomMinimumSize = new Vector2(155, 40) };
        var icon = text switch { "Feed" => "feed", "Fresh bedding" => "bedding", "Turnout" => "turnout", "Grooming" => "grooming", _ => null };
        if (icon != null) CozyTheme.WithIcon(toggle, icon, 30);
        toggle.AddThemeColorOverride("font_disabled_color", CozyTheme.Text);
        return toggle;
    }

    /// <summary>Presentation-only daily weather (D-018), the same for a given stable and day on every load.</summary>
    private static YardWeather WeatherFor(StableSnapshot state, int day)
    {
        if (day == 0) return YardWeather.Sunny; // a bright first morning
        var roll = Convert.ToInt32(Canonical.Hash("yard-weather.v1", state.Header.SaveId.ToString(), day.ToString(System.Globalization.CultureInfo.InvariantCulture)).Substring(0, 4), 16) % 100;
        var season = day / StableCalendar.DaysPerSeason % 4;
        var rainy = season switch { 1 => 12, 2 => 26, 3 => 22, _ => 22 };
        return roll < rainy ? YardWeather.Rain : roll < rainy + 25 ? YardWeather.Cloudy : YardWeather.Sunny;
    }

    private static string WeatherName(YardWeather weather, int season) => weather switch
    {
        YardWeather.Rain => season == 3 ? "SNOWY" : "RAINY",
        YardWeather.Cloudy => "CLOUDY",
        _ => season == 3 ? "CRISP" : "SUNNY",
    };

    private static Color CardTint(HorseRecord horse) => horse.LifeState.Stage is LifeStage.Foal or LifeStage.Youngster
        ? Color.FromHtml("#fbe7b0") : horse.ReproductionState.Role == ReproductiveRole.Mare ? Color.FromHtml("#fde4ec") : Color.FromHtml("#d9ecf8");

    /// <summary>A small labelled meter, 0-10000 basis points.</summary>
    private static Control CareBar(string label, int basisPoints)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        var name = new Label { Text = label, CustomMinimumSize = new Vector2(64, 0) };
        name.AddThemeFontSizeOverride("font_size", 12); name.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        row.AddChild(name);
        var bar = new ProgressBar { MinValue = 0, MaxValue = 10000, Value = basisPoints, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = Percent(basisPoints) };
        var back = CozyTheme.Box(new Color(CozyTheme.Border, 0.6f), 5, 0); back.ContentMarginLeft = back.ContentMarginRight = 0;
        var fill = CozyTheme.Box(basisPoints >= 6000 ? Color.FromHtml("#8fd1a8") : basisPoints >= 3500 ? Color.FromHtml("#f6c76b") : Color.FromHtml("#f19a9a"), 5, 0);
        fill.ContentMarginLeft = fill.ContentMarginRight = 0;
        bar.AddThemeStyleboxOverride("background", back); bar.AddThemeStyleboxOverride("fill", fill);
        row.AddChild(bar);
        return row;
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
