using System;
using System.Collections.Generic;
using Loom.Internal;

namespace Loom
{
    public sealed partial class World
    {
        private readonly HashSet<int> _relationComponentIds = new HashSet<int>();
        private readonly List<Action<Entity>> _incomingRelationRemovers = new List<Action<Entity>>();

        /// <summary>
        /// Sets (or replaces) a typed link from <paramref name="from"/> to <paramref name="target"/>.
        /// <typeparamref name="T"/> must declare a field named <c>Target</c> of type <see cref="Entity"/>.
        /// </summary>
        public ref T SetRelation<T>(Entity from, Entity target) where T : struct, IRelationComponent
        {
            RequireAlive(from);
            RequireAlive(target);
            EnsureRelationTracked<T>();

            if (Has<T>(from))
                Remove<T>(from);
            return ref Add(from, RelationAccess<T>.Create(target));
        }

        public bool RemoveRelation<T>(Entity from) where T : struct, IRelationComponent
        {
            if (!Has<T>(from))
                return false;
            Remove<T>(from);
            return true;
        }

        public bool HasRelation<T>(Entity from) where T : struct, IRelationComponent => Has<T>(from);

        public bool TryGetRelationTarget<T>(Entity from, out Entity target) where T : struct, IRelationComponent
        {
            if (!Has<T>(from))
            {
                target = Entity.Null;
                return false;
            }

            target = RelationAccess<T>.GetTarget(Get<T>(from));
            return true;
        }

        /// <summary>
        /// Invokes <paramref name="action"/> for every entity that currently links to
        /// <paramref name="target"/> with <typeparamref name="T"/> (scans that relation's sparse set).
        /// </summary>
        public void ForEachRelationSource<T>(Entity target, Action<Entity> action)
            where T : struct, IRelationComponent
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (target.IsNull)
                return;

            EnsureRelationTracked<T>();
            var info = _componentTypes.GetOrRegister<T>();
            if (!_sparseSetsByComponentId.TryGetValue(info.Id, out var setObj))
                return;

            var set = (SparseSet<T>)setObj;
            var ids = set.DenseEntityIds;
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                if (id <= 0 || id >= _nextId || !_records[id].IsAlive)
                    continue;
                if (RelationAccess<T>.GetTarget(in set.GetRef(id)).Id != target.Id)
                    continue;
                action(new Entity(id, _records[id].Version));
            }
        }

        private void EnsureRelationTracked<T>() where T : struct, IRelationComponent
        {
            var info = _componentTypes.GetOrRegister<T>();
            if (!_relationComponentIds.Add(info.Id))
                return;

            _incomingRelationRemovers.Add(RemoveIncomingOfType<T>);
        }

        private void RemoveIncomingOfType<T>(Entity target) where T : struct, IRelationComponent
        {
            var info = _componentTypes.GetOrRegister<T>();
            if (!_sparseSetsByComponentId.TryGetValue(info.Id, out var setObj))
                return;

            var set = (SparseSet<T>)setObj;
            var span = set.DenseEntityIds;
            if (span.Length == 0)
                return;

            // Snapshot — Remove packs the dense array.
            var ids = span.ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                if (!set.Has(id))
                    continue;
                if (RelationAccess<T>.GetTarget(in set.GetRef(id)).Id != target.Id)
                    continue;

                var entity = new Entity(id, _records[id].Version);
                if (IsAlive(entity) && Has<T>(entity))
                    Remove<T>(entity);
            }
        }

        private void RemoveIncomingRelations(Entity target)
        {
            for (int i = 0; i < _incomingRelationRemovers.Count; i++)
                _incomingRelationRemovers[i](target);
        }
    }
}
