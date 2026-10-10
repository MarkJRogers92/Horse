using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class PedigreeTests
    {
        public static IEnumerable<object[]> Cases()
        {
            using var file = FixtureFiles.LoadGenetics();
            foreach (var row in file.RootElement.GetProperty("pedigreeFixtures").GetProperty("cases").EnumerateArray()) yield return new object[] { row.GetProperty("id").GetString()! };
        }
        public static IEnumerable<object[]> ValidationCases()
        {
            using var file = FixtureFiles.LoadGenetics();
            foreach (var row in file.RootElement.GetProperty("pedigreeFixtures").GetProperty("validationCases").EnumerateArray()) yield return new object[] { row.GetProperty("id").GetString()! };
        }
        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryFrozenPedigreeCoefficientIsExact(string caseId)
        {
            using var file = FixtureFiles.LoadGenetics(); var fixtures = file.RootElement.GetProperty("pedigreeFixtures");
            var input = fixtures.GetProperty("sharedPedigree").EnumerateArray().ToArray();
            var ids = input.Select((n, i) => (Name: n.GetProperty("horseId").GetString()!, Id: RecordContractTests.Id(i + 1))).ToDictionary(p => p.Name, p => p.Id);
            var row = fixtures.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == caseId);
            var nodes = input.Select(n =>
            {
                var name = n.GetProperty("horseId").GetString()!; var parents = n.GetProperty("parentIds").EnumerateArray().Select(v => ids[v.GetString()!]).ToArray();
                var f = row.TryGetProperty("founderInbreedingOverrides", out var overrides) && overrides.TryGetProperty(name, out var value) ? FrozenFixtureData.Fraction(value) : Rational.Zero;
                var parentage = parents.Length == 0 ? new Parentage(founderBackground: new FounderBackground("frozen.declared-founders", f)) : new Parentage(parents[0], parents[1]);
                return new PedigreeNode(ids[name], parentage);
            }).ToArray();
            var queried = row.GetProperty("parentIds"); var a = ids[queried[0].GetString()!]; var b = ids[queried[1].GetString()!];
            var expected = FrozenFixtureData.Fraction(row.GetProperty("expectedOffspringInbreeding")); var graph = new PedigreeGraph(nodes);
            Assert.Equal(expected, graph.ProposedOffspringInbreeding(a, b).Coefficient); Assert.Equal(expected, graph.Kinship(b, a).Coefficient);
            var reversed = nodes.Reverse().Select(n => new PedigreeNode(n.HorseId, n.Parentage.FounderBackground != null ? n.Parentage
                : new Parentage(n.Parentage.ParentBId, n.Parentage.ParentAId))).ToArray();
            Assert.Equal(expected, new PedigreeGraph(reversed).Kinship(a, b).Coefficient);
        }
        [Theory]
        [MemberData(nameof(ValidationCases))]
        public void EveryFrozenPedigreeValidationCaseIsEnforced(string id)
        {
            var a = RecordContractTests.Id(1); var b = RecordContractTests.Id(2);
            switch (id)
            {
                case "pedigreeCycle": Assert.Throws<ArgumentException>(() => new PedigreeGraph(new[] { new PedigreeNode(a, new Parentage(b)), new PedigreeNode(b, new Parentage(a)) })); break;
                case "selfParent": Assert.Throws<ArgumentException>(() => new PedigreeNode(a, new Parentage(a))); break;
                case "unknownBackground":
                    var result = new PedigreeGraph(new[] { new PedigreeNode(a, new Parentage()), new PedigreeNode(b, new Parentage()) }).Kinship(a, b);
                    Assert.False(result.IsComplete); Assert.Null(result.Coefficient); Assert.Contains("not established", result.Qualification); break;
                default: throw new InvalidOperationException("Unimplemented frozen validation fixture: " + id);
            }
        }
        [Fact]
        public void SelfKinshipIncludesFounderInbreedingAndMissingAncestorsStayUnknown()
        {
            var a = RecordContractTests.Id(1); var b = RecordContractTests.Id(2); var missing = RecordContractTests.Id(3);
            var graph = new PedigreeGraph(new[] { new PedigreeNode(a, new Parentage(founderBackground: new FounderBackground("known", new Rational(1, 8)))), new PedigreeNode(b, new Parentage(a, missing)) });
            Assert.Equal(new Rational(9, 16), graph.Kinship(a, a).Coefficient); Assert.Null(graph.Kinship(a, b).Coefficient); Assert.Null(graph.Kinship(missing, a).Coefficient);
        }
        [Fact]
        public void DifferentUndeclaredFounderGroupsAreNotAssumedUnrelated()
        {
            var a = new PedigreeNode(RecordContractTests.Id(1), new Parentage(founderBackground: new FounderBackground("group1", Rational.Zero)));
            var b = new PedigreeNode(RecordContractTests.Id(2), new Parentage(founderBackground: new FounderBackground("group2", Rational.Zero)));
            Assert.Null(new PedigreeGraph(new[] { a, b }).Kinship(a.HorseId, b.HorseId).Coefficient);
            Assert.Throws<ArgumentException>(() => new PedigreeGraph(new[] { a, a }));
        }
        [Fact]
        public void FullRecordedAncestrySurvivesBeyondTheFiveGenerationUi()
        {
            const int generations = 128; var original = RecordContractTests.Id(1); var current = original;
            Parentage Root() => new Parentage(founderBackground: new FounderBackground("outcross-fixture", Rational.Zero));
            var nodes = new List<PedigreeNode> { new PedigreeNode(original, Root()) };
            for (var i = 0; i < generations; i++)
            {
                var mate = RecordContractTests.Id(2 + 2 * i); var child = RecordContractTests.Id(3 + 2 * i);
                nodes.Add(new PedigreeNode(mate, Root())); nodes.Add(new PedigreeNode(child, new Parentage(current, mate))); current = child;
            }
            var graph = new PedigreeGraph(nodes.AsEnumerable().Reverse());
            Assert.Equal(new Rational(1, BigInteger.Pow(2, generations + 1)), graph.Kinship(original, current).Coefficient);
            Assert.Throws<InvalidOperationException>(() => graph.Kinship(original, current, 1));
        }
    }
}
