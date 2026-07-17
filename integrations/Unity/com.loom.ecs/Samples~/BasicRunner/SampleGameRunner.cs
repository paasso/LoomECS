using Loom;
using UnityEngine;

namespace Loom.Unity.Samples
{
    /// <summary>
    /// Minimal playable sample: spawns entities with <see cref="UnityPosition"/> + velocity,
    /// moves them each tick, and syncs bound <see cref="EntityBehaviour"/> transforms.
    /// </summary>
    public sealed class SampleGameRunner : LoomRunner
    {
        [SerializeField] private int spawnCount = 8;
        [SerializeField] private float speed = 1.5f;
        [SerializeField] private GameObject? entityViewPrefab;

        private TransformSyncSystem _transformSync = null!;

        protected override void OnRuntimeCreated(Runtime runtime)
        {
            _transformSync = new TransformSyncSystem();
            Systems.Add(_transformSync);
            Systems.Add(new SampleMotionSystem { Speed = speed });

            for (int i = 0; i < spawnCount; i++)
            {
                float angle = (Mathf.PI * 2f * i) / Mathf.Max(1, spawnCount);
                var entity = World.Create(
                    new UnityPosition
                    {
                        X = Mathf.Cos(angle) * 2f,
                        Y = Mathf.Sin(angle) * 2f,
                        Z = 0f
                    },
                    new SampleVelocity
                    {
                        X = -Mathf.Sin(angle),
                        Y = Mathf.Cos(angle)
                    });

                if (entityViewPrefab == null)
                    continue;

                var go = Instantiate(entityViewPrefab);
                go.name = $"Entity_{entity.Id}";
                var behaviour = go.GetComponent<EntityBehaviour>() ?? go.AddComponent<EntityBehaviour>();
                behaviour.Bind(this, entity);
                _transformSync.Register(behaviour);
            }
        }
    }

    public struct SampleVelocity
    {
        public float X, Y;
    }

    /// <summary>Moves entities; register before <see cref="TransformSyncSystem"/> so views see fresh positions.</summary>
    public sealed class SampleMotionSystem : ISystem
    {
        public float Speed = 1.5f;

        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            float dt = world.HasSingleton<FrameDelta>()
                ? world.GetSingleton<FrameDelta>().Seconds
                : Time.deltaTime;

            float speed = Speed;
            world.Query().Each<UnityPosition, SampleVelocity>(
                (Entity _, ref UnityPosition pos, ref SampleVelocity vel) =>
                {
                    pos.X += vel.X * speed * dt;
                    pos.Y += vel.Y * speed * dt;
                });
        }
    }
}
