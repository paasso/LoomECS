using System.Threading;

namespace Loom.Tests;

public class ParallelQueryTests
{
    private const int EntityCount = 2500; // > Archetype.ChunkCapacity (1024) → multiple chunks

    [Fact]
    public void ParallelForEach_VisitsEveryMatchingEntity()
    {
        var world = new World();
        for (int i = 0; i < EntityCount; i++)
            world.Create(new Position { X = i });

        int visited = 0;
        world.Query().With<Position>().ParallelForEach(_ => Interlocked.Increment(ref visited));
        Assert.Equal(EntityCount, visited);
    }

    [Fact]
    public void ParallelEach_Dense_MutatesLikeEach()
    {
        var sequential = BuildWorld();
        var parallel = BuildWorld();

        sequential.Query().Each<Position, Velocity>((Entity _, ref Position p, ref Velocity v) =>
        {
            p.X += v.X * 2;
            p.Y += v.Y * 2;
        });

        parallel.Query().ParallelEach<Position, Velocity>((Entity _, ref Position p, ref Velocity v) =>
        {
            p.X += v.X * 2;
            p.Y += v.Y * 2;
        });

        AssertSamePositions(sequential, parallel);
    }

    [Fact]
    public void ParallelEach_WithSparseFilter_MatchesEach()
    {
        var sequential = BuildWorld();
        var parallel = BuildWorld();

        // Poison every other entity.
        sequential.Query().Each<Position>((Entity e, ref Position _) =>
        {
            if ((e.Id & 1) == 0)
                sequential.Add(e, new Poisoned { DamagePerTick = 1 });
        });
        parallel.Query().Each<Position>((Entity e, ref Position _) =>
        {
            if ((e.Id & 1) == 0)
                parallel.Add(e, new Poisoned { DamagePerTick = 1 });
        });

        sequential.Query().With<Poisoned>().Each<Position>((Entity _, ref Position p) => p.X += 10);
        parallel.Query().With<Poisoned>().ParallelEach<Position>((Entity _, ref Position p) => p.X += 10);

        AssertSamePositions(sequential, parallel);
    }

    [Fact]
    public void ParallelEach_SparseComponent_MutatesValues()
    {
        var world = new World();
        for (int i = 0; i < EntityCount; i++)
        {
            var e = world.Create(new Position { X = i });
            world.Add(e, new Poisoned { DamagePerTick = 1 });
        }

        world.Query().ParallelEach<Poisoned>((Entity _, ref Poisoned p) => p.DamagePerTick += 1);

        int sum = 0;
        world.Query().Each<Poisoned>((Entity _, ref Poisoned p) => sum += p.DamagePerTick);
        Assert.Equal(EntityCount * 2, sum);
    }

    [Fact]
    public void ParallelForEach_EmptyWorld_IsNoOp()
    {
        var world = new World();
        int visited = 0;
        world.Query().With<Position>().ParallelForEach(_ => Interlocked.Increment(ref visited));
        Assert.Equal(0, visited);
    }

    [Fact]
    public void ParallelEach_WithoutFilter_ExcludesMatches()
    {
        var world = BuildWorld();
        world.Query().Each<Position>((Entity e, ref Position _) =>
        {
            if ((e.Id & 1) == 0)
                world.Add(e, new Poisoned { DamagePerTick = 1 });
        });

        world.Query().Without<Poisoned>().ParallelEach<Position>((Entity _, ref Position p) => p.X = -1);

        int kept = 0;
        int cleared = 0;
        world.Query().Each<Position>((Entity e, ref Position p) =>
        {
            if (world.Has<Poisoned>(e))
            {
                Assert.NotEqual(-1, p.X);
                kept++;
            }
            else
            {
                Assert.Equal(-1, p.X);
                cleared++;
            }
        });
        Assert.True(kept > 0);
        Assert.True(cleared > 0);
        Assert.Equal(EntityCount, kept + cleared);
    }

    [Fact]
    public void ParallelEach_Enabled_SkipsDisabled()
    {
        var world = new World();
        for (int i = 0; i < 128; i++)
        {
            var e = world.Create(new Position { X = i });
            if ((i & 1) == 0)
                world.SetEnabled(e, false);
        }

        world.Query().Enabled().ParallelEach<Position>((Entity _, ref Position p) => p.X = 999);

        world.Query().Each<Position>((Entity e, ref Position p) =>
        {
            if (world.IsEnabled(e))
                Assert.Equal(999, p.X);
            else
                Assert.NotEqual(999, p.X);
        });
    }

    [Fact]
    public void ParallelForEach_WithAny_MatchesUnion()
    {
        var world = new World();
        for (int i = 0; i < 64; i++)
        {
            if ((i & 1) == 0)
                world.Create(new Position { X = i });
            else
                world.Create(new Velocity { X = i });
        }

        int visited = 0;
        world.Query().WithAny<Position, Velocity>().ParallelForEach(_ => Interlocked.Increment(ref visited));
        Assert.Equal(64, visited);
    }

    private static World BuildWorld()
    {
        var world = new World();
        for (int i = 0; i < EntityCount; i++)
            world.Create(new Position { X = i, Y = i * 0.5f }, new Velocity { X = 1, Y = -1 });
        return world;
    }

    private static void AssertSamePositions(World a, World b)
    {
        var listA = a.Query().With<Position>().ToList();
        var listB = b.Query().With<Position>().ToList();
        Assert.Equal(listA.Count, listB.Count);

        for (int i = 0; i < listA.Count; i++)
        {
            var pa = a.Get<Position>(listA[i]);
            var pb = b.Get<Position>(listB[i]);
            Assert.Equal(pa.X, pb.X);
            Assert.Equal(pa.Y, pb.Y);
        }
    }
}
