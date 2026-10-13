using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// Practice gallop (P2-05 slice, D-009/D-011). The domain decides the whole race up front; this panel only replays
/// its recorded frames, so playback speed or frame rate can never change a result. Practice pays nothing; each
/// horse's first gallop of the day is saved as gallop training.
/// </summary>
public partial class StableDay
{
    private const float PracticePlaybackSpeed = 4f;
    private Control _practiceOverlay = null!;
    private VBoxContainer _practicePicks = null!;
    private VBoxContainer _practiceTrack = null!;
    private Label _practiceResults = null!;
    private Button _practiceStart = null!;
    private Button _practiceButton = null!;
    private readonly Dictionary<HorseId, CheckBox> _practiceChoices = new();
    private readonly List<RaceLane> _practiceLanes = new();
    private PracticeGallopResult? _practiceResult;
    private Dictionary<HorseId, int> _practiceGains = new();
    private double _practiceElapsedMs = -1;
    private int _practiceRuns;

    private void BuildPracticeOverlay(Control root)
    {
        _practiceOverlay = CozyTheme.Dialog(root, $"Practice gallop · {PracticeGallop.DistanceMeters} m", 660, out var box, out var buttons, out _);
        CozyTheme.DialogHeader(box, "gallop", 616, 84);
        box.AddChild(SmallText("A friendly gallop on the home track. Speed, acceleration, stamina and calm all matter. Each horse's first gallop of the day counts as training."));
        _practicePicks = new VBoxContainer(); _practicePicks.AddThemeConstantOverride("separation", 4); box.AddChild(_practicePicks);
        _practiceTrack = new VBoxContainer(); _practiceTrack.AddThemeConstantOverride("separation", 6); box.AddChild(_practiceTrack);
        _practiceResults = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(600, 0) };
        box.AddChild(_practiceResults);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(130, 46) };
        close.Pressed += () => { _practiceOverlay.Visible = false; _practiceElapsedMs = -1; };
        buttons.AddChild(close);
        _practiceStart = new Button { Text = "Start gallop", CustomMinimumSize = new Vector2(160, 46) };
        _practiceStart.Pressed += () => _ = StartPracticeAsync();
        buttons.AddChild(_practiceStart);
        CozyTheme.FinishDialog(box, buttons);
    }

    private void ShowPractice()
    {
        if (_state == null || _busy) return;
        _practicePicks.QueueFreeChildren(); _practiceTrack.QueueFreeChildren(); _practiceChoices.Clear(); _practiceLanes.Clear();
        _practiceResult = null; _practiceElapsedMs = -1; _practiceResults.Text = ""; _practiceStart.Text = "Start gallop";
        var eligible = 0;
        foreach (var entry in PracticeGallop.Eligibility(_state).Where(e => !HorseMarket.IsSold(_state.Horses.Single(h => h.HorseId.Equals(e.HorseId)))))
        {
            var horse = _state.Horses.Single(h => h.HorseId.Equals(entry.HorseId));
            var note = !entry.Eligible ? $" · {entry.BlockReason}" : PracticeTraining.TrainedToday(horse, _state.Header.CalendarDay) ? " · trained today" : "";
            var choice = new CheckBox { Text = entry.Name + note, Disabled = !entry.Eligible, ButtonPressed = entry.Eligible && eligible < PracticeGallop.MaxEntrants };
            if (entry.Eligible) { eligible++; _practiceChoices[entry.HorseId] = choice; }
            _practicePicks.AddChild(choice);
        }
        _practiceStart.Disabled = eligible == 0;
        if (eligible == 0) _practiceResults.Text = "No adult horse is ready to gallop today.";
        _practiceOverlay.Visible = true;
    }

    private async Task StartPracticeAsync()
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        var picked = _practiceChoices.Where(p => p.Value.ButtonPressed).Select(p => p.Key).ToArray();
        if (picked.Length == 0 || picked.Length > PracticeGallop.MaxEntrants)
        {
            _practiceResults.Text = $"Choose 1 to {PracticeGallop.MaxEntrants} horses.";
            return;
        }
        _busy = true; _practiceStart.Disabled = true;
        try
        {
            var before = _state;
            _practiceResult = PracticeGallop.Run(before, picked, _practiceRuns++);
            var owner = before.PlayerOwnerId; var revision = before.Header.SaveRevision;
            _state = await Task.Run(() => _store.RecordPractice(owner, revision, picked));
            _practiceGains = picked.ToDictionary(id => id, id => PracticeTraining.Skill(_state.Horses.Single(h => h.HorseId.Equals(id)))
                - PracticeTraining.Skill(before.Horses.Single(h => h.HorseId.Equals(id))));
            _practiceTrack.QueueFreeChildren(); _practiceLanes.Clear();
            foreach (var entrant in _practiceResult.Entrants)
            {
                var lane = new HBoxContainer(); lane.AddThemeConstantOverride("separation", 10);
                var name = CozyTheme.Title(entrant.Name, 15); name.CustomMinimumSize = new Vector2(96, 0); name.VerticalAlignment = VerticalAlignment.Center;
                lane.AddChild(name);
                var horse = _state.Horses.Single(h => h.HorseId.Equals(entrant.HorseId));
                var track = new RaceLane { Running = true };
                track.SetToken(Portrait(horse, 46, CardTint(horse)));
                lane.AddChild(track); _practiceLanes.Add(track); _practiceTrack.AddChild(lane);
            }
            _practiceResults.Text = "And they're off!";
            _practiceElapsedMs = 0;
        }
        catch (Exception ex)
        {
            _practiceResults.Text = "The gallop could not start. " + PlainError(ex);
            _practiceStart.Disabled = false;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    public override void _Process(double delta)
    {
        if (_practiceElapsedMs < 0 || _practiceResult == null) return;
        _practiceElapsedMs += delta * 1000 * PracticePlaybackSpeed;
        AdvancePracticePlayback();
    }

    /// <summary>Shows the recorded race at the current playback time; finishes once the last frame is reached.</summary>
    private void AdvancePracticePlayback()
    {
        var result = _practiceResult!;
        var position = _practiceElapsedMs / result.FrameIntervalMilliseconds;
        var index = (int)Math.Min(Math.Floor(position), result.Frames.Count - 1);
        var next = Math.Min(index + 1, result.Frames.Count - 1);
        var t = Math.Clamp(position - index, 0, 1);
        for (var lane = 0; lane < _practiceLanes.Count; lane++)
            _practiceLanes[lane].Value = (float)(result.Frames[index][lane] + (result.Frames[next][lane] - result.Frames[index][lane]) * t);
        if (index < result.Frames.Count - 1) return;
        _practiceElapsedMs = -1;
        for (var lane = 0; lane < _practiceLanes.Count; lane++)
        {
            _practiceLanes[lane].Running = false;
            _practiceLanes[lane].ShowPlace(result.Entrants[lane].Place);
        }
        _practiceStart.Disabled = false; _practiceStart.Text = "Gallop again";
        var lines = result.Entrants.OrderBy(e => e.Place).Select(e => $"{Ordinal(e.Place)} · {e.Name} · {e.FinishMilliseconds / 1000.0:0.0} s"
            + (_practiceGains.TryGetValue(e.HorseId, out var gain) && gain > 0 ? $"  ·  training +{Percent(gain)}" : "")).ToList();
        lines.Add("");
        lines.AddRange(result.Observations);
        _practiceResults.Text = string.Join("\n", lines);
    }

    private void FinishPracticePlayback()
    {
        if (_practiceResult == null) return;
        _practiceElapsedMs = (double)(_practiceResult.Frames.Count - 1) * _practiceResult.FrameIntervalMilliseconds;
        AdvancePracticePlayback();
    }

    private static string Ordinal(int place) => place switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => place + "th" };
}
