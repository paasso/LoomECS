namespace Loom.Tests;

public class SystemOrderTests
{
    [UpdateAfter(typeof(SystemA))]
    sealed class SystemB : ISystem
    {
        private readonly List<string> _order;
        public SystemB(List<string> order) => _order = order;
        public void Update(Runtime runtime, CommandBuffer commands) => _order.Add("b");
    }

    sealed class SystemA : ISystem
    {
        private readonly List<string> _order;
        public SystemA(List<string> order) => _order = order;
        public void Update(Runtime runtime, CommandBuffer commands) => _order.Add("a");
    }

    [UpdateBefore(typeof(SystemA))]
    sealed class SystemEarly : ISystem
    {
        private readonly List<string> _order;
        public SystemEarly(List<string> order) => _order = order;
        public void Update(Runtime runtime, CommandBuffer commands) => _order.Add("early");
    }

    static void Frame(Runtime runtime, SystemGroup systems)
    {
        runtime.Run(systems);
        runtime.EndFrame();
    }

    [Fact]
    public void UpdateAfter_ReordersDespiteRegistrationOrder()
    {
        var order = new List<string>();
        var world = new World();
        var sim = new Runtime(world);
        var systems = new SystemGroup()
            .Add(new SystemB(order))
            .Add(new SystemA(order));

        Frame(sim, systems);
        Assert.Equal(new[] { "a", "b" }, order);
    }

    [Fact]
    public void UpdateBefore_RunsEarlier()
    {
        var order = new List<string>();
        var world = new World();
        var sim = new Runtime(world);
        var systems = new SystemGroup()
            .Add(new SystemA(order))
            .Add(new SystemEarly(order));

        Frame(sim, systems);
        Assert.Equal(new[] { "early", "a" }, order);
    }

    [Fact]
    public void Cycle_ThrowsOnRun()
    {
        var world = new World();
        var sim = new Runtime(world);
        var systems = new SystemGroup().Add(new CycleA()).Add(new CycleB());
        Assert.Throws<InvalidOperationException>(() => Frame(sim, systems));
    }

    [UpdateAfter(typeof(CycleB))]
    sealed class CycleA : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands) { }
    }

    [UpdateAfter(typeof(CycleA))]
    sealed class CycleB : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands) { }
    }

    [OrderFirst]
    [UpdateAfter(typeof(AnchorSys))]
    sealed class FirstButAfterAnchor : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands) { }
    }

    sealed class AnchorSys : ISystem
    {
        public void Update(Runtime runtime, CommandBuffer commands) { }
    }

    [Fact]
    public void OrderFirst_PlusUpdateAfterNonFirst_CreatesCycle()
    {
        var world = new World();
        var sim = new Runtime(world);
        var systems = new SystemGroup().Add(new FirstButAfterAnchor()).Add(new AnchorSys());
        Assert.Throws<InvalidOperationException>(() => Frame(sim, systems));
    }

    [Fact]
    public void UpdateAfter_AcrossNestedGroupBoundary_IsIgnoredForOuterPeers()
    {
        var order = new List<string>();
        var world = new World();
        var sim = new Runtime(world);
        var nested = new SystemGroup("nested")
            .Add(new SystemB(order))
            .Add(new SystemA(order));

        var systems = new SystemGroup()
            .Add(nested)
            .Add(new DelegateSystem(_ => order.Add("outer")));

        Frame(sim, systems);
        Assert.Equal(new[] { "a", "b", "outer" }, order);
    }

    [Fact]
    public void SetEnabled_False_SkipsSystem()
    {
        var order = new List<string>();
        var world = new World();
        var sim = new Runtime(world);
        var mid = new MidNamed("mid", order);
        var systems = new SystemGroup().Add(new MidNamed("a", order)).Add(mid);
        systems.SetEnabled(mid, false);

        Frame(sim, systems);
        Assert.Equal(new[] { "a" }, order);

        systems.SetEnabled(mid, true);
        Frame(sim, systems);
        Assert.Equal(new[] { "a", "a", "mid" }, order);
    }

    sealed class MidNamed : ISystem
    {
        private readonly string _name;
        private readonly List<string> _order;
        public MidNamed(string name, List<string> order)
        {
            _name = name;
            _order = order;
        }

        public void Update(Runtime runtime, CommandBuffer commands) => _order.Add(_name);
    }
}
