namespace Loom.Internal
{
    /// <summary>
    /// Per-entity bookkeeping for the father/child hierarchy, stored in a
    /// <see cref="SparseSet{T}"/> keyed by entity id — present only for entities that are a father,
    /// a child, or both (most entities are neither and never get an entry, same lazily-populated
    /// side-table pattern as <c>World._sparseMasks</c>). A father's children form a doubly-linked
    /// sibling list (<see cref="PrevSibling"/>/<see cref="NextSibling"/>) rooted at
    /// <see cref="FirstChild"/>, so detaching a child — from the middle of the list, or either end —
    /// is O(1): no array shifting, and no scanning every entity in the world to answer "who are the
    /// children of X". <see cref="Entity.Null"/> (default) doubles as the "no such link" sentinel in
    /// every field, consistent with entity id 0 being reserved as "no entity" everywhere else in
    /// this codebase.
    /// </summary>
    internal struct EntityRelations
    {
        public Entity Father;
        public Entity FirstChild;
        public Entity PrevSibling;
        public Entity NextSibling;
    }
}
