using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// The practice gallop in 3D (D-015): the entrants gallop down a sand track with white rails and a
/// chequered finish while a camera tracks the leader. Positions come only from the domain's recorded
/// frames (0-1000 of the distance), so this view can never change a result.
/// </summary>
public partial class RaceView : SubViewport
{
    public const float TrackLength = 110f;
    private const float LaneSpacing = 1.9f;
    private readonly List<HorsePresenter> _horses = new();
    private Camera3D _camera = null!;
    private Node3D _world = null!;
    private float _cameraX;

    public override void _Ready()
    {
        OwnWorld3D = true;
        Msaa3D = Msaa.Msaa2X;
        RenderTargetUpdateMode = UpdateMode.Always;
        _world = new Node3D { Name = "RaceWorld" };
        AddChild(_world);
        _world.AddChild(Lighting.Build(LightingMode.Stable));
        BuildTrack();
        _camera = new Camera3D { Fov = 34, Current = true };
        _world.AddChild(_camera);
        PlaceCamera(0, 1);
    }

    /// <summary>Put one galloping horse per lane; null descriptors leave an empty lane.</summary>
    public void Setup(IReadOnlyList<VisualDescriptor?> entrants)
    {
        foreach (var horse in _horses) horse.QueueFree();
        _horses.Clear();
        for (var lane = 0; lane < entrants.Count; lane++)
        {
            var presenter = new HorsePresenter { Name = "Runner" + lane };
            _world.AddChild(presenter);
            if (entrants[lane] is { } descriptor) presenter.PresentGallop(descriptor);
            presenter.Position = new Vector3(0, 0, LaneZ(lane, entrants.Count));
            _horses.Add(presenter);
        }
        _cameraX = 0;
        PlaceCamera(0, 1);
    }

    /// <summary>Move each runner to its recorded progress (0-1000).</summary>
    public void SetProgress(IReadOnlyList<float> progress, double delta)
    {
        for (var lane = 0; lane < _horses.Count && lane < progress.Count; lane++)
        {
            var h = _horses[lane];
            h.Position = new Vector3(progress[lane] / 1000f * TrackLength, 0, h.Position.Z);
        }
        float lead = progress.Count == 0 ? 0 : progress.Max() / 1000f * TrackLength;
        float k = 1f - Mathf.Exp(-4f * (float)delta);
        // Follow the leader, but stop short of the line so the runners gallop across it in view.
        float goal = Mathf.Min(lead - 1.5f, TrackLength - 3.5f);
        _cameraX = Mathf.Abs(goal - _cameraX) > 12f ? goal : Mathf.Lerp(_cameraX, goal, k);
        PlaceCamera(_cameraX, _horses.Count);
    }

    /// <summary>Stop the gallop at the line: runners settle into Idle.</summary>
    public void Finish()
    {
        foreach (var h in _horses) if (h.Descriptor != null) h.Present(h.Descriptor, "idle");
    }

    private static float LaneZ(int lane, int count) => (lane - (count - 1) / 2f) * LaneSpacing;

    private void PlaceCamera(float x, int lanes)
    {
        if (_camera == null) return;
        float depth = 7.2f + lanes * 1.0f;
        _camera.Position = new Vector3(x + 2.2f, 2.6f, depth);
        _camera.LookAt(new Vector3(x + 1.2f, 0.9f, 0), Vector3.Up);
    }

    private void BuildTrack()
    {
        var grass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass_ground.gdshader") };
        _world.AddChild(Sets.Mesh(new PlaneMesh { Size = new Vector2(TrackLength + 120, 120) }, grass, new Vector3(TrackLength / 2, 0, 0)));
        var sand = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/paddock_sand.gdshader") };
        var track = Sets.Mesh(new BoxMesh { Size = new Vector3(TrackLength + 40, 0.02f, 10f) }, Sets.Mat(Color.FromHtml("#e3c9a0"), 1f), new Vector3(TrackLength / 2, 0.01f, 0));
        _world.AddChild(track);
        var white = Sets.Mat(Sets.Cream, 0.6f);
        foreach (var z in new[] { -5.3f, 5.3f })
        {
            for (float x = -20; x <= TrackLength + 20; x += 3f)
                _world.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(0.12f, 1.1f, 0.12f) }, white, new Vector3(x, 0.55f, z)));
            _world.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(TrackLength + 40, 0.1f, 0.08f) }, white, new Vector3(TrackLength / 2, 1.05f, z)));
        }
        // Chequered finish line and two posts with a banner.
        for (var i = 0; i < 20; i++)
            _world.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(0.5f, 0.025f, 0.5f) },
                Sets.Mat(i % 2 == 0 ? Colors.White : Sets.Plum, 0.8f), new Vector3(TrackLength + (i / 10) * 0.5f, 0.02f, -4.75f + (i % 10) * 1.0f + (i / 10) * 0.5f)));
        foreach (var z in new[] { -5.6f, 5.6f })
            _world.AddChild(Sets.Mesh(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 3.6f }, white, new Vector3(TrackLength, 1.8f, z)));
        _world.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(0.1f, 0.7f, 11.2f) }, Sets.Mat(Color.FromHtml("#f3a6c0"), 0.7f), new Vector3(TrackLength, 3.3f, 0)));
        var label = new Label3D { Text = "FINISH", FontSize = 120, PixelSize = 0.006f, Modulate = Colors.White, OutlineSize = 0,
            Font = UI.CozyTheme.BoldFont, Position = new Vector3(TrackLength + 0.06f, 3.3f, 0), RotationDegrees = new Vector3(0, 90, 0) };
        _world.AddChild(label);
        // Trees and hills along the far side so the backdrop has depth.
        var rng = new RandomNumberGenerator { Seed = 41 };
        var oak = ResourceLoader.Exists("res://assets/scenery/oak_tree/oak_tree.glb") ? GD.Load<PackedScene>("res://assets/scenery/oak_tree/oak_tree.glb") : null;
        for (float x = -10; x < TrackLength + 30; x += rng.RandfRange(7, 12))
        {
            Node3D tree;
            if (oak != null) { tree = oak.Instantiate<Node3D>(); tree.Scale = Vector3.One * rng.RandfRange(4.5f, 6.5f); }
            else tree = Sets.Mesh(new SphereMesh { Radius = 2.2f, Height = 4f }, Sets.Mat(Sets.Sage, 1f), Vector3.Zero);
            tree.Position = new Vector3(x, oak != null ? 0 : 3f, rng.RandfRange(-16, -11));
            tree.RotationDegrees = new Vector3(0, rng.RandfRange(0, 360), 0);
            _world.AddChild(tree);
        }
        var hill = Sets.Mat(Color.FromHtml("#b3d39c"), 1f);
        for (var i = 0; i < 8; i++)
        {
            var h = Sets.Mesh(new SphereMesh { Radius = 1, Height = 2 }, hill, new Vector3(-20 + i * 22, -6, -45));
            h.Scale = new Vector3(rng.RandfRange(16, 24), rng.RandfRange(8, 12), 10);
            _world.AddChild(h);
        }
    }
}
