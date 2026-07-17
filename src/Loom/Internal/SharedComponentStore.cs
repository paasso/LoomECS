using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Loom.Internal
{
    internal interface ISharedComponentStore
    {
        bool Remove(int entityId);
        bool TryGetBoxed(int entityId, out object? value);
        int UniqueInstanceCount { get; }
        void Clear();
    }

    /// <summary>
    /// Interns <typeparamref name="T"/> values so identical Adds share one slot; entities hold a
    /// handle in a <see cref="SparseSet{T}"/> of ints. Linear search on intern keeps identity correct
    /// even after in-place mutation through <see cref="GetRef"/>.
    /// </summary>
    internal sealed class SharedComponentStore<T> : ISharedComponentStore where T : struct
    {
        private T[] _values = Array.Empty<T>();
        private int[] _refCounts = Array.Empty<int>();
        private int _slotCount;
        private readonly Stack<int> _freeHandles = new Stack<int>();
        private readonly SparseSet<int> _entityHandles = new SparseSet<int>();
        private static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;

        public int UniqueInstanceCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slotCount; i++)
                {
                    if (_refCounts[i] > 0)
                        n++;
                }

                return n;
            }
        }

        public ref T Add(int entityId, T value)
        {
            if (_entityHandles.Has(entityId))
                throw new InvalidOperationException($"Entity {entityId} already has a shared {typeof(T).Name}.");

            int handle = Intern(value);
            _refCounts[handle]++;
            _entityHandles.Set(entityId, handle);
            return ref _values[handle];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRef(int entityId) => ref _values[_entityHandles.GetRef(entityId)];

        public bool Remove(int entityId)
        {
            if (!_entityHandles.Has(entityId))
                return false;

            int handle = _entityHandles.GetRef(entityId);
            _entityHandles.Remove(entityId);
            Release(handle);
            return true;
        }

        public bool TryGetBoxed(int entityId, out object? value)
        {
            if (!_entityHandles.Has(entityId))
            {
                value = null;
                return false;
            }

            value = _values[_entityHandles.GetRef(entityId)];
            return true;
        }

        public void Clear()
        {
            _entityHandles.Clear();
            Array.Clear(_values, 0, _slotCount);
            Array.Clear(_refCounts, 0, _slotCount);
            _freeHandles.Clear();
            _slotCount = 0;
        }

        private int Intern(T value)
        {
            for (int i = 0; i < _slotCount; i++)
            {
                if (_refCounts[i] > 0 && Comparer.Equals(_values[i], value))
                    return i;
            }

            if (_freeHandles.Count > 0)
            {
                int reused = _freeHandles.Pop();
                _values[reused] = value;
                _refCounts[reused] = 0;
                return reused;
            }

            EnsureSlotCapacity(_slotCount + 1);
            int handle = _slotCount++;
            _values[handle] = value;
            _refCounts[handle] = 0;
            return handle;
        }

        private void Release(int handle)
        {
            if (--_refCounts[handle] > 0)
                return;

            _values[handle] = default;
            _freeHandles.Push(handle);
        }

        private void EnsureSlotCapacity(int needed)
        {
            if (needed <= _values.Length)
                return;

            int capacity = _values.Length == 0 ? 4 : _values.Length * 2;
            while (capacity < needed)
                capacity *= 2;

            Array.Resize(ref _values, capacity);
            Array.Resize(ref _refCounts, capacity);
        }
    }
}
