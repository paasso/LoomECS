namespace Loom.Tests;

public class SerializationTests
{
    private static WorldSerializer CreateSerializer() =>
        new WorldSerializer()
            .Register<Position>()
            .Register<Velocity>()
            .Register<Health>()
            .Register<Dead>()
            .Register<Poisoned>()
            .Register<Burning>();

    [Fact]
    public void RoundTrip_PreservesDenseSparseTagsAndValues()
    {
        var world = new World();
        var a = world.Create(new Position { X = 1, Y = 2 }, new Velocity { X = 3, Y = 4 });
        world.Add(a, new Dead());
        world.Add(a, new Poisoned { DamagePerTick = 7 });
        world.Add(a, new Burning());

        var serializer = CreateSerializer();
        string json = serializer.SaveToJson(world);

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        Assert.True(loaded.IsAlive(a));
        Assert.Equal(1, loaded.Get<Position>(a).X);
        Assert.Equal(2, loaded.Get<Position>(a).Y);
        Assert.Equal(3, loaded.Get<Velocity>(a).X);
        Assert.Equal(7, loaded.Get<Poisoned>(a).DamagePerTick);
        Assert.True(loaded.Has<Dead>(a));
        Assert.True(loaded.Has<Burning>(a));
        Assert.Equal(1, loaded.EntityCount);
    }

    [Fact]
    public void RoundTrip_PreservesFatherChildLinks()
    {
        var world = new World();
        var father = world.Create(new Position { X = 10 });
        var child = world.Create(new Position { X = 20 });
        world.SetFather(child, father);

        var serializer = CreateSerializer();
        string json = serializer.SaveToJson(world);

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        Assert.Equal(father, loaded.GetFather(child));
        Assert.True(loaded.HasChildren(father));
        var children = new List<Entity>();
        foreach (var c in loaded.GetChildren(father))
            children.Add(c);
        Assert.Equal(new[] { child }, children);
    }

    [Fact]
    public void RoundTrip_PreservesEntityVersionAfterRecycle()
    {
        var world = new World();
        var first = world.Create(new Position { X = 1 });
        world.Destroy(first);
        var second = world.Create(new Position { X = 9 });
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Version + 1, second.Version);

