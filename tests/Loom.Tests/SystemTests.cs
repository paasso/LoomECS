namespace Loom.Tests;

public class SystemTests
{
    static void Frame(Runtime runtime, SystemGroup systems)
    {
        runtime.Run(systems);
        runtime.EndFrame();
    }

    [Fact]
    public void Run_RunsSystemsInRegistrationOrder()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        var order = new List<string>();

        systems
            .Add(new DelegateSystem(_ => order.Add("a")))
            .Add(new DelegateSystem(_ => order.Add("b")))
            .Add(new DelegateSystem(_ => order.Add("c")));

        Frame(runtime, systems);

        Assert.Equal(new[] { "a", "b", "c" }, order);
    }

    [Fact]
    public void SetEnabled_False_SkipsSystemOnRun()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        var order = new List<string>();
        var middle = new DelegateSystem(_ => order.Add("b"));

        systems
            .Add(new DelegateSystem(_ => order.Add("a")))
            .Add(middle)
            .Add(new DelegateSystem(_ => order.Add("c")));

        systems.SetEnabled(middle, false);
        Frame(runtime, systems);

        Assert.Equal(new[] { "a", "c" }, order);
        Assert.False(systems.IsEnabled(middle));
    }

    [Fact]
    public void StructuralChangeInEarlierSystem_IsVisibleToLaterSystem()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();

        systems
            .Add(new DelegateSystem(s => s.World.Create(new Position { X = 1 })))
            .Add(new DelegateSystem(s =>
            {
                int count = 0;
                s.World.Query().Each<Position>((Entity _, ref Position p) =>
                {
                    count++;
                    Assert.Equal(1, p.X);
                });
                Assert.Equal(1, count);
            }));

        Frame(runtime, systems);
        Assert.Equal(1, world.EntityCount);
    }

    [Fact]
    public void SystemGroup_CanRunAgainstDifferentRuntimes()
    {
        var group = new SystemGroup();
        var seen = new List<Runtime>();
        group.Add(new DelegateSystem(s => seen.Add(s)));

        var a = new Runtime(new World());
        var b = new Runtime(new World());
        a.Run(group);
        b.Run(group);

        Assert.Equal(new[] { a, b }, seen);
    }

    [Fact]
    public void SystemGroups_AreIndependentPerOwner()
    {
        var a = new Runtime(new World());
        var b = new Runtime(new World());
        var aSystems = new SystemGroup();
        var bSystems = new SystemGroup();
        int aTicks = 0;
        int bTicks = 0;

        aSystems.Add(new DelegateSystem(_ => aTicks++));
        bSystems.Add(new DelegateSystem(_ => bTicks++));

        Frame(a, aSystems);
        Frame(a, aSystems);
        Frame(b, bSystems);

        Assert.Equal(2, aTicks);
        Assert.Equal(1, bTicks);
    }

    [Fact]
    public void Add_SameInstanceTwice_Throws()
    {
        var group = new SystemGroup();
        var system = new DelegateSystem(_ => { });
        group.Add(system);
        Assert.Throws<InvalidOperationException>(() => group.Add(system));
    }

    [Fact]
    public void Remove_DropsSystemFromSubsequentRuns()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        int calls = 0;
        var system = new DelegateSystem(_ => calls++);

        systems.Add(system);
        Frame(runtime, systems);
        Assert.True(systems.Remove(system));
        Frame(runtime, systems);

        Assert.Equal(1, calls);
        Assert.False(systems.Contains(system));
    }

    [Fact]
    public void SystemsAddedDuringRun_WaitUntilNextFrame()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        var order = new List<string>();
        var late = new DelegateSystem(_ => order.Add("late"));

        systems.Add(new DelegateSystem(_ =>
        {
            order.Add("first");
            if (!systems.Contains(late))
                systems.Add(late);
        }));

        Frame(runtime, systems);
        Assert.Equal(new[] { "first" }, order);

        Frame(runtime, systems);
        Assert.Equal(new[] { "first", "first", "late" }, order);
    }

    [Fact]
    public void MotionAndPoisonSystems_MatchSampleBehaviour()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        for (int i = 0; i < 3; i++)
            world.Create(new Position { X = i, Y = 0 }, new Velocity { X = 0, Y = 1 });

        var entities = world.Query().With<Position>().ToList();
        world.Add(entities[0], new Poisoned { DamagePerTick = 2 });

        systems
            .Add(new DelegateSystem(s =>
            {
                s.World.Query().Each<Position, Velocity>((Entity _, ref Position p, ref Velocity v) =>
                {
                    p.X += v.X;
                    p.Y += v.Y;
                });
            }))
            .Add(new DelegateSystem(s =>
            {
                s.World.Query().With<Poisoned>().Each<Position>((Entity e, ref Position _) =>
                {
                    ref var poison = ref s.World.Get<Poisoned>(e);
                    poison.DamagePerTick -= 1;
                    if (poison.DamagePerTick <= 0)
                        s.World.Remove<Poisoned>(e);
                });
            }));

        Frame(runtime, systems);

        Assert.Equal(1, world.Get<Position>(entities[0]).Y);
        Assert.Equal(1, world.Get<Poisoned>(entities[0]).DamagePerTick);

        Frame(runtime, systems);
        Assert.False(world.Has<Poisoned>(entities[0]));
    }
}
