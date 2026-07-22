using System;
using System.Collections.Generic;

namespace Loom.Net
{
    /// <summary>
    /// In-process duplex transport for tests and console samples. Pair two instances with
    /// <see cref="Connect"/> so Send/Broadcast on one enqueues into the other.
    /// </summary>
    public sealed class LoopbackTransport : INetTransport
    {
        private readonly Queue<NetPacket> _inbox = new Queue<NetPacket>();
        private readonly List<LoopbackTransport> _peers = new List<LoopbackTransport>();
        private readonly NetPeerId _localId;

        public LoopbackTransport(int localId)
        {
            if (localId == 0)
                throw new ArgumentOutOfRangeException(nameof(localId), "Peer id 0 is reserved.");
            _localId = new NetPeerId(localId);
        }

        public NetPeerId LocalId => _localId;

        /// <summary>Links two loopbacks so each can Send/Broadcast to the other.</summary>
        public static void Connect(LoopbackTransport a, LoopbackTransport b)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));
            if (b == null)
                throw new ArgumentNullException(nameof(b));
            if (ReferenceEquals(a, b))
                throw new ArgumentException("Cannot connect a transport to itself.");

            if (!a._peers.Contains(b))
                a._peers.Add(b);
            if (!b._peers.Contains(a))
                b._peers.Add(a);
        }

        public void Send(NetPeerId peer, ReadOnlySpan<byte> payload)
        {
            for (int i = 0; i < _peers.Count; i++)
            {
                if (_peers[i]._localId == peer)
                {
                    _peers[i].Enqueue(_localId, payload);
                    return;
                }
            }

            throw new InvalidOperationException($"No connected peer with id {peer.Value}.");
        }

        public void Broadcast(ReadOnlySpan<byte> payload)
        {
            for (int i = 0; i < _peers.Count; i++)
                _peers[i].Enqueue(_localId, payload);
        }

        public bool TryReceive(out NetPacket packet)
        {
            if (_inbox.Count == 0)
            {
                packet = default;
                return false;
            }

            packet = _inbox.Dequeue();
            return true;
        }

        private void Enqueue(NetPeerId from, ReadOnlySpan<byte> payload)
        {
            _inbox.Enqueue(new NetPacket(from, payload.ToArray()));
        }
    }
}
