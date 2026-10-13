using System;
using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>Read-only horse inspection (P2-01 slice). Shows only the domain's public HorseProfile.</summary>
public partial class StableDay
{
    private Control _inspectOverlay = null!;
    private Label _inspectTitle = null!;
    private Label _inspectDetails = null!;
    private HBoxContainer _inspectRow = null!;
    private VBoxContainer _inspectAbilities = null!;
    private Button _inspectSellButton = null!;
    private Button _inspectTackButton = null!;
    private HorseId? _inspectedHorse;
    private readonly Dictionary<HorseId, Button> _inspectButtons = new();

    private void BuildInspectOverlay(Control root)
    {
        _inspectOverlay = CozyTheme.Dialog(root, "", 640, out var body, out var buttons, out _inspectTitle);
        CozyTheme.DialogHeader(body, "details", 596, 96);
        _inspectRow = new HBoxContainer(); _inspectRow.AddThemeConstantOverride("separation", 16);
        _inspectDetails = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _inspectRow.AddChild(_inspectDetails);
        body.AddChild(_inspectRow);
        _inspectAbilities = new VBoxContainer(); _inspectAbilities.AddThemeConstantOverride("separation", 2);
        body.AddChild(_inspectAbilities);
        _inspectSellButton = CozyTheme.WithIcon(new Button { Text = "Ask for an offer", CustomMinimumSize = new Vector2(190, 46) }, "sell", 28);
        _inspectSellButton.Pressed += () => { if (_inspectedHorse != null) ShowSaleOffer(_inspectedHorse); };
        buttons.AddChild(_inspectSellButton);
        _inspectTackButton = new Button { Text = "Dress up", CustomMinimumSize = new Vector2(140, 46) };
        _inspectTackButton.Pressed += () => { if (_inspectedHorse != null) ShowTack(_inspectedHorse); };
        buttons.AddChild(_inspectTackButton);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(130, 46) };
        close.Pressed += () => { _inspectOverlay.Visible = false; _yard.Focus(null); };
        buttons.AddChild(close);
        CozyTheme.FinishDialog(body, buttons);
    }

    private Button InspectButton(HorseId horseId)
    {
        var button = new Button { Text = "Details", CustomMinimumSize = new Vector2(104, 38), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        button.Pressed += () => ShowInspection(horseId);
        _inspectButtons[horseId] = button;
        return button;
    }

    private void ShowInspection(HorseId horseId)
    {
        if (_state == null) return;
        try
        {
            var profile = HorseProfiles.For(_state, horseId);
            var horse = _state.Horses.Single(h => h.HorseId.Equals(horseId));
            _inspectedHorse = horseId;
            _inspectTitle.Text = profile.Name;
            _inspectDetails.Text = string.Join("\n", InspectionLines(profile, PracticeTraining.Skill(horse)));
            foreach (var old in _inspectRow.GetChildren().Where(c => c != _inspectDetails)) old.QueueFree();
            var portrait = Portrait(horse, 200, CardTint(horse)); portrait.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            _inspectRow.AddChild(portrait); _inspectRow.MoveChild(portrait, 0);
            _yard.Focus(horse.HorseId.ToString());
            FillAbilities(horse, profile);
            var offer = HorseMarket.Quote(_state, horseId);
            _inspectSellButton.Visible = horse.OriginCommitId != null && !HorseMarket.IsSold(horse);
            _inspectSellButton.Disabled = !offer.Allowed || _busy;
            _inspectTackButton.Visible = Tack.BlockReason(_state, horseId, Tack.PadSlot, null) == null;
            _inspectSellButton.TooltipText = offer.Allowed ? "" : offer.BlockReason ?? "";
            _inspectOverlay.Visible = true;
            _guideSawDetails = true; RefreshGuide();
        }
        catch (Exception ex)
        {
            SetStatus("This horse could not be shown. " + PlainError(ex));
        }
    }

    /// <summary>
    /// Star ratings for the abilities the practice gallop uses (developed traits, coarse 1-5 stars).
    /// They stay hidden until the horse has galloped once, so watching it race is how you learn them.
    /// </summary>
    private void FillAbilities(HorseRecord horse, HorseProfile profile)
    {
        foreach (var child in _inspectAbilities.GetChildren()) child.QueueFree();
        if (profile.Stage != LifeStage.Adult) return;
        var heading = CozyTheme.Title("ABILITIES", 11); heading.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        _inspectAbilities.AddChild(heading);
        var skill = PracticeTraining.Skill(horse);
        var traits = new PhenotypeResolver().ResolveGenetics(horse).Traits?.Developed;
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 2);
        foreach (var (key, label) in new[] { ("speed", "Speed"), ("acceleration", "Acceleration"), ("endurance", "Stamina"), ("composure", "Calm") })
        {
            var name = SmallText(label); name.CustomMinimumSize = new Vector2(100, 0);
            grid.AddChild(name);
            grid.AddChild(CozyTheme.StarRating(skill > 0 && traits != null ? traits.Get(key, 5000) : null));
        }
        _inspectAbilities.AddChild(grid);
        _inspectAbilities.AddChild(CareBar("Training", skill));
        var bond = new HBoxContainer(); bond.AddThemeConstantOverride("separation", 6);
        var bondName = SmallText("Bond"); bondName.CustomMinimumSize = new Vector2(64, 0); bond.AddChild(bondName);
        bond.AddChild(CozyTheme.HeartRating(HorseBond.Hearts(horse), 16));
        _inspectAbilities.AddChild(bond);
        if (skill == 0) _inspectAbilities.AddChild(SmallText("Take this horse for a practice gallop to discover its abilities. Each day's first gallop also trains it."));
    }

    private static IEnumerable<string> InspectionLines(HorseProfile profile, int gallopSkill)
    {
        var role = profile.Role switch { ReproductiveRole.Mare => "Mare", ReproductiveRole.Stallion => "Stallion", _ => "Horse" };
        var age = profile.AgeDays switch
        {
            null => "",
            0 => " · born today",
            1 => " · 1 day old",
            var days => $" · {days} days old"
        };
        yield return $"{role} · {StageName(profile.Stage)}{age} · {HomeName(profile.Home)}";
        yield return $"Coat: {profile.VisibleCoat}";
        yield return profile.IsFounder ? "Family: founding horse of this stable (parents not recorded)."
            : $"Parents: {profile.ParentAName ?? "unknown"} × {profile.ParentBName ?? "unknown"}";
        if (profile.OffspringNames.Count > 0) yield return "Offspring: " + string.Join(", ", profile.OffspringNames);
        if (profile.DueDay.HasValue) yield return $"Expecting a foal on calendar day {profile.DueDay.Value + 1}.";
        if (profile.Care != null)
            yield return $"Care: nutrition {Percent(profile.Care.Nutrition)} · bedding {Percent(profile.Care.Bedding)} · turnout {Percent(profile.Care.Turnout)} · grooming {Percent(profile.Care.Grooming)}";
        yield return "";
        if (profile.KnownGenetics.Count == 0)
            yield return "Known genetics: no DNA tests recorded yet. Breeding forecasts use only what is known.";
        else
        {
            yield return "Known genetics (recorded tests):";
            foreach (var call in profile.KnownGenetics) yield return $"• {call.Locus}: {call.Call}";
        }
    }
}
