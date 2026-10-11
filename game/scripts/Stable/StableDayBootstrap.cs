using System;
using System.Linq;
using Cloverbrook.Domain;

namespace Cloverbrook.Game.Stable;

/// <summary>Creates the two first-play horses using only the canonical genetics catalog.</summary>
internal static class StableDayBootstrap
{
    public static StableSnapshot Create(StableDayService service)
    {
        var owner = new OwnerId(Guid.NewGuid());
        var seedBytes = Guid.NewGuid().ToByteArray();
        var seed = new Seed128(BitConverter.ToUInt64(seedBytes, 0), BitConverter.ToUInt64(seedBytes, 8));
        var genome = new GenomeSnapshot(CoreGeneticsCatalog.Model.Blocks.Values.Select(block =>
        {
            var defaults = block.Loci.Select(locus => locus.Variant.SupportedAlleleIds[0]).ToArray();
            return new PhasedBlockPair(block.BlockId, block.IsSynthetic, block.Loci.Select(locus => locus.LocusId), defaults, defaults);
        }), CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.SyntheticVersion);

        var horses = new[]
        {
            StarterHorse("Juniper", owner, genome, new Seed128(seed.High, seed.Low ^ 0x4a554e49504552UL), ReproductiveRole.Mare),
            StarterHorse("Maple", owner, genome, new Seed128(seed.High ^ 0x4d41504c45UL, seed.Low), ReproductiveRole.Stallion)
        };
        var header = new SaveHeader(new SaveId(Guid.NewGuid()), SaveMode.Sandbox, StableSnapshot.SchemaVersion,
            "stable-management-prototype", 0, 0, service.CalendarProfileVersion, CareMode.Standard,
            CoreGeneticsCatalog.CatalogVersion, CoreGeneticsCatalog.InheritanceVersion, CoreGeneticsCatalog.ResolverVersion,
            new[] { CoreGeneticsCatalog.ResolverVersion }, RngStreams.FromSeed(seed), 0,
            new[] { StableBreedingPlanner.StarterMigrationMarker }, "unwritten");
        return new StableSnapshot(header, owner, horses, openingCoinBalance: 500, capacityRevision: 0);
    }

    private static HorseRecord StarterHorse(string name, OwnerId owner, GenomeSnapshot genome, Seed128 appearanceSeed, ReproductiveRole role) =>
        new HorseRecord(new HorseId(Guid.NewGuid()), name, new Parentage(), genome, CoreGeneticsCatalog.ResolverVersion,
            appearanceSeed, owner, new LifeState(LifeStage.Adult, 12, VisualAge.Adult),
            new ReproductionState(role), homeAssignment: HomeKind.ActiveStable,
            originRecord: "stable-day-starter");
}
