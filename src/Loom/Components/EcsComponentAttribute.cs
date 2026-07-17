using System;

namespace Loom.Components
{
    /// <summary>
    /// Marks a component struct for <c>Loom.Generators</c>: the source generator emits a
    /// typed <c>world.Get(entity).TypeName</c> accessor (ref property) for each marked type in the
    /// consuming assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class EcsComponentAttribute : Attribute
    {
    }
}
