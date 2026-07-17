#if LOOM_SIMD
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Loom.Internal
{
    /// <summary>
    /// Optional SIMD helpers for <see cref="ComponentMask"/> word ops (enabled with
    /// <c>-p:LoomSimd=true</c>). Safe managed code — no <c>unsafe</c> required.
    /// </summary>
    internal static class MaskSimd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsAll4(ulong a0, ulong a1, ulong a2, ulong a3,
            ulong r0, ulong r1, ulong r2, ulong r3)
        {
            if (!Vector.IsHardwareAccelerated || Vector<ulong>.Count < 2)
            {
                return (a0 & r0) == r0 && (a1 & r1) == r1 && (a2 & r2) == r2 && (a3 & r3) == r3;
            }

            Span<ulong> self = stackalloc ulong[4];
            Span<ulong> required = stackalloc ulong[4];
            self[0] = a0; self[1] = a1; self[2] = a2; self[3] = a3;
            required[0] = r0; required[1] = r1; required[2] = r2; required[3] = r3;

            int width = Vector<ulong>.Count;
            for (int i = 0; i <= 4 - width; i += width)
            {
                var vs = Unsafe.As<ulong, Vector<ulong>>(ref self[i]);
                var vr = Unsafe.As<ulong, Vector<ulong>>(ref required[i]);
                if (!Vector.EqualsAll(vs & vr, vr))
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IntersectsAny4(ulong a0, ulong a1, ulong a2, ulong a3,
            ulong b0, ulong b1, ulong b2, ulong b3)
        {
            if (!Vector.IsHardwareAccelerated || Vector<ulong>.Count < 2)
            {
                return (a0 & b0) != 0 || (a1 & b1) != 0 || (a2 & b2) != 0 || (a3 & b3) != 0;
            }

            Span<ulong> self = stackalloc ulong[4];
            Span<ulong> other = stackalloc ulong[4];
            self[0] = a0; self[1] = a1; self[2] = a2; self[3] = a3;
            other[0] = b0; other[1] = b1; other[2] = b2; other[3] = b3;

            int width = Vector<ulong>.Count;
            for (int i = 0; i <= 4 - width; i += width)
            {
                var vs = Unsafe.As<ulong, Vector<ulong>>(ref self[i]);
                var vo = Unsafe.As<ulong, Vector<ulong>>(ref other[i]);
                if (!Vector.EqualsAll(vs & vo, Vector<ulong>.Zero))
                    return true;
            }

            return false;
        }
    }
}
#endif
