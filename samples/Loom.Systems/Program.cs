using System.Collections.Concurrent;
using Loom;

// Ordering attributes, DelegateSystem, lifecycle, nested groups, IParallelSystem.
// Own SystemGroups + Runtime.Run / EndFrame — no built-in stages on Runtime.

var world = new World();
var runtime = new Runtime(world);
var init = new SystemGroup("init");
var systems = new SystemGroup("systems");
var presentation = new SystemGroup("presentation");
var order = new ConcurrentQueue<string>();

init.Add(new DelegateSystem(_ => order.Enqueue("init")));
presentation.Add(new DelegateSystem(_ => order.Enqueue("pres")));

var life = new LifeSystem(order);
systems
    .Add(new MidSystem(order))
    .Add(new LastSystem(order))
    .Add(new FirstSystem(order))
    .Add(life)
    .Add(new SystemGroup("nested")
        .Add(new DelegateSystem(_ => order.Enqueue("nested-a")))
        .Add(new DelegateSystem(_ => order.Enqueue("nested-b"))))
    .Add(new ParallelProbe("p1", order))
    .Add(new ParallelProbe("p2", order));

void Frame()
{
    runtime.Run(init);
    runtime.Run(systems);
    runtime.Run(presentation);
    runtime.EndFrame();
}

Frame();
Frame();
Console.WriteLine("tick order:");
foreach (var step in order)
    Console.WriteLine($"  {step}");

systems.Remove(life);
Console.WriteLine($"lifecycle: creates={life.Creates} updates={life.Updates} destroys={life.Destroys}");

[OrderFirst]
sealed class FirstSystem : ISystem
{
    private readonly ConcurrentQueue<string> _order;
    public FirstSystem(ConcurrentQueue<string> order) => _order = order;
    public void Update(Runtime runtime, CommandBuffer commands) => _order.Enqueue("first");
}

sealed class MidSystem : ISystem
{
    private readonly ConcurrentQueue<string> _order;
    public MidSystem(ConcurrentQueue<string> order) => _order = order;
    public void Update(Runtime runtime, CommandBuffer commands) => _order.Enqueue("mid");
}

[OrderLast]
sealed class LastSystem : ISystem
{
    private readonly ConcurrentQueue<string> _order;
    public LastSystem(ConcurrentQueue<string> order) => _order = order;
    public void Update(Runtime runtime, CommandBuffer commands) => _order.Enqueue("last");
}

sealed class LifeSystem : ISystem, ISystemLifecycle
{
    private readonly ConcurrentQueue<string> _order;
    public int Creates, Updates, Destroys;
    public LifeSystem(ConcurrentQueue<string> order) => _order = order;
    public void OnCreate(Runtime runtime)
    {
        Creates++;
        _order.Enqueue("life-create");
    }

    public void OnDestroy(Runtime runtime)
    {
        Destroys++;
        _order.Enqueue("life-destroy");
    }

    public void Update(Runtime runtime, CommandBuffer commands)
    {
        Updates++;
        _order.Enqueue("life-update");
    }
}

sealed class ParallelProbe : IParallelSystem
{
    private readonly string _name;
    private readonly ConcurrentQueue<string> _order;
    public ParallelProbe(string name, ConcurrentQueue<string> order)
    {
        _name = name;
        _order = order;
    }

    public void Update(Runtime runtime, CommandBuffer commands) => _order.Enqueue(_name);
}
