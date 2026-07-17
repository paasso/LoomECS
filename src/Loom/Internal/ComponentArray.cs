using System.Runtime.CompilerServices;

namespace Loom.Internal
{
    /// <summary>
    /// One chunk's column for a component type: a densely packed, fixed-size
    /// <typeparamref name="T"/>[] parallel to the chunk's Entities array. Fixed size because the
    /// owning <see cref="Chunk"/> never grows — it's allocated once at <see cref="Chunk.Capacity"/>
    /// and a full chunk means the archetype starts a new one, so there's no resize/copy path here
    /// at all (unlike a single ever-growing archetype-wide array).
    /// </summary>
    internal sealed class ComponentArray<T> : IComponentArray where T : struct
    {
        // Unmanaged T: vacated slots past Count are overwritten on next Add — no need to write default
        // (Friflo-style). Managed-bearing T still clears so GC can reclaim references.
        private static readonly bool NeedsClear = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        public readonly T[] Items;

        public ComponentArray(int capacity)
        {
            Items = new T[capacity];
        }

        public void CopyRowTo(int srcRow, IComponentArray dst, int dstRow)
        {
            ((ComponentArray<T>)dst).Items[dstRow] = Items[srcRow];
        }

        public void Clear(int row)
        {
            if (NeedsClear)
                Items[row] = default;
        }

        public void MoveRowTo(int srcRow, IComponentArray dst, int dstRow)
        {
            ((ComponentArray<T>)dst).Items[dstRow] = Items[srcRow];
            if (NeedsClear)
                Items[srcRow] = default;
        }

        public object GetBoxed(int row) => Items[row];

        public void SetBoxed(int row, object value) => Items[row] = (T)value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRef(int row) => ref Items[row];
    }
}
