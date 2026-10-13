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
            await CaptureFrame(System.IO.Path.Combine(output, "04_yard_only.png"));
            for (var s = 0; s < 4; s++)
            {
                _yard.SetSeason(s);
                var before = _yard.ElapsedSeconds;
                while (_yard.ElapsedSeconds - before < 2.5) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, $"04_season_{s}_{StableYard.SeasonName(s).ToLowerInvariant()}.png"));
            }
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
            ShowFair();
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
            var resolution = new PhenotypeResolver().ResolveForPresentation(horse, DomainBridge.Matrix, VisualCatalog.Instance.Version, AnatomicalMaskSetId);
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
            shown.ToDictionary(h => h.HorseId.ToString(), h => h.Name));
    }

    private static readonly Shader PortraitShader = GD.Load<Shader>("res://shaders/portrait_card.gdshader");

    private static ShaderMaterial PortraitMaterial(Color background)
    {
        var material = new ShaderMaterial { Shader = PortraitShader };
        material.SetShaderParameter("background", background);
        return material;
    }

    /// <summary>A rounded portrait card that fills in when the render finishes.</summary>
    private Control Portrait(HorseRecord horse, int size, Color background)
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
        if (_portraits.TryGetCached(descriptor, out var ready)) { picture.Texture = ready ?? Silhouette(); return picture; }
        _ = SetPortraitAsync(picture, descriptor);
        return picture;
    }

    private async System.Threading.Tasks.Task SetPortraitAsync(TextureRect picture, VisualDescriptor descriptor)
    {
        var texture = await _portraits.RenderAsync(descriptor);
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
        if (@event is InputEventMouseMotion motion) { _yard.Hover(motion.Position); return; }
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click || _state == null) return;
        var key = _yard.PickHorse(click.Position);
        if (key == null) { _yard.Focus(null); return; }
        var horse = _state.Horses.FirstOrDefault(h => h.HorseId.ToString() == key);
        if (horse == null) return;
        _yard.Focus(key);
        ShowInspection(horse.HorseId);
        GetViewport().SetInputAsHandled();
    }
}
