using System;
using System.Collections.Generic;

namespace Loom.Systems
{
    /// <summary>Handler for a buffered simulation event. Invoked from <see cref="Runtime.FlushEvents"/>
    /// (including the automatic flush at the end of <see cref="Runtime.Tick"/>).</summary>
    public delegate void RuntimeEventHandler<T>(Runtime runtime, in T e) where T : struct;

    /// <summary>
    /// Buffered events owned by a <see cref="Runtime"/>. <see cref="Emit{T}"/> queues;
    /// <see cref="Flush"/> delivers to subscribers. Emits that happen during a flush are queued
    /// and delivered before Flush returns (re-entrant safe, no lost events).
    /// </summary>
    internal sealed class EventBus
    {
        private readonly Dictionary<Type, IEventQueue> _queues = new Dictionary<Type, IEventQueue>();
        // Parallel list so Flush can walk queues without allocating a key snapshot each call.
        private readonly List<IEventQueue> _queueList = new List<IEventQueue>();

        public void Subscribe<T>(RuntimeEventHandler<T> handler) where T : struct
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            GetOrCreateQueue<T>().Subscribe(handler);
        }

        public void Unsubscribe<T>(RuntimeEventHandler<T> handler) where T : struct
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_queues.TryGetValue(typeof(T), out var queue))
                ((EventQueue<T>)queue).Unsubscribe(handler);
        }

        public void Emit<T>(in T e) where T : struct => GetOrCreateQueue<T>().Emit(e);

        public void Flush(Runtime runtime)
        {
            // Snapshot count: a brand-new event type subscribed mid-flush is appended to
            // _queueList and waits until the next Flush/Tick (same rule as before).
            int count = _queueList.Count;
            for (int i = 0; i < count; i++)
                _queueList[i].Flush(runtime);
        }

        /// <summary>Drops queued events; keeps subscribers.</summary>
        public void ClearPending()
        {
            for (int i = 0; i < _queueList.Count; i++)
                _queueList[i].ClearPending();
        }

        private EventQueue<T> GetOrCreateQueue<T>() where T : struct
        {
            var type = typeof(T);
            if (_queues.TryGetValue(type, out var existing))
                return (EventQueue<T>)existing;

            var created = new EventQueue<T>();
            _queues[type] = created;
            _queueList.Add(created);
            return created;
        }

        private interface IEventQueue
        {
            void Flush(Runtime runtime);
            void ClearPending();
        }

        private sealed class EventQueue<T> : IEventQueue where T : struct
        {
            private List<T> _pending = new List<T>();
            private List<T> _duringFlush = new List<T>();
            private RuntimeEventHandler<T>? _handlers;
            private bool _flushing;

            public void Subscribe(RuntimeEventHandler<T> handler) => _handlers += handler;

            public void Unsubscribe(RuntimeEventHandler<T> handler) => _handlers -= handler;

            public void Emit(in T e)
            {
                if (_flushing)
                    _duringFlush.Add(e);
                else
                    _pending.Add(e);
            }

            public void ClearPending()
            {
                _pending.Clear();
                _duringFlush.Clear();
            }

            public void Flush(Runtime runtime)
            {
                if (_handlers == null)
                {
                    _pending.Clear();
                    _duringFlush.Clear();
                    return;
                }

                _flushing = true;
                try
                {
                    while (true)
                    {
                        if (_pending.Count == 0)
                        {
                            if (_duringFlush.Count == 0)
                                break;

                            // Swap lists instead of copying elements into pending.
                            var swap = _pending;
                            _pending = _duringFlush;
                            _duringFlush = swap;
                        }

                        int count = _pending.Count;
                        for (int i = 0; i < count; i++)
                        {
                            var e = _pending[i];
                            _handlers.Invoke(runtime, e);
                        }
                        _pending.Clear();
                    }
                }
                finally
                {
                    _flushing = false;
                }
            }
        }
    }
}
