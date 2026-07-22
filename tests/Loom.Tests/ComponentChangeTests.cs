namespace Loom.Tests;

public class ComponentChangeTests
{
    [Fact]
    public void Untracked_AddRemoveSet_DoNotRecord()
    {
        var world = new World();
        var e = world.Create(new Position { X = 1 });
        world.Add(e, new Velocity { X = 2 });
        world.Set(e, new Position { X = 9 });
        world.Remove<Velocity>(e);

        Assert.Equal(0, world.AddedCount<Position>());
        Assert.Equal(0, world.RemovedCount<Velocity>());
        Assert.Equal(0, world.ChangedCount<Position>());
        Assert.False(world.AnyChanges<Position>());
    }

    [Fact]
    public void TrackChanges_RecordsAddedRemovedAndChanged()
    {
        var world = new World();
        world.TrackChanges<Position>().TrackChanges<Velocity>().TrackChanges<Poisoned>();

        var e = world.Create(new Position { X = 1 });
        Assert.Equal(1, world.AddedCount<Position>());

        world.Add(e, new Velocity { X = 3 });
        world.Add(e, new Poisoned { DamagePerTick = 2 });
        Assert.Equal(1, world.AddedCount<Velocity>());
        Assert.Equal(1, world.AddedCount<Poisoned>());

        world.Set(e, new Position { X = 5 });
        // Still in Added this frame — Changed is coalesced away.
        Assert.Equal(0, world.ChangedCount<Position>());
        Assert.Equal(5, world.Get<Position>(e).X);

        world.ClearComponentChanges();
        world.Get<Velocity>(e).X = 7;
        world.MarkChanged<Velocity>(e);
        Assert.Equal(1, world.ChangedCount<Velocity>());

        Assert.True(world.Remove<Poisoned>(e));
        Assert.Equal(1, world.RemovedCount<Poisoned>());
        Assert.True(world.AnyChanges<Poisoned>());
    }

    [Fact]
    public void Coalesce_AddThenRemove_CancelsBoth()
    {
        var world = new World();
        world.TrackChanges<Velocity>();
        var e = world.Create(new Position());

        world.Add(e, new Velocity());
        Assert.Equal(1, world.AddedCount<Velocity>());
        world.Remove<Velocity>(e);

        Assert.Equal(0, world.AddedCount<Velocity>());
        Assert.Equal(0, world.RemovedCount<Velocity>());
        Assert.False(world.AnyChanges<Velocity>());
    }

    [Fact]
    public void Coalesce_MarkChanged_OnAdded_StaysAddedOnly()
    {
        var world = new World();
        world.TrackChanges<Position>();
        var e = world.Create(new Position { X = 1 });
        world.MarkChanged<Position>(e);

        Assert.Equal(1, world.AddedCount<Position>());
        Assert.Equal(0, world.ChangedCount<Position>());
    }

    [Fact]
    public void Coalesce_DuplicateMarkChanged_IsUnique()
    {
        var world = new World();
        world.TrackChanges<Position>();
        var e = world.Create(new Position());
        world.ClearComponentChanges();

        world.MarkChanged<Position>(e);
        world.MarkChanged<Position>(e);
        Assert.Equal(1, world.ChangedCount<Position>());
    }

    [Fact]
    public void AddOrSet_AddsOrOverwrites()
    {
        var world = new World();
        world.TrackChanges<Position>();
        var e = world.Create();

        world.AddOrSet(e, new Position { X = 1 });
        Assert.Equal(1, world.AddedCount<Position>());
        Assert.Equal(1, world.Get<Position>(e).X);

        world.ClearComponentChanges();
        world.AddOrSet(e, new Position { X = 2 });
        Assert.Equal(1, world.ChangedCount<Position>());
        Assert.Equal(2, world.Get<Position>(e).X);
    }

    [Fact]
    public void ForEachAndCopy_ExposeRecordedEntities()
    {
        var world = new World();
        world.TrackChanges<Position>();

        var a = world.Create(new Position());
        var b = world.Create(new Position());
        world.ClearComponentChanges();
        world.Set(a, new Position { X = 1 });

        var added = new List<Entity>();
        world.ForEachAdded<Position>(e => added.Add(e));
        Assert.Empty(added);

        var changed = new List<Entity>();
        world.CopyChangedTo<Position>(changed);
        Assert.Equal(new[] { a }, changed);
    }

