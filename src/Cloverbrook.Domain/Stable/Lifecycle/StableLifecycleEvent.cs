using System;
using System.Globalization;

namespace Cloverbrook.Domain
{
    public sealed class StableCommandRejectedException : Exception
    {
        public StableCommandRejectedException(string code, string message) : base(message) { Code = Check.Text(code, nameof(code)); }
        public string Code { get; }
    }

    public enum LifecycleEventKind { Birth, FoalToYoungster, YoungsterToAdult }

    /// <summary>A durable, deterministic lifecycle change published with one EndDay receipt.</summary>
    public sealed class StableLifecycleEvent
    {
        public StableLifecycleEvent(LifecycleEventKind kind, HorseId horseId, int effectiveDay,
            LifeStage? fromStage, LifeStage toStage, HorseId? relatedHorseId = null, BreedingCommitId? breedingEventId = null)
        {
            if (!Enum.IsDefined(typeof(LifecycleEventKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind; HorseId = Check.NotNull(horseId, nameof(horseId)); EffectiveDay = Check.NonNegative(effectiveDay, nameof(effectiveDay));
            FromStage = fromStage; ToStage = toStage; RelatedHorseId = relatedHorseId; BreedingEventId = breedingEventId;
            var valid = kind switch
            {
                LifecycleEventKind.Birth => fromStage == LifeStage.Unborn && toStage == LifeStage.Foal && relatedHorseId != null && breedingEventId != null,
                LifecycleEventKind.FoalToYoungster => fromStage == LifeStage.Foal && toStage == LifeStage.Youngster && relatedHorseId == null && breedingEventId == null,
                LifecycleEventKind.YoungsterToAdult => fromStage == LifeStage.Youngster && toStage == LifeStage.Adult && relatedHorseId == null && breedingEventId == null,
                _ => false
            };
            if (!valid) throw new ArgumentException("Lifecycle event fields do not match its transition.");
        }

        public LifecycleEventKind Kind { get; }
        public HorseId HorseId { get; }
        public HorseId? RelatedHorseId { get; }
        public BreedingCommitId? BreedingEventId { get; }
        public LifeStage? FromStage { get; }
        public LifeStage ToStage { get; }
        public int EffectiveDay { get; }
        internal string CanonicalKey => string.Join("|", Kind.ToString(), HorseId.ToString(), RelatedHorseId?.ToString() ?? "",
            BreedingEventId?.ToString() ?? "", FromStage?.ToString() ?? "", ToStage.ToString(), EffectiveDay.ToString(CultureInfo.InvariantCulture));
    }
}
