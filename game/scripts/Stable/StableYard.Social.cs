using System;
using System.Collections.Generic;
using Cloverbrook.Domain;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.Stable;

public partial class StableYard
{
    // D-041: ephemeral presentation state only. At most six horses; all searches are bounded by Slots.Length.
    private sealed class SocialState
    {
        public double GreetingAfter, ApproachAfter;
        public string? Companion;
        public bool Joining, GrazePending, ApproachPending;
        public int WanderChoices;
    }
    private readonly Dictionary<string, SocialState> _social = new(StringComparer.Ordinal);
    private double _socialClock;
    public int GreetingCount { get; private set; }
    public int CompanionVisitCount { get; private set; }
    public int YieldCount { get; private set; }

    private SocialState SocialFor(string key)
    {
        if (!_social.TryGetValue(key, out var state))
            _social[key] = state = new SocialState { GreetingAfter = _time + 4 + (uint)StableSeed(key) % 11 };
        return state;
    }

    private string TraitFor(string key) => _emotes.TryGetValue(key, out var emote) ? emote.Trait : Personality.Dreamy;

    /// <summary>Readable deterministic reaction profile for the preview/probe, not a saved relationship or reward.</summary>
    public string SocialReactionFor(string key) => TraitFor(key) switch
    {
        Personality.Cuddly => "warm glance", Personality.Cheeky => "playful nod", Personality.Shy => "timid peek",
        Personality.Greedy => "hopeful sniff", Personality.Brave => "proud greeting", _ => "unhurried glance",
    };

    private void ReactToApproach(string key)
    {
        if (!Wandering || PhotoMode) return;
        var state = SocialFor(key);
        if (_time < state.ApproachAfter) return;
        state.ApproachPending = true; // a moving horse answers after its normal stop bridge
        state.ApproachAfter = _time + 9;
    }

    private void Gesture(string key, HorsePresenter presenter, Vector3 target)
    {
        if (presenter.IdleLife is not { } life || presenter.IsTransitioning) return;
        var local = presenter.GlobalBasis.Inverse() * (target - presenter.GlobalPosition);
        var trait = TraitFor(key);
        float lift = trait switch { Personality.Brave => 9, Personality.Cuddly => 4, Personality.Shy => -2, Personality.Greedy => -4, _ => 2 };
        float nod = trait switch { Personality.Cheeky => 3.5f, Personality.Greedy => 1.8f, Personality.Cuddly => 0.8f, _ => 0 };
        double delay = trait switch { Personality.Dreamy => 0.8, Personality.Shy => 0.45, _ => 0 };
        life.Attend(local, lift, nod, trait == Personality.Dreamy ? 3.4 : 2.3, delay, trait == Personality.Shy);
        if (_wander.TryGetValue(presenter, out var w)) w.RestLeft = Math.Max(w.RestLeft, 4.5 + delay);
    }

