using System;

namespace Loom.Net
{
    /// <summary>One authoritative simulation tick: monotonic index plus fixed delta seconds.</summary>
    public readonly struct NetworkTick
    {
        public NetworkTick(long index, float deltaTime)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));

            Index = index;
            DeltaTime = deltaTime;
        }

        public long Index { get; }
        public float DeltaTime { get; }
    }

    /// <summary>
    /// Accumulates wall/frame time and emits fixed <see cref="NetworkTick"/> steps for an
    /// authoritative server (or lockstep-style client). Does not sleep or schedule threads.
    /// </summary>
    public sealed class NetworkClock
    {
        private float _accumulator;

        public NetworkClock(float tickDurationSeconds, long startTick = 0)
        {
            if (tickDurationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(tickDurationSeconds));
            if (startTick < 0)
                throw new ArgumentOutOfRangeException(nameof(startTick));

            TickDuration = tickDurationSeconds;
            CurrentTick = startTick;
        }

        public float TickDuration { get; }

        /// <summary>Index of the next tick that <see cref="TryAdvance"/> will emit.</summary>
        public long CurrentTick { get; private set; }

        public float Accumulator => _accumulator;

        /// <summary>Adds real-time seconds and returns true when a fixed tick is ready.</summary>
        public bool TryAdvance(float realDeltaSeconds, out NetworkTick tick)
        {
            if (realDeltaSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds));

            _accumulator += realDeltaSeconds;
            if (_accumulator < TickDuration)
            {
                tick = default;
                return false;
            }

            _accumulator -= TickDuration;
            tick = new NetworkTick(CurrentTick, TickDuration);
            CurrentTick++;
            return true;
        }

        /// <summary>Drains every pending fixed tick into <paramref name="onTick"/> (spiral-of-death
        /// risk if sim is slower than real time — clamp externally if needed).</summary>
        public void AdvanceAll(float realDeltaSeconds, Action<NetworkTick> onTick)
        {
            if (onTick == null)
                throw new ArgumentNullException(nameof(onTick));

            while (TryAdvance(realDeltaSeconds, out var tick))
            {
                onTick(tick);
                realDeltaSeconds = 0f;
            }
        }
    }
}
