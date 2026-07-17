namespace Loom.Tests;

public class InspectionTests
{
    [Fact]
    public void ForEachAlive_VisitsAllLiveEntities()
    {
        var world = new World();
        var a = world.Create();
        var b = world.Create();
        world.Destroy(a);

        var seen = new List<Entity>();
        world.ForEachAlive(e => seen.Add(e));

        Assert.Equal(new[] { b }, seen);
        Assert.True(world.TryGetAliveEntity(b.Id, out var resolved));
        Assert.Equal(b, resolved);
        Assert.False(world.TryGetAliveEntity(a.Id, out _));
    }

    [Fact]
    public void ForEachComponent_ReportsDenseSparseSharedAndTags()
    {
        var world = new World();
        var shared = new Material { ShaderId = 7, Roughness = 0.5f };
        var e = world.Create(new Position { X = 1, Y = 2 }, new Dead());
        world.Add(e, new Poisoned { DamagePerTick = 3 });
        world.Add(e, shared);

        var byName = new Dictionary<string, ComponentDebugInfo>();
        world.ForEachComponent(e, info => byName[info.Type.Name] = info);

        Assert.Equal(ComponentStorageKind.Dense, byName["Position"].Kind);
        Assert.Equal(1f, ((Position)byName["Position"].Value!).X);

        Assert.Equal(ComponentStorageKind.Tag, byName["Dead"].Kind);
        Assert.Null(byName["Dead"].Value);

        Assert.Equal(ComponentStorageKind.Sparse, byName["Poisoned"].Kind);
        Assert.Equal(3, ((Poisoned)byName["Poisoned"].Value!).DamagePerTick);

        Assert.Equal(ComponentStorageKind.Shared, byName["Material"].Kind);
        Assert.Equal(7, ((Material)byName["Material"].Value!).ShaderId);
    }

    [Fact]
    public void ForEachSingletonDebug_ListsSingletons()
    {
        var world = new World();
        world.SetSingleton(new FrameTime { Frame = 1, Delta = 0.016f });

        var found = new List<(Type Type, object Value)>();
        world.ForEachSingletonDebug((t, v) => found.Add((t, v)));

        Assert.Single(found);
        Assert.Equal(typeof(FrameTime), found[0].Type);
        Assert.Equal(0.016f, ((FrameTime)found[0].Value).Delta);
    }

    [Fact]
    public void TrySetComponent_WritesDenseAndSparseValues()
    {
        var world = new World();
        var e = world.Create(new Position { X = 1, Y = 2 });
        world.Add(e, new Poisoned { DamagePerTick = 3 });

        Assert.True(world.TrySetComponent(e, typeof(Position), new Position { X = 9, Y = 8 }));
        Assert.True(world.TrySetComponent(e, typeof(Poisoned), new Poisoned { DamagePerTick = 11 }));
        Assert.Equal(9, world.Get<Position>(e).X);
        Assert.Equal(11, world.Get<Poisoned>(e).DamagePerTick);
        Assert.False(world.TrySetComponent(e, typeof(Velocity), new Velocity()));
    }

    [Fact]
    public void TryGetEntityArchetype_ReportsDenseTypesAndCounts()
    {
        var world = new World();
        var e = world.Create(new Position(), new Velocity());
        Assert.True(world.TryGetEntityArchetype(e, out var info));
        Assert.True(info.EntityCount >= 1);
        Assert.True(info.ChunkCount >= 1);
        Assert.Contains(typeof(Position), info.ComponentTypes);
        Assert.Contains(typeof(Velocity), info.ComponentTypes);

        int archetypes = 0;
        world.ForEachArchetype(_ => archetypes++);
        Assert.True(archetypes >= 2); // empty + at least one data archetype
    }
}
