using Loom;

// ISharedComponent values are interned per World: identical Adds share one instance.
// In-place Get edits affect every holder; Remove+Add switches to a different value.

var world = new World();

var redA = world.Create(new Name { Value = "red-a" });
var redB = world.Create(new Name { Value = "red-b" });
var blue = world.Create(new Name { Value = "blue" });

world.Add(redA, new Material { ShaderId = 1, Roughness = 0.2f });
world.Add(redB, new Material { ShaderId = 1, Roughness = 0.2f }); // same value → same instance
world.Add(blue, new Material { ShaderId = 2, Roughness = 0.8f });

Console.WriteLine($"shared Material instances: {world.SharedInstanceCount<Material>()}"); // 2

ref var sharedRed = ref world.Get<Material>(redA);
sharedRed.Roughness = 0.5f; // mutates the interned value for both red holders
Console.WriteLine($"red-a roughness={world.Get<Material>(redA).Roughness}");
Console.WriteLine($"red-b roughness={world.Get<Material>(redB).Roughness}");
Console.WriteLine($"blue  roughness={world.Get<Material>(blue).Roughness}");

world.Remove<Material>(redB);
world.Add(redB, new Material { ShaderId = 3, Roughness = 0.1f });
Console.WriteLine($"after switch, instances: {world.SharedInstanceCount<Material>()}"); // 3

struct Name
{
    public string Value;
}

struct Material : ISharedComponent
{
    public int ShaderId;
    public float Roughness;
}
