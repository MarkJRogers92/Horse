using System;
using System.Globalization;
using System.Numerics;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class ValueContractTests
    {
        [Fact]
        public void IdentityIsTypedAndNotADisplayName()
        {
            var guid = Guid.Parse("a4115f98-6805-4eb9-a9c6-b586321dbbd6");
            Assert.Equal(new HorseId(guid), new HorseId(guid));
            Assert.False(new HorseId(guid).Equals(new OwnerId(guid)));
            Assert.Throws<ArgumentException>(() => new HorseId(Guid.Empty));
        }
        [Fact]
        public void RationalArithmeticIsExactAndDefaultIsZero()
        {
            Assert.Equal(Rational.Zero, default(Rational));
            Assert.Equal(Rational.Zero.GetHashCode(), default(Rational).GetHashCode());
            Assert.Equal(new Rational(1, 3), new Rational(2, 6));
            Assert.Equal(new Rational(1, 2), new Rational(1, 3) + new Rational(1, 6));
            var huge = BigInteger.Pow(2, 200);
            Assert.Equal(new Rational(1, 3), new Rational(huge, huge * 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Rational(1, 0));
            Assert.Throws<DivideByZeroException>(() => Rational.One / Rational.Zero);
        }
        [Fact]
        public void CanonicalHashHasAnIndependentGoldenVectorAndUnambiguousFields()
        {
            Assert.Equal("b50c3d2739fd1e990d63833bb2f689748c77015dc36f68da6bc25825f6c48522", Canonical.Hash("ab", "c"));
            Assert.NotEqual(Canonical.Hash("ab", "c"), Canonical.Hash("a", "bc"));
        }
        [Fact]
        public void SeedDerivationMatchesAnIndependentGoldenVector()
        {
            Assert.Equal("7f53f79489e7d1b2a288ca01efb207b3", Seeds.Derive(new Seed128(1, 2), "appearance", 7).ToString());
            Assert.Equal(new Seed128(1, 2), Seed128.Parse("00000000000000010000000000000002"));
            Assert.Throws<FormatException>(() => Seed128.Parse("abc"));
        }
        [Fact]
        public void CounterRngMatchesGoldenDrawsAndRestoresExactly()
        {
            var rng = new CounterRng(new Seed128(1, 2), "fixture");
            Assert.Equal(0x6fc8a0bad30c3ae9UL, rng.NextUInt64());
            Assert.Equal(0x6e37c9029fcb4363UL, rng.NextUInt64());
            var restored = new CounterRng(rng.State);
            Assert.Equal(0x1af138eb2a6470e5UL, restored.NextUInt64());
            Assert.Equal(restored.State.EventCounter, new CounterRng(rng.State).State.EventCounter + 1);
            Assert.Equal(0x1af138eb2a6470e5UL, rng.NextUInt64());
        }
        [Fact]
        public void ForecastCosmeticsAndCultureCannotConsumeBreedingState()
        {
            var seed = new Seed128(5, 7);
            var streams = RngStreams.FromSeed(seed);
            var before = Seeds.Derive(streams.Breeding.Seed, "breeding", streams.Breeding.EventCounter);
            var forecast = new CounterRng(seed, "forecast/preview");
            var cosmetics = new CounterRng(streams.Cosmetic);
            for (var i = 0; i < 100; i++) { forecast.NextUInt64(); cosmetics.NextUInt64(); }
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                Assert.Equal(before, Seeds.Derive(streams.Breeding.Seed, "breeding", streams.Breeding.EventCounter));
            }
            finally { CultureInfo.CurrentCulture = old; }
            Assert.Equal(0UL, streams.Breeding.EventCounter);
            Assert.NotEqual(new CounterRng(seed, "world").NextUInt64(), new CounterRng(seed, "breeding").NextUInt64());
        }
        [Fact]
        public void SamplingHandlesLargeBoundsAndChecksProbabilities()
        {
            var rng = new CounterRng(new Seed128(4, 9), "bounds");
            var bound = BigInteger.Pow(2, 140) + 7;
            for (var i = 0; i < 100; i++) Assert.InRange(rng.NextBelow(bound), BigInteger.Zero, bound - 1);
            Assert.False(rng.Chance(Rational.Zero)); Assert.True(rng.Chance(Rational.One));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Chance(new Rational(2, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBelow(BigInteger.Zero));
        }
        [Fact]
        public void UnknownAlgorithmsAndCounterWrapAreRejected()
        {
            Assert.Throws<NotSupportedException>(() => new CounterRng(new RngStreamState(default, "breeding", 0, "unknown")));
            Assert.Throws<OverflowException>(() => new CounterRng(default, "test", ulong.MaxValue).NextUInt64());
            Assert.Throws<OverflowException>(() => new RngStreamState(default, "breeding", ulong.MaxValue).Advance());
        }
    }
}
