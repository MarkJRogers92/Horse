using System.Security.Cryptography;
using System.Text.Json;
using Cloverbrook.Domain;

var root = FindRepositoryRoot();
var outputPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "game/data/fixtures/appearance-studio.json");
var matrixPath = Path.Combine(root, "docs/contracts/COAT_SUPPORT_MATRIX.json");
var matrix = LoadArtTestMatrix(matrixPath);
var resolver = new PhenotypeResolver();
var presets = new[]
{
    new Preset("light_racer", "Light · athletic", "chestnut", "palomino", -850, -850, 850, -850),
    new Preset("light_endurance", "Light · long-stride", "bay", "buckskin", 850, -850, 850, 0),
    new Preset("stock_muscular", "Stock · muscular", "black", "smoky_black", -850, 850, -850, 850),
    new Preset("stock_cob", "Stock · cob-influenced", "red_dun", "bay_dun", 0, 850, 0, 850),
};

var fixtureRows = new List<object>();
var metadataRows = new List<object>();
foreach (var preset in presets)
{
    var presetRows = new List<object>();
    foreach (var coat in new[] { preset.FirstCoat, preset.SecondCoat })
    {
        var horse = MakeHorse(preset, coat);
        var resolution = resolver.ResolveForPresentation(horse, matrix, "reference-assets.v1", "canonical-anatomy.v1");
        if (resolution.Status != ResolutionStatus.Supported || resolution.Descriptor == null)
            throw new InvalidOperationException($"Expected the authored phase-one art-test fixture to resolve: {preset.Id}/{coat}: {string.Join("; ", resolution.Reasons)}");

        var descriptor = resolution.Descriptor;
        var fixtureId = preset.Id + "." + coat;
        fixtureRows.Add(new
        {
            fixtureId,
            displayLabel = preset.Label + " · " + resolution.DisplayLabel,
            presetId = preset.Id,
            coatClassId = descriptor.CoatClassId,
            authoredInput = new
            {
                source = "production-fixtures tool; not a save or offspring",
                genome = new { coatClass = coat, gameMarkers = new { preset.BodyLength, preset.ChestWidth, preset.NeckLength, preset.Muscle } },
                developmentalFactors = "all zero (fixed test input)",
                appearanceSeed = horse.AppearanceSeed.ToString(),
                horseId = horse.HorseId.ToString()
            },
            descriptor = new
            {
                horseId = descriptor.HorseId.ToString(),
                descriptor.ResolverVersion,
                descriptor.CoatClassId,
                descriptor.RequiredCoatClasses,
                descriptor.VisualCapabilityVersion,
                descriptor.VisualAssetCatalogVersion,
                descriptor.MeshFamilyId,
                descriptor.AnatomicalMaskSetId,
                descriptor.MorphWeights,
                descriptor.CoatLayers,
                descriptor.CoatRecipe,
                descriptor.SkinRecipe,
                descriptor.EyeRecipe,
                descriptor.HairRecipe,
                descriptor.GrayAmount,
                descriptor.PrimitiveMarkingStrength,
                descriptor.LpPatternPresent,
                descriptor.Patn1Coverage,
                descriptor.MarkingRealizationKey,
                descriptor.CosmeticLayer,
                descriptor.CacheKey
            }
        });
        presetRows.Add(new { fixtureId, coatClass = descriptor.CoatClassId, meshFamily = descriptor.MeshFamilyId, descriptor.MarkingRealizationKey });
    }
    metadataRows.Add(new { id = preset.Id, label = preset.Label, examples = presetRows });
}

var document = new
{
    formatVersion = "cloverbrook.visual-fixtures.v1",
    evidence = "canonical_resolver_art_test_only",
    renderingStatus = "generated_not_rendered",
    notForLiveStock = true,
    resolverVersion = resolver.Version,
    traitModelVersion = GameTraitResolver.Version,
    matrixVersion = matrix.Version,
    note = "Deterministic art-test examples generated through the canonical genetics and phenotype resolver. Four authored body profiles × two supported coat classes; synthetic game-marker inputs and zero developmental factors are disclosed per fixture. These are not player saves, breed records, or offspring, and the in-memory phase-one matrix promotion does not change release status.",
    presets = metadataRows,
    fixtures = fixtureRows
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"Wrote {fixtureRows.Count} canonical test-only descriptors across {presets.Length} presets: {outputPath}");

