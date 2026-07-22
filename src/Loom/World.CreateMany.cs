using System;
using Loom.Internal;

namespace Loom
{
    public sealed partial class World
    {
        /// <summary>Creates <paramref name="destination"/>.Length entities each carrying the same
        /// four components — see <see cref="CreateMany"/> and <see cref="Create{T1,T2,T3,T4}"/>.</summary>
        public void CreateMany<T1, T2, T3, T4>(
            Span<Entity> destination, T1 component1, T2 component2, T3 component3, T4 component4)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            int count = destination.Length;
            if (count == 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();
            var info4 = _componentTypes.GetOrRegister<T4>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info3.Id);
            if (!ComponentTypeTraits<T4>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info4.Id);

            PrefetchCreateCapacity(count, archetype);

            bool allDenseData =
                IsDenseData<T1>() && IsDenseData<T2>() && IsDenseData<T3>() && IsDenseData<T4>();

            if (allDenseData)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                int col2 = archetype.ColumnIndex(info2.Id);
                int col3 = archetype.ColumnIndex(info3.Id);
                int col4 = archetype.ColumnIndex(info4.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;
                T2[]? items2 = null;
                T3[]? items3 = null;
                T4[]? items4 = null;

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
                        items4 = chunk.GetItems<T4>(col4);
                    }

