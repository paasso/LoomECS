using Loom.Entities;
using UnityEngine;

namespace Loom.Unity
{
    /// <summary>
    /// Links a GameObject to a Loom <see cref="Entity"/>. Assign
    /// <see cref="Bind"/> after creating the entity in your runner / factory.
    /// </summary>
    public class EntityBehaviour : MonoBehaviour
    {
        public LoomRunner? Runner;
        public Entity Entity { get; private set; }

        public bool IsBound => Runner != null && Runner.Runtime != null && Runner.World.IsAlive(Entity);

        public void Bind(LoomRunner runner, Entity entity)
        {
            Runner = runner;
            Entity = entity;
        }

        /// <summary>Copies <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/> onto
        /// <see cref="Transform.position"/> (2D-friendly: z defaults to current).</summary>
        public void ApplyPosition(float x, float y, float? z = null)
        {
            var p = transform.position;
            transform.position = new Vector3(x, y, z ?? p.z);
        }
    }
}
