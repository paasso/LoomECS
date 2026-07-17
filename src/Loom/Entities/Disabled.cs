namespace Loom.Entities
{
    /// <summary>
    /// Sparse enable flag. Present ⇒ entity is disabled. Queries include disabled entities
    /// unless narrowed with <see cref="Query.Enabled"/>.
    /// </summary>
    public struct Disabled : ISparseComponent
    {
    }
}
