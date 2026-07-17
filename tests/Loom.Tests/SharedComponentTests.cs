namespace Loom.Tests;

public class SharedComponentTests
{
    [Fact]
    public void IdenticalValues_ShareOneInstance()
    {
        var world = new World();
        var mat = new Material { ShaderId = 3, Roughness = 0.5f };
        var a = world.Create(mat);
        var b = world.Create(mat);
        var c = world.Create(new Material { ShaderId = 9, Roughness = 0.1f });

        Assert.Equal(2, world.SharedInstanceCount<Material>());
        Assert.True(world.Has<Material>(a));
        Assert.Equal(3, world.Get<Material>(b).ShaderId);
        Assert.Equal(9, world.Get<Material>(c).ShaderId);
    }

    [Fact]
    public void MutatingGet_UpdatesAllHolders()
    {
        var world = new World();
        var mat = new Material { ShaderId = 1, Roughness = 0.2f };
        var a = world.Create(mat);
        var b = world.Create(mat);

        world.Get<Material>(a).Roughness = 0.9f;

        Assert.Equal(0.9f, world.Get<Material>(b).Roughness);
        Assert.Equal(1, world.SharedInstanceCount<Material>());
    }

    [Fact]
    public void RemoveAndDestroy_ReleaseInternedSlots()
    {
        var world = new World();
        var mat = new Material { ShaderId = 2, Roughness = 0.3f };
        var a = world.Create(mat);
        var b = world.Create(mat);
        Assert.Equal(1, world.SharedInstanceCount<Material>());

        Assert.True(world.Remove<Material>(a));
        Assert.Equal(1, world.SharedInstanceCount<Material>());

        world.Destroy(b);
        Assert.Equal(0, world.SharedInstanceCount<Material>());
    }

    [Fact]
    public void QueryEach_ResolvesSharedRefs()
    {
        var world = new World();
        var mat = new Material { ShaderId = 4, Roughness = 0f };
        var a = world.Create(new Position { X = 1 }, mat);
        world.Create(new Position { X = 2 }, mat);

        int seen = 0;
        world.Query().Each<Position, Material>((Entity _, ref Position p, ref Material m) =>
        {
            seen++;
            m.Roughness = p.X;
        });

        Assert.Equal(2, seen);
        // Last writer wins on the single shared instance.
        Assert.Equal(2f, world.Get<Material>(a).Roughness);
    }

    [Fact]
    public void Add_DoesNotMoveArchetype()
    {
        var world = new World();
        var e = world.Create(new Position { X = 1 });
        var before = world.GetArchetype(e);

        world.Add(e, new Material { ShaderId = 1, Roughness = 0 });
        Assert.Same(before, world.GetArchetype(e));
    }

    [Fact]
    public void Serialization_RoundTripsSharedValues()
    {
        var world = new World();
        var mat = new Material { ShaderId = 7, Roughness = 0.25f };
        var a = world.Create(new Position { X = 1 }, mat);
        world.Create(new Position { X = 2 }, mat);

        var serializer = new WorldSerializer()
            .Register<Position>()
            .Register<Material>();
        string json = serializer.SaveToJson(world);

        var loaded = new World();
        serializer.LoadFromJson(loaded, json);

        Assert.Equal(7, loaded.Get<Material>(a).ShaderId);
        Assert.Equal(1, loaded.SharedInstanceCount<Material>());
    }
}
