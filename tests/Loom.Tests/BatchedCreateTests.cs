namespace Loom.Tests;

public class BatchedCreateTests
{
    [Fact]
    public void CreateWithOneComponent_SetsValue()
    {
        var world = new World();

        var e = world.Create(new Position { X = 5, Y = 6 });

        Assert.True(world.Has<Position>(e));
        Assert.Equal(5, world.Get<Position>(e).X);
        Assert.Equal(6, world.Get<Position>(e).Y);
    }

    [Fact]
    public void CreateWithTwoComponents_SetsBothValues()
    {
        var world = new World();

        var e = world.Create(new Position { X = 1, Y = 2 }, new Velocity { X = 3, Y = 4 });

        Assert.True(world.Has<Position>(e));
        Assert.True(world.Has<Velocity>(e));
        Assert.Equal(1, world.Get<Position>(e).X);
        Assert.Equal(3, world.Get<Velocity>(e).X);
    }

    [Fact]
    public void CreateWithTwoComponents_SharesArchetypeGraphWithIncrementalAdd()
    {
        // Create<T1,T2> walks the same cached add-edges Add<T>() uses (empty -> {Position} ->
        // {Position, Velocity}), so both styles end up with the same three archetype-graph nodes —
        // but unlike Create() + Add<Position>() + Add<Velocity>(), which really does move the
        // entity's row from {Position} into {Position, Velocity}, a batched Create never places an
        // entity in the intermediate {Position}-only archetype at all: it's only ever touched as a
        // graph node while resolving the final archetype, so there's no row copy to avoid twice.
        var incremental = new World();
        var e = incremental.Create();
        incremental.Add(e, new Position());
        incremental.Add(e, new Velocity());
        Assert.Equal(3, incremental.ArchetypeCount);

        var batched = new World();
        batched.Create(new Position(), new Velocity());
        Assert.Equal(3, batched.ArchetypeCount);
    }

    [Fact]
    public void CreateWithMixedDenseAndSparseComponents_RoutesEachCorrectly()
    {
        var world = new World();

        var e = world.Create(new Position { X = 9 }, new Poisoned { DamagePerTick = 2 });

        Assert.True(world.Has<Position>(e));
        Assert.True(world.Has<Poisoned>(e));
        Assert.Equal(9, world.Get<Position>(e).X);
        Assert.Equal(2, world.Get<Poisoned>(e).DamagePerTick);

        // The sparse component must not have created an extra archetype: only the empty archetype
        // and the Position-only archetype should exist (Poisoned never touches the archetype graph).
        Assert.Equal(2, world.ArchetypeCount);
    }

    [Fact]
    public void CreateWithOneSparseComponent_DoesNotCreateNewArchetype()
    {
        var world = new World();
        var baseline = world.ArchetypeCount;

        var e = world.Create(new Poisoned { DamagePerTick = 1 });

        Assert.True(world.Has<Poisoned>(e));
        Assert.Equal(baseline, world.ArchetypeCount);
    }

    [Fact]
    public void CreatedEntities_AreIndependent()
    {
        var world = new World();

        var a = world.Create(new Position { X = 1 }, new Velocity { X = 10 });
        var b = world.Create(new Position { X = 2 }, new Velocity { X = 20 });

        Assert.Equal(1, world.Get<Position>(a).X);
        Assert.Equal(2, world.Get<Position>(b).X);
        Assert.Equal(10, world.Get<Velocity>(a).X);
        Assert.Equal(20, world.Get<Velocity>(b).X);
    }

    [Fact]
    public void CreateWithThreeComponents_SetsAllValues()
    {
        var world = new World();

        var e = world.Create(
            new Position { X = 1, Y = 2 },
            new Velocity { X = 3, Y = 4 },
            new Health { Value = 50 });

        Assert.Equal(1, world.Get<Position>(e).X);
        Assert.Equal(3, world.Get<Velocity>(e).X);
        Assert.Equal(50, world.Get<Health>(e).Value);
    }

    [Fact]
    public void CreateWithFourComponents_SetsAllValues()
    {
        var world = new World();

        var e = world.Create(
            new Position { X = 1 },
            new Velocity { X = 2 },
            new Health { Value = 3 },
            new Dead());

        Assert.Equal(1, world.Get<Position>(e).X);
        Assert.Equal(2, world.Get<Velocity>(e).X);
        Assert.Equal(3, world.Get<Health>(e).Value);
        Assert.True(world.Has<Dead>(e));
    }

    [Fact]
    public void CreateMany_FillsDestinationWithAliveEmptyEntities()
    {
        var world = new World();
        var entities = new Entity[5];

        world.CreateMany(entities);

        Assert.Equal(5, world.EntityCount);
        foreach (var e in entities)
        {
            Assert.True(world.IsAlive(e));
            Assert.False(world.Has<Position>(e));
        }
    }

    [Fact]
    public void CreateManyWithTwoComponents_SetsSameValuesOnEveryEntity()
    {
        var world = new World();
        var entities = new Entity[3];

        world.CreateMany(entities, new Position { X = 7 }, new Velocity { X = 9 });

        Assert.Equal(3, world.EntityCount);
        foreach (var e in entities)
        {
            Assert.Equal(7, world.Get<Position>(e).X);
            Assert.Equal(9, world.Get<Velocity>(e).X);
        }
    }

    [Fact]
    public void DestroyMany_RemovesEveryEntity()
    {
        var world = new World();
        var entities = new Entity[4];
        world.CreateMany(entities, new Position());

        world.DestroyMany(entities);

        Assert.Equal(0, world.EntityCount);
        foreach (var e in entities)
            Assert.False(world.IsAlive(e));
    }
}
