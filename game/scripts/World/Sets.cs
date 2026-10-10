using System;
using Godot;

namespace Cloverbrook.Game.World;

/// <summary>
/// Procedural, reusable environment sets. The horse stands at the origin facing +X;
/// presentation cameras look from the +X/+Z quadrant, so scenery sits toward -X/-Z.
/// Everything is built from primitives so CI needs no extra art assets.
/// </summary>
public static class Sets
{
    // Cloverbrook palette (sRGB)
    public static readonly Color Blush = Color.FromHtml("#f6c9d4");
    public static readonly Color Lavender = Color.FromHtml("#b9a3dc");
    public static readonly Color Butter = Color.FromHtml("#fbe7b0");
    public static readonly Color Sage = Color.FromHtml("#a9c99a");
    public static readonly Color Sky = Color.FromHtml("#bfe0f5");
    public static readonly Color Mint = Color.FromHtml("#a8dcc8");
    public static readonly Color Cream = Color.FromHtml("#fbf3e4");
    public static readonly Color Wood = Color.FromHtml("#e9d2ad");
    public static readonly Color Plum = Color.FromHtml("#5b3a5e");

    // ----------------------------------------------------------------------------------
    public static Node3D Studio()
    {
        var root = new Node3D { Name = "StudioSet" };
        // 18% grey sweep: floor plus a curved back wall so there is no horizon line
        var floorMat = Mat(new Color(0.46f, 0.46f, 0.46f), 0.9f);
        root.AddChild(Mesh(new PlaneMesh { Size = new Vector2(40, 40) }, floorMat, Vector3.Zero));
        var sweep = new CylinderMesh { TopRadius = 14, BottomRadius = 14, Height = 12, RadialSegments = 64, CapTop = false, CapBottom = false };
        var sweepMat = Mat(new Color(0.46f, 0.46f, 0.46f), 0.95f); sweepMat.CullMode = BaseMaterial3D.CullModeEnum.Front;
        root.AddChild(Mesh(sweep, sweepMat, new Vector3(0, 6, 0)));
        return root;
    }

