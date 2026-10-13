using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>Automated smoke and screen-capture checks run by CI (--stable-*-capture, --stable-day-smoke).</summary>
public partial class StableDay
{
    private async Task RunSmokeAsync()
    {
        try
        {
            LoadOrCreate();
            if (_state == null || _state.Header.CalendarDay != 0) throw new InvalidOperationException("Smoke requires a fresh temporary save slot.");
            var startCoins = _state.Coins;
            var plan = new CarePlan(_state.Horses.Select(h => new HorseCareAssignment(h.HorseId, true, true, false, false)));
            var preview = _service.Preview(_state, plan);
            if (!preview.Allowed) throw new InvalidOperationException("Fresh starter day was blocked: " + preview.BlockReason);
            var request = new EndDayRequest(new TransactionId(Guid.NewGuid()), _state.PlayerOwnerId, _state.Header.SaveRevision,
                _state.Header.CalendarDay, plan, _service.PolicyVersion);
            var committed = await Task.Run(() => _store.CommitEndDay(request, _service));
            var reloaded = _store.Load();
            var replay = await Task.Run(() => _store.CommitEndDay(request, _service));
            if (reloaded.State.Header.CalendarDay != 1 || committed.State.Header.CalendarDay != 1 || replay.State.Header.CalendarDay != 1 || !replay.Replayed)
                throw new InvalidOperationException("Commit, reload or retry did not retain exactly one day advancement.");
            if (reloaded.State.Coins != startCoins + preview.IncomeCoins - preview.WalletDebitCoins || reloaded.State.Coins != preview.ProjectedCoins
                || committed.State.Coins != reloaded.State.Coins || replay.State.Coins != reloaded.State.Coins)
                throw new InvalidOperationException("Commit, reload or retry changed the wallet more than once.");
            GD.Print($"[stable-day-smoke] OK day={reloaded.State.Header.CalendarDay} coins={reloaded.State.Coins} replayed={replay.Replayed} path={_store.SavePath}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[stable-day-smoke] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task RunCaptureAsync(string output)
    {
        try
        {
            if (_state == null || _recoveryRequired || _state.Header.CalendarDay != 0)
                throw new InvalidOperationException("Capture requires a fresh, healthy temporary save slot.");
            var startCoins = _state.Coins;
            if (startCoins != 500 || _state.Horses.Count != 2)
                throw new InvalidOperationException("Capture expects the two-horse starter stable with 500 coins.");
            var directory = Path.GetFullPath(output);
            Directory.CreateDirectory(directory);

            await CaptureFrame(Path.Combine(directory, "01_stable_day.png"));
            if (!_guideRow.Visible || _guideStep != 1)
                throw new InvalidOperationException("A new stable did not open on the first guide step.");
            var inspected = _state.Horses.OrderBy(h => h.Name, StringComparer.Ordinal).First();
            _inspectButtons[inspected.HorseId].EmitSignal(BaseButton.SignalName.Pressed);
            if (!_inspectOverlay.Visible || _inspectTitle.Text != inspected.Name || !_inspectDetails.Text.Contains("Coat: ", StringComparison.Ordinal)
                || _inspectDetails.Text.Contains("Not born yet", StringComparison.Ordinal))
                throw new InvalidOperationException("The Details button did not open the horse inspection with a visible coat.");
            await CaptureFrame(Path.Combine(directory, "01b_inspect.png"));
            _inspectOverlay.Visible = false;
            if (_guideStep != 2)
                throw new InvalidOperationException("Opening Details did not advance the guide to care planning.");
            var turnout = _turnoutButtons.First();
            SetOptional(turnout.Key, true, null);
            turnout.Value.ButtonPressed = true;
            if (_guideStep != 3)
                throw new InvalidOperationException("Choosing comfort care did not advance the guide to ending the day.");
            _reviewButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!_reviewOverlay.Visible || _preview?.Allowed != true)
                throw new InvalidOperationException("The Review day button did not open an allowed day preview.");
            if (!_reviewedPlan!.For(turnout.Key)!.Turnout || _preview.HorseOutcomes.Single(o => o.HorseId.Equals(turnout.Key)).After.Turnout != 6200)
                throw new InvalidOperationException("The selected turnout choice did not appear in the reviewed care result.");
            await CaptureFrame(Path.Combine(directory, "02_review.png"));

            _confirmDayButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 600 && _busy; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_busy || _pendingRequest != null || _state?.Header.CalendarDay != 1)
                throw new InvalidOperationException("Confirm did not complete exactly one day advancement.");

            _reloadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_state == null || _state.Header.CalendarDay != 1 || _state.Coins != startCoins + 50 - 36
                || _state.DailyState.Receipts.Count != 1 || _state.Ledger.Count(e => e.Kind == "stable-day") != 1
                || _state.Ledger.Count(e => e.Kind == "stable-income") != 1
                || _state.DailyState.For(turnout.Key).Turnout != 6200)
                throw new InvalidOperationException("Reload did not show exactly one saved day, one 50-coin club support credit and one 36-coin debit.");
            if (_guideStep != 4)
                throw new InvalidOperationException("Ending the first day did not advance the guide to breeding.");
            await CaptureFrame(Path.Combine(directory, "03_day_2.png"));
            var coinsBeforePractice = _state.Coins; var hashBeforePractice = _state.Header.PayloadHash;
            _practiceButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!_practiceOverlay.Visible || _practiceStart.Disabled || _practiceChoices.Count != 2)
                throw new InvalidOperationException("Practice gallop did not offer both starter horses.");
            _practiceStart.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 600 && (_busy || _practiceResult == null); frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_practiceResult == null) throw new InvalidOperationException("Practice gallop did not start.");
            _practiceElapsedMs = (_practiceResult.Frames.Count - 1) * _practiceResult.FrameIntervalMilliseconds * 0.55;
            AdvancePracticePlayback();
            await CaptureFrame(Path.Combine(directory, "04a_practice_running.png"));
            FinishPracticePlayback();
            if (_practiceResult == null || !_practiceResults.Text.StartsWith("1st · ", StringComparison.Ordinal)
                || _practiceLanes.Any(lane => lane.Value < 1000) || _state.Coins != coinsBeforePractice || _state.Header.PayloadHash == hashBeforePractice
                || _state.Horses.Any(h => PracticeTraining.Skill(h) != PracticeTraining.BaseGain) || !_practiceResults.Text.Contains("training +", StringComparison.Ordinal))
                throw new InvalidOperationException("Practice gallop did not finish with placings and one saved day of training for each horse.");
            await CaptureFrame(Path.Combine(directory, "04_practice.png"));
            _practiceOverlay.Visible = false;
            GD.Print($"[stable-day-capture] OK screenshots=6 day={_state.Header.CalendarDay} coins={_state.Coins} output={directory}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[stable-day-capture] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task RunLifecycleCaptureAsync(string output)
    {
        try
        {
            if (_state == null || _recoveryRequired || _state.Header.CalendarDay != 10 || _state.Coins != 450 || _state.DailyState.Receipts.Count != 0)
                throw new InvalidOperationException("Lifecycle capture requires a fresh isolated save initialized from the foaling demo fixture.");
            var mare = _state.Horses.Single(h => h.ReproductionState.PregnancyId != null);
            var unborn = _state.Horses.Single(h => h.LifeState.Stage == LifeStage.Unborn);
            var directory = Path.GetFullPath(output);
            Directory.CreateDirectory(directory);
            await CaptureFrame(Path.Combine(directory, "01_pregnancy.png"));
            if (!ParentNames(unborn).Contains(mare.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("The foal roster does not show its canonical parents.");

            var birthCaptured = false;
            var youngsterCaptured = false;
            var adultCaptured = false;
            var renamed = false;
            var captureStep = 0;
            while (captureStep++ < 24)
            {
                var fromDay = _state.Header.CalendarDay;
                _reviewButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!_reviewOverlay.Visible || _preview?.Allowed != true)
                    throw new InvalidOperationException("The actual Review day UI did not produce an allowed lifecycle preview.");
                if (_preview.LifecycleEvents.Count > 0 && !birthCaptured)
                    await CaptureFrame(Path.Combine(directory, "02_birth_review.png"));
                var coinsBefore = _state.Coins;
                var expectedWalletDebit = _preview.WalletDebitCoins;
                var expectedIncome = _preview.IncomeCoins;
                var creditsBefore = _state.Ledger.Count(entry => entry.Kind == "stable-income");
                var receiptsBefore = _state.DailyState.Receipts.Count;
                var debitsBefore = _state.Ledger.Count(entry => entry.Kind == "stable-day");
                _confirmDayButton.EmitSignal(BaseButton.SignalName.Pressed);
                for (var frame = 0; frame < 600 && _busy; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_busy || _pendingRequest != null || _state == null || _state.Header.CalendarDay != fromDay + 1)
                    throw new InvalidOperationException("The real End day UI did not save exactly one calendar day.");

                if (_state.DailyState.Receipts.Count != receiptsBefore + 1)
                    throw new InvalidOperationException("A confirmed End day did not append exactly one durable receipt.");
                var committedReceipt = _state.DailyState.Receipts.Last();
                if (committedReceipt.FromDay != fromDay || committedReceipt.ToDay != fromDay + 1
                    || committedReceipt.WalletDebitCoins != expectedWalletDebit || committedReceipt.IncomeCoins != expectedIncome
                    || _state.Coins != coinsBefore + committedReceipt.IncomeCoins - committedReceipt.WalletDebitCoins
                    || _state.Ledger.Count(entry => entry.Kind == "stable-day") != debitsBefore + (committedReceipt.WalletDebitCoins > 0 ? 1 : 0)
                    || _state.Ledger.Count(entry => entry.Kind == "stable-income") != creditsBefore + (committedReceipt.IncomeCoins > 0 ? 1 : 0))
                    throw new InvalidOperationException("The confirmed day did not persist exactly its previewed income and wallet debit.");
                var committedEvents = committedReceipt.LifecycleEvents;
                if (committedEvents.Any(item => item.Kind.ToString() == "Birth"))
                {
                    var child = _state.Horses.Single(h => h.HorseId.Equals(committedEvents.Single(item => item.Kind.ToString() == "Birth").HorseId));
                    if (child.LifeState.Stage != LifeStage.Foal || !child.BirthDay.HasValue)
                        throw new InvalidOperationException("Birth did not create a born foal with a saved birth day.");
                    var childParents = ParentNames(child);
                    if (childParents.Length == 0 || child.ReproductionState.PregnancyId != null)
                        throw new InvalidOperationException("The born foal's pedigree or pregnancy transition was not preserved.");
                    var mother = _state.Horses.Single(h => h.HorseId.Equals(mare.HorseId));
                    if (mother.ReproductionState.RecoveryUntilDay != child.BirthDay.Value + 2)
                        throw new InvalidOperationException("The mare's two-day recovery date was not persisted.");
                    await CaptureFrame(Path.Combine(directory, "03_birth_reveal.png"));
                    if (!await NameFoalThroughUiAsync(child.HorseId, "Clover"))
                        throw new InvalidOperationException("The actual foal naming controls did not save the chosen name.");
                    renamed = true;
                    _reloadButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (_state.Horses.Single(h => h.HorseId.Equals(child.HorseId)).Name != "Clover")
                        throw new InvalidOperationException("Foal naming did not survive a save reload.");
                }
                if (committedEvents.Any(item => item.Kind.ToString() == "FoalToYoungster"))
                {
                    youngsterCaptured = true;
                    await CaptureFrame(Path.Combine(directory, "04_youngster.png"));
                }
                if (committedEvents.Any(item => item.Kind.ToString() == "YoungsterToAdult"))
                {
                    adultCaptured = true;
                    await CaptureFrame(Path.Combine(directory, "05_adult.png"));
                }

                if (adultCaptured && _state.Horses.Any(h => h.Name == "Clover" && h.BirthDay.HasValue && _state.Header.CalendarDay >= h.BirthDay.Value + 8))
                {
                    var foal = _state.Horses.Single(h => h.Name == "Clover");
                    var allEvents = _state.DailyState.Receipts.SelectMany(r => r.LifecycleEvents).ToArray();
                    if (!renamed || !birthCaptured && !_state.DailyState.Receipts.SelectMany(r => r.LifecycleEvents).Any(e => e.Kind.ToString() == "Birth")
                        || !youngsterCaptured || foal.LifeState.Stage != LifeStage.Adult)
                        throw new InvalidOperationException("Saved birth, naming, or growth lifecycle did not complete exactly once.");
                    if (allEvents.Count(e => e.Kind.ToString() == "Birth") != 1
                        || allEvents.Count(e => e.Kind.ToString() == "FoalToYoungster") != 1
                        || allEvents.Count(e => e.Kind.ToString() == "YoungsterToAdult") != 1)
                        throw new InvalidOperationException("The saved history contains a missing or duplicate birth/growth event.");
                    if (foal.BirthDay != null && _state.Header.CalendarDay < foal.BirthDay.Value + 6)
                        throw new InvalidOperationException("The foal reached adulthood before its six-day growth interval.");
                    if (!foal.BirthDay.HasValue || _state.Header.CalendarDay < foal.BirthDay.Value + 8)
                        throw new InvalidOperationException("The capture ended before the eight-day breeding-age milestone.");
                    _reloadButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (_state.Horses.Single(h => h.HorseId.Equals(foal.HorseId)).LifeState.Stage != LifeStage.Adult)
                        throw new InvalidOperationException("The adult stage did not survive a reload.");
                    await CaptureFrame(Path.Combine(directory, "06_reloaded_adult.png"));
                    await CaptureFrame(Path.Combine(directory, "07_breeding_age.png"));
                    GD.Print($"[stable-lifecycle-capture] OK screenshots=7 day={_state.Header.CalendarDay} coins={_state.Coins} output={directory}");
                    GetTree().Quit(0);
                    return;
                }
                if (committedEvents.Any(item => item.Kind.ToString() == "Birth")) birthCaptured = true;
            }
            throw new InvalidOperationException("Capture did not reach the adult milestone within 24 days.");
        }
        catch (Exception ex)
        {
            GD.PushError("[stable-lifecycle-capture] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task<bool> NameFoalThroughUiAsync(HorseId horseId, string name)
    {
        var rows = _roster.GetChildren().OfType<HBoxContainer>().ToArray();
        var renameButton = rows.SelectMany(row => row.GetChildren()).OfType<Button>().FirstOrDefault(button => button.Text == "Name foal");
        var nameInput = rows.SelectMany(row => row.GetChildren()).OfType<LineEdit>().FirstOrDefault();
        if (renameButton == null || nameInput == null) return false;
        nameInput.Text = name;
        renameButton.EmitSignal(BaseButton.SignalName.Pressed);
        for (var frame = 0; frame < 600 && _busy; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var named = _state?.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
        return !_busy && named?.Name == name;
    }

    private async Task CaptureFrame(string path)
    {
        _guideSpeaker.Finish(); _saleSpeaker.Finish();
        // Portraits render asynchronously; screenshots must show them, not placeholders.
        for (var i = 0; i < 600 && _portraits.Busy; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        if (error != Error.Ok) throw new IOException($"Could not save UI capture ({error}): {path}");
    }
}
