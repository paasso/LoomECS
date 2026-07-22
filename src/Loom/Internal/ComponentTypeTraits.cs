using System;
using System.Reflection;

namespace Loom.Internal
{
    /// <summary>
    /// Per-closed-generic-type cache of intrinsic facts about a component type T — properties that
    /// are the same answer in every World, unlike a component's <em>id</em> (world-scoped, see
    /// <see cref="ComponentTypeRegistry"/>). Caching them here means the reflection checks below run
    /// at most once per T for the whole process (the CLR guarantees a generic type's static
    /// constructor runs at most once, lazily and thread-safely) instead of once per (World, T) pair,
    /// and callers get a plain static field read — about as cheap as a check can get.
    /// </summary>
    internal static class ComponentTypeTraits<T> where T : struct
    {
        public static readonly Type ComponentType = typeof(T);
        public static readonly int DeterministicHash = ComponentType.ComputeDeterministicHash();
        public static readonly bool IsSparse = typeof(ISparseComponent).IsAssignableFrom(typeof(T));
        public static readonly bool IsShared = typeof(ISharedComponent).IsAssignableFrom(typeof(T));
        public static readonly bool IsRelation = typeof(IRelationComponent).IsAssignableFrom(typeof(T));

        /// <summary>Sparse and shared both live outside archetype columns and share the per-entity
        /// sparse <see cref="ComponentMask"/> id space.</summary>
        public static readonly bool UsesSparseMask = IsSparse || IsShared;

        // Field initializers above run before this explicit static constructor body (both are part
        // of the same type initializer), so DeterministicHash/ComponentType are already set here.
        // Checking uniqueness at this point — once per closed T, for the whole process — catches a
        // collision the moment any two colliding component types are ever both touched, rather than
        // only when they happen to be registered in the same World (see ComponentTypeRegistry,
        // which only ever sees the T values one particular World actually uses).
        static ComponentTypeTraits()
        {
            if (IsSparse && IsShared)
            {
                throw new InvalidOperationException(
                    $"Component type '{ComponentType.FullName}' cannot implement both " +
                    $"{nameof(ISparseComponent)} and {nameof(ISharedComponent)}.");
            }

            if (IsRelation && !IsSparse)
            {
                throw new InvalidOperationException(
                    $"Relation component '{ComponentType.FullName}' must implement {nameof(ISparseComponent)} " +
                    $"(via {nameof(IRelationComponent)}).");
            }

            if (IsRelation && IsShared)
            {
                throw new InvalidOperationException(
                    $"Relation component '{ComponentType.FullName}' cannot also be {nameof(ISharedComponent)}.");
            }

            ComponentTypeHashRegistry.RegisterOrThrow(DeterministicHash, ComponentType);
        }

        /// <summary>True when T has no instance fields — a pure tag/marker with no data
        /// (<c>struct Dead { }</c>, <c>struct Poisoned : ISparseComponent { }</c>, ...). Every
        /// instance of such a type is indistinguishable from every other, so storing one per entity
        /// — an archetype column, or a sparse-set slot — would be pure waste: presence alone (a bit
        /// in <c>Archetype.Mask</c> or <c>EntityRecord.SparseMask</c>) is the entire story.
        /// World.Add/Get skip all backing storage for such a T and hand back a ref to
        /// <see cref="EmptyValue"/> instead — one shared instance for the whole process, which is
        /// safe precisely because there are no fields for two "different" values to disagree on.</summary>
        public static readonly bool IsEmpty = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0;

        public static T EmptyValue;
    }
}
