using System;
using System.Collections.Generic;

namespace Loom.Components
{
    /// <summary>
    /// Fixed-width signature identifying which *dense* (archetype-tracked) component types an
    /// archetype contains, and (independently, in <c>EntityRecord.SparseMask</c>) which sparse
    /// component types an entity has. Sparse presence never sets a bit in an <c>Archetype.Mask</c>
    /// — sparse components live outside the archetype graph entirely, in per-type
    /// <see cref="Internal.SparseSet{T}"/> storage.
    /// <para/>
    /// Width is a compile-time choice: define exactly one of <c>ECS_MASK_64</c> (1 word, 64
    /// components) or <c>ECS_MASK_128</c> (2 words, 128 components) as an MSBuild
    /// <c>DefineConstants</c>/preprocessor symbol to shrink this struct (and therefore
    /// <c>Archetype.Mask</c> and every <c>EntityRecord</c>'s sparse mask) for a project that will
    /// never need anywhere close to the 256-component default. Leave both undefined for the
    /// default 4-word / 256-component capacity. Each tier is a fully separate implementation (not
    /// a parameterized one) so there's no per-call branching on width — the compiled struct simply
    /// doesn't have the unused fields.
    /// </summary>
    public readonly struct ComponentMask : IEquatable<ComponentMask>
    {
#if ECS_MASK_64
        public const int Capacity = 64;

        private readonly ulong _b0;

        private ComponentMask(ulong b0)
        {
            _b0 = b0;
        }

        public static readonly ComponentMask Empty = default;

        public bool Get(int bit)
        {
            CheckBit(bit);
            return (_b0 & (1UL << bit)) != 0;
        }

        public ComponentMask With(int bit)
        {
            CheckBit(bit);
            return new ComponentMask(_b0 | (1UL << bit));
        }

        public ComponentMask Without(int bit)
        {
            CheckBit(bit);
            return new ComponentMask(_b0 & ~(1UL << bit));
        }

        public bool ContainsAll(in ComponentMask required) => (_b0 & required._b0) == required._b0;

        public bool IntersectsAny(in ComponentMask other) => (_b0 & other._b0) != 0;

        public int[] EnumerateBits()
        {
            var list = new List<int>();
            CollectWord(_b0, 0, list);
            return list.ToArray();
        }

        public BitEnumerator GetEnumerator() => new BitEnumerator(_b0);

        public bool Equals(ComponentMask other) => _b0 == other._b0;

        public override int GetHashCode() => _b0.GetHashCode();

        private static void CheckBit(int bit)
        {
            if ((uint)bit >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(bit), $"Component id {bit} exceeds ComponentMask.Capacity ({Capacity}).");
        }
#elif ECS_MASK_128
        public const int Capacity = 128;

        private readonly ulong _b0;
        private readonly ulong _b1;

        private ComponentMask(ulong b0, ulong b1)
        {
            _b0 = b0;
            _b1 = b1;
        }

        public static readonly ComponentMask Empty = default;

        public bool Get(int bit)
        {
            int w = bit >> 6;
            ulong m = 1UL << (bit & 63);
            switch (w)
            {
                case 0: return (_b0 & m) != 0;
                case 1: return (_b1 & m) != 0;
                default: throw new ArgumentOutOfRangeException(nameof(bit), $"Component id {bit} exceeds ComponentMask.Capacity ({Capacity}).");
            }
        }

        public ComponentMask With(int bit)
        {
            int w = bit >> 6;
            ulong m = 1UL << (bit & 63);
            switch (w)
            {
                case 0: return new ComponentMask(_b0 | m, _b1);
                case 1: return new ComponentMask(_b0, _b1 | m);
                default: throw new ArgumentOutOfRangeException(nameof(bit), $"Component id {bit} exceeds ComponentMask.Capacity ({Capacity}).");
            }
        }

        public ComponentMask Without(int bit)
        {
            int w = bit >> 6;
            ulong m = ~(1UL << (bit & 63));
            switch (w)
            {
                case 0: return new ComponentMask(_b0 & m, _b1);
                case 1: return new ComponentMask(_b0, _b1 & m);
                default: throw new ArgumentOutOfRangeException(nameof(bit), $"Component id {bit} exceeds ComponentMask.Capacity ({Capacity}).");
            }
        }

        public bool ContainsAll(in ComponentMask required) =>
            (_b0 & required._b0) == required._b0 &&
            (_b1 & required._b1) == required._b1;

        public bool IntersectsAny(in ComponentMask other) =>
            (_b0 & other._b0) != 0 ||
            (_b1 & other._b1) != 0;

        public int[] EnumerateBits()
        {
            var list = new List<int>();
            CollectWord(_b0, 0, list);
            CollectWord(_b1, 64, list);
            return list.ToArray();
        }

        public BitEnumerator GetEnumerator() => new BitEnumerator(_b0, _b1);

        public bool Equals(ComponentMask other) => _b0 == other._b0 && _b1 == other._b1;

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _b0.GetHashCode();
                hash = hash * 31 + _b1.GetHashCode();
                return hash;
            }
        }
