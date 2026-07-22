using Loom;
using Loom.Net;

// Minimal authoritative tick → snapshot → loopback → client apply demo (no sockets).

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

var player = serverWorld.Create(new Pos { X = 0 }, new Vel { X = 1 });

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
                serverWorld.Set(player, new Vel { X = 5 });
        }

        ref var pos = ref serverWorld.Get<Pos>(player);
        var vel = serverWorld.Get<Vel>(player);
        pos.X += vel.X * tick.DeltaTime;
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

    Console.WriteLine($"client tick={tick} kind={kind} pos.X={clientWorld.Get<Pos>(player).X:F2}");
}

Console.WriteLine($"server pos.X={serverWorld.Get<Pos>(player).X:F2}");
Console.WriteLine("done.");

struct Pos
{
    public float X;
}

struct Vel
{
    public float X;
}
