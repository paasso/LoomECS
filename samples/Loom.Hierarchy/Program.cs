using Loom;

// Father/child tree: one father per entity, independent of components.
// Destroy(father) cascades to every descendant.

var world = new World();

var root = world.Create(new Label { Text = "root" });
var left = world.Create(new Label { Text = "left" });
var right = world.Create(new Label { Text = "right" });
var leaf = world.Create(new Label { Text = "leaf" });

world.SetFather(left, root);
world.SetFather(right, root);
world.SetFather(leaf, left);

Console.WriteLine($"father of leaf: {world.Get<Label>(world.GetFather(leaf)).Text}");
Console.WriteLine($"root has children? {world.HasChildren(root)}");

Console.WriteLine("children of root:");
foreach (var child in world.GetChildren(root))
    Console.WriteLine($"  {world.Get<Label>(child).Text}");

world.RemoveFather(right);
Console.WriteLine($"after detach, right is root? {!world.HasFather(right)}");

world.Destroy(left); // cascades to leaf
Console.WriteLine($"left alive? {world.IsAlive(left)}; leaf alive? {world.IsAlive(leaf)}; root alive? {world.IsAlive(root)}");

struct Label
{
    public string Text;
}
