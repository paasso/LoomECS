namespace Loom.Tests;

public class SparseComponentTests
{
    [Fact]
    public void Add_ThenGet_ReturnsStoredValue()
    {
        var world = new World();
        var e = world.Create();

        world.Add(e, new Poisoned { DamagePerTick = 3 });

        Assert.True(world.Has<Poisoned>(e));
        Assert.Equal(3, world.Get<Poisoned>(e).DamagePerTick);
    }

    [Fact]
    public void Remove_DropsComponent()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Poisoned { DamagePerTick = 3 });

        Assert.True(world.Remove<Poisoned>(e));
        Assert.False(world.Has<Poisoned>(e));
    }

    [Fact]
    public void SparseChurn_DoesNotFragmentArchetypeGraph()
    {
        // The whole point of routing "hot" components through a sparse set: adding/removing
        // them must never create a new archetype, unlike a dense component add/remove.
        var world = new World();
        var e = world.Create();
        world.Add(e, new Position());
        int baseline = world.ArchetypeCount;

        for (int i = 0; i < 50; i++)
        {
            world.Add(e, new Poisoned { DamagePerTick = i });
            world.Remove<Poisoned>(e);
        }

        Assert.Equal(baseline, world.ArchetypeCount);
    }

    [Fact]
    public void SparseComponent_CoexistsWithDenseComponents_Independently()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Position { X = 7 });

        world.Add(e, new Poisoned { DamagePerTick = 5 });
        world.Remove<Position>(e);

        Assert.False(world.Has<Position>(e));
        Assert.True(world.Has<Poisoned>(e));
        Assert.Equal(5, world.Get<Poisoned>(e).DamagePerTick);
    }

    [Fact]
    public void Destroy_ClearsSparseComponents_SoRecycledIdStartsClean()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Poisoned { DamagePerTick = 9 });
        world.Destroy(e);

        var reused = world.Create();

        Assert.Equal(e.Id, reused.Id);
        Assert.False(world.Has<Poisoned>(reused));
    }

    [Fact]
    public void TwoSparseComponentTypes_OnSameEntity_HaveIndependentBits()
    {
        var world = new World();
        var e = world.Create();

        world.Add(e, new Poisoned { DamagePerTick = 4 });
        world.Add(e, new Burning());

        Assert.True(world.Has<Poisoned>(e));
        Assert.True(world.Has<Burning>(e));

        world.Remove<Poisoned>(e);

        Assert.False(world.Has<Poisoned>(e));
        Assert.True(world.Has<Burning>(e)); // removing one sparse component's bit must not clear the other's
    }

    [Fact]
    public void MultipleEntities_HaveIndependentSparseData()
    {
        var world = new World();
        var a = world.Create();
        var b = world.Create();
        world.Add(a, new Poisoned { DamagePerTick = 1 });
        world.Add(b, new Poisoned { DamagePerTick = 2 });

        world.Remove<Poisoned>(a);

        Assert.False(world.Has<Poisoned>(a));
        Assert.True(world.Has<Poisoned>(b));
        Assert.Equal(2, world.Get<Poisoned>(b).DamagePerTick);
    }
}
