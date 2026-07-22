using BenchmarkDotNet.Attributes;
using DefaultEcs;

namespace Loom.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class DefaultEcsBenchmarks
{
    private global::DefaultEcs.World _dense = null!;
    private global::DefaultEcs.World _filtered = null!;
    private global::DefaultEcs.World _toggle = null!;
    private global::DefaultEcs.World _sparse = null!;
    private global::DefaultEcs.EntitySet _denseSet = null!;
    private global::DefaultEcs.EntitySet _filteredSet = null!;
    private global::DefaultEcs.Entity[] _toggleEntities = null!;
    private global::DefaultEcs.Entity[] _sparseEntities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dense = new global::DefaultEcs.World();
        for (var i = 0; i < WorkloadConstants.EntityCount; i++)
        {
            var entity = _dense.CreateEntity();
            entity.Set(new Position { X = 1, Y = 2, Z = 3 });
            entity.Set(new Velocity { X = 4, Y = 5, Z = 6 });
        }
        _denseSet = _dense.GetEntities().With<Position>().With<Velocity>().AsSet();

        _filtered = new global::DefaultEcs.World();
        for (var group = 0; group < WorkloadConstants.GroupCount; group++)
        for (var i = 0; i < WorkloadConstants.EntitiesPerGroup; i++)
        {
            var entity = _filtered.CreateEntity();
            entity.Set(new Position { X = i });
            entity.Set(new Velocity { Y = 1 });
            if (group == 1) entity.Set<Excluded>();
            if (group == 2) entity.Set<GroupC>();
            if (group == 3) entity.Set<GroupD>();
        }
        _filteredSet = _filtered.GetEntities().With<Position>().With<Velocity>().Without<Excluded>().AsSet();

        _toggle = new global::DefaultEcs.World();
        _toggleEntities = new global::DefaultEcs.Entity[WorkloadConstants.ToggleCount];
        for (var i = 0; i < _toggleEntities.Length; i++)
        {
            var entity = _toggle.CreateEntity();
            entity.Set(new Position { X = i });
            _toggleEntities[i] = entity;
        }

        // DefaultEcs stores every component in sparse sets — same churn model as LeoECS Lite pools /
        // Loom ISparseComponent (no archetype move when toggling Status).
        _sparse = new global::DefaultEcs.World();
        _sparseEntities = new global::DefaultEcs.Entity[WorkloadConstants.ToggleCount];
        for (var i = 0; i < _sparseEntities.Length; i++)
        {
            var entity = _sparse.CreateEntity();
            entity.Set(new Position { X = i });
            _sparseEntities[i] = entity;
        }
    }

    [Benchmark]
    public float DenseIteration_PositionVelocity()
    {
        var checksum = 0f;
        foreach (var entity in _denseSet.GetEntities())
        {
            ref var position = ref entity.Get<Position>();
            ref var velocity = ref entity.Get<Velocity>();
            position.X += velocity.X;
            position.Y += velocity.Y;
            position.Z += velocity.Z;
            checksum += Checksum.Position(position);
        }
        return checksum;
    }

    [Benchmark]
    public float FilteredQuery_MixedArchetypes()
    {
        var checksum = 0f;
        foreach (var entity in _filteredSet.GetEntities())
        {
            ref var position = ref entity.Get<Position>();
            position.X += entity.Get<Velocity>().Y;
            checksum += position.X;
        }
        return checksum;
    }

    [Benchmark]
    public int BulkCreate_ThreeComponents()
    {
        var world = new global::DefaultEcs.World();
        for (var i = 0; i < WorkloadConstants.EntityCount; i++)
        {
            var entity = world.CreateEntity();
            entity.Set(new Position { X = 1 });
            entity.Set(new Velocity { Y = 2 });
            entity.Set(new Rotation { Z = 3 });
        }
        return WorkloadConstants.EntityCount;
    }

    [Benchmark]
    public int DenseStructuralToggle()
    {
        foreach (var entity in _toggleEntities) entity.Set(new Status { Value = 1 });
        foreach (var entity in _toggleEntities) entity.Remove<Status>();
        return _toggleEntities.Length;
    }

    [Benchmark]
    public int SparseChurn_Status()
    {
        foreach (var entity in _sparseEntities) entity.Set(new Status { Value = 1 });
        foreach (var entity in _sparseEntities) entity.Remove<Status>();
        return _sparseEntities.Length;
    }
}