        var serializer = CreateSerializer();
        string json = serializer.SaveToJson(world);

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        Assert.False(loaded.IsAlive(first));
        Assert.True(loaded.IsAlive(second));
        Assert.Equal(9, loaded.Get<Position>(second).X);
    }

    [Fact]
    public void LoadFromJson_RequiresPristineWorld()
    {
        var serializer = CreateSerializer();
        var source = new World();
        source.Create(new Position());
        string json = serializer.SaveToJson(source);

        var dirty = new World();
        dirty.Create();

        Assert.Throws<InvalidOperationException>(() => serializer.LoadFromJson(dirty, json));
    }

    [Fact]
    public void Save_ThrowsWhenComponentTypeNotRegistered()
    {
        var world = new World();
        world.Create(new Position());

        var serializer = new WorldSerializer(); // Position not registered
        Assert.Throws<InvalidOperationException>(() => serializer.SaveToJson(world));
    }

    [Fact]
    public void Load_ThrowsOnUnknownComponentTypeName()
    {
        var serializer = CreateSerializer();
        const string json = """
            {
              "FormatVersion": 1,
              "Entities": [
                {
                  "Id": 1,
                  "Version": 0,
                  "Dense": [ { "Type": "Definitely.Not.Registered" } ]
                }
              ]
            }
            """;

        Assert.Throws<InvalidOperationException>(() => serializer.LoadFromJson(new World(), json));
    }

    [Fact]
    public void Load_ChainsComponentMigrationsAcrossVersions()
    {
        // v1: Hits → v2: Value = Hits*10 → v3: Value = Value+1
        var serializer = new WorldSerializer()
            .Register<Health>(version: 3)
            .RegisterMigration<Health>(fromVersion: 1, data =>
            {
                int hits = data.GetProperty("Hits").GetInt32();
                return System.Text.Json.JsonSerializer.SerializeToElement(new { Value = hits * 10 });
            })
            .RegisterMigration<Health>(fromVersion: 2, data =>
            {
                int value = data.GetProperty("Value").GetInt32();
                return new Health { Value = value + 1 };
            });

        string healthName = typeof(Health).FullName!;
        string json = $$"""
            {
              "FormatVersion": 1,
              "Entities": [
                {
                  "Id": 1,
                  "Version": 0,
                  "Dense": [
                    {
                      "Type": "{{healthName}}",
                      "Version": 1,
                      "Data": { "Hits": 4 }
                    }
                  ]
                }
              ]
            }
            """;

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);
        Assert.Equal(41, loaded.Get<Health>(new Entity(1, 0)).Value);
    }

    [Fact]
    public void Load_MigratesComponentDataVersion()
    {
        // v1 Health used "Hits"; v2 uses "Value".
        var serializer = new WorldSerializer()
            .Register<Health>(version: 2)
            .RegisterMigration<Health>(fromVersion: 1, data =>
            {
                int hits = data.GetProperty("Hits").GetInt32();
                return new Health { Value = hits * 10 };
            });

        string healthName = typeof(Health).FullName!;
        string json = $$"""
            {
              "FormatVersion": 1,
              "Entities": [
                {
                  "Id": 1,
                  "Version": 0,
                  "Dense": [
                    {
                      "Type": "{{healthName}}",
                      "Version": 1,
                      "Data": { "Hits": 4 }
                    }
                  ]
                }
              ]
            }
            """;

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        var entity = new Entity(1, 0);
        Assert.Equal(40, loaded.Get<Health>(entity).Value);
    }

    [Fact]
    public void Load_ResolvesTypeAlias()
    {
        string current = typeof(Position).FullName!;
        var serializer = new WorldSerializer()
            .Register<Position>()
            .RegisterTypeAlias("OldGame.Pos", current);

        const string json = """
            {
              "FormatVersion": 1,
              "Entities": [
                {
                  "Id": 1,
                  "Version": 0,
                  "Dense": [
                    {
                      "Type": "OldGame.Pos",
                      "Version": 1,
                      "Data": { "X": 5, "Y": 6 }
                    }
                  ]
                }
              ]
            }
            """;

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        var entity = new Entity(1, 0);
        Assert.Equal(5, loaded.Get<Position>(entity).X);
        Assert.Equal(6, loaded.Get<Position>(entity).Y);
    }

    [Fact]
    public void Load_RunsFormatMigration()
    {
        // Older docs stored entities under "Units" instead of "Entities".
        var serializer = new WorldSerializer()
            .Register<Position>()
            .RegisterFormatMigration(fromVersion: 0, node =>
            {
                node["Entities"] = node["Units"]!.DeepClone();
                node.AsObject().Remove("Units");
                return node;
            });

        string positionName = typeof(Position).FullName!;
        string json = $$"""
            {
              "FormatVersion": 0,
              "Units": [
                {
                  "Id": 1,
                  "Version": 0,
                  "Dense": [
                    {
                      "Type": "{{positionName}}",
                      "Version": 1,
                      "Data": { "X": 3, "Y": 4 }
                    }
                  ]
                }
              ]
            }
            """;

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        var entity = new Entity(1, 0);
        Assert.Equal(3, loaded.Get<Position>(entity).X);
        Assert.Equal(4, loaded.Get<Position>(entity).Y);
    }

    [Fact]
    public void Load_ThrowsWhenComponentMigrationMissing()
    {
        var serializer = new WorldSerializer().Register<Health>(version: 2);
        string healthName = typeof(Health).FullName!;
        string json = $$"""
            {
              "FormatVersion": 1,
              "Entities": [
                {
                  "Id": 1,
                  "Version": 0,
                  "Dense": [
                    {
                      "Type": "{{healthName}}",
                      "Version": 1,
                      "Data": { "Value": 1 }
                    }
                  ]
                }
              ]
            }
            """;

        Assert.Throws<InvalidOperationException>(() => serializer.LoadFromJson(new World(), json));
    }

    [Fact]
    public void RoundTrip_PreservesRegisteredSingletons()
    {
        var world = new World();
        world.Create(new Position { X = 1 });
        world.SetSingleton(new FrameTime { Frame = 42, Delta = 0.016f });

        var serializer = CreateSerializer().RegisterSingleton<FrameTime>();
        string json = serializer.SaveToJson(world);

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        Assert.True(loaded.HasSingleton<FrameTime>());
        ref var time = ref loaded.GetSingleton<FrameTime>();
        Assert.Equal(42, time.Frame);
        Assert.Equal(0.016f, time.Delta);
    }

    [Fact]
    public void Save_ThrowsWhenSingletonTypeNotRegistered()
    {
        var world = new World();
        world.SetSingleton(new FrameTime { Frame = 1 });

        var serializer = CreateSerializer(); // FrameTime not registered as singleton
        Assert.Throws<InvalidOperationException>(() => serializer.SaveToJson(world));
    }

    [Fact]
    public void Load_MigratesSingletonDataVersion()
    {
        var serializer = new WorldSerializer()
            .RegisterSingleton<FrameTime>(version: 2)
            .RegisterMigration<FrameTime>(fromVersion: 1, data =>
            {
                int ticks = data.GetProperty("Ticks").GetInt32();
                return new FrameTime { Frame = ticks, Delta = 0.01f };
            });

        string name = typeof(FrameTime).FullName!;
        string json = $$"""
            {
              "FormatVersion": 1,
              "Entities": [],
              "Singletons": [
                {
                  "Type": "{{name}}",
                  "Version": 1,
                  "Data": { "Ticks": 9 }
                }
              ]
            }
            """;

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        Assert.Equal(9, loaded.GetSingleton<FrameTime>().Frame);
        Assert.Equal(0.01f, loaded.GetSingleton<FrameTime>().Delta);
    }

    [Fact]
    public void Load_AllowsSnapshotWithoutSingletonsSection()
    {
        var serializer = CreateSerializer().RegisterSingleton<FrameTime>();
        const string json = """
            {
              "FormatVersion": 1,
              "Entities": []
            }
            """;

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);
        Assert.False(loaded.HasSingleton<FrameTime>());
    }

    [Fact]
    public void MemoryPackRoundTrip_PreservesDenseSparseTagsAndValues()
    {
        var world = new World();
        var a = world.Create(new Position { X = 1, Y = 2 }, new Velocity { X = 3, Y = 4 });
        world.Add(a, new Dead());
        world.Add(a, new Poisoned { DamagePerTick = 7 });
        world.Add(a, new Burning());
        world.SetSingleton(new FrameTime { Frame = 3, Delta = 0.016f });

        var serializer = CreateSerializer().RegisterSingleton<FrameTime>();
        byte[] bytes = serializer.SaveToMemoryPack(world);

        Assert.Equal((byte)'L', bytes[0]);
        Assert.Equal((byte)'C', bytes[1]);
        Assert.Equal((byte)'M', bytes[2]);
        Assert.Equal((byte)'P', bytes[3]);

        var loaded = new World();
        serializer.LoadFromMemoryPack(loaded, bytes);

        Assert.True(loaded.IsAlive(a));
        Assert.Equal(1, loaded.Get<Position>(a).X);
        Assert.Equal(4, loaded.Get<Velocity>(a).Y);
        Assert.Equal(7, loaded.Get<Poisoned>(a).DamagePerTick);
        Assert.True(loaded.Has<Dead>(a));
        Assert.True(loaded.Has<Burning>(a));
        Assert.Equal(3, loaded.GetSingleton<FrameTime>().Frame);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void MemoryPackBrotliRoundTrip_CompressesAndRestores()
    {
        var world = new World();
        for (int i = 0; i < 200; i++)
            world.Create(new Position { X = i, Y = i }, new Velocity { X = 1, Y = 1 });

        var serializer = CreateSerializer();
        byte[] raw = serializer.SaveToMemoryPack(world, compress: false);
        byte[] compressed = serializer.SaveToMemoryPack(world, compress: true);

        Assert.Equal((byte)'B', compressed[3]);
        Assert.True(compressed.Length < raw.Length);

        var loaded = new World();
        serializer.LoadFromMemoryPack(loaded, compressed);
        Assert.Equal(200, loaded.EntityCount);
        Assert.Equal(42, loaded.Get<Position>(new Entity(43, 0)).X);
    }

    [Fact]
    public void LoadFromMemoryPack_RequiresMatchingComponentVersion()
    {
        var serializer = new WorldSerializer().Register<Health>(version: 2);
        string healthName = typeof(Health).FullName!;
        using var ms = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(new byte[] { (byte)'L', (byte)'C', (byte)'M', (byte)'P' });
            w.Write(1);
            w.Write(1);
            w.Write(1);
            w.Write(0);
            w.Write(-1);
            w.Write(1);
            w.Write(healthName);
            w.Write(1);
            w.Write(4);
            w.Write(new byte[] { 1, 0, 0, 0 });
            w.Write(0);
            w.Write(0);
        }

        Assert.Throws<InvalidOperationException>(() =>
            serializer.LoadFromMemoryPack(new World(), ms.ToArray()));
    }

    [Fact]
    public void LoadFromMemoryPack_ThrowsOnBadMagic()
    {
        var serializer = CreateSerializer();
        Assert.Throws<InvalidOperationException>(() =>
            serializer.LoadFromMemoryPack(new World(), new byte[] { 1, 2, 3, 4, 0, 0, 0, 1 }));
    }
}
