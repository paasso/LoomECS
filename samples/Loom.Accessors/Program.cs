using Loom.Components;

// [EcsComponent] triggers the Loom.Generators analyzer → world.Get(entity).TypeName accessors.

var world = new World();
var entity = world.Create(new Position { X = 2, Y = 3 }, new Velocity { X = 1, Y = 0 });

ref Position pos = ref world.Get(entity).Position;
pos.X += world.Get(entity).Velocity.X;

Console.WriteLine($"Position via accessor: ({world.Get(entity).Position.X}, {world.Get(entity).Position.Y})");
Console.WriteLine($"HasPosition={world.Get(entity).HasPosition}; HasHealth={world.Get(entity).HasHealth}");

// Generic Get still works:
world.Get<Velocity>(entity).X = 5;
Console.WriteLine($"Velocity.X after generic mutate: {world.Get(entity).Velocity.X}");

[EcsComponent]
public struct Position
{
    public float X, Y;
}

[EcsComponent]
public struct Velocity
{
    public float X, Y;
}

[EcsComponent]
public struct Health
{
    public int Value;
}
