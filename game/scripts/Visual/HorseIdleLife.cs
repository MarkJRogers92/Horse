using System;
using System.Collections.Generic;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// D-030: procedural idle life drawn on top of the Idle clip of a resting yard horse: grazing
/// (neck and head lowered), small head lifts, ear flicks and a tail swish. It is a
/// <see cref="SkeletonModifier3D"/>, so it runs after the AnimationPlayer has written the pose,
/// and costs a handful of bone writes per frame. State advances in <see cref="Tick"/> (called by
/// <see cref="HorsePresenter"/>, so tests can step it on a fixed clock); the modifier only applies
/// it. Everything fades to exactly zero when <see cref="Calm"/> is false, which the yard clears
/// before any walk, turn or Idle/Walk bridge, and it is never attached to a Walk model.
/// </summary>
public partial class HorseIdleLife : SkeletonModifier3D
{
    // Neck and head chain, base to tip, with each bone's share of the total pitch. Bones a rig lacks
    // (the foal has fewer) are dropped and the shares renormalised.
    private static readonly (string Bone, float Share)[] NeckChain =
    {
        ("Neck_Base", 0.14f), ("Neck", 0.20f), ("Neck_Mid", 0.20f), ("Neck_Upper", 0.14f), ("Neck_Top", 0.10f),
        ("Head_Base", 0.05f), ("Head", 0.05f),
    };
    private static readonly string[] TailChain = { "Tail", "Tail_01", "Tail_02", "Tail_03", "Tail_04", "Tail_05" };
    private static readonly string[] EarBones = { "Ear_L", "Ear_R" };

    private const float GrazePitchDegrees = 82f, FoalGrazePitchDegrees = 88f;
    private const float LiftPitchDegrees = 7f;
    private const float TailSwayDegrees = 4.5f;
    private const float EarFlickDegrees = 16f;

    private sealed class Target
    {
        public int Bone;
        public float Share;
        public Vector3 Axis;       // rotation axis in the parent bone's frame
        public Quaternion Base, Last;
        public bool Has;
    }

    private HorsePresenter? _owner;
    private Skeleton3D? _skeleton;
    private readonly List<Target> _neck = new(), _tail = new(), _ears = new();
    private RandomNumberGenerator _rng = new();
    private float _pitchSign = 1, _pitchDegrees = GrazePitchDegrees;

    private float _master;        // 0..1 fade for everything, follows Calm
    private float _grazeT;        // 0..1 linear progress in or out of a graze
    private double _grazeLeft, _nextGraze, _grazeClock;
    private double _liftLeft, _nextLift, _liftClock;
    private double _earLeft, _nextEar, _earClock;
    private int _earSide;
    private double _gustLeft, _nextGust;
    private double _tailPhase;
    private bool _grazeRequested;

    /// <summary>True while the horse is standing at rest and may graze, swish and flick.</summary>
    public bool Calm { get; set; }

    /// <summary>Largest current offset weight (0 = pose is exactly the clip). The yard waits for this to fall before moving.</summary>
    public float Offset => Math.Max(Smooth(_master), Smooth(_grazeT) * _master);
    public bool Grazing => _grazeT > 0.5f;
    public int GrazeCount { get; private set; }
    /// <summary>True while a graze is under way (the yard lets it finish before the horse moves on).</summary>
    public bool Busy => _grazeLeft > 0;
    /// <summary>Seconds of standing rest left before the next graze; carried across Idle/Walk swaps by the presenter.</summary>
    public double NextGraze { get => _nextGraze; set => _nextGraze = value; }
    public float GrazeWeight => Smooth(_grazeT);

