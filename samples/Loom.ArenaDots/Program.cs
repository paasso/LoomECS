using Loom;
using Loom.Entities;
using Loom.Net;
using Raylib_cs;

namespace Loom.ArenaDots;

static class Program
{
    static int Main(string[] args)
    {
        int players = 2;
        bool visual = false;
        int ticks = 40;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--players" && i + 1 < args.Length && int.TryParse(args[++i], out var n))
                players = n;
            else if (args[i] == "--visual" || args[i] == "--raylib")
                visual = true;
            else if (args[i] == "--ticks" && i + 1 < args.Length && int.TryParse(args[++i], out var t))
                ticks = t;
            else if (args[i] is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }
        }

        players = Math.Clamp(players, 1, 4);

        using var session = new ArenaSession(players);
        session.JoinAll();

        Console.WriteLine($"Arena Dots — {players} players, tick={ArenaSession.TickHz:0} Hz");
        Console.WriteLine("Join complete. Server entities={0}", session.ServerWorld.EntityCount);
        for (int i = 0; i < session.Clients.Count; i++)
            Console.WriteLine("  client[{0}] peer={1} entities={2} tick={3}",
                i, session.ClientPeerId(i).Value,
                session.Clients[i].World.EntityCount,
                session.Clients[i].LastAppliedTick);

        if (visual)
            return RunVisual(session);

        return RunConsole(session, ticks);
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
            Loom Arena Dots — authoritative multiplayer dots over loopback.

