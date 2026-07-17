using System;
using System.Collections.Generic;
using Loom.Internal;

namespace Loom.Entities
{
    /// <summary>
    /// A reusable entity recipe: an ordered list of components to apply on
    /// <see cref="Instantiate"/>. Capture from a live entity with <see cref="FromEntity"/>, or
    /// build with <see cref="Add{T}"/>.
    /// </summary>
    /// <remarks>
    /// Prefabs store component <em>values</em> only — not father/child links, systems, or
    /// singletons. Instantiation uses <see cref="World.Create()"/> then <see cref="World.Add{T}"/>
    /// for each component (correctness over micro-optimizing archetype placement).
    /// </remarks>
    public sealed class EntityPrefab
    {
        private readonly List<IPrefabComponent> _components = new List<IPrefabComponent>();

        public int ComponentCount => _components.Count;

        /// <summary>Appends a component value to this prefab. Dense, sparse, and empty tags are all
        /// supported. Order is preserved for instantiation.</summary>
        public EntityPrefab Add<T>(T value = default) where T : struct
        {
            _components.Add(new PrefabComponent<T>(value));
            return this;
        }

        /// <summary>Creates a new entity in <paramref name="world"/> and applies every stored
        /// component.</summary>
        public Entity Instantiate(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            var entity = world.Create();
            for (int i = 0; i < _components.Count; i++)
                _components[i].Apply(world, entity);
            return entity;
        }

        /// <summary>Instantiates <paramref name="destination"/>.Length copies into
        /// <paramref name="destination"/>.</summary>
        public void InstantiateMany(World world, Span<Entity> destination)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            for (int i = 0; i < destination.Length; i++)
                destination[i] = Instantiate(world);
        }

        /// <summary>Builds a prefab by copying every dense and sparse component currently on
        /// <paramref name="entity"/>.</summary>
        public static EntityPrefab FromEntity(World world, Entity entity)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (!world.IsAlive(entity))
                throw new InvalidOperationException($"{entity} is not alive.");

            var prefab = new EntityPrefab();
            var registry = world.ComponentTypes;

            var archetype = world.GetArchetype(entity);
            if (archetype != null)
            {
                foreach (int componentId in archetype.ComponentIds)
                {
                    var info = registry.Get(componentId);
                    object? boxed = info.IsEmpty ? null : world.GetDenseBoxed(entity, componentId);
                    PrefabComponentFactory.Add(prefab, info.ClrType, boxed, info.IsEmpty);
                }
            }

            foreach (int sparseId in world.GetSparseMaskForSerialization(entity.Id).EnumerateBits())
            {
                var info = registry.GetSparse(sparseId);
                object? boxed = null;
                if (!info.IsEmpty && !world.TryGetSparseBoxed(entity.Id, info.Id, out boxed))
                    throw new InvalidOperationException($"Sparse component '{info.ClrType.Name}' missing value for {entity}.");
                PrefabComponentFactory.Add(prefab, info.ClrType, boxed, info.IsEmpty);
            }

            return prefab;
        }

        private interface IPrefabComponent
        {
            void Apply(World world, Entity entity);
        }

        private sealed class PrefabComponent<T> : IPrefabComponent where T : struct
        {
            private readonly T _value;

            public PrefabComponent(T value) => _value = value;

            public void Apply(World world, Entity entity) => world.Add(entity, _value);
        }

        private static class PrefabComponentFactory
        {
            public static void Add(EntityPrefab prefab, Type clrType, object? boxed, bool isEmpty)
            {
                var method = typeof(PrefabComponentFactory)
                    .GetMethod(nameof(AddTyped))!
                    .MakeGenericMethod(clrType);
                method.Invoke(null, new object?[] { prefab, boxed, isEmpty });
            }

            public static void AddTyped<T>(EntityPrefab prefab, object? boxed, bool isEmpty) where T : struct
            {
                T value = isEmpty || boxed == null ? default : (T)boxed;
                prefab.Add(value);
            }
        }
    }

    public static class WorldPrefabExtensions
    {
        /// <summary>Shorthand for <see cref="EntityPrefab.Instantiate"/>.</summary>
        public static Entity Instantiate(this World world, EntityPrefab prefab)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));
            return prefab.Instantiate(world);
        }
    }
}
