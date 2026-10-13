using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace Cloverbrook.Game.Visual;

public sealed record CoatRecipe(string Label, Color Body, Color Points, float PointsAmount, Color Counter, Color? Primitive,
    float Sooty, float Dapple, float Sheen)
{
    /// <summary>D-024 shade range (light, dark) around <see cref="Body"/>; null keeps one shade.</summary>
    public (Color Light, Color Dark)? BodyRange { get; init; }
    public (float Min, float Max)? SootyRange { get; init; }
    public (float Min, float Max)? DappleRange { get; init; }
    /// <summary>D-024: how much darker a dun's undiluted head reads than its body (0 = not dun).</summary>
    public float DunHead { get; init; }
}
public sealed record HairRecipe(Color Color, Color Tip);
public sealed record SkinRecipe(Color Color, float NoseAmount);
public sealed record EyeRecipe(Color Iris, Color Ring);
/// <summary>
/// Optional Light Body-only RGB8 signed multiplier. Source bytes and accepted UV evidence
/// are checked before import, then imported pixels are checked losslessly. Missing or
/// incompatible inputs leave the coat procedural and this detail layer disabled.
/// </summary>
public sealed record BodySignedDetail(string Path, string Encoding, float Strength, string SourceGlbSha256, string TextureSha256,
    string PixelSha256, string HandoffPath, string BindingPath, string CorpusSha256, float BakedStrength,
    string UvReuseStatus, int RetainedTriangleCount, int NeutralRecoveredFaceCount, int NeutralRecoveredPixelCount,
    string NeutralMaskPath, string NeutralMaskSha256, string HandoffSha256, string BindingSha256,
    RigOnlyDerivationEvidence? RigOnlyDerivation = null);
public sealed record RigOnlyDerivationEvidence(string PreservationReportPath, string PreservationReportSha256,
    string BaselineBlendSha256, string BaselineGlbSha256, string CandidateBlendSha256, string CandidateGlbSha256,
    string ParentBindingSha256, string ParentHandoffSha256);
public sealed record WalkAnimationVariant(string Scene, string SourceGlbSha256, string CandidateBlendSha256,
    string MotionProfilePath, string MotionProfileSha256, string ReceiptPath, string ReceiptSha256,
    string CpuMotionAuditPath, string CpuMotionAuditSha256, double ForwardSpeedMetersPerSecond,
    int PeriodFrames, int FramesPerSecond, float DutyFactor, BodySignedDetail? BodySignedDetail);
public sealed record MeshFamily(string Scene, bool Placeholder, string Age, string HairGeometry = "cards", string? Limitation = null,
    Vector3? HeadViewTarget = null, BodySignedDetail? BodySignedDetail = null, string? BodySignedDetailDisabledReason = null,
    WalkAnimationVariant? WalkVariant = null, string? WalkVariantDisabledReason = null, string? RestGeometryDisabledReason = null);

/// <summary>Result of checking a descriptor against what this renderer can actually draw.</summary>
public sealed record VisualSupport(bool Supported, IReadOnlyList<string> Problems, IReadOnlyList<string> Limitations)
{
    public string Summary => Supported
        ? (Limitations.Count == 0 ? "Supported" : "Supported with limitations: " + string.Join("; ", Limitations))
        : "UNSUPPORTED: " + string.Join("; ", Problems);
}

/// <summary>
/// The renderer's capability list (data/visual_catalog.json). It maps descriptor recipe
/// strings to artistic parameters. A string that is not listed is reported, never guessed.
/// </summary>
public sealed class VisualCatalog
{
    public const string Path = "res://data/visual_catalog.json";