    // ----------------------------------------------------------------------------------
    public static Node3D Stable()
    {
        var root = new Node3D { Name = "StableSet" };
        var rng = new RandomNumberGenerator { Seed = 7 }; // fixed layout = reproducible screenshots

        // Ground: soft grass with a sandy paddock and path
        var grass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass_ground.gdshader") };
        root.AddChild(Mesh(new PlaneMesh { Size = new Vector2(120, 120), SubdivideWidth = 2, SubdivideDepth = 2 }, grass, Vector3.Zero));
        var sand = Mat(Color.FromHtml("#e8d3ae"), 0.95f);
        root.AddChild(Mesh(new CylinderMesh { TopRadius = 4.3f, BottomRadius = 4.3f, Height = 0.02f, RadialSegments = 48 }, sand, new Vector3(0.3f, 0.005f, 0)));
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(2.2f, 0.02f, 9f) }, sand, new Vector3(-3.6f, 0.006f, -6.5f), new Vector3(0, 40, 0)));

        // Distant hills and trees frame the scene
        var hill = Mat(Color.FromHtml("#b9d6a3"), 1f);
        for (int i = 0; i < 7; i++)
        {
            float a = Mathf.DegToRad(150 + i * 22), r = 38 + rng.RandfRange(-4, 6);
            var h = Mesh(new SphereMesh { Radius = 1, Height = 2 }, hill, new Vector3(Mathf.Cos(a) * r, -6, Mathf.Sin(a) * r));
            h.Scale = new Vector3(rng.RandfRange(14, 22), rng.RandfRange(8, 11), rng.RandfRange(10, 16));
            root.AddChild(h);
        }
        foreach (var p in new[] { new Vector3(-13, 0, -9), new Vector3(-9.5f, 0, -14), new Vector3(-16, 0, -3), new Vector3(2, 0, -16), new Vector3(-4, 0, -18), new Vector3(9, 0, -14) })
            root.AddChild(Tree(p, rng));

        root.AddChild(Barn(new Vector3(-6.2f, 0, -7.0f), 35));
        root.AddChild(Fence(rng));
        root.AddChild(Flowers(rng));
        root.AddChild(Grass(rng));
        return root;
    }

    private static Node3D Barn(Vector3 pos, float yawDeg)
    {
        var b = new Node3D { Name = "Barn", Position = pos, RotationDegrees = new Vector3(0, yawDeg, 0) };
        var wall = Mat(Blush, 0.85f); var trim = Mat(Cream, 0.7f); var roof = Mat(Lavender, 0.6f);
        var door = Mat(Mint, 0.7f); var wood = Mat(Wood, 0.85f);
        float w = 6.5f, d = 5f, hgt = 3.2f;
        b.AddChild(Mesh(new BoxMesh { Size = new Vector3(w, hgt, d) }, wall, new Vector3(0, hgt / 2, 0)));
        // gables (front faces +Z of the barn)
        var gable = new PrismMesh { Size = new Vector3(w, 1.9f, d), LeftToRight = 0.5f };
        b.AddChild(Mesh(gable, wall, new Vector3(0, hgt + 0.95f, 0)));
        // roof slabs
        float slope = Mathf.RadToDeg(Mathf.Atan2(1.9f, w / 2));
        float len = Mathf.Sqrt(1.9f * 1.9f + w * w / 4) + 0.35f;
        b.AddChild(Mesh(new BoxMesh { Size = new Vector3(len, 0.16f, d + 0.6f) }, roof, new Vector3(-w / 4 - 0.05f, hgt + 1.0f, 0), new Vector3(0, 0, slope)));
        b.AddChild(Mesh(new BoxMesh { Size = new Vector3(len, 0.16f, d + 0.6f) }, roof, new Vector3(w / 4 + 0.05f, hgt + 1.0f, 0), new Vector3(0, 0, -slope)));
        // corner trim and base
        foreach (var sx in new[] { -1, 1 })
        foreach (var sz in new[] { -1, 1 })
            b.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.18f, hgt, 0.18f) }, trim, new Vector3(sx * w / 2, hgt / 2, sz * d / 2)));
        b.AddChild(Mesh(new BoxMesh { Size = new Vector3(w + 0.1f, 0.25f, d + 0.1f) }, trim, new Vector3(0, 0.12f, 0)));
        // big sliding door with white X brace
        var front = new Vector3(0, 0, d / 2 + 0.03f);
        b.AddChild(Mesh(new BoxMesh { Size = new Vector3(2.4f, 2.5f, 0.08f) }, door, front + new Vector3(0, 1.25f, 0)));
        b.AddChild(Mesh(new BoxMesh { Size = new Vector3(2.6f, 0.14f, 0.1f) }, trim, front + new Vector3(0, 2.55f, 0.02f)));
        foreach (var s in new[] { -1, 1 })
        {
            b.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.14f, 2.6f, 0.1f) }, trim, front + new Vector3(s * 1.25f, 1.25f, 0.02f)));
            b.AddChild(Mesh(new BoxMesh { Size = new Vector3(3.3f, 0.12f, 0.1f) }, trim, front + new Vector3(0, 1.25f, 0.05f), new Vector3(0, 0, s * 46)));
        }
        // hayloft window with heart
        b.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.0f, 0.9f, 0.08f) }, Mat(Butter, 0.6f), front + new Vector3(0, 3.75f, 0)));
        b.AddChild(Mesh(new SphereMesh { Radius = 0.14f, Height = 0.1f }, Mat(Color.FromHtml("#f08aa6"), 0.5f), front + new Vector3(0, 3.78f, 0.06f)));
        // side windows with flower boxes
        foreach (var s in new[] { -1, 1 })
        {
            var wp = front + new Vector3(s * 2.35f, 1.7f, 0);
            b.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.8f, 0.06f) }, Mat(Sky, 0.2f), wp));
            b.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.05f, 0.95f, 0.04f) }, trim, wp - new Vector3(0, 0, 0.02f)));
            b.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.0f, 0.22f, 0.25f) }, wood, wp + new Vector3(0, -0.55f, 0.12f)));
            for (int i = 0; i < 7; i++)
                b.AddChild(Mesh(new SphereMesh { Radius = 0.09f, Height = 0.16f }, Mat(i % 2 == 0 ? Color.FromHtml("#f39bb4") : Butter, 0.6f),
                    wp + new Vector3(-0.42f + i * 0.14f, -0.38f, 0.14f)));
        }
        // bunting along the eave
        var flagCols = new[] { Blush, Butter, Mint, Lavender, Sky };
        for (int i = 0; i < 11; i++)
        {
            float x = -w / 2 + 0.3f + i * (w - 0.6f) / 10f;
            float sag = 0.25f * (1 - Mathf.Pow((x / (w / 2)), 2));
            b.AddChild(Mesh(new PrismMesh { Size = new Vector3(0.32f, 0.38f, 0.02f) }, Mat(flagCols[i % flagCols.Length], 0.7f),
                new Vector3(x, hgt - 0.05f - sag, d / 2 + 0.25f), new Vector3(0, 0, 180)));
        }
        // open-front stall on the right side of the barn
        var stall = new Node3D { Name = "Stall", Position = new Vector3(w / 2 + 1.6f, 0, 0.4f) };
        stall.AddChild(Mesh(new BoxMesh { Size = new Vector3(3.2f, 1.25f, 0.12f) }, wood, new Vector3(0, 0.63f, -1.6f)));
        stall.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.12f, 1.25f, 3.2f) }, wood, new Vector3(1.6f, 0.63f, 0)));
        stall.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.4f, 1.1f, 0.1f) }, Mat(Mint, 0.7f), new Vector3(0.85f, 0.55f, 1.6f))); // half door
        foreach (var px in new[] { -1.6f, 1.6f })
        foreach (var pz in new[] { -1.6f, 1.6f })
            stall.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.16f, 2.7f, 0.16f) }, trim, new Vector3(px, 1.35f, pz)));
        stall.AddChild(Mesh(new BoxMesh { Size = new Vector3(3.8f, 0.12f, 3.8f) }, roof, new Vector3(0.1f, 2.75f, 0), new Vector3(0, 0, -8)));
        stall.AddChild(Mesh(new BoxMesh { Size = new Vector3(3.2f, 0.04f, 3.2f) }, Mat(Color.FromHtml("#f1dc9a"), 1f), new Vector3(0, 0.03f, 0))); // straw
        stall.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.45f, 0.5f) }, Mat(Color.FromHtml("#efd68a"), 1f), new Vector3(-0.9f, 0.23f, -1.1f))); // hay bale
        stall.AddChild(Mesh(new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.16f, Height = 0.32f }, Mat(Sky, 0.4f), new Vector3(1.2f, 0.16f, -1.2f))); // bucket
        stall.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.7f, 0.22f, 0.04f) }, Mat(Lavender, 0.6f), new Vector3(0, 2.25f, 1.65f))); // name plaque
        b.AddChild(stall);
        return b;
    }

    private static Node3D Fence(RandomNumberGenerator rng)
    {
        var f = new Node3D { Name = "PaddockFence" };
        var white = Mat(Cream, 0.6f);
        // oval paddock behind and around the horse (open toward the camera)
        int posts = 22; float rx = 6.0f, rz = 5.0f;
        Vector3 P(int i) { float a = Mathf.DegToRad(100 + i * (230f / (posts - 1))); return new Vector3(Mathf.Cos(a) * rx + 0.5f, 0, Mathf.Sin(a) * rz); }
        for (int i = 0; i < posts; i++)
        {
            var p = P(i);
            f.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.13f, 1.25f, 0.13f) }, white, p + new Vector3(0, 0.62f, 0)));
            f.AddChild(Mesh(new SphereMesh { Radius = 0.085f, Height = 0.12f }, white, p + new Vector3(0, 1.27f, 0)));
            if (i == posts - 1) break;
            var q = P(i + 1); var mid = (p + q) / 2; float len = p.DistanceTo(q);
            float yaw = Mathf.RadToDeg(Mathf.Atan2(-(q.Z - p.Z), q.X - p.X));
            foreach (var hgt in new[] { 0.55f, 1.0f })
                f.AddChild(Mesh(new BoxMesh { Size = new Vector3(len, 0.09f, 0.05f) }, white, mid + new Vector3(0, hgt, 0), new Vector3(0, yaw, 0)));
        }
        return f;
    }

    private static Node3D Flowers(RandomNumberGenerator rng)
    {
        var root = new Node3D { Name = "Flowers" };
        var petals = new[] { "#f7a1bd", "#fbd36b", "#c9a7ef", "#ffffff", "#ff9fa0", "#9fd3f5" };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new SphereMesh { Radius = 0.06f, Height = 0.09f, RadialSegments = 8, Rings = 4 } };
        var stems = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.01f, Height = 0.3f, RadialSegments = 4 } };
        const int n = 900;
        mm.InstanceCount = n; stems.InstanceCount = n;
        for (int i = 0; i < n; i++)
        {
            Vector3 p;
            if (i < 500) { float a = rng.RandfRange(Mathf.DegToRad(105), Mathf.DegToRad(325)); float r = rng.RandfRange(6.3f, 7.4f); p = new Vector3(Mathf.Cos(a) * r * 1.0f + 0.5f, 0, Mathf.Sin(a) * r * 0.83f); }
            else { p = new Vector3(rng.RandfRange(-14, 10), 0, rng.RandfRange(-16, -7.5f)); }
            float hgt = rng.RandfRange(0.18f, 0.36f);
            stems.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(1, hgt / 0.3f, 1)), p + new Vector3(0, hgt / 2, 0)));
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * rng.RandfRange(0.7f, 1.3f)), p + new Vector3(0, hgt, 0)));
            mm.SetInstanceColor(i, Color.FromHtml(petals[rng.RandiRange(0, petals.Length - 1)]));
        }
        var petalMat = Mat(Colors.White, 0.6f); petalMat.VertexColorUseAsAlbedo = true;
        root.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = petalMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        root.AddChild(new MultiMeshInstance3D { Multimesh = stems, MaterialOverride = Mat(Color.FromHtml("#6fa25c"), 0.8f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }

    private static Node3D Grass(RandomNumberGenerator rng)
    {
        var blade = new PrismMesh { Size = new Vector3(0.05f, 0.22f, 0.01f) };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = blade };
        const int n = 9000; mm.InstanceCount = n;
        for (int i = 0; i < n; i++)
        {
            Vector3 p;
            do { p = new Vector3(rng.RandfRange(-18, 14), 0, rng.RandfRange(-18, 12)); }
            while (new Vector2((p.X - 0.3f), p.Z).Length() < 4.6f);
            var basis = Basis.Identity.Rotated(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Rotated(Vector3.Right, rng.RandfRange(-0.25f, 0.25f)).Scaled(Vector3.One * rng.RandfRange(0.6f, 1.4f));
            mm.SetInstanceTransform(i, new Transform3D(basis, p + new Vector3(0, 0.1f, 0)));
            mm.SetInstanceColor(i, Color.FromHtml("#7fb36a").Lerp(Color.FromHtml("#b7d88f"), rng.Randf()));
        }
        var m = Mat(Colors.White, 0.85f); m.VertexColorUseAsAlbedo = true; m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return new MultiMeshInstance3D { Name = "GrassTufts", Multimesh = mm, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    private static Node3D Tree(Vector3 pos, RandomNumberGenerator rng)
    {
        var t = new Node3D { Name = "Tree", Position = pos };
        float s = rng.RandfRange(0.8f, 1.3f);
        t.AddChild(Mesh(new CylinderMesh { TopRadius = 0.18f * s, BottomRadius = 0.28f * s, Height = 2.4f * s }, Mat(Color.FromHtml("#a07a5c"), 0.9f), new Vector3(0, 1.2f * s, 0)));
        var leaf = Mat(Color.FromHtml(rng.Randf() < 0.5f ? "#9cc98a" : "#b4d68f"), 0.9f);
        for (int i = 0; i < 4; i++)
            t.AddChild(Mesh(new SphereMesh { Radius = 1.1f * s, Height = 2f * s }, leaf,
                new Vector3(rng.RandfRange(-0.6f, 0.6f) * s, (2.9f + rng.RandfRange(-0.2f, 0.6f)) * s, rng.RandfRange(-0.6f, 0.6f) * s)));
        return t;
    }

    // helpers ----------------------------------------------------------------------------
    public static StandardMaterial3D Mat(Color c, float roughness) => new() { AlbedoColor = c, Roughness = roughness };

    public static MeshInstance3D Mesh(Mesh mesh, Material mat, Vector3 pos, Vector3? rotDeg = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        if (rotDeg.HasValue) mi.RotationDegrees = rotDeg.Value;
        return mi;
    }
}
