using System;
using System.Collections.Generic;

namespace Loom.Net
{
    /// <summary>Applies one predicted command to local state (must match server movement rules).</summary>
    public delegate void PredictionStepHandler<TState>(ref TState state, ReadOnlySpan<byte> commandPayload, float deltaTime)
        where TState : struct;

    /// <summary>Measures error between authoritative and predicted state (e.g. position distance).</summary>
    public delegate float PredictionErrorMetric<TState>(in TState authoritative, in TState predicted)
        where TState : struct;

    /// <summary>Optional soft blend toward the reconciled state (alpha in 0..1).</summary>
    public delegate TState PredictionBlendHandler<TState>(in TState current, in TState reconciled, float alpha)
        where TState : struct;

    public enum CorrectionKind : byte
    {
        None = 0,
        Soft = 1,
        Hard = 2,
    }

    public readonly struct ReconcileResult
    {
        public ReconcileResult(CorrectionKind kind, float error, int replayed)
        {
            Kind = kind;
            Error = error;
            Replayed = replayed;
        }

        public CorrectionKind Kind { get; }
        public float Error { get; }
        public int Replayed { get; }
    }

    /// <summary>
    /// Client-only prediction for a single owned entity: ring of unacked inputs + predicted state,
    /// reconciled against authoritative samples (server remains source of truth).
    /// </summary>
    /// <typeparam name="TState">Game-defined predicted state (e.g. pos+vel).</typeparam>
    public sealed class ClientPredictor<TState> where TState : struct
    {
        private readonly PredictedInputBuffer _inputs;
        private readonly PredictionStepHandler<TState> _step;
        private readonly float _deltaTime;
        private readonly Dictionary<long, TState> _stateAtTick = new Dictionary<long, TState>();
        private readonly List<long> _stateTicks = new List<long>();
        private readonly int _historyCapacity;

        private TState _predicted;
        private bool _hasState;

        public ClientPredictor(
            PredictionStepHandler<TState> step,
            float deltaTime,
            int inputCapacity = 64,
            int historyCapacity = 64)
        {
            _step = step ?? throw new ArgumentNullException(nameof(step));
            if (deltaTime <= 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (historyCapacity < 2)
                throw new ArgumentOutOfRangeException(nameof(historyCapacity));

            _deltaTime = deltaTime;
            _historyCapacity = historyCapacity;
            _inputs = new PredictedInputBuffer(inputCapacity);
        }

        public PredictedInputBuffer Inputs => _inputs;
        public TState Predicted => _predicted;
        public bool HasState => _hasState;
        public float DeltaTime => _deltaTime;

        /// <summary>Seeds or replaces the predicted state (join / hard resync).</summary>
        public void Reset(in TState state)
        {
            _predicted = state;
            _hasState = true;
            _inputs.Clear();
            _stateAtTick.Clear();
            _stateTicks.Clear();
        }

        /// <summary>Overwrites predicted state without clearing the input ring.</summary>
        public void SetPredicted(in TState state)
        {
            _predicted = state;
            _hasState = true;
        }

        /// <summary>
        /// Records <paramref name="payload"/> for <paramref name="tick"/> and advances predicted state.
        /// </summary>
        public void Predict(long tick, byte[] payload)
        {
            if (!_hasState)
                throw new InvalidOperationException("Call Reset with an initial state before Predict.");
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            _inputs.Push(tick, payload);
            var state = _predicted;
            _step(ref state, payload, _deltaTime);
            _predicted = state;
            StoreHistory(tick, state);
        }

        /// <summary>
        /// Reconciles against authoritative state that includes commands through <paramref name="ackedTick"/>.
        /// Drops acked inputs, measures error vs the predicted sample at that tick (when available),
        /// then replays remaining unacked inputs from the authoritative base.
        /// </summary>
        public ReconcileResult Reconcile(
            in TState authoritative,
            long ackedTick,
            float softErrorThreshold,
            float hardErrorThreshold,
            PredictionErrorMetric<TState> errorMetric,
            PredictionBlendHandler<TState>? softBlend = null,
            float softBlendAlpha = 0.35f)
        {
            if (!_hasState)
                throw new InvalidOperationException("Call Reset with an initial state before Reconcile.");
            if (errorMetric == null)
                throw new ArgumentNullException(nameof(errorMetric));
            if (softErrorThreshold < 0f)
                throw new ArgumentOutOfRangeException(nameof(softErrorThreshold));
            if (hardErrorThreshold < softErrorThreshold)
                throw new ArgumentOutOfRangeException(nameof(hardErrorThreshold));

            float error = 0f;
            if (_stateAtTick.TryGetValue(ackedTick, out var predictedAtAck))
                error = errorMetric(authoritative, predictedAtAck);
            else
                error = errorMetric(authoritative, _predicted);

            _inputs.AckThrough(ackedTick);
            PruneHistoryThrough(ackedTick);

            var replayed = authoritative;
            int replayCount = _inputs.Count;
            for (int i = 0; i < _inputs.Count; i++)
            {
                var cmd = _inputs[i];
                _step(ref replayed, cmd.Payload, _deltaTime);
                StoreHistory(cmd.Tick, replayed);
            }

            CorrectionKind kind;
            if (error <= softErrorThreshold)
            {
                // Below soft threshold: accept reconciled (optionally light blend for continuity).
                if (softBlend != null && softBlendAlpha > 0f && error > 0f)
                {
                    _predicted = softBlend(_predicted, replayed, softBlendAlpha);
                    kind = CorrectionKind.Soft;
                }
                else
                {
                    _predicted = replayed;
                    kind = error > 0f ? CorrectionKind.Soft : CorrectionKind.None;
                }
            }
            else if (error <= hardErrorThreshold)
            {
                _predicted = softBlend != null
                    ? softBlend(_predicted, replayed, Math.Clamp(softBlendAlpha * 2f, 0f, 1f))
                    : replayed;
                kind = CorrectionKind.Soft;
            }
            else
            {
                _predicted = replayed;
                kind = CorrectionKind.Hard;
            }

            return new ReconcileResult(kind, error, replayCount);
        }

        private void StoreHistory(long tick, in TState state)
        {
            if (!_stateAtTick.ContainsKey(tick))
                _stateTicks.Add(tick);
            _stateAtTick[tick] = state;

            while (_stateTicks.Count > _historyCapacity)
            {
                long old = _stateTicks[0];
                _stateTicks.RemoveAt(0);
                _stateAtTick.Remove(old);
            }
        }

        private void PruneHistoryThrough(long ackedTick)
        {
            int i = 0;
            while (i < _stateTicks.Count && _stateTicks[i] <= ackedTick)
            {
                _stateAtTick.Remove(_stateTicks[i]);
                i++;
            }

            if (i > 0)
                _stateTicks.RemoveRange(0, i);
        }
    }
}
