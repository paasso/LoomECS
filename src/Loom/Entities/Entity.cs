using System;

namespace Loom.Entities
{
    /// <summary>
    /// Handle to an entity: a recycled slot index plus a generation ("Version") that is
    /// bumped on every destroy, so stale handles into a reused slot compare unequal and fail IsAlive checks.
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Id;
        public readonly int Version;

        internal Entity(int id, int version)
        {
            Id = id;
            Version = version;
        }

        /// <summary>Id 0 is permanently reserved as "no entity" — <see cref="World"/> never hands
        /// it out from Create(), so it doubles as the default(Entity) value and as a safe sentinel
        /// (e.g. an empty archetype slot) without needing a negative id.</summary>
        public static readonly Entity Null = default;

        public bool IsNull => Id == 0;

        public bool Equals(Entity other) => Id == other.Id && Version == other.Version;

        public override bool Equals(object obj) => obj is Entity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Id * 397) ^ Version;
            }
        }

        public static bool operator ==(Entity a, Entity b) => a.Equals(b);
        public static bool operator !=(Entity a, Entity b) => !a.Equals(b);

        public override string ToString() => IsNull ? "Entity.Null" : $"Entity({Id}:{Version})";
    }
}
