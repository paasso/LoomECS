namespace Loom.Tests;

public class QueryFilterTests
{
    struct A { public int V; }
    struct B { public int V; }
    struct C { public int V; }
    struct Flag : ISparseComponent { }

    [Fact]
    public void WithAny_MatchesUnionOfDenseTypes()
    {
        var world = new World();
        var onlyA = world.Create(new A { V = 1 });
        var onlyB = world.Create(new B { V = 2 });
        var both = world.Create(new A { V = 3 }, new B { V = 4 });
        world.Create(new C { V = 5 });

        var hit = new List<Entity>();
        world.Query().WithAny<A, B>().ForEach(e => hit.Add(e));

        Assert.Equal(3, hit.Count);
        Assert.Contains(onlyA, hit);
        Assert.Contains(onlyB, hit);
        Assert.Contains(both, hit);
    }

    [Fact]
    public void WithAny_WithWith_IsIntersection()
    {
        var world = new World();
        world.Create(new A { V = 1 });
        var ab = world.Create(new A { V = 2 }, new B { V = 3 });
        world.Create(new B { V = 4 }, new C { V = 5 });

        var hit = new List<Entity>();
        world.Query().With<A>().WithAny<B, C>().ForEach(e => hit.Add(e));
        Assert.Equal(new[] { ab }, hit);
    }

    [Fact]
    public void Enabled_SkipsDisabledEntities()
    {
        var world = new World();
        var live = world.Create(new A { V = 1 });
        var dead = world.Create(new A { V = 2 });
        world.SetEnabled(dead, false);

        Assert.True(world.IsEnabled(live));
        Assert.False(world.IsEnabled(dead));

        var hit = new List<Entity>();
        world.Query().Enabled().With<A>().ForEach(e => hit.Add(e));
        Assert.Equal(new[] { live }, hit);

        world.SetEnabled(dead, true);
        hit.Clear();
        world.Query().Enabled().With<A>().ForEach(e => hit.Add(e));
        Assert.Equal(2, hit.Count);
    }

    [Fact]
    public void WithAny_SparseFlags()
    {
        var world = new World();
        var with = world.Create(new A());
        world.Add(with, new Flag());
        world.Create(new A());

        var hit = new List<Entity>();
        world.Query().With<A>().WithAny<Flag>().ForEach(e => hit.Add(e));
        Assert.Equal(new[] { with }, hit);
    }

    [Fact]
    public void QueryFilter_PresetMasks_MatchFluentQuery()
    {
        var world = new World();
        var keep = world.Create(new A { V = 1 }, new B { V = 2 });
        world.Create(new A { V = 3 });
        world.Create(new B { V = 4 });

        var filter = world.Query().With<A>().With<B>().ToFilter();

        var viaFilter = new List<Entity>();
        world.Query(in filter).ForEach(e => viaFilter.Add(e));

        var viaFluent = world.Query().With<A>().With<B>().ToList();
        Assert.Equal(viaFluent, viaFilter);
        Assert.Equal(new[] { keep }, viaFilter);
    }

    [Fact]
    public void QueryFilter_CanExtendWithAdditionalConstraints()
    {
        var world = new World();
        var ab = world.Create(new A { V = 1 }, new B { V = 2 });
        world.Create(new A { V = 3 }, new B { V = 4 }, new C { V = 5 });

        var filter = world.Query().With<A>().With<B>().ToFilter();
        var hit = world.Query(in filter).Without<C>().ToList();
        Assert.Equal(new[] { ab }, hit);
    }

    [Fact]
    public void QueryFilter_PreservesSparseMasks()
    {
        var world = new World();
        var flagged = world.Create(new A());
        world.Add(flagged, new Flag());
        world.Create(new A());

        var filter = world.Query().With<A>().With<Flag>().ToFilter();
        Assert.Equal(new[] { flagged }, world.Query(in filter).ToList());
    }
}
