namespace Loom.Tests;

public struct Position
{
    public float X, Y;
}

public struct Velocity
{
    public float X, Y;
}

public struct Health
{
    public int Value;
}

/// <summary>Sparse: applied to few entities and churns often (classic status-effect tag).</summary>
public struct Poisoned : ISparseComponent
{
    public int DamagePerTick;
}

/// <summary>Sparse marker with no data.</summary>
public struct Burning : ISparseComponent
{
}

/// <summary>Dense marker with no data — a tag that participates in the archetype graph
/// (unlike Burning) but, like Burning, has no per-entity value to store.</summary>
public struct Dead
{
}

/// <summary>Per-world value used by serialization singleton tests (not an entity component).</summary>
public struct FrameTime
{
    public int Frame;
    public float Delta;
}

/// <summary>Shared: identical values intern to one instance for many entities.</summary>
public struct Material : ISharedComponent
{
    public int ShaderId;
    public float Roughness;
}

// Plain data-bearing dense components with no semantic meaning beyond their arity position —
// exist purely to exercise Query.Each<T1..T8> at higher arities than Position/Velocity/Health cover.
public struct Comp3 { public int Value; }
public struct Comp4 { public int Value; }
public struct Comp5 { public int Value; }
public struct Comp6 { public int Value; }
public struct Comp7 { public int Value; }
public struct Comp8 { public int Value; }
