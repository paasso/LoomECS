using System;
using System.Collections.Generic;
using Loom;

namespace Loom.Net
{
    /// <summary>Applies one buffered client command on the authoritative world.</summary>
    public delegate void NetCommandHandler(World world, NetCommand command);

    /// <summary>Runs one fixed simulation step after commands for that tick were applied.</summary>
    public delegate void NetSimulateHandler(World world, NetworkTick tick);

    /// <summary>
    /// Optional game hook when you prefer an interface over separate delegates.
    /// </summary>
    public interface IAuthoritativeSimulation
    {
        void ApplyCommand(World world, NetCommand command);
        void Simulate(World world, NetworkTick tick);
    }

    /// <summary>
    /// Thin authoritative host built from <see cref="NetworkClock"/>, <see cref="NetCommandBuffer"/>,
    /// <see cref="SnapshotSync"/>, <see cref="DeltaSync"/>, and <see cref="INetTransport"/>.
    /// Not a full game framework: no prediction, interest management, or sockets.
    /// </summary>
    /// <remarks>
    /// Typical loop:
    /// <list type="number">
    /// <item><see cref="SendSnapshot"/> on peer connect (or wait for <see cref="NetMessageKind.SnapshotRequest"/>).</item>
    /// <item>Each frame call <see cref="Update"/>: poll inbound commands, advance fixed ticks,
    /// apply commands, run sim, broadcast non-empty deltas (optional periodic snapshots).</item>
    /// </list>
    /// </remarks>
    public sealed class AuthoritativeServer
    {
        private readonly World _world;
        private readonly INetTransport _transport;
        private readonly SnapshotSync _snapshots;
        private readonly DeltaSync _deltas;
        private readonly NetworkClock _clock;
        private readonly NetCommandBuffer _commands = new NetCommandBuffer();
        private readonly NetCommandHandler? _applyCommand;
        private readonly NetSimulateHandler _simulate;
        private readonly int _snapshotIntervalTicks;
        private readonly List<NetPeerId> _snapshotRequestScratch = new List<NetPeerId>();

        private long _lastCompletedTick = -1;

        public AuthoritativeServer(
            World world,
            INetTransport transport,
            SnapshotSync snapshots,
            DeltaSync deltas,
            float tickDurationSeconds,
            NetSimulateHandler simulate,
            NetCommandHandler? applyCommand = null,
            int snapshotIntervalTicks = 0,
            long startTick = 0)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _deltas = deltas ?? throw new ArgumentNullException(nameof(deltas));
            _simulate = simulate ?? throw new ArgumentNullException(nameof(simulate));
            if (snapshotIntervalTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshotIntervalTicks));

            _applyCommand = applyCommand;
            _snapshotIntervalTicks = snapshotIntervalTicks;
            _clock = new NetworkClock(tickDurationSeconds, startTick);
            _deltas.EnableTracking(_world);
            _world.ClearComponentChanges();
        }

        public AuthoritativeServer(
            World world,
            INetTransport transport,
            SnapshotSync snapshots,
            DeltaSync deltas,
            float tickDurationSeconds,
            IAuthoritativeSimulation simulation,
            int snapshotIntervalTicks = 0,
            long startTick = 0)
            : this(
                world,
                transport,
                snapshots,
                deltas,
                tickDurationSeconds,
                simulation != null
                    ? simulation.Simulate
                    : throw new ArgumentNullException(nameof(simulation)),
                simulation.ApplyCommand,
                snapshotIntervalTicks,
                startTick)
        {
        }

        public World World => _world;
        public INetTransport Transport => _transport;
        public SnapshotSync Snapshots => _snapshots;
        public DeltaSync Deltas => _deltas;
        public NetworkClock Clock => _clock;
        public NetCommandBuffer Commands => _commands;

        /// <summary>Index of the last tick that finished simulate + broadcast, or -1 before any tick.</summary>
        public long LastCompletedTick => _lastCompletedTick;

        /// <summary>Unicasts a full framed snapshot to <paramref name="peer"/> (join / resync).</summary>
        public void SendSnapshot(NetPeerId peer)
        {
            long tick = _lastCompletedTick >= 0 ? _lastCompletedTick : _clock.CurrentTick;
            _transport.Send(peer, _snapshots.CaptureFramed(_world, tick));
        }

        /// <summary>Broadcasts a full framed snapshot to every connected peer.</summary>
        public void BroadcastSnapshot()
        {
            long tick = _lastCompletedTick >= 0 ? _lastCompletedTick : _clock.CurrentTick;
            _transport.Broadcast(_snapshots.CaptureFramed(_world, tick));
        }

        /// <summary>
        /// Drains the transport inbox: enqueues <see cref="NetMessageKind.Command"/> payloads and
        /// answers <see cref="NetMessageKind.SnapshotRequest"/>.
        /// </summary>
        public int PollInbound()
        {
            int handled = 0;
            _snapshotRequestScratch.Clear();

            while (_transport.TryReceive(out var packet))
            {
                if (!NetMessage.TryUnpack(packet.Payload, out var kind, out long tick, out var payload))
                    continue;

                handled++;
                switch (kind)
                {
                    case NetMessageKind.Command:
                        _commands.Enqueue(packet.Peer, tick, payload.ToArray());
                        break;
                    case NetMessageKind.SnapshotRequest:
                        _snapshotRequestScratch.Add(packet.Peer);
                        break;
                }
            }

            for (int i = 0; i < _snapshotRequestScratch.Count; i++)
                SendSnapshot(_snapshotRequestScratch[i]);

            return handled;
        }

        /// <summary>
        /// Polls inbound traffic, advances the fixed clock by <paramref name="realDeltaSeconds"/>,
        /// and runs every ready tick. Returns how many ticks completed.
        /// </summary>
        public int Update(float realDeltaSeconds)
        {
            PollInbound();

            int ticks = 0;
            while (_clock.TryAdvance(realDeltaSeconds, out var tick))
            {
                RunTick(tick);
                ticks++;
                realDeltaSeconds = 0f;
            }

            return ticks;
        }

        /// <summary>Forces exactly one tick regardless of the clock accumulator (tests / stepped hosts).</summary>
        public void TickOnce()
        {
            PollInbound();
            float dt = _clock.TickDuration - _clock.Accumulator;
            if (dt < 0f)
                dt = 0f;
            if (!_clock.TryAdvance(dt, out var tick))
                throw new InvalidOperationException("NetworkClock failed to emit a tick.");
            RunTick(tick);
        }

        private void RunTick(NetworkTick tick)
        {
            _commands.DropOlderThan(tick.Index);
            IReadOnlyList<NetCommand> forTick = _commands.DrainForTick(tick.Index);

            if (_applyCommand != null)
            {
                for (int i = 0; i < forTick.Count; i++)
                    _applyCommand(_world, forTick[i]);
            }

            _simulate(_world, tick);

            bool periodicSnapshot = _snapshotIntervalTicks > 0 &&
                                    tick.Index % _snapshotIntervalTicks == 0;
            if (periodicSnapshot)
            {
                _transport.Broadcast(_snapshots.CaptureFramed(_world, tick.Index));
            }
            else if (_deltas.TryCapture(_world, out byte[] delta) && delta.Length > 0)
            {
                _transport.Broadcast(NetMessage.Pack(NetMessageKind.Delta, tick.Index, delta));
            }

            _world.ClearComponentChanges();
            _lastCompletedTick = tick.Index;
        }
    }
}
