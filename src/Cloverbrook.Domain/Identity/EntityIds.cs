using System;

namespace Cloverbrook.Domain
{
    // Reference value objects avoid the invalid default(Guid) state of structs.
    // Equality includes the concrete ID type; an OwnerId is never a HorseId.
    public abstract class EntityId : IEquatable<EntityId>
    {
        protected EntityId(Guid value)
        {
            if (value == Guid.Empty) throw new ArgumentException("An identity cannot be empty.", nameof(value));
            Value = value;
        }
        public Guid Value { get; }
        public bool Equals(EntityId? other) => other != null && GetType() == other.GetType() && Value == other.Value;
        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(GetType(), Value);
        public override string ToString() => Value.ToString("D");
    }
    public sealed class HorseId : EntityId { public HorseId(Guid value) : base(value) { } }
    public sealed class OwnerId : EntityId { public OwnerId(Guid value) : base(value) { } }
    public sealed class BreedingCommitId : EntityId { public BreedingCommitId(Guid value) : base(value) { } }
    public sealed class SaveId : EntityId { public SaveId(Guid value) : base(value) { } }
    public sealed class TransactionId : EntityId { public TransactionId(Guid value) : base(value) { } }
    public sealed class CapacityReservationId : EntityId { public CapacityReservationId(Guid value) : base(value) { } }
    public sealed class ForecastId : EntityId { public ForecastId(Guid value) : base(value) { } }
}
