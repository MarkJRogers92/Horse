using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class EligibilityTests
    {
        private static readonly BreedingCommitId Event = new BreedingCommitId(Guid.Parse("20000000-0000-0000-0000-000000000001"));
        private static readonly BreedingPolicy Policy = new BreedingPolicy("test.game-policy.v1", new Rational(1, 8));
        private static Parentage Root() => new Parentage(founderBackground: new FounderBackground("declared", Rational.Zero));
        private static HorseRecord Mare(int days = 12, ReproductionState? state = null, HomeKind home = HomeKind.ActiveStable, Parentage? ancestry = null) =>
            FrozenFixtureData.Horse(FrozenFixtureData.Genome(), 1, parentage: ancestry ?? Root(), days: days, reproduction: state, home: home);
        private static HorseRecord Stallion() => FrozenFixtureData.Horse(FrozenFixtureData.Genome(), 2, ReproductiveRole.Stallion, Root());
        private static ReservedCapacityWindow Window(CapacityStage stage, int start, int? end, int occupied = 0, bool confirmed = true) => new ReservedCapacityWindow(stage, start, end, 1, occupied, 0, confirmed);
        private static BreedingCapacityPlan Capacity(IEnumerable<ReservedCapacityWindow>? rows = null) => new BreedingCapacityPlan(Event,
            new CapacityReservationId(Guid.Parse("30000000-0000-0000-0000-000000000001")), 7,
            rows ?? new[] { Window(CapacityStage.Pregnancy, 20, 24), Window(CapacityStage.Foal, 24, 27), Window(CapacityStage.Youngster, 27, 30), Window(CapacityStage.Adult, 30, null) });
        private static BreedingEligibilityResult Evaluate(HorseRecord mare, HorseRecord? stallion = null, BreedingCapacityPlan? plan = null, BreedingPolicy? policy = null)
        {
            stallion ??= Stallion(); var graph = new PedigreeGraph(new[] { new PedigreeNode(mare.HorseId, mare.Parentage), new PedigreeNode(stallion.HorseId, stallion.Parentage) });
            return new BreedingEligibility().Evaluate(mare, stallion, graph, policy ?? Policy, 20, Event, plan ?? Capacity(), 0, 0, 7);
        }
        [Fact]
        public void ValidPairWithCompleteFutureReservationsIsEligible()
        {
            var result = Evaluate(Mare()); Assert.True(result.IsEligible); Assert.Empty(result.Rejections); Assert.Empty(result.Warnings); Assert.Equal(Rational.Zero, result.Relatedness.Coefficient);
        }
        [Fact]
        public void AgeReproductiveStateHomeAndRevisionsGiveReadableReasons()
        {
            Assert.Contains(Evaluate(Mare(days: 11)).Rejections, r => r.Code == "age");
            Assert.Contains(Evaluate(Mare(state: new ReproductionState(ReproductiveRole.Mare, Event))).Rejections, r => r.Code == "pregnant");
            Assert.Contains(Evaluate(Mare(state: new ReproductionState(ReproductiveRole.Mare, recoveryUntilDay: 21))).Rejections, r => r.Code == "recovery");
            Assert.Contains(Evaluate(Mare(home: HomeKind.CooperativePasture)).Rejections, r => r.Code == "home");
            Assert.Contains(Evaluate(Mare(home: HomeKind.InstitutionalLease)).Rejections, r => r.Code == "home");
            Assert.Contains(Evaluate(Mare(state: new ReproductionState(ReproductiveRole.NonBreeding))).Rejections, r => r.Code == "reproductive_roles");
            Assert.Contains(Evaluate(Mare().Rename("New name")).Rejections, r => r.Code == "stale_parent");
            Assert.True(Evaluate(Mare(state: new ReproductionState(ReproductiveRole.Mare, recoveryUntilDay: 20))).IsEligible);
        }
        [Fact]
        public void UnknownAncestryCannotBeAdvertisedAsZeroEvenUnderAnAllowPolicy()
        {
            var mare = Mare(ancestry: new Parentage()); Assert.Contains(Evaluate(mare).Rejections, r => r.Code == "unknown_ancestry");
            var result = Evaluate(mare, policy: new BreedingPolicy("explicit-allow.v1", new Rational(1, 8), true));
            Assert.True(result.IsEligible); Assert.Null(result.Relatedness.Coefficient); Assert.Contains(result.Warnings, r => r.Code == "unknown_ancestry");
        }
        [Fact]
        public void MissingReservationStaleCapacityAndWrongEventAreRejected()
        {
            var mare = Mare(); var sire = Stallion(); var graph = new PedigreeGraph(new[] { new PedigreeNode(mare.HorseId, mare.Parentage), new PedigreeNode(sire.HorseId, sire.Parentage) });
            var check = new BreedingEligibility();
            Assert.Contains(check.Evaluate(mare, sire, graph, Policy, 20, Event, null, 0, 0, 7).Rejections, r => r.Code == "capacity_missing");
            Assert.Contains(check.Evaluate(mare, sire, graph, Policy, 20, Event, Capacity(), 0, 0, 8).Rejections, r => r.Code == "stale_capacity");
            Assert.Contains(check.Evaluate(mare, sire, graph, Policy, 20, new BreedingCommitId(Guid.NewGuid()), Capacity(), 0, 0, 7).Rejections, r => r.Code == "capacity_event");
        }
        [Fact]
        public void AdultGrowthAndLateLeaseReturnsCannotOverbookHousing()
        {
            var rows = Capacity().Windows.Where(w => w.Stage != CapacityStage.Adult).ToList();
            rows.Add(Window(CapacityStage.Adult, 30, 40)); rows.Add(Window(CapacityStage.Adult, 40, null, occupied: 1));
            Assert.Contains(Evaluate(Mare(), plan: Capacity(rows)).Rejections, r => r.Code == "capacity.adult");
            rows.RemoveAt(rows.Count - 1); rows.Add(Window(CapacityStage.Adult, 41, null)); // unaccounted day 40
            Assert.Contains(Evaluate(Mare(), plan: Capacity(rows)).Rejections, r => r.Code == "capacity.adult");
            rows.RemoveAt(rows.Count - 1); rows.Add(Window(CapacityStage.Adult, 40, null)); Assert.True(Evaluate(Mare(), plan: Capacity(rows)).IsEligible);
        }
        [Fact]
        public void UnconfirmedFoalingHoldAndOverlappingWindowsFail()
        {
            var rows = Capacity().Windows.Select(w => w.Stage == CapacityStage.Pregnancy ? Window(CapacityStage.Pregnancy, 20, 24, confirmed: false) : w).ToArray();
            Assert.Contains(Evaluate(Mare(), plan: Capacity(rows)).Rejections, r => r.Code == "capacity.pregnancy");
            var duplicate = Capacity().Windows.Concat(new[] { Window(CapacityStage.Foal, 24, 27) });
            Assert.Contains(Evaluate(Mare(), plan: Capacity(duplicate)).Rejections, r => r.Code == "capacity.foal");
        }
        [Fact]
        public void RelationshipPolicyIsSeparateFromMathematicalEvaluability()
        {
            var mare = Mare(); var other = new PedigreeNode(RecordContractTests.Id(3), Root());
            var sire = FrozenFixtureData.Horse(FrozenFixtureData.Genome(), 2, ReproductiveRole.Stallion, new Parentage(mare.HorseId, other.HorseId));
            var graph = new PedigreeGraph(new[] { new PedigreeNode(mare.HorseId, mare.Parentage), other, new PedigreeNode(sire.HorseId, sire.Parentage) });
            var result = new BreedingEligibility().Evaluate(mare, sire, graph, Policy, 20, Event, Capacity(), 0, 0, 7);
            Assert.Equal(new Rational(1, 4), result.Relatedness.Coefficient); Assert.Contains(result.Rejections, r => r.Code == "relationship_policy");
        }
    }
}
