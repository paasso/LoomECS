namespace Loom.Entities
{
    /// <summary>Allocation-free enumerable over a father's direct children, in most-recently-attached-first
    /// order (each <see cref="World.SetFather"/> call inserts at the head of the sibling list). Returned by
    /// <see cref="World.GetChildren"/>; walks <c>World</c>'s internal sibling links directly instead of
    /// materializing a list.</summary>
    public readonly struct ChildEnumerable
    {
        private readonly World _world;
        private readonly Entity _father;

        internal ChildEnumerable(World world, Entity father)
        {
            _world = world;
            _father = father;
        }

        public ChildEnumerator GetEnumerator() => new ChildEnumerator(_world, _father);
    }

    public struct ChildEnumerator
    {
        private readonly World _world;
        private Entity _next;

        public Entity Current { get; private set; }

        internal ChildEnumerator(World world, Entity father)
        {
            _world = world;
            _next = world.GetRelationsOrDefault(father.Id).FirstChild;
            Current = Entity.Null;
        }

        public bool MoveNext()
        {
            if (_next.IsNull)
                return false;

            Current = _next;
            _next = _world.GetRelationsOrDefault(_next.Id).NextSibling;
            return true;
        }
    }
}
