using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Godot;

namespace Cloverbrook.Game.Stable;

public partial class StableDay
{
    private async Task LifeCaptureFrame(string path)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--stable-life-headless")) { await CaptureFrame(path); return; }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task RunStableLifeCaptureAsync(string output)
    {
        try
        {
            if (_state == null || _recoveryRequired || _state.Header.CalendarDay != 0)
                throw new InvalidOperationException("Stable life capture requires a fresh isolated save.");
            Directory.CreateDirectory(output);
            _nameStableOverlay.Visible = false;
            var horse = _state.Horses[0];
            var revision = _state.Header.SaveRevision;
            ShowPasture();
            await LifeCaptureFrame(Path.Combine(output, "01_pasture.png"));
            await TransferPastureAsync(horse.HorseId, HomeKind.CooperativePasture);
            if (_state.Horses.Single(h => h.HorseId.Equals(horse.HorseId)).HomeAssignment != HomeKind.CooperativePasture
                || HorsePasture.ActivePlacesUsed(_state) != 1) throw new Exception("Boarding did not free one active place.");
            await LifeCaptureFrame(Path.Combine(output, "02_boarded.png"));
            _state = _store.Load().State;
            await TransferPastureAsync(horse.HorseId, HomeKind.ActiveStable);
            if (_state.Header.SaveRevision != revision + 2 || HorsePasture.ActivePlacesUsed(_state) != 2)
                throw new Exception("Pasture round trip changed the wrong number of places or revisions.");
            _pastureOverlay.Visible = false;
            revision = _state.Header.SaveRevision;
            ShowGrooming(horse.HorseId);
            if (_groomingView == null || !_groomingView.Supported) throw new Exception("Horse could not be groomed.");
            _groomingView.BrushPatch(0);
            _groomingView._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (_store.Load().State.Header.SaveRevision != revision) throw new Exception("Canceling grooming saved a reward.");
            ShowGrooming(horse.HorseId);
            if (_groomingView!.Session.TotalStrokes != 0) throw new Exception("Reopened grooming retained discarded dirt progress.");
            await LifeCaptureFrame(Path.Combine(output, "03_grooming_dirty.png"));
            var at = _groomingView.PatchCenters[0] * _groomingView.Size;
            _groomingView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
            _groomingView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
            if (_groomingView.Session.TotalStrokes != 1) throw new Exception("A pointer tap did not brush the visible patch.");
            _groomingView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(-5, -5) });
            if (_groomingView.Session.TotalStrokes != 1) throw new Exception("An outside tap affected grooming.");
            for (int p = 0; p < 3; p++) for (int i = 0; i < UI.GroomingSession.StrokesPerPatch; i++) _groomingView.BrushPatch(p);
            if (!_groomingView.Session.Complete) throw new Exception("Grooming did not complete all patches.");
            await ToSignal(GetTree().CreateTimer(0.85), SceneTreeTimer.SignalName.Timeout); // let the last dust puff settle
            await LifeCaptureFrame(Path.Combine(output, "04_grooming_clean.png"));
            var savePath = _store.SavePath;
            _store = new AtomicSaveStore(savePath, faultInjector: stage => { if (stage == SaveWriteStage.BeforeReplace) throw new IOException("capture interruption before publish"); });
            await FinishGroomingAsync();
            if (_groomingView == null || _groomingView.Session.Saving || _store.Load().State.Header.SaveRevision != revision)
                throw new Exception("Interrupted grooming could not be retried safely.");
            _store = new AtomicSaveStore(savePath, faultInjector: stage => { if (stage == SaveWriteStage.AfterReplace) throw new IOException("capture interruption after publish"); });
            await FinishGroomingAsync();
            if (_groomingView != null) throw new Exception("A durable brush was not reconciled after an interrupted response.");
            _store = new AtomicSaveStore(savePath);
            await FinishGroomingAsync();
            var saved = _store.Load().State;
            if (saved.Header.SaveRevision != revision + 1
                || !HorseBond.DoneToday(saved.Horses.Single(h => h.HorseId.Equals(horse.HorseId)), "brush", saved.Header.CalendarDay))
                throw new Exception("Grooming completion did not save exactly one canonical brush.");
            var other = _state!.Horses.First(h => !h.HorseId.Equals(horse.HorseId));
            ShowGrooming(other.HorseId);
            for (int p = 0; p < 3; p++) for (int i = 0; i < UI.GroomingSession.StrokesPerPatch; i++) _groomingView!.BrushPatch(p);
            await InteractAsync(other.HorseId, "brush");
            if (!HorseBond.DoneToday(_store.Load().State.Horses.Single(h => h.HorseId.Equals(other.HorseId)), "brush", _state.Header.CalendarDay))
                throw new Exception("Quick brush stopped working.");
            revision = _state.Header.SaveRevision;
            await FinishGroomingAsync();
            if (_groomingView != null || _store.Load().State.Header.SaveRevision != revision)
                throw new Exception("A superseded grooming session did not close without another brush.");
            await LifeCaptureFrame(Path.Combine(output, "05_cared_yard.png"));
            // Dispose test audio while the engine can still process its playback releases.
            GetTree().Root.GetNodeOrNull<Audio.GameAudio>("GameAudio")?.QueueFree();
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            GC.Collect(); GC.WaitForPendingFinalizers();
            GD.Print("[stable-life-capture] OK pasture round trip/reload, cancel/reopen, pointer/accessibility, save interruption/retry/reconciliation, grooming once-only finish, quick brush");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError("[stable-life-capture] FAILED " + ex); GetTree().Quit(1); }
    }
}
