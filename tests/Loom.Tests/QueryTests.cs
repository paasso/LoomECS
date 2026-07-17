namespace Loom.Tests;

public class QueryTests
{
    private static World BuildWorld(out Entity moving, out Entity still, out Entity poisonedMover)
    {
        var world = new World();

        moving = world.Create();
        world.Add(moving, new Position { X = 0, Y = 0 });
        world.Add(moving, new Velocity { X = 1, Y = 1 });

        still = world.Create();
        world.Add(still, new Position { X = 10, Y = 10 });

        poisonedMover = world.Create();
        world.Add(poisonedMover, new Position { X = 0, Y = 0 });
        world.Add(poisonedMover, new Velocity { X = 2, Y = 2 });
        world.Add(poisonedMover, new Poisoned { DamagePerTick = 1 });

        return world;
    }

    [Fact]
    public void With_FiltersByDenseComponent()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        var result = world.Query().With<Velocity>().ToList();

        Assert.Contains(moving, result);
        Assert.Contains(poisonedMover, result);
        Assert.DoesNotContain(still, result);
    }

    [Fact]
    public void Without_ExcludesByDenseComponent()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        var result = world.Query().With<Position>().Without<Velocity>().ToList();

        Assert.Contains(still, result);
        Assert.DoesNotContain(moving, result);
        Assert.DoesNotContain(poisonedMover, result);
    }

    [Fact]
    public void With_SparseComponent_FiltersAcrossArchetypes()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        var result = world.Query().With<Velocity>().With<Poisoned>().ToList();

        Assert.Single(result);
        Assert.Equal(poisonedMover, result[0]);
    }

    [Fact]
    public void Without_SparseComponent_ExcludesMatchingEntity()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        var result = world.Query().With<Velocity>().Without<Poisoned>().ToList();

        Assert.Single(result);
        Assert.Equal(moving, result[0]);
    }

    [Fact]
    public void Each_SingleComponent_VisitsExpectedEntities_AndAllowsMutation()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        int visited = 0;
        world.Query().Each<Position>((Entity e, ref Position p) =>
        {
            visited++;
            p.X += 1;
        });

        Assert.Equal(3, visited);
        Assert.Equal(1, world.Get<Position>(moving).X);
        Assert.Equal(11, world.Get<Position>(still).X);
    }

    [Fact]
    public void Each_TwoComponents_OnlyVisitsEntitiesWithBoth_AndIntegratesMotion()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        int visited = 0;
        world.Query().Each<Position, Velocity>((Entity e, ref Position p, ref Velocity v) =>
        {
            visited++;
            p.X += v.X;
            p.Y += v.Y;
        });

        Assert.Equal(2, visited);
        Assert.Equal(1, world.Get<Position>(moving).X);
        Assert.Equal(2, world.Get<Position>(poisonedMover).X);
        Assert.Equal(10, world.Get<Position>(still).X); // untouched: no Velocity
    }

    [Fact]
    public void Each_WithSparseFilter_RestrictsFastPathIteration()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        var visited = new List<Entity>();
        world.Query().With<Poisoned>().Each<Position>((Entity e, ref Position p) => visited.Add(e));

        Assert.Single(visited);
        Assert.Equal(poisonedMover, visited[0]);
    }

    [Fact]
    public void Each_ThreeComponents_OnlyVisitsEntitiesWithAllThree_AndAllowsMutation()
    {
        var world = new World();

        var complete = world.Create();
        world.Add(complete, new Position { X = 1 });
        world.Add(complete, new Velocity { X = 2 });
        world.Add(complete, new Comp3 { Value = 3 });

        var missingThird = world.Create();
        world.Add(missingThird, new Position());
        world.Add(missingThird, new Velocity());

        int visited = 0;
        world.Query().Each<Position, Velocity, Comp3>((Entity e, ref Position p, ref Velocity v, ref Comp3 c3) =>
        {
            visited++;
            p.X += 10;
            c3.Value += 100;
        });

        Assert.Equal(1, visited);
        Assert.Equal(11, world.Get<Position>(complete).X);
        Assert.Equal(103, world.Get<Comp3>(complete).Value);
    }

    [Fact]
    public void Each_ThreeComponents_WithSparseFilter_RestrictsIteration()
    {
        var world = new World();

        var poisonedComplete = world.Create();
        world.Add(poisonedComplete, new Position());
        world.Add(poisonedComplete, new Velocity());
        world.Add(poisonedComplete, new Comp3());
        world.Add(poisonedComplete, new Poisoned { DamagePerTick = 1 });

        var plainComplete = world.Create();
        world.Add(plainComplete, new Position());
        world.Add(plainComplete, new Velocity());
        world.Add(plainComplete, new Comp3());

        var visited = new List<Entity>();
        world.Query().With<Poisoned>().Each<Position, Velocity, Comp3>(
            (Entity e, ref Position p, ref Velocity v, ref Comp3 c3) => visited.Add(e));

        Assert.Single(visited);
        Assert.Equal(poisonedComplete, visited[0]);
    }

    [Fact]
    public void Each_EightComponents_OnlyVisitsEntitiesWithAllEight_AndAllowsMutation()
    {
        var world = new World();

        var complete = world.Create();
        world.Add(complete, new Position { X = 1 });
        world.Add(complete, new Velocity { X = 2 });
        world.Add(complete, new Comp3 { Value = 3 });
        world.Add(complete, new Comp4 { Value = 4 });
        world.Add(complete, new Comp5 { Value = 5 });
        world.Add(complete, new Comp6 { Value = 6 });
        world.Add(complete, new Comp7 { Value = 7 });
        world.Add(complete, new Comp8 { Value = 8 });

        var incomplete = world.Create();
        world.Add(incomplete, new Position());
        world.Add(incomplete, new Velocity());
        world.Add(incomplete, new Comp3());
        world.Add(incomplete, new Comp4());
        world.Add(incomplete, new Comp5());
        // missing Comp6, Comp7, Comp8

        int visited = 0;
        world.Query().Each<Position, Velocity, Comp3, Comp4, Comp5, Comp6, Comp7, Comp8>(
            (Entity e, ref Position p, ref Velocity v, ref Comp3 c3, ref Comp4 c4, ref Comp5 c5, ref Comp6 c6, ref Comp7 c7, ref Comp8 c8) =>
            {
                visited++;
                Assert.Equal(complete, e);
                p.X += 100;
                c8.Value += 1000;
            });

        Assert.Equal(1, visited);
        Assert.Equal(101, world.Get<Position>(complete).X);
        Assert.Equal(1008, world.Get<Comp8>(complete).Value);
    }

    [Fact]
    public void Each_SparseComponentAlone_VisitsOnlyEntitiesWithIt_AndAllowsMutation()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        int visited = 0;
        world.Query().Each<Poisoned>((Entity e, ref Poisoned p) =>
        {
            visited++;
            Assert.Equal(poisonedMover, e);
            p.DamagePerTick += 10;
        });

        Assert.Equal(1, visited);
        Assert.Equal(11, world.Get<Poisoned>(poisonedMover).DamagePerTick);
    }

    [Fact]
    public void Each_MixedDenseAndSparseComponents_OnlyVisitsEntitiesWithBoth_AndAllowsMutatingBoth()
    {
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        int visited = 0;
        world.Query().Each<Position, Poisoned>((Entity e, ref Position p, ref Poisoned poison) =>
        {
            visited++;
            Assert.Equal(poisonedMover, e);
            p.X += 5;
            poison.DamagePerTick += 1;
        });

        Assert.Equal(1, visited);
        Assert.Equal(5, world.Get<Position>(poisonedMover).X);
        Assert.Equal(2, world.Get<Poisoned>(poisonedMover).DamagePerTick);
        // Untouched: neither has Poisoned.
        Assert.Equal(0, world.Get<Position>(moving).X);
        Assert.Equal(10, world.Get<Position>(still).X);
    }

    [Fact]
    public void Each_OnEmptySparseComponent_Throws()
    {
        var world = new World();

        Assert.Throws<InvalidOperationException>(() =>
            world.Query().Each<Burning>((Entity e, ref Burning b) => { }));
    }

    [Fact]
    public void EmptyWorld_QueryProducesNoResults()
    {
        var world = new World();

        var result = world.Query().With<Position>().ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void Query_CanBeBuiltAcrossStatements_AsALocal()
    {
        // Query is a ref struct: it can't be boxed, stored in a field, or captured by a lambda,
        // but holding it in a local across several statements (as opposed to one fluent
        // one-liner) is still perfectly legal.
        var world = BuildWorld(out var moving, out var still, out var poisonedMover);

        var query = world.Query();
        query = query.With<Velocity>();
        query = query.Without<Poisoned>();
        var result = query.ToList();

        Assert.Single(result);
        Assert.Equal(moving, result[0]);
    }

    [Fact]
    public void Each_ArchetypeEnumerationAllocatesNothing()
    {
        // MatchingArchetypes used to be a `yield return` iterator, which heap-allocates a
        // compiler-generated enumerator on every call. It's now a hand-written struct
        // enumerable/enumerator instead, so a non-capturing Each() call should be allocation-free.
        var world = new World();
        world.Create(new Position());

        // Reuse one delegate instance for both calls: a `static` lambda's backing delegate is
        // cached per call site on first use, so calling two textually-different (but identical)
        // lambda expressions would itself allocate a second delegate and pollute the measurement.
        RefAction<Position> action = static (Entity _, ref Position _) => { };

        // Warm up JIT so first-call codegen doesn't pollute the measurement.
        world.Query().Each(action);

        long before = GC.GetAllocatedBytesForCurrentThread();
        world.Query().Each(action);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
