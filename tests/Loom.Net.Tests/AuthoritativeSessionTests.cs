using System;
using System.Buffers.Binary;
using Loom;
using Loom.Entities;
using Loom.Net;

namespace Loom.Net.Tests;

public class AuthoritativeSessionTests
{
    private const byte CmdSpawn = 1;
    private const byte CmdMove = 2;

    private static (WorldSerializer serializer, SnapshotSync snapshots, DeltaSync deltas) CreateSync()
    {
        var serializer = new WorldSerializer()
            .Register<Position>()
            .Register<Velocity>();
        var snapshots = new SnapshotSync(serializer);
        var deltas = new DeltaSync().Register<Position>().Register<Velocity>();
        return (serializer, snapshots, deltas);
    }

    private static void ApplyCommand(World world, NetCommand command)
    {
        var payload = command.Payload;
        if (payload.Length == 0)
            return;

        switch (payload[0])
        {
            case CmdSpawn:
            {
                // [op][x:f32][y:f32]
                if (payload.Length < 1 + 8)
                    return;
                float x = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(1));
                float y = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(5));
                world.Create(new Position { X = x, Y = y }, new Velocity { X = 0, Y = 0 });
                break;
            }
            case CmdMove:
            {
                // [op][entityId:i32][x:f32][y:f32]
                if (payload.Length < 1 + 4 + 8)
                    return;
                int id = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1));
                float x = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(5));
                float y = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(9));
                if (!world.TryGetAliveEntity(id, out var entity))
                    return;
                world.Set(entity, new Position { X = x, Y = y });
                break;
            }
        }
    }

    private static byte[] SpawnCommand(float x, float y)
    {
        var bytes = new byte[1 + 8];
        bytes[0] = CmdSpawn;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(1), x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(5), y);
        return bytes;
    }

    private static byte[] MoveCommand(int entityId, float x, float y)
    {
        var bytes = new byte[1 + 4 + 8];
        bytes[0] = CmdMove;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), entityId);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(5), x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9), y);
        return bytes;
    }

    [Fact]
    public void Loopback_SnapshotThenDeltas_ClientConvergesFromCommands()
    {
        var (_, snapshots, deltas) = CreateSync();

        var serverWorld = new World();
        var clientWorld = new World();

        var serverNet = new LoopbackTransport(1);
        var clientNet = new LoopbackTransport(2);
        LoopbackTransport.Connect(serverNet, clientNet);

        var server = new AuthoritativeServer(
            serverWorld,
            serverNet,
            snapshots,
            deltas,
            tickDurationSeconds: 0.05f,
            simulate: static (_, _) => { },
            applyCommand: ApplyCommand);

        var client = new NetClient(clientWorld, clientNet, snapshots, deltas, serverPeer: serverNet.LocalId);

        // Join: client requests snapshot of empty world.
        client.RequestSnapshot();
        server.PollInbound();
        Assert.Equal(1, client.Poll());
        Assert.True(client.HasSnapshot);
        Assert.Equal(0, clientWorld.EntityCount);

        // Tick 0: spawn from command.
        client.SendCommand(0, SpawnCommand(10f, 20f));
        server.TickOnce();
        Assert.Equal(1, client.Poll());
        Assert.Equal(0, client.LastAppliedTick);
        Assert.Equal(1, clientWorld.EntityCount);

        var spawned = serverWorld.Query().With<Position>().ToList()[0];
        Assert.True(clientWorld.IsAlive(spawned));
        Assert.Equal(10f, clientWorld.Get<Position>(spawned).X);
        Assert.Equal(20f, clientWorld.Get<Position>(spawned).Y);

        // Tick 1: move from command → delta converges.
        client.SendCommand(1, MoveCommand(spawned.Id, 42f, 7f));
        server.TickOnce();
        Assert.Equal(1, client.Poll());
        Assert.Equal(1, client.LastAppliedTick);
        Assert.Equal(42f, clientWorld.Get<Position>(spawned).X);
        Assert.Equal(7f, clientWorld.Get<Position>(spawned).Y);
        Assert.Equal(42f, serverWorld.Get<Position>(spawned).X);
    }

    [Fact]
    public void Update_RunsSimulateAndSkipsEmptyDeltas()
    {
        var (_, snapshots, deltas) = CreateSync();

        var serverWorld = new World();
        var clientWorld = new World();
        var serverNet = new LoopbackTransport(1);
        var clientNet = new LoopbackTransport(2);
        LoopbackTransport.Connect(serverNet, clientNet);

        int simCalls = 0;
        var server = new AuthoritativeServer(
            serverWorld,
            serverNet,
            snapshots,
            deltas,
            tickDurationSeconds: 0.1f,
            simulate: (_, _) => simCalls++,
            applyCommand: ApplyCommand);

        var client = new NetClient(clientWorld, clientNet, snapshots, deltas, serverPeer: serverNet.LocalId);
        server.SendSnapshot(clientNet.LocalId);
        client.Poll();

        // Two ticks with no world mutations → no delta packets.
        Assert.Equal(2, server.Update(0.2f));
        Assert.Equal(2, simCalls);
        Assert.Equal(0, client.Poll());
        Assert.Equal(0, client.LastAppliedTick); // snapshot tick stamp only
    }

    [Fact]
    public void SendSnapshot_OnConnect_AppliesBeforeDeltas()
    {
        var (_, snapshots, deltas) = CreateSync();

        var serverWorld = new World();
        var existing = serverWorld.Create(new Position { X = 1, Y = 2 }, new Velocity { X = 3, Y = 4 });

        var clientWorld = new World();
        var serverNet = new LoopbackTransport(1);
        var clientNet = new LoopbackTransport(2);
        LoopbackTransport.Connect(serverNet, clientNet);

        var server = new AuthoritativeServer(
            serverWorld,
            serverNet,
            snapshots,
            deltas,
            tickDurationSeconds: 1f / 20f,
            simulate: (world, tick) =>
            {
                world.Query().Each<Position, Velocity>((Entity e, ref Position pos, ref Velocity vel) =>
                {
                    pos.X += vel.X * tick.DeltaTime;
                    pos.Y += vel.Y * tick.DeltaTime;
                    world.MarkChanged<Position>(e);
                });
            });

        var client = new NetClient(clientWorld, clientNet, snapshots, deltas, serverPeer: serverNet.LocalId);

        server.SendSnapshot(clientNet.LocalId);
        Assert.Equal(1, client.Poll());
        Assert.Equal(1f, clientWorld.Get<Position>(existing).X);

        server.TickOnce();
        Assert.Equal(1, client.Poll());
        float expectedX = 1f + 3f * server.Clock.TickDuration;
        Assert.Equal(expectedX, clientWorld.Get<Position>(existing).X, precision: 3);
        Assert.Equal(serverWorld.Get<Position>(existing).X, clientWorld.Get<Position>(existing).X, precision: 3);
    }

    [Fact]
    public void Commands_AreAppliedInClientIdOrder()
    {
        var (_, snapshots, deltas) = CreateSync();
        var serverWorld = new World();
        var serverNet = new LoopbackTransport(1);
        var clientA = new LoopbackTransport(2);
        var clientB = new LoopbackTransport(3);
        LoopbackTransport.Connect(serverNet, clientA);
        LoopbackTransport.Connect(serverNet, clientB);

        var applied = new System.Collections.Generic.List<int>();
        var server = new AuthoritativeServer(
            serverWorld,
            serverNet,
            snapshots,
            deltas,
            tickDurationSeconds: 0.05f,
            simulate: static (_, _) => { },
            applyCommand: (_, cmd) => applied.Add(cmd.Client.Value));

        // Enqueue out of order for the same tick; DrainForTick sorts by client id.
        server.Commands.Enqueue(new NetPeerId(3), 0, new byte[] { 1 });
        server.Commands.Enqueue(new NetPeerId(2), 0, new byte[] { 1 });
        server.TickOnce();

        Assert.Equal(new[] { 2, 3 }, applied);
    }
}
