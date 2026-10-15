using System;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

public partial class StableDay
{
    private Control _groomingOverlay = null!;
    private VBoxContainer _groomingBody = null!;
    private Label _groomingTitle = null!, _groomingProgress = null!;
    private Button _groomingFinish = null!, _groomingCancel = null!;
    private HBoxContainer _groomingPatchButtons = null!;
    private GroomingView? _groomingView;
    private HorseId? _groomingHorse;
    private int _groomingDay;

    private void BuildGroomingOverlay(Control root)
    {
        _groomingOverlay = CozyTheme.Dialog(root, "Groom by hand", 684, out _groomingBody, out var buttons, out _groomingTitle);
        _groomingBody.AddThemeConstantOverride("separation", 6);
        var hint = SmallText("Brush the three dusty patches by dragging or tapping. You can also use the buttons below. Finish earns today's usual brush bond; quick care is always available.");
        hint.CustomMinimumSize = new Vector2(640, 0); _groomingBody.AddChild(hint);
        _groomingProgress = CozyTheme.Title("", 16); _groomingBody.AddChild(_groomingProgress);
        _groomingPatchButtons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _groomingPatchButtons.AddThemeConstantOverride("separation", 10);
        for (int p = 0; p < 3; p++)
        {
            int patch = p;
            var button = new Button { Text = "Brush " + GroomingView.PatchNames[p].ToLowerInvariant(), CustomMinimumSize = new Vector2(200, 40) };
            button.Pressed += () => _groomingView?.BrushPatch(patch); _groomingPatchButtons.AddChild(button);
        }
        _groomingBody.AddChild(_groomingPatchButtons);
        _groomingCancel = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(110, 44) };
        _groomingCancel.Pressed += CloseGrooming; buttons.AddChild(_groomingCancel);
        _groomingFinish = new Button { Text = "Finish grooming", CustomMinimumSize = new Vector2(190, 44) };
        _groomingFinish.Pressed += () => _ = FinishGroomingAsync(); buttons.AddChild(_groomingFinish);
        CozyTheme.FinishDialog(_groomingBody, buttons);
    }

    private void ShowGrooming(HorseId horseId)
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        var reason = HorseBond.BlockReason(_state, horseId, "brush");
        if (reason != null) { SetStatus(reason); return; }
        CloseGrooming(); CloseCareBubble();
        var horse = _state.Horses.Single(h => h.HorseId.Equals(horseId));
        _groomingHorse = horseId; _groomingDay = _state.Header.CalendarDay;
        _groomingTitle.Text = "Groom " + horse.Name;
        var view = new GroomingView { CustomMinimumSize = new Vector2(640, 320) };
        _groomingView = view; _groomingBody.AddChild(view); _groomingBody.MoveChild(view, 2);
        try
        {
            view.Present(DescriptorFor(horse) ?? throw new InvalidOperationException("This horse has no supported appearance."));
            if (!view.Supported) throw new InvalidOperationException(view.SupportMessage);
            view.Changed += RefreshGrooming; view.CancelRequested += CloseGrooming;
            _groomingOverlay.Visible = true; RefreshGrooming();
        }
        catch (Exception ex) { CloseGrooming(); SetStatus("This grooming view could not open. Quick brush is still available. " + PlainError(ex)); }
    }

    private void RefreshGrooming()
    {
        if (_groomingView == null || !_groomingView.Supported) return;
        var session = _groomingView.Session;
        _groomingProgress.Text = session.Complete ? "All clean! Finish to save today's brush." : $"{session.CleanPatches} of 3 patches clean · {session.TotalStrokes} / {3 * GroomingSession.StrokesPerPatch} strokes";
        _groomingFinish.Disabled = !session.Complete || _busy || session.Saving;
        _groomingCancel.Disabled = _busy || session.Saving;
        for (int p = 0; p < 3; p++) ((Button)_groomingPatchButtons.GetChild(p)).Disabled = session.DirtRemaining(p) == 0 || _busy || session.Saving;
    }

    private void CloseGrooming()
    {
        if (_groomingView?.Session?.Saving == true) return;
        _groomingView?.Session?.Cancel();
        if (_groomingView != null) { _groomingBody.RemoveChild(_groomingView); _groomingView.QueueFree(); _groomingView = null; }
        _groomingHorse = null;
        if (IsInstanceValid(_groomingOverlay)) _groomingOverlay.Visible = false;
    }

    private async Task FinishGroomingAsync()
    {
        if (_state == null || _groomingHorse == null || _groomingView == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        var session = _groomingView.Session; var horseId = _groomingHorse;
        bool eligible = _state.Header.CalendarDay == _groomingDay && HorseBond.BlockReason(_state, horseId, "brush") == null;
        if (!eligible) { CloseGrooming(); SetStatus("The stable changed or this horse was already brushed. Open a fresh session when they are ready."); return; }
        if (!session.TryBeginFinish(eligible)) return;
        _busy = true; RefreshGrooming(); bool saved = false, expired = false;
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision;
            _state = await Task.Run(() => _store.CommitInteraction(owner, horseId, "brush", revision));
            saved = true;
            _yard.Cheer(horseId.ToString()); Audio.GameAudio.Play("care_cheer", -6);
            SetStatus($"{_state.Horses.Single(h => h.HorseId.Equals(horseId)).Name} is gleaming. Today's brush and bond are saved.");
        }
        catch (Exception ex)
        {
            // A write can be durable even when its response failed. Read the authoritative
            // slot before offering a retry; never award another brush to infer success.
            try
            {
                var loaded = await Task.Run(() => _store.Load());
                ApplyLoadedState(loaded);
                var current = _state!.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
                saved = !loaded.NeedsRecoveryAcknowledgment && _state.Header.CalendarDay == _groomingDay
                    && current != null && current.OwnerId.Equals(_state.PlayerOwnerId)
                    && HorseBond.DoneToday(current, "brush", _groomingDay);
                expired = loaded.NeedsRecoveryAcknowledgment || _state.Header.CalendarDay != _groomingDay
                    || (!saved && HorseBond.BlockReason(_state, horseId, "brush") != null);
                SetStatus(saved ? "Today's brush was already saved safely. Your bond was awarded once."
                    : expired ? "The stable changed. Open a fresh grooming session after checking the stable."
                    : "The brush was not saved. Your clean patches are ready; try Finish again.");
            }
            catch (Exception reloadError)
            {
                SetStatus("The brush could not be confirmed. Cancel and reload before continuing. " + PlainError(ex) + " " + PlainError(reloadError));
            }
        }
        finally
        {
            session.ResolveFinish(saved); _busy = false;
            if (saved || expired) CloseGrooming(); else RefreshGrooming();
            Refresh();
        }
    }
}
