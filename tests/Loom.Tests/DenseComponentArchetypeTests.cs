namespace Loom.Tests;

public class DenseComponentArchetypeTests
{
    [Fact]
    public void Add_ThenGet_ReturnsStoredValue()
    {
        var world = new World();
        var e = world.Create();

        world.Add(e, new Position { X = 1, Y = 2 });

        Assert.True(world.Has<Position>(e));
        var pos = world.Get<Position>(e);
        Assert.Equal(1, pos.X);
        Assert.Equal(2, pos.Y);
    }

    [Fact]
    public void Add_Twice_Throws()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Position());

        Assert.Throws<InvalidOperationException>(() => world.Add(e, new Position()));
    }

    [Fact]
    public void Get_Ref_MutatesStoredComponent()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Position { X = 1, Y = 1 });

        ref var pos = ref world.Get<Position>(e);
        pos.X = 99;

        Assert.Equal(99, world.Get<Position>(e).X);
    }

    [Fact]
    public void AddingSecondComponent_MovesArchetype_PreservesFirstComponent()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Position { X = 5, Y = 6 });

        world.Add(e, new Velocity { X = 1, Y = 1 });

        Assert.True(world.Has<Position>(e));
        Assert.True(world.Has<Velocity>(e));
        Assert.Equal(5, world.Get<Position>(e).X);
    }

    [Fact]
    public void Remove_DropsComponent_KeepsOthers()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Position { X = 1, Y = 1 });
        world.Add(e, new Velocity { X = 2, Y = 2 });

        bool removed = world.Remove<Position>(e);

        Assert.True(removed);
        Assert.False(world.Has<Position>(e));
        Assert.True(world.Has<Velocity>(e));
        Assert.Equal(2, world.Get<Velocity>(e).X);
    }

    [Fact]
    public void Remove_Absent_ReturnsFalse()
    {
        var world = new World();
        var e = world.Create();

        Assert.False(world.Remove<Position>(e));
    }

    [Fact]
    public void ArchetypeMove_SwapBack_FixesUpDisplacedEntitysRow()
    {
        // Three entities share one archetype (Position). Adding Velocity to the *first*
        // created entity forces a swap-back removal from that archetype: the last entity
        // in the table gets moved into slot 0. If World didn't patch that entity's stored
        // row index, its component reads would silently return someone else's data.
        var world = new World();
        var e0 = world.Create();
        var e1 = world.Create();
        var e2 = world.Create();
        world.Add(e0, new Position { X = 0 });
        world.Add(e1, new Position { X = 1 });
        world.Add(e2, new Position { X = 2 });

        world.Add(e0, new Velocity { X = 100 });

        Assert.Equal(0, world.Get<Position>(e0).X);
        Assert.Equal(1, world.Get<Position>(e1).X);
        Assert.Equal(2, world.Get<Position>(e2).X);
    }

    [Fact]
    public void Destroy_SwapBack_FixesUpDisplacedEntitysRow()
    {
        var world = new World();
        var e0 = world.Create();
        var e1 = world.Create();
        var e2 = world.Create();
        world.Add(e0, new Position { X = 0 });
        world.Add(e1, new Position { X = 1 });
        world.Add(e2, new Position { X = 2 });

        world.Destroy(e0);

        Assert.Equal(1, world.Get<Position>(e1).X);
        Assert.Equal(2, world.Get<Position>(e2).X);
    }

    [Fact]
    public void RepeatedAddRemove_ReusesCachedArchetypeGraphEdges()
    {
        var world = new World();
        var e = world.Create();

        world.Add(e, new Position());
        int afterFirstAdd = world.ArchetypeCount;
        world.Remove<Position>(e);
        world.Add(e, new Position());
        world.Remove<Position>(e);

        // The Position <-> empty archetype pair should be cached and reused, not
        // recreated on every add/remove cycle.
        Assert.Equal(afterFirstAdd, world.ArchetypeCount);
    }
}
