using System;
using Loom;
using UnityEngine;

namespace Loom.Unity.Samples.HordeRush
{
    static class GameFactory
    {
        public static void Configure(Runtime runtime, SystemGroup systems)
        {
            var world = runtime.World;
            world.SetSingleton(new FrameTime { Delta = 1f / 60f });
            world.SetSingleton(new InputState());
            world.SetSingleton(new ArenaConfig
            {
                Width = GameConfig.ArenaWidth,
                Height = GameConfig.ArenaHeight,
            });
            world.SetSingleton(new GameSession
            {
                Phase = GamePhase.Playing,
                Score = 0,
                Kills = 0,
                Wave = 0,
                WaveTimer = 0.6f,
                AliveEnemies = 0,
                UseParallel = true,
            });

            systems
                .Add(new PlayerControlSystem())
                .Add(new EnemySpawnSystem())
                .Add(new EnemyChaseSystem())
                .Add(new MotionSystem())
                .Add(new EnemySeparationSystem())
                .Add(new LifetimeSystem())
                .Add(new CombatSystem())
                .Add(new HitFlashSystem());

            runtime.Subscribe((Runtime s, in EnemyKilled e) =>
            {
                ref var session = ref s.World.GetSingleton<GameSession>();
                session.Score += e.ScoreValue;
                session.Kills++;
            });
            runtime.Subscribe((Runtime _, in PlayerDied e) =>
                Debug.Log($"HordeRush: down — score {e.FinalScore}"));

            SpawnPlayer(world);
        }

        public static void ResetRun(World world, CommandBuffer commands)
        {
            world.Query().WithAny<Enemy, Bullet>().ForEach(entity => commands.Destroy(entity));

            world.Query().With<Player>().Each<Position, Velocity, Health, Player>(
                (Entity _, ref Position pos, ref Velocity vel, ref Health hp, ref Player player) =>
                {
                    pos.X = GameConfig.ArenaWidth * 0.5f;
                    pos.Y = GameConfig.ArenaHeight * 0.5f;
                    vel.X = 0f;
                    vel.Y = 0f;
                    hp.Current = hp.Max;
                    player.FireCooldown = 0f;
                    player.AimX = 1f;
                    player.AimY = 0f;
                });

            world.Query().With<Player>().ForEach(entity =>
            {
                if (world.Has<HitFlash>(entity))
                    commands.Remove<HitFlash>(entity);
            });

            ref var session = ref world.GetSingleton<GameSession>();
            session.Phase = GamePhase.Playing;
            session.Score = 0;
            session.Kills = 0;
            session.Wave = 0;
            session.WaveTimer = 0.6f;
            session.AliveEnemies = 0;
        }

        public static void SpawnPlayer(World world)
        {
            world.Create(
                new Position
                {
                    X = GameConfig.ArenaWidth * 0.5f,
                    Y = GameConfig.ArenaHeight * 0.5f,
                },
                new Velocity(),
                new Player { AimX = 1f, AimY = 0f },
                new Health
                {
                    Current = GameConfig.PlayerMaxHealth,
                    Max = GameConfig.PlayerMaxHealth,
                });
        }

        public static EnemyKind KindForWave(int wave)
        {
            // One shared instance per wave: stats scale linearly so the horde gets harder smoothly.
            float t = Math.Max(0, wave - 1);
            float speed = Math.Min(
                GameConfig.EnemySpeedCap,
                GameConfig.EnemySpeedBase + t * GameConfig.EnemySpeedPerWave);
            int score = GameConfig.EnemyScoreBase + (int)(t * GameConfig.EnemyScorePerWave);

            // Tint shifts red → orange → magenta → yellow as difficulty climbs.
            float u = Math.Min(1f, t / 24f);
            float r = 0.85f + 0.1f * u;
            float g = 0.2f + 0.55f * u;
            float b = 0.15f + 0.35f * (1f - Math.Abs(u * 2f - 1f));

            return new EnemyKind
            {
                Speed = speed,
                R = r,
                G = g,
                B = b,
                ScoreValue = score,
            };
        }

        public static int EnemyCountForWave(int wave)
        {
            int t = Math.Max(0, wave - 1);
            return Math.Min(
                GameConfig.EnemiesWaveCap,
                GameConfig.EnemiesWaveBase + t * GameConfig.EnemiesPerWave);
        }

        public static float EnemyRadiusForWave(int wave)
        {
            float t = Math.Max(0, wave - 1);
            return Math.Min(
                GameConfig.EnemyRadiusCap,
                GameConfig.EnemyRadiusBase + t * GameConfig.EnemyRadiusPerWave);
        }

        public static int EnemyHpForWave(int wave)
        {
            float t = Math.Max(0, wave - 1);
            return Math.Max(1, 1 + (int)(t * GameConfig.EnemyHpPerWave));
        }
    }
}
