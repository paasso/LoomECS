using System;
using System.Collections.Generic;
using Loom.Internal;

namespace Loom.Queries
{
    /// <summary>
    /// Frame-scoped iteration over Added / Removed / Changed entity lists from
    /// <see cref="World.TrackChanges{T}"/>. Returned by <see cref="Query.Added{T}"/>,
    /// <see cref="Query.Changed{T}"/>, and <see cref="Query.Removed{T}"/>.
    /// Supports further <see cref="With{T}"/> / <see cref="Without{T}"/> filters; does not
    /// participate in <see cref="Query.ToFilter"/> or Parallel iteration.
    /// </summary>
    public ref struct ChangeQuery
    {
        private enum Kind : byte
        {
            Added = 1,
            Removed = 2,
            Changed = 3,
        }

        private readonly World _world;
        private readonly List<Entity> _entities;
        private readonly Kind _kind;
        private ComponentMask _denseAll;
        private ComponentMask _denseNone;
        private ComponentMask _denseAny;
        private ComponentMask _sparseAll;
        private ComponentMask _sparseNone;
        private ComponentMask _sparseAny;

        private ChangeQuery(
            World world,
            List<Entity> entities,
            Kind kind,
            ComponentMask denseAll,
            ComponentMask denseNone,
            ComponentMask denseAny,
            ComponentMask sparseAll,
            ComponentMask sparseNone,
            ComponentMask sparseAny)
        {
            _world = world;
            _entities = entities;
            _kind = kind;
            _denseAll = denseAll;
            _denseNone = denseNone;
            _denseAny = denseAny;
            _sparseAll = sparseAll;
            _sparseNone = sparseNone;
            _sparseAny = sparseAny;
        }

        internal static ChangeQuery Added(
            World world, List<Entity> entities,
            ComponentMask denseAll, ComponentMask denseNone, ComponentMask denseAny,
            ComponentMask sparseAll, ComponentMask sparseNone, ComponentMask sparseAny) =>
            new ChangeQuery(world, entities, Kind.Added,
                denseAll, denseNone, denseAny, sparseAll, sparseNone, sparseAny);

        internal static ChangeQuery Removed(
            World world, List<Entity> entities,
            ComponentMask denseAll, ComponentMask denseNone, ComponentMask denseAny,
            ComponentMask sparseAll, ComponentMask sparseNone, ComponentMask sparseAny) =>
            new ChangeQuery(world, entities, Kind.Removed,
                denseAll, denseNone, denseAny, sparseAll, sparseNone, sparseAny);

        internal static ChangeQuery Changed(
            World world, List<Entity> entities,
            ComponentMask denseAll, ComponentMask denseNone, ComponentMask denseAny,
            ComponentMask sparseAll, ComponentMask sparseNone, ComponentMask sparseAny) =>
            new ChangeQuery(world, entities, Kind.Changed,
                denseAll, denseNone, denseAny, sparseAll, sparseNone, sparseAny);

        public ChangeQuery With<T>() where T : struct
        {
            var info = _world.GetComponentInfo<T>();
            if (ComponentTypeTraits<T>.UsesSparseMask)
                _sparseAll = _sparseAll.With(info.Id);
            else
                _denseAll = _denseAll.With(info.Id);
            return this;
        }

        public ChangeQuery Without<T>() where T : struct
        {
            var info = _world.GetComponentInfo<T>();
            if (ComponentTypeTraits<T>.UsesSparseMask)
                _sparseNone = _sparseNone.With(info.Id);
            else
                _denseNone = _denseNone.With(info.Id);
            return this;
        }

        public ChangeQuery WithAny<T1>() where T1 : struct => AddAny<T1>();

        public ChangeQuery WithAny<T1, T2>() where T1 : struct where T2 : struct
        {
            AddAny<T1>();
            return AddAny<T2>();
        }

        public ChangeQuery WithAny<T1, T2, T3>()
            where T1 : struct where T2 : struct where T3 : struct
        {
            AddAny<T1>();
            AddAny<T2>();
            return AddAny<T3>();
        }

        public ChangeQuery WithAny<T1, T2, T3, T4>()
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            AddAny<T1>();
            AddAny<T2>();
            AddAny<T3>();
            return AddAny<T4>();
        }

        public ChangeQuery Enabled() => Without<Disabled>();

        private ChangeQuery AddAny<T>() where T : struct
        {
            var info = _world.GetComponentInfo<T>();
            if (ComponentTypeTraits<T>.UsesSparseMask)
                _sparseAny = _sparseAny.With(info.Id);
            else
                _denseAny = _denseAny.With(info.Id);
            return this;
        }

