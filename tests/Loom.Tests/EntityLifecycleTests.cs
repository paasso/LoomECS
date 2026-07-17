namespace Loom.Tests;

public class EntityLifecycleTests
{
    [Fact]
    public void Create_ProducesDistinctAliveEntities()
    {
        var world = new World();
        var a = world.Create();
        var b = world.Create();

        Assert.NotEqual(a, b);
        Assert.True(world.IsAlive(a));
        Assert.True(world.IsAlive(b));
        Assert.Equal(2, world.EntityCount);
    }

    [Fact]
    public void Destroy_MarksEntityDead()
    {
        var world = new World();
        var e = world.Create();

        world.Destroy(e);

        Assert.False(world.IsAlive(e));
        Assert.Equal(0, world.EntityCount);
    }

    [Fact]
    public void Destroy_RecycledId_BumpsGeneration_StaleHandleIsRejected()
    {
        var world = new World();
        var first = world.Create();
        world.Destroy(first);

        var second = world.Create();

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.Version, second.Version);
        Assert.False(world.IsAlive(first));
        Assert.True(world.IsAlive(second));
    }

    [Fact]
    public void IdZero_IsReservedAsNull_NeverHandedOutByCreate()
    {
        var world = new World();

        var first = world.Create();

        Assert.NotEqual(0, first.Id);
        Assert.True(Entity.Null.IsNull);
        Assert.True(default(Entity).IsNull);
        Assert.False(world.IsAlive(Entity.Null));
    }

    [Fact]
    public void OperatingOnDeadEntity_Throws()
    {
        var world = new World();
        var e = world.Create();
        world.Destroy(e);

        Assert.Throws<InvalidOperationException>(() => world.Add(e, new Position()));
        Assert.Throws<InvalidOperationException>(() => world.Get<Position>(e));
        Assert.Throws<InvalidOperationException>(() => world.Destroy(e));
    }
}