    [Fact]
    public void Destroy_RecordsRemovedForTrackedComponents()
    {
        var world = new World();
        world.TrackChanges<Position>().TrackChanges<Poisoned>();

        var e = world.Create(new Position { X = 1 });
        world.Add(e, new Poisoned { DamagePerTick = 4 });
        world.ClearComponentChanges();

        world.Destroy(e);
        Assert.Equal(1, world.RemovedCount<Position>());
        Assert.Equal(1, world.RemovedCount<Poisoned>());
    }

    [Fact]
    public void Destroy_OfJustCreated_CancelsAdded()
    {
        var world = new World();
        world.TrackChanges<Position>();
        var e = world.Create(new Position());
        world.Destroy(e);

        Assert.Equal(0, world.AddedCount<Position>());
        Assert.Equal(0, world.RemovedCount<Position>());
    }

    [Fact]
    public void EndFrame_ClearsChangesAfterEvents()
    {
        var world = new World();
        var sim = new Runtime(world);
        world.TrackChanges<Position>();
        world.Create(new Position());
        Assert.Equal(1, world.AddedCount<Position>());

        sim.EndFrame();
        Assert.Equal(0, world.AddedCount<Position>());
    }

    [Fact]
    public void CommandBuffer_AddRemove_AreTracked()
    {
        var world = new World();
        var sim = new Runtime(world);
        world.TrackChanges<Velocity>();
        var e = world.Create(new Position());

        sim.Commands.Add(e, new Velocity { X = 1 });
        Assert.Equal(0, world.AddedCount<Velocity>());
        sim.Commands.Playback();
        Assert.Equal(1, world.AddedCount<Velocity>());

        sim.Commands.Remove<Velocity>(e);
        sim.Commands.Playback();
        Assert.Equal(0, world.AddedCount<Velocity>());
        Assert.Equal(0, world.RemovedCount<Velocity>());
    }

    [Fact]
    public void Tracking_IsPerWorld_Independent()
    {
        var a = new World();
        var b = new World();
        a.TrackChanges<Position>();

        a.Create(new Position());
        b.Create(new Position());

        Assert.Equal(1, a.AddedCount<Position>());
        Assert.Equal(0, b.AddedCount<Position>());
        Assert.True(a.IsTrackingChanges<Position>());
        Assert.False(b.IsTrackingChanges<Position>());
    }

    [Fact]
    public void Set_ThrowsWhenMissing()
    {
        var world = new World();
        var e = world.Create();
        Assert.Throws<InvalidOperationException>(() => world.Set(e, new Position()));
    }

    [Fact]
    public void MarkChanged_ThrowsWhenMissing()
    {
        var world = new World();
        world.TrackChanges<Position>();
        var e = world.Create();
        Assert.Throws<InvalidOperationException>(() => world.MarkChanged<Position>(e));
    }

    [Fact]
    public void Query_Changed_FiltersToChangedEntities()
    {
        var world = new World();
        world.TrackChanges<Position>();
        var a = world.Create(new Position { X = 1 }, new Velocity { X = 1 });
        var b = world.Create(new Position { X = 2 }, new Velocity { X = 2 });
        world.ClearComponentChanges();

        world.Set(a, new Position { X = 10 });
        world.MarkChanged<Position>(b);
        // Coalesce: Set on a also marks Changed
        Assert.Equal(2, world.ChangedCount<Position>());

        var hit = new List<Entity>();
        world.Query().Changed<Position>().ForEach(e => hit.Add(e));
        Assert.Equal(2, hit.Count);
        Assert.Contains(a, hit);
        Assert.Contains(b, hit);

        var values = new List<float>();
        world.Query().Changed<Position>().Each((Entity e, ref Position p) => values.Add(p.X));
        Assert.Contains(10f, values);
        Assert.Contains(2f, values);
    }

    [Fact]
    public void Query_Added_WithExtraFilter()
    {
        var world = new World();
        world.TrackChanges<Velocity>();
        var withHealth = world.Create(new Position(), new Health { Value = 1 });
        var without = world.Create(new Position());

        world.Add(withHealth, new Velocity { X = 1 });
        world.Add(without, new Velocity { X = 2 });

        var hit = world.Query().Added<Velocity>().With<Health>().ToList();
        Assert.Single(hit);
        Assert.Equal(withHealth, hit[0]);
    }

