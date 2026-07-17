namespace Loom.Tests;

public class CommandBufferTests
{
    static void Frame(Runtime runtime, SystemGroup systems)
    {
        runtime.Run(systems);
        runtime.EndFrame();
    }

    [Fact]
    public void DestroyDuringEach_AppliesAfterPlayback()
    {
        var world = new World();
        var sim = new Runtime(world);
        var keep = world.Create(new Position { X = 1 });
        var drop = world.Create(new Position { X = 2 });

        world.Query().Each<Position>((Entity e, ref Position p) =>
        {
            if (p.X == 2)
                sim.Commands.Destroy(e);
        });

        Assert.True(world.IsAlive(drop));
        sim.Commands.Playback();

        Assert.True(world.IsAlive(keep));
        Assert.False(world.IsAlive(drop));
        Assert.Equal(1, world.EntityCount);
    }

    [Fact]
    public void CreateAndAdd_BecomeQueryableAfterPlayback()
    {
        var world = new World();
        var sim = new Runtime(world);
        var e = sim.Commands.Create(new Position { X = 9 }, new Velocity { X = 3 });

        Assert.True(world.IsAlive(e));
        Assert.False(world.Has<Position>(e)); // reserved, not placed yet

        int before = 0;
        world.Query().Each<Position>((Entity _, ref Position __) => before++);
        Assert.Equal(0, before);

        sim.Commands.Playback();

        Assert.True(world.Has<Position>(e));
        Assert.Equal(9, world.Get<Position>(e).X);
        Assert.Equal(3, world.Get<Velocity>(e).X);
    }

    [Fact]
    public void EndFrame_AfterRun_PlaysSystemBuffers()
    {
        var world = new World();
        var sim = new Runtime(world);
        var systems = new SystemGroup();
        var e = world.Create(new Health { Value = 1 });

        systems.Add(new DelegateSystem((s, commands) =>
        {
            s.World.Query().Each<Health>((Entity ent, ref Health h) =>
            {
                h.Value--;
                if (h.Value <= 0)
                    commands.Destroy(ent);
            });
        }));

        Frame(sim, systems);

        Assert.False(world.IsAlive(e));
        Assert.Equal(0, sim.Commands.PendingCount);
    }

    [Fact]
    public void PerSystemBuffer_PlaysBackBeforeNextSystem()
    {
        var world = new World();
        var sim = new Runtime(world);
        var systems = new SystemGroup();
        var victim = world.Create(new Position());
        bool secondSawAlive = true;

        systems
            .Add(new DelegateSystem((_, commands) => commands.Destroy(victim)))
            .Add(new DelegateSystem((s, _) => secondSawAlive = s.World.IsAlive(victim)));

        Frame(sim, systems);

        Assert.False(secondSawAlive);
        Assert.False(world.IsAlive(victim));
    }

    [Fact]
    public void Clear_DestroysReservedEntities()
    {
        var world = new World();
        var sim = new Runtime(world);
        var e = sim.Commands.Create();
        Assert.Equal(1, world.EntityCount);

        sim.Commands.Clear();

        Assert.False(world.IsAlive(e));
        Assert.Equal(0, world.EntityCount);
        Assert.Equal(0, sim.Commands.PendingCount);
    }

    [Fact]
    public void Remove_AppliesOnPlayback()
    {
        var world = new World();
        var sim = new Runtime(world);
        var e = world.Create(new Position(), new Velocity { X = 1 });

        sim.Commands.Remove<Velocity>(e);
        Assert.True(world.Has<Velocity>(e));

        sim.Commands.Playback();
        Assert.False(world.Has<Velocity>(e));
        Assert.True(world.Has<Position>(e));
    }
}
