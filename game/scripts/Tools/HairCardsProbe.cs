using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Cloverbrook.Game.Scenes;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// D-039 executable contracts against the actual imported horse rigs. Headless by default;
/// add --capture for render evidence (requires a rendering display). --out=DIR selects the report directory.
/// Optional --capture-families=ID,..., --capture-coats=ID,... and --capture-views=side,head,... limit images only;
/// the full four-family executable contract suite always runs.
/// The CPU skin checks verify bind/morph/pose contracts, not the visual quality of alpha coverage.
/// </summary>
public partial class HairCardsProbe : Node3D
{
    private readonly List<object> _measurements = new();
    private readonly HashSet<int> _influenceWidths = new();
    private HorseStage _stage = null!;
    private int _checks;
    private string _output = "/tmp/hair-cards-probe";
    private bool _capture;

    public override void _Ready()
    {
        _output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? _output;
        _capture = OS.GetCmdlineUserArgs().Contains("--capture");
        CallDeferred(nameof(Run));
    }

    private async void Run()
    {
        Directory.CreateDirectory(_output);
        try
        {
            _stage = new HorseStage { Name = "Stage" }; AddChild(_stage);
            _stage.SetProcess(false);
            _stage.SetMode(LightingMode.Neutral);
            _stage.Presenter.SetProcess(false);
            var catalog = VisualCatalog.Instance;
            Check(catalog.MeshFamilies.Count == 4, "probe covers exactly the four shipped family mappings");
            foreach (var (familyId, family) in catalog.MeshFamilies.OrderBy(x => x.Key))
            foreach (string animation in new[] { "idle", "walk" })
            {
                var descriptor = FixtureLibrary.Get("bay", family.Age == "foal") with { MeshFamilyId = familyId, TestOnly = true };
                string scene = animation == "walk" ? family.WalkVariant!.Scene : family.Scene;
                CheckImportedSource(scene, family.Age == "foal");
                CheckPresented(descriptor, animation, scene);
                if (_capture && Selected("--capture-families", descriptor.MeshFamilyId)) await Capture(descriptor, animation);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // release detached prior models
            }
            Check(_influenceWidths.SetEquals(new[] { 4, 8 }), "real imported fixtures exercised both four- and eight-influence surfaces");
            CheckSyntheticMorphs(catalog.MeshFamilies["horse.standard.adult.v1"].Scene);
            CheckCacheLifecycle(catalog.MeshFamilies["horse.standard.adult.v1"].Scene);
            CheckMaterialVariants();
            WriteReport(null);
            GD.Print($"[hair-cards] PASS {_checks} checks; report {_output}/hair-cards-report.json");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            WriteReport(error.ToString());
            GD.PushError("[hair-cards] FAILED " + error);
            GetTree().Quit(1);
        }
    }

    private void CheckImportedSource(string path, bool foal)
    {
        var root = GD.Load<PackedScene>(path).Instantiate<Node3D>(); AddChild(root);
        try
        {
            var body = MeshNamed(root, "Body");
            string originalBody = Fingerprint(body.Mesh);
            foreach (string component in new[] { "Mane", "Tail" })
            {
                var source = MeshNamed(root, component);
                var originalMesh = source.Mesh;
                string original = Fingerprint(originalMesh);
                for (int i = 0; i < source.GetBlendShapeCount(); i++) source.SetBlendShapeValue(i, i % 2 == 0 ? 0.75f : -0.5f);
                var cards = HairCards.Apply(source, 1701, foal);
                Check(cards != null, path + " generated " + component);
                Check(cards!.Name == component + "Cards", "generated node uses the explicit hair-card name");
                Check(cards.GetParent() == source.GetParent(), "card node is a sibling of its source");
                Check(cards.Transform == source.Transform && cards.Skin == source.Skin && cards.Skeleton == source.Skeleton,
                    "cards inherit exact mesh-space transform, Skin and skeleton path");
                Check(source.Mesh == originalMesh && Fingerprint(source.Mesh) == original, "source hair mesh and UVs remain unchanged");
                CheckGeometry(cards, source);
                if (component == "Mane") CheckWrapCoverage(cards, source, foal);
                for (int i = 0; i < source.GetBlendShapeCount(); i++)
                    Check(Math.Abs(cards.GetBlendShapeValue(i) - source.GetBlendShapeValue(i)) < 1e-6, "Apply copies current source morph values");
                Check(HairCards.Apply(source, 1701, foal) == cards, "repeated Apply is idempotent for the same source node and seed");
                string first = Fingerprint(cards.Mesh);
                var retained = cards.Mesh;
                cards.Free();
                var cached = HairCards.Apply(source, 1701, foal)!;
                Check(cached.Mesh == retained, "identical source/seed reuses generated mesh resource");
                cached.Free();
                HairCards.ReleaseCache();
                var rebuilt = HairCards.Apply(source, 1701, foal)!;
                Check(rebuilt.Mesh != retained && Fingerprint(rebuilt.Mesh) == first, "cleared cache rebuild is bit-for-bit deterministic");
                Check(Fingerprint(retained) == first, "cache clearing never disposes live mesh resources");
                rebuilt.Free();
                var changed = HairCards.Apply(source, 1702, foal)!;
                Check(Fingerprint(changed.Mesh) != first, "different seeds produce different geometry");
                Check(Fingerprint(body.Mesh) == originalBody, "hair generation leaves all body arrays and morphs unchanged");
            }
        }
        finally { root.Free(); }
    }

