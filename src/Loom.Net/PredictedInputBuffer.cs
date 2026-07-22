using System;
using System.Collections.Generic;

namespace Loom.Net
{
    /// <summary>One client command waiting for authoritative acknowledgement.</summary>
    public readonly struct PredictedCommand
    {
        public PredictedCommand(long tick, byte[] payload)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));
            Tick = tick;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public long Tick { get; }
        public byte[] Payload { get; }
    }

    /// <summary>
    /// Ring of unacked client commands for prediction / reconciliation.
    /// Capacity is a soft max: oldest entries are dropped when full.
    /// </summary>
    public sealed class PredictedInputBuffer
    {
        private readonly int _capacity;
        private readonly List<PredictedCommand> _commands = new List<PredictedCommand>();

        public PredictedInputBuffer(int capacity = 64)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Capacity => _capacity;
        public int Count => _commands.Count;

        public long OldestTick => _commands.Count == 0 ? -1 : _commands[0].Tick;
        public long NewestTick => _commands.Count == 0 ? -1 : _commands[_commands.Count - 1].Tick;

        /// <summary>Appends a command. Replaces an existing entry with the same tick.</summary>
        public void Push(long tick, byte[] payload)
        {
            var cmd = new PredictedCommand(tick, payload);
            for (int i = 0; i < _commands.Count; i++)
            {
                if (_commands[i].Tick == tick)
                {
                    _commands[i] = cmd;
                    return;
                }
            }

            _commands.Add(cmd);
            while (_commands.Count > _capacity)
                _commands.RemoveAt(0);
        }

        /// <summary>Drops every command with <c>Tick &lt;= ackedTick</c>.</summary>
        public int AckThrough(long ackedTick)
        {
            int removed = 0;
            while (_commands.Count > 0 && _commands[0].Tick <= ackedTick)
            {
                _commands.RemoveAt(0);
                removed++;
            }

            return removed;
        }

        public PredictedCommand this[int index] => _commands[index];

        public void Clear() => _commands.Clear();
    }
}
