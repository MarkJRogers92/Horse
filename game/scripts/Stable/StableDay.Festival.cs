using System;
using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// D-032 seasonal festivals in the stable UI: a small festival pill in the top bar, a ribbon toast on a festival's first day,
/// and the yard decorations toggled from <see cref="Refresh"/>. Everything shown is derived from the calendar day and the
/// saved horses by the domain <see cref="Festivals"/>; nothing here is stored.
/// </summary>
public partial class StableDay
{
    private PanelContainer _festivalPill = null!;
    private bool _festivalPreviewing;
    private string? _announcedFestival;

    /// <summary>A round "fair" icon beside the wallet pill, shown only during a festival week (the top bar is already full).</summary>
    private Control BuildFestivalPill()
    {
        _festivalPill = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        var style = CozyTheme.Box(Color.FromHtml("#fff4d6"), 22, 4); style.BorderColor = Color.FromHtml("#f5a8c4"); style.SetBorderWidthAll(3);
        _festivalPill.AddThemeStyleboxOverride("panel", style);
        _festivalPill.AddChild(new TextureRect { Texture = CozyTheme.Icon("fair"), CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore });
        return _festivalPill;
    }

    private void RefreshFestival()
    {
        if (_state == null || _festivalPill == null) return;
        var festival = Festivals.Current(_state.Header.CalendarDay);
        _festivalPill.Visible = festival != null;
        if (festival != null)
        {
            var left = festival.DaysRemaining == 1 ? "last day" : $"{festival.DaysRemaining} days left";
            _festivalPill.TooltipText = $"{festival.Name} ({left}). Give your horses a {festival.TreatLower}: three treat days unlock the {Tack.Find(festival.RewardItemId)?.Label}.";
        }
        if (!_festivalPreviewing && _yard.Festival != festival?.Id) _yard.SetFestival(festival?.Id);
    }

