namespace Loom.Tests;

public class ClearEntitiesTests
{
    struct Pos { public float X; }
    struct Tag : ISparseComponent { }

    [Fact]
    public void ClearEntities_RemovesAll_AndRecyclesIds()
    {
        var world = new World();
        world.SetSingleton(new FrameMarker { Value = 7 });
        var a = world.Create(new Pos { X = 1 });
        var b = world.Create();
        world.Add(b, new Tag());
        world.SetFather(b, a);

        world.ClearEntities();

        Assert.Equal(0, world.EntityCount);
        Assert.False(world.IsAlive(a));
        Assert.False(world.IsAlive(b));
        Assert.Equal(7, world.GetSingleton<FrameMarker>().Value);

        var c = world.Create(new Pos { X = 2 });
        Assert.True(world.IsAlive(c));
        Assert.Equal(1, world.EntityCount);
    }

    [Fact]
    public void ClearEntities_ClearSingletons_WipesSingletons()
    {
        var world = new World();
        world.SetSingleton(new FrameMarker { Value = 1 });
        world.Create();
        world.ClearEntities(clearSingletons: true);
        Assert.False(world.HasSingleton<FrameMarker>());
    }

    struct FrameMarker { public int Value; }
}
