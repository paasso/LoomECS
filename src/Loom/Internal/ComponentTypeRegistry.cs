using System;
using System.Collections.Generic;

namespace Loom.Internal
{
    internal sealed class ComponentTypeInfo
    {
        public readonly int Id;
        public readonly Type ClrType;
        public readonly bool IsSparse;
        public readonly bool IsShared;
        public readonly bool IsEmpty;

        /// <summary>Null for sparse/shared components (never part of an archetype) <em>and</em> for empty
        /// (tag) components (nothing to store — see <see cref="ComponentTypeTraits{T}.IsEmpty"/>).</summary>
        public readonly Func<int, IComponentArray>? CreateArray;

        public ComponentTypeInfo(
            int id, Type clrType, bool isSparse, bool isShared, bool isEmpty, Func<int, IComponentArray>? createArray)
        {
            Id = id;
            ClrType = clrType;
            IsSparse = isSparse;
            IsShared = isShared;
            IsEmpty = isEmpty;
            CreateArray = createArray;
        }
    }

    /// <summary>Thrown when two distinct component types hash to the same
    /// <see cref="ComponentTypeTraits{T}.DeterministicHash"/> within one <see cref="World"/> — see
    /// <see cref="ComponentTypeRegistry"/> for why this is checked instead of silently aliasing the
    /// two types.</summary>
    public sealed class ComponentHashCollisionException : InvalidOperationException
    {
        public ComponentHashCollisionException(Type existing, Type incoming, int hash)
            : base($"Component type hash collision: '{existing.FullName}' and '{incoming.FullName}' both hash to {hash}. " +
                   "Rename one of the types, or adjust DeterministicHashProvider.StartHash/HashFactor for this process.")
        {
        }
    }

    /// <summary>
    /// Assigns every component type a stable integer id the first time <see cref="World"/> sees it.
    /// One registry per <see cref="World"/> instance (not a process-wide static) — this is what lets
    /// two independent worlds assign different ids to the same CLR type, or run entirely different
    /// component sets, without stepping on each other or sharing either cap below.
    /// <para/>
    /// Dense ids double as bit indices into <see cref="Archetype.Mask"/>; sparse ids double as bit
    /// indices into <c>EntityRecord.SparseMask</c>. Those are two <em>independent</em>
    /// <see cref="ComponentMask"/> instances — a dense id and a sparse id are never tested against
    /// the same mask, so they're assigned from two separate counters, each capped at
    /// <see cref="ComponentMask.Capacity"/> on its own. (An earlier version shared one counter for
    /// both, which capped the *combined* dense+sparse total at Capacity instead of allowing that
    /// many of each — a real bug, since the two id spaces never actually collide.)
    /// </summary>
    internal sealed class ComponentTypeRegistry
    {
        // Keyed by ComponentTypeTraits<T>.DeterministicHash rather than typeof(T) itself: the caller
        // already has that hash sitting in a static readonly field (computed once per closed T, at
        // type-init time), so a lookup here is a raw int compare with no virtual
        // GetHashCode()/Equals() dispatch through System.Type. ClrType is still stored on
        // ComponentTypeInfo and checked on every hit, so a hash collision between two distinct
        // component types fails loudly (see ComponentHashCollisionException) instead of silently
        // treating them as the same component.
        private readonly Dictionary<int, ComponentTypeInfo> _byHash = new Dictionary<int, ComponentTypeInfo>();

        // Dense ids are looked up during archetype/chunk work; sparse ids are looked up when
        // serializing an entity's sparse mask bits back to CLR types.
        private readonly List<ComponentTypeInfo> _denseById = new List<ComponentTypeInfo>();
        private readonly List<ComponentTypeInfo> _sparseById = new List<ComponentTypeInfo>();

        public ComponentTypeInfo GetOrRegister<T>() where T : struct
        {
            // Per-closed-T last-hit cache: structural Add/Remove hammers the same few types on one
            // World. Multi-world ping-pong only loses the fast path (still correct).
            if (ReferenceEquals(LastHitCache<T>.Registry, this))
                return LastHitCache<T>.Info!;

            int hash = ComponentTypeTraits<T>.DeterministicHash;
            if (_byHash.TryGetValue(hash, out var existing))
            {
                if (existing.ClrType != typeof(T))
                    throw new ComponentHashCollisionException(existing.ClrType, typeof(T), hash);
                LastHitCache<T>.Registry = this;
                LastHitCache<T>.Info = existing;
                return existing;
            }

            var type = typeof(T);
            bool isSparse = ComponentTypeTraits<T>.IsSparse;
            bool isShared = ComponentTypeTraits<T>.IsShared;
            bool isEmpty = ComponentTypeTraits<T>.IsEmpty;
            bool usesSparseIds = isSparse || isShared;

            int id;
            if (usesSparseIds)
            {
                if (_sparseById.Count >= ComponentMask.Capacity)
                {
                    throw new InvalidOperationException(
                        $"Cannot register sparse/shared component type '{type.Name}': this world's limit of " +
                        $"{ComponentMask.Capacity} sparse-mask component types has been reached.");
                }
                id = _sparseById.Count;
            }
            else
            {
                if (_denseById.Count >= ComponentMask.Capacity)
                {
                    throw new InvalidOperationException(
                        $"Cannot register dense component type '{type.Name}': this world's limit of " +
                        $"{ComponentMask.Capacity} dense component types has been reached.");
                }
                id = _denseById.Count;
            }

            bool skipsStorage = usesSparseIds || isEmpty;
            var info = new ComponentTypeInfo(
                id, type, isSparse, isShared, isEmpty,
                skipsStorage ? null : capacity => new ComponentArray<T>(capacity));

            _byHash[hash] = info;
            if (usesSparseIds)
                _sparseById.Add(info);
            else
                _denseById.Add(info);

            LastHitCache<T>.Registry = this;
            LastHitCache<T>.Info = info;
            return info;
        }

        private static class LastHitCache<T> where T : struct
        {
            [ThreadStatic] public static ComponentTypeRegistry? Registry;
            [ThreadStatic] public static ComponentTypeInfo? Info;
        }

        /// <summary>Looks up a <em>dense</em> component's info by its dense-space id. Never valid for a sparse id.</summary>
        public ComponentTypeInfo Get(int denseId) => _denseById[denseId];

        /// <summary>Looks up a <em>sparse</em> component's info by its sparse-space id.</summary>
        public ComponentTypeInfo GetSparse(int sparseId) => _sparseById[sparseId];
    }
}