                    items1![index] = component1;
                    items2![index] = component2;
                    items3![index] = component3;
                    items4![index] = component4;
                    destination[i] = entity;
                    RecordAdded<T1>(entity);
                    RecordAdded<T2>(entity);
                    RecordAdded<T3>(entity);
                    RecordAdded<T4>(entity);
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
                    SetNewComponent(entity, archetype, chunk, index, info4.Id, component4);
                    destination[i] = entity;
                }
            }

            _liveCount += count;
        }

        /// <summary>Creates <paramref name="destination"/>.Length entities each carrying the same
        /// five components — see <see cref="CreateMany"/>.</summary>
        public void CreateMany<T1, T2, T3, T4, T5>(
            Span<Entity> destination,
            T1 component1, T2 component2, T3 component3, T4 component4, T5 component5)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            int count = destination.Length;
            if (count == 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();
            var info4 = _componentTypes.GetOrRegister<T4>();
            var info5 = _componentTypes.GetOrRegister<T5>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info3.Id);
            if (!ComponentTypeTraits<T4>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info4.Id);
            if (!ComponentTypeTraits<T5>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info5.Id);

            PrefetchCreateCapacity(count, archetype);

            bool allDenseData =
                IsDenseData<T1>() && IsDenseData<T2>() && IsDenseData<T3>() && IsDenseData<T4>() && IsDenseData<T5>();

            if (allDenseData)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                int col2 = archetype.ColumnIndex(info2.Id);
                int col3 = archetype.ColumnIndex(info3.Id);
                int col4 = archetype.ColumnIndex(info4.Id);
                int col5 = archetype.ColumnIndex(info5.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;
                T2[]? items2 = null;
                T3[]? items3 = null;
                T4[]? items4 = null;
                T5[]? items5 = null;

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
                        items4 = chunk.GetItems<T4>(col4);
                        items5 = chunk.GetItems<T5>(col5);
                    }

                    items1![index] = component1;
                    items2![index] = component2;
                    items3![index] = component3;
                    items4![index] = component4;
                    items5![index] = component5;
                    destination[i] = entity;
                    RecordAdded<T1>(entity);
                    RecordAdded<T2>(entity);
                    RecordAdded<T3>(entity);
                    RecordAdded<T4>(entity);
                    RecordAdded<T5>(entity);
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
                    SetNewComponent(entity, archetype, chunk, index, info4.Id, component4);
                    SetNewComponent(entity, archetype, chunk, index, info5.Id, component5);
                    destination[i] = entity;
                }
            }

            _liveCount += count;
        }

        /// <summary>Creates <paramref name="destination"/>.Length entities each carrying the same
        /// six components — see <see cref="CreateMany"/>.</summary>
        public void CreateMany<T1, T2, T3, T4, T5, T6>(
            Span<Entity> destination,
            T1 component1, T2 component2, T3 component3, T4 component4, T5 component5, T6 component6)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
            where T5 : struct where T6 : struct
        {
            int count = destination.Length;
            if (count == 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();
            var info4 = _componentTypes.GetOrRegister<T4>();
            var info5 = _componentTypes.GetOrRegister<T5>();
            var info6 = _componentTypes.GetOrRegister<T6>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info3.Id);
            if (!ComponentTypeTraits<T4>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info4.Id);
            if (!ComponentTypeTraits<T5>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info5.Id);
            if (!ComponentTypeTraits<T6>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info6.Id);

            PrefetchCreateCapacity(count, archetype);

            bool allDenseData =
                IsDenseData<T1>() && IsDenseData<T2>() && IsDenseData<T3>() && IsDenseData<T4>() &&
                IsDenseData<T5>() && IsDenseData<T6>();

            if (allDenseData)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                int col2 = archetype.ColumnIndex(info2.Id);
                int col3 = archetype.ColumnIndex(info3.Id);
                int col4 = archetype.ColumnIndex(info4.Id);
                int col5 = archetype.ColumnIndex(info5.Id);
                int col6 = archetype.ColumnIndex(info6.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;
                T2[]? items2 = null;
                T3[]? items3 = null;
                T4[]? items4 = null;
                T5[]? items5 = null;
                T6[]? items6 = null;

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
                        items4 = chunk.GetItems<T4>(col4);
                        items5 = chunk.GetItems<T5>(col5);
                        items6 = chunk.GetItems<T6>(col6);
                    }

                    items1![index] = component1;
                    items2![index] = component2;
                    items3![index] = component3;
                    items4![index] = component4;
                    items5![index] = component5;
                    items6![index] = component6;
                    destination[i] = entity;
                    RecordAdded<T1>(entity);
                    RecordAdded<T2>(entity);
                    RecordAdded<T3>(entity);
                    RecordAdded<T4>(entity);
                    RecordAdded<T5>(entity);
                    RecordAdded<T6>(entity);
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
                    SetNewComponent(entity, archetype, chunk, index, info4.Id, component4);
                    SetNewComponent(entity, archetype, chunk, index, info5.Id, component5);
                    SetNewComponent(entity, archetype, chunk, index, info6.Id, component6);
                    destination[i] = entity;
                }
            }

            _liveCount += count;
        }

        /// <summary>Creates <paramref name="destination"/>.Length entities each carrying the same
        /// seven components — see <see cref="CreateMany"/>.</summary>
        public void CreateMany<T1, T2, T3, T4, T5, T6, T7>(
            Span<Entity> destination,
            T1 component1, T2 component2, T3 component3, T4 component4,
            T5 component5, T6 component6, T7 component7)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
            where T5 : struct where T6 : struct where T7 : struct
        {
            int count = destination.Length;
            if (count == 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();
            var info4 = _componentTypes.GetOrRegister<T4>();
            var info5 = _componentTypes.GetOrRegister<T5>();
            var info6 = _componentTypes.GetOrRegister<T6>();
            var info7 = _componentTypes.GetOrRegister<T7>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info3.Id);
            if (!ComponentTypeTraits<T4>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info4.Id);
            if (!ComponentTypeTraits<T5>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info5.Id);
            if (!ComponentTypeTraits<T6>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info6.Id);
            if (!ComponentTypeTraits<T7>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info7.Id);

            PrefetchCreateCapacity(count, archetype);

            bool allDenseData =
                IsDenseData<T1>() && IsDenseData<T2>() && IsDenseData<T3>() && IsDenseData<T4>() &&
                IsDenseData<T5>() && IsDenseData<T6>() && IsDenseData<T7>();

            if (allDenseData)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                int col2 = archetype.ColumnIndex(info2.Id);
                int col3 = archetype.ColumnIndex(info3.Id);
                int col4 = archetype.ColumnIndex(info4.Id);
                int col5 = archetype.ColumnIndex(info5.Id);
                int col6 = archetype.ColumnIndex(info6.Id);
                int col7 = archetype.ColumnIndex(info7.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;
                T2[]? items2 = null;
                T3[]? items3 = null;
                T4[]? items4 = null;
                T5[]? items5 = null;
                T6[]? items6 = null;
                T7[]? items7 = null;

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
                        items4 = chunk.GetItems<T4>(col4);
                        items5 = chunk.GetItems<T5>(col5);
                        items6 = chunk.GetItems<T6>(col6);
                        items7 = chunk.GetItems<T7>(col7);
                    }

                    items1![index] = component1;
                    items2![index] = component2;
                    items3![index] = component3;
                    items4![index] = component4;
                    items5![index] = component5;
                    items6![index] = component6;
                    items7![index] = component7;
                    destination[i] = entity;
                    RecordAdded<T1>(entity);
                    RecordAdded<T2>(entity);
                    RecordAdded<T3>(entity);
                    RecordAdded<T4>(entity);
                    RecordAdded<T5>(entity);
                    RecordAdded<T6>(entity);
                    RecordAdded<T7>(entity);
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
                    SetNewComponent(entity, archetype, chunk, index, info4.Id, component4);
                    SetNewComponent(entity, archetype, chunk, index, info5.Id, component5);
                    SetNewComponent(entity, archetype, chunk, index, info6.Id, component6);
                    SetNewComponent(entity, archetype, chunk, index, info7.Id, component7);
                    destination[i] = entity;
                }
            }

            _liveCount += count;
        }

        /// <summary>Creates <paramref name="destination"/>.Length entities each carrying the same
        /// eight components — see <see cref="CreateMany"/>.</summary>
        public void CreateMany<T1, T2, T3, T4, T5, T6, T7, T8>(
            Span<Entity> destination,
            T1 component1, T2 component2, T3 component3, T4 component4,
            T5 component5, T6 component6, T7 component7, T8 component8)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
            where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            int count = destination.Length;
            if (count == 0)
                return;

            var info1 = _componentTypes.GetOrRegister<T1>();
            var info2 = _componentTypes.GetOrRegister<T2>();
            var info3 = _componentTypes.GetOrRegister<T3>();
            var info4 = _componentTypes.GetOrRegister<T4>();
            var info5 = _componentTypes.GetOrRegister<T5>();
            var info6 = _componentTypes.GetOrRegister<T6>();
            var info7 = _componentTypes.GetOrRegister<T7>();
            var info8 = _componentTypes.GetOrRegister<T8>();

            var archetype = _emptyArchetype;
            if (!ComponentTypeTraits<T1>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info1.Id);
            if (!ComponentTypeTraits<T2>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info2.Id);
            if (!ComponentTypeTraits<T3>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info3.Id);
            if (!ComponentTypeTraits<T4>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info4.Id);
            if (!ComponentTypeTraits<T5>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info5.Id);
            if (!ComponentTypeTraits<T6>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info6.Id);
            if (!ComponentTypeTraits<T7>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info7.Id);
            if (!ComponentTypeTraits<T8>.UsesSparseMask) archetype = GetArchetypeViaAddEdge(archetype, info8.Id);

            PrefetchCreateCapacity(count, archetype);

            bool allDenseData =
                IsDenseData<T1>() && IsDenseData<T2>() && IsDenseData<T3>() && IsDenseData<T4>() &&
                IsDenseData<T5>() && IsDenseData<T6>() && IsDenseData<T7>() && IsDenseData<T8>();

            if (allDenseData)
            {
                int col1 = archetype.ColumnIndex(info1.Id);
                int col2 = archetype.ColumnIndex(info2.Id);
                int col3 = archetype.ColumnIndex(info3.Id);
                int col4 = archetype.ColumnIndex(info4.Id);
                int col5 = archetype.ColumnIndex(info5.Id);
                int col6 = archetype.ColumnIndex(info6.Id);
                int col7 = archetype.ColumnIndex(info7.Id);
                int col8 = archetype.ColumnIndex(info8.Id);
                Chunk? currentChunk = null;
                T1[]? items1 = null;
                T2[]? items2 = null;
                T3[]? items3 = null;
                T4[]? items4 = null;
                T5[]? items5 = null;
                T6[]? items6 = null;
                T7[]? items7 = null;
                T8[]? items8 = null;

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
                        items4 = chunk.GetItems<T4>(col4);
                        items5 = chunk.GetItems<T5>(col5);
                        items6 = chunk.GetItems<T6>(col6);
                        items7 = chunk.GetItems<T7>(col7);
                        items8 = chunk.GetItems<T8>(col8);
                    }

                    items1![index] = component1;
                    items2![index] = component2;
                    items3![index] = component3;
                    items4![index] = component4;
                    items5![index] = component5;
                    items6![index] = component6;
                    items7![index] = component7;
                    items8![index] = component8;
                    destination[i] = entity;
                    RecordAdded<T1>(entity);
                    RecordAdded<T2>(entity);
                    RecordAdded<T3>(entity);
                    RecordAdded<T4>(entity);
                    RecordAdded<T5>(entity);
                    RecordAdded<T6>(entity);
                    RecordAdded<T7>(entity);
                    RecordAdded<T8>(entity);
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
                    SetNewComponent(entity, archetype, chunk, index, info4.Id, component4);
                    SetNewComponent(entity, archetype, chunk, index, info5.Id, component5);
                    SetNewComponent(entity, archetype, chunk, index, info6.Id, component6);
                    SetNewComponent(entity, archetype, chunk, index, info7.Id, component7);
                    SetNewComponent(entity, archetype, chunk, index, info8.Id, component8);
                    destination[i] = entity;
                }
            }

            _liveCount += count;
        }

        private static bool IsDenseData<T>() where T : struct =>
            !ComponentTypeTraits<T>.UsesSparseMask && !ComponentTypeTraits<T>.IsEmpty;
    }
}