    public string Version { get; private init; } = "";
    public HashSet<string> ImplementedLayers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, MeshFamily> MeshFamilies { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, CoatRecipe> Coats { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HairRecipe> Hair { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, SkinRecipe> Skin { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, EyeRecipe> Eyes { get; } = new(StringComparer.Ordinal);
    public Color White { get; private set; }
    public Color PinkSkin { get; private set; }
    public Color HoofDark { get; private set; }
    public Color HoofPale { get; private set; }
    public float HeightScalePerUnit { get; private set; }
    public float BodyBlendShapeNormalization { get; private set; }
    public Dictionary<string, string> BodyBlendShapes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> MeshBlendShapes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> FamilyMeshBlendShapes { get; } = new(StringComparer.Ordinal);
    public List<string> ComponentMorphLimitations { get; } = new();
    public HashSet<string> UnsupportedMorphs { get; } = new(StringComparer.Ordinal);

    private static VisualCatalog? _instance;
    public static VisualCatalog Instance => _instance ??= Load(FileAccess.GetFileAsString(Path));

    public static VisualCatalog Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var cat = new VisualCatalog { Version = r.GetProperty("visualAssetCatalogVersion").GetString()! };
        foreach (var l in r.GetProperty("implementedLayers").EnumerateArray()) cat.ImplementedLayers.Add(l.GetString()!);
        foreach (var m in r.GetProperty("meshFamilies").EnumerateObject())
        {
            string scene = m.Value.GetProperty("scene").GetString()!;
            var detail = ParseBodySignedDetail(m.Value, m.Name, scene, out var detailDisabledReason);
            var walkVariant = ParseWalkAnimationVariant(m.Value, m.Name,
                m.Value.GetProperty("age").GetString()!, out var walkVariantDisabledReason);
            cat.MeshFamilies[m.Name] = new MeshFamily(scene, m.Value.GetProperty("placeholder").GetBoolean(),
                m.Value.GetProperty("age").GetString()!,
                m.Value.TryGetProperty("hairGeometry", out var hairGeometry) ? hairGeometry.GetString()! : "cards",
                m.Value.TryGetProperty("limitation", out var limitation) ? limitation.GetString() : null,
                m.Value.TryGetProperty("headViewTarget", out var headTarget)
                    ? new Vector3(headTarget[0].GetSingle(), headTarget[1].GetSingle(), headTarget[2].GetSingle()) : null,
                detail, detailDisabledReason, walkVariant, walkVariantDisabledReason,
                FoalRestGeometryEvidence.DisabledReason(m.Value, m.Name, scene));
        }
        foreach (var c in r.GetProperty("coatRecipes").EnumerateObject())
        {
            var v = c.Value;
            cat.Coats[c.Name] = new CoatRecipe(v.GetProperty("label").GetString()!, Hex(v, "body"), Hex(v, "points"),
                v.GetProperty("pointsAmount").GetSingle(), Hex(v, "counter"),
                v.TryGetProperty("primitive", out _) ? Hex(v, "primitive") : null,
                v.GetProperty("sooty").GetSingle(), v.GetProperty("dapple").GetSingle(), v.GetProperty("sheen").GetSingle())
            {
                BodyRange = v.TryGetProperty("bodyRange", out var br) ? (Color.FromHtml(br[0].GetString()!), Color.FromHtml(br[1].GetString()!)) : null,
                SootyRange = v.TryGetProperty("sootyRange", out var sr) ? (sr[0].GetSingle(), sr[1].GetSingle()) : null,
                DappleRange = v.TryGetProperty("dappleRange", out var dr) ? (dr[0].GetSingle(), dr[1].GetSingle()) : null,
                DunHead = v.TryGetProperty("dunHead", out var dh) ? dh.GetSingle() : 0f,
            };
        }
        foreach (var h in r.GetProperty("hairRecipes").EnumerateObject()) cat.Hair[h.Name] = new HairRecipe(Hex(h.Value, "color"), Hex(h.Value, "tip"));
        foreach (var s in r.GetProperty("skinRecipes").EnumerateObject()) cat.Skin[s.Name] = new SkinRecipe(Hex(s.Value, "color"), s.Value.GetProperty("noseAmount").GetSingle());
        foreach (var e in r.GetProperty("eyeRecipes").EnumerateObject()) cat.Eyes[e.Name] = new EyeRecipe(Hex(e.Value, "iris"), Hex(e.Value, "ring"));
        var w = r.GetProperty("white");
        cat.White = Hex(w, "color"); cat.PinkSkin = Hex(w, "pinkSkin"); cat.HoofDark = Hex(w, "hoofDark"); cat.HoofPale = Hex(w, "hoofPale");
        var morph = r.GetProperty("morphSupport");
        cat.HeightScalePerUnit = morph.GetProperty("height").GetProperty("perUnit").GetSingle();
        cat.BodyBlendShapeNormalization = morph.GetProperty("bodyBlendShapeNormalization").GetSingle();
        if (!float.IsFinite(cat.BodyBlendShapeNormalization) || cat.BodyBlendShapeNormalization <= 0f)
            throw new JsonException("morphSupport.bodyBlendShapeNormalization must be a finite positive number");
        foreach (var shape in morph.GetProperty("bodyBlendShapes").EnumerateObject())
            cat.BodyBlendShapes[shape.Name] = shape.Value.GetString()!;
        cat.MeshBlendShapes["Body"] = cat.BodyBlendShapes;
        if (morph.TryGetProperty("meshBlendShapes", out var meshShapes))
            foreach (var mesh in meshShapes.EnumerateObject())
            {
                var channels = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var shape in mesh.Value.EnumerateObject()) channels[shape.Name] = shape.Value.GetString()!;
                cat.MeshBlendShapes[mesh.Name] = channels;
            }
        if (cat.MeshBlendShapes.TryGetValue("Body", out var declaredBody) &&
            (declaredBody.Count != cat.BodyBlendShapes.Count || cat.BodyBlendShapes.Any(pair =>
                !declaredBody.TryGetValue(pair.Key, out var shape) || !string.Equals(pair.Value, shape, StringComparison.Ordinal))))
            throw new JsonException("morphSupport.bodyBlendShapes must exactly match meshBlendShapes.Body");
        if (!morph.TryGetProperty("familyMeshBlendShapes", out var familyMeshShapes))
            throw new JsonException("morphSupport.familyMeshBlendShapes is required; declare each mesh family's actual shape channels");
        foreach (var family in familyMeshShapes.EnumerateObject())
        {
            if (!cat.MeshFamilies.ContainsKey(family.Name))
                throw new JsonException($"morphSupport.familyMeshBlendShapes references unknown mesh family '{family.Name}'");
            var components = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var mesh in family.Value.EnumerateObject())
            {
                var channels = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var shape in mesh.Value.EnumerateObject())
                {
                    if (!cat.BodyBlendShapes.ContainsKey(shape.Name))
                        throw new JsonException($"morphSupport.familyMeshBlendShapes.{family.Name}.{mesh.Name} references unknown descriptor morph '{shape.Name}'");
                    var shapeName = shape.Value.GetString()!;
                    if (string.IsNullOrWhiteSpace(shapeName))
                        throw new JsonException($"morphSupport.familyMeshBlendShapes.{family.Name}.{mesh.Name}.{shape.Name} has an empty shape key");
                    channels[shape.Name] = shapeName;
                }
                if (channels.Values.Distinct(StringComparer.Ordinal).Count() != channels.Count)
                    throw new JsonException($"morphSupport.familyMeshBlendShapes.{family.Name}.{mesh.Name} maps multiple descriptor morphs to one shape key");
                components[mesh.Name] = channels;
            }
            var bodyChannels = components.TryGetValue("Body", out var bodyMap)
                ? bodyMap.Keys.ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            foreach (var (component, channels) in components)
            {
                if (component == "Body") continue;
                var accessoryOnly = channels.Keys.Where(channel => !bodyChannels.Contains(channel)).ToArray();
                if (accessoryOnly.Length > 0)
                    throw new JsonException($"morphSupport.familyMeshBlendShapes.{family.Name}.{component} declares channels absent from Body: {string.Join(", ", accessoryOnly)}");
            }
            cat.FamilyMeshBlendShapes[family.Name] = components;
        }
        foreach (var familyId in cat.MeshFamilies.Keys)
            if (!cat.FamilyMeshBlendShapes.ContainsKey(familyId))
                throw new JsonException($"morphSupport.familyMeshBlendShapes must explicitly declare '{familyId}' (use an empty object when no channels are supported)");
        if (morph.TryGetProperty("componentMorphLimitations", out var componentLimits))
            foreach (var item in componentLimits.EnumerateArray()) cat.ComponentMorphLimitations.Add(item.GetString()!);
        foreach (var u in morph.GetProperty("unsupported").EnumerateArray()) cat.UnsupportedMorphs.Add(u.GetString()!);
        return cat;
    }

