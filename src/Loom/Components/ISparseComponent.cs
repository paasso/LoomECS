namespace Loom.Components
{
    /// <summary>
    /// Marker interface for components that should bypass the archetype graph and live in a
    /// per-type sparse set instead. Use this for components that are added/removed frequently
    /// (tags, transient flags, rare heavy payloads) — mutating them never triggers an archetype move.
    /// Components that don't implement this interface are stored densely, packed into archetype
    /// columns for fast contiguous iteration.
    /// </summary>
    public interface ISparseComponent
    {
    }
}
