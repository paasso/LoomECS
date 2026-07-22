using Loom;
using Loom.Net;

namespace Loom.Net.Tests;

public class SnapshotSyncTests
{
    private static WorldSerializer CreateSerializer() =>
        new WorldSerializer()
            .Register<Position>()
            .Register<Velocity>()
            .Register<Health>()
            .Register<Dead>()
            .RegisterSingleton<FrameTime>();

    [Fact]
    public void CaptureApply_RoundTripsEntitiesAndComponents()
    {
        var server = new World();
        var a = server.Create(new Position { X = 1, Y = 2 }, new Velocity { X = 3, Y = 4 });
        server.Add(a, new Dead());
        server.SetSingleton(new FrameTime { Frame = 9, Delta = 0.016f });

        var sync = new SnapshotSync(CreateSerializer());
        byte[] snapshot = sync.Capture(server);

        var client = new World();
        sync.Apply(client, snapshot);

        Assert.True(client.IsAlive(a));
        Assert.Equal(1, client.Get<Position>(a).X);
        Assert.Equal(4, client.Get<Velocity>(a).Y);
        Assert.True(client.Has<Dead>(a));
        Assert.Equal(9, client.GetSingleton<FrameTime>().Frame);
        Assert.Equal(1, client.EntityCount);
    }

    [Fact]
    public void Apply_ReplacesLiveClientWorld()
    {
        var server = new World();
        var hero = server.Create(new Position { X = 10 }, new Health { Value = 100 });

        var client = new World();
        client.Create(new Position { X = -1 }); // stale local state
        Assert.False(client.IsPristine);

        var sync = new SnapshotSync(CreateSerializer());
        sync.Apply(client, sync.Capture(server));

        Assert.True(client.IsAlive(hero));
        Assert.Equal(10, client.Get<Position>(hero).X);
        Assert.Equal(100, client.Get<Health>(hero).Value);
        Assert.Equal(1, client.EntityCount);
    }

    [Fact]
    public void FramedSnapshot_RoundTripsOverLoopback()
    {
        var server = new World();
        var e = server.Create(new Position { X = 42 });

        var sync = new SnapshotSync(CreateSerializer());
        var serverNet = new LoopbackTransport(1);
        var clientNet = new LoopbackTransport(2);
        LoopbackTransport.Connect(serverNet, clientNet);

        serverNet.Broadcast(sync.CaptureFramed(server, tick: 7));

        Assert.True(clientNet.TryReceive(out var packet));
        var client = new World();
        sync.ApplyFramed(client, packet.Payload, out long tick);

        Assert.Equal(7, tick);
        Assert.Equal(42, client.Get<Position>(e).X);
    }
}
