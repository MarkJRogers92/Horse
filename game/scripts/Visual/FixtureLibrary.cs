using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// Reference descriptors for the pilot: the 26 domain-exported phase-1 fixtures plus
/// clearly labelled TEST-ONLY extras (marking variety, deliberately unsupported cases).
/// </summary>
public static class FixtureLibrary
{
    public const string FixturePath = "res://data/fixtures/visual-descriptor-fixtures.json";

    private static List<VisualDescriptor>? _phase1;
    public static IReadOnlyList<VisualDescriptor> Phase1 => _phase1 ??= VisualDescriptor.LoadFixtureFile(FileAccess.GetFileAsString(FixturePath));

    public static IEnumerable<string> ClassIds => Phase1.Select(d => d.FixtureId.Split('.')[0]).Distinct();

    public static VisualDescriptor Get(string classId, bool foal) =>
        Phase1.First(d => d.FixtureId == classId + (foal ? ".foal" : ".adult"));

    /// <summary>Deterministic marking keys for the marking-variety sheet (test-only).</summary>
    public static IEnumerable<VisualDescriptor> MarkingVariety(string classId, bool foal, int count)
    {
        var baseD = Get(classId, foal);
        for (int i = 0; i < count; i++)
        {
            var key = Hex(SHA256.HashData(Encoding.UTF8.GetBytes("cloverbrook.test-markings." + i)));
            yield return baseD.WithTestMarkingKey(key, $"{baseD.DisplayLabel} - test markings #{i}");
        }
    }

    /// <summary>Descriptors the renderer must refuse. Proves "never silently substitute".</summary>
    public static IEnumerable<VisualDescriptor> UnsupportedProbes()
    {
        var bay = Get("bay", false);
        yield return bay with { FixtureId = "probe.grey", DisplayLabel = "Bay (progressive grey) - probe", GrayAmount = 5000,
            CoatLayers = bay.CoatLayers.Append("grey_age").ToArray(), TestOnly = true };
        yield return bay with { FixtureId = "probe.unknown-recipe", DisplayLabel = "Unknown pigment recipe - probe",
            CoatRecipe = "eumelanin_points_pheomelanin_body:champagne", TestOnly = true };
        yield return bay with { FixtureId = "probe.unknown-mesh", DisplayLabel = "Draft body family - probe",
            MeshFamilyId = "horse.draft.adult.v1", TestOnly = true };
    }

    private static string Hex(byte[] b) => string.Concat(b.Select(x => x.ToString("x2")));
}
