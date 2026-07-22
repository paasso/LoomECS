using System;
using System.Diagnostics;

namespace Loom.Net
{
    /// <summary>
    /// Counts framed payload bytes and packets through an inner <see cref="INetTransport"/>.
    /// Approximate application-level throughput (not UDP/IP headers).
    /// </summary>
    public sealed class MeteredTransport : INetTransport
    {
        private readonly INetTransport _inner;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _bytesSent;
        private long _bytesReceived;
        private long _packetsSent;
        private long _packetsReceived;
        private long _sampleBytesSent;
        private long _sampleBytesReceived;
        private long _sampleStartMs;

        public MeteredTransport(INetTransport inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _sampleStartMs = _watch.ElapsedMilliseconds;
        }

        public INetTransport Inner => _inner;
        public long BytesSent => _bytesSent;
        public long BytesReceived => _bytesReceived;
        public long PacketsSent => _packetsSent;
        public long PacketsReceived => _packetsReceived;

        public void Send(NetPeerId peer, ReadOnlySpan<byte> payload)
        {
            _bytesSent += payload.Length;
            _sampleBytesSent += payload.Length;
            _packetsSent++;
            _inner.Send(peer, payload);
        }

        public void Broadcast(ReadOnlySpan<byte> payload)
        {
            _bytesSent += payload.Length;
            _sampleBytesSent += payload.Length;
            _packetsSent++;
            _inner.Broadcast(payload);
        }

        public bool TryReceive(out NetPacket packet)
        {
            if (!_inner.TryReceive(out packet))
                return false;

            _bytesReceived += packet.Payload.Length;
            _sampleBytesReceived += packet.Payload.Length;
            _packetsReceived++;
            return true;
        }

        /// <summary>
        /// Average send/receive rates since construction (bytes/sec and approximate Mbps).
        /// </summary>
        public void GetAverageRates(out double txBytesPerSec, out double rxBytesPerSec, out double txMbps, out double rxMbps)
        {
            double seconds = Math.Max(_watch.Elapsed.TotalSeconds, 1e-6);
            txBytesPerSec = _bytesSent / seconds;
            rxBytesPerSec = _bytesReceived / seconds;
            txMbps = txBytesPerSec * 8.0 / 1_000_000.0;
            rxMbps = rxBytesPerSec * 8.0 / 1_000_000.0;
        }

        /// <summary>
        /// Instantaneous rates over the window since the last <see cref="SampleRates"/> call
        /// (or construction), then resets the window counters.
        /// </summary>
        public void SampleRates(out double txBytesPerSec, out double rxBytesPerSec, out double txMbps, out double rxMbps)
        {
            long now = _watch.ElapsedMilliseconds;
            double seconds = Math.Max((now - _sampleStartMs) / 1000.0, 1e-6);
            txBytesPerSec = _sampleBytesSent / seconds;
            rxBytesPerSec = _sampleBytesReceived / seconds;
            txMbps = txBytesPerSec * 8.0 / 1_000_000.0;
            rxMbps = rxBytesPerSec * 8.0 / 1_000_000.0;
            _sampleBytesSent = 0;
            _sampleBytesReceived = 0;
            _sampleStartMs = now;
        }
    }
}