#else
        public const int Capacity = 256;

        private readonly ulong _b0;
        private readonly ulong _b1;
        private readonly ulong _b2;
        private readonly ulong _b3;

        private ComponentMask(ulong b0, ulong b1, ulong b2, ulong b3)
        {
            _b0 = b0;
            _b1 = b1;
            _b2 = b2;
            _b3 = b3;
        }

        public static readonly ComponentMask Empty = default;

        public bool Get(int bit)
        {
            int w = bit >> 6;
            ulong m = 1UL << (bit & 63);
            switch (w)
            {
                case 0: return (_b0 & m) != 0;
                case 1: return (_b1 & m) != 0;
                case 2: return (_b2 & m) != 0;
                case 3: return (_b3 & m) != 0;
                default: throw new ArgumentOutOfRangeException(nameof(bit), $"Component id {bit} exceeds ComponentMask.Capacity ({Capacity}).");
            }
        }

        public ComponentMask With(int bit)
        {
            int w = bit >> 6;
            ulong m = 1UL << (bit & 63);
            switch (w)
            {
                case 0: return new ComponentMask(_b0 | m, _b1, _b2, _b3);
                case 1: return new ComponentMask(_b0, _b1 | m, _b2, _b3);
                case 2: return new ComponentMask(_b0, _b1, _b2 | m, _b3);
                case 3: return new ComponentMask(_b0, _b1, _b2, _b3 | m);
                default: throw new ArgumentOutOfRangeException(nameof(bit), $"Component id {bit} exceeds ComponentMask.Capacity ({Capacity}).");
            }
        }

        public ComponentMask Without(int bit)
        {
            int w = bit >> 6;
            ulong m = ~(1UL << (bit & 63));
            switch (w)
            {
                case 0: return new ComponentMask(_b0 & m, _b1, _b2, _b3);
                case 1: return new ComponentMask(_b0, _b1 & m, _b2, _b3);
                case 2: return new ComponentMask(_b0, _b1, _b2 & m, _b3);
                case 3: return new ComponentMask(_b0, _b1, _b2, _b3 & m);
                default: throw new ArgumentOutOfRangeException(nameof(bit), $"Component id {bit} exceeds ComponentMask.Capacity ({Capacity}).");
            }
        }

        /// <summary>True if this mask has every bit set that <paramref name="required"/> has.</summary>
        public bool ContainsAll(in ComponentMask required) =>
#if LOOM_SIMD
            Internal.MaskSimd.ContainsAll4(
                _b0, _b1, _b2, _b3,
                required._b0, required._b1, required._b2, required._b3);
#else
            (_b0 & required._b0) == required._b0 &&
            (_b1 & required._b1) == required._b1 &&
            (_b2 & required._b2) == required._b2 &&
            (_b3 & required._b3) == required._b3;
#endif

        /// <summary>True if this mask shares at least one set bit with <paramref name="other"/>.</summary>
        public bool IntersectsAny(in ComponentMask other) =>
#if LOOM_SIMD
            Internal.MaskSimd.IntersectsAny4(
                _b0, _b1, _b2, _b3,
                other._b0, other._b1, other._b2, other._b3);
#else
            (_b0 & other._b0) != 0 ||
            (_b1 & other._b1) != 0 ||
            (_b2 & other._b2) != 0 ||
            (_b3 & other._b3) != 0;
