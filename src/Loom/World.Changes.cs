using System;
using System.Collections.Generic;

namespace Loom
{
    public sealed partial class World
    {
        // Opt-in component change tracking. Zero cost when nothing is TrackChanges<>()'d
        // (_trackedChangeCount == 0 skips all record calls without a dictionary lookup).
        // Buffers live only on this World — no process-wide typed cache (multi-world safe).
        private readonly Dictionary<Type, IComponentChangeBuffer> _changeBuffers =
            new Dictionary<Type, IComponentChangeBuffer>();
        private readonly List<IComponentChangeBuffer> _changeBufferList = new List<IComponentChangeBuffer>();
        private int _trackedChangeCount;

        /// <summary>Enables Added/Removed/Changed recording for <typeparamref name="T"/> on this
        /// world. Call once at setup. Untracked types incur no per-mutation work.</summary>
        public World TrackChanges<T>() where T : struct
        {
            var type = typeof(T);
            if (_changeBuffers.ContainsKey(type))
                return this;

            var buffer = new ComponentChangeBuffer();
            _changeBuffers[type] = buffer;
            _changeBufferList.Add(buffer);
            _trackedChangeCount++;
            return this;
        }

        /// <summary>True when <see cref="TrackChanges{T}"/> was called for <typeparamref name="T"/>.</summary>
        public bool IsTrackingChanges<T>() where T : struct =>
            _trackedChangeCount > 0 && _changeBuffers.ContainsKey(typeof(T));

        /// <summary>Overwrites an existing component value and, when tracking is enabled, records
        /// <c>Changed</c>. Throws if the entity does not have <typeparamref name="T"/>.
        /// In-place mutation through <see cref="Get{T}"/> is not tracked — use this or
        /// <see cref="MarkChanged{T}"/> after editing.</summary>
        public void Set<T>(Entity entity, T value) where T : struct
        {
            ref var slot = ref Get<T>(entity);
            slot = value;
            RecordChanged<T>(entity);
        }

        /// <summary>Writes <paramref name="value"/> whether or not the entity already has
        /// <typeparamref name="T"/>: <see cref="Add{T}"/> when missing, <see cref="Set{T}"/> when
        /// present.</summary>
        public void AddOrSet<T>(Entity entity, T value) where T : struct
        {
            if (Has<T>(entity))
                Set(entity, value);
            else
                Add(entity, value);
        }

        /// <summary>Records <c>Changed</c> for an entity that already has <typeparamref name="T"/>
        /// (when tracking is enabled). Use after mutating via <see cref="Get{T}"/>.</summary>
        public void MarkChanged<T>(Entity entity) where T : struct
        {
            if (!Has<T>(entity))
                throw new InvalidOperationException($"{entity} has no component {typeof(T).Name}.");
            RecordChanged<T>(entity);
        }

        /// <summary>Number of Added records for <typeparamref name="T"/> since the last
        /// <see cref="ClearComponentChanges"/> (auto-cleared at the end of <see cref="Tick"/>).</summary>
        public int AddedCount<T>() where T : struct =>
            TryGetChangeBuffer<T>(out var buffer) ? buffer.Added.Count : 0;

        public int RemovedCount<T>() where T : struct =>
            TryGetChangeBuffer<T>(out var buffer) ? buffer.Removed.Count : 0;

        public int ChangedCount<T>() where T : struct =>
            TryGetChangeBuffer<T>(out var buffer) ? buffer.Changed.Count : 0;

        /// <summary>True when any Added/Removed/Changed entry exists for <typeparamref name="T"/>.</summary>
        public bool AnyChanges<T>() where T : struct
        {
            if (!TryGetChangeBuffer<T>(out var buffer))
                return false;
            return buffer.Added.Count > 0 || buffer.Removed.Count > 0 || buffer.Changed.Count > 0;
        }

        /// <summary>Invokes <paramref name="action"/> for every entity that received
        /// <typeparamref name="T"/> since the last clear.</summary>
        public void ForEachAdded<T>(Action<Entity> action) where T : struct =>
            ForEachChangeList<T>(action, ChangeListKind.Added);

        public void ForEachRemoved<T>(Action<Entity> action) where T : struct =>
            ForEachChangeList<T>(action, ChangeListKind.Removed);

        public void ForEachChanged<T>(Action<Entity> action) where T : struct =>
            ForEachChangeList<T>(action, ChangeListKind.Changed);

        /// <summary>Clears <paramref name="destination"/> and copies Added entities for
        /// <typeparamref name="T"/>.</summary>
        public void CopyAddedTo<T>(List<Entity> destination) where T : struct =>
            CopyChangeListTo<T>(destination, ChangeListKind.Added);

        public void CopyRemovedTo<T>(List<Entity> destination) where T : struct =>
            CopyChangeListTo<T>(destination, ChangeListKind.Removed);

        public void CopyChangedTo<T>(List<Entity> destination) where T : struct =>
            CopyChangeListTo<T>(destination, ChangeListKind.Changed);

        /// <summary>Drops every recorded Added/Removed/Changed entry. Called automatically at the
        /// end of <see cref="Tick"/> after events flush.</summary>
        public void ClearComponentChanges()
        {
            for (int i = 0; i < _changeBufferList.Count; i++)
                _changeBufferList[i].Clear();
        }

        private void ForEachChangeList<T>(Action<Entity> action, ChangeListKind kind) where T : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (!TryGetChangeBuffer<T>(out var buffer))
                return;

