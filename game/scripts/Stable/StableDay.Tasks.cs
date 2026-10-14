using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// D-031 Pip's daily tasks: a compact checklist under Pip's bubble. Everything shown is derived from the save by the domain
/// <see cref="DailyTasks"/>; the Claim button publishes the ledger credit through the store. Hidden while the first-session
/// guide is showing and in the foaling demo.
/// </summary>
public partial class StableDay
{
    private PanelContainer _tasksPanel = null!;
    private VBoxContainer _tasksRows = null!;
    private Label _tasksStreak = null!;
    private Label _tasksTitle = null!;
    private Button _tasksToggle = null!;
    private TextureRect _tasksHeart = null!;
    private bool _tasksCollapsed, _tasksPrefLoaded, _tasksWasClaimable;
    private ImageTexture? _chevronDown, _chevronRight;

    // D-036: the card can be folded to its header row. The choice is a display preference kept in tasks.cfg beside the save
    // (like decor.cfg and guide.cfg), not saved in the foaling demo. A newly claimable task opens the card again.
    private string TasksPrefPath => Path.Combine(Path.GetDirectoryName(_store.SavePath) ?? ".", "tasks.cfg");

    private void LoadTasksPref()
    {
        _tasksPrefLoaded = true;
        var config = new ConfigFile();
        if (File.Exists(TasksPrefPath) && config.Load(TasksPrefPath) == Error.Ok) _tasksCollapsed = (bool)config.GetValue("card", "collapsed", false);
    }

    private void SaveTasksPref()
    {
        if (_foalingDemoMode) return;
        var config = new ConfigFile();
        config.SetValue("card", "collapsed", _tasksCollapsed);
        config.Save(TasksPrefPath);
    }

    private void SetTasksCollapsed(bool collapsed, bool remember)
    {
        _tasksCollapsed = collapsed;
        if (remember) SaveTasksPref();
        RefreshDailyTasks();
    }

