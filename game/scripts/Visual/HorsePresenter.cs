using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// The one horse presenter. Every venue (viewer, stable, capture, future portraits)
/// shows a horse by calling <see cref="Present"/> with a descriptor. It never reads
/// genetics and never substitutes a coat: unsupported descriptors render nothing and
/// report why.
/// </summary>
public partial class HorsePresenter : Node3D
{
    private static readonly Shader CoatShader = GD.Load<Shader>("res://shaders/horse_coat.gdshader");
    private static readonly Shader EyeShader = GD.Load<Shader>("res://shaders/horse_eye.gdshader");
    private static readonly Shader HairShader = GD.Load<Shader>("res://shaders/horse_hair.gdshader");
    private static readonly Dictionary<string, PackedScene> SceneCache = new();
    private static readonly Dictionary<string, bool> SceneAnimationAvailabilityCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> SignedDetailImportValidation = new(StringComparer.Ordinal);

    [Signal] public delegate void PresentedEventHandler(string summary, bool supported);

    public VisualDescriptor? Descriptor { get; private set; }
    public VisualSupport? Support { get; private set; }
    public Markings? Markings { get; private set; }
    public Node3D? Model { get; private set; }
    public AnimationPlayer? Animator { get; private set; }
    public string CurrentAnimation { get; private set; } = "";
    public Vector3? HeadViewTarget { get; private set; }
    public double LocomotionSpeedMetersPerSecond { get; private set; }
    public double LocomotionPeriodSeconds { get; private set; }
    public bool UsesWalkVariant { get; private set; }

    /// <summary>
    /// Playback pace of the current clip (1 = the checked clip speed). A caller that moves the
    /// horse slower than <see cref="LocomotionSpeedMetersPerSecond"/> must set the same pace so
    /// the stance feet keep matching the ground instead of sliding.
    /// </summary>
    public double Pace { get; private set; } = 1;

    /// <summary>Ground speed that matches the playing clip at the current <see cref="Pace"/>.</summary>
    public double EffectiveSpeedMetersPerSecond => LocomotionSpeedMetersPerSecond * Pace;

    public VisualSupport Present(VisualDescriptor descriptor, string animation = "idle")
    {
        CancelFoalTransition();
        return PresentModel(descriptor, animation);
    }