    public void Setup(HorsePresenter owner, Skeleton3D skeleton, int seed, bool foal)
    {
        _owner = owner;
        _skeleton = skeleton;
        _rng = new RandomNumberGenerator { Seed = (ulong)(uint)seed };
        _pitchDegrees = foal ? FoalGrazePitchDegrees : GrazePitchDegrees;
        _nextGraze = _rng.RandfRange(8f, 20f);
        _nextLift = _rng.RandfRange(10f, 24f);
        _nextEar = _rng.RandfRange(3f, 9f);
        _nextGust = _rng.RandfRange(4f, 11f);
        _tailPhase = _rng.RandfRange(0f, Mathf.Tau);

        // Axes: lateral (pitch) and up (tail sway) of the horse, expressed in each bone's parent frame.
        var toHorse = Basis.Identity;
        for (Node? n = skeleton; n != null && n != owner; n = n.GetParent())
            if (n is Node3D n3) toHorse = n3.Basis * toHorse;
        toHorse = toHorse.Orthonormalized();
        var fromHorse = toHorse.Inverse();
        var lateral = (fromHorse * Vector3.Back).Normalized(); // skeleton space; the presenter faces +X, so Z is across the body
        var up = (fromHorse * Vector3.Up).Normalized();

        Vector3 InParent(int bone, Vector3 axis)
        {
            int parent = skeleton.GetBoneParent(bone);
            var basis = parent >= 0 ? skeleton.GetBoneGlobalRest(parent).Basis.Orthonormalized() : Basis.Identity;
            return (basis.Inverse() * axis).Normalized();
        }

        float total = 0;
        foreach (var (name, share) in NeckChain)
        {
            int bone = skeleton.FindBone(name);
            if (bone < 0) continue;
            _neck.Add(new Target { Bone = bone, Share = share, Axis = InParent(bone, lateral) });
            total += share;
        }
        foreach (var t in _neck) t.Share /= Math.Max(total, 0.0001f);
        foreach (var name in TailChain)
        {
            int bone = skeleton.FindBone(name);
            if (bone >= 0) _tail.Add(new Target { Bone = bone, Axis = InParent(bone, up) });
        }
        foreach (var name in EarBones)
        {
            int bone = skeleton.FindBone(name);
            if (bone >= 0) _ears.Add(new Target { Bone = bone, Axis = InParent(bone, lateral) });
        }

        // Which way is "down"? Rotating the neck about the lateral axis must carry the head toward the ground.
        int head = skeleton.FindBone("Head"), neckBase = skeleton.FindBone("Neck_Base");
        if (head >= 0 && neckBase >= 0)
        {
            var forward = skeleton.GetBoneGlobalRest(head).Origin - skeleton.GetBoneGlobalRest(neckBase).Origin;
            var down = (fromHorse * Vector3.Down).Normalized();
            _pitchSign = lateral.Cross(forward).Dot(down) >= 0 ? 1 : -1;
        }
    }

    /// <summary>True when the rig has at least a neck to move (otherwise the component does nothing).</summary>
    public bool HasNeck => _neck.Count > 0;

    private static float Smooth(float t) { t = Mathf.Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }

    /// <summary>Advance the schedule and easing by <paramref name="delta"/> seconds.</summary>
    public void Tick(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0) return;
        float d = (float)delta;
        _master = Mathf.MoveToward(_master, Calm ? 1f : 0f, d * 1.6f);
        bool settled = Calm && _master > 0.95f;

        // Graze: every 8 to 20 s, 4 to 8 s with the head down.
        if (_grazeLeft > 0 && settled) _grazeLeft -= delta;
        else if (_grazeLeft > 0) _grazeLeft = 0; // calm lost: let the head come up
        if (_grazeLeft <= 0 && _grazeT <= 0.001f && settled)
        {
            _nextGraze -= delta;
            if (_nextGraze <= 0)
            {
                _grazeLeft = _rng.RandfRange(4f, 8f);
                _nextGraze = _rng.RandfRange(8f, 20f);
                GrazeCount++;
            }
        }
        _grazeT = Mathf.MoveToward(_grazeT, _grazeLeft > 0 ? 1f : 0f, d / (_grazeLeft > 0 ? 1.5f : 1.1f));
        if (_grazeT > 0) _grazeClock += delta; else _grazeClock = 0;

        // Small head lift (looking up) while standing, never during a graze.
        if (_liftLeft > 0) { _liftLeft -= delta; _liftClock += delta; }
        else if (settled && _grazeT <= 0.001f)
        {
            _nextLift -= delta;
            if (_nextLift <= 0) { _liftLeft = _liftTotal = _rng.RandfRange(1.6f, 2.6f); _liftClock = 0; _nextLift = _rng.RandfRange(14f, 30f); }
        }

        // Ear flick: one ear, a quarter of a second.
        if (_earLeft > 0) { _earLeft -= delta; _earClock += delta; }
        else if (settled)
        {
            _nextEar -= delta;
            if (_nextEar <= 0) { _earLeft = 0.3; _earClock = 0; _earSide = _rng.Randf() < 0.5f ? 0 : 1; _nextEar = _rng.RandfRange(5f, 12f); }
        }

