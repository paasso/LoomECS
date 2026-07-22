using Loom;
using Loom.Internal;
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
        server.ClearComponentChanges();

        server.Set(a, new Position { X = 9, Y = 8 });
        server.Set(a, new Velocity { X = 1, Y = 1 });

        Assert.True(deltas.TryCapture(server, out byte[] delta));
        Assert.True(delta.Length > 0);
        Assert.Equal((byte)'T', delta[3]); // LDLT
        // FormatVersion 3 at offset 4
        Assert.Equal(3, BitConverter.ToInt32(delta, 4));
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

        var deltas = new DeltaSync().Register<Health>().Register<Position>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();

        server.Add(a, new Health { Value = 50 });
        deltas.Apply(client, deltas.Capture(server));
        Assert.Equal(50, client.Get<Health>(a).Value);

        server.ClearComponentChanges();
        Assert.True(server.Remove<Health>(a));
        deltas.Apply(client, deltas.Capture(server));
        Assert.False(client.Has<Health>(a));
    }

    [Fact]
    public void CaptureApply_SpawnsEntitiesOnClient()
    {
        var serializer = new WorldSerializer()
            .Register<Position>()
            .Register<Velocity>();

        var server = new World();
        var client = new World();
        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server)); // empty join

        var deltas = new DeltaSync().Register<Position>().Register<Velocity>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();

        var spawned = server.Create(new Position { X = 4, Y = 5 }, new Velocity { X = 1, Y = 2 });
        Assert.True(deltas.TryCapture(server, out byte[] delta));
        deltas.Apply(client, delta);

        Assert.True(client.IsAlive(spawned));
        Assert.Equal(4, client.Get<Position>(spawned).X);
        Assert.Equal(2, client.Get<Velocity>(spawned).Y);
        Assert.Equal(1, client.EntityCount);
    }

    [Fact]
    public void CaptureApply_DespawnsEntitiesOnClient()
    {
        var serializer = new WorldSerializer()
            .Register<Position>();

        var server = new World();
        var client = new World();
        var a = server.Create(new Position { X = 1 });
        var b = server.Create(new Position { X = 2 });

        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server));

        var deltas = new DeltaSync().Register<Position>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();

        server.Destroy(b);
        Assert.True(deltas.TryCapture(server, out byte[] delta));
        deltas.Apply(client, delta);

        Assert.True(client.IsAlive(a));
        Assert.False(client.IsAlive(b));
        Assert.Equal(1, client.EntityCount);
    }

    [Fact]
    public void CaptureApply_MixesSpawnDespawnAndDirtyUpdates()
    {
        var serializer = new WorldSerializer()
            .Register<Position>()
            .Register<Velocity>();

        var server = new World();
        var client = new World();
        var existing = server.Create(new Position { X = 1, Y = 1 }, new Velocity { X = 0, Y = 0 });
        var doomed = server.Create(new Position { X = 9 });

        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server));

        var deltas = new DeltaSync().Register<Position>().Register<Velocity>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();

        server.Set(existing, new Position { X = 10, Y = 20 });
        server.Destroy(doomed);
        var newborn = server.Create(new Position { X = 7, Y = 8 }, new Velocity { X = 3, Y = 4 });

        deltas.Apply(client, deltas.Capture(server));

        Assert.Equal(10, client.Get<Position>(existing).X);
        Assert.Equal(20, client.Get<Position>(existing).Y);
        Assert.False(client.IsAlive(doomed));
        Assert.True(client.IsAlive(newborn));
        Assert.Equal(7, client.Get<Position>(newborn).X);
        Assert.Equal(3, client.Get<Velocity>(newborn).X);
        Assert.Equal(2, client.EntityCount);
    }

    [Fact]
    public void CaptureApply_RecyclesEntityIdViaDespawnThenSpawn()
    {
        var serializer = new WorldSerializer().Register<Position>();
        var server = new World();
        var client = new World();
        var first = server.Create(new Position { X = 1 });

        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server));

        var deltas = new DeltaSync().Register<Position>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();

        server.Destroy(first);
        var recycled = server.Create(new Position { X = 42 });
        Assert.Equal(first.Id, recycled.Id);
        Assert.NotEqual(first.Version, recycled.Version);

        deltas.Apply(client, deltas.Capture(server));
        Assert.False(client.IsAlive(first));
        Assert.True(client.IsAlive(recycled));
        Assert.Equal(42, client.Get<Position>(recycled).X);
    }

    [Fact]
    public void TryCapture_ReturnsFalseWhenNoDirtyOps()
    {
        var server = new World();
        var e = server.Create(new Position { X = 1 });
        var deltas = new DeltaSync().Register<Position>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();

        Assert.False(deltas.TryCapture(server, out byte[] delta));
        Assert.Empty(delta);
        Assert.Empty(deltas.Capture(server));
        Assert.Empty(deltas.CaptureFramed(server, tick: 1));

        // Empty apply is a no-op
        deltas.Apply(server, Array.Empty<byte>());
        Assert.Equal(1, server.Get<Position>(e).X);
    }

    [Fact]
    public void WireFormat_UsesDeterministicHashNotFullName()
    {
        var server = new World();
        var client = new World();
        var a = server.Create(new Position { X = 1, Y = 2 });

        var snapshots = new SnapshotSync(new WorldSerializer().Register<Position>());
        snapshots.Apply(client, snapshots.Capture(server));

        var deltas = new DeltaSync().Register<Position>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();
        server.Set(a, new Position { X = 3, Y = 4 });

        byte[] delta = deltas.Capture(server);
        Assert.Equal(3, BitConverter.ToInt32(delta, 4));
        int despawnCount = BitConverter.ToInt32(delta, 8);
        int spawnCount = BitConverter.ToInt32(delta, 12);
        Assert.Equal(0, despawnCount);
        Assert.Equal(0, spawnCount);
        int typeCount = BitConverter.ToInt32(delta, 16);
        Assert.Equal(1, typeCount);
        int typeHash = BitConverter.ToInt32(delta, 20);
        Assert.Equal(typeof(Position).ComputeDeterministicHash(), typeHash);

        // FullName must not appear as a length-prefixed BinaryWriter string in the payload
        byte[] fullNameUtf8 = System.Text.Encoding.UTF8.GetBytes(typeof(Position).FullName!);
        Assert.False(ContainsSequence(delta, fullNameUtf8));

        deltas.Apply(client, delta);
        Assert.Equal(3, client.Get<Position>(a).X);
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
            return false;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return true;
        }
        return false;
    }

    [Fact]
    public void BrotliRoundTrip_CompressesLargePayload()
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
        server.ClearComponentChanges();

        for (int id = 1; id <= 32; id++)
        {
            Assert.True(server.TryGetAliveEntity(id, out var e));
            server.Set(e, new Position { X = id + 10, Y = id });
            server.Set(e, new Velocity { X = 2, Y = 3 });
        }

        byte[] raw = rawDeltas.Capture(server);
        byte[] compressed = brotliDeltas.Capture(server);

        Assert.Equal((byte)'T', raw[3]);
        Assert.True(raw.Length >= DeltaSync.DefaultCompressThreshold);
        Assert.Equal((byte)'B', compressed[3]);
        Assert.True(compressed.Length < raw.Length);

        brotliDeltas.Apply(client, compressed);
        Assert.True(client.TryGetAliveEntity(1, out var first));
        Assert.Equal(11, client.Get<Position>(first).X);
        Assert.Equal(2, client.Get<Velocity>(first).X);
    }

    [Fact]
    public void AdaptiveCompression_SmallPayloadStaysUncompressed()
    {
        var serializer = new WorldSerializer().Register<Position>();
        var server = new World();
        var client = new World();
        var a = server.Create(new Position { X = 1 });

        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server));

        var deltas = new DeltaSync(compress: true).Register<Position>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();
        server.Set(a, new Position { X = 2 });

        byte[] delta = deltas.Capture(server);
        Assert.True(delta.Length > 0);
        Assert.True(delta.Length < DeltaSync.DefaultCompressThreshold);
        Assert.Equal((byte)'T', delta[3]); // still LDLT despite compress:true

        deltas.Apply(client, delta);
        Assert.Equal(2, client.Get<Position>(a).X);
    }

    [Fact]
    public void AdaptiveCompression_RespectsCustomThreshold()
    {
        var serializer = new WorldSerializer().Register<Position>();
        var server = new World();
        var client = new World();
        var a = server.Create(new Position { X = 1 });

        var snapshots = new SnapshotSync(serializer);
        snapshots.Apply(client, snapshots.Capture(server));

        var deltas = new DeltaSync(compress: true, compressThreshold: 1_000_000).Register<Position>();
        deltas.EnableTracking(server);
        server.ClearComponentChanges();
        server.Set(a, new Position { X = 3 });

        byte[] delta = deltas.Capture(server);
        Assert.Equal((byte)'T', delta[3]);
        deltas.Apply(client, delta);
        Assert.Equal(3, client.Get<Position>(a).X);
    }
}

public class NetFloat3QuantizeTests
{
    [Fact]
    public void Int16_RoundTripsWithinCentimetreTolerance()
    {
        float x = 12.34f, y = -5.67f, z = 0.089f;
        byte[] bytes = NetFloat3Quantize.ToInt16Bytes(x, y, z);
        Assert.Equal(6, bytes.Length);

        NetFloat3Quantize.ReadInt16(bytes, out float rx, out float ry, out float rz);
        Assert.Equal(x, rx, precision: 2);
        Assert.Equal(y, ry, precision: 2);
        Assert.Equal(z, rz, precision: 2);
    }

#if NET5_0_OR_GREATER
    [Fact]
    public void Half_RoundTripsApproximately()
    {
        float x = 100.5f, y = -0.25f, z = 3.125f;
        byte[] bytes = NetFloat3Quantize.ToHalfBytes(x, y, z);
        Assert.Equal(6, bytes.Length);

        NetFloat3Quantize.ReadHalf(bytes, out float rx, out float ry, out float rz);
        Assert.Equal(x, rx, precision: 1);
        Assert.Equal(y, ry, precision: 2);
        Assert.Equal(z, rz, precision: 2);
    }
#endif
}
