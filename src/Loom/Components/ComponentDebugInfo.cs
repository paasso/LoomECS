using System;

namespace Loom.Components
{
    /// <summary>How a component is stored on an entity — for inspectors / debuggers.</summary>
    public enum ComponentStorageKind : byte
    {
        Dense = 0,
        Sparse = 1,
        Shared = 2,
        /// <summary>Empty tag struct (no fields) — <see cref="ComponentDebugInfo.Value"/> is null.</summary>
        Tag = 3,
    }

    /// <summary>Boxed snapshot of one component on an entity for debug UI.</summary>
    public readonly struct ComponentDebugInfo
    {
        public Type Type { get; }
        /// <summary>Boxed value, or <c>null</c> for <see cref="ComponentStorageKind.Tag"/>.</summary>
        public object? Value { get; }
        public ComponentStorageKind Kind { get; }

        public bool IsTag => Kind == ComponentStorageKind.Tag;

        public ComponentDebugInfo(Type type, object? value, ComponentStorageKind kind)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Value = value;
            Kind = kind;
        }

        public override string ToString() =>
            IsTag ? $"{Type.Name} (tag)" : $"{Type.Name} [{Kind}] = {Value}";
    }

    /// <summary>Debug snapshot of one archetype table (entity count, chunks, dense component types).</summary>
    public readonly struct ArchetypeDebugInfo
    {
        public int Id { get; }
        public int EntityCount { get; }
        public int ChunkCount { get; }
        public Type[] ComponentTypes { get; }

        public ArchetypeDebugInfo(int id, int entityCount, int chunkCount, Type[] componentTypes)
        {
            Id = id;
            EntityCount = entityCount;
            ChunkCount = chunkCount;
            ComponentTypes = componentTypes ?? throw new ArgumentNullException(nameof(componentTypes));
        }

        public override string ToString() =>
            $"Archetype#{Id} entities={EntityCount} chunks={ChunkCount} types={ComponentTypes.Length}";
    }
}
