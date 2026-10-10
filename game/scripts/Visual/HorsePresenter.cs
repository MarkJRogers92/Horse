using System;
using System.Collections.Generic;
using System.Linq;
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

    [Signal] public delegate void PresentedEventHandler(string summary, bool supported);

    public VisualDescriptor? Descriptor { get; private set; }
    public VisualSupport? Support { get; private set; }
    public Markings? Markings { get; private set; }
    public Node3D? Model { get; private set; }
    public AnimationPlayer? Animator { get; private set; }
    public string CurrentAnimation { get; private set; } = "";

    public VisualSupport Present(VisualDescriptor descriptor, string animation = "idle")
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
        if (!SceneCache.TryGetValue(family.Scene, out var packed))
            SceneCache[family.Scene] = packed = GD.Load<PackedScene>(family.Scene);
        Model = packed.Instantiate<Node3D>();
        Model.Name = "Model";
        AddChild(Model);

        var coat = catalog.Coats[descriptor.CoatRecipe];
        var hair = catalog.Hair[descriptor.HairRecipe];
        var skin = catalog.Skin[descriptor.SkinRecipe];
        var eye = catalog.Eyes[descriptor.EyeRecipe];
        Markings = Markings.FromKey(descriptor.MarkingRealizationKey);

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

        foreach (var mesh in FindAll<MeshInstance3D>(Model))
        {
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

        // Bounded body variation. The placeholder has no blend shapes, so only height is approximated.
        float height = 1f + (descriptor.MorphWeights.TryGetValue("height", out var h) ? h : 0) * catalog.HeightScalePerUnit;
        Model.Scale = Vector3.One * height;

        Animator = FindAll<AnimationPlayer>(Model).FirstOrDefault();
        if (Animator != null)
            foreach (var name in Animator.GetAnimationList())
                Animator.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        Play(animation);
        EmitSignal(SignalName.Presented, Support.Summary, true);
        return Support;
    }

    public void Play(string animation)
    {
        CurrentAnimation = animation;
        if (Animator == null) return;
        if (string.IsNullOrEmpty(animation) || !Animator.HasAnimation(animation)) { Animator.Stop(); return; }
        Animator.Play(animation);
    }

    /// <summary>Freeze the current animation at a time (used for deterministic captures).</summary>
    public void Pose(string animation, double time)
    {
        if (Animator == null || !Animator.HasAnimation(animation)) return;
        Animator.Play(animation);
        Animator.Seek(time, true);
        Animator.Pause();
    }

    public float ModelHeight => Descriptor?.IsFoal == true ? 1.15f : 1.75f;

    private void ClearModel()
    {
        if (Model != null) { RemoveChild(Model); Model.QueueFree(); }
        Model = null; Animator = null;
    }

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T t) yield return t;
            foreach (var x in FindAll<T>(child)) yield return x;
        }
    }
}
