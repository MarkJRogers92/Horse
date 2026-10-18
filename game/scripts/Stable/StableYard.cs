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
public enum YardWeather { Sunny, Cloudy, Rain }

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
    private readonly Dictionary<string, string> _mothers = new(); // foal key -> mare key, for horses both in the yard (D-030)
    private const float FoalLeashMeters = 2.5f; // a foal's wander targets stay this close to its mare
    private const float FoalFollowTriggerMeters = 2.6f; // a resting foal sets off after its mare once she is this far away
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
        public int SeenGrazes;   // D-036: grazes already counted, so the chew sound plays once per graze
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
        _petals = Petals();
        AddChild(_petals);
        _lights = GetChildren().OfType<Node3D>().First(n => n.Name.ToString().StartsWith("Lighting", StringComparison.Ordinal));
        foreach (var sun in All<DirectionalLight3D>(_lights)) _sunDefaults[sun] = (sun.LightColor, sun.LightEnergy);
        // Godot renames duplicate sibling names, so find the trees by group, not by name.
        foreach (var holder in GetChildren().OfType<Node3D>().Where(n => n.IsInGroup("scenery_oak_tree")))
            _trees.AddRange(All<MeshInstance3D>(holder));
        _camera = new Camera3D { Fov = 40, Current = true, Position = _cameraHome };
        AddChild(_camera);
        _cameraGoal = _cameraHome; _targetGoal = _homeTarget; _lookTarget = _homeTarget;
        _camera.LookAt(_lookTarget, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        UpdateAtmosphere(delta);
        // Slow cinematic drift plus easing toward a selected horse.
        var drift = new Vector3(Mathf.Sin((float)_time * 0.08f) * 0.6f, Mathf.Sin((float)_time * 0.11f) * 0.12f, 0);
        float k = 1f - Mathf.Exp(-2.5f * (float)delta);
        if (PhotoMode)
        {
            // D-023 photo mode: an orbit around the chosen horse (or the paddock), no drift.
            var target = _photoKey != null && _horses.FirstOrDefault(h => h.Key == _photoKey).Presenter is { } subject
                ? subject.GlobalPosition + Vector3.Up * (0.95f * (subject.Model?.Scale.Y ?? 1f) * (subject.Descriptor?.IsFoal == true ? 0.66f : 1f)) : _homeTarget;
            _targetGoal = target;
            var pitch = Mathf.DegToRad(_photoPitch); var yaw = Mathf.DegToRad(_photoYaw);
            _cameraGoal = target + new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * _photoDistance;
            drift = Vector3.Zero;
        }
        _lookTarget = _lookTarget.Lerp(_targetGoal, k);
        _camera.Position = _camera.Position.Lerp(_cameraGoal + drift, k);
        _camera.LookAt(_lookTarget, Vector3.Up);
        if (Wandering)
        {
            UpdateSocial(delta);
            foreach (var (key, presenter) in _horses) Wander(key, presenter, delta);
        }
        if (Wandering && _emotes.Count > 0) UpdateEmotes();
        if (Wandering && !PhotoMode) AmbientHorseVoices(delta);
    }

    // D-036: gentle horse sounds from the yard. Every 20 to 60 s one resting horse nickers or stamps, a graze sometimes comes with a
    // soft chew, and a click on a resting horse is sometimes answered with a nicker. All go through GameAudio, so the SFX bus
    // volume and the mute toggle apply, and they are kept well below the care sounds.
    private double _voiceClock = 30;

    private bool IsResting(HorsePresenter presenter) =>
        _wander.TryGetValue(presenter, out var w) && !w.Walking && w.Target == null && !presenter.IsTransitioning;

    private void AmbientHorseVoices(double delta)
    {
        foreach (var (_, presenter) in _horses)
            if (_wander.TryGetValue(presenter, out var w) && presenter.IdleLife is { } life && life.GrazeCount != w.SeenGrazes)
            {
                w.SeenGrazes = life.GrazeCount;
                if (_rng.Randf() < 0.4f) GetTree().CreateTimer(1.3).Timeout += () => Audio.GameAudio.Play("horse_chew", -24);
            }
        _voiceClock -= delta;
        if (_voiceClock > 0) return;
        _voiceClock = _rng.RandfRange(20f, 60f);
        var resting = _horses.Where(h => h.Key != _focusedKey && IsResting(h.Presenter)).ToList();
        if (resting.Count == 0) return;
        var pick = resting[(int)(_rng.Randi() % (uint)resting.Count)];
        bool foal = pick.Presenter.Descriptor?.IsFoal == true;
        if (_rng.Randf() < 0.65f) Audio.GameAudio.Play("horse_nicker", foal ? -17 : -20);
        else Audio.GameAudio.Play("horse_stamp", -21);
    }


    private void Wander(string key, HorsePresenter presenter, double delta)
    {
        if (!_wander.TryGetValue(presenter, out var w)) _wander[presenter] = w = new Wanderer { RestLeft = _rng.RandfRange(3, 9), TurnGoal = presenter.RotationDegrees.Y };
        // A bridge between Idle and Walk (also used when a horse stops) holds the horse still.
        if (presenter.Descriptor == null || presenter.IsTransitioning) return;
        // D-030 idle life (grazing, tail swish) runs only while the horse stands fully at rest.
        if (presenter.IdleLife is { } life) life.Calm = key != _focusedKey && !w.Walking && w.Target == null && (w.RestLeft > 0 || life.Busy);
        if (key == _focusedKey)
        {
            if (w.Walking) StopWalking(presenter, w);
            w.Target = null;
            return;
        }

        if (w.Target is { } target)
        {
            var to = target - presenter.Position; to.Y = 0;
            var direction = SteerAroundHorses(presenter, to.Normalized());
            float yawGoal = Mathf.RadToDeg(Mathf.Atan2(-direction.Z, direction.X));
            float yaw = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(presenter.RotationDegrees.Y), Mathf.DegToRad(yawGoal), 1f - Mathf.Exp(-1.8f * (float)delta)));
            float oldYaw = presenter.Rotation.Y;
            float nextYaw = Mathf.DegToRad(yaw);
            if (!ClearStep(presenter, presenter.Position, nextYaw))
            {
                Yield(presenter, w, key);
                return;
            }
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
                var nextPosition = presenter.Position + presenter.Basis.X.Normalized() * (float)(presenter.EffectiveSpeedMetersPerSecond * delta);
                if (!ClearStep(presenter, nextPosition, nextYaw))
                {
                    presenter.Rotation = new Vector3(0, oldYaw, 0);
                    Yield(presenter, w, key);
                    return;
                }
                presenter.Position = nextPosition;
                if (distance < ArrivalStopMeters) { StopWalking(presenter, w); w.Target = null; w.RestLeft = RestDuration(key, 5, 12); OnSocialArrival(key, presenter); }
            }
            else if (facing < 0.05f)
            {
                w.Target = null; w.RestLeft = _rng.RandfRange(6, 14);
            }
            return;
        }

        // A foal sets off after its mare rather than being left behind.
        if (_mothers.TryGetValue(key, out var mareKey) && MareOf(mareKey) is { } mare && w.RestLeft > 0.6 &&
            presenter.Position.DistanceTo(mare.Position) > FoalFollowTriggerMeters &&
            _wander.TryGetValue(mare, out var mareState) && (mareState.Walking || mareState.Target != null))
            w.RestLeft = _rng.RandfRange(0.3f, 0.6f);
        w.RestLeft -= delta;
        if (w.RestLeft > 0 || presenter.IdleLife is { Busy: true }) return; // a graze in progress finishes first
        // Let the head come up (and the tail settle) before any walk or turn starts.
        if (presenter.IdleLife is { } resting && resting.Offset > 0.02f) { resting.Calm = false; return; }
        var next = PickSpot(presenter, key);
        if (next.DistanceTo(presenter.Position) < ArrivalStopMeters) { w.RestLeft = RestDuration(key, 3, 6); return; }
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
    private Vector3 PickSpot(HorsePresenter self, string key)
    {
        // D-030: a foal stays within a short leash of its mare (and goes where she is going).
        if (_mothers.TryGetValue(key, out var mareKey) && MareOf(mareKey) is { } mare)
        {
            var anchor = _wander.TryGetValue(mare, out var ms) && ms.Target is { } mareTarget ? mareTarget : mare.Position;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                float a = _rng.RandfRange(0, Mathf.Tau), r = _rng.RandfRange(0.9f, FoalLeashMeters - 0.3f);
                var p = ClampToPaddock(anchor + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                if (p.DistanceTo(anchor) <= FoalLeashMeters &&
                    _horses.All(h => h.Presenter == self || h.Presenter == mare || h.Presenter.Position.DistanceTo(p) > 1.6f))
                    return p;
            }
            return ClampToPaddock(anchor + new Vector3(1.2f, 0, 0.6f));
        }

        if (_weather != YardWeather.Rain && TryCompanionSpot(self, key) is { } company) return company;

        // D-030: on rain (or snow) days horses drift toward the barn side of the paddock, the back edge under the stable block.
        bool shelter = _weather == YardWeather.Rain;
        float clearance = shelter ? 2.0f : 2.6f;
        Vector3? best = null;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            float a = _rng.RandfRange(0, Mathf.Tau), r = Mathf.Sqrt(_rng.Randf());
            // The open screen area between the stable's side panels (see D-012 layout).
            var p = new Vector3(Mathf.Cos(a) * WanderRadius.X * r + WanderCentre.X, 0, Mathf.Sin(a) * WanderRadius.Y * r + WanderCentre.Z);
            if (_horses.All(h => h.Presenter == self || h.Presenter.Position.DistanceTo(p) > clearance) &&
                _wander.Where(kv => kv.Key != self && kv.Value.Target.HasValue).All(kv => kv.Value.Target!.Value.DistanceTo(p) > clearance))
            {
                if (!shelter) return p;
                if (best is not { } b || p.Z < b.Z) best = p; // the barn stands at negative Z
            }
        }
        return best ?? self.Position;
    }

    private HorsePresenter? MareOf(string mareKey) => _horses.FirstOrDefault(h => h.Key == mareKey).Presenter;

    private Vector3 ClampToPaddock(Vector3 p)
    {
        var o = new Vector2((p.X - WanderCentre.X) / (WanderRadius.X * 1.1f), (p.Z - WanderCentre.Z) / (WanderRadius.Y * 1.1f));
        if (o.LengthSquared() > 1f) { o = o.Normalized(); p = new Vector3(o.X * WanderRadius.X * 1.1f + WanderCentre.X, 0, o.Y * WanderRadius.Y * 1.1f + WanderCentre.Z); }
        p.Y = 0;
        return p;
    }

    /// <summary>Show exactly these horses (key = horse id), reusing presenters that did not change.</summary>
    /// <param name="mothers">Optional foal key to mare key map; a foal whose mare is also shown stays near her (D-030).</param>
    public void SetHorses(IReadOnlyList<(string Key, VisualDescriptor? Descriptor)> horses, IReadOnlyDictionary<string, string>? names = null,
        IReadOnlyDictionary<string, string>? mothers = null)
    {
        _mothers.Clear();
        if (mothers != null)
            foreach (var (foal, mare) in mothers)
                if (foal != mare && horses.Any(h => h.Key == foal && h.Descriptor?.IsFoal == true) && horses.Any(h => h.Key == mare && h.Descriptor?.IsFoal == false))
                    _mothers[foal] = mare;
        var wanted = horses.Where(h => h.Descriptor != null).Take(Slots.Length).ToList();
        foreach (var stale in _horses.Where(h => wanted.All(w => w.Key != h.Key || w.Descriptor!.CacheKey != h.Presenter.Descriptor?.CacheKey)).ToList())
        {
            stale.Presenter.QueueFree();
            _wander.Remove(stale.Presenter);
            _social.Remove(stale.Key);
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
                if (Wandering) existing.EnableIdleLife(StableSeed(key)); // D-030: live captures keep still horses
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

    /// <summary>Stable per-horse seed (string.GetHashCode is randomised per run).</summary>
    private static int StableSeed(string key)
    {
        uint h = 2166136261;
        foreach (char c in key) h = (h ^ c) * 16777619;
        return unchecked((int)h);
    }

    private readonly Dictionary<HorsePresenter, Label3D> _tags = new();
    private string? _hoverKey;

    // D-035: a tiny emote above the name tag now and then (presentation only: one billboard label per horse, no animation rig).
    private readonly Dictionary<string, (Label3D Label, string Trait)> _emotes = new();

    // Nunito's latin subset has no ♥ or ✦, so the emotes use the system symbol fonts (the cozy font stays the fallback).
    private static Font? _emoteFont;
    private static Font EmoteFont => _emoteFont ??= MakeEmoteFont();

    private static Font MakeEmoteFont()
    {
        var font = new SystemFont { FontNames = new[] { "Apple Symbols", "Segoe UI Symbol", "Noto Sans Symbols2", "DejaVu Sans", "Arial Unicode MS" }, FontWeight = 700 };
        if (UI.CozyTheme.BoldFont is { } cozy) font.Fallbacks = new Godot.Collections.Array<Font> { cozy };
        return font;
    }

    private static string EmoteGlyph(string trait) => trait switch
    {
        Cloverbrook.Domain.Personality.Cuddly => "♥", Cloverbrook.Domain.Personality.Dreamy => "✦", Cloverbrook.Domain.Personality.Brave => "!",
        Cloverbrook.Domain.Personality.Cheeky => "~", Cloverbrook.Domain.Personality.Shy => "…", _ => "?",
    };

    /// <summary>Trait id per horse key (call after SetHorses). Horses then show their emote on a slow, per-horse cadence.</summary>
    public void SetPersonalities(IReadOnlyDictionary<string, string> traits)
    {
        foreach (var stale in _emotes.Keys.Where(k => !traits.ContainsKey(k) || _horses.All(h => h.Key != k)).ToList())
        {
            if (GodotObject.IsInstanceValid(_emotes[stale].Label)) _emotes[stale].Label.QueueFree();
            _emotes.Remove(stale);
        }
        foreach (var (key, presenter) in _horses)
        {
            if (!traits.TryGetValue(key, out var trait)) continue;
            if (_emotes.TryGetValue(key, out var have) && GodotObject.IsInstanceValid(have.Label) && have.Label.GetParent() == presenter)
            {
                if (have.Trait != trait) { have.Label.Text = EmoteGlyph(trait); _emotes[key] = (have.Label, trait); }
                continue;
            }
            var label = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.0046f, OutlineSize = 14, Text = EmoteGlyph(trait),
                Modulate = Color.FromHtml("#e48aa9"), OutlineModulate = new Color(1, 1, 1, 0.92f), Font = EmoteFont,
                NoDepthTest = true, RenderPriority = 11, Visible = false,
            };
            presenter.AddChild(label);
            _emotes[key] = (label, trait);
        }
    }

    private void UpdateEmotes()
    {
        foreach (var (key, presenter) in _horses)
        {
            if (!_emotes.TryGetValue(key, out var e) || !GodotObject.IsInstanceValid(e.Label)) continue;
            if (PhotoMode || presenter.Descriptor == null) { e.Label.Visible = false; continue; }
            var seed = (uint)StableSeed(key);
            double period = 16.0 + seed % 9, phase = (_time + seed % 13) % period;
            const double shown = 2.6;
            bool lovesAttention = e.Trait == Cloverbrook.Domain.Personality.Cuddly && key == _focusedKey; // ♥ while you give them attention
            if (phase >= shown && !lovesAttention) { e.Label.Visible = false; continue; }
            var t = lovesAttention ? (float)((_time * 0.5) % 1.0) : (float)(phase / shown);
            float s = presenter.Descriptor.IsFoal ? 0.66f : 1f, fade = Mathf.Clamp(Mathf.Sin(t * Mathf.Pi) * 2.5f, 0f, 1f);
            e.Label.Visible = true;
            e.Label.Modulate = new Color(1f, 1f, 1f, fade) * Color.FromHtml("#e48aa9");
            e.Label.Position = new Vector3(0.3f * s, 1.95f * s + 0.38f + t * 0.14f, 0);
        }
    }

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
        var prop = key == null ? PickProp(screenPoint) : null;
        if (_fairTag is { } fairTag)
        {
            fairTag.Modulate = prop == "fair" ? Color.FromHtml("#e48aa9") : Color.FromHtml("#5b3a5e");
            fairTag.Scale = Vector3.One * (prop == "fair" ? 1.2f : 1f);
        }
        Input.SetDefaultCursorShape(key == null && prop == null ? Input.CursorShape.Arrow : Input.CursorShape.PointingHand);
        if (key == _hoverKey) return key;
        Highlight(key);
        return key;
    }

    /// <summary>Light up one horse's name tag (from the yard pointer or its roster card); null clears it.</summary>
    public void Highlight(string? key)
    {
        if (_hoverKey != key && key != null) ReactToApproach(key);
        _hoverKey = key;
        foreach (var (k, presenter) in _horses)
            if (_tags.TryGetValue(presenter, out var tag))
            {
                tag.Modulate = k == key ? Color.FromHtml("#e48aa9") : Color.FromHtml("#5b3a5e");
                tag.Scale = Vector3.One * (k == key ? 1.25f : 1f);
            }
    }

    /// <summary>A burst of hearts and sparkles over a horse (comfort care switched on).</summary>
    public void Cheer(string key)
    {
        var presenter = _horses.FirstOrDefault(h => h.Key == key).Presenter;
        if (presenter == null) return;
        Audio.GameAudio.Play("care_cheer", -10);
        float s = presenter.Descriptor?.IsFoal == true ? 0.66f : 1f;
        // A happy little hop (D-021).
        var groundY = presenter.Position.Y;
        var hop = CreateTween();
        hop.TweenProperty(presenter, "position:y", groundY + 0.09f * s, 0.14).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        hop.TweenProperty(presenter, "position:y", groundY, 0.22).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
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

    // D-022 decorations: owned items stand at fixed, camera-visible spots outside the paddock.
    public static readonly (Vector3 Position, float Yaw)[] DecorSpots =
    {
        (new Vector3(-8.0f, 0, -2.6f), 40f), (new Vector3(-10.2f, 0, -9.2f), 55f), (new Vector3(-6.0f, 0, -11.0f), 15f),
        (new Vector3(6.6f, 0, -10.0f), -20f), (new Vector3(9.6f, 0, -4.2f), -50f), (new Vector3(-1.6f, 0, -9.4f), 5f),
        (new Vector3(2.0f, 0, -9.0f), -10f), (new Vector3(13.0f, 0, -12.0f), -35f),
    };
    private readonly Dictionary<string, Node3D> _decor = new();
    private Node3D? _annex;

    /// <summary>Show the owned decorations at their spots (asset, target height, spot index); unlisted ones are removed.</summary>
    public void SetDecor(IReadOnlyList<(string Key, string Asset, float Height, int Spot)> items)
    {
        foreach (var key in _decor.Keys.Where(k => items.All(i => i.Key != k)).ToList()) { _decor[key].QueueFree(); _decor.Remove(key); }
        foreach (var (key, asset, height, spot) in items)
        {
            if (spot < 0 || spot >= DecorSpots.Length) continue;
            var (position, yaw) = DecorSpots[spot];
            if (_decor.TryGetValue(key, out var existing))
            {
                existing.Position = position; existing.RotationDegrees = new Vector3(0, yaw, 0);
                continue;
            }
            var node = Place(asset, height, position, yaw, () => new Node3D());
            node.Scale = Vector3.One * 0.01f;
            CreateTween().TweenProperty(node, "scale", Vector3.One, 0.45).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _decor[key] = node;
        }
    }

    /// <summary>D-022: a second stable block appears once extra stalls are built.</summary>
    public void SetStalls(int stalls)
    {
        var show = stalls > 4;
        if (show && _annex == null) _annex = Place("stable_block", 4.2f, new Vector3(-13.2f, 0, -13.4f), 28f, () => new Node3D());
        if (_annex != null) _annex.Visible = show;
    }

    private Node3D _fairTent = null!, _trophy = null!;
    private Label3D _trophyTag = null!, _fairTag = null!;

    private static Label3D PropTag(string text, float height) => new()
    {
        Text = text, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.0075f, OutlineSize = 18,
        Modulate = Color.FromHtml("#5b3a5e"), OutlineModulate = new Color(1, 1, 1, 0.92f), Font = UI.CozyTheme.BoldFont,
        Position = new Vector3(0, height, 0), NoDepthTest = true, RenderPriority = 9,
    };

    /// <summary>Shows the trophy stand with the number of cups won (hidden until the first win).</summary>
    public void SetTrophies(int wins)
    {
        _trophy.Visible = wins > 0;
        _trophyTag.Text = wins > 1 ? $"Cup wins ×{wins}" : "Cup champion";
    }

    /// <summary>The clickable landmark under the pointer: "fair" or "trophy", or null.</summary>
    public string? PickProp(Vector2 screenPoint)
    {
        foreach (var (key, node, centreHeight, radius) in new[] { ("fair", _fairTent, 1.6f, 2.0f), ("trophy", _trophy, 0.7f, 0.9f) })
        {
            if (node == null || !node.Visible) continue;
            var centre = node.GlobalPosition + Vector3.Up * centreHeight;
            if (_camera.IsPositionBehind(centre)) continue;
            var projected = _camera.UnprojectPosition(centre);
            var edge = _camera.UnprojectPosition(centre + _camera.GlobalBasis.X * radius);
            if (projected.DistanceTo(screenPoint) < projected.DistanceTo(edge)) return key;
        }
        return null;
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
        var previous = _focusedKey;
        _focusedKey = key;
        if (key != null && key != previous) ReactToApproach(key);
        var presenter = _horses.FirstOrDefault(h => h.Key == key).Presenter;
        // D-036: a resting horse sometimes answers a click with a soft nicker.
        if (presenter != null && key != previous && Wandering && IsResting(presenter) && _rng.Randf() < 0.45f)
            GetTree().CreateTimer(0.45).Timeout += () => Audio.GameAudio.Play("horse_nicker", -15);
        if (presenter == null) { _cameraGoal = _cameraHome; _targetGoal = _homeTarget; return; }
        var p = presenter.GlobalPosition;
        _targetGoal = p + new Vector3(0, 0.95f, 0);
        _cameraGoal = p + new Vector3(0.4f, 2.6f, 6.0f);
    }

    public int HorseCount => _horses.Count;

    // ---- D-023 photo mode ----
    public bool PhotoMode { get; private set; }
    private string? _photoKey;
    private float _photoYaw = 15f, _photoPitch = 14f, _photoDistance = 6.5f;

    public string? PhotoSubject => _photoKey;

    /// <summary>The focused horse if any, else the first horse, else the whole yard.</summary>
    public string? PhotoNextSubjectFirst() => _focusedKey ?? _horses.Select(h => h.Key).FirstOrDefault();

    public void EnterPhotoMode(string? key)
    {
        PhotoMode = true; _photoKey = key;
        foreach (var tag in _tags.Values) tag.Visible = false; // clean photos: no name tags
        if (_fairTag != null) _fairTag.Visible = false;
        if (_trophyTag != null) _trophyTag.Visible = false;
        _photoYaw = FrontYaw(key); _photoPitch = 14f; _photoDistance = key == null ? 12f : 5.5f;
    }

    public void ExitPhotoMode()
    {
        PhotoMode = false; _photoKey = null; Focus(null);
        foreach (var tag in _tags.Values) tag.Visible = true;
        if (_fairTag != null) _fairTag.Visible = true;
        if (_trophyTag != null) _trophyTag.Visible = true;
    }

    public void PhotoOrbit(float yawDegrees, float pitchDegrees)
    {
        _photoYaw += yawDegrees;
        _photoPitch = Mathf.Clamp(_photoPitch + pitchDegrees, 3f, 70f);
    }

    /// <summary>Orbit angle that frames a horse from its front three-quarter (horses face +X in their own space).</summary>
    private float FrontYaw(string? key)
    {
        var presenter = _horses.FirstOrDefault(h => h.Key == key).Presenter;
        return presenter == null ? 15f : presenter.GlobalRotationDegrees.Y + 90f - 35f;
    }

    public void PhotoZoom(float factor) => _photoDistance = Mathf.Clamp(_photoDistance * factor, 2.4f, 18f);

    /// <summary>Point the photo camera at the next horse (then the whole paddock); returns its key or null.</summary>
    public string? PhotoNextSubject()
    {
        var keys = _horses.Select(h => h.Key).ToList();
        var index = _photoKey == null ? -1 : keys.IndexOf(_photoKey);
        _photoKey = index + 1 < keys.Count ? keys[index + 1] : null;
        _photoDistance = _photoKey == null ? 12f : 5.5f;
        _photoYaw = FrontYaw(_photoKey);
        return _photoKey;
    }

    /// <summary>Dim the sun toward dusk and back over the given seconds.</summary>
    public void PlayNightfall(double seconds)
    {
        var suns = All<DirectionalLight3D>(this).ToList();
        var dusk = CreateTween().SetParallel();
        foreach (var sun in suns)
        {
            dusk.TweenProperty(sun, "light_color", new Color(1f, 0.6f, 0.4f), seconds * 0.3);
            dusk.TweenProperty(sun, "light_energy", sun.LightEnergy * 0.25f, seconds * 0.45);
        }
        // Dawn rises to the new day's season and weather, which may have changed overnight.
        GetTree().CreateTimer(seconds * 0.65).Timeout += () =>
        {
            if (!IsInsideTree()) return;
            var dim = suns.ToDictionary(sun => sun, sun => (sun.LightColor, sun.LightEnergy));
            ApplyWeather();
            var dawn = CreateTween().SetParallel();
            foreach (var sun in suns)
            {
                var (colour, energy) = (sun.LightColor, sun.LightEnergy);
                (sun.LightColor, sun.LightEnergy) = dim[sun];
                dawn.TweenProperty(sun, "light_color", colour, seconds * 0.35);
                dawn.TweenProperty(sun, "light_energy", energy, seconds * 0.35);
            }
        };
    }
    public double ElapsedSeconds => _time;

    // Seasons (presentation only): the calendar's season index picks the yard's look.
    private ShaderMaterial _grassMat = null!, _sandMat = null!;
    private StandardMaterial3D _hillMat = null!, _bladeMat = null!;
    private MultiMeshInstance3D _blades = null!, _flowers = null!;
    private CpuParticles3D _petals = null!;
    private Node3D _lights = null!;
    private readonly Dictionary<DirectionalLight3D, (Color Colour, float Energy)> _sunDefaults = new();
    private readonly List<MeshInstance3D> _trees = new();

    private Label3D? _signLabel;

    /// <summary>Paint the player's stable name on the signpost (two lines when it is long).</summary>
    public void SetStableName(string name)
    {
        if (_signLabel == null) return;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _signLabel.Text = words.Length > 1 && name.Length > 11
            ? string.Join(' ', words.Take(words.Length / 2 + words.Length % 2)) + "\n" + string.Join(' ', words.Skip(words.Length / 2 + words.Length % 2))
            : name;
        _signLabel.FontSize = name.Length > 22 ? 70 : 96;
    }

    public static string SeasonName(int seasonIndex) => (seasonIndex % 4) switch { 0 => "Spring", 1 => "Summer", 2 => "Autumn", _ => "Winter" };

    /// <summary>Dress the yard for the season: 0 spring, 1 summer, 2 autumn, 3 winter (repeating).</summary>
    public void SetSeason(int seasonIndex)
    {
        int s = ((seasonIndex % 4) + 4) % 4;
        var (a, b, clover, hill, blade) = s switch
        {
            1 => ("#5f9e45", "#9ccc5e", "#4f8c3f", "#a6cf88", "#e8ffd8"),
            2 => ("#a39a52", "#cdb46a", "#8f8a46", "#c9b77c", "#e6c27a"),
            3 => ("#e9eef4", "#ffffff", "#d7e1ea", "#eef3f7", "#ffffff"),
            _ => ("#8fbd73", "#b3d585", "#78ad6b", "#b3d39c", "#ffffff"),
        };
        _grassMat.SetShaderParameter("grass_a", Color.FromHtml(a));
        _grassMat.SetShaderParameter("grass_b", Color.FromHtml(b));
        _grassMat.SetShaderParameter("clover", Color.FromHtml(clover));
        _hillMat.AlbedoColor = Color.FromHtml(hill);
        _sandMat.SetShaderParameter("rim_grass", Color.FromHtml(s == 3 ? "#eef3f8" : s == 2 ? "#bfae66" : "#9ec27a"));
        _bladeMat.AlbedoColor = Color.FromHtml(blade);
        _blades.Visible = s != 3;
        _flowers.Visible = s <= 1;

        var ramp = new Gradient();
        switch (s)
        {
            case 2: // falling leaves
                ramp.SetColor(0, Color.FromHtml("#e8893a")); ramp.SetColor(1, Color.FromHtml("#c9452f")); ramp.AddPoint(0.5f, Color.FromHtml("#f2c14e"));
                _petals.Amount = 70; _petals.ScaleAmountMin = 1.4f; _petals.ScaleAmountMax = 2.2f;
                _petals.Gravity = new Vector3(0.2f, -0.35f, 0.05f); _petals.Emitting = true; break;
            case 3: // snow
                ramp.SetColor(0, Colors.White); ramp.SetColor(1, Color.FromHtml("#e6f0ff"));
                _petals.Amount = 220; _petals.ScaleAmountMin = 0.6f; _petals.ScaleAmountMax = 1.2f;
                _petals.Gravity = new Vector3(0.03f, -0.45f, 0.02f); _petals.Emitting = true; break;
            case 1: // summer: clear air
                _petals.Emitting = false; break;
            default: // spring blossom
                ramp.SetColor(0, Color.FromHtml("#fbd0dd")); ramp.SetColor(1, Colors.White);
                _petals.Amount = 60; _petals.ScaleAmountMin = 1f; _petals.ScaleAmountMax = 1f;
                _petals.Gravity = new Vector3(0.15f, -0.25f, 0.05f); _petals.Emitting = true; break;
        }
        if (s != 1) _petals.ColorInitialRamp = ramp;

        // Tint the Meshy oaks with a translucent overlay rather than editing their textures.
        StandardMaterial3D? overlay = s switch
        {
            2 => new StandardMaterial3D { AlbedoColor = new Color(0.93f, 0.52f, 0.2f, 0.42f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            3 => new StandardMaterial3D { AlbedoColor = new Color(0.96f, 0.98f, 1f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            _ => null,
        };
        foreach (var tree in _trees) tree.MaterialOverlay = overlay;

        _season = s;
        ApplyWeather();
    }

    private int _season;
    private YardWeather _weather = YardWeather.Sunny;
    private CpuParticles3D? _rain;
    private WorldEnvironment? _environment;
    private (Color Top, Color Horizon, Color GroundHorizon, float Fog, float Ambient)? _skyDefaults;

    /// <summary>Presentation-only weather for the day (D-018): clouds dim the sun, rain falls (snow thickens in winter).</summary>
    public void SetWeather(YardWeather weather)
    {
        // D-030: when the rain starts, resting horses soon head for the barn side.
        if (weather == YardWeather.Rain && _weather != YardWeather.Rain)
            foreach (var w in _wander.Values)
                if (w.Target == null) w.RestLeft = Math.Min(w.RestLeft, _rng.RandfRange(1f, 6f));
        _weather = weather;
        ApplyWeather();
    }

    public YardWeather Weather => _weather;

    private void ApplyWeather()
    {
        int s = _season;
        float sunScale = _weather switch { YardWeather.Rain => 0.5f, YardWeather.Cloudy => 0.78f, _ => 1f };
        foreach (var (sun, (colour, energy)) in _sunDefaults)
        {
            var seasonal = s switch { 2 => colour.Lerp(new Color(1f, 0.72f, 0.48f), 0.45f), 3 => colour.Lerp(new Color(0.82f, 0.9f, 1f), 0.5f), 1 => colour.Lerp(new Color(1f, 0.97f, 0.88f), 0.3f), _ => colour };
            sun.LightColor = _weather == YardWeather.Sunny ? seasonal : seasonal.Lerp(new Color(0.8f, 0.84f, 0.95f), _weather == YardWeather.Rain ? 0.5f : 0.3f);
            sun.LightEnergy = energy * (s == 3 ? 0.92f : s == 1 ? 1.08f : 1f) * sunScale;
            sun.ShadowOpacity = _weather switch { YardWeather.Rain => 0.35f, YardWeather.Cloudy => 0.65f, _ => 1f }; // overcast light casts soft shadows
        }
        _environment ??= All<WorldEnvironment>(_lights).FirstOrDefault();
        if (_environment?.Environment is { } env && env.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
        {
            _skyDefaults ??= (sky.SkyTopColor, sky.SkyHorizonColor, sky.GroundHorizonColor, env.FogDensity, env.AmbientLightEnergy);
            var d = _skyDefaults.Value;
            var grey = new Color(0.62f, 0.66f, 0.74f);
            float w = _weather switch { YardWeather.Rain => 0.7f, YardWeather.Cloudy => 0.4f, _ => 0f };
            sky.SkyTopColor = d.Top.Lerp(grey, w);
            sky.SkyHorizonColor = d.Horizon.Lerp(new Color(0.82f, 0.8f, 0.84f), w);
            sky.GroundHorizonColor = d.GroundHorizon.Lerp(grey, w * 0.6f);
            env.FogDensity = d.Fog * (_weather == YardWeather.Rain ? 4f : _weather == YardWeather.Cloudy ? 1.8f : 1f);
            env.AmbientLightEnergy = d.Ambient * (_weather == YardWeather.Rain ? 0.85f : 1f);
        }
        if (_rain == null) { _rain = Rain(); AddChild(_rain); }
        bool raining = _weather == YardWeather.Rain;
        _rain.Emitting = raining && s != 3;
        _rain.Visible = raining && s != 3;
        if (s == 3) _petals.Amount = raining ? 480 : 220; // a winter "rain" day is a snowy one
        else if (s != 1) _petals.Emitting = !raining; // no blossom or leaves drifting through the rain
        else _petals.Emitting = false;
        ApplyAtmosphereWeather();
    }

    private static CpuParticles3D Rain()
    {
        var rain = new CpuParticles3D
        {
            Name = "Rain", Amount = 1600, Lifetime = 1.1, Preprocess = 2, Emitting = false,
            Mesh = new QuadMesh { Size = new Vector2(0.012f, 0.32f) },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(13, 0.5f, 9),
            Position = new Vector3(0, 8, -3), Direction = new Vector3(0.08f, -1, 0.02f), Spread = 2,
            Gravity = new Vector3(0, -9.8f, 0), InitialVelocityMin = 6.5f, InitialVelocityMax = 8f,
            ParticleFlagAlignY = true, Color = new Color(0.88f, 0.92f, 1f, 0.7f),
        };
        rain.MaterialOverride = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
        };
        return rain;
    }

    // ---------------------------------------------------------------------------------
    private void BuildGround()
    {
        var grass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass_ground.gdshader") };
        _grassMat = grass;
        AddChild(Sets.Mesh(new PlaneMesh { Size = new Vector2(160, 160), SubdivideWidth = 2, SubdivideDepth = 2 }, grass, Vector3.Zero));
        var sand = Sets.Mat(Color.FromHtml("#e6cfa6"), 0.95f);
        var raked = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/paddock_sand.gdshader") };
        _sandMat = raked;
        var paddock = Sets.Mesh(new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.02f, RadialSegments = 64 }, raked, new Vector3(0, 0.006f, -0.5f));
        paddock.Scale = new Vector3(7.0f, 1, 5.0f);
        AddChild(paddock);
        var path = Sets.Mesh(new BoxMesh { Size = new Vector3(2.4f, 0.02f, 9f) }, sand, new Vector3(-5.2f, 0.007f, -8.5f), new Vector3(0, 28, 0));
        AddChild(path);

        var rng = new RandomNumberGenerator { Seed = 11 };
        AddChild(GrassAndFlowers(rng));
        InitializeAtmosphere(path);
        var hill = Sets.Mat(Color.FromHtml("#b3d39c"), 1f);
        _hillMat = hill;
        for (var i = 0; i < 9; i++)
        {
            float a = Mathf.DegToRad(200 + i * 18), r = 48 + rng.RandfRange(-5, 8);
            var h = Sets.Mesh(new SphereMesh { Radius = 1, Height = 2 }, hill, new Vector3(Mathf.Cos(a) * r, -7, Mathf.Sin(a) * r));
            h.Scale = new Vector3(rng.RandfRange(16, 26), rng.RandfRange(9, 13), rng.RandfRange(10, 16));
            AddChild(h);
        }
    }

    /// <summary>Blossom petals drifting slowly across the yard.</summary>
    private static CpuParticles3D Petals()
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
    private Node3D GrassAndFlowers(RandomNumberGenerator rng)
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
        _blades = new MultiMeshInstance3D { Multimesh = blades, MaterialOverride = bladeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _bladeMat = bladeMat;
        root.AddChild(_blades);

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
        _flowers = new MultiMeshInstance3D { Multimesh = heads, MaterialOverride = petalMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        root.AddChild(_flowers);
        return root;
    }

    private void BuildScenery()
    {
        var rng = new RandomNumberGenerator { Seed = 23 };
        Place("barn", 7.5f, new Vector3(-3.5f, 0, -17f), 10f, () => FallbackBarn());

        // Paddock fence around the sand ellipse, open toward the camera.
        const int segments = FenceSegments; const float rx = FenceRx, rz = FenceRz;
        for (var i = 0; i < segments; i++)
        {
            float a0 = Mathf.DegToRad(FenceStartDeg + i * (FenceSpanDeg / segments)), a1 = Mathf.DegToRad(FenceStartDeg + (i + 1) * (FenceSpanDeg / segments));
            var p0 = new Vector3(Mathf.Cos(a0) * rx, 0, Mathf.Sin(a0) * rz - 0.5f);
            var p1 = new Vector3(Mathf.Cos(a1) * rx, 0, Mathf.Sin(a1) * rz - 0.5f);
            var mid = (p0 + p1) / 2; float yaw = Mathf.RadToDeg(Mathf.Atan2(-(p1.Z - p0.Z), p1.X - p0.X));
            PlaceLength("fence", p0.DistanceTo(p1) + 0.15f, mid, yaw, () => FallbackFence(p0.DistanceTo(p1)));
        }

        foreach (var (pos, size) in new[] { (new Vector3(-13, 0, -9), 7.5f), (new Vector3(-10f, 0, -17), 8.5f), (new Vector3(16f, 0, -17), 8f),
                     (new Vector3(14, 0, -5), 7f), (new Vector3(4, 0, -20), 9f), (new Vector3(-17, 0, -2), 6.5f) })
            Place("oak_tree", size * rng.RandfRange(0.85f, 1.1f), pos, rng.RandfRange(0, 360), () => FallbackTree(rng));
        Place("hay_props", 1.6f, new Vector3(2.4f, 0, -12.6f), -25f, () => FallbackHay());
        Place("water_trough", 0.85f, new Vector3(4.4f, 0, -3.8f), 75f, () => FallbackTrough());
        // D-045: decorative feed trough; existing care and water-trough behavior are unchanged.
        Place("feed_trough", 0.85f, new Vector3(-1.8f, 0, -13.1f), 10f, () => new Node3D());
        Place("stable_bucket", 0.60f, new Vector3(-0.8f, 0, -12.5f), -15f, () => new Node3D());
        Place("stable_block", 5.2f, new Vector3(8.6f, 0, -15.5f), -18f, () => new Node3D());
        Place("planter_box", 0.75f, new Vector3(-8.2f, 0, -13.0f), 10f, () => new Node3D());
        Place("planter_box", 0.75f, new Vector3(1.3f, 0, -13.2f), 10f, () => new Node3D());
        Place("bench", 1.0f, new Vector3(-8.6f, 0, -6.4f), 50f, () => new Node3D());
        Place("saddle_rack", 1.25f, new Vector3(-0.2f, 0, -12.2f), 15f, () => new Node3D());
        Place("wheelbarrow", 0.85f, new Vector3(4.6f, 0, -11.6f), -60f, () => new Node3D());
        Place("show_jump", 1.3f, new Vector3(7.2f, 0, -7.5f), -25f, () => new Node3D());
        // Clickable landmarks (D-019): the fair tent opens the Horse Fair; the trophy stand appears once a cup is won.
        _fairTent = Place("fair_tent", 3.1f, new Vector3(10.0f, 0, -8.6f), -30f, () => new Node3D());
        _fairTag = PropTag("Horse Fair", 3.2f);
        _fairTent.AddChild(_fairTag);
        _trophy = Place("trophy_stand", 1.5f, new Vector3(-3.2f, 0, -9.2f), 15f, () => new Node3D());
        _trophyTag = PropTag("", 2.05f);
        _trophy.AddChild(_trophyTag);
        _trophy.Visible = false;
        var sign = Place("signpost", 2.5f, new Vector3(-5.6f, 0, -4.2f), 18f, () => new Node3D());
        // The generated board is blank on purpose; the stable's name is real text so it stays crisp.
        _signLabel = new Label3D
        {
            Text = "Cloverbrook\nStables", FontSize = 96, PixelSize = 0.0022f, OutlineSize = 0, Modulate = Color.FromHtml("#5b3a5e"),
            Font = UI.CozyTheme.BoldFont, Position = new Vector3(0.0f, 1.62f, 0.2f), NoDepthTest = false,
        };
        sign.AddChild(_signLabel);
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
        holder.AddToGroup("scenery_" + asset);
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

    /// <summary>Drop managed references to cached scenery so nothing outlives the scene tree at shutdown.</summary>
    public static void ReleaseCaches()
    {
        foreach (var scene in AssetCache.Values) scene?.Dispose();
        AssetCache.Clear();
    }

    internal static Node3D? LoadAsset(string asset, out Aabb bounds)
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
