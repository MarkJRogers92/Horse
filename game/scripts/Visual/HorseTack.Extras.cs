using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// D-033 dress-up pieces: turnout rug, halter, braids and tail bow. Like the D-020 pad and bow they are measured from
/// the rig's bind pose and the body's own vertices (all in skeleton space), then parented to a bone.
/// </summary>
public static partial class HorseTack
{
    /// <summary>Everything the extra pieces share: rig frame (forward, up, side), body vertices and bone lookups.</summary>
    private sealed record TackContext(Skeleton3D Skeleton, Func<int, Transform3D> Global, Basis Frame, Vector3 Forward, Vector3 Up, Vector3 Side,
        List<Vector3> Verts, float BodyLength, Func<string[], int> Bone, Color Hair, List<Vector3> ManeVerts)
    {
        public Vector3 Rest(int bone) => Global(bone).Origin;
        public int Find(params string[] names) => Bone(names);
        public float F(Vector3 v) => v.Dot(Forward);
        public float U(Vector3 v) => v.Dot(Up);
        public float S(Vector3 v) => v.Dot(Side);
        public Vector3 At(float f, float u, float s) => Forward * f + Up * u + Side * s;
    }

    private static List<Vector3> BodyVertices(MeshInstance3D body, Skeleton3D skeleton, string cacheKey)
    {
        if (VertexCache.TryGetValue(cacheKey, out var known)) return known;
        var toSkeleton = skeleton.GlobalTransform.AffineInverse() * body.GlobalTransform;
        var verts = new List<Vector3>();
        if (body.Mesh is { } mesh)
            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
                foreach (var vertex in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) verts.Add(toSkeleton * vertex);
        return VertexCache[cacheKey] = verts;
    }

    private static List<Vector3> ManeVertices(Node3D model, Skeleton3D skeleton, string cacheKey)
    {
        var key = cacheKey + "#mane";
        if (VertexCache.TryGetValue(key, out var known)) return known;
        var verts = new List<Vector3>();
        if (Find<MeshInstance3D>(model, m => m.Name == "Mane") is { Mesh: { } mesh } mane)
        {
            var toSkeleton = skeleton.GlobalTransform.AffineInverse() * mane.GlobalTransform;
            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
                foreach (var vertex in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) verts.Add(toSkeleton * vertex);
        }
        return VertexCache[key] = verts;
    }

    /// <summary>The mane's own colour (set by the presenter on the hair shader), so braids match it.</summary>
    private static Color HairColour(Node3D model)
    {
        if (Find<MeshInstance3D>(model, m => m.Name == "Mane") is { } mane && mane.GetSurfaceOverrideMaterial(0) is ShaderMaterial material
            && material.GetShaderParameter("hair_color").VariantType == Variant.Type.Color)
            return material.GetShaderParameter("hair_color").AsColor();
        return new Color(0.28f, 0.2f, 0.14f);
    }

