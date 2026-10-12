using System;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class HorseProfilesTests
    {
        [Fact]
        public void BornHorsesShowAVisibleCoatAndNoHiddenGenetics()
        {
            var state = SaveTestData.Initial();
            foreach (var horse in state.Horses)
            {
                var profile = HorseProfiles.For(state, horse.HorseId);
                Assert.Equal(horse.Name, profile.Name); Assert.True(profile.IsFounder);
                Assert.False(string.IsNullOrWhiteSpace(profile.VisibleCoat)); Assert.NotEqual("Not born yet", profile.VisibleCoat);
                Assert.Equal(horse.PlayerKnowledge.RevealedCalls.Count, profile.KnownGenetics.Count);
                Assert.Null(profile.DueDay); Assert.Empty(profile.OffspringNames);
            }
        }

        [Fact]
        public void PregnancyShowsDueDayParentsAndAnUnobservableUnbornCoat()
        {
            var committed = SaveTestData.Commit(); var state = committed.State; var commit = committed.Receipt.Commit;
            var mare = state.Horses.Single(h => h.ReproductionState.PregnancyId != null);
            var mareProfile = HorseProfiles.For(state, mare.HorseId);
            Assert.Equal(commit.PlannedBirthDay, mareProfile.DueDay);
            var foal = state.Horses.Single(h => h.HorseId.Equals(commit.OffspringHorseId));
            Assert.Contains(foal.Name, mareProfile.OffspringNames);
            var foalProfile = HorseProfiles.For(state, foal.HorseId);
            Assert.Equal("Not born yet", foalProfile.VisibleCoat); Assert.Null(foalProfile.AgeDays); Assert.False(foalProfile.IsFounder);
            var parentNames = new[] { foalProfile.ParentAName, foalProfile.ParentBName };
            Assert.Contains(mare.Name, parentNames);
        }

        [Fact]
        public void RevealedCallsAreListedInStableOrderAndUnknownHorsesAreRejected()
        {
            var state = SaveTestData.Initial(); var horse = state.Horses[0];
            var known = new PlayerKnowledgeRecord(new[] { new System.Collections.Generic.KeyValuePair<string, AllelePair>("MC1R", new AllelePair("E", "e")),
                new System.Collections.Generic.KeyValuePair<string, AllelePair>("ASIP", new AllelePair("A", "a")) }, Array.Empty<HorseId>());
            var revealed = state.WithHorses(state.Horses.Select(h => h.HorseId.Equals(horse.HorseId)
                ? new HorseRecord(h.HorseId, h.Name, h.Parentage, h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState,
                    h.ReproductionState, h.DevelopmentalFactors, h.InheritedGamePotential, h.TrainingState, known, h.CosmeticState,
                    h.HomeAssignment, h.OriginRecord, h.OriginCommitId, h.BirthDay, h.Revision) : h));
            var profile = HorseProfiles.For(revealed, horse.HorseId);
            Assert.Equal(new[] { "ASIP", "MC1R" }, profile.KnownGenetics.Select(c => c.Locus));
            Assert.Equal("E/e", profile.KnownGenetics.Single(c => c.Locus == "MC1R").Call);
            Assert.Throws<ArgumentException>(() => HorseProfiles.For(state, new HorseId(Guid.NewGuid())));
        }
    }
}
