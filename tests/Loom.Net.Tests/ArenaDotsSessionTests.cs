using System.Buffers.Binary;
using Loom;
using Loom.Entities;
using Loom.Net;

namespace Loom.Net.Tests;

/// <summary>
/// End-to-end arena session: 2 clients, spawn-on-join, move commands, position convergence.
/// Mirrors samples/Loom.ArenaDots without referencing the sample project.
/// </summary>
public class ArenaDotsSessionTests
{
    const float HalfExtent = 10f;
    const float MaxSpeed = 8f;
    const float Accel = 28f;
    const float Friction = 6f;
    const float PlayerRadius = 0.45f;
    const float TickDt = 1f / 20f;
    const byte OpMove = 1;

    struct Pos
    {
        public float X, Y, Z;
    }

    struct Vel
    {
        public float X, Y, Z;
    }

    struct PlayerOwner
    {
        public int PeerId;
    }

    sealed class ArenaSim : IAuthoritativeSimulation
    {
        readonly Dictionary<int, (float Dx, float Dy)> _thrust = new();

        public void ApplyCommand(World world, NetCommand command)
        {
            var payload = command.Payload;
            if (payload.Length < 1 + 8 || payload[0] != OpMove)
                return;
            if (!TryFindPlayer(world, command.Client.Value, out _))
                return;

            float dx = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(1));
            float dy = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(5));
            _thrust[command.Client.Value] = (Math.Clamp(dx, -1f, 1f), Math.Clamp(dy, -1f, 1f));
        }

        public void Simulate(World world, NetworkTick tick)
        {
            float dt = tick.DeltaTime;
            world.Query().Each<Pos, Vel, PlayerOwner>((Entity e, ref Pos pos, ref Vel vel, ref PlayerOwner owner) =>
            {
                float ox = vel.X, oy = vel.Y, opx = pos.X, opy = pos.Y;
                if (_thrust.TryGetValue(owner.PeerId, out var thrust))
                {
                    vel.X += thrust.Dx * Accel * dt;
                    vel.Y += thrust.Dy * Accel * dt;
                }

                float damp = MathF.Exp(-Friction * dt);
                vel.X *= damp;
                vel.Y *= damp;
                float len = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
                if (len > MaxSpeed)
                {
                    float s = MaxSpeed / len;
                    vel.X *= s;
                    vel.Y *= s;
                }

                pos.X += vel.X * dt;
                pos.Y += vel.Y * dt;
                pos.X = Math.Clamp(pos.X, -HalfExtent + PlayerRadius, HalfExtent - PlayerRadius);
                pos.Y = Math.Clamp(pos.Y, -HalfExtent + PlayerRadius, HalfExtent - PlayerRadius);

                if (vel.X != ox || vel.Y != oy)
                    world.MarkChanged<Vel>(e);
                if (pos.X != opx || pos.Y != opy)
                    world.MarkChanged<Pos>(e);
            });
            _thrust.Clear();
        }
    }

    static byte[] EncodeMove(float dx, float dy)
    {
        var bytes = new byte[1 + 8];
        bytes[0] = OpMove;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(1), dx);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(5), dy);
        return bytes;
    }

    static Entity SpawnPlayer(World world, int peerId, float x, float y) =>
        world.Create(
            new Pos { X = x, Y = y, Z = 0 },
            new Vel { X = 0, Y = 0, Z = 0 },
            new PlayerOwner { PeerId = peerId });

    static bool TryFindPlayer(World world, int peerId, out Entity player)
    {
        Entity found = default;
        bool ok = false;
        world.Query().Each<PlayerOwner>((Entity e, ref PlayerOwner owner) =>
        {
            if (!ok && owner.PeerId == peerId)
            {
                found = e;
                ok = true;
            }
        });
        player = found;
        return ok;
    }

    static (WorldSerializer, SnapshotSync, DeltaSync) CreateSync()
    {
        var serializer = new WorldSerializer()
            .Register<Pos>()
            .Register<Vel>()
            .Register<PlayerOwner>();
        var snapshots = new SnapshotSync(serializer);
        var deltas = new DeltaSync()
            .Register<Pos>()
            .Register<Vel>()
            .Register<PlayerOwner>();
        return (serializer, snapshots, deltas);
    }

    [Fact]
    public void ArenaSession_TwoClients_ConvergeOnPlayerPositions()
    {
        var (_, snapshots, deltas) = CreateSync();
        var sim = new ArenaSim();

        var serverWorld = new World();
        var serverNet = new LoopbackTransport(1);
        var clientNetA = new LoopbackTransport(2);
        var clientNetB = new LoopbackTransport(3);
        LoopbackTransport.Connect(serverNet, clientNetA);
        LoopbackTransport.Connect(serverNet, clientNetB);

        var server = new AuthoritativeServer(
            serverWorld, serverNet, snapshots, deltas, TickDt, sim);

        var clientA = new NetClient(new World(), clientNetA, snapshots, deltas, serverNet.LocalId);
        var clientB = new NetClient(new World(), clientNetB, snapshots, deltas, serverNet.LocalId);

        // Join A with first player.
        var pA = SpawnPlayer(serverWorld, clientNetA.LocalId.Value, -4f, 0f);
        server.SendSnapshot(clientNetA.LocalId);
        Assert.Equal(1, clientA.Poll());
        Assert.True(clientA.World.IsAlive(pA));

        // Join B: spawn via delta to A, snapshot to B.
        var pB = SpawnPlayer(serverWorld, clientNetB.LocalId.Value, 4f, 0f);
        server.SendSnapshot(clientNetB.LocalId);
        Assert.Equal(1, clientB.Poll());
        Assert.True(clientB.World.IsAlive(pA));
        Assert.True(clientB.World.IsAlive(pB));

        server.TickOnce(); // delta spawn of B → A
        Assert.True(clientA.Poll() >= 1);
        Assert.True(clientA.World.IsAlive(pB));

        // Drive a few move ticks and assert exact position match.
        for (long tick = 1; tick <= 8; tick++)
        {
            clientA.SendCommand(tick, EncodeMove(1f, 0.2f));
            clientB.SendCommand(tick, EncodeMove(-1f, -0.2f));
            server.TickOnce();
            clientA.Poll();
            clientB.Poll();
        }

        AssertPlayerMatch(serverWorld, clientA.World, pA);
        AssertPlayerMatch(serverWorld, clientA.World, pB);
        AssertPlayerMatch(serverWorld, clientB.World, pA);
        AssertPlayerMatch(serverWorld, clientB.World, pB);

        // Players should have moved away from spawn.
        Assert.True(serverWorld.Get<Pos>(pA).X > -4f);
        Assert.True(serverWorld.Get<Pos>(pB).X < 4f);
    }

    static void AssertPlayerMatch(World server, World client, Entity player)
    {
        Assert.True(client.IsAlive(player));
        var sp = server.Get<Pos>(player);
        var cp = client.Get<Pos>(player);
        Assert.Equal(sp.X, cp.X, precision: 4);
        Assert.Equal(sp.Y, cp.Y, precision: 4);
        Assert.Equal(server.Get<PlayerOwner>(player).PeerId, client.Get<PlayerOwner>(player).PeerId);
    }
}