    private static BodySignedDetail? ParseBodySignedDetail(JsonElement family, string familyId, string scene, out string? disabledReason)
    {
        disabledReason = null;
        if (!family.TryGetProperty("bodySignedDetail", out var detail)) return null;
        if (detail.ValueKind != JsonValueKind.Object)
        {
            disabledReason = "metadata is not an object";
            return null;
        }
        if (!detail.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True)
        {
            disabledReason = "disabled by family metadata";
            return null;
        }
        string? path = ReadString(detail, "path");
        string? encoding = ReadString(detail, "encoding");
        string? sourceHash = ReadString(detail, "sourceGlbSha256");
        string? textureHash = ReadString(detail, "textureSha256");
        if (familyId != "horse.standard.adult.v1")
            return DisableBodySignedDetail("signed detail is currently supported only for the Light adult Body", out disabledReason);
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://assets/horse/production/", StringComparison.Ordinal) ||
            !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return DisableBodySignedDetail("detail path must be a production PNG resource", out disabledReason);
        if (!string.Equals(encoding, "rgb8_signed_multiplier_v1", StringComparison.Ordinal))
            return DisableBodySignedDetail($"unknown encoding '{encoding ?? ""}'", out disabledReason);
        if (!detail.TryGetProperty("strength", out var strengthNode) || strengthNode.ValueKind != JsonValueKind.Number ||
            !strengthNode.TryGetSingle(out float strength) || !float.IsFinite(strength) || strength < 0f || strength > 1f)
            return DisableBodySignedDetail("strength must be finite and between 0 and 1", out disabledReason);
        if (!IsSha256(sourceHash) || !IsSha256(textureHash))
            return DisableBodySignedDetail("source and texture SHA-256 provenance hashes are required", out disabledReason);
        if (!FileAccess.FileExists(scene)) return DisableBodySignedDetail("source GLB is unavailable for provenance checking", out disabledReason);
        var sourceBytes = FileAccess.GetFileAsBytes(scene);
        if (!string.Equals(Sha256(sourceBytes), sourceHash, StringComparison.Ordinal))
            return DisableBodySignedDetail("source GLB SHA-256 does not match family metadata", out disabledReason);
        if (!FileAccess.FileExists(path)) return DisableBodySignedDetail("declared detail atlas is missing", out disabledReason);
        var textureBytes = FileAccess.GetFileAsBytes(path);
        if (!string.Equals(Sha256(textureBytes), textureHash, StringComparison.Ordinal))
            return DisableBodySignedDetail("detail atlas SHA-256 does not match family metadata", out disabledReason);

        string? handoffPath = ReadString(detail, "handoffPath");
        string? handoffHash = ReadString(detail, "handoffSha256");
        string? bindingPath = ReadString(detail, "bindingPath");
        string? bindingHash = ReadString(detail, "bindingSha256");
        string? corpusHash = ReadString(detail, "corpusSha256");
        string? neutralMaskPath = ReadString(detail, "neutralMaskPath");
        string? neutralMaskHash = ReadString(detail, "neutralMaskSha256");
        string? uvReuseStatus = ReadString(detail, "uvReuseStatus");
        bool originalEvidence = IsProductionEvidencePath(handoffPath, "game-integration-handoff.json") &&
                                IsProductionEvidencePath(bindingPath, "light-v5-uv-reuse-binding.json");
        bool walkDerivedEvidence = IsWalkDerivedEvidencePath(handoffPath, "game-integration-handoff.json") &&
                                   IsWalkDerivedEvidencePath(bindingPath, "light-v5-uv-reuse-binding.json");
        if ((!originalEvidence && !walkDerivedEvidence) ||
            !IsProductionEvidencePath(neutralMaskPath, "ai-neutral-skin-mask.png") ||
            !IsSha256(handoffHash) || !IsSha256(bindingHash) || !IsSha256(corpusHash) || !IsSha256(neutralMaskHash))
            return DisableBodySignedDetail("UV reuse evidence paths or hashes are missing or malformed", out disabledReason);
        if (uvReuseStatus != "retained-subset+neutral-recovered-skin")
            return DisableBodySignedDetail("UV reuse status is not the approved retained-subset plus neutral-recovered-skin scope", out disabledReason);
        if (!FileAccess.FileExists(handoffPath) || !FileAccess.FileExists(bindingPath) || !FileAccess.FileExists(neutralMaskPath))
            return DisableBodySignedDetail("UV reuse handoff or binding evidence is missing", out disabledReason);
        if (!string.Equals(Sha256(FileAccess.GetFileAsBytes(handoffPath)), handoffHash, StringComparison.Ordinal) ||
            !string.Equals(Sha256(FileAccess.GetFileAsBytes(bindingPath)), bindingHash, StringComparison.Ordinal) ||
            !string.Equals(Sha256(FileAccess.GetFileAsBytes(neutralMaskPath)), neutralMaskHash, StringComparison.Ordinal))
            return DisableBodySignedDetail("UV reuse handoff or binding SHA-256 does not match metadata", out disabledReason);

        if (!detail.TryGetProperty("bakedStrength", out var bakedNode) || bakedNode.ValueKind != JsonValueKind.Number ||
            !bakedNode.TryGetSingle(out float bakedStrength) || !float.IsFinite(bakedStrength) || Mathf.Abs(bakedStrength - 0.3f) > 0.000001f ||
            !TryReadPositiveInt(detail, "retainedTriangleCount", out int retainedTriangles) || retainedTriangles != 21253 ||
            !TryReadPositiveInt(detail, "neutralRecoveredFaceCount", out int neutralFaces) || neutralFaces != 453 ||
            !TryReadPositiveInt(detail, "neutralRecoveredPixelCount", out int neutralPixels) || neutralPixels != 14577)
            return DisableBodySignedDetail("UV reuse compatibility counts or baked detail strength are incompatible", out disabledReason);

        if (!TryValidateAcceptedEvidence(handoffPath!, bindingPath!, sourceHash!, textureHash!, handoffHash!, bindingHash!, corpusHash!, neutralMaskHash!,
                retainedTriangles, neutralFaces, neutralPixels, walkDerivedEvidence, out string evidenceReason, out var rigOnlyDerivation))
            return DisableBodySignedDetail(evidenceReason, out disabledReason);

        var image = new Image();
        if (image.LoadPngFromBuffer(textureBytes) != Error.Ok || image.GetWidth() != 2048 || image.GetHeight() != 2048 || image.GetFormat() != Image.Format.Rgb8)
            return DisableBodySignedDetail("detail atlas must be a decodable 2048x2048 RGB8 PNG", out disabledReason);
        var pixels = image.GetData();
        byte minCode = byte.MaxValue, maxCode = byte.MinValue;
        for (int i = 0; i + 2 < pixels.Length; i += 3)
        {
            if (pixels[i] != pixels[i + 1] || pixels[i] != pixels[i + 2])
                return DisableBodySignedDetail("detail atlas RGB channels are not equal data values", out disabledReason);
            minCode = Math.Min(minCode, pixels[i]);
            maxCode = Math.Max(maxCode, pixels[i]);
        }
        if (minCode != 90 || maxCode != 166)
            return DisableBodySignedDetail("detail atlas code range does not match accepted v5 evidence", out disabledReason);

        var mask = new Image();
        var maskBytes = FileAccess.GetFileAsBytes(neutralMaskPath);
        if (mask.LoadPngFromBuffer(maskBytes) != Error.Ok || mask.GetWidth() != 2048 || mask.GetHeight() != 2048 || mask.GetFormat() != Image.Format.L8)
            return DisableBodySignedDetail("neutral-skin evidence mask must be a decodable 2048x2048 grayscale PNG", out disabledReason);
        var maskPixels = mask.GetData();
        int neutralMaskCount = 0;
        for (int i = 0; i < maskPixels.Length; i++)
        {
            if (maskPixels[i] != 255) continue;
            neutralMaskCount++;
            if (pixels[i * 3] != 128)
                return DisableBodySignedDetail("accepted atlas is not neutral on recovered historical skin pixels", out disabledReason);
        }
        if (neutralMaskCount != neutralPixels)
            return DisableBodySignedDetail("neutral recovered pixel count does not match the accepted mask", out disabledReason);

        return new BodySignedDetail(path, encoding!, strength, sourceHash!, textureHash!, Sha256(pixels),
            handoffPath!, bindingPath!, corpusHash!, bakedStrength, uvReuseStatus!, retainedTriangles, neutralFaces, neutralPixels,
            neutralMaskPath!, neutralMaskHash!, handoffHash!, bindingHash!, rigOnlyDerivation);
    }

