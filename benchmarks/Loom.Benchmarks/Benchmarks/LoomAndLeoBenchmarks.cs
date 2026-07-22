using BenchmarkDotNet.Attributes;
using Leopotam.EcsLite;
using LoomEntity = global::Loom.Entities.Entity;

namespace Loom.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class LoomBenchmarks
{
    private global::Loom.World _dense = null!;
    private global::Loom.World _filtered = null!;
    private global::Loom.World _toggle = null!;
    private global::Loom.World _sparse = null!;
    private LoomEntity[] _toggleEntities = null!;
    private LoomEntity[] _sparseEntities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dense = new global::Loom.World();
        var denseEntities = new LoomEntity[WorkloadConstants.EntityCount];
        _dense.CreateMany(denseEntities, new Position { X = 1, Y = 2, Z = 3 }, new Velocity { X = 4, Y = 5, Z = 6 });

        _filtered = new global::Loom.World();
        for (var group = 0; group < WorkloadConstants.GroupCount; group++)
        for (var i = 0; i < WorkloadConstants.EntitiesPerGroup; i++)
        {
            var entity = _filtered.Create(new Position { X = i }, new Velocity { Y = 1 });
            if (group == 1) _filtered.Add(entity, new Excluded());
            if (group == 2) _filtered.Add(entity, new GroupC());
            if (group == 3) _filtered.Add(entity, new GroupD());
        }

        _toggle = new global::Loom.World();
        _toggleEntities = new LoomEntity[WorkloadConstants.ToggleCount];
        for (var i = 0; i < _toggleEntities.Length; i++)
            _toggleEntities[i] = _toggle.Create(new Position { X = i }, new Velocity { Y = 1 });

        _sparse = new global::Loom.World();
        _sparseEntities = new LoomEntity[WorkloadConstants.ToggleCount];
        for (var i = 0; i < _sparseEntities.Length; i++)
            _sparseEntities[i] = _sparse.Create(new Position { X = i });
    }

    [Benchmark]
    public float DenseIteration_PositionVelocity()
    {
        var checksum = 0f;
        _dense.Query().Each<Position, Velocity>((LoomEntity _, ref Position position, ref Velocity velocity) =>
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
        _filtered.Query().With<Position>().With<Velocity>().Without<Excluded>()
            .Each<Position, Velocity>((LoomEntity _, ref Position position, ref Velocity velocity) =>
            {
                position.X += velocity.Y;
                checksum += position.X;
            });
        return checksum;
    }

    [Benchmark]
    public int BulkCreate_ThreeComponents()
    {
        var world = new global::Loom.World();
        world.CreateMany(
            WorkloadConstants.EntityCount,
            new Position { X = 1 },
            new Velocity { Y = 2 },
            new Rotation { Z = 3 });
        return world.EntityCount;
    }

    [Benchmark]
    public int DenseStructuralToggle()
    {
        _toggle.AddMany(_toggleEntities, new Status { Value = 1 });
        _toggle.RemoveMany<Status>(_toggleEntities);
        return _toggle.EntityCount;
    }

    [Benchmark]
    public int SparseChurn_Status()
    {
        foreach (var entity in _sparseEntities)
            _sparse.Add(entity, new SparseStatus { Value = 1 });
        foreach (var entity in _sparseEntities)
            _sparse.Remove<SparseStatus>(entity);
        return _sparse.EntityCount;
    }

    [Benchmark]
    public int SharedInterning_Material()
    {
        foreach (var entity in _sparseEntities)
            _sparse.Add(entity, new Material { Id = entity.Id % 16 });
        var unique = _sparse.SharedInstanceCount<Material>();
        foreach (var entity in _sparseEntities)
            _sparse.Remove<Material>(entity);
        return unique;
    }
}