    private void UpdateSocial(double delta)
    {
        _socialClock -= delta;
        if (_socialClock > 0 || PhotoMode) return;
        _socialClock = 0.35;
        foreach (var (key, horse) in _horses)
        {
            var state = SocialFor(key);
            if (!IsResting(horse) || horse.IdleLife == null) continue;
            if (state.GrazePending)
            {
                state.GrazePending = false;
                if (_weather != YardWeather.Rain)
                {
                    horse.IdleLife.NextGraze = Math.Min(horse.IdleLife.NextGraze, 0.8);
                    if (state.Companion != null && MareOf(state.Companion) is { IdleLife: { } companionLife } companion && IsResting(companion))
                    {
                        companionLife.NextGraze = Math.Min(companionLife.NextGraze, 2.5);
                        if (_wander.TryGetValue(companion, out var companionRest)) companionRest.RestLeft = Math.Max(companionRest.RestLeft, 12);
                    }
                }
            }
            if (state.ApproachPending)
            {
                state.ApproachPending = false;
                if (key == _hoverKey || key == _focusedKey) Gesture(key, horse, _camera.GlobalPosition);
                continue;
            }
            if (key == _focusedKey || _time < state.GreetingAfter || horse.IdleLife.Busy) continue;
            HorsePresenter? neighbour = null; string? neighbourKey = null; float best = 3.5f;
            foreach (var (otherKey, other) in _horses)
            {
                if (other == horse || otherKey == _focusedKey || !IsResting(other) || other.IdleLife is not { Busy: false }) continue;
                float distance = horse.Position.DistanceTo(other.Position);
                if (distance < best && _time >= SocialFor(otherKey).GreetingAfter)
                { best = distance; neighbour = other; neighbourKey = otherKey; }
            }
            if (neighbour == null || neighbourKey == null) continue;
            Gesture(key, horse, neighbour.GlobalPosition);
            Gesture(neighbourKey, neighbour, horse.GlobalPosition);
            state.GreetingAfter = _time + 24 + (uint)StableSeed(key) % 15;
            var otherState = SocialFor(neighbourKey);
            otherState.GreetingAfter = _time + 27 + (uint)StableSeed(neighbourKey) % 15;
            if (state.Companion == null || MareOf(state.Companion) == null) state.Companion = neighbourKey;
            if (otherState.Companion == null || MareOf(otherState.Companion) == null) otherState.Companion = key;
            GreetingCount++;
        }
    }

    private double RestDuration(string key, float min, float max)
    {
        float factor = TraitFor(key) switch { Personality.Dreamy => 1.35f, Personality.Greedy => 1.2f, Personality.Cheeky => 0.85f, _ => 1f };
        return _rng.RandfRange(min, max) * factor;
    }

    private Vector3? TryCompanionSpot(HorsePresenter horse, string key)
    {
        var state = SocialFor(key);
        state.Joining = false;
        if (++state.WanderChoices % 3 != 0 || state.Companion == null) return null;
        var companion = MareOf(state.Companion);
        if (companion == null || !IsResting(companion)) return null;
        float spacing = TraitFor(key) == Personality.Shy ? 2.35f : 1.95f;
        // Side-by-side company, rather than a nose-to-nose paired animation.
        for (int side = -1; side <= 1; side += 2)
        {
            var point = ClampToPaddock(companion.Position + companion.Basis.Z.Normalized() * spacing * side);
            if (point.DistanceTo(horse.Position) < 0.5f || point.DistanceTo(companion.Position) < 1.65f) continue;
            bool clear = true;
            foreach (var (_, other) in _horses)
            {
                if (other == horse) continue;
                if (BodyGap(horse, point, companion.Rotation.Y, other, other.Position, other.Rotation.Y) < 0.2f)
                { clear = false; break; }
                if (_wander.TryGetValue(other, out var w) && w.Target is { } reserved && reserved.DistanceTo(point) < 1.7f)
                { clear = false; break; }
            }
            if (!clear) continue;
            state.Joining = true;
            return point;
        }
        return null;
    }

    private void OnSocialArrival(string key, HorsePresenter horse)
    {
        var state = SocialFor(key);
        if (!state.Joining) return;
        state.Joining = false;
        state.GrazePending = true;
        CompanionVisitCount++;
        // The Idle modifier is created after the bridge, so keep the intent as a small rest-time schedule.
        if (_wander.TryGetValue(horse, out var w)) w.RestLeft = Math.Max(w.RestLeft, 14);
        state.GreetingAfter = Math.Max(state.GreetingAfter, _time + 14);
    }

    private Vector3 SteerAroundHorses(HorsePresenter horse, Vector3 desired)
    {
        var steered = desired;
        var side = new Vector3(desired.Z, 0, -desired.X);
        foreach (var (_, other) in _horses)
        {
            if (horse == other) continue;
            var gap = other.Position - horse.Position; gap.Y = 0;
            float ahead = gap.Dot(desired), lateral = gap.Dot(side);
            if (ahead <= 0 || ahead > 2.8f || Math.Abs(lateral) > 1.35f) continue;
            float strength = (1f - ahead / 2.8f) * (1f - Math.Abs(lateral) / 1.35f);
            // A consistent right-hand pass breaks head-on symmetry without random per-frame jitter.
            steered += side * (lateral > 0.15f ? -1 : 1) * strength * 2.2f;
        }
        return steered.Normalized();
    }

