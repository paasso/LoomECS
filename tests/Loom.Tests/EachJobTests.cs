namespace Loom.Tests;

public class EachJobTests
{
    private struct MoveJob : IJob<Position, Velocity>
    {
        public float Dt;

        public void Execute(Entity entity, ref Position position, ref Velocity velocity)
        {
            position.X += velocity.X * Dt;
            position.Y += velocity.Y * Dt;
        }
    }

    private struct CountJob : IJob<Position>
    {
        public int Count;

        public void Execute(Entity entity, ref Position position) => Count++;
    }

    private struct DamageJob : IJob<Health>
    {
        public void Execute(Entity entity, ref Health health) => health.Value -= 1;
    }

    [Fact]
    public void Each_MoveJob_IntegratesPositionByVelocity()
    {
        var world = new World();
        var a = world.Create(new Position { X = 0, Y = 0 }, new Velocity { X = 10, Y = 5 });
        var b = world.Create(new Position { X = 1, Y = 2 }, new Velocity { X = 1, Y = -1 });
        world.Create(new Position { X = 100, Y = 100 }); // no Velocity — skipped

        var job = new MoveJob { Dt = 0.5f };
        world.Query().Each<MoveJob, Position, Velocity>(ref job);

        Assert.Equal(5, world.Get<Position>(a).X);
        Assert.Equal(2.5f, world.Get<Position>(a).Y);
        Assert.Equal(1.5f, world.Get<Position>(b).X);
        Assert.Equal(1.5f, world.Get<Position>(b).Y);
    }

    [Fact]
    public void Each_CountJob_RefPreservesMutableState()
    {
        var world = new World();
        const int entityCount = 17;
        for (int i = 0; i < entityCount; i++)
            world.Create(new Position { X = i });

        var job = new CountJob();
        world.Query().Each<CountJob, Position>(ref job);

        Assert.Equal(entityCount, job.Count);
    }

    [Fact]
    public void Each_WithSparseFilter_OnlyVisitsMatchingEntities()
    {
        var world = new World();
        var poisoned = world.Create(new Health { Value = 10 }, new Poisoned { DamagePerTick = 1 });
        var healthy = world.Create(new Health { Value = 10 });

        var job = new DamageJob();
        world.Query().With<Poisoned>().Each<DamageJob, Health>(ref job);

        Assert.Equal(9, world.Get<Health>(poisoned).Value);
        Assert.Equal(10, world.Get<Health>(healthy).Value);
    }
}
