using System;
using System.Linq;
using System.Text.Json;

namespace Cloverbrook.Game.Visual;

/// <summary>Additional anatomical gates; physical, surface and contact gates remain independent.</summary>
internal static class WalkAnatomyEvidence
{
    public static bool Validate(JsonElement report, string glb, string profile)
    {
        if (report.GetProperty("schema").GetString() != "horse.walk.anatomy.v1" ||
            report.GetProperty("sourceGlbSha256").GetString() != glb ||
            report.GetProperty("motionProfileSha256").GetString() != profile ||
            report.GetProperty("sampleCount").GetInt32() != 117 ||
            report.GetProperty("failures").GetArrayLength() != 0) return false;
        var limbs = report.GetProperty("limbs");
        if (!limbs.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "fore_l", "fore_r" })) return false;
        foreach (var limb in limbs.EnumerateObject())
        {
            bool Within(string key, double min, double max)
            {
                double value = limb.Value.GetProperty(key).GetDouble();
                return double.IsFinite(value) && value >= min && value <= max;
            }
            if (!Within("minSignedCarpusOffsetM", -0.002, 0.5) ||
                !Within("maxStanceFlexionDegrees", 0, 12) ||
                !Within("maxSwingFlexionDegrees", 20, 100) ||
                !Within("hoofPitchRangeDegrees", 20, 80) ||
                !Within("stanceHoofPitchRangeDegrees", 0, 0.5)) return false;
        }
        return true;
    }
}