static CoatSupportMatrix LoadArtTestMatrix(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var root = document.RootElement;
    var layers = root.GetProperty("layers").EnumerateArray().Select(layer => layer.GetProperty("id").GetString()!).ToArray();
    var classes = root.GetProperty("coatClasses").EnumerateArray().Select(row =>
    {
        bool artTestRelease = row.GetProperty("phase").GetInt32() == 1;
        SupportLevel Parse(string property) => Enum.Parse<SupportLevel>(row.GetProperty(property).GetString()!, true);
        var genetic = artTestRelease ? SupportLevel.Released : Parse("geneticStatus");
        var visual = artTestRelease ? SupportLevel.Released : Parse("visualStatus");
        var release = artTestRelease ? SupportLevel.Released : Parse("releaseStatus");
        return new CoatClassSupport(row.GetProperty("id").GetString()!, row.GetProperty("layers").EnumerateArray().Select(layer => layer.GetString()!), genetic, visual, release);
    }).ToArray();
    return new CoatSupportMatrix(root.GetProperty("formatVersion").GetString()! + ".appearance-studio-test-only", layers, classes);
}

static HorseRecord MakeHorse(Preset preset, string coat)
{
    var calls = new Dictionary<string, (string A, string B)>(StringComparer.Ordinal)
    {
        ["mc1r"] = ("E", "E"), ["asip"] = ("A", "A"), ["slc45a2"] = ("N", "N"), ["tbx3"] = ("nd2", "nd2"),
        ["pmel"] = ("N", "N"), ["slc36a1"] = ("N", "N"), ["stx17"] = ("N", "N"),
        ["trpm1"] = ("N", "N"), ["patn1"] = ("N", "N"),
        ["pattern.eca3"] = ("baseline", "baseline"), ["pattern.mitf"] = ("baseline", "baseline"),
        ["pattern.pax3"] = ("baseline", "baseline"), ["pattern.ednrb"] = ("baseline", "baseline")
    };
    if (coat is "chestnut" or "palomino" or "cremello" or "red_dun") calls["mc1r"] = ("e", "e");
    if (coat is "black" or "smoky_black" or "grullo") calls["asip"] = ("a", "a");
    if (coat is "palomino" or "buckskin" or "smoky_black") calls["slc45a2"] = ("N", "Cr");
    if (coat is "cremello" or "perlino" or "smoky_cream") calls["slc45a2"] = ("Cr", "Cr");
    if (coat is "red_dun" or "bay_dun" or "grullo") calls["tbx3"] = ("D", "nd2");

    for (var i = 0; i < 32; i++) calls["game.marker." + i.ToString("D2")] = ("0", "0");
    ApplyTrait("body_length", preset.BodyLength);
    ApplyTrait("chest_width", preset.ChestWidth);
    ApplyTrait("neck_length", preset.NeckLength);
    ApplyTrait("muscle", preset.Muscle);

    var blocks = CoreGeneticsCatalog.Model.Blocks.Values.Select(block => new PhasedBlockPair(block.BlockId, block.IsSynthetic,
        block.Loci.Select(locus => locus.LocusId), block.Loci.Select(locus => calls[locus.LocusId].A), block.Loci.Select(locus => calls[locus.LocusId].B)));
    var genome = new GenomeSnapshot(blocks, CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.SyntheticVersion);
    var name = preset.Label + " / " + coat;
    return new HorseRecord(new HorseId(DeterministicGuid("horse-appearance-studio/" + preset.Id + "/" + coat)), name, new Parentage(), genome,
        CoreGeneticsCatalog.ResolverVersion, SeedFor("appearance-studio/markings/" + preset.Id + "/" + coat),
        new OwnerId(Guid.Parse("a8110000-0000-4000-8000-000000000001")), new LifeState(LifeStage.Adult, 12, VisualAge.Adult),
        new ReproductionState(ReproductiveRole.NonBreeding), QuantizedVector.Empty, originRecord: "appearance-studio-test-only");

    void ApplyTrait(string trait, int value)
    {
        int first = trait switch { "body_length" => 2, "chest_width" => 4, "neck_length" => 6, "muscle" => 8, _ => throw new ArgumentOutOfRangeException(nameof(trait)) };
        var allelePair = value switch { < 0 => ("0", "0"), > 0 => ("1", "1"), _ => ("0", "1") };
        calls["game.marker." + first.ToString("D2")] = allelePair;
        calls["game.marker." + (first + 1).ToString("D2")] = allelePair;
    }
}

static Seed128 SeedFor(string source) => Seed128.Parse(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).ToLowerInvariant()[..32]);
static Guid DeterministicGuid(string source) => new(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))[..16]);
static string FindRepositoryRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "STATUS.md"))) return directory.FullName;
    throw new DirectoryNotFoundException("Run this tool from inside the Horse repository.");
}

sealed record Preset(string Id, string Label, string FirstCoat, string SecondCoat, int BodyLength, int ChestWidth, int NeckLength, int Muscle);
