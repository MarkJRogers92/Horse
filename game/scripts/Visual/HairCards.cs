using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// D-039: small overlapping, skinned ribbons over the imported hair. The original Mane/Tail remain the
/// opaque coverage and tack landmarks. All construction is in the source mesh's rest-local coordinates;
/// generated siblings share its transform, skin and skeleton path, including the Walk's eight influences.
/// UV=(across, 1-rootToTip), COLOR.r is a stable per-lock random value, and COLOR.g marks generated cards.
/// The production masters face +X with +Y up. Their juvenile master is a narrow crest and short dock,
/// rather than the adults' sculpted curtain, so it receives shorter, softer locks.
/// </summary>
public static class HairCards
{
    public const int MaxCacheEntries = 64;
    public static int CachedMeshCount => Cache.Count;
    private readonly record struct Key(ArrayMesh Source, int Seed, bool Foal, bool Tail, ArrayMesh? Body,
        Transform3D BodyToSource, Skin? HairSkin, Skin? BodySkin);
    private static readonly Dictionary<Key, ArrayMesh> Cache = new();
    private static readonly Queue<Key> Order = new();
    public static void ReleaseCache() { Cache.Clear(); Order.Clear(); }

    /// <summary>Add the card sibling once. Source material/geometry are unchanged; the caller supplies the
    /// card material. Current blend-shape weights are copied, so call after applying descriptor morphs.</summary>
    public static MeshInstance3D? Apply(MeshInstance3D source, int seed, bool isFoal = false)
    {
        bool tail = source.Name == "Tail";
        if ((!tail && source.Name != "Mane") || source.Mesh is not ArrayMesh mesh || source.GetParent() is not { } parent)
            return null;
        string name = tail ? "TailCards" : "ManeCards";
        var bodyNode = isFoal && !tail ? parent.GetNodeOrNull<MeshInstance3D>("Body") : null;
        var bodyMesh = bodyNode?.Mesh as ArrayMesh;
        var bodyToSource = bodyNode != null ? source.Transform.AffineInverse() * bodyNode.Transform : Transform3D.Identity;
        var key = new Key(mesh, seed, isFoal, tail, bodyMesh, bodyToSource, source.Skin, bodyNode?.Skin);
        if (!Cache.TryGetValue(key, out var built))
        {
            var data = Source.Read(mesh);
            if (data == null) return null;
            // On foals the original crest is almost a line. The skin surface keeps its small new locks
            // on the outside of the neck, rather than burying them in it. No body resource is changed.
            Source? body = bodyNode != null ? Source.ReadRootDonor(bodyNode, source, bodyToSource) : null;
            var builder = new Builder(data, seed);
            if (tail) builder.Tail(isFoal); else builder.Mane(isFoal, body);
            built = builder.Build(mesh);
            if (built == null) return null;
            if (Cache.Count >= MaxCacheEntries) Cache.Remove(Order.Dequeue());
            Cache.Add(key, built); Order.Enqueue(key);
        }
        if (parent.GetNodeOrNull<MeshInstance3D>(name) is { } previous)
        {
            if (previous.Mesh == built)
            {
                previous.Transform = source.Transform; previous.Skin = source.Skin; previous.Skeleton = source.Skeleton;
                for (int i = 0; i < mesh.GetBlendShapeCount(); i++) previous.SetBlendShapeValue(i, source.GetBlendShapeValue(i));
                return previous;
            }
            parent.RemoveChild(previous); previous.QueueFree();
        }
        var cards = new MeshInstance3D
        {
            Name = name, Mesh = built, Transform = source.Transform, Skin = source.Skin,
            Skeleton = source.Skeleton, CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            ExtraCullMargin = 0.12f,
        };
        cards.SetMeta("hair_seed", seed);
        parent.AddChild(cards);
        for (int i = 0; i < mesh.GetBlendShapeCount(); i++) cards.SetBlendShapeValue(i, source.GetBlendShapeValue(i));
        return cards;
    }

    private sealed class Surface
    {
        public Vector3[] Positions = Array.Empty<Vector3>();
        public int[] Indices = Array.Empty<int>();
        public Vector3 Min, Max;
        private readonly List<int>[] _bins = Enumerable.Range(0, 48).Select(_ => new List<int>()).ToArray();
        private int Bin(float x) => Math.Clamp((int)((x - Min.X) / Math.Max(Max.X - Min.X, 0.0001f) * 48), 0, 47);

