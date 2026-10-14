using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>D-040: owned horses can rest in cooperative pasture without using an active home place.</summary>
    public static class HorsePasture
    {
        /// <summary>Every active-home horse counts once, including unborn foals, plus unconsumed holds.</summary>
        public static int ActivePlacesUsed(StableSnapshot state) => Check.NotNull(state, nameof(state)).Horses
            .Count(h => h.OwnerId.Equals(state.PlayerOwnerId) && h.HomeAssignment == HomeKind.ActiveStable)
            + state.PendingCapacityPlans.Count;

        /// <summary>Why a requested destination is unavailable, or null (including an unchanged destination).</summary>
        public static string? BlockReason(StableSnapshot state, HorseId horseId, HomeKind destination)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            if (destination != HomeKind.ActiveStable && destination != HomeKind.CooperativePasture)
                return "choose the active stable or cooperative pasture.";
            var horse = state.Horses.SingleOrDefault(h => h.HorseId.Equals(horseId));
            if (horse == null || !horse.OwnerId.Equals(state.PlayerOwnerId) || HorseMarket.IsSold(horse))
                return "only a horse you own can move between the stable and pasture.";
            if (horse.HomeAssignment != HomeKind.ActiveStable && horse.HomeAssignment != HomeKind.CooperativePasture)
                return "this horse must finish its current placement before moving to pasture or the stable.";
            if (horse.HomeAssignment == destination) return null;
            if (state.Header.SaveRevision == ulong.MaxValue || horse.Revision == ulong.MaxValue || state.CapacityRevision == ulong.MaxValue)
                return "the saved record revisions cannot advance beyond their supported range.";
            if (destination == HomeKind.CooperativePasture) return BoardingProblem(state, horse);
            return ActivePlacesUsed(state) >= StableShop.Stalls(state)
                ? "every stall is occupied or reserved; make room before bringing this horse home." : null;
        }

        // Also checked by save validation, so an unsafe pasture assignment cannot be loaded.
        internal static string? BoardingProblem(StableSnapshot state, HorseRecord horse)
        {
            if (horse.LifeState.Stage != LifeStage.Adult) return "only adult horses can board in pasture.";
            if (horse.ReproductionState.PregnancyId != null) return "a mare expecting a foal needs to stay at the active stable.";
            if (state.Header.CalendarDay < horse.ReproductionState.RecoveryUntilDay) return "this horse is still recovering and needs to stay at the active stable.";
            if (horse.ReproductionState.Role == ReproductiveRole.Mare && state.Horses.Any(child =>
                child.OwnerId.Equals(state.PlayerOwnerId) && child.LifeState.Stage != LifeStage.Adult
                && (horse.HorseId.Equals(child.Parentage.ParentAId) || horse.HorseId.Equals(child.Parentage.ParentBId))))
                return "a mare with a young horse needs to stay at the active stable until her offspring is adult.";
            return null;
        }
    }
}