    private void Yield(HorsePresenter horse, Wanderer state, string key)
    {
        if (state.Walking) StopWalking(horse, state);
        state.Target = null;
        state.RestLeft = 1.4 + (uint)StableSeed(key) % 5 * 0.3;
        SocialFor(key).Joining = false;
        YieldCount++;
    }

    // Conservative horizontal body capsules: includes the neck/head, scales down for foals and with height.
    private static float BodyScale(HorsePresenter horse)
    {
        var descriptor = horse.Descriptor;
        float height = 1f + (descriptor?.MorphWeights.TryGetValue("height", out var h) == true ? h : 0) * VisualCatalog.Instance.HeightScalePerUnit;
        // Reserve the larger of the horse's intended size and the neutral bridge.
        // Reading Model.Scale here lets a stationary bridge grow into its neighbour.
        return (descriptor?.IsFoal == true ? 0.66f : 1f) * Math.Max(1f, height);
    }

    public float HorseClearance(HorsePresenter a, HorsePresenter b) => BodyGap(a, a.Position, a.Rotation.Y, b, b.Position, b.Rotation.Y);

    private static float BodyGap(HorsePresenter a, Vector3 aPosition, float aYaw, HorsePresenter b, Vector3 bPosition, float bYaw)
    {
        float sa = BodyScale(a), sb = BodyScale(b);
        var ac = new Vector2(aPosition.X, aPosition.Z); var bc = new Vector2(bPosition.X, bPosition.Z);
        var alongA = new Vector2(Mathf.Cos(aYaw), -Mathf.Sin(aYaw)) * (0.7f * sa);
        var alongB = new Vector2(Mathf.Cos(bYaw), -Mathf.Sin(bYaw)) * (0.7f * sb);
        return SegmentGap(ac - alongA, ac + alongA, bc - alongB, bc + alongB) - 0.48f * (sa + sb);
    }

    private bool ClearStep(HorsePresenter horse, Vector3 position, float yaw)
    {
        foreach (var (_, other) in _horses)
        {
            if (other == horse) continue;
            float before = HorseClearance(horse, other);
            float after = BodyGap(horse, position, yaw, other, other.Position, other.Rotation.Y);
            // Slot/shape changes may begin close together. Allow only motion that improves that existing overlap.
            if (after < -0.00001f && after < before - 0.00001f) return false;
            // Sample the midpoint as well as the destination to guard ordinary frame-sized crossing steps.
            var midpoint = horse.Position.Lerp(position, 0.5f);
            float midYaw = Mathf.LerpAngle(horse.Rotation.Y, yaw, 0.5f);
            float middle = BodyGap(horse, midpoint, midYaw, other, other.Position, other.Rotation.Y);
            if (middle < -0.00001f && middle < before - 0.00001f) return false;
        }
        return true;
    }

    /// <summary>Deterministic 2D segment distance, including crossing, parallel and degenerate segments.</summary>
    public static float SegmentGap(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var ab = b - a; var cd = d - c;
        float cross = ab.Cross(cd);
        if (Math.Abs(cross) > 1e-7f)
        {
            float t = (c - a).Cross(cd) / cross, u = (c - a).Cross(ab) / cross;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return 0;
        }
        static float PointGap(Vector2 p, Vector2 x, Vector2 y)
        {
            var v = y - x;
            return p.DistanceTo(x + v * (v.LengthSquared() < 1e-8f ? 0 : Mathf.Clamp((p - x).Dot(v) / v.LengthSquared(), 0, 1)));
        }
        return Math.Min(Math.Min(PointGap(a, c, d), PointGap(b, c, d)), Math.Min(PointGap(c, a, b), PointGap(d, a, b)));
    }
}
