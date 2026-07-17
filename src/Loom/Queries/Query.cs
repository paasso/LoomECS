using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Loom.Internal;

namespace Loom.Queries
{
    public delegate void RefAction<T1>(Entity entity, ref T1 c1);
    public delegate void RefAction<T1, T2>(Entity entity, ref T1 c1, ref T2 c2);
    public delegate void RefAction<T1, T2, T3>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3);
    public delegate void RefAction<T1, T2, T3, T4>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4);
    public delegate void RefAction<T1, T2, T3, T4, T5>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5);
    public delegate void RefAction<T1, T2, T3, T4, T5, T6>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5, ref T6 c6);
    public delegate void RefAction<T1, T2, T3, T4, T5, T6, T7>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5, ref T6 c6, ref T7 c7);
    public delegate void RefAction<T1, T2, T3, T4, T5, T6, T7, T8>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5, ref T6 c6, ref T7 c7, ref T8 c8);

    /// <summary>
    /// Builds a filter over dense archetype masks (With/Without narrow which archetypes are
    /// visited at all) plus a sparse <see cref="ComponentMask"/> post-filter (checked per entity
    /// against the entity's sparse mask, since sparse components don't affect archetype
    /// membership). <c>Each&lt;T1&gt;</c> through <c>Each&lt;T1..T8&gt;</c> are the fast path.
    /// Each one takes a single branch, decided once per call (not per row) on whether any of its
    /// type parameters is sparse:
    /// <list type="bullet">
    /// <item>all dense — the original column-only loop: direct array `ref` access, no per-row
    /// branching at all beyond the (already-existing) explicit sparse With/Without check.</item>
    /// <item>any sparse — every parameter is resolved through <see cref="EachSlot{T}"/> instead,
    /// which indexes a dense column per row for dense parameters and a component's
    /// <see cref="SparseSet{T}"/> by entity id for sparse ones. This path always pays the per-row
    /// sparse-mask check, since a sparse component's presence isn't implied by archetype
    /// membership the way a dense one's is.</item>
    /// </list>
    /// An earlier version resolved every parameter through <see cref="EachSlot{T}"/> unconditionally
    /// (dense or not) via a per-row ternary; benchmarking that against the all-dense baseline showed
    /// a real 28-65% regression on <c>Each&lt;Position&gt;</c>/<c>Each&lt;Position,Velocity&gt;</c>
    /// (see benchmarks/README.md), so the all-dense path was restored verbatim and the branch moved
    /// to decide once per call instead of once per row per parameter.
    /// <see cref="ForEach"/> is the general-purpose slow path that also supports sparse With/Without
    /// without needing a per-entity value back.
    /// A Query is scoped to the <see cref="World"/> it was created from — component ids are resolved
    /// via that world's own registry, not a shared/static one.
    /// <c>ref struct</c>: a query is just a small stack-only builder over a couple of
    /// <see cref="ComponentMask"/> values, built and consumed within one fluent chain
    /// (<c>world.Query().With&lt;T&gt;().Each(...)</c>) — making it a ref struct means that chain
    /// allocates nothing on the heap. Cache the masks with <see cref="ToFilter"/> /
    /// <see cref="World.Query(in QueryFilter)"/> when the same filter runs every frame.
    /// The trade-off is the usual one for ref structs: a Query can't
    /// be stored in a field, boxed, captured by a lambda, or held across an `await`; it only ever
    /// lives as a local/temporary within the method that built it.
    /// </summary>
    public ref partial struct Query
    {
        private readonly World _world;
        private ComponentMask _denseAll;
        private ComponentMask _denseNone;
        private ComponentMask _denseAny;
        private ComponentMask _sparseAll;
        private ComponentMask _sparseNone;
        private ComponentMask _sparseAny;

        internal Query(World world)
        {
            _world = world;
            _denseAll = ComponentMask.Empty;
            _denseNone = ComponentMask.Empty;
            _denseAny = ComponentMask.Empty;
            _sparseAll = ComponentMask.Empty;
            _sparseNone = ComponentMask.Empty;
            _sparseAny = ComponentMask.Empty;
        }

        /// <summary>
        /// Starts a query with masks already resolved (typically from
        /// <see cref="ToFilter"/> / a cached <see cref="QueryFilter"/>). Further
        /// <see cref="With{T}"/> / <see cref="Without{T}"/> / <see cref="WithAny{T1}"/> calls still apply.
        /// </summary>
        internal Query(World world, in QueryFilter filter)
        {
            _world = world;
            _denseAll = filter.DenseAll;
            _denseNone = filter.DenseNone;
            _denseAny = filter.DenseAny;
            _sparseAll = filter.SparseAll;
            _sparseNone = filter.SparseNone;
            _sparseAny = filter.SparseAny;
        }

        /// <summary>Captures the current filter masks so they can be reused across frames.</summary>
        public QueryFilter ToFilter() => new QueryFilter(
            _denseAll, _denseNone, _denseAny,
            _sparseAll, _sparseNone, _sparseAny);

        public Query With<T>() where T : struct
        {
            var info = _world.GetComponentInfo<T>();
            if (ComponentTypeTraits<T>.UsesSparseMask)
                _sparseAll = _sparseAll.With(info.Id);
            else
                _denseAll = _denseAll.With(info.Id);
            return this;
        }

        public Query Without<T>() where T : struct
        {
            var info = _world.GetComponentInfo<T>();
            if (ComponentTypeTraits<T>.UsesSparseMask)
                _sparseNone = _sparseNone.With(info.Id);
            else
                _denseNone = _denseNone.With(info.Id);
            return this;
        }

        /// <summary>Match entities that have at least one of the listed components (OR group).
        /// Combines with <see cref="With{T}"/> as AND: must have all With bits and any WithAny bit.</summary>
        public Query WithAny<T1>() where T1 : struct => AddAny<T1>();

        public Query WithAny<T1, T2>() where T1 : struct where T2 : struct
        {
            AddAny<T1>();
            return AddAny<T2>();
        }

        public Query WithAny<T1, T2, T3>()
            where T1 : struct where T2 : struct where T3 : struct
        {
            AddAny<T1>();
            AddAny<T2>();
            return AddAny<T3>();
        }

        public Query WithAny<T1, T2, T3, T4>()
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            AddAny<T1>();
            AddAny<T2>();
            AddAny<T3>();
            return AddAny<T4>();
        }

        /// <summary>Excludes entities tagged with <see cref="Disabled"/>.</summary>
        public Query Enabled() => Without<Disabled>();

        private Query AddAny<T>() where T : struct
        {
            var info = _world.GetComponentInfo<T>();
            if (ComponentTypeTraits<T>.UsesSparseMask)
                _sparseAny = _sparseAny.With(info.Id);
            else
                _denseAny = _denseAny.With(info.Id);
            return this;
        }

        /// <summary>General-purpose iteration; supports sparse filters but pays a per-entity lookup for them.</summary>
        public void ForEach(Action<Entity> action)
        {
            bool filtered = HasSparseFilter;
            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                foreach (var chunk in archetype.Chunks)
                {
                    var entities = chunk.Entities;
                    int count = chunk.Count;
                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i]);
                    }
                }
            }
        }

        public List<Entity> ToList()
        {
            var result = new List<Entity>();
            bool filtered = HasSparseFilter;
            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                foreach (var chunk in archetype.Chunks)
                {
                    var entities = chunk.Entities;
                    int count = chunk.Count;
                    for (int i = 0; i < count; i++)
                    {
                        if (!filtered || PassesSparseFilters(entities[i].Id))
                            result.Add(entities[i]);
                    }
                }
            }
            return result;
        }

        /// <summary>Fast path: direct ref access to a component, dense or sparse. See the <see cref="Query"/>
        /// class summary for how the all-dense vs any-sparse branch is chosen.</summary>
        public void Each<T1>(RefAction<T1> action) where T1 : struct
        {
            RequireNotEmpty<T1>();
            With<T1>();

            if (ComponentTypeTraits<T1>.UsesSparseMask)
            {
                var s1 = new EachSlot<T1>(_world);
                foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
                {
                    s1.EnterArchetype(archetype);
                    foreach (var chunk in archetype.Chunks)
                    {
                        s1.EnterChunk(chunk);
                        var entities = chunk.Entities;
                        int count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int id = entities[i].Id;
                            if (PassesSparseFilters(id))
                                action(entities[i], ref s1.GetRef(i, id));
                        }
                    }
                }
                return;
            }

            int id1 = _world.GetComponentInfo<T1>().Id;
            bool filtered = HasSparseFilter;

            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1Index = archetype.ColumnIndex(id1);
                var chunks = archetype.Chunks;
                for (int ci = 0; ci < chunks.Count; ci++)
                {
                    var chunk = chunks[ci];
                    int count = chunk.Count;
                    if (count == 0)
                        continue;

                    var entities = chunk.Entities;
                    var items1 = chunk.GetItems<T1>(col1Index);

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i], ref items1[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i], ref items1[i]);
                    }
                }
            }
        }

        /// <summary>Fast path over two components at once, dense or sparse. See the <see cref="Query"/> class summary.</summary>
        public void Each<T1, T2>(RefAction<T1, T2> action) where T1 : struct where T2 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            With<T1>();
            With<T2>();

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask)
            {
                var s1 = new EachSlot<T1>(_world);
                var s2 = new EachSlot<T2>(_world);
                foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
                {
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    foreach (var chunk in archetype.Chunks)
                    {
                        s1.EnterChunk(chunk);
                        s2.EnterChunk(chunk);
                        var entities = chunk.Entities;
                        int count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int id = entities[i].Id;
                            if (PassesSparseFilters(id))
                                action(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id));
                        }
                    }
                }
                return;
            }

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            bool filtered = HasSparseFilter;

            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1Index = archetype.ColumnIndex(id1);
                int col2Index = archetype.ColumnIndex(id2);
                var chunks = archetype.Chunks;
                for (int ci = 0; ci < chunks.Count; ci++)
                {
                    var chunk = chunks[ci];
                    int count = chunk.Count;
                    if (count == 0)
                        continue;

                    var entities = chunk.Entities;
                    var items1 = chunk.GetItems<T1>(col1Index);
                    var items2 = chunk.GetItems<T2>(col2Index);

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i], ref items1[i], ref items2[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i], ref items1[i], ref items2[i]);
                    }
                }
            }
        }

        /// <summary>Fast path over three components at once, dense or sparse. See the <see cref="Query"/> class summary.</summary>
        public void Each<T1, T2, T3>(RefAction<T1, T2, T3> action) where T1 : struct where T2 : struct where T3 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            With<T1>();
            With<T2>();
            With<T3>();

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask || ComponentTypeTraits<T3>.UsesSparseMask)
            {
                var s1 = new EachSlot<T1>(_world);
                var s2 = new EachSlot<T2>(_world);
                var s3 = new EachSlot<T3>(_world);
                foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
                {
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s3.EnterArchetype(archetype);
                    foreach (var chunk in archetype.Chunks)
                    {
                        s1.EnterChunk(chunk);
                        s2.EnterChunk(chunk);
                        s3.EnterChunk(chunk);
                        var entities = chunk.Entities;
                        int count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int id = entities[i].Id;
                            if (PassesSparseFilters(id))
                                action(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id));
                        }
                    }
                }
                return;
            }

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            int id3 = _world.GetComponentInfo<T3>().Id;
            bool filtered = HasSparseFilter;

            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1Index = archetype.ColumnIndex(id1);
                int col2Index = archetype.ColumnIndex(id2);
                int col3Index = archetype.ColumnIndex(id3);
                foreach (var chunk in archetype.Chunks)
                {
                    var entities = chunk.Entities;
                    var items1 = chunk.GetItems<T1>(col1Index);
                    var items2 = chunk.GetItems<T2>(col2Index);
                    var items3 = chunk.GetItems<T3>(col3Index);
                    int count = chunk.Count;

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i], ref items1[i], ref items2[i], ref items3[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i], ref items1[i], ref items2[i], ref items3[i]);
                    }
                }
            }
        }

        /// <summary>Fast path over four components at once, dense or sparse. See the <see cref="Query"/> class summary.</summary>
        public void Each<T1, T2, T3, T4>(RefAction<T1, T2, T3, T4> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask || ComponentTypeTraits<T3>.UsesSparseMask || ComponentTypeTraits<T4>.UsesSparseMask)
            {
                var s1 = new EachSlot<T1>(_world);
                var s2 = new EachSlot<T2>(_world);
                var s3 = new EachSlot<T3>(_world);
                var s4 = new EachSlot<T4>(_world);
                foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
                {
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s3.EnterArchetype(archetype);
                    s4.EnterArchetype(archetype);
                    foreach (var chunk in archetype.Chunks)
                    {
                        s1.EnterChunk(chunk);
                        s2.EnterChunk(chunk);
                        s3.EnterChunk(chunk);
                        s4.EnterChunk(chunk);
                        var entities = chunk.Entities;
                        int count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int id = entities[i].Id;
                            if (PassesSparseFilters(id))
                                action(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id));
                        }
                    }
                }
                return;
            }

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            int id3 = _world.GetComponentInfo<T3>().Id;
            int id4 = _world.GetComponentInfo<T4>().Id;
            bool filtered = HasSparseFilter;

            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1Index = archetype.ColumnIndex(id1);
                int col2Index = archetype.ColumnIndex(id2);
                int col3Index = archetype.ColumnIndex(id3);
                int col4Index = archetype.ColumnIndex(id4);
                foreach (var chunk in archetype.Chunks)
                {
                    var entities = chunk.Entities;
                    var items1 = chunk.GetItems<T1>(col1Index);
                    var items2 = chunk.GetItems<T2>(col2Index);
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    int count = chunk.Count;

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i]);
                    }
                }
            }
        }

        /// <summary>Fast path over five components at once, dense or sparse. See the <see cref="Query"/> class summary.</summary>
        public void Each<T1, T2, T3, T4, T5>(RefAction<T1, T2, T3, T4, T5> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            RequireNotEmpty<T5>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            With<T5>();

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask || ComponentTypeTraits<T3>.UsesSparseMask
                || ComponentTypeTraits<T4>.UsesSparseMask || ComponentTypeTraits<T5>.UsesSparseMask)
            {
                var s1 = new EachSlot<T1>(_world);
                var s2 = new EachSlot<T2>(_world);
                var s3 = new EachSlot<T3>(_world);
                var s4 = new EachSlot<T4>(_world);
                var s5 = new EachSlot<T5>(_world);
                foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
                {
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s3.EnterArchetype(archetype);
                    s4.EnterArchetype(archetype);
                    s5.EnterArchetype(archetype);
                    foreach (var chunk in archetype.Chunks)
                    {
                        s1.EnterChunk(chunk);
                        s2.EnterChunk(chunk);
                        s3.EnterChunk(chunk);
                        s4.EnterChunk(chunk);
                        s5.EnterChunk(chunk);
                        var entities = chunk.Entities;
                        int count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int id = entities[i].Id;
                            if (PassesSparseFilters(id))
                                action(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id), ref s5.GetRef(i, id));
                        }
                    }
                }
                return;
            }

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            int id3 = _world.GetComponentInfo<T3>().Id;
            int id4 = _world.GetComponentInfo<T4>().Id;
            int id5 = _world.GetComponentInfo<T5>().Id;
            bool filtered = HasSparseFilter;

            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1Index = archetype.ColumnIndex(id1);
                int col2Index = archetype.ColumnIndex(id2);
                int col3Index = archetype.ColumnIndex(id3);
                int col4Index = archetype.ColumnIndex(id4);
                int col5Index = archetype.ColumnIndex(id5);
                foreach (var chunk in archetype.Chunks)
                {
                    var entities = chunk.Entities;
                    var items1 = chunk.GetItems<T1>(col1Index);
                    var items2 = chunk.GetItems<T2>(col2Index);
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    var items5 = chunk.GetItems<T5>(col5Index);
                    int count = chunk.Count;

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i]);
                    }
                }
            }
        }

        /// <summary>Fast path over six components at once, dense or sparse. See the <see cref="Query"/> class summary.</summary>
        public void Each<T1, T2, T3, T4, T5, T6>(RefAction<T1, T2, T3, T4, T5, T6> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            RequireNotEmpty<T5>();
            RequireNotEmpty<T6>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            With<T5>();
            With<T6>();

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask || ComponentTypeTraits<T3>.UsesSparseMask
                || ComponentTypeTraits<T4>.UsesSparseMask || ComponentTypeTraits<T5>.UsesSparseMask || ComponentTypeTraits<T6>.UsesSparseMask)
            {
                var s1 = new EachSlot<T1>(_world);
                var s2 = new EachSlot<T2>(_world);
                var s3 = new EachSlot<T3>(_world);
                var s4 = new EachSlot<T4>(_world);
                var s5 = new EachSlot<T5>(_world);
                var s6 = new EachSlot<T6>(_world);
                foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
                {
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s3.EnterArchetype(archetype);
                    s4.EnterArchetype(archetype);
                    s5.EnterArchetype(archetype);
                    s6.EnterArchetype(archetype);
                    foreach (var chunk in archetype.Chunks)
                    {
                        s1.EnterChunk(chunk);
                        s2.EnterChunk(chunk);
                        s3.EnterChunk(chunk);
                        s4.EnterChunk(chunk);
                        s5.EnterChunk(chunk);
                        s6.EnterChunk(chunk);
                        var entities = chunk.Entities;
                        int count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int id = entities[i].Id;
                            if (PassesSparseFilters(id))
                                action(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id), ref s5.GetRef(i, id), ref s6.GetRef(i, id));
                        }
                    }
                }
                return;
            }

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            int id3 = _world.GetComponentInfo<T3>().Id;
            int id4 = _world.GetComponentInfo<T4>().Id;
            int id5 = _world.GetComponentInfo<T5>().Id;
            int id6 = _world.GetComponentInfo<T6>().Id;
            bool filtered = HasSparseFilter;

            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1Index = archetype.ColumnIndex(id1);
                int col2Index = archetype.ColumnIndex(id2);
                int col3Index = archetype.ColumnIndex(id3);
                int col4Index = archetype.ColumnIndex(id4);
                int col5Index = archetype.ColumnIndex(id5);
                int col6Index = archetype.ColumnIndex(id6);
                foreach (var chunk in archetype.Chunks)
                {
                    var entities = chunk.Entities;
                    var items1 = chunk.GetItems<T1>(col1Index);
                    var items2 = chunk.GetItems<T2>(col2Index);
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    var items5 = chunk.GetItems<T5>(col5Index);
                    var items6 = chunk.GetItems<T6>(col6Index);
                    int count = chunk.Count;

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i]);
                    }
                }
            }
        }

        /// <summary>Fast path over seven components at once, dense or sparse. See the <see cref="Query"/> class summary.</summary>
        public void Each<T1, T2, T3, T4, T5, T6, T7>(RefAction<T1, T2, T3, T4, T5, T6, T7> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            RequireNotEmpty<T5>();
            RequireNotEmpty<T6>();
            RequireNotEmpty<T7>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            With<T5>();
            With<T6>();
            With<T7>();

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask || ComponentTypeTraits<T3>.UsesSparseMask
                || ComponentTypeTraits<T4>.UsesSparseMask || ComponentTypeTraits<T5>.UsesSparseMask || ComponentTypeTraits<T6>.UsesSparseMask
                || ComponentTypeTraits<T7>.UsesSparseMask)
            {
                var s1 = new EachSlot<T1>(_world);
                var s2 = new EachSlot<T2>(_world);
                var s3 = new EachSlot<T3>(_world);
                var s4 = new EachSlot<T4>(_world);
                var s5 = new EachSlot<T5>(_world);
                var s6 = new EachSlot<T6>(_world);
                var s7 = new EachSlot<T7>(_world);
                foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
                {
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s3.EnterArchetype(archetype);
                    s4.EnterArchetype(archetype);
                    s5.EnterArchetype(archetype);
                    s6.EnterArchetype(archetype);
                    s7.EnterArchetype(archetype);
                    foreach (var chunk in archetype.Chunks)
                    {
                        s1.EnterChunk(chunk);
                        s2.EnterChunk(chunk);
                        s3.EnterChunk(chunk);
                        s4.EnterChunk(chunk);
                        s5.EnterChunk(chunk);
                        s6.EnterChunk(chunk);
                        s7.EnterChunk(chunk);
                        var entities = chunk.Entities;
                        int count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int id = entities[i].Id;
                            if (PassesSparseFilters(id))
                                action(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id), ref s5.GetRef(i, id), ref s6.GetRef(i, id), ref s7.GetRef(i, id));
                        }
                    }
                }
                return;
            }

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            int id3 = _world.GetComponentInfo<T3>().Id;
            int id4 = _world.GetComponentInfo<T4>().Id;
            int id5 = _world.GetComponentInfo<T5>().Id;
            int id6 = _world.GetComponentInfo<T6>().Id;
            int id7 = _world.GetComponentInfo<T7>().Id;
            bool filtered = HasSparseFilter;

            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1Index = archetype.ColumnIndex(id1);
                int col2Index = archetype.ColumnIndex(id2);
                int col3Index = archetype.ColumnIndex(id3);
                int col4Index = archetype.ColumnIndex(id4);
                int col5Index = archetype.ColumnIndex(id5);
                int col6Index = archetype.ColumnIndex(id6);
                int col7Index = archetype.ColumnIndex(id7);
                foreach (var chunk in archetype.Chunks)
                {
                    var entities = chunk.Entities;
                    var items1 = chunk.GetItems<T1>(col1Index);
                    var items2 = chunk.GetItems<T2>(col2Index);
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    var items5 = chunk.GetItems<T5>(col5Index);
                    var items6 = chunk.GetItems<T6>(col6Index);
                    var items7 = chunk.GetItems<T7>(col7Index);
                    int count = chunk.Count;

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i], ref items7[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i], ref items7[i]);
                    }
                }
            }
        }

        /// <summary>Fast path over eight components at once, dense or sparse. See the <see cref="Query"/> class summary.</summary>
        public void Each<T1, T2, T3, T4, T5, T6, T7, T8>(RefAction<T1, T2, T3, T4, T5, T6, T7, T8> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            RequireNotEmpty<T5>();
            RequireNotEmpty<T6>();
            RequireNotEmpty<T7>();
            RequireNotEmpty<T8>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            With<T5>();
            With<T6>();
            With<T7>();
            With<T8>();

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask || ComponentTypeTraits<T3>.UsesSparseMask
                || ComponentTypeTraits<T4>.UsesSparseMask || ComponentTypeTraits<T5>.UsesSparseMask || ComponentTypeTraits<T6>.UsesSparseMask
                || ComponentTypeTraits<T7>.UsesSparseMask || ComponentTypeTraits<T8>.UsesSparseMask)
            {
                var s1 = new EachSlot<T1>(_world);
                var s2 = new EachSlot<T2>(_world);
                var s3 = new EachSlot<T3>(_world);
                var s4 = new EachSlot<T4>(_world);
                var s5 = new EachSlot<T5>(_world);
                var s6 = new EachSlot<T6>(_world);
                var s7 = new EachSlot<T7>(_world);
                var s8 = new EachSlot<T8>(_world);
                foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
                {
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s3.EnterArchetype(archetype);
                    s4.EnterArchetype(archetype);
                    s5.EnterArchetype(archetype);
                    s6.EnterArchetype(archetype);
                    s7.EnterArchetype(archetype);
                    s8.EnterArchetype(archetype);
                    foreach (var chunk in archetype.Chunks)
                    {
                        s1.EnterChunk(chunk);
                        s2.EnterChunk(chunk);
                        s3.EnterChunk(chunk);
                        s4.EnterChunk(chunk);
                        s5.EnterChunk(chunk);
                        s6.EnterChunk(chunk);
                        s7.EnterChunk(chunk);
                        s8.EnterChunk(chunk);
                        var entities = chunk.Entities;
                        int count = chunk.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int id = entities[i].Id;
                            if (PassesSparseFilters(id))
                                action(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id), ref s5.GetRef(i, id), ref s6.GetRef(i, id), ref s7.GetRef(i, id), ref s8.GetRef(i, id));
                        }
                    }
                }
                return;
            }

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            int id3 = _world.GetComponentInfo<T3>().Id;
            int id4 = _world.GetComponentInfo<T4>().Id;
            int id5 = _world.GetComponentInfo<T5>().Id;
            int id6 = _world.GetComponentInfo<T6>().Id;
            int id7 = _world.GetComponentInfo<T7>().Id;
            int id8 = _world.GetComponentInfo<T8>().Id;
            bool filtered = HasSparseFilter;

            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1Index = archetype.ColumnIndex(id1);
                int col2Index = archetype.ColumnIndex(id2);
                int col3Index = archetype.ColumnIndex(id3);
                int col4Index = archetype.ColumnIndex(id4);
                int col5Index = archetype.ColumnIndex(id5);
                int col6Index = archetype.ColumnIndex(id6);
                int col7Index = archetype.ColumnIndex(id7);
                int col8Index = archetype.ColumnIndex(id8);
                foreach (var chunk in archetype.Chunks)
                {
                    var entities = chunk.Entities;
                    var items1 = chunk.GetItems<T1>(col1Index);
                    var items2 = chunk.GetItems<T2>(col2Index);
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    var items5 = chunk.GetItems<T5>(col5Index);
                    var items6 = chunk.GetItems<T6>(col6Index);
                    var items7 = chunk.GetItems<T7>(col7Index);
                    var items8 = chunk.GetItems<T8>(col8Index);
                    int count = chunk.Count;

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i], ref items7[i], ref items8[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            action(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i], ref items7[i], ref items8[i]);
                    }
                }
            }
        }

        private static void RequireNotEmpty<T>() where T : struct
        {
            if (ComponentTypeTraits<T>.IsEmpty)
                throw new InvalidOperationException(
                    $"Each<T>() requires a component with data; {typeof(T).Name} has no fields, so there's no " +
                    "per-entity value to hand back a ref to. Use With<T>()/Without<T>() to filter by presence instead.");
        }

        private bool HasSparseFilter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                _sparseAll != ComponentMask.Empty
                || _sparseNone != ComponentMask.Empty
                || _sparseAny != ComponentMask.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool PassesSparseFilters(int entityId)
        {
            var mask = _world.GetSparseMask(entityId);
            if (!mask.ContainsAll(_sparseAll) || mask.IntersectsAny(_sparseNone))
                return false;
            if (_sparseAny != ComponentMask.Empty && !mask.IntersectsAny(_sparseAny))
                return false;
            return true;
        }

        /// <summary>Per-type-parameter accessor used only by the side-storage (sparse/shared) branch of
        /// <c>Each&lt;T1..T8&gt;</c> (never touched by the all-dense fast path — see the
        /// <see cref="Query"/> class summary for why that distinction matters). For a dense
        /// component it resolves a column index once per archetype and the backing array once per
        /// chunk. For sparse/shared there's no column — values are resolved by entity id. Callers
        /// only ever call <see cref="GetRef"/> for entities that already passed the sparse mask
        /// filter. Assumes T is not <see cref="ComponentTypeTraits{T}.IsEmpty"/>.</summary>
        private struct EachSlot<T> where T : struct
        {
            private readonly byte _kind; // 0 dense, 1 sparse, 2 shared
            private readonly int _id;
            private readonly SparseSet<T>? _sparseSet;
            private readonly SharedComponentStore<T>? _sharedStore;
            private int _colIndex;
            private T[] _items;

            public EachSlot(World world)
            {
                _id = world.GetComponentInfo<T>().Id;
                if (ComponentTypeTraits<T>.IsShared)
                {
                    _kind = 2;
                    _sharedStore = world.GetOrCreateSharedStore<T>(_id);
                    _sparseSet = null;
                }
                else if (ComponentTypeTraits<T>.IsSparse)
                {
                    _kind = 1;
                    _sparseSet = world.GetOrCreateSparseSet<T>(_id);
                    _sharedStore = null;
                }
                else
                {
                    _kind = 0;
                    _sparseSet = null;
                    _sharedStore = null;
                }

                _colIndex = 0;
                _items = Array.Empty<T>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnterArchetype(Archetype archetype)
            {
                if (_kind == 0)
                    _colIndex = archetype.ColumnIndex(_id);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnterChunk(Chunk chunk)
            {
                if (_kind == 0)
                    _items = chunk.GetItems<T>(_colIndex);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref T GetRef(int row, int entityId)
            {
                if (_kind == 1)
                    return ref _sparseSet!.GetRef(entityId);
                if (_kind == 2)
                    return ref _sharedStore!.GetRef(entityId);
                return ref _items[row];
            }
        }
    }
}
