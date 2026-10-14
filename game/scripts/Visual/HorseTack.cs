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
public static partial class HorseTack
{
    private static readonly Dictionary<string, string> Colours = new(StringComparer.Ordinal)
    {
        ["rose"] = "#f3a6c0", ["mint"] = "#a9dcc4", ["sky"] = "#a8cdf0", ["lavender"] = "#c9b3ec",
        ["buttercup"] = "#f7dc7a", ["cream"] = "#fbf1df", ["gold"] = "#f2c14e", ["daisy"] = "#fff6d8",
        // D-032 festival rewards: flower crown (bow slot), sunflower tail bow, harvest braids, fairy-light halter.
        ["blossom"] = "#f6b8cf", ["sunflower"] = "#f5c518", ["harvest"] = "#e0762a", ["fairy"] = "#fff1c9",
    };

    // D-033: slot-specific palettes (turnout rugs, halters and braid ribbons use their own, truer colours).
    private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new(StringComparer.Ordinal)
    {
        ["blanket"] = new(StringComparer.Ordinal)
        {
            ["navy"] = "#2f4678", ["forest"] = "#2f6b4a", ["cranberry"] = "#9c2f4f", ["lavender"] = "#b9a2e0", ["cream"] = "#f6ecd6", ["snowflake"] = "#f4f8ff",
        },
        ["halter"] = new(StringComparer.Ordinal)
        {
            ["pink"] = "#f08fb4", ["sky"] = "#5aa8e8", ["mint"] = "#4fc995", ["red"] = "#d6403f", ["purple"] = "#8c62c9", ["fairy"] = "#fff1c9", // D-032 fairy-light halter
        },
        ["braids"] = new(StringComparer.Ordinal)
        {
            ["button"] = "#8a6a4a", ["pink"] = "#f27fab", ["white"] = "#ffffff", ["blue"] = "#6fb2ea", ["harvest"] = "#e0762a", // D-032 harvest braids
        },
    };

    private static readonly Dictionary<string, string> BlanketTrim = new(StringComparer.Ordinal)
    {
        ["navy"] = "#e9dfc8", ["forest"] = "#e9dfc8", ["cranberry"] = "#efe3d0", ["lavender"] = "#ffffff", ["cream"] = "#c9b186", ["snowflake"] = "#9fc6ee",
    };

    private static bool Known(string slot, string colour) =>
        Palettes.TryGetValue(slot, out var palette) ? palette.ContainsKey(colour) : Colours.ContainsKey(colour);

    private sealed record BackSurface(float Top, float HalfWidth);
    private static readonly Dictionary<string, BackSurface> SurfaceCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<Vector3>> VertexCache = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, Vector3[]> FestivalVertexCache = new(StringComparer.Ordinal);

    public static void ReleaseCaches() { SurfaceCache.Clear(); VertexCache.Clear(); FestivalVertexCache.Clear(); }

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
            if (Known(pair[0], colour)) worn[pair[0]] = colour;
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
        // D-033: the foal rig's "Chest" bone sits behind its pelvis, so face the way the head is.
        if (Rest(head).Dot(forward) < Rest(pelvis).Dot(forward)) forward = -forward;
        var side = forward.Cross(up).Normalized();
        var frame = new Basis(forward, up, side);

        // A turnout rug covers the saddle pad, so the pad is only hidden (both stay stored).
        if (worn.TryGetValue("pad", out var padColour) && body != null && !worn.ContainsKey("blanket"))
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

        if (worn.TryGetValue("bow", out var bowColour) && bowColour != "blossom")
        {
            var ears = earL >= 0 && earR >= 0 ? (Rest(earL) + Rest(earR)) / 2 : Rest(head) + up * 0.12f;
            // On top of the poll, just behind the ears, so it reads from the side and the front.
            var at = ears - forward * 0.05f + up * 0.035f;
            var size = earL >= 0 && earR >= 0 ? Rest(earL).DistanceTo(Rest(earR)) : 0.16f;
            Attach(skeleton, head, Global(head), Bow(Colour(bowColour), Mathf.Clamp(size * 1.7f, 0.14f, 0.42f), bowColour == "gold"),
                new Transform3D(frame, at));
        }

