using System;
using System.Runtime.CompilerServices;

namespace Loom.Internal
{
    /// <summary>
    /// Classic sparse set: <c>_sparse[entityId]</c> holds <c>denseIndex + 1</c>, or
    /// <see cref="Absent"/> (0) if the entity has no entry. The +1 offset lets 0 double as both the
    /// "absent" sentinel and the default value C# already zero-fills new/resized arrays with, so
    /// growing <c>_sparse</c> needs no manual fill loop — it's already correct as-is. This lines up
    /// with <see cref="Entity"/>.Id 0 being reserved as "no entity" (<see cref="Entity.Null"/>), so
    /// a lookup for the null entity naturally reports "absent" too.
    /// Storage for components marked <see cref="ISparseComponent"/> — adding or removing one of
    /// these never touches the archetype graph, which is the point: components that churn a lot
    /// (tags, transient flags) would otherwise cause constant archetype-move overhead.
    /// </summary>
    internal sealed class SparseSet<T> : ISparseSet where T : struct
    {
        private const int Absent = 0;

        private int[] _sparse = Array.Empty<int>();
        private int[] _denseEntities = new int[4];
        private T[] _denseItems = new T[4];
        private int _count;

        public int Count => _count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(int entityId) => entityId >= 0 && entityId < _sparse.Length && _sparse[entityId] != Absent;

        /// <summary>Inserts or overwrites the component for <paramref name="entityId"/> and returns a ref to its stored slot.</summary>
        public ref T Set(int entityId, T value)
        {
            EnsureSparseCapacity(entityId);
            int slot = _sparse[entityId];
            int dense;
            if (slot == Absent)
            {
                EnsureDenseCapacity(_count + 1);
                dense = _count++;
                _sparse[entityId] = dense + 1;
                _denseEntities[dense] = entityId;
            }
            else
            {
                dense = slot - 1;
            }
            _denseItems[dense] = value;
            return ref _denseItems[dense];
        }

        /// <summary>Ref to an existing entry. Caller must have verified <see cref="Has"/> first.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRef(int entityId) => ref _denseItems[_sparse[entityId] - 1];

        /// <summary>Ensures <paramref name="entityId"/> has a slot (inserting
        /// <paramref name="defaultValue"/> if absent) and returns a ref to it — one sparse probe
        /// instead of Has + Set + GetRef.</summary>
        public ref T GetOrCreateRef(int entityId, T defaultValue = default)
        {
            EnsureSparseCapacity(entityId);
            int slot = _sparse[entityId];
            if (slot != Absent)
                return ref _denseItems[slot - 1];

            EnsureDenseCapacity(_count + 1);
            int dense = _count++;
            _sparse[entityId] = dense + 1;
            _denseEntities[dense] = entityId;
            _denseItems[dense] = defaultValue;
            return ref _denseItems[dense];
        }

        public bool Remove(int entityId)
        {
            if ((uint)entityId >= (uint)_sparse.Length)
                return false;

            int slot = _sparse[entityId];
            if (slot == Absent)
                return false;

            int dense = slot - 1;
            int lastDense = _count - 1;
            int lastEntity = _denseEntities[lastDense];

            _denseItems[dense] = _denseItems[lastDense];
            _denseEntities[dense] = lastEntity;
            _sparse[lastEntity] = dense + 1;

            _denseItems[lastDense] = default;
            _denseEntities[lastDense] = Absent;
            _sparse[entityId] = Absent;
            _count--;
            return true;
        }

        public bool TryGetBoxed(int entityId, out object value)
        {
            if (!Has(entityId))
            {
                value = null!;
                return false;
            }

            value = _denseItems[_sparse[entityId] - 1];
            return true;
        }

        public ReadOnlySpan<int> DenseEntityIds => new ReadOnlySpan<int>(_denseEntities, 0, _count);

        public void Clear()
        {
            if (_count == 0)
                return;

            Array.Clear(_denseItems, 0, _count);
            Array.Clear(_denseEntities, 0, _count);
            Array.Clear(_sparse, 0, _sparse.Length);
            _count = 0;
        }

        private void EnsureSparseCapacity(int entityId)
        {
            if (entityId < _sparse.Length) return;
            int newLen = Math.Max(entityId + 1, Math.Max(4, _sparse.Length * 2));
            Array.Resize(ref _sparse, newLen); // new slots are zero-filled by Array.Resize == Absent
        }

        private void EnsureDenseCapacity(int min)
        {
            if (_denseItems.Length >= min) return;
            int newCap = Math.Max(min, _denseItems.Length * 2);
            Array.Resize(ref _denseItems, newCap);
            Array.Resize(ref _denseEntities, newCap);
        }
    }
}
