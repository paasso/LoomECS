using System;
using Loom.Internal;

namespace Loom
{
    public sealed partial class World
    {
        /// <summary>True when this world has never created or destroyed an entity, or was returned
        /// to that state via <see cref="Reset"/>. Required by <see cref="WorldSerializer"/> load APIs
        /// so restored ids/versions land in a clean slot table.</summary>
        public bool IsPristine => _nextId == 1 && _liveCount == 0 && _freeIds.Count == 0;

        internal int NextEntityIdExclusive => _nextId;

        internal ComponentTypeRegistry ComponentTypes => _componentTypes;

        internal Archetype? GetArchetype(Entity entity) =>
            IsAlive(entity) ? _records[entity.Id].Archetype : null;

        internal ComponentMask GetSparseMaskForSerialization(int entityId) => GetSparseMask(entityId);

        /// <summary>Ensures <typeparamref name="T"/> is registered in this world and returns its
        /// dense or sparse id. Used by <see cref="WorldSerializer"/> restore.</summary>
        internal int EnsureComponentId<T>() where T : struct =>
            _componentTypes.GetOrRegister<T>().Id;

        internal object? GetDenseBoxed(Entity entity, int denseComponentId)
        {
            var info = _componentTypes.Get(denseComponentId);
            if (info.IsEmpty)
                return null;

            ref var rec = ref _records[entity.Id];
            var archetype = rec.Archetype!;
            var (chunk, index) = archetype.Locate(rec.Row);
            int col = archetype.ColumnIndex(denseComponentId);
            return chunk.Columns[col].GetBoxed(index);
        }

        /// <summary>Writes a boxed dense value into an entity that already sits in an archetype
        /// whose mask includes <paramref name="denseComponentId"/>. Used by snapshot restore after
        /// a single final-archetype placement (avoids N structural moves).</summary>
        internal void SetDenseBoxed(Entity entity, int denseComponentId, object value)
        {
            ref var rec = ref _records[entity.Id];
            var archetype = rec.Archetype!;
            var (chunk, index) = archetype.Locate(rec.Row);
            int col = archetype.ColumnIndex(denseComponentId);
            chunk.Columns[col].SetBoxed(index, value);
        }

        internal bool TryGetSparseBoxed(int entityId, int sparseComponentId, out object? value)
        {
            var info = _componentTypes.GetSparse(sparseComponentId);
            if (info.IsEmpty)
            {
                value = null;
                return GetSparseMask(entityId).Get(sparseComponentId);
            }

            if (info.IsShared)
            {
                if (_sharedStoresByComponentId.TryGetValue(sparseComponentId, out var shared) &&
                    shared.TryGetBoxed(entityId, out value))
                    return true;

                value = null;
                return false;
            }

            if (_sparseSetsByComponentId.TryGetValue(sparseComponentId, out var set) &&
                set.TryGetBoxed(entityId, out var boxed))
            {
                value = boxed;
                return true;
            }

            value = null;
            return false;
        }

        internal Entity GetFatherForSerialization(Entity entity) =>
            _relations.Has(entity.Id) ? _relations.GetRef(entity.Id).Father : Entity.Null;

        /// <summary>Restores a specific id/version into an empty world and places the entity in the
        /// empty archetype. Used by <see cref="WorldSerializer"/> — not a public gameplay API.</summary>
        internal Entity RestoreEntity(int id, int version) =>
            RestoreEntity(id, version, ComponentMask.Empty);

        /// <summary>Restores a specific id/version and places the entity directly into the archetype
        /// for <paramref name="denseMask"/> (one placement, no per-component structural moves).</summary>
        internal Entity RestoreEntity(int id, int version, ComponentMask denseMask)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Entity id must be positive.");
            if (version < 0)
                throw new ArgumentOutOfRangeException(nameof(version));

            EnsureRecordCapacity(id + 1);
            if (id >= _nextId)
                _nextId = id + 1;

            ref var rec = ref _records[id];
            if (rec.IsAlive)
                throw new InvalidOperationException($"Cannot restore {id}: slot is already alive.");

            rec.IsAlive = true;
            rec.Version = version;
            rec.Archetype = null;
            rec.Row = 0;

            var entity = new Entity(id, version);
            var archetype = denseMask.Equals(ComponentMask.Empty)
                ? _emptyArchetype
                : GetOrCreateArchetype(denseMask);
            PlaceInArchetype(entity, archetype);
            _liveCount++;
            return entity;
        }
    }
}
