using BenchmarkDotNet.Attributes;
using Friflo.Engine.ECS;

namespace Loom.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class FrifloBenchmarks
{
    private global::Friflo.Engine.ECS.EntityStore _dense = null!;
    private global::Friflo.Engine.ECS.EntityStore _filtered = null!;
    private global::Friflo.Engine.ECS.EntityStore _toggle = null!;
    private global::Friflo.Engine.ECS.ArchetypeQuery<Position, Velocity> _denseQuery = null!;
    private global::Friflo.Engine.ECS.ArchetypeQuery<Position, Velocity> _filteredQuery = null!;
    private global::Friflo.Engine.ECS.Entity[] _toggleEntities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dense = new global::Friflo.Engine.ECS.EntityStore();
        for (var i = 0; i < WorkloadConstants.EntityCount; i++)
            _dense.CreateEntity(new Position { X = 1, Y = 2, Z = 3 }, new Velocity { X = 4, Y = 5, Z = 6 });
        _denseQuery = _dense.Query<Position, Velocity>();

        _filtered = new global::Friflo.Engine.ECS.EntityStore();
        for (var group = 0; group < WorkloadConstants.GroupCount; group++)
        for (var i = 0; i < WorkloadConstants.EntitiesPerGroup; i++)
        {
            var entity = _filtered.CreateEntity(new Position { X = i }, new Velocity { Y = 1 });
            if (group == 1) entity.AddComponent(new Excluded());
            if (group == 2) entity.AddComponent(new GroupC());
            if (group == 3) entity.AddComponent(new GroupD());
        }
        _filteredQuery = _filtered.Query<Position, Velocity>()
            .WithoutAnyComponents(global::Friflo.Engine.ECS.ComponentTypes.Get<Excluded>());

        _toggle = new global::Friflo.Engine.ECS.EntityStore();
        _toggleEntities = new global::Friflo.Engine.ECS.Entity[WorkloadConstants.ToggleCount];
        for (var i = 0; i < _toggleEntities.Length; i++)
            _toggleEntities[i] = _toggle.CreateEntity(new Position { X = i });
    }

    [Benchmark]
    public float DenseIteration_PositionVelocity()
    {
        var checksum = 0f;
        _denseQuery.ForEachEntity((ref Position position, ref Velocity velocity, global::Friflo.Engine.ECS.Entity _) =>
        {
            position.X += velocity.X;
            position.Y += velocity.Y;
            position.Z += velocity.Z;
            checksum += Checksum.Position(position);
        });
        return checksum;
    }

    [Benchmark]
    public float FilteredQuery_MixedArchetypes()
    {
        var checksum = 0f;
        _filteredQuery.ForEachEntity((ref Position position, ref Velocity velocity, global::Friflo.Engine.ECS.Entity _) =>
        {
            position.X += velocity.Y;
            checksum += position.X;
        });
        return checksum;
    }

    [Benchmark]
    public int BulkCreate_ThreeComponents()
    {
        var world = new global::Friflo.Engine.ECS.EntityStore();
        for (var i = 0; i < WorkloadConstants.EntityCount; i++)
            world.CreateEntity(new Position { X = 1 }, new Velocity { Y = 2 }, new Rotation { Z = 3 });
        return WorkloadConstants.EntityCount;
    }

    [Benchmark]
    public int DenseStructuralToggle()
    {
        foreach (var entity in _toggleEntities) entity.AddComponent(new Status { Value = 1 });
        foreach (var entity in _toggleEntities) entity.RemoveComponent<Status>();
        return _toggleEntities.Length;
    }
}