    /// <summary>On the first day of a festival: a ribbon toast naming it and the treat to give (once per festival per session).</summary>
    private void AnnounceFestival()
    {
        if (_state == null || !_yard.Wandering) return;
        var festival = Festivals.Current(_state.Header.CalendarDay);
        if (festival == null || festival.DaysRemaining != Festivals.Length || _announcedFestival == festival.CountKey) return;
        _announcedFestival = festival.CountKey;
        Audio.GameAudio.Play("ribbon", -5);
        var toast = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        var style = CozyTheme.Box(Color.FromHtml("#fff4d6"), 22, 10); style.BorderColor = Color.FromHtml("#f5a8c4"); style.SetBorderWidthAll(3);
        style.ShadowColor = new Color(0.4f, 0.25f, 0.1f, 0.25f); style.ShadowSize = 12;
        toast.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); toast.AddChild(row);
        row.AddChild(new TextureRect { Texture = CozyTheme.Icon("fair"), CustomMinimumSize = new Vector2(48, 48), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        var text = new VBoxContainer(); text.AddThemeConstantOverride("separation", 0);
        var heading = CozyTheme.Title("FESTIVAL WEEK", 11); heading.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        text.AddChild(heading);
        text.AddChild(CozyTheme.Title($"{festival.Name} this week!", 18));
        var hint = new Label { Text = $"Give your horses a {festival.TreatLower}." };
        hint.AddThemeFontSizeOverride("font_size", 14); hint.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        text.AddChild(hint);
        row.AddChild(text);
        toast.AnchorLeft = toast.AnchorRight = 0.5f; toast.OffsetLeft = -190; toast.OffsetTop = 392; // below Pip's tasks, over the yard
        _uiLayer.AddChild(toast);
        var tween = CreateTween();
        tween.TweenInterval(4.0); // let the day transition finish first
        tween.TweenProperty(toast, "modulate", Colors.White, 0.35);
        tween.TweenInterval(4.0);
        tween.TweenProperty(toast, "modulate", new Color(1, 1, 1, 0), 0.6);
        tween.TweenCallback(Callable.From(toast.QueueFree));
    }

    /// <summary>Pip's festival tip: a horse to treat during the festival, or the next festival's countdown otherwise.</summary>
    private string? FestivalTip(System.Collections.Generic.IReadOnlyList<HorseRecord> home)
    {
        if (_state == null) return null;
        var day = _state.Header.CalendarDay;
        if (Festivals.Current(day) is { } festival)
        {
            var reward = Tack.Find(festival.RewardItemId);
            var unlocked = reward != null && reward.IsUnlocked(_state);
            if (home.FirstOrDefault(h => Festivals.BlockReason(_state, h.HorseId) == null) is { } hungry)
                return $"Tip: it's the {festival.Name}! Give {hungry.Name} a {festival.TreatLower}" + (unlocked ? "." : $". Three treat days unlock the {reward?.Label}.");
            return null;
        }
        return null;
    }

    /// <summary>--hub-preview=DIR --hub-festival: plays to the first festival, treats the horses on three days, and captures the UI.</summary>
    private async System.Threading.Tasks.Task RunFestivalUiPreviewAsync(string output)
    {
        async System.Threading.Tasks.Task Frames(double seconds)
        {
            var from = _yard.ElapsedSeconds;
            while (_yard.ElapsedSeconds - from < seconds) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async System.Threading.Tasks.Task EndDay()
        {
            ShowReview(); ConfirmReviewedDay();
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        string Path(string name) => System.IO.Path.Combine(output, name);
        _guideDismissed = true; Refresh();
        while (_state != null && _state.Header.CalendarDay < Festivals.FirstDayOfSeason - 1) { await EndDay(); await Frames(4.3); }
        await CaptureFrame(Path("f00_day_before.png"));
        await EndDay();
        await Frames(6.0); // the transition, then the festival toast
        await CaptureFrame(Path("f01_festival_toast.png"));
        await Frames(5.0);
        var horses = _state!.Horses.Where(h => h.OwnerId.Equals(_state.PlayerOwnerId) && h.LifeState.Stage == LifeStage.Adult).OrderBy(h => h.Name, StringComparer.Ordinal).ToList();
        var first = horses[0];
        var spot = GetViewport().GetVisibleRect().Size / 2 + new Vector2(-80, 40);
        ShowCareBubble(first.HorseId, spot);
        await CaptureFrame(Path("f02_care_bubble_treat.png"));
        for (var treatDay = 1; treatDay <= Festivals.RewardThreshold; treatDay++)
        {
            foreach (var h in horses) await InteractAsync(h.HorseId, Festivals.TreatKind);
            CloseCareBubble();
            await Frames(0.7);
            if (treatDay == 1)
            {
                await CaptureFrame(Path("f03_treat_hearts_tasks.png"));
                ShowCareBubble(first.HorseId, spot);
                await CaptureFrame(Path("f04_care_bubble_done.png"));
                CloseCareBubble();
            }
            if (treatDay < Festivals.RewardThreshold) { await EndDay(); await Frames(4.3); }
        }
        await CaptureFrame(Path("f05_unlocked_status.png"));
        ShowTack(first.HorseId);
        do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
        await CaptureFrame(Path("f06_dress_up_unlocked.png"));
        await WearAsync(first.HorseId, Tack.BowSlot, "bow.blossom");
        do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); while (_portraits.Busy);
        await CaptureFrame(Path("f07_dress_up_crown.png"));
        _tackOverlay.Visible = false;
        ShowJournal();
        await CaptureFrame(Path("f08_journal_festival_friend.png"));
        _journalOverlay.Visible = false;
        await Frames(1.0);
        await CaptureFrame(Path("f09_yard_festival_day.png"));
    }

    private string NextFestivalLine()
    {
        var next = Festivals.Next(_state?.Header.CalendarDay ?? 0);
        return $"Next festival: {next.Name} in {Days(next.DaysUntilStart)}.";
    }
}
