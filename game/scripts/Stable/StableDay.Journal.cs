using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// Stable Journal (D-016): ribbons worked out from the saved stable itself, so there is no new save
/// data and nothing can be lost or double-counted. Which ribbons the player has already been shown
/// is a display preference stored next to the save.
/// </summary>
public partial class StableDay
{
    private sealed record Ribbon(string Id, string Title, string Hint, string Icon, Func<StableSnapshot, bool> Earned);

    private static readonly Ribbon[] Ribbons =
    {
        new("first_foal", "First Foal", "Welcome a foal born in your stable.", "foal",
            s => s.Horses.Any(h => h.OriginCommitId != null && h.BirthDay.HasValue)),
        new("named", "Name Giver", "Choose a name for a foal.", "foal",
            s => s.Horses.Any(h => h.OriginCommitId != null && h.BirthDay.HasValue && h.Name != "New foal")),
        new("breeder", "Budding Breeder", "Raise three foals of your own.", "breeding",
            s => s.Horses.Count(h => h.OriginCommitId != null && h.BirthDay.HasValue) >= 3),
        new("grown", "All Grown Up", "Raise a foal to adulthood.", "day",
            s => s.Horses.Any(h => h.OriginCommitId != null && h.LifeState.Stage == LifeStage.Adult)),
        new("sale", "Happy Home", "Sell a horse you bred to the riding club.", "sell",
            s => s.Horses.Any(HorseMarket.IsSold)),
        new("trained", "Galloper", "Train a horse's gallop to 30%.", "gallop",
            s => s.Horses.Any(h => PracticeTraining.Skill(h) >= 3000)),
        new("champion", "Speed Star", "Train a horse's gallop to 60%.", "gallop",
            s => s.Horses.Any(h => PracticeTraining.Skill(h) >= 6000)),
        new("pampered", "Pampered Herd", "Get every horse's grooming and turnout to 80%.", "grooming",
            s => ActiveHorses(s).Any() && ActiveHorses(s).All(h => s.DailyState.For(h.HorseId) is var c && c.Grooming >= 8000 && c.Turnout >= 8000)),
        new("full_house", "Full House", "Have four horses at home at once.", "turnout",
            s => ActiveHorses(s).Count() >= 4),
        new("savings", "Nest Egg", "Save up 1,000 coins.", "coins", s => s.Coins >= 1000),
        new("season", "A Whole Season", "Reach the summer of your first year.", "day",
            s => s.Header.CalendarDay >= StableCalendar.DaysPerSeason),
        new("year", "Happy Anniversary", "Care for your stable for a whole year.", "end_day",
            s => s.Header.CalendarDay >= StableCalendar.DaysPerSeason * 4),
    };

    private static IEnumerable<HorseRecord> ActiveHorses(StableSnapshot s) => s.Horses.Where(h =>
        h.OwnerId.Equals(s.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn);

    private Control _journalOverlay = null!;
    private GridContainer _journalGrid = null!;
    private Label _journalSummary = null!;
    private Button _journalButton = null!;
    private HashSet<string> _seenRibbons = new();
    private bool _ribbonsLoaded;

    private string RibbonPath => Path.Combine(Path.GetDirectoryName(_store.SavePath) ?? ".", "journal.cfg");

    private void BuildJournalOverlay(Control root)
    {
        _journalOverlay = CozyTheme.Dialog(root, "Stable Journal", 700, out var body, out var buttons, out _);
        _journalSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _journalSummary.AddThemeFontSizeOverride("font_size", 15);
        body.AddChild(_journalSummary);
        _journalGrid = new GridContainer { Columns = 3 };
        _journalGrid.AddThemeConstantOverride("h_separation", 10); _journalGrid.AddThemeConstantOverride("v_separation", 10);
        body.AddChild(_journalGrid);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(130, 46) };
        close.Pressed += () => _journalOverlay.Visible = false;
        buttons.AddChild(close);
        CozyTheme.FinishDialog(body, buttons);
    }

    private Button JournalButton()
    {
        _journalButton = CozyTheme.WithIcon(new Button { Text = "Journal", CustomMinimumSize = new Vector2(130, 44) }, "gallop", 26);
        _journalButton.Pressed += ShowJournal;
        return _journalButton;
    }

