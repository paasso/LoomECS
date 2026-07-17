
using Xunit;

namespace Loom.GeneratorTests;

public class ComponentAccessTests
{
    [Fact]
    public void WorldGet_Entity_ExposesGeneratedRefProperties()
    {
        var world = new World();
        var e = world.Create(new GenPosition { X = 3, Y = 4 }, new GenVelocity { X = 1, Y = 0 });

        ComponentAccess access = world.Get(e);
        Assert.True(access.HasGenPosition);
        Assert.True(access.HasGenVelocity);
        Assert.Equal(3, access.GenPosition.X);

        access.GenPosition.X += access.GenVelocity.X;
        Assert.Equal(4, world.Get<GenPosition>(e).X);
    }
}
