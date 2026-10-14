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
        new("fair", "Fair Find", "Buy a horse at the horse fair.", "fair",
            s => s.Horses.Any(h => HorseFair.IsPurchased(h) && h.OwnerId.Equals(s.PlayerOwnerId))),
        new("colours", "Colourful Herd", "Collect five different coats.", "breeding",
            s => CollectedCoats(s).Count >= 5),
        new("cup", "Cup Champion", "Win the Willowbrook Cup.", "gallop",
            s => s.Ledger.Any(l => l.Kind == ClubRace.PrizeLedgerKind && l.DeltaCoins == ClubRace.PrizeFor(1))),
        new("style", "Stable Style", "Dress a horse in a saddle pad and a bow.", "grooming",
            s => s.Horses.Any(h => h.OwnerId.Equals(s.PlayerOwnerId) && Tack.Worn(h, Tack.PadSlot) != null && Tack.Worn(h, Tack.BowSlot) != null)),
        new("fashion", "Fashion Show", "Dress a horse in four different pieces at once.", "grooming",
            s => s.Horses.Any(h => h.OwnerId.Equals(s.PlayerOwnerId) && Tack.Slots.Count(slot => Tack.Worn(h, slot) != null) >= 4)),
        new("friends", "Best Friends", "Reach a full five-heart bond with a horse.", "breeding",
            s => s.Horses.Any(h => h.OwnerId.Equals(s.PlayerOwnerId) && HorseBond.Hearts(h) >= 5)),
        new("personalities", "Getting to Know You", "Meet all six personalities.", "breeding",
            s => s.Horses.Where(h => h.OwnerId.Equals(s.PlayerOwnerId) && h.LifeState.Stage != LifeStage.Unborn).Select(h => Personality.TraitId(s, h)).Distinct().Count() >= Personality.Ids.Count),
        new("gene_detective", "Gene Detective", "DNA-test three horses.", "breeding",
            s => DnaTest.TestedCount(s) >= 3),
        new("best_in_show", "Best in Show", "Win the Coat & Turnout Show.", "grooming",
            s => s.Ledger.Any(l => l.Kind == HorseShow.PrizeLedgerKind && l.DeltaCoins == HorseShow.PrizeFor(1))),
        new("leopard_lover", "Leopard Lover", "Raise a homebred leopard appaloosa.", "foal", HasHomebredLeopard),
        new("silver_lining", "Silver Lining", "Own a grey that has turned light.", "day", HasLightGrey),
        new("pattern_collector", "Pattern Collector", "Collect tobiano, roan, grey and appaloosa.", "breeding",
            s => CollectedCoats(s) is var c && new[] { "tobiano_overlay", "roan_overlay", "grey_overlay", "leopard_complex_overlay" }.All(c.ContainsKey)),
        new("pip_helper", "Pip's Helper", "Finish all three daily tasks.", "coins",
            s => s.Ledger.Any(l => l.Kind == DailyTasks.StreakLedgerKind)),
        new("busy_week", "Busy Week", "Keep a 7-day task streak.", "day",
            s => s.Ledger.Any(l => l.Kind == DailyTasks.StreakLedgerKind && l.DeltaCoins == DailyTasks.MaxStreakCredit)),
        new("festival_friend", "Festival Friend", "Give festival treats on 3 days of one festival.", "fair",
            s => Festivals.SeasonIds.Any(id => Festivals.RewardUnlocked(s, id))),
        new("year_of_festivals", "Year of Festivals", "Unlock all four festival rewards.", "fair",
            s => Festivals.RewardsUnlocked(s) == Festivals.SeasonIds.Count),
        new("generations", "Three Generations", "Raise a foal whose grandparents were born at your stable.", "foal", HasThreeGenerations),
        new("savings", "Nest Egg", "Save up 1,000 coins.", "coins", s => s.Coins >= 1000),
        new("season", "A Whole Season", "Reach the summer of your first year.", "day",
            s => s.Header.CalendarDay >= StableCalendar.DaysPerSeason),
        new("year", "Happy Anniversary", "Care for your stable for a whole year.", "end_day",
            s => s.Header.CalendarDay >= StableCalendar.DaysPerSeason * 4),
    };

    private static bool HasHomebredLeopard(StableSnapshot s) => s.Horses.Any(h => h.OwnerId.Equals(s.PlayerOwnerId)
        && (h.Parentage.ParentAId != null || h.Parentage.ParentBId != null)
        && new PhenotypeResolver().ResolveGenetics(h).Phenotype is { } p && p.CoatLayers.Contains("leopard_complex") && p.Patn1Coverage > 0);

    // A homebred horse whose sire or dam is itself homebred (born here from a recorded breeding), so a grandparent was born at the stable.
    private static bool HasThreeGenerations(StableSnapshot s)
    {
        var bred = s.Horses.Where(h => h.OriginCommitId != null && h.BirthDay.HasValue).Select(h => h.HorseId).ToHashSet();
        return s.Horses.Any(h => h.OwnerId.Equals(s.PlayerOwnerId) && h.OriginCommitId != null
            && s.Horses.Where(p => p.HorseId.Equals(h.Parentage.ParentAId) || p.HorseId.Equals(h.Parentage.ParentBId))
                .Any(p => new[] { p.Parentage.ParentAId, p.Parentage.ParentBId }.Any(g => g != null && bred.Contains(g))));
    }

    private static bool HasLightGrey(StableSnapshot s) => s.Horses.Any(h => h.OwnerId.Equals(s.PlayerOwnerId)
        && new PhenotypeResolver().ResolveGenetics(h).Phenotype is { } p && p.CoatLayers.Contains("grey_age") && p.GrayAmount >= 6000);

    private static IEnumerable<HorseRecord> ActiveHorses(StableSnapshot s) => s.Horses.Where(h =>
        h.OwnerId.Equals(s.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable && h.LifeState.Stage != LifeStage.Unborn);

    private Control _journalOverlay = null!;
    private GridContainer _journalGrid = null!;
    private Label _journalSummary = null!;
    private Button _journalButton = null!;
    private Button _journalRibbonTab = null!;
    private Button _journalCoatTab = null!;
    private HashSet<string> _seenRibbons = new();
    private bool _ribbonsLoaded;

    private string RibbonPath => Path.Combine(Path.GetDirectoryName(_store.SavePath) ?? ".", "journal.cfg");

    private void BuildJournalOverlay(Control root)
    {
        _journalOverlay = CozyTheme.Dialog(root, "Stable Journal", 900, out var body, out var buttons, out _);
        var tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tabs.AddThemeConstantOverride("separation", 8);
        var group = new ButtonGroup();
        _journalRibbonTab = new Button { Text = "Ribbons", ToggleMode = true, ButtonGroup = group, ButtonPressed = true, CustomMinimumSize = new Vector2(150, 40) };
        _journalCoatTab = new Button { Text = "Coat collection", ToggleMode = true, ButtonGroup = group, CustomMinimumSize = new Vector2(190, 40) };
        _journalRibbonTab.Pressed += () => ShowJournalPage(false);
        _journalCoatTab.Pressed += () => ShowJournalPage(true);
        tabs.AddChild(_journalRibbonTab); tabs.AddChild(_journalCoatTab);
        body.AddChild(tabs);
        _journalSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _journalSummary.AddThemeFontSizeOverride("font_size", 15);
        body.AddChild(_journalSummary);
        _journalGrid = new GridContainer { Columns = 3 };
        _journalGrid.AddThemeConstantOverride("h_separation", 10); _journalGrid.AddThemeConstantOverride("v_separation", 10);
        // Scrolls once the ribbons outgrow the window (21+).
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 380), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_journalGrid);
        body.AddChild(scroll);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(130, 46) };
        close.Pressed += () => _journalOverlay.Visible = false;
        buttons.AddChild(close);
        CozyTheme.FinishDialog(body, buttons);
    }

    private Button JournalButton()
    {
        _journalButton = CozyTheme.WithIcon(new Button { Text = "Journal", CustomMinimumSize = new Vector2(130, 44) }, "journal", 26);
        _journalButton.Pressed += ShowJournal;
        return _journalButton;
    }

    private void ShowJournal()
    {
        if (_state == null) return;
        _journalRibbonTab.ButtonPressed = true;
        ShowJournalPage(false);
        _journalOverlay.Visible = true;
        Audio.GameAudio.Play("page", -10);
    }

    private void ShowJournalPage(bool coats)
    {
        if (_state == null) return;
        if (_journalOverlay.Visible) Audio.GameAudio.Play("page", -12);
        foreach (var child in _journalGrid.GetChildren()) child.QueueFree();
        if (coats)
        {
            var collected = CollectedCoats(_state);
            _journalSummary.Text = $"{collected.Count} of {CoatCollection.Length} coats collected. Breed foals or visit the Horse fair to find new colours.";
            _journalGrid.Columns = 5;
            foreach (var (id, label) in CoatCollection) _journalGrid.AddChild(CoatCard(label, collected.TryGetValue(id, out var horse) ? horse : null));
            return;
        }
        var earned = Ribbons.Where(r => r.Earned(_state)).Select(r => r.Id).ToHashSet();
        _journalSummary.Text = $"{earned.Count} of {Ribbons.Length} ribbons earned. They come from what really happens in your stable.";
        _journalGrid.Columns = 4;
        foreach (var ribbon in Ribbons) _journalGrid.AddChild(RibbonCard(ribbon, earned.Contains(ribbon.Id)));
    }

    /// <summary>The 13 coats D-013 released, the 6 D-026 added (cream duns and the pattern overlays) and the 6 D-037 added
    /// (silver, champagne, pearl, cream pearl, sabino, splash), in collecting order.</summary>
    private static readonly (string Id, string Label)[] CoatCollection =
    {
        ("chestnut", "Chestnut"), ("bay", "Bay"), ("black", "Black"), ("palomino", "Palomino"), ("buckskin", "Buckskin"),
        ("smoky_black", "Smoky black"), ("cremello", "Cremello"), ("perlino", "Perlino"), ("smoky_cream", "Smoky cream"),
        ("red_dun", "Red dun"), ("bay_dun", "Bay dun"), ("grullo", "Grullo"), ("non_dun1_marked", "Primitive marked"),
        ("dunalino_family", "Dunalino & dunskin"), ("double_cream_dun", "Double cream dun"), ("tobiano_overlay", "Tobiano"),
        ("roan_overlay", "Roan"), ("grey_overlay", "Grey"), ("leopard_complex_overlay", "Appaloosa"),
        ("silver_overlay", "Silver"), ("champagne_overlay", "Champagne"), ("pearl", "Pearl"), ("cream_pearl", "Cream pearl"),
        ("sabino1_overlay", "Sabino"), ("splash_overlay", "Splash"),
    };

    private static readonly string[] PatternCoats = { "tobiano_overlay", "roan_overlay", "grey_overlay", "leopard_complex_overlay",
        "silver_overlay", "champagne_overlay", "sabino1_overlay", "splash_overlay" };

    /// <summary>Every horse that has lived at this stable (sold horses keep their records), first one per coat.</summary>
    private static Dictionary<string, HorseRecord> CollectedCoats(StableSnapshot state)
    {
        var found = new Dictionary<string, HorseRecord>(StringComparer.Ordinal);
        var resolver = new PhenotypeResolver();
        foreach (var horse in state.Horses.Where(h => h.LifeState.Stage != LifeStage.Unborn
                     && (h.OwnerId.Equals(state.PlayerOwnerId) || HorseMarket.IsSold(h))).OrderBy(h => h.BirthDay ?? -1).ThenBy(h => h.Name, StringComparer.Ordinal))
        {
            try
            {
                var genetics = resolver.ResolveGenetics(horse);
                if (genetics.Phenotype == null) continue;
                var id = genetics.RequiredCoatClasses.Contains("non_dun1_marked") ? "non_dun1_marked" : genetics.Phenotype.CoatClassId;
                // A pattern counts on its own page as well as the colour underneath it (a bay tobiano fills both).
                foreach (var each in PatternCoats.Where(genetics.RequiredCoatClasses.Contains).Prepend(id))
                    if (!found.ContainsKey(each)) found[each] = horse;
            }
            catch (Exception) { }
        }
        return found;
    }

    private Control CoatCard(string label, HorseRecord? horse)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(118, 0) };
        var style = CozyTheme.Box(horse != null ? Color.FromHtml("#fff4d6") : new Color(1, 1, 1, 0.55f), 16, 6);
        style.BorderColor = horse != null ? Color.FromHtml("#f5c542") : CozyTheme.Border; style.SetBorderWidthAll(horse != null ? 3 : 2);
        card.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 2); card.AddChild(box);
        Control picture;
        if (horse != null) picture = Portrait(horse, 92, CardTint(horse));
        else picture = new TextureRect { Texture = Silhouette(), CustomMinimumSize = new Vector2(92, 92), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Modulate = new Color(1, 1, 1, 0.5f) };
        picture.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(picture);
        var title = CozyTheme.Title(label, 13); title.HorizontalAlignment = HorizontalAlignment.Center;
        if (horse == null) title.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        box.AddChild(title);
        var who = new Label { Text = horse?.Name ?? "not found yet", HorizontalAlignment = HorizontalAlignment.Center };
        who.AddThemeFontSizeOverride("font_size", 11); who.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        box.AddChild(who);
        return card;
    }

    private static Control RibbonCard(Ribbon ribbon, bool earned)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(200, 0) };
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
        Audio.GameAudio.Play("ribbon", -5);
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
