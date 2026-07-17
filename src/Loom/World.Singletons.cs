using System;
using System.Collections.Generic;

namespace Loom
{
    public sealed partial class World
    {
        // Singletons are rare, per-world values (Time, Input, config) — not entities. Type-keyed
        // storage is fine here; this is not a per-entity hot path.
        private readonly Dictionary<Type, ISingletonBox> _singletons = new Dictionary<Type, ISingletonBox>();

        public bool HasSingleton<T>() where T : struct => _singletons.ContainsKey(typeof(T));

        /// <summary>Returns a ref to the singleton value. Throws if it was never set — use
        /// <see cref="GetOrCreateSingleton{T}"/> when a default-initialized value is fine.</summary>
        public ref T GetSingleton<T>() where T : struct
        {
            if (!_singletons.TryGetValue(typeof(T), out var boxed))
                throw new InvalidOperationException($"World has no singleton {typeof(T).Name}.");
            return ref ((SingletonHolder<T>)boxed).Value;
        }

        /// <summary>Returns a ref to the singleton, creating a default-initialized one if missing.</summary>
        public ref T GetOrCreateSingleton<T>() where T : struct
        {
            var type = typeof(T);
            if (!_singletons.TryGetValue(type, out var boxed))
            {
                boxed = new SingletonHolder<T>();
                _singletons[type] = boxed;
            }
            return ref ((SingletonHolder<T>)boxed).Value;
        }

        public void SetSingleton<T>(T value) where T : struct
        {
            var type = typeof(T);
            if (_singletons.TryGetValue(type, out var boxed))
                ((SingletonHolder<T>)boxed).Value = value;
            else
                _singletons[type] = new SingletonHolder<T> { Value = value };
        }

        /// <summary>Removes the singleton if present. Returns false if it wasn't set.</summary>
        public bool RemoveSingleton<T>() where T : struct => _singletons.Remove(typeof(T));

        internal void ClearSingletons() => _singletons.Clear();

        internal int SingletonCount => _singletons.Count;

        /// <summary>Enumerates live singletons for <see cref="WorldSerializer"/> (type + boxed value).</summary>
        internal void ForEachSingleton(Action<Type, object> action)
        {
            foreach (var kv in _singletons)
                action(kv.Key, kv.Value.GetValue());
        }

        internal void SetSingletonBoxed(Type clrType, object value)
        {
            if (clrType == null)
                throw new ArgumentNullException(nameof(clrType));
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (!clrType.IsValueType)
                throw new ArgumentException("Singleton types must be structs.", nameof(clrType));
            if (value.GetType() != clrType)
            {
                throw new ArgumentException(
                    $"Value type {value.GetType().FullName} does not match singleton {clrType.FullName}.",
                    nameof(value));
            }

            if (_singletons.TryGetValue(clrType, out var existing))
                existing.SetValue(value);
            else
            {
                var holderType = typeof(SingletonHolder<>).MakeGenericType(clrType);
                var holder = (ISingletonBox)Activator.CreateInstance(holderType)!;
                holder.SetValue(value);
                _singletons[clrType] = holder;
            }
        }

        private interface ISingletonBox
        {
            object GetValue();
            void SetValue(object value);
        }

        private sealed class SingletonHolder<T> : ISingletonBox where T : struct
        {
            public T Value;
            public object GetValue() => Value;
            public void SetValue(object value) => Value = (T)value;
        }
    }
}
