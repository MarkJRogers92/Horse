using System;
using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.DomainIntegration;
using Cloverbrook.Game.UI;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>Scene-first hub (D-012): the 3D yard behind the UI, horse portraits and click-to-inspect.</summary>
public partial class StableDay
{
    private const string AnatomicalMaskSetId = "canonical-anatomy.v1";
    private StableYard _yard = null!;
    private HorsePortraitStudio _portraits = null!;
    private readonly Dictionary<string, VisualDescriptor?> _descriptors = new();

    private void BuildHub()
    {
        _yard = new StableYard { Name = "StableYard" };
        AddChild(_yard);
        _portraits = new HorsePortraitStudio { Name = "PortraitStudio" };
        AddChild(_portraits);
    }

    /// <summary>Presentation-only descriptor for a saved horse, or null when it cannot be shown.</summary>
    private VisualDescriptor? DescriptorFor(HorseRecord horse)
    {
        if (horse.LifeState.Stage == LifeStage.Unborn) return null;
        var key = $"{horse.HorseId}:{horse.Revision}:{horse.LifeState.Stage}";
        if (_descriptors.TryGetValue(key, out var cached)) return cached;
        VisualDescriptor? descriptor = null;
        try
        {
            var resolution = new PhenotypeResolver().ResolveForStablePreview(horse, VisualCatalog.Instance.Version, AnatomicalMaskSetId);
            var (visual, reasons) = DomainBridge.FromPresentation(resolution);
            if (visual != null && VisualCatalog.Instance.Check(visual).Supported) descriptor = visual;
            else GD.Print($"[stable-hub] {horse.Name} shown as a silhouette: {string.Join("; ", reasons.Concat(visual == null ? Array.Empty<string>() : VisualCatalog.Instance.Check(visual).Problems))}");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[stable-hub] {horse.Name} could not be resolved for display: {ex.Message}");
        }
        _descriptors[key] = descriptor;
        return descriptor;
    }

    private void RefreshYard()
    {
        if (_state == null) return;
        var shown = _state.Horses
            .Where(h => !HorseMarket.IsSold(h) && h.OwnerId.Equals(_state.PlayerOwnerId) && h.LifeState.Stage != LifeStage.Unborn)
            .OrderBy(h => h.Name, StringComparer.Ordinal)
            .Select(h => (h.HorseId.ToString(), DescriptorFor(h)))
            .ToList();
        _yard.SetHorses(shown);
    }

    private static readonly Shader PortraitShader = GD.Load<Shader>("res://shaders/portrait_card.gdshader");

    /// <summary>A rounded portrait card that fills in when the render finishes.</summary>
    private Control Portrait(HorseRecord horse, int size, Color background)
    {
        var material = new ShaderMaterial { Shader = PortraitShader };
        material.SetShaderParameter("background", background);
        var picture = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = material, Texture = Silhouette(),
        };
        var descriptor = DescriptorFor(horse);
        if (descriptor == null) return picture;
        if (_portraits.TryGetCached(descriptor, out var ready)) { picture.Texture = ready ?? Silhouette(); return picture; }
        _ = SetPortraitAsync(picture, descriptor);
        return picture;
    }

    private async System.Threading.Tasks.Task SetPortraitAsync(TextureRect picture, VisualDescriptor descriptor)
    {
        var texture = await _portraits.RenderAsync(descriptor);
        if (IsInstanceValid(picture)) picture.Texture = texture ?? Silhouette();
    }

    private static ImageTexture? _silhouette;

    /// <summary>Neutral placeholder: never a substituted coat.</summary>
    private static ImageTexture Silhouette() => _silhouette ??= CreateSilhouette();

    private static ImageTexture CreateSilhouette()
    {
        const int n = 96;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var ink = new Color(CozyTheme.SoftText, 0.35f);
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            // A simple horse-head outline: a tilted ellipse for the head and a block for the neck.
            float hx = (x - 52) / 15f, hy = (y - 38) / 24f;
            float rx = hx * 0.87f + hy * 0.5f, ry = -hx * 0.5f + hy * 0.87f;
            bool head = rx * rx + ry * ry < 1f;
            bool neck = x > 30 && x < 58 && y > 44 && y < 96 && x - 30 > (y - 44) * 0.15f;
            bool ear = Math.Abs(x - 44) < 4 && y > 8 && y < 20;
            image.SetPixel(x, y, head || neck || ear ? ink : new Color(0, 0, 0, 0));
        }
        return ImageTexture.CreateFromImage(image);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click || _state == null) return;
        var key = _yard.PickHorse(click.Position);
        if (key == null) { _yard.Focus(null); return; }
        var horse = _state.Horses.FirstOrDefault(h => h.HorseId.ToString() == key);
        if (horse == null) return;
        _yard.Focus(key);
        ShowInspection(horse.HorseId);
        GetViewport().SetInputAsHandled();
    }
}
