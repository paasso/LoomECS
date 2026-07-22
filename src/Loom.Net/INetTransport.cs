using System;

namespace Loom.Net
{
    /// <summary>One received datagram from a peer. Payload ownership is transferred to the caller
    /// of <see cref="INetTransport.TryReceive"/> — copy if you need to retain it across polls.</summary>
    public readonly struct NetPacket
    {
        public NetPacket(NetPeerId peer, byte[] payload)
        {
            Peer = peer;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public NetPeerId Peer { get; }
        public byte[] Payload { get; }
    }

    /// <summary>
    /// Byte-oriented transport seam. Loom.Net does not open sockets — plug LiteNetLib, Mirror,
    /// Steam, WebRTC, or an in-process loopback behind this interface.
    /// </summary>
    public interface INetTransport
    {
        /// <summary>Sends <paramref name="payload"/> to <paramref name="peer"/>.</summary>
        void Send(NetPeerId peer, ReadOnlySpan<byte> payload);

        /// <summary>Broadcasts <paramref name="payload"/> to every connected remote peer.</summary>
        void Broadcast(ReadOnlySpan<byte> payload);

        /// <summary>Dequeues the next inbound packet when available.</summary>
        bool TryReceive(out NetPacket packet);
    }
}