        // D-033 pieces (harvest braids included); the D-032 fairy halter, flower crown and sunflower tail bow have their own builders below.
        bool Ordinary(string slot, string festivalColour) => worn.TryGetValue(slot, out var c) && c != festivalColour;
        if (worn.ContainsKey("blanket") || Ordinary("halter", "fairy") || worn.ContainsKey("braids") || Ordinary("tail", "sunflower"))
        {
            var context = new TackContext(skeleton, Global, frame, forward, up, side, body == null ? new List<Vector3>() : BodyVertices(body, skeleton, cacheKey),
                Rest(chest).DistanceTo(Rest(pelvis)), Bone, HairColour(model),
                worn.ContainsKey("braids") ? ManeVertices(model, skeleton, cacheKey) : new List<Vector3>());
            if (worn.TryGetValue("blanket", out var blanketColour)) AddBlanket(context, blanketColour, chest, pelvis, back, bodyWidth);
            if (Ordinary("halter", "fairy") && worn.TryGetValue("halter", out var halterColour)) AddHalter(context, halterColour, head, earL, earR);
            if (worn.TryGetValue("braids", out var braidsColour)) AddBraids(context, braidsColour); // includes the D-032 harvest ribbons
            if (Ordinary("tail", "sunflower") && worn.TryGetValue("tail", out var tailColour)) AddTailBow(context, tailColour);
        }
        // D-032 festival rewards. The head and neck ones are sized from the body's own vertices, so every family and shape fits.
        var crownOn = worn.TryGetValue("bow", out var crownColour) && crownColour == "blossom";
        var fairyOn = worn.TryGetValue("halter", out var fairyColour) && fairyColour == "fairy";
        if ((crownOn || fairyOn) && body != null)
        {
            var shape = MeasureHead(Vertices(body, skeleton, cacheKey), Rest(head), forward, up, side);
            if (crownOn) Attach(skeleton, head, Global(head), FlowerCrown(shape, up, side), new Transform3D(Basis.Identity, shape.Section(0.15f).Centre));
            if (fairyOn) Attach(skeleton, head, Global(head), FairyHalter(shape, side), new Transform3D(Basis.Identity, Vector3.Zero));
        }

