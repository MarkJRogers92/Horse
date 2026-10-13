using System;
using System.Linq;
using Godot;

namespace Cloverbrook.Game.Visual;

public partial class HorsePresenter
{
    // A family's Idle and Walk scenes have identical corrected rest surfaces but different rigs
    // (checked for the foal and both adults). Never blend unrelated bone indices, and never play
    // Walk with body morphs. The name is historical: the bridge works for every age.
    public const double FoalTransitionHalfSeconds = 0.28;
    public bool IsTransitioning { get; private set; }
    public bool PreserveRoutePlacement { get; private set; }
    public string RequestedAnimation => _transitionAnimation ?? CurrentAnimation;

    private VisualDescriptor? _transitionDescriptor;
    private string? _transitionAnimation;
    private ShapePose? _transitionFrom, _transitionTo;
    private double _transitionElapsed;
    private bool _transitionIncoming;
    private double _resumePace = 1; // pace the incoming clip starts at (see SetPace)

    /// <summary>
    /// Optional interactive Idle/Walk bridge for foals and adults. Direct Present stays immediate.
    /// Repeated requests rebase from the currently visible shape, never a saved
    /// original mesh. Other horses/appearances cancel the bridge and present normally.
    /// </summary>
    public VisualSupport TransitionTo(VisualDescriptor descriptor, string animation)
    {
        var catalog = VisualCatalog.Instance;
        var support = catalog.Check(descriptor);
        bool wantsWalk = IsWalkRequest(animation);
        bool eligible = support.Supported && Support?.Supported == true && Model != null &&
            Descriptor != null && Descriptor.IsFoal == descriptor.IsFoal && SameAppearance(Descriptor, descriptor) &&
            catalog.MeshFamilies.TryGetValue(descriptor.MeshFamilyId, out var family) && family.WalkVariant != null &&
            (!wantsWalk || CanWalkWithShape(descriptor, family, catalog)) &&
            (wantsWalk || string.Equals(animation, "idle", StringComparison.OrdinalIgnoreCase)) &&
            (IsTransitioning || wantsWalk != UsesWalkVariant);
        if (!eligible) return Present(descriptor, animation);

        if (IsTransitioning && _transitionDescriptor != null &&
            SameAppearance(_transitionDescriptor, descriptor) && SameWeights(_transitionDescriptor, descriptor) &&
            string.Equals(_transitionAnimation, animation, StringComparison.OrdinalIgnoreCase)) return support;

        Animator?.Pause();
        // During reversals retain the unlifted origin; clearance is recomputed
        // from this exact visible pose rather than treating its lift as a new base.
        if (!IsTransitioning) PrepareTransitionFloor();
        _transitionFrom = CaptureShapePose();
        // D-013: shaped horses bridge too. The swap still passes through the neutral shape, the only
        // state where the Idle and Walk skins are measured identical (their body-shape deltas differ
        // by up to ~36 mm), so a shaped horse eases to neutral and back instead of popping.
        _transitionTo = NeutralShapePose();
        _transitionDescriptor = descriptor;
        _transitionAnimation = animation;
        _transitionElapsed = 0;
        _transitionIncoming = false;
        _resumePace = 1;
        IsTransitioning = true;
        PreserveRoutePlacement = true;
        // Keep both animation and locomotion stopped through the entire bridge,
        // including the incoming neutral-to-frame-zero pose interpolation.
        LocomotionSpeedMetersPerSecond = 0;
        LocomotionPeriodSeconds = 0;
        return support;
    }

    public override void _Process(double delta)
    {
        if (!IsTransitioning || _transitionFrom == null || _transitionTo == null ||
            Model == null || !double.IsFinite(delta) || delta <= 0) return;
        _transitionElapsed = Math.Min(FoalTransitionHalfSeconds, _transitionElapsed + delta);
        float t = (float)(_transitionElapsed / FoalTransitionHalfSeconds);
        ApplyShapePose(_transitionFrom, _transitionTo, t * t * (3f - 2f * t));
        if (_transitionElapsed < FoalTransitionHalfSeconds) return;

        if (!_transitionIncoming)
        {
            // Outgoing rig is now exactly at bind rest, zero morphs and unit scale.
            // PresentModel emits while IsTransitioning is true, so Stage preserves
            // root/camera placement. No frame can render the newly loaded posed mesh.
            var descriptor = _transitionDescriptor!;
            string animation = _transitionAnimation!;
            var support = PresentModel(descriptor, animation);
            if (!support.Supported || Model == null)
            {
                CancelFoalTransition();
                return;
            }
            if (Animator != null && !string.IsNullOrEmpty(CurrentAnimation))
            {
                Animator.Seek(0, true);
                Animator.Pause();
            }
            _transitionTo = CaptureShapePose();
            _transitionFrom = NeutralShapePose();
            PrepareTransitionFloor();
            ApplyShapePose(_transitionFrom, _transitionFrom, 0);
            LocomotionSpeedMetersPerSecond = 0;
            LocomotionPeriodSeconds = 0;
            _transitionIncoming = true;
            _transitionElapsed = 0;
            return;
        }

        string resume = _transitionAnimation!;
        double resumePace = _resumePace;
        ClearTransitionState();
        // All intended morphs/height are installed before motion is allowed.
        ApplyAnimation(resume, resumePace);
    }

