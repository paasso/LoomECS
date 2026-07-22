using System;
using System.Collections.Generic;

namespace Loom.Net
{
    /// <summary>Client → server input/command envelope. Payload is opaque bytes (or MemoryPack of a
    /// user struct). Server buffers by tick and applies in order.</summary>
    public readonly struct NetCommand
    {
        public NetCommand(NetPeerId client, long tick, byte[] payload)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));

            Client = client;
            Tick = tick;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public NetPeerId Client { get; }
        public long Tick { get; }
        public byte[] Payload { get; }
    }

    /// <summary>
    /// Simple server-side command queue: clients enqueue packets targeting a simulation tick;
    /// the authoritative host drains commands for the current tick (stable client id order).
    /// </summary>
    public sealed class NetCommandBuffer
    {
        private readonly List<NetCommand> _pending = new List<NetCommand>();
        private readonly List<NetCommand> _scratch = new List<NetCommand>();

        public int PendingCount => _pending.Count;

        public void Enqueue(NetCommand command) => _pending.Add(command);

        public void Enqueue(NetPeerId client, long tick, byte[] payload) =>
            Enqueue(new NetCommand(client, tick, payload));

        /// <summary>Removes and returns all commands whose <see cref="NetCommand.Tick"/> equals
        /// <paramref name="tick"/>, sorted by client id then enqueue order.</summary>
        public IReadOnlyList<NetCommand> DrainForTick(long tick)
        {
            _scratch.Clear();
            for (int i = 0; i < _pending.Count;)
            {
                if (_pending[i].Tick == tick)
                {
                    _scratch.Add(_pending[i]);
                    _pending.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }

            _scratch.Sort(static (a, b) =>
            {
                int cmp = a.Client.Value.CompareTo(b.Client.Value);
                return cmp != 0 ? cmp : 0;
            });
            return _scratch;
        }

        /// <summary>Drops commands older than <paramref name="minTick"/> (late / stale input).</summary>
        public int DropOlderThan(long minTick)
        {
            int removed = 0;
            for (int i = 0; i < _pending.Count;)
            {
                if (_pending[i].Tick < minTick)
                {
                    _pending.RemoveAt(i);
                    removed++;
                }
                else
                {
                    i++;
                }
            }

            return removed;
        }

        public void Clear() => _pending.Clear();
    }
}
