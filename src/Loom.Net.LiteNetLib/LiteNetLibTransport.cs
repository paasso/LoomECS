using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LiteNetLib;
using Loom.Net;

namespace Loom.Net.LiteNetLib
{
    /// <summary>
    /// <see cref="INetTransport"/> backed by LiteNetLib reliable-ordered UDP.
    /// Call <see cref="Poll"/> regularly (before <see cref="TryReceive"/> / server tick).
    /// </summary>
    public sealed class LiteNetLibTransport : INetTransport, INetEventListener, IDisposable
    {
        public const string DefaultConnectionKey = "Loom.Net";
        public const int DefaultServerPeerId = 1;

        private static readonly byte[] HandshakeMagic = { (byte)'L', (byte)'N', (byte)'I', (byte)'D' };
        private static readonly long STicksPerMs = Math.Max(1, Stopwatch.Frequency / 1000);

        private readonly NetManager _manager;
        private readonly bool _isHost;
        private readonly string _connectionKey;
        private readonly object _gate = new object();
        private readonly Queue<NetPacket> _inbox = new Queue<NetPacket>();
        private readonly Dictionary<int, NetPeer> _peersById = new Dictionary<int, NetPeer>();
        private readonly Dictionary<NetPeer, NetPeerId> _idsByPeer = new Dictionary<NetPeer, NetPeerId>();
        private readonly List<NetPeerId> _connectedRemotes = new List<NetPeerId>();

        private NetPeerId _localId;
        private NetPeerId _serverPeerId;
        private NetPeer? _serverPeer;
        private int _nextClientPeerId = DefaultServerPeerId + 1;
        private bool _assigned;
        private bool _disposed;
        private string? _disconnectReason;
        private Action<NetPeerId>? _peerConnected;
        private Action<NetPeerId>? _peerDisconnected;

        private LiteNetLibTransport(bool isHost, NetPeerId localId, NetPeerId serverPeerId, string connectionKey)
        {
            _isHost = isHost;
            _localId = localId;
            _serverPeerId = serverPeerId;
            _connectionKey = string.IsNullOrEmpty(connectionKey) ? DefaultConnectionKey : connectionKey;
            _assigned = isHost;
            _manager = new NetManager(this)
            {
                AutoRecycle = true,
                DisconnectTimeout = 15000,
            };
        }

        /// <summary>Starts a UDP host on <paramref name="port"/>. Local peer id is <see cref="DefaultServerPeerId"/>.</summary>
        public static LiteNetLibTransport StartHost(
            int port,
            string connectionKey = DefaultConnectionKey,
            int localPeerId = DefaultServerPeerId)
        {
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            if (localPeerId == 0)
                throw new ArgumentOutOfRangeException(nameof(localPeerId));

            var transport = new LiteNetLibTransport(
                isHost: true,
                localId: new NetPeerId(localPeerId),
                serverPeerId: new NetPeerId(localPeerId),
                connectionKey: connectionKey);

            if (!transport._manager.Start(port))
                throw new InvalidOperationException($"Failed to bind LiteNetLib host on port {port}.");

            return transport;
        }

