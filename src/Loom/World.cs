using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Loom.Internal;

namespace Loom
{
    internal struct EntityRecord
    {
        public int Version;
        public bool IsAlive;
        public Archetype? Archetype;
        public int Row;
    }

    /// <summary>
    /// Owns all entities and components. Dense components (the common case) are stored in
    /// archetypes — entities with an identical set of dense component types share one columnar
    /// table, giving fast contiguous iteration. Components marked <see cref="ISparseComponent"/>
    /// or <see cref="ISharedComponent"/> live outside the archetype graph (sparse mask id space)
    /// so adding/removing them never moves the entity. Sparse holds a unique value per entity;
    /// shared interns equal values so many entities reference one instance.
    /// </summary>
    public sealed partial class World
    {
        private EntityRecord[] _records = new EntityRecord[64];
        private readonly Stack<int> _freeIds = new Stack<int>();
        // Id 0 is reserved as Entity.Null and is never handed out by Create().
        private int _nextId = 1;
        private int _liveCount;

        // Instance state, not static: every World gets its own component id space, so two
        // worlds can register the same CLR type under different ids, run different component
        // sets, or hit their own independent 256-id cap without interfering with each other.
        private readonly ComponentTypeRegistry _componentTypes = new ComponentTypeRegistry();

        private readonly List<Archetype> _archetypes = new List<Archetype>();
        private readonly Dictionary<ComponentMask, Archetype> _archetypeByMask = new Dictionary<ComponentMask, Archetype>();
        private readonly Archetype _emptyArchetype;

        private readonly Dictionary<int, ISparseSet> _sparseSetsByComponentId = new Dictionary<int, ISparseSet>();
        private readonly Dictionary<int, ISharedComponentStore> _sharedStoresByComponentId =
            new Dictionary<int, ISharedComponentStore>();

        // Sparse presence used to live inline on EntityRecord (a 32-byte ComponentMask on every
        // single entity, sparse components or not), which meaningfully bloated EntityRecord and,
        // with it, the cost of every _records[] resize. Most entities never touch a sparse
        // component, so it lives here instead — a lazily-populated, per-entity side table that
        // only ever holds an entry for entities that actually have at least one sparse component.
        // A SparseSet (not a Dictionary): O(1) array indexing instead of hashing, and it's the
        // same trusted structure already backing every per-type sparse component store below.
        private readonly SparseSet<ComponentMask> _sparseMasks = new SparseSet<ComponentMask>();

        // Father/child hierarchy — same lazily-populated SparseSet pattern as _sparseMasks. Most
        // entities are never a father or a child and never get an entry here at all.
        private readonly SparseSet<EntityRelations> _relations = new SparseSet<EntityRelations>();

        public World()
        {
            _emptyArchetype = CreateArchetype(ComponentMask.Empty, Array.Empty<int>());
        }

        public int EntityCount => _liveCount;

        /// <summary>Allocates a fresh command buffer bound to this world. Prefer the buffer
        /// passed into <see cref="ISystem.Update"/> or <see cref="Runtime.Commands"/> when
        /// driving a frame.</summary>
        public CommandBuffer CreateCommandBuffer() => new CommandBuffer(this);

        public Entity Create()
        {
            var entity = AllocateEntityId();
            PlaceInArchetype(entity, _emptyArchetype);
            _liveCount++;
            return entity;
        }

        /// <summary>Creates an entity with one component already set, in a single step. Prefer this
        /// over <c>Create()</c> + <c>Add&lt;T1&gt;()</c> when the component is dense: that path would
        /// place the entity in the empty archetype and then immediately move it, doing a whole
        /// archetype-row copy for nothing. This builds the entity directly into its final archetype
        /// by walking the same cached add-edges <see cref="Add{T}"/> uses (a small int-keyed
        /// <see cref="ArchetypeEdge"/> table per archetype) instead of building a
        /// <see cref="ComponentMask"/> and hitting the coarser, more expensive
        /// <c>ComponentMask</c>-keyed archetype table on every call.</summary>
        public Entity Create<T1>(T1 component1) where T1 : struct
        {
            var info1 = _componentTypes.GetOrRegister<T1>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info1.Id);

            var entity = AllocateEntityId();
            var (chunk, index) = PlaceInArchetype(entity, archetype);
            SetNewComponent(entity, archetype, chunk, index, info1.Id, component1);

            _liveCount++;
            return entity;
        }

        /// <summary>Creates an entity with two components already set, in a single step — see
        /// <see cref="Create{T1}"/>. For N dense components this is one archetype placement instead
        /// of N archetype moves.</summary>
        public Entity Create<T1, T2>(T1 component1, T2 component2) where T1 : struct where T2 : struct
        {
            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info2.Id);

            var entity = AllocateEntityId();
            var (chunk, index) = PlaceInArchetype(entity, archetype);
            SetNewComponent(entity, archetype, chunk, index, info1.Id, component1);
            SetNewComponent(entity, archetype, chunk, index, info2.Id, component2);