    private static WalkAnimationVariant? ParseWalkAnimationVariant(JsonElement family, string familyId, string age,
        out string? disabledReason)
    {
        disabledReason = null;
        if (!family.TryGetProperty("walkVariant", out var variant)) return null;
        disabledReason = "Walk variant metadata is malformed or incompatible";
        try
        {
            if (variant.ValueKind != JsonValueKind.Object) return null;
            if (familyId != "horse.standard.adult.v1")
                return RemainingWalkEvidence.Parse(variant, familyId, age, out disabledReason);
            const string expectedFamily = "horse.standard.adult.v1";
            const string expectedScene = "res://assets/horse/production/horse_light_walk.glb";
            const string expectedGlb = "b9e2d3ee455c14fb037596cf2dfd9a8241c5760ceea38855238b523bd734a628";
            const string expectedBlend = "9f6f3c08a002a1869b308f4c99a4bcd71ecab1073261adca14171cb8354df7da";
            const string expectedEvidenceRoot = "res://assets/horse/production/evidence/light-walk-v6-anatomy/";
            const string expectedProfileSha = "7a6fa0bea5aee0070054e7f142ae5c87ea937ed07e642b2af3953e47f39cc5d0";
            const string expectedReceiptSha = "49c552de3011fd43d8ed78e9e4d2f71ebddf2788efe241f3dd9fe11007ec3a6e";
            const string expectedAuditSha = "58e7b16ff8235d52c251b4207b70e58edd3ef81ad2bfbeb4cab16a273c512253";
            const double expectedSpeed = 0.749997329711914;

            string scene = ReadString(variant, "scene") ?? "";
            string sourceGlb = ReadString(variant, "sourceGlbSha256") ?? "";
            string candidateBlend = ReadString(variant, "candidateBlendSha256") ?? "";
            string profilePath = ReadString(variant, "motionProfilePath") ?? "";
            string profileSha = ReadString(variant, "motionProfileSha256") ?? "";
            string receiptPath = ReadString(variant, "receiptPath") ?? "";
            string receiptSha = ReadString(variant, "receiptSha256") ?? "";
            string auditPath = ReadString(variant, "cpuMotionAuditPath") ?? "";
            string auditSha = ReadString(variant, "cpuMotionAuditSha256") ?? "";
            if (familyId != expectedFamily || age != "adult" ||
                scene != expectedScene || sourceGlb != expectedGlb || candidateBlend != expectedBlend ||
                profilePath != expectedEvidenceRoot + "walk-motion-profile.json" || profileSha != expectedProfileSha ||
                receiptPath != expectedEvidenceRoot + "walk-motion-receipt.json" || receiptSha != expectedReceiptSha ||
                auditPath != expectedEvidenceRoot + "neutral-motion-audit.json" || auditSha != expectedAuditSha)
                return null;

            if (!variant.TryGetProperty("forwardSpeedMetersPerSecond", out var speedNode) ||
                speedNode.ValueKind != JsonValueKind.Number || !speedNode.TryGetDouble(out var speed) ||
                !double.IsFinite(speed) || Math.Abs(speed - expectedSpeed) > 1e-12 ||
                !variant.TryGetProperty("periodFrames", out var periodNode) || !periodNode.TryGetInt32(out int period) || period != 58 ||
                !variant.TryGetProperty("fps", out var fpsNode) || !fpsNode.TryGetInt32(out int fps) || fps != 60 ||
                !variant.TryGetProperty("dutyFactor", out var dutyNode) || !dutyNode.TryGetDouble(out double duty) ||
                !double.IsFinite(duty) || Math.Abs(duty - 0.6) > 1e-6 ||
                !FileAccess.FileExists(scene) || Sha256(FileAccess.GetFileAsBytes(scene)) != expectedGlb)
                return null;

            // These additional gates bind actual exported anatomy and the exact project import.
            const string expectedAnatomySha = "49c57886d5c5eaa47c9151f15e319bcceb0518a3b1fd91d0b0621bcc0d1d4fc4";
            const string expectedImportSha = "9d09b85b64b4aeeb50bd4278d617c306b306b715a2e18fedee70d29b95e0b88d";
            byte[] anatomyBytes = FileAccess.GetFileAsBytes(expectedEvidenceRoot + "anatomy-report.json");
            byte[] importBytes = FileAccess.GetFileAsBytes(expectedEvidenceRoot + "game-import-report.json");
            if (Sha256(anatomyBytes) != expectedAnatomySha || Sha256(importBytes) != expectedImportSha) return null;
            const string expectedNativeAnatomySha = "c3316079ce3be72cd8d86b71c5b3069ace8267b636905a50efaa08f6c8b35973";
            byte[] nativeAnatomyBytes = FileAccess.GetFileAsBytes(expectedEvidenceRoot + "native-anatomy-report.json");
            if (Sha256(nativeAnatomyBytes) != expectedNativeAnatomySha) return null;
            using var nativeAnatomyDoc = JsonDocument.Parse(nativeAnatomyBytes);
            if (!WalkAnatomyEvidence.Validate(nativeAnatomyDoc.RootElement, expectedGlb, expectedProfileSha) ||
                !nativeAnatomyDoc.RootElement.GetProperty("usesProjectImport").GetBoolean()) return null;
            using var anatomyDoc = JsonDocument.Parse(anatomyBytes);
            using var importDoc = JsonDocument.Parse(importBytes);
            if (!WalkAnatomyEvidence.Validate(anatomyDoc.RootElement, expectedGlb, expectedProfileSha) ||
                importDoc.RootElement.GetProperty("candidateGlbSha256").GetString() != expectedGlb ||
                importDoc.RootElement.GetProperty("failures").GetArrayLength() != 0 ||
                !importDoc.RootElement.GetProperty("usesProjectImport").GetBoolean() ||
                importDoc.RootElement.GetProperty("projectImportFps").GetInt32() != 60 ||
                importDoc.RootElement.GetProperty("animationOptimizerEnabled").GetBoolean()) return null;

            // The body detail is separately validated against the derived GLB and the exact
            // rig-only UV preservation evidence. The ordinary family keeps its original scene
            // and original, independently validated signed-detail metadata.
            var walkDetail = ParseBodySignedDetail(variant, familyId, scene, out _);
            if (walkDetail == null || walkDetail.SourceGlbSha256 != expectedGlb) return null;

            if (!FileAccess.FileExists(profilePath) || !FileAccess.FileExists(receiptPath) || !FileAccess.FileExists(auditPath))
                return null;
            byte[] profileBytes = FileAccess.GetFileAsBytes(profilePath);
            byte[] receiptBytes = FileAccess.GetFileAsBytes(receiptPath);
            byte[] auditBytes = FileAccess.GetFileAsBytes(auditPath);
            if (Sha256(profileBytes) != profileSha || Sha256(receiptBytes) != receiptSha || Sha256(auditBytes) != auditSha)
                return null;

            using var profileDoc = JsonDocument.Parse(profileBytes);
            using var receiptDoc = JsonDocument.Parse(receiptBytes);
            using var auditDoc = JsonDocument.Parse(auditBytes);
            var profile = profileDoc.RootElement;
            var receipt = receiptDoc.RootElement;
            var audit = auditDoc.RootElement;
            if (profile.GetProperty("schema").GetString() != "horse.light_walk.motion_profile.v1" ||
                profile.GetProperty("candidateBlendSha256").GetString() != expectedBlend ||
                profile.GetProperty("sourceGlbSha256").GetString() != expectedGlb ||
                profile.GetProperty("periodFrames").GetInt32() != period || profile.GetProperty("fps").GetInt32() != fps ||
                Math.Abs(profile.GetProperty("forwardSpeedMetersPerSecond").GetDouble() - speed) > 1e-12 ||
                Math.Abs(profile.GetProperty("dutyFactor").GetDouble() - duty) > 1e-6 ||
                profile.GetProperty("sourceRootInPlace").ValueKind != JsonValueKind.True ||
                receipt.GetProperty("schema").GetString() != "horse.light_walk.motion_receipt.v1" ||
                receipt.GetProperty("candidateBlendSha256").GetString() != expectedBlend ||
                receipt.GetProperty("sourceGlbSha256").GetString() != expectedGlb ||
                receipt.GetProperty("motionProfilePath").GetString() != profilePath ||
                receipt.GetProperty("motionProfileSha256").GetString() != profileSha ||
                receipt.GetProperty("cpuMotionAuditPath").GetString() != auditPath ||
                receipt.GetProperty("cpuMotionAuditSha256").GetString() != auditSha ||
                receipt.GetProperty("neutralPhysicalGatesPassed").ValueKind != JsonValueKind.True ||
                audit.GetProperty("stage").GetString() != "footplant_neutral_61_phase_audit" ||
                audit.GetProperty("candidate_sha256").GetString() != expectedBlend ||
                audit.GetProperty("accepted_cpu_neutral").ValueKind != JsonValueKind.True ||
                audit.GetProperty("fps").GetInt32() != fps || Math.Abs(audit.GetProperty("period_frames").GetDouble() - period) > 1e-9 ||
                Math.Abs(audit.GetProperty("duty_factor").GetDouble() - duty) > 1e-9 ||
                Math.Abs(audit.GetProperty("shared_speed_mps").GetDouble() - speed) > 1e-12 ||
                audit.GetProperty("sampled_frames").GetArrayLength() != 61)
                return null;

            string[] requiredGates =
            {
                "normalized_weights", "whole_body_stretch_le_1_6", "collar_stretch_le_1_6",
                "loop_root_relative_le_1mm", "stance_slip_le_5mm", "sole_min_z_ge_minus_2mm",
                "stance_p05_z_le_3mm", "swing_middle_clearance_ge_30mm", "endpoint_target_mismatch_le_2mm"
            };
            var metricGates = audit.GetProperty("metric_gates");
            foreach (string gate in requiredGates)
                if (!metricGates.TryGetProperty(gate, out var passed) || passed.ValueKind != JsonValueKind.True)
                    return null;

            disabledReason = null;
            return new WalkAnimationVariant(scene, sourceGlb, candidateBlend, profilePath, profileSha,
                receiptPath, receiptSha, auditPath, auditSha, speed, period, fps, (float)duty, walkDetail);
        }
        catch (Exception e)
        {
            disabledReason = "Walk variant validation failed: " + e.Message;
            return null;
        }
    }

