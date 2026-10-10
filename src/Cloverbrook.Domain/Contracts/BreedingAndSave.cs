using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    // Immutable transaction RESULT contract. It performs no debit, reservation,
    // save write or RNG advancement. Those must be atomic in the later command layer.
    public sealed class BreedingCommit
    {
        public BreedingCommit(BreedingCommitId id, HorseRecord parentA, HorseRecord parentB, HorseRecord offspring,
            GameteSnapshot gameteA, GameteSnapshot gameteB, Seed128 breedingSeed, ulong breedingEventIndex,
            int conceptionDay, int plannedBirthDay, string calendarProfileVersion,
            IEnumerable<CapacityReservationId> reservationIds, TransactionId costTransactionId,
            string compatibilityRuleVersion, ForecastId? forecastReference = null)
        {
            BreedingCommitId = Check.NotNull(id, nameof(id)); Check.NotNull(parentA, nameof(parentA)); Check.NotNull(parentB, nameof(parentB)); Check.NotNull(offspring, nameof(offspring));
            GameteA = Check.NotNull(gameteA, nameof(gameteA)); GameteB = Check.NotNull(gameteB, nameof(gameteB));
            if (parentA.HorseId.Equals(parentB.HorseId) || offspring.HorseId.Equals(parentA.HorseId) || offspring.HorseId.Equals(parentB.HorseId)) throw new ArgumentException("A breeding event requires three distinct horse identities.");
            if (!parentA.HorseId.Equals(gameteA.ParentId) || !parentB.HorseId.Equals(gameteB.ParentId)) throw new ArgumentException("Gamete parent identity mismatch.");
            if (!parentA.HorseId.Equals(offspring.Parentage.ParentAId) || !parentB.HorseId.Equals(offspring.Parentage.ParentBId)) throw new ArgumentException("Offspring parentage mismatch.");
            if (offspring.Genome.GenomeHash != GenomeSnapshot.FromGametes(gameteA, gameteB).GenomeHash) throw new ArgumentException("Offspring is not the recorded gamete combination.");
            if (offspring.LifeState.Stage != LifeStage.Unborn || !id.Equals(offspring.OriginCommitId)) throw new ArgumentException("A conception records the existing unborn identity and origin event.");
            if (offspring.AppearanceSeed != Seeds.Derive(breedingSeed, "appearance")) throw new ArgumentException("Appearance must derive from this breeding event seed.");
            ParentAId = parentA.HorseId; ParentBId = parentB.HorseId; ParentAGenomeHash = parentA.Genome.GenomeHash; ParentBGenomeHash = parentB.Genome.GenomeHash;
            ParentARevision = parentA.Revision; ParentBRevision = parentB.Revision;
            ConceptionDay = Check.NonNegative(conceptionDay, nameof(conceptionDay));
            if (plannedBirthDay <= conceptionDay) throw new ArgumentOutOfRangeException(nameof(plannedBirthDay));
            PlannedBirthDay = plannedBirthDay; CalendarProfileVersion = Check.Text(calendarProfileVersion, nameof(calendarProfileVersion));
            InheritanceModelVersion = gameteA.InheritanceModelVersion; OffspringResolverVersion = offspring.ResolverVersion;
            BreedingSeed = breedingSeed; BreedingEventIndex = breedingEventIndex; OffspringHorseId = offspring.HorseId; OffspringGenome = offspring.Genome;
            OffspringDevelopmentalFactors = offspring.DevelopmentalFactors; OffspringAppearanceSeed = offspring.AppearanceSeed;
            ReservationIds = Snapshot.List(reservationIds);
            if (ReservationIds.Count == 0 || ReservationIds.Distinct().Count() != ReservationIds.Count) throw new ArgumentException("Distinct capacity reservations are required.");
            CostTransactionId = Check.NotNull(costTransactionId, nameof(costTransactionId)); CompatibilityRuleVersion = Check.Text(compatibilityRuleVersion, nameof(compatibilityRuleVersion)); ForecastReference = forecastReference;
            CommitHash = Canonical.Hash(new[] { "breeding.commit.v1", id.ToString(), ParentAId.ToString(), ParentBId.ToString(), ParentAGenomeHash, ParentBGenomeHash,
                ParentARevision.ToString(CultureInfo.InvariantCulture), ParentBRevision.ToString(CultureInfo.InvariantCulture), conceptionDay.ToString(CultureInfo.InvariantCulture),
                plannedBirthDay.ToString(CultureInfo.InvariantCulture), CalendarProfileVersion, InheritanceModelVersion, OffspringResolverVersion,
                breedingEventIndex.ToString(CultureInfo.InvariantCulture), breedingSeed.ToString(), gameteA.ProvenanceHash, gameteB.ProvenanceHash,
                OffspringHorseId.ToString(), OffspringGenome.GenomeHash, OffspringDevelopmentalFactors.Hash, OffspringAppearanceSeed.ToString(),
                CostTransactionId.ToString(), CompatibilityRuleVersion, forecastReference?.ToString() ?? "" }.Concat(ReservationIds.Select(r => r.ToString())));
        }
        public BreedingCommitId BreedingCommitId { get; }
        public HorseId ParentAId { get; }
        public HorseId ParentBId { get; }
        public string ParentAGenomeHash { get; }
        public string ParentBGenomeHash { get; }
        public ulong ParentARevision { get; }
        public ulong ParentBRevision { get; }
        public int ConceptionDay { get; }
        public int PlannedBirthDay { get; }
        public string CalendarProfileVersion { get; }
        public string InheritanceModelVersion { get; }
        public string OffspringResolverVersion { get; }
        public ulong BreedingEventIndex { get; }
        public Seed128 BreedingSeed { get; }
        public GameteSnapshot GameteA { get; }
        public GameteSnapshot GameteB { get; }
        public HorseId OffspringHorseId { get; }
        public GenomeSnapshot OffspringGenome { get; }
        public QuantizedVector OffspringDevelopmentalFactors { get; }
        public Seed128 OffspringAppearanceSeed { get; }
        public IReadOnlyList<CapacityReservationId> ReservationIds { get; }
        public TransactionId CostTransactionId { get; }
        public string CompatibilityRuleVersion { get; }
        public ForecastId? ForecastReference { get; }
        public string CommitHash { get; }
    }

    public enum SaveMode { Standard, Relaxed, Sandbox }
    public enum CareMode { Standard, Gentle }
    public sealed class SaveHeader
    {
        public SaveHeader(SaveId saveId, SaveMode saveMode, string schemaVersion, string gameBuildVersion, ulong saveRevision,
            int calendarDay, string calendarProfileVersion, CareMode careMode, string activeVariantCatalogVersion,
            string activeInheritanceModelVersion, string defaultOffspringResolverVersion, IEnumerable<string> retainedResolverVersions,
            RngStreams rngStreams, ulong lastCommittedBreedingEventIndex, IEnumerable<string> migrationHistory, string payloadHash)
        {
            SaveId = Check.NotNull(saveId, nameof(saveId)); SaveMode = saveMode; CareMode = careMode;
            if (!Enum.IsDefined(typeof(SaveMode), saveMode) || !Enum.IsDefined(typeof(CareMode), careMode)) throw new ArgumentOutOfRangeException(nameof(saveMode));
            SchemaVersion = Check.Text(schemaVersion, nameof(schemaVersion)); GameBuildVersion = Check.Text(gameBuildVersion, nameof(gameBuildVersion));
            SaveRevision = saveRevision; CalendarDay = Check.NonNegative(calendarDay, nameof(calendarDay)); CalendarProfileVersion = Check.Text(calendarProfileVersion, nameof(calendarProfileVersion));
            ActiveVariantCatalogVersion = Check.Text(activeVariantCatalogVersion, nameof(activeVariantCatalogVersion)); ActiveInheritanceModelVersion = Check.Text(activeInheritanceModelVersion, nameof(activeInheritanceModelVersion));
            DefaultOffspringResolverVersion = Check.Text(defaultOffspringResolverVersion, nameof(defaultOffspringResolverVersion));
            RetainedResolverVersions = Snapshot.List(Check.NotNull(retainedResolverVersions, nameof(retainedResolverVersions)).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal));
            foreach (var version in RetainedResolverVersions) Check.Text(version, nameof(retainedResolverVersions));
            if (!RetainedResolverVersions.Contains(defaultOffspringResolverVersion)) throw new ArgumentException("The default resolver must be retained.");
            RngStreams = Check.NotNull(rngStreams, nameof(rngStreams)); LastCommittedBreedingEventIndex = lastCommittedBreedingEventIndex;
            MigrationHistory = Snapshot.List(migrationHistory); PayloadHash = Check.Text(payloadHash, nameof(payloadHash));
        }
        public SaveId SaveId { get; }
        public SaveMode SaveMode { get; }
        public string SchemaVersion { get; }
        public string GameBuildVersion { get; }
        public ulong SaveRevision { get; }
        public int CalendarDay { get; }
        public string CalendarProfileVersion { get; }
        public CareMode CareMode { get; }
        public string ActiveVariantCatalogVersion { get; }
        public string ActiveInheritanceModelVersion { get; }
        public string DefaultOffspringResolverVersion { get; }
        public IReadOnlyList<string> RetainedResolverVersions { get; }
        public RngStreams RngStreams { get; }
        public ulong LastCommittedBreedingEventIndex { get; }
        public IReadOnlyList<string> MigrationHistory { get; }
        public string PayloadHash { get; }
    }
}
