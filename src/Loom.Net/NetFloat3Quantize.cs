using System;
using System.Runtime.CompilerServices;

namespace Loom.Net
{
    /// <summary>
    /// Encode/decode helpers for float3 net replication. Game components stay as float fields;
    /// call these at the encode/decode boundary when packing custom delta payloads.
    /// </summary>
    /// <remarks>
    /// Int16 path stores each axis as <c>round(value * unitsPerMeter)</c> clamped to
    /// <see cref="short.MinValue"/>..<see cref="short.MaxValue"/> (6 bytes total).
    /// Half path (when available) stores IEEE binary16 per axis (also 6 bytes, wider dynamic range).
    /// </remarks>
    public static class NetFloat3Quantize
    {
        public const int EncodedByteCount = 6;

        /// <summary>Default scale: centimetres (100 units per meter).</summary>
        public const float DefaultUnitsPerMeter = 100f;

        /// <summary>Writes X,Y,Z as 3× Int16 into <paramref name="destination"/> (must be ≥ 6 bytes).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt16(Span<byte> destination, float x, float y, float z,
            float unitsPerMeter = DefaultUnitsPerMeter)
        {
            if (destination.Length < EncodedByteCount)
                throw new ArgumentException("Destination must be at least 6 bytes.", nameof(destination));

            BitConverter.TryWriteBytes(destination, QuantizeAxis(x, unitsPerMeter));
            BitConverter.TryWriteBytes(destination.Slice(2), QuantizeAxis(y, unitsPerMeter));
            BitConverter.TryWriteBytes(destination.Slice(4), QuantizeAxis(z, unitsPerMeter));
        }

        /// <summary>Writes X,Y,Z as 3× Int16 into a new 6-byte array.</summary>
        public static byte[] ToInt16Bytes(float x, float y, float z,
            float unitsPerMeter = DefaultUnitsPerMeter)
        {
            var bytes = new byte[EncodedByteCount];
            WriteInt16(bytes, x, y, z, unitsPerMeter);
            return bytes;
        }

        /// <summary>Reads 3× Int16 from <paramref name="source"/> and converts back to floats.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadInt16(ReadOnlySpan<byte> source, out float x, out float y, out float z,
            float unitsPerMeter = DefaultUnitsPerMeter)
        {
            if (source.Length < EncodedByteCount)
                throw new ArgumentException("Source must be at least 6 bytes.", nameof(source));
            if (unitsPerMeter == 0f)
                throw new ArgumentOutOfRangeException(nameof(unitsPerMeter));

            float inv = 1f / unitsPerMeter;
            x = BitConverter.ToInt16(source) * inv;
            y = BitConverter.ToInt16(source.Slice(2)) * inv;
            z = BitConverter.ToInt16(source.Slice(4)) * inv;
        }

#if NET5_0_OR_GREATER
        /// <summary>Writes X,Y,Z as 3× <see cref="Half"/> (IEEE binary16) — 6 bytes.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteHalf(Span<byte> destination, float x, float y, float z)
        {
            if (destination.Length < EncodedByteCount)
                throw new ArgumentException("Destination must be at least 6 bytes.", nameof(destination));

            BitConverter.TryWriteBytes(destination, (Half)x);
            BitConverter.TryWriteBytes(destination.Slice(2), (Half)y);
            BitConverter.TryWriteBytes(destination.Slice(4), (Half)z);
        }

        /// <summary>Writes X,Y,Z as 3× Half into a new 6-byte array.</summary>
        public static byte[] ToHalfBytes(float x, float y, float z)
        {
            var bytes = new byte[EncodedByteCount];
            WriteHalf(bytes, x, y, z);
            return bytes;
        }

        /// <summary>Reads 3× Half from <paramref name="source"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadHalf(ReadOnlySpan<byte> source, out float x, out float y, out float z)
        {
            if (source.Length < EncodedByteCount)
                throw new ArgumentException("Source must be at least 6 bytes.", nameof(source));

            x = (float)BitConverter.ToHalf(source);
            y = (float)BitConverter.ToHalf(source.Slice(2));
            z = (float)BitConverter.ToHalf(source.Slice(4));
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short QuantizeAxis(float value, float unitsPerMeter)
        {
            float scaled = value * unitsPerMeter;
            if (scaled > short.MaxValue) return short.MaxValue;
            if (scaled < short.MinValue) return short.MinValue;
            return (short)MathF.Round(scaled);
        }
    }
}
