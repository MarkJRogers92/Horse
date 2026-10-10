using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cloverbrook.Domain;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// Engine-side, read-only copy of the domain's <see cref="HorseVisualDescriptor"/>.
/// It can be built from a live domain object or from the exported fixture JSON.
/// The renderer only ever sees this recipe - never a genome.
/// </summary>
public sealed record VisualDescriptor(
    string FixtureId,
    string DisplayLabel,
    string HorseId,
    string ResolverVersion,
    string CoatClassId,
    IReadOnlyList<string> RequiredCoatClasses,
    string VisualCapabilityVersion,
    string VisualAssetCatalogVersion,
    string MeshFamilyId,
    string AnatomicalMaskSetId,
    IReadOnlyDictionary<string, int> MorphWeights,
    IReadOnlyList<string> CoatLayers,
    string CoatRecipe,
    string SkinRecipe,
    string EyeRecipe,
    string HairRecipe,
    int GrayAmount,
    int PrimitiveMarkingStrength,
    bool LpPatternPresent,
    int Patn1Coverage,
    string MarkingRealizationKey,
    string? CosmeticLayer,
    string CacheKey,
    bool TestOnly)
{
    public bool IsFoal => MeshFamilyId.Contains(".foal.", StringComparison.Ordinal);

    /// <summary>Copy a live domain descriptor (the integration path once horses come from saves).</summary>
    public static VisualDescriptor FromDomain(HorseVisualDescriptor d, string displayLabel, bool testOnly) => new(
        d.HorseId.ToString(), displayLabel, d.HorseId.ToString(), d.ResolverVersion, d.CoatClassId, d.RequiredCoatClasses.ToArray(),
        d.VisualCapabilityVersion, d.VisualAssetCatalogVersion, d.MeshFamilyId, d.AnatomicalMaskSetId,
        new Dictionary<string, int>(d.MorphWeights), d.CoatLayers.ToArray(), d.CoatRecipe, d.SkinRecipe, d.EyeRecipe, d.HairRecipe,
        d.GrayAmount, d.PrimitiveMarkingStrength, d.LpPatternPresent, d.Patn1Coverage, d.MarkingRealizationKey, d.CosmeticLayer,
        d.CacheKey, testOnly);

    /// <summary>Load the descriptor fixture file exported by the domain test suite.</summary>
    public static List<VisualDescriptor> LoadFixtureFile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        bool testOnly = root.TryGetProperty("notForLiveStock", out var nfl) && nfl.GetBoolean();
        var list = new List<VisualDescriptor>();
        foreach (var f in root.GetProperty("fixtures").EnumerateArray())
        {
            var d = f.GetProperty("descriptor");
            string S(string n) => d.GetProperty(n).GetString() ?? "";
            int I(string n) => d.GetProperty(n).GetInt32();
            list.Add(new VisualDescriptor(
                f.GetProperty("fixtureId").GetString()!, f.GetProperty("displayLabel").GetString()!,
                S("horseId"), S("resolverVersion"), S("coatClassId"),
                d.GetProperty("requiredCoatClasses").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                S("visualCapabilityVersion"), S("visualAssetCatalogVersion"), S("meshFamilyId"), S("anatomicalMaskSetId"),
                d.GetProperty("morphWeights").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32()),
                d.GetProperty("coatLayers").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                S("coatRecipe"), S("skinRecipe"), S("eyeRecipe"), S("hairRecipe"),
                I("grayAmount"), I("primitiveMarkingStrength"), d.GetProperty("lpPatternPresent").GetBoolean(), I("patn1Coverage"),
                S("markingRealizationKey"),
                d.TryGetProperty("cosmeticLayer", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
                S("cacheKey"), testOnly));
        }
        return list;
    }

    /// <summary>
    /// TEST-ONLY variant with a different marking key, used to show that markings are
    /// deterministic per key. Clearly labelled; never written to a save.
    /// </summary>
    public VisualDescriptor WithTestMarkingKey(string key, string label) =>
        this with { FixtureId = FixtureId + ".markings-" + key[..6], DisplayLabel = label, MarkingRealizationKey = key, TestOnly = true };
}
