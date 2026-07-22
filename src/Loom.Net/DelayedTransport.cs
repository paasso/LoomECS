using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Loom.Net
{
    /// <summary>How <see cref="DelayedTransport"/> interprets the configured latency value.</summary>
    public enum LatencyMode : byte
    {
        /// <summary>Each Send/Broadcast waits the full latency (+jitter) before delivery.</summary>
        OneWay = 0,

        /// <summary>
        /// Each direction waits half the configured value so end-to-end RTT ≈ latency
        /// when both peers wrap with the same settings.
        /// </summary>
        RoundTrip = 1,
    }

    /// <summary>
    /// Queues outbound packets and delivers them to an inner <see cref="INetTransport"/> after a
    /// configurable delay (+ optional jitter). Call <see cref="Flush"/> (also done inside
    /// <see cref="TryReceive"/>) so due packets leave the queue. Useful for exercising prediction
    /// / reconcile on loopback without real sockets.
    /// </summary>
    public sealed class DelayedTransport : INetTransport
    {
        private static readonly long STicksPerMs = Math.Max(1, Stopwatch.Frequency / 1000);

        private readonly INetTransport _inner;
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly Func<long> _nowMs;
        private readonly Random _rng;
        private readonly int _baseDelayMs;

        public DelayedTransport(
            INetTransport inner,
            int latencyMilliseconds,
            int jitterMilliseconds = 0,
            LatencyMode mode = LatencyMode.OneWay,
            Func<long>? nowMilliseconds = null,
            int? randomSeed = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (latencyMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(latencyMilliseconds));
            if (jitterMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(jitterMilliseconds));

            LatencyMilliseconds = latencyMilliseconds;
            JitterMilliseconds = jitterMilliseconds;
            Mode = mode;
            _nowMs = nowMilliseconds ?? new Func<long>(DefaultNowMs);
            _rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

            _baseDelayMs = mode == LatencyMode.RoundTrip
                ? latencyMilliseconds / 2
                : latencyMilliseconds;
        }

        public INetTransport Inner => _inner;
        public int LatencyMilliseconds { get; }
        public int JitterMilliseconds { get; }
        public LatencyMode Mode { get; }

        /// <summary>Packets waiting for their delivery time.</summary>
        public int PendingCount => _pending.Count;

        public void Send(NetPeerId peer, ReadOnlySpan<byte> payload)
        {
            Schedule(isBroadcast: false, peer, payload);
        }

        public void Broadcast(ReadOnlySpan<byte> payload)
        {
            Schedule(isBroadcast: true, NetPeerId.None, payload);
        }

        public bool TryReceive(out NetPacket packet)
        {
            Flush();
            return _inner.TryReceive(out packet);
        }

        /// <summary>Delivers every queued packet whose delivery time has elapsed.</summary>
        public void Flush()
        {
            if (_pending.Count == 0)
                return;

            long now = _nowMs();
            int i = 0;
            while (i < _pending.Count)
            {
                var item = _pending[i];
                if (item.DeliverAtMs > now)
                {
                    i++;
                    continue;
                }

                _pending.RemoveAt(i);
                if (item.IsBroadcast)
                    _inner.Broadcast(item.Payload);
                else
                    _inner.Send(item.Peer, item.Payload);
            }
        }

        /// <summary>Delivers all queued packets immediately (tests / shutdown).</summary>
        public void FlushAll()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var item = _pending[i];
                if (item.IsBroadcast)
                    _inner.Broadcast(item.Payload);
                else
                    _inner.Send(item.Peer, item.Payload);
            }

            _pending.Clear();
        }

        private void Schedule(bool isBroadcast, NetPeerId peer, ReadOnlySpan<byte> payload)
        {
            int delay = _baseDelayMs;
            if (JitterMilliseconds > 0)
                delay += _rng.Next(0, JitterMilliseconds + 1);
            if (delay < 0)
                delay = 0;

            long deliverAt = _nowMs() + delay;
            _pending.Add(new Pending(deliverAt, isBroadcast, peer, payload.ToArray()));

            // Keep roughly ordered by delivery time for cheaper Flush scans on large queues.
            for (int i = _pending.Count - 1; i > 0; i--)
            {
                if (_pending[i - 1].DeliverAtMs <= _pending[i].DeliverAtMs)
                    break;
                var tmp = _pending[i - 1];
                _pending[i - 1] = _pending[i];
                _pending[i] = tmp;
            }
        }

        private static long DefaultNowMs() => Stopwatch.GetTimestamp() / STicksPerMs;

        private readonly struct Pending
        {
            public Pending(long deliverAtMs, bool isBroadcast, NetPeerId peer, byte[] payload)
            {
                DeliverAtMs = deliverAtMs;
                IsBroadcast = isBroadcast;
                Peer = peer;
                Payload = payload;
            }

            public long DeliverAtMs { get; }
            public bool IsBroadcast { get; }
            public NetPeerId Peer { get; }
            public byte[] Payload { get; }
        }
    }
}
