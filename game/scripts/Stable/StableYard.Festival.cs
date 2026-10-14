using System;
using System.Collections.Generic;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// D-032 festival decorations (presentation only). Each season's festival dresses the paddock with a few dozen primitive
/// meshes built once and shared-material: bunting or lights strung along the fence, and a handful of props standing in the
/// free band between the horses' wander area and the fence (never on the decor spots outside the fence).
/// </summary>
public partial class StableYard
{
    // The paddock fence is an ellipse arc open toward the camera (see BuildScenery).
    private const float FenceRx = 7.4f, FenceRz = 5.4f, FenceCentreZ = -0.5f, FenceStartDeg = 160f, FenceSpanDeg = 220f;
    private const int FenceSegments = 13;

    private readonly Dictionary<string, Node3D> _festivalProps = new();
    private string? _festival;

    /// <summary>The festival whose decorations are showing ("spring", "summer", "autumn", "winter"), or null.</summary>
    public string? Festival => _festival;

    /// <summary>Shows one festival's decorations (or none). Public so a preview can force any season's festival.</summary>
    public void SetFestival(string? festivalId)
    {
        _festival = festivalId;
        if (festivalId != null && !_festivalProps.ContainsKey(festivalId))
        {
            var root = BuildFestival(festivalId);
            AddChild(root);
            _festivalProps[festivalId] = root;
        }
        foreach (var (id, node) in _festivalProps) node.Visible = id == festivalId;
    }

    // ---- shared helpers -------------------------------------------------------------------
    private static readonly Dictionary<string, StandardMaterial3D> FestivalMats = new(StringComparer.Ordinal);
    private static StandardMaterial3D FestMat(string hex, float roughness = 0.75f, bool glow = false)
    {
        var key = hex + roughness + (glow ? "!" : "");
        if (FestivalMats.TryGetValue(key, out var known)) return known;
        var material = new StandardMaterial3D { AlbedoColor = Color.FromHtml(hex), Roughness = roughness };
        if (glow) { material.EmissionEnabled = true; material.Emission = Color.FromHtml(hex); material.EmissionEnergyMultiplier = 2.5f; }
        return FestivalMats[key] = material;
    }

