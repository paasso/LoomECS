using System.Collections.Generic;
using Loom.Commands;
using Loom.Entities;
using Loom.Systems;
using UnityEngine;

namespace Loom.Unity
{
    /// <summary>
    /// Example bridge system: reads a dense <see cref="UnityPosition"/> component and pushes it to
    /// registered <see cref="EntityBehaviour"/> transforms. Games typically replace this with their
    /// own component types; this shows the wiring pattern.
    /// </summary>
    public sealed class TransformSyncSystem : ISystem
    {
        private readonly Dictionary<int, EntityBehaviour> _behaviours = new Dictionary<int, EntityBehaviour>();

        public void Register(EntityBehaviour behaviour)
        {
            if (behaviour == null || !behaviour.IsBound)
                return;
            _behaviours[behaviour.Entity.Id] = behaviour;
        }

        public void Unregister(Entity entity) => _behaviours.Remove(entity.Id);

        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            world.Query().Each<UnityPosition>((Entity entity, ref UnityPosition pos) =>
            {
                if (_behaviours.TryGetValue(entity.Id, out var behaviour) && behaviour != null)
                    behaviour.ApplyPosition(pos.X, pos.Y, pos.Z);
            });
        }
    }

    /// <summary>Dense position used by the sample <see cref="TransformSyncSystem"/>.</summary>
    public struct UnityPosition
    {
        public float X, Y, Z;
    }
}
