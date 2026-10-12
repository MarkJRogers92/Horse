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
        "7a3a78e1eb0cb30ec2fe989218f82bc429cc369acc60f70d471676025099b188",
        "ac23bb9241663b6129faf5cac89e33ad68a67db59deca3d000508e8eb1057109", 0.48275862068965525, 176,
        new Dictionary<string, string> {
            ["anatomy-report"] = "f3da8f56e4e267afba99886c6620551adfde14080812b59de361ad41af2bd681",
            ["attachment-audit"] = "a1f9cbe3b394ca38a44e600ee176b24f0e5602a912e15d53976d1511cd617851",
            ["dense-deformation-audit"] = "247ec052662f491620f8844a9b740dfba42fa33ab7454c1d56fd42cfc6eaee7a",
            ["game-import-report"] = "c3953140d461c93a685dde9201bc48de94226ef1f775522bf803c105c94831e6",
            ["native-anatomy-report"] = "0932b2d92927766f1ff7a29c0d29435421d44be75cc84878aee841ef948fad80",
            ["placement-report"] = "b591ccebabd465d4432fca494e80e3daa7af1f2736b61d9833993eb9fda29e31",
            ["report"] = "bf51ff728f80eedfee982640c9dde3b44b7222659c727de4ff7b40e88cea153b",
            ["source-audit"] = "e04e0d1a6848365ce6552e11194acb6ec6a35bac99dcd1f9c7a1c27b43653f51",
            ["surface-report"] = "91c931a489b41128d2ddf3dbf424e5e89e96f41eac2df985abe3b07391cabc19",
            ["walk-motion-profile"] = "8a595f6e879c02eb79e4bd36337d90fa14fb6897eceeed84debaeeb6b07c34d9",
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
                    Text(source, "schema") != $"horse.{a.Asset}_walk.candidate.v1" ||
                    Text(source, "candidateGlbSha256") != a.Glb || Text(source, "candidateBlendSha256") != a.Blend ||
                    !True(source, "protectedMeshDataExact") || source.GetProperty("reusedLightAtlas").GetBoolean() ||
                    source.GetProperty("bodyMaxEdgeStretch").GetDouble() > 2 ||
                    source.GetProperty("loopMaxVertexDeltaM").GetDouble() > 0.001 ||
                    Text(dense, "candidateBlendSha256") != a.Blend || !True(dense, "bodyStretchPass") ||
                    dense.GetProperty("sampleCount").GetInt32() != 581 ||
                    Text(surface, "candidateGlbSha256") != a.Glb || !Empty(surface, "failures")) return null;
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
