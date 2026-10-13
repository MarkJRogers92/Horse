using System;
using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// The live 3D stable yard behind the stable UI (D-012). Scenery comes from the Meshy
/// assets in res://assets/scenery when present, otherwise from procedural stand-ins, so
/// CI never depends on a generated file. Horses are shown through HorsePresenter only.
/// </summary>
public partial class StableYard : Node3D
{
    private const string SceneryRoot = "res://assets/scenery/";
    // Paddock slots in front of the barn, all inside the screen area the side panels leave open.
    private static readonly (Vector3 Position, float Yaw)[] Slots =
    {
        (new Vector3(-0.9f, 0, 1.0f), 18f), (new Vector3(1.7f, 0, -0.5f), 160f), (new Vector3(0.7f, 0, 2.6f), 335f),
        (new Vector3(-3.0f, 0, -1.2f), 60f), (new Vector3(2.6f, 0, -2.0f), 125f), (new Vector3(-0.2f, 0, -2.2f), 200f),
    };
    private readonly List<(string Key, HorsePresenter Presenter)> _horses = new();
    private Camera3D _camera = null!;
    private static readonly Vector3 HomeTarget = new(0.45f, 0.75f, -0.4f);
    private Vector3 _lookTarget = HomeTarget;
    private Vector3 _cameraHome = new(0.45f, 3.7f, 11.6f);
    private Vector3 _cameraGoal;
    private Vector3 _targetGoal;
    private double _time;

