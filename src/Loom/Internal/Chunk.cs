using System;
using System.Runtime.CompilerServices;

namespace Loom.Internal
{
    /// <summary>
    /// A fixed-capacity page of an <see cref="Archetype"/>'s rows: an <see cref="Entities"/> array
    /// plus one column per dense component type, all sized to <see cref="Capacity"/> and allocated
    /// once. An archetype grows by appending new chunks rather than resizing one ever-larger array,
    /// so a big archetype never pays for a "copy everything into a bigger array" step, and each
    /// chunk stays a small, cache-friendly unit on its own.
    /// Column layout (index correspondence to a component id) is owned by the parent
    /// <see cref="Archetype"/> — every chunk of the same archetype shares the same column order,
    /// so the archetype's <c>ColumnIndex(componentId)</c> applies to any of its chunks' <see cref="Columns"/>.
    /// </summary>
    internal sealed class Chunk
    {
        public readonly Entity[] Entities;
        public readonly IComponentArray[] Columns;
        public int Count;

        public int Capacity => Entities.Length;

        public Chunk(int capacity, int[] componentIds, ComponentTypeRegistry registry)
        {
            Entities = new Entity[capacity];
            Columns = new IComponentArray[componentIds.Length];
            for (int i = 0; i < componentIds.Length; i++)
            {
                var info = registry.Get(componentIds[i]);
                if (info.CreateArray == null)
                    throw new InvalidOperationException($"Component '{info.ClrType.Name}' is sparse and cannot be part of an archetype.");
                Columns[i] = info.CreateArray(capacity);
            }
        }

        /// <summary>Typed column items without a checked cast — column layout is fixed by the
        /// archetype, so <typeparamref name="T"/> is known by the caller.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] GetItems<T>(int columnIndex) where T : struct =>
            Unsafe.As<ComponentArray<T>>(Columns[columnIndex]).Items;
    }
}