    private void CheckWrapCoverage(MeshInstance3D cards, MeshInstance3D source, bool foal)
    {
        // Each strip has ten three-vertex rows. Inspect the actual generated mesh, not a
        // builder counter: long near-side locks must coexist with short far-side coverage.
        var points = cards.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var imported = Enumerable.Range(0, source.Mesh.GetSurfaceCount())
            .SelectMany(s => source.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array()).ToArray();
        float peak = imported.Max(p => p.Y);
        float poll = imported.Where(p => p.Y > peak - 0.004f).Average(p => p.X);
        float begin = imported.Min(p => p.X);
        int originalCards = foal ? 110 : 174;
        var far = Enumerable.Range(originalCards, Math.Max(0, points.Length / 30 - originalCards))
            .Where(i => points[i * 30 + 28].Z < -0.015f && points[i * 30 + 28].Z < points[i * 30 + 1].Z - 0.025f && points[i * 30 + 1].X < poll - 0.015f)
            .ToArray();
        Check(far.Length >= (foal ? 12 : 20), "short far-side locks cover the crest, rather than only the forelock");
        float covered = far.Max(i => points[i * 30 + 1].X) - far.Min(i => points[i * 30 + 1].X);
        Check(covered > (poll - begin) * 0.75f, "far-side lock roots span at least three quarters of the crest");
        foreach (int i in far)
        {
            var root = points[i * 30 + 1]; var tip = points[i * 30 + 28];
            Check(root.Z > tip.Z && root.Y > tip.Y, "far-side locks wrap down from their crest roots");
            Check(root.Y - tip.Y < (foal ? 0.065f : 0.12f), $"opposite-side locks remain short instead of duplicating the long mane: card {i}, root {root}, tip {tip}, source {source.Mesh.ResourcePath}");
        }
        Check(cards.Mesh.GetSurfaceCount() == 1, "wrap coverage shares the existing mane draw surface");
        Check(points.Length <= originalCards * 30 * 1.2f, "wrap coverage adds at most twenty percent mane geometry");
        _measurements.Add(new { kind = "wrap-coverage", source = source.Mesh.ResourcePath, foal,
            farSideLocks = far.Length, crestCoverage = covered / (poll - begin), vertices = points.Length });
    }