    private static bool IsProductionEvidencePath(string? path, string fileName) =>
        string.Equals(path, "res://assets/horse/production/evidence/light-v5-detail/" + fileName, StringComparison.Ordinal);

    private static bool IsWalkDerivedEvidencePath(string? path, string fileName) =>
        string.Equals(path, "res://assets/horse/production/evidence/light-walk-v6-anatomy/" + fileName, StringComparison.Ordinal);

    private static bool TryReadPositiveInt(JsonElement node, string key, out int value)
    {
        value = 0;
        return node.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number &&
               item.TryGetInt32(out value) && value > 0;
    }

    private static bool TryValidateAcceptedEvidence(string handoffPath, string bindingPath, string sourceHash, string textureHash,
        string handoffHash, string bindingHash, string corpusHash, string neutralMaskHash, int retainedTriangles, int neutralFaces, int neutralPixels,
        bool walkDerivedEvidence, out string reason, out RigOnlyDerivationEvidence? rigOnlyDerivation)
    {
        reason = "UV reuse handoff or binding content is malformed or incompatible";
        rigOnlyDerivation = null;
        try
        {
            using var handoffDoc = JsonDocument.Parse(FileAccess.GetFileAsBytes(handoffPath));
            using var bindingDoc = JsonDocument.Parse(FileAccess.GetFileAsBytes(bindingPath));
            var handoff = handoffDoc.RootElement;
            var binding = bindingDoc.RootElement;
            var reference = handoff.GetProperty("reference");
            var compatibility = reference.GetProperty("compatibility");
            var reuse = handoff.GetProperty("reuse_proof");
            var detail = handoff.GetProperty("detail");
            var contract = handoff.GetProperty("runtime_contract");
            var source = handoff.GetProperty("source");

            if (handoff.GetProperty("status").GetString() != "ACCEPTED_LIGHT_DETAIL_READY_FOR_GAME" ||
                handoff.GetProperty("family_scope").GetString() != "Light Body only" ||
                source.GetProperty("glb_sha256").GetString() != sourceHash ||
                detail.GetProperty("sha256").GetString() != textureHash ||
                compatibility.GetProperty("default_whole_body_status").GetString() != "incompatible" ||
                compatibility.GetProperty("binding_approved_status").GetString() != "retained-subset+neutral-recovered-skin" ||
                compatibility.GetProperty("retained_body_compatibility").GetProperty("status").GetString() != "retained-subset" ||
                compatibility.GetProperty("retained_body_compatibility").GetProperty("comparison_scope").GetString() != "exact primary-UV triangle footprints and multiplicities only" ||
                compatibility.GetProperty("retained_body_compatibility").GetProperty("reference_triangle_count").GetInt32() != 25222 ||
                compatibility.GetProperty("retained_body_compatibility").GetProperty("current_triangle_count").GetInt32() != retainedTriangles ||
                compatibility.GetProperty("retained_body_compatibility").GetProperty("removed_triangle_count").GetInt32() != 3969 ||
                reuse.GetProperty("binding_file").GetString() != "recipe/light-v5-uv-reuse-binding.json" ||
                reuse.GetProperty("binding_sha256").GetString() != bindingHash ||
                reuse.GetProperty("corpus_file").GetString() != "source/horse_light-v5-uv0-provenance.json" ||
                reuse.GetProperty("corpus_sha256").GetString() != corpusHash ||
                reuse.GetProperty("neutral_mask_file").GetString() != "ai-neutral-skin-mask.png" ||
                reuse.GetProperty("neutral_mask_sha256").GetString() != neutralMaskHash ||
                reuse.GetProperty("retained_triangles").GetInt32() != retainedTriangles ||
                reuse.GetProperty("neutral_recovered_faces").GetInt32() != neutralFaces ||
                reuse.GetProperty("neutral_recovered_pixels").GetInt32() != neutralPixels ||
                detail.GetProperty("baked_detail_strength").GetDouble() != 0.3 ||
                detail.GetProperty("code_bounds")[0].GetInt32() != 90 || detail.GetProperty("code_bounds")[1].GetInt32() != 166 ||
                contract.GetProperty("integration_blend").GetDouble() != 1.0 ||
                contract.GetProperty("disabled_blend").GetDouble() != 0.0 ||
                contract.GetProperty("import").GetString() != "lossless" ||
                contract.GetProperty("primary_uv").GetString() != "UVMap / Godot UV; keep normal PNG orientation" ||
                contract.GetProperty("sampler").GetString() != "Non-Color/data; no source_color or sRGB decode" ||
                contract.GetProperty("decode").GetString() != "1 + ((texture.r * 255 - 128) / 127) * 0.12")
                return false;

            if (binding.GetProperty("schema").GetString() != "horse_ai_uv_reuse_binding.v1" ||
                binding.GetProperty("kind").GetString() != "neutral-only-recovered-skin" ||
                binding.GetProperty("current_glb_sha256").GetString() != sourceHash ||
                binding.GetProperty("corpus_sha256").GetString() != corpusHash ||
                binding.GetProperty("corpus_relative_path").GetString() != "source/horse_light-v5-uv0-provenance.json" ||
                binding.GetProperty("current_blend_sha256").GetString() != source.GetProperty("sha256").GetString() ||
                binding.GetProperty("reference_blend_sha256").GetString() != reference.GetProperty("source_sha256").GetString() ||
                binding.GetProperty("reference_triangle_count").GetInt32() != 25222 ||
                binding.GetProperty("reference_uv_contract_sha256").GetString() != reference.GetProperty("uv_contract_sha256").GetString())
                return false;

            if (walkDerivedEvidence)
            {
                if (!TryValidateRigOnlyDerivation(handoff, binding, sourceHash, textureHash, corpusHash, neutralMaskHash,
                        out rigOnlyDerivation, out reason))
                    return false;
            }
            else if (handoff.TryGetProperty("rig_only_derivation", out _) || binding.TryGetProperty("rig_only_derivation", out _))
                return false;

            reason = "";
            return true;
        }
        catch (Exception e)
        {
            reason = "UV reuse evidence could not be read: " + e.Message;
            return false;
        }
    }

