using System;
using System.Reflection;

namespace Loom.Internal
{
    /// <summary>Reflects the required <c>Entity Target</c> field on <typeparamref name="T"/>.</summary>
    internal static class RelationAccess<T> where T : struct, IRelationComponent
    {
        private static readonly FieldInfo TargetField;

        static RelationAccess()
        {
            TargetField = typeof(T).GetField(
                             "Target",
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? throw new InvalidOperationException(
                             $"Relation component '{typeof(T).FullName}' must declare a field " +
                             $"named Target of type {nameof(Entity)}.");

            if (TargetField.FieldType != typeof(Entity))
            {
                throw new InvalidOperationException(
                    $"Relation component '{typeof(T).FullName}'.Target must be of type {nameof(Entity)}, " +
                    $"not {TargetField.FieldType.FullName}.");
            }
        }

        public static Entity GetTarget(in T value)
        {
            object boxed = value;
            return (Entity)TargetField.GetValue(boxed)!;
        }

        public static T Create(Entity target)
        {
            object boxed = default(T)!;
            TargetField.SetValue(boxed, target);
            return (T)boxed;
        }
    }
}