    public override void _Ready()
    {
        AddChild(Lighting.Build(LightingMode.Stable));
        BuildGround();
        BuildScenery();
        _camera = new Camera3D { Fov = 40, Current = true, Position = _cameraHome };
        AddChild(_camera);
        _cameraGoal = _cameraHome; _targetGoal = _lookTarget;
        _camera.LookAt(_lookTarget, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        // Slow cinematic drift plus easing toward a selected horse.
        var drift = new Vector3(Mathf.Sin((float)_time * 0.08f) * 0.6f, Mathf.Sin((float)_time * 0.11f) * 0.12f, 0);
        float k = 1f - Mathf.Exp(-2.5f * (float)delta);
        _lookTarget = _lookTarget.Lerp(_targetGoal, k);
        _camera.Position = _camera.Position.Lerp(_cameraGoal + drift, k);
        _camera.LookAt(_lookTarget, Vector3.Up);
    }

    /// <summary>Show exactly these horses (key = horse id), reusing presenters that did not change.</summary>
    public void SetHorses(IReadOnlyList<(string Key, VisualDescriptor? Descriptor)> horses)
    {
        var wanted = horses.Where(h => h.Descriptor != null).Take(Slots.Length).ToList();
        foreach (var stale in _horses.Where(h => wanted.All(w => w.Key != h.Key || w.Descriptor!.CacheKey != h.Presenter.Descriptor?.CacheKey)).ToList())
        {
            stale.Presenter.QueueFree();
            _horses.Remove(stale);
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var (key, descriptor) = wanted[i];
            var existing = _horses.FirstOrDefault(h => h.Key == key).Presenter;
            if (existing == null)
            {
                existing = new HorsePresenter { Name = "Horse_" + i };
                AddChild(existing);
                if (!existing.Present(descriptor!, "idle").Supported) { existing.QueueFree(); continue; }
                _horses.Add((key, existing));
            }
            existing.Position = Slots[i].Position;
            existing.RotationDegrees = new Vector3(0, Slots[i].Yaw, 0);
        }
    }

    /// <summary>Horse key under a screen point, or null. Uses each horse's projected body centre.</summary>
    public string? PickHorse(Vector2 screenPoint)
    {
        string? best = null; float bestDistance = float.MaxValue;
        foreach (var (key, presenter) in _horses)
        {
            float s = presenter.Descriptor?.IsFoal == true ? 0.66f : 1f;
            var centre = presenter.GlobalTransform * new Vector3(0.2f * s, 1.0f * s, 0);
            if (_camera.IsPositionBehind(centre)) continue;
            var projected = _camera.UnprojectPosition(centre);
            var edge = _camera.UnprojectPosition(centre + _camera.GlobalBasis.X * 1.3f * s);
            float radius = projected.DistanceTo(edge);
            float distance = projected.DistanceTo(screenPoint);
            if (distance < radius && distance < bestDistance) { best = key; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>Glide the camera toward a horse, or back to the overview when key is null.</summary>
    public void Focus(string? key)
    {
        var presenter = _horses.FirstOrDefault(h => h.Key == key).Presenter;
        if (presenter == null) { _cameraGoal = _cameraHome; _targetGoal = HomeTarget; return; }
        var p = presenter.GlobalPosition;
        _targetGoal = p + new Vector3(0, 0.95f, 0);
        _cameraGoal = p + new Vector3(0.4f, 2.6f, 6.0f);
    }

    public int HorseCount => _horses.Count;

    // ---------------------------------------------------------------------------------
    private void BuildGround()
    {
        var grass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass_ground.gdshader") };
        AddChild(Sets.Mesh(new PlaneMesh { Size = new Vector2(160, 160), SubdivideWidth = 2, SubdivideDepth = 2 }, grass, Vector3.Zero));
        var sand = Sets.Mat(Color.FromHtml("#e6cfa6"), 0.95f);
        var paddock = Sets.Mesh(new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.02f, RadialSegments = 64 }, sand, new Vector3(0, 0.006f, -0.5f));
        paddock.Scale = new Vector3(6.4f, 1, 4.6f);
        AddChild(paddock);
        var path = Sets.Mesh(new BoxMesh { Size = new Vector3(2.4f, 0.02f, 9f) }, sand, new Vector3(-5.2f, 0.007f, -8.5f), new Vector3(0, 28, 0));
        AddChild(path);

        var rng = new RandomNumberGenerator { Seed = 11 };
        var hill = Sets.Mat(Color.FromHtml("#b3d39c"), 1f);
        for (var i = 0; i < 9; i++)
        {
            float a = Mathf.DegToRad(200 + i * 18), r = 48 + rng.RandfRange(-5, 8);
            var h = Sets.Mesh(new SphereMesh { Radius = 1, Height = 2 }, hill, new Vector3(Mathf.Cos(a) * r, -7, Mathf.Sin(a) * r));
            h.Scale = new Vector3(rng.RandfRange(16, 26), rng.RandfRange(9, 13), rng.RandfRange(10, 16));
            AddChild(h);
        }
    }

    private void BuildScenery()
    {
        var rng = new RandomNumberGenerator { Seed = 23 };
        Place("barn", 7.5f, new Vector3(-3.5f, 0, -17f), 10f, () => FallbackBarn());

        // Paddock fence around the sand ellipse, open toward the camera.
        const int segments = 13; const float rx = 7.4f, rz = 5.4f;
        for (var i = 0; i < segments; i++)
        {
            float a0 = Mathf.DegToRad(160 + i * (220f / segments)), a1 = Mathf.DegToRad(160 + (i + 1) * (220f / segments));
            var p0 = new Vector3(Mathf.Cos(a0) * rx, 0, Mathf.Sin(a0) * rz - 0.5f);
            var p1 = new Vector3(Mathf.Cos(a1) * rx, 0, Mathf.Sin(a1) * rz - 0.5f);
            var mid = (p0 + p1) / 2; float yaw = Mathf.RadToDeg(Mathf.Atan2(-(p1.Z - p0.Z), p1.X - p0.X));
            PlaceLength("fence", p0.DistanceTo(p1) + 0.15f, mid, yaw, () => FallbackFence(p0.DistanceTo(p1)));
        }

        foreach (var (pos, size) in new[] { (new Vector3(-13, 0, -9), 7.5f), (new Vector3(-10f, 0, -17), 8.5f), (new Vector3(9.5f, 0, -13), 8f),
                     (new Vector3(14, 0, -5), 7f), (new Vector3(4, 0, -20), 9f), (new Vector3(-17, 0, -2), 6.5f) })
            Place("oak_tree", size * rng.RandfRange(0.85f, 1.1f), pos, rng.RandfRange(0, 360), () => FallbackTree(rng));
        Place("hay_props", 1.6f, new Vector3(2.4f, 0, -12.6f), -25f, () => FallbackHay());
        Place("water_trough", 0.85f, new Vector3(4.4f, 0, -3.8f), 75f, () => FallbackTrough());
    }

    /// <summary>Load a Meshy asset scaled to a target height, or build its stand-in.</summary>
    private void Place(string asset, float height, Vector3 position, float yaw, Func<Node3D> fallback)
    {
        var node = LoadAsset(asset, out var bounds);
        if (node != null && bounds.Size.Y > 0.0001f)
        {
            float scale = height / bounds.Size.Y;
            node.Scale = Vector3.One * scale;
            node.Position = -new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z) * scale;
        }
        else node = fallback();
        var holder = new Node3D { Name = asset, Position = position, RotationDegrees = new Vector3(0, yaw, 0) };
        holder.AddChild(node);
        AddChild(holder);
    }

    /// <summary>Like Place, but scales a fence section to span a given length along X.</summary>
    private void PlaceLength(string asset, float length, Vector3 position, float yaw, Func<Node3D> fallback)
    {
        var node = LoadAsset(asset, out var bounds);
        if (node != null && bounds.Size.X > 0.0001f)
        {
            bool alongZ = bounds.Size.Z > bounds.Size.X;
            float span = alongZ ? bounds.Size.Z : bounds.Size.X;
            float scale = length / span;
            node.Scale = Vector3.One * scale;
            if (alongZ) node.RotationDegrees = new Vector3(0, 90, 0);
            var offset = new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z) * scale;
            node.Position = alongZ ? -new Vector3(offset.Z, offset.Y, -offset.X) : -offset; // +90° yaw maps (x, z) to (z, -x)
        }
        else node = fallback();
        var holder = new Node3D { Name = asset, Position = position, RotationDegrees = new Vector3(0, yaw, 0) };
        holder.AddChild(node);
        AddChild(holder);
    }

    private static readonly Dictionary<string, PackedScene?> AssetCache = new();

    private static Node3D? LoadAsset(string asset, out Aabb bounds)
    {
        bounds = default;
        var path = $"{SceneryRoot}{asset}/{asset}.glb";
        if (!AssetCache.TryGetValue(path, out var packed))
            AssetCache[path] = packed = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
        if (packed == null) return null;
        var node = packed.Instantiate<Node3D>();
        bool first = true;
        foreach (var mesh in All<MeshInstance3D>(node))
        {
            var box = LocalTransform(node, mesh) * mesh.GetAabb();
            bounds = first ? box : bounds.Merge(box);
            first = false;
        }
        return node;
    }

    private static Transform3D LocalTransform(Node3D root, Node3D node)
    {
        var t = Transform3D.Identity;
        for (Node? n = node; n != null && n != root; n = n.GetParent()) if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }

    private static IEnumerable<T> All<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in All<T>(child)) yield return nested;
        }
    }

    // Procedural stand-ins -------------------------------------------------------------
    private static Node3D FallbackBarn()
    {
        var root = new Node3D();
        root.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(8, 4, 6) }, Sets.Mat(Sets.Cream, 0.8f), new Vector3(0, 2, 0)));
        root.AddChild(Sets.Mesh(new PrismMesh { Size = new Vector3(8.6f, 2.5f, 6.4f) }, Sets.Mat(Color.FromHtml("#d98a96"), 0.7f), new Vector3(0, 5.25f, 0)));
        root.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(2.6f, 3f, 0.1f) }, Sets.Mat(Sets.Sage, 0.7f), new Vector3(0, 1.5f, 3.02f)));
        return root;
    }

    private static Node3D FallbackFence(float length)
    {
        var root = new Node3D(); var white = Sets.Mat(Sets.Cream, 0.6f);
        foreach (var x in new[] { -length / 2, length / 2 })
            root.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(0.14f, 1.3f, 0.14f) }, white, new Vector3(x, 0.65f, 0)));
        foreach (var y in new[] { 0.45f, 0.8f, 1.15f })
            root.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(length, 0.1f, 0.05f) }, white, new Vector3(0, y, 0)));
        return root;
    }

    private static Node3D FallbackTree(RandomNumberGenerator rng)
    {
        var t = new Node3D();
        t.AddChild(Sets.Mesh(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.4f, Height = 3f }, Sets.Mat(Color.FromHtml("#a07a5c"), 0.9f), new Vector3(0, 1.5f, 0)));
        var leaf = Sets.Mat(Color.FromHtml("#9cc98a"), 0.9f);
        for (var i = 0; i < 4; i++)
            t.AddChild(Sets.Mesh(new SphereMesh { Radius = 1.6f, Height = 3f }, leaf, new Vector3(rng.RandfRange(-0.8f, 0.8f), 4f + rng.RandfRange(-0.3f, 0.6f), rng.RandfRange(-0.8f, 0.8f))));
        return t;
    }

    private static Node3D FallbackHay()
    {
        var root = new Node3D(); var hay = Sets.Mat(Color.FromHtml("#efd68a"), 1f);
        root.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(1.1f, 0.5f, 0.6f) }, hay, new Vector3(0, 0.25f, 0)));
        root.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(1.1f, 0.5f, 0.6f) }, hay, new Vector3(0.1f, 0.75f, 0), new Vector3(0, 12, 0)));
        return root;
    }

    private static Node3D FallbackTrough()
    {
        var root = new Node3D();
        root.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(2f, 0.6f, 0.7f) }, Sets.Mat(Sets.Wood, 0.8f), new Vector3(0, 0.45f, 0)));
        root.AddChild(Sets.Mesh(new BoxMesh { Size = new Vector3(1.85f, 0.02f, 0.55f) }, Sets.Mat(Sets.Sky, 0.1f), new Vector3(0, 0.72f, 0)));
        return root;
    }
}
