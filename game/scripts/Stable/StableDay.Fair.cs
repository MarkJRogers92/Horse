using System;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Cloverbrook.Game.DomainIntegration;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// The weekly horse fair (D-017). Offers come from the domain, so the same three horses wait all week and
/// buying one is a single atomic save command. Each card shows the horse's real portrait and coat.
/// </summary>
public partial class StableDay
{
    private Control _fairOverlay = null!;
    private BunnySpeaker _fairSpeaker = null!;
    private HBoxContainer _fairCards = null!;
    private Button _fairButton = null!;
    private ulong _fairRevision;

    private Button FairButton()
    {
        _fairButton = CozyTheme.WithIcon(new Button { Text = "Horse fair", CustomMinimumSize = new Vector2(170, 50) }, "fair", 32);
        _fairButton.Pressed += ShowFair;
        return _fairButton;
    }

    private void BuildFairOverlay(Control root)
    {
        _fairOverlay = CozyTheme.Dialog(root, "The Horse Fair", 780, out var body, out var buttons, out _);
        CozyTheme.DialogHeader(body, "fair", 736, 96);
        _fairSpeaker = new BunnySpeaker { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddChild(_fairSpeaker);
        _fairCards = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _fairCards.AddThemeConstantOverride("separation", 14);
        body.AddChild(_fairCards);
        var close = new Button { Text = "Maybe later", CustomMinimumSize = new Vector2(160, 46) };
        close.Pressed += () => _fairOverlay.Visible = false;
        buttons.AddChild(close);
        CozyTheme.FinishDialog(body, buttons);
    }

    private void ShowFair()
    {
        if (_state == null) return;
        _fairRevision = _state.Header.SaveRevision;
        foreach (var child in _fairCards.GetChildren()) child.QueueFree();
        var offers = HorseFair.Offers(_state);
        var days = HorseFair.DaysUntilNextFair(_state.Header.CalendarDay);
        var open = offers.Count(o => !o.AlreadyBought);
        _fairSpeaker.Say(open == 0
            ? $"You found new friends at this week's fair! Fresh horses arrive in {Days(days)}."
            : $"Welcome to the fair! {open} lovely horse{(open == 1 ? " is" : "s are")} looking for a home. New faces arrive in {Days(days)}.");
        foreach (var offer in offers) _fairCards.AddChild(FairCard(offer));
        _fairOverlay.Visible = true;
    }

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    private Control FairCard(FairOffer offer)
    {
        var horse = offer.Horse;
        var card = new PanelContainer { CustomMinimumSize = new Vector2(232, 0) };
        card.AddThemeStyleboxOverride("panel", CozyTheme.Box(CozyTheme.Cream, 18, 10));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 4); card.AddChild(box);
        var portrait = Portrait(horse, 150, CardTint(horse));
        portrait.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        if (offer.AlreadyBought) portrait.Modulate = new Color(1, 1, 1, 0.55f);
        box.AddChild(portrait);
        var name = CozyTheme.Title(horse.Name, 20); name.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(name);
        var role = horse.ReproductionState.Role == ReproductiveRole.Mare ? "MARE" : "STALLION";
        var tag = CozyTheme.Title($"{CoatLabel(horse)} · {role}", 12);
        tag.HorizontalAlignment = HorizontalAlignment.Center; tag.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        box.AddChild(tag);
        var hints = new Label
        {
            Text = string.Join("\n", offer.Reasons.Skip(1)), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(212, 40),
        };
        hints.AddThemeFontSizeOverride("font_size", 12); hints.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        box.AddChild(hints);
        var block = _state == null ? "" : HorseFair.BlockReason(_state, offer);
        var buy = CozyTheme.WithIcon(new Button
        {
            Text = offer.AlreadyBought ? "Coming home!" : $"Buy · {offer.PriceCoins:N0}",
            CustomMinimumSize = new Vector2(0, 46), Disabled = block != null || _busy || _recoveryRequired,
            TooltipText = block == null ? "" : Capitalise(block),
        }, offer.AlreadyBought ? "foal" : "coins", 26);
        buy.Pressed += () => _ = BuyAsync(offer);
        box.AddChild(buy);
        if (block != null && !offer.AlreadyBought)
        {
            var why = new Label { Text = Capitalise(block), AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
            why.AddThemeFontSizeOverride("font_size", 11); why.AddThemeColorOverride("font_color", CozyTheme.SoftText);
            box.AddChild(why);
        }
        return card;
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string CoatLabel(HorseRecord horse)
    {
        try
        {
            var label = new PhenotypeResolver().ResolveForPresentation(horse, DomainBridge.Matrix, "label", "label").DisplayLabel;
            return string.IsNullOrEmpty(label) ? "Horse" : label!;
        }
        catch (Exception) { return "Horse"; }
    }

    private async Task BuyAsync(FairOffer offer)
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        if (_state.Header.SaveRevision != _fairRevision) { ShowFair(); SetStatus("The stable changed; here is the fair again."); return; }
        _busy = true; var bought = false;
        Refresh();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision;
            _state = await Task.Run(() => _store.CommitPurchase(owner, offer.HorseId, revision, offer.PriceCoins));
            _fairOverlay.Visible = false;
            SetStatus($"{offer.Name} has joined your stable! −{offer.PriceCoins:N0} coins.");
            Audio.GameAudio.Play("coins");
            bought = true;
        }
        catch (Exception ex)
        {
            SetStatus("The purchase was not saved. Reload the stable and try again. " + PlainError(ex));
        }
        finally
        {
            _busy = false;
            Refresh();
            if (bought) _yard.Cheer(offer.HorseId.ToString());
        }
    }
}
