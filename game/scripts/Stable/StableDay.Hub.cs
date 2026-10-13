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
        var args = OS.GetCmdlineUserArgs();
        _yard = new StableYard { Name = "StableYard", Wandering = !args.Any(a => a.StartsWith("--stable-", StringComparison.Ordinal)) };
        AddChild(_yard);
        _portraits = new HorsePortraitStudio { Name = "PortraitStudio" };
        AddChild(_portraits);
    }

    /// <summary>--hub-preview=DIR: timed screenshots of a live (wandering) yard, for visual review.</summary>
    private async System.Threading.Tasks.Task RunHubPreviewAsync(string output)
    {
        try
        {
            System.IO.Directory.CreateDirectory(output);
            foreach (var (seconds, name) in new[] { (3.0, "01_hub_live"), (14.0, "02_hub_live"), (24.0, "03_hub_live") })
            {
                while (_yard.ElapsedSeconds < seconds) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CaptureFrame(System.IO.Path.Combine(output, name + ".png"));
            }
            _uiLayer.Visible = false;
            await CaptureFrame(System.IO.Path.Combine(output, "04_yard_only.png"));
            GD.Print("[hub-preview] OK " + output);
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError("[hub-preview] FAILED " + ex); GetTree().Quit(1); }
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

    private static ShaderMaterial PortraitMaterial(Color background)
    {
        var material = new ShaderMaterial { Shader = PortraitShader };
        material.SetShaderParameter("background", background);
        return material;
    }

    /// <summary>A rounded portrait card that fills in when the render finishes.</summary>
    private Control Portrait(HorseRecord horse, int size, Color background)
    {
        var material = PortraitMaterial(background);
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
            // A horseshoe: an open ring, thicker at the toe, with nail holes.
            float dx = x - 48f, dy = y - 44f, r = MathF.Sqrt(dx * dx + dy * dy);
            bool open = dy < -6f && MathF.Abs(dx) < 15f;
            float thickness = 9f + MathF.Max(0f, dy) * 0.12f;
            bool shoe = r < 30f && r > 30f - thickness && !open;
            bool nail = MathF.Abs(r - (30f - thickness / 2f)) < 1.6f && (MathF.Abs(MathF.Atan2(dy, dx) * 180f / MathF.PI % 30f) < 4f);
            image.SetPixel(x, y, shoe && !nail ? ink : new Color(0, 0, 0, 0));
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
