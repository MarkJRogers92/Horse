using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class PedigreeNode
    {
        public PedigreeNode(HorseId horseId, Parentage parentage)
        {
            HorseId = Check.NotNull(horseId, nameof(horseId)); Parentage = Check.NotNull(parentage, nameof(parentage));
            if (horseId.Equals(parentage.ParentAId) || horseId.Equals(parentage.ParentBId)) throw new ArgumentException("A horse cannot be its own parent.");
        }
        public HorseId HorseId { get; }
        public Parentage Parentage { get; }
        internal bool IsRoot => Parentage.ParentAId == null && Parentage.ParentBId == null;
    }

    public sealed class KinshipResult
    {
        internal KinshipResult(Rational? coefficient, Rational minimumCoefficient, string pedigreeHash)
        {
            Coefficient = coefficient; MinimumCoefficient = minimumCoefficient; PedigreeHash = pedigreeHash;
            Qualification = coefficient.HasValue
                ? "Exact pedigree kinship, conditional on the declared founder backgrounds; not a DNA measurement."
                : "Incomplete ancestry or undeclared founder relationships. The minimum proven kinship is "
                    + minimumCoefficient.ToString() + "; zero relatedness is not established.";
        }
        // phi(parentA,parentB) equals the proposed child's F, not twice phi.
        public Rational? Coefficient { get; }
        // Conservative lower bound from the recorded parent links. Unknown
        // cross-relationships contribute zero; known edges still contribute.
        public Rational MinimumCoefficient { get; }
        public bool IsComplete => Coefficient.HasValue;
        public string Qualification { get; }
        public string PedigreeHash { get; }
    }

    /// <summary>
    /// Complete recorded pedigree, independent of the UI generation limit.
    /// F(child)=phi(a,b); phi(i,i)=(1+F(i))/2;
    /// phi(child,j)=(phi(a,j)+phi(b,j))/2. Unknowns propagate as unknowns.
    /// Roots sharing an explicit FounderBackground.AssumptionId are declared
    /// mutually unrelated; root inbreeding can still be nonzero.
    /// Query-local memoization and an explicit stack avoid recursion depth and
    /// cross-thread cache mutation. Topological validation rejects every cycle.
    /// </summary>
    public sealed class PedigreeGraph
    {
        private readonly IReadOnlyList<PedigreeNode> ordered;
        private readonly Dictionary<HorseId, int> indices;
        private readonly int declaredNodeCount;
        public PedigreeGraph(IEnumerable<PedigreeNode> nodes)
        {
            var input = Snapshot.List(nodes);
            var map = new Dictionary<HorseId, PedigreeNode>();
            foreach (var node in input) if (!map.TryAdd(node.HorseId, node)) throw new ArgumentException("Duplicate horse identity in pedigree.", nameof(nodes));
            declaredNodeCount = input.Count;
            // A named but unavailable ancestor is still a shared identity. Model
            // it as an undeclared terminal so repeated references retain the
            // minimum relatedness proved by their common edge.
            foreach (var parentId in input.SelectMany(n => new[] { n.Parentage.ParentAId, n.Parentage.ParentBId }).Where(id => id != null).Distinct())
                if (!map.ContainsKey(parentId!)) map.Add(parentId!, new PedigreeNode(parentId!, new Parentage()));
            var allNodes = map.Values.ToArray();
            var children = map.Keys.ToDictionary(id => id, _ => new List<HorseId>());
            var degree = map.Keys.ToDictionary(id => id, _ => 0);
            foreach (var node in allNodes)
                foreach (var parent in new[] { node.Parentage.ParentAId, node.Parentage.ParentBId })
                    if (parent != null && map.ContainsKey(parent)) { children[parent].Add(node.HorseId); degree[node.HorseId]++; }
            var queue = new Queue<HorseId>(allNodes.Where(n => degree[n.HorseId] == 0).Select(n => n.HorseId));
            var result = new List<PedigreeNode>();
            while (queue.Count != 0)
            {
                var id = queue.Dequeue(); result.Add(map[id]);
                foreach (var child in children[id]) if (--degree[child] == 0) queue.Enqueue(child);
            }
            if (result.Count != map.Count) throw new ArgumentException("A horse appears among its own ancestors (pedigree cycle).", nameof(nodes));
            ordered = Snapshot.List(result); indices = ordered.Select((n, i) => (n.HorseId, Index: i)).ToDictionary(p => p.HorseId, p => p.Index);
            PedigreeHash = Canonical.Hash(new[] { "pedigree.v1" }.Concat(input.OrderBy(n => n.HorseId.ToString(), StringComparer.Ordinal).SelectMany(n => new[] {
                n.HorseId.ToString(), n.Parentage.ParentAId?.ToString() ?? "unknown", n.Parentage.ParentBId?.ToString() ?? "unknown",
                n.Parentage.FounderBackground?.AssumptionId ?? "undeclared", n.Parentage.FounderBackground?.InbreedingCoefficient.ToString() ?? "unknown" })));
        }
        public string PedigreeHash { get; }
        public int Count => declaredNodeCount;
        public bool MatchesParentage(HorseRecord horse)
        {
            Check.NotNull(horse, nameof(horse));
            if (!indices.TryGetValue(horse.HorseId, out var index)) return false;
            var a = ordered[index].Parentage; var b = horse.Parentage;
            var parentsMatch = (Equals(a.ParentAId, b.ParentAId) && Equals(a.ParentBId, b.ParentBId)) || (Equals(a.ParentAId, b.ParentBId) && Equals(a.ParentBId, b.ParentAId));
            var rootsMatch = a.FounderBackground == null ? b.FounderBackground == null : b.FounderBackground != null
                && a.FounderBackground.AssumptionId == b.FounderBackground.AssumptionId && a.FounderBackground.InbreedingCoefficient == b.FounderBackground.InbreedingCoefficient;
            return parentsMatch && rootsMatch;
        }
        private static (int A, int B) Key(int a, int b) => a >= b ? (a, b) : (b, a);
        private IReadOnlyList<int> KnownParents(PedigreeNode node) => new[] { node.Parentage.ParentAId, node.Parentage.ParentBId }
            .Where(id => id != null && indices.ContainsKey(id)).Select(id => indices[id!]).ToArray();

        private readonly struct KinshipValue
        {
            public KinshipValue(Rational? exact, Rational minimum) { Exact = exact; Minimum = minimum; }
            public Rational? Exact { get; }
            public Rational Minimum { get; }
        }
        public KinshipResult Kinship(HorseId first, HorseId second, int maxComputedPairs = 1000000)
        {
            Check.NotNull(first, nameof(first)); Check.NotNull(second, nameof(second));
            if (maxComputedPairs < 1) throw new ArgumentOutOfRangeException(nameof(maxComputedPairs));
            if (!indices.TryGetValue(first, out var firstIndex) || !indices.TryGetValue(second, out var secondIndex))
                return new KinshipResult(null, Rational.Zero, PedigreeHash);
            var target = Key(firstIndex, secondIndex);
            var memo = new Dictionary<(int A, int B), KinshipValue>(); var pending = new Stack<(int A, int B)>(); pending.Push(target);
            while (pending.Count != 0)
            {
                var key = pending.Peek();
                if (memo.ContainsKey(key)) { pending.Pop(); continue; }
                if (memo.Count >= maxComputedPairs) throw new InvalidOperationException("Pedigree calculation budget exceeded; no partial coefficient is returned.");
                var a = ordered[key.A]; var b = ordered[key.B];
                if (key.A == key.B && a.IsRoot)
                {
                    var exact = a.Parentage.FounderBackground == null ? (Rational?)null
                        : (Rational.One + a.Parentage.FounderBackground.InbreedingCoefficient) * Rational.Half;
                    memo[key] = new KinshipValue(exact, exact ?? Rational.Half);
                    continue;
                }
                if (key.A != key.B && a.IsRoot && b.IsRoot)
                {
                    var fa = a.Parentage.FounderBackground; var fb = b.Parentage.FounderBackground;
                    var exact = fa != null && fb != null && fa.AssumptionId == fb.AssumptionId ? (Rational?)Rational.Zero : null;
                    memo[key] = new KinshipValue(exact, Rational.Zero);
                    continue;
                }
                if (key.A == key.B)
                {
                    var parents = KnownParents(a);
                    if (parents.Count != 2) { memo[key] = new KinshipValue(null, Rational.Half); continue; }
                    var parentPair = Key(parents[0], parents[1]);
                    if (!memo.ContainsKey(parentPair)) { pending.Push(parentPair); continue; }
                    var parental = memo[parentPair];
                    memo[key] = new KinshipValue(parental.Exact.HasValue ? (Rational.One + parental.Exact.Value) * Rational.Half : (Rational?)null,
                        (Rational.One + parental.Minimum) * Rational.Half);
                    continue;
                }
                var expand = !a.IsRoot ? a : b; var otherIndex = !a.IsRoot ? key.B : key.A;
                var knownParents = KnownParents(expand);
                if (knownParents.Count == 0) { memo[key] = new KinshipValue(null, Rational.Zero); continue; }
                var dependencies = knownParents.Select(parent => Key(parent, otherIndex)).ToArray();
                var missing = dependencies.FirstOrDefault(dependency => !memo.ContainsKey(dependency));
                if (dependencies.Any(dependency => !memo.ContainsKey(dependency))) { pending.Push(missing); continue; }
                var values = dependencies.Select(dependency => memo[dependency]).ToArray();
                var exactPair = knownParents.Count == 2 && values.All(value => value.Exact.HasValue);
                Rational exactSum = Rational.Zero; Rational minimumSum = Rational.Zero;
                foreach (var value in values)
                {
                    if (value.Exact.HasValue) exactSum += value.Exact.Value;
                    minimumSum += value.Minimum;
                }
                memo[key] = new KinshipValue(exactPair ? exactSum * Rational.Half : (Rational?)null,
                    minimumSum * Rational.Half);
            }
            var result = memo[target]; return new KinshipResult(result.Exact, result.Minimum, PedigreeHash);
        }
        public KinshipResult ProposedOffspringInbreeding(HorseId parentA, HorseId parentB) => Kinship(parentA, parentB);
    }
}
