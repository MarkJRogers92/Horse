using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>Player choices for today's optional comfort care. Feeding and bedding are mandatory for active-stable horses.</summary>
    public sealed class HorseCareAssignment
    {
        public HorseCareAssignment(HorseId horseId, bool feed, bool bedding, bool turnout, bool grooming)
        { HorseId = Check.NotNull(horseId, nameof(horseId)); Feed = feed; Bedding = bedding; Turnout = turnout; Grooming = grooming; }
        public HorseId HorseId { get; }
        public bool Feed { get; }
        public bool Bedding { get; }
        public bool Turnout { get; }
        public bool Grooming { get; }
    }

    /// <summary>An immutable set of explicit care choices keyed by stable horse identity.</summary>
    public sealed class CarePlan
    {
        public CarePlan(IEnumerable<HorseCareAssignment> assignments)
        {
            if (assignments == null) throw new ArgumentNullException(nameof(assignments));
            var values = assignments.ToArray();
            if (values.Any(a => a == null)) throw new ArgumentException("Care assignments cannot be null.", nameof(assignments));
            Assignments = Snapshot.List(values.OrderBy(a => a.HorseId.ToString(), StringComparer.Ordinal));
            if (Assignments.Select(a => a.HorseId).Distinct().Count() != Assignments.Count)
                throw new ArgumentException("A care plan can assign each horse only once.", nameof(assignments));
            RequestHash = Canonical.Hash(new[] { "stable.care-plan.v1" }.Concat(Assignments.SelectMany(a => new[]
            { a.HorseId.ToString(), a.Feed ? "1" : "0", a.Bedding ? "1" : "0", a.Turnout ? "1" : "0", a.Grooming ? "1" : "0" })));
        }
        public IReadOnlyList<HorseCareAssignment> Assignments { get; }
        public string RequestHash { get; }
        public HorseCareAssignment? For(HorseId horseId) => Assignments.SingleOrDefault(a => a.HorseId.Equals(horseId));
    }
}