    private void BuildTasksPanel(Control root)
    {
        _tasksPanel = new PanelContainer { Visible = false };
        _tasksPanel.AddThemeStyleboxOverride("panel", CozyTheme.Box(new Color(1, 0.98f, 0.96f, 0.93f), 18, 6));
        // Sits under Pip's bubble (x 368-824) and grows downward with its three rows.
        Anchor(_tasksPanel, 0, 0, 1, 0, 368, 202, -456, 202);
        root.AddChild(_tasksPanel);
        root.MoveChild(_tasksPanel, 1); // stay beneath every dialog overlay
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 2); _tasksPanel.AddChild(stack);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 6); stack.AddChild(header);
        _chevronDown = CozyTheme.Icons.Arrow();
        var turned = _chevronDown.GetImage(); turned.Rotate90(ClockDirection.Counterclockwise);
        _chevronRight = ImageTexture.CreateFromImage(turned);
        _tasksToggle = new Button { Flat = true, CustomMinimumSize = new Vector2(30, 26), Icon = _chevronDown, IconAlignment = HorizontalAlignment.Center,
            ExpandIcon = false, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None };
        _tasksToggle.Pressed += () => SetTasksCollapsed(!_tasksCollapsed, true);
        header.AddChild(_tasksToggle);
        _tasksTitle = CozyTheme.Title("Pip's tasks today", 17);
        header.AddChild(_tasksTitle);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _tasksHeart = new TextureRect { Texture = CozyTheme.Icons.HeartOn, CustomMinimumSize = new Vector2(16, 16), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        header.AddChild(_tasksHeart);
        _tasksStreak = new Label(); _tasksStreak.AddThemeFontSizeOverride("font_size", 13); _tasksStreak.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        header.AddChild(_tasksStreak);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(2, 0) }); // keeps the streak off the card edge
        _tasksRows = new VBoxContainer(); _tasksRows.AddThemeConstantOverride("separation", 2); stack.AddChild(_tasksRows);
    }

    /// <summary>Rebuilds the checklist from the save. Called with the guide refresh, so ticks appear as soon as a task is done.</summary>
    private void RefreshDailyTasks()
    {
        if (_tasksPanel == null || !GodotObject.IsInstanceValid(_tasksPanel)) return;
        // Hidden only on the very first day while Pip introduces the stable; afterwards the tasks show alongside any guide line.
        var tasks = _state == null || _foalingDemoMode || (_state.Header.CalendarDay == 0 && GuideLine().Line != null) ? Array.Empty<DailyTask>() : DailyTasks.For(_state);
        _tasksPanel.Visible = _state != null && tasks.Count > 0;
        foreach (var child in _tasksRows.GetChildren()) { _tasksRows.RemoveChild(child); child.QueueFree(); }
        if (_state == null || tasks.Count == 0) return;
        if (!_tasksPrefLoaded) LoadTasksPref();
        var claimable = tasks.Any(t => t.IsDone(_state) && !DailyTasks.IsClaimedToday(_state, t));
        if (claimable && !_tasksWasClaimable) _tasksCollapsed = false; // something to collect: open the card
        _tasksWasClaimable = claimable;
        _tasksToggle.Icon = _tasksCollapsed ? _chevronRight : _chevronDown;
        _tasksToggle.TooltipText = _tasksCollapsed ? "Show Pip's tasks" : "Fold the task card away";
        _tasksRows.Visible = !_tasksCollapsed;
        _tasksHeart.Visible = !_tasksCollapsed;
        _tasksStreak.Visible = !_tasksCollapsed;
        if (_tasksCollapsed)
        {
            var finished = tasks.Count(t => t.IsDone(_state) || DailyTasks.IsClaimedToday(_state, t));
            _tasksTitle.Text = $"Pip's tasks · {finished} of {tasks.Count} done" + (claimable ? " · Claim!" : "");
            _tasksTitle.AddThemeColorOverride("font_color", claimable ? CozyTheme.AccentDeep : CozyTheme.Text);
            return;
        }
        _tasksTitle.Text = "Pip's tasks today";
        _tasksTitle.AddThemeColorOverride("font_color", CozyTheme.Text);
        var streak = DailyTasks.CurrentStreak(_state);
        _tasksStreak.Text = streak <= 0 ? "Streak: none yet" : $"Streak: {streak} {(streak == 1 ? "day" : "days")}";
        _tasksStreak.TooltipText = "Finish and collect all three tasks on consecutive days: 5 bonus coins per day of streak, up to 35.";
        foreach (var task in tasks)
        {
            var done = task.IsDone(_state); var claimed = DailyTasks.IsClaimedToday(_state, task);
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 30) }; row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new TextureRect { Texture = CozyTheme.Icons.Check(done || claimed), CustomMinimumSize = new Vector2(22, 22), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            var title = new Label { Text = task.Title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center,
                ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            title.AddThemeFontSizeOverride("font_size", 14);
            if (claimed) title.AddThemeColorOverride("font_color", CozyTheme.SoftText);
            row.AddChild(title);
            var reward = new Label { Text = "+" + task.Reward, VerticalAlignment = VerticalAlignment.Center };
            reward.AddThemeFontSizeOverride("font_size", 14); reward.AddThemeColorOverride("font_color", claimed ? CozyTheme.SoftText : CozyTheme.AccentDeep);
            row.AddChild(new TextureRect { Texture = CozyTheme.Icon("coins"), CustomMinimumSize = new Vector2(18, 18), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(reward);
            if (claimed)
                row.AddChild(new Label { Text = "Collected", CustomMinimumSize = new Vector2(74, 0), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center });
            else
            {
                var claim = new Button { Text = "Claim", CustomMinimumSize = new Vector2(74, 28), Disabled = !done || _busy || _recoveryRequired,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = done ? "Collect Pip's reward." : "Finish the task first." };
                var id = task.Id;
                claim.Pressed += () => _ = ClaimDailyTaskAsync(id);
                row.AddChild(claim);
            }
            _tasksRows.AddChild(row);
        }
    }

    private async Task ClaimDailyTaskAsync(string taskId)
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        _busy = true;
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision; var day = _state.Header.CalendarDay;
            var reward = DailyTasks.For(_state).Single(t => t.Id == taskId).Reward;
            _state = await Task.Run(() => _store.CommitDailyTaskReward(owner, taskId, revision));
            Audio.GameAudio.Play("coins");
            var line = $"+{reward} coins from Pip!";
            var bonus = DailyTasks.StreakCreditOn(_state, day);
            if (bonus > 0)
            {
                var streak = DailyTasks.CurrentStreak(_state);
                line += $" All three done: a {streak}-day streak earns +{bonus} bonus coins!";
                Audio.GameAudio.Play("ribbon", -6);
            }
            SetStatus(line);
        }
        catch (Exception ex) { SetStatus("Pip could not hand over that reward. " + PlainError(ex)); }
        finally { _busy = false; Refresh(); }
    }
}
