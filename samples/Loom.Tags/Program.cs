using Loom;

// Fieldless structs are empty tags: presence only (mask bit), no column / sparse set.
// Filter with With/Without — Query.Each rejects empty T (nothing to hand a per-entity ref to).

var world = new World();

var alive = world.Create(new Position { X = 1 }, new Player());
var enemy = world.Create(new Position { X = 2 }, new Enemy());
var both = world.Create(new Position { X = 3 }, new Player(), new Enemy()); // unusual but legal

Console.WriteLine("players:");
world.Query().With<Player>().ForEach(e =>
    Console.WriteLine($"  {e} x={world.Get<Position>(e).X} hasEnemy={world.Has<Enemy>(e)}"));

Console.WriteLine("enemies that are not players:");
world.Query().With<Enemy>().Without<Player>().ForEach(e =>
    Console.WriteLine($"  {e} x={world.Get<Position>(e).X}"));

world.Remove<Player>(both);
Console.WriteLine($"after Remove<Player>, both has Player? {world.Has<Player>(both)}");

// Empty tags still participate in WithAny:
int hits = 0;
world.Query().WithAny<Player, Enemy>().ForEach(_ => hits++);
Console.WriteLine($"WithAny<Player, Enemy> count: {hits}");

struct Position
{
    public float X;
}

struct Player
{
}

struct Enemy
{
}
