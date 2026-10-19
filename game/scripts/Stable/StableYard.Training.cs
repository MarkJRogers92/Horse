using Godot;

namespace Cloverbrook.Game.Stable;

public partial class StableYard
{
    /// <summary>
    /// D-046: a small equipment nook behind the paddock. Presentation only: horses
    /// remain in their existing roam ellipse, and practice gallop rules are unchanged.
    /// Like the other yard scenery these props have no physics or navigation bodies.
    /// </summary>
    private void BuildTrainingScenery()
    {
        PlaceTrainingProp("training_jump", new Vector3(5.2f, 0, -7.10f));
        // Ground poles between the jump and its built-in front cavaletti.
        PlaceTrainingProp("ground_pole", new Vector3(5.2f, 0, -6.50f));
        PlaceTrainingProp("ground_pole", new Vector3(5.2f, 0, -6.25f));
        PlaceTrainingProp("low_cavaletti", new Vector3(1.1f, 0, -6.8f));
        PlaceTrainingProp("course_cone", new Vector3(4.8f, 0, -5.25f));
        PlaceTrainingProp("course_cone", new Vector3(5.7f, 0, -4.80f));
        PlaceTrainingProp("course_cone", new Vector3(6.3f, 0, -4.20f));
        PlaceTrainingProp("training_barrel", new Vector3(7.2f, 0, -5.6f));
    }

    // Keep the small equipment footprint mown so 11cm poles remain visible.
    private static bool InTrainingArea(Vector3 p) =>
        (p.X > -0.6f && p.X < 7.7f && p.Z > -8.2f && p.Z < -5.3f) ||
        (p.X > 4.4f && p.X < 7.7f && p.Z >= -5.9f && p.Z < -3.8f);

    private void PlaceTrainingProp(string asset, Vector3 position)
    {
        // These authored GLBs already use metres and a ground-plane pivot. Do not
        // apply Place's Meshy height/bounds normalization: rotated jump parts have
        // conservative AABBs below the real feet, which would lift/shrink the kit.
        var model = LoadAsset(asset, out _) ?? new Node3D();
        var holder = new Node3D { Name = asset, Position = position };
        holder.AddToGroup("scenery_" + asset);
        holder.AddChild(model);
        AddChild(holder);
    }
}
