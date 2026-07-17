using System;

namespace Loom.Queries
{
    public ref partial struct Query
    {
        /// <summary>
        /// Returns a <see cref="ChangeQuery"/> over entities with a recorded <c>Added</c> for
        /// <typeparamref name="T"/> this frame. Implies <see cref="With{T}"/>.
        /// Requires <see cref="World.TrackChanges{T}"/>. Carries current With/Without masks.
        /// </summary>
        public ChangeQuery Added<T>() where T : struct
        {
            EnsureTracking<T>();
            With<T>();
            return ChangeQuery.Added(
                _world, _world.GetAddedEntities<T>(),
                _denseAll, _denseNone, _denseAny,
                _sparseAll, _sparseNone, _sparseAny);
        }

        /// <summary>
        /// Returns a <see cref="ChangeQuery"/> over entities with a recorded <c>Changed</c> for
        /// <typeparamref name="T"/> this frame. Implies <see cref="With{T}"/>.
        /// Requires <see cref="World.TrackChanges{T}"/>. Carries current With/Without masks.
        /// </summary>
        public ChangeQuery Changed<T>() where T : struct
        {
            EnsureTracking<T>();
            With<T>();
            return ChangeQuery.Changed(
                _world, _world.GetChangedEntities<T>(),
                _denseAll, _denseNone, _denseAny,
                _sparseAll, _sparseNone, _sparseAny);
        }

        /// <summary>
        /// Returns a <see cref="ChangeQuery"/> over entities with a recorded <c>Removed</c> for
        /// <typeparamref name="T"/> this frame. Does <em>not</em> add <see cref="With{T}"/>
        /// (the component is gone). Requires <see cref="World.TrackChanges{T}"/>.
        /// Carries current With/Without masks. <see cref="ChangeQuery.Each{T1}"/> still requires
        /// live components for its type arguments.
        /// </summary>
        public ChangeQuery Removed<T>() where T : struct
        {
            EnsureTracking<T>();
            return ChangeQuery.Removed(
                _world, _world.GetRemovedEntities<T>(),
                _denseAll, _denseNone, _denseAny,
                _sparseAll, _sparseNone, _sparseAny);
        }

        private void EnsureTracking<T>() where T : struct
        {
            if (!_world.IsTrackingChanges<T>())
            {
                throw new InvalidOperationException(
                    $"ChangeQuery requires World.TrackChanges<{typeof(T).Name}>() first.");
            }
        }
    }
}
