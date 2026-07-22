using System;
using System.Collections.Generic;

namespace Loom.Net
{
    /// <summary>
    /// Ring of authoritative entity transforms keyed by simulation tick.
    /// Used by <see cref="StateInterpolator"/> for remote (non-predicted) entities.
    /// Keeps the last <see cref="Capacity"/> distinct ticks.
    /// </summary>
    public sealed class SnapshotBuffer
    {
        private readonly int _capacity;
        private readonly List<long> _ticks = new List<long>();
        private readonly Dictionary<long, Dictionary<int, NetTransform>> _byTick =
            new Dictionary<long, Dictionary<int, NetTransform>>();

        public SnapshotBuffer(int capacity = 32)
        {
            if (capacity < 2)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 2.");
            _capacity = capacity;
        }

        public int Capacity => _capacity;
        public int TickCount => _ticks.Count;

        /// <summary>Oldest retained tick, or -1 when empty.</summary>
        public long OldestTick => _ticks.Count == 0 ? -1 : _ticks[0];

        /// <summary>Newest retained tick, or -1 when empty.</summary>
        public long NewestTick => _ticks.Count == 0 ? -1 : _ticks[_ticks.Count - 1];

        /// <summary>Stores or replaces the transform for <paramref name="entityId"/> at <paramref name="tick"/>.</summary>
        public void Push(long tick, int entityId, in NetTransform transform)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));
            if (entityId == 0)
                throw new ArgumentOutOfRangeException(nameof(entityId), "Entity id 0 is reserved.");

            if (!_byTick.TryGetValue(tick, out var map))
            {
                map = new Dictionary<int, NetTransform>();
                _byTick[tick] = map;
                InsertTickSorted(tick);
                Trim();
            }

            map[entityId] = transform;
        }

        public bool TryGet(long tick, int entityId, out NetTransform transform)
        {
            if (_byTick.TryGetValue(tick, out var map) && map.TryGetValue(entityId, out transform))
                return true;
            transform = default;
            return false;
        }

        public bool ContainsTick(long tick) => _byTick.ContainsKey(tick);

        public void Clear()
        {
            _ticks.Clear();
            _byTick.Clear();
        }

        private void InsertTickSorted(long tick)
        {
            int i = _ticks.Count;
            while (i > 0 && _ticks[i - 1] > tick)
                i--;
            if (i < _ticks.Count && _ticks[i] == tick)
                return;
            _ticks.Insert(i, tick);
        }

        private void Trim()
        {
            while (_ticks.Count > _capacity)
            {
                long old = _ticks[0];
                _ticks.RemoveAt(0);
                _byTick.Remove(old);
            }
        }
    }
}