        // Tail: a constant gentle sway with a stronger, quicker swish every 6 to 14 s.
        if (_gustLeft > 0) _gustLeft -= delta;
        else { _nextGust -= delta; if (_nextGust <= 0) { _gustLeft = 2.2; _nextGust = _rng.RandfRange(6f, 14f); } }
        float gust = _gustLeft > 0 ? Mathf.Sin(Mathf.Pi * (float)(1 - _gustLeft / 2.2)) : 0f;
        _tailPhase += d * Mathf.Tau * (0.45f + 0.7f * gust);
        _gustAmount = gust;
    }

    private double _liftTotal = 2;
    private float _gustAmount;

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (_skeleton == null || _owner == null || _owner.IsTransitioning || _neck.Count == 0) return;
        float master = Smooth(_master);
        if (master <= 0.0005f && _grazeT <= 0.0005f) { Release(); return; }

        float graze = Smooth(_grazeT) * master;
        float lift = 0;
        if (_liftLeft > 0)
        {
            float u = Mathf.Clamp((float)(_liftClock / _liftTotal), 0f, 1f);
            lift = Mathf.Sin(Mathf.Pi * u); lift *= lift;
        }
        // Head angle in degrees, positive = down. While grazing the jaw works a little.
        float chew = graze > 0.5f ? Mathf.Sin((float)_grazeClock * 7.5f) * 1.6f * graze : 0f;
        float sway = Mathf.Sin((float)_grazeClock * 0.9f) * 2.2f * graze;
        float pitch = (graze * (_pitchDegrees + sway) - lift * LiftPitchDegrees * master) * _pitchSign;
        foreach (var t in _neck)
        {
            bool headBone = t.Share > 0 && _skeleton.GetBoneName(t.Bone).ToString().StartsWith("Head", StringComparison.Ordinal);
            Apply(t, Mathf.DegToRad(pitch * t.Share + (headBone ? chew * _pitchSign : 0f)));
        }

        float amp = TailSwayDegrees * (0.5f + 1.5f * _gustAmount) * master;
        for (int i = 0; i < _tail.Count; i++)
            Apply(_tail[i], Mathf.DegToRad(amp * (0.5f + 0.18f * i) * Mathf.Sin((float)_tailPhase - 0.6f * i)));

        for (int i = 0; i < _ears.Count; i++)
        {
            float flick = _earLeft > 0 && i == _earSide ? Mathf.Sin(Mathf.Pi * Mathf.Clamp((float)(_earClock / 0.3), 0f, 1f)) : 0f;
            Apply(_ears[i], Mathf.DegToRad(EarFlickDegrees * flick * master * (i == 0 ? 1 : -1) * 0.8f));
        }
    }

    // The pose a bone has when our offset is not on it: the clip's value, or the value we saved when the
    // clip has no track for the bone (so a bone the clip never writes does not accumulate our rotation).
    private void Apply(Target t, float radians)
    {
        var skeleton = _skeleton!;
        var current = skeleton.GetBonePoseRotation(t.Bone);
        var baseRotation = t.Has && current == t.Last ? t.Base : current;
        var next = new Quaternion(t.Axis, radians) * baseRotation;
        skeleton.SetBonePoseRotation(t.Bone, next);
        t.Base = baseRotation; t.Last = next; t.Has = true;
    }

    private void Release()
    {
        foreach (var list in new[] { _neck, _tail, _ears })
            foreach (var t in list)
                if (t.Has && _skeleton!.GetBonePoseRotation(t.Bone) == t.Last)
                {
                    _skeleton.SetBonePoseRotation(t.Bone, t.Base);
                    t.Has = false;
                }
    }

    /// <summary>Drop every offset right now (the bridge is about to read the clip pose).</summary>
    public void Clear()
    {
        Calm = false;
        _master = 0; _grazeT = 0; _grazeLeft = 0; _liftLeft = 0; _earLeft = 0; _gustLeft = 0;
        if (_skeleton != null) Release();
    }

    /// <summary>The bone names this component drives on the current rig (for the one-time log).</summary>
    public string DescribeBones()
    {
        string Names(List<Target> list)
        {
            var names = new List<string>();
            foreach (var t in list) names.Add(_skeleton!.GetBoneName(t.Bone).ToString());
            return string.Join(",", names);
        }
        return $"neck=[{Names(_neck)}] tail=[{Names(_tail)}] ears=[{Names(_ears)}]";
    }
}
