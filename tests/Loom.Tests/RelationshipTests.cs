namespace Loom.Tests;

public class RelationshipTests
{
    [Fact]
    public void SetFather_SetsGetFather()
    {
        var world = new World();
        var father = world.Create();
        var child = world.Create();

        world.SetFather(child, father);

        Assert.Equal(father, world.GetFather(child));
        Assert.True(world.HasFather(child));
    }

    [Fact]
    public void GetFather_RootEntity_ReturnsNull()
    {
        var world = new World();
        var e = world.Create();

        Assert.True(world.GetFather(e).IsNull);
        Assert.False(world.HasFather(e));
    }

    [Fact]
    public void GetChildren_EnumeratesDirectChildrenOnly_NotGrandchildren()
    {
        var world = new World();
        var father = world.Create();
        var childA = world.Create();
        var childB = world.Create();
        var grandchild = world.Create();

        world.SetFather(childA, father);
        world.SetFather(childB, father);
        world.SetFather(grandchild, childA);

        var seen = new List<Entity>();
        foreach (var child in world.GetChildren(father))
            seen.Add(child);

        Assert.Equal(2, seen.Count);
        Assert.Contains(childA, seen);
        Assert.Contains(childB, seen);
        Assert.DoesNotContain(grandchild, seen);
        Assert.True(world.HasChildren(father));
    }

    [Fact]
    public void HasChildren_NoChildren_ReturnsFalse()
    {
        var world = new World();
        var e = world.Create();

        Assert.False(world.HasChildren(e));
    }

    [Fact]
    public void SetFather_Reparenting_MovesChildBetweenFathers()
    {
        var world = new World();
        var oldFather = world.Create();
        var newFather = world.Create();
        var child = world.Create();

        world.SetFather(child, oldFather);
        world.SetFather(child, newFather);

        Assert.Equal(newFather, world.GetFather(child));
        Assert.False(world.HasChildren(oldFather));
        Assert.True(world.HasChildren(newFather));

        var childrenOfNewFather = new List<Entity>();
        foreach (var c in world.GetChildren(newFather))
            childrenOfNewFather.Add(c);
        Assert.Single(childrenOfNewFather);
        Assert.Equal(child, childrenOfNewFather[0]);
    }

    [Fact]
    public void RemoveFather_DetachesChild_ChildStaysAlive()
    {
        var world = new World();
        var father = world.Create();
        var child = world.Create();
        world.SetFather(child, father);

        var removed = world.RemoveFather(child);

        Assert.True(removed);
        Assert.True(world.IsAlive(child));
        Assert.True(world.GetFather(child).IsNull);
        Assert.False(world.HasChildren(father));
    }

    [Fact]
    public void RemoveFather_NoFather_ReturnsFalse()
    {
        var world = new World();
        var e = world.Create();

        Assert.False(world.RemoveFather(e));
    }

    [Fact]
    public void SetFather_SelfParent_Throws()
    {
        var world = new World();
        var e = world.Create();

        Assert.Throws<InvalidOperationException>(() => world.SetFather(e, e));
    }

    [Fact]
    public void SetFather_CreatingCycle_Throws()
    {
        var world = new World();
        var a = world.Create();
        var b = world.Create();
        var c = world.Create();

        // a -> b -> c (c is a's grandfather)
        world.SetFather(b, a);
        world.SetFather(c, b);

        // Making a a child of c would close the loop a -> b -> c -> a.
        Assert.Throws<InvalidOperationException>(() => world.SetFather(a, c));
    }

    [Fact]
    public void Destroy_Father_CascadesToChildrenAndGrandchildren()
    {
        var world = new World();
        var father = world.Create();
        var child = world.Create();
        var grandchild = world.Create();
        world.SetFather(child, father);
        world.SetFather(grandchild, child);

        world.Destroy(father);

        Assert.False(world.IsAlive(father));
        Assert.False(world.IsAlive(child));
        Assert.False(world.IsAlive(grandchild));
        Assert.Equal(0, world.EntityCount);
    }

    [Fact]
    public void Destroy_OneChild_DoesNotAffectFatherOrSiblings()
    {
        var world = new World();
        var father = world.Create();
        var childA = world.Create();
        var childB = world.Create();
        world.SetFather(childA, father);
        world.SetFather(childB, father);

        world.Destroy(childA);

        Assert.True(world.IsAlive(father));
        Assert.True(world.IsAlive(childB));
        Assert.False(world.IsAlive(childA));

        var remaining = new List<Entity>();
        foreach (var c in world.GetChildren(father))
            remaining.Add(c);
        Assert.Single(remaining);
        Assert.Equal(childB, remaining[0]);
    }

    [Fact]
    public void Destroy_LastChild_FatherHasNoChildrenLeft()
    {
        var world = new World();
        var father = world.Create();
        var child = world.Create();
        world.SetFather(child, father);

        world.Destroy(child);

        Assert.True(world.IsAlive(father));
        Assert.False(world.HasChildren(father));
    }

    [Fact]
    public void Destroy_EntityWithNoRelations_Works()
    {
        var world = new World();
        var e = world.Create();

        world.Destroy(e);

        Assert.False(world.IsAlive(e));
    }
}
