using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class PracticeGallopTests
    {
        private static HorseRecord WithDevelopment(HorseRecord h, HorseId id, string name, int speedAdjust) => new HorseRecord(id, name, h.Parentage,
            h.Genome, h.ResolverVersion, h.AppearanceSeed, h.OwnerId, h.LifeState, new ReproductionState(h.ReproductionState.Role), new QuantizedVector(
                GameTraitResolver.TraitIds.Select(t => new KeyValuePair<string, int>(t, t is "speed" or "acceleration" or "endurance" ? speedAdjust : 0))),
            h.InheritedGamePotential, h.TrainingState, h.PlayerKnowledge, h.CosmeticState, h.HomeAssignment, h.OriginRecord, h.OriginCommitId, h.BirthDay, h.Revision);

        [Fact]
        public void SameSaveDayRunAndEntrantsReproduceTheExactResult()
        {
            var state = SaveTestData.Initial(); var ids = state.Horses.Select(h => h.HorseId).ToArray();
            var first = PracticeGallop.Run(state, ids, 0); var again = PracticeGallop.Run(state, ids.Reverse(), 0);
            Assert.Equal(first.Seed, again.Seed); Assert.Equal(PracticeGallop.Version, first.RulesVersion);
            Assert.Equal(first.Entrants.Select(e => (e.HorseId, e.FinishMilliseconds, e.Place)), again.Entrants.Select(e => (e.HorseId, e.FinishMilliseconds, e.Place)));
            Assert.Equal(first.Frames.SelectMany(f => f), again.Frames.SelectMany(f => f));
            Assert.Equal(new[] { 1, 2 }, first.Entrants.Select(e => e.Place).OrderBy(p => p));
            Assert.All(first.Entrants, e => Assert.InRange(e.FinishMilliseconds, 35_000, 90_000));
            Assert.All(first.Frames[first.Frames.Count - 1], p => Assert.Equal(1000, p));
            Assert.Equal(2, first.Observations.Count);
            Assert.NotEqual(first.Seed, PracticeGallop.Run(state, ids, 1).Seed);
        }

        [Fact]
        public void PracticeDoesNotChangeTheSave()
        {
            var state = SaveTestData.Initial(); var before = new StableSaveCodec().Encode(state);
            PracticeGallop.Run(state, state.Horses.Select(h => h.HorseId), 3);
            Assert.Equal(before, new StableSaveCodec().Encode(state));
        }

        [Fact]
        public void PregnantUnbornAndUnknownHorsesCannotGallop()
        {
            var state = SaveTestData.Commit().State;
            var eligibility = PracticeGallop.Eligibility(state).ToDictionary(e => e.HorseId);
            var mare = state.Horses.Single(h => h.ReproductionState.PregnancyId != null);
            var unborn = state.Horses.Single(h => h.LifeState.Stage == LifeStage.Unborn);
            Assert.False(eligibility[mare.HorseId].Eligible); Assert.False(eligibility[unborn.HorseId].Eligible);
            Assert.Throws<ArgumentException>(() => PracticeGallop.Run(state, new[] { mare.HorseId }, 0));
            Assert.Throws<ArgumentException>(() => PracticeGallop.Run(state, new[] { new HorseId(Guid.NewGuid()) }, 0));
            Assert.Throws<ArgumentException>(() => PracticeGallop.Run(state, Array.Empty<HorseId>(), 0));
        }

        [Fact]
        public void BetterDevelopedRunningTraitsWinMostPractices()
        {
            var state = SaveTestData.Initial(); var template = state.Horses[0];
            var quick = WithDevelopment(template, new HorseId(Guid.Parse("aa000000-0000-0000-0000-00000000000a")), "Quick", 500);
            var steady = WithDevelopment(template, new HorseId(Guid.Parse("aa000000-0000-0000-0000-00000000000b")), "Steady", -500);
            var pair = state.WithHorses(state.Horses.Concat(new[] { quick, steady }));
            var wins = Enumerable.Range(0, 20).Count(run =>
                PracticeGallop.Run(pair, new[] { quick.HorseId, steady.HorseId }, run).Entrants.Single(e => e.HorseId.Equals(quick.HorseId)).Place == 1);
            Assert.InRange(wins, 15, 20);
        }
    }
}