        public static Surface Read(ArrayMesh mesh, Transform3D? transform = null)
        {
            var p = new List<Vector3>(); var indices = new List<int>();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var a = mesh.SurfaceGetArrays(s);
                var points = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int offset = p.Count;
                p.AddRange(transform.HasValue ? points.Select(v => transform.Value * v) : points);
                if (a[(int)Mesh.ArrayType.Index].VariantType != Variant.Type.Nil)
                    indices.AddRange(a[(int)Mesh.ArrayType.Index].AsInt32Array().Select(i => i + offset));
                else indices.AddRange(Enumerable.Range(offset, points.Length));
            }
            var result = new Surface { Positions = p.ToArray(), Indices = indices.ToArray() };
            result.Initialise(); return result;
        }

        public void Initialise()
        {
            if (Positions.Length == 0) return;
            foreach (var bin in _bins) bin.Clear();
            Min = Max = Positions[0];
            foreach (var p in Positions) { Min = Min.Min(p); Max = Max.Max(p); }
            for (int i = 0; i + 2 < Indices.Length; i += 3)
            {
                float lo = Math.Min(Positions[Indices[i]].X, Math.Min(Positions[Indices[i + 1]].X, Positions[Indices[i + 2]].X));
                float hi = Math.Max(Positions[Indices[i]].X, Math.Max(Positions[Indices[i + 1]].X, Positions[Indices[i + 2]].X));
                for (int b = Bin(lo); b <= Bin(hi); b++) _bins[b].Add(i);
            }
        }

        // Exact triangle/plane intersections, rather than the highest nearby vertex (which shifts a
        // sloping crest forward and leaves holes in the foal's very sparsely tessellated strip).
        public (float Low, float High, float Side) AtX(float x)
        {
            x = Mathf.Clamp(x, Min.X + 0.00001f, Max.X - 0.00001f);
            float low = float.MaxValue, high = float.MinValue, side = 0;
            foreach (int i in _bins[Bin(x)])
                for (int e = 0; e < 3; e++)
                {
                    var a = Positions[Indices[i + e]]; var b = Positions[Indices[i + (e + 1) % 3]];
                    if ((a.X - x) * (b.X - x) > 0 || Math.Abs(a.X - b.X) < 0.000001f) continue;
                    var q = a.Lerp(b, (x - a.X) / (b.X - a.X));
                    low = Math.Min(low, q.Y); high = Math.Max(high, q.Y); side = Math.Max(side, q.Z);
                }
            if (low == float.MaxValue)
            {
                var p = Positions.MinBy(p => Math.Abs(p.X - x)); return (p.Y, p.Y, p.Z);
            }
            return (low, high, side);
        }

        public bool Outward(float x, float y, out float z)
        {
            z = float.MinValue;
            if (x < Min.X || x > Max.X || y < Min.Y || y > Max.Y) return false;
            foreach (int i in _bins[Bin(x)])
            {
                var a = Positions[Indices[i]]; var b = Positions[Indices[i + 1]]; var c = Positions[Indices[i + 2]];
                float det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                if (Math.Abs(det) < 1e-9f) continue;
                float u = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / det;
                float v = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / det;
                if (u >= -0.0001f && v >= -0.0001f && u + v <= 1.0001f)
                    z = Math.Max(z, u * a.Z + v * b.Z + (1 - u - v) * c.Z);
            }
            return z != float.MinValue;
        }