    private VisualSupport PresentModel(VisualDescriptor descriptor, string animation)
    {
        var catalog = VisualCatalog.Instance;
        Descriptor = descriptor;
        Support = catalog.Check(descriptor);
        ClearModel();
        if (!Support.Supported)
        {
            GD.PushWarning($"[HorsePresenter] {descriptor.FixtureId}: {Support.Summary}");
            EmitSignal(SignalName.Presented, Support.Summary, false);
            return Support;
        }

        var family = catalog.MeshFamilies[descriptor.MeshFamilyId];
        bool requestedWalk = IsWalkRequest(animation);
        bool useWalkVariant = requestedWalk && family.WalkVariant != null;
        if (useWalkVariant && !CanWalkWithShape(descriptor, family, catalog))
        {
            Support = Support with
            {
                Supported = false,
                Problems = Support.Problems.Append("Walk requires a checked family Walk with the declared body channels").ToArray()
            };
            GD.PushWarning($"[HorsePresenter] {descriptor.FixtureId}: {Support.Summary}");
            EmitSignal(SignalName.Presented, Support.Summary, false);
            return Support;
        }

        string scenePath = useWalkVariant ? family.WalkVariant!.Scene : family.Scene;
        if (!SceneCache.TryGetValue(scenePath, out var packed))
            SceneCache[scenePath] = packed = GD.Load<PackedScene>(scenePath);
        Model = packed.Instantiate<Node3D>();
        Model.Name = "Model";
        AddChild(Model);

        var meshGroups = FindAll<MeshInstance3D>(Model)
            .GroupBy(mesh => mesh.Name.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var familyMorphShapes = catalog.FamilyMeshBlendShapes[descriptor.MeshFamilyId];
        // Every declared component/channel pair is an explicit family asset
        // contract. It applies equally to juvenile masters; legacy families with
        // an empty map intentionally leave those descriptor controls unapplied.
        if (familyMorphShapes.Count > 0)
        {
            var missing = familyMorphShapes
                .Where(pair => pair.Value.Count > 0)
                .SelectMany(pair =>
                {
                    if (!meshGroups.TryGetValue(pair.Key, out var matches) || matches.Length != 1)
                        return new[] { $"{pair.Key} component expected exactly once; found {(matches?.Length ?? 0)}" };
                    var mesh = matches[0];
                    return pair.Value.Values.Where(shape => mesh.Mesh == null || !HasBlendShape(mesh, shape))
                        .Select(shape => $"{pair.Key}.{shape}");
                })
                .ToArray();
            if (missing.Length > 0)
            {
                Support = Support with
                {
                    Supported = false,
                    Problems = Support.Problems.Append("mesh family is missing declared component blend shapes: " + string.Join(", ", missing)).ToArray()
                };
                ClearModel();
                GD.PushWarning($"[HorsePresenter] {descriptor.FixtureId}: {Support.Summary}");
                EmitSignal(SignalName.Presented, Support.Summary, false);
                return Support;
            }
        }

        var coat = catalog.Coats[descriptor.CoatRecipe];
        var hair = catalog.Hair[descriptor.HairRecipe];
        var skin = catalog.Skin[descriptor.SkinRecipe];
        var eye = catalog.Eyes[descriptor.EyeRecipe];
        Markings = Markings.FromKey(descriptor.MarkingRealizationKey);
        var signedDetailMetadata = useWalkVariant ? family.WalkVariant!.BodySignedDetail : family.BodySignedDetail;
        Texture2D? bodySignedDetail = signedDetailMetadata is { } detail
            ? GD.Load<Texture2D>(detail.Path)
            : null;
        if (bodySignedDetail != null && signedDetailMetadata is { } importedDetailMetadata &&
            !ValidateImportedSignedDetail(bodySignedDetail, importedDetailMetadata))
            bodySignedDetail = null;
        if (signedDetailMetadata != null && bodySignedDetail == null)
            Support = Support with { Limitations = Support.Limitations.Append("body signed detail disabled: atlas could not be loaded losslessly or its imported pixels did not match provenance").ToArray() };

        var coatMat = new ShaderMaterial { Shader = CoatShader };
        coatMat.SetShaderParameter("body_color", coat.Body);
        coatMat.SetShaderParameter("points_color", coat.Points);
        coatMat.SetShaderParameter("points_amount", coat.PointsAmount);
        coatMat.SetShaderParameter("counter_color", coat.Counter);
        coatMat.SetShaderParameter("primitive_color", coat.Primitive ?? coat.Points);
        coatMat.SetShaderParameter("primitive_strength", descriptor.CoatLayers.Contains("primitive_markings") ? descriptor.PrimitiveMarkingStrength / 10000f : 0f);
        coatMat.SetShaderParameter("skin_color", skin.Color);
        coatMat.SetShaderParameter("nose_amount", skin.NoseAmount);
        coatMat.SetShaderParameter("white_color", catalog.White);
        coatMat.SetShaderParameter("pink_skin", catalog.PinkSkin);
        coatMat.SetShaderParameter("hoof_dark", catalog.HoofDark);
        coatMat.SetShaderParameter("hoof_pale", catalog.HoofPale);
        coatMat.SetShaderParameter("sooty", coat.Sooty);
        coatMat.SetShaderParameter("dapple", descriptor.CoatLayers.Contains("shading_modifiers") ? coat.Dapple : 0f);
        coatMat.SetShaderParameter("sheen", coat.Sheen);
        // Data atlas is intentionally opt-in per family and remains off when its
        // metadata is absent, invalid, stale, or fails Godot's texture import.
        coatMat.SetShaderParameter("use_signed_detail", false);
        coatMat.SetShaderParameter("signed_detail_strength", 0f);
        if (bodySignedDetail != null && signedDetailMetadata is { } signedDetail)
        {
            coatMat.SetShaderParameter("signed_detail_map", bodySignedDetail);
            coatMat.SetShaderParameter("use_signed_detail", true);
            coatMat.SetShaderParameter("signed_detail_strength", signedDetail.Strength);
        }
        bool whiteMarkings = descriptor.CoatLayers.Contains("individual_white_markings");
        coatMat.SetShaderParameter("face_type", whiteMarkings ? (int)Markings.Face : 0);
        coatMat.SetShaderParameter("face_params", new Vector4(Markings.StarSize, Markings.StripWidth, Markings.BlazeWidth, Markings.SnipSize));
        coatMat.SetShaderParameter("face_offset", Markings.FaceOffset);
        var legs = whiteMarkings ? Markings.LegHeights : new float[4];
        coatMat.SetShaderParameter("leg_white", new Vector4(legs[0], legs[1], legs[2], legs[3]));
        coatMat.SetShaderParameter("edge_seed", Markings.EdgeSeed);

        var eyeMat = new ShaderMaterial { Shader = EyeShader };
        eyeMat.SetShaderParameter("iris", eye.Iris);
        eyeMat.SetShaderParameter("ring", eye.Ring);

        var hairMat = new ShaderMaterial { Shader = HairShader };
        hairMat.SetShaderParameter("hair_color", hair.Color);
        hairMat.SetShaderParameter("tip_color", hair.Tip);
        hairMat.SetShaderParameter("seed", Markings.EdgeSeed);
        hairMat.SetShaderParameter("solid_clumps", family.HairGeometry == "solid_clumps");

        foreach (var mesh in FindAll<MeshInstance3D>(Model))
        {
            if (mesh.Name == "Body")
                for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                    if (mesh.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D { NormalEnabled: true, NormalTexture: not null } source)
                    {
                        coatMat.SetShaderParameter("coat_normal", source.NormalTexture);
                        coatMat.SetShaderParameter("use_coat_normal", true);
                        coatMat.SetShaderParameter("coat_normal_depth", source.NormalScale);
                        break;
                    }
            var mat = mesh.Name.ToString() switch
            {
                "Body" => coatMat,
                "Eyes" => eyeMat,
                "Mane" or "Tail" => (Material)hairMat,
                _ => null,
            };
            if (mat is null) continue;
            for (int s = 0; s < mesh.GetSurfaceOverrideMaterialCount(); s++) mesh.SetSurfaceOverrideMaterial(s, mat);
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        }

        // Height is retained as whole-model scale from the pre-existing descriptor behavior.
        float height = 1f + (descriptor.MorphWeights.TryGetValue("height", out var h) ? h : 0) * catalog.HeightScalePerUnit;
        Model.Scale = Vector3.One * height;
        HeadViewTarget = family.HeadViewTarget * height;

        foreach (var (component, channels) in familyMorphShapes)
        {
            if (!meshGroups.TryGetValue(component, out var matches) || matches.Length != 1) continue;
            var mesh = matches[0];
            foreach (var (morph, shape) in channels)
            {
                float normalized = descriptor.MorphWeights.TryGetValue(morph, out var weight)
                    ? Mathf.Clamp(weight / catalog.BodyBlendShapeNormalization, -1f, 1f)
                    : 0f;
                mesh.SetBlendShapeValue(mesh.FindBlendShapeByName(shape), normalized);
            }
        }

        Animator = FindAll<AnimationPlayer>(Model).FirstOrDefault();
        if (Animator != null)
            foreach (var name in Animator.GetAnimationList())
                Animator.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        UsesWalkVariant = useWalkVariant;
        LocomotionSpeedMetersPerSecond = 0;
        LocomotionPeriodSeconds = 0;
        ApplyAnimation(animation);
        if (!string.IsNullOrEmpty(animation) && string.IsNullOrEmpty(ResolveAnimation(animation)))
            Support = Support with { Limitations = Support.Limitations.Append($"animation '{animation}' unavailable; static pose").ToArray() };
        EmitSignal(SignalName.Presented, Support.Summary, true);
        return Support;
    }

    private static bool ValidateImportedSignedDetail(Texture2D texture, BodySignedDetail metadata)
    {
        string key = metadata.Path + "|" + metadata.PixelSha256;
        if (SignedDetailImportValidation.TryGetValue(key, out bool cached)) return cached;
        var image = texture.GetImage();
        bool valid = image.GetWidth() == 2048 && image.GetHeight() == 2048 && image.GetFormat() == Image.Format.Rgb8 &&
                     string.Equals(Convert.ToHexString(SHA256.HashData(image.GetData())).ToLowerInvariant(), metadata.PixelSha256, StringComparison.Ordinal);
        SignedDetailImportValidation[key] = valid;
        return valid;
    }

    public void Play(string animation)
    {
        if (IsTransitioning && _transitionDescriptor != null)
        {
            // Explicit playback APIs retain their immediate behavior, but must
            // apply/validate the complete intended shape before resuming a clip.
            Present(_transitionDescriptor, animation);
            return;
        }
        CancelFoalTransition();
        if (Descriptor != null && Support?.Supported == true &&
            VisualCatalog.Instance.MeshFamilies.TryGetValue(Descriptor.MeshFamilyId, out var family))
        {
            bool wantsWalkVariant = IsWalkRequest(animation) && family.WalkVariant != null;
            if (wantsWalkVariant != UsesWalkVariant)
            {
                Present(Descriptor, animation);
                return;
            }
        }
        ApplyAnimation(animation);
    }

    /// <summary>
    /// Scale the playing clip's speed (clamped to 0.05 to 2). See <see cref="Pace"/>. Called while an
    /// Idle/Walk bridge is running, it sets the pace the incoming clip starts at, so its first frame
    /// already matches the ground speed the caller will move at.
    /// </summary>
    public void SetPace(double pace)
    {
        pace = double.IsFinite(pace) ? Math.Clamp(pace, 0.05, 2.0) : 1.0;
        if (IsTransitioning) { _resumePace = pace; return; }
        Pace = pace;
        if (Animator != null) Animator.SpeedScale = (float)Pace;
    }

    /// <summary>
    /// True when this exact descriptor may show the checked Walk (D-013: any body shape inside the
    /// declared channels, see <see cref="CanWalkWithShape"/>). The one gate callers ask before Walk,
    /// so they can choose Walk or a stand-in without first loading (and possibly clearing) a model.
    /// </summary>
    public bool CanWalk(VisualDescriptor descriptor)
    {
        var catalog = VisualCatalog.Instance;
        return catalog.Check(descriptor).Supported &&
               catalog.MeshFamilies.TryGetValue(descriptor.MeshFamilyId, out var family) &&
               CanWalkWithShape(descriptor, family, catalog);
    }

    private void ApplyAnimation(string animation, double pace = 1)
    {
        CurrentAnimation = ResolveAnimation(animation) ?? "";
        Pace = pace;
        if (Animator == null) return;
        Animator.SpeedScale = (float)pace;
        if (string.IsNullOrEmpty(CurrentAnimation))
        {
            // A missing clip leaves the bind pose; say so loudly instead of failing silently.
            if (!string.IsNullOrEmpty(animation))
                GD.PushWarning($"[HorsePresenter] {Descriptor?.FixtureId}: animation '{animation}' not found in {string.Join(", ", Animator.GetAnimationList())}; showing the static pose");
            Animator.Stop();
            return;
        }
        Animator.Play(CurrentAnimation);
        if (UsesWalkVariant && string.Equals(CurrentAnimation, "Walk", StringComparison.OrdinalIgnoreCase) &&
            Descriptor != null && VisualCatalog.Instance.MeshFamilies.TryGetValue(Descriptor.MeshFamilyId, out var family) && family.WalkVariant is { } walk)
        {
            LocomotionSpeedMetersPerSecond = walk.ForwardSpeedMetersPerSecond * (Model?.Scale.X ?? 1f);
            LocomotionPeriodSeconds = (double)walk.PeriodFrames / walk.FramesPerSecond;
        }
        else
        {
            LocomotionSpeedMetersPerSecond = 0;
            LocomotionPeriodSeconds = 0;
        }
    }

    /// <summary>Returns true when the current clip resolves or the checked family Walk variant can be selected.</summary>
    public bool IsAnimationAvailable(string animation)
    {
        if (!string.IsNullOrEmpty(ResolveAnimation(animation))) return true;
        if (Support?.Supported != true || Descriptor == null ||
            !VisualCatalog.Instance.MeshFamilies.TryGetValue(Descriptor.MeshFamilyId, out var family)) return false;
        if (IsWalkRequest(animation) && family.WalkVariant != null) return true;
        return UsesWalkVariant && IsAnimationAvailableOnScene(family.Scene, animation);
    }

    private static bool IsAnimationAvailableOnScene(string scenePath, string animation)
    {
        string cacheKey = scenePath + "|" + animation.ToUpperInvariant();
        if (SceneAnimationAvailabilityCache.TryGetValue(cacheKey, out bool available)) return available;
        if (!SceneCache.TryGetValue(scenePath, out var packed))
            SceneCache[scenePath] = packed = GD.Load<PackedScene>(scenePath);
        var instance = packed?.Instantiate<Node3D>();
        var player = instance == null ? null : FindAll<AnimationPlayer>(instance).FirstOrDefault();
        available = !string.IsNullOrEmpty(ResolveAnimation(player, animation));
        instance?.Free();
        SceneAnimationAvailabilityCache[cacheKey] = available;
        return available;
    }

    private static bool IsWalkRequest(string animation) => string.Equals(animation, "walk", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// D-013: Walk plays with any body shape inside the declared channel range (clamped to ±1) and any
    /// height; the extreme-shape deformed review is in docs/visual/evidence/walk-shapes-*. Probes and
    /// families without a checked Walk or declared Body channels are still refused.
    /// </summary>
    private static bool CanWalkWithShape(VisualDescriptor descriptor, MeshFamily family, VisualCatalog catalog)
    {
        if (family.WalkVariant == null || family.Age != (descriptor.IsFoal ? "foal" : "adult") ||
            descriptor.FixtureId.StartsWith("probe.", StringComparison.Ordinal)) return false;
        return catalog.FamilyMeshBlendShapes.TryGetValue(descriptor.MeshFamilyId, out var meshes) &&
               meshes.TryGetValue("Body", out var channels) && channels.Count > 0;
    }

    /// <summary>The foal Idle/Walk bridge interpolates through the neutral rest shape, so it still needs one.</summary>
    private static bool HasNeutralWalkShape(VisualDescriptor descriptor, MeshFamily family, VisualCatalog catalog)
    {
        if (family.WalkVariant == null || family.Age != (descriptor.IsFoal ? "foal" : "adult") ||
            descriptor.FixtureId.StartsWith("probe.", StringComparison.Ordinal)) return false;
        if (descriptor.MorphWeights.TryGetValue("height", out int height) && height != 0) return false;
        if (!catalog.FamilyMeshBlendShapes.TryGetValue(descriptor.MeshFamilyId, out var meshes) ||
            !meshes.TryGetValue("Body", out var channels)) return false;
        return channels.Count > 0 && descriptor.MorphWeights.Values.All(value => value == 0);
    }

    /// <summary>Freeze the current animation at a time (used for deterministic captures).</summary>
    public void Pose(string animation, double time)
    {
        if (IsTransitioning && _transitionDescriptor != null)
            Present(_transitionDescriptor, animation);
        CancelFoalTransition();
        var resolved = ResolveAnimation(animation);
        if (Animator == null || string.IsNullOrEmpty(resolved)) return;
        CurrentAnimation = resolved;
        Animator.Play(resolved);
        Animator.Seek(time, true);
        Animator.Pause();
    }

    /// <summary>
    /// Resolve animation requests exactly first, then allow a unique case-insensitive
    /// alias for imported clip naming (for example, the stable's "idle" and GLB's "Idle").
    /// Ambiguous aliases are rejected instead of choosing an arbitrary clip.
    /// </summary>
    private string? ResolveAnimation(string requested)
        => ResolveAnimation(Animator, requested);

    private static string? ResolveAnimation(AnimationPlayer? animator, string requested)
    {
        if (animator == null || string.IsNullOrEmpty(requested)) return null;
        var names = animator.GetAnimationList();
        if (names.Contains(requested, StringComparer.Ordinal)) return requested;
        var matches = names.Where(name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Form review: replace every material with matte grey clay (anatomy only, no coat).</summary>
    public void ApplyClay()
    {
        if (Model == null) return;
        var clay = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.62f, 0.62f), Roughness = 0.85f };
        foreach (var mesh in FindAll<MeshInstance3D>(Model))
        {
            var m = mesh.Name == "Mane" || mesh.Name == "Tail" ? mesh.GetSurfaceOverrideMaterial(0) : clay;
            for (int s = 0; s < mesh.GetSurfaceOverrideMaterialCount(); s++) mesh.SetSurfaceOverrideMaterial(s, m);
        }
    }

    public float ModelHeight => Descriptor?.IsFoal == true ? 1.15f : 1.75f;

    private void ClearModel()
    {
        if (Model != null) { RemoveChild(Model); Model.QueueFree(); }
        Model = null; Animator = null; HeadViewTarget = null; CurrentAnimation = ""; Pace = 1;
        UsesWalkVariant = false; LocomotionSpeedMetersPerSecond = 0; LocomotionPeriodSeconds = 0;
    }

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T t) yield return t;
            foreach (var x in FindAll<T>(child)) yield return x;
        }
    }

    private static bool HasBlendShape(MeshInstance3D mesh, string shape) => mesh.FindBlendShapeByName(shape) >= 0;
}
