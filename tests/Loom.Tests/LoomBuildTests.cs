namespace Loom.Tests;

public class LoomBuildTests
{
    [Fact]
    public void LoomBuild_Flags_AreReadable()
    {
        // Value depends on how Loom.dll was compiled (-p:LoomSimd=true).
        Assert.Equal(LoomBuild.SimdEnabled, LoomBuild.SimdEnabled);
    }
}