    private static StandardMaterial3D Fabric(Color colour, float roughness = 0.8f, float metallic = 0f) =>
        new() { AlbedoColor = colour, Roughness = roughness, Metallic = metallic, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

    private static Color Darker(Color c, float k) => new(c.R * k, c.G * k, c.B * k);

    // ---------------------------------------------------------------- blanket

    /// <summary>
    /// A turnout rug: for each slice behind the withers, the body's own half-width is read at several depths below
    /// the topline, so the rug hugs the back and ribs and stops at about mid-barrel.
    /// </summary>
    private static void AddBlanket(TackContext c, string colourName, int chest, int pelvis, int back, float bodyWidth)
    {
        if (c.Verts.Count == 0) return;
        const int slices = 16, depths = 8;
        // The foal rig labels its bones the other way round, so order the two barrel anchors by where they really are.
        var chestP = c.Rest(chest); var pelvisP = c.Rest(pelvis);
        if (c.F(chestP) < c.F(pelvisP)) (chestP, pelvisP) = (pelvisP, chestP);
        float fChest = c.F(chestP), fPelvis = c.F(pelvisP), span = fChest - fPelvis;
        float fFront = fPelvis + span * 0.92f, fRear = fPelvis - span * 0.22f;
        float sCentre = c.S(chestP);
        // The body morphs are not in the rest vertices: wide and muscular shapes swell out past them, lean ones sit inside.
        float grow = bodyWidth > 0f ? bodyWidth * 0.03f : bodyWidth * 0.008f;

        float TopAt(float f)
        {
            var top = float.MinValue;
            foreach (var v in c.Verts)
                if (Mathf.Abs(c.F(v) - f) < 0.035f && Mathf.Abs(c.S(v) - sCentre) < 0.07f) top = Mathf.Max(top, c.U(v));
            return top;
        }
        // Barrel height from the middle of the rug: topline to the belly line.
        float fMid = (fFront + fRear) / 2f, topMid = TopAt(fMid), belly = topMid;
        foreach (var v in c.Verts)
            if (Mathf.Abs(c.F(v) - fMid) < 0.05f && Mathf.Abs(c.S(v) - sCentre) < 0.05f) belly = Mathf.Min(belly, c.U(v));
        float drape = Mathf.Clamp((topMid - belly) * 0.68f, 0.18f, 0.7f);

        var tops = new float[slices + 1]; var width = new float[slices + 1, depths + 1];
        for (var i = 0; i <= slices; i++)
        {
            float f = Mathf.Lerp(fFront, fRear, i / (float)slices);
            tops[i] = TopAt(f);
            var near = c.Verts.Where(v => Mathf.Abs(c.F(v) - f) < 0.035f).ToList();
            for (var k = 0; k <= depths; k++)
            {
                float level = tops[i] - drape * k / depths, w = k > 0 ? width[i, k - 1] : 0.02f;
                foreach (var v in near)
                    if (Mathf.Abs(c.U(v) - level) < 0.018f) w = Mathf.Max(w, Mathf.Abs(c.S(v) - sCentre));
                width[i, k] = w;
            }
        }
        // Smooth along the length and never let the rug narrow as it goes down.
        var smooth = new float[slices + 1, depths + 1]; var smoothTop = new float[slices + 1];
        for (var i = 0; i <= slices; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(slices, i + 1);
            smoothTop[i] = (tops[a] + tops[i] + tops[b]) / 3f;
            for (var k = 0; k <= depths; k++)
                smooth[i, k] = (width[a, k] + width[i, k] + width[b, k]) / 3f;
        }
        for (var i = 0; i <= slices; i++)
            for (var k = 1; k <= depths; k++) smooth[i, k] = Mathf.Max(smooth[i, k], smooth[i, k - 1]);

        const float off = 0.016f;
        Vector3 Point(int i, int column)
        {
            // Columns run from the left hem, over the top, to the right hem.
            int k = column <= depths ? depths - column : column - depths - 1;
            float sign = column <= depths ? -1f : 1f;
            float f = Mathf.Lerp(fFront, fRear, i / (float)slices);
            float edge = Mathf.Abs(i / (float)slices - 0.5f) * 2f;
            float hemLift = drape * 0.22f * Mathf.Pow(edge, 3f); // the corners curve up a little
            float depth = drape * k / depths - (k == depths ? hemLift : k == depths - 1 ? hemLift * 0.5f : 0f);
            float sideValue = smooth[i, k] + off + grow;
            return c.At(f, smoothTop[i] - depth + off * (k == 0 ? 1f : 0.35f) + Mathf.Max(0f, grow) * (k == 0 ? 0.7f : 0.3f), sCentre + sign * (k == 0 ? Mathf.Min(sideValue, 0.04f + off) : sideValue));
        }

        var body = Colour(colourName, "blanket"); var trim = Color.FromHtml(BlanketTrim.GetValueOrDefault(colourName, "#ffffff"));
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int columns = (depths + 1) * 2;
        for (var i = 0; i < slices; i++)
            for (var j = 0; j < columns - 1; j++)
            {
                if (j == depths) { /* the quad across the topline stays part of the rug */ }
                int kRow = j < depths ? depths - j - 1 : j - depths; // depth of the quad's upper row
                bool hem = kRow >= depths - 1 || i == 0 || i == slices - 1;
                st.SetColor(hem ? trim : body);
                var p00 = Point(i, j); var p10 = Point(i + 1, j); var p11 = Point(i + 1, j + 1); var p01 = Point(i, j + 1);
                st.AddVertex(p00); st.AddVertex(p10); st.AddVertex(p11);
                st.AddVertex(p00); st.AddVertex(p11); st.AddVertex(p01);
            }
        st.GenerateNormals();
        var material = Fabric(Colors.White, 0.9f);
        material.VertexColorUseAsAlbedo = true; material.VertexColorIsSrgb = true;
        var rug = new Node3D { Name = "TackBlanket" };
        rug.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });

