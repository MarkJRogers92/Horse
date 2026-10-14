using System;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// Dress-up (D-020): pick a saddle pad and a mane bow. Each choice is one free save command; the portrait and the
/// horse in the yard update straight away. Locked colours show how to earn them.
/// </summary>
public partial class StableDay
{
    private Control _tackOverlay = null!;
    private Label _tackTitle = null!;
    private HBoxContainer _tackPreview = null!;
    private VBoxContainer _tackRows = null!;
    private HorseId? _tackHorse;

    private void BuildTackOverlay(Control root)
    {
        _tackOverlay = CozyTheme.Dialog(root, "Dress up", 620, out var body, out var buttons, out _tackTitle);
        _tackPreview = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        body.AddChild(_tackPreview);
        _tackRows = new VBoxContainer(); _tackRows.AddThemeConstantOverride("separation", 6);
        body.AddChild(_tackRows);
        var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(140, 46) };
        done.Pressed += () => { _tackOverlay.Visible = false; if (_tackHorse != null) ShowInspection(_tackHorse); };
        buttons.AddChild(done);
        CozyTheme.FinishDialog(body, buttons);
    }

    private void ShowTack(HorseId horseId)
    {
        if (_state == null) return;
        var horse = _state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
        if (horse == null) return;
        _tackHorse = horseId;
        _tackTitle.Text = $"Dress up {horse.Name}";
        foreach (var child in _tackPreview.GetChildren()) child.QueueFree();
        _tackPreview.AddChild(Portrait(horse, 140, CardTint(horse), fullBody: true));
        foreach (var child in _tackRows.GetChildren()) child.QueueFree();
        foreach (var (slot, heading) in new[] { (Tack.PadSlot, "SADDLE PAD"), (Tack.BowSlot, "MANE BOW OR CROWN") })
        {
            var title = CozyTheme.Title(heading, 12); title.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
            _tackRows.AddChild(title);
            _tackRows.AddChild(SwatchRow(horse, slot));
        }
        // D-032 festival rewards: one compact row each (heading beside the swatches) so the dialog stays on screen.
        var festival = CozyTheme.Title("FESTIVAL REWARDS", 12); festival.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        _tackRows.AddChild(festival);
        foreach (var (slot, heading) in new[] { (Tack.HalterSlot, "Halter"), (Tack.BraidsSlot, "Braids"), (Tack.TailSlot, "Tail bow") })
        {
            var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 10);
            var name = new Label { Text = heading, CustomMinimumSize = new Vector2(84, 0), VerticalAlignment = VerticalAlignment.Center };
            name.AddThemeFontSizeOverride("font_size", 15);
            line.AddChild(name); line.AddChild(SwatchRow(horse, slot));
            _tackRows.AddChild(line);
        }
        _inspectOverlay.Visible = false;
        _tackOverlay.Visible = true;
    }

    private HFlowContainer SwatchRow(HorseRecord horse, string slot)
    {
        var row = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("h_separation", 8); row.AddThemeConstantOverride("v_separation", 8);
        var worn = Tack.Worn(horse, slot)?.Id;
        row.AddChild(Swatch(horse.HorseId, slot, null, "None", CozyTheme.Cream, worn == null, null));
        foreach (var item in Tack.Items.Where(i => i.Slot == slot))
            row.AddChild(Swatch(horse.HorseId, slot, item.Id, item.Label, Color.FromHtml(item.ColourHex), worn == item.Id,
                item.IsUnlocked(_state!) ? null : item.UnlockHint));
        return row;
    }

    private Button Swatch(HorseId horseId, string slot, string? itemId, string label, Color colour, bool selected, string? lockedHint)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(46, 46), TooltipText = lockedHint == null ? label : $"{label} · {lockedHint} to unlock",
            Disabled = lockedHint != null || _busy || _recoveryRequired,
        };
        var style = CozyTheme.Box(lockedHint == null ? colour : colour.Lerp(new Color(0.85f, 0.85f, 0.88f), 0.6f), 23, 4);
        style.BorderColor = selected ? CozyTheme.AccentDeep : Colors.White; style.SetBorderWidthAll(selected ? 4 : 2);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            button.AddThemeStyleboxOverride(state, state == "hover" ? HoverStyle(style) : style);
        if (itemId == null) button.Text = "—";
        else if (lockedHint != null) button.Icon = CozyTheme.Icon("gallop");
        button.AddThemeConstantOverride("icon_max_width", 26);
        button.Pressed += () => _ = WearAsync(horseId, slot, itemId);
        return button;
    }

    private static StyleBoxFlat HoverStyle(StyleBoxFlat style)
    {
        var hover = (StyleBoxFlat)style.Duplicate();
        hover.BgColor = style.BgColor.Lightened(0.15f);
        return hover;
    }

    private async Task WearAsync(HorseId horseId, string slot, string? itemId)
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        _busy = true;
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision;
            _state = await Task.Run(() => _store.CommitTack(owner, horseId, slot, itemId, revision));
            Audio.GameAudio.Play("care_cheer", -10);
        }
        catch (Exception ex)
        {
            SetStatus("The new look was not saved. " + PlainError(ex));
        }
        finally
        {
            _busy = false;
            Refresh();
            ShowTack(horseId);
        }
    }
}
