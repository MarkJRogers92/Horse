using System;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

public partial class StableDay
{
    private Control _pastureOverlay = null!;
    private VBoxContainer _pastureRows = null!;
    private Label _pastureSummary = null!;
    private Button _pastureButton = null!;

    private Button PastureButton()
    {
        _pastureButton = new Button { Text = "Pasture", CustomMinimumSize = new Vector2(90, 36),
            TooltipText = "Keep your favourite adults in free cooperative pasture and free a stall." };
        _pastureButton.Pressed += ShowPasture;
        return _pastureButton;
    }

    private void BuildPastureOverlay(Control root)
    {
        _pastureOverlay = CozyTheme.Dialog(root, "Cooperative pasture", 720, out var body, out var buttons, out _);
        var note = SmallText("Keep your favourites with you. Pasture is free, frees a stall, and preserves family, bond and care. Return a horse when you have a free stall to resume activities.");
        note.CustomMinimumSize = new Vector2(670, 0); body.AddChild(note);
        _pastureSummary = CozyTheme.Title("", 17); body.AddChild(_pastureSummary);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(670, 310), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        _pastureRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pastureRows.AddThemeConstantOverride("separation", 10); scroll.AddChild(_pastureRows);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(100, 44) };
        close.Pressed += () => { if (!_busy) _pastureOverlay.Visible = false; };
        buttons.AddChild(close); CozyTheme.FinishDialog(body, buttons);
    }

    private void ShowPasture()
    {
        if (_state == null || _busy) return;
        CloseCareBubble(); _inspectOverlay.Visible = false;
        _pastureOverlay.Visible = true; RefreshPastureControls();
    }

    private void RefreshPastureControls()
    {
        if (_state == null || !IsInstanceValid(_pastureButton)) return;
        bool locked = _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null;
        _pastureButton.Disabled = locked;
        if (!IsInstanceValid(_pastureOverlay) || !_pastureOverlay.Visible) return;
        int boarded = _state.Horses.Count(h => h.OwnerId.Equals(_state.PlayerOwnerId) && h.HomeAssignment == HomeKind.CooperativePasture);
        _pastureSummary.Text = $"Active places: {HorsePasture.ActivePlacesUsed(_state)} / {StableShop.Stalls(_state)}  ·  In pasture: {boarded}";
        _pastureRows.QueueFreeChildren();
        foreach (var horse in _state.Horses.Where(h => h.OwnerId.Equals(_state.PlayerOwnerId) && !HorseMarket.IsSold(h)).OrderBy(h => h.Name))
        {
            bool inPasture = horse.HomeAssignment == HomeKind.CooperativePasture;
            var destination = inPasture ? HomeKind.ActiveStable : HomeKind.CooperativePasture;
            var reason = HorsePasture.BlockReason(_state, horse.HorseId, destination);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
            row.AddChild(Portrait(horse, 74, CardTint(horse)));
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddChild(CozyTheme.Title(horse.Name, 18));
            var line = SmallText(reason ?? (inPasture ? "Resting in pasture · still part of your family" : "Active stable · can rest in pasture"));
            line.CustomMinimumSize = new Vector2(350, 0); text.AddChild(line); row.AddChild(text);
            var button = new Button { Text = inPasture ? "Bring back" : "To pasture", CustomMinimumSize = new Vector2(130, 44),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Disabled = locked || reason != null, TooltipText = reason ?? "Free and reversible" };
            button.Pressed += () => _ = TransferPastureAsync(horse.HorseId, destination);
            row.AddChild(button); _pastureRows.AddChild(row);
        }
    }

    private async Task TransferPastureAsync(HorseId horseId, HomeKind destination)
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        var horse = _state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
        if (horse == null) return;
        var blocked = HorsePasture.BlockReason(_state, horseId, destination);
        if (blocked != null) { SetStatus(blocked); return; }
        _busy = true; RefreshPastureControls();
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision;
            _state = await Task.Run(() => _store.CommitPastureTransfer(owner, horseId, revision, horse.Revision, destination));
            _optionalCare.Remove(horseId); ClearReviewedPlan();
            SetStatus(destination == HomeKind.CooperativePasture ? $"{horse.Name} is resting in pasture. A stall is free, and you can bring them back anytime there is room."
                : $"Welcome back, {horse.Name}! Their stall and stable activities are ready.");
        }
        catch (Exception ex) { SetStatus("The move could not be confirmed. Reload before trying again. " + PlainError(ex)); }
        finally { _busy = false; Refresh(); }
    }
}
