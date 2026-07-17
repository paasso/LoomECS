using System;
using Loom.Internal;

namespace Loom.Queries
{
    public ref partial struct Query
    {
        /// <summary>
        /// Runs a struct <see cref="IJob{T1}"/> over matching entities. <typeparamref name="TJob"/> must be a
        /// concrete <c>struct</c> (not the interface) so the JIT can devirtualize/inline
        /// <see cref="IJob{T1}.Execute"/>. Pass <paramref name="job"/> by <c>ref</c> so mutable job state
        /// (counters, accumulators) survives the call.
        /// </summary>
        public void Each<TJob, T1>(ref TJob job)
            where TJob : struct, IJob<T1>
            where T1 : struct
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
                                job.Execute(entities[i], ref s1.GetRef(i, id));
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
                                job.Execute(entities[i], ref items1[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            job.Execute(entities[i], ref items1[i]);
                    }
                }
            }
        }

        public void Each<TJob, T1>(TJob job)
            where TJob : struct, IJob<T1>
            where T1 : struct
            => Each<TJob, T1>(ref job);

        public void Each<TJob, T1, T2>(ref TJob job)
            where TJob : struct, IJob<T1, T2>
            where T1 : struct
            where T2 : struct
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
                                job.Execute(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id));
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
                                job.Execute(entities[i], ref items1[i], ref items2[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            job.Execute(entities[i], ref items1[i], ref items2[i]);
                    }
                }
            }
        }

        public void Each<TJob, T1, T2>(TJob job)
            where TJob : struct, IJob<T1, T2>
            where T1 : struct
            where T2 : struct
            => Each<TJob, T1, T2>(ref job);

        public void Each<TJob, T1, T2, T3>(ref TJob job)
            where TJob : struct, IJob<T1, T2, T3>
            where T1 : struct
            where T2 : struct
            where T3 : struct
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
                                job.Execute(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id));
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
                    var items3 = chunk.GetItems<T3>(col3Index);

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i]);
                    }
                }
            }
        }

        public void Each<TJob, T1, T2, T3>(TJob job)
            where TJob : struct, IJob<T1, T2, T3>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            => Each<TJob, T1, T2, T3>(ref job);

        public void Each<TJob, T1, T2, T3, T4>(ref TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
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
                                job.Execute(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id));
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
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i]);
                    }
                }
            }
        }

        public void Each<TJob, T1, T2, T3, T4>(TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            => Each<TJob, T1, T2, T3, T4>(ref job);

        public void Each<TJob, T1, T2, T3, T4, T5>(ref TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4, T5>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
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
                                job.Execute(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id), ref s5.GetRef(i, id));
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
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    var items5 = chunk.GetItems<T5>(col5Index);

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i]);
                    }
                }
            }
        }

        public void Each<TJob, T1, T2, T3, T4, T5>(TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4, T5>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            => Each<TJob, T1, T2, T3, T4, T5>(ref job);

        public void Each<TJob, T1, T2, T3, T4, T5, T6>(ref TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4, T5, T6>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
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
                                job.Execute(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id), ref s5.GetRef(i, id), ref s6.GetRef(i, id));
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
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    var items5 = chunk.GetItems<T5>(col5Index);
                    var items6 = chunk.GetItems<T6>(col6Index);

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i]);
                    }
                }
            }
        }

        public void Each<TJob, T1, T2, T3, T4, T5, T6>(TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4, T5, T6>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            => Each<TJob, T1, T2, T3, T4, T5, T6>(ref job);

        public void Each<TJob, T1, T2, T3, T4, T5, T6, T7>(ref TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4, T5, T6, T7>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
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
                                job.Execute(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id), ref s5.GetRef(i, id), ref s6.GetRef(i, id), ref s7.GetRef(i, id));
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
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    var items5 = chunk.GetItems<T5>(col5Index);
                    var items6 = chunk.GetItems<T6>(col6Index);
                    var items7 = chunk.GetItems<T7>(col7Index);

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i], ref items7[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i], ref items7[i]);
                    }
                }
            }
        }

        public void Each<TJob, T1, T2, T3, T4, T5, T6, T7>(TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4, T5, T6, T7>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            => Each<TJob, T1, T2, T3, T4, T5, T6, T7>(ref job);

        public void Each<TJob, T1, T2, T3, T4, T5, T6, T7, T8>(ref TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4, T5, T6, T7, T8>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
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
                                job.Execute(entities[i], ref s1.GetRef(i, id), ref s2.GetRef(i, id), ref s3.GetRef(i, id), ref s4.GetRef(i, id), ref s5.GetRef(i, id), ref s6.GetRef(i, id), ref s7.GetRef(i, id), ref s8.GetRef(i, id));
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
                    var items3 = chunk.GetItems<T3>(col3Index);
                    var items4 = chunk.GetItems<T4>(col4Index);
                    var items5 = chunk.GetItems<T5>(col5Index);
                    var items6 = chunk.GetItems<T6>(col6Index);
                    var items7 = chunk.GetItems<T7>(col7Index);
                    var items8 = chunk.GetItems<T8>(col8Index);

                    if (filtered)
                    {
                        for (int i = 0; i < count; i++)
                            if (PassesSparseFilters(entities[i].Id))
                                job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i], ref items7[i], ref items8[i]);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                            job.Execute(entities[i], ref items1[i], ref items2[i], ref items3[i], ref items4[i], ref items5[i], ref items6[i], ref items7[i], ref items8[i]);
                    }
                }
            }
        }

        public void Each<TJob, T1, T2, T3, T4, T5, T6, T7, T8>(TJob job)
            where TJob : struct, IJob<T1, T2, T3, T4, T5, T6, T7, T8>
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            => Each<TJob, T1, T2, T3, T4, T5, T6, T7, T8>(ref job);
    }
}
