using System;

namespace Loom.Systems
{
    /// <summary>
    /// Declares that this system should run after <see cref="SystemType"/> when
    /// <see cref="SystemGroup"/> sorts by dependencies. Registration order is the tie-breaker
    /// among systems with no relative constraint.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class UpdateAfterAttribute : Attribute
    {
        public Type SystemType { get; }

        public UpdateAfterAttribute(Type systemType) =>
            SystemType = systemType ?? throw new ArgumentNullException(nameof(systemType));
    }

    /// <summary>
    /// Declares that this system should run before <see cref="SystemType"/> when
    /// <see cref="SystemGroup"/> sorts by dependencies.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class UpdateBeforeAttribute : Attribute
    {
        public Type SystemType { get; }

        public UpdateBeforeAttribute(Type systemType) =>
            SystemType = systemType ?? throw new ArgumentNullException(nameof(systemType));
    }

    /// <summary>
    /// Runs this system before siblings that are neither <see cref="OrderFirstAttribute"/> nor
    /// constrained ahead of it by UpdateAfter/UpdateBefore.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class OrderFirstAttribute : Attribute
    {
    }

    /// <summary>
    /// Runs this system after siblings that are neither <see cref="OrderLastAttribute"/> nor
    /// constrained after it by UpdateAfter/UpdateBefore.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class OrderLastAttribute : Attribute
    {
    }
}
