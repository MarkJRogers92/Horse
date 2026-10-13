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
    private RaceView? _raceView;
    private SubViewportContainer? _raceFrame;
    private double _practiceDelta;
    private Control? _practiceArt;
    private Label _practiceIntro = null!;
    private const string PracticeIntro = "A friendly gallop on the home track. Speed, acceleration, stamina and calm all matter. Each horse's first gallop of the day counts as training.";
    private HBoxContainer _practiceModes = null!;
    private Button _practiceTab = null!, _cupTab = null!;
    private bool _cupMode;
    private Label _practiceTitle = null!;
    private readonly Dictionary<HorseId, HorseRecord> _raceHorses = new();

    private void BuildPracticeOverlay(Control root)
    {
        _practiceOverlay = CozyTheme.Dialog(root, $"Practice gallop · {PracticeGallop.DistanceMeters} m", 660, out var box, out var buttons, out _practiceTitle);
        _practiceArt = CozyTheme.DialogHeader(box, "gallop", 616, 84);
        _practiceModes = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _practiceModes.AddThemeConstantOverride("separation", 8);
        var modes = new ButtonGroup();
        _practiceTab = new Button { Text = "Practice", ToggleMode = true, ButtonGroup = modes, ButtonPressed = true, CustomMinimumSize = new Vector2(150, 40) };
        _cupTab = CozyTheme.WithIcon(new Button { Text = ClubRace.Name, ToggleMode = true, ButtonGroup = modes, CustomMinimumSize = new Vector2(220, 40) }, "gallop", 24);
        _practiceTab.Pressed += () => ShowPractice(false);
        _cupTab.Pressed += () => ShowPractice(true);
        _practiceModes.AddChild(_practiceTab); _practiceModes.AddChild(_cupTab);
        box.AddChild(_practiceModes);
        _practiceIntro = SmallText(PracticeIntro);
        box.AddChild(_practiceIntro);
        _practicePicks = new VBoxContainer(); _practicePicks.AddThemeConstantOverride("separation", 4); box.AddChild(_practicePicks);
        _practiceTrack = new VBoxContainer(); _practiceTrack.AddThemeConstantOverride("separation", 6); box.AddChild(_practiceTrack);
        _practiceResults = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(600, 0) };
        box.AddChild(_practiceResults);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(130, 46) };
        close.Pressed += () => { if (_practiceElapsedMs >= 0) FinishPracticePlayback(); _practiceOverlay.Visible = false; _practiceElapsedMs = -1; Audio.GameAudio.Loop(null); };
        buttons.AddChild(close);
        _practiceStart = new Button { Text = "Start gallop", CustomMinimumSize = new Vector2(160, 46) };
        _practiceStart.Pressed += () => _ = (_cupMode ? StartCupAsync() : StartPracticeAsync());
        buttons.AddChild(_practiceStart);
        CozyTheme.FinishDialog(box, buttons);
    }

    private void ShowPractice() => ShowPractice(false);

    private void ShowPractice(bool cup)
    {
        if (_state == null || _busy) return;
        _cupMode = cup; _raceHorses.Clear(); _cupOutcome = null;
        _practiceTab.SetPressedNoSignal(!cup); _cupTab.SetPressedNoSignal(cup);
        _practiceModes.Visible = true;
        _practiceTitle.Text = cup ? $"{ClubRace.Name} · {PracticeGallop.DistanceMeters} m" : $"Practice gallop · {PracticeGallop.DistanceMeters} m";
        _cupRivalRow?.QueueFree(); _cupRivalRow = null;
        _practicePicks.QueueFreeChildren(); _practiceTrack.QueueFreeChildren(); _practiceChoices.Clear(); _practiceLanes.Clear();
        _practiceResult = null; _practiceElapsedMs = -1; _practiceResults.Text = ""; _practiceStart.Text = cup ? $"Enter · {ClubRace.EntryFee} coins" : "Start gallop";
        _practicePicks.Visible = true; _practiceIntro.Visible = true;
        if (_practiceArt != null) _practiceArt.Visible = true;
        _raceFrame?.QueueFree(); _raceFrame = null; _raceView = null;
        _practicePodium?.QueueFree(); _practicePodium = null; _practiceTrack.Visible = true;
        if (cup) { FillCupPicker(); _practiceOverlay.Visible = true; return; }
        _practiceIntro.Text = PracticeIntro;
        var eligible = 0;
        foreach (var entry in PracticeGallop.Eligibility(_state).Where(e => !HorseMarket.IsSold(_state.Horses.Single(h => h.HorseId.Equals(e.HorseId)))))
        {
            var horse = _state.Horses.Single(h => h.HorseId.Equals(entry.HorseId));
            var note = !entry.Eligible ? $" · {entry.BlockReason}" : PracticeTraining.TrainedToday(horse, _state.Header.CalendarDay) ? " · trained today" : "";
            var choice = new CheckBox { Text = entry.Name + note, Disabled = !entry.Eligible, ButtonPressed = entry.Eligible && eligible < PracticeGallop.MaxEntrants };
            // The horse's own portrait beside its name, once rendered.
            if (DescriptorFor(horse) is { } look)
            {
                if (_portraits.TryGetCached(look, out var ready) && ready != null) choice.Icon = ready;
                else _ = SetIconAsync(choice, look);
                choice.AddThemeConstantOverride("icon_max_width", 36);
            }
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
            BeginRacePlayback(_practiceResult);
            _practiceResults.Text = "And they're off!";
            Audio.GameAudio.Play("race_bell", -4);
            Audio.GameAudio.Loop("hooves_gallop");
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

    /// <summary>Swap the picker for the 3D race and the position lanes, ready to replay <paramref name="result"/>.</summary>
    private void BeginRacePlayback(PracticeGallopResult result)
    {
        _practiceTrack.QueueFreeChildren(); _practiceLanes.Clear();
        // The race in 3D above the lanes; the lanes stay as an exact position read-out.
        _practicePicks.Visible = false; _practiceIntro.Visible = false; _practiceModes.Visible = false;
        _cupRivalRow?.QueueFree(); _cupRivalRow = null;
        if (_practiceArt != null) _practiceArt.Visible = false;
        _raceFrame?.QueueFree();
        _practicePodium?.QueueFree(); _practicePodium = null; _practiceTrack.Visible = true;
        var height = _cupMode ? 200 : 250;
        _raceFrame = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(616, height) };
        _raceView = new RaceView { Size = new Vector2I(616, height) };
        _practiceTrack.AddThemeConstantOverride("separation", _cupMode ? 2 : 6);
        _raceFrame.AddChild(_raceView);
        _practiceTrack.GetParent().AddChild(_raceFrame);
        _practiceTrack.GetParent().MoveChild(_raceFrame, _practiceTrack.GetIndex());
        _raceView.Setup(result.Entrants.Select(e => DescriptorFor(RaceHorse(e.HorseId))).ToList());
        foreach (var entrant in result.Entrants)
        {
            var lane = new HBoxContainer(); lane.AddThemeConstantOverride("separation", 10);
            var horse = RaceHorse(entrant.HorseId);
            var mine = horse.OwnerId.Equals(_state?.PlayerOwnerId);
            var name = CozyTheme.Title(entrant.Name, 15); name.CustomMinimumSize = new Vector2(110, 0); name.ClipText = true; name.VerticalAlignment = VerticalAlignment.Center;
            if (_cupMode && mine) name.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
            lane.AddChild(name);
            var track = new RaceLane { Running = true, Compact = _cupMode };
            track.SetToken(Portrait(horse, _cupMode ? 30 : 46, CardTint(horse)));
            lane.AddChild(track); _practiceLanes.Add(track); _practiceTrack.AddChild(lane);
        }
    }

    /// <summary>Entrants come from the stable, or for the cup from this week's visiting rivals.</summary>
    private HorseRecord RaceHorse(HorseId id) =>
        _raceHorses.TryGetValue(id, out var horse) ? horse : _state!.Horses.Single(h => h.HorseId.Equals(id));

    public override void _Process(double delta)
    {
        if (_practiceElapsedMs < 0 || _practiceResult == null) return;
        _practiceElapsedMs += delta * 1000 * PracticePlaybackSpeed;
        _practiceDelta = delta;
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
        var progress = new List<float>();
        for (var lane = 0; lane < _practiceLanes.Count; lane++)
        {
            _practiceLanes[lane].Value = (float)(result.Frames[index][lane] + (result.Frames[next][lane] - result.Frames[index][lane]) * t);
            progress.Add(_practiceLanes[lane].Value);
        }
        _raceView?.SetProgress(progress, _practiceDelta);
        if (index < result.Frames.Count - 1) return;
        _practiceElapsedMs = -1;
        Audio.GameAudio.Loop(null);
        Audio.GameAudio.Play("care_cheer", -6);
        _raceView?.Finish();
        ShowPodium(result);
        for (var lane = 0; lane < _practiceLanes.Count; lane++)
        {
            _practiceLanes[lane].Running = false;
            _practiceLanes[lane].ShowPlace(result.Entrants[lane].Place);
        }
        if (_cupMode) { FinishCupPlayback(result); return; }
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

    /// <summary>Swap the 3D race for a podium: portraits on stepped rosette blocks, 2nd-1st-3rd.</summary>
    private void ShowPodium(PracticeGallopResult result)
    {
        if (_raceFrame == null || _state == null) return;
        var podium = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(616, 200) };
        podium.AddThemeConstantOverride("separation", 18);
        var order = result.Entrants.OrderBy(e => e.Place).Take(3).ToList();
        var layout = order.Count switch { 1 => new[] { 0 }, 2 => new[] { 1, 0 }, _ => new[] { 1, 0, 2 } };
        foreach (var index in layout)
        {
            var entrant = order[index];
            var horse = RaceHorse(entrant.HorseId);
            var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            column.AddThemeConstantOverride("separation", 4);
            var portrait = Portrait(horse, entrant.Place == 1 ? 92 : 74, CardTint(horse));
            portrait.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            column.AddChild(portrait);
            var name = CozyTheme.Title(entrant.Name, 15); name.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(name);
            var block = new PanelContainer { CustomMinimumSize = new Vector2(120, entrant.Place switch { 1 => 72, 2 => 52, _ => 38 }) };
            var colour = entrant.Place switch { 1 => Color.FromHtml("#f5c542"), 2 => Color.FromHtml("#c9d1dc"), _ => Color.FromHtml("#e0a46e") };
            var style = CozyTheme.Box(colour, 14, 4); style.BorderColor = Colors.White; style.SetBorderWidthAll(3);
            block.AddThemeStyleboxOverride("panel", style);
            var label = CozyTheme.Title($"{Ordinal(entrant.Place)}\n{entrant.FinishMilliseconds / 1000.0:0.0} s", 15);
            label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
            block.AddChild(label);
            column.AddChild(block);
            podium.AddChild(column);
        }
        var parent = _raceFrame.GetParent();
        var at = _raceFrame.GetIndex();
        _raceFrame.QueueFree(); _raceFrame = null; _raceView = null;
        _practiceTrack.Visible = false;
        _practicePodium = podium;
        parent.AddChild(podium);
        parent.MoveChild(podium, at);
    }

    private Control? _practicePodium;

    private async Task SetIconAsync(Button button, Visual.VisualDescriptor look)
    {
        var texture = await _portraits.RenderAsync(look);
        if (IsInstanceValid(button) && texture != null) button.Icon = texture;
    }

    private static string Ordinal(int place) => place switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => place + "th" };
}
