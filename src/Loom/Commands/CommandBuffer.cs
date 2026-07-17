using System;
using System.Collections.Generic;
using Loom.Internal;

namespace Loom.Commands
{
    /// <summary>
    /// Records structural changes and plays them back later — safe to fill from inside
    /// <c>Query.Each</c> without invalidating the iteration. Each <see cref="ISystem"/> receives
    /// a buffer that <see cref="SystemGroup.Run"/> plays back before the next system.
    /// <see cref="Runtime.Commands"/> is played back after every <see cref="Runtime.Run"/>;
    /// <see cref="World.CreateCommandBuffer"/> makes an independent one you play yourself.
    /// </summary>
    /// <remarks>
    /// <see cref="Create()"/> reserves a real <see cref="Entity"/> id immediately so you can queue
    /// <see cref="Add{T}"/> against it before playback, but the entity is not placed in an
    /// archetype (and will not appear in queries) until <see cref="Playback"/>. Commands run in
    /// record order.
    /// <para>
    /// Recording is allocation-light: each command is a struct header in a linear list.
    /// <see cref="Add{T}"/> stores values in a reused per-type <c>List&lt;T&gt;</c> (no boxing of
    /// the component value, no per-command heap object).
    /// </para>
    /// </remarks>
    public sealed class CommandBuffer
    {
        private enum Kind : byte
        {
            Create = 0,
            Destroy = 1,
            Add = 2,
            Remove = 3,
        }

        private struct Header
        {
            public Kind Kind;
            public Entity Entity;
            /// <summary>For Add: index into the typed value bag. For Remove: index into
            /// <see cref="_removeActions"/>. Unused for Create/Destroy.</summary>
            public int Payload;
            /// <summary>For Add: <see cref="ComponentTypeTraits{T}.DetermenisticHash"/> of the
            /// component type (selects the value bag).</summary>
            public int TypeKey;
        }

        private readonly World _world;
        private readonly List<Header> _headers = new List<Header>();
        private readonly Dictionary<int, ValueBag> _addBags = new Dictionary<int, ValueBag>();
        private readonly List<Action<World, Entity>> _removeActions = new List<Action<World, Entity>>();

        public CommandBuffer(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public int PendingCount => _headers.Count;

        /// <summary>Reserves an entity id and queues placement into the empty archetype on
        /// playback. The returned handle is alive but not queryable until <see cref="Playback"/>.</summary>
        public Entity Create()
        {
            var entity = _world.ReserveEntityForCommandBuffer();
            _headers.Add(new Header { Kind = Kind.Create, Entity = entity });
            return entity;
        }

        public Entity Create<T1>(T1 component1) where T1 : struct
        {
            var entity = Create();
            Add(entity, component1);
            return entity;
        }

        public Entity Create<T1, T2>(T1 component1, T2 component2) where T1 : struct where T2 : struct
        {
            var entity = Create();
            Add(entity, component1);
            Add(entity, component2);
            return entity;
        }

        public void Destroy(Entity entity) =>
            _headers.Add(new Header { Kind = Kind.Destroy, Entity = entity });

        public void Add<T>(Entity entity, T value = default) where T : struct
        {
            int typeKey = ComponentTypeTraits<T>.DetermenisticHash;
            if (!_addBags.TryGetValue(typeKey, out var bag))
            {
                bag = new ValueBag<T>();
                _addBags[typeKey] = bag;
            }

            var typed = (ValueBag<T>)bag;
            int index = typed.Count;
            typed.Add(value);
            _headers.Add(new Header
            {
                Kind = Kind.Add,
                Entity = entity,
                Payload = index,
                TypeKey = typeKey,
            });
        }

        public void Remove<T>(Entity entity) where T : struct
        {
            int actionIndex = _removeActions.Count;
            _removeActions.Add(RemoveActionCache<T>.Action);
            _headers.Add(new Header
            {
                Kind = Kind.Remove,
                Entity = entity,
                Payload = actionIndex,
            });
        }

        /// <summary>Applies every recorded command in order, then clears the buffer.</summary>
        public void Playback()
        {
            for (int i = 0; i < _headers.Count; i++)
            {
                var header = _headers[i];
                switch (header.Kind)
                {
                    case Kind.Create:
                        _world.PlayCommandBufferCreate(header.Entity);
                        break;
                    case Kind.Destroy:
                        if (_world.IsAlive(header.Entity))
                            _world.Destroy(header.Entity);
                        break;
                    case Kind.Add:
                        _addBags[header.TypeKey].Apply(_world, header.Entity, header.Payload);
                        break;
                    case Kind.Remove:
                        _removeActions[header.Payload](_world, header.Entity);
                        break;
                }
            }

            ResetBuffers();
        }

        /// <summary>Discards pending commands. Reserved-but-not-yet-placed entities from
        /// <see cref="Create()"/> are destroyed so their ids are not leaked.</summary>
        public void Clear()
        {
            for (int i = 0; i < _headers.Count; i++)
            {
                var header = _headers[i];
                if (header.Kind == Kind.Create && _world.IsAlive(header.Entity))
                    _world.Destroy(header.Entity);
            }

            ResetBuffers();
        }

        private void ResetBuffers()
        {
            _headers.Clear();
            _removeActions.Clear();
            foreach (var bag in _addBags.Values)
                bag.Clear();
        }

        private abstract class ValueBag
        {
            public abstract void Apply(World world, Entity entity, int index);
            public abstract void Clear();
        }

        private sealed class ValueBag<T> : ValueBag where T : struct
        {
            private readonly List<T> _values = new List<T>();

            public int Count => _values.Count;

            public void Add(T value) => _values.Add(value);

            public override void Apply(World world, Entity entity, int index) =>
                world.Add(entity, _values[index]);

            public override void Clear() => _values.Clear();
        }

        private static class RemoveActionCache<T> where T : struct
        {
            // One static delegate per closed T for the process — never allocated per Remove call.
            public static readonly Action<World, Entity> Action = Invoke;

            private static void Invoke(World world, Entity entity) => world.Remove<T>(entity);
        }
    }
}
