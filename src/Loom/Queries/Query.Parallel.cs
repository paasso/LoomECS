using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Loom.Internal;

namespace Loom.Queries
{
    public ref partial struct Query
    {
        /// <summary>
        /// Parallel variant of <see cref="ForEach"/>: one worker task per archetype chunk
        /// (~<see cref="Archetype.ChunkCapacity"/> entities). The body must not create/destroy
        /// entities or add/remove components on this world; value mutation of dense/sparse
        /// components on distinct entities is fine. Concurrent writes through
        /// <see cref="ISharedComponent"/> to the same interned instance are races — avoid or
        /// synchronize externally.
        /// </summary>
        public void ParallelForEach(Action<Entity> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var world = _world;
            var denseAll = _denseAll;
            var denseNone = _denseNone;
            var denseAny = _denseAny;
            var sparseAll = _sparseAll;
            var sparseNone = _sparseNone;
            var sparseAny = _sparseAny;
            bool filtered = sparseAll != ComponentMask.Empty || sparseNone != ComponentMask.Empty || sparseAny != ComponentMask.Empty;

            var chunks = CollectChunks(world, denseAll, denseNone, denseAny);
            Parallel.For(0, chunks.Count, i =>
            {
                var chunk = chunks[i];
                var entities = chunk.Entities;
                int count = chunk.Count;
                if (filtered)
                {
                    for (int r = 0; r < count; r++)
                    {
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, entities[r].Id))
                            action(entities[r]);
                    }
                }
                else
                {
                    for (int r = 0; r < count; r++)
                        action(entities[r]);
                }
            });
        }

        /// <summary>Parallel <see cref="Each{T1}"/> — see <see cref="ParallelForEach"/> for safety rules.</summary>
        public void ParallelEach<T1>(RefAction<T1> action) where T1 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            RequireNotEmpty<T1>();
            With<T1>();

            var world = _world;
            var denseAll = _denseAll;
            var denseNone = _denseNone;
            var denseAny = _denseAny;
            var sparseAll = _sparseAll;
            var sparseNone = _sparseNone;
            var sparseAny = _sparseAny;
            bool filtered = sparseAll != ComponentMask.Empty || sparseNone != ComponentMask.Empty || sparseAny != ComponentMask.Empty;

            if (ComponentTypeTraits<T1>.UsesSparseMask)
            {
                var jobs = CollectArchetypeChunks(world, denseAll, denseNone, denseAny);
                Parallel.For(0, jobs.Count, i =>
                {
                    var (archetype, chunk) = jobs[i];
                    var s1 = new EachSlot<T1>(world);
                    s1.EnterArchetype(archetype);
                    s1.EnterChunk(chunk);
                    var entities = chunk.Entities;
                    int count = chunk.Count;
                    for (int r = 0; r < count; r++)
                    {
                        int id = entities[r].Id;
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, id))
                            action(entities[r], ref s1.GetRef(r, id));
                    }
                });
                return;
            }

            int id1 = world.GetComponentInfo<T1>().Id;
            var denseJobs = CollectDenseJobs(world, denseAll, denseNone, denseAny, id1);
            Parallel.For(0, denseJobs.Count, i =>
            {
                var job = denseJobs[i];
                var entities = job.Chunk.Entities;
                var items1 = job.Chunk.GetItems<T1>(job.Col0);
                int count = job.Chunk.Count;
                if (filtered)
                {
                    for (int r = 0; r < count; r++)
                    {
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, entities[r].Id))
                            action(entities[r], ref items1[r]);
                    }
                }
                else
                {
                    for (int r = 0; r < count; r++)
                        action(entities[r], ref items1[r]);
                }
            });
        }

        /// <summary>Parallel <see cref="Each{T1,T2}"/> — see <see cref="ParallelForEach"/> for safety rules.</summary>
        public void ParallelEach<T1, T2>(RefAction<T1, T2> action) where T1 : struct where T2 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            With<T1>();
            With<T2>();

            var world = _world;
            var denseAll = _denseAll;
            var denseNone = _denseNone;
            var denseAny = _denseAny;
            var sparseAll = _sparseAll;
            var sparseNone = _sparseNone;
            var sparseAny = _sparseAny;
            bool filtered = sparseAll != ComponentMask.Empty || sparseNone != ComponentMask.Empty || sparseAny != ComponentMask.Empty;

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask)
            {
                var jobs = CollectArchetypeChunks(world, denseAll, denseNone, denseAny);
                Parallel.For(0, jobs.Count, i =>
                {
                    var (archetype, chunk) = jobs[i];
                    var s1 = new EachSlot<T1>(world);
                    var s2 = new EachSlot<T2>(world);
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s1.EnterChunk(chunk);
                    s2.EnterChunk(chunk);
                    var entities = chunk.Entities;
                    int count = chunk.Count;
                    for (int r = 0; r < count; r++)
                    {
                        int id = entities[r].Id;
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, id))
                            action(entities[r], ref s1.GetRef(r, id), ref s2.GetRef(r, id));
                    }
                });
                return;
            }

            int id1 = world.GetComponentInfo<T1>().Id;
            int id2 = world.GetComponentInfo<T2>().Id;
            var denseJobs = CollectDenseJobs(world, denseAll, denseNone, denseAny, id1, id2);
            Parallel.For(0, denseJobs.Count, i =>
            {
                var job = denseJobs[i];
                var entities = job.Chunk.Entities;
                var items1 = job.Chunk.GetItems<T1>(job.Col0);
                var items2 = job.Chunk.GetItems<T2>(job.Col1);
                int count = job.Chunk.Count;
                if (filtered)
                {
                    for (int r = 0; r < count; r++)
                    {
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, entities[r].Id))
                            action(entities[r], ref items1[r], ref items2[r]);
                    }
                }
                else
                {
                    for (int r = 0; r < count; r++)
                        action(entities[r], ref items1[r], ref items2[r]);
                }
            });
        }

        /// <summary>Parallel <see cref="Each{T1,T2,T3}"/> — see <see cref="ParallelForEach"/> for safety rules.</summary>
        public void ParallelEach<T1, T2, T3>(RefAction<T1, T2, T3> action)
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            With<T1>();
            With<T2>();
            With<T3>();

            var world = _world;
            var denseAll = _denseAll;
            var denseNone = _denseNone;
            var denseAny = _denseAny;
            var sparseAll = _sparseAll;
            var sparseNone = _sparseNone;
            var sparseAny = _sparseAny;
            bool filtered = sparseAll != ComponentMask.Empty || sparseNone != ComponentMask.Empty || sparseAny != ComponentMask.Empty;

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask ||
                ComponentTypeTraits<T3>.UsesSparseMask)
            {
                var jobs = CollectArchetypeChunks(world, denseAll, denseNone, denseAny);
                Parallel.For(0, jobs.Count, i =>
                {
                    var (archetype, chunk) = jobs[i];
                    var s1 = new EachSlot<T1>(world);
                    var s2 = new EachSlot<T2>(world);
                    var s3 = new EachSlot<T3>(world);
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s3.EnterArchetype(archetype);
                    s1.EnterChunk(chunk);
                    s2.EnterChunk(chunk);
                    s3.EnterChunk(chunk);
                    var entities = chunk.Entities;
                    int count = chunk.Count;
                    for (int r = 0; r < count; r++)
                    {
                        int id = entities[r].Id;
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, id))
                            action(entities[r], ref s1.GetRef(r, id), ref s2.GetRef(r, id), ref s3.GetRef(r, id));
                    }
                });
                return;
            }

            int id1 = world.GetComponentInfo<T1>().Id;
            int id2 = world.GetComponentInfo<T2>().Id;
            int id3 = world.GetComponentInfo<T3>().Id;
            var denseJobs = CollectDenseJobs(world, denseAll, denseNone, denseAny, id1, id2, id3);
            Parallel.For(0, denseJobs.Count, i =>
            {
                var job = denseJobs[i];
                var entities = job.Chunk.Entities;
                var items1 = job.Chunk.GetItems<T1>(job.Col0);
                var items2 = job.Chunk.GetItems<T2>(job.Col1);
                var items3 = job.Chunk.GetItems<T3>(job.Col2);
                int count = job.Chunk.Count;
                if (filtered)
                {
                    for (int r = 0; r < count; r++)
                    {
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, entities[r].Id))
                            action(entities[r], ref items1[r], ref items2[r], ref items3[r]);
                    }
                }
                else
                {
                    for (int r = 0; r < count; r++)
                        action(entities[r], ref items1[r], ref items2[r], ref items3[r]);
                }
            });
        }

        /// <summary>Parallel <see cref="Each{T1,T2,T3,T4}"/> — see <see cref="ParallelForEach"/> for safety rules.</summary>
        public void ParallelEach<T1, T2, T3, T4>(RefAction<T1, T2, T3, T4> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();

            var world = _world;
            var denseAll = _denseAll;
            var denseNone = _denseNone;
            var denseAny = _denseAny;
            var sparseAll = _sparseAll;
            var sparseNone = _sparseNone;
            var sparseAny = _sparseAny;
            bool filtered = sparseAll != ComponentMask.Empty || sparseNone != ComponentMask.Empty || sparseAny != ComponentMask.Empty;

            if (ComponentTypeTraits<T1>.UsesSparseMask || ComponentTypeTraits<T2>.UsesSparseMask ||
                ComponentTypeTraits<T3>.UsesSparseMask || ComponentTypeTraits<T4>.UsesSparseMask)
            {
                var jobs = CollectArchetypeChunks(world, denseAll, denseNone, denseAny);
                Parallel.For(0, jobs.Count, i =>
                {
                    var (archetype, chunk) = jobs[i];
                    var s1 = new EachSlot<T1>(world);
                    var s2 = new EachSlot<T2>(world);
                    var s3 = new EachSlot<T3>(world);
                    var s4 = new EachSlot<T4>(world);
                    s1.EnterArchetype(archetype);
                    s2.EnterArchetype(archetype);
                    s3.EnterArchetype(archetype);
                    s4.EnterArchetype(archetype);
                    s1.EnterChunk(chunk);
                    s2.EnterChunk(chunk);
                    s3.EnterChunk(chunk);
                    s4.EnterChunk(chunk);
                    var entities = chunk.Entities;
                    int count = chunk.Count;
                    for (int r = 0; r < count; r++)
                    {
                        int id = entities[r].Id;
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, id))
                            action(entities[r], ref s1.GetRef(r, id), ref s2.GetRef(r, id), ref s3.GetRef(r, id), ref s4.GetRef(r, id));
                    }
                });
                return;
            }

            int id1 = world.GetComponentInfo<T1>().Id;
            int id2 = world.GetComponentInfo<T2>().Id;
            int id3 = world.GetComponentInfo<T3>().Id;
            int id4 = world.GetComponentInfo<T4>().Id;
            var denseJobs = CollectDenseJobs(world, denseAll, denseNone, denseAny, id1, id2, id3, id4);
            Parallel.For(0, denseJobs.Count, i =>
            {
                var job = denseJobs[i];
                var entities = job.Chunk.Entities;
                var items1 = job.Chunk.GetItems<T1>(job.Col0);
                var items2 = job.Chunk.GetItems<T2>(job.Col1);
                var items3 = job.Chunk.GetItems<T3>(job.Col2);
                var items4 = job.Chunk.GetItems<T4>(job.Col3);
                int count = job.Chunk.Count;
                if (filtered)
                {
                    for (int r = 0; r < count; r++)
                    {
                        if (PassesSparseFilters(world, sparseAll, sparseNone, sparseAny, entities[r].Id))
                            action(entities[r], ref items1[r], ref items2[r], ref items3[r], ref items4[r]);
                    }
                }
                else
                {
                    for (int r = 0; r < count; r++)
                        action(entities[r], ref items1[r], ref items2[r], ref items3[r], ref items4[r]);
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool PassesSparseFilters(
            World world,
            ComponentMask sparseAll,
            ComponentMask sparseNone,
            ComponentMask sparseAny,
            int entityId)
        {
            var mask = world.GetSparseMask(entityId);
            if (!mask.ContainsAll(sparseAll) || mask.IntersectsAny(sparseNone))
                return false;
            if (sparseAny != ComponentMask.Empty && !mask.IntersectsAny(sparseAny))
                return false;
            return true;
        }

        private static List<Chunk> CollectChunks(
            World world, ComponentMask denseAll, ComponentMask denseNone, ComponentMask denseAny)
        {
            var chunks = new List<Chunk>();
            foreach (var archetype in world.MatchingArchetypes(denseAll, denseNone, denseAny))
            {
                foreach (var chunk in archetype.Chunks)
                {
                    if (chunk.Count > 0)
                        chunks.Add(chunk);
                }
            }

            return chunks;
        }

        private static List<(Archetype Archetype, Chunk Chunk)> CollectArchetypeChunks(
            World world, ComponentMask denseAll, ComponentMask denseNone, ComponentMask denseAny)
        {
            var jobs = new List<(Archetype, Chunk)>();
            foreach (var archetype in world.MatchingArchetypes(denseAll, denseNone, denseAny))
            {
                foreach (var chunk in archetype.Chunks)
                {
                    if (chunk.Count > 0)
                        jobs.Add((archetype, chunk));
                }
            }

            return jobs;
        }

        private static List<DenseChunkJob> CollectDenseJobs(
            World world,
            ComponentMask denseAll,
            ComponentMask denseNone,
            ComponentMask denseAny,
            params int[] componentIds)
        {
            var jobs = new List<DenseChunkJob>();
            foreach (var archetype in world.MatchingArchetypes(denseAll, denseNone, denseAny))
            {
                int col0 = componentIds.Length > 0 ? archetype.ColumnIndex(componentIds[0]) : -1;
                int col1 = componentIds.Length > 1 ? archetype.ColumnIndex(componentIds[1]) : -1;
                int col2 = componentIds.Length > 2 ? archetype.ColumnIndex(componentIds[2]) : -1;
                int col3 = componentIds.Length > 3 ? archetype.ColumnIndex(componentIds[3]) : -1;

                foreach (var chunk in archetype.Chunks)
                {
                    if (chunk.Count > 0)
                        jobs.Add(new DenseChunkJob(chunk, col0, col1, col2, col3));
                }
            }

            return jobs;
        }

        private readonly struct DenseChunkJob
        {
            public readonly Chunk Chunk;
            public readonly int Col0;
            public readonly int Col1;
            public readonly int Col2;
            public readonly int Col3;

            public DenseChunkJob(Chunk chunk, int col0, int col1, int col2, int col3)
            {
                Chunk = chunk;
                Col0 = col0;
                Col1 = col1;
                Col2 = col2;
                Col3 = col3;
            }
        }
    }
}
