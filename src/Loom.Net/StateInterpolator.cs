using System;

namespace Loom.Net
{
    /// <summary>
    /// Samples <see cref="NetTransform"/> between buffered authoritative ticks for smooth remote motion.
    /// Does not mutate the replicated sim world — keep that authoritative and render from samples.
    /// </summary>
    public sealed class StateInterpolator
    {
        private readonly SnapshotBuffer _buffer;

        public StateInterpolator(SnapshotBuffer buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        public SnapshotBuffer Buffer => _buffer;

        /// <summary>
        /// Interpolates between <paramref name="tick"/> and <paramref name="tick"/>+1 using
        /// <paramref name="alpha"/> in [0,1]. Returns false if either sample is missing.
        /// </summary>
        public bool TrySample(long tick, float alpha, int entityId, out NetTransform transform)
        {
            if (!_buffer.TryGet(tick, entityId, out var a))
            {
                transform = default;
                return false;
            }

            if (alpha <= 0f)
            {
                transform = a;
                return true;
            }

            if (!_buffer.TryGet(tick + 1, entityId, out var b))
            {
                // Hold last known if the next tick is not buffered yet.
                transform = a;
                return true;
            }

            transform = NetTransform.Lerp(a, b, alpha);
            return true;
        }

        /// <summary>
        /// Samples at a continuous render tick (e.g. <c>newestTick - delay + frameFraction</c>).
        /// </summary>
        public bool TrySample(double renderTick, int entityId, out NetTransform transform)
        {
            if (renderTick < 0)
            {
                transform = default;
                return false;
            }

            long tick = (long)Math.Floor(renderTick);
            float alpha = (float)(renderTick - tick);
            return TrySample(tick, alpha, entityId, out transform);
        }

        /// <summary>
        /// Convenience: render <paramref name="interpolationDelayTicks"/> behind the newest buffered tick,
        /// plus <paramref name="tickFraction"/> (0..1) within that interval.
        /// </summary>
        public bool TrySampleDelayed(
            float interpolationDelayTicks,
            float tickFraction,
            int entityId,
            out NetTransform transform)
        {
            long newest = _buffer.NewestTick;
            if (newest < 0)
            {
                transform = default;
                return false;
            }

            double renderTick = newest - interpolationDelayTicks + tickFraction;
            if (renderTick < 0)
                renderTick = 0;
            return TrySample(renderTick, entityId, out transform);
        }
    }
}
