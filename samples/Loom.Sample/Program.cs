using Loom;

var world = new World();
var runtime = new Runtime(world);
var systems = new SystemGroup();
world.SetSingleton(new FrameTime { Delta = 1f, Frame = 0 });

for (int i = 0; i < 5; i++)
    world.Create(new Position { X = i, Y = 0 }, new Velocity { X = 0, Y = 1 }, new Health { Value = 3 });

var entities = world.Query().With<Position>().ToList();
world.Add(entities[0], new Poisoned { DamagePerTick = 2 });
world.Add(entities[2], new Poisoned { DamagePerTick = 5 });

runtime.Subscribe((Runtime _, in EntityDied e) =>
    Console.WriteLine($"  [event] {e.Entity} died"));

systems
    .Add(new AdvanceTimeSystem())
    .Add(new MotionSystem())
    .Add(new PoisonSystem());

void Frame()
{
    runtime.Run(systems);
    runtime.EndFrame();
}

Console.WriteLine("-- tick 1 --");
Frame();

Console.WriteLine();
Console.WriteLine("-- tick 2 --");
Frame();

Console.WriteLine();
Console.WriteLine("-- tick 3 (poisoned entities should die via CommandBuffer + event) --");
Frame();

sealed class AdvanceTimeSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        ref var time = ref world.GetSingleton<FrameTime>();
        time.Frame++;
        Console.WriteLine($"frame {time.Frame} (dt={time.Delta})");
    }
}

sealed class MotionSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        float dt = world.GetSingleton<FrameTime>().Delta;
        world.Query().Each<Position, Velocity>((Entity _, ref Position pos, ref Velocity vel) =>
        {
            pos.X += vel.X * dt;
            pos.Y += vel.Y * dt;
        });
    }
}

sealed class PoisonSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        world.Query().With<Poisoned>().Each<Health>((Entity entity, ref Health health) =>
        {
            ref var poison = ref world.Get<Poisoned>(entity);
            health.Value -= poison.DamagePerTick;
            Console.WriteLine($"  {entity} health -> {health.Value} (poison {poison.DamagePerTick})");
            if (health.Value <= 0)
            {
                commands.Destroy(entity);
                runtime.Emit(new EntityDied { Entity = entity });
            }
        });
    }
}

struct Position
{
    public float X, Y;
}

struct Velocity
{
    public float X, Y;
}

struct Health
{
    public int Value;
}

struct Poisoned : ISparseComponent
{
    public int DamagePerTick;
}

struct FrameTime
{
    public float Delta;
    public int Frame;
}

struct EntityDied
{
    public Entity Entity;
}