    private void CancelFoalTransition()
    {
        ClearTransitionState();
        PreserveRoutePlacement = false;
    }

    private void ClearTransitionState()
    {
        IsTransitioning = false;
        _transitionDescriptor = null;
        _transitionAnimation = null;
        _transitionFrom = _transitionTo = null;
        _transitionElapsed = 0;
        _transitionIncoming = false;
        _resumePace = 1;
        _transitionFloor = null;
    }

    private static bool SameAppearance(VisualDescriptor a, VisualDescriptor b) =>
        // Compare collection contents too, so equivalent freshly resolved domain
        // descriptors do not accidentally bypass the bridge. CacheKey is derived
        // from appearance, including the morph values we intentionally interpolate.
        a.RequiredCoatClasses.SequenceEqual(b.RequiredCoatClasses) && a.CoatLayers.SequenceEqual(b.CoatLayers) &&
        a with { MorphWeights = b.MorphWeights, RequiredCoatClasses = b.RequiredCoatClasses,
            CoatLayers = b.CoatLayers, CacheKey = b.CacheKey } == b;

    private static bool SameWeights(VisualDescriptor a, VisualDescriptor b) =>
        a.MorphWeights.Count == b.MorphWeights.Count && a.MorphWeights.All(pair =>
            b.MorphWeights.TryGetValue(pair.Key, out int weight) && weight == pair.Value);

    private sealed record BonePose(Vector3 Position, Quaternion Rotation, Vector3 Scale);
    private sealed record RigPose(Skeleton3D Skeleton, BonePose[] Bones);
    private sealed record MorphPose(MeshInstance3D Mesh, float[] Values);
    private sealed record ShapePose(RigPose[] Rigs, MorphPose[] Morphs, Vector3 Scale, Vector3? HeadTarget);

    private ShapePose CaptureShapePose() => new(
        FindAll<Skeleton3D>(Model!).Select(skeleton => new RigPose(skeleton,
            Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => new BonePose(
                skeleton.GetBonePosePosition(i), skeleton.GetBonePoseRotation(i), skeleton.GetBonePoseScale(i))).ToArray())).ToArray(),
        FindAll<MeshInstance3D>(Model!).Select(mesh => new MorphPose(mesh,
            Enumerable.Range(0, mesh.GetBlendShapeCount()).Select(mesh.GetBlendShapeValue).ToArray())).ToArray(),
        Model!.Scale, HeadViewTarget);

    private ShapePose NeutralShapePose() => new(
        FindAll<Skeleton3D>(Model!).Select(skeleton => new RigPose(skeleton,
            Enumerable.Range(0, skeleton.GetBoneCount()).Select(i =>
            {
                var rest = skeleton.GetBoneRest(i);
                return new BonePose(rest.Origin, rest.Basis.GetRotationQuaternion(), rest.Basis.Scale);
            }).ToArray())).ToArray(),
        FindAll<MeshInstance3D>(Model!).Select(mesh => new MorphPose(mesh, new float[mesh.GetBlendShapeCount()])).ToArray(),
        Vector3.One, VisualCatalog.Instance.MeshFamilies[Descriptor!.MeshFamilyId].HeadViewTarget);

    private void ApplyShapePose(ShapePose from, ShapePose to, float weight)
    {
        for (int r = 0; r < from.Rigs.Length; r++)
        {
            var rig = from.Rigs[r];
            for (int i = 0; i < rig.Bones.Length; i++)
            {
                var a = rig.Bones[i];
                var b = to.Rigs[r].Bones[i];
                rig.Skeleton.SetBonePosePosition(i, a.Position.Lerp(b.Position, weight));
                rig.Skeleton.SetBonePoseRotation(i, a.Rotation.Slerp(b.Rotation, weight));
                rig.Skeleton.SetBonePoseScale(i, a.Scale.Lerp(b.Scale, weight));
            }
        }
        for (int m = 0; m < from.Morphs.Length; m++)
            for (int i = 0; i < from.Morphs[m].Values.Length; i++)
                from.Morphs[m].Mesh.SetBlendShapeValue(i, Mathf.Lerp(from.Morphs[m].Values[i], to.Morphs[m].Values[i], weight));
        Model!.Scale = from.Scale.Lerp(to.Scale, weight);
        if (from.HeadTarget.HasValue && to.HeadTarget.HasValue)
            HeadViewTarget = from.HeadTarget.Value.Lerp(to.HeadTarget.Value, weight);
        KeepTransitionAboveFloor();
    }
}
