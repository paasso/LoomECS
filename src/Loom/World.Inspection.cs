using System;
using System.Reflection;
using Loom.Internal;

namespace Loom
{
    public sealed partial class World
    {
        /// <summary>Resolves a live entity by id. Returns false when the slot is unused or freed.</summary>
        public bool TryGetAliveEntity(int id, out Entity entity)
        {
            if (id > 0 && id < _nextId && _records[id].IsAlive)
            {
                entity = new Entity(id, _records[id].Version);
                return true;
            }

            entity = Entity.Null;
            return false;
        }

        /// <summary>Invokes <paramref name="action"/> for every alive entity (id order).</summary>
        public void ForEachAlive(Action<Entity> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            for (int id = 1; id < _nextId; id++)
            {
                if (_records[id].IsAlive)
                    action(new Entity(id, _records[id].Version));
            }
        }

        /// <summary>
        /// Enumerates every dense and sparse/shared component currently on <paramref name="entity"/>
        /// as boxed debug snapshots. Values are copies — mutating them does not write back unless
        /// you call <see cref="TrySetComponent"/>.
        /// </summary>
        public void ForEachComponent(Entity entity, Action<ComponentDebugInfo> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (!IsAlive(entity))
                throw new InvalidOperationException($"{entity} is not alive.");

            var registry = _componentTypes;
            var archetype = _records[entity.Id].Archetype;
            if (archetype != null)
            {
                foreach (int componentId in archetype.ComponentIds)
                {
                    var info = registry.Get(componentId);
                    if (info.IsEmpty)
                    {
                        action(new ComponentDebugInfo(info.ClrType, null, ComponentStorageKind.Tag));
                        continue;
                    }

                    action(new ComponentDebugInfo(
                        info.ClrType,
                        GetDenseBoxed(entity, componentId),
                        ComponentStorageKind.Dense));
                }
            }

            foreach (int sparseId in GetSparseMask(entity.Id).EnumerateBits())
            {
                var info = registry.GetSparse(sparseId);
                if (info.IsEmpty)
                {
                    action(new ComponentDebugInfo(info.ClrType, null, ComponentStorageKind.Tag));
                    continue;
                }

                if (!TryGetSparseBoxed(entity.Id, info.Id, out var boxed))
                {
                    throw new InvalidOperationException(
                        $"Sparse component '{info.ClrType.Name}' missing value for {entity}.");
                }

                var kind = info.IsShared ? ComponentStorageKind.Shared : ComponentStorageKind.Sparse;
                action(new ComponentDebugInfo(info.ClrType, boxed, kind));
            }
        }

        /// <summary>Writes a boxed component value onto <paramref name="entity"/>. Tags ignore
        /// <paramref name="value"/>. Shared writes mutate the interned instance (all holders see it).
        /// Returns false if the entity does not have that component type.</summary>
        public bool TrySetComponent(Entity entity, Type componentType, object? value)
        {
            if (componentType == null)
                throw new ArgumentNullException(nameof(componentType));
            if (!componentType.IsValueType)
                throw new ArgumentException("Component types must be structs.", nameof(componentType));
            if (!IsAlive(entity))
                return false;

            var method = typeof(World)
                .GetMethod(nameof(TrySetComponentTyped), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(componentType);
            return (bool)method.Invoke(this, new object?[] { entity, value })!;
        }

        private bool TrySetComponentTyped<T>(Entity entity, object? boxed) where T : struct
        {
            if (!Has<T>(entity))
                return false;

            if (ComponentTypeTraits<T>.IsEmpty)
                return true;

            T value = boxed == null ? default : (T)boxed;
            Get<T>(entity) = value;
            return true;
        }

        /// <summary>Enumerates live singletons as (CLR type, boxed value) for debug UI.</summary>
        public void ForEachSingletonDebug(Action<Type, object> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            ForEachSingleton(action);
        }

        /// <summary>Writes a boxed singleton value (creates the singleton if missing).</summary>
        public void SetSingletonDebug(Type singletonType, object value) =>
            SetSingletonBoxed(singletonType, value);

        /// <summary>Debug view of every archetype currently materialized in this world.</summary>
        public void ForEachArchetype(Action<ArchetypeDebugInfo> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            for (int i = 0; i < _archetypes.Count; i++)
                action(ToArchetypeDebugInfo(_archetypes[i]));
        }

        /// <summary>Returns the archetype table currently holding <paramref name="entity"/>.</summary>
        public bool TryGetEntityArchetype(Entity entity, out ArchetypeDebugInfo info)
        {
            if (!IsAlive(entity) || _records[entity.Id].Archetype == null)
            {
                info = default;
                return false;
            }

            info = ToArchetypeDebugInfo(_records[entity.Id].Archetype!);
            return true;
        }

        private ArchetypeDebugInfo ToArchetypeDebugInfo(Archetype archetype)
        {
            var types = new Type[archetype.ComponentIds.Length];
            for (int i = 0; i < archetype.ComponentIds.Length; i++)
                types[i] = _componentTypes.Get(archetype.ComponentIds[i]).ClrType;

            return new ArchetypeDebugInfo(
                archetype.Id,
                archetype.Count,
                archetype.Chunks.Count,
                types);
        }
    }
}
