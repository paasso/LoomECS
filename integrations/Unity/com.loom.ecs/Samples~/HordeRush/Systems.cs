using System;
using Loom;
using Loom.Commands;
using Loom.Entities;
using Loom.Systems;
using UnityEngine;

namespace Loom.Unity.Samples.HordeRush
{
    [OrderFirst]
    sealed class PlayerControlSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            ref var input = ref world.GetSingleton<InputState>();

            if (session.Phase == GamePhase.Dead)
            {
                if (input.Restart)
                    GameFactory.ResetRun(world, commands);
                return;
            }

            float dt = world.GetSingleton<FrameTime>().Delta;
            float speed = GameConfig.PlayerSpeed;
            float moveX = input.MoveX;
            float moveY = input.MoveY;
            float aimX = input.AimX;
            float aimY = input.AimY;
            bool fire = input.Fire;

            world.Query().With<Player>().Each<Position, Velocity, Player>(
                (Entity _, ref Position pos, ref Velocity vel, ref Player player) =>
                {
                    float mx = moveX;
                    float my = moveY;
                    float mag = Mathf.Sqrt(mx * mx + my * my);
                    if (mag > 1e-4f)
                    {
                        mx /= mag;
                        my /= mag;
                    }

                    vel.X = mx * speed;
                    vel.Y = my * speed;

                    float aimLen = Mathf.Sqrt(aimX * aimX + aimY * aimY);
                    if (aimLen > 0.2f)
                    {
                        player.AimX = aimX / aimLen;
                        player.AimY = aimY / aimLen;
                    }

                    player.FireCooldown -= dt;
                    if (!fire || player.FireCooldown > 0f)
                        return;

                    player.FireCooldown = GameConfig.FireInterval;
                    float ax = player.AimX;
                    float ay = player.AimY;
                    float spawnX = pos.X + ax * (GameConfig.PlayerRadius + 6f);
                    float spawnY = pos.Y + ay * (GameConfig.PlayerRadius + 6f);

                    var bullet = commands.Create(
                        new Position { X = spawnX, Y = spawnY },
                        new Velocity
                        {
                            X = ax * GameConfig.BulletSpeed,
                            Y = ay * GameConfig.BulletSpeed,
                        });
                    commands.Add(bullet, new Bullet
                    {
                        Radius = GameConfig.BulletRadius,
                        Damage = GameConfig.BulletDamage,
                    });
                    commands.Add(bullet, new Lifetime { Remaining = GameConfig.BulletLife });
                });
        }
    }

    [UpdateAfter(typeof(PlayerControlSystem))]
    sealed class EnemySpawnSystem : ISystem
    {
        private readonly System.Random _rng = new(7);

        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            if (session.Phase != GamePhase.Playing)
                return;

            float dt = world.GetSingleton<FrameTime>().Delta;
            session.WaveTimer -= dt;
            if (session.WaveTimer > 0f)
                return;

            session.Wave++;
            session.WaveTimer = GameConfig.WaveInterval;

            int count = GameFactory.EnemyCountForWave(session.Wave);
            ref var arena = ref world.GetSingleton<ArenaConfig>();
            var kind = GameFactory.KindForWave(session.Wave);
            float radius = GameFactory.EnemyRadiusForWave(session.Wave);
            int hp = GameFactory.EnemyHpForWave(session.Wave);

            var batch = new Entity[count];
            world.CreateMany(batch, new Position(), new Velocity());
            for (int i = 0; i < count; i++)
            {
                PlaceOnEdge(arena.Width, arena.Height, out float x, out float y);
                ref var pos = ref world.Get<Position>(batch[i]);
                pos.X = x;
                pos.Y = y;
                world.Add(batch[i], new Enemy { Radius = radius });
                world.Add(batch[i], new Health { Current = hp, Max = hp });
                world.Add(batch[i], kind);
            }

            session.AliveEnemies += count;
        }

        private void PlaceOnEdge(float w, float h, out float x, out float y)
        {
            float pad = GameConfig.EnemySpawnPadding;
            int side = _rng.Next(4);
            float t = (float)_rng.NextDouble();
            switch (side)
            {
                case 0: x = t * w; y = -pad; break;
                case 1: x = t * w; y = h + pad; break;
                case 2: x = -pad; y = t * h; break;
                default: x = w + pad; y = t * h; break;
            }
        }
    }

    /// <summary>Steering only — no structural changes — safe for ParallelEach.</summary>
    [UpdateAfter(typeof(EnemySpawnSystem))]
    sealed class EnemyChaseSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            if (session.Phase != GamePhase.Playing)
                return;

            float px = 0f, py = 0f;
            bool found = false;
            world.Query().With<Player>().Each<Position>((Entity _, ref Position pos) =>
            {
                px = pos.X;
                py = pos.Y;
                found = true;
            });
            if (!found)
                return;

            if (session.UseParallel)
            {
                world.Query().With<Enemy>().ParallelEach<Position, Velocity, EnemyKind>(
                    (Entity _, ref Position pos, ref Velocity vel, ref EnemyKind kind) =>
                        Steer(ref pos, ref vel, ref kind, px, py));
            }
            else
            {
                world.Query().With<Enemy>().Each<Position, Velocity, EnemyKind>(
                    (Entity _, ref Position pos, ref Velocity vel, ref EnemyKind kind) =>
                        Steer(ref pos, ref vel, ref kind, px, py));
            }
        }

        private static void Steer(
            ref Position pos, ref Velocity vel, ref EnemyKind kind, float px, float py)
        {
            float dx = px - pos.X;
            float dy = py - pos.Y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f)
                return;
            float inv = 1f / len;
            vel.X = dx * inv * kind.Speed;
            vel.Y = dy * inv * kind.Speed;
        }
    }

    [UpdateAfter(typeof(EnemyChaseSystem))]
    sealed class MotionSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var session = ref world.GetSingleton<GameSession>();
            if (session.Phase != GamePhase.Playing)
                return;

            float dt = world.GetSingleton<FrameTime>().Delta;
            ref var arena = ref world.GetSingleton<ArenaConfig>();
            float w = arena.Width;
            float h = arena.Height;

            if (session.UseParallel)
            {
                world.Query().ParallelEach<Position, Velocity>(
                    (Entity _, ref Position pos, ref Velocity vel) =>
                    {
                        pos.X += vel.X * dt;
                        pos.Y += vel.Y * dt;
                    });
            }
            else
            {
                world.Query().Each<Position, Velocity>(
                    (Entity _, ref Position pos, ref Velocity vel) =>
                    {
                        pos.X += vel.X * dt;
                        pos.Y += vel.Y * dt;
                    });
            }

            // Only the player is locked to the arena; bullets expire via Lifetime, enemies stream in.
            world.Query().With<Player>().Each<Position>((Entity _, ref Position pos) =>
            {
                if (pos.X < 0f) pos.X = 0f;
                else if (pos.X > w) pos.X = w;
                if (pos.Y < 0f) pos.Y = 0f;
                else if (pos.Y > h) pos.Y = h;
            });
        }
    }

    /// <summary>
    /// Push overlapping enemies apart so the horde does not stack into one blob.
    /// Sequential O(n²) on a position snapshot — fine up to <see cref="GameConfig.EnemiesWaveCap"/>.
    /// </summary>
    [UpdateAfter(typeof(MotionSystem))]
    sealed class EnemySeparationSystem : ISystem
    {
        private readonly Entity[] _entities = new Entity[512];
        private readonly float[] _x = new float[512];
        private readonly float[] _y = new float[512];
        private readonly float[] _r = new float[512];

        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            if (world.GetSingleton<GameSession>().Phase != GamePhase.Playing)
                return;

            int n = 0;
            world.Query().With<Enemy>().Each<Position, Enemy>((Entity e, ref Position pos, ref Enemy enemy) =>
            {
                if (n >= _entities.Length)
                    return;
                _entities[n] = e;
                _x[n] = pos.X;
                _y[n] = pos.Y;
                _r[n] = enemy.Radius;
                n++;
            });

            if (n < 2)
                return;

            float pad = GameConfig.EnemySeparationPadding;
            int iterations = GameConfig.EnemySeparationIterations;
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        float dx = _x[j] - _x[i];
                        float dy = _y[j] - _y[i];
                        float minDist = _r[i] + _r[j] + pad;
                        float distSq = dx * dx + dy * dy;
                        float minDistSq = minDist * minDist;
                        if (distSq >= minDistSq)
                            continue;

                        float dist = Mathf.Sqrt(distSq);
                        float nx;
                        float ny;
                        if (dist > 1e-4f)
                        {
                            float inv = 1f / dist;
                            nx = dx * inv;
                            ny = dy * inv;
                        }
                        else
                        {
                            // Identical positions — deterministic push so they don't stick forever.
                            float angle = (i * 12.9898f + j * 78.233f) * 0.017453f;
                            nx = Mathf.Cos(angle);
                            ny = Mathf.Sin(angle);
                            dist = 0f;
                        }

                        float push = (minDist - dist) * 0.5f;
                        _x[i] -= nx * push;
                        _y[i] -= ny * push;
                        _x[j] += nx * push;
                        _y[j] += ny * push;
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (!world.IsAlive(_entities[i]))
                    continue;
                ref var pos = ref world.Get<Position>(_entities[i]);
                pos.X = _x[i];
                pos.Y = _y[i];
            }
        }
    }

    [UpdateAfter(typeof(EnemySeparationSystem))]
    sealed class LifetimeSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            float dt = world.GetSingleton<FrameTime>().Delta;
            world.Query().With<Lifetime>().Each<Lifetime>((Entity entity, ref Lifetime life) =>
            {
                life.Remaining -= dt;
                if (life.Remaining <= 0f)
                    commands.Destroy(entity);
            });
        }
    }

    [UpdateAfter(typeof(LifetimeSystem))]
    sealed class CombatSystem : ISystem
    {
        private readonly Entity[] _enemyScratch = new Entity[512];
        private readonly float[] _ex = new float[512];
        private readonly float[] _ey = new float[512];
        private readonly float[] _er = new float[512];

        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            if (world.GetSingleton<GameSession>().Phase != GamePhase.Playing)
                return;

            int wave = world.GetSingleton<GameSession>().Wave;
            int n = 0;
            world.Query().With<Enemy>().Each<Position, Enemy>((Entity e, ref Position pos, ref Enemy enemy) =>
            {
                if (n >= _enemyScratch.Length)
                    return;
                _enemyScratch[n] = e;
                _ex[n] = pos.X;
                _ey[n] = pos.Y;
                _er[n] = enemy.Radius;
                n++;
            });

            // Bullets vs enemies.
            world.Query().With<Bullet>().Each<Position, Bullet>(
                (Entity bullet, ref Position bPos, ref Bullet b) =>
                {
                    for (int i = 0; i < n; i++)
                    {
                        float dx = bPos.X - _ex[i];
                        float dy = bPos.Y - _ey[i];
                        float r = b.Radius + _er[i];
                        if (dx * dx + dy * dy > r * r)
                            continue;

                        var enemy = _enemyScratch[i];
                        if (!world.IsAlive(enemy) || !world.Has<Health>(enemy))
                            continue;

                        ref var hp = ref world.Get<Health>(enemy);
                        hp.Current -= b.Damage;
                        commands.Destroy(bullet);

                        if (hp.Current <= 0)
                        {
                            int score = world.Has<EnemyKind>(enemy)
                                ? world.Get<EnemyKind>(enemy).ScoreValue
                                : 10;
                            commands.Destroy(enemy);
                            ref var session = ref world.GetSingleton<GameSession>();
                            session.AliveEnemies = Math.Max(0, session.AliveEnemies - 1);
                            runtime.Emit(new EnemyKilled { ScoreValue = score, Wave = wave });
                        }
                        else if (!world.Has<HitFlash>(enemy))
                        {
                            commands.Add(enemy, new HitFlash { Age = 0f });
                        }

                        // Mark slot dead for further bullets this frame.
                        _er[i] = -1f;
                        break;
                    }
                });

            // Enemies vs player.
            world.Query().With<Player>().Each<Position, Health>(
                (Entity player, ref Position pPos, ref Health pHp) =>
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (_er[i] < 0f)
                            continue;
                        float dx = pPos.X - _ex[i];
                        float dy = pPos.Y - _ey[i];
                        float r = GameConfig.PlayerRadius + _er[i];
                        if (dx * dx + dy * dy > r * r)
                            continue;

                        pHp.Current -= 1;
                        var enemy = _enemyScratch[i];
                        ref var session = ref world.GetSingleton<GameSession>();
                        if (world.IsAlive(enemy))
                        {
                            commands.Destroy(enemy);
                            session.AliveEnemies = Math.Max(0, session.AliveEnemies - 1);
                        }

                        if (!world.Has<HitFlash>(player))
                            commands.Add(player, new HitFlash { Age = 0f });

                        if (pHp.Current <= 0)
                        {
                            session.Phase = GamePhase.Dead;
                            runtime.Emit(new PlayerDied { FinalScore = session.Score });
                        }

                        break;
                    }
                });
        }
    }

    [UpdateAfter(typeof(CombatSystem))]
    sealed class HitFlashSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            float dt = world.GetSingleton<FrameTime>().Delta;
            world.Query().With<HitFlash>().Each<HitFlash>((Entity entity, ref HitFlash flash) =>
            {
                flash.Age += dt;
                if (flash.Age >= GameConfig.HitFlashDuration)
                    commands.Remove<HitFlash>(entity);
            });
        }
    }
}
