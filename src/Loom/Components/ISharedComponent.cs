namespace Loom.Components
{
    /// <summary>
    /// Marker for components whose values are interned per <see cref="World"/>: many entities can
    /// share one instance. Presence is tracked like <see cref="ISparseComponent"/> (sparse mask —
    /// no archetype move). <see cref="World.Get{T}"/> returns a ref into the shared store, so
    /// mutating fields updates every entity that holds that instance.
    /// </summary>
    /// <remarks>
    /// Mutually exclusive with <see cref="ISparseComponent"/>. Prefer <c>Remove</c> + <c>Add</c>
    /// when an entity should switch to a different logical value (re-intern); in-place field edits
    /// change the shared instance for all holders.
    /// </remarks>
    public interface ISharedComponent
    {
    }
}
