using BenchmarkDotNet.Attributes;
using Arch.Core;

namespace Loom.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class ArchBenchmarks
{
    private global::Arch.Core.World _dense = null!;
    private global::Arch.Core.World _filtered = null!;
    private global::Arch.Core.World _toggle = null!;
    private global::Arch.Core.QueryDescription _denseQuery;
    private global::Arch.Core.QueryDescription _filteredQuery;
    private global::Arch.Core.Entity[] _toggleEntities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dense = global::Arch.Core.World.Create();
        var denseTypes = new global::Arch.Core.ComponentType[] { typeof(Position), typeof(Velocity) };
        for (var i = 0; i < WorkloadConstants.EntityCount; i++)
        {
            var entity = _dense.Create(denseTypes);
            _dense.Get<Position>(entity) = new Position { X = 1, Y = 2, Z = 3 };
            _dense.Get<Velocity>(entity) = new Velocity { X = 4, Y = 5, Z = 6 };
        }
        _denseQuery = new global::Arch.Core.QueryDescription().WithAll<Position, Velocity>();

        _filtered = global::Arch.Core.World.Create();
        for (var group = 0; group < WorkloadConstants.GroupCount; group++)
        for (var i = 0; i < WorkloadConstants.EntitiesPerGroup; i++)
        {
            var types = group switch
            {
                1 => new global::Arch.Core.ComponentType[] { typeof(Position), typeof(Velocity), typeof(Excluded) },
                2 => new global::Arch.Core.ComponentType[] { typeof(Position), typeof(Velocity), typeof(GroupC) },
                3 => new global::Arch.Core.ComponentType[] { typeof(Position), typeof(Velocity), typeof(GroupD) },
                _ => new global::Arch.Core.ComponentType[] { typeof(Position), typeof(Velocity) },
            };
            var entity = _filtered.Create(types);
            _filtered.Get<Position>(entity) = new Position { X = i };
            _filtered.Get<Velocity>(entity) = new Velocity { Y = 1 };
        }
        _filteredQuery = new global::Arch.Core.QueryDescription()
            .WithAll<Position, Velocity>()
            .WithNone<Excluded>();

        _toggle = global::Arch.Core.World.Create();
        _toggleEntities = new global::Arch.Core.Entity[WorkloadConstants.ToggleCount];
        var toggleTypes = new global::Arch.Core.ComponentType[] { typeof(Position) };
        for (var i = 0; i < _toggleEntities.Length; i++)
        {
            var entity = _toggle.Create(toggleTypes);
            _toggle.Get<Position>(entity) = new Position { X = i };
            _toggleEntities[i] = entity;
        }
    }

    [Benchmark]
    public float DenseIteration_PositionVelocity()
    {
        var checksum = 0f;
        _dense.Query(in _denseQuery, entity =>
        {
            ref var position = ref _dense.Get<Position>(entity);
            ref var velocity = ref _dense.Get<Velocity>(entity);
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
        _filtered.Query(in _filteredQuery, entity =>
        {
            ref var position = ref _filtered.Get<Position>(entity);
            position.X += _filtered.Get<Velocity>(entity).Y;
            checksum += position.X;
        });
        return checksum;
    }

    [Benchmark]
    public int BulkCreate_ThreeComponents()
    {
        var world = global::Arch.Core.World.Create();
        var types = new global::Arch.Core.ComponentType[] { typeof(Position), typeof(Velocity), typeof(Rotation) };
        for (var i = 0; i < WorkloadConstants.EntityCount; i++)
        {
            var entity = world.Create(types);
            world.Get<Position>(entity) = new Position { X = 1 };
            world.Get<Velocity>(entity) = new Velocity { Y = 2 };
            world.Get<Rotation>(entity) = new Rotation { Z = 3 };
        }
        return WorkloadConstants.EntityCount;
    }

    [Benchmark]
    public int DenseStructuralToggle()
    {
        foreach (var entity in _toggleEntities) _toggle.Add(entity, new Status { Value = 1 });
        foreach (var entity in _toggleEntities) _toggle.Remove<Status>(entity);
        return _toggleEntities.Length;
    }
}