    private void ShowJournal()
    {
        if (_state == null) return;
        foreach (var child in _journalGrid.GetChildren()) child.QueueFree();
        var earned = Ribbons.Where(r => r.Earned(_state)).Select(r => r.Id).ToHashSet();
        _journalSummary.Text = $"{earned.Count} of {Ribbons.Length} ribbons earned. They come from what really happens in your stable.";
        foreach (var ribbon in Ribbons) _journalGrid.AddChild(RibbonCard(ribbon, earned.Contains(ribbon.Id)));
        _journalOverlay.Visible = true;
    }

    private static Control RibbonCard(Ribbon ribbon, bool earned)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(205, 0) };
        var style = CozyTheme.Box(earned ? Color.FromHtml("#fff4d6") : new Color(1, 1, 1, 0.55f), 18, 8);
        style.BorderColor = earned ? Color.FromHtml("#f5c542") : CozyTheme.Border; style.SetBorderWidthAll(earned ? 3 : 2);
        card.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); card.AddChild(row);
        var icon = new TextureRect { Texture = CozyTheme.Icon(ribbon.Icon), CustomMinimumSize = new Vector2(44, 44), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Modulate = earned ? Colors.White : new Color(1, 1, 1, 0.3f) };
        row.AddChild(icon);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 0);
        var title = CozyTheme.Title(earned ? ribbon.Title : "? ? ?", 15);
        if (!earned) title.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        var hint = new Label { Text = ribbon.Hint, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeFontSizeOverride("font_size", 12); hint.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        text.AddChild(title); text.AddChild(hint); row.AddChild(text);
        return card;
    }

    /// <summary>After any state change: announce ribbons the player has not been shown yet.</summary>
    private void CheckRibbons()
    {
        if (_state == null || _recoveryRequired) return;
        if (!_ribbonsLoaded)
        {
            _ribbonsLoaded = true;
            var config = new ConfigFile();
            if (config.Load(RibbonPath) == Error.Ok)
                _seenRibbons = ((string)config.GetValue("journal", "seen", "")).Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            else
            {
                // A stable opened for the first time with the journal: quietly accept what is already earned.
                _seenRibbons = Ribbons.Where(r => r.Earned(_state)).Select(r => r.Id).ToHashSet();
                SaveSeenRibbons();
            }
        }
        var fresh = Ribbons.Where(r => !_seenRibbons.Contains(r.Id) && r.Earned(_state)).ToList();
        if (fresh.Count == 0) return;
        foreach (var r in fresh) _seenRibbons.Add(r.Id);
        SaveSeenRibbons();
        if (_yard.Wandering) ShowRibbonToast(fresh);
    }

    private void SaveSeenRibbons()
    {
        var config = new ConfigFile();
        config.SetValue("journal", "seen", string.Join(",", _seenRibbons.OrderBy(s => s, StringComparer.Ordinal)));
        config.Save(RibbonPath);
    }

    private void ShowRibbonToast(IReadOnlyList<Ribbon> ribbons)
    {
        var toast = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        var style = CozyTheme.Box(Color.FromHtml("#fff4d6"), 22, 10); style.BorderColor = Color.FromHtml("#f5c542"); style.SetBorderWidthAll(3);
        style.ShadowColor = new Color(0.4f, 0.25f, 0.1f, 0.25f); style.ShadowSize = 12;
        toast.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); toast.AddChild(row);
        row.AddChild(new TextureRect { Texture = CozyTheme.Icon(ribbons[0].Icon), CustomMinimumSize = new Vector2(48, 48), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        var text = new VBoxContainer(); text.AddThemeConstantOverride("separation", 0);
        var heading = CozyTheme.Title("NEW RIBBON", 11); heading.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        text.AddChild(heading);
        text.AddChild(CozyTheme.Title(string.Join(" · ", ribbons.Select(r => r.Title)), 18));
        row.AddChild(text);
        toast.AnchorLeft = toast.AnchorRight = 0.5f; toast.OffsetLeft = -170; toast.OffsetTop = 214;
        _uiLayer.AddChild(toast);
        Audio.GameAudio.Play("coins", -6);
        var tween = CreateTween();
        tween.TweenInterval(4.0); // let the day transition finish first
        tween.TweenProperty(toast, "modulate", Colors.White, 0.35);
        tween.TweenInterval(3.2);
        tween.TweenProperty(toast, "modulate", new Color(1, 1, 1, 0), 0.6);
        tween.TweenCallback(Callable.From(toast.QueueFree));
    }
}
