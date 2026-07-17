using Loom;

// Typed sparse relation links (IRelationComponent) — a graph edge, not the father/child tree.
// Destroy(target) clears incoming links of that relation type.

var world = new World();

var owner = world.Create(new Label { Text = "owner" });
var sword = world.Create(new Label { Text = "sword" });
var shield = world.Create(new Label { Text = "shield" });

world.SetRelation<OwnedBy>(sword, owner);
world.SetRelation<OwnedBy>(shield, owner);

if (world.TryGetRelationTarget<OwnedBy>(sword, out var swordOwner))
    Console.WriteLine($"sword owned by {world.Get<Label>(swordOwner).Text}");

Console.WriteLine("items owned by owner:");
world.ForEachRelationSource<OwnedBy>(owner, source =>
    Console.WriteLine($"  {world.Get<Label>(source).Text}"));

world.Destroy(owner);
Console.WriteLine($"after owner destroy, sword has OwnedBy? {world.HasRelation<OwnedBy>(sword)}");
Console.WriteLine($"sword still alive? {world.IsAlive(sword)}");

struct Label
{
    public string Text;
}

struct OwnedBy : IRelationComponent
{
    public Entity Target;
}