              --players N   1–4 clients (default 2)
              --ticks N     console sim ticks (default 40)
              --visual      Raylib top-down view (WASD / arrows, Space/Enter fire)
              --help
            """);
    }

    static int RunConsole(ArenaSession session, int ticks)
    {
        long baseTick = session.Server.LastCompletedTick + 1;

        for (int i = 0; i < ticks; i++)
        {
            long tick = baseTick + i;
            float t = i / (float)ticks;

            session.SendMove(0, tick, dx: 1f, dy: t < 0.5f ? 0.4f : -0.2f);
            if (session.Clients.Count > 1)
                session.SendMove(1, tick, dx: -0.8f, dy: t < 0.5f ? -0.5f : 0.3f);

            if (i == ticks / 3)
                session.SendFire(0, tick);
            if (session.Clients.Count > 1 && i == ticks / 2)
                session.SendFire(1, tick);

            session.Tick();

            if (i % 5 == 0 || i == ticks - 1)
                PrintConvergence(session, tick);
        }

        Console.WriteLine("done.");
        return 0;
    }

    static void PrintConvergence(ArenaSession session, long tick)
    {
        Console.WriteLine($"--- tick {tick} (server last={session.Server.LastCompletedTick}) ---");
        session.ServerWorld.Query().Each<Pos, PlayerOwner>((Entity e, ref Pos pos, ref PlayerOwner owner) =>
        {
            Console.Write($"  server peer={owner.PeerId} pos=({pos.X,7:F2},{pos.Y,7:F2})");
            for (int c = 0; c < session.Clients.Count; c++)
            {
                var cw = session.Clients[c].World;
                if (!cw.IsAlive(e))
                {
                    Console.Write($" | c{c}=missing");
                    continue;
                }

                var cp = cw.Get<Pos>(e);
                float dx = cp.X - pos.X, dy = cp.Y - pos.Y;
                Console.Write($" | c{c}=({cp.X,7:F2},{cp.Y,7:F2}) d={MathF.Sqrt(dx * dx + dy * dy):F4}");
            }

            Console.WriteLine();
        });
    }

    static int RunVisual(ArenaSession session)
    {
        const int width = 900;
        const int height = 900;
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(width, height, "Loom — Arena Dots");
        Raylib.SetTargetFPS(60);

        float accumulator = 0f;
        long nextCommandTick = session.Server.LastCompletedTick + 1;

        var colors = new[]
        {
            new Color(80, 180, 255, 255),
            new Color(255, 140, 80, 255),
            new Color(120, 220, 120, 255),
            new Color(220, 120, 220, 255),
        };

        while (!Raylib.WindowShouldClose())
        {
            float frameDt = Raylib.GetFrameTime();
            accumulator += frameDt;

            while (accumulator >= ArenaSession.TickDt)
            {
                accumulator -= ArenaSession.TickDt;
                long tick = nextCommandTick++;

                var (dx0, dy0) = ReadAxes(
                    KeyboardKey.A, KeyboardKey.D, KeyboardKey.W, KeyboardKey.S);
                session.SendMove(0, tick, dx0, dy0);
                if (Raylib.IsKeyPressed(KeyboardKey.Space))
                    session.SendFire(0, tick);

                if (session.Clients.Count > 1)
                {
                    var (dx1, dy1) = ReadAxes(
                        KeyboardKey.Left, KeyboardKey.Right, KeyboardKey.Up, KeyboardKey.Down);
                    session.SendMove(1, tick, dx1, dy1);
                    if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
                        session.SendFire(1, tick);
                }

                session.Tick();
            }

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(18, 22, 28, 255));

            var view = session.Clients[0].World;
            DrawArena(width, height);

            view.Query().Each<Pos, Lifetime>((Entity _, ref Pos pos, ref Lifetime _) =>
            {
                var (sx, sy) = WorldToScreen(pos.X, pos.Y, width, height);
                Raylib.DrawCircle((int)sx, (int)sy, 5f, new Color(240, 220, 90, 255));
            });

            view.Query().Each<Pos, PlayerOwner>((Entity _, ref Pos pos, ref PlayerOwner owner) =>
            {
                int idx = Math.Clamp(owner.PeerId - 2, 0, colors.Length - 1);
                var (sx, sy) = WorldToScreen(pos.X, pos.Y, width, height);
                float r = ArenaSim.PlayerRadius * (width / (ArenaSim.HalfExtent * 2f));
                Raylib.DrawCircle((int)sx, (int)sy, r, colors[idx]);
                Raylib.DrawCircleLines((int)sx, (int)sy, r, Color.RayWhite);
            });

            Raylib.DrawText(
                $"tick={session.Clients[0].LastAppliedTick}  entities={view.EntityCount}  P1 WASD+Space  P2 arrows+Enter",
                16, 16, 18, Color.RayWhite);
            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
        return 0;
    }

    static (float Dx, float Dy) ReadAxes(KeyboardKey left, KeyboardKey right, KeyboardKey up, KeyboardKey down)
    {
        float dx = 0f, dy = 0f;
        if (Raylib.IsKeyDown(left)) dx -= 1f;
        if (Raylib.IsKeyDown(right)) dx += 1f;
        if (Raylib.IsKeyDown(up)) dy += 1f;
        if (Raylib.IsKeyDown(down)) dy -= 1f;
        return (dx, dy);
    }

    static void DrawArena(int width, int height)
    {
        var (x0, y0) = WorldToScreen(-ArenaSim.HalfExtent, ArenaSim.HalfExtent, width, height);
        var (x1, y1) = WorldToScreen(ArenaSim.HalfExtent, -ArenaSim.HalfExtent, width, height);
        Raylib.DrawRectangleLines((int)x0, (int)y0, (int)(x1 - x0), (int)(y1 - y0), new Color(60, 70, 90, 255));
        var (cx, cy) = WorldToScreen(0, 0, width, height);
        Raylib.DrawCircleLines((int)cx, (int)cy, 4f, new Color(50, 55, 70, 255));
    }

    static (float X, float Y) WorldToScreen(float x, float y, int width, int height)
    {
        float pad = 40f;
        float span = ArenaSim.HalfExtent * 2f;
        float sx = pad + (x + ArenaSim.HalfExtent) / span * (width - pad * 2f);
        float sy = pad + (ArenaSim.HalfExtent - y) / span * (height - pad * 2f);
        return (sx, sy);
    }
}
