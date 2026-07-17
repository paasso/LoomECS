using System;
using Loom;

namespace Loom.Unity.Samples.FlappyBird
{
    [OrderFirst]
    sealed class BirdControlSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            ref var input = ref world.GetSingleton<InputState>();

            if (session.Phase == GamePhase.Dead)
            {
                if (input.Restart)
                    GameFactory.ResetRound(world, commands);
                return;
            }

            if (!input.Flap)
                return;

            if (session.Phase == GamePhase.Ready)
                session.Phase = GamePhase.Playing;

            world.Query().With<Bird>().Each<Velocity>((Entity _, ref Velocity vel) =>
            {
                vel.Y = GameConfig.FlapVelocity;
            });
        }
    }

    [UpdateAfter(typeof(BirdControlSystem))]
    sealed class BirdPhysicsSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            if (session.Phase != GamePhase.Playing)
                return;

            float dt = world.GetSingleton<FrameTime>().Delta;

            world.Query().With<Bird>().Each<Position, Velocity>((Entity _, ref Position pos, ref Velocity vel) =>
            {
                vel.Y += GameConfig.Gravity * dt;
                if (vel.Y > GameConfig.MaxFallSpeed)
                    vel.Y = GameConfig.MaxFallSpeed;

                pos.Y += vel.Y * dt;
                pos.X = GameConfig.BirdStartX;
            });
        }
    }

    sealed class PipeSpawnSystem : ISystem
    {
        private readonly Random _rng = new();

        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            if (session.Phase != GamePhase.Playing)
                return;

            float dt = world.GetSingleton<FrameTime>().Delta;
            session.SpawnTimer -= dt;
            if (session.SpawnTimer > 0f)
                return;

            session.SpawnTimer = GameConfig.PipeSpawnInterval;
            float gapCenter = GameConfig.PipeMinGapCenter
                + (float)_rng.NextDouble() * (GameConfig.PipeMaxGapCenter - GameConfig.PipeMinGapCenter);

            var pipe = commands.Create(
                new Position { X = GameConfig.PipeSpawnX, Y = 0f },
                new Velocity { X = -GameConfig.PipeSpeed, Y = 0f });
            commands.Add(pipe, new Pipe
            {
                GapCenterY = gapCenter,
                GapSize = GameConfig.PipeGap,
                Width = GameConfig.PipeWidth,
                Scored = false,
            });
        }
    }

    sealed class PipeMoveSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            if (session.Phase != GamePhase.Playing)
                return;

            float dt = world.GetSingleton<FrameTime>().Delta;

            world.Query().With<Pipe>().Each<Position, Velocity>((Entity entity, ref Position pos, ref Velocity vel) =>
            {
                pos.X += vel.X * dt;
                if (pos.X + GameConfig.PipeWidth < -10f)
                    commands.Destroy(entity);
            });
        }
    }

    sealed class CollisionSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            if (session.Phase != GamePhase.Playing)
                return;

            Position birdPos = default;
            float radius = 0f;
            bool found = false;

            world.Query().Each<Bird, Position>((Entity _, ref Bird bird, ref Position pos) =>
            {
                birdPos = pos;
                radius = bird.Radius;
                found = true;
            });

            if (!found)
                return;

            if (birdPos.Y - radius < 0f || birdPos.Y + radius > GameConfig.GroundY)
            {
                Kill(simulation, ref session);
                return;
            }

            bool hit = false;
            world.Query().With<Pipe>().Each<Position, Pipe>((Entity _, ref Position pipePos, ref Pipe pipe) =>
            {
                if (hit)
                    return;

                float left = pipePos.X;
                float right = pipePos.X + pipe.Width;
                float gapTop = pipe.GapCenterY - pipe.GapSize * 0.5f;
                float gapBottom = pipe.GapCenterY + pipe.GapSize * 0.5f;

                if (birdPos.X + radius < left || birdPos.X - radius > right)
                    return;

                if (birdPos.Y - radius < gapTop || birdPos.Y + radius > gapBottom)
                    hit = true;
            });

            if (hit)
                Kill(simulation, ref session);
        }

        private static void Kill(Runtime runtime, ref GameSession session)
        {
            session.Phase = GamePhase.Dead;
            if (session.Score > session.Best)
                session.Best = session.Score;
            runtime.Emit(new BirdDied { FinalScore = session.Score });
        }
    }

    sealed class ScoreSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            if (session.Phase != GamePhase.Playing)
                return;

            float birdX = GameConfig.BirdStartX;
            world.Query().With<Bird>().Each<Position>((Entity _, ref Position pos) => birdX = pos.X);

            int score = session.Score;
            world.Query().With<Pipe>().Each<Position, Pipe>((Entity _, ref Position pipePos, ref Pipe pipe) =>
            {
                if (pipe.Scored)
                    return;

                float pipeCenter = pipePos.X + pipe.Width * 0.5f;
                if (birdX > pipeCenter)
                {
                    pipe.Scored = true;
                    score++;
                    runtime.Emit(new Scored { Score = score });
                }
            });
            session.Score = score;
        }
    }
}
