using Loom;
using Loom.Commands;
using Loom.Entities;
using Loom.Systems;
using UnityEngine;

namespace Loom.Unity.Samples.FlappyBird
{
    static class GameFactory
    {
        public static void Configure(Runtime runtime, SystemGroup systems)
        {
            var world = runtime.World;
            world.SetSingleton(new FrameTime());
            world.SetSingleton(new InputState());
            world.SetSingleton(new GameSession
            {
                Phase = GamePhase.Ready,
                Score = 0,
                Best = 0,
                SpawnTimer = 0.6f,
            });

            systems
                .Add(new BirdControlSystem())
                .Add(new BirdPhysicsSystem())
                .Add(new PipeSpawnSystem())
                .Add(new PipeMoveSystem())
                .Add(new CollisionSystem())
                .Add(new ScoreSystem());

            runtime.Subscribe((Runtime _, in BirdDied e) =>
                Debug.Log($"FlappyBird: died — score {e.FinalScore}"));
            runtime.Subscribe((Runtime _, in Scored e) =>
                Debug.Log($"FlappyBird: score {e.Score}"));

            SpawnBird(world);
        }

        public static void ResetRound(World world, CommandBuffer commands)
        {
            world.Query().With<Pipe>().ForEach(entity => commands.Destroy(entity));

            world.Query().With<Bird>().Each<Position, Velocity>((Entity _, ref Position pos, ref Velocity vel) =>
            {
                pos.X = GameConfig.BirdStartX;
                pos.Y = GameConfig.BirdStartY;
                vel.X = 0f;
                vel.Y = 0f;
            });

            ref var session = ref world.GetSingleton<GameSession>();
            session.Phase = GamePhase.Ready;
            session.Score = 0;
            session.SpawnTimer = 0.6f;
        }

        private static void SpawnBird(World world)
        {
            world.Create(
                new Position { X = GameConfig.BirdStartX, Y = GameConfig.BirdStartY },
                new Velocity { X = 0f, Y = 0f },
                new Bird { Radius = GameConfig.BirdRadius });
        }
    }
}