    private static bool TryValidateRigOnlyDerivation(JsonElement handoff, JsonElement binding, string sourceHash,
        string textureHash, string corpusHash, string neutralMaskHash,
        out RigOnlyDerivationEvidence? evidence, out string reason)
    {
        evidence = null;
        reason = "rig-only geometry/UV preservation evidence is malformed or incompatible";
        try
        {
            if (!handoff.TryGetProperty("rig_only_derivation", out var handoffDerivation) ||
                !binding.TryGetProperty("rig_only_derivation", out var bindingDerivation) ||
                handoffDerivation.ValueKind != JsonValueKind.Object || bindingDerivation.ValueKind != JsonValueKind.Object)
                return false;
            string[] derivationKeys = { "baseline_blend_sha256", "baseline_glb_sha256", "parent_binding_sha256", "parent_handoff_sha256", "preservation_report_path", "preservation_report_sha256" };
            if (!HasExactProperties(handoffDerivation, derivationKeys) || !HasExactProperties(bindingDerivation, derivationKeys))
                return false;
            foreach (var key in derivationKeys)
                if (handoffDerivation.GetProperty(key).GetString() != bindingDerivation.GetProperty(key).GetString()) return false;

            const string baselineBlend = "9c2975ba2cd2cf4311b45e96c827bae0474c9259b92fe8b81bafa100f12bc295";
            const string baselineGlb = "26a4870d8e93c488a513171fce52de3e76a0837692ed8c1a9a59e40d4a1c6cbc";
            const string parentBinding = "f81192626f5583179b46362c6ae4f1fbdc92341b25f697973f5524d0309db3eb";
            const string parentHandoff = "6410f515e3445726b0a1a652f7c6bfa096b25006c726d219ef3df91353d5f773";
            const string proofPath = "res://assets/horse/production/evidence/light-walk-v6-anatomy/geometry-uv-preservation.json";
            const string baselineGlbPath = "res://assets/horse/production/horse_light.glb";
            const string atlasPath = "res://assets/horse/production/horse_light_body_detail_v5.png";
            const string parentHandoffPath = "res://assets/horse/production/evidence/light-v5-detail/game-integration-handoff.json";
            const string parentBindingPath = "res://assets/horse/production/evidence/light-v5-detail/light-v5-uv-reuse-binding.json";
            const string neutralMaskPath = "res://assets/horse/production/evidence/light-v5-detail/ai-neutral-skin-mask.png";
            string? Read(JsonElement node, string key) => node.GetProperty(key).GetString();
            string? proofSha = Read(handoffDerivation, "preservation_report_sha256");
            if (Read(handoffDerivation, "baseline_blend_sha256") != baselineBlend ||
                Read(handoffDerivation, "baseline_glb_sha256") != baselineGlb ||
                Read(handoffDerivation, "parent_binding_sha256") != parentBinding ||
                Read(handoffDerivation, "parent_handoff_sha256") != parentHandoff ||
                Read(handoffDerivation, "preservation_report_path") != proofPath || !IsSha256(proofSha) ||
                !FileAccess.FileExists(proofPath) ||
                !string.Equals(Sha256(FileAccess.GetFileAsBytes(proofPath)), proofSha, StringComparison.Ordinal))
                return false;

            if (!FileAccess.FileExists(baselineGlbPath) || !FileAccess.FileExists(atlasPath) ||
                !FileAccess.FileExists(parentHandoffPath) || !FileAccess.FileExists(parentBindingPath) || !FileAccess.FileExists(neutralMaskPath) ||
                Sha256(FileAccess.GetFileAsBytes(baselineGlbPath)) != baselineGlb ||
                Sha256(FileAccess.GetFileAsBytes(atlasPath)) != textureHash ||
                Sha256(FileAccess.GetFileAsBytes(parentHandoffPath)) != parentHandoff ||
                Sha256(FileAccess.GetFileAsBytes(parentBindingPath)) != parentBinding ||
                Sha256(FileAccess.GetFileAsBytes(neutralMaskPath)) != neutralMaskHash)
                return false;
            using (var parentHandoffDoc = JsonDocument.Parse(FileAccess.GetFileAsBytes(parentHandoffPath)))
            using (var parentBindingDoc = JsonDocument.Parse(FileAccess.GetFileAsBytes(parentBindingPath)))
            {
                var parent = parentHandoffDoc.RootElement;
                var parentBind = parentBindingDoc.RootElement;
                if (parent.GetProperty("status").GetString() != "ACCEPTED_LIGHT_DETAIL_READY_FOR_GAME" ||
                    parent.GetProperty("family_scope").GetString() != "Light Body only" ||
                    parent.GetProperty("source").GetProperty("sha256").GetString() != baselineBlend ||
                    parent.GetProperty("source").GetProperty("glb_sha256").GetString() != baselineGlb ||
                    parent.GetProperty("detail").GetProperty("sha256").GetString() != textureHash ||
                    parent.GetProperty("reuse_proof").GetProperty("binding_sha256").GetString() != parentBinding ||
                    parent.GetProperty("reuse_proof").GetProperty("corpus_sha256").GetString() != corpusHash ||
                    parent.GetProperty("reuse_proof").GetProperty("neutral_mask_sha256").GetString() != neutralMaskHash ||
                    parentBind.GetProperty("schema").GetString() != "horse_ai_uv_reuse_binding.v1" ||
                    parentBind.GetProperty("current_blend_sha256").GetString() != baselineBlend ||
                    parentBind.GetProperty("current_glb_sha256").GetString() != baselineGlb ||
                    parentBind.GetProperty("corpus_sha256").GetString() != corpusHash)
                    return false;
            }

            using var proofDoc = JsonDocument.Parse(FileAccess.GetFileAsBytes(proofPath));
            var proof = proofDoc.RootElement;
            string[] preserved = { "positions", "topology", "transform", "UV_layers_order", "UV_active_render", "frozen_UVs", "colors", "morphs", "UV0" };
            string[] components = { "Body", "Eyes", "Mane", "Tail" };
            string[] snapshotKeys = preserved.Append("weights").ToArray();
            if (proof.GetProperty("schema").GetString() != "horse_rig_only_geometry_uv_preservation.v1" ||
                proof.GetProperty("pass").ValueKind != JsonValueKind.True ||
                proof.GetProperty("baseline_blend_sha256").GetString() != baselineBlend ||
                proof.GetProperty("glb_sha256").GetString() != sourceHash ||
                proof.GetProperty("mesh_set_matches").ValueKind != JsonValueKind.True ||
                proof.GetProperty("forbidden_changes").GetArrayLength() != 0 ||
                !ExactStringArray(proof.GetProperty("preserved_fields"), preserved))
                return false;
            string? candidateBlendHash = proof.GetProperty("candidate_blend_sha256").GetString();
            var handoffSource = handoff.GetProperty("source");
            if (!IsSha256(candidateBlendHash) || handoffSource.GetProperty("sha256").GetString() != candidateBlendHash ||
                handoffSource.GetProperty("glb_sha256").GetString() != sourceHash ||
                binding.GetProperty("current_blend_sha256").GetString() != candidateBlendHash ||
                binding.GetProperty("current_glb_sha256").GetString() != sourceHash)
                return false;

            var baselineMeshes = proof.GetProperty("baseline");
            var candidateMeshes = proof.GetProperty("candidate");
            if (!HasExactProperties(baselineMeshes, components) || !HasExactProperties(candidateMeshes, components)) return false;
            foreach (var component in components)
            {
                var before = baselineMeshes.GetProperty(component);
                var after = candidateMeshes.GetProperty(component);
                if (!HasExactProperties(before, snapshotKeys) || !HasExactProperties(after, snapshotKeys)) return false;
                foreach (var field in preserved)
                {
                    string? beforeHash = before.GetProperty(field).GetString();
                    string? afterHash = after.GetProperty(field).GetString();
                    if (!IsSha256(beforeHash) || !IsSha256(afterHash) || beforeHash != afterHash) return false;
                }
                if (!IsSha256(before.GetProperty("weights").GetString()) || !IsSha256(after.GetProperty("weights").GetString())) return false;
            }

            if (!TryValidateExportCornerAudit(proof.GetProperty("export_corner_audit"), candidateBlendHash!, sourceHash, out reason) ||
                !proof.TryGetProperty("export_morph_audit", out var morphAudit) ||
                !TryValidateExportMorphAudit(morphAudit, candidateBlendHash!, sourceHash, out reason)) return false;
            evidence = new RigOnlyDerivationEvidence(proofPath, proofSha!, baselineBlend, baselineGlb, candidateBlendHash!, sourceHash, parentBinding, parentHandoff);
            reason = "";
            return true;
        }
        catch (Exception e)
        {
            reason = "rig-only preservation evidence could not be read: " + e.Message;
            return false;
        }
    }

