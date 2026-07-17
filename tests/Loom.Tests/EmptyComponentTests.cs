using Loom.Internal;

namespace Loom.Tests;

/// <summary>Covers the "tag" optimization: components with no fields (Dead: dense, Burning:
/// sparse) skip all backing storage entirely — an archetype column, or a SparseSet — and are
/// tracked purely via the presence bit (Archetype.Mask / EntityRecord.SparseMask).</summary>
public class EmptyComponentTests
{
    [Fact]
    public void DenseTag_AddThenHas_ReportsPresence()
    {
        var world = new World();
        var e = world.Create();

        world.Add(e, new Dead());

        Assert.True(world.Has<Dead>(e));
    }

    [Fact]
    public void DenseTag_Get_ReturnsAValue()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Dead());

        // Nothing meaningful to assert about the value itself (Dead has no fields) — the point is
        // Get<T> doesn't throw/crash despite there being no backing column for this component.
        var _ = world.Get<Dead>(e);
        Assert.True(world.Has<Dead>(e));
    }

    [Fact]
    public void DenseTag_Remove_DropsPresence()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Dead());

        Assert.True(world.Remove<Dead>(e));

        Assert.False(world.Has<Dead>(e));
    }

    [Fact]
    public void DenseTag_CoexistsWithDataComponent_ArchetypeMove()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Position { X = 7 });

        world.Add(e, new Dead());

        Assert.True(world.Has<Dead>(e));
        Assert.True(world.Has<Position>(e));
        Assert.Equal(7, world.Get<Position>(e).X);

        world.Remove<Dead>(e);

        Assert.False(world.Has<Dead>(e));
        Assert.True(world.Has<Position>(e));
        Assert.Equal(7, world.Get<Position>(e).X);
    }

    [Fact]
    public void DenseTag_CreatedViaBatchedCreate_HasCorrectPresenceAndSiblingData()
    {
        var world = new World();

        var e = world.Create(new Position { X = 3 }, new Dead());

        Assert.True(world.Has<Dead>(e));
        Assert.True(world.Has<Position>(e));
        Assert.Equal(3, world.Get<Position>(e).X);
    }

    [Fact]
    public void DenseTag_Destroy_LeavesNoStaleStateOnRecycledId()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Dead());
        world.Destroy(e);

        var reused = world.Create();

        Assert.Equal(e.Id, reused.Id);
        Assert.False(world.Has<Dead>(reused));
    }

    [Fact]
    public void DenseTag_ManyEntitiesAcrossChunks_AllReportPresenceIndependently()
    {
        var world = new World();
        int n = Archetype.ChunkCapacity + 50;
        var entities = new Entity[n];
        for (int i = 0; i < n; i++)
            entities[i] = world.Create(new Position { X = i });

        // Tag only the odd ones.
        for (int i = 1; i < n; i += 2)
            world.Add(entities[i], new Dead());

        for (int i = 0; i < n; i++)
            Assert.Equal(i % 2 == 1, world.Has<Dead>(entities[i]));
    }

    [Fact]
    public void DenseTag_QueryWith_FiltersCorrectly()
    {
        var world = new World();
        var tagged = world.Create(new Position { X = 1 });
        world.Add(tagged, new Dead());
        var untagged = world.Create(new Position { X = 2 });

        var result = world.Query().With<Dead>().ToList();

        Assert.Single(result);
        Assert.Equal(tagged, result[0]);
    }

    [Fact]
    public void DenseTag_QueryWithout_FiltersCorrectly()
    {
        var world = new World();
        var tagged = world.Create(new Position());
        world.Add(tagged, new Dead());
        var untagged = world.Create(new Position());

        var result = world.Query().With<Position>().Without<Dead>().ToList();

        Assert.Single(result);
        Assert.Equal(untagged, result[0]);
    }

    [Fact]
    public void DenseTag_Each_Throws()
    {
        var world = new World();

        Assert.Throws<InvalidOperationException>(() =>
            world.Query().Each<Dead>((Entity e, ref Dead d) => { }));
    }

    [Fact]
    public void SparseTag_AddGetRemove_WorksWithoutBackingStorage()
    {
        var world = new World();
        var e = world.Create();

        world.Add(e, new Burning());
        Assert.True(world.Has<Burning>(e));
        var _ = world.Get<Burning>(e);

        Assert.True(world.Remove<Burning>(e));
        Assert.False(world.Has<Burning>(e));
    }

    [Fact]
    public void SparseTag_Destroy_DoesNotThrowDespiteNoSparseSet()
    {
        var world = new World();
        var e = world.Create();
        world.Add(e, new Burning());

        // Regression guard: Destroy's sparse-set cleanup loop used to index the sparse-set
        // dictionary directly, which would KeyNotFoundException for a tag type that never got a
        // SparseSet created for it.
        world.Destroy(e);

        Assert.False(world.IsAlive(e));
    }

    [Fact]
    public void SparseTag_QueryWith_FiltersCorrectly()
    {
        var world = new World();
        var burning = world.Create(new Position());
        world.Add(burning, new Burning());
        var notBurning = world.Create(new Position());

        var result = world.Query().With<Burning>().ToList();

        Assert.Single(result);
        Assert.Equal(burning, result[0]);
    }
}