            _liveCount++;
            return entity;
        }

        /// <summary>Creates an entity with three components already set — see <see cref="Create{T1}"/>.</summary>
        public Entity Create<T1, T2, T3>(T1 component1, T2 component2, T3 component3)
            where T1 : struct where T2 : struct where T3 : struct
        {
            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info3.Id);

            var entity = AllocateEntityId();
            var (chunk, index) = PlaceInArchetype(entity, archetype);
            SetNewComponent(entity, archetype, chunk, index, info1.Id, component1);
            SetNewComponent(entity, archetype, chunk, index, info2.Id, component2);
            SetNewComponent(entity, archetype, chunk, index, info3.Id, component3);

            _liveCount++;
            return entity;
        }

        /// <summary>Creates an entity with four components already set — see <see cref="Create{T1}"/>.</summary>
        public Entity Create<T1, T2, T3, T4>(T1 component1, T2 component2, T3 component3, T4 component4)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();
            var info4 = _componentTypes.GetOrRegister<T4>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info3.Id);
            if (!ComponentTypeTraits<T4>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info4.Id);

            var entity = AllocateEntityId();
            var (chunk, index) = PlaceInArchetype(entity, archetype);
            SetNewComponent(entity, archetype, chunk, index, info1.Id, component1);
            SetNewComponent(entity, archetype, chunk, index, info2.Id, component2);
            SetNewComponent(entity, archetype, chunk, index, info3.Id, component3);
            SetNewComponent(entity, archetype, chunk, index, info4.Id, component4);

            _liveCount++;
            return entity;
        }

        /// <summary>Creates <paramref name="destination"/>.Length empty entities into
        /// <paramref name="destination"/>. Grows the entity-record table and empty-archetype chunk
        /// pages once for the whole batch instead of once per entity — prefer this over a
        /// <see cref="Create()"/> loop when you know the count upfront.</summary>
        public void CreateMany(Span<Entity> destination)
        {
            int count = destination.Length;
            if (count == 0)
                return;

            PrefetchCreateCapacity(count, _emptyArchetype);

            for (int i = 0; i < count; i++)
            {
                var entity = AllocateEntityId();
                PlaceInArchetype(entity, _emptyArchetype);
                destination[i] = entity;
            }

            _liveCount += count;
        }

        /// <summary>Creates <paramref name="destination"/>.Length entities each carrying
        /// <paramref name="component1"/> — see <see cref="CreateMany"/> and <see cref="Create{T1}"/>.</summary>
        public void CreateMany<T1>(Span<Entity> destination, T1 component1) where T1 : struct
        {
            int count = destination.Length;
            if (count == 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info1.Id);

            PrefetchCreateCapacity(count, archetype);

            // Dense data: hoist column Items[] and only refresh when AddEntityRow crosses a chunk.
            if (!ComponentTypeTraits<T1>.UsesSparseMask && !ComponentTypeTraits<T1>.IsEmpty)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;

                for (int i = 0; i < count; i++)
                {
                    var entity = AllocateEntityId();
                    ref var rec = ref _records[entity.Id];
                    rec.Archetype = archetype;
                    var (row, chunk, index) = archetype.AddEntityRow(entity);
                    rec.Row = row;
                    if (!ReferenceEquals(chunk, currentChunk))
                    {
                        currentChunk = chunk;
                        items1 = chunk.GetItems<T1>(col1);
                    }

                    items1![index] = component1;
                    destination[i] = entity;
                    RecordAdded<T1>(entity);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var entity = AllocateEntityId();
                    var (chunk, index) = PlaceInArchetype(entity, archetype);
                    SetNewComponent(entity, archetype, chunk, index, info1.Id, component1);
                    destination[i] = entity;
                }
            }

            _liveCount += count;
        }

        /// <summary>Creates <paramref name="destination"/>.Length entities each carrying the same
        /// two components — see <see cref="CreateMany"/> and <see cref="Create{T1,T2}"/>.</summary>
        public void CreateMany<T1, T2>(Span<Entity> destination, T1 component1, T2 component2)
            where T1 : struct where T2 : struct
        {
            int count = destination.Length;
            if (count == 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info2.Id);

            PrefetchCreateCapacity(count, archetype);

            bool t1DenseData = !ComponentTypeTraits<T1>.UsesSparseMask && !ComponentTypeTraits<T1>.IsEmpty;
            bool t2DenseData = !ComponentTypeTraits<T2>.UsesSparseMask && !ComponentTypeTraits<T2>.IsEmpty;

            if (t1DenseData && t2DenseData)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                int col2 = archetype.ColumnIndex(info2.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;
                T2[]? items2 = null;

                for (int i = 0; i < count; i++)
                {
                    var entity = AllocateEntityId();
                    ref var rec = ref _records[entity.Id];
                    rec.Archetype = archetype;
                    var (row, chunk, index) = archetype.AddEntityRow(entity);
                    rec.Row = row;
                    if (!ReferenceEquals(chunk, currentChunk))
                    {
                        currentChunk = chunk;
                        items1 = chunk.GetItems<T1>(col1);
                        items2 = chunk.GetItems<T2>(col2);
                    }

                    items1![index] = component1;
                    items2![index] = component2;
                    destination[i] = entity;
                    RecordAdded<T1>(entity);
                    RecordAdded<T2>(entity);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var entity = AllocateEntityId();
                    var (chunk, index) = PlaceInArchetype(entity, archetype);
                    SetNewComponent(entity, archetype, chunk, index, info1.Id, component1);
                    SetNewComponent(entity, archetype, chunk, index, info2.Id, component2);
                    destination[i] = entity;
                }
            }

            _liveCount += count;
        }

        /// <summary>Creates <paramref name="destination"/>.Length entities each carrying the same
        /// three components — see <see cref="CreateMany"/> and <see cref="Create{T1,T2,T3}"/>.</summary>
        public void CreateMany<T1, T2, T3>(
            Span<Entity> destination, T1 component1, T2 component2, T3 component3)
            where T1 : struct where T2 : struct where T3 : struct
        {
            int count = destination.Length;
            if (count == 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info3.Id);

            PrefetchCreateCapacity(count, archetype);

            bool t1DenseData = !ComponentTypeTraits<T1>.UsesSparseMask && !ComponentTypeTraits<T1>.IsEmpty;
            bool t2DenseData = !ComponentTypeTraits<T2>.UsesSparseMask && !ComponentTypeTraits<T2>.IsEmpty;
            bool t3DenseData = !ComponentTypeTraits<T3>.UsesSparseMask && !ComponentTypeTraits<T3>.IsEmpty;

            if (t1DenseData && t2DenseData && t3DenseData)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                int col2 = archetype.ColumnIndex(info2.Id);
                int col3 = archetype.ColumnIndex(info3.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;
                T2[]? items2 = null;
                T3[]? items3 = null;

                for (int i = 0; i < count; i++)
                {
                    var entity = AllocateEntityId();
                    ref var rec = ref _records[entity.Id];
                    rec.Archetype = archetype;
                    var (row, chunk, index) = archetype.AddEntityRow(entity);
                    rec.Row = row;
                    if (!ReferenceEquals(chunk, currentChunk))
                    {
                        currentChunk = chunk;
                        items1 = chunk.GetItems<T1>(col1);
                        items2 = chunk.GetItems<T2>(col2);
                        items3 = chunk.GetItems<T3>(col3);
                    }

                    items1![index] = component1;
                    items2![index] = component2;
                    items3![index] = component3;
                    destination[i] = entity;
                    RecordAdded<T1>(entity);
                    RecordAdded<T2>(entity);
                    RecordAdded<T3>(entity);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var entity = AllocateEntityId();
                    var (chunk, index) = PlaceInArchetype(entity, archetype);
                    SetNewComponent(entity, archetype, chunk, index, info1.Id, component1);
                    SetNewComponent(entity, archetype, chunk, index, info2.Id, component2);
                    SetNewComponent(entity, archetype, chunk, index, info3.Id, component3);
                    destination[i] = entity;
                }
            }

            _liveCount += count;
        }

        /// <summary>Same as <see cref="CreateMany{T1,T2,T3}(Span{Entity},T1,T2,T3)"/> but does not
        /// write created ids into a caller buffer — prefer this when you only need the entities to
        /// exist (e.g. bulk spawn measured by <see cref="EntityCount"/>).</summary>
        public void CreateMany<T1, T2, T3>(int count, T1 component1, T2 component2, T3 component3)
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (count <= 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask)
                archetype = GetArchetypeViaAddEdge(archetype, info3.Id);

            PrefetchCreateCapacity(count, archetype);

            bool t1DenseData = !ComponentTypeTraits<T1>.UsesSparseMask && !ComponentTypeTraits<T1>.IsEmpty;
            bool t2DenseData = !ComponentTypeTraits<T2>.UsesSparseMask && !ComponentTypeTraits<T2>.IsEmpty;
            bool t3DenseData = !ComponentTypeTraits<T3>.UsesSparseMask && !ComponentTypeTraits<T3>.IsEmpty;

            if (t1DenseData && t2DenseData && t3DenseData)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                int col2 = archetype.ColumnIndex(info2.Id);
                int col3 = archetype.ColumnIndex(info3.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;
                T2[]? items2 = null;
                T3[]? items3 = null;

                for (int i = 0; i < count; i++)
                {
                    var entity = AllocateEntityId();
                    ref var rec = ref _records[entity.Id];
                    rec.Archetype = archetype;
                    var (row, chunk, index) = archetype.AddEntityRow(entity);
                    rec.Row = row;
                    if (!ReferenceEquals(chunk, currentChunk))
                    {
                        currentChunk = chunk;
                        items1 = chunk.GetItems<T1>(col1);
                        items2 = chunk.GetItems<T2>(col2);
                        items3 = chunk.GetItems<T3>(col3);
                    }

                    items1![index] = component1;
                    items2![index] = component2;
                    items3![index] = component3;
                    RecordAdded<T1>(entity);
                    RecordAdded<T2>(entity);
                    RecordAdded<T3>(entity);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var entity = AllocateEntityId();
                    var (chunk, index) = PlaceInArchetype(entity, archetype);
                    SetNewComponent(entity, archetype, chunk, index, info1.Id, component1);
                    SetNewComponent(entity, archetype, chunk, index, info2.Id, component2);
                    SetNewComponent(entity, archetype, chunk, index, info3.Id, component3);
                }
            }

            _liveCount += count;
        }

        /// <summary>Destroys every entity in <paramref name="entities"/>. Each entry is handled by
        /// <see cref="Destroy"/> (cascading children, sparse cleanup, id recycle) — this is a
        /// convenience loop, not a separate bulk path, so order and hierarchy semantics match
        /// destroying them one by one.</summary>
        public void DestroyMany(ReadOnlySpan<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
                Destroy(entities[i]);
        }

        public void Destroy(Entity entity)
        {
            RequireAlive(entity);

            // Cascade: destroy every descendant first. Each recursive Destroy call detaches itself
            // from its own father's child list as one of its first steps (see DetachFromFather), so
            // `entity`'s FirstChild naturally advances to the next child — no separate snapshot of
            // the child list is needed. Recursion depth is bounded by hierarchy depth, not breadth.
            if (_relations.Has(entity.Id))
            {
                Entity firstChild;
                while (!(firstChild = _relations.GetRef(entity.Id).FirstChild).IsNull)
                    Destroy(firstChild);
            }

            DetachFromFather(entity);

            // Drop typed links that pointed at this entity (sources keep living).
            RemoveIncomingRelations(entity);

            ref var rec = ref _records[entity.Id];

            RecordEntityDestroyed(entity);

            if (_trackedChangeCount > 0)
                RecordTrackedRemovalsOnDestroy(entity, ref rec);

            // Most entities never touch a sparse component at all, so this lookup found nothing and
            // we skip the cleanup below entirely — no EnumerateBits() allocation, no per-sparse-type
            // work. For the entities that do have an entry, it tells us exactly which (few) sets to
            // touch rather than scanning every sparse type ever registered in this world. Empty
            // (tag) sparse types never get a SparseSet created for them in the first place (see
            // Add<T>), so the inner lookup has to be a TryGetValue too.
            if (_sparseMasks.Has(entity.Id))
            {
                foreach (var componentId in _sparseMasks.GetRef(entity.Id))
                {
                    if (_sharedStoresByComponentId.TryGetValue(componentId, out var shared))
                        shared.Remove(entity.Id);
                    else if (_sparseSetsByComponentId.TryGetValue(componentId, out var set))
                        set.Remove(entity.Id);
                }
                _sparseMasks.Remove(entity.Id);
            }

            // CommandBuffer.Create reserves an id without placing a row — skip swap-back for those.
            if (rec.Archetype != null)
            {
                var moved = rec.Archetype.RemoveRowSwapBack(rec.Row);
                if (!moved.IsNull)
                    _records[moved.Id].Row = rec.Row;
            }

            rec.IsAlive = false;
            rec.Archetype = null;
            rec.Version++;
            _liveCount--;
            _freeIds.Push(entity.Id);
        }

        public bool IsAlive(Entity entity) =>
            entity.Id > 0 && entity.Id < _nextId &&
            _records[entity.Id].IsAlive && _records[entity.Id].Version == entity.Version;

        public bool Has<T>(Entity entity) where T : struct
        {
            RequireAlive(entity);
            var info = _componentTypes.GetOrRegister<T>();

            if (ComponentTypeTraits<T>.UsesSparseMask)
                return GetSparseMask(entity.Id).Get(info.Id);

            var archetype = _records[entity.Id].Archetype;
            // Reserved by CommandBuffer.Create but not yet played back — not in any archetype.
            return archetype != null && archetype.Mask.Get(info.Id);
        }

        /// <summary>Adds a component. Throws if the entity already has one — use <see cref="Get{T}"/>
        /// or <see cref="Set{T}"/> to mutate in place instead.</summary>
        public ref T Add<T>(Entity entity, T value = default) where T : struct
        {
            RequireAlive(entity);
            var info = _componentTypes.GetOrRegister<T>();

            if (ComponentTypeTraits<T>.UsesSparseMask)
            {
                // GetOrCreateSparseMaskRef gives us a direct ref into the SparseSet's backing
                // array — mutating through it writes the mask in place, no separate "read the old
                // value, then write the new one back" round trip.
                ref var mask = ref GetOrCreateSparseMaskRef(entity.Id);
                if (mask.Get(info.Id))
                    throw new InvalidOperationException($"{entity} already has component {typeof(T).Name}.");
                ComponentMask.SetBit(ref mask, info.Id);
                // Empty (tag) types have nothing to store — every instance is indistinguishable,
                // so no value store is created; the mask bit above is the whole story.
                if (ComponentTypeTraits<T>.IsEmpty)
                {
                    RecordAdded<T>(entity);
                    return ref ComponentTypeTraits<T>.EmptyValue;
                }
                if (ComponentTypeTraits<T>.IsShared)
                {
                    ref var shared = ref GetOrCreateSharedStore<T>(info.Id).Add(entity.Id, value);
                    RecordAdded<T>(entity);
                    return ref shared;
                }
                ref var sparse = ref GetOrCreateSparseSet<T>(info.Id).Set(entity.Id, value);
                RecordAdded<T>(entity);
                return ref sparse;
            }

            RequirePlaced(entity);
            ref var dense = ref AddDense(entity, info.Id, value);
            RecordAdded<T>(entity);
            return ref dense;
        }

        /// <summary>Removes a component. Returns false if the entity didn't have one.</summary>
        public bool Remove<T>(Entity entity) where T : struct
        {
            RequireAlive(entity);
            var info = _componentTypes.GetOrRegister<T>();

            if (ComponentTypeTraits<T>.UsesSparseMask)
            {
                if (!_sparseMasks.Has(entity.Id))
                    return false;

                ref var mask = ref _sparseMasks.GetRef(entity.Id);
                if (!mask.Get(info.Id))
                    return false;
                ComponentMask.ClearBit(ref mask, info.Id);
                // Deliberately not pruning the SparseSet<ComponentMask> entry even if the mask is
                // now Empty — that would need its own swap-back removal on every Remove call. An
                // Empty mask sitting in a reused slot is harmless (GetSparseMask/Has still answer
                // correctly); Destroy() is what actually reclaims the slot.

                if (ComponentTypeTraits<T>.IsEmpty)
                {
                    RecordRemoved<T>(entity);
                    return true; // no value store was ever created
                }
                if (ComponentTypeTraits<T>.IsShared)
                {
                    bool sharedRemoved = GetOrCreateSharedStore<T>(info.Id).Remove(entity.Id);
                    if (sharedRemoved)
                        RecordRemoved<T>(entity);
                    return sharedRemoved;
                }
                bool sparseRemoved = GetOrCreateSparseSet<T>(info.Id).Remove(entity.Id);
                if (sparseRemoved)
                    RecordRemoved<T>(entity);
                return sparseRemoved;
            }

            if (_records[entity.Id].Archetype == null)
                return false;

            bool denseRemoved = RemoveDense<T>(entity, info.Id);
            if (denseRemoved)
                RecordRemoved<T>(entity);
            return denseRemoved;
        }

        /// <summary>Adds <typeparamref name="T"/> to every entity in <paramref name="entities"/>.
        /// When all targets share one archetype and the span covers that archetype entirely, the
        /// transition copies shared columns in one pass and clears the source without per-entity
        /// swap-back relocation.</summary>
        public void AddMany<T>(ReadOnlySpan<Entity> entities, T value = default) where T : struct
        {
            int count = entities.Length;
            if (count == 0)
                return;

            if (ComponentTypeTraits<T>.UsesSparseMask)
            {
                for (int i = 0; i < count; i++)
                    Add(entities[i], value);
                return;
            }

            var info = _componentTypes.GetOrRegister<T>();
            if (!TryGetHomogeneousDenseSource(entities, info.Id, requirePresent: false, out var from))
            {
                for (int i = 0; i < count; i++)
                    Add(entities[i], value);
                return;
            }

            var edge = GetAddEdge(from, info.Id);
            if (count == from.Count)
            {
                TransitionEntireArchetype(from, edge, info.Id, add: true, value);
                return;
            }

            TransitionPartialDescending(entities, from, edge, info.Id, add: true, value);
        }

        /// <summary>Removes <typeparamref name="T"/> from every entity in <paramref name="entities"/>.
        /// Same whole-archetype fast path as <see cref="AddMany{T}"/> when applicable.</summary>
        public void RemoveMany<T>(ReadOnlySpan<Entity> entities) where T : struct
        {
            int count = entities.Length;
            if (count == 0)
                return;

            if (ComponentTypeTraits<T>.UsesSparseMask)
            {
                for (int i = 0; i < count; i++)
                    Remove<T>(entities[i]);
                return;
            }

            var info = _componentTypes.GetOrRegister<T>();
            if (!TryGetHomogeneousDenseSource(entities, info.Id, requirePresent: true, out var from))
            {
                for (int i = 0; i < count; i++)
                    Remove<T>(entities[i]);
                return;
            }

            var edge = GetRemoveEdge(from, info.Id);
            if (count == from.Count)
            {
                TransitionEntireArchetype(from, edge, info.Id, add: false, default(T));
                return;
            }

            TransitionPartialDescending(entities, from, edge, info.Id, add: false, default(T));
        }

        public ref T Get<T>(Entity entity) where T : struct
        {
            RequireAlive(entity);
            var info = _componentTypes.GetOrRegister<T>();

            if (ComponentTypeTraits<T>.UsesSparseMask)
            {
                if (!GetSparseMask(entity.Id).Get(info.Id))
                    throw new InvalidOperationException($"{entity} has no component {typeof(T).Name}.");
                if (ComponentTypeTraits<T>.IsEmpty)
                    return ref ComponentTypeTraits<T>.EmptyValue;
                if (ComponentTypeTraits<T>.IsShared)
                    return ref GetOrCreateSharedStore<T>(info.Id).GetRef(entity.Id);
                return ref GetOrCreateSparseSet<T>(info.Id).GetRef(entity.Id);
            }

            RequirePlaced(entity);
            ref var rec = ref _records[entity.Id];
            var archetype = rec.Archetype!;
            if (!archetype.Mask.Get(info.Id))
                throw new InvalidOperationException($"{entity} has no component {typeof(T).Name}.");

            // Same story for dense: a tag type has no column at all (Archetype.DataComponentIds
            // excludes it), so the mask bit we just checked is the entire answer.
            if (ComponentTypeTraits<T>.IsEmpty)
                return ref ComponentTypeTraits<T>.EmptyValue;

            var (chunk, index) = archetype.Locate(rec.Row);
            return ref archetype.GetColumn<T>(info.Id, chunk).GetRef(index);
        }

        /// <summary>How many distinct interned values of shared <typeparamref name="T"/> are live
        /// in this world (refcount &gt; 0). Useful for tests and diagnostics.</summary>
        public int SharedInstanceCount<T>() where T : struct
        {
            if (!ComponentTypeTraits<T>.IsShared)
                throw new InvalidOperationException($"{typeof(T).Name} is not an ISharedComponent.");

            var info = _componentTypes.GetOrRegister<T>();
            return _sharedStoresByComponentId.TryGetValue(info.Id, out var store)
                ? store.UniqueInstanceCount
                : 0;
        }

        public Query Query() => new Query(this);

        /// <summary>
        /// Starts a query from a previously captured <see cref="QueryFilter"/>
        /// (see <see cref="Query.ToFilter"/>). Masks must belong to this world.
        /// </summary>
        public Query Query(in QueryFilter filter) => new Query(this, in filter);

        /// <summary>Makes <paramref name="father"/> the father of <paramref name="child"/>, inserting
        /// it at the head of <paramref name="father"/>'s child list. If <paramref name="child"/>
        /// already had a different father, it's detached from that one first. Throws if
        /// <paramref name="child"/> and <paramref name="father"/> are the same entity, or if
        /// <paramref name="father"/> is already a descendant of <paramref name="child"/> (which would
        /// create a cycle).</summary>
        public void SetFather(Entity child, Entity father)
        {
            RequireAlive(child);
            RequireAlive(father);

            if (child.Id == father.Id)
                throw new InvalidOperationException($"{child} cannot be its own father.");
            if (IsAncestorOf(child, father))
                throw new InvalidOperationException($"Setting {father} as the father of {child} would create a relationship cycle.");

            DetachFromFather(child);

            EnsureRelationsEntry(child.Id);
            EnsureRelationsEntry(father.Id);

            var oldFirstChild = _relations.GetRef(father.Id).FirstChild;

            ref var childRel = ref _relations.GetRef(child.Id);
            childRel.Father = father;
            childRel.PrevSibling = Entity.Null;
            childRel.NextSibling = oldFirstChild;

            if (!oldFirstChild.IsNull)
                _relations.GetRef(oldFirstChild.Id).PrevSibling = child;

            _relations.GetRef(father.Id).FirstChild = child;
        }

        /// <summary>Detaches <paramref name="child"/> from its current father, if it has one — the
        /// entity itself is untouched, it just becomes a root. Returns false if it had no father.</summary>
        public bool RemoveFather(Entity child)
        {
            RequireAlive(child);

            if (!_relations.Has(child.Id) || _relations.GetRef(child.Id).Father.IsNull)
                return false;

            DetachFromFather(child);
            return true;
        }

        public bool HasFather(Entity child) => !GetFather(child).IsNull;

        /// <summary>The entity's father, or <see cref="Entity.Null"/> if it's a root — not an
        /// exception, since "no father" is the normal state for most entities, unlike a missing
        /// <c>Get&lt;T&gt;</c> component.</summary>
        public Entity GetFather(Entity child)
        {
            RequireAlive(child);
            return _relations.Has(child.Id) ? _relations.GetRef(child.Id).Father : Entity.Null;
        }

        public bool HasChildren(Entity father)
        {
            RequireAlive(father);
            return _relations.Has(father.Id) && !_relations.GetRef(father.Id).FirstChild.IsNull;
        }

        /// <summary>Allocation-free enumeration of <paramref name="father"/>'s direct children (not
        /// grandchildren), in most-recently-attached-first order. See <see cref="ChildEnumerable"/>.</summary>
        public ChildEnumerable GetChildren(Entity father)
        {
            RequireAlive(father);
            return new ChildEnumerable(this, father);
        }

        /// <summary>Used by <see cref="ChildEnumerator"/> to walk the sibling list without exposing
        /// <see cref="EntityRelations"/> itself publicly.</summary>
        internal EntityRelations GetRelationsOrDefault(int entityId) =>
            _relations.Has(entityId) ? _relations.GetRef(entityId) : default;

        /// <summary>Number of distinct archetypes ever created (never shrinks — empty archetypes are cached for reuse). Exposed for diagnostics/tests; a good way to see that sparse-component churn doesn't fragment the archetype graph.</summary>
        internal int ArchetypeCount => _archetypes.Count;

        /// <summary>Resolves (registering on first use) the id/storage-kind for T within this world. Used by <see cref="Query"/>, which holds a World reference rather than its own registry.</summary>
        internal ComponentTypeInfo GetComponentInfo<T>() where T : struct => _componentTypes.GetOrRegister<T>();

        private Entity AllocateEntityId()
        {
            int id;
            if (_freeIds.Count > 0)
            {
                id = _freeIds.Pop();
            }
            else
            {
                id = _nextId++;
                EnsureRecordCapacity(_nextId);
            }

            _records[id].IsAlive = true;
            var entity = new Entity(id, _records[id].Version);
            RecordEntityCreated(entity);
            return entity;
        }

        /// <summary>Used by <see cref="CommandBuffer.Create"/>: allocates a live id without placing
        /// a row. <see cref="PlayCommandBufferCreate"/> finishes placement on playback.</summary>
        internal Entity ReserveEntityForCommandBuffer()
        {
            var entity = AllocateEntityId();
            _liveCount++;
            return entity;
        }

        internal void PlayCommandBufferCreate(Entity entity)
        {
            RequireAlive(entity);
            ref var rec = ref _records[entity.Id];
            if (rec.Archetype != null)
                throw new InvalidOperationException($"{entity} was already placed; CommandBuffer.Create playback expected a reserved entity.");
            PlaceInArchetype(entity, _emptyArchetype);
        }

        /// <summary>Places the entity's row and returns its already-resolved chunk/index, so a
        /// caller setting several components on a freshly-placed entity (<see cref="Create{T1,T2}"/>)
        /// only resolves the location once instead of once per component.</summary>
        private (Chunk Chunk, int Index) PlaceInArchetype(Entity entity, Archetype archetype)
        {
            ref var rec = ref _records[entity.Id];
            rec.Archetype = archetype;
            var (row, chunk, index) = archetype.AddEntityRow(entity);
            rec.Row = row;
            return (chunk, index);
        }

        /// <summary>Sets a just-created entity's component, dense or sparse. Only valid for a brand
        /// new row: unlike <see cref="Add{T}"/> it doesn't check "already has" (a fresh entity
        /// can't) and doesn't move archetypes (the caller already placed the entity correctly, and
        /// passes in the chunk/index that placement already resolved — see <see cref="PlaceInArchetype"/>).</summary>
        private void RecordTrackedRemovalsOnDestroy(Entity entity, ref EntityRecord rec)
        {
            if (rec.Archetype != null)
            {
                var ids = rec.Archetype.ComponentIds;
                for (int i = 0; i < ids.Length; i++)
                    RecordRemovedByClrType(_componentTypes.Get(ids[i]).ClrType, entity);
            }

            if (_sparseMasks.Has(entity.Id))
            {
                foreach (int sparseId in _sparseMasks.GetRef(entity.Id))
                    RecordRemovedByClrType(_componentTypes.GetSparse(sparseId).ClrType, entity);
            }
        }

        private void SetNewComponent<T>(Entity entity, Archetype archetype, Chunk chunk, int index, int componentId, T value) where T : struct
        {
            if (ComponentTypeTraits<T>.UsesSparseMask)
            {
                // GetOrCreateSparseMaskRef (not a blind write) because Create<T1,T2> can call this
                // twice for the same brand-new entity when both components use the sparse mask —
                // the second call must see the bit the first one set, not stomp over it.
                ref var mask = ref GetOrCreateSparseMaskRef(entity.Id);
                mask = mask.With(componentId);
                if (!ComponentTypeTraits<T>.IsEmpty)
                {
                    if (ComponentTypeTraits<T>.IsShared)
                        GetOrCreateSharedStore<T>(componentId).Add(entity.Id, value);
                    else
                        GetOrCreateSparseSet<T>(componentId).Set(entity.Id, value);
                }
            }
            else if (!ComponentTypeTraits<T>.IsEmpty)
            {
                archetype.GetColumn<T>(componentId, chunk).Items[index] = value;
            }
            // Dense + empty: nothing left to do — the target archetype's mask (chosen by
            // World.Create<T1[,T2]> before calling this) already has the bit set.
            RecordAdded<T>(entity);
        }

        private ref T AddDense<T>(Entity entity, int componentId, T value) where T : struct
        {
            ref var rec = ref _records[entity.Id];
            var from = rec.Archetype!;

            if (from.Mask.Get(componentId))
                throw new InvalidOperationException($"{entity} already has component {typeof(T).Name}.");

            var edge = GetAddEdge(from, componentId);
            var (newRow, chunk, index) = MoveEntity(entity, from, rec.Row, edge);
            rec.Archetype = edge.Target!;
            rec.Row = newRow;

            // Tag type: no column exists for it (Archetype.DataComponentIds excludes it) — the
            // archetype move above already gave it presence via `to`'s mask, nothing left to store.
            if (ComponentTypeTraits<T>.IsEmpty)
                return ref ComponentTypeTraits<T>.EmptyValue;

            var column = edge.Target!.GetColumn<T>(componentId, chunk);
            column.Items[index] = value;
            return ref column.Items[index];
        }

        private bool RemoveDense<T>(Entity entity, int componentId) where T : struct
        {
            ref var rec = ref _records[entity.Id];
            var from = rec.Archetype!;

            if (!from.Mask.Get(componentId))
                return false;

            var edge = GetRemoveEdge(from, componentId);
            var (newRow, _, _) = MoveEntity(entity, from, rec.Row, edge);
            rec.Archetype = edge.Target!;
            rec.Row = newRow;
            return true;
        }

        /// <summary>Moves the entity at <paramref name="fromRow"/> in <paramref name="from"/> into
        /// <paramref name="edge"/>.Target, copying shared columns via the edge's compacted copy plan
        /// (built once when the edge was first materialized). Fixes up the row of whichever entity
        /// got swapped into the vacated slot. Returns the new row along with its already-resolved
        /// chunk/index, so callers that need to write a just-added component's value
        /// (<see cref="AddDense{T}"/>) don't have to re-<c>Locate</c> it themselves.</summary>
        private (int Row, Chunk Chunk, int Index) MoveEntity(Entity entity, Archetype from, int fromRow, ArchetypeEdge edge)
        {
            var to = edge.Target!;
            var (toRow, toChunk, toIndex) = to.AddEntityRow(entity);
            var (fromChunk, fromIndex) = from.Locate(fromRow);

            // Compacted map: only columns present on both archetypes (no -1 skips).
            var fromCols = edge.FromColumnIndices;
            var toCols = edge.ToColumnIndices;
            for (int i = 0; i < fromCols.Length; i++)
                fromChunk.Columns[fromCols[i]].CopyRowTo(fromIndex, toChunk.Columns[toCols[i]], toIndex);

            var moved = from.RemoveRowSwapBack(fromRow, fromChunk, fromIndex);
            if (!moved.IsNull)
                _records[moved.Id].Row = fromRow;

            return (toRow, toChunk, toIndex);
        }

        private bool TryGetHomogeneousDenseSource(
            ReadOnlySpan<Entity> entities, int componentId, bool requirePresent, out Archetype from)
        {
            from = null!;
            RequireAlive(entities[0]);
            RequirePlaced(entities[0]);
            from = _records[entities[0].Id].Archetype!;
            bool firstHas = from.Mask.Get(componentId);
            if (requirePresent ? !firstHas : firstHas)
                return false;

            for (int i = 1; i < entities.Length; i++)
            {
                RequireAlive(entities[i]);
                if (!ReferenceEquals(_records[entities[i].Id].Archetype, from))
                    return false;
            }

            return true;
        }

        private void TransitionEntireArchetype<T>(
            Archetype from, ArchetypeEdge edge, int componentId, bool add, T value)
            where T : struct
        {
            var to = edge.Target!;
            int n = from.Count;
            int toStartRow = to.Count;
            to.EnsureRowCapacity(n);

            var fromCols = edge.FromColumnIndices;
            var toCols = edge.ToColumnIndices;
            bool writeAddedValue = add && !ComponentTypeTraits<T>.IsEmpty;
            int valueCol = writeAddedValue ? to.ColumnIndex(componentId) : -1;

            // Place every entity into the target first (packed append), then bulk-copy columns.
            for (int row = 0; row < n; row++)
            {
                var (fromChunk, fromIndex) = from.Locate(row);
                var entity = fromChunk.Entities[fromIndex];
                var (toRow, _, _) = to.AddEntityRow(entity);

                ref var rec = ref _records[entity.Id];
                rec.Archetype = to;
                rec.Row = toRow;

                if (add)
                    RecordAdded<T>(entity);
                else
                    RecordRemoved<T>(entity);
            }

            for (int c = 0; c < fromCols.Length; c++)
                CopyArchetypeColumnRange(from, fromCols[c], 0, to, toCols[c], toStartRow, n);

            if (writeAddedValue)
            {
                int remaining = n;
                int toRow = toStartRow;
                while (remaining > 0)
                {
                    var (chunk, index) = to.Locate(toRow);
                    int run = Math.Min(remaining, Archetype.ChunkCapacity - index);
                    var items = chunk.GetItems<T>(valueCol);
                    for (int i = 0; i < run; i++)
                        items[index + i] = value;
                    toRow += run;
                    remaining -= run;
                }
            }

            from.ClearAllRows();
        }

        private static void CopyArchetypeColumnRange(
            Archetype from, int fromCol, int fromRowStart,
            Archetype to, int toCol, int toRowStart, int count)
        {
            int remaining = count;
            int fromRow = fromRowStart;
            int toRow = toRowStart;
            while (remaining > 0)
            {
                var (fromChunk, fromIndex) = from.Locate(fromRow);
                var (toChunk, toIndex) = to.Locate(toRow);
                int run = remaining;
                int fromRoom = Archetype.ChunkCapacity - fromIndex;
                int toRoom = Archetype.ChunkCapacity - toIndex;
                if (fromRoom < run) run = fromRoom;
                if (toRoom < run) run = toRoom;

                fromChunk.Columns[fromCol].CopyRange(fromIndex, toChunk.Columns[toCol], toIndex, run);
                fromRow += run;
                toRow += run;
                remaining -= run;
            }
        }

        private void TransitionPartialDescending<T>(
            ReadOnlySpan<Entity> entities, Archetype from, ArchetypeEdge edge, int componentId, bool add, T value)
            where T : struct
        {
            int count = entities.Length;
            var order = System.Buffers.ArrayPool<int>.Shared.Rent(count);
            var rows = System.Buffers.ArrayPool<int>.Shared.Rent(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    order[i] = i;
                    rows[i] = _records[entities[i].Id].Row;
                }

                System.Array.Sort(rows, order, 0, count);
                // Highest row first so RemoveRowSwapBack often hits the cheap last-row path.
                System.Array.Reverse(rows, 0, count);
                System.Array.Reverse(order, 0, count);

                bool writeAddedValue = add && !ComponentTypeTraits<T>.IsEmpty;
                var to = edge.Target!;

                for (int i = 0; i < count; i++)
                {
                    var entity = entities[order[i]];
                    int fromRow = _records[entity.Id].Row;
                    var (newRow, chunk, index) = MoveEntity(entity, from, fromRow, edge);
                    ref var rec = ref _records[entity.Id];
                    rec.Archetype = to;
                    rec.Row = newRow;

                    if (writeAddedValue)
                        to.GetColumn<T>(componentId, chunk).Items[index] = value;

                    if (add)
                        RecordAdded<T>(entity);
                    else
                        RecordRemoved<T>(entity);
                }
            }
            finally
            {
                System.Buffers.ArrayPool<int>.Shared.Return(order);
                System.Buffers.ArrayPool<int>.Shared.Return(rows);
            }
        }

        /// <summary>Walks/creates the cached add-edge for <paramref name="componentId"/>. Create
        /// paths only need the target archetype; Add/Remove also need the edge's column mapping for
        /// <see cref="MoveEntity"/>.</summary>
        private Archetype GetArchetypeViaAddEdge(Archetype from, int componentId) =>
            GetAddEdge(from, componentId).Target!;

        private ArchetypeEdge GetAddEdge(Archetype from, int componentId)
        {
            if (from.TryGetAddEdge(componentId, out var cached))
                return cached;

            var to = GetOrCreateArchetype(from.Mask.With(componentId));
            // Materialize both directions at once: the first Add of T also pays for the Remove
            // mapping, so a later Remove&lt;T&gt; hit doesn't rebuild a nearly-identical table.
            var addEdge = ArchetypeEdge.Build(from, to);
            var removeEdge = ArchetypeEdge.Build(to, from);
            from.SetAddEdge(componentId, addEdge);
            to.SetRemoveEdge(componentId, removeEdge);
            return addEdge;
        }

        private ArchetypeEdge GetRemoveEdge(Archetype from, int componentId)
        {
            if (from.TryGetRemoveEdge(componentId, out var cached))
                return cached;

            var to = GetOrCreateArchetype(from.Mask.Without(componentId));
            var removeEdge = ArchetypeEdge.Build(from, to);
            var addEdge = ArchetypeEdge.Build(to, from);
            from.SetRemoveEdge(componentId, removeEdge);
            to.SetAddEdge(componentId, addEdge);
            return removeEdge;
        }

        /// <summary>One-shot capacity growth for a bulk create: enough <see cref="_records"/> slots
        /// for any brand-new ids this batch will mint, plus enough archetype chunk pages for the
        /// rows about to be appended.</summary>
        private void PrefetchCreateCapacity(int count, Archetype archetype)
        {
            int newIds = count - _freeIds.Count;
            if (newIds > 0)
                EnsureRecordCapacity(_nextId + newIds);
            archetype.EnsureRowCapacity(count);
        }

        private Archetype GetOrCreateArchetype(ComponentMask mask)
        {
            if (_archetypeByMask.TryGetValue(mask, out var existing))
                return existing;

            return CreateArchetype(mask, mask.EnumerateBits());
        }

        private Archetype CreateArchetype(ComponentMask mask, int[] componentIds)
        {
            var archetype = new Archetype(_archetypes.Count, mask, componentIds, _componentTypes);
            _archetypes.Add(archetype);
            _archetypeByMask[mask] = archetype;
            return archetype;
        }

        /// <summary>Internal (not private) so <see cref="Query"/>'s Each&lt;T1..T8&gt; fast path can
        /// resolve a sparse component's backing store directly, the same way it resolves a dense
        /// column via <see cref="GetComponentInfo{T}"/> — a Query holds a World reference rather than
        /// duplicating its storage.</summary>
        internal SparseSet<T> GetOrCreateSparseSet<T>(int id) where T : struct
        {
            if (ReferenceEquals(SparseStoreCache<T>.World, this))
                return SparseStoreCache<T>.Store!;

            if (_sparseSetsByComponentId.TryGetValue(id, out var existing))
            {
                var typed = (SparseSet<T>)existing;
                SparseStoreCache<T>.World = this;
                SparseStoreCache<T>.Store = typed;
                return typed;
            }

            var created = new SparseSet<T>();
            _sparseSetsByComponentId[id] = created;
            SparseStoreCache<T>.World = this;
            SparseStoreCache<T>.Store = created;
            return created;
        }

        internal SharedComponentStore<T> GetOrCreateSharedStore<T>(int id) where T : struct
        {
            if (ReferenceEquals(SharedStoreCache<T>.World, this))
                return SharedStoreCache<T>.Store!;

            if (_sharedStoresByComponentId.TryGetValue(id, out var existing))
            {
                var typed = (SharedComponentStore<T>)existing;
                SharedStoreCache<T>.World = this;
                SharedStoreCache<T>.Store = typed;
                return typed;
            }

            var created = new SharedComponentStore<T>();
            _sharedStoresByComponentId[id] = created;
            SharedStoreCache<T>.World = this;
            SharedStoreCache<T>.Store = created;
            return created;
        }

        private static class SparseStoreCache<T> where T : struct
        {
            [ThreadStatic] public static World? World;
            [ThreadStatic] public static SparseSet<T>? Store;
        }

        private static class SharedStoreCache<T> where T : struct
        {
            [ThreadStatic] public static World? World;
            [ThreadStatic] public static SharedComponentStore<T>? Store;
        }

        /// <summary>The entity's sparse-component bit set (<see cref="ComponentMask.Empty"/> if it
        /// has none — the common case, and the reason this isn't a plain field read: an entity that
        /// never touches a sparse component never gets an entry in <see cref="_sparseMasks"/> at
        /// all). Also used by <see cref="Query"/>'s sparse With/Without filtering.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentMask GetSparseMask(int entityId) =>
            _sparseMasks.Has(entityId) ? _sparseMasks.GetRef(entityId) : ComponentMask.Empty;

        /// <summary>Ref to the entity's sparse mask slot, creating an (Empty-initialized) entry
        /// first if it doesn't have one yet. Mutating through the returned ref writes directly into
        /// the <see cref="SparseSet{T}"/>'s backing array — no separate write-back call needed.</summary>
        private ref ComponentMask GetOrCreateSparseMaskRef(int entityId) =>
            ref _sparseMasks.GetOrCreateRef(entityId, ComponentMask.Empty);

        /// <summary>Allocation-free: returns a struct enumerable, not an <c>IEnumerable&lt;T&gt;</c> —
        /// see <see cref="ArchetypeMatchEnumerable"/>.</summary>
        internal ArchetypeMatchEnumerable MatchingArchetypes(
            ComponentMask all, ComponentMask none, ComponentMask any) =>
            new ArchetypeMatchEnumerable(_archetypes, all, none, any);

        private void EnsureRelationsEntry(int entityId)
        {
            if (!_relations.Has(entityId))
                _relations.Set(entityId, default);
        }

        /// <summary>True if <paramref name="potentialAncestor"/> appears somewhere in
        /// <paramref name="entity"/>'s father chain — used by <see cref="SetFather"/> to reject a
        /// reparent that would turn the hierarchy into a cycle.</summary>
        private bool IsAncestorOf(Entity potentialAncestor, Entity entity)
        {
            var current = entity;
            while (_relations.Has(current.Id))
            {
                var father = _relations.GetRef(current.Id).Father;
                if (father.IsNull)
                    return false;
                if (father.Id == potentialAncestor.Id)
                    return true;
                current = father;
            }
            return false;
        }

        /// <summary>Unlinks <paramref name="entity"/> from its father's sibling list (a no-op if it
        /// has no relations entry, or no father) and prunes its <see cref="_relations"/> entry once
        /// it's neither a father nor a child of anything. Used by both the public
        /// <see cref="RemoveFather"/> and internally by <see cref="Destroy"/> — the entity itself is
        /// never touched otherwise, it just stops being linked to its father.</summary>
        private void DetachFromFather(Entity entity)
        {
            if (!_relations.Has(entity.Id))
                return;

            ref var rel = ref _relations.GetRef(entity.Id);
            var father = rel.Father;
            if (father.IsNull)
            {
                TryPruneRelations(entity.Id);
                return;
            }

            var prev = rel.PrevSibling;
            var next = rel.NextSibling;

            if (!prev.IsNull)
                _relations.GetRef(prev.Id).NextSibling = next;
            else if (_relations.Has(father.Id))
                _relations.GetRef(father.Id).FirstChild = next;

            if (!next.IsNull)
                _relations.GetRef(next.Id).PrevSibling = prev;

            rel.Father = Entity.Null;
            rel.PrevSibling = Entity.Null;
            rel.NextSibling = Entity.Null;

            TryPruneRelations(entity.Id);
        }

        /// <summary>Removes the entity's <see cref="_relations"/> entry once it's neither a father
        /// (no children) nor a child (no father) of anything — most entities that ever touch this
        /// table settle back into that state, so this keeps the SparseSet from accumulating dead
        /// weight for entities that briefly had a relationship and no longer do.</summary>
        private void TryPruneRelations(int entityId)
        {
            if (!_relations.Has(entityId))
                return;
            ref var rel = ref _relations.GetRef(entityId);
            if (rel.Father.IsNull && rel.FirstChild.IsNull)
                _relations.Remove(entityId);
        }

        private void RequireAlive(Entity entity)
        {
            if (!IsAlive(entity))
                throw new InvalidOperationException($"{entity} is not alive.");
        }

        private void RequirePlaced(Entity entity)
        {
            if (_records[entity.Id].Archetype == null)
                throw new InvalidOperationException(
                    $"{entity} is reserved by CommandBuffer but has not been played back yet.");
        }

        private void EnsureRecordCapacity(int min)
        {
            if (_records.Length >= min) return;
            Array.Resize(ref _records, Math.Max(min, _records.Length * 2));
        }
    }
}