[MemoryDiagnoser]
public class LeoEcsLiteBenchmarks
{
    private EcsWorld _dense = null!;
    private EcsWorld _filtered = null!;
    private EcsWorld _toggle = null!;
    private EcsWorld _sparse = null!;
    private EcsPool<Position> _densePositions = null!;
    private EcsPool<Velocity> _denseVelocities = null!;
    private EcsFilter _denseFilter = null!;
    private EcsFilter _filteredFilter = null!;
    private int[] _toggleEntities = null!;
    private int[] _sparseEntities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dense = new EcsWorld();
        _densePositions = _dense.GetPool<Position>();
        _denseVelocities = _dense.GetPool<Velocity>();
        for (var i = 0; i < WorkloadConstants.EntityCount; i++)
        {
            var entity = _dense.NewEntity();
            _densePositions.Add(entity) = new Position { X = 1, Y = 2, Z = 3 };
            _denseVelocities.Add(entity) = new Velocity { X = 4, Y = 5, Z = 6 };
        }
        _denseFilter = _dense.Filter<Position>().Inc<Velocity>().End();

        _filtered = new EcsWorld();
        var positions = _filtered.GetPool<Position>();
        var velocities = _filtered.GetPool<Velocity>();
        var excluded = _filtered.GetPool<Excluded>();
        var groupC = _filtered.GetPool<GroupC>();
        var groupD = _filtered.GetPool<GroupD>();
        for (var group = 0; group < WorkloadConstants.GroupCount; group++)
        for (var i = 0; i < WorkloadConstants.EntitiesPerGroup; i++)
        {
            var entity = _filtered.NewEntity();
            positions.Add(entity) = new Position { X = i };
            velocities.Add(entity) = new Velocity { Y = 1 };
            if (group == 1) excluded.Add(entity);
            if (group == 2) groupC.Add(entity);
            if (group == 3) groupD.Add(entity);
        }
        _filteredFilter = _filtered.Filter<Position>().Inc<Velocity>().Exc<Excluded>().End();

        _toggle = new EcsWorld();
        _toggleEntities = new int[WorkloadConstants.ToggleCount];
        var togglePositions = _toggle.GetPool<Position>();
        for (var i = 0; i < _toggleEntities.Length; i++)
        {
            var entity = _toggle.NewEntity();
            togglePositions.Add(entity) = new Position { X = i };
            _toggleEntities[i] = entity;
        }

        _sparse = new EcsWorld();
        _sparseEntities = new int[WorkloadConstants.ToggleCount];
        var sparsePositions = _sparse.GetPool<Position>();
        for (var i = 0; i < _sparseEntities.Length; i++)
        {
            var entity = _sparse.NewEntity();
            sparsePositions.Add(entity) = new Position { X = i };
            _sparseEntities[i] = entity;
        }
    }

    [Benchmark]
    public float DenseIteration_PositionVelocity()
    {
        var checksum = 0f;
        foreach (var entity in _denseFilter)
        {
            ref var position = ref _densePositions.Get(entity);
            ref var velocity = ref _denseVelocities.Get(entity);
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
        var positions = _filtered.GetPool<Position>();
        var velocities = _filtered.GetPool<Velocity>();
        var checksum = 0f;
        foreach (var entity in _filteredFilter)
        {
            ref var position = ref positions.Get(entity);
            position.X += velocities.Get(entity).Y;
            checksum += position.X;
        }
        return checksum;
    }

    [Benchmark]
    public int BulkCreate_ThreeComponents()
    {
        var world = new EcsWorld();
        var positions = world.GetPool<Position>();
        var velocities = world.GetPool<Velocity>();
        var rotations = world.GetPool<Rotation>();
        for (var i = 0; i < WorkloadConstants.EntityCount; i++)
        {
            var entity = world.NewEntity();
            positions.Add(entity) = new Position { X = 1 };
            velocities.Add(entity) = new Velocity { Y = 2 };
            rotations.Add(entity) = new Rotation { Z = 3 };
        }
        return WorkloadConstants.EntityCount;
    }

    [Benchmark]
    public int DenseStructuralToggle()
    {
        var pool = _toggle.GetPool<Status>();
        foreach (var entity in _toggleEntities) pool.Add(entity) = new Status { Value = 1 };
        foreach (var entity in _toggleEntities) pool.Del(entity);
        return _toggleEntities.Length;
    }

    [Benchmark]
    public int SparseChurn_Status()
    {
        var pool = _sparse.GetPool<Status>();
        foreach (var entity in _sparseEntities) pool.Add(entity) = new Status { Value = 1 };
        foreach (var entity in _sparseEntities) pool.Del(entity);
        return _sparseEntities.Length;
    }
}