        // The juvenile's old Mane is below the visible dorsal ridge. The body also has a narrow
        // midline trench, so a ray at exactly Z=0 misses that ridge. Intersect a thin dorsal band
        // on the mane side instead. A 2.8 cm band includes the 2.3 cm neck ridge but excludes
        // the inner ear faces, which otherwise make pale spikes beside the poll.
        public Vector3? Crest(float x, float sideLimit = 0.028f)
        {
            Vector3? best = null;
            foreach (int i in _bins[Bin(x)])
            {
                var hits = new List<Vector3>(3);
                for (int e = 0; e < 3; e++)
                {
                    var a = Positions[Indices[i + e]]; var b = Positions[Indices[i + (e + 1) % 3]];
                    if ((a.X - x) * (b.X - x) > 0 || Math.Abs(a.X - b.X) < 0.000001f) continue;
                    hits.Add(a.Lerp(b, (x - a.X) / (b.X - a.X)));
                }
                for (int a = 0; a < hits.Count; a++)
                    for (int b = a + 1; b < hits.Count; b++)
                    {
                        var p = hits[a]; var q = hits[b];
                        float lo = 0, hi = 1, dz = q.Z - p.Z;
                        if (Math.Abs(dz) < 0.000001f)
                        { if (p.Z < 0 || p.Z > sideLimit) continue; }
                        else
                        {
                            float t0 = -p.Z / dz, t1 = (sideLimit - p.Z) / dz;
                            lo = Math.Max(0, Math.Min(t0, t1)); hi = Math.Min(1, Math.Max(t0, t1));
                            if (lo > hi) continue;
                        }
                        var left = p.Lerp(q, lo); var right = p.Lerp(q, hi);
                        var top = left.Y > right.Y ? left : right;
                        if (!best.HasValue || top.Y > best.Value.Y) best = top;
                    }
            }
            return best;
        }

