namespace Loom.Tests;

public class MultipleWorldsTests
{
    [Fact]
    public void TwoWorlds_HaveIndependentComponentStorage_ForTheSameType()
    {
        var worldA = new World();
        var worldB = new World();

        var a = worldA.Create();
        var b = worldB.Create();
        worldA.Add(a, new Position { X = 1 });
        worldB.Add(b, new Position { X = 2 });

        Assert.Equal(1, worldA.Get<Position>(a).X);
        Assert.Equal(2, worldB.Get<Position>(b).X);
    }

    [Fact]
    public void TwoWorlds_CanRegisterComponentTypesInDifferentOrders_WithoutCollision()
    {
        // World A only ever touches Velocity; World B touches Position first, then Velocity.
        // Component ids are assigned per-world on first use, so the two worlds are free to
        // end up with different ids for the same CLR type without any cross-talk.
        var worldA = new World();
        var worldB = new World();

        var a = worldA.Create();
        worldA.Add(a, new Velocity { X = 5 });

        var b = worldB.Create();
        worldB.Add(b, new Position { X = 1 });
        worldB.Add(b, new Velocity { X = 9 });

        Assert.Equal(5, worldA.Get<Velocity>(a).X);
        Assert.False(worldA.Has<Position>(a));
        Assert.Equal(1, worldB.Get<Position>(b).X);
        Assert.Equal(9, worldB.Get<Velocity>(b).X);
    }

    [Fact]
    public void QueryOnOneWorld_DoesNotSeeEntitiesFromAnotherWorld()
    {
        var worldA = new World();
        var worldB = new World();

        var a = worldA.Create();
        worldA.Add(a, new Position());
        var b = worldB.Create();
        worldB.Add(b, new Position());

        var resultA = worldA.Query().With<Position>().ToList();

        Assert.Single(resultA);
        Assert.Equal(a, resultA[0]);
    }
}
