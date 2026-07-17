using System;
using Loom.Internal;

namespace Loom.Queries
{
    /// <summary>One call per archetype chunk (~1024 rows). Prefer this over per-entity
    /// <see cref="Query.Each{T1}"/> when the body can run as a tight span loop — avoids 1k+
    /// delegate invokes per chunk.</summary>
    public delegate void ChunkRefAction<T1>(ReadOnlySpan<Entity> entities, Span<T1> c1);
    public delegate void ChunkRefAction<T1, T2>(ReadOnlySpan<Entity> entities, Span<T1> c1, Span<T2> c2);
    public delegate void ChunkRefAction<T1, T2, T3>(ReadOnlySpan<Entity> entities, Span<T1> c1, Span<T2> c2, Span<T3> c3);
    public delegate void ChunkRefAction<T1, T2, T3, T4>(ReadOnlySpan<Entity> entities, Span<T1> c1, Span<T2> c2, Span<T3> c3, Span<T4> c4);

    public ref partial struct Query
    {
        /// <summary>Dense chunk-span iteration (1 component). Type parameters must be dense;
        /// sparse With/Without/WithAny filters are not supported here — use <see cref="Each{T1}"/>.</summary>
        public void EachChunk<T1>(ChunkRefAction<T1> action) where T1 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            RequireDenseChunkParams<T1>();
            With<T1>();
            EnsureNoSparseFilter();

            int id1 = _world.GetComponentInfo<T1>().Id;
            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1 = archetype.ColumnIndex(id1);
                var chunks = archetype.Chunks;
                for (int ci = 0; ci < chunks.Count; ci++)
                {
                    var chunk = chunks[ci];
                    int count = chunk.Count;
                    if (count == 0)
                        continue;
                    action(
                        new ReadOnlySpan<Entity>(chunk.Entities, 0, count),
                        new Span<T1>(chunk.GetItems<T1>(col1), 0, count));
                }
            }
        }

        /// <summary>Dense chunk-span iteration (2 components). See <see cref="EachChunk{T1}"/>.</summary>
        public void EachChunk<T1, T2>(ChunkRefAction<T1, T2> action)
            where T1 : struct
            where T2 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            RequireDenseChunkParams<T1, T2>();
            With<T1>();
            With<T2>();
            EnsureNoSparseFilter();

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1 = archetype.ColumnIndex(id1);
                int col2 = archetype.ColumnIndex(id2);
                var chunks = archetype.Chunks;
                for (int ci = 0; ci < chunks.Count; ci++)
                {
                    var chunk = chunks[ci];
                    int count = chunk.Count;
                    if (count == 0)
                        continue;
                    action(
                        new ReadOnlySpan<Entity>(chunk.Entities, 0, count),
                        new Span<T1>(chunk.GetItems<T1>(col1), 0, count),
                        new Span<T2>(chunk.GetItems<T2>(col2), 0, count));
                }
            }
        }

        /// <summary>Dense chunk-span iteration (3 components). See <see cref="EachChunk{T1}"/>.</summary>
        public void EachChunk<T1, T2, T3>(ChunkRefAction<T1, T2, T3> action)
            where T1 : struct
            where T2 : struct
            where T3 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            RequireDenseChunkParams<T1, T2, T3>();
            With<T1>();
            With<T2>();
            With<T3>();
            EnsureNoSparseFilter();

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            int id3 = _world.GetComponentInfo<T3>().Id;
            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1 = archetype.ColumnIndex(id1);
                int col2 = archetype.ColumnIndex(id2);
                int col3 = archetype.ColumnIndex(id3);
                var chunks = archetype.Chunks;
                for (int ci = 0; ci < chunks.Count; ci++)
                {
                    var chunk = chunks[ci];
                    int count = chunk.Count;
                    if (count == 0)
                        continue;
                    action(
                        new ReadOnlySpan<Entity>(chunk.Entities, 0, count),
                        new Span<T1>(chunk.GetItems<T1>(col1), 0, count),
                        new Span<T2>(chunk.GetItems<T2>(col2), 0, count),
                        new Span<T3>(chunk.GetItems<T3>(col3), 0, count));
                }
            }
        }

        /// <summary>Dense chunk-span iteration (4 components). See <see cref="EachChunk{T1}"/>.</summary>
        public void EachChunk<T1, T2, T3, T4>(ChunkRefAction<T1, T2, T3, T4> action)
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            RequireDenseChunkParams<T1, T2, T3, T4>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            EnsureNoSparseFilter();

            int id1 = _world.GetComponentInfo<T1>().Id;
            int id2 = _world.GetComponentInfo<T2>().Id;
            int id3 = _world.GetComponentInfo<T3>().Id;
            int id4 = _world.GetComponentInfo<T4>().Id;
            foreach (var archetype in _world.MatchingArchetypes(_denseAll, _denseNone, _denseAny))
            {
                int col1 = archetype.ColumnIndex(id1);
                int col2 = archetype.ColumnIndex(id2);
                int col3 = archetype.ColumnIndex(id3);
                int col4 = archetype.ColumnIndex(id4);
                var chunks = archetype.Chunks;
                for (int ci = 0; ci < chunks.Count; ci++)
                {
                    var chunk = chunks[ci];
                    int count = chunk.Count;
                    if (count == 0)
                        continue;
                    action(
                        new ReadOnlySpan<Entity>(chunk.Entities, 0, count),
                        new Span<T1>(chunk.GetItems<T1>(col1), 0, count),
                        new Span<T2>(chunk.GetItems<T2>(col2), 0, count),
                        new Span<T3>(chunk.GetItems<T3>(col3), 0, count),
                        new Span<T4>(chunk.GetItems<T4>(col4), 0, count));
                }
            }
        }

        private void EnsureNoSparseFilter()
        {
            if (HasSparseFilter)
                throw new InvalidOperationException(
                    "EachChunk does not support sparse With/Without/WithAny (including Enabled/Disabled). Use Each instead.");
        }

        private static void RequireDenseChunkParams<T1>() where T1 : struct
        {
            RequireNotEmpty<T1>();
            if (ComponentTypeTraits<T1>.UsesSparseMask)
                throw new InvalidOperationException($"EachChunk<{typeof(T1).Name}> requires a dense component; use Each for sparse/shared.");
        }

        private static void RequireDenseChunkParams<T1, T2>()
            where T1 : struct
            where T2 : struct
        {
            RequireDenseChunkParams<T1>();
            RequireDenseChunkParams<T2>();
        }

        private static void RequireDenseChunkParams<T1, T2, T3>()
            where T1 : struct
            where T2 : struct
            where T3 : struct
        {
            RequireDenseChunkParams<T1, T2>();
            RequireDenseChunkParams<T3>();
        }

        private static void RequireDenseChunkParams<T1, T2, T3, T4>()
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
        {
            RequireDenseChunkParams<T1, T2, T3>();
            RequireDenseChunkParams<T4>();
        }
    }
}
