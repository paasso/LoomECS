using Loom;
using Loom.Entities;
using Loom.Net;

namespace Loom.Net.Tests;

public class DelayedTransportTests
{
    [Fact]
    public void Send_DeliversOnlyAfterDelay()
    {
        long now = 1_000;
        var a = new LoopbackTransport(1);
        var b = new LoopbackTransport(2);
        LoopbackTransport.Connect(a, b);

        var delayed = new DelayedTransport(
            a,
            latencyMilliseconds: 40,
            nowMilliseconds: () => now,
            randomSeed: 1);

        delayed.Send(b.LocalId, new byte[] { 9, 8 });
        Assert.Equal(1, delayed.PendingCount);
        Assert.False(b.TryReceive(out _));

        delayed.Flush();
        Assert.False(b.TryReceive(out _));

        now += 39;
        delayed.Flush();
        Assert.False(b.TryReceive(out _));

        now += 1;
        delayed.Flush();
        Assert.True(b.TryReceive(out var packet));
        Assert.Equal(1, packet.Peer.Value);
        Assert.Equal(new byte[] { 9, 8 }, packet.Payload);
        Assert.Equal(0, delayed.PendingCount);
    }

    [Fact]
    public void RoundTripMode_UsesHalfDelayPerDirection()
    {
        long now = 0;
        var a = new LoopbackTransport(1);
        var b = new LoopbackTransport(2);
        LoopbackTransport.Connect(a, b);

        var delayedA = new DelayedTransport(
            a, latencyMilliseconds: 100, mode: LatencyMode.RoundTrip, nowMilliseconds: () => now);
        var delayedB = new DelayedTransport(
            b, latencyMilliseconds: 100, mode: LatencyMode.RoundTrip, nowMilliseconds: () => now);

        delayedA.Send(b.LocalId, new byte[] { 1 });
        now += 49;
        delayedA.Flush();
        Assert.False(delayedB.TryReceive(out _));

        now += 1;
        delayedA.Flush();
        Assert.True(delayedB.TryReceive(out var packet));
        Assert.Equal(new byte[] { 1 }, packet.Payload);
    }

    [Fact]
    public void FlushAll_DeliversImmediately()
    {
        long now = 0;
        var a = new LoopbackTransport(1);
        var b = new LoopbackTransport(2);
        LoopbackTransport.Connect(a, b);
        var delayed = new DelayedTransport(a, 1_000, nowMilliseconds: () => now);

        delayed.Broadcast(new byte[] { 3 });
        delayed.FlushAll();
        Assert.True(b.TryReceive(out var packet));
        Assert.Equal(new byte[] { 3 }, packet.Payload);
    }

    [Fact]
    public void MeteredTransport_CountsBytes()
    {
        var a = new LoopbackTransport(1);
        var b = new LoopbackTransport(2);
        LoopbackTransport.Connect(a, b);
        var metered = new MeteredTransport(a);

        metered.Send(b.LocalId, new byte[] { 1, 2, 3, 4 });
        Assert.Equal(4, metered.BytesSent);
        Assert.Equal(1, metered.PacketsSent);

        Assert.True(b.TryReceive(out _));
        Assert.Equal(0, metered.BytesReceived);

        b.Send(a.LocalId, new byte[] { 5, 6 });
        Assert.True(metered.TryReceive(out var pkt));
        Assert.Equal(2, metered.BytesReceived);
        Assert.Equal(new byte[] { 5, 6 }, pkt.Payload);
    }
}

public class DelayedLoopbackSessionTests
{
    struct DelayPos
    {
        public float X, Y, Z;
    }

    struct DelayVel
    {
        public float X, Y, Z;
    }

    struct DelayOwner
    {
        public int PeerId;
    }

    sealed class Sim : IAuthoritativeSimulation
    {
        public void ApplyCommand(World world, NetCommand command)
        {
            if (command.Payload.Length < 4)
                return;
            float dx = BitConverter.ToSingle(command.Payload, 0);
            world.Query().Each<DelayPos, DelayOwner>((Entity e, ref DelayPos pos, ref DelayOwner owner) =>
            {
                if (owner.PeerId != command.Client.Value)
                    return;
                pos.X += dx;
                world.MarkChanged<DelayPos>(e);
            });
        }

        public void Simulate(World world, NetworkTick tick)
        {
        }
    }

    [Fact]
    public void DelayedLoopback_SessionStillConverges()
    {
        long now = 0;
        var serializer = new WorldSerializer().Register<DelayPos>().Register<DelayVel>().Register<DelayOwner>();
        var snapshots = new SnapshotSync(serializer);
        var deltas = new DeltaSync().Register<DelayPos>().Register<DelayVel>().Register<DelayOwner>();
        var sim = new Sim();

        var rawServer = new LoopbackTransport(1);
        var rawClient = new LoopbackTransport(2);
        LoopbackTransport.Connect(rawServer, rawClient);

        var serverNet = new DelayedTransport(rawServer, 25, nowMilliseconds: () => now);
        var clientNet = new DelayedTransport(rawClient, 25, nowMilliseconds: () => now);

        var serverWorld = new World();
        var server = new AuthoritativeServer(serverWorld, serverNet, snapshots, deltas, 0.05f, sim);
        var client = new NetClient(new World(), clientNet, snapshots, deltas, rawServer.LocalId);

        var player = serverWorld.Create(
            new DelayPos { X = 0, Y = 0, Z = 0 },
            new DelayVel(),
            new DelayOwner { PeerId = 2 });

        server.SendSnapshot(rawClient.LocalId);
        now += 25;
        serverNet.Flush();
        Assert.Equal(1, client.Poll());
        Assert.True(client.World.IsAlive(player));

        client.SendCommand(0, BitConverter.GetBytes(2f));
        now += 25;
        clientNet.Flush();
        server.TickOnce();

        now += 25;
        serverNet.Flush();
        client.Poll();

        Assert.Equal(2f, serverWorld.Get<DelayPos>(player).X, precision: 3);
        Assert.Equal(2f, client.World.Get<DelayPos>(player).X, precision: 3);
    }
}
