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
        "a37ff181d158517c6297c97655d8c2218ff2810a2fe48694510901a4ee82dbd8",
        "80d28ab85ab32bb658a0b2f8b6e3adf7776ebc939a99378cc9e9772b0dcc03c6", 0.3448275862068966, 176,
        new Dictionary<string, string> {
            ["native-anatomy-report"] = "1093328adda212cec83e3aef7c6c3c1851506a67b6619f9482e74017ff14a233",
            ["anatomy-report"] = "a6f9317749fb4a71c0c5d7276c8260efd208b8b5fd9480c341854d73ccc4870a",
            ["attachment-audit"] = "943da9d0646eccb7b9b07b7385e16a55f6a6abc5eacd531142d407f14beb6968",
            ["dense-deformation-audit"] = "8c0924190eab1ef944e16d6ac8db0455e7fb3ffdc6c423480d3454a0633d43a1",
            ["game-import-report"] = "4d76f93e8726e50dad225bb1e8c3f5086493cb6e4927445499111815f6be0d82",
            ["report"] = "8e997ae0c001d13993b8565329c41c9c11a5ab754ac516f94cca57e77b02561e",
            ["source-audit"] = "c9bf85510924b8748ca99e2a1cbb9b8df78a8c7ccb1468f0cce07f14431e5d38",
            ["surface-report"] = "5e3f969da8cb8cd2150888f400676dcc5cded4eb391f00f0fc7f16595b08ea14",
            ["walk-motion-profile"] = "7dfc563da3d61ebaa6d88ed565fe163e59a65e5b5e64f4eef37885917b035699",
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
