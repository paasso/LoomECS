using Loom;
using Loom.Net;

namespace Loom.Net.Tests;

public class DeltaSyncTests
{
    [Fact]
    public void CaptureApply_SyncsChangedComponents()
    {
        var serializer = new WorldSerializer()
            .Register<Position>()
            .Register<Velocity>();

        var server = new World();
        var client = new World();
        var a = server.Create(new Position { X = 1, Y = 2 }, new Velocity { X = 0, Y = 0 });

        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server));

        var deltas = new DeltaSync().Register<Position>().Register<Velocity>();
        deltas.EnableTracking(server);

        server.Set(a, new Position { X = 9, Y = 8 });
        server.Set(a, new Velocity { X = 1, Y = 1 });

        byte[] delta = deltas.Capture(server);
        deltas.Apply(client, delta);

        Assert.Equal(9, client.Get<Position>(a).X);
        Assert.Equal(8, client.Get<Position>(a).Y);
        Assert.Equal(1, client.Get<Velocity>(a).X);
    }

    [Fact]
    public void CaptureApply_HandlesAddedAndRemovedComponents()
    {
        var serializer = new WorldSerializer()
            .Register<Position>()
            .Register<Health>();

        var server = new World();
        var client = new World();
        var a = server.Create(new Position { X = 1 });

        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server));

        var deltas = new DeltaSync().Register<Health>();
        deltas.EnableTracking(server);

        server.Add(a, new Health { Value = 50 });
        deltas.Apply(client, deltas.Capture(server));
        Assert.Equal(50, client.Get<Health>(a).Value);

        server.ClearComponentChanges();
        Assert.True(server.Remove<Health>(a));
        deltas.Apply(client, deltas.Capture(server));
        Assert.False(client.Has<Health>(a));
    }

    [Fact]
    public void BrotliRoundTrip_CompressesAndRestores()
    {
        var serializer = new WorldSerializer()
            .Register<Position>()
            .Register<Velocity>();

        var server = new World();
        var client = new World();
        for (int i = 0; i < 32; i++)
            server.Create(new Position { X = i, Y = i * 2 }, new Velocity { X = 1, Y = 0 });

        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server));

        var rawDeltas = new DeltaSync(compress: false).Register<Position>().Register<Velocity>();
        var brotliDeltas = new DeltaSync(compress: true).Register<Position>().Register<Velocity>();
        rawDeltas.EnableTracking(server);
        // Tracking is world-side; one EnableTracking is enough. Register types on both syncers.

        for (int id = 1; id <= 32; id++)
        {
            Assert.True(server.TryGetAliveEntity(id, out var e));
            server.Set(e, new Position { X = id + 10, Y = id });
            server.Set(e, new Velocity { X = 2, Y = 3 });
        }

        byte[] raw = rawDeltas.Capture(server);
        byte[] compressed = brotliDeltas.Capture(server);

        Assert.Equal((byte)'T', raw[3]);
        Assert.Equal((byte)'B', compressed[3]);
        Assert.True(compressed.Length < raw.Length);

        brotliDeltas.Apply(client, compressed);
        Assert.True(client.TryGetAliveEntity(1, out var first));
        Assert.Equal(11, client.Get<Position>(first).X);
        Assert.Equal(2, client.Get<Velocity>(first).X);
    }
}
