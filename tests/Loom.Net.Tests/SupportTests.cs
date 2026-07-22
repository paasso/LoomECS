using Loom.Net;

namespace Loom.Net.Tests;

public class NetworkClockTests
{
    [Fact]
    public void TryAdvance_EmitsFixedTicks()
    {
        var clock = new NetworkClock(0.5f);
        Assert.False(clock.TryAdvance(0.25f, out _));
        Assert.True(clock.TryAdvance(0.25f, out var tick0));
        Assert.Equal(0, tick0.Index);
        Assert.Equal(0.5f, tick0.DeltaTime);
        Assert.Equal(1, clock.CurrentTick);
    }
}

public class NetCommandBufferTests
{
    [Fact]
    public void DrainForTick_ReturnsSortedByClientId()
    {
        var buffer = new NetCommandBuffer();
        buffer.Enqueue(new NetPeerId(2), 3, new byte[] { 2 });
        buffer.Enqueue(new NetPeerId(1), 3, new byte[] { 1 });
        buffer.Enqueue(new NetPeerId(1), 4, new byte[] { 9 });

        var forTick = buffer.DrainForTick(3);
        Assert.Equal(2, forTick.Count);
        Assert.Equal(1, forTick[0].Client.Value);
        Assert.Equal(2, forTick[1].Client.Value);
        Assert.Equal(1, buffer.PendingCount);
    }
}

public class LoopbackTransportTests
{
    [Fact]
    public void Send_DeliversToPeerInbox()
    {
        var a = new LoopbackTransport(1);
        var b = new LoopbackTransport(2);
        LoopbackTransport.Connect(a, b);

        a.Send(b.LocalId, new byte[] { 7, 8 });
        Assert.True(b.TryReceive(out var packet));
        Assert.Equal(1, packet.Peer.Value);
        Assert.Equal(new byte[] { 7, 8 }, packet.Payload);
        Assert.False(a.TryReceive(out _));
    }
}
