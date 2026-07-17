using Loom.Internal;

namespace Loom.Tests;

/// <summary>Regression coverage for a real bug: dense component ids are bits in
/// <c>Archetype.Mask</c> and sparse component ids are bits in a completely separate
/// <c>EntityRecord</c>-scoped <see cref="ComponentMask"/> — the two never collide because nothing
/// ever tests a dense id against a sparse mask or vice versa. An earlier version assigned both
/// from one shared counter, which capped the *combined* dense+sparse total at
/// <see cref="ComponentMask.Capacity"/> instead of allowing that many of each independently.</summary>
public class ComponentTypeRegistryTests
{
    [Fact]
    public void DenseAndSparseIdCounters_AreIndependent()
    {
        var registry = new ComponentTypeRegistry();

        var dense0 = registry.GetOrRegister<Position>();
        var sparse0 = registry.GetOrRegister<Poisoned>();
        var dense1 = registry.GetOrRegister<Velocity>();
        var sparse1 = registry.GetOrRegister<Burning>();

        // The first sparse registration gets id 0 — the same as the first dense registration —
        // which is only possible if they're drawn from two independent counters. The old, buggy
        // shared counter would have given the first sparse type id 1 (the *second* registration
        // overall), not 0.
        Assert.Equal(0, dense0.Id);
        Assert.Equal(0, sparse0.Id);
        Assert.Equal(1, dense1.Id);
        Assert.Equal(1, sparse1.Id);
    }

    [Fact]
    public void Get_ByDenseId_ReturnsTheRegisteredDenseType()
    {
        var registry = new ComponentTypeRegistry();
        var info = registry.GetOrRegister<Position>();

        Assert.Same(info, registry.Get(info.Id));
    }

    [Fact]
    public void GetOrRegister_IsIdempotentPerType()
    {
        var registry = new ComponentTypeRegistry();

        var first = registry.GetOrRegister<Position>();
        var second = registry.GetOrRegister<Position>();

        Assert.Same(first, second);
    }

    [Fact]
    public void IsSparseAndIsEmpty_AreRecordedCorrectly()
    {
        var registry = new ComponentTypeRegistry();

        var position = registry.GetOrRegister<Position>();
        var poisoned = registry.GetOrRegister<Poisoned>();
        var dead = registry.GetOrRegister<Dead>();
        var burning = registry.GetOrRegister<Burning>();

        Assert.False(position.IsSparse);
        Assert.False(position.IsEmpty);

        Assert.True(poisoned.IsSparse);
        Assert.False(poisoned.IsEmpty);

        Assert.False(dead.IsSparse);
        Assert.True(dead.IsEmpty);

        Assert.True(burning.IsSparse);
        Assert.True(burning.IsEmpty);
    }
}
