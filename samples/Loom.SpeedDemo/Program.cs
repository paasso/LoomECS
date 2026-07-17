using System.Diagnostics;
using System.Globalization;
using Loom;
using Loom.SpeedDemo;
using Raylib_cs;

internal static class Program
{
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.ShowHelp)
        {
            Options.PrintHelp();
            return 0;
        }

        return options.Headless ? RunHeadless(options) : RunWindowed(options);
    }

    private static int RunHeadless(Options options)
    {
        Console.WriteLine($"Loom Speed Demo (headless) — {options.Entities:N0} entities, {options.Seconds:F1}s");
        Console.WriteLine($"parallel={options.Parallel}  sparseChurn={options.SparseChurn}");

        var (sim, systems) = DemoBootstrap.CreateRuntime(options.Entities, DemoBootstrap.DefaultWidth, DemoBootstrap.DefaultHeight);
        var world = sim.World;
        ref var cfg = ref world.GetSingleton<DemoConfig>();
        cfg.UseParallel = options.Parallel;
        cfg.SparseChurn = options.SparseChurn;
        cfg.Draw = false;

        // Warmup
        for (int i = 0; i < 30; i++)
            DemoBootstrap.TickMeasured(sim, systems);

        var sw = Stopwatch.StartNew();
        long frames = 0;
        double tickMsSum = 0;
        while (sw.Elapsed.TotalSeconds < options.Seconds)
        {
            world.GetSingleton<FrameTime>().Delta = 1f / 60f;
            DemoBootstrap.TickMeasured(sim, systems);
            tickMsSum += world.GetSingleton<FrameMetrics>().TickMilliseconds;
            frames++;
        }

        sw.Stop();
        ref var m = ref world.GetSingleton<FrameMetrics>();
        double avgMs = frames > 0 ? tickMsSum / frames : 0;
        double avgFps = avgMs > 0 ? 1000.0 / avgMs : 0;
        double throughput = avgMs > 0 ? m.EntityCount * (1000.0 / avgMs) : 0;

        Console.WriteLine();
        Console.WriteLine($"frames     {frames:N0}");
        Console.WriteLine($"entities   {m.EntityCount:N0}");
        Console.WriteLine($"avg tick   {avgMs:F3} ms");
        Console.WriteLine($"avg simFPS {avgFps:F1}");
        Console.WriteLine($"throughput {throughput / 1_000_000:F2} M entity·updates/s");
        Console.WriteLine($"wall       {sw.Elapsed.TotalSeconds:F2}s");
        return 0;
    }

    private static int RunWindowed(Options options)
    {
        const int width = 1280;
        const int height = 720;

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(width, height, "Loom — Speed Demo");
        Raylib.SetTargetFPS(0); // uncapped: HUD shows sim cost, not vsync-limited wall FPS

        var (sim, systems) = DemoBootstrap.CreateRuntime(options.Entities, width, height);
        var world = sim.World;
        ref var cfg = ref world.GetSingleton<DemoConfig>();
        cfg.UseParallel = options.Parallel;
        cfg.SparseChurn = options.SparseChurn;

        var particle = new Color(80, 220, 200, 180);
        var pulse = new Color(255, 180, 70, 220);
        var bg = new Color(12, 16, 22, 255);
        var panel = new Color(8, 10, 14, 200);

        using var particles = new ParticleInstancedRenderer();
        particles.Load(width, height);

        while (!Raylib.WindowShouldClose())
        {
            HandleInput(world);

            float dt = Raylib.GetFrameTime();
            if (dt > 0.05f)
                dt = 0.05f;
            world.GetSingleton<FrameTime>().Delta = dt;

            if (!world.GetSingleton<DemoConfig>().Paused || Raylib.IsKeyPressed(KeyboardKey.Period))
                DemoBootstrap.TickMeasured(sim, systems);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(bg);

            ref var drawCfg = ref world.GetSingleton<DemoConfig>();
            if (drawCfg.Draw)
                particles.Draw(world, particle, pulse);

            DrawHud(world, panel);
            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
        return 0;
    }

    private static void HandleInput(World world)
    {
        ref var cfg = ref world.GetSingleton<DemoConfig>();

        if (Raylib.IsKeyPressed(KeyboardKey.Space))
            cfg.Paused = !cfg.Paused;
        if (Raylib.IsKeyPressed(KeyboardKey.P))
            cfg.UseParallel = !cfg.UseParallel;
        if (Raylib.IsKeyPressed(KeyboardKey.C))
            cfg.SparseChurn = !cfg.SparseChurn;
        if (Raylib.IsKeyPressed(KeyboardKey.D))
            cfg.Draw = !cfg.Draw;

        if (Raylib.IsKeyPressed(KeyboardKey.Equal) || Raylib.IsKeyPressed(KeyboardKey.KpAdd))
            DemoBootstrap.Spawn(world, DemoBootstrap.BatchSize);
        if (Raylib.IsKeyPressed(KeyboardKey.Minus) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract))
            DemoBootstrap.Despawn(world, DemoBootstrap.BatchSize);

        if (Raylib.IsKeyPressed(KeyboardKey.R))
        {
            // Soft reset: despawn all via buffer, respawn default count.
            DemoBootstrap.Despawn(world, world.EntityCount);
            DemoBootstrap.Spawn(world, DemoBootstrap.DefaultEntities);
        }
    }

    private static void DrawHud(World world, Color panel)
    {
        Raylib.DrawRectangle(12, 12, 420, 168, panel);
        var lines = DemoBootstrap.FormatHud(world).Split('\n');
        int y = 22;
        for (int i = 0; i < lines.Length; i++)
        {
            Raylib.DrawText(lines[i], 24, y, i == 0 ? 22 : 18, Color.RayWhite);
            y += i == 0 ? 28 : 22;
        }

        Raylib.DrawText(
            "[Space] pause  [+/-] ±10k  [P] parallel  [C] sparse  [D] draw  [R] reset",
            12, Raylib.GetScreenHeight() - 28, 16, new Color(180, 190, 200, 255));
    }

    private sealed class Options
    {
        public bool Headless;
        public bool ShowHelp;
        public bool Parallel;
        public bool SparseChurn;
        public int Entities = DemoBootstrap.DefaultEntities;
        public double Seconds = 3.0;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-h":
                    case "--help":
                        o.ShowHelp = true;
                        break;
                    case "--headless":
                        o.Headless = true;
                        break;
                    case "--parallel":
                        o.Parallel = true;
                        break;
                    case "--sparse":
                        o.SparseChurn = true;
                        break;
                    case "--entities":
                        if (i + 1 < args.Length &&
                            int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                            o.Entities = Math.Max(0, n);
                        break;
                    case "--seconds":
                        if (i + 1 < args.Length &&
                            double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
                            o.Seconds = Math.Max(0.1, s);
                        break;
                }
            }

            return o;
        }

        public static void PrintHelp()
        {
            Console.WriteLine("""
                Loom Speed Demo — interactive stress test for Loom ECS

                  dotnet run -c Release --project samples/Loom.SpeedDemo
                  dotnet run -c Release --project samples/Loom.SpeedDemo -- --headless --entities 100000 --seconds 3

                Options:
                  --headless          no window; print throughput and exit
                  --entities N        initial count (default 50000)
                  --seconds S         headless duration (default 3)
                  --parallel          start with Query.ParallelEach
                  --sparse            start with sparse Pulse churn
                  -h, --help          this help

                Window keys: Space pause, +/- spawn batch, P parallel, C sparse, D draw, R reset
                """);
        }
    }
}
