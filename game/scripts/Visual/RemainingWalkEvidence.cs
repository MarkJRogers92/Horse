using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>Exact, independently proven neutral Stock/juvenile assets. No implicit family fallback.</summary>
internal static class RemainingWalkEvidence
{
    private sealed record Accepted(string Asset, string Age, string Glb, string Blend, double Speed,
        int SoleWitnesses, IReadOnlyDictionary<string, string> Evidence);

    // Generated from the reviewed bytes, never from untrusted catalog declarations.
    private static readonly Accepted Stock = new("stock", "adult",
        "d705a9a8443b30ee8e4c4eb859cf8bc1a20ab1e9178fcc28fa439f5308cf4168",
        "71b8c95f6291bd9b4a8b89978aa76aa1efe6bf1ea4b819822e68564b1e5dfab8", 0.749997329711914, 429,
        new Dictionary<string, string> {
            ["native-anatomy-report"] = "08af5addb1153ded1e732ba1693f28995f94e57404086e96b5ef763760f6b298",
            ["anatomy-report"] = "e5a72caa00b770209e84844f684a06a89d210272ea8ab507236a733f50292ae6",
            ["dense-deformation-audit"] = "f1a2b035fdd1004f96e48f99e67fe6a99563426b896982880c18bea04c20ffc9",
            ["game-import-report"] = "543305d29cc5e68cf54e47d1003a77f47d386dfc74030b94cd8fc0631c23aa14",
            ["report"] = "fc4e4bdc7462c7583564cefc37a67b5f841ec36b69df43a46855c237212e35db",
            ["source-audit"] = "f8f0653124b3c1fe2a3829a5474f734f2c8d23193b9160448fe63c4b1db1931b",
            ["surface-report"] = "c44948f6c94ebb4deec7a343232a988ce52992325dea08c3b61c7b6a88258514",
            ["walk-motion-profile"] = "f46be30cfa401b2fc45d917d9f5fe7fa1c392c5a084084aa736a37f398549be7",
         });
    private static readonly Accepted Foal = new("foal", "foal",
        "5f302d885e7a331de7cbc7f494846becac5eceae062e8ffed805ab2ff31beddc",
        "6183af14b955d9fa20636deabaf38ca8aa1527e4a13af6216653ede6152bfcca", 0.48275862068965525, 176,
        new Dictionary<string, string> {
            ["anatomy-report"] = "89b42b511985e0ac3c035bc70ce8920517d3d9335a42696028bc497e9a8b3e43",
            ["attachment-audit"] = "5172fe26530e3041b492a5ea0747e021999da9a87dcad1492c5bc9515ac8fe5e",
            ["dense-deformation-audit"] = "cf734d5228b6e220e5778f01d3e0cbceae0c8ece81ba64d5e2fdc7291448eea9",
            ["game-import-report"] = "b49df41ac653e8b6900071a5ae20757675ff3e02f289bdce8057d8bd0e70be27",
            ["native-anatomy-report"] = "f8e7ff20ec4f19a78fae4149f7bfe3ee1776a9bc4cfc6d4d8c6c6363ea340db9",
            ["native-hind-report"] = "e5916d4628aeb7c413abeca6ff894998206df167ff5a9f4f1ee8af33d28eadc9",
            ["placement-report"] = "f816ca2c2ca8a18e16b6d29fd796a2a7ff9d8e8fda3958b718cf434777bf0ff7",
            ["report"] = "cbc314698a96d5842153b6d7dc5c7eea1e1b23f45b01e15f3df9d63a27f2911d",
            ["source-audit"] = "fe82b2de54dc538aa1a3ff7e6029d0fb7e591d8e654f88663a67d429df8e2cec",
            ["surface-report"] = "ba80273c4d73537fbb2adfd0dba56ca2f169087a499f76934c20881dcca4d96f",
            ["walk-motion-profile"] = "d06129015da0de7561fdcc81b456bdfd14d730402848207ed3e23d4b70bae634",
         });

