using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// Renders head-and-shoulders horse portraits (D-012) through the one
/// <see cref="HorsePresenter"/> in a private, transparent SubViewport. Portraits are
/// cached by descriptor cache key. Unsupported descriptors return null so callers
/// show a silhouette instead of a substituted coat.
/// </summary>
public partial class HorsePortraitStudio : SubViewport
{
    public const int PortraitSize = 320;
    private readonly Dictionary<string, ImageTexture?> _cache = new();
    private readonly Queue<(VisualDescriptor Descriptor, TaskCompletionSource<ImageTexture?> Done)> _queue = new();
    private HorsePresenter _presenter = null!;
    private Camera3D _camera = null!;
    private bool _rendering;

    public override void _Ready()
    {
        Size = new Vector2I(PortraitSize, PortraitSize);
        OwnWorld3D = true;
        TransparentBg = true;
        Msaa3D = Msaa.Msaa4X;
        RenderTargetUpdateMode = UpdateMode.Disabled;

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.93f, 0.9f, 0.95f),
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.05f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D { LightColor = new Color(1f, 0.92f, 0.82f), LightEnergy = 1.9f, RotationDegrees = new Vector3(-35, 55, 0) });
        AddChild(new DirectionalLight3D { LightColor = new Color(0.8f, 0.86f, 1f), LightEnergy = 0.5f, RotationDegrees = new Vector3(-15, -120, 0) });
        AddChild(new DirectionalLight3D { LightColor = new Color(1f, 0.85f, 0.92f), LightEnergy = 0.7f, RotationDegrees = new Vector3(-10, 200, 0) });
        _presenter = new HorsePresenter { Name = "PortraitHorse" };
        AddChild(_presenter);
        _camera = new Camera3D { Fov = 30, Current = true };
        AddChild(_camera);
    }

    public override void _ExitTree()
    {
        // Drop cached textures so nothing outlives the scene at shutdown.
        foreach (var texture in _cache.Values) texture?.Dispose();
        _cache.Clear();
    }

    public bool Busy => _rendering || _queue.Count > 0;

    public bool TryGetCached(VisualDescriptor descriptor, out ImageTexture? texture) => _cache.TryGetValue(descriptor.CacheKey, out texture);

    public Task<ImageTexture?> RenderAsync(VisualDescriptor descriptor)
    {
        if (_cache.TryGetValue(descriptor.CacheKey, out var cached)) return Task.FromResult(cached);
        var done = new TaskCompletionSource<ImageTexture?>();
        _queue.Enqueue((descriptor, done));
        if (!_rendering) _ = DrainAsync();
        return done.Task;
    }

    private async Task DrainAsync()
    {
        _rendering = true;
        while (_queue.Count > 0)
        {
            var (descriptor, done) = _queue.Dequeue();
            if (_cache.TryGetValue(descriptor.CacheKey, out var cached)) { done.SetResult(cached); continue; }
            ImageTexture? texture = null;
            var support = _presenter.Present(descriptor, "idle");
            if (support.Supported)
            {
                if (_presenter.Animator is { } animator && animator.IsPlaying())
                {
                    animator.Seek(0.4, true);
                    animator.Pause();
                }
                Frame(descriptor.IsFoal);
                RenderTargetUpdateMode = UpdateMode.Always;
                for (var i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var image = GetTexture().GetImage();
                if (System.Environment.GetEnvironmentVariable("CLOVERBROOK_PORTRAIT_DUMP") is { Length: > 0 } dump)
                    image.SavePng(System.IO.Path.Combine(dump, descriptor.CacheKey[..12] + ".png"));
                texture = ImageTexture.CreateFromImage(image);
                RenderTargetUpdateMode = UpdateMode.Disabled;
            }
            _cache[descriptor.CacheKey] = texture;
            done.SetResult(texture);
        }
        _rendering = false;
    }

    private void Frame(bool foal)
    {
        // Horse faces +X with its mane side toward +Z: a three-quarter front view of
        // the head and shoulders, like a trading-card portrait.
        float s = foal ? 0.66f : 1f;
        var head = _presenter.HeadViewTarget ?? new Vector3(1.2f * s, 1.75f * s, 0);
        var target = head.Lerp(new Vector3(0.55f * s, 1.25f * s, 0), 0.18f);
        var direction = new Vector3(0.8f, 0.16f, 0.58f).Normalized();
        _camera.Position = target + direction * (2.35f * s);
        _camera.LookAt(target, Vector3.Up);
    }
}