    private static readonly SphereMesh FestBall = new() { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 };
    private static readonly SphereMesh FestSmallBall = new() { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 };
    private static readonly CylinderMesh FestCylinder = new() { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 12 };

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3 scale, Vector3? rotationDegrees = null)
    {
        var part = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position, Scale = scale, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        if (rotationDegrees.HasValue) part.RotationDegrees = rotationDegrees.Value;
        parent.AddChild(part);
        return part;
    }

    /// <summary>A point on the ground inside the fence: fraction k of the way out along the fence ellipse at angle a.</summary>
    private static Vector3 FestivalSpot(float angleDegrees, float k)
    {
        var a = Mathf.DegToRad(angleDegrees);
        return new Vector3(Mathf.Cos(a) * FenceRx * k, 0, Mathf.Sin(a) * FenceRz * k + FenceCentreZ);
    }

    private Node3D BuildFestival(string id)
    {
        var root = new Node3D { Name = "Festival_" + id };
        switch (id)
        {
            case "spring": BuildSpring(root); break;
            case "summer": BuildSummer(root); break;
            case "autumn": BuildAutumn(root); break;
            default: BuildWinter(root); break;
        }
        return root;
    }

    // ---- strings along the fence ------------------------------------------------------------
    /// <summary>The catenary sample points of every fence segment: inside the fence, hung between the posts.</summary>
    private static IEnumerable<Vector3[]> FenceStrings(float height, float sag, int samples)
    {
        for (var i = 0; i < FenceSegments; i++)
        {
            float a0 = Mathf.DegToRad(FenceStartDeg + i * (FenceSpanDeg / FenceSegments)), a1 = Mathf.DegToRad(FenceStartDeg + (i + 1) * (FenceSpanDeg / FenceSegments));
            Vector3 Post(float a)
            {
                var p = new Vector3(Mathf.Cos(a) * FenceRx, 0, Mathf.Sin(a) * FenceRz + FenceCentreZ);
                var inward = (new Vector3(0, 0, FenceCentreZ) - p).Normalized();
                return p + inward * 0.1f + Vector3.Up * height;
            }
            var p0 = Post(a0); var p1 = Post(a1);
            var points = new Vector3[samples + 1];
            for (var s = 0; s <= samples; s++)
            {
                var t = s / (float)samples;
                points[s] = p0.Lerp(p1, t) - Vector3.Up * (sag * 4f * t * (1f - t));
            }
            yield return points;
        }
    }

    /// <summary>One mesh for all the bunting: double-sided pennants with vertex colours, plus the cord as thin quads.</summary>
    private static Node3D Bunting(string[] colours, int bands, float flagWidth, float flagDrop, float height, float sag)
    {
        var holder = new Node3D { Name = "Bunting" };
        var flags = new SurfaceTool(); flags.Begin(Mesh.PrimitiveType.Triangles);
        var cord = new SurfaceTool(); cord.Begin(Mesh.PrimitiveType.Triangles);
        var colourIndex = 0;
        foreach (var points in FenceStrings(height, sag, 20))
        {
            // The cord.
            for (var s = 0; s < points.Length - 1; s++)
            {
                var a = points[s]; var b = points[s + 1]; var h = Vector3.Up * 0.006f;
                cord.SetColor(Colors.White);
                foreach (var v in new[] { a - h, b - h, b + h, a - h, b + h, a + h }) cord.AddVertex(v);
            }
            // Flags hang from evenly spaced points along the cord.
            var length = 0f; for (var s = 0; s < points.Length - 1; s++) length += points[s].DistanceTo(points[s + 1]);
            var count = Mathf.Max(3, Mathf.RoundToInt(length / (flagWidth * 1.35f)));
            for (var f = 0; f < count; f++)
            {
                var t = (f + 0.5f) / count * (points.Length - 1);
                var i = Mathf.Min(points.Length - 2, Mathf.FloorToInt(t));
                var centre = points[i].Lerp(points[i + 1], t - i);
                var along = (points[i + 1] - points[i]).Normalized();
                var left = centre - along * flagWidth / 2; var right = centre + along * flagWidth / 2;
                var tip = centre - Vector3.Up * flagDrop;
                var baseColour = Color.FromHtml(colours[colourIndex++ % colours.Length]);
                // Horizontal bands, widest at the cord and narrowing to the tip; alternate bands use the lighter shade.
                for (var band = 0; band < bands; band++)
                {
                    float t0 = band / (float)bands, t1 = (band + 1) / (float)bands;
                    Vector3 L(float u) => left.Lerp(tip, u); Vector3 R(float u) => right.Lerp(tip, u);
                    var colour = band % 2 == 0 ? baseColour : baseColour.Lerp(Colors.White, 0.7f);
                    flags.SetColor(colour);
                    var quad = band == bands - 1
                        ? new[] { L(t0), R(t0), tip }
                        : new[] { L(t0), R(t0), R(t1), L(t0), R(t1), L(t1) };
                    foreach (var v in quad) flags.AddVertex(v);
                }
            }
        }
        flags.GenerateNormals(); cord.GenerateNormals();
        var flagMaterial = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        holder.AddChild(new MeshInstance3D { Mesh = flags.Commit(), MaterialOverride = flagMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var cordMaterial = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        holder.AddChild(new MeshInstance3D { Mesh = cord.Commit(), MaterialOverride = cordMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return holder;
    }

    /// <summary>Strings of small glowing bulbs hung along the fence, in a few warm colours (one multimesh per colour).</summary>
    private static Node3D FairyLights(float height, float sag)
    {
        var holder = new Node3D { Name = "FairyLights" };
        var hex = new[] { "#fff0c2", "#ffc46b", "#ffd9e0" };
        var positions = new List<Vector3>[] { new(), new(), new() };
        var cord = new SurfaceTool(); cord.Begin(Mesh.PrimitiveType.Triangles); cord.SetColor(Color.FromHtml("#3f5a3c"));
        var n = 0;
        foreach (var points in FenceStrings(height, sag, 24))
        {
            for (var s = 0; s < points.Length - 1; s++)
            {
                var a = points[s]; var b = points[s + 1]; var h = Vector3.Up * 0.005f;
                foreach (var v in new[] { a - h, b - h, b + h, a - h, b + h, a + h }) cord.AddVertex(v);
                // A bulb at every sample, hanging a little below the wire.
                if (s % 3 == 0) positions[n++ % 3].Add(a - Vector3.Up * 0.04f);
            }
        }
        cord.GenerateNormals();
        holder.AddChild(new MeshInstance3D { Mesh = cord.Commit(), MaterialOverride = new StandardMaterial3D { AlbedoColor = Color.FromHtml("#3f5a3c"), CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        for (var c = 0; c < 3; c++)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 8, Rings = 4 }, InstanceCount = positions[c].Count };
            for (var i = 0; i < positions[c].Count; i++) mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, positions[c][i]));
            holder.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = FestMat(hex[c], 0.3f, glow: true), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        return holder;
    }

    // ---- spring: Blossom Fair ---------------------------------------------------------------
    private void BuildSpring(Node3D root)
    {
        root.AddChild(Bunting(new[] { "#f58fb4", "#ffe680", "#9fdcbc", "#c3a8f0", "#ffffff", "#ffb08a" }, 1, 0.4f, 0.46f, 0.98f, 0.2f));
        var pots = new[] { "#f6c9d4", "#fbf3e4", "#bfe0f5" };
        var heads = new[] { "#f7a8c4", "#fff3b0", "#ffffff", "#e6b8f2" };
        var rng = new RandomNumberGenerator { Seed = 32 };
        var index = 0;
        foreach (var (angle, k) in new[] { (190f, 0.9f), (208f, 0.88f), (226f, 0.86f), (244f, 0.86f), (262f, 0.86f), (280f, 0.86f), (298f, 0.86f), (350f, 0.9f), (170f, 0.92f), (10f, 0.92f) })
        {
            var pot = new Node3D { Position = FestivalSpot(angle, k), RotationDegrees = new Vector3(0, rng.RandfRange(0, 360), 0), Scale = Vector3.One * 1.5f };
            Part(pot, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.14f, Height = 0.3f, RadialSegments = 14 }, FestMat(pots[index % 3], 0.8f), new Vector3(0, 0.15f, 0), Vector3.One);
            Part(pot, FestBall, FestMat("#7fb069", 0.9f), new Vector3(0, 0.36f, 0), new Vector3(0.44f, 0.26f, 0.44f));
            for (var f = 0; f < 6; f++)
            {
                var phi = Mathf.Tau * f / 6f + index; var r = f == 5 ? 0f : 0.14f;
                var at = new Vector3(Mathf.Cos(phi) * r, 0.43f + (f == 5 ? 0.07f : 0.02f * (f % 2)), Mathf.Sin(phi) * r);
                Part(pot, FestSmallBall, FestMat(heads[(f + index) % 4], 0.6f), at, new Vector3(0.15f, 0.11f, 0.15f));
                Part(pot, FestSmallBall, FestMat("#f2b632", 0.6f), at + new Vector3(0, 0.05f, 0), new Vector3(0.06f, 0.05f, 0.06f));
            }
            root.AddChild(pot);
            index++;
        }
    }

    // ---- summer: Summer Picnic ----------------------------------------------------------------
    private static ImageTexture Stripes(int cells, bool checker, Color a, Color b)
    {
        var image = Image.CreateEmpty(cells, checker ? cells : 1, false, Image.Format.Rgba8);
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < cells; x++)
                image.SetPixel(x, y, (checker ? (x + y) % 2 == 0 : x % 2 == 0) ? a : b);
        return ImageTexture.CreateFromImage(image);
    }

    private void BuildSummer(Node3D root)
    {
        root.AddChild(Bunting(new[] { "#ef5350", "#2f93e0", "#f6c21a", "#ef5350", "#4fb860" }, 3, 0.4f, 0.48f, 0.98f, 0.2f));
        var picnic = new Node3D { Position = new Vector3(4.9f, 0, -1.1f), RotationDegrees = new Vector3(0, -18, 0), Scale = Vector3.One * 1.25f };
        var blanket = new StandardMaterial3D { AlbedoTexture = Stripes(8, true, Color.FromHtml("#e8574f"), Color.FromHtml("#fff7ea")), TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Roughness = 0.95f };
        Part(picnic, new BoxMesh { Size = new Vector3(1.7f, 0.03f, 1.3f) }, blanket, new Vector3(0, 0.02f, 0), Vector3.One);
        // A basket with a lid, a watermelon wedge on a plate and two cups.
        Part(picnic, new BoxMesh { Size = new Vector3(0.42f, 0.24f, 0.3f) }, FestMat("#c99a62", 0.9f), new Vector3(-0.42f, 0.16f, -0.18f), Vector3.One, new Vector3(0, 15, 0));
        Part(picnic, new BoxMesh { Size = new Vector3(0.44f, 0.04f, 0.32f) }, FestMat("#b3834f", 0.9f), new Vector3(-0.42f, 0.30f, -0.18f), Vector3.One, new Vector3(0, 15, 0));
        Part(picnic, new TorusMesh { InnerRadius = 0.12f, OuterRadius = 0.14f, Rings = 16, RingSegments = 6 }, FestMat("#b3834f", 0.9f), new Vector3(-0.42f, 0.34f, -0.18f), Vector3.One, new Vector3(90, 15, 0));
        Part(picnic, FestCylinder, FestMat("#fff7ea", 0.4f), new Vector3(0.35f, 0.045f, 0.2f), new Vector3(0.34f, 0.025f, 0.34f));
        Part(picnic, new SphereMesh { Radius = 0.5f, Height = 0.5f, RadialSegments = 20, Rings = 6, IsHemisphere = true }, FestMat("#e5484d", 0.5f), new Vector3(0.35f, 0.06f, 0.2f), new Vector3(0.28f, 0.2f, 0.28f));
        Part(picnic, FestSmallBall, FestMat("#3d6b34", 0.6f), new Vector3(0.35f, 0.075f, 0.2f), new Vector3(0.3f, 0.07f, 0.3f));
        for (var i = 0; i < 2; i++)
            Part(picnic, FestCylinder, FestMat(i == 0 ? "#f6cf3a" : "#4aa3df", 0.5f), new Vector3(0.1f + i * 0.2f, 0.09f, -0.3f), new Vector3(0.08f, 0.14f, 0.08f));
        root.AddChild(picnic);
        // The parasol beside it, tilted a touch, with striped panels.
        var parasol = new Node3D { Position = picnic.Position + new Vector3(1.1f, 0, -1.2f), RotationDegrees = new Vector3(0, 0, -6) };
        Part(parasol, FestCylinder, FestMat("#f4ead8", 0.6f), new Vector3(0, 0.95f, 0), new Vector3(0.04f, 1.9f, 0.04f));
        var canopy = new StandardMaterial3D { AlbedoTexture = Stripes(12, false, Color.FromHtml("#f6a8a0"), Color.FromHtml("#fff7ea")), TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Roughness = 0.8f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        Part(parasol, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 1.0f, Height = 0.38f, RadialSegments = 12 }, canopy, new Vector3(0, 1.98f, 0), Vector3.One);
        Part(parasol, FestSmallBall, FestMat("#f6cf3a", 0.5f), new Vector3(0, 2.2f, 0), new Vector3(0.09f, 0.09f, 0.09f));
        root.AddChild(parasol);
    }

    // ---- autumn: Harvest Festival -------------------------------------------------------------------
    private static Node3D Pumpkin(float size, string hex, RandomNumberGenerator rng)
    {
        var pumpkin = new Node3D { RotationDegrees = new Vector3(0, rng.RandfRange(0, 360), 0) };
        var skin = FestMat(hex, 0.6f);
        // Three overlapping squashed lobes read as a ribbed pumpkin.
        Part(pumpkin, FestBall, skin, new Vector3(0, size * 0.36f, 0), new Vector3(size * 1.0f, size * 0.72f, size * 1.0f));
        Part(pumpkin, FestBall, skin, new Vector3(size * 0.22f, size * 0.36f, 0), new Vector3(size * 0.7f, size * 0.7f, size * 0.9f));
        Part(pumpkin, FestBall, skin, new Vector3(-size * 0.22f, size * 0.36f, 0), new Vector3(size * 0.7f, size * 0.7f, size * 0.9f));
        Part(pumpkin, FestCylinder, FestMat("#6b5a2a", 0.9f), new Vector3(0, size * 0.78f, 0), new Vector3(size * 0.1f, size * 0.2f, size * 0.1f), new Vector3(8, 0, 6));
        return pumpkin;
    }

    private static Node3D AppleBasket()
    {
        var basket = new Node3D();
        var wicker = FestMat("#b98a55", 0.95f);
        Part(basket, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.24f, Height = 0.26f, RadialSegments = 14 }, wicker, new Vector3(0, 0.13f, 0), Vector3.One);
        var rng = new RandomNumberGenerator { Seed = 3 };
        string[] apples = { "#d6382f", "#e8553a", "#9bbf4a", "#d6382f" };
        for (var i = 0; i < 9; i++)
        {
            var phi = Mathf.Tau * i / 7f; var r = i < 7 ? 0.17f : 0.0f;
            Part(basket, FestBall, FestMat(apples[i % 4], 0.45f), new Vector3(Mathf.Cos(phi) * r, i < 7 ? 0.29f : 0.38f, Mathf.Sin(phi) * r), Vector3.One * (0.17f + rng.RandfRange(-0.01f, 0.02f)));
        }
        Part(basket, new TorusMesh { InnerRadius = 0.27f, OuterRadius = 0.31f, Rings = 20, RingSegments = 6 }, wicker, new Vector3(0, 0.42f, 0), Vector3.One, new Vector3(90, 0, 0));
        return basket;
    }

    private void BuildAutumn(Node3D root)
    {
        // Warm pennants hang a little lower than in spring; leaves and pumpkins carry the colour.
        root.AddChild(Bunting(new[] { "#e0661a", "#b8281b", "#f0a81c", "#8a4a1b", "#d9822e" }, 1, 0.4f, 0.46f, 0.98f, 0.2f));
        var rng = new RandomNumberGenerator { Seed = 33 };
        var oranges = new[] { "#ee8a2f", "#e57a1f", "#f29a3f" };
        foreach (var (angle, k, count) in new[] { (200f, 0.9f, 4), (232f, 0.88f, 3), (264f, 0.88f, 3), (296f, 0.88f, 2), (340f, 0.92f, 4), (172f, 0.93f, 2) })
        {
            var cluster = new Node3D { Position = FestivalSpot(angle, k) };
            for (var i = 0; i < count; i++)
            {
                var pumpkin = Pumpkin(rng.RandfRange(0.42f, 0.8f), oranges[(i + (int)angle) % 3], rng);
                pumpkin.Position = new Vector3(Mathf.Cos(i * 2.1f) * 0.6f * (i > 0 ? 1 : 0), 0, Mathf.Sin(i * 2.1f) * 0.5f * (i > 0 ? 1 : 0));
                cluster.AddChild(pumpkin);
            }
            root.AddChild(cluster);
        }
        foreach (var (angle, k) in new[] { (248f, 0.82f), (280f, 0.84f) })
        {
            var basket = AppleBasket(); basket.Scale = Vector3.One * 1.5f; basket.Position = FestivalSpot(angle, k); basket.RotationDegrees = new Vector3(0, angle, 0);
            root.AddChild(basket);
        }
    }

    // ---- winter: Winter Lights ------------------------------------------------------------------------
    private void BuildWinter(Node3D root)
    {
        root.AddChild(FairyLights(0.98f, 0.18f));
        var snowman = new Node3D { Position = FestivalSpot(252f, 0.8f), RotationDegrees = new Vector3(0, 8, 0), Scale = Vector3.One * 1.3f };
        var snow = FestMat("#f7fbff", 0.9f);
        Part(snowman, FestBall, snow, new Vector3(0, 0.36f, 0), Vector3.One * 0.72f);
        Part(snowman, FestBall, snow, new Vector3(0, 0.9f, 0), Vector3.One * 0.56f);
        Part(snowman, FestBall, snow, new Vector3(0, 1.3f, 0), Vector3.One * 0.4f);
        var coal = FestMat("#2d2a32", 0.8f);
        foreach (var x in new[] { -0.07f, 0.07f }) Part(snowman, FestSmallBall, coal, new Vector3(x, 1.35f, 0.17f), Vector3.One * 0.04f);
        for (var i = 0; i < 3; i++) Part(snowman, FestSmallBall, coal, new Vector3(0, 0.98f - i * 0.14f, 0.26f), Vector3.One * 0.045f);
        Part(snowman, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.04f, Height = 0.22f, RadialSegments = 8 }, FestMat("#f08a3c", 0.7f), new Vector3(0, 1.3f, 0.26f), Vector3.One, new Vector3(90, 0, 0));
        // Top hat and a red scarf.
        Part(snowman, FestCylinder, FestMat("#35303d", 0.6f), new Vector3(0, 1.5f, 0), new Vector3(0.34f, 0.03f, 0.34f));
        Part(snowman, FestCylinder, FestMat("#35303d", 0.6f), new Vector3(0, 1.62f, 0), new Vector3(0.22f, 0.24f, 0.22f));
        Part(snowman, FestCylinder, FestMat("#c93a4f", 0.7f), new Vector3(0, 1.545f, 0), new Vector3(0.225f, 0.05f, 0.225f));
        Part(snowman, new TorusMesh { InnerRadius = 0.17f, OuterRadius = 0.23f, Rings = 20, RingSegments = 8 }, FestMat("#c93a4f", 0.8f), new Vector3(0, 1.12f, 0), Vector3.One);
        Part(snowman, FestCylinder, FestMat("#c93a4f", 0.8f), new Vector3(0.14f, 0.96f, 0.14f), new Vector3(0.09f, 0.3f, 0.04f), new Vector3(0, 40, 8));
        // Twig arms.
        var twig = FestMat("#6b4a35", 0.95f);
        Part(snowman, FestCylinder, twig, new Vector3(-0.42f, 1.0f, 0), new Vector3(0.03f, 0.55f, 0.03f), new Vector3(0, 0, 62));
        Part(snowman, FestCylinder, twig, new Vector3(0.42f, 1.0f, 0), new Vector3(0.03f, 0.55f, 0.03f), new Vector3(0, 0, -62));
        root.AddChild(snowman);
    }
}
