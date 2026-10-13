using System;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// Hands-on care (D-021). Clicking a horse in the yard opens a little bubble beside it: pat, brush, give a carrot
/// or open Details. Each interaction is one free save command that raises the horse's bond once a day; the horse
/// answers with hearts and a happy sound.
/// </summary>
public partial class StableDay
{
    private PanelContainer? _careBubble;
    private HorseId? _careHorse;
    private Vector2 _careNear;

    private static readonly (string Kind, string Label, string Icon)[] CareActions =
    {
        ("pat", "Pat", "breeding"), ("brush", "Brush", "grooming"), ("carrot", "Carrot", "feed"),
    };

    private void ShowCareBubble(HorseId horseId, Vector2 near)
    {
        if (_state == null) return;
        var horse = _state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
        if (horse == null) return;
        CloseCareBubble();
        _careHorse = horseId; _careNear = near;
        var bubble = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var style = CozyTheme.Box(CozyTheme.Cream, 20, 10); style.BorderColor = CozyTheme.Accent; style.SetBorderWidthAll(3);
        style.ShadowColor = new Color(0.4f, 0.2f, 0.35f, 0.25f); style.ShadowSize = 8;
        bubble.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6); bubble.AddChild(box);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
        head.AddChild(CozyTheme.Title(horse.Name, 18));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "×", CustomMinimumSize = new Vector2(30, 30) };
        close.Pressed += CloseCareBubble;
        head.AddChild(close);
        box.AddChild(head);
        var bondRow = new HBoxContainer(); bondRow.AddThemeConstantOverride("separation", 6);
        var bondLabel = CozyTheme.Title("BOND", 11); bondLabel.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        bondLabel.VerticalAlignment = VerticalAlignment.Center;
        bondRow.AddChild(bondLabel);
        bondRow.AddChild(CozyTheme.HeartRating(HorseBond.Hearts(horse)));
        box.AddChild(bondRow);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 6);
        foreach (var (kind, label, icon) in CareActions)
        {
            var block = HorseBond.BlockReason(_state, horseId, kind);
            var button = CozyTheme.WithIcon(new Button
            {
                Text = label, CustomMinimumSize = new Vector2(96, 44), Disabled = block != null || _busy || _recoveryRequired,
                TooltipText = block ?? $"{label} {horse.Name}",
            }, icon, 24);
            button.Pressed += () => _ = InteractAsync(horseId, kind);
            actions.AddChild(button);
        }
        box.AddChild(actions);
        var details = new Button { Text = "Details", CustomMinimumSize = new Vector2(0, 38) };
        details.Pressed += () => { CloseCareBubble(); ShowInspection(horseId); };
        box.AddChild(details);
        if (CareActions.All(a => HorseBond.BlockReason(_state, horseId, a.Kind) != null))
            box.AddChild(SmallText($"{horse.Name} has had lots of love today. Come back tomorrow!"));
        _uiLayer.GetChild(0).AddChild(bubble);
        // Beside the horse, kept on screen.
        bubble.ResetSize();
        var screen = GetViewport().GetVisibleRect().Size;
        var size = bubble.GetCombinedMinimumSize();
        bubble.Position = new Vector2(Mathf.Clamp(near.X + 24, 16, screen.X - size.X - 16), Mathf.Clamp(near.Y - size.Y / 2, 80, screen.Y - size.Y - 80));
        _careBubble = bubble;
    }

    private void CloseCareBubble()
    {
        _careBubble?.QueueFree(); _careBubble = null; _careHorse = null;
    }

    private async Task InteractAsync(HorseId horseId, string kind)
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        var near = _careNear;
        _busy = true;
        var before = _state.Horses.Single(h => h.HorseId.Equals(horseId));
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision;
            _state = await Task.Run(() => _store.CommitInteraction(owner, horseId, kind, revision));
            var after = _state.Horses.Single(h => h.HorseId.Equals(horseId));
            _yard.Cheer(horseId.ToString());
            Audio.GameAudio.Play(kind == "brush" ? "care_cheer" : kind == "carrot" ? "hoof_clop" : "ribbon", kind == "pat" ? -10 : -6);
            var line = kind switch
            {
                "pat" => $"{after.Name} nuzzles your hand.",
                "brush" => $"{after.Name}'s coat is shining.",
                _ => $"{after.Name} crunches the carrot happily.",
            };
            if (HorseBond.Hearts(after) > HorseBond.Hearts(before)) line += $" Your bond grew to {HorseBond.Hearts(after)} hearts!";
            SetStatus(line);
        }
        catch (Exception ex)
        {
            SetStatus("That was not saved. " + PlainError(ex));
        }
        finally
        {
            _busy = false;
            Refresh();
            if (_careHorse != null && _careHorse.Equals(horseId)) ShowCareBubble(horseId, near);
        }
    }
}
