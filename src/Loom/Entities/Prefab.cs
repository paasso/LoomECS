using System;
using System.Collections.Generic;

namespace Loom.Entities
{
    /// <summary>
    /// Reusable entity blueprint: an ordered list of components applied when
    /// <see cref="Spawn(World)"/> / <see cref="Spawn(CommandBuffer)"/> runs. Built with fluent
    /// <see cref="With{T}"/> calls; not itself an entity and not tied to one <see cref="World"/>.
    /// </summary>
    public sealed class Prefab
    {
        private readonly List<Step> _steps = new List<Step>();

        /// <summary>Appends component <typeparamref name="T"/> (value copied into the prefab).</summary>
        public Prefab With<T>(T component = default) where T : struct
        {
            T copy = component;
            _steps.Add(new Step(
                (world, entity) => world.Add(entity, copy),
                (commands, entity) => commands.Add(entity, copy)));
            return this;
        }

        public int ComponentCount => _steps.Count;

        /// <summary>Creates an entity immediately and applies every component (may move archetypes
        /// once per dense component — fine for spawn, not a hot bulk path).</summary>
        public Entity Spawn(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            var entity = world.Create();
            for (int i = 0; i < _steps.Count; i++)
                _steps[i].ApplyImmediate(world, entity);
            return entity;
        }

        /// <summary>Reserves an entity on <paramref name="commands"/> and queues every component add.
        /// The entity appears in queries after the buffer is played back.</summary>
        public Entity Spawn(CommandBuffer commands)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            var entity = commands.Create();
            for (int i = 0; i < _steps.Count; i++)
                _steps[i].ApplyDeferred(commands, entity);
            return entity;
        }

        /// <summary>Spawns <paramref name="destination"/>.Length copies via immediate
        /// <see cref="Spawn(World)"/>.</summary>
        public void SpawnMany(World world, Span<Entity> destination)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            for (int i = 0; i < destination.Length; i++)
                destination[i] = Spawn(world);
        }

        /// <summary>Spawns into <paramref name="destination"/> via <paramref name="commands"/>.</summary>
        public void SpawnMany(CommandBuffer commands, Span<Entity> destination)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            for (int i = 0; i < destination.Length; i++)
                destination[i] = Spawn(commands);
        }

        private readonly struct Step
        {
            public readonly Action<World, Entity> ApplyImmediate;
            public readonly Action<CommandBuffer, Entity> ApplyDeferred;

            public Step(Action<World, Entity> applyImmediate, Action<CommandBuffer, Entity> applyDeferred)
            {
                ApplyImmediate = applyImmediate;
                ApplyDeferred = applyDeferred;
            }
        }
    }
}
