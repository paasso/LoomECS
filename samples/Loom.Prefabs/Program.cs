using Loom;

// EntityPrefab / Prefab: reusable recipes. Values only — no father/child links.

var world = new World();

var recipe = new EntityPrefab()
    .Add(new Position { X = 0, Y = 0 })
    .Add(new Velocity { X = 1, Y = 0 })
    .Add(new Poisoned { DamagePerTick = 1 });

Entity a = world.Instantiate(recipe);
Entity b = EntityPrefab.FromEntity(world, a).Instantiate(world);

Console.WriteLine($"instantiate: {a} pos=({world.Get<Position>(a).X},{world.Get<Position>(a).Y})");
Console.WriteLine($"clone:       {b} poison={world.Get<Poisoned>(b).DamagePerTick}");

var batch = new Entity[3];
recipe.InstantiateMany(world, batch);
Console.WriteLine($"InstantiateMany produced {batch.Length} entities");

// Prefab also supports deferred spawn through a CommandBuffer:
var deferred = new Prefab()
    .With(new Position { X = 10, Y = 20 })
    .With(new Velocity { X = -1, Y = 0 });

var commands = world.CreateCommandBuffer();
Entity queued = deferred.Spawn(commands);
Console.WriteLine($"queued {queued} alive before playback? {world.IsAlive(queued)} (id reserved)");
Console.WriteLine($"has Position before playback? {world.Has<Position>(queued)}");
commands.Playback();
Console.WriteLine($"after playback: pos=({world.Get<Position>(queued).X},{world.Get<Position>(queued).Y})");

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
