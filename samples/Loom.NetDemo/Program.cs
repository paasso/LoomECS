using Loom;
using Loom.Net;

// Default: measure snapshot/delta sizes for 100 Pos+Vel entities (float3 each).
// Pass --demo to run the original authoritative tick → loopback sample instead.
if (args is ["--demo"])
{
    RunLoopbackDemo();
}
else
{
    TrafficProbe.Run();
}

static void RunLoopbackDemo()
{
    var serializer = new WorldSerializer()
        .Register<Pos>()
        .Register<Vel>();

    var snapshots = new SnapshotSync(serializer);
    var deltas = new DeltaSync().Register<Pos>();

    var serverWorld = new World();
    var clientWorld = new World();
    deltas.EnableTracking(serverWorld);

    var serverNet = new LoopbackTransport(localId: 1);
    var clientNet = new LoopbackTransport(localId: 2);
    LoopbackTransport.Connect(serverNet, clientNet);

    var clock = new NetworkClock(tickDurationSeconds: 1f / 20f);
    var commands = new NetCommandBuffer();

    var player = serverWorld.Create(
        new Pos { X = 0, Y = 0, Z = 0 },
        new Vel { X = 1, Y = 0, Z = 0 });

    // Pretend a client sent "move right" for tick 0.
    commands.Enqueue(clientNet.LocalId, tick: 0, payload: new byte[] { 1 });

    float simulated = 0f;
    const float frameDt = 1f / 60f;
    while (simulated < 0.2f)
    {
        simulated += frameDt;
        while (clock.TryAdvance(frameDt, out var tick))
        {
            foreach (var cmd in commands.DrainForTick(tick.Index))
            {
                if (cmd.Payload.Length > 0 && cmd.Payload[0] == 1)
                    serverWorld.Set(player, new Vel { X = 5, Y = 0, Z = 0 });
            }

            ref var pos = ref serverWorld.Get<Pos>(player);
            var vel = serverWorld.Get<Vel>(player);
            pos.X += vel.X * tick.DeltaTime;
            pos.Y += vel.Y * tick.DeltaTime;
            pos.Z += vel.Z * tick.DeltaTime;
            serverWorld.MarkChanged<Pos>(player);

            if (tick.Index == 0)
            {
                // Full state on first tick (join).
                serverNet.Broadcast(snapshots.CaptureFramed(serverWorld, tick.Index));
            }
            else
            {
                serverNet.Broadcast(deltas.CaptureFramed(serverWorld, tick.Index));
            }

            serverWorld.ClearComponentChanges();
        }
    }

    while (clientNet.TryReceive(out var packet))
    {
        if (!NetMessage.TryUnpack(packet.Payload, out var kind, out var tick, out _))
            continue;

        if (kind == NetMessageKind.Snapshot)
            snapshots.ApplyFramed(clientWorld, packet.Payload, out _);
        else if (kind == NetMessageKind.Delta)
            deltas.ApplyFramed(clientWorld, packet.Payload, out _);

        var p = clientWorld.Get<Pos>(player);
        Console.WriteLine($"client tick={tick} kind={kind} pos=({p.X:F2},{p.Y:F2},{p.Z:F2})");
    }

    var sp = serverWorld.Get<Pos>(player);
    Console.WriteLine($"server pos=({sp.X:F2},{sp.Y:F2},{sp.Z:F2})");
    Console.WriteLine("done.");
}

/// <summary>float3 position (X, Y, Z).</summary>
struct Pos
{
    public float X;
    public float Y;
    public float Z;
}

/// <summary>float3 velocity (X, Y, Z).</summary>
struct Vel
{
    public float X;
    public float Y;
    public float Z;
}
