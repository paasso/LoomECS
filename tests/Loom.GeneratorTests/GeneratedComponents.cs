using Loom;

namespace Loom.GeneratorTests;

[EcsComponent]
public struct GenPosition
{
    public float X, Y;
}

[EcsComponent]
public struct GenVelocity
{
    public float X, Y;
}
