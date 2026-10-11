using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>
    /// Derived view of each committed offspring's single current stage obligation.
    /// This is not a facility occupancy ledger or a new reservation transaction.
    /// </summary>
    public sealed class StableLifecycleCapacityObligation
    {
        internal StableLifecycleCapacityObligation(BreedingCommitId breedingEventId, HorseId offspringHorseId,
            CapacityReservationId reservationId, CapacityStage stage, bool usesConfirmedReservation, bool countsAsAdultHorse)
        {
            BreedingEventId = breedingEventId; OffspringHorseId = offspringHorseId; ReservationId = reservationId; Stage = stage;
            UsesConfirmedReservation = usesConfirmedReservation; CountsAsAdultHorse = countsAsAdultHorse;
        }
        public BreedingCommitId BreedingEventId { get; }
        public HorseId OffspringHorseId { get; }
        public CapacityReservationId ReservationId { get; }
        public CapacityStage Stage { get; }
        public bool UsesConfirmedReservation { get; }
        public bool CountsAsAdultHorse { get; }
    }

    public sealed class StableLifecycleCapacityProjection
    {
        internal StableLifecycleCapacityProjection(IEnumerable<StableLifecycleCapacityObligation> obligations)
        { Obligations = Snapshot.List(obligations); }
        public IReadOnlyList<StableLifecycleCapacityObligation> Obligations { get; }
        public static StableLifecycleCapacityProjection From(StableSnapshot snapshot)
        {
            Check.NotNull(snapshot, nameof(snapshot));
            var horses = snapshot.Horses.ToDictionary(h => h.HorseId);
            return new StableLifecycleCapacityProjection(snapshot.Conceptions.Select(conception =>
            {
                var foal = horses[conception.Commit.OffspringHorseId];
                var stage = foal.LifeState.Stage switch
                {
                    LifeStage.Unborn => CapacityStage.Pregnancy,
                    LifeStage.Foal => CapacityStage.Foal,
                    LifeStage.Youngster => CapacityStage.Youngster,
                    LifeStage.Adult => CapacityStage.Adult,
                    _ => throw new SaveIntegrityException("An offspring has an unsupported lifecycle stage.")
                };
                var adult = stage == CapacityStage.Adult;
                return new StableLifecycleCapacityObligation(conception.Commit.BreedingCommitId, foal.HorseId,
                    conception.Capacity.ReservationId, stage, !adult, adult);
            }));
        }
    }
}