            List<Entity> list = SelectList(buffer, kind);
            for (int i = 0; i < list.Count; i++)
                action(list[i]);
        }

        private void CopyChangeListTo<T>(List<Entity> destination, ChangeListKind kind) where T : struct
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!TryGetChangeBuffer<T>(out var buffer))
                return;
            destination.AddRange(SelectList(buffer, kind));
        }

        private static List<Entity> SelectList(ComponentChangeBuffer buffer, ChangeListKind kind)
        {
            if (kind == ChangeListKind.Added)
                return buffer.Added;
            if (kind == ChangeListKind.Removed)
                return buffer.Removed;
            return buffer.Changed;
        }

        private bool TryGetChangeBuffer<T>(out ComponentChangeBuffer buffer) where T : struct
        {
            if (_trackedChangeCount == 0)
            {
                buffer = null!;
                return false;
            }

            if (_changeBuffers.TryGetValue(typeof(T), out var boxed))
            {
                buffer = (ComponentChangeBuffer)boxed;
                return true;
            }

            buffer = null!;
            return false;
        }

        /// <summary>Change-list view for <see cref="Query"/> filters. Empty when not tracking.</summary>
        internal List<Entity> GetAddedEntities<T>() where T : struct =>
            TryGetChangeBuffer<T>(out var buffer) ? buffer.Added : EmptyChangeEntities;

        internal List<Entity> GetRemovedEntities<T>() where T : struct =>
            TryGetChangeBuffer<T>(out var buffer) ? buffer.Removed : EmptyChangeEntities;

        internal List<Entity> GetChangedEntities<T>() where T : struct =>
            TryGetChangeBuffer<T>(out var buffer) ? buffer.Changed : EmptyChangeEntities;

        private static readonly List<Entity> EmptyChangeEntities = new List<Entity>();

        private void RecordAdded<T>(Entity entity) where T : struct
        {
            if (_trackedChangeCount == 0)
                return;
            if (_changeBuffers.TryGetValue(typeof(T), out var buffer))
                buffer.RecordAdded(entity);
        }

        private void RecordRemoved<T>(Entity entity) where T : struct
        {
            if (_trackedChangeCount == 0)
                return;
            if (_changeBuffers.TryGetValue(typeof(T), out var buffer))
                buffer.RecordRemoved(entity);
        }

        private void RecordChanged<T>(Entity entity) where T : struct
        {
            if (_trackedChangeCount == 0)
                return;
            if (_changeBuffers.TryGetValue(typeof(T), out var buffer))
                buffer.RecordChanged(entity);
        }

        private void RecordRemovedByClrType(Type clrType, Entity entity)
        {
            if (_trackedChangeCount == 0)
                return;
            if (_changeBuffers.TryGetValue(clrType, out var buffer))
                buffer.RecordRemoved(entity);
        }

        private enum ChangeListKind : byte
        {
            Added = 0,
            Removed = 1,
            Changed = 2,
        }

        private interface IComponentChangeBuffer
        {
            void Clear();
            void RecordAdded(Entity entity);
            void RecordRemoved(Entity entity);
            void RecordChanged(Entity entity);
        }

        /// <summary>
        /// Coalesces same-frame noise: Add+Remove cancels; Changed on a just-Added entity stays
        /// Added-only; Remove drops any Changed; duplicate Marks are ignored.
        /// </summary>
        private sealed class ComponentChangeBuffer : IComponentChangeBuffer
        {
            public readonly List<Entity> Added = new List<Entity>();
            public readonly List<Entity> Removed = new List<Entity>();
            public readonly List<Entity> Changed = new List<Entity>();

            private readonly HashSet<int> _addedIds = new HashSet<int>();
            private readonly HashSet<int> _removedIds = new HashSet<int>();
            private readonly HashSet<int> _changedIds = new HashSet<int>();

            public void Clear()
            {
                Added.Clear();
                Removed.Clear();
                Changed.Clear();
                _addedIds.Clear();
                _removedIds.Clear();
                _changedIds.Clear();
            }

            public void RecordAdded(Entity entity)
            {
                // Re-add after remove this frame: drop Removed, treat as fresh Added.
                RemoveId(Removed, _removedIds, entity.Id);
                RemoveId(Changed, _changedIds, entity.Id);
                AddUnique(Added, _addedIds, entity);
            }

            public void RecordRemoved(Entity entity)
            {
                // Created and destroyed in the same window: net no change.
                if (RemoveId(Added, _addedIds, entity.Id))
                {
                    RemoveId(Changed, _changedIds, entity.Id);
                    return;
                }

                RemoveId(Changed, _changedIds, entity.Id);
                AddUnique(Removed, _removedIds, entity);
            }

            public void RecordChanged(Entity entity)
            {
                // Still "new" this window — Added already covers it. Gone — ignore.
                if (_addedIds.Contains(entity.Id) || _removedIds.Contains(entity.Id))
                    return;
                AddUnique(Changed, _changedIds, entity);
            }

            private static void AddUnique(List<Entity> list, HashSet<int> ids, Entity entity)
            {
                if (!ids.Add(entity.Id))
                {
                    // Same id recorded again (e.g. recycle): refresh the handle in-place.
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].Id == entity.Id)
                        {
                            list[i] = entity;
                            return;
                        }
                    }
                }

                list.Add(entity);
            }

            private static bool RemoveId(List<Entity> list, HashSet<int> ids, int id)
            {
                if (!ids.Remove(id))
                    return false;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == id)
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }

                return true;
            }
        }
    }
}