        public (Vector3 Centre, float RadiusX, float RadiusZ) AtY(float y)
        {
            float band = Math.Max((Max.Y - Min.Y) * 0.045f, 0.004f);
            var points = Positions.Where(p => Math.Abs(p.Y - y) < band).ToArray();
            if (points.Length == 0) points = Positions.OrderBy(p => Math.Abs(p.Y - y)).Take(8).ToArray();
            float x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), z0 = points.Min(p => p.Z), z1 = points.Max(p => p.Z);
            return (new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f), (x1 - x0) * 0.5f, (z1 - z0) * 0.5f);
        }
    }

    private sealed class Source
    {
        public Surface Surface = new();
        public int Influences;
        public int[] Bones = Array.Empty<int>();
        public float[] Weights = Array.Empty<float>();
        public Vector3[][] Shapes = Array.Empty<Vector3[]>();
        public bool Relative;

        public static Source? Read(ArrayMesh mesh)
        {
            if (mesh.GetSurfaceCount() == 0) return null;
            if (Enumerable.Range(0, mesh.GetSurfaceCount()).Any(s => mesh.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles)) return null;
            var surface = Surface.Read(mesh);
            if (surface.Positions.Length < 3 || surface.Indices.Length < 3) return null;
            int influences = Enumerable.Range(0, mesh.GetSurfaceCount()).Any(s =>
                (mesh.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights) != 0) ? 8 : 4;
            var bones = new List<int>(); var weights = new List<float>();
            var shapes = Enumerable.Range(0, mesh.GetBlendShapeCount()).Select(_ => new List<Vector3>()).ToArray();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var a = mesh.SurfaceGetArrays(s); var positions = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (a[(int)Mesh.ArrayType.Bones].VariantType == Variant.Type.Nil || a[(int)Mesh.ArrayType.Weights].VariantType == Variant.Type.Nil)
                    return null;
                var b = a[(int)Mesh.ArrayType.Bones].AsInt32Array(); var w = a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int stride = b.Length / positions.Length;
                if (stride is not (4 or 8) || w.Length != b.Length) return null;
                for (int v = 0; v < positions.Length; v++)
                    for (int k = 0; k < influences; k++)
                    { bones.Add(k < stride ? b[v * stride + k] : 0); weights.Add(k < stride ? w[v * stride + k] : 0); }
                var targets = mesh.SurfaceGetBlendShapeArrays(s);
                for (int t = 0; t < shapes.Length; t++)
                {
                    if (t >= targets.Count || targets[t][(int)Mesh.ArrayType.Vertex].VariantType == Variant.Type.Nil) return null;
                    var target = targets[t][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    if (target.Length != positions.Length) return null;
                    shapes[t].AddRange(target);
                }
            }
            return new Source { Surface = surface, Influences = influences, Bones = bones.ToArray(), Weights = weights.ToArray(),
                Shapes = shapes.Select(s => s.ToArray()).ToArray(), Relative = mesh.BlendShapeMode == Mesh.BlendShapeMode.Relative };
        }

        public static Source? ReadRootDonor(MeshInstance3D body, MeshInstance3D hair, Transform3D toHair)
        {
            if (body.Mesh is not ArrayMesh bodyMesh || hair.Mesh is not ArrayMesh hairMesh || body.Skin == null || hair.Skin == null)
                return null;
            var skeleton = hair.GetNodeOrNull<Skeleton3D>(hair.Skeleton);
            if (skeleton == null || body.GetNodeOrNull<Skeleton3D>(body.Skeleton) != skeleton) return null;
            var donor = Read(bodyMesh);
            if (donor == null) return null;
            string BindName(Skin skin, int bind)
            {
                var name = skin.GetBindName(bind);
                if (!name.IsEmpty) return name.ToString();
                int bone = skin.GetBindBone(bind);
                return bone >= 0 && bone < skeleton.GetBoneCount() ? skeleton.GetBoneName(bone).ToString() : "";
            }
            var targetBinds = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < hair.Skin.GetBindCount(); i++)
            { string name = BindName(hair.Skin, i); if (name.Length > 0) targetBinds[name] = i; }
            var mapping = new int[body.Skin.GetBindCount()];
            for (int i = 0; i < mapping.Length; i++) mapping[i] = targetBinds.GetValueOrDefault(BindName(body.Skin, i), -1);
            for (int i = 0; i < donor.Bones.Length; i++)
            {
                int bone = donor.Bones[i];
                if (bone < 0 || bone >= mapping.Length || mapping[bone] < 0)
                { if (donor.Weights[i] > 0) return null; donor.Bones[i] = 0; }
                else donor.Bones[i] = mapping[bone];
            }
            var ordered = new Vector3[hairMesh.GetBlendShapeCount()][];
            for (int i = 0; i < ordered.Length; i++)
            {
                int match = Enumerable.Range(0, bodyMesh.GetBlendShapeCount()).FirstOrDefault(j => bodyMesh.GetBlendShapeName(j) == hairMesh.GetBlendShapeName(i), -1);
                if (match < 0) return null;
                ordered[i] = donor.Shapes[match].Select(p => donor.Relative ? toHair.Basis * p : toHair * p).ToArray();
            }
            donor.Shapes = ordered;
            donor.Surface.Positions = donor.Surface.Positions.Select(p => toHair * p).ToArray();
            donor.Surface.Initialise();
            return donor;
        }

        public (int[] Vertices, float[] Shares) Nearest(Vector3 p)
        {
            var ids = new int[3]; var distances = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            for (int i = 0; i < Surface.Positions.Length; i++)
            {
                float d = p.DistanceSquaredTo(Surface.Positions[i]);
                for (int j = 0; j < 3; j++)
                    if (d < distances[j])
                    {
                        for (int k = 2; k > j; k--) { distances[k] = distances[k - 1]; ids[k] = ids[k - 1]; }
                        distances[j] = d; ids[j] = i; break;
                    }
            }
            var shares = distances.Select(d => 1f / Math.Max(d, 0.000001f)).ToArray();
            float sum = shares.Sum(); for (int j = 0; j < 3; j++) shares[j] /= sum;
            return (ids, shares);
        }
    }

    private sealed class Builder
    {
        private const int Rows = 10, Columns = 3;
        private readonly Source _source;
        private uint _state;
        private readonly List<Vector3> _positions = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<Color> _colours = new();
        private readonly List<int> _indices = new(), _bones = new();
        private readonly List<float> _weights = new();
        private readonly List<Vector3>[] _shapes;
        public Builder(Source source, int seed)
        {
            _source = source; _state = unchecked((uint)seed) ^ 0x9e3779b9u;
            _shapes = source.Shapes.Select(_ => new List<Vector3>()).ToArray();
        }
        private float Random()
        { _state += 0x6d2b79f5u; uint z = _state; z = (z ^ (z >> 15)) * (z | 1); z ^= z + ((z ^ (z >> 7)) * (z | 61)); return ((z ^ (z >> 14)) >> 8) / 16777216f; }
        private float Range(float lo, float hi) => Mathf.Lerp(lo, hi, Random());

        public void Mane(bool foal, Source? body)
        {
            var surface = _source.Surface;
            // The poll is the highest part of the hair; vertices beyond it belong to the forelock.
            float peak = surface.Max.Y;
            float poll = surface.Positions.Where(p => p.Y > peak - 0.004f).Average(p => p.X);
            float begin = surface.Min.X + (poll - surface.Min.X) * 0.015f;
            float span = poll - begin;
            int count = foal ? 32 : 52;
            for (int layer = 0; layer < 3; layer++)
                for (int card = 0; card < count; card++)
                {
                    float t = Mathf.Clamp((card + 0.35f + Range(-0.16f, 0.16f) + layer * 0.22f) / count, 0.015f, 0.99f);
                    float x = begin + span * t;
                    var profile = surface.AtX(x);
                    var bodyCrest = body?.Surface.Crest(x);
                    if (bodyCrest.HasValue) profile.High = Mathf.Lerp(Math.Max(profile.High, Math.Min(bodyCrest.Value.Y, peak + 0.002f)), profile.High,
                        Mathf.SmoothStep(0.9f, 0.99f, t));
                    float depth = foal ? Range(0.055f, 0.095f) : Math.Max(0.07f, profile.High - profile.Low) * Range(1.28f, 1.36f) + 0.025f;
                    float drift = Range(-0.018f, 0.018f), wave = Range(0.003f, foal ? 0.009f : 0.014f);
                    float width = span / count * Range(1.55f, 2.05f);
                    float lastSide = surface.Outward(x, profile.High - Math.Min(0.003f, (profile.High - profile.Low) * 0.2f), out var crestSide)
                        ? crestSide : 0;
                    if (bodyCrest.HasValue) lastSide = bodyCrest.Value.Z;
                    float dx = span * 0.025f;
                    float topBefore = body?.Surface.Crest(Math.Max(begin, x - dx))?.Y ?? surface.AtX(Math.Max(begin, x - dx)).High;
                    float topAfter = body?.Surface.Crest(Math.Min(poll, x + dx))?.Y ?? surface.AtX(Math.Min(poll, x + dx)).High;
                    float slope = (topAfter - topBefore) / (2f * dx);
                    var path = new Vector3[Rows]; var across = new Vector3[Rows];
                    for (int row = 0; row < Rows; row++)
                    {
                        float v = row / (float)(Rows - 1);
                        float px = x + drift * v + wave * Mathf.Sin(v * Mathf.Pi);
                        float py = profile.High + 0.001f - depth * v;
                        if (surface.Outward(px, py, out var side)) lastSide = side;
                        if (body != null && body.Surface.Outward(px, py, out var skin)) lastSide = Math.Max(lastSide, skin);
                        float z = lastSide + 0.002f + layer * 0.003f * Mathf.SmoothStep(0, 0.2f, v)
                            + Mathf.Sin(v * Mathf.Pi) * (foal ? 0.006f : 0.009f);
                        path[row] = new Vector3(px, py, z);
                        // Across-U points toward the withers, so across x root-to-tip points out of
                        // the positive-Z curtain. Tail and forelock use their own outward frames.
                        across[row] = -new Vector3(1, slope * (1 - v * 0.65f), 0).Normalized();
                    }
                    Strip(path, across, width, Random(), true, body);
                }

            // Separate forward-falling forelock, fanned between the ears instead of extending the neck curtain.
            int locks = foal ? 14 : 18;
            float reach = foal ? 0.085f : Math.Max(0.085f, surface.Max.X - poll) + 0.018f;
            float fall = foal ? 0.065f : Math.Max(0.085f, peak - surface.AtX(surface.Max.X - 0.003f).Low) + 0.035f;
            for (int card = 0; card < locks; card++)
            {
                float z = Mathf.Lerp(foal ? -0.022f : -0.041f, foal ? 0.022f : 0.041f, card / (float)(locks - 1));
                float centre = 1 - Math.Abs(card / (float)(locks - 1) * 2 - 1);
                float length = foal ? Range(0.88f, 1.12f) : Range(0.92f, 1.06f) * Mathf.Lerp(0.94f, 1.06f, centre);
                float bend = Range(-0.008f, 0.008f);
                var path = new Vector3[Rows]; var across = new Vector3[Rows];
                for (int row = 0; row < Rows; row++)
                {
                    float v = row / (float)(Rows - 1);
                    float x = poll - 0.009f + reach * v * length;
                    float y = foal ? peak + 0.009f - fall * Mathf.Pow(v, 1.55f) * length
                        : surface.AtX(Math.Min(x, surface.Max.X - 0.001f)).High + 0.006f - Math.Max(0, x - surface.Max.X) * 2.5f;
                    if (foal) y += 0.006f * Mathf.Sin(v * Mathf.Pi); // clear the convex forehead; roots and tips stay put
                    path[row] = new Vector3(x, y, z * (1 - 0.3f * v) + bend * Mathf.Sin(v * Mathf.Pi));
                    across[row] = Vector3.Back;
                }
                Strip(path, across, foal ? Range(0.012f, 0.018f) : Range(0.018f, 0.027f), Random(), true);
            }
        }

        public void Tail(bool foal)
        {
            var surface = _source.Surface;
            float height = surface.Max.Y - surface.Min.Y;
            int count = foal ? 40 : 64;
            for (int card = 0; card < count; card++)
            {
                float angle = card * 2.39996323f + Range(-0.1f, 0.1f);
                float root = surface.Max.Y - height * Range(0.025f, 0.16f);
                float end = surface.Min.Y - (foal ? Range(0.065f, 0.11f) : Range(0.025f, 0.095f));
                float wave = Range(0.003f, foal ? 0.007f : 0.015f), phase = Range(0, Mathf.Tau);
                var path = new Vector3[Rows]; var across = new Vector3[Rows];
                for (int row = 0; row < Rows; row++)
                {
                    float v = row / (float)(Rows - 1), y = Mathf.Lerp(root, end, v);
                    var section = surface.AtY(Math.Max(y, surface.Min.Y + height * 0.06f));
                    float extra = Math.Max(0, surface.Min.Y - y);
                    float radius = (foal ? 0.011f : 0.019f) + wave * Mathf.Sin(v * 5 + phase);
                    float taper = 1 - Mathf.SmoothStep(0.75f, 1, v) * 0.7f;
                    float rx = Math.Max(foal ? 0.012f : 0.01f, section.RadiusX + radius) * taper;
                    float rz = Math.Max(foal ? 0.014f : 0.01f, section.RadiusZ + radius) * taper;
                    float a = angle + 0.18f * Mathf.Sin(v * 4 + phase);
                    path[row] = new Vector3(section.Centre.X - extra * 0.25f + Mathf.Cos(a) * rx, y, section.Centre.Z + Mathf.Sin(a) * rz);
                    across[row] = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                }
                Strip(path, across, foal ? Range(0.012f, 0.022f) : Range(0.028f, 0.046f), Random(), false);
            }
        }

        private void Strip(Vector3[] path, Vector3[] across, float width, float variation, bool rootAnchored, Source? rootSource = null)
        {
            int start = _positions.Count;
            var donorSource = rootSource ?? _source;
            var rootDonors = donorSource.Nearest(path[0]);
            for (int row = 0; row < Rows; row++)
            {
                float v = row / (float)(Rows - 1);
                // A mane lock hangs from one point on the crest; inheriting neck skin weights at
                // every row stretches it between unrelated joints during grazing. Tail ribbons
                // instead follow the long tail chain, so their weights vary along the curve.
                var (donors, shares) = rootAnchored ? rootDonors : donorSource.Nearest(path[row]);
                var influences = new Dictionary<int, float>();
                for (int d = 0; d < donors.Length; d++)
                    for (int k = 0; k < donorSource.Influences; k++)
                    {
                        int slot = donors[d] * donorSource.Influences + k, bone = donorSource.Bones[slot];
                        float weight = donorSource.Weights[slot] * shares[d];
                        if (weight > 0) influences[bone] = influences.GetValueOrDefault(bone) + weight;
                    }
                var skin = influences.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(_source.Influences).ToArray();
                float total = skin.Sum(p => p.Value);
                var deltas = new Vector3[_shapes.Length];
                for (int shape = 0; shape < deltas.Length; shape++)
                    for (int d = 0; d < donors.Length; d++)
                        deltas[shape] += (donorSource.Shapes[shape][donors[d]] - (donorSource.Relative ? Vector3.Zero : donorSource.Surface.Positions[donors[d]])) * shares[d];
                var tangent = row + 1 < Rows ? path[row + 1] - path[row] : path[row] - path[row - 1];
                var normal = across[row].Cross(tangent).Normalized();
                for (int column = 0; column < Columns; column++)
                {
                    float u = column / (float)(Columns - 1);
                    float taper = Mathf.Lerp(1, 0.30f, Mathf.SmoothStep(0.65f, 1, v))
                        * (rootAnchored ? Mathf.Lerp(0.65f, 1, Mathf.SmoothStep(0, 0.18f, v)) : 1);
                    var p = path[row] + across[row] * ((u - 0.5f) * width * taper) + normal * (column == 1 ? width * 0.04f : 0);
                    _positions.Add(p); _uv.Add(new Vector2(u, 1 - v)); _colours.Add(new Color(variation, 1, 0, 1));
                    for (int k = 0; k < _source.Influences; k++)
                    { _bones.Add(k < skin.Length ? skin[k].Key : 0); _weights.Add(k < skin.Length && total > 0 ? skin[k].Value / total : 0); }
                    for (int shape = 0; shape < _shapes.Length; shape++) _shapes[shape].Add(_source.Relative ? deltas[shape] : p + deltas[shape]);
                }
            }
            for (int row = 0; row < Rows - 1; row++)
                for (int column = 0; column < Columns - 1; column++)
                {
                    int a = start + row * Columns + column, b = a + 1, c = a + Columns, d = c + 1;
                    _indices.AddRange(new[] { a, c, b, b, c, d });
                }
        }

        private static (Vector3[] Normals, float[] Tangents) Directions(Vector3[] positions, int[] indices)
        {
            var normals = new Vector3[positions.Length]; var tangents = new float[positions.Length * 4];
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                var n = (positions[c] - positions[a]).Cross(positions[b] - positions[a]);
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
            for (int i = 0; i < positions.Length; i++)
            {
                normals[i] = normals[i].LengthSquared() > 1e-10f ? normals[i].Normalized() : Vector3.Back;
                int row = i - i % Columns;
                var t = (positions[row + Columns - 1] - positions[row]).Normalized();
                t = (t - normals[i] * normals[i].Dot(t)).Normalized();
                tangents[i * 4] = t.X; tangents[i * 4 + 1] = t.Y; tangents[i * 4 + 2] = t.Z; tangents[i * 4 + 3] = -1;
            }
            return (normals, tangents);
        }

        public ArrayMesh? Build(ArrayMesh source)
        {
            if (_positions.Count == 0) return null;
            var positions = _positions.ToArray(); var indices = _indices.ToArray();
            var (normals, tangents) = Directions(positions, indices);
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions; arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Tangent] = tangents; arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = _colours.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices;
            arrays[(int)Mesh.ArrayType.Bones] = _bones.ToArray(); arrays[(int)Mesh.ArrayType.Weights] = _weights.ToArray();
            var blendShapes = new Godot.Collections.Array<Godot.Collections.Array>();
            var result = new ArrayMesh { BlendShapeMode = source.BlendShapeMode, ResourceName = source.ResourceName + "_HairCards" };
            for (int shape = 0; shape < _shapes.Length; shape++)
            {
                result.AddBlendShape(source.GetBlendShapeName(shape));
                var target = _shapes[shape].ToArray();
                var absolute = _source.Relative ? target.Select((p, i) => p + positions[i]).ToArray() : target;
                var (targetNormals, targetTangents) = Directions(absolute, indices);
                if (_source.Relative)
                {
                    for (int i = 0; i < targetNormals.Length; i++) targetNormals[i] -= normals[i];
                    for (int i = 0; i < targetTangents.Length; i++) targetTangents[i] -= tangents[i];
                }
                var a = new Godot.Collections.Array(); a.Resize((int)Mesh.ArrayType.Max);
                a[(int)Mesh.ArrayType.Vertex] = target; a[(int)Mesh.ArrayType.Normal] = targetNormals; a[(int)Mesh.ArrayType.Tangent] = targetTangents;
                blendShapes.Add(a);
            }
            var flags = _source.Influences == 8 ? Mesh.ArrayFormat.FlagUse8BoneWeights : 0;
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes, null, flags);
            result.SurfaceSetName(0, "DirectionalHairCards");
            return result;
        }
    }
}
