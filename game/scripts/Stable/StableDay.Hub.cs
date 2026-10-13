using System;
using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.DomainIntegration;
using Cloverbrook.Game.UI;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>Scene-first hub (D-012): the 3D yard behind the UI, horse portraits and click-to-inspect.</summary>
public partial class StableDay
{
    private const string AnatomicalMaskSetId = "canonical-anatomy.v1";
    private StableYard _yard = null!;
    private HorsePortraitStudio _portraits = null!;
    private readonly Dictionary<string, VisualDescriptor?> _descriptors = new();

    public override void _ExitTree()
    {
        // Managed wrappers (portrait icons, clips) otherwise keep engine resources alive until after
        // the engine's shutdown leak check; collect them while the scene is closing.
        CozyTheme.Icons.Release();
        StableYard.ReleaseCaches();
        HorsePresenter.ReleaseCaches();
        _silhouette?.Dispose(); _silhouette = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void BuildHub()
    {
        var args = OS.GetCmdlineUserArgs();
        _yard = new StableYard { Name = "StableYard", Wandering = !args.Any(a => a.StartsWith("--stable-", StringComparison.Ordinal)) };
        AddChild(_yard);
        _portraits = new HorsePortraitStudio { Name = "PortraitStudio" };
        AddChild(_portraits);
    }

    /// <summary>
    /// Sunset, a moment of night, then morning over the yard after a saved day (presentation only;
    /// the save has already committed). Skipped in automated captures so screenshots stay stable.
    /// </summary>
    private void PlayDayTransition(int calendarDay, string? news)
    {
        if (!_yard.Wandering) return;
        var veil = new ColorRect { Color = new Color(1f, 0.62f, 0.42f, 0f), MouseFilter = Control.MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var season = calendarDay / StableCalendar.DaysPerSeason + 1;
        var day = calendarDay % StableCalendar.DaysPerSeason + 1;
        var banner = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Modulate = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        banner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var morning = CozyTheme.Title("Good morning!", 44); morning.HorizontalAlignment = HorizontalAlignment.Center;
        morning.AddThemeColorOverride("font_color", Colors.White);
        var date = CozyTheme.Title($"{StableYard.SeasonName(season - 1)} · Day {day} · Year {(season - 1) / 4 + 1}", 26); date.HorizontalAlignment = HorizontalAlignment.Center;
        date.AddThemeColorOverride("font_color", new Color(1, 0.93f, 0.85f));
        banner.AddChild(morning); banner.AddChild(date);
        if (!string.IsNullOrEmpty(news))
        {
            var line = CozyTheme.Title(news, 20); line.HorizontalAlignment = HorizontalAlignment.Center;
            line.AddThemeColorOverride("font_color", Color.FromHtml("#ffd6e4"));
            banner.AddChild(line);
        }
        _uiLayer.AddChild(veil); _uiLayer.AddChild(banner);
        _yard.PlayNightfall(3.6);
        Audio.GameAudio.Play("day_end", -4);
        GetTree().CreateTimer(3.0).Timeout += () => Audio.GameAudio.Play("coins", -8); // the club's daily support arrives with the morning
        var tween = CreateTween();
        tween.TweenProperty(veil, "color", new Color(1f, 0.55f, 0.35f, 0.42f), 0.9);       // golden hour
        tween.TweenProperty(veil, "color", new Color(0.12f, 0.12f, 0.32f, 0.82f), 0.7);    // night
        tween.Parallel().TweenProperty(banner, "modulate", Colors.White, 0.5).SetDelay(0.4);
        tween.TweenInterval(0.9);
        tween.TweenProperty(veil, "color", new Color(1f, 0.9f, 0.8f, 0f), 1.1);             // sunrise
        tween.Parallel().TweenProperty(banner, "modulate", new Color(1, 1, 1, 0), 0.8).SetDelay(0.3);
        tween.TweenCallback(Callable.From(() => { veil.QueueFree(); banner.QueueFree(); }));
    }

    private bool _hubPreviewRunning;

    /// <summary>--hub-preview=DIR: timed screenshots of a live (wandering) yard, for visual review.</summary>
    private async System.Threading.Tasks.Task RunHubPreviewAsync(string output)
    {
        try
        {
            System.IO.Directory.CreateDirectory(output);
            _hubPreviewRunning = true; _nameStableOverlay.Visible = false; // a fresh preview save would otherwise ask for a stable name
            foreach (var (seconds, name) in new[] { (3.0, "01_hub_live"), (14.0, "02_hub_live"), (24.0, "03_hub_live") })
            {
                while (_yard.ElapsedSeconds < seconds) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (name == "02_hub_live" && _state?.Horses.OrderBy(h => h.Name, StringComparer.Ordinal).FirstOrDefault() is { } cheered)
                {
                    // Comfort care switched on: hearts over the horse, with the pointer hovering it.
                    _turnoutButtons.TryGetValue(cheered.HorseId, out var turnout);
                    if (turnout != null) turnout.ButtonPressed = true;
                    while (_yard.ElapsedSeconds < seconds + 0.5) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await CaptureFrame(System.IO.Path.Combine(output, name + ".png"));
            }
            _uiLayer.Visible = false;
            _yard.SetTrophies(1); // preview only: show the trophy stand in the UI-free view (Refresh restores the real count)
            await CaptureFrame(System.IO.Path.Combine(output, "04_yard_only.png"));
            for (var s = 0; s < 4; s++)
            {
                _yard.SetSeason(s);
                var before = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - before < 2.5) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, $"04_season_{s}_{StableYard.SeasonName(s).ToLowerInvariant()}.png"));
            }
            foreach (var (season, weather, name) in new[] { (0, YardWeather.Rain, "04_weather_spring_rain"), (2, YardWeather.Cloudy, "04_weather_autumn_cloudy"), (3, YardWeather.Rain, "04_weather_winter_snowy") })
            {
                _yard.SetSeason(season); _yard.SetWeather(weather);
                var before = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - before < 2.5) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, name + ".png"));
            }
            _yard.SetWeather(YardWeather.Sunny);
            _yard.SetSeason(_shownSeason);
            _uiLayer.Visible = true;
            ShowReview(); ConfirmReviewedDay();
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ended = _yard.ElapsedSeconds;
            foreach (var (after, name) in new[] { (0.9, "05_sunset"), (1.9, "06_night"), (3.6, "07_morning") })
            {
                while (_yard.ElapsedSeconds - ended < after) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, name + ".png"));
            }
            if (_state?.Horses.FirstOrDefault(h => h.OwnerId.Equals(_state.PlayerOwnerId) && h.LifeState.Stage == LifeStage.Adult) is { } petted)
            {
                var spot = GetViewport().GetVisibleRect().Size / 2 + new Vector2(-80, 40);
                ShowCareBubble(petted.HorseId, spot);
                await CaptureFrame(System.IO.Path.Combine(output, "07b_care_bubble.png"));
                await InteractAsync(petted.HorseId, "pat");
                await InteractAsync(petted.HorseId, "carrot");
                var hearts = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - hearts < 0.8) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, "07c_care_hearts.png"));
                // All three done: the "lots of love" bubble must stay small, on screen and closable.
                await InteractAsync(petted.HorseId, "brush");
                ShowCareBubble(petted.HorseId, spot);
                await CaptureFrame(System.IO.Path.Combine(output, "07d_care_all_done.png"));
                var shown = _careBubble;
                if (shown == null || shown.Size.Y > 320) throw new InvalidOperationException("The all-done care bubble is missing or oversized.");
                CloseCareBubble();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (IsInstanceValid(shown)) throw new InvalidOperationException("The care bubble did not close.");
            }
            // D-031 Pip's daily tasks: show the list even while the first-session guide is running, after claiming a finished task.
            {
                var guideWas = _guideDismissed; _guideDismissed = true; Refresh();
                if (_state != null && !DailyTasks.For(_state).Any(t => t.IsDone(_state)) && DailyTasks.For(_state).FirstOrDefault(t => t.Id.Contains(':') && !t.Id.EndsWith(":all") && !t.Id.EndsWith(":week")) is { } care)
                {
                    var parts = care.Id.Split(':');
                    await InteractAsync(_state.Horses.Single(h => h.HorseId.ToString() == parts[1]).HorseId, parts[0]);
                    while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                if (_state != null && DailyTasks.For(_state).FirstOrDefault(t => t.IsDone(_state) && !DailyTasks.IsClaimedToday(_state, t)) is { } finished)
                {
                    await ClaimDailyTaskAsync(finished.Id);
                    while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, "07e_daily_tasks.png"));
                _guideDismissed = guideWas; Refresh();
            }
            // D-028 DNA test, before the fair and shop spend the coins.
            if (_state?.Horses.FirstOrDefault(h => h.OwnerId.Equals(_state.PlayerOwnerId) && h.LifeState.Stage == LifeStage.Adult) is { } tested)
            {
                ShowInspection(tested.HorseId);
                do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
                await CaptureFrame(System.IO.Path.Combine(output, "24_details_dna_button.png"));
                await DnaTestAsync(tested.HorseId);
                do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
                await CaptureFrame(System.IO.Path.Combine(output, "25_details_dna_results.png"));
                _inspectOverlay.Visible = false;
            }
            if (OS.GetCmdlineUserArgs().Contains("--hub-birth"))
            {
                for (var day = 0; day < 12 && _birthFoal == null; day++)
                {
                    var before = _yard.ElapsedSeconds;
                    while (_yard.ElapsedSeconds - before < 4.2) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    ShowReview(); ConfirmReviewedDay();
                    while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                var born = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - born < 4.4) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, "08_birth_celebration.png"));
                _birthOverlay.Visible = false;
                var shown = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - shown < 2.0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, "09_ribbon_toast.png"));
                ShowJournal();
                await CaptureFrame(System.IO.Path.Combine(output, "10_journal.png"));
            }
            _journalOverlay.Visible = false;
            ShowFair(); _fairSpeaker.Finish();
            do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
            await CaptureFrame(System.IO.Path.Combine(output, "11_fair.png"));
            if (_state != null && HorseFair.Offers(_state).FirstOrDefault(o => HorseFair.BlockReason(_state, o) == null) is { } offer)
            {
                await BuyAsync(offer);
                var bought = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - bought < 1.2) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                while (_portraits.Busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, "12_fair_bought.png"));
            }
            ShowJournal();
            await CaptureFrame(System.IO.Path.Combine(output, "13_journal_ribbons.png"));
            _journalCoatTab.ButtonPressed = true; ShowJournalPage(true);
            do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
            await CaptureFrame(System.IO.Path.Combine(output, "14_journal_coats.png"));
            _journalOverlay.Visible = false;
            ShowPractice(true);
            do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
            await CaptureFrame(System.IO.Path.Combine(output, "15_cup_entry.png"));
            if (_practiceChoices.Count > 0)
            {
                await StartCupAsync();
                var started = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - started < 2.5) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, "16_cup_race.png"));
                FinishPracticePlayback();
                do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
                await CaptureFrame(System.IO.Path.Combine(output, "17_cup_result.png"));
                _practiceOverlay.Visible = false; _uiLayer.Visible = false;
                var shown = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - shown < 1.5) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, "18_yard_landmarks.png"));
                _uiLayer.Visible = true;
            }
            if (_state?.Horses.FirstOrDefault(h => h.OwnerId.Equals(_state.PlayerOwnerId) && h.LifeState.Stage == LifeStage.Adult) is { } model)
            {
                _practiceOverlay.Visible = false;
                await WearAsync(model.HorseId, Tack.PadSlot, "pad.lavender");
                await WearAsync(model.HorseId, Tack.BowSlot, "bow.rose");
                do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
                await CaptureFrame(System.IO.Path.Combine(output, "19_dress_up.png"));
                _tackOverlay.Visible = false; _uiLayer.Visible = false;
                _yard.Focus(model.HorseId.ToString());
                var focused = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - focused < 2.5) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, "20_dressed_in_yard.png"));
                _uiLayer.Visible = true;
            }
            _tackOverlay.Visible = false;
            foreach (var item in new[] { "hay", "planter", "birdhouse" })
                if (_state != null && StableShop.DecorBlockReason(_state, item) == null) await BuyDecorAsync(item);
            ShowShop();
            do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
            await CaptureFrame(System.IO.Path.Combine(output, "21_shop.png"));
            _shopOverlay.Visible = false; _uiLayer.Visible = false;
            _yard.Focus(null);
            var decorated = _yard.ElapsedSeconds;
            while (_yard.ElapsedSeconds - decorated < 2.0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await CaptureFrame(System.IO.Path.Combine(output, "22_decorated_yard.png"));
            _uiLayer.Visible = true;
            _shopOverlay.Visible = false;
            EnterPhotoMode();
            _yard.PhotoOrbit(-40f, 4f); _yard.PhotoZoom(0.85f);
            var posed = _yard.ElapsedSeconds;
            while (_yard.ElapsedSeconds - posed < 2.5) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await CaptureFrame(System.IO.Path.Combine(output, "23_photo_mode.png"));
            ExitPhotoMode();
            // D-029 show, D-030 grazing (the D-028 DNA test runs earlier, while coins remain).
            OpenShow();
            do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
            await CaptureFrame(System.IO.Path.Combine(output, "26_show_entry.png"));
            await EnterShowAsync();
            do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
            await CaptureFrame(System.IO.Path.Combine(output, "27_show_result.png"));
            _showOverlay.Visible = false; _uiLayer.Visible = false;
            foreach (var wait in new[] { 6.0, 12.0, 18.0 })
            {
                var start = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - start < 6.0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, $"28_yard_life_{wait:0}s.png"));
            }
            _uiLayer.Visible = true;
            GD.Print("[hub-preview] OK " + output);
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError("[hub-preview] FAILED " + ex); GetTree().Quit(1); }
    }

    /// <summary>Presentation-only descriptor for a saved horse, or null when it cannot be shown.</summary>
    private VisualDescriptor? DescriptorFor(HorseRecord horse)
    {
        if (horse.LifeState.Stage == LifeStage.Unborn) return null;
        var key = $"{horse.HorseId}:{horse.Revision}:{horse.LifeState.Stage}";
        if (_descriptors.TryGetValue(key, out var cached)) return cached;
        VisualDescriptor? descriptor = null;
        try
        {
            var resolution = new PhenotypeResolver().ResolveForPresentation(horse, DomainBridge.Matrix, VisualCatalog.Instance.Version, AnatomicalMaskSetId, Tack.Layer(horse));
            var (visual, reasons) = DomainBridge.FromPresentation(resolution);
            if (visual != null && VisualCatalog.Instance.Check(visual).Supported) descriptor = visual;
            else GD.Print($"[stable-hub] {horse.Name} shown as a silhouette: {string.Join("; ", reasons.Concat(visual == null ? Array.Empty<string>() : VisualCatalog.Instance.Check(visual).Problems))}");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[stable-hub] {horse.Name} could not be resolved for display: {ex.Message}");
        }
        _descriptors[key] = descriptor;
        return descriptor;
    }

    private void RefreshYard()
    {
        if (_state == null) return;
        var shown = _state.Horses
            .Where(h => !HorseMarket.IsSold(h) && h.OwnerId.Equals(_state.PlayerOwnerId) && h.LifeState.Stage != LifeStage.Unborn)
            .OrderBy(h => h.Name, StringComparer.Ordinal)
            .ToList();
        _yard.SetHorses(shown.Select(h => (h.HorseId.ToString(), DescriptorFor(h))).ToList(),
            shown.ToDictionary(h => h.HorseId.ToString(), h => h.Name),
            MothersOf(shown));
    }

    /// <summary>D-030: foal id to mare id for foals whose mare is also in the yard (presentation only).</summary>
    private static Dictionary<string, string> MothersOf(IReadOnlyList<HorseRecord> shown)
    {
        var mothers = new Dictionary<string, string>();
        foreach (var foal in shown)
        {
            if (foal.LifeState.VisualAge != VisualAge.Foal) continue;
            foreach (var parent in new[] { foal.Parentage.ParentAId, foal.Parentage.ParentBId })
                if (parent != null && shown.FirstOrDefault(h => h.HorseId.Equals(parent)) is { } mare && mare.ReproductionState.Role == ReproductiveRole.Mare)
                    mothers[foal.HorseId.ToString()] = mare.HorseId.ToString();
        }
        return mothers;
    }

    private static readonly Shader PortraitShader = GD.Load<Shader>("res://shaders/portrait_card.gdshader");

    private static ShaderMaterial PortraitMaterial(Color background)
    {
        var material = new ShaderMaterial { Shader = PortraitShader };
        material.SetShaderParameter("background", background);
        return material;
    }

    /// <summary>A rounded portrait card that fills in when the render finishes.</summary>
    private Control Portrait(HorseRecord horse, int size, Color background, bool fullBody = false)
    {
        var material = PortraitMaterial(background);
        var picture = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = material, Texture = Silhouette(),
        };
        var descriptor = DescriptorFor(horse);
        if (descriptor == null) return picture;
        if (_portraits.TryGetCached(descriptor, out var ready, fullBody)) { picture.Texture = ready ?? Silhouette(); return picture; }
        _ = SetPortraitAsync(picture, descriptor, fullBody);
        return picture;
    }

    private async System.Threading.Tasks.Task SetPortraitAsync(TextureRect picture, VisualDescriptor descriptor, bool fullBody = false)
    {
        var texture = await _portraits.RenderAsync(descriptor, fullBody);
        if (IsInstanceValid(picture)) picture.Texture = texture ?? Silhouette();
    }

    private static ImageTexture? _silhouette;

    /// <summary>Neutral placeholder: never a substituted coat.</summary>
    private static ImageTexture Silhouette() => _silhouette ??= CreateSilhouette();

    private static ImageTexture CreateSilhouette()
    {
        const int n = 96;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var ink = new Color(CozyTheme.SoftText, 0.35f);
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            // A horseshoe: an open ring, thicker at the toe, with nail holes.
            float dx = x - 48f, dy = y - 44f, r = MathF.Sqrt(dx * dx + dy * dy);
            bool open = dy < -6f && MathF.Abs(dx) < 15f;
            float thickness = 9f + MathF.Max(0f, dy) * 0.12f;
            bool shoe = r < 30f && r > 30f - thickness && !open;
            bool nail = MathF.Abs(r - (30f - thickness / 2f)) < 1.6f && (MathF.Abs(MathF.Atan2(dy, dx) * 180f / MathF.PI % 30f) < 4f);
            image.SetPixel(x, y, shoe && !nail ? ink : new Color(0, 0, 0, 0));
        }
        return ImageTexture.CreateFromImage(image);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (HandlePhotoInput(@event)) { GetViewport().SetInputAsHandled(); return; }
        if (@event is InputEventMouseMotion motion) { _yard.Hover(motion.Position); return; }
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click || _state == null) return;
        var key = _yard.PickHorse(click.Position);
        if (key == null && _yard.PickProp(click.Position) is { } prop)
        {
            GetViewport().SetInputAsHandled();
            Audio.GameAudio.Play("ui_click", -8);
            if (prop == "fair" && !_fairButton.Disabled) ShowFair();
            else if (prop == "trophy") { ShowPractice(true); }
            return;
        }
        if (key == null) { _yard.Focus(null); CloseCareBubble(); return; }
        var horse = _state.Horses.FirstOrDefault(h => h.HorseId.ToString() == key);
        if (horse == null) return;
        _yard.Focus(key);
        Audio.GameAudio.Play("hoof_clop", -8);
        if (horse.LifeState.Stage == LifeStage.Unborn) ShowInspection(horse.HorseId);
        else ShowCareBubble(horse.HorseId, click.Position); // D-021: pat, brush, carrot or Details
        GetViewport().SetInputAsHandled();
    }
}
