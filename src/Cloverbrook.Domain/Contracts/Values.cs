using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Cloverbrook.Domain
{
    internal static class Check
    {
        internal static string Text(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A nonblank stable value is required.", name);
            return value;
        }
        internal static T NotNull<T>(T? value, string name) where T : class => value ?? throw new ArgumentNullException(name);
        internal static int NonNegative(int value, string name)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(name);
            return value;
        }
    }

    internal static class Snapshot
    {
        internal static IReadOnlyList<T> List<T>(IEnumerable<T> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = values.ToArray();
            if (copy.Any(value => value is null)) throw new ArgumentException("Null collection entries are not supported.", nameof(values));
            return Array.AsReadOnly(copy);
        }
        internal static IReadOnlyDictionary<string, T> Map<T>(IEnumerable<KeyValuePair<string, T>> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new SortedDictionary<string, T>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                Check.Text(pair.Key, nameof(values));
                if (pair.Value is null) throw new ArgumentException("Null map values are not supported.", nameof(values));
                copy.Add(pair.Key, pair.Value);
            }
            return new ReadOnlyDictionary<string, T>(copy);
        }
    }

    // Exact arithmetic, with default(Rational) defined as zero. Probabilities are
    // range-checked at probability-consuming boundaries; arithmetic may be signed.
    public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
    {
        private readonly BigInteger numerator;
        private readonly BigInteger denominator;
        public Rational(BigInteger numerator, BigInteger denominator)
        {
            if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            this.numerator = numerator / divisor;
            this.denominator = denominator / divisor;
        }
        public BigInteger Numerator => numerator;
        public BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;
        public static Rational Zero => new Rational(0, 1);
        public static Rational One => new Rational(1, 1);
        public static Rational Half => new Rational(1, 2);
        public double ToDouble() => (double)Numerator / (double)Denominator;
        public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
        public bool Equals(Rational other) => CompareTo(other) == 0;
        public override bool Equals(object? obj) => obj is Rational other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
        public override string ToString() => Numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
        public static Rational operator +(Rational a, Rational b) => new Rational(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator -(Rational a, Rational b) => new Rational(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator *(Rational a, Rational b) => new Rational(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
        public static Rational operator /(Rational a, Rational b)
        {
            if (b.Numerator.IsZero) throw new DivideByZeroException();
            var sign = b.Numerator.Sign;
            return new Rational(a.Numerator * b.Denominator * sign, a.Denominator * BigInteger.Abs(b.Numerator));
        }
        public static bool operator ==(Rational a, Rational b) => a.Equals(b);
        public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
        public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
        public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
        public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;
    }

    public sealed class Weighted<T>
    {
        public Weighted(T value, Rational probability)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (probability <= Rational.Zero || probability > Rational.One) throw new ArgumentOutOfRangeException(nameof(probability));
            Value = value;
            Probability = probability;
        }
        public T Value { get; }
        public Rational Probability { get; }
    }

    // Canonical format v1: little-endian Int32 field count followed by each UTF-8
    // byte length (Int32 LE) and bytes. Callers include a type/version discriminator.
    // Never hash incidental JSON ordering, object hash codes, or current culture.
    public static class Canonical
    {
        public static byte[] Bytes(IEnumerable<string> parts)
        {
            var fields = Snapshot.List(parts);
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                writer.Write(fields.Count);
                foreach (var field in fields)
                {
                    var bytes = Encoding.UTF8.GetBytes(field);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                }
            }
            return buffer.ToArray();
        }
        public static string Hash(params string[] parts) => Hash((IEnumerable<string>)parts);
        public static string Hash(IEnumerable<string> parts)
        {
            using var sha = SHA256.Create();
            var digest = sha.ComputeHash(Bytes(parts));
            return string.Concat(digest.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
