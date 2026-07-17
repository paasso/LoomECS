using System.Collections.Generic;

namespace Loom.Internal
{
    /// <summary>
    /// Enumerates the archetypes matching dense With / Without / WithAny masks. Hand-written struct
    /// enumerable/enumerator instead of a `yield return` iterator method: a `yield return` method
    /// compiles to a heap-allocated class implementing IEnumerable/IEnumerator, so every
    /// <c>Query.Each/ForEach/ToList</c> call would allocate one. This type and its nested
    /// <see cref="Enumerator"/> are both structs with a `GetEnumerator()`/`MoveNext()`/`Current`
    /// shape, which is all `foreach` needs (duck typing, resolved at compile time) — so iterating
    /// this costs nothing beyond the underlying <see cref="List{T}"/> walk.
    /// </summary>
    internal readonly struct ArchetypeMatchEnumerable
    {
        private readonly List<Archetype> _archetypes;
        private readonly ComponentMask _all;
        private readonly ComponentMask _none;
        private readonly ComponentMask _any;
        private readonly bool _hasAny;

        public ArchetypeMatchEnumerable(
            List<Archetype> archetypes, ComponentMask all, ComponentMask none, ComponentMask any)
        {
            _archetypes = archetypes;
            _all = all;
            _none = none;
            _any = any;
            _hasAny = any != ComponentMask.Empty;
        }

        public Enumerator GetEnumerator() => new Enumerator(_archetypes, _all, _none, _any, _hasAny);

        public struct Enumerator
        {
            private readonly List<Archetype> _archetypes;
            private readonly ComponentMask _all;
            private readonly ComponentMask _none;
            private readonly ComponentMask _any;
            private readonly bool _hasAny;
            private int _index;

            internal Enumerator(
                List<Archetype> archetypes,
                ComponentMask all,
                ComponentMask none,
                ComponentMask any,
                bool hasAny)
            {
                _archetypes = archetypes;
                _all = all;
                _none = none;
                _any = any;
                _hasAny = hasAny;
                _index = -1;
                Current = null!;
            }

            public Archetype Current { get; private set; }

            public bool MoveNext()
            {
                while (++_index < _archetypes.Count)
                {
                    var candidate = _archetypes[_index];
                    if (candidate.Count == 0) continue;
                    if (!candidate.Mask.ContainsAll(_all)) continue;
                    if (candidate.Mask.IntersectsAny(_none)) continue;
                    if (_hasAny && !candidate.Mask.IntersectsAny(_any)) continue;

                    Current = candidate;
                    return true;
                }
                return false;
            }
        }
    }
}
