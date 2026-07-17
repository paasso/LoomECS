namespace Loom.Tests;

public class PrefabTests
{
    [Fact]
    public void Instantiate_AppliesAllComponents()
    {
        var prefab = new EntityPrefab()
            .Add(new Position { X = 1, Y = 2 })
            .Add(new Velocity { X = 3, Y = 4 })
            .Add(new Dead())
            .Add(new Poisoned { DamagePerTick = 5 });

        var world = new World();
        var e = world.Instantiate(prefab);

        Assert.Equal(1, world.Get<Position>(e).X);
        Assert.Equal(3, world.Get<Velocity>(e).X);
        Assert.True(world.Has<Dead>(e));
        Assert.Equal(5, world.Get<Poisoned>(e).DamagePerTick);
    }

    [Fact]
    public void FromEntity_RoundTripsValues()
    {
        var world = new World();
        var source = world.Create(new Position { X = 9, Y = 8 }, new Velocity { X = 1 });
        world.Add(source, new Poisoned { DamagePerTick = 2 });
        world.Add(source, new Burning());

        var prefab = EntityPrefab.FromEntity(world, source);
        var copy = prefab.Instantiate(world);

        Assert.NotEqual(source, copy);
        Assert.Equal(9, world.Get<Position>(copy).X);
        Assert.Equal(8, world.Get<Position>(copy).Y);
        Assert.Equal(1, world.Get<Velocity>(copy).X);
        Assert.Equal(2, world.Get<Poisoned>(copy).DamagePerTick);
        Assert.True(world.Has<Burning>(copy));
    }

    [Fact]
    public void InstantiateMany_FillsDestination()
    {
        var prefab = new EntityPrefab().Add(new Position { X = 7 });
        var world = new World();
        var entities = new Entity[3];

        prefab.InstantiateMany(world, entities);

        Assert.Equal(3, world.EntityCount);
        foreach (var e in entities)
            Assert.Equal(7, world.Get<Position>(e).X);
    }

    [Fact]
    public void FromEntity_DoesNotCaptureFatherLinks()
    {
        var world = new World();
        var father = world.Create();
        var child = world.Create(new Position());
        world.SetFather(child, father);

        var copy = EntityPrefab.FromEntity(world, child).Instantiate(world);

        Assert.True(world.GetFather(copy).IsNull);
    }
}
