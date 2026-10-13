using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// Stable shop (D-022): build extra stalls and buy decorations for the yard. Purchases are domain ledger commands;
/// where each decoration stands is a display preference kept in decor.cfg beside the save.
/// </summary>
public partial class StableDay
{
    private Control _shopOverlay = null!;
    private Label _shopStallLabel = null!;
    private Button _shopStallButton = null!;
    private GridContainer _shopGrid = null!;
    private Label _shopSummary = null!;
    private Dictionary<string, int> _decorSpots = new(StringComparer.Ordinal);
    private bool _decorLoaded;

    private static readonly Dictionary<string, float> DecorHeights = new(StringComparer.Ordinal)
    {
        ["planter_box"] = 0.75f, ["hay_props"] = 1.6f, ["bench"] = 1.0f, ["wheelbarrow"] = 0.85f, ["saddle_rack"] = 1.25f,
        ["water_trough"] = 0.85f, ["flower_cart"] = 1.6f, ["lamp_post"] = 2.6f, ["birdhouse"] = 1.4f, ["picnic_table"] = 1.0f, ["rose_arch"] = 2.6f,
    };

    private string DecorPath => Path.Combine(Path.GetDirectoryName(_store.SavePath) ?? ".", "decor.cfg");

    private Button ShopButton()
    {
        var button = CozyTheme.WithIcon(new Button { Text = "Shop", CustomMinimumSize = new Vector2(110, 44) }, "coins", 26);
        button.Pressed += ShowShop;
        return button;
    }

