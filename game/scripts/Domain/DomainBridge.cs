using Cloverbrook.Domain;
using Godot;

namespace Cloverbrook.Game.DomainIntegration;

/// <summary>
/// Compile-time and runtime proof that the game links the ONE shared domain library.
/// Later integration (saved horses -> descriptors) goes through here; the game never
/// re-implements genetics.
/// </summary>
public static class DomainBridge
{
    public static string ResolverVersion => new PhenotypeResolver().Version;

    private static CoatSupportMatrix? _matrix;

    /// <summary>The authoritative coat release record (game copy of docs/contracts/COAT_SUPPORT_MATRIX.json, D-013).</summary>
    public static CoatSupportMatrix Matrix => _matrix ??= ParseMatrix(FileAccess.GetFileAsString("res://data/coat_support_matrix.json"));

    public static CoatSupportMatrix ParseMatrix(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        SupportLevel Level(System.Text.Json.JsonElement row, string name) => System.Enum.Parse<SupportLevel>(row.GetProperty(name).GetString()!, true);
        var classes = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(root.GetProperty("coatClasses").EnumerateArray(), row =>
            new CoatClassSupport(row.GetProperty("id").GetString()!,
                System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(row.GetProperty("layers").EnumerateArray(), v => v.GetString()!)),
                Level(row, "geneticStatus"), Level(row, "visualStatus"), Level(row, "releaseStatus"))));
        var layers = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(root.GetProperty("layers").EnumerateArray(), v => v.GetProperty("id").GetString()!));
        return new CoatSupportMatrix(root.GetProperty("formatVersion").GetString()!, layers, classes);
    }

    /// <summary>Convert a live domain descriptor for the presenter.</summary>
    public static Visual.VisualDescriptor ToVisual(HorseVisualDescriptor descriptor, string displayLabel) =>
        Visual.VisualDescriptor.FromDomain(descriptor, displayLabel, testOnly: false);

    /// <summary>
    /// Adapter for StableBreedingQueries.ResolveVisualDescriptor (see docs/genetics/godot_domain_api.md).
    /// Returns the descriptor for the presenter, or the domain's explicit refusal reasons.
    /// </summary>
    public static (Visual.VisualDescriptor? Descriptor, string[] Reasons) FromPresentation(PresentationResolution resolution)
    {
        if (resolution.Status != ResolutionStatus.Supported || resolution.Descriptor == null)
            return (null, System.Linq.Enumerable.ToArray(resolution.Reasons));
        return (ToVisual(resolution.Descriptor, resolution.DisplayLabel ?? resolution.Descriptor.CoatClassId), System.Array.Empty<string>());
    }

    public static void LogVersions() =>
        GD.Print($"[DomainBridge] domain resolver {ResolverVersion}; game links Cloverbrook.Domain");
}
