namespace Loom.Tests;

public class EventTests
{
    private struct Damaged
    {
        public int Amount;
    }

    [Fact]
    public void Emit_DoesNotInvokeUntilFlush()
    {
        var world = new World();
        var sim = new Runtime(world);
        int calls = 0;
        sim.Subscribe((Runtime _, in Damaged e) => calls += e.Amount);

        sim.Emit(new Damaged { Amount = 3 });
        Assert.Equal(0, calls);

        sim.FlushEvents();
        Assert.Equal(3, calls);
    }

    [Fact]
    public void EndFrame_FlushesEventsAfterSystemsAndCommands()
    {
        var world = new World();
        var sim = new Runtime(world);
        var systems = new SystemGroup();
        var order = new List<string>();

        sim.Subscribe((Runtime _, in Damaged __) => order.Add("event"));

        systems.Add(new DelegateSystem((s, commands) =>
        {
            order.Add("system");
            commands.Create(new Position());
            s.Emit(new Damaged { Amount = 1 });
        }));

        sim.Run(systems);
        sim.EndFrame();

        Assert.Equal(new[] { "system", "event" }, order);
        Assert.Equal(1, world.EntityCount); // command played before events
    }

    [Fact]
    public void EmitDuringFlush_IsDeliveredBeforeFlushReturns()
    {
        var world = new World();
        var sim = new Runtime(world);
        var amounts = new List<int>();

        sim.Subscribe((Runtime s, in Damaged e) =>
        {
            amounts.Add(e.Amount);
            if (e.Amount == 1)
                s.Emit(new Damaged { Amount = 2 });
        });

        sim.Emit(new Damaged { Amount = 1 });
        sim.FlushEvents();

        Assert.Equal(new[] { 1, 2 }, amounts);
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        var world = new World();
        var sim = new Runtime(world);
        int calls = 0;
        RuntimeEventHandler<Damaged> handler = (Runtime _, in Damaged __) => calls++;

        sim.Subscribe(handler);
        sim.Emit(new Damaged());
        sim.FlushEvents();
        Assert.Equal(1, calls);

        sim.Unsubscribe(handler);
        sim.Emit(new Damaged());
        sim.FlushEvents();
        Assert.Equal(1, calls);
    }
}
