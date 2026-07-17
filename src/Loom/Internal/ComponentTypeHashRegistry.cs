using System;
using System.Collections.Generic;

namespace Loom.Internal
{
    /// <summary>
    /// Process-wide record of every <c>ComponentTypeTraits&lt;T&gt;.DetermenisticHash</c> ever
    /// computed, mapped back to the <see cref="Type"/> it came from. A closed generic type's static
    /// constructor runs at most once for the whole process, so <see cref="RegisterOrThrow"/> — called
    /// from <see cref="ComponentTypeTraits{T}"/>'s static constructor — only ever runs once per T,
    /// but different T's constructors can run concurrently on different threads, hence the lock.
    /// </summary>
    internal static class ComponentTypeHashRegistry
    {
        private static readonly Dictionary<int, Type> _typeByHash = new Dictionary<int, Type>();
        private static readonly object _lock = new object();

        public static void RegisterOrThrow(int hash, Type type)
        {
            lock (_lock)
            {
                if (_typeByHash.TryGetValue(hash, out var existing))
                {
                    if (existing != type)
                        throw new ComponentHashCollisionException(existing, type, hash);
                    return;
                }
                _typeByHash[hash] = type;
            }
        }
    }
}
