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
        "3850db293a2b50d084fc9a4c2359c751e71f5456ba01219bf2dd1449dbda2e9e",
        "4efbbb61a865c08367d7bcdba8b7c756cf2100a8ad1ec90125e1641a7bc1d5b0", 0.749997329711914, 429,
        new Dictionary<string, string> { 
            ["surface-report"] = "a1537671ae465410a83a56dd91fa530f6051a2f3a3aaa7f2d9fb58b46e916992",
            ["report"] = "0b88e2a1b5aab88c162be1a3a2ff50801163b09cd7fcd01ecd35f87e3a1cb985",
            ["dense-deformation-audit"] = "3cc96700d39790cbde3375518a7312de0c250228f018f6a992e92b29f244054a",
            ["source-audit"] = "f9c67068d6204abb0d97c87cc3c1581fbe52a453fc59dfe596bb0c6949db2993",
            ["walk-motion-profile"] = "337be1230a74561a667e925f051c7a4c03bcae800ff5308816804dfd22ef25bf",
            ["game-import-report"] = "956fe08e5f776a35616ba6a5131d8e3a8151ec79fe5bf2cad6579ee6ebbec5b1",
         });
    private static readonly Accepted Foal = new("foal", "foal",
        "219061fb162cfb227a9c0a6e025e12c83c8af6e834ba8517e3754ac8e3dd0769",
        "5f94f6d90946d6a899c57239861ec79ef17f27767dd318e039938f5ed70cc96d", 0.3448275862068966, 176,
        new Dictionary<string, string> { 
            ["surface-report"] = "6050eab92558e1cd54fdb1618509d4ce0d259ba71e0f3b2a15d19de3535d6237",
            ["report"] = "4d59b1e33f29943e68984a65d8fbc77163439d3ef7a2f06735943d357cd043ec",
            ["attachment-audit"] = "7711757453e03a1d84e7cc372f060531330abdd53d053c83b4c523116c0a0816",
            ["dense-deformation-audit"] = "d2bcb33a13664f3c51a808e10d690b38c1416447edc535ef1c6617ec23ef0bc9",
            ["source-audit"] = "6e4b23cc2c1218ad1164efd2326361fa75e7253dcde938b8eacd010b546b7f61",
            ["walk-motion-profile"] = "f525314057884aba7d8dd8f5836f7bbb083825227ce551dc12f4c3248fd6f762",
            ["game-import-report"] = "753d4bb08188910751e2546b62a23d7349236115b5a315b09042b478423432d2",
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
        string root = $"res://assets/horse/production/evidence/{a.Asset}-walk-polish/";
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
