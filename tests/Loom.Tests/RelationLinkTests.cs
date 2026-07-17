namespace Loom.Tests;

public class RelationLinkTests
{
    struct OwnedBy : IRelationComponent
    {
        public Entity Target;
    }

    [Fact]
    public void SetRelation_SetsTarget_AndReverseLookup()
    {
        var world = new World();
        var owner = world.Create();
        var item = world.Create();

        world.SetRelation<OwnedBy>(item, owner);

        Assert.True(world.HasRelation<OwnedBy>(item));
        Assert.True(world.TryGetRelationTarget<OwnedBy>(item, out var target));
        Assert.Equal(owner, target);

        var sources = new List<Entity>();
        world.ForEachRelationSource<OwnedBy>(owner, e => sources.Add(e));
        Assert.Equal(new[] { item }, sources);
    }

    [Fact]
    public void DestroyTarget_RemovesIncomingRelations()
    {
        var world = new World();
        var owner = world.Create();
        var item = world.Create();
        world.SetRelation<OwnedBy>(item, owner);

        world.Destroy(owner);

        Assert.True(world.IsAlive(item));
        Assert.False(world.HasRelation<OwnedBy>(item));
    }

    [Fact]
    public void RemoveRelation_ClearsLink()
    {
        var world = new World();
        var a = world.Create();
        var b = world.Create();
        world.SetRelation<OwnedBy>(a, b);
        Assert.True(world.RemoveRelation<OwnedBy>(a));
        Assert.False(world.HasRelation<OwnedBy>(a));
    }
}
