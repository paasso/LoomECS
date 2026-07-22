namespace Loom.Benchmarks;

// Identical unmanaged payloads are used for every ECS. Marker structs are intentionally empty.
public struct Position : global::Friflo.Engine.ECS.IComponent { public float X, Y, Z; }
public struct Velocity : global::Friflo.Engine.ECS.IComponent { public float X, Y, Z; }
public struct Rotation : global::Friflo.Engine.ECS.IComponent { public float X, Y, Z; }
public struct Status : global::Friflo.Engine.ECS.IComponent { public int Value; }
public struct Excluded : global::Friflo.Engine.ECS.IComponent { }
public struct GroupA : global::Friflo.Engine.ECS.IComponent { }
public struct GroupB : global::Friflo.Engine.ECS.IComponent { }
public struct GroupC : global::Friflo.Engine.ECS.IComponent { }
public struct GroupD : global::Friflo.Engine.ECS.IComponent { }
public struct SparseStatus : global::Friflo.Engine.ECS.IComponent, global::Loom.Components.ISparseComponent { public int Value; }
public struct Material : global::Friflo.Engine.ECS.IComponent, global::Loom.Components.ISharedComponent { public int Id; }

internal static class Checksum
{
    // Returning a value from each benchmark prevents dead-code elimination.
    public static float Position(in Position position) => position.X + position.Y + position.Z;
}
