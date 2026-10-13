using System;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>Club buyer offers (P2-03 slice, D-010). The offer is a domain quote; selling is one atomic save command.</summary>
public partial class StableDay
{
    private Control _saleOverlay = null!;
    private Label _saleTitle = null!;
    private BunnySpeaker _saleSpeaker = null!;
    private Label _saleDetails = null!;
    private Button _confirmSaleButton = null!;
    private SaleOffer? _reviewedOffer;
    private HBoxContainer _saleRow = null!;
    private ulong _reviewedOfferRevision;

    private void BuildSaleOverlay(Control root)
    {
        _saleOverlay = CozyTheme.Dialog(root, "A buyer is interested", 600, out var body, out var buttons, out _saleTitle);
        CozyTheme.DialogHeader(body, "sale", 556, 110);
        _saleRow = new HBoxContainer(); _saleRow.AddThemeConstantOverride("separation", 12);
        _saleSpeaker = new BunnySpeaker { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _saleRow.AddChild(_saleSpeaker);
        body.AddChild(_saleRow);
        _saleDetails = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(540, 0) };
        body.AddChild(_saleDetails);
        var keep = new Button { Text = "Keep my horse", CustomMinimumSize = new Vector2(160, 46) };
        keep.Pressed += () => { _saleOverlay.Visible = false; _reviewedOffer = null; };
        buttons.AddChild(keep);
        _confirmSaleButton = CozyTheme.WithIcon(new Button { Text = "Sell", CustomMinimumSize = new Vector2(180, 46) }, "sell", 28);
        _confirmSaleButton.Pressed += () => _ = ConfirmSaleAsync();
        buttons.AddChild(_confirmSaleButton);
        CozyTheme.FinishDialog(body, buttons);
    }

    private void ShowSaleOffer(HorseId horseId)
    {
        if (_state == null || _busy) return;
        var offer = HorseMarket.Quote(_state, horseId);
        if (!offer.Allowed) { SetStatus($"{offer.Name} cannot be sold: {offer.BlockReason}"); return; }
        _reviewedOffer = offer; _reviewedOfferRevision = _state.Header.SaveRevision;
        _saleSpeaker.Say($"{offer.BuyerName} would love to give {offer.Name} a happy home! They offer {offer.PriceCoins:N0} coins.");
        _saleDetails.Text = string.Join("\n", offer.Reasons.Select(r => "• " + r)
            .Concat(new[] { "", $"Wallet after the sale: {_state.Coins + offer.PriceCoins:N0} coins.",
                $"{offer.Name} keeps their place in your family tree, and frees a home place for a new foal." }));
        _confirmSaleButton.Text = $"Sell · +{offer.PriceCoins:N0}";
        foreach (var old in _saleRow.GetChildren().Where(c => c != _saleSpeaker)) old.QueueFree();
        var horse = _state.Horses.Single(h => h.HorseId.Equals(horseId));
        var portrait = Portrait(horse, 120, CardTint(horse)); portrait.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _saleRow.AddChild(portrait); _saleRow.MoveChild(portrait, 0);
        _inspectOverlay.Visible = false;
        _saleOverlay.Visible = true;
    }

    private async Task ConfirmSaleAsync()
    {
        var offer = _reviewedOffer;
        if (_state == null || offer == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        if (_state.Header.SaveRevision != _reviewedOfferRevision) { _saleOverlay.Visible = false; SetStatus("The stable changed; ask for a fresh offer."); return; }
        _busy = true; _confirmSaleButton.Disabled = true;
        Refresh();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision;
            _state = await Task.Run(() => _store.CommitSale(owner, offer.HorseId, revision, offer.PriceCoins));
            _saleOverlay.Visible = false; _reviewedOffer = null;
            SetStatus($"{offer.Name} has gone to {offer.BuyerName}. +{offer.PriceCoins:N0} coins.");
        }
        catch (Exception ex)
        {
            SetStatus("The sale was not saved. Reload the stable and try again. " + PlainError(ex));
        }
        finally
        {
            _busy = false; _confirmSaleButton.Disabled = false;
            Refresh();
        }
    }
}
