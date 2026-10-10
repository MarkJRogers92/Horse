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
        internal KinshipResult(Rational? coefficient, string pedigreeHash)
        {
            Coefficient = coefficient; PedigreeHash = pedigreeHash;
            Qualification = coefficient.HasValue
                ? "Exact pedigree kinship, conditional on the declared founder backgrounds; not a DNA measurement."
                : "Incomplete ancestry or undeclared founder relationships. Zero relatedness is not established.";
        }
        // phi(parentA,parentB) equals the proposed child's F, not twice phi.
        public Rational? Coefficient { get; }
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
        public PedigreeGraph(IEnumerable<PedigreeNode> nodes)
        {
            var input = Snapshot.List(nodes);
            var map = new Dictionary<HorseId, PedigreeNode>();
            foreach (var node in input) if (!map.TryAdd(node.HorseId, node)) throw new ArgumentException("Duplicate horse identity in pedigree.", nameof(nodes));
            var children = map.Keys.ToDictionary(id => id, _ => new List<HorseId>());
            var degree = map.Keys.ToDictionary(id => id, _ => 0);
            foreach (var node in input)
                foreach (var parent in new[] { node.Parentage.ParentAId, node.Parentage.ParentBId })
                    if (parent != null && map.ContainsKey(parent)) { children[parent].Add(node.HorseId); degree[node.HorseId]++; }
            var queue = new Queue<HorseId>(input.Where(n => degree[n.HorseId] == 0).Select(n => n.HorseId));
            var result = new List<PedigreeNode>();
            while (queue.Count != 0)
            {
                var id = queue.Dequeue(); result.Add(map[id]);
                foreach (var child in children[id]) if (--degree[child] == 0) queue.Enqueue(child);
            }
            if (result.Count != input.Count) throw new ArgumentException("A horse appears among its own ancestors (pedigree cycle).", nameof(nodes));
            ordered = Snapshot.List(result); indices = ordered.Select((n, i) => (n.HorseId, Index: i)).ToDictionary(p => p.HorseId, p => p.Index);
            PedigreeHash = Canonical.Hash(new[] { "pedigree.v1" }.Concat(input.OrderBy(n => n.HorseId.ToString(), StringComparer.Ordinal).SelectMany(n => new[] {
                n.HorseId.ToString(), n.Parentage.ParentAId?.ToString() ?? "unknown", n.Parentage.ParentBId?.ToString() ?? "unknown",
                n.Parentage.FounderBackground?.AssumptionId ?? "undeclared", n.Parentage.FounderBackground?.InbreedingCoefficient.ToString() ?? "unknown" })));
        }
        public string PedigreeHash { get; }
        public int Count => ordered.Count;
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
        private bool KnownParents(PedigreeNode node, out int a, out int b)
        {
            a = b = -1;
            return node.Parentage.ParentAId != null && node.Parentage.ParentBId != null
                && indices.TryGetValue(node.Parentage.ParentAId, out a) && indices.TryGetValue(node.Parentage.ParentBId, out b);
        }
        public KinshipResult Kinship(HorseId first, HorseId second, int maxComputedPairs = 1000000)
        {
            Check.NotNull(first, nameof(first)); Check.NotNull(second, nameof(second));
            if (maxComputedPairs < 1) throw new ArgumentOutOfRangeException(nameof(maxComputedPairs));
            if (!indices.TryGetValue(first, out var firstIndex) || !indices.TryGetValue(second, out var secondIndex)) return new KinshipResult(null, PedigreeHash);
            var target = Key(firstIndex, secondIndex);
            var memo = new Dictionary<(int A, int B), Rational?>(); var pending = new Stack<(int A, int B)>(); pending.Push(target);
            while (pending.Count != 0)
            {
                var key = pending.Peek();
                if (memo.ContainsKey(key)) { pending.Pop(); continue; }
                if (memo.Count >= maxComputedPairs) throw new InvalidOperationException("Pedigree calculation budget exceeded; no partial coefficient is returned.");
                var a = ordered[key.A]; var b = ordered[key.B];
                if (key.A == key.B && a.IsRoot)
                {
                    memo[key] = a.Parentage.FounderBackground == null ? (Rational?)null : (Rational.One + a.Parentage.FounderBackground.InbreedingCoefficient) * Rational.Half;
                    continue;
                }
                if (key.A != key.B && a.IsRoot && b.IsRoot)
                {
                    var fa = a.Parentage.FounderBackground; var fb = b.Parentage.FounderBackground;
                    memo[key] = fa != null && fb != null && fa.AssumptionId == fb.AssumptionId ? Rational.Zero : (Rational?)null;
                    continue;
                }
                var expand = !a.IsRoot ? a : b; var otherIndex = !a.IsRoot ? key.B : key.A;
                if (!KnownParents(expand, out var pa, out var pb)) { memo[key] = null; continue; }
                var left = key.A == key.B ? Key(pa, pb) : Key(pa, otherIndex);
                var right = key.A == key.B ? left : Key(pb, otherIndex);
                if (!memo.ContainsKey(left)) { pending.Push(left); continue; }
                if (!memo.ContainsKey(right)) { pending.Push(right); continue; }
                var x = memo[left]; var y = memo[right];
                memo[key] = !x.HasValue || !y.HasValue ? (Rational?)null : key.A == key.B
                    ? (Rational.One + x.Value) * Rational.Half : (x.Value + y.Value) * Rational.Half;
            }
            return new KinshipResult(memo[target], PedigreeHash);
        }
        public KinshipResult ProposedOffspringInbreeding(HorseId parentA, HorseId parentB) => Kinship(parentA, parentB);
    }
}