    private void CheckGeometry(MeshInstance3D cards, MeshInstance3D source)
    {
        Check(cards.Mesh is ArrayMesh, "generated geometry is an ArrayMesh");
        var mesh = (ArrayMesh)cards.Mesh;
        var sourceMesh = (ArrayMesh)source.Mesh;
        Check(mesh.GetSurfaceCount() > 0, "generated mesh has actual surfaces");
        Check(mesh.BlendShapeMode == sourceMesh.BlendShapeMode && mesh.GetBlendShapeCount() == sourceMesh.GetBlendShapeCount(),
            "blend shape count and Relative/Normalized mode are preserved");
        for (int b = 0; b < mesh.GetBlendShapeCount(); b++)
            Check(mesh.GetBlendShapeName(b) == sourceMesh.GetBlendShapeName(b), "blend shape names/order are preserved");
        var skeleton = cards.GetNodeOrNull<Skeleton3D>(cards.Skeleton);
        Check(skeleton != null && cards.Skin != null, "cards resolve a real skeleton and skin");
        for (int b = 0; b < cards.Skin!.GetBindCount(); b++)
        {
            int bone = cards.Skin.GetBindName(b).IsEmpty ? cards.Skin.GetBindBone(b) : skeleton!.FindBone(cards.Skin.GetBindName(b));
            Check(bone >= 0 && bone < skeleton!.GetBoneCount(), "every skin bind resolves a skeleton bone");
        }
        int vertexCount = 0, triangles = 0;
        float maxWeightError = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var a = mesh.SurfaceGetArrays(s);
            var p = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var n = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var t = a[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
            var uv = a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var c = a[(int)Mesh.ArrayType.Color].AsColorArray();
            var bones = a[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var indices = a[(int)Mesh.ArrayType.Index].AsInt32Array();
            Check(p.Length > 100 && p.Length < 20000, "cards contain bounded, nontrivial geometry");
            Check(n.Length == p.Length && t.Length == p.Length * 4 && uv.Length == p.Length && c.Length == p.Length,
                "every card vertex has normal, tangent, UV and deterministic color attributes");
            int width = weights.Length / p.Length;
            Check((width == 4 || width == 8) && bones.Length == p.Length * width && weights.Length == bones.Length,
                "skin influence arrays are complete, with four or eight slots");
            Check(((mesh.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights) != 0) == (width == 8), "eight-weight format flag matches stored data");
            _influenceWidths.Add(width);
            Check(uv.Any(v => Math.Abs(v.Y - 1f) < 1e-6) && uv.Any(v => Math.Abs(v.Y) < 1e-6), "UVs include root V=1 and tip V=0 after Godot V flip");
            for (int i = 0; i < p.Length; i++)
            {
                Check(p[i].IsFinite() && n[i].IsFinite() && uv[i].IsFinite(), "card vertex attributes are finite");
                Check(n[i].LengthSquared() > 0.9f && n[i].LengthSquared() < 1.1f, "card normal has unit length");
                Check(uv[i].X >= 0 && uv[i].X <= 1 && uv[i].Y >= 0 && uv[i].Y <= 1, "card UV is inside its root-to-tip domain");
                var tangent = new Vector3(t[i * 4], t[i * 4 + 1], t[i * 4 + 2]);
                Check(tangent.IsFinite() && tangent.LengthSquared() > 0.9f && tangent.LengthSquared() < 1.1f && Math.Abs(t[i * 4 + 3]) == 1,
                    "card has a finite normalized tangent and handedness");
                Check(Math.Abs(tangent.Dot(n[i])) < 0.04, "card tangent and normal define a usable anisotropic basis");
                float sum = 0;
                for (int j = 0; j < width; j++)
                {
                    int slot = i * width + j;
                    Check(float.IsFinite(weights[slot]) && weights[slot] >= 0 && bones[slot] >= 0 && bones[slot] < cards.Skin.GetBindCount(),
                        "all influences have finite nonnegative weights and valid bind indices");
                    sum += weights[slot];
                }
                maxWeightError = Math.Max(maxWeightError, Math.Abs(sum - 1));
                Check(Math.Abs(sum - 1) < 0.0003, "skin weight sums stay normalized after Godot quantization");
            }
            Check(indices.Length > 0 && indices.Length % 3 == 0, "cards have indexed triangles");
            foreach (int index in indices) Check(index >= 0 && index < p.Length, "triangle index is in bounds");
            for (int i = 0; i < indices.Length; i += 3)
                Check((p[indices[i + 1]] - p[indices[i]]).Cross(p[indices[i + 2]] - p[indices[i]]).LengthSquared() > 1e-15,
                    "card triangles have nonzero area");
            foreach (var shape in mesh.SurfaceGetBlendShapeArrays(s))
            {
                var points = shape[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Check(points.Length == p.Length && points.All(v => v.IsFinite()), "every blend shape has finite positions for every generated vertex");
            }
            vertexCount += p.Length; triangles += indices.Length / 3;
        }
        _measurements.Add(new { kind = "geometry", source = sourceMesh.ResourcePath, component = source.Name.ToString(), vertexCount, triangles, maxWeightError });
    }

    private void CheckPresented(VisualDescriptor descriptor, string animation, string sourcePath)
    {
        var p = _stage.Presenter;
        Check(p.Present(descriptor, animation).Supported, descriptor.MeshFamilyId + " presents " + animation);
        var raw = GD.Load<PackedScene>(sourcePath).Instantiate<Node3D>();
        try
        {
            var baselineBody = MeshNamed(raw, "Body");
            // BodyMap predates hair cards and re-packs the imported surface when adding CUSTOM0.
            // Apply the same existing stage to the control; direct generation above separately proves
            // HairCards does not touch even a single imported body byte.
            BodyMap.Apply(baselineBody);
            Check(Fingerprint(MeshNamed(p.Model!, "Body").Mesh) == Fingerprint(baselineBody.Mesh),
                "presenter body positions, normals, UV0, UV2, skin and morph arrays match the independent BodyMap baseline");
        }
        finally { raw.Free(); }
        foreach (string component in new[] { "Mane", "Tail" })
        {
            var source = MeshNamed(p.Model!, component); var cards = MeshNamed(p.Model!, component + "Cards");
            CheckMaterials(source, cards);
            CheckGeometry(cards, source);
            if (component == "Mane") CheckWrapCoverage(cards, source, descriptor.IsFoal);
        }
        Check(All<MeshInstance3D>(p.Model!).Count(m => m.Name == "ManeCards" || m.Name == "TailCards") == 2, "presenter creates exactly one mane and one tail card node");
        p.Pose(animation, 0);
        var before = HairSurface(p.Model!);
        double period = animation == "walk" ? p.LocomotionPeriodSeconds : 2.0;
        float moved = 0;
        foreach (double phase in new[] { 0.23, 0.51, 0.79 })
        {
            p.Pose(animation, period * phase);
            var after = HairSurface(p.Model!);
            Check(after.Values.SelectMany(v => v).All(v => v.IsFinite()), "actual animated card vertices remain finite");
            MeasureStretch(descriptor.MeshFamilyId + ":" + animation + ":" + phase, after);
            moved = Math.Max(moved, Distance(before, after));
        }
        Check(moved > 0.00001f, "hair actually moves under the " + animation + " clip (not a frozen/no-op pose)");
        _measurements.Add(new { kind = "animation", family = descriptor.MeshFamilyId, animation, maximumDisplacementMeters = moved });
        foreach (int sign in new[] { -1, 1 })
        {
            var channels = VisualCatalog.Instance.FamilyMeshBlendShapes[descriptor.MeshFamilyId].Values.SelectMany(m => m.Keys).Distinct();
            var weights = descriptor.MorphWeights.ToDictionary(x => x.Key, x => x.Value);
            foreach (string channel in channels) weights[channel] = sign * (int)VisualCatalog.Instance.BodyBlendShapeNormalization;
            var extreme = descriptor with { MorphWeights = weights };
            Check(p.Present(extreme, animation).Supported, "all-channel morph extreme is supported");
            foreach (string component in new[] { "Mane", "Tail" })
            {
                var source = MeshNamed(p.Model!, component); var cards = MeshNamed(p.Model!, component + "Cards");
                for (int i = 0; i < source.GetBlendShapeCount(); i++)
                    Check(Math.Abs(source.GetBlendShapeValue(i) - cards.GetBlendShapeValue(i)) < 1e-6, "presenter propagates extreme morph values to cards");
            }
            p.Pose(animation, period * 0.37);
            var extremeSurface = HairSurface(p.Model!);
            Check(extremeSurface.Values.SelectMany(v => v).All(v => v.IsFinite()), "morphed and animated skin stays finite");
            MeasureStretch(descriptor.MeshFamilyId + ":" + animation + ":morph" + sign, extremeSurface);
        }
        if (animation == "idle") CheckGraze(descriptor);
        Check(p.Present(descriptor, animation).Supported, "descriptor can be re-presented after probe poses");
        p.ApplyClay();
        foreach (string component in new[] { "Mane", "Tail" })
            Check(MeshNamed(p.Model!, component + "Cards").GetActiveMaterial(0) is ShaderMaterial, "clay mode retains the hair shader on generated cards");
    }

    private void CheckGraze(VisualDescriptor descriptor)
    {
        var p = _stage.Presenter;
        Check(p.Present(descriptor, "idle").Supported, "grazing fixture loads");
        p.Pose("idle", 0);
        p.EnableIdleLife(1701);
        var life = p.IdleLife;
        Check(life != null, "idle-life modifier exists for grazing fixture");
        var before = HairSurface(p.Model!);
        life!.HoldGraze(1);
        life._ProcessModificationWithDelta(0); // sample before Godot restores post-modifier pose
        var after = HairSurface(p.Model!);
        Check(after.Values.SelectMany(v => v).All(v => v.IsFinite()), "full graze produces finite card skin positions");
        MeasureStretch(descriptor.MeshFamilyId + ":graze", after);
        float moved = MaxDistance(before["ManeCards"], after["ManeCards"]);
        Check(moved > 0.08f, "mane follows the lowered neck by a meaningful distance");
        var roots = RootIndices(MeshNamed(p.Model!, "ManeCards").Mesh);
        var frontRoots = roots.OrderByDescending(i => before["ManeCards"][i].X).Take(Math.Max(2, roots.Length / 8)).ToArray();
        float frontMove = frontRoots.Average(i => before["ManeCards"][i].DistanceTo(after["ManeCards"][i]));
        Check(frontMove > 0.05f, "frontmost/forelock card roots move with grazing head instead of floating at rest");
        _measurements.Add(new { kind = "graze", family = descriptor.MeshFamilyId, maximumManeMovementMeters = moved, forelockRootMeanMovementMeters = frontMove });
        life.Clear();
    }

    private void CheckSyntheticMorphs(string sourcePath)
    {
        foreach (var mode in new[] { Mesh.BlendShapeMode.Relative, Mesh.BlendShapeMode.Normalized })
        {
            var root = GD.Load<PackedScene>(sourcePath).Instantiate<Node3D>(); AddChild(root);
            try
            {
                var source = MeshNamed(root, "Mane"); var original = (ArrayMesh)source.Mesh;
                var mesh = new ArrayMesh { BlendShapeMode = mode };
                mesh.AddBlendShape("probe_translation");
                var delta = new Vector3(0.07f, -0.035f, 0.021f);
                for (int s = 0; s < original.GetSurfaceCount(); s++)
                {
                    var arrays = original.SurfaceGetArrays(s);
                    var target = new Godot.Collections.Array(); target.Resize((int)Mesh.ArrayType.Max);
                    var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    target[(int)Mesh.ArrayType.Vertex] = vertices.Select(v => mode == Mesh.BlendShapeMode.Relative ? delta : v + delta).ToArray();
                    target[(int)Mesh.ArrayType.Normal] = mode == Mesh.BlendShapeMode.Relative ? new Vector3[vertices.Length] : arrays[(int)Mesh.ArrayType.Normal];
                    if (arrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil)
                        target[(int)Mesh.ArrayType.Tangent] = mode == Mesh.BlendShapeMode.Relative ? new float[vertices.Length * 4] : arrays[(int)Mesh.ArrayType.Tangent];
                    mesh.AddSurfaceFromArrays(original.SurfaceGetPrimitiveType(s), arrays, new Godot.Collections.Array<Godot.Collections.Array> { target }, null,
                        original.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights);
                }
                source.Mesh = mesh; source.SetBlendShapeValue(0, -0.65f);
                var cards = HairCards.Apply(source, 99)!;
                Check(cards != null && ((ArrayMesh)cards.Mesh).GetBlendShapeCount() == 1, "synthetic translation target reaches generated cards");
                float worst = 0;
                for (int s = 0; s < cards!.Mesh.GetSurfaceCount(); s++)
                {
                    var basis = cards.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var target = cards.Mesh.SurfaceGetBlendShapeArrays(s)[0][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    for (int i = 0; i < basis.Length; i++)
                        worst = Math.Max(worst, (mode == Mesh.BlendShapeMode.Relative ? target[i] : target[i] - basis[i]).DistanceTo(delta));
                }
                Check(worst < 0.00002f, "every generated vertex inherits the synthetic translation exactly in " + mode + " mode");
                Check(Math.Abs(cards.GetBlendShapeValue(0) + 0.65f) < 1e-6, "negative synthetic morph weight is copied without clamping away its sign");
                _measurements.Add(new { kind = "synthetic-morph", mode = mode.ToString(), maximumTranslationErrorMeters = worst });
            }
            finally { root.Free(); }
        }
    }

    private void CheckCacheLifecycle(string path)
    {
        HairCards.ReleaseCache();
        var root = GD.Load<PackedScene>(path).Instantiate<Node3D>(); AddChild(root);
        try
        {
            var source = MeshNamed(root, "Mane");
            var first = HairCards.Apply(source, 0)!; var mesh = first.Mesh; string hash = Fingerprint(mesh); first.Free();
            for (int seed = 1; seed <= HairCards.MaxCacheEntries + 1; seed++)
            {
                HairCards.Apply(source, seed)!.Free();
                Check(HairCards.CachedMeshCount <= HairCards.MaxCacheEntries, "cache never exceeds its explicit resource bound");
            }
            var again = HairCards.Apply(source, 0)!;
            Check(again.Mesh != mesh && Fingerprint(again.Mesh) == hash, "bounded cache evicts old resources and regenerates deterministic geometry");
            Check(Fingerprint(mesh) == hash, "eviction does not invalidate a retained mesh");
        }
        finally { root.Free(); }
    }

    private void CheckMaterialVariants()
    {
        bool grey = false, white = false, dark = false;
        var fixtures = FixtureLibrary.Phase1.Where(d => !d.IsFoal && (d.GrayAmount > 0 || d.CoatLayers.Any(l => l.StartsWith("white_", StringComparison.Ordinal)) || d.CoatLayers.Contains("silver"))).ToArray();
        foreach (var descriptor in fixtures)
        {
            Check(_stage.Presenter.Present(descriptor, "idle").Supported, "pigment inheritance fixture is supported");
            var source = MeshNamed(_stage.Presenter.Model!, "Mane"); var cards = MeshNamed(_stage.Presenter.Model!, "ManeCards");
            CheckMaterials(source, cards);
            var material = (ShaderMaterial)cards.GetActiveMaterial(0);
            grey |= material.GetShaderParameter("grey_amount").AsSingle() > 0;
            white |= material.GetShaderParameter("white_fraction").AsSingle() > 0;
            dark |= material.GetShaderParameter("dark_fraction").AsSingle() > 0;
        }
        Check(grey && white && dark, "material checks exercised nonzero grey, white locks and silver dark strands (not only zero defaults)");
    }

    private void CheckMaterials(MeshInstance3D source, MeshInstance3D cards)
    {
        Check(source.GetActiveMaterial(0) is ShaderMaterial && cards.GetActiveMaterial(0) is ShaderMaterial, "both layers use a hair ShaderMaterial");
        var under = (ShaderMaterial)source.GetActiveMaterial(0); var over = (ShaderMaterial)cards.GetActiveMaterial(0);
        Check(under.Shader == over.Shader && under != over, "cards inherit the hair shader with their own material instance");
        foreach (string uniform in new[] { "hair_color", "tip_color", "seed", "grey_amount", "white_fraction", "dark_color", "dark_fraction" })
            Check(under.GetShaderParameter(uniform).Equals(over.GetShaderParameter(uniform)), "cards preserve hair uniform " + uniform);
        Check(under.GetShaderParameter("solid_clumps").AsBool() && !over.GetShaderParameter("solid_clumps").AsBool(), "underlayer is opaque and card material uses cutout path");
    }

    private async Task Capture(VisualDescriptor descriptor, string animation)
    {
        var p = _stage.Presenter;
        foreach (string coat in Option("--capture-coats", "bay,chestnut,silver_black,grey_overlay").Split(','))
        {
            var fixture = FixtureLibrary.Phase1.FirstOrDefault(f => f.IsFoal == descriptor.IsFoal && f.FixtureId.StartsWith(coat + ".", StringComparison.Ordinal));
            Check(fixture != null, "requested capture coat exists: " + coat);
            Check(p.Present(fixture! with { MeshFamilyId = descriptor.MeshFamilyId }, animation).Supported, "capture fixture loads");
            p.Pose(animation, animation == "walk" ? p.LocomotionPeriodSeconds * 0.37 : 0.43);
            foreach (string view in Option("--capture-views", "side,side_left,head,mane,mane_left,tail").Split(','))
            {
                if (view is "mane" or "mane_left" or "tail")
                {
                    var surface = HairSurface(p.Model!)[view == "tail" ? "TailCards" : "ManeCards"];
                    var min = surface.Aggregate(new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), (a, b) => a.Min(b));
                    var max = surface.Aggregate(new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity), (a, b) => a.Max(b));
                    _stage.Camera.SetView(view == "mane_left" ? 180 : 0, 4, Math.Max(1f, (max - min).Length() * 2f), (max + min) * 0.5f);
                }
                else _stage.FrameView(view);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng(Path.Combine(_output, $"{descriptor.MeshFamilyId}__{coat}__{animation}__{view}.png")) == Error.Ok, "capture PNG is written");
            }
        }
        if (animation == "idle")
        {
            Check(p.Present(descriptor, animation).Supported, "graze capture fixture loads");
            p.Pose("idle", 0); p.EnableIdleLife(1701); p.IdleLife!.HoldGraze(1);
            _stage.FrameView("q3");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(_output, $"{descriptor.MeshFamilyId}__graze.png")) == Error.Ok, "graze capture PNG is written");
            p.IdleLife.Clear();
        }
        foreach (int sign in new[] { -1, 1 })
        {
            var weights = descriptor.MorphWeights.ToDictionary(x => x.Key, x => x.Value);
            foreach (string channel in VisualCatalog.Instance.FamilyMeshBlendShapes[descriptor.MeshFamilyId].Values.SelectMany(m => m.Keys).Distinct())
                weights[channel] = sign * (int)VisualCatalog.Instance.BodyBlendShapeNormalization;
            Check(p.Present(descriptor with { MorphWeights = weights }, animation).Supported, "extreme-morph capture fixture loads");
            p.Pose(animation, animation == "walk" ? p.LocomotionPeriodSeconds * 0.37 : 0.43);
            _stage.FrameView("q3");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(_output, $"{descriptor.MeshFamilyId}__{animation}__morph_{(sign > 0 ? "plus" : "minus")}.png")) == Error.Ok,
                "extreme-morph capture PNG is written");
        }
    }

    private static string Option(string name, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? fallback;
    private static bool Selected(string option, string value) => Option(option, value).Split(',').Contains(value);

    private static Dictionary<string, Vector3[]> HairSurface(Node3D model) => new[] { "ManeCards", "TailCards" }
        .ToDictionary(name => name, name => SkinnedVertices(MeshNamed(model, name), model));

    private static Vector3[] SkinnedVertices(MeshInstance3D instance, Node3D model, bool applySkin = true)
    {
        var skeleton = instance.GetNode<Skeleton3D>(instance.Skeleton); var skin = instance.Skin!;
        var transforms = Enumerable.Range(0, skin.GetBindCount()).Select(i =>
        {
            int bone = skin.GetBindName(i).IsEmpty ? skin.GetBindBone(i) : skeleton.FindBone(skin.GetBindName(i));
            return skeleton.GetBoneGlobalPose(bone) * skin.GetBindPose(i);
        }).ToArray();
        var toModel = model.GlobalTransform.AffineInverse() * (applySkin ? skeleton.GlobalTransform : instance.GlobalTransform);
        var result = new List<Vector3>();
        for (int s = 0; s < instance.Mesh.GetSurfaceCount(); s++)
        {
            var a = instance.Mesh.SurfaceGetArrays(s);
            var positions = a[(int)Mesh.ArrayType.Vertex].AsVector3Array(); var basis = positions.ToArray();
            var bones = a[(int)Mesh.ArrayType.Bones].AsInt32Array(); var weights = a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            bool relative = ((ArrayMesh)instance.Mesh).BlendShapeMode == Mesh.BlendShapeMode.Relative;
            var targets = instance.Mesh.SurfaceGetBlendShapeArrays(s);
            for (int b = 0; b < instance.GetBlendShapeCount(); b++)
            {
                float weight = instance.GetBlendShapeValue(b); if (weight == 0) continue;
                var shape = targets[b][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                for (int i = 0; i < positions.Length; i++) positions[i] += weight * (relative ? shape[i] : shape[i] - basis[i]);
            }
            int width = weights.Length / positions.Length;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 skinned = applySkin ? Vector3.Zero : positions[i];
                if (applySkin)
                    for (int j = 0; j < width; j++) skinned += weights[i * width + j] * (transforms[bones[i * width + j]] * positions[i]);
                result.Add(toModel * skinned);
            }
        }
        return result.ToArray();
    }

    private void MeasureStretch(string pose, Dictionary<string, Vector3[]> posed)
    {
        foreach (var (component, deformed) in posed)
        {
            var instance = MeshNamed(_stage.Presenter.Model!, component);
            var basis = SkinnedVertices(instance, _stage.Presenter.Model!, applySkin: false);
            var ratios = new List<float>();
            int offset = 0, worstA = -1, worstB = -1;
            float maximum = 0, worstRest = 0, worstPosed = 0, maximumAddedLength = 0;
            for (int s = 0; s < instance.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = instance.Mesh.SurfaceGetArrays(s);
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                for (int i = 0; i < indices.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = offset + indices[i + e], b = offset + indices[i + (e + 1) % 3];
                    float restLength = basis[a].DistanceTo(basis[b]);
                    if (restLength <= 0.00001f) continue;
                    float posedLength = deformed[a].DistanceTo(deformed[b]);
                    float ratio = posedLength / restLength; ratios.Add(ratio);
                    maximumAddedLength = Math.Max(maximumAddedLength, posedLength - restLength);
                    if (ratio > maximum) { maximum = ratio; worstA = a; worstB = b; worstRest = restLength; worstPosed = posedLength; }
                }
                offset += arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
            }
            ratios.Sort();
            Check(ratios.Count > 0 && ratios.All(float.IsFinite), "deformed hair edges have finite length ratios");
            _measurements.Add(new { kind = "edge-stretch", pose, component, maximum, percentile99 = ratios[(int)((ratios.Count - 1) * 0.99f)],
                maximumAddedLengthMeters = maximumAddedLength, worstA, worstB, worstRestLengthMeters = worstRest, worstPosedLengthMeters = worstPosed });
            // Same 2x edge gate already used by FoalTransitionProbe; below this still needs visual review.
            // A narrow ribbon stretched many times its rest width is a tear, even if every coordinate is finite.
            Check(maximum <= 2f, $"hair ribbon edges retain the existing 2x deformation gate: {pose} {component} measured {maximum:F3}x");
        }
    }

    private static int[] RootIndices(Mesh mesh)
    {
        var roots = new List<int>(); int offset = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var uv = mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            for (int i = 0; i < uv.Length; i++) if (uv[i].Y > 0.9999f) roots.Add(offset + i);
            offset += uv.Length;
        }
        return roots.ToArray();
    }

    private static string Fingerprint(Mesh mesh)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var arrayMesh = (ArrayMesh)mesh;
        writer.Write(mesh.GetSurfaceCount()); writer.Write(arrayMesh.GetBlendShapeCount());
        writer.Write((int)arrayMesh.BlendShapeMode);
        for (int b = 0; b < arrayMesh.GetBlendShapeCount(); b++) writer.Write(arrayMesh.GetBlendShapeName(b).ToString());
        void Arrays(Godot.Collections.Array arrays)
        {
            foreach (var kind in new[] { Mesh.ArrayType.Vertex, Mesh.ArrayType.Normal, Mesh.ArrayType.Tangent, Mesh.ArrayType.TexUV, Mesh.ArrayType.TexUV2,
                         Mesh.ArrayType.Color, Mesh.ArrayType.Bones, Mesh.ArrayType.Weights, Mesh.ArrayType.Index })
            {
                var value = arrays[(int)kind]; writer.Write((int)value.VariantType);
                switch (value.VariantType)
                {
                    case Variant.Type.PackedVector3Array:
                        foreach (var v in value.AsVector3Array()) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); } break;
                    case Variant.Type.PackedVector2Array:
                        foreach (var v in value.AsVector2Array()) { writer.Write(v.X); writer.Write(v.Y); } break;
                    case Variant.Type.PackedFloat32Array:
                        foreach (float v in value.AsFloat32Array()) writer.Write(v); break;
                    case Variant.Type.PackedInt32Array:
                        foreach (int v in value.AsInt32Array()) writer.Write(v); break;
                    case Variant.Type.PackedColorArray:
                        foreach (var v in value.AsColorArray()) { writer.Write(v.R); writer.Write(v.G); writer.Write(v.B); writer.Write(v.A); } break;
                    case Variant.Type.Nil: break;
                    default: throw new InvalidOperationException("Uncovered mesh fingerprint type " + value.VariantType);
                }
            }
        }
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            Arrays(mesh.SurfaceGetArrays(s));
            foreach (var shape in mesh.SurfaceGetBlendShapeArrays(s)) Arrays(shape);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static MeshInstance3D MeshNamed(Node root, string name) => All<MeshInstance3D>(root).Single(m => m.Name == name);
    private static IEnumerable<T> All<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in All<T>(child)) yield return nested;
        }
    }
    private static float Distance(Dictionary<string, Vector3[]> a, Dictionary<string, Vector3[]> b) => a.Max(x => MaxDistance(x.Value, b[x.Key]));
    private static float MaxDistance(Vector3[] a, Vector3[] b) => a.Zip(b, (x, y) => x.DistanceTo(y)).Max();
    private void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private void WriteReport(string? failure) => File.WriteAllText(Path.Combine(_output, "hair-cards-report.json"), JsonSerializer.Serialize(new
    {
        checks = _checks, passed = failure == null, failure, measurements = _measurements,
        limitations = "CPU skin sampling validates geometry/binds/morphs, meaningful motion and a 2x per-edge stretch bound. Forelock movement is not proof of precise root attachment: the generator exposes no donor correspondence. It does not prove silhouette quality, neck coverage, self-intersection, or aliasing; inspect the rendered evidence. Both foal families intentionally share an authored master. Body CUSTOM0 is excluded; presenter body arrays are compared with an independent instance after the pre-existing BodyMap step. Direct HairCards generation separately checks unmodified imported body arrays."
    }, new JsonSerializerOptions { WriteIndented = true }));
}
