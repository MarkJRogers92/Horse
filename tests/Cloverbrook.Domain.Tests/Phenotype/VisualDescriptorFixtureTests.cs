using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class VisualDescriptorFixtureTests
    {
        // Independent reference calls for each phase-1 matrix class. The final row
        // is an overlay on bay, not a replacement pigment class. Synthetic markers
        // are fixed in FrozenFixtureData solely to make neutral comparisons useful.
        public static IEnumerable<object[]> Phase1Rows()
        {
            yield return new object[] { "chestnut", "e", "A", "N", "N", "nd2", "Chestnut" };
            yield return new object[] { "bay", "E", "A", "N", "N", "nd2", "Bay" };
            yield return new object[] { "black", "E", "a", "N", "N", "nd2", "Black" };
            yield return new object[] { "palomino", "e", "A", "Cr", "N", "nd2", "Palomino" };
            yield return new object[] { "buckskin", "E", "A", "Cr", "N", "nd2", "Buckskin" };
            yield return new object[] { "smoky_black", "E", "a", "Cr", "N", "nd2", "Smoky black" };
            yield return new object[] { "cremello", "e", "A", "Cr", "Cr", "nd2", "Cremello" };
            yield return new object[] { "perlino", "E", "A", "Cr", "Cr", "nd2", "Perlino" };
            yield return new object[] { "smoky_cream", "E", "a", "Cr", "Cr", "nd2", "Smoky cream" };
            yield return new object[] { "red_dun", "e", "A", "N", "N", "D", "Red dun" };
            yield return new object[] { "bay_dun", "E", "A", "N", "N", "D", "Bay dun" };
            yield return new object[] { "grullo", "E", "a", "N", "N", "D", "Grullo" };
            yield return new object[] { "non_dun1_marked", "E", "A", "N", "N", "nd1", "Bay with primitive markings" };
        }
        // D-026 releases: cream-dun stacking and the white/pattern overlays, each shown on one reference base.
        // The last column is extra "locus=A/B" calls on top of the base row.
        public static IEnumerable<object[]> D026Rows()
        {
            yield return new object[] { "dunalino_family", "E", "A", "Cr", "N", "D", "Dunskin", "" };
            yield return new object[] { "double_cream_dun", "E", "A", "Cr", "Cr", "D", "Perlino dun", "" };
            yield return new object[] { "tobiano_overlay", "E", "A", "N", "N", "nd2", "Bay tobiano", "pattern.eca3=TO/baseline" };
            yield return new object[] { "roan_overlay", "E", "A", "N", "N", "nd2", "Bay roan", "pattern.eca3=Rn/baseline" };
            yield return new object[] { "grey_overlay", "E", "a", "N", "N", "nd2", "Black (progressive grey)", "stx17=G/N" };
            yield return new object[] { "leopard_complex_overlay", "e", "A", "N", "N", "nd2", "Chestnut blanket appaloosa", "trpm1=LP/N" };
        }
        private static (string Locus, string A, string B)[] Extra(string extra) => extra.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => { var parts = c.Split('='); var calls = parts[1].Split('/'); return (parts[0], calls[0], calls[1]); }).ToArray();
        private static HorseRecord ReferenceHorse(string extension, string agouti, string creamA, string creamB, string dun, VisualAge age, string extra = "")
        {
            var genome = FrozenFixtureData.With(new[] { ("mc1r", extension, extension), ("asip", agouti, agouti), ("slc45a2", creamA, creamB), ("tbx3", dun, "nd2") }.Concat(Extra(extra)).ToArray());
            return FrozenFixtureData.Horse(genome, stage: age == VisualAge.Foal ? LifeStage.Foal : LifeStage.Adult, age: age);
        }
        [Theory]
        [MemberData(nameof(Phase1Rows))]
        public void EveryPhase1ClassHasConsistentAdultAndFoalDescriptors(string classId, string extension, string agouti,
            string creamA, string creamB, string dun, string visibleLabel)
        {
            var matrix = PhenotypeTests.Matrix(true); var resolver = new PhenotypeResolver();
            var adult = resolver.ResolveForPresentation(ReferenceHorse(extension, agouti, creamA, creamB, dun, VisualAge.Adult), matrix, "reference-assets.v1", "canonical-anatomy.v1");
            var foal = resolver.ResolveForPresentation(ReferenceHorse(extension, agouti, creamA, creamB, dun, VisualAge.Foal), matrix, "reference-assets.v1", "canonical-anatomy.v1");
            Assert.Equal(ResolutionStatus.Supported, adult.Status); Assert.Equal(ResolutionStatus.Supported, foal.Status);
            Assert.Equal(visibleLabel, adult.DisplayLabel); Assert.Equal(visibleLabel, foal.DisplayLabel);
            Assert.Contains(classId, adult.Descriptor!.RequiredCoatClasses); Assert.Contains(classId, foal.Descriptor!.RequiredCoatClasses);
            Assert.All(matrix.Classes[classId].LayerIds, layer => Assert.Contains(layer, adult.Descriptor.CoatLayers));
            Assert.Equal(adult.Descriptor.MarkingRealizationKey, foal.Descriptor.MarkingRealizationKey);
            Assert.Equal(adult.Descriptor.CoatRecipe, foal.Descriptor.CoatRecipe); Assert.NotEqual(adult.Descriptor.MeshFamilyId, foal.Descriptor.MeshFamilyId);
            Assert.NotEqual(adult.Descriptor.CacheKey, foal.Descriptor.CacheKey);
            var real = resolver.ResolveForPresentation(ReferenceHorse(extension, agouti, creamA, creamB, dun, VisualAge.Adult), PhenotypeTests.Matrix(), "reference-assets.v1", "canonical-anatomy.v1");
            Assert.Equal(ResolutionStatus.Supported, real.Status); // released by D-013
            Assert.Equal(adult.Descriptor.CoatRecipe, real.Descriptor!.CoatRecipe);
        }
        [Theory]
        [MemberData(nameof(D026Rows))]
        public void EveryD026ClassIsReleasedWithConsistentAdultAndFoalDescriptors(string classId, string extension, string agouti,
            string creamA, string creamB, string dun, string visibleLabel, string extra)
        {
            var matrix = PhenotypeTests.Matrix(); var resolver = new PhenotypeResolver();
            var adult = resolver.ResolveForPresentation(ReferenceHorse(extension, agouti, creamA, creamB, dun, VisualAge.Adult, extra), matrix, "reference-assets.v1", "canonical-anatomy.v1");
            var foal = resolver.ResolveForPresentation(ReferenceHorse(extension, agouti, creamA, creamB, dun, VisualAge.Foal, extra), matrix, "reference-assets.v1", "canonical-anatomy.v1");
            Assert.Equal(ResolutionStatus.Supported, adult.Status); Assert.Equal(ResolutionStatus.Supported, foal.Status);
            Assert.Equal(visibleLabel, adult.DisplayLabel); Assert.Equal(visibleLabel, foal.DisplayLabel);
            Assert.Contains(classId, adult.Descriptor!.RequiredCoatClasses);
            Assert.All(matrix.Classes[classId].LayerIds, layer => Assert.Contains(layer, adult.Descriptor.CoatLayers));
            Assert.Equal(adult.Descriptor.CoatRecipe, foal.Descriptor!.CoatRecipe);
        }
        [Fact]
        public void ExportReferenceDescriptorsForEveryPhase1ClassWithoutChangingReleaseStatus()
        {
            using var source = FixtureFiles.Load("COAT_SUPPORT_MATRIX.json");
            var expected = source.RootElement.GetProperty("coatClasses").EnumerateArray().Where(c => c.GetProperty("phase").GetInt32() == 1).Select(c => c.GetProperty("id").GetString()!).OrderBy(s => s).ToArray();
            Assert.Equal(expected, Phase1Rows().Select(r => (string)r[0]).OrderBy(s => s).ToArray());
            var rows = Phase1Rows().Select(r => r.Take(6).Append("").ToArray()).Concat(D026Rows().Select(r => r.Take(6).Append(r[7]).ToArray())).ToArray();
            expected = expected.Concat(D026Rows().Select(r => (string)r[0])).ToArray();
            var resolver = new PhenotypeResolver(); var examples = new List<object>(); var testMatrix = PhenotypeTests.Matrix(true);
            foreach (var row in rows)
            {
                var classId = (string)row[0]; var extension = (string)row[1]; var agouti = (string)row[2]; var creamA = (string)row[3]; var creamB = (string)row[4]; var dun = (string)row[5]; var extra = (string)row[6];
                foreach (var age in new[] { VisualAge.Adult, VisualAge.Foal })
                {
                    var horse = ReferenceHorse(extension, agouti, creamA, creamB, dun, age, extra);
                    var overrides = new Dictionary<string, string[]> { ["mc1r"] = new[] { extension, extension }, ["asip"] = new[] { agouti, agouti },
                        ["slc45a2"] = new[] { creamA, creamB }, ["tbx3"] = new[] { dun, "nd2" } };
                    foreach (var (locus, a, b) in Extra(extra)) overrides[locus] = new[] { a, b };
                    var result = resolver.ResolveForPresentation(horse, testMatrix, "reference-assets.v1", "canonical-anatomy.v1");
                    Assert.Equal(ResolutionStatus.Supported, result.Status); var d = result.Descriptor!;
                    examples.Add(new {
                        fixtureId = classId + "." + age.ToString().ToLowerInvariant(), matrixClassId = classId, visualAge = age.ToString(), displayLabel = result.DisplayLabel,
                        genotypeOverrides = overrides,
                        descriptor = new { horseId = d.HorseId.ToString(), d.PhenotypeInputHash, d.ResolverVersion, d.CoatClassId, d.RequiredCoatClasses,
                            d.VisualCapabilityVersion, d.VisualAssetCatalogVersion, d.MeshFamilyId, d.AnatomicalMaskSetId, d.MorphWeights, d.CoatLayers,
                            d.CoatRecipe, d.SkinRecipe, d.EyeRecipe, d.HairRecipe, d.GrayAmount, d.PrimitiveMarkingStrength, d.LpPatternPresent,
                            d.Patn1Coverage, d.MarkingRealizationKey, d.CosmeticLayer, d.CacheKey }
                    });
                }
            }
            var payload = new { formatVersion = "cloverbrook.visual-fixtures.v1", evidence = "headless_reference_only", renderingStatus = "not_rendered",
                notForLiveStock = true, resolverVersion = resolver.Version, traitModelVersion = GameTraitResolver.Version,
                note = "13 phase-1 and 6 D-026 class references at adult and foal ages. Matrix promotion exists only in test memory. Asset IDs are requirements, not available meshes. Use for neutral/stable/portrait contact-sheet implementation; no visuals approved.",
                fixtures = examples };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var parsed = JsonDocument.Parse(json); Assert.Equal(38, parsed.RootElement.GetProperty("fixtures").GetArrayLength());
            Assert.True(parsed.RootElement.GetProperty("notForLiveStock").GetBoolean());
            Assert.DoesNotContain(PhenotypeTests.Matrix().Classes.Values, c => c.Release == SupportLevel.Released && !expected.Contains(c.Id));
            var directory = Path.Combine(AppContext.BaseDirectory, "TestResults"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "visual-descriptor-fixtures.json"), json);
        }
    }
}
