namespace Loom.Tests;

/// <summary>Written to be agnostic of which width tier (ECS_MASK_64/ECS_MASK_128/default 256) the
/// library was compiled with — everything is driven off <see cref="ComponentMask.Capacity"/>, so
/// this suite validates whichever tier is actually active, including the word-boundary-crossing
/// logic (<c>Capacity / 2</c>, and every 64-bit boundary the active tier has).</summary>
public class ComponentMaskTests
{
    [Fact]
    public void Empty_HasNoBitsSet()
    {
        Assert.Empty(ComponentMask.Empty.EnumerateBits());
    }

    [Fact]
    public void With_SetsOnlyThatBit()
    {
        int bit = ComponentMask.Capacity / 2; // crosses a word boundary whenever Capacity > 64
        var mask = ComponentMask.Empty.With(bit);

        Assert.True(mask.Get(bit));
        Assert.False(mask.Get(0));
        Assert.False(mask.Get(ComponentMask.Capacity - 1));
    }

    [Fact]
    public void Without_ClearsOnlyThatBit()
    {
        var mask = ComponentMask.Empty.With(0).With(ComponentMask.Capacity - 1).Without(0);

        Assert.False(mask.Get(0));
        Assert.True(mask.Get(ComponentMask.Capacity - 1));
    }

    [Fact]
    public void FirstAndLastBit_RoundTripCorrectly()
    {
        var mask = ComponentMask.Empty.With(0).With(ComponentMask.Capacity - 1);

        Assert.True(mask.Get(0));
        Assert.True(mask.Get(ComponentMask.Capacity - 1));
        for (int i = 1; i < ComponentMask.Capacity - 1; i++)
            Assert.False(mask.Get(i));
    }

    [Fact]
    public void EveryWordBoundary_SetsAndReadsIndependently()
    {
        for (int wordStart = 0; wordStart < ComponentMask.Capacity; wordStart += 64)
        {
            var mask = ComponentMask.Empty.With(wordStart);
            Assert.True(mask.Get(wordStart));
            if (wordStart > 0)
                Assert.False(mask.Get(wordStart - 1));
            if (wordStart + 1 < ComponentMask.Capacity)
                Assert.False(mask.Get(wordStart + 1));
        }
    }

    [Fact]
    public void ContainsAll_And_IntersectsAny_AreConsistentWithBits()
    {
        var a = ComponentMask.Empty.With(0).With(ComponentMask.Capacity - 1);
        var b = ComponentMask.Empty.With(0);
        var c = ComponentMask.Empty.With(1);

        Assert.True(a.ContainsAll(b));
        Assert.False(b.ContainsAll(a));
        Assert.True(a.IntersectsAny(b));
        Assert.False(b.IntersectsAny(c));
    }

    [Fact]
    public void EnumerateBits_ReturnsExactlySetBits()
    {
        var mask = ComponentMask.Empty.With(0).With(ComponentMask.Capacity / 2).With(ComponentMask.Capacity - 1);

        var bits = mask.EnumerateBits();

        Assert.Equal(new[] { 0, ComponentMask.Capacity / 2, ComponentMask.Capacity - 1 }, bits);
    }

    [Fact]
    public void Equality_ComparesTheWholeMask()
    {
        var a = ComponentMask.Empty.With(0).With(ComponentMask.Capacity - 1);
        var b = ComponentMask.Empty.With(0).With(ComponentMask.Capacity - 1);
        var c = ComponentMask.Empty.With(0);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.NotEqual(a, c);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void OutOfRangeBit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ComponentMask.Empty.Get(ComponentMask.Capacity));
        Assert.Throws<ArgumentOutOfRangeException>(() => ComponentMask.Empty.With(ComponentMask.Capacity));
    }
}