    private static bool TryValidateExportCornerAudit(JsonElement audit, string candidateBlendHash, string glbHash, out string reason)
    {
        reason = "rig-only Blender-to-GLB corner audit failed or is incomplete";
        string[] components = { "Body", "Eyes", "Mane", "Tail" };
        string[] meshKeys = { "source_vertices", "export_vertices", "source_triangles", "export_triangles", "unmatched_export_triangles", "unmatched_source_triangles", "ambiguous_matches", "max_absolute_attribute_error", "pass" };
        if (audit.GetProperty("pass").ValueKind != JsonValueKind.True ||
            audit.GetProperty("source_sha256").GetString() != candidateBlendHash ||
            audit.GetProperty("glb_sha256").GetString() != glbHash ||
            audit.GetProperty("mesh_set_matches").ValueKind != JsonValueKind.True ||
            !HasExactProperties(audit.GetProperty("meshes"), components)) return false;
        foreach (var component in components)
        {
            var mesh = audit.GetProperty("meshes").GetProperty(component);
            if (!HasExactProperties(mesh, meshKeys) || mesh.GetProperty("pass").ValueKind != JsonValueKind.True ||
                mesh.GetProperty("unmatched_export_triangles").GetInt32() != 0 ||
                mesh.GetProperty("unmatched_source_triangles").GetInt32() != 0 ||
                mesh.GetProperty("ambiguous_matches").GetInt32() != 0 ||
                mesh.GetProperty("source_triangles").GetInt32() != mesh.GetProperty("export_triangles").GetInt32()) return false;
            var errors = mesh.GetProperty("max_absolute_attribute_error");
            string[] expectedErrors = component switch
            {
                "Body" => new[] { "TEXCOORD_0", "TEXCOORD_1", "COLOR_0" },
                "Eyes" => new[] { "TEXCOORD_0", "TEXCOORD_1" },
                _ => new[] { "TEXCOORD_0" },
            };
            if (!HasExactProperties(errors, expectedErrors)) return false;
            foreach (var key in expectedErrors)
            {
                double value = errors.GetProperty(key).GetDouble();
                double limit = key == "COLOR_0" ? 0.5 / 65535.0 + 1e-7 : 2e-6;
                if (!double.IsFinite(value) || value < 0 || value > limit) return false;
            }
        }
        reason = "";
        return true;
    }