        if (worn.TryGetValue("tail", out var sunflowerColour) && sunflowerColour == "sunflower")
        {
            // Tied on the tail hair about a third of the way down the tail's bones (the dock is Tail_01).
            var tailBones = new[] { "Tail_01", "Tail_02", "Tail_03", "Tail_04", "Tail_05", "Tail_06", "Tail_07" }.Select(n => skeleton.FindBone(n)).Where(i => i >= 0).ToArray();
            if (tailBones.Length >= 2)
            {
                var f = 0.38f * (tailBones.Length - 1); var i0 = Mathf.FloorToInt(f);
                var knot = Rest(tailBones[i0]).Lerp(Rest(tailBones[Mathf.Min(tailBones.Length - 1, i0 + 1)]), f - i0);
                var scale = Mathf.Clamp(Rest(chest).DistanceTo(Rest(pelvis)) * 0.42f, 0.2f, 0.5f);
                Attach(skeleton, tailBones[i0], Global(tailBones[i0]), SunflowerBow(scale), new Transform3D(frame, knot));
            }
        }
    }

    private static Vector3[] Vertices(MeshInstance3D body, Skeleton3D skeleton, string cacheKey)
    {
        if (FestivalVertexCache.TryGetValue(cacheKey, out var known)) return known;
        var toSkeleton = skeleton.GlobalTransform.AffineInverse() * body.GlobalTransform;
        var list = new List<Vector3>();
        if (body.Mesh is { } mesh)
            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
                foreach (var vertex in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) list.Add(toSkeleton * vertex);
        return FestivalVertexCache[cacheKey] = list.ToArray();
    }

    /// <summary>The head from the poll to the nose (skeleton space): its axis, length and cross-sections.</summary>
    private sealed class HeadShape
    {
        public HeadShape(Vector3[] vertices, Vector3 poll, Vector3 axis, float length) { Vertices = vertices; Poll = poll; Axis = axis; Length = length; }
        public Vector3[] Vertices { get; }
        public Vector3 Poll { get; }
        public Vector3 Axis { get; }
        public float Length { get; }

        /// <summary>Centre and radius of the head at fraction t of the way from the poll to the nose.</summary>
        public (Vector3 Centre, float Radius) Section(float t)
        {
            var at = Length * t; var sum = Vector3.Zero; var slab = new List<Vector3>();
            foreach (var v in Vertices)
            {
                var d = v - Poll;
                if (Mathf.Abs(d.Dot(Axis) - at) > Length * 0.035f || (d - Axis * d.Dot(Axis)).Length() > Length * 0.4f) continue;
                slab.Add(v); sum += v;
            }
            if (slab.Count == 0) return (Poll + Axis * at, Length * 0.15f);
            var centre = sum / slab.Count;
            var distances = slab.Select(v => (v - centre - Axis * (v - centre).Dot(Axis)).Length()).OrderBy(x => x).ToArray();
            return (centre, Mathf.Max(0.035f, distances[(int)(distances.Length * 0.85f)]));
        }
    }

    private static HeadShape MeasureHead(Vector3[] vertices, Vector3 poll, Vector3 forward, Vector3 up, Vector3 side)
    {
        // The head is what lies near the poll, within reach and not far below it: the nose is its most forward vertex.
        var nose = poll; var best = 0f;
        foreach (var v in vertices)
        {
            var d = v - poll;
            if (d.Dot(up) < -0.55f || d.Length() > 0.95f || Mathf.Abs(d.Dot(side)) > 0.15f) continue;
            if (d.Dot(forward) > best) { best = d.Dot(forward); nose = v; }
        }
        if (best < 0.2f) { nose = poll + forward * 0.4f - up * 0.2f; }
        return new HeadShape(vertices, poll, (nose - poll).Normalized(), poll.DistanceTo(nose));
    }

    private static readonly Dictionary<string, StandardMaterial3D> FestivalMaterials = new(StringComparer.Ordinal);
    private static StandardMaterial3D Fabric(string hex, bool glow = false, float roughness = 0.7f)
    {
        var key = hex + (glow ? "!" : "");
        if (FestivalMaterials.TryGetValue(key, out var known)) return known;
        var material = new StandardMaterial3D { AlbedoColor = Color.FromHtml(hex), Roughness = roughness };
        if (glow) { material.EmissionEnabled = true; material.Emission = Color.FromHtml(hex); material.EmissionEnergyMultiplier = 2.2f; }
        return FestivalMaterials[key] = material;
    }

    private static readonly SphereMesh Ball = new() { Radius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 6 };

    private static MeshInstance3D Piece(Node parent, Mesh mesh, StandardMaterial3D material, Vector3 position, Basis basis)
    {
        var part = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position, Basis = basis, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(part);
        return part;
    }

    /// <summary>Scales a rotated basis along its own axes (Basis.Scaled would scale in the parent's frame and shear).</summary>
    private static Basis Local(Basis rotation, Vector3 scale) => rotation * Basis.FromScale(scale);

    /// <summary>A basis whose Y axis is <paramref name="y"/> and whose Z leans toward <paramref name="hint"/>.</summary>
    private static Basis BasisFromY(Vector3 y, Vector3 hint)
    {
        y = y.Normalized();
        var z = (hint - y * hint.Dot(y)).Normalized();
        return new Basis(y.Cross(z), y, z);
    }

    /// <summary>A crown of blossoms across the forehead, in front of the ears: pastel pink, yellow and white flowers with leaves.</summary>
    private static Node3D FlowerCrown(HeadShape shape, Vector3 up, Vector3 side)
    {
        var crown = new Node3D { Name = "TackFlowerCrown" };
        var (_, measured) = shape.Section(0.15f);
        var radius = Mathf.Min(measured, shape.Length * 0.2f); // a foal's smaller head keeps the crown from becoming a wreath
        var tilt = (up - shape.Axis * up.Dot(shape.Axis)).Normalized(); // the head's own "up", perpendicular to its axis
        var across = tilt.Cross(shape.Axis).Normalized();
        var petals = new[] { "#f7a8c4", "#fff3b0", "#ffffff" };
        const int flowers = 9;
        var size = Mathf.Clamp(radius * 0.62f, 0.035f, 0.075f);
        for (var i = 0; i < flowers * 2 - 1; i++)
        {
            var theta = Mathf.DegToRad(-105 + i * 210f / (flowers * 2 - 2));
            var outward = tilt * Mathf.Cos(theta) + across * Mathf.Sin(theta);
            var position = outward * (radius * 1.02f);
            var basis = BasisFromY(outward, shape.Axis);
            if (i % 2 == 0)
            {
                var flower = new Node3D { Position = position, Basis = basis };
                var petal = Fabric(petals[(i / 2) % 3], roughness: 0.6f);
                for (var p = 0; p < 5; p++)
                {
                    var phi = Mathf.Tau * p / 5f;
                    Piece(flower, Ball, petal, new Vector3(Mathf.Cos(phi), 0.1f, Mathf.Sin(phi)) * size * 0.34f, Basis.Identity.Scaled(new Vector3(size * 0.62f, size * 0.34f, size * 0.62f)));
                }
                Piece(flower, Ball, Fabric("#f2b632"), new Vector3(0, size * 0.1f, 0), Basis.Identity.Scaled(Vector3.One * size * 0.42f));
                crown.AddChild(flower);
            }
            else Piece(crown, Ball, Fabric("#7fb069"), position - outward * size * 0.1f, Local(basis, new Vector3(size * 0.95f, size * 0.32f, size * 0.55f)));
        }
        return crown;
    }

    /// <summary>A halter strung with tiny glowing beads: a noseband, a ring behind the cheeks and two cheek straps.</summary>
    private static Node3D FairyHalter(HeadShape shape, Vector3 side)
    {
        var halter = new Node3D { Name = "TackFairyHalter" };
        var webbing = Fabric("#f3e3c6", roughness: 0.8f); var glow = Fabric("#ffe7a0", glow: true, roughness: 0.4f);
        var (nose, noseRadius) = shape.Section(0.8f); var (cheek, cheekRadius) = shape.Section(0.2f);
        cheekRadius = Mathf.Min(cheekRadius, shape.Length * 0.27f); noseRadius = Mathf.Min(noseRadius, shape.Length * 0.17f); // ears and jowls must not balloon the straps on a foal
        noseRadius *= 1.06f; cheekRadius *= 1.04f;
        var basis = BasisFromY(shape.Axis, side);
        foreach (var (centre, radius, beads) in new[] { (nose, noseRadius, 14), (cheek, cheekRadius, 20) })
        {
            Piece(halter, new TorusMesh { InnerRadius = radius - 0.007f, OuterRadius = radius + 0.007f, Rings = 24, RingSegments = 8 }, webbing, centre, basis);
            var bead = Mathf.Clamp(radius * 0.11f, 0.006f, 0.012f);
            for (var i = 0; i < beads; i++)
            {
                var phi = Mathf.Tau * i / beads;
                Piece(halter, Ball, glow, centre + basis * new Vector3(Mathf.Cos(phi), 0, Mathf.Sin(phi)) * (radius + 0.004f), Basis.Identity.Scaled(Vector3.One * bead * 2f));
            }
        }
        // Cheek straps run from the ring to the noseband on both sides.
        var lateral = (side - shape.Axis * side.Dot(shape.Axis)).Normalized();
        foreach (var sign in new[] { -1f, 1f })
        {
            var a = cheek + lateral * sign * cheekRadius; var b = nose + lateral * sign * noseRadius;
            var length = a.DistanceTo(b);
            Piece(halter, new BoxMesh { Size = new Vector3(0.012f, 0.012f, length) }, webbing, (a + b) / 2, Basis.LookingAt((b - a).Normalized(), Vector3.Up));
            for (var i = 1; i <= 4; i++)
                Piece(halter, Ball, glow, a.Lerp(b, i / 5f) + lateral * sign * 0.006f, Basis.Identity.Scaled(Vector3.One * 0.014f));
        }
        return halter;
    }

    /// <summary>A yellow ribbon bow with a sunflower on each side, hung on the tail a little below the dock.</summary>
    private static Node3D SunflowerBow(float scale)
    {
        var root = new Node3D { Name = "TackSunflowerBow" };
        var ribbon = Fabric("#f6cf3a", roughness: 0.55f); var brown = Fabric("#5a3a1e", roughness: 0.9f); var petal = Fabric("#f5b800", roughness: 0.6f);
        // Two loops along the tail, a knot and two hanging ends.
        Piece(root, Ball, ribbon, new Vector3(0.55f, 0.18f, 0) * scale * 0.5f, Local(Basis.Identity.Rotated(Vector3.Back, Mathf.DegToRad(-28)), new Vector3(0.62f, 0.3f, 0.32f) * scale));
        Piece(root, Ball, ribbon, new Vector3(-0.55f, 0.18f, 0) * scale * 0.5f, Local(Basis.Identity.Rotated(Vector3.Back, Mathf.DegToRad(28)), new Vector3(0.62f, 0.3f, 0.32f) * scale));
        Piece(root, Ball, ribbon, Vector3.Zero, Basis.Identity.Scaled(Vector3.One * scale * 0.3f));
        Piece(root, new BoxMesh { Size = Vector3.One }, ribbon, new Vector3(0.06f, -0.2f, 0) * scale, Local(Basis.Identity.Rotated(Vector3.Back, Mathf.DegToRad(12)), new Vector3(0.12f, 0.5f, 0.2f) * scale));
        Piece(root, new BoxMesh { Size = Vector3.One }, ribbon, new Vector3(-0.1f, -0.2f, 0) * scale, Local(Basis.Identity.Rotated(Vector3.Back, Mathf.DegToRad(-12)), new Vector3(0.12f, 0.5f, 0.2f) * scale));
        foreach (var sign in new[] { -1f, 1f })
        {
            var flower = new Node3D { Position = new Vector3(0, 0.04f * scale, sign * 0.3f * scale) };
            for (var p = 0; p < 12; p++)
            {
                var phi = Mathf.Tau * p / 12f;
                var dir = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0);
                Piece(flower, Ball, petal, dir * scale * 0.2f + new Vector3(0, 0, sign * 0.01f * scale),
                    Local(Basis.Identity.Rotated(Vector3.Back, phi), new Vector3(0.22f, 0.09f, 0.04f) * scale));
            }
            Piece(flower, Ball, brown, new Vector3(0, 0, sign * 0.02f * scale), Basis.Identity.Scaled(new Vector3(0.25f, 0.25f, 0.1f) * scale));
            root.AddChild(flower);
        }
        return root;
    }

    /// <summary>The swatch colour, a little deeper so pastel fabric still reads under bright yard light.</summary>
    internal static Color Colour(string name, string? slot = null)
    {
        if (slot != null && Palettes.TryGetValue(slot, out var palette) && palette.TryGetValue(name, out var exact)) return Color.FromHtml(exact);
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

    internal static void Attach(Skeleton3D skeleton, int index, Transform3D boneAtBind, Node3D piece, Transform3D skeletonSpace)
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
    internal static Node3D Bow(Color colour, float size, bool shiny)
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

    internal static T? Find<T>(Node node, Func<T, bool>? match = null) where T : Node
    {
        if (node is T hit && (match == null || match(hit))) return hit;
        foreach (var child in node.GetChildren())
            if (Find(child, match) is { } found) return found;
        return null;
    }
}
