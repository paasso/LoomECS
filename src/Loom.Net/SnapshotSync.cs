using System;
using Loom;

namespace Loom.Net
{
    /// <summary>
    /// Captures full-world MemoryPack snapshots from an authoritative server world and applies
    /// them on clients via <see cref="WorldSerializer.ReplaceFromMemoryPack"/> (reset + restore).
    /// </summary>
    /// <remarks>
    /// When constructed with <c>compress: true</c>, Capture <em>allows</em> Brotli wrapping
    /// (LCMB) only when the uncompressed LCMP payload is ≥ the configured threshold; smaller
    /// snapshots stay raw LCMP. Apply accepts both magics via WorldSerializer.
    /// </remarks>
    public sealed class SnapshotSync
    {
        /// <summary>Default minimum uncompressed LCMP size before Brotli wrapping when
        /// <c>compress: true</c>. Matches <see cref="DeltaSync.DefaultCompressThreshold"/>.</summary>
        public const int DefaultCompressThreshold = DeltaSync.DefaultCompressThreshold;

        private readonly WorldSerializer _serializer;
        private readonly bool _compress;
        private readonly int _compressThreshold;

        public SnapshotSync(
            WorldSerializer serializer,
            bool compress = false,
            int compressThreshold = DefaultCompressThreshold)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            if (compressThreshold < 0)
                throw new ArgumentOutOfRangeException(nameof(compressThreshold));

            _compress = compress;
            _compressThreshold = compressThreshold;
        }

        public WorldSerializer Serializer => _serializer;

        /// <summary>When true, Capture may wrap large enough payloads as LCMB.</summary>
        public bool Compress => _compress;

        /// <summary>Minimum uncompressed LCMP length required before Brotli wrapping.</summary>
        public int CompressThreshold => _compressThreshold;

        /// <summary>Serializes the entire <paramref name="world"/> to a MemoryPack snapshot.</summary>
        public byte[] Capture(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            return _serializer.SaveToMemoryPack(world, _compress, _compressThreshold);
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
