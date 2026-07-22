using System;
using Loom;

namespace Loom.Net
{
    /// <summary>
    /// Captures full-world MemoryPack snapshots from an authoritative server world and applies
    /// them on clients via <see cref="WorldSerializer.ReplaceFromMemoryPack"/> (reset + restore).
    /// </summary>
    public sealed class SnapshotSync
    {
        private readonly WorldSerializer _serializer;
        private readonly bool _compress;

        public SnapshotSync(WorldSerializer serializer, bool compress = false)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _compress = compress;
        }

        public WorldSerializer Serializer => _serializer;

        /// <summary>Serializes the entire <paramref name="world"/> to a MemoryPack snapshot.</summary>
        public byte[] Capture(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            return _serializer.SaveToMemoryPack(world, _compress);
        }

        /// <summary>Applies a snapshot to a live client world (resets entities/singletons first).</summary>
        public void Apply(World world, byte[] snapshot)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            _serializer.ReplaceFromMemoryPack(world, snapshot);
        }

        /// <summary>Capture + optional <see cref="NetMessage"/> framing for transport send.</summary>
        public byte[] CaptureFramed(World world, long tick) =>
            NetMessage.Pack(NetMessageKind.Snapshot, tick, Capture(world));

        /// <summary>Applies a framed snapshot packet produced by <see cref="CaptureFramed"/>.</summary>
        public void ApplyFramed(World world, ReadOnlySpan<byte> packet, out long tick)
        {
            if (!NetMessage.TryUnpack(packet, out var kind, out tick, out var payload) ||
                kind != NetMessageKind.Snapshot)
            {
                throw new InvalidOperationException("Packet is not a framed Snapshot message.");
            }

            Apply(world, payload.ToArray());
        }
    }
}
