namespace Loom.Components
{
    /// <summary>
    /// Sparse link component: associates the owning entity with another via a public
    /// <c>Entity Target</c> field (required). Adding/removing never moves archetypes.
    /// Hierarchy (<see cref="World.SetFather"/>) stays separate.
    /// </summary>
    public interface IRelationComponent : ISparseComponent
    {
    }
}
