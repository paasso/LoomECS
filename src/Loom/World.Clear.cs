using System;
using Loom.Internal;

namespace Loom
{
    public sealed partial class World
    {
        /// <summary>
        /// Destroys every entity and clears sparse/shared/relation side tables, recycling entity ids.
        /// Keeps archetype pages and component registries pooled for the next round.
        /// Does not touch systems or events — those live on <see cref="Runtime"/>.
        /// </summary>
        /// <param name="clearSingletons">When true, also removes all singleton values.</param>
        public void ClearEntities(bool clearSingletons = false)
        {
            ClearEntityStorage(recycleIds: true, clearSingletons);
        }

        /// <summary>
        /// Destroys every entity and returns the world to a pristine entity-id state
        /// (<see cref="IsPristine"/>) suitable for <see cref="WorldSerializer"/> load and net
        /// snapshot apply. Keeps archetype pages and component type registrations.
        /// Does not touch systems or events — those live on <see cref="Runtime"/>.
        /// </summary>
        /// <param name="clearSingletons">When true, also removes all singleton values.</param>
        public void Reset(bool clearSingletons = true)
        {
            ClearEntityStorage(recycleIds: false, clearSingletons);
        }

        private void ClearEntityStorage(bool recycleIds, bool clearSingletons)
        {
            ClearComponentChanges();

            for (int i = 0; i < _archetypes.Count; i++)
                _archetypes[i].ClearAllRows();

            _sparseMasks.Clear();
            _relations.Clear();

            foreach (var set in _sparseSetsByComponentId.Values)
                set.Clear();
            foreach (var store in _sharedStoresByComponentId.Values)
                store.Clear();

            _freeIds.Clear();
            for (int id = 1; id < _nextId; id++)
            {
                ref var rec = ref _records[id];
                if (recycleIds)
                {
                    if (rec.IsAlive || rec.Version != 0 || rec.Archetype != null)
                    {
                        rec.IsAlive = false;
                        rec.Archetype = null;
                        rec.Row = 0;
                        rec.Version++;
                    }

                    _freeIds.Push(id);
                }
                else
                {
                    rec.IsAlive = false;
                    rec.Archetype = null;
                    rec.Row = 0;
                    rec.Version = 0;
                }
            }

            _liveCount = 0;
            if (!recycleIds)
                _nextId = 1;

            if (clearSingletons)
            {
                // World.Singletons is a partial; clear via known dictionary.
                ClearSingletons();
            }
        }

        /// <summary>Enabled when the entity does not have <see cref="Disabled"/>.</summary>
        public bool IsEnabled(Entity entity) => IsAlive(entity) && !Has<Disabled>(entity);

        /// <summary>Adds or removes the <see cref="Disabled"/> sparse tag.</summary>
        public void SetEnabled(Entity entity, bool enabled)
        {
            RequireAlive(entity);
            if (enabled)
            {
                if (Has<Disabled>(entity))
                    Remove<Disabled>(entity);
            }
            else if (!Has<Disabled>(entity))
            {
                Add<Disabled>(entity);
            }
        }
    }
}
