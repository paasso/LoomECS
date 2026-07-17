namespace Loom.Internal
{
    /// <summary>
    /// Type-erased handle onto one chunk's column for a component type, so <see cref="Chunk"/> can
    /// hold an array of heterogeneous columns and move rows between chunks (even across different
    /// chunks/archetypes) without knowing T at compile time. Fixed-capacity for the chunk's
    /// lifetime — chunks never grow, a full chunk just means the next entity starts a new one.
    /// </summary>
    internal interface IComponentArray
    {
        /// <summary>Copies the value at <paramref name="srcRow"/> into <paramref name="dst"/> at <paramref name="dstRow"/>, leaving <paramref name="srcRow"/> untouched. <paramref name="dst"/> must be a <see cref="ComponentArray{T}"/> of the same T. Used when an entity gains/loses a component: the value needs to exist in both the old and new archetype's row until the old row is separately cleaned up.</summary>
        void CopyRowTo(int srcRow, IComponentArray dst, int dstRow);

        /// <summary>Resets a vacated row to <c>default</c> (releases any reference-typed fields).</summary>
        void Clear(int row);

        /// <summary>Copies the value at <paramref name="srcRow"/> into <paramref name="dst"/> at <paramref name="dstRow"/> <em>and</em> clears <paramref name="srcRow"/> — one virtual call instead of a separate <see cref="CopyRowTo"/> + <see cref="Clear"/>. Used for swap-back removal, where the source row is always being vacated anyway.</summary>
        void MoveRowTo(int srcRow, IComponentArray dst, int dstRow);

        /// <summary>Boxes the value at <paramref name="row"/> for serialization. Not used on the
        /// gameplay hot path.</summary>
        object GetBoxed(int row);

        /// <summary>Writes a boxed value at <paramref name="row"/> (debug / tooling). Not a gameplay
        /// hot path.</summary>
        void SetBoxed(int row, object value);
    }
}
