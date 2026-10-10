using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class StableBreedingQueriesTests
    {
        private static StableBreedingQueries Queries(StableSnapshot state, ConceptionService? conception = null) => new StableBreedingQueries(state, conception ?? ConceptionTestData.Service());
        [Fact]
        public void PublicHorseAndPregnancyViewsCannotExposeAuthoritativeGenomes()
        {
            var state = SaveTestData.Commit().State; var queries = Queries(state);
            Assert.DoesNotContain(typeof(PublicHorseSummary).GetProperties(), p => p.PropertyType == typeof(GenomeSnapshot) || p.PropertyType == typeof(HorseRecord) || p.PropertyType == typeof(Phenotype));
            Assert.DoesNotContain(typeof(PublicPregnancy).GetProperties(), p => p.PropertyType == typeof(GenomeSnapshot) || p.PropertyType == typeof(HorseRecord));
            var pregnancy = queries.GetPregnancy(ForecastTestData.Id(1))!;
            Assert.Equal(state.Conceptions[0].Commit.OffspringHorseId, pregnancy.OffspringHorseId); Assert.Equal(14, pregnancy.PlannedBirthDay);
            Assert.Equal(pregnancy.OffspringHorseId, queries.GetOffspring(pregnancy.EventId).HorseId);
            Assert.Empty(queries.GetKnownGenetics(pregnancy.OffspringHorseId).RevealedCalls);
        }
        [Fact]
        public void ReadQueriesPreserveHiddenGenotypeIndependenceAndRngIsolation()
        {
            StableSnapshot Stable(string agouti)
            {
                var parents = ConceptionTestData.Parents();
                var genome = ForecastTestData.Genome(("asip", agouti, agouti), ("slc45a2", "N", "Cr"));
                var first = parents[0];
                var knowledge = ForecastTestData.Known(1, genome, "asip").Knowledge;
                first = new HorseRecord(first.HorseId, first.Name, first.Parentage, genome, first.ResolverVersion, first.AppearanceSeed,
                    first.OwnerId, first.LifeState, first.ReproductionState, playerKnowledge: knowledge);
                var second = parents[1];
                second = new HorseRecord(second.HorseId, second.Name, second.Parentage, second.Genome, second.ResolverVersion, second.AppearanceSeed,
                    second.OwnerId, second.LifeState, second.ReproductionState, playerKnowledge: ForecastTestData.Known(2, second.Genome).Knowledge);
                return SaveTestData.Initial().WithHorses(new[] { first, second });
            }
            var left = Stable("A"); var right = Stable("a");
            var a = Queries(left).PreviewBreeding(ForecastTestData.Id(1), ForecastTestData.Id(2), PublicCoatScope.AllSupported());
            var b = Queries(right).PreviewBreeding(ForecastTestData.Id(1), ForecastTestData.Id(2), PublicCoatScope.AllSupported());
            Assert.Equal(a.Coat.CacheKey, b.Coat.CacheKey); Assert.Equal(ForecastMethod.Exact, a.Coat.Method);
            Assert.Equal(Rational.Half, a.Coat.Outcomes.Single(o => o.Label == "Palomino").ExactProbability);
            Assert.Equal(a.Traits.Select(t => t.InheritedMinimum), b.Traits.Select(t => t.InheritedMinimum));
            Assert.Equal(a.Variants.SelectMany(v => v.Outcomes.Select(o => o.LowerProbability)), b.Variants.SelectMany(v => v.Outcomes.Select(o => o.LowerProbability)));
            Assert.False(a.Relatedness!.IsComplete); Assert.Equal(0UL, left.Header.RngStreams.Breeding.EventCounter);
            Assert.True(Queries(left).CheckBreedingEligibility(ConceptionTestData.Request()).IsEligible);
            Assert.Equal(0UL, left.Header.RngStreams.Breeding.EventCounter); Assert.Null(Queries(left).GetPregnancy(ForecastTestData.Id(1)));
        }
        [Fact]
        public void ReadinessCatchesSaleChangedCapacityFundsAndUnavailableVisuals()
        {
            var initial = SaveTestData.Initial(); var queries = Queries(SaveTestData.Initial(49));
            Assert.Contains(queries.CheckBreedingEligibility(ConceptionTestData.Request()).Rejections, e => e.Code == "funds");
            var changedCapacity = new StableSnapshot(initial.Header, initial.PlayerOwnerId, initial.Horses, 100, 1, pendingCapacityPlans: initial.PendingCapacityPlans);
            Assert.Contains(Queries(changedCapacity).CheckBreedingEligibility(ConceptionTestData.Request()).Rejections, e => e.Code == "stale_capacity");
            var h = initial.Horses[0]; var sold = new HorseRecord(h.HorseId, h.Name, h.Parentage, h.Genome, h.ResolverVersion, h.AppearanceSeed,
                new OwnerId(new Guid("ba000000-0000-0000-0000-000000000099")), h.LifeState, h.ReproductionState, revision: h.Revision + 1);
            var sale = initial.WithHorses(initial.Horses.Select(x => x.HorseId.Equals(h.HorseId) ? sold : x));
            Assert.Contains(Queries(sale).CheckBreedingEligibility(ConceptionTestData.Request()).Rejections, e => e.Code == "ownership");
            Assert.Contains(Queries(initial, ConceptionTestData.Service(ForecastTestData.Unreleased())).CheckBreedingEligibility(ConceptionTestData.Request()).Rejections, e => e.Code == "unreleased_catalog");
            Assert.Equal(ResolutionStatus.Unsupported, Queries(initial, ConceptionTestData.Service(ForecastTestData.Unreleased())).ResolveVisualDescriptor(h.HorseId).Status);
        }
        [Fact]
        public void ReadinessProtectsDuplicateIdsAndDoesNotRecheckHistoricalQuotes()
        {
            var committed = SaveTestData.Commit().State; var queries = Queries(committed, ConceptionTestData.Service(ForecastTestData.Unreleased()));
            var replay = queries.CheckBreedingEligibility(ConceptionTestData.Request()); Assert.True(replay.IsEligible); Assert.True(replay.IsIdenticalRetry);
            Assert.Contains(queries.CheckBreedingEligibility(ConceptionTestData.Request(expectedSaveRevision: 1)).Rejections, e => e.Code == "idempotency_conflict");
            Assert.False(queries.CompareHypotheticalPairing(committed.Horses[0].Genome, committed.Horses[1].Genome).AuthorizesConception);
        }
        [Fact]
        public void ActualForecastsNeverSubstituteCurrentRulesForUnknownHistoricalVersions()
        {
            var state = SaveTestData.Initial(); var h = state.Header;
            var unknown = new SaveHeader(h.SaveId, h.SaveMode, h.SchemaVersion, h.GameBuildVersion, h.SaveRevision, h.CalendarDay, h.CalendarProfileVersion,
                h.CareMode, h.ActiveVariantCatalogVersion, "uninstalled-historical-inheritance", h.DefaultOffspringResolverVersion,
                h.RetainedResolverVersions, h.RngStreams, h.LastCommittedBreedingEventIndex, h.MigrationHistory, h.PayloadHash);
            var queries = Queries(state.WithHeader(unknown));
            Assert.Throws<SaveCompatibilityException>(() => queries.PreviewBreeding(ForecastTestData.Id(1), ForecastTestData.Id(2), PublicCoatScope.AllSupported()));
            Assert.Contains(queries.CheckBreedingEligibility(ConceptionTestData.Request()).Rejections, e => e.Code == "unsupported_versions");
        }
    }
}
