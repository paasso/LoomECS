using System;

namespace Loom.Net
{
    /// <summary>Accumulated prediction/reconcile counters (optional; samples can poll these).</summary>
    public sealed class PredictionCounters
    {
        public int SoftCorrects { get; private set; }
        public int HardCorrects { get; private set; }
        public int TotalReplayed { get; private set; }
        public int ReconcileCount { get; private set; }
        public float LastError { get; private set; }
        public float MaxError { get; private set; }
        public CorrectionKind LastKind { get; private set; }

        public void Record(in ReconcileResult result)
        {
            ReconcileCount++;
            LastError = result.Error;
            LastKind = result.Kind;
            TotalReplayed += result.Replayed;
            if (result.Error > MaxError)
                MaxError = result.Error;

            switch (result.Kind)
            {
                case CorrectionKind.Soft:
                    SoftCorrects++;
                    break;
                case CorrectionKind.Hard:
                    HardCorrects++;
                    break;
            }
        }

        public void Reset()
        {
            SoftCorrects = 0;
            HardCorrects = 0;
            TotalReplayed = 0;
            ReconcileCount = 0;
            LastError = 0f;
            MaxError = 0f;
            LastKind = CorrectionKind.None;
        }
    }
}
