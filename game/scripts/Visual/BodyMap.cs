using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// D-025 body map. The coat's UV0 is a Smart UV project, so anything drawn across the trunk broke at its seams, and
/// the canonical UV2 coordinates only cover the head and legs. This adds one continuous body coordinate to every
/// Body vertex, taken from the rest pose (the mesh's own vertex positions before skinning and morphs), so patterns
/// stay glued to the skin while the horse walks or changes shape.
///
/// CUSTOM0 = (u, h, s, 1):
///   u  along the body: 0 at the buttock, 1 at the elbow line (girth); the chest, neck and head continue past 1.
///   h  height: 0 at the belly, 1 at the topline of the back; legs go below 0, the neck rises above 1.
///   s  side: -1..1 across the widest part of the barrel; 0 is the dorsal/ventral midline.
/// Landmarks come from the canonical UV2 regions (head, the four legs, trunk), so the same numbers mean the same
/// place on adults and foals of every family. Presentation only: nothing here touches the save or the descriptor.
/// </summary>
public static class BodyMap
{
    public readonly record struct Frame(Vector3 Forward, float RumpU, float GirthU, float BellyY, float TopY, float HalfWidth)
    {
        /// <summary>Body length units per normalised axis, so shaders can draw round (not stretched) spots.</summary>
        public Vector3 Aspect => new(1f, (TopY - BellyY) / Math.Max(1e-4f, GirthU - RumpU), HalfWidth / Math.Max(1e-4f, GirthU - RumpU));
    }

    private static readonly Dictionary<Mesh, (ArrayMesh Mesh, Frame Frame)> Cache = new();

    public static void ReleaseCache() => Cache.Clear();

    /// <summary>Replace the Body mesh with a copy that carries the body map. Returns the frame, or null when the
    /// mesh lacks the canonical regions (it then keeps its original mesh and the shader falls back to UV0).</summary>
    public static Frame? Apply(MeshInstance3D body)
    {
        if (body.Mesh is not ArrayMesh source) return null;
        if (!Cache.TryGetValue(source, out var built))
        {
            var mapped = Build(source);
            if (mapped == null) return null;
            Cache[source] = built = mapped.Value;
        }
        body.Mesh = built.Mesh;
        return built.Frame;
    }

    private static (ArrayMesh, Frame)? Build(ArrayMesh source)
    {
        var surfaces = Enumerable.Range(0, source.GetSurfaceCount()).Select(source.SurfaceGetArrays).ToArray();
        var frame = Measure(surfaces);
        if (frame == null) return null;
        var f = frame.Value;
        var mesh = new ArrayMesh { BlendShapeMode = source.BlendShapeMode, CustomAabb = source.CustomAabb };
        for (int b = 0; b < source.GetBlendShapeCount(); b++) mesh.AddBlendShape(source.GetBlendShapeName(b));
        var side = new Vector3(-f.Forward.Z, 0, f.Forward.X);
        for (int s = 0; s < surfaces.Length; s++)
        {
            var arrays = surfaces[s];
            var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var custom = new float[positions.Length * 4];
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                custom[i * 4] = (p.Dot(f.Forward) - f.RumpU) / (f.GirthU - f.RumpU);
                custom[i * 4 + 1] = (p.Y - f.BellyY) / (f.TopY - f.BellyY);
                custom[i * 4 + 2] = p.Dot(side) / f.HalfWidth;
                custom[i * 4 + 3] = 1f;
            }
            arrays[(int)Mesh.ArrayType.Custom0] = custom;
            var format = source.SurfaceGetFormat(s);
            var flags = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
                        | (format & Mesh.ArrayFormat.FlagUse8BoneWeights);
            mesh.AddSurfaceFromArrays(source.SurfaceGetPrimitiveType(s), arrays, source.SurfaceGetBlendShapeArrays(s), null, flags);
            mesh.SurfaceSetMaterial(s, source.SurfaceGetMaterial(s));
            mesh.SurfaceSetName(s, source.SurfaceGetName(s));
        }
        mesh.ResourceName = source.ResourceName;
        return (mesh, f);
    }

    /// <summary>Find the body frame from the canonical UV2 regions (head, legs 10-13, trunk sentinel).</summary>
    private static Frame? Measure(Godot.Collections.Array[] surfaces)
    {
        var head = new List<Vector3>(); var trunk = new List<Vector3>();
        var legTops = new List<Vector3>[4] { new(), new(), new(), new() };
        foreach (var arrays in surfaces)
        {
            if (arrays[(int)Mesh.ArrayType.TexUV2].VariantType == Variant.Type.Nil) return null;
            var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var uv2 = arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            if (uv2.Length != positions.Length) return null;
            for (int i = 0; i < positions.Length; i++)
            {
                float x = uv2[i].X, y = 1f - uv2[i].Y; // glTF V flip, as in horse_coat.gdshader
                if (x > -0.5f && x < 1.6f && Math.Abs(y) < 2f) head.Add(positions[i]);
                else if (x > 9.5f && x < 14f) { if (y > 0.9f) legTops[Math.Clamp((int)Math.Round(x - 10f), 0, 3)].Add(positions[i]); }
                else trunk.Add(positions[i]);
            }
        }
        if (head.Count == 0 || trunk.Count == 0 || legTops.Any(l => l.Count == 0)) return null;
        Vector3 Mean(IEnumerable<Vector3> points) { var a = points.ToArray(); return a.Aggregate(Vector3.Zero, (x, y) => x + y) / a.Length; }
        var front = Mean(legTops[0].Concat(legTops[1]));
        var hind = Mean(legTops[2].Concat(legTops[3]));
        var forward = new Vector3(front.X - hind.X, 0, front.Z - hind.Z).Normalized();
        if (!forward.IsFinite() || forward.LengthSquared() < 0.5f) return null;
        float rump = trunk.Min(p => p.Dot(forward));
        float girth = front.Dot(forward);
        if (girth - rump < 1e-3f) return null;
        // Topline and belly from the barrel only (the neck rises above the back, the legs hang below the belly).
        var barrel = trunk.Where(p => { var u = (p.Dot(forward) - rump) / (girth - rump); return u > 0.25f && u < 0.85f; }).ToArray();
        if (barrel.Length == 0) return null;
        var side = new Vector3(-forward.Z, 0, forward.X);
        float top = barrel.Max(p => p.Y), belly = barrel.Min(p => p.Y);
        float half = barrel.Max(p => Math.Abs(p.Dot(side)));
        if (top - belly < 1e-3f || half < 1e-3f) return null;
        return new Frame(forward, rump, girth, belly, top, half);
    }
}
