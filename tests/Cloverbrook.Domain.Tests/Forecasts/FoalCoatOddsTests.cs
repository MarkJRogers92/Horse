using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Cloverbrook.Domain.Tests
{
    public sealed class FoalCoatOddsTests
    {
        private readonly ITestOutputHelper output;
        public FoalCoatOddsTests(ITestOutputHelper output) { this.output = output; }
        private static HorseRecord H(int id, ReproductiveRole role, params (string, string, string)[] calls) => FrozenFixtureData.Horse(FrozenFixtureData.With(calls), id, role);

        private System.Collections.Generic.IReadOnlyList<FoalCoatChance> Odds(HorseRecord a, HorseRecord b, string name)
        {
            var watch = Stopwatch.StartNew();
            var odds = FoalCoatOdds.For(a, b, PhenotypeTests.Matrix());
            output.WriteLine($"{name} ({watch.ElapsedMilliseconds} ms): " + string.Join(", ", odds.Select(o => o.Label + " " + o.Text)));
            Assert.True(watch.ElapsedMilliseconds < 5000, name + " took too long for a dialog");
            return odds;
        }

        [Fact]
        public void StarterDunsLeadWithDunWithoutAssumingHiddenCalls()
        {
            var odds = Odds(H(1, ReproductiveRole.Mare, ("tbx3", "D", "D")), H(2, ReproductiveRole.Stallion, ("tbx3", "D", "D")), "starter duns");
            // The player cannot see that both are D/D, so non-dun foals stay possible (honest ranges), but dun leads.
            Assert.Equal("Bay dun", odds[0].Label); Assert.Equal(1.0, odds[0].High, 6);
            Assert.All(odds.Skip(1), o => Assert.Equal(0, o.Low, 6));
        }

        [Fact]
        public void VisiblePatternsGiveHonestRangesWithoutLeakingHiddenCalls()
        {
            var tobiano = H(3, ReproductiveRole.Mare, ("pattern.eca3", "TO", "baseline"));
            var dun = H(4, ReproductiveRole.Stallion, ("tbx3", "D", "nd2"));
            var odds = Odds(tobiano, dun, "bay tobiano x bay dun");
            Assert.Contains(odds, o => o.Label.Contains("tobiano") && o.High >= 0.5 - 1e-9);
            Assert.Contains(odds, o => o.Label == "Bay" || o.Label.StartsWith("Bay"));
            // A hidden TO/TO versus TO/baseline cannot be seen, so "no tobiano" is a range that starts at zero.
            var plain = odds.Where(o => !o.Label.Contains("tobiano")).ToArray();
            Assert.All(plain, o => Assert.Equal(0, o.Low, 6));
            Odds(H(5, ReproductiveRole.Mare, ("stx17", "G", "N")), H(6, ReproductiveRole.Stallion, ("trpm1", "LP", "N"), ("patn1", "PATN1", "N")), "grey x leopard");
            Odds(H(7, ReproductiveRole.Mare, ("slc45a2", "Cr", "N")), H(8, ReproductiveRole.Stallion, ("tbx3", "D", "D")), "buckskin x bay dun");
        }

        [Fact]
        public void RarerFamiliesAreDominantOrRecessiveAsTheyShouldBeAndStayFast()
        {
            // Silver black x silver black: both visibly silver, so silver foals lead; plain black is possible only if both are Z/N.
            var silver = Odds(H(1, ReproductiveRole.Mare, ("asip", "a", "a"), ("pmel", "Z", "N")), H(2, ReproductiveRole.Stallion, ("asip", "a", "a"), ("pmel", "Z", "N")), "silver black x silver black");
            Assert.Equal("Silver black", silver[0].Label); Assert.True(silver[0].High >= 0.75 - 1e-9);
            Assert.Contains(silver, o => o.Label == "Black" && o.High <= 0.25 + 1e-9);
            // A chestnut parent can hide silver, but only a silver mate makes that worth showing.
            var hidden = Odds(H(3, ReproductiveRole.Mare, ("mc1r", "e", "e")), H(4, ReproductiveRole.Stallion, ("asip", "a", "a"), ("pmel", "Z", "N")), "chestnut x silver black");
            Assert.Contains(hidden, o => o.Label.StartsWith("Silver"));
            Assert.Contains(hidden, o => o.Label == "Chestnut");
            // Champagne is dominant: a champagne parent over a plain one gives champagne up to half of the time.
            var champagne = Odds(H(5, ReproductiveRole.Mare, ("slc36a1", "Ch", "N")), H(6, ReproductiveRole.Stallion), "amber champagne x bay");
            Assert.Contains(champagne, o => o.Label == "Amber champagne" && o.High >= 0.5 - 1e-9);
            // Pearl is recessive: two pearl horses breed pearl, and the carriers they could hide are explored.
            var pearl = Odds(H(7, ReproductiveRole.Mare, ("slc45a2", "Prl", "Prl")), H(8, ReproductiveRole.Stallion, ("slc45a2", "Prl", "Prl")), "pearl x pearl");
            Assert.Equal("Pearl bay", pearl[0].Label); Assert.Equal(1.0, pearl[0].High, 6);
            // Sabino and splash are dominant white patterns; SB1/SB1 is the visible "sabino white".
            var sabino = Odds(H(9, ReproductiveRole.Mare, ("pattern.eca3", "SB1", "baseline")), H(10, ReproductiveRole.Stallion, ("pattern.mitf", "SW1", "baseline")), "sabino x splash");
            Assert.Contains(sabino, o => o.Label == "Bay sabino" && o.High >= 0.25 - 1e-9);
            Assert.Contains(sabino, o => o.Label == "Bay splash" && o.High >= 0.25 - 1e-9);
            Assert.Contains(sabino, o => o.Label == "Bay sabino splash" && o.High >= 0.25 - 1e-9);
            // Everything together stays inside the dialog's time budget.
            Odds(H(11, ReproductiveRole.Mare, ("pmel", "Z", "N"), ("slc36a1", "Ch", "N"), ("pattern.eca3", "TO", "SB1"), ("tbx3", "D", "nd2"), ("slc45a2", "Cr", "N"), ("stx17", "G", "N")),
                H(12, ReproductiveRole.Stallion, ("slc45a2", "Cr", "Prl"), ("pattern.mitf", "SW1", "baseline"), ("trpm1", "LP", "N"), ("pattern.eca3", "Rn", "baseline")), "kitchen sink");
        }

        [Fact]
        public void ChanceTextIsFriendly()
        {
            var odds = FoalCoatOdds.For(H(1, ReproductiveRole.Mare), H(2, ReproductiveRole.Stallion), PhenotypeTests.Matrix());
            Assert.All(odds, o => Assert.Matches(@"^(\d+%|\d+–\d+%|up to \d+%|under 1%)$", o.Text));
        }
    }
}