        /// <summary>
        /// Starts a client socket and begins connecting. Call <see cref="Poll"/> until
        /// <see cref="IsAssigned"/> (host must also <see cref="Poll"/> in another process/thread).
        /// </summary>
        public static LiteNetLibTransport BeginConnect(
            string host,
            int port,
            string connectionKey = DefaultConnectionKey,
            int serverPeerId = DefaultServerPeerId)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host is required.", nameof(host));
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));

            var transport = new LiteNetLibTransport(
                isHost: false,
                localId: NetPeerId.None,
                serverPeerId: new NetPeerId(serverPeerId),
                connectionKey: connectionKey);

            if (!transport._manager.Start())
                throw new InvalidOperationException("Failed to start LiteNetLib client socket.");

            transport._serverPeer = transport._manager.Connect(host, port, connectionKey)
                ?? throw new InvalidOperationException($"Connect to {host}:{port} returned null.");

            return transport;
        }

        /// <summary>
        /// Connects to <paramref name="host"/>:<paramref name="port"/> and blocks until the host
        /// handshake assigns <see cref="LocalId"/>. The host process must be polling concurrently.
        /// </summary>
        public static LiteNetLibTransport Connect(
            string host,
            int port,
            string connectionKey = DefaultConnectionKey,
            int serverPeerId = DefaultServerPeerId,
            int connectTimeoutMs = 5000)
        {
            var transport = BeginConnect(host, port, connectionKey, serverPeerId);
            transport.WaitUntilAssigned(connectTimeoutMs);
            return transport;
        }

        /// <summary>Polls until the host handshake assigns <see cref="LocalId"/>.</summary>
        public void WaitUntilAssigned(int timeoutMs = 5000)
        {
            if (_isHost)
                return;

            long deadline = NowMs() + timeoutMs;
            while (!_assigned)
            {
                Poll();
                if (_disconnectReason != null)
                    throw new InvalidOperationException($"LiteNetLib connect failed: {_disconnectReason}");
                if (NowMs() > deadline)
                    throw new TimeoutException("Timed out waiting for LiteNetLib peer-id handshake.");
                Thread.Sleep(5);
            }
        }

        public bool IsHost => _isHost;
        public NetPeerId LocalId => _localId;
        public NetPeerId ServerPeerId => _serverPeerId;
        public bool IsAssigned => _assigned;
        public int ConnectedRemoteCount
        {
            get
            {
                lock (_gate)
                    return _connectedRemotes.Count;
            }
        }

        public IReadOnlyList<NetPeerId> ConnectedRemotes
        {
            get
            {
                lock (_gate)
                    return _connectedRemotes.ToArray();
            }
        }

        /// <summary>Raised on the host after a remote peer is assigned an id (after handshake send).</summary>
        public event Action<NetPeerId>? PeerConnected
        {
            add => _peerConnected += value;
            remove => _peerConnected -= value;
        }

        public event Action<NetPeerId>? PeerDisconnected
        {
            add => _peerDisconnected += value;
            remove => _peerDisconnected -= value;
        }

        /// <summary>Pumps LiteNetLib and delivers handshake / inbox packets.</summary>
        public void Poll()
        {
            ThrowIfDisposed();
            _manager.PollEvents();
        }

        public void Send(NetPeerId peer, ReadOnlySpan<byte> payload)
        {
            ThrowIfDisposed();
            NetPeer? netPeer;
            lock (_gate)
            {
                if (!_peersById.TryGetValue(peer.Value, out netPeer))
                    throw new InvalidOperationException($"No connected peer with id {peer.Value}.");
            }

            netPeer.Send(Copy(payload), DeliveryMethod.ReliableOrdered);
        }

        public void Broadcast(ReadOnlySpan<byte> payload)
        {
            ThrowIfDisposed();
            byte[] bytes = Copy(payload);
            lock (_gate)
            {
                foreach (var kv in _peersById)
                {
                    if (_isHost && kv.Key == _localId.Value)
                        continue;
                    kv.Value.Send(bytes, DeliveryMethod.ReliableOrdered);
                }
            }
        }

        public bool TryReceive(out NetPacket packet)
        {
            ThrowIfDisposed();
            lock (_gate)
            {
                if (_inbox.Count == 0)
                {
                    packet = default;
                    return false;
                }

                packet = _inbox.Dequeue();
                return true;
            }
        }

        /// <summary>Blocks until at least <paramref name="count"/> remotes are connected (host only).</summary>
        public void WaitForRemotes(int count, int timeoutMs = 30000)
        {
            if (!_isHost)
                throw new InvalidOperationException("WaitForRemotes is host-only.");
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            long deadline = NowMs() + timeoutMs;
            while (ConnectedRemoteCount < count)
            {
                Poll();
                if (NowMs() > deadline)
                    throw new TimeoutException($"Timed out waiting for {count} remote peer(s).");
                Thread.Sleep(5);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _manager.Stop();
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            if (_isHost)
            {
                int id;
                lock (_gate)
                {
                    id = _nextClientPeerId++;
                    var peerId = new NetPeerId(id);
                    _peersById[id] = peer;
                    _idsByPeer[peer] = peerId;
                    _connectedRemotes.Add(peerId);
                }

                SendHandshake(peer, id);
                _peerConnected?.Invoke(new NetPeerId(id));
            }
            else
            {
                lock (_gate)
                {
                    _serverPeer = peer;
                    _peersById[_serverPeerId.Value] = peer;
                    _idsByPeer[peer] = _serverPeerId;
                }
            }
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            NetPeerId id = NetPeerId.None;
            lock (_gate)
            {
                if (_idsByPeer.TryGetValue(peer, out id))
                {
                    _idsByPeer.Remove(peer);
                    _peersById.Remove(id.Value);
                    _connectedRemotes.Remove(id);
                }
            }

            if (!_assigned && !_isHost)
                _disconnectReason = disconnectInfo.Reason.ToString();

            if (id != NetPeerId.None)
                _peerDisconnected?.Invoke(id);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketErrorCode)
        {
            if (!_assigned && !_isHost)
                _disconnectReason = socketErrorCode.ToString();
        }

        void INetEventListener.OnNetworkReceive(
            NetPeer peer,
            NetPacketReader reader,
            byte channelNumber,
            DeliveryMethod deliveryMethod)
        {
            int len = reader.AvailableBytes;
            var payload = new byte[len];
            reader.GetBytes(payload, 0, len);

            if (TryHandleHandshake(payload, out int assignedId))
            {
                if (!_isHost && !_assigned)
                {
                    _localId = new NetPeerId(assignedId);
                    _assigned = true;
                }

                return;
            }

            NetPeerId from;
            lock (_gate)
            {
                if (!_idsByPeer.TryGetValue(peer, out from))
                    return;
                _inbox.Enqueue(new NetPacket(from, payload));
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(
            IPEndPoint remoteEndPoint,
            NetPacketReader reader,
            UnconnectedMessageType messageType)
        {
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            if (!_isHost)
            {
                request.Reject();
                return;
            }

            request.AcceptIfKey(_connectionKey);
        }

        private void SendHandshake(NetPeer peer, int assignedPeerId)
        {
            var bytes = new byte[HandshakeMagic.Length + 4];
            Buffer.BlockCopy(HandshakeMagic, 0, bytes, 0, HandshakeMagic.Length);
            bytes[HandshakeMagic.Length] = (byte)assignedPeerId;
            bytes[HandshakeMagic.Length + 1] = (byte)(assignedPeerId >> 8);
            bytes[HandshakeMagic.Length + 2] = (byte)(assignedPeerId >> 16);
            bytes[HandshakeMagic.Length + 3] = (byte)(assignedPeerId >> 24);
            peer.Send(bytes, DeliveryMethod.ReliableOrdered);
        }

        private static bool TryHandleHandshake(byte[] payload, out int assignedPeerId)
        {
            assignedPeerId = 0;
            if (payload.Length != HandshakeMagic.Length + 4)
                return false;
            for (int i = 0; i < HandshakeMagic.Length; i++)
            {
                if (payload[i] != HandshakeMagic[i])
                    return false;
            }

            assignedPeerId = payload[HandshakeMagic.Length]
                | (payload[HandshakeMagic.Length + 1] << 8)
                | (payload[HandshakeMagic.Length + 2] << 16)
                | (payload[HandshakeMagic.Length + 3] << 24);
            return assignedPeerId != 0;
        }

        private static byte[] Copy(ReadOnlySpan<byte> payload)
        {
            var bytes = new byte[payload.Length];
            payload.CopyTo(bytes);
            return bytes;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LiteNetLibTransport));
        }

        private static long NowMs() => Stopwatch.GetTimestamp() / STicksPerMs;
    }
}
