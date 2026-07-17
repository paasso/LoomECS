namespace Loom.Internal
{
    /// <summary>Non-generic face of <see cref="SparseSet{T}"/> so <see cref="Loom.World"/> can hold
    /// all sparse component stores in one dictionary keyed by component id. Presence is tracked by
    /// each entity's <c>EntityRecord.SparseMask</c>, not by asking the set itself — this interface
    /// only needs to remove a value once World already knows (via the mask) that one exists.</summary>
    internal interface ISparseSet
    {
        bool Remove(int entityId);

        /// <summary>Boxes the value for <paramref name="entityId"/> when present. Serialization only.</summary>
        bool TryGetBoxed(int entityId, out object value);

        void Clear();
    }
}