        if (colourName == "snowflake")
        {
            // Pale blue dots on the flanks.
            var dot = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 5 };
            var dotMaterial = Fabric(trim, 0.8f);
            for (var i = 2; i < slices - 1; i += 3)
                for (var k = 2; k < depths - 1; k += 3)
                    foreach (var column in new[] { depths - k, depths + 1 + k })
                    {
                        if (((i + k) & 1) == 0) continue;
                        var at = Point(i, column);
                        var outward = (at - c.At(c.F(at), c.U(at), sCentre)).Normalized();
                        rug.AddChild(new MeshInstance3D { Mesh = dot, MaterialOverride = dotMaterial, Position = at + outward * 0.002f, Scale = new Vector3(0.045f, 0.045f, 0.045f) * Mathf.Max(0.8f, c.BodyLength) });
                    }
        }

        var centreBone = Enumerable.Range(0, c.Skeleton.GetBoneCount())
            .Where(i => c.Skeleton.GetBoneName(i).ToString().ToLowerInvariant() is var n && (n.Contains("spine") || n.Contains("ribcage") || n == "chest"))
            .DefaultIfEmpty(back).OrderBy(i => c.Rest(i).DistanceTo(c.At(fMid, c.U(c.Rest(back)), sCentre))).First();
        Attach(c.Skeleton, centreBone, c.Global(centreBone), rug, Transform3D.Identity);
    }

    // ---------------------------------------------------------------- halter

    private readonly record struct HeadSlice(Vector3 Centre, float Up, float Side);

    private static void AddHalter(TackContext c, string colourName, int head, int earL, int earR)
    {
        if (c.Verts.Count == 0) return;
        int nose = c.Find("Head_Nose", "nose");
        if (nose < 0) { GD.Print("[tack] no nose bone for halter"); return; }
        var origin = c.Rest(head);
        // The "nose" bone sits mid-head, so the axis runs from the poll to the muzzle tip read from the body surface.
        var noseP = c.Verts.Where(v => c.U(v) > origin.Dot(c.Up) - 0.65f * c.BodyLength && Mathf.Abs(c.S(v) - c.S(origin)) < 0.1f && c.F(v) > c.F(origin))
            .OrderByDescending(c.F).DefaultIfEmpty(c.Rest(nose)).First();
        var axisVector = noseP - origin; float length = axisVector.Length();
        var axis = axisVector / length;
        var vertical = (c.Up - axis * c.Up.Dot(axis)).Normalized();
        Dictionary<int, HeadSlice> cache = new(); // explicit type: a captured "var" local reads as maybe-null inside the local function
        HeadSlice Slice(float t)
        {
            int key = (int)MathF.Round(t * 1000f);
            if (cache.TryGetValue(key, out var known)) return known;
            float lo = float.MaxValue, hi = float.MinValue, wide = 0f; int n = 0;
            foreach (var v in c.Verts)
            {
                var d = v - origin; float along = d.Dot(axis);
                if (Mathf.Abs(along - t) > 0.02f) continue;
                var radial = d - axis * along;
                if (radial.Length() > 0.3f * length) continue;
                float vv = radial.Dot(vertical), sv = Mathf.Abs(radial.Dot(c.Side));
                lo = Mathf.Min(lo, vv); hi = Mathf.Max(hi, vv); wide = Mathf.Max(wide, sv); n++;
            }
            var result = n == 0 ? new HeadSlice(origin + axis * t, 0.08f, 0.06f)
                : new HeadSlice(origin + axis * t + vertical * ((lo + hi) / 2f), (hi - lo) / 2f, wide);
            return cache[key] = result;
        }
        Vector3 Around(float t, float angle, float lift)
        {
            var s = Slice(t); float pad = length * 0.008f;
            return s.Centre + vertical * ((s.Up + pad) * Mathf.Cos(angle)) + c.Side * ((s.Side + pad) * Mathf.Sin(angle)) + axis * lift;
        }

        var earMid = earL >= 0 && earR >= 0 ? (c.Rest(earL) + c.Rest(earR)) / 2f : origin;
        float tCrown = (earMid - origin).Dot(axis) - length * 0.06f, tNose = length * 0.74f;
        var material = Fabric(Colour(colourName, "halter"), 0.7f);
        var metal = Fabric(new Color(0.95f, 0.8f, 0.45f), 0.3f, 0.7f);
        float radius = Mathf.Clamp(length * 0.02f, 0.006f, 0.014f);
        var halter = new Node3D { Name = "TackHalter" };

        List<Vector3> Ring(float t) => Enumerable.Range(0, 24).Select(i => Around(t, i / 24f * Mathf.Tau, 0f)).ToList();
        halter.AddChild(Tube(Ring(tNose), radius * 1.15f, material, true, axis));
        halter.AddChild(Tube(Ring(tCrown), radius, material, true, axis));
        foreach (var sign in new[] { -1f, 1f })
        {
            var cheek = Enumerable.Range(0, 8).Select(i =>
            {
                float t = Mathf.Lerp(tNose, tCrown, i / 7f);
                var s = Slice(t);
                return s.Centre + vertical * (s.Up * 0.06f) + c.Side * (sign * (s.Side + length * 0.008f)) ;
            }).ToList();
            halter.AddChild(Tube(cheek, radius, material, false, c.Up));
            foreach (var end in new[] { cheek[0], cheek[^1] })
                halter.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = radius * 1.4f, Height = radius * 2.8f, RadialSegments = 10, Rings = 5 }, MaterialOverride = metal, Position = end });
        }
        Attach(c.Skeleton, head, c.Global(head), halter, Transform3D.Identity);
    }

    /// <summary>A round-section tube swept along a polyline.</summary>
    private static MeshInstance3D Tube(IReadOnlyList<Vector3> points, float radius, Material material, bool closed, Vector3 reference)
    {
        const int sides = 7;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int count = points.Count, segments = closed ? count : count - 1;
        var rings = new List<Vector3[]>();
        for (var i = 0; i < count; i++)
        {
            var tangent = closed ? points[(i + 1) % count] - points[(i - 1 + count) % count] : points[Math.Min(count - 1, i + 1)] - points[Math.Max(0, i - 1)];
            tangent = tangent.Normalized();
            var ref2 = Mathf.Abs(tangent.Dot(reference.Normalized())) > 0.95f ? Vector3.Up : reference.Normalized();
            var n1 = tangent.Cross(ref2).Normalized(); var n2 = tangent.Cross(n1).Normalized();
            rings.Add(Enumerable.Range(0, sides).Select(j => points[i] + (n1 * Mathf.Cos(j / (float)sides * Mathf.Tau) + n2 * Mathf.Sin(j / (float)sides * Mathf.Tau)) * radius).ToArray());
        }
        for (var i = 0; i < segments; i++)
        {
            var r0 = rings[i]; var r1 = rings[(i + 1) % count];
            for (var j = 0; j < sides; j++)
            {
                int j1 = (j + 1) % sides;
                st.AddVertex(r0[j]); st.AddVertex(r1[j]); st.AddVertex(r1[j1]);
                st.AddVertex(r0[j]); st.AddVertex(r1[j1]); st.AddVertex(r0[j1]);
            }
        }
        st.GenerateNormals();
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
    }

    // ---------------------------------------------------------------- braids

    private static void AddBraids(TackContext c, string ribbonName)
    {
        // The neck chain, from the withers to the poll; each rig names it differently.
        var chain = new List<int>();
        foreach (var names in new[]
        {
            new[] { "Neck_Base", "ribcage_front" }, new[] { "Neck", "spine_5" }, new[] { "Neck_Mid", "spine_6" }, new[] { "Neck_Upper" }, new[] { "Neck_Top" }, new[] { "Head_Base" },
        })
        {
            var bone = c.Find(names);
            if (bone >= 0 && !chain.Contains(bone)) chain.Add(bone);
        }
        int headBone = c.Find("Head", "head");
        if (headBone >= 0 && chain.Count < 3 || chain.Count < 2) chain.Add(headBone);
        chain = chain.Where(b => b >= 0).Distinct().ToList();
        if (chain.Count < 2) { GD.Print("[tack] no neck chain for braids"); return; }
        var path = chain.Select(c.Rest).ToList();
        float total = 0f; for (var i = 1; i < path.Count; i++) total += path[i].DistanceTo(path[i - 1]);
        Vector3 Along(float distance, out Vector3 tangent)
        {
            for (var i = 1; i < path.Count; i++)
            {
                float segment = path[i].DistanceTo(path[i - 1]);
                if (distance <= segment || i == path.Count - 1)
                {
                    tangent = (path[i] - path[i - 1]).Normalized();
                    return path[i - 1] + tangent * Mathf.Min(distance, segment);
                }
                distance -= segment;
            }
            tangent = Vector3.Forward; return path[^1];
        }

        const int count = 8;
        float radius = Mathf.Clamp(total * 0.055f, 0.022f, 0.06f);
        var crestVerts = c.Verts.Concat(c.ManeVerts).ToList();
        var hair = Darker(c.Hair, 0.78f);
        var hairMaterial = Fabric(hair, 0.85f);
        var knob = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 6 };
        var byBone = new Dictionary<int, Node3D>();
        float sCentre = c.S(path[0]);
        for (var i = 0; i < count; i++)
        {
            float distance = total * Mathf.Lerp(0.04f, 0.97f, i / (float)(count - 1));
            var p = Along(distance, out var tangent);
            var neckUp = (c.Up - tangent * c.Up.Dot(tangent)).Normalized();
            // The crest: the body vertex furthest "up the neck" in a thin slice around this point.
            var crest = p + neckUp * 0.14f; var best = float.MinValue;
            foreach (var v in crestVerts)
            {
                var d = v - p;
                if (Mathf.Abs(d.Dot(tangent)) > 0.03f || Mathf.Abs(c.S(v) - sCentre) > 0.05f) continue;
                float h = d.Dot(neckUp);
                if (h > best && h < 0.5f) { best = h; crest = v; }
            }
            var at = crest + neckUp * radius * 0.2f;
            int carrier = chain.OrderBy(b => c.Rest(b).DistanceTo(at)).First();
            if (!byBone.TryGetValue(carrier, out var holder)) { holder = new Node3D { Name = "TackBraids" }; byBone[carrier] = holder; }
            // A knob that leans a little down the neck, with a hint of a tail below it.
            var lean = Basis.LookingAt(neckUp, c.Side);
            holder.AddChild(new MeshInstance3D
            {
                Mesh = knob, MaterialOverride = hairMaterial,
                Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(radius * 2f, radius * 2.2f, radius * 2f)), at + tangent * (radius * 0.2f)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            });
            holder.AddChild(new MeshInstance3D
            {
                Mesh = knob, MaterialOverride = hairMaterial,
                Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(radius * 1.3f, radius * 1.5f, radius * 1.3f)), at - neckUp * radius * 1.1f),
            });
            if (ribbonName != "button")
            {
                var ribbon = Fabric(Colour(ribbonName, "braids"), 0.55f);
                var bow = new MeshInstance3D { Mesh = knob, MaterialOverride = ribbon, Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(radius * 2.5f, radius * 0.9f, radius * 2.5f)), at - neckUp * radius * 0.35f) };
                holder.AddChild(bow);
                holder.AddChild(new MeshInstance3D { Mesh = knob, MaterialOverride = ribbon, Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(radius * 0.9f, radius * 1.1f, radius * 0.9f)), at + neckUp * radius * 0.9f) });
                _ = lean;
            }
        }
        foreach (var (bone, holder) in byBone) Attach(c.Skeleton, bone, c.Global(bone), holder, Transform3D.Identity);
    }

    // ---------------------------------------------------------------- tail bow

    private static void AddTailBow(TackContext c, string colourName)
    {
        int tail1 = c.Find("Tail_01", "tail_1"), tail2 = c.Find("Tail_02", "tail_2");
        if (tail1 < 0) { GD.Print("[tack] no tail bone"); return; }
        var root = c.Rest(tail1);
        var direction = tail2 >= 0 ? (c.Rest(tail2) - root).Normalized() : -c.Forward;
        float size = Mathf.Clamp(c.BodyLength * 0.34f, 0.22f, 0.42f);
        // Loops across the tail, ribbon ends hanging down along it.
        var x = (-direction).Normalized(); var z = c.Side; var y = z.Cross(x).Normalized();
        var at = root + direction * (size * 0.1f) + y * (size * 0.22f);
        Attach(c.Skeleton, tail1, c.Global(tail1), Bow(Colour(colourName), size, colourName == "gold"), new Transform3D(new Basis(x, y, z), at));
    }
}
