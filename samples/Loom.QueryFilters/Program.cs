using Loom;

// WithAny (OR), Enabled()/Disabled, and QueryFilter mask caching (ids are per-world).

var world = new World();

var melee = world.Create(new Position { X = 1 }, new Melee());
var ranged = world.Create(new Position { X = 2 }, new Ranged());
var both = world.Create(new Position { X = 3 }, new Melee(), new Ranged());
var dead = world.Create(new Position { X = 4 }, new Melee());
world.SetEnabled(dead, false);

Console.WriteLine("WithAny<Melee, Ranged> (includes disabled):");
world.Query().WithAny<Melee, Ranged>().ForEach(e =>
    Console.WriteLine($"  id={e.Id} enabled={world.IsEnabled(e)}"));

Console.WriteLine("Enabled().WithAny<Melee, Ranged>:");
world.Query().Enabled().WithAny<Melee, Ranged>().ForEach(e =>
    Console.WriteLine($"  id={e.Id} x={world.Get<Position>(e).X}"));

// Cache masks once, reuse every frame:
QueryFilter fighters = world.Query().Enabled().WithAny<Melee, Ranged>().ToFilter();
int count = 0;
world.Query(fighters).Each<Position>((Entity _, ref Position p) =>
{
    count++;
    p.X += 10;
});
Console.WriteLine($"QueryFilter fighters updated {count} entities");

world.ClearEntities();
Console.WriteLine($"after ClearEntities, alive fighters: {CountAlive(world, fighters)}");

static int CountAlive(World world, in QueryFilter filter)
{
    int n = 0;
    world.Query(filter).ForEach(_ => n++);
    return n;
}

struct Position
{
    public float X;
}

struct Melee
{
}

struct Ranged
{
}
