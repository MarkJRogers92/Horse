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
