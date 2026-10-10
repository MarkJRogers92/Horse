using System;
using System.Globalization;
using System.Numerics;

namespace Cloverbrook.Domain
{
    public readonly struct Seed128 : IEquatable<Seed128>
    {
        public Seed128(ulong high, ulong low) { High = high; Low = low; }
        public ulong High { get; }
        public ulong Low { get; }
        public override string ToString() => High.ToString("x16", CultureInfo.InvariantCulture) + Low.ToString("x16", CultureInfo.InvariantCulture);
        public static Seed128 Parse(string hex)
        {
            if (hex == null || hex.Length != 32) throw new FormatException("Seed128 requires exactly 32 hexadecimal characters.");
            return new Seed128(ulong.Parse(hex.Substring(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
                ulong.Parse(hex.Substring(16, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
        }
        public bool Equals(Seed128 other) => High == other.High && Low == other.Low;
        public override bool Equals(object? obj) => obj is Seed128 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(High, Low);
        public static bool operator ==(Seed128 a, Seed128 b) => a.Equals(b);
        public static bool operator !=(Seed128 a, Seed128 b) => !a.Equals(b);
    }

    public static class Seeds
    {
        public const string AlgorithmVersion = "cloverbrook.seed.v1";
        public static Seed128 Derive(Seed128 seed, string purpose, ulong index = 0)
        {
            Check.Text(purpose, nameof(purpose));
            return Seed128.Parse(Canonical.Hash(AlgorithmVersion, seed.ToString(), purpose,
                index.ToString(CultureInfo.InvariantCulture)).Substring(0, 32));
        }
    }

    public sealed class RngStreamState
    {
        public RngStreamState(Seed128 seed, string purpose, ulong eventCounter = 0, string algorithmVersion = Seeds.AlgorithmVersion)
        {
            Seed = seed;
            Purpose = Check.Text(purpose, nameof(purpose));
            EventCounter = eventCounter;
            AlgorithmVersion = Check.Text(algorithmVersion, nameof(algorithmVersion));
        }
        public Seed128 Seed { get; }
        public string Purpose { get; }
        public ulong EventCounter { get; }
        public string AlgorithmVersion { get; }
        public RngStreamState Advance() => new RngStreamState(Seed, Purpose, checked(EventCounter + 1), AlgorithmVersion);
    }

    // Local counter-based PRNG, not a global service. Persist root + purpose + counter.
    // Rejection sampling avoids modulo bias. This is reproducibility, not anti-cheat.
    public sealed class CounterRng
    {
        private readonly Seed128 seed;
        private readonly string purpose;
        public CounterRng(Seed128 seed, string purpose, ulong counter = 0)
        {
            this.seed = seed;
            this.purpose = Check.Text(purpose, nameof(purpose));
            Counter = counter;
        }
        public CounterRng(RngStreamState state) : this(Check.NotNull(state, nameof(state)).Seed, state.Purpose, state.EventCounter)
        {
            if (state.AlgorithmVersion != Seeds.AlgorithmVersion) throw new NotSupportedException("Unknown RNG algorithm: " + state.AlgorithmVersion);
        }
        public ulong Counter { get; private set; }
        public RngStreamState State => new RngStreamState(seed, purpose, Counter);
        public ulong NextUInt64()
        {
            if (Counter == ulong.MaxValue) throw new OverflowException("RNG counter exhausted; refusing to wrap.");
            return Seeds.Derive(seed, purpose + "/draw", Counter++).High;
        }
        public BigInteger NextBelow(BigInteger exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            if (exclusiveUpperBound.IsOne) return BigInteger.Zero;
            var maximum = (exclusiveUpperBound - 1).ToByteArray();
            var length = maximum.Length;
            while (length > 1 && maximum[length - 1] == 0) length--;
            int mask = 1;
            while (mask < maximum[length - 1]) mask = (mask << 1) | 1;
            var bytes = new byte[length + 1]; // last zero byte forces nonnegative BigInteger
            while (true)
            {
                ulong word = 0;
                for (var i = 0; i < length; i++)
                {
                    if (i % 8 == 0) word = NextUInt64();
                    bytes[i] = (byte)(word & 255);
                    word >>= 8;
                }
                bytes[length - 1] &= (byte)mask;
                var candidate = new BigInteger(bytes);
                if (candidate < exclusiveUpperBound) return candidate;
            }
        }
        public bool Chance(Rational probability)
        {
            if (probability < Rational.Zero || probability > Rational.One) throw new ArgumentOutOfRangeException(nameof(probability));
            if (probability == Rational.Zero) return false;
            if (probability == Rational.One) return true;
            return NextBelow(probability.Denominator) < probability.Numerator;
        }
    }

    public sealed class RngStreams
    {
        public RngStreams(RngStreamState breeding, RngStreamState cosmetic, RngStreamState world, RngStreamState competition)
        {
            Breeding = Require(breeding, "breeding"); Cosmetic = Require(cosmetic, "cosmetic");
            World = Require(world, "world"); Competition = Require(competition, "competition");
        }
        private static RngStreamState Require(RngStreamState state, string purpose)
        {
            Check.NotNull(state, nameof(state));
            if (state.Purpose != purpose) throw new ArgumentException("Expected " + purpose + " stream.", nameof(state));
            return state;
        }
        public RngStreamState Breeding { get; }
        public RngStreamState Cosmetic { get; }
        public RngStreamState World { get; }
        public RngStreamState Competition { get; }
        public static RngStreams FromSeed(Seed128 seed) => new RngStreams(new RngStreamState(seed, "breeding"),
            new RngStreamState(seed, "cosmetic"), new RngStreamState(seed, "world"), new RngStreamState(seed, "competition"));
    }
}
