using System;
using System.Collections.Generic;

namespace Loom.Systems
{
    /// <summary>
    /// Owns command buffers and events for a <see cref="World"/>.
    /// Does not own system groups — create <see cref="SystemGroup"/>s yourself and drive them
    /// with <see cref="Run"/> / <see cref="EndFrame"/> at whatever order and rate you need.
    /// </summary>
    public sealed class Runtime
    {
        private readonly CommandBuffer _commands;
        private readonly CommandBuffer _systemCommands;
        private readonly EventBus _events = new EventBus();

        public Runtime(World world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            _commands = new CommandBuffer(world);
            _systemCommands = new CommandBuffer(world);
        }

        /// <summary>Entity/component storage this runtime drives.</summary>
        public World World { get; }

        /// <summary>Deferred structural mutations outside the per-system buffer.
        /// Played back after every <see cref="Run"/>, and again in <see cref="EndFrame"/>
        /// if anything remains.</summary>
        public CommandBuffer Commands => _commands;

        /// <summary>Shared buffer passed into each <see cref="ISystem.Update"/>; played back by
        /// <see cref="SystemGroup"/> after every sequential system.</summary>
        internal CommandBuffer SystemCommandBuffer => _systemCommands;

        /// <summary>
        /// Runs <paramref name="group"/>, then plays back <see cref="Commands"/> so the next
        /// group sees structural changes recorded on the runtime buffer.
        /// </summary>
        public void Run(SystemGroup group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));
            group.Run(this);
            _commands.Playback();
        }

        /// <summary>
        /// End of a display frame: play back any remaining <see cref="Commands"/>, flush events,
        /// clear change-tracking. Call once after the group runs for this frame.
        /// </summary>
        public void EndFrame()
        {
            _commands.Playback();
            FlushEvents();
            World.ClearComponentChanges();
        }

        /// <summary>Queues <paramref name="e"/> for delivery on the next <see cref="FlushEvents"/>
        /// / <see cref="EndFrame"/>.</summary>
        public void Emit<T>(in T e) where T : struct => _events.Emit(in e);

        /// <summary>Registers a handler invoked during <see cref="FlushEvents"/> for every queued
        /// event of type <typeparamref name="T"/>.</summary>
        public void Subscribe<T>(RuntimeEventHandler<T> handler) where T : struct =>
            _events.Subscribe(handler);

        /// <summary>Removes a previously registered handler.</summary>
        public void Unsubscribe<T>(RuntimeEventHandler<T> handler) where T : struct =>
            _events.Unsubscribe(handler);

        /// <summary>Delivers every queued event to its subscribers, then clears the queues.</summary>
        public void FlushEvents() => _events.Flush(this);

        /// <summary>
        /// Clears pending commands and queued events, then destroys every entity on
        /// <see cref="World"/>. Event subscribers stay registered; system groups are owned by you.
        /// </summary>
        public void ClearEntities(bool clearSingletons = false)
        {
            _commands.Clear();
            _systemCommands.Clear();
            _events.ClearPending();
            World.ClearEntities(clearSingletons);
        }
    }
}
