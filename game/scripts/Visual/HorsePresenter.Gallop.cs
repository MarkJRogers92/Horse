using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Cloverbrook.Game.Visual;

public partial class HorsePresenter
{
    // D-015: an authored in-place gallop per adult family, exported from Blender as an
    // animation-only GLB next to the checked Walk model (tools/horse/author_gallop.py).
    public const double GallopPeriodSeconds = 16.0 / 24.0;
    public const double GallopSpeedMetersPerSecond = 6.4;
    /// <summary>Show the horse galloping (Walk model + Gallop clip). Falls back to Walk when no gallop exists.</summary>
    public VisualSupport PresentGallop(VisualDescriptor descriptor)
    {
        var support = Present(descriptor, "walk");
        if (!support.Supported || !UsesWalkVariant || Animator == null || Model == null) return support;
        var family = VisualCatalog.Instance.MeshFamilies[descriptor.MeshFamilyId];
        var library = Animator.GetAnimationLibrary("");
        if (!library.HasAnimation("Gallop"))
        {
            using var clip = GallopClip(family.WalkVariant!.Scene);
            if (clip == null) return support with { Limitations = support.Limitations.Append("no authored gallop for this family; walking").ToArray() };
            library.AddAnimation("Gallop", clip);
        }
        Animator.Play("Gallop");
        CurrentAnimation = "Gallop";
        LocomotionSpeedMetersPerSecond = GallopSpeedMetersPerSecond * Model.Scale.X;
        LocomotionPeriodSeconds = GallopPeriodSeconds;
        return support;
    }

    // C# wrappers are disposed as soon as the clip is in the model's library, so no engine
    // resource is kept alive by managed references after the scene is freed.
    private Animation? GallopClip(string walkScene)
    {
        var path = walkScene.Replace("_walk.glb", "_gallop.glb", StringComparison.Ordinal);
        if (path == walkScene || !ResourceLoader.Exists(path)) return null;
        using var packed = GD.Load<PackedScene>(path);
        var instance = packed.Instantiate<Node>();
        try
        {
            var player = FindAll<AnimationPlayer>(instance).FirstOrDefault();
            if (player == null || !player.HasAnimation("Gallop")) return null;
            using var source = player.GetAnimation("Gallop");
            var clip = (Animation)source.Duplicate();
            clip.LoopMode = Animation.LoopModeEnum.Linear;
            var root = Animator!.GetNode(Animator.RootNode);
            var missing = Enumerable.Range(0, clip.GetTrackCount()).Count(i => root.GetNodeOrNull(clip.TrackGetPath(i).GetConcatenatedNames()) == null);
            if (missing == 0) return clip;
            GD.PushWarning($"[HorsePresenter] gallop {path}: {missing} tracks do not match the Walk model; not used");
            clip.Dispose();
            return null;
        }
        finally { instance.Free(); }
    }
}
