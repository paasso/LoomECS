using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Loom.Internal
{
    /// <summary>Cached add/remove transition: target archetype plus a compacted column map
    /// (only columns present on both sides — no <c>-1</c> probes on the hot path). Built once when
    /// the edge is first materialized.</summary>
    internal readonly struct ArchetypeEdge
    {
        /// <summary>Null means "no edge cached in this slot" when stored in the flat edge tables.</summary>
        public readonly Archetype? Target;
        public readonly int[] FromColumnIndices;
        public readonly int[] ToColumnIndices;

        public ArchetypeEdge(Archetype target, int[] fromColumnIndices, int[] toColumnIndices)
        {
            Target = target;
            FromColumnIndices = fromColumnIndices;
            ToColumnIndices = toColumnIndices;
        }

        internal static ArchetypeEdge Build(Archetype from, Archetype to)
        {
            var fromIds = from.DataComponentIds;
            int capacity = fromIds.Length;
            var fromCols = new int[capacity];
            var toCols = new int[capacity];
            int n = 0;

            for (int i = 0; i < fromIds.Length; i++)
            {
                if (!to.TryGetColumnIndex(fromIds[i], out int toIdx))
                    continue;

                fromCols[n] = i;
                toCols[n] = toIdx;
                n++;
            }

            if (n != capacity)
            {
                Array.Resize(ref fromCols, n);
                Array.Resize(ref toCols, n);
            }

            return new ArchetypeEdge(to, fromCols, toCols);
        }
    }

    /// <summary>
    /// A table of entities that all share the exact same set of dense component types, stored as a
    /// list of fixed-capacity <see cref="Chunk"/>s rather than one ever-growing array — growth
    /// appends a new chunk instead of resizing+copying an existing one.
    /// Every row operation is driven by the archetype's total <see cref="Count"/>: a new entity
    /// always goes at global row <c>Count</c>, and removal always swaps in whatever currently sits
    /// at global row <c>Count - 1</c> (see <see cref="Locate"/>). Chunks are never removed from the
    /// list once allocated — a chunk drained back to zero by removals just sits there for the next
    /// <see cref="AddEntityRow"/> to reuse — so "which chunk is the current tail" is a division by
    /// <see cref="ChunkCapacity"/>, never a search or list-length assumption.
    /// Add/RemoveComponent edges are cached per component id so repeated structural changes
    /// (e.g. an entity gaining then losing the same component type) don't re-hash the mask.
    /// </summary>
    internal sealed class Archetype
    {
        /// <summary>Entities per chunk. A fixed entity count (rather than a byte-size budget like
        /// Unity DOTS' 16 KB chunks) keeps this independent of component size/blittability — simple
        /// and portable, at the cost of not being memory-size-tuned per archetype. Kept a power of
        /// two so row-to-chunk resolution (<see cref="Locate"/>, called several times per
        /// structural change) is a shift+mask instead of a signed division/modulo.</summary>
        public const int ChunkCapacity = 1024;
        private const int ChunkIndexShift = 10; // 1 << 10 == ChunkCapacity
        private const int InChunkIndexMask = ChunkCapacity - 1;

        public readonly int Id;
        public readonly ComponentMask Mask;

        /// <summary>Every dense component id this archetype's mask has set, including empty (tag)
        /// types. Use this when you only care about presence — e.g. building the mask itself.</summary>
        public readonly int[] ComponentIds;

        /// <summary>Subset of <see cref="ComponentIds"/> that actually have a backing column — tag
        /// types (<see cref="ComponentTypeTraits{T}.IsEmpty"/>) are deliberately excluded, since a
        /// zero-field type has nothing to store: its bit in <see cref="Mask"/> already says
        /// everything there is to say. This is what chunk columns are built from, and what
        /// row-copying loops (<c>World.MoveEntity</c>) walk instead of <see cref="ComponentIds"/>.</summary>
        public readonly int[] DataComponentIds;

        // Indexed by dense component id (0..Capacity-1), -1 when this archetype has no column for it.
        // A flat array beats a Dictionary on every MoveEntity column lookup — ids are small integers
        // drawn from a per-world counter capped at ComponentMask.Capacity anyway.
        private readonly int[] _columnIndexByComponentId;
        private readonly ComponentTypeRegistry _registry;
        private readonly List<Chunk> _chunks = new List<Chunk>();

        // Flat edge tables (indexed by dense component id). Target == null = not materialized yet —
        // same idea as _columnIndexByComponentId: ids are small integers, so an array beats a
        // Dictionary probe on every Add/Remove after the first miss fills the slot.
        private readonly ArchetypeEdge[] _addEdges = new ArchetypeEdge[ComponentMask.Capacity];
        private readonly ArchetypeEdge[] _removeEdges = new ArchetypeEdge[ComponentMask.Capacity];

        /// <summary>Total entity count across every chunk.</summary>
        public int Count;

        static Archetype()
        {
            System.Diagnostics.Debug.Assert(1 << ChunkIndexShift == ChunkCapacity, "ChunkIndexShift must match ChunkCapacity");
            System.Diagnostics.Debug.Assert(InChunkIndexMask == ChunkCapacity - 1, "InChunkIndexMask must match ChunkCapacity");
        }

        public Archetype(int id, ComponentMask mask, int[] componentIds, ComponentTypeRegistry registry)
        {
            Id = id;
            Mask = mask;
            ComponentIds = componentIds;
            _registry = registry;

            var dataIds = new List<int>(componentIds.Length);
            foreach (var componentId in componentIds)
                if (!registry.Get(componentId).IsEmpty)
                    dataIds.Add(componentId);
            DataComponentIds = dataIds.ToArray();

            _columnIndexByComponentId = new int[ComponentMask.Capacity];
            for (int i = 0; i < _columnIndexByComponentId.Length; i++)
                _columnIndexByComponentId[i] = -1;
            for (int i = 0; i < DataComponentIds.Length; i++)
                _columnIndexByComponentId[DataComponentIds[i]] = i;
        }

        /// <summary>Every chunk currently backing this archetype, in append order. Exposed as the
        /// concrete <see cref="List{T}"/> type (not <c>IEnumerable</c>) so <c>foreach</c> binds to
        /// its struct enumerator and iterating allocates nothing.</summary>
        public List<Chunk> Chunks => _chunks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetColumnIndex(int componentId, out int columnIndex)
        {
            columnIndex = _columnIndexByComponentId[componentId];
            return columnIndex >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ColumnIndex(int componentId) => _columnIndexByComponentId[componentId];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentArray<T> GetColumn<T>(int componentId, Chunk chunk) where T : struct =>
            Unsafe.As<ComponentArray<T>>(chunk.Columns[_columnIndexByComponentId[componentId]]);

        /// <summary>Maps a global row (as stored in <c>EntityRecord.Row</c>, always <c>&lt; Count</c>)
        /// to the chunk and in-chunk index holding it — pure arithmetic, no per-chunk state
        /// involved. This only works because every chunk's own <c>Count</c> is kept in lockstep
        /// with this same division/modulo by <see cref="AddEntityRow"/>/<see cref="RemoveRowSwapBack"/>,
        /// both of which derive "which chunk" from the archetype's total <see cref="Count"/> rather
        /// than from list bookkeeping like "the last chunk in the list" — that distinction matters
        /// once a trailing chunk has been fully drained by removals but is still sitting in
        /// <see cref="Chunks"/> for reuse (see <see cref="RemoveRowSwapBack"/>).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (Chunk Chunk, int Index) Locate(int globalRow)
        {
            int chunkIndex = globalRow >> ChunkIndexShift;
            int indexInChunk = globalRow & InChunkIndexMask;
            return (_chunks[chunkIndex], indexInChunk);
        }

        /// <summary>Appends <paramref name="entity"/> at the next free row and returns that row
        /// together with its already-resolved chunk/index (callers used to re-<see cref="Locate"/>
        /// immediately — that second pass is gone).</summary>
        public (int Row, Chunk Chunk, int Index) AddEntityRow(Entity entity)
        {
            int globalRow = Count;
            int chunkIndex = globalRow >> ChunkIndexShift;
            int indexInChunk = globalRow & InChunkIndexMask;

            // chunkIndex is always either an already-allocated chunk, or exactly the next one
            // (Count only ever grows by one row at a time) — reusing an existing, possibly
            // previously-drained-to-empty chunk here is what avoids reallocating on churn.
            if (chunkIndex >= _chunks.Count)
                _chunks.Add(new Chunk(ChunkCapacity, DataComponentIds, _registry));

            var chunk = _chunks[chunkIndex];
            chunk.Entities[indexInChunk] = entity;
            chunk.Count++;
            Count++;
            return (globalRow, chunk, indexInChunk);
        }

        /// <summary>Pre-allocates enough <see cref="Chunk"/> pages to absorb
        /// <paramref name="additionalRows"/> more entities without hitting the
        /// <c>chunkIndex &gt;= _chunks.Count</c> branch inside <see cref="AddEntityRow"/> on every
        /// row. Used by bulk create paths that know upfront how many entities they will place.</summary>
        public void EnsureRowCapacity(int additionalRows)
        {
            if (additionalRows <= 0)
                return;

            int neededRows = Count + additionalRows;
            int neededChunks = (neededRows + ChunkCapacity - 1) >> ChunkIndexShift;
            while (_chunks.Count < neededChunks)
                _chunks.Add(new Chunk(ChunkCapacity, DataComponentIds, _registry));
        }

        /// <summary>
        /// Removes the row at <paramref name="row"/> by swapping the archetype's very last entity
        /// (at global row <c>Count - 1</c>, located the same way <paramref name="row"/> is — not by
        /// assuming the last chunk in <see cref="Chunks"/> is the one holding it, since a fully
        /// drained trailing chunk stays in the list for reuse rather than being removed) into its
        /// place. Returns the entity that got moved into <paramref name="row"/> (caller must fix up
        /// that entity's stored row index), or <see cref="Entity.Null"/> if the removed row was
        /// already last.
        /// </summary>
        public Entity RemoveRowSwapBack(int row)
        {
            var (chunk, index) = Locate(row);
            return RemoveRowSwapBack(row, chunk, index);
        }

        /// <summary>Same as <see cref="RemoveRowSwapBack(int)"/>, but for callers (namely
        /// <c>World.MoveEntity</c>) that already resolved <paramref name="row"/>'s chunk/index while
        /// doing other work with it — skips re-deriving it via another <see cref="Locate"/> call.</summary>
        public Entity RemoveRowSwapBack(int row, Chunk chunk, int index)
        {
            var (lastChunk, lastIndex) = Locate(Count - 1);
            Entity moved = Entity.Null;

            if (row != Count - 1)
            {
                chunk.Entities[index] = lastChunk.Entities[lastIndex];
                moved = chunk.Entities[index];
                // MoveRowTo copies AND clears the source in one virtual call, instead of a
                // separate CopyRowTo + Clear per column (this is the common case, so halving the
                // interface dispatches here is worth the extra interface method).
                for (int i = 0; i < chunk.Columns.Length; i++)
                    lastChunk.Columns[i].MoveRowTo(lastIndex, chunk.Columns[i], index);
            }
            else
            {
                foreach (var column in lastChunk.Columns)
                    column.Clear(lastIndex);
            }

            lastChunk.Entities[lastIndex] = Entity.Null;
            lastChunk.Count--;

            Count--;
            return moved;
        }

        public bool TryGetAddEdge(int componentId, out ArchetypeEdge edge)
        {
            edge = _addEdges[componentId];
            return edge.Target != null;
        }

        public bool TryGetRemoveEdge(int componentId, out ArchetypeEdge edge)
        {
            edge = _removeEdges[componentId];
            return edge.Target != null;
        }

        public void SetAddEdge(int componentId, ArchetypeEdge edge) => _addEdges[componentId] = edge;
        public void SetRemoveEdge(int componentId, ArchetypeEdge edge) => _removeEdges[componentId] = edge;

        /// <summary>Empties every chunk row without releasing chunk pages (pool reuse).</summary>
        public void ClearAllRows()
        {
            if (Count == 0)
                return;

            for (int c = 0; c < _chunks.Count; c++)
            {
                var chunk = _chunks[c];
                int n = chunk.Count;
                if (n == 0)
                    continue;

                for (int col = 0; col < chunk.Columns.Length; col++)
                {
                    var column = chunk.Columns[col];
                    for (int i = 0; i < n; i++)
                        column.Clear(i);
                }

                Array.Clear(chunk.Entities, 0, n);
                chunk.Count = 0;
            }

            Count = 0;
        }
    }
}
