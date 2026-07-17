namespace Loom.Tests;

public class SingletonTests
{
    private struct Time
    {
        public float Delta;
        public float Total;
    }

    [Fact]
    public void SetAndGetSingleton_RoundTrips()
    {
        var world = new World();
        world.SetSingleton(new Time { Delta = 0.016f, Total = 1f });

        ref var time = ref world.GetSingleton<Time>();
        Assert.Equal(0.016f, time.Delta);

        time.Total = 2f;
        Assert.Equal(2f, world.GetSingleton<Time>().Total);
    }

    [Fact]
    public void GetSingleton_Missing_Throws()
    {
        var world = new World();
        Assert.Throws<InvalidOperationException>(() => world.GetSingleton<Time>());
    }

    [Fact]
    public void GetOrCreateSingleton_InitializesDefault()
    {
        var world = new World();
        ref var time = ref world.GetOrCreateSingleton<Time>();
        Assert.Equal(0f, time.Delta);
        Assert.True(world.HasSingleton<Time>());
    }

    [Fact]
    public void RemoveSingleton_ClearsPresence()
    {
        var world = new World();
        world.SetSingleton(new Time { Delta = 1 });
        Assert.True(world.RemoveSingleton<Time>());
        Assert.False(world.HasSingleton<Time>());
        Assert.False(world.RemoveSingleton<Time>());
    }

    [Fact]
    public void Singletons_ArePerWorld()
    {
        var a = new World();
        var b = new World();
        a.SetSingleton(new Time { Delta = 1 });
        b.SetSingleton(new Time { Delta = 2 });

        Assert.Equal(1, a.GetSingleton<Time>().Delta);
        Assert.Equal(2, b.GetSingleton<Time>().Delta);
    }
}
