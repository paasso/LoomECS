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
}
