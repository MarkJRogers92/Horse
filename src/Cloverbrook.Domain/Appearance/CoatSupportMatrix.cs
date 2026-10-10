using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    public enum SupportLevel { None, Specified, Prototype, Approved, Released }
    public sealed class CoatClassSupport
    {
        public CoatClassSupport(string id, IEnumerable<string> layerIds, SupportLevel genetics, SupportLevel visuals, SupportLevel release)
        {
            Id = Check.Text(id, nameof(id)); LayerIds = Snapshot.List(layerIds);
            if (LayerIds.Count == 0 || LayerIds.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("A coat support row requires valid layer IDs.");
            foreach (var level in new[] { genetics, visuals, release }) if (!Enum.IsDefined(typeof(SupportLevel), level)) throw new ArgumentOutOfRangeException(nameof(release));
            if (release > genetics || release > visuals) throw new ArgumentException("Release status exceeds genetic or visual support.");
            Genetics = genetics; Visuals = visuals; Release = release;
        }
        public string Id { get; }
        public IReadOnlyList<string> LayerIds { get; }
        public SupportLevel Genetics { get; }
        public SupportLevel Visuals { get; }
        public SupportLevel Release { get; }
    }
    public sealed class CoatSupportMatrix
    {
        public CoatSupportMatrix(string version, IEnumerable<string> layers, IEnumerable<CoatClassSupport> classes)
        {
            Version = Check.Text(version, nameof(version)); LayerIds = Snapshot.List(layers);
            if (LayerIds.Any(string.IsNullOrWhiteSpace) || LayerIds.Distinct(StringComparer.Ordinal).Count() != LayerIds.Count) throw new ArgumentException("Duplicate or blank render layer.");
            Classes = Snapshot.Map(Check.NotNull(classes, nameof(classes)).Select(c => new KeyValuePair<string, CoatClassSupport>(c.Id, c)));
            if (Classes.Values.Any(c => c.LayerIds.Any(l => !LayerIds.Contains(l)))) throw new ArgumentException("A coat references an undefined layer.");
        }
        public string Version { get; }
        public IReadOnlyList<string> LayerIds { get; }
        public IReadOnlyDictionary<string, CoatClassSupport> Classes { get; }
        public IReadOnlyList<string> ReleasedSupportProblems(IEnumerable<string> requiredClasses, IEnumerable<string> requiredLayers)
        {
            var requested = Snapshot.List(requiredClasses); var layers = Snapshot.List(requiredLayers); var problems = new List<string>();
            if (requested.Count == 0) problems.Add("No coat class was declared.");
            var permittedLayers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in requested)
            {
                if (!Classes.TryGetValue(id, out var entry)) { problems.Add("No supported matrix class: " + id); continue; }
                if (entry.Release != SupportLevel.Released || entry.Genetics != SupportLevel.Released || entry.Visuals != SupportLevel.Released)
                    problems.Add("Coat class is not released in both tracks: " + id);
                foreach (var layer in entry.LayerIds) permittedLayers.Add(layer);
            }
            // Common artistic realization layers are part of each horse's art approval.
            foreach (var common in new[] { "individual_white_markings", "shading_modifiers" }) if (LayerIds.Contains(common)) permittedLayers.Add(common);
            foreach (var layer in layers)
                if (!LayerIds.Contains(layer) || !permittedLayers.Contains(layer)) problems.Add("Missing released layer coverage: " + layer);
            return Snapshot.List(problems.Distinct(StringComparer.Ordinal));
        }
    }
    public sealed class CrossSupportAudit
    {
        internal CrossSupportAudit(bool evaluatedAll, int outcomeCount, IEnumerable<string> reasons)
        { EvaluatedAll = evaluatedAll; OutcomeCount = outcomeCount; Reasons = Snapshot.List(reasons); }
        public bool EvaluatedAll { get; }
        public int OutcomeCount { get; }
        public IReadOnlyList<string> Reasons { get; }
        public bool IsSupported => EvaluatedAll && OutcomeCount > 0 && Reasons.Count == 0;
    }
    public static class LiveCoatGuard
    {
        // Conservative pre-draw check of a concrete pairing, not a complete
        // multi-generation live-founder closure proof (P1-07).
        // Never sample first and condition on the drawable outcome.
        public static CrossSupportAudit CheckPair(HorseRecord a, HorseRecord b, CoatSupportMatrix matrix, int maxOutcomes = 65536)
        {
            Check.NotNull(a, nameof(a)); Check.NotNull(b, nameof(b)); Check.NotNull(matrix, nameof(matrix));
            var resolver = new PhenotypeResolver(); var reasons = new List<string>();
            foreach (var parent in new[] { a, b })
            {
                var parentResult = resolver.ResolveGenetics(parent);
                if (parentResult.Status == ResolutionStatus.Unsupported) reasons.Add(parentResult.UnsupportedReason!);
                else reasons.AddRange(matrix.ReleasedSupportProblems(parentResult.RequiredCoatClasses, parentResult.Phenotype!.CoatLayers));
            }
            try
            {
                var outcomes = new MeiosisService(CoreGeneticsCatalog.Model).EnumerateOffspring(a, b, maxOutcomes);
                foreach (var outcome in outcomes)
                {
                    var child = new HorseRecord(new HorseId(new Guid("eeeeeeee-0000-0000-0000-000000000001")), "Reachability analysis only", new Parentage(),
                        outcome.Value, CoreGeneticsCatalog.ResolverVersion, default, a.OwnerId, new LifeState(LifeStage.Adult, 12, VisualAge.Adult), new ReproductionState(ReproductiveRole.NonBreeding));
                    var resolved = resolver.ResolveGenetics(child);
                    if (resolved.Status == ResolutionStatus.Unsupported) reasons.Add(resolved.UnsupportedReason!);
                    else reasons.AddRange(matrix.ReleasedSupportProblems(resolved.RequiredCoatClasses, resolved.Phenotype!.CoatLayers));
                }
                return new CrossSupportAudit(true, outcomes.Count, reasons.Distinct(StringComparer.Ordinal));
            }
            catch (ExactEnumerationLimitException exception) { reasons.Add(exception.Message); }
            catch (GeneticsSupportException exception) { reasons.Add(exception.Message); }
            return new CrossSupportAudit(false, 0, reasons.Distinct(StringComparer.Ordinal));
        }
    }
}
