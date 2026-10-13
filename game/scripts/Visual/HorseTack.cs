using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// D-020 cosmetic tack drawn on a presented horse: a saddle pad over the back and a bow at the poll.
/// Parsed from the descriptor's cosmetic layer ("bow=bow.rose;pad=pad.mint"). Each piece is placed in skeleton
/// space from the rig's rest bones and the body's own surface, then parented to a bone so it follows every
/// animation. Tack never changes the coat or shape; unknown items are ignored.
/// </summary>
public static class HorseTack
{
    private static readonly Dictionary<string, string> Colours = new(StringComparer.Ordinal)
    {
        ["rose"] = "#f3a6c0", ["mint"] = "#a9dcc4", ["sky"] = "#a8cdf0", ["lavender"] = "#c9b3ec",
        ["buttercup"] = "#f7dc7a", ["cream"] = "#fbf1df", ["gold"] = "#f2c14e", ["daisy"] = "#fff6d8",
    };

    private sealed record BackSurface(float Top, float HalfWidth);
    private static readonly Dictionary<string, BackSurface> SurfaceCache = new(StringComparer.Ordinal);

    public static void ReleaseCaches() => SurfaceCache.Clear();

    /// <summary>Parse "slot=item;…" into slot → colour name (the part after the dot).</summary>
    public static Dictionary<string, string> Parse(string? layer)
    {
        var worn = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(layer)) return worn;
        foreach (var part in layer.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=');
            if (pair.Length != 2) continue;
            var colour = pair[1].Contains('.') ? pair[1][(pair[1].IndexOf('.') + 1)..] : pair[1];
            if (Colours.ContainsKey(colour)) worn[pair[0]] = colour;
        }
        return worn;
    }

    public static bool IsKnownLayer(string? layer) => !string.IsNullOrEmpty(layer) && Parse(layer).Count > 0;

    /// <param name="bodyWidth">Normalised chest_width + muscle (-2..2): the body morphs that widen the back.</param>
    public static void Apply(Node3D model, string? layer, string cacheKey, float bodyWidth = 0f)
    {
        var worn = Parse(layer);
        if (worn.Count == 0) return;
        var skeleton = Find<Skeleton3D>(model);
        var body = Find<MeshInstance3D>(model, m => m.Name == "Body");
        if (skeleton == null) return;
        // The production, walk and foal rigs name their bones differently; each role lists its candidates.
        int Bone(params string[] names) => names.Select(n => skeleton.FindBone(n)).FirstOrDefault(i => i >= 0, -1);
        int chest = Bone("Chest", "ribcage_front", "spine_4"), back = Bone("Spine_Mid", "Spine", "Spine_Base", "spine_3", "ribcage_middle", "spine_2");
        int pelvis = Bone("Pelvis", "hips"), head = Bone("Head", "head"), earL = Bone("Ear_L", "ear_1_l"), earR = Bone("Ear_R", "ear_1_r");
        if (chest < 0 || back < 0 || pelvis < 0 || head < 0)
        {
            GD.Print($"[tack] no tack anchors in {cacheKey}");
            return;
        }
        // Use the skin's bind pose where it exists: the body vertices were modelled against it, not the rest pose.
        var bind = BindPoses(body, skeleton);
        Transform3D Global(int bone) => bind.TryGetValue(bone, out var t) ? t : skeleton.GetBoneGlobalRest(bone);
        Vector3 Rest(int bone) => Global(bone).Origin;

        // Skeleton-space frame: up from the world, forward from the hips toward the chest.
        var toSkeleton = skeleton.GlobalTransform.AffineInverse();
        var up = (toSkeleton.Basis * Vector3.Up).Normalized();
        var forward = Rest(chest) - Rest(pelvis);
        forward = (forward - up * forward.Dot(up)).Normalized();
        var side = forward.Cross(up).Normalized();
        var frame = new Basis(forward, up, side);

        if (worn.TryGetValue("pad", out var padColour) && body != null)
        {
            // The saddle sits just behind the withers, a little over halfway from the back bone to the chest.
            var length = Rest(chest).DistanceTo(Rest(pelvis)) * 0.6f;
            var seat = Rest(pelvis).Lerp(Rest(chest), 0.58f);
            var surface = SurfaceCache.TryGetValue(cacheKey, out var known) ? known : SurfaceCache[cacheKey] = MeasureBack(body, skeleton, seat, forward, up, side);
            var centre = seat + up * (surface.Top - seat.Dot(up) - 0.012f);
            // Ride on the spine bone nearest the seat so the pad bends with the back while walking.
            var carrier = Enumerable.Range(0, skeleton.GetBoneCount())
                .Where(i => skeleton.GetBoneName(i).ToString().ToLowerInvariant() is var n && (n.Contains("spine") || n.Contains("ribcage") || n == "chest"))
                .DefaultIfEmpty(back).OrderBy(i => Rest(i).DistanceTo(seat)).First();
            // Wider, more muscular shapes push the back up and out; the pad grows with them so it never sinks in.
            var grow = 1f + Mathf.Max(0f, bodyWidth) * 0.07f;
            centre += up * Mathf.Max(0f, bodyWidth) * 0.012f;
            var pad = new Node3D { Name = "TackPad" };
            pad.AddChild(Sheet(length * 1.06f, surface.HalfWidth * 1.1f * grow, 1.2f, Colour("cream"), 0.008f, false));
            pad.AddChild(Sheet(length, surface.HalfWidth * 1.08f * grow, 1.12f, Colour(padColour), 0.016f, padColour == "gold"));
            Attach(skeleton, carrier, Global(carrier), pad, new Transform3D(frame, centre));
        }

        if (worn.TryGetValue("bow", out var bowColour))
        {
            var ears = earL >= 0 && earR >= 0 ? (Rest(earL) + Rest(earR)) / 2 : Rest(head) + up * 0.12f;
            // On top of the poll, just behind the ears, so it reads from the side and the front.
            var at = ears - forward * 0.05f + up * 0.035f;
            var size = earL >= 0 && earR >= 0 ? Rest(earL).DistanceTo(Rest(earR)) : 0.16f;
            Attach(skeleton, head, Global(head), Bow(Colour(bowColour), Mathf.Clamp(size * 1.7f, 0.14f, 0.42f), bowColour == "gold"),
                new Transform3D(frame, at));
        }
    }

    /// <summary>The swatch colour, a little deeper so pastel fabric still reads under bright yard light.</summary>
    private static Color Colour(string name)
    {
        var c = Color.FromHtml(Colours.TryGetValue(name, out var hex) ? hex : "#ffffff");
        return name is "cream" or "daisy" ? c : Color.FromHsv(c.H, Mathf.Min(1f, c.S * 1.6f), c.V * 0.92f);
    }

    private static Dictionary<int, Transform3D> BindPoses(MeshInstance3D? body, Skeleton3D skeleton)
    {
        var poses = new Dictionary<int, Transform3D>();
        if (body?.Skin is not { } skin) return poses;
        for (var i = 0; i < skin.GetBindCount(); i++)
        {
            var name = skin.GetBindName(i);
            var bone = name.IsEmpty ? skin.GetBindBone(i) : skeleton.FindBone(name);
            if (bone >= 0) poses[bone] = skin.GetBindPose(i).AffineInverse();
        }
        return poses;
    }

    private static void Attach(Skeleton3D skeleton, int index, Transform3D boneAtBind, Node3D piece, Transform3D skeletonSpace)
    {
        var attachment = new BoneAttachment3D { Name = "Tack_" + skeleton.GetBoneName(index), BoneName = skeleton.GetBoneName(index) };
        skeleton.AddChild(attachment);
        piece.Transform = boneAtBind.AffineInverse() * skeletonSpace;
        attachment.AddChild(piece);
    }

    /// <summary>The top of the back and its half-width at the saddle, read from the body's rest vertices.</summary>
    private static BackSurface MeasureBack(MeshInstance3D body, Skeleton3D skeleton, Vector3 spine, Vector3 forward, Vector3 up, Vector3 side)
    {
        var toSkeleton = skeleton.GlobalTransform.AffineInverse() * body.GlobalTransform;
        float spineAlong = spine.Dot(forward), spineSide = spine.Dot(side);
        float top = spine.Dot(up) + 0.15f, halfWidth = 0.2f;
        var mesh = body.Mesh;
        if (mesh == null) return new BackSurface(top, halfWidth);
        var tops = new List<float>(); var widths = new List<(float Up, float Side)>();
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            foreach (var vertex in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                var p = toSkeleton * vertex;
                if (Mathf.Abs(p.Dot(forward) - spineAlong) > 0.06f) continue;
                widths.Add((p.Dot(up), Mathf.Abs(p.Dot(side) - spineSide)));
                if (Mathf.Abs(p.Dot(side) - spineSide) < 0.05f) tops.Add(p.Dot(up));
            }
        }
        if (tops.Count > 0) top = tops.Max();
        var upper = widths.Where(w => w.Up > top - 0.28f).Select(w => w.Side).ToList();
        if (upper.Count > 0) halfWidth = upper.Max();
        return new BackSurface(top, halfWidth);
    }

    /// <summary>A pad draped over the back: a curved sheet along X (forward), curving down both sides (Z).</summary>
    private static MeshInstance3D Sheet(float length, float halfWidth, float sweep, Color colour, float lift, bool shiny)
    {
        const int along = 10, around = 16;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var radius = halfWidth;
        Vector3 Point(int i, int j)
        {
            float u = (i / (float)along - 0.5f) * length;
            float a = (j / (float)around - 0.5f) * 2f * sweep;
            // Round the four corners a little so it reads as fabric.
            float corner = 1f - 0.12f * Mathf.Pow(Mathf.Abs(i / (float)along - 0.5f) * 2f, 4) * Mathf.Pow(Mathf.Abs(j / (float)around - 0.5f) * 2f, 2);
            float r = (radius + lift) * corner;
            return new Vector3(u * corner, r * Mathf.Cos(a) - radius + lift, r * Mathf.Sin(a));
        }
        for (var i = 0; i < along; i++)
        for (var j = 0; j < around; j++)
        {
            var a = Point(i, j); var b = Point(i + 1, j); var c = Point(i + 1, j + 1); var d = Point(i, j + 1);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        }
        st.GenerateNormals();
        var material = new StandardMaterial3D
        {
            AlbedoColor = colour, Roughness = shiny ? 0.35f : 0.85f, Metallic = shiny ? 0.45f : 0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
    }

    /// <summary>A soft ribbon bow: two loops, a knot and two tails, sized from the ear spacing.</summary>
    private static Node3D Bow(Color colour, float size, bool shiny)
    {
        var material = new StandardMaterial3D { AlbedoColor = colour, Roughness = shiny ? 0.35f : 0.6f, Metallic = shiny ? 0.45f : 0f };
        var bow = new Node3D { Name = "TackBow" };
        float s = size;
        MeshInstance3D Part(Mesh mesh, Vector3 position, Vector3 rotationDegrees, Vector3 scale)
        {
            var part = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position, RotationDegrees = rotationDegrees, Scale = scale };
            bow.AddChild(part);
            return part;
        }
        var loop = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 };
        // Two loops tilted up into a V, a round knot, and two tails down the back of the head.
        Part(loop, new Vector3(0, 0.12f * s, 0.26f * s), new Vector3(-28, 0, 0), new Vector3(0.18f * s, 0.36f * s, 0.56f * s));
        Part(loop, new Vector3(0, 0.12f * s, -0.26f * s), new Vector3(28, 0, 0), new Vector3(0.18f * s, 0.36f * s, 0.56f * s));
        Part(loop, new Vector3(0, 0.08f * s, 0), Vector3.Zero, new Vector3(0.2f * s, 0.22f * s, 0.22f * s));
        var tail = new BoxMesh { Size = Vector3.One };
        Part(tail, new Vector3(-0.12f * s, -0.18f * s, 0.12f * s), new Vector3(20, 0, 25), new Vector3(0.04f * s, 0.42f * s, 0.12f * s));
        Part(tail, new Vector3(-0.12f * s, -0.18f * s, -0.12f * s), new Vector3(-20, 0, 25), new Vector3(0.04f * s, 0.42f * s, 0.12f * s));
        return bow;
    }

    private static T? Find<T>(Node node, Func<T, bool>? match = null) where T : Node
    {
        if (node is T hit && (match == null || match(hit))) return hit;
        foreach (var child in node.GetChildren())
            if (Find(child, match) is { } found) return found;
        return null;
    }
}