        public void ForEach(Action<Entity> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (Passes(entity))
                    action(entity);
            }
        }

        public List<Entity> ToList()
        {
            var result = new List<Entity>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (Passes(entity))
                    result.Add(entity);
            }
            return result;
        }

        public void Each<T1>(RefAction<T1> action) where T1 : struct
        {
            RequireNotEmpty<T1>();
            With<T1>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (!Passes(entity))
                    continue;
                action(entity, ref _world.Get<T1>(entity));
            }
        }

        public void Each<T1, T2>(RefAction<T1, T2> action)
            where T1 : struct where T2 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            With<T1>();
            With<T2>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (!Passes(entity))
                    continue;
                action(entity, ref _world.Get<T1>(entity), ref _world.Get<T2>(entity));
            }
        }

        public void Each<T1, T2, T3>(RefAction<T1, T2, T3> action)
            where T1 : struct where T2 : struct where T3 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            With<T1>();
            With<T2>();
            With<T3>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (!Passes(entity))
                    continue;
                action(entity,
                    ref _world.Get<T1>(entity),
                    ref _world.Get<T2>(entity),
                    ref _world.Get<T3>(entity));
            }
        }

        public void Each<T1, T2, T3, T4>(RefAction<T1, T2, T3, T4> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (!Passes(entity))
                    continue;
                action(entity,
                    ref _world.Get<T1>(entity),
                    ref _world.Get<T2>(entity),
                    ref _world.Get<T3>(entity),
                    ref _world.Get<T4>(entity));
            }
        }

        public void Each<T1, T2, T3, T4, T5>(RefAction<T1, T2, T3, T4, T5> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            RequireNotEmpty<T5>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            With<T5>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (!Passes(entity))
                    continue;
                action(entity,
                    ref _world.Get<T1>(entity),
                    ref _world.Get<T2>(entity),
                    ref _world.Get<T3>(entity),
                    ref _world.Get<T4>(entity),
                    ref _world.Get<T5>(entity));
            }
        }

        public void Each<T1, T2, T3, T4, T5, T6>(RefAction<T1, T2, T3, T4, T5, T6> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
            where T5 : struct where T6 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            RequireNotEmpty<T5>();
            RequireNotEmpty<T6>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            With<T5>();
            With<T6>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (!Passes(entity))
                    continue;
                action(entity,
                    ref _world.Get<T1>(entity),
                    ref _world.Get<T2>(entity),
                    ref _world.Get<T3>(entity),
                    ref _world.Get<T4>(entity),
                    ref _world.Get<T5>(entity),
                    ref _world.Get<T6>(entity));
            }
        }

        public void Each<T1, T2, T3, T4, T5, T6, T7>(RefAction<T1, T2, T3, T4, T5, T6, T7> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
            where T5 : struct where T6 : struct where T7 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            RequireNotEmpty<T5>();
            RequireNotEmpty<T6>();
            RequireNotEmpty<T7>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            With<T5>();
            With<T6>();
            With<T7>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (!Passes(entity))
                    continue;
                action(entity,
                    ref _world.Get<T1>(entity),
                    ref _world.Get<T2>(entity),
                    ref _world.Get<T3>(entity),
                    ref _world.Get<T4>(entity),
                    ref _world.Get<T5>(entity),
                    ref _world.Get<T6>(entity),
                    ref _world.Get<T7>(entity));
            }
        }

        public void Each<T1, T2, T3, T4, T5, T6, T7, T8>(RefAction<T1, T2, T3, T4, T5, T6, T7, T8> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
            where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            RequireNotEmpty<T1>();
            RequireNotEmpty<T2>();
            RequireNotEmpty<T3>();
            RequireNotEmpty<T4>();
            RequireNotEmpty<T5>();
            RequireNotEmpty<T6>();
            RequireNotEmpty<T7>();
            RequireNotEmpty<T8>();
            With<T1>();
            With<T2>();
            With<T3>();
            With<T4>();
            With<T5>();
            With<T6>();
            With<T7>();
            With<T8>();
            var list = _entities;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                if (!Passes(entity))
                    continue;
                action(entity,
                    ref _world.Get<T1>(entity),
                    ref _world.Get<T2>(entity),
                    ref _world.Get<T3>(entity),
                    ref _world.Get<T4>(entity),
                    ref _world.Get<T5>(entity),
                    ref _world.Get<T6>(entity),
                    ref _world.Get<T7>(entity),
                    ref _world.Get<T8>(entity));
            }
        }

        private bool Passes(Entity entity)
        {
            if (!_world.IsAlive(entity))
            {
                // Destroyed while on the Removed list: only match bare Removed (no With/WithAny).
                return _kind == Kind.Removed
                       && _denseAll == ComponentMask.Empty
                       && _sparseAll == ComponentMask.Empty
                       && _denseAny == ComponentMask.Empty
                       && _sparseAny == ComponentMask.Empty;
            }

            var archetype = _world.GetArchetype(entity);
            if (archetype == null)
                return false;

            var mask = archetype.Mask;
            if (_denseAll != ComponentMask.Empty && !mask.ContainsAll(_denseAll))
                return false;
            if (_denseNone != ComponentMask.Empty && mask.IntersectsAny(_denseNone))
                return false;
            if (_denseAny != ComponentMask.Empty && !mask.IntersectsAny(_denseAny))
                return false;

            return PassesSparseFilters(entity.Id);
        }

        private bool PassesSparseFilters(int entityId)
        {
            var mask = _world.GetSparseMask(entityId);
            if (!mask.ContainsAll(_sparseAll) || mask.IntersectsAny(_sparseNone))
                return false;
            if (_sparseAny != ComponentMask.Empty && !mask.IntersectsAny(_sparseAny))
                return false;
            return true;
        }

        private static void RequireNotEmpty<T>() where T : struct
        {
            if (ComponentTypeTraits<T>.IsEmpty)
                throw new InvalidOperationException(
                    $"Each<T>() requires a component with data; {typeof(T).Name} has no fields, so there's no " +
                    "per-entity value to hand back a ref to. Use With<T>()/Without<T>() to filter by presence instead.");
        }
    }
}