    private void BuildShopOverlay(Control root)
    {
        _shopOverlay = CozyTheme.Dialog(root, "Stable Shop", 860, out var body, out var buttons, out _);
        var stallRow = new HBoxContainer(); stallRow.AddThemeConstantOverride("separation", 12);
        var stallIcon = new TextureRect { Texture = GD.Load<Texture2D>("res://assets/scenery/stable_block/thumbnail.png"), CustomMinimumSize = new Vector2(84, 84),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        stallRow.AddChild(stallIcon);
        var stallText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        stallText.AddChild(CozyTheme.Title("More stalls", 18));
        _shopStallLabel = SmallText("");
        stallText.AddChild(_shopStallLabel);
        stallRow.AddChild(stallText);
        _shopStallButton = CozyTheme.WithIcon(new Button { CustomMinimumSize = new Vector2(190, 46), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }, "coins", 24);
        _shopStallButton.Pressed += () => _ = BuildStallAsync();
        stallRow.AddChild(_shopStallButton);
        body.AddChild(stallRow);
        var heading = CozyTheme.Title("DECORATE YOUR YARD", 12); heading.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        body.AddChild(heading);
        _shopSummary = SmallText("");
        body.AddChild(_shopSummary);
        _shopGrid = new GridContainer { Columns = 4 };
        _shopGrid.AddThemeConstantOverride("h_separation", 10); _shopGrid.AddThemeConstantOverride("v_separation", 10);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 330), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_shopGrid);
        body.AddChild(scroll);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(130, 46) };
        close.Pressed += () => _shopOverlay.Visible = false;
        buttons.AddChild(close);
        CozyTheme.FinishDialog(body, buttons);
    }

    private void ShowShop()
    {
        if (_state == null) return;
        FillShop();
        _shopOverlay.Visible = true;
    }

    private void FillShop()
    {
        if (_state == null) return;
        var stalls = StableShop.Stalls(_state);
        var next = StableShop.NextStallPrice(_state);
        var used = _state.Horses.Count(h => !HorseMarket.IsSold(h)) + _state.PendingCapacityPlans.Count;
        _shopStallLabel.Text = next == null
            ? $"Your stable has all {stalls} stalls ({used} in use). The annex is finished!"
            : $"{stalls} stalls, {used} in use. A new stall gives room for another horse or foal.";
        var stallBlock = StableShop.StallBlockReason(_state);
        _shopStallButton.Text = next == null ? "All built" : $"Build · {next:N0}";
        _shopStallButton.Disabled = stallBlock != null || _busy || _recoveryRequired;
        _shopStallButton.TooltipText = stallBlock ?? "";

        foreach (var child in _shopGrid.GetChildren()) child.QueueFree();
        var owned = StableShop.OwnedDecor(_state).Count;
        _shopSummary.Text = $"{owned} of {StableShop.Decor.Count} decorations. Up to {StableYard.DecorSpots.Length} stand in the yard; use Move to try another spot.";
        foreach (var item in StableShop.Decor) _shopGrid.AddChild(DecorCard(item));
    }

    private Control DecorCard(DecorItem item)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(196, 0) };
        var mine = _state != null && StableShop.Owns(_state, item.Id);
        var style = CozyTheme.Box(mine ? Color.FromHtml("#fff4d6") : CozyTheme.Cream, 16, 8);
        style.BorderColor = mine ? Color.FromHtml("#f5c542") : CozyTheme.Border; style.SetBorderWidthAll(mine ? 3 : 2);
        card.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 4); card.AddChild(box);
        var path = $"res://assets/scenery/{item.Asset}/thumbnail.png";
        box.AddChild(new TextureRect
        {
            Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : Silhouette(), CustomMinimumSize = new Vector2(150, 110),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        var name = CozyTheme.Title(item.Label, 15); name.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(name);
        if (mine)
        {
            var spot = _decorSpots.TryGetValue(item.Id, out var s) ? s : -1;
            var where = SmallText(spot >= 0 ? $"In the yard · spot {spot + 1}" : "In storage (every spot is used)");
            where.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(where);
            var move = new Button { Text = "Move", CustomMinimumSize = new Vector2(0, 40) };
            move.Pressed += () => MoveDecor(item.Id);
            box.AddChild(move);
        }
        else
        {
            var block = _state == null ? "" : StableShop.DecorBlockReason(_state, item.Id);
            var buy = CozyTheme.WithIcon(new Button
            {
                Text = $"Buy · {item.PriceCoins:N0}", CustomMinimumSize = new Vector2(0, 40),
                Disabled = block != null || _busy || _recoveryRequired, TooltipText = block ?? "",
            }, "coins", 22);
            buy.Pressed += () => _ = BuyDecorAsync(item.Id);
            box.AddChild(buy);
        }
        return card;
    }

    private async Task BuildStallAsync()
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        var price = StableShop.NextStallPrice(_state);
        if (price == null) return;
        _busy = true;
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision; var quoted = price.Value;
            _state = await Task.Run(() => _store.CommitStall(owner, revision, quoted));
            Audio.GameAudio.Play("coins");
            Audio.GameAudio.Play("birth_fanfare", -10);
            SetStatus($"The builders finished a new stall! Your stable now has {StableShop.Stalls(_state)} home places.");
        }
        catch (Exception ex) { SetStatus("The stall was not built. " + PlainError(ex)); }
        finally { _busy = false; Refresh(); FillShop(); }
    }

    private async Task BuyDecorAsync(string itemId)
    {
        if (_state == null || _busy || _recoveryRequired || _pendingRequest != null || _pendingBreedingRequest != null) return;
        _busy = true;
        try
        {
            var owner = _state.PlayerOwnerId; var revision = _state.Header.SaveRevision;
            _state = await Task.Run(() => _store.CommitDecor(owner, itemId, revision));
            Audio.GameAudio.Play("coins");
            var label = StableShop.Decor.Single(d => d.Id == itemId).Label;
            SetStatus($"Your new {label.ToLowerInvariant()} is in the yard!");
        }
        catch (Exception ex) { SetStatus("That was not bought. " + PlainError(ex)); }
        finally { _busy = false; Refresh(); FillShop(); }
    }

    /// <summary>Move a decoration to the next spot, swapping with whatever stands there.</summary>
    private void MoveDecor(string itemId)
    {
        if (_state == null) return;
        var spots = StableYard.DecorSpots.Length;
        var current = _decorSpots.TryGetValue(itemId, out var s) ? s : -1;
        var target = (current + 1) % spots;
        var occupant = _decorSpots.FirstOrDefault(p => p.Value == target && p.Key != itemId).Key;
        if (occupant != null) { if (current >= 0) _decorSpots[occupant] = current; else _decorSpots.Remove(occupant); }
        _decorSpots[itemId] = target;
        SaveDecorSpots();
        RefreshDecor();
        FillShop();
        Audio.GameAudio.Play("ui_toggle", -8);
    }

    /// <summary>Called from Refresh: show owned decorations and built stalls in the yard.</summary>
    private void RefreshDecor()
    {
        if (_state == null) return;
        if (!_decorLoaded) LoadDecorSpots();
        var owned = StableShop.OwnedDecor(_state);
        foreach (var gone in _decorSpots.Keys.Where(k => owned.All(o => o.Id != k)).ToList()) _decorSpots.Remove(gone);
        foreach (var item in owned.Where(o => !_decorSpots.ContainsKey(o.Id)))
        {
            var free = Enumerable.Range(0, StableYard.DecorSpots.Length).FirstOrDefault(i => !_decorSpots.ContainsValue(i), -1);
            if (free < 0) break;
            _decorSpots[item.Id] = free;
        }
        _yard.SetDecor(owned.Where(o => _decorSpots.ContainsKey(o.Id))
            .Select(o => (o.Id, o.Asset, DecorHeights.TryGetValue(o.Asset, out var h) ? h : 1f, _decorSpots[o.Id])).ToList());
        _yard.SetStalls(StableShop.Stalls(_state));
    }

    private void LoadDecorSpots()
    {
        _decorLoaded = true;
        var config = new ConfigFile();
        if (File.Exists(DecorPath) && config.Load(DecorPath) == Error.Ok)
            foreach (var key in config.GetSectionKeys("spots"))
                _decorSpots[key] = (int)config.GetValue("spots", key, -1);
        foreach (var bad in _decorSpots.Where(p => p.Value < 0 || p.Value >= StableYard.DecorSpots.Length).Select(p => p.Key).ToList()) _decorSpots.Remove(bad);
    }

    private void SaveDecorSpots()
    {
        if (_foalingDemoMode) return;
        var config = new ConfigFile();
        foreach (var (key, spot) in _decorSpots) config.SetValue("spots", key, spot);
        config.Save(DecorPath);
    }
}
