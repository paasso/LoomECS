namespace Loom.Queries
{
    /// <summary>
    /// Snapshot of the dense/sparse With · Without · WithAny masks used by <see cref="Query"/>.
    /// Component ids are world-specific — build a filter from the same <see cref="World"/> you
    /// will query (e.g. <c>world.Query().With&lt;T&gt;().ToFilter()</c>), then reuse it:
    /// <c>world.Query(filter).Each(...)</c>.
    /// </summary>
    public readonly struct QueryFilter
    {
        public ComponentMask DenseAll { get; }
        public ComponentMask DenseNone { get; }
        public ComponentMask DenseAny { get; }
        public ComponentMask SparseAll { get; }
        public ComponentMask SparseNone { get; }
        public ComponentMask SparseAny { get; }

        public static QueryFilter Empty { get; } = default;

        public QueryFilter(
            ComponentMask denseAll,
            ComponentMask denseNone,
            ComponentMask denseAny,
            ComponentMask sparseAll,
            ComponentMask sparseNone,
            ComponentMask sparseAny)
        {
            DenseAll = denseAll;
            DenseNone = denseNone;
            DenseAny = denseAny;
            SparseAll = sparseAll;
            SparseNone = sparseNone;
            SparseAny = sparseAny;
        }
    }
}
