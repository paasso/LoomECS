using Loom;

// Own three SystemGroups at different rates (Runtime only provides Run + EndFrame):
//   init         — once per second (spawn / refresh)
//   simulation   — fixed 20 Hz
//   presentation — every display frame (~60 Hz here)

const float DisplayHz = 60f;
const float DisplayDt = 1f / DisplayHz;
const float InitInterval = 1f;
const float SimHz = 20f;
const float SimDt = 1f / SimHz;
const float Duration = 3f;

var world = new World();
var runtime = new Runtime(world);
var init = new SystemGroup("init");
var simulation = new SystemGroup("simulation");
var presentation = new SystemGroup("presentation");

world.SetSingleton(new FrameTime { Delta = DisplayDt });
world.SetSingleton(new StageCounters());

init.Add(new WaveSpawnSystem());
simulation.Add(new MotionSystem());
presentation.Add(new PresentSystem());

float initCooldown = 0f;
float simAccum = 0f;
int displayFrames = 0;

for (float t = 0f; t < Duration; t += DisplayDt)
{
    displayFrames++;
    world.GetSingleton<FrameTime>().Delta = DisplayDt;

    initCooldown -= DisplayDt;
    if (initCooldown <= 0f)
    {
        runtime.Run(init);
        initCooldown += InitInterval;
    }

    simAccum += DisplayDt;
    while (simAccum >= SimDt)
    {
        world.GetSingleton<FrameTime>().Delta = SimDt;
        runtime.Run(simulation);
        simAccum -= SimDt;
    }

    world.GetSingleton<FrameTime>().Delta = DisplayDt;
    runtime.Run(presentation);
    runtime.EndFrame();
}

ref readonly var counters = ref world.GetSingleton<StageCounters>();
Console.WriteLine($"display frames (~{DisplayHz} Hz × {Duration}s): {displayFrames}");
Console.WriteLine($"init runs (~1 Hz):                           {counters.InitRuns}");
Console.WriteLine($"simulation runs (~{SimHz} Hz):                 {counters.SimRuns}");
Console.WriteLine($"presentation runs (every frame):             {counters.PresentRuns}");
Console.WriteLine($"entities alive after waves:                  {world.EntityCount}");
Console.WriteLine($"last presented positions sampled:            {counters.LastPresented}");

public struct FrameTime
{
    public float Delta;
}

public struct StageCounters
{
    public int InitRuns;
    public int SimRuns;
    public int PresentRuns;
    public int LastPresented;
}

public struct Position
{
    public float X;
}

public struct Velocity
{
    public float X;
}

sealed class WaveSpawnSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        ref var counters = ref world.GetSingleton<StageCounters>();
        counters.InitRuns++;

        for (int i = 0; i < 3; i++)
        {
            commands.Create(
                new Position { X = i },
                new Velocity { X = 1f });
        }
    }
}

sealed class MotionSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        ref var counters = ref world.GetSingleton<StageCounters>();
        counters.SimRuns++;

        float dt = world.GetSingleton<FrameTime>().Delta;
        world.Query().Each<Position, Velocity>((Entity _, ref Position p, ref Velocity v) =>
        {
            p.X += v.X * dt;
        });
    }
}

sealed class PresentSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        ref var counters = ref world.GetSingleton<StageCounters>();
        counters.PresentRuns++;

        int n = 0;
        world.Query().Each<Position>((Entity _, ref Position _) => n++);
        counters.LastPresented = n;
    }
}
