using System;
using Loom;

namespace Loom.Net
{
    /// <summary>
    /// Thin client helper: enqueue opaque commands to the server and apply incoming
    /// snapshot/delta frames onto a local <see cref="World"/>.
    /// Prediction / interpolation live beside this type (<see cref="ClientPredictor{TState}"/>,
    /// <see cref="StateInterpolator"/>) — sim world stays authoritative-apply; render samples separately.
    /// </summary>
    public sealed class NetClient
    {
        private readonly World _world;
        private readonly INetTransport _transport;
        private readonly SnapshotSync _snapshots;
        private readonly DeltaSync _deltas;
        private readonly NetPeerId _serverPeer;

        private long _lastAppliedTick = -1;
        private bool _hasSnapshot;

        public NetClient(
            World world,
            INetTransport transport,
            SnapshotSync snapshots,
            DeltaSync deltas,
            NetPeerId serverPeer)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _deltas = deltas ?? throw new ArgumentNullException(nameof(deltas));
            if (serverPeer == NetPeerId.None)
                throw new ArgumentException("Server peer id is required.", nameof(serverPeer));

            _serverPeer = serverPeer;
        }

        public World World => _world;
        public INetTransport Transport => _transport;
        public SnapshotSync Snapshots => _snapshots;
        public DeltaSync Deltas => _deltas;
        public NetPeerId ServerPeer => _serverPeer;

        /// <summary>Tick stamped on the last applied snapshot or delta, or -1 before any apply.</summary>
        public long LastAppliedTick => _lastAppliedTick;

        /// <summary>True after at least one snapshot was applied (deltas before that are ignored).</summary>
        public bool HasSnapshot => _hasSnapshot;

        /// <summary>
        /// Optional hook after a snapshot or delta is applied (<c>world</c>, tick, kind).
        /// Use to push remote transforms into a <see cref="SnapshotBuffer"/> or run reconciliation.
        /// </summary>
        public Action<World, long, NetMessageKind>? AfterStateApplied { get; set; }

        /// <summary>Asks the server for a full snapshot (join / resync).</summary>
        public void RequestSnapshot()
        {
            _transport.Send(_serverPeer, NetMessage.Pack(NetMessageKind.SnapshotRequest, 0, ReadOnlySpan<byte>.Empty));
        }

        /// <summary>Sends an opaque command targeting simulation <paramref name="tick"/>.</summary>
        public void SendCommand(long tick, ReadOnlySpan<byte> payload)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));

            _transport.Send(_serverPeer, NetMessage.Pack(NetMessageKind.Command, tick, payload));
        }

        /// <summary>Sends an opaque command targeting simulation <paramref name="tick"/>.</summary>
        public void SendCommand(long tick, byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            SendCommand(tick, payload.AsSpan());
        }

        /// <summary>
        /// Drains the transport inbox and applies snapshot/delta messages.
        /// Deltas received before the first snapshot are skipped.
        /// Returns how many framed messages were handled.
        /// </summary>
        public int Poll()
        {
            int handled = 0;
            while (_transport.TryReceive(out var packet))
            {
                if (!NetMessage.TryUnpack(packet.Payload, out var kind, out long tick, out _))
                    continue;

                switch (kind)
                {
                    case NetMessageKind.Snapshot:
                        _snapshots.ApplyFramed(_world, packet.Payload, out tick);
                        _hasSnapshot = true;
                        _lastAppliedTick = tick;
                        AfterStateApplied?.Invoke(_world, tick, NetMessageKind.Snapshot);
                        handled++;
                        break;

                    case NetMessageKind.Delta:
                        if (!_hasSnapshot)
                            break;
                        _deltas.ApplyFramed(_world, packet.Payload, out tick);
                        _lastAppliedTick = tick;
                        AfterStateApplied?.Invoke(_world, tick, NetMessageKind.Delta);
                        handled++;
                        break;
                }
            }

            return handled;
        }
    }
}
