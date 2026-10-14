using System;
using System.Collections.Generic;
using System.Linq;
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
    // Neck and head chain, base to tip. Share: how much of a head lift (the small "look up") each bone takes. Bones a rig lacks
    // (the foal has fewer) are dropped and the shares renormalised.
    private static readonly (string Bone, float Share)[] NeckChain =
    {
        ("Neck_Base", 0.14f), ("Neck", 0.20f), ("Neck_Mid", 0.20f), ("Neck_Upper", 0.14f), ("Neck_Top", 0.10f),
        ("Head_Base", 0.05f), ("Head", 0.05f),
    };
    private static readonly string[] TailChain = { "Tail", "Tail_01", "Tail_02", "Tail_03", "Tail_04", "Tail_05" };
    private static readonly string[] EarBones = { "Ear_L", "Ear_R" };

    private const float LiftPitchDegrees = 7f;
    private const float TailSwayDegrees = 4.5f;
    private const float EarFlickDegrees = 16f;

    private sealed class Target
    {
        public int Bone;
        public float Share;
        public float GrazeDegrees;   // pitch at the bottom of a graze (neck chain) or the full dip/splay angle (body)
        public Vector3 Axis;       // rotation axis in the parent bone's frame
        public Quaternion Base, Last;
        public bool Has;
    }

    private HorsePresenter? _owner;
    private Skeleton3D? _skeleton;
    private readonly List<Target> _neck = new(), _tail = new(), _ears = new();
    private RandomNumberGenerator _rng = new();
    private float _pitchSign = 1;
    private readonly List<Target> _dip = new(), _legs = new();   // D-036: shoulder dip and front-leg stance while grazing
    private bool _foal;
    /// <summary>D-036 graze pose for one mesh family: pitch per neck-chain bone, shoulder dip, and the foreleg stance (one forward, one back).</summary>
    private sealed record Tune(float[] Neck, float Dip, float LegForward, float LegBack, float Counter);
    // D-036: pitch (degrees, positive = down) each neck-chain bone takes at the bottom of a graze. The bend sits mostly in the lower
    // neck and the head bones extend back (negative) so the face points down and forward instead of tucking under the chest. The
    // foal rig has only Neck_Base, Neck, Head_Base and Head (the zeros are bones it lacks). Dip is the shoulder tip in degrees and the
    // forelegs stand one forward and one back; both were tuned against GrazePreview's muzzle and hoof height readout per family.
    private static readonly Tune StandardAdult = new(new[] { 42f, 32f, 18f, 8f, 4f, -25f, -25f }, 13f, 16f, 20f, 1f);
    private static readonly Tune CompactAdult = new(new[] { 46f, 34f, 18f, 8f, 4f, -36f, -34f }, 18f, 20f, 26f, 1f);
    private static readonly Tune Foal = new(new[] { 40f, 40f, 0f, 0f, 0f, -20f, -15f }, 12f, 18f, 14f, 1f);
    private static Tune TuneFor(string family, bool foal) =>
        foal ? Foal : family.Contains("compact", StringComparison.Ordinal) ? CompactAdult : StandardAdult;
    private Tune _tune = StandardAdult;
    private static readonly (string Bone, float Weight)[] AdultDipBones = { ("Spine_Mid", 0.3f), ("Spine_Upper", 0.4f), ("Chest", 0.3f) };
    private static readonly (string Bone, float Weight)[] FoalDipBones = { ("Spine_Base", 0.5f), ("Chest", 0.5f) };

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

    /// <summary>Preview and test hook: hold the graze pose at a fixed weight (0..1) until <see cref="Clear"/>.</summary>
    public void HoldGraze(float weight)
    {
        Calm = true; _master = 1; _grazeT = weight; _grazeLeft = 1e9;
    }

    /// <summary>
    /// Preview and test hook: where the muzzle tip is (owner space: Y above the ground, X forward) with the current pose, found as the
    /// rest-pose vertex furthest forward near the Head bone, carried by that bone.
    /// </summary>
    private Transform3D ComposedPose(int bone)
    {
        var chain = new List<int>();
        for (int b = bone; b >= 0; b = _skeleton!.GetBoneParent(b)) chain.Add(b);
        var global = Transform3D.Identity;
        for (int i = chain.Count - 1; i >= 0; i--) global *= _skeleton!.GetBonePose(chain[i]);
        return global;
    }

    /// <summary>Set to have the modifier measure the muzzle after writing its pose (the skeleton restores poses after each update).</summary>
    public bool MeasureEveryFrame { get; set; }
    private string _muzzle = "not measured";
    public string MeasureMuzzle() => _muzzle;

    private string ComputeMuzzle()
    {
        if (_skeleton == null || _owner == null) return "n/a";
        int head = _skeleton.FindBone("Head");
        if (head < 0) return "no head";
        var toOwner = _owner.GlobalTransform.AffineInverse();
        var restHead = _skeleton.GetBoneGlobalRest(head);
        var headOrigin = toOwner * (_skeleton.GlobalTransform * restHead.Origin);
        Vector3 best = Vector3.Zero; float bestX = float.MinValue;
        var pending = new Stack<Node>(); pending.Push(_owner);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            foreach (var child in node.GetChildren()) pending.Push(child);
            if (node is not MeshInstance3D { Mesh: { } mesh } mi || mi.Name == "Mane") continue;
            var toSkeleton = _skeleton.GlobalTransform.AffineInverse() * mi.GlobalTransform;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                foreach (var v in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    var w = toSkeleton * v;
                    var o = toOwner * (_skeleton.GlobalTransform * w);
                    if ((o - headOrigin).Length() < 0.55f && o.X > bestX) { bestX = o.X; best = w; }
                }
        }
        var local = restHead.AffineInverse() * best;
        var now = toOwner * (_skeleton.GlobalTransform * (ComposedPose(head) * local));
        var rest = toOwner * (_skeleton.GlobalTransform * best);
        string hooves = "";
        foreach (var name in new[] { "Front_L_Hoof_02", "Front_R_Hoof_02" })
        {
            int h = _skeleton.FindBone(name);
            if (h < 0) continue;
            var atRest = toOwner * (_skeleton.GlobalTransform * _skeleton.GetBoneGlobalRest(h).Origin);
            var posed = toOwner * (_skeleton.GlobalTransform * ComposedPose(h).Origin);
            hooves += $" {name} dy={posed.Y - atRest.Y:0.000} dx={posed.X - atRest.X:0.000};";
        }
        return $"muzzle tip height {now.Y:0.000} m, forward {now.X:0.000} m (rest pose {rest.Y:0.000} m, {rest.X:0.000} m);{hooves}";
    }

    public void Setup(HorsePresenter owner, Skeleton3D skeleton, int seed, bool foal, string family = "")
    {
        _owner = owner;
        _skeleton = skeleton;
        _rng = new RandomNumberGenerator { Seed = (ulong)(uint)seed };
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

        _foal = foal;
        _tune = TuneFor(family, foal);
        float total = 0;
        var degrees = _tune.Neck;
        for (int i = 0; i < NeckChain.Length; i++)
        {
            var (name, share) = NeckChain[i];
            int bone = skeleton.FindBone(name);
            if (bone < 0) continue;
            _neck.Add(new Target { Bone = bone, Share = share, GrazeDegrees = degrees[i], Axis = InParent(bone, lateral) });
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

        // Shoulder dip: the spine in front of the withers tips nose-down a few degrees, and the forelegs spread to take the lost height.
        foreach (var (name, weight) in foal ? FoalDipBones : AdultDipBones)
        {
            int bone = skeleton.FindBone(name);
            if (bone >= 0) _dip.Add(new Target { Bone = bone, Share = weight, Axis = InParent(bone, lateral) });
        }
        foreach (var (name, side) in new[] { ("Front_L_Upper", 1f), ("Front_R_Upper", -1f) })
        {
            int bone = skeleton.FindBone(name);
            if (bone >= 0) _legs.Add(new Target { Bone = bone, Share = side, Axis = InParent(bone, lateral) });
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
        float swayK = 1f + Mathf.Sin((float)_grazeClock * 0.9f) * 0.03f * graze;
        foreach (var t in _neck)
        {
            bool headBone = _skeleton.GetBoneName(t.Bone).ToString().StartsWith("Head", StringComparison.Ordinal);
            float degrees = graze * t.GrazeDegrees * swayK - lift * LiftPitchDegrees * master * t.Share + (headBone ? chew : 0f);
            Apply(t, Mathf.DegToRad(degrees * _pitchSign));
        }

        // Shoulder dip and foreleg stance (one foot forward, one back), so the muzzle can reach the grass without tucking the head.
        {
            float dip = _tune.Dip * graze;
            foreach (var t in _dip) Apply(t, Mathf.DegToRad(dip * t.Share * _pitchSign));
            var tune = _tune;
            foreach (var t in _legs)
            {
                float stance = t.Share > 0 ? -tune.LegForward : tune.LegBack; // positive = pitched down/back
                Apply(t, Mathf.DegToRad((-dip * tune.Counter + stance * graze) * _pitchSign));
            }
        }

        float amp = TailSwayDegrees * (0.5f + 1.5f * _gustAmount) * master;
        for (int i = 0; i < _tail.Count; i++)
            Apply(_tail[i], Mathf.DegToRad(amp * (0.5f + 0.18f * i) * Mathf.Sin((float)_tailPhase - 0.6f * i)));

        for (int i = 0; i < _ears.Count; i++)
        {
            float flick = _earLeft > 0 && i == _earSide ? Mathf.Sin(Mathf.Pi * Mathf.Clamp((float)(_earClock / 0.3), 0f, 1f)) : 0f;
            Apply(_ears[i], Mathf.DegToRad(EarFlickDegrees * flick * master * (i == 0 ? 1 : -1) * 0.8f));
        }
        if (MeasureEveryFrame) _muzzle = ComputeMuzzle();
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
        foreach (var list in new[] { _neck, _tail, _ears, _dip, _legs })
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