    [Fact]
    public void Query_Removed_DoesNotRequireComponent()
    {
        var world = new World();
        world.TrackChanges<Poisoned>();
        var e = world.Create(new Position(), new Poisoned { DamagePerTick = 1 });
        world.ClearComponentChanges();

        Assert.True(world.Remove<Poisoned>(e));
        var hit = world.Query().Removed<Poisoned>().ToList();
        Assert.Single(hit);
        Assert.Equal(e, hit[0]);
        Assert.False(world.Has<Poisoned>(e));
    }

    [Fact]
    public void Query_Changed_RequiresTrackChanges()
    {
        var world = new World();
        Assert.Throws<InvalidOperationException>(() => world.Query().Changed<Position>().ToList());
    }

    [Fact]
    public void Query_WithMasks_CarryIntoChangeQuery()
    {
        var world = new World();
        world.TrackChanges<Velocity>();
        var withHealth = world.Create(new Position(), new Health { Value = 1 });
        var without = world.Create(new Position());

        world.Add(withHealth, new Velocity { X = 1 });
        world.Add(without, new Velocity { X = 2 });

        var hit = world.Query().With<Health>().Added<Velocity>().ToList();
        Assert.Single(hit);
        Assert.Equal(withHealth, hit[0]);
    }

    [Fact]
    public void ChangeQuery_IsSeparateFromQueryFilter()
    {
        var world = new World();
        world.TrackChanges<Position>();
        var e = world.Create(new Position { X = 1 });
        world.ClearComponentChanges();
        world.Set(e, new Position { X = 2 });

        var filter = world.Query().With<Position>().ToFilter();
        Assert.Single(world.Query(in filter).ToList());
        world.ClearComponentChanges();
        Assert.Single(world.Query(in filter).ToList());
        Assert.Empty(world.Query().Changed<Position>().ToList());
    }

    [Fact]
    public void TrackEntityLifecycle_RecordsCreateAndDestroy()
    {
        var world = new World();
        world.TrackEntityLifecycle();

        var a = world.Create(new Position { X = 1 });
        var b = world.Create();
        Assert.Equal(2, world.CreatedEntityCount);
        Assert.Equal(0, world.DestroyedEntityCount);

        world.ClearComponentChanges();
        Assert.Equal(0, world.CreatedEntityCount);

        world.Destroy(b);
        Assert.Equal(1, world.DestroyedEntityCount);

        var destroyed = new List<Entity>();
        world.CopyDestroyedEntitiesTo(destroyed);
        Assert.Equal(new[] { b }, destroyed);
        Assert.True(world.IsAlive(a));
    }

    [Fact]
    public void TrackEntityLifecycle_CreateThenDestroy_Cancels()
    {
        var world = new World();
        world.TrackEntityLifecycle();
        var e = world.Create();
        world.Destroy(e);

        Assert.Equal(0, world.CreatedEntityCount);
        Assert.Equal(0, world.DestroyedEntityCount);
        Assert.False(world.AnyEntityLifecycleChanges);
    }

    [Fact]
    public void TrackEntityLifecycle_DestroyThenRecreate_KeepsBoth()
    {
        var world = new World();
        world.TrackEntityLifecycle();
        var first = world.Create(new Position());
        world.ClearComponentChanges();

        world.Destroy(first);
        var second = world.Create(new Position { X = 9 });
        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.Version, second.Version);

        Assert.Equal(1, world.DestroyedEntityCount);
        Assert.Equal(1, world.CreatedEntityCount);

        var destroyed = new List<Entity>();
        var created = new List<Entity>();
        world.CopyDestroyedEntitiesTo(destroyed);
        world.CopyCreatedEntitiesTo(created);
        Assert.Equal(first, destroyed[0]);
        Assert.Equal(second, created[0]);
    }

    [Fact]
    public void UntrackedEntityLifecycle_DoesNotRecord()
    {
        var world = new World();
        var e = world.Create();
        world.Destroy(e);
        Assert.Equal(0, world.CreatedEntityCount);
        Assert.Equal(0, world.DestroyedEntityCount);
        Assert.False(world.IsTrackingEntityLifecycle);
    }
}
