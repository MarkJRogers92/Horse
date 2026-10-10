using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public enum LifeStage { Unborn, Foal, Youngster, Adult }
    public enum VisualAge { Foal, Adult, Senior }
    public enum ReproductiveRole { Mare, Stallion, NonBreeding }
    public enum HomeKind { ActiveStable, CooperativePasture, InstitutionalLease }

    public sealed class QuantizedVector
    {
        public QuantizedVector(IEnumerable<KeyValuePair<string, int>> values)
        {
            Values = Snapshot.Map(values);
            if (Values.Values.Any(v => v < -10000 || v > 10000)) throw new ArgumentOutOfRangeException(nameof(values), "Quantized values are bounded to [-10000,10000].");
            Hash = Canonical.Hash(new[] { "quantized.vector.v1" }.Concat(Values.SelectMany(p => new[] { p.Key, p.Value.ToString(CultureInfo.InvariantCulture) })));
        }
        public IReadOnlyDictionary<string, int> Values { get; }
        public string Hash { get; }
        public int Get(string key, int fallback = 0) => Values.TryGetValue(key, out var value) ? value : fallback;
        public static QuantizedVector Empty { get; } = new QuantizedVector(Array.Empty<KeyValuePair<string, int>>());
    }

    public sealed class FounderBackground
    {
        public FounderBackground(string assumptionId, Rational inbreedingCoefficient)
        {
            AssumptionId = Check.Text(assumptionId, nameof(assumptionId));
            if (inbreedingCoefficient < Rational.Zero || inbreedingCoefficient > Rational.One) throw new ArgumentOutOfRangeException(nameof(inbreedingCoefficient));
            InbreedingCoefficient = inbreedingCoefficient;
        }
        // Sharing an assumption ID explicitly declares mutually unrelated founder
        // backgrounds, while retaining each founder's own inbreeding coefficient.
        public string AssumptionId { get; }
        public Rational InbreedingCoefficient { get; }
    }

    public sealed class Parentage
    {
        public Parentage(HorseId? parentAId = null, HorseId? parentBId = null, FounderBackground? founderBackground = null)
        {
            if (parentAId != null && parentAId.Equals(parentBId)) throw new ArgumentException("Parents must be distinct.");
            if (founderBackground != null && (parentAId != null || parentBId != null)) throw new ArgumentException("Founder background applies only at a recorded pedigree root.");
            ParentAId = parentAId; ParentBId = parentBId; FounderBackground = founderBackground;
        }
        public HorseId? ParentAId { get; }
        public HorseId? ParentBId { get; }
        public FounderBackground? FounderBackground { get; }
    }

    public sealed class LifeState
    {
        public LifeState(LifeStage stage, int daysSinceConception, VisualAge visualAge)
        {
            if (!Enum.IsDefined(typeof(LifeStage), stage) || !Enum.IsDefined(typeof(VisualAge), visualAge)) throw new ArgumentOutOfRangeException(nameof(stage));
            Stage = stage; DaysSinceConception = Check.NonNegative(daysSinceConception, nameof(daysSinceConception)); VisualAge = visualAge;
        }
        public LifeStage Stage { get; }
        public int DaysSinceConception { get; }
        public VisualAge VisualAge { get; }
    }

    public sealed class ReproductionState
    {
        public ReproductionState(ReproductiveRole role, BreedingCommitId? pregnancyId = null, int recoveryUntilDay = 0)
        {
            if (!Enum.IsDefined(typeof(ReproductiveRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (pregnancyId != null && role != ReproductiveRole.Mare) throw new ArgumentException("Only a mare can have a pending pregnancy.");
            Role = role; PregnancyId = pregnancyId; RecoveryUntilDay = Check.NonNegative(recoveryUntilDay, nameof(recoveryUntilDay));
        }
        public ReproductiveRole Role { get; }
        public BreedingCommitId? PregnancyId { get; }
        public int RecoveryUntilDay { get; }
    }

    public sealed class PlayerKnowledgeRecord
    {
        public PlayerKnowledgeRecord(IEnumerable<KeyValuePair<string, AllelePair>> revealedCalls, IEnumerable<HorseId> knownAncestors)
        {
            RevealedCalls = Snapshot.Map(revealedCalls); KnownAncestors = Snapshot.List(knownAncestors);
            KnowledgeHash = Canonical.Hash(new[] { "knowledge.v1" }.Concat(RevealedCalls.SelectMany(p => new[] { p.Key, p.Value.A, p.Value.B }))
                .Concat(new[] { "ancestors" }).Concat(KnownAncestors.Select(id => id.ToString()).OrderBy(s => s, StringComparer.Ordinal)));
        }
        public IReadOnlyDictionary<string, AllelePair> RevealedCalls { get; }
        public IReadOnlyList<HorseId> KnownAncestors { get; }
        public string KnowledgeHash { get; }
        public static PlayerKnowledgeRecord Unknown { get; } = new PlayerKnowledgeRecord(Array.Empty<KeyValuePair<string, AllelePair>>(), Array.Empty<HorseId>());
    }

    public sealed class HorseRecord
    {
        public HorseRecord(HorseId horseId, string name, Parentage parentage, GenomeSnapshot genome, string resolverVersion,
            Seed128 appearanceSeed, OwnerId ownerId, LifeState lifeState, ReproductionState reproductionState,
            QuantizedVector? developmentalFactors = null, QuantizedVector? inheritedGamePotential = null,
            QuantizedVector? trainingState = null, PlayerKnowledgeRecord? playerKnowledge = null,
            IEnumerable<KeyValuePair<string, string>>? cosmeticState = null, HomeKind homeAssignment = HomeKind.ActiveStable,
            string originRecord = "founder", BreedingCommitId? originCommitId = null, int? birthDay = null, ulong revision = 0)
        {
            HorseId = Check.NotNull(horseId, nameof(horseId)); Name = Check.Text(name, nameof(name));
            Parentage = Check.NotNull(parentage, nameof(parentage)); Genome = Check.NotNull(genome, nameof(genome));
            if (HorseId.Equals(parentage.ParentAId) || HorseId.Equals(parentage.ParentBId)) throw new ArgumentException("A horse cannot parent itself.");
            ResolverVersion = Check.Text(resolverVersion, nameof(resolverVersion)); AppearanceSeed = appearanceSeed;
            OwnerId = Check.NotNull(ownerId, nameof(ownerId)); LifeState = Check.NotNull(lifeState, nameof(lifeState));
            ReproductionState = Check.NotNull(reproductionState, nameof(reproductionState));
            DevelopmentalFactors = developmentalFactors ?? QuantizedVector.Empty; InheritedGamePotential = inheritedGamePotential ?? QuantizedVector.Empty;
            TrainingState = trainingState ?? QuantizedVector.Empty; PlayerKnowledge = playerKnowledge ?? PlayerKnowledgeRecord.Unknown;
            CosmeticState = Snapshot.Map(cosmeticState ?? Array.Empty<KeyValuePair<string, string>>());
            if (!Enum.IsDefined(typeof(HomeKind), homeAssignment)) throw new ArgumentOutOfRangeException(nameof(homeAssignment));
            HomeAssignment = homeAssignment; OriginRecord = Check.Text(originRecord, nameof(originRecord)); OriginCommitId = originCommitId;
            if (birthDay.HasValue && (birthDay.Value < 0 || lifeState.Stage == LifeStage.Unborn)) throw new ArgumentException("Invalid recorded birth.");
            BirthDay = birthDay; Revision = revision;
        }
        public HorseId HorseId { get; }
        public string Name { get; }
        public Parentage Parentage { get; }
        public GenomeSnapshot Genome { get; }
        public string ResolverVersion { get; }
        public Seed128 AppearanceSeed { get; }
        public OwnerId OwnerId { get; }
        public LifeState LifeState { get; }
        public ReproductionState ReproductionState { get; }
        public QuantizedVector DevelopmentalFactors { get; }
        public QuantizedVector InheritedGamePotential { get; }
        public QuantizedVector TrainingState { get; }
        public PlayerKnowledgeRecord PlayerKnowledge { get; }
        public IReadOnlyDictionary<string, string> CosmeticState { get; }
        public HomeKind HomeAssignment { get; }
        public string OriginRecord { get; }
        public BreedingCommitId? OriginCommitId { get; }
        public int? BirthDay { get; }
        public ulong Revision { get; }
        public HorseRecord Rename(string name) => new HorseRecord(HorseId, name, Parentage, Genome, ResolverVersion, AppearanceSeed,
            OwnerId, LifeState, ReproductionState, DevelopmentalFactors, InheritedGamePotential, TrainingState, PlayerKnowledge,
            CosmeticState, HomeAssignment, OriginRecord, OriginCommitId, BirthDay, checked(Revision + 1));
    }
}
