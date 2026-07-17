using System.Diagnostics;
using Loom;

namespace Loom.SpeedDemo;

static class DemoBootstrap
{
    public const int DefaultEntities = 50_000;
    public const int BatchSize = 10_000;
    public const float DefaultWidth = 1280f;
    public const float DefaultHeight = 720f;

    public static (Runtime Runtime, SystemGroup Systems) CreateRuntime(int entityCount, float width, float height)
    {
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup("systems");
        world.SetSingleton(new FrameTime { Delta = 1f / 60f });
        world.SetSingleton(new DemoConfig
        {
            Width = width,
            Height = height,
            UseParallel = false,
            SparseChurn = false,
            Paused = false,
            Draw = true,
            DrawBudget = 100_000,
        });
        world.SetSingleton(new FrameMetrics());

        systems
            .Add(new MotionSystem())
            .Add(new SparseChurnSystem());

        Spawn(world, entityCount);
        return (runtime, systems);
    }

    public static void Spawn(World world, int count)
    {
        if (count <= 0)
            return;

        ref var cfg = ref world.GetSingleton<DemoConfig>();
        var batch = new Entity[count];
        var rng = new Random(42 + world.EntityCount);

        // CreateMany places into empty archetype; then we set Position/Velocity via Add in a second
        // pass would be two moves — better: CreateMany with both components.
        var positions = new Position[count];
        var velocities = new Velocity[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = new Position
            {
                X = (float)rng.NextDouble() * cfg.Width,
                Y = (float)rng.NextDouble() * cfg.Height,
            };
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float speed = 40f + (float)rng.NextDouble() * 120f;
            velocities[i] = new Velocity
            {
                X = MathF.Cos(angle) * speed,
                Y = MathF.Sin(angle) * speed,
            };
        }

        // CreateMany requires the same value for all — spawn individually with Create(pos, vel)
        // in chunks for variety, or CreateMany with default then overwrite. Overwrite via Get is fine.
        world.CreateMany(batch, new Position(), new Velocity());
        for (int i = 0; i < count; i++)
        {
            world.Get<Position>(batch[i]) = positions[i];
            world.Get<Velocity>(batch[i]) = velocities[i];
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
        var sw = Stopwatch.StartNew();
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
            $"Loom Speed Demo\n" +
            $"entities  {m.EntityCount:N0}\n" +
            $"tick      {m.TickMilliseconds:F2} ms\n" +
            $"sim FPS   {m.Fps:F0}\n" +
            $"throughput {m.EntitiesPerSecond / 1_000_000:F2} M entity·updates/s\n" +
            $"parallel  {(cfg.UseParallel ? "ON" : "off")}   sparse churn {(cfg.SparseChurn ? "ON" : "off")}\n" +
            $"draw      {(cfg.Draw ? $"instanced (≤{cfg.DrawBudget:N0})" : "off")}" +
            (cfg.Paused ? "   PAUSED" : "");
    }
}
