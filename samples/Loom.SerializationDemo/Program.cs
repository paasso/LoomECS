using Loom;

// LoomECS.Serialization: register every component/singleton you want in the snapshot.
// Load requires a pristine World so ids/versions restore exactly.

var world = new World();
world.SetSingleton(new FrameTime { Delta = 1f / 60f, Frame = 42 });

var e = world.Create(new Position { X = 3, Y = 4 }, new Velocity { X = 1, Y = 0 });
world.Add(e, new Poisoned { DamagePerTick = 2 });
world.Add(e, new Stunned()); // empty tag

var child = world.Create(new Position { X = 0, Y = 1 });
world.SetFather(child, e);

var serializer = new WorldSerializer()
    .Register<Position>()
    .Register<Velocity>()
    .Register<Poisoned>()
    .Register<Stunned>()
    .RegisterSingleton<FrameTime>();

string json = serializer.SaveToJson(world);
Console.WriteLine("saved JSON (truncated):");
Console.WriteLine(json.Length > 200 ? json[..200] + "…" : json);

var loaded = new World(); // must never have Create/Destroy'd
serializer.LoadFromJson(loaded, json);

Console.WriteLine($"loaded frame={loaded.GetSingleton<FrameTime>().Frame}");
Console.WriteLine($"entity {e} alive? {loaded.IsAlive(e)}; poison={loaded.Get<Poisoned>(e).DamagePerTick}");
Console.WriteLine($"has Stunned? {loaded.Has<Stunned>(e)}; child father ok? {loaded.GetFather(child) == e}");

struct Position
{
    public float X, Y;
}

struct Velocity
{
    public float X, Y;
}

struct Poisoned : ISparseComponent
{
    public int DamagePerTick;
}

struct Stunned
{
}

struct FrameTime
{
    public float Delta;
    public int Frame;
}