    private static bool TryValidateExportMorphAudit(JsonElement audit, string candidateBlendHash, string glbHash, out string reason)
    {
        reason = "rig-only Blender-to-GLB morph corner audit failed or is incomplete";
        string[] components = { "Body", "Eyes", "Mane", "Tail" };
        string[] targets = { "body_length", "chest_width", "neck_length", "muscle" };
        string[] auditKeys = { "source_sha256", "glb_sha256", "meshes", "pass", "mesh_set_matches" };
        string[] meshKeys = { "target_names", "source_triangles", "export_triangles", "unmatched_export_triangles", "unmatched_source_triangles", "ambiguous_matches", "max_position_delta_error", "pass" };
        if (!HasExactProperties(audit, auditKeys) || audit.GetProperty("pass").ValueKind != JsonValueKind.True ||
            audit.GetProperty("source_sha256").GetString() != candidateBlendHash ||
            audit.GetProperty("glb_sha256").GetString() != glbHash ||
            audit.GetProperty("mesh_set_matches").ValueKind != JsonValueKind.True ||
            !HasExactProperties(audit.GetProperty("meshes"), components)) return false;
        foreach (var component in components)
        {
            var mesh = audit.GetProperty("meshes").GetProperty(component);
            if (!HasExactProperties(mesh, meshKeys) || mesh.GetProperty("pass").ValueKind != JsonValueKind.True ||
                !ExactStringArray(mesh.GetProperty("target_names"), targets) ||
                mesh.GetProperty("unmatched_export_triangles").GetInt32() != 0 ||
                mesh.GetProperty("unmatched_source_triangles").GetInt32() != 0 ||
                mesh.GetProperty("ambiguous_matches").GetInt32() != 0 ||
                mesh.GetProperty("source_triangles").GetInt32() <= 0 || mesh.GetProperty("export_triangles").GetInt32() <= 0 ||
                mesh.GetProperty("source_triangles").GetInt32() != mesh.GetProperty("export_triangles").GetInt32()) return false;
            var errors = mesh.GetProperty("max_position_delta_error");
            if (!HasExactProperties(errors, targets)) return false;
            foreach (var target in targets)
            {
                double value = errors.GetProperty(target).GetDouble();
                if (!double.IsFinite(value) || value < 0 || value > 2e-6) return false;
            }
        }
        reason = "";
        return true;
    }

    private static bool HasExactProperties(JsonElement node, IEnumerable<string> keys) =>
        node.ValueKind == JsonValueKind.Object && node.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(keys.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool ExactStringArray(JsonElement node, IReadOnlyList<string> expected) =>
        node.ValueKind == JsonValueKind.Array && node.GetArrayLength() == expected.Count &&
        node.EnumerateArray().Select(item => item.GetString()).SequenceEqual(expected);

    private static BodySignedDetail? DisableBodySignedDetail(string reason, out string? disabledReason)
    {
        disabledReason = reason;
        return null;
    }

    private static string? ReadString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Color Hex(JsonElement e, string name) => Color.FromHtml(e.GetProperty(name).GetString()!);

    /// <summary>Explicit support check. Problems block rendering; limitations are shown but allowed.</summary>
    public VisualSupport Check(VisualDescriptor d)
    {
        var problems = new List<string>();
        var limits = new List<string>();
        if (d.VisualAssetCatalogVersion != Version)
            problems.Add($"asset catalog '{d.VisualAssetCatalogVersion}' is not installed (renderer has '{Version}')");
        foreach (var layer in d.CoatLayers.Where(l => !ImplementedLayers.Contains(l)))
            problems.Add($"coat layer '{layer}' has no renderer implementation");
        if (!Coats.ContainsKey(d.CoatRecipe)) problems.Add($"coat recipe '{d.CoatRecipe}' is not in the visual catalog");
        if (!Hair.ContainsKey(d.HairRecipe)) problems.Add($"hair recipe '{d.HairRecipe}' is not in the visual catalog");
        if (!Skin.ContainsKey(d.SkinRecipe)) problems.Add($"skin recipe '{d.SkinRecipe}' is not in the visual catalog");
        if (!Eyes.ContainsKey(d.EyeRecipe)) problems.Add($"eye recipe '{d.EyeRecipe}' is not in the visual catalog");
        if (!MeshFamilies.TryGetValue(d.MeshFamilyId, out var family)) problems.Add($"mesh family '{d.MeshFamilyId}' has no asset");
        else
        {
            if (!string.IsNullOrEmpty(family.RestGeometryDisabledReason)) problems.Add(family.RestGeometryDisabledReason);
            if (family.Placeholder) limits.Add("TEMPORARY placeholder mesh");
            if (!string.IsNullOrEmpty(family.Limitation)) limits.Add(family.Limitation);
            if (!string.IsNullOrEmpty(family.WalkVariantDisabledReason)) limits.Add("checked Walk variant disabled: " + family.WalkVariantDisabledReason);
            if (!string.IsNullOrEmpty(family.BodySignedDetailDisabledReason))
                limits.Add("body signed detail disabled: " + family.BodySignedDetailDisabledReason);
            if (family.BodySignedDetail is { } bodyDetail)
                limits.Add($"Light Body signed detail UV scope: {bodyDetail.RetainedTriangleCount} retained triangles plus {bodyDetail.NeutralRecoveredFaceCount} recovered historical skin faces kept neutral; whole-body UV compatibility is not claimed");
        }
        if (d.GrayAmount != 0 && !ImplementedLayers.Contains("grey_age")) problems.Add("grey progression is not implemented");
        if (d.LpPatternPresent || d.Patn1Coverage != 0) problems.Add("leopard complex / PATN1 is not implemented");
        if (d.PrimitiveMarkingStrength > 0 && Coats.TryGetValue(d.CoatRecipe, out var c) && c.Primitive is null && !d.CoatLayers.Contains("primitive_markings"))
            problems.Add("primitive markings requested without a primitive_markings layer");
        if (!string.IsNullOrEmpty(d.CosmeticLayer) && !HorseTack.IsKnownLayer(d.CosmeticLayer)) limits.Add($"cosmetic layer '{d.CosmeticLayer}' ignored (unknown tack)");
        var familyMorphs = FamilyMeshBlendShapes.TryGetValue(d.MeshFamilyId, out var familyShapes) && familyShapes.TryGetValue("Body", out var bodyShapes)
            ? bodyShapes.Keys.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var unappliedMorphs = d.MorphWeights.Where(kv => kv.Value != 0 &&
            (UnsupportedMorphs.Contains(kv.Key) || (kv.Key != "height" && !familyMorphs.Contains(kv.Key))))
            .Select(kv => kv.Key).ToArray();
        if (unappliedMorphs.Length > 0) limits.Add("morphs not applied: " + string.Join(",", unappliedMorphs));
        limits.AddRange(ComponentMorphLimitations);
        if (d.TestOnly) limits.Add("test-only descriptor");
        return new VisualSupport(problems.Count == 0, problems, limits);
    }
}