    public static WalkAnimationVariant? Parse(JsonElement variant, string family, string age, out string? reason)
    {
        reason = "Walk evidence is missing, stale, or incompatible with this family";
        var accepted = family switch
        {
            "horse.compact.adult.v1" => Stock,
            "horse.standard.foal.v1" or "horse.compact.foal.v1" => Foal,
            _ => null
        };
        if (accepted == null || age != accepted.Age) return null;
        var a = accepted;
        string scene = $"res://assets/horse/production/horse_{a.Asset}_walk.glb";
        string root = $"res://assets/horse/production/evidence/{a.Asset}-walk-anatomy/";
        try
        {
            if (Text(variant, "scene") != scene || Text(variant, "sourceGlbSha256") != a.Glb ||
                Text(variant, "candidateBlendSha256") != a.Blend ||
                !Number(variant, "forwardSpeedMetersPerSecond", a.Speed) ||
                !Number(variant, "periodFrames", 58) || !Number(variant, "fps", 60) ||
                !Number(variant, "dutyFactor", 0.6) ||
                (variant.TryGetProperty("bodySignedDetail", out var detail) && detail.ValueKind != JsonValueKind.Null) ||
                HashFile(scene) != a.Glb) return null;

            var files = new Dictionary<string, JsonDocument>();
            try
            {
                foreach (var (name, sha) in a.Evidence)
                {
                    if (HashFile(root + name + ".json") != sha) return null;
                    files[name] = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + name + ".json"));
                }
                if (Text(variant, "motionProfilePath") != root + "walk-motion-profile.json" ||
                    Text(variant, "motionProfileSha256") != a.Evidence["walk-motion-profile"] ||
                    Text(variant, "receiptPath") != root + "game-import-report.json" ||
                    Text(variant, "receiptSha256") != a.Evidence["game-import-report"] ||
                    Text(variant, "cpuMotionAuditPath") != root + "source-audit.json" ||
                    Text(variant, "cpuMotionAuditSha256") != a.Evidence["source-audit"]) return null;

                if (!WalkAnatomyEvidence.Validate(files["anatomy-report"].RootElement, a.Glb, a.Evidence["walk-motion-profile"])) return null;
                if (!WalkAnatomyEvidence.Validate(files["native-anatomy-report"].RootElement, a.Glb, a.Evidence["walk-motion-profile"]) ||
                    !True(files["native-anatomy-report"].RootElement, "usesProjectImport")) return null;
                if (a.Asset == "foal" && !ValidateFoalPlacement(files["placement-report"].RootElement,
                    files["anatomy-report"].RootElement, a.Glb, a.Evidence["walk-motion-profile"])) return null;
                var profile = files["walk-motion-profile"].RootElement;
                var source = files["source-audit"].RootElement;
                var surface = files["surface-report"].RootElement;
                var runtime = files["game-import-report"].RootElement;
                var dense = files["dense-deformation-audit"].RootElement;
                if (Text(profile, "schema") != $"horse.{a.Asset}_walk.motion_profile.v1" ||
                    Text(profile, "sourceGlbSha256") != a.Glb || Text(profile, "candidateBlendSha256") != a.Blend ||
                    !Number(profile, "periodFrames", 58) || !Number(profile, "fps", 60) ||
                    !Number(profile, "forwardSpeedMetersPerSecond", a.Speed) || !Number(profile, "dutyFactor", 0.6) ||
                    !True(profile, "sourceRootInPlace") ||
                    Text(source, "schema") != $"horse.{a.Asset}_walk.candidate.{(a.Asset == "foal" ? "v2" : "v1")}" ||
                    Text(source, "candidateGlbSha256") != a.Glb || Text(source, "candidateBlendSha256") != a.Blend ||
                    (a.Asset == "foal" ? source.GetProperty("protectedMeshDataExact").ValueKind != JsonValueKind.False ||
                        !ValidateFoalGeometry(surface, a.Glb, a.Blend) : !True(source, "protectedMeshDataExact")) ||
                    source.GetProperty("reusedLightAtlas").GetBoolean() ||
                    source.GetProperty("bodyMaxEdgeStretch").GetDouble() > 2 ||
                    source.GetProperty("loopMaxVertexDeltaM").GetDouble() > 0.001 ||
                    Text(dense, "candidateBlendSha256") != a.Blend || !True(dense, "bodyStretchPass") ||
                    dense.GetProperty("sampleCount").GetInt32() != 581 ||
                    Text(surface, "candidateGlbSha256") != a.Glb || !Empty(surface, "failures")) return null;
                if (a.Asset == "foal")
                {
                    if (!ValidateFoalHind(files["native-hind-report"].RootElement, a.Glb)) return null;
                }
                else
                    foreach (var component in surface.GetProperty("components").EnumerateObject())
                        foreach (var attribute in component.Value.EnumerateObject())
                            if (!Number(attribute.Value, "maximumError", 0)) return null;
                var engine = runtime.GetProperty("engine");
                if (Text(runtime, "candidateGlbSha256") != a.Glb || !True(runtime, "usesProjectImport") ||
                    !Number(engine, "major", 4) || !Number(engine, "minor", 7) || !Number(engine, "patch", 2) ||
                    !Number(runtime, "importBakeFps", 60) || !Number(runtime, "projectImportFps", 60) ||
                    runtime.GetProperty("animationOptimizerEnabled").GetBoolean() ||
                    !Number(runtime, "meshCount", 4) || !Empty(runtime, "failures")) return null;
                var meshes = runtime.GetProperty("meshes").EnumerateArray().ToArray();
                if (meshes.Length != 4 || !meshes.Select(m => Text(m, "name")).ToHashSet().SetEquals(new[] { "Body", "Eyes", "Mane", "Tail" }) ||
                    meshes.Any(m => !Number(m, "blendShapes", 4) || !Number(m, "zeroWeightVertices", 0) || !Number(m, "nonUnitWeightVertices", 0))) return null;
                var contact = runtime.GetProperty("contact");
                if (!Number(contact, "registered", a.SoleWitnesses) || !Empty(contact, "failures")) return null;
                foreach (var limb in contact.GetProperty("limbs").EnumerateObject())
                    if (limb.Value.GetProperty("maxPerAxisStanceMotionMeters").GetDouble() > 0.005 ||
                        limb.Value.GetProperty("minStanceHeightMeters").GetDouble() < -0.002 ||
                        limb.Value.GetProperty("maxStanceSoleMinimumMeters").GetDouble() > 0.003 ||
                        limb.Value.GetProperty("centralSwingMinimumMeters").GetDouble() < 0.01) return null;
                foreach (var component in runtime.GetProperty("allComponentDeformation").EnumerateObject())
                    if (!Empty(component.Value, "failures") || component.Value.GetProperty("maxBodyEdgeStretch").GetDouble() > 2) return null;
                if (!Empty(runtime.GetProperty("attachments"), "failures")) return null;
                reason = null;
                return new WalkAnimationVariant(scene, a.Glb, a.Blend, root + "walk-motion-profile.json", a.Evidence["walk-motion-profile"],
                    root + "game-import-report.json", a.Evidence["game-import-report"], root + "source-audit.json", a.Evidence["source-audit"],
                    a.Speed, 58, 60, 0.6f, null);
            }
            finally { foreach (var file in files.Values) file.Dispose(); }
        }
        catch (Exception error) { reason += ": " + error.Message; return null; }
    }

    // Explicit owner-approved geometry branch. Historical rig-only proofs remain immutable;
    // positions are not relabeled as exact. Stock still requires the original exact contract.
    private static bool ValidateFoalGeometry(JsonElement report, string glb, string blend)
    {
        if (Text(report, "schema") != "horse.foal_walk.bounded_geometry.v1" ||
            Text(report, "sourceBlendSha256") != "4aa253f8c6a63f1fc377801460db3d4e4129513c4ee2a2fa2eefee155f62869d" ||
            Text(report, "sourceGlbSha256") != "28d2893d1de080f80ca95f946b2872a18d74beb4f12428340e8cd328f32812e3" ||
            Text(report, "candidateGlbSha256") != glb || Text(report, "candidateBlendSha256") != blend ||
            !True(report, "pass") || !Empty(report, "failures") ||
            report.GetProperty("allPositionsExact").ValueKind != JsonValueKind.False ||
            !Number(report, "changedVertexCount", 1361)) return false;
        double offset = report.GetProperty("maxOffsetM").GetDouble();
        if (!double.IsFinite(offset) || offset <= 0 || offset > 0.050001) return false;
        foreach (string key in new[] { "meshSetMatches", "topologyExact", "uvExact", "colorsExact", "transformsExact",
            "weightsExact", "untouchedPositionsExact", "positionsWithinAuthorizedRegion", "maxOffsetWithinLimit",
            "morphDeltasPreserved", "untouchedShapePositionsExact", "normalsOutsideSupportExact",
            "exportCornerCorrespondenceExact", "exportProtectedAttributesExact", "exportPositionsMatchCandidate",
            "exportMorphDeltasPreserved", "exportNormalsOutsideSupportExact", "rawPackedNormalsOutsideSupportExact",
            "exportUntouchedPositionsExact", "exportSkinAttributesExact", "liveGlbRegionPass" })
            if (!True(report, key)) return false;
        var limits = report.GetProperty("limits");
        return Text(limits, "mesh") == "Body" && Number(limits, "sourceXExclusiveMaximumM", -0.24) &&
            Number(limits, "sourceZExclusiveMinimumM", 0.40) && Number(limits, "sourceZExclusiveMaximumM", 0.91) &&
            Number(limits, "maxOffsetM", 0.09) && Number(limits, "morphDeltaToleranceM", 1e-7);
    }

    private static bool ValidateFoalHind(JsonElement report, string glb)
    {
        if (Text(report, "sourceGlbSha256") != glb || !True(report, "usesProjectImport") ||
            !Number(report, "sampleCount", 581) || !Empty(report, "failures")) return false;
        var limbs = report.GetProperty("limbs");
        if (!limbs.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "L", "R" })) return false;
        foreach (var limb in limbs.EnumerateObject())
        {
            double gap = limb.Value.GetProperty("maximumJointCoincidenceErrorM").GetDouble();
            double stifle = limb.Value.GetProperty("minimumForwardStifleOffsetM").GetDouble();
            double hock = limb.Value.GetProperty("maximumForwardHockOffsetM").GetDouble();
            double lo = limb.Value.GetProperty("relativeUpperGaskinRotationMinDegrees").GetDouble();
            double hi = limb.Value.GetProperty("relativeUpperGaskinRotationMaxDegrees").GetDouble();
            if (!double.IsFinite(gap) || gap < 0 || gap > 0.001 || !double.IsFinite(stifle) || stifle <= 0 ||
                !double.IsFinite(hock) || hock >= 0 || !double.IsFinite(lo) || !double.IsFinite(hi) || hi - lo < 5) return false;
        }
        return true;
    }

    // Rig-specific style bounds supplement, and do not replace, the physical/anatomy gates.
    // Signed lanes must remain separate. These are authored targets, not veterinary norms.
    private static bool ValidateFoalPlacement(JsonElement report, JsonElement anatomy, string glb, string profile)
    {
        if (Text(report, "schema") != "horse.walk.placement.v1" || Text(report, "sourceGlbSha256") != glb ||
            Text(report, "motionProfileSha256") != profile || !Number(report, "sampleCount", 117) ||
            !Number(report, "registeredSolePoints", 176) || report.GetProperty("phases").GetArrayLength() != 117) return false;
        var metrics = report.GetProperty("metrics");
        bool Within(string key, double min, double max)
        {
            double value = metrics.GetProperty(key).GetDouble();
            return double.IsFinite(value) && value >= min && value <= max;
        }
        if (!Within("foreTrackWidthMinM", 0.09, 0.13) || !Within("foreTrackWidthMaxM", 0.09, 0.13) ||
            !Within("hindTrackWidthMinM", 0.15, 0.19) || !Within("hindTrackWidthMaxM", 0.15, 0.19) ||
            !Within("meanForeHindSpanM", 0.70, 0.80) || !Within("stanceTravelM", 0.27, 0.29)) return false;
        foreach (var limb in anatomy.GetProperty("limbs").EnumerateObject())
        {
            double fold = limb.Value.GetProperty("maxSwingFlexionDegrees").GetDouble();
            if (!double.IsFinite(fold) || fold < 20 || fold > 45) return false;
        }
        return true;
    }

    private static string? Text(JsonElement value, string key) => value.GetProperty(key).GetString();
    private static bool Number(JsonElement value, string key, double expected)
    {
        double number = value.GetProperty(key).GetDouble();
        return double.IsFinite(number) && Math.Abs(number - expected) <= 1e-12;
    }
    private static bool True(JsonElement value, string key) => value.GetProperty(key).ValueKind == JsonValueKind.True;
    private static bool Empty(JsonElement value, string key) => value.GetProperty(key).GetArrayLength() == 0;
    private static string HashFile(string path) => FileAccess.FileExists(path)
        ? Convert.ToHexString(SHA256.HashData(FileAccess.GetFileAsBytes(path))).ToLowerInvariant() : "";
}
