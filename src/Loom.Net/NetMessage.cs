using System;

namespace Loom.Net
{
    /// <summary>Framed net message kinds used by the sample helpers. Custom transports may use
    /// their own envelopes; these values are only required when using <see cref="NetMessage"/>.</summary>
    public enum NetMessageKind : byte
    {
        Snapshot = 1,
        Delta = 2,
        Command = 3,
        /// <summary>Client → server request for a full <see cref="Snapshot"/> (join / resync).</summary>
        SnapshotRequest = 4,
    }

    /// <summary>Minimal envelope: kind + tick + payload. Not required by SnapshotSync/DeltaSync
    /// themselves — useful when multiplexing over a single transport channel.</summary>
    public static class NetMessage
    {
        public const int HeaderSize = 1 + 8; // kind + tick

        public static byte[] Pack(NetMessageKind kind, long tick, ReadOnlySpan<byte> payload)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));

            var result = new byte[HeaderSize + payload.Length];
            result[0] = (byte)kind;
            WriteInt64(result, 1, tick);
            payload.CopyTo(result.AsSpan(HeaderSize));
            return result;
        }

        public static bool TryUnpack(ReadOnlySpan<byte> packet, out NetMessageKind kind, out long tick, out ReadOnlySpan<byte> payload)
        {
            kind = default;
            tick = 0;
            payload = default;
            if (packet.Length < HeaderSize)
                return false;

            kind = (NetMessageKind)packet[0];
            tick = ReadInt64(packet, 1);
            payload = packet.Slice(HeaderSize);
            return true;
        }

        private static void WriteInt64(byte[] buffer, int offset, long value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            buffer[offset + 4] = (byte)(value >> 32);
            buffer[offset + 5] = (byte)(value >> 40);
            buffer[offset + 6] = (byte)(value >> 48);
            buffer[offset + 7] = (byte)(value >> 56);
        }

        private static long ReadInt64(ReadOnlySpan<byte> buffer, int offset) =>
            buffer[offset]
            | ((long)buffer[offset + 1] << 8)
            | ((long)buffer[offset + 2] << 16)
            | ((long)buffer[offset + 3] << 24)
            | ((long)buffer[offset + 4] << 32)
            | ((long)buffer[offset + 5] << 40)
            | ((long)buffer[offset + 6] << 48)
            | ((long)buffer[offset + 7] << 56);
    }
}
