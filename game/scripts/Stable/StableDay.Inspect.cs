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
    private readonly Dictionary<HorseId, Button> _inspectButtons = new();

    private void BuildInspectOverlay(Control root)
    {
        _inspectOverlay = new Control { Visible = false };
        _inspectOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_inspectOverlay);
        var dim = new ColorRect { Color = new Color(0.16f, 0.11f, 0.17f, 0.52f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _inspectOverlay.AddChild(dim);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect); _inspectOverlay.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) }; center.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12); panel.AddChild(box);
        _inspectTitle = CozyTheme.Title("", 26); box.AddChild(_inspectTitle);
        _inspectDetails = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 0) };
        box.AddChild(_inspectDetails);
        var buttons = new HBoxContainer(); buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(150, 44) };
        close.Pressed += () => _inspectOverlay.Visible = false;
        buttons.AddChild(close); box.AddChild(buttons);
    }

    private Button InspectButton(HorseId horseId)
    {
        var button = new Button { Text = "Details", CustomMinimumSize = new Vector2(110, 34), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
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
            _inspectTitle.Text = profile.Name;
            _inspectDetails.Text = string.Join("\n", InspectionLines(profile));
            _inspectOverlay.Visible = true;
            _guideSawDetails = true; RefreshGuide();
        }
        catch (Exception ex)
        {
            SetStatus("This horse could not be shown. " + PlainError(ex));
        }
    }

    private static IEnumerable<string> InspectionLines(HorseProfile profile)
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
