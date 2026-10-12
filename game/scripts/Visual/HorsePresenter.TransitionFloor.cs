using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Cloverbrook.Game.Visual;

public partial class HorsePresenter
{
    // Local bone-pose interpolation can swing a toe below the ground even when
    // both endpoint poses are supported. Apply only the small vertical clearance
    // needed by the current actual imported skin, never locomotion or a new gait.
    private const float TransitionFloorHeight = -0.0015f; // Inside the existing -2 mm floor bound, with a precision margin.
    private TransitionFloor? _transitionFloor;

    private sealed record FloorVertex(Vector3 Position, Vector3[] MorphDeltas, int[] Binds, float[] Weights);
    private sealed record TransitionFloor(MeshInstance3D Mesh, Skeleton3D Skeleton, int[] BoneMap,
        Transform3D[] BindPoses, FloorVertex[] Vertices, Vector3 ModelRestPosition);

    private void PrepareTransitionFloor()
    {
        var mesh = FindAll<MeshInstance3D>(Model!).Single(m => m.Name == "Body");
        var skeleton = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
        var skin = mesh.Skin;
        if (skeleton == null || skin == null) { _transitionFloor = null; return; }
        var vertices = new List<FloorVertex>();
        for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(s);
            var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var binds = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int width = weights.Length / positions.Length;
            var targets = mesh.Mesh.SurfaceGetBlendShapeArrays(s).Select(a => a[(int)Mesh.ArrayType.Vertex].AsVector3Array()).ToArray();
            bool relative = mesh.Mesh is ArrayMesh am && am.BlendShapeMode == Mesh.BlendShapeMode.Relative;
            for (int i = 0; i < positions.Length; i++)
            {
                // Include the entire lower hoof/ankle region, not just an averaged
                // hoof pivot. Checked foal meshes use +Y up and metre coordinates.
                if (positions[i].Y > 0.12f) continue;
                vertices.Add(new FloorVertex(positions[i], targets.Select(t => relative ? t[i] : t[i] - positions[i]).ToArray(),
                    binds.Skip(i * width).Take(width).ToArray(), weights.Skip(i * width).Take(width).ToArray()));
            }
        }
        _transitionFloor = new TransitionFloor(mesh, skeleton,
            Enumerable.Range(0, skin.GetBindCount()).Select(i => skin.GetBindName(i).IsEmpty ? skin.GetBindBone(i) : skeleton.FindBone(skin.GetBindName(i))).ToArray(),
            Enumerable.Range(0, skin.GetBindCount()).Select(skin.GetBindPose).ToArray(), vertices.ToArray(), Model!.Position);
    }

    private void KeepTransitionAboveFloor()
    {
        var floor = _transitionFloor;
        if (floor == null || floor.Vertices.Length == 0 || Model == null) return;
        Model.Position = floor.ModelRestPosition;
        var weights = Enumerable.Range(0, floor.Mesh.GetBlendShapeCount()).Select(floor.Mesh.GetBlendShapeValue).ToArray();
        var transforms = floor.BoneMap.Select((bone, i) => floor.Skeleton.GetBoneGlobalPose(bone) * floor.BindPoses[i]).ToArray();
        var world = floor.Skeleton.GlobalTransform;
        float minimum = float.PositiveInfinity;
        foreach (var vertex in floor.Vertices)
        {
            var shape = vertex.Position;
            for (int m = 0; m < weights.Length; m++) shape += vertex.MorphDeltas[m] * weights[m];
            var point = Vector3.Zero;
            for (int w = 0; w < vertex.Weights.Length; w++)
                if (vertex.Weights[w] != 0) point += vertex.Weights[w] * (transforms[vertex.Binds[w]] * shape);
            minimum = Math.Min(minimum, (world * point).Y);
        }
        if (float.IsFinite(minimum) && minimum < TransitionFloorHeight)
            Model.Position += Vector3.Up * (TransitionFloorHeight - minimum);
    }
}
