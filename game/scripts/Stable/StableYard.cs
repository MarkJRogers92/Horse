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
    private readonly Dictionary<HorsePresenter, Wanderer> _wander = new();
    private readonly RandomNumberGenerator _rng = new() { Seed = 5 };
    private string? _focusedKey;

    /// <summary>Where wandering horses may go: centre and half-extents of an ellipse on the ground.</summary>
    public Vector3 WanderCentre { get; set; } = new(0.4f, 0, -0.6f);
    public Vector2 WanderRadius { get; set; } = new(2.6f, 2.3f);

    /// <summary>When false (automated captures) horses stay on their slots for repeatable screenshots.</summary>
    public bool Wandering { get; set; } = true;

    // Per-horse life in the paddock: rest, then walk (or turn in place when Walk is
    // not allowed for this body shape) toward a new spot.
    private sealed class Wanderer
    {
        public double RestLeft;
        public Vector3? Target;
        public float TurnGoal;
        public bool Walking;
        public bool? CanWalk; // learned once from the descriptor's shape; not every body shape has a checked Walk
        public double Pace = MinWalkPace; // fraction of the clip's ground speed; eased so horses accelerate and brake
    }

    // The Walk clip's feet only stay planted when the clip plays at the same fraction of its
    // checked speed as the horse travels, so ground speed and clip pace always move together.
    private const float MinWalkPace = 0.4f;      // slowest pace while arcing round or arriving
    private const float TurnFullPaceRadians = 1.0f; // facing error at which the pace has fallen to its minimum
    private const float ArrivalSlowMeters = 1.0f; // start braking this far from the target
    private const float ArrivalStopMeters = 0.2f;
    private const double PaceChangePerSecond = 0.9;
    private Camera3D _camera = null!;
    private static readonly Vector3 HomeTarget = new(0.45f, 0.75f, -0.4f);
    private Vector3 _homeTarget = HomeTarget;
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
        AddChild(Petals());
        _camera = new Camera3D { Fov = 40, Current = true, Position = _cameraHome };
        AddChild(_camera);
        _cameraGoal = _cameraHome; _targetGoal = _homeTarget; _lookTarget = _homeTarget;
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
        if (Wandering) foreach (var (key, presenter) in _horses) Wander(key, presenter, delta);
    }

    private void Wander(string key, HorsePresenter presenter, double delta)
    {
        if (!_wander.TryGetValue(presenter, out var w)) _wander[presenter] = w = new Wanderer { RestLeft = _rng.RandfRange(3, 9), TurnGoal = presenter.RotationDegrees.Y };
        // A bridge between Idle and Walk (also used when a horse stops) holds the horse still.
        if (presenter.Descriptor == null || presenter.IsTransitioning) return;
        if (key == _focusedKey)
        {
            if (w.Walking) StopWalking(presenter, w);
            return;
        }

        if (w.Target is { } target)
        {
            var to = target - presenter.Position; to.Y = 0;
            float yawGoal = Mathf.RadToDeg(Mathf.Atan2(-to.Z, to.X));
            float yaw = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(presenter.RotationDegrees.Y), Mathf.DegToRad(yawGoal), 1f - Mathf.Exp(-1.8f * (float)delta)));
            presenter.RotationDegrees = new Vector3(0, yaw, 0);
            float facing = Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(yaw), Mathf.DegToRad(yawGoal)));
            if (w.Walking)
            {
                // Walk forward while the yaw settles, slowing for sharp turns and for the
                // last metre; the clip is slowed by exactly the same factor so feet never skate.
                float distance = to.Length();
                double wanted = Math.Min(
                    Mathf.Clamp(1f - facing / TurnFullPaceRadians, MinWalkPace, 1f),
                    Mathf.Clamp(distance / ArrivalSlowMeters, MinWalkPace, 1f));
                w.Pace = Mathf.MoveToward(w.Pace, wanted, PaceChangePerSecond * delta);
                presenter.SetPace(w.Pace);
                presenter.Position += presenter.Basis.X.Normalized() * (float)(presenter.EffectiveSpeedMetersPerSecond * delta);
                if (distance < ArrivalStopMeters) { StopWalking(presenter, w); w.Target = null; w.RestLeft = _rng.RandfRange(5, 12); }
            }
            else if (facing < 0.05f)
            {
                w.Target = null; w.RestLeft = _rng.RandfRange(6, 14);
            }
            return;
        }

        w.RestLeft -= delta;
        if (w.RestLeft > 0) return;
        var next = PickSpot(presenter);
        w.Target = next;
        w.Walking = false;
        // Decide from the descriptor so a body shape without a checked Walk never loads (and
        // clears) a model just to be refused. The bridge hides the Idle-to-Walk model swap.
        w.CanWalk ??= presenter.CanWalk(presenter.Descriptor);
        if (w.CanWalk == true)
        {
            var support = presenter.TransitionTo(presenter.Descriptor, "walk");
            w.Walking = support.Supported;
            w.Pace = MinWalkPace;
            presenter.SetPace(w.Pace); // during the bridge this is the pace the Walk clip starts at
            if (!w.Walking) { w.CanWalk = false; presenter.Present(presenter.Descriptor, "idle"); }
        }
        if (!w.Walking)
        {
            // Walk is only checked for the neutral body shape, so other horses shift
            // their weight with a small turn in place instead of travelling.
            // Keep faces toward the camera side: yaw in (0°, 180°) would show the horse's back.
            float goal = Mathf.Wrap(presenter.RotationDegrees.Y + _rng.RandfRange(25, 55) * (_rng.Randf() < 0.5f ? -1 : 1), -200f, 160f);
            goal = Mathf.Clamp(goal, -195f, 15f);
            float turn = Mathf.DegToRad(goal);
            w.Target = presenter.Position + new Vector3(Mathf.Cos(turn), 0, -Mathf.Sin(turn)) * 3f;
            w.Walking = false;
        }
    }

    private void StopWalking(HorsePresenter presenter, Wanderer w)
    {
        w.Walking = false;
        if (presenter.Descriptor != null) presenter.TransitionTo(presenter.Descriptor, "idle");
    }

    /// <summary>A spot inside the paddock, clear of the other horses.</summary>
    private Vector3 PickSpot(HorsePresenter self)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            float a = _rng.RandfRange(0, Mathf.Tau), r = Mathf.Sqrt(_rng.Randf());
            // The open screen area between the stable's side panels (see D-012 layout).
            var p = new Vector3(Mathf.Cos(a) * WanderRadius.X * r + WanderCentre.X, 0, Mathf.Sin(a) * WanderRadius.Y * r + WanderCentre.Z);
            if (_horses.All(h => h.Presenter == self || h.Presenter.Position.DistanceTo(p) > 2.6f) &&
                _wander.Where(kv => kv.Key != self && kv.Value.Target.HasValue).All(kv => kv.Value.Target!.Value.DistanceTo(p) > 2.6f))
                return p;
        }
        return self.Position;
    }

    /// <summary>Show exactly these horses (key = horse id), reusing presenters that did not change.</summary>
    public void SetHorses(IReadOnlyList<(string Key, VisualDescriptor? Descriptor)> horses, IReadOnlyDictionary<string, string>? names = null)
    {
        var wanted = horses.Where(h => h.Descriptor != null).Take(Slots.Length).ToList();
        foreach (var stale in _horses.Where(h => wanted.All(w => w.Key != h.Key || w.Descriptor!.CacheKey != h.Presenter.Descriptor?.CacheKey)).ToList())
        {
            stale.Presenter.QueueFree();
            _wander.Remove(stale.Presenter);
            _tags.Remove(stale.Presenter);
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
                // Horses that all breathe in unison look like clones; start each at its own point in the loop.
                if (Wandering && existing.Animator is { } idle && !string.IsNullOrEmpty(existing.CurrentAnimation))
                    idle.Seek(_rng.RandfRange(0f, (float)idle.GetAnimation(existing.CurrentAnimation).Length), true);
                _horses.Add((key, existing));
                existing.Position = Slots[i].Position;
                existing.RotationDegrees = new Vector3(0, Slots[i].Yaw, 0);
            }
            else if (!Wandering)
            {
                existing.Position = Slots[i].Position;
                existing.RotationDegrees = new Vector3(0, Slots[i].Yaw, 0);
            }
            if (names != null && names.TryGetValue(key, out var name)) NameTag(existing, name);
        }
    }

    private readonly Dictionary<HorsePresenter, Label3D> _tags = new();
    private string? _hoverKey;

    /// <summary>A floating name plate that always faces the camera.</summary>
    private void NameTag(HorsePresenter presenter, string name)
    {
        if (!_tags.TryGetValue(presenter, out var tag))
        {
            tag = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.0042f, OutlineSize = 18,
                Modulate = Color.FromHtml("#5b3a5e"), OutlineModulate = new Color(1, 1, 1, 0.92f), Font = UI.CozyTheme.BoldFont,
                NoDepthTest = true, RenderPriority = 10, FixedSize = false,
            };
            presenter.AddChild(tag);
            _tags[presenter] = tag;
        }
        tag.Text = name;
        float s = presenter.Descriptor?.IsFoal == true ? 0.66f : 1f;
        tag.Position = new Vector3(0.3f * s, 1.95f * s + 0.1f, 0);
    }

    /// <summary>Highlight the horse under the pointer; returns its key.</summary>
    public string? Hover(Vector2 screenPoint)
    {
        var key = PickHorse(screenPoint);
        if (key == _hoverKey) return key;
        _hoverKey = key;
        foreach (var (k, presenter) in _horses)
            if (_tags.TryGetValue(presenter, out var tag))
            {
                tag.Modulate = k == key ? Color.FromHtml("#e48aa9") : Color.FromHtml("#5b3a5e");
                tag.Scale = Vector3.One * (k == key ? 1.25f : 1f);
            }
        Input.SetDefaultCursorShape(key == null ? Input.CursorShape.Arrow : Input.CursorShape.PointingHand);
        return key;
    }

    /// <summary>A burst of hearts and sparkles over a horse (comfort care switched on).</summary>
    public void Cheer(string key)
    {
        var presenter = _horses.FirstOrDefault(h => h.Key == key).Presenter;
        if (presenter == null) return;
        Audio.GameAudio.Play("care_cheer", -10);
        float s = presenter.Descriptor?.IsFoal == true ? 0.66f : 1f;
        var burst = new CpuParticles3D
        {
            Emitting = true, OneShot = true, Amount = 30, Lifetime = 1.8, Explosiveness = 0.7f,
            Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 8, Rings = 4 },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.5f,
            Direction = Vector3.Up, Spread = 40, Gravity = new Vector3(0, 0.6f, 0), InitialVelocityMin = 0.4f, InitialVelocityMax = 1.1f,
            ScaleAmountMin = 0.6f, ScaleAmountMax = 1.4f, Position = presenter.Position + new Vector3(0.3f * s, 1.7f * s, 0),
        };
        var ramp = new Gradient(); ramp.SetColor(0, Color.FromHtml("#ff4f8b")); ramp.SetColor(1, Color.FromHtml("#ffb81c"));
        burst.ColorInitialRamp = ramp;
        var glow = new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        burst.MaterialOverride = glow;
        AddChild(burst);
        GetTree().CreateTimer(2.2).Timeout += burst.QueueFree;
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

    /// <summary>Change the overview framing (the title screen frames the yard to the right of its menu).</summary>
    public void SetCameraHome(Vector3 position, Vector3 target)
    {
        _cameraHome = position; _homeTarget = target;
        if (_camera != null) { _camera.Position = position; _lookTarget = target; _camera.LookAt(target, Vector3.Up); }
        _cameraGoal = position; _targetGoal = target;
    }

    /// <summary>Glide the camera toward a horse, or back to the overview when key is null.</summary>
    public void Focus(string? key)
    {
        _focusedKey = key;
        var presenter = _horses.FirstOrDefault(h => h.Key == key).Presenter;
        if (presenter == null) { _cameraGoal = _cameraHome; _targetGoal = _homeTarget; return; }
        var p = presenter.GlobalPosition;
        _targetGoal = p + new Vector3(0, 0.95f, 0);
        _cameraGoal = p + new Vector3(0.4f, 2.6f, 6.0f);
    }

    public int HorseCount => _horses.Count;

    /// <summary>Dim the sun toward dusk and back over the given seconds.</summary>
    public void PlayNightfall(double seconds)
    {
        foreach (var sun in All<DirectionalLight3D>(this))
        {
            var energy = sun.LightEnergy; var colour = sun.LightColor;
            var tween = CreateTween();
            tween.TweenProperty(sun, "light_color", new Color(1f, 0.6f, 0.4f), seconds * 0.3);
            tween.Parallel().TweenProperty(sun, "light_energy", energy * 0.25f, seconds * 0.45);
            tween.TweenInterval(seconds * 0.2);
            tween.TweenProperty(sun, "light_color", colour, seconds * 0.35);
            tween.Parallel().TweenProperty(sun, "light_energy", energy, seconds * 0.35);
        }
    }
    public double ElapsedSeconds => _time;

    // ---------------------------------------------------------------------------------
    private void BuildGround()
    {
        var grass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass_ground.gdshader") };
        AddChild(Sets.Mesh(new PlaneMesh { Size = new Vector2(160, 160), SubdivideWidth = 2, SubdivideDepth = 2 }, grass, Vector3.Zero));
        var sand = Sets.Mat(Color.FromHtml("#e6cfa6"), 0.95f);
        var raked = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/paddock_sand.gdshader") };
        var paddock = Sets.Mesh(new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.02f, RadialSegments = 64 }, raked, new Vector3(0, 0.006f, -0.5f));
        paddock.Scale = new Vector3(7.0f, 1, 5.0f);
        AddChild(paddock);
        var path = Sets.Mesh(new BoxMesh { Size = new Vector3(2.4f, 0.02f, 9f) }, sand, new Vector3(-5.2f, 0.007f, -8.5f), new Vector3(0, 28, 0));
        AddChild(path);

        var rng = new RandomNumberGenerator { Seed = 11 };
        AddChild(GrassAndFlowers(rng));
        var hill = Sets.Mat(Color.FromHtml("#b3d39c"), 1f);
        for (var i = 0; i < 9; i++)
        {
            float a = Mathf.DegToRad(200 + i * 18), r = 48 + rng.RandfRange(-5, 8);
            var h = Sets.Mesh(new SphereMesh { Radius = 1, Height = 2 }, hill, new Vector3(Mathf.Cos(a) * r, -7, Mathf.Sin(a) * r));
            h.Scale = new Vector3(rng.RandfRange(16, 26), rng.RandfRange(9, 13), rng.RandfRange(10, 16));
            AddChild(h);
        }
    }

    /// <summary>Blossom petals drifting slowly across the yard.</summary>
    private static Node3D Petals()
    {
        var petals = new CpuParticles3D
        {
            Name = "Petals", Amount = 60, Lifetime = 14, Preprocess = 14,
            Mesh = new QuadMesh { Size = new Vector2(0.07f, 0.05f) },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(14, 1.5f, 9),
            Position = new Vector3(0, 6, -6), Direction = new Vector3(1, -0.4f, 0.3f), Spread = 30,
            Gravity = new Vector3(0.15f, -0.25f, 0.05f), InitialVelocityMin = 0.2f, InitialVelocityMax = 0.6f,
            AngularVelocityMin = -120, AngularVelocityMax = 120, ParticleFlagAlignY = false,
        };
        var ramp = new Gradient(); ramp.SetColor(0, Color.FromHtml("#fbd0dd")); ramp.SetColor(1, Colors.White);
        petals.ColorInitialRamp = ramp;
        petals.MaterialOverride = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        return petals;
    }

    /// <summary>Grass tufts and wildflowers outside the paddock (instanced, cheap).</summary>
    private static Node3D GrassAndFlowers(RandomNumberGenerator rng)
    {
        var root = new Node3D { Name = "GrassAndFlowers" };
        bool InPaddock(Vector3 p) => Mathf.Pow(p.X / 7.4f, 2) + Mathf.Pow((p.Z + 0.5f) / 5.4f, 2) < 1f;
        bool OnPath(Vector3 p) => p.Z < -11f && p.Z > -14.5f && p.X > -7f && p.X < 1.5f;
        var blades = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new PrismMesh { Size = new Vector3(0.05f, 0.2f, 0.01f) } };
        const int tufts = 14000; blades.InstanceCount = tufts;
        for (var i = 0; i < tufts; i++)
        {
            Vector3 p;
            do p = new Vector3(rng.RandfRange(-22, 22), 0, rng.RandfRange(-24, 3.5f)); while (InPaddock(p) || OnPath(p));
            var basis = Basis.Identity.Rotated(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Rotated(Vector3.Right, rng.RandfRange(-0.3f, 0.3f)).Scaled(Vector3.One * rng.RandfRange(0.6f, 1.5f));
            blades.SetInstanceTransform(i, new Transform3D(basis, p + new Vector3(0, 0.11f, 0)));
            blades.SetInstanceColor(i, Color.FromHtml("#5f9a4d").Lerp(Color.FromHtml("#94c070"), rng.Randf()));
        }
        var bladeMat = Sets.Mat(Colors.White, 1f); bladeMat.VertexColorUseAsAlbedo = true; bladeMat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled; bladeMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        root.AddChild(new MultiMeshInstance3D { Multimesh = blades, MaterialOverride = bladeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        var petals = new[] { "#f7a1bd", "#fbd36b", "#c9a7ef", "#ffffff", "#ff9fa0", "#9fd3f5" };
        var heads = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new SphereMesh { Radius = 0.045f, Height = 0.06f, RadialSegments = 8, Rings = 4 } };
        const int flowers = 1600; heads.InstanceCount = flowers;
        for (var i = 0; i < flowers; i++)
        {
            // Clustered drifts: pick a cluster centre, then scatter around it.
            var centre = new Vector3(rng.RandfRange(-16, 16), 0, rng.RandfRange(-20, 2.5f));
            Vector3 p;
            int guard = 0;
            do p = centre + new Vector3(rng.RandfRange(-1.4f, 1.4f), 0, rng.RandfRange(-1.4f, 1.4f)); while ((InPaddock(p) || OnPath(p)) && ++guard < 20);
            if (InPaddock(p)) p = new Vector3(p.X > 0 ? 9 : -9, 0, p.Z);
            heads.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * rng.RandfRange(0.7f, 1.3f)), p + new Vector3(0, rng.RandfRange(0.1f, 0.22f), 0)));
            heads.SetInstanceColor(i, Color.FromHtml(petals[(i / 40) % petals.Length]));
        }
        var petalMat = Sets.Mat(Colors.White, 0.6f); petalMat.VertexColorUseAsAlbedo = true;
        root.AddChild(new MultiMeshInstance3D { Multimesh = heads, MaterialOverride = petalMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
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
        Place("planter_box", 0.75f, new Vector3(-8.2f, 0, -13.0f), 10f, () => new Node3D());
        Place("planter_box", 0.75f, new Vector3(1.3f, 0, -13.2f), 10f, () => new Node3D());
        Place("bench", 1.0f, new Vector3(-8.6f, 0, -6.4f), 50f, () => new Node3D());
        Place("saddle_rack", 1.25f, new Vector3(-0.2f, 0, -12.2f), 15f, () => new Node3D());
        Place("wheelbarrow", 0.85f, new Vector3(4.6f, 0, -11.6f), -60f, () => new Node3D());
        Place("show_jump", 1.3f, new Vector3(7.2f, 0, -7.5f), -25f, () => new Node3D());
        var sign = Place("signpost", 2.5f, new Vector3(-5.6f, 0, -4.2f), 18f, () => new Node3D());
        // The generated board is blank on purpose; the stable's name is real text so it stays crisp.
        sign.AddChild(new Label3D
        {
            Text = "Cloverbrook\nStables", FontSize = 96, PixelSize = 0.0022f, OutlineSize = 0, Modulate = Color.FromHtml("#5b3a5e"),
            Font = UI.CozyTheme.BoldFont, Position = new Vector3(0.0f, 1.62f, 0.2f), NoDepthTest = false,
        });
    }

    /// <summary>Load a Meshy asset scaled to a target height, or build its stand-in.</summary>
    private Node3D Place(string asset, float height, Vector3 position, float yaw, Func<Node3D> fallback)
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
        return holder;
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