#endif

        public int[] EnumerateBits()
        {
            var list = new List<int>();
            CollectWord(_b0, 0, list);
            CollectWord(_b1, 64, list);
            CollectWord(_b2, 128, list);
            CollectWord(_b3, 192, list);
            return list.ToArray();
        }

        public BitEnumerator GetEnumerator() => new BitEnumerator(_b0, _b1, _b2, _b3);

        public bool Equals(ComponentMask other) =>
            _b0 == other._b0 && _b1 == other._b1 && _b2 == other._b2 && _b3 == other._b3;

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _b0.GetHashCode();
                hash = hash * 31 + _b1.GetHashCode();
                hash = hash * 31 + _b2.GetHashCode();
                hash = hash * 31 + _b3.GetHashCode();
                return hash;
            }
        }
#endif

        /// <summary>Sets a bit in a stored mask without rewriting sibling words (mutates the
        /// <paramref name="mask"/> location in place). Safe for sparse-presence slots; do not use on
        /// masks that are dictionary keys or shared immutable values.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static void SetBit(ref ComponentMask mask, int bit)
        {
            CheckBitStatic(bit);
            System.Runtime.CompilerServices.Unsafe.Add(
                ref System.Runtime.CompilerServices.Unsafe.As<ComponentMask, ulong>(ref mask),
                bit >> 6) |= 1UL << (bit & 63);
        }

        /// <summary>Clears a bit in a stored mask without rewriting sibling words — see
        /// <see cref="SetBit"/>.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static void ClearBit(ref ComponentMask mask, int bit)
        {
            CheckBitStatic(bit);
            System.Runtime.CompilerServices.Unsafe.Add(
                ref System.Runtime.CompilerServices.Unsafe.As<ComponentMask, ulong>(ref mask),
                bit >> 6) &= ~(1UL << (bit & 63));
        }

        private static void CheckBitStatic(int bit)
        {
            if ((uint)bit >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(bit), $"Component id {bit} exceeds ComponentMask.Capacity ({Capacity}).");
        }

        // --- Members below this point are identical across every tier. ---

        public override bool Equals(object obj) => obj is ComponentMask other && Equals(other);

        public static bool operator ==(ComponentMask a, ComponentMask b) => a.Equals(b);
        public static bool operator !=(ComponentMask a, ComponentMask b) => !a.Equals(b);

        private static void CollectWord(ulong word, int offset, List<int> list)
        {
            while (word != 0)
            {
                int tz = TrailingZeroCount(word);
                list.Add(offset + tz);
                word &= word - 1; // clear lowest set bit
            }
        }

        private static int TrailingZeroCount(ulong value)
        {
            int count = 0;
            while ((value & 1) == 0)
            {
                value >>= 1;
                count++;
            }
            return count;
        }

        /// <summary>Zero-alloc enumerator over set bit indices. Prefer
        /// <c>foreach (int id in mask)</c> over <see cref="EnumerateBits"/> on hot paths.</summary>
        public struct BitEnumerator
        {
            private readonly ulong _b0;
            private readonly ulong _b1;
            private readonly ulong _b2;
            private readonly ulong _b3;
            private readonly int _wordCount;
            private int _wordIndex;
            private ulong _word;
            private int _current;

            public BitEnumerator(ulong b0)
            {
                _b0 = b0;
                _b1 = _b2 = _b3 = 0;
                _wordCount = 1;
                _wordIndex = -1;
                _word = 0;
                _current = 0;
            }

            public BitEnumerator(ulong b0, ulong b1)
            {
                _b0 = b0;
                _b1 = b1;
                _b2 = _b3 = 0;
                _wordCount = 2;
                _wordIndex = -1;
                _word = 0;
                _current = 0;
            }

            public BitEnumerator(ulong b0, ulong b1, ulong b2, ulong b3)
            {
                _b0 = b0;
                _b1 = b1;
                _b2 = b2;
                _b3 = b3;
                _wordCount = 4;
                _wordIndex = -1;
                _word = 0;
                _current = 0;
            }

            public int Current => _current;

            public bool MoveNext()
            {
                while (true)
                {
                    if (_word != 0)
                    {
                        int tz = TrailingZeroCount(_word);
                        _current = (_wordIndex << 6) + tz;
                        _word &= _word - 1;
                        return true;
                    }

                    _wordIndex++;
                    if (_wordIndex >= _wordCount)
                        return false;

                    switch (_wordIndex)
                    {
                        case 0: _word = _b0; break;
                        case 1: _word = _b1; break;
                        case 2: _word = _b2; break;
                        default: _word = _b3; break;
                    }
                }
            }
        }
    }
}
