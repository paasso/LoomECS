using System;
using Loom;
using UnityEngine;

namespace Loom.Unity.Samples.SpeedDemo
{
    [OrderFirst]
    sealed class MotionSystem : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var cfg = ref world.GetSingleton<DemoConfig>();
            if (cfg.Paused)
                return;

            float dt = world.GetSingleton<FrameTime>().Delta;
            float w = cfg.Width;
            float h = cfg.Height;

            if (cfg.UseParallel)
            {
                world.Query().ParallelEach<Position, Velocity>((Entity _, ref Position pos, ref Velocity vel) =>
                    Integrate(ref pos, ref vel, dt, w, h));
            }
            else
            {
                world.Query().Each<Position, Velocity>((Entity _, ref Position pos, ref Velocity vel) =>
                    Integrate(ref pos, ref vel, dt, w, h));
            }
        }

        private static void Integrate(ref Position pos, ref Velocity vel, float dt, float w, float h)
        {
            pos.X += vel.X * dt;
            pos.Y += vel.Y * dt;

            if (pos.X < 0f) { pos.X = 0f; vel.X = Math.Abs(vel.X); }
            else if (pos.X > w) { pos.X = w; vel.X = -Math.Abs(vel.X); }

            if (pos.Y < 0f) { pos.Y = 0f; vel.Y = Math.Abs(vel.Y); }
            else if (pos.Y > h) { pos.Y = h; vel.Y = -Math.Abs(vel.Y); }
        }
    }

    [UpdateAfter(typeof(MotionSystem))]
    sealed class SparseChurnSystem : ISystem
    {
        private int _cursor;

        public void Update(Runtime runtime, CommandBuffer commands)
        {
            var world = runtime.World;
            ref var cfg = ref world.GetSingleton<DemoConfig>();
            if (cfg.Paused || !cfg.SparseChurn)
                return;

            int count = world.EntityCount;
            if (count == 0)
                return;

            int visits = Math.Max(1, count / 200);
            for (int i = 0; i < visits; i++)
            {
                _cursor++;
                if (_cursor >= world.EntityCount + 8)
                    _cursor = 1;

                if (!world.TryGetAliveEntity(_cursor, out var entity))
                    continue;

                if (world.Has<Pulse>(entity))
                {
                    ref var pulse = ref world.Get<Pulse>(entity);
                    pulse.Age += world.GetSingleton<FrameTime>().Delta;
                    if (pulse.Age > 0.35f)
                        world.Remove<Pulse>(entity);
                }
                else if ((_cursor & 3) == 0)
                {
                    world.Add(entity, new Pulse { Age = 0f });
                }
            }
        }
    }

    static class DemoBootstrap
    {
        public const int DefaultEntities = 20_000;
        public const int BatchSize = 5_000;

        public static void Configure(Runtime runtime, SystemGroup systems, float width, float height, int entityCount)
        {
            var world = runtime.World;
            world.SetSingleton(new FrameTime { Delta = 1f / 60f });
            world.SetSingleton(new DemoConfig
            {
                Width = width,
                Height = height,
                UseParallel = false,
                SparseChurn = false,
                Paused = false,
                Draw = true,
                DrawBudget = 50_000,
            });
            world.SetSingleton(new FrameMetrics());
            systems.Add(new MotionSystem()).Add(new SparseChurnSystem());
            Spawn(world, entityCount);
        }

        public static void Spawn(World world, int count)
        {
            if (count <= 0)
                return;

            ref var cfg = ref world.GetSingleton<DemoConfig>();
            var batch = new Entity[count];
            var rng = new System.Random(42 + world.EntityCount);
            world.CreateMany(batch, new Position(), new Velocity());
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2);
                float speed = 40f + (float)rng.NextDouble() * 120f;
                world.Get<Position>(batch[i]) = new Position
                {
                    X = (float)rng.NextDouble() * cfg.Width,
                    Y = (float)rng.NextDouble() * cfg.Height,
                };
                world.Get<Velocity>(batch[i]) = new Velocity
                {
                    X = Mathf.Cos(angle) * speed,
                    Y = Mathf.Sin(angle) * speed,
                };
            }
        }

        public static void Despawn(World world, int count)
        {
            if (count <= 0 || world.EntityCount == 0)
                return;

            int removed = 0;
            var buffer = world.CreateCommandBuffer();
            world.Query().ForEach(entity =>
            {
                if (removed >= count)
                    return;
                buffer.Destroy(entity);
                removed++;
            });
            buffer.Playback();
        }

        public static void TickMeasured(Runtime runtime, SystemGroup systems)
        {
            var world = runtime.World;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            runtime.Run(systems);
            runtime.EndFrame();
            sw.Stop();

            ref var metrics = ref world.GetSingleton<FrameMetrics>();
            double ms = sw.Elapsed.TotalMilliseconds;
            metrics.TickMilliseconds = ms;
            metrics.EntityCount = world.EntityCount;
            metrics.Frame++;
            metrics.Fps = ms > 0.0001 ? 1000.0 / ms : 0;
            metrics.EntitiesPerSecond = ms > 0.0001 ? world.EntityCount * (1000.0 / ms) : 0;
        }

        public static string FormatHud(World world)
        {
            ref var m = ref world.GetSingleton<FrameMetrics>();
            ref var cfg = ref world.GetSingleton<DemoConfig>();
            return
                $"Loom Speed Demo (Unity)\n" +
                $"entities  {m.EntityCount:N0}\n" +
                $"tick      {m.TickMilliseconds:F2} ms\n" +
                $"sim FPS   {m.Fps:F0}\n" +
                $"throughput {m.EntitiesPerSecond / 1_000_000:F2} M/s\n" +
                $"parallel  {(cfg.UseParallel ? "ON" : "off")}  sparse {(cfg.SparseChurn ? "ON" : "off")}\n" +
                $"draw      {(cfg.Draw ? $"indirect ≤{cfg.DrawBudget:N0}" : "off")}" +
                (cfg.Paused ? "  PAUSED" : "");
        }
    }
}
