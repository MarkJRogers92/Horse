using System;
using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// Practice gallop (P2-05 slice, D-009). The domain decides the whole race up front; this panel only replays
/// its recorded frames, so playback speed or frame rate can never change a result. Practice pays nothing and
/// does not change the save.
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
    private readonly List<ProgressBar> _practiceLanes = new();
    private PracticeGallopResult? _practiceResult;
    private double _practiceElapsedMs = -1;
    private int _practiceRuns;

    private void BuildPracticeOverlay(Control root)
    {
        _practiceOverlay = new Control { Visible = false };
        _practiceOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_practiceOverlay);
        var dim = new ColorRect { Color = new Color(0.16f, 0.11f, 0.17f, 0.52f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _practiceOverlay.AddChild(dim);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect); _practiceOverlay.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(640, 0) }; center.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); panel.AddChild(box);
        box.AddChild(CozyTheme.Title($"Practice gallop · {PracticeGallop.DistanceMeters} m", 26));
        box.AddChild(SmallText("A friendly gallop on the home track. Speed, acceleration, stamina and calm all matter. Practice is free and changes nothing in your save."));
        _practicePicks = new VBoxContainer(); _practicePicks.AddThemeConstantOverride("separation", 4); box.AddChild(_practicePicks);
        _practiceTrack = new VBoxContainer(); _practiceTrack.AddThemeConstantOverride("separation", 6); box.AddChild(_practiceTrack);
        _practiceResults = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(580, 0) };
        box.AddChild(_practiceResults);
        var buttons = new HBoxContainer(); buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(150, 44) };
        close.Pressed += () => { _practiceOverlay.Visible = false; _practiceElapsedMs = -1; };
        buttons.AddChild(close);
        _practiceStart = new Button { Text = "Start gallop", CustomMinimumSize = new Vector2(150, 44) };
        _practiceStart.Pressed += StartPractice;
        buttons.AddChild(_practiceStart); box.AddChild(buttons);
    }

    private void ShowPractice()
    {
        if (_state == null || _busy) return;
        _practicePicks.QueueFreeChildren(); _practiceTrack.QueueFreeChildren(); _practiceChoices.Clear(); _practiceLanes.Clear();
        _practiceResult = null; _practiceElapsedMs = -1; _practiceResults.Text = "";
        var eligible = 0;
        foreach (var entry in PracticeGallop.Eligibility(_state))
        {
            var choice = new CheckBox { Text = entry.Eligible ? entry.Name : $"{entry.Name} · {entry.BlockReason}", Disabled = !entry.Eligible,
                ButtonPressed = entry.Eligible && eligible < PracticeGallop.MaxEntrants };
            if (entry.Eligible) { eligible++; _practiceChoices[entry.HorseId] = choice; }
            _practicePicks.AddChild(choice);
        }
        _practiceStart.Disabled = eligible == 0;
        if (eligible == 0) _practiceResults.Text = "No adult horse is ready to gallop today.";
        _practiceOverlay.Visible = true;
    }

    private void StartPractice()
    {
        if (_state == null) return;
        try
        {
            var picked = _practiceChoices.Where(p => p.Value.ButtonPressed).Select(p => p.Key).ToArray();
            if (picked.Length == 0 || picked.Length > PracticeGallop.MaxEntrants)
            {
                _practiceResults.Text = $"Choose 1 to {PracticeGallop.MaxEntrants} horses.";
                return;
            }
            _practiceResult = PracticeGallop.Run(_state, picked, _practiceRuns++);
            _practiceTrack.QueueFreeChildren(); _practiceLanes.Clear();
            foreach (var entrant in _practiceResult.Entrants)
            {
                var lane = new HBoxContainer(); lane.AddThemeConstantOverride("separation", 10);
                var name = SmallText(entrant.Name); name.CustomMinimumSize = new Vector2(120, 0); lane.AddChild(name);
                var bar = new ProgressBar { MinValue = 0, MaxValue = 1000, ShowPercentage = false, CustomMinimumSize = new Vector2(440, 22),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                bar.AddThemeStyleboxOverride("background", CozyTheme.Box(CozyTheme.Warning, 8, 0));
                bar.AddThemeStyleboxOverride("fill", CozyTheme.Box(CozyTheme.Accent, 8, 0));
                lane.AddChild(bar); _practiceLanes.Add(bar); _practiceTrack.AddChild(lane);
            }
            _practiceResults.Text = "And they're off!";
            _practiceStart.Disabled = true;
            _practiceElapsedMs = 0;
        }
        catch (Exception ex)
        {
            _practiceResults.Text = "The gallop could not start. " + PlainError(ex);
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
            _practiceLanes[lane].Value = result.Frames[index][lane] + (result.Frames[next][lane] - result.Frames[index][lane]) * t;
        if (index < result.Frames.Count - 1) return;
        _practiceElapsedMs = -1;
        _practiceStart.Disabled = false; _practiceStart.Text = "Gallop again";
        var lines = result.Entrants.OrderBy(e => e.Place).Select(e => $"{Ordinal(e.Place)} · {e.Name} · {e.FinishMilliseconds / 1000.0:0.0} s").ToList();
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
