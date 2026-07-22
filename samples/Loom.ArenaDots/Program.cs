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
        int latencyMs = 0;
        int jitterMs = 0;
        bool rttLatency = false;
        int? listenPort = null;
        string? connectEndpoint = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--players" && i + 1 < args.Length && int.TryParse(args[++i], out var n))
                players = n;
            else if (args[i] == "--visual" || args[i] == "--raylib")
                visual = true;
            else if (args[i] == "--ticks" && i + 1 < args.Length && int.TryParse(args[++i], out var t))
                ticks = t;
            else if (args[i] == "--latency" && i + 1 < args.Length && int.TryParse(args[++i], out var lat))
                latencyMs = lat;
            else if (args[i] == "--jitter" && i + 1 < args.Length && int.TryParse(args[++i], out var jit))
                jitterMs = jit;
            else if (args[i] == "--rtt")
                rttLatency = true;
            else if (args[i] == "--listen" && i + 1 < args.Length && int.TryParse(args[++i], out var port))
                listenPort = port;
            else if (args[i] == "--connect" && i + 1 < args.Length)
                connectEndpoint = args[++i];
            else if (args[i] is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }
        }

        if (listenPort.HasValue && connectEndpoint != null)
        {
            Console.Error.WriteLine("Use either --listen or --connect, not both.");
            return 1;
        }

        players = Math.Clamp(players, 1, 4);
        var latencyMode = rttLatency ? LatencyMode.RoundTrip : LatencyMode.OneWay;

        using ArenaSession session = listenPort.HasValue
            ? ArenaSession.CreateListen(listenPort.Value, remotePlayers: players)
            : connectEndpoint != null
                ? CreateConnectSession(connectEndpoint)
                : ArenaSession.CreateLoopback(players, latencyMs, jitterMs, latencyMode);

        if (!listenPort.HasValue && connectEndpoint == null)
            session.JoinAll();

        PrintBanner(session, players, latencyMs, jitterMs, latencyMode, listenPort, connectEndpoint);

        if (visual)
            return RunVisual(session);

        return RunConsole(session, ticks);
    }

    static ArenaSession CreateConnectSession(string endpoint)
    {
        var parts = endpoint.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            throw new ArgumentException($"Expected host:port, got '{endpoint}'.");
        return ArenaSession.CreateConnect(parts[0], port);
    }

    static void PrintBanner(
        ArenaSession session,
        int players,
        int latencyMs,
        int jitterMs,
        LatencyMode mode,
        int? listenPort,
        string? connectEndpoint)
    {
        if (listenPort.HasValue)
        {
            Console.WriteLine($"Arena Dots — UDP host :{listenPort.Value}, remotes={players}, tick={ArenaSession.TickHz:0} Hz");
        }
        else if (connectEndpoint != null)
        {
            Console.WriteLine($"Arena Dots — UDP client → {connectEndpoint}, tick={ArenaSession.TickHz:0} Hz (predict + interpolate)");
        }
        else
        {
            string lat = latencyMs > 0 || jitterMs > 0
                ? $", latency={latencyMs}ms{(jitterMs > 0 ? $"+jitter{jitterMs}" : "")} ({mode})"
                : "";
            Console.WriteLine($"Arena Dots — {players} players loopback{lat}, tick={ArenaSession.TickHz:0} Hz (predict + interpolate)");
            Console.WriteLine("Join complete. Server entities={0}", session.ServerWorld.EntityCount);
            for (int i = 0; i < session.Clients.Count; i++)
                Console.WriteLine("  client[{0}] peer={1} entities={2} tick={3}",
                    i, session.ClientPeerId(i).Value,
                    session.Clients[i].World.EntityCount,
                    session.Clients[i].LastAppliedTick);
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
            Loom Arena Dots — authoritative multiplayer dots (loopback or LiteNetLib UDP).
            Local player is client-predicted; remotes/projectiles are interpolated.

              --players N     loopback clients 1–4 (default 2), or UDP host remote count
              --ticks N       console sim ticks (default 40)
              --latency N     one-way delay ms on loopback (exercises predict/reconcile)
              --jitter N      extra random delay 0..N ms (with --latency)
              --rtt           treat --latency as total RTT (half per direction)
              --listen PORT   UDP host (LiteNetLib); waits for --players remotes
              --connect H:P   UDP client (LiteNetLib), e.g. 127.0.0.1:9050
              --visual        Raylib top-down view (WASD / arrows, Space/Enter fire)
              --help

            Metrics (console + visual overlay): prediction error / soft·hard corrects,
            replayed inputs, approx tx/rx Mbps (framed payloads), tick + interp buffer.

            Two-process UDP:
              Terminal A:  dotnet run --project samples/Loom.ArenaDots -- --listen 9050 --players 1
              Terminal B:  dotnet run --project samples/Loom.ArenaDots -- --connect 127.0.0.1:9050 --visual
            """);
    }

    static int RunConsole(ArenaSession session, int ticks)
    {
        // Host UDP without local clients: just tick and print metrics.
        if (session.IsHost && session.Clients.Count == 0)
            return RunHostConsole(session, ticks);

        // UDP client: scripted moves + poll.
        if (!session.IsHost)
            return RunClientConsole(session, ticks);

        long baseTick = session.Server.LastCompletedTick + 1;
        int inputDelay = session.InputDelayTicks;

        for (int i = 0; i < ticks; i++)
        {
            long tick = baseTick + i;
            long cmdTick = tick + inputDelay;
            float t = i / (float)ticks;

            session.SendMove(0, cmdTick, dx: 1f, dy: t < 0.5f ? 0.4f : -0.2f);
            if (session.Clients.Count > 1)
                session.SendMove(1, cmdTick, dx: -0.8f, dy: t < 0.5f ? -0.5f : 0.3f);

            if (i == ticks / 3)
                session.SendFire(0, cmdTick);
            if (session.Clients.Count > 1 && i == ticks / 2)
                session.SendFire(1, cmdTick);

            session.TickWithPacing();

            if (i % 5 == 0 || i == ticks - 1)
                PrintConvergence(session, tick);
        }

        // Drain any remaining delayed packets so final reconcile settles.
        if (session.LatencyMilliseconds > 0)
        {
            for (int i = 0; i < 10; i++)
                session.TickWithPacing();
        }

        Console.WriteLine("done. {0}", session.CollectMetrics());
        return 0;
    }

    static int RunHostConsole(ArenaSession session, int ticks)
    {
        Console.WriteLine("Host pumping {0} ticks (remotes drive input)…", ticks);
        for (int i = 0; i < ticks; i++)
        {
            session.Tick();
            if (i % 5 == 0 || i == ticks - 1)
                Console.WriteLine("  {0}", session.CollectMetrics());
            // Give remotes time to send when using real UDP in interactive runs.
            if (session.Meters.Count > 0)
                Thread.Sleep( (int)(ArenaSession.TickDt * 1000));
        }

        Console.WriteLine("done. {0}", session.CollectMetrics());
        return 0;
    }

    static int RunClientConsole(ArenaSession session, int ticks)
    {
        long tick = Math.Max(0, session.Clients[0].LastAppliedTick + 1);
        for (int i = 0; i < ticks; i++)
        {
            float t = i / (float)ticks;
            session.SendMove(0, tick, dx: 1f, dy: t < 0.5f ? 0.3f : -0.25f);
            if (i == ticks / 3)
                session.SendFire(0, tick);
            session.Tick();
            tick++;

            if (i % 5 == 0 || i == ticks - 1)
            {
                var m = session.CollectMetrics();
                Console.WriteLine("--- {0} ---", m);
                var rec = session.Views[0].LastReconcile;
                Console.WriteLine("  last reconcile={0} err={1:F4} replay={2}", rec.Kind, rec.Error, rec.Replayed);
            }

            Thread.Sleep((int)(ArenaSession.TickDt * 1000));
        }

        Console.WriteLine("done. {0}", session.CollectMetrics());
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
                var view = session.Views[c];
                if (view.TryGetRenderTransform(e, owner.PeerId, tickFraction: 0f, out var sample))
                {
                    float dx = sample.PosX - pos.X, dy = sample.PosY - pos.Y;
                    string tag = owner.PeerId == view.LocalPeerId ? "pred" : "lerp";
                    Console.Write($" | c{c}/{tag}=({sample.PosX,7:F2},{sample.PosY,7:F2}) d={MathF.Sqrt(dx * dx + dy * dy):F4}");
                }
                else
                {
                    Console.Write($" | c{c}=missing");
                }
            }

            Console.WriteLine();
        });

        Console.WriteLine("  {0}", session.CollectMetrics());
    }

    static int RunVisual(ArenaSession session)
    {
        const int width = 900;
        const int height = 900;
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(width, height, "Loom — Arena Dots (predict + interpolate)");
        Raylib.SetTargetFPS(60);

        float accumulator = 0f;
        long nextCommandTick = session.IsHost && session.Clients.Count > 0
            ? session.Server.LastCompletedTick + 1
            : Math.Max(0, session.Clients.Count > 0 ? session.Clients[0].LastAppliedTick + 1 : 0);

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
                long tick = nextCommandTick + session.InputDelayTicks;
                nextCommandTick++;

                if (session.Clients.Count > 0)
                {
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
                }

                session.Tick();
            }

            float tickFraction = accumulator / ArenaSession.TickDt;
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(18, 22, 28, 255));
            DrawArena(width, height);

            if (session.Views.Count > 0)
                DrawClientView(session.Views[0], tickFraction, width, height, colors);
            else if (session.IsHost)
                DrawServerWorld(session, width, height, colors);

            var metrics = session.CollectMetrics();
            Raylib.DrawText(metrics.ToString(), 16, 16, 16, Color.RayWhite);
            Raylib.DrawText(
                session.Views.Count > 0
                    ? "WASD+Space (predicted)  arrows+Enter = P2 when loopback"
                    : "UDP host — authoritative view (clients drive input)",
                16, 40, 16, new Color(180, 190, 200, 255));
            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
        return 0;
    }

    static void DrawClientView(ArenaClientView view, float tickFraction, int width, int height, Color[] colors)
    {
        var simWorld = view.Client.World;

        simWorld.Query().Each<Pos, Lifetime>((Entity e, ref Pos _, ref Lifetime _) =>
        {
            if (!view.TryGetProjectileTransform(e, tickFraction, out var xf))
                return;
            var (sx, sy) = WorldToScreen(xf.PosX, xf.PosY, width, height);
            Raylib.DrawCircle((int)sx, (int)sy, 5f, new Color(240, 220, 90, 255));
        });

        simWorld.Query().Each<Pos, PlayerOwner>((Entity e, ref Pos _, ref PlayerOwner owner) =>
        {
            if (!view.TryGetRenderTransform(e, owner.PeerId, tickFraction, out var xf))
                return;
            int idx = Math.Clamp(owner.PeerId - 2, 0, colors.Length - 1);
            var (sx, sy) = WorldToScreen(xf.PosX, xf.PosY, width, height);
            float r = ArenaSim.PlayerRadius * (width / (ArenaSim.HalfExtent * 2f));
            Raylib.DrawCircle((int)sx, (int)sy, r, colors[idx]);
            Raylib.DrawCircleLines((int)sx, (int)sy, r, Color.RayWhite);
            if (owner.PeerId == view.LocalPeerId)
                Raylib.DrawCircleLines((int)sx, (int)sy, r + 3f, new Color(255, 255, 255, 120));
        });
    }

    static void DrawServerWorld(ArenaSession session, int width, int height, Color[] colors)
    {
        session.ServerWorld.Query().Each<Pos, Lifetime>((Entity _, ref Pos pos, ref Lifetime _) =>
        {
            var (sx, sy) = WorldToScreen(pos.X, pos.Y, width, height);
            Raylib.DrawCircle((int)sx, (int)sy, 5f, new Color(240, 220, 90, 255));
        });

        session.ServerWorld.Query().Each<Pos, PlayerOwner>((Entity _, ref Pos pos, ref PlayerOwner owner) =>
        {
            int idx = Math.Clamp(owner.PeerId - 2, 0, colors.Length - 1);
            var (sx, sy) = WorldToScreen(pos.X, pos.Y, width, height);
            float r = ArenaSim.PlayerRadius * (width / (ArenaSim.HalfExtent * 2f));
            Raylib.DrawCircle((int)sx, (int)sy, r, colors[idx]);
            Raylib.DrawCircleLines((int)sx, (int)sy, r, Color.RayWhite);
        });
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
