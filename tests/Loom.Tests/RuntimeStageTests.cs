namespace Loom.Tests;

public class RuntimeStageTests
{
    static void Frame(Runtime runtime, params SystemGroup[] stages)
    {
        for (int i = 0; i < stages.Length; i++)
            runtime.Run(stages[i]);
        runtime.EndFrame();
    }

    [Fact]
    public void Frame_RunsOwnedGroupsInCallerOrder()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var init = new SystemGroup("init");
        var systems = new SystemGroup("systems");
        var pres = new SystemGroup("pres");
        var order = new List<string>();

        init.Add(new DelegateSystem(_ => order.Add("init")));
        systems.Add(new DelegateSystem(_ => order.Add("systems")));
        pres.Add(new DelegateSystem(_ => order.Add("pres")));

        Frame(runtime, init, systems, pres);
        Assert.Equal(new[] { "init", "systems", "pres" }, order);
    }

    [Fact]
    public void Run_PlaysRuntimeCommands_BeforeNextGroup()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var init = new SystemGroup();
        var systems = new SystemGroup();
        bool systemsSawEntity = false;

        init.Add(new DelegateSystem((s, _) =>
            s.Commands.Create(new Position { X = 1 })));
        systems.Add(new DelegateSystem((s, _) =>
        {
            int n = 0;
            s.World.Query().Each<Position>((Entity _, ref Position __) => n++);
            systemsSawEntity = n == 1;
        }));

        Frame(runtime, init, systems);
        Assert.True(systemsSawEntity);
        Assert.Equal(1, world.EntityCount);
    }

    sealed class LifeSystem : ISystem, ISystemLifecycle
    {
        public int Creates;
        public int Destroys;
        public int Updates;

        public void OnCreate(Runtime runtime) => Creates++;
        public void OnDestroy(Runtime runtime) => Destroys++;
        public void Update(Runtime runtime, CommandBuffer commands) => Updates++;
    }

    [Fact]
    public void Lifecycle_OnCreateOnce_OnDestroyOnRemove()
    {
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        var life = new LifeSystem();
        systems.Add(life);

        Frame(runtime, systems);
        Frame(runtime, systems);
        Assert.Equal(1, life.Creates);
        Assert.Equal(2, life.Updates);

        Assert.True(systems.Remove(life));
        Assert.Equal(1, life.Destroys);
        Frame(runtime, systems);
        Assert.Equal(2, life.Updates);
    }

    [OrderFirst]
    sealed class FirstSys : ISystem
    {
        private readonly List<string> _order;
        public FirstSys(List<string> order) => _order = order;
        public void Update(Runtime runtime, CommandBuffer commands) => _order.Add("first");
    }

    [OrderLast]
    sealed class LastSys : ISystem
    {
        private readonly List<string> _order;
        public LastSys(List<string> order) => _order = order;
        public void Update(Runtime runtime, CommandBuffer commands) => _order.Add("last");
    }

    sealed class MidSys : ISystem
    {
        private readonly List<string> _order;
        public MidSys(List<string> order) => _order = order;
        public void Update(Runtime runtime, CommandBuffer commands) => _order.Add("mid");
    }

    [Fact]
    public void OrderFirstAndLast_WrapMiddleSystems()
    {
        var order = new List<string>();
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        systems
            .Add(new MidSys(order))
            .Add(new LastSys(order))
            .Add(new FirstSys(order));

        Frame(runtime, systems);
        Assert.Equal(new[] { "first", "mid", "last" }, order);
    }

    [Fact]
    public void NestedGroup_RunsAsSystem()
    {
        var order = new List<string>();
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        var nested = new SystemGroup("nested")
            .Add(new DelegateSystem(_ => order.Add("n1")))
            .Add(new DelegateSystem(_ => order.Add("n2")));

        systems
            .Add(new DelegateSystem(_ => order.Add("before")))
            .Add(nested)
            .Add(new DelegateSystem(_ => order.Add("after")));

        Frame(runtime, systems);
        Assert.Equal(new[] { "before", "n1", "n2", "after" }, order);
    }

    [Fact]
    public void TryGet_FindsRegisteredSystem()
    {
        var systems = new SystemGroup();
        var mid = new MidSys(new List<string>());
        systems.Add(mid);
        Assert.True(systems.TryGet<MidSys>(out var found));
        Assert.Same(mid, found);
        Assert.Same(mid, systems.Get<MidSys>());
    }

    sealed class ParallelBarrierSystem : IParallelSystem
    {
        private readonly Barrier _barrier;
        public bool Arrived;

        public ParallelBarrierSystem(Barrier barrier) => _barrier = barrier;

        public void Update(Runtime runtime, CommandBuffer commands)
        {
            Arrived = _barrier.SignalAndWait(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void ParallelWave_RunsIndependentParallelSystemsConcurrently()
    {
        var barrier = new Barrier(2);
        var a = new ParallelBarrierSystem(barrier);
        var b = new ParallelBarrierSystem(barrier);
        var world = new World();
        var runtime = new Runtime(world);
        var systems = new SystemGroup();
        systems.Add(a).Add(b);

        // Sequential execution would deadlock on a 2-party barrier; completion proves overlap.
        Frame(runtime, systems);
        Assert.True(a.Arrived);
        Assert.True(b.Arrived);
    }
}
