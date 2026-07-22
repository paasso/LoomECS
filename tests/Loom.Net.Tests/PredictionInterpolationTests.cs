using Loom.Net;

namespace Loom.Net.Tests;

public class StateInterpolatorTests
{
    [Fact]
    public void TrySample_Midway_LerpsPositionAndVelocity()
    {
        var buffer = new SnapshotBuffer(8);
        buffer.Push(10, entityId: 1, new NetTransform(0f, 0f, 0f, 0f, 0f, 0f));
        buffer.Push(11, entityId: 1, new NetTransform(10f, 4f, 0f, 2f, 1f, 0f));

        var lerp = new StateInterpolator(buffer);
        Assert.True(lerp.TrySample(tick: 10, alpha: 0.5f, entityId: 1, out var mid));
        Assert.Equal(5f, mid.PosX, precision: 4);
        Assert.Equal(2f, mid.PosY, precision: 4);
        Assert.Equal(1f, mid.VelX, precision: 4);
        Assert.Equal(0.5f, mid.VelY, precision: 4);
    }

    [Fact]
    public void TrySample_RenderTick_UsesFractionalPart()
    {
        var buffer = new SnapshotBuffer(8);
        buffer.Push(0, 2, new NetTransform(0f, 0f, 0f, 0f, 0f, 0f));
        buffer.Push(1, 2, new NetTransform(8f, 0f, 0f, 0f, 0f, 0f));

        var lerp = new StateInterpolator(buffer);
        Assert.True(lerp.TrySample(renderTick: 0.25, entityId: 2, out var sample));
        Assert.Equal(2f, sample.PosX, precision: 4);
    }

    [Fact]
    public void TrySample_MissingNextTick_HoldsPrevious()
    {
        var buffer = new SnapshotBuffer(8);
        buffer.Push(5, 3, new NetTransform(1f, 2f, 0f, 0f, 0f, 0f));

        var lerp = new StateInterpolator(buffer);
        Assert.True(lerp.TrySample(5, 0.8f, 3, out var held));
        Assert.Equal(1f, held.PosX);
        Assert.Equal(2f, held.PosY);
    }
}

public class ClientPredictorTests
{
    struct State
    {
        public float X;
        public float V;
    }

    static void Step(ref State state, ReadOnlySpan<byte> payload, float dt)
    {
        float input = payload.Length > 0 ? payload[0] / 10f : 0f;
        state.V += input * dt;
        state.X += state.V * dt;
    }

    static float Error(in State a, in State b) => MathF.Abs(a.X - b.X);

    static State Blend(in State cur, in State rec, float alpha) => new()
    {
        X = cur.X + (rec.X - cur.X) * alpha,
        V = cur.V + (rec.V - cur.V) * alpha,
    };

    [Fact]
    public void Reconcile_MatchingAuth_ReplaysUnackedWithNoHardCorrect()
    {
        var predictor = new ClientPredictor<State>(Step, deltaTime: 0.1f);
        predictor.Reset(new State { X = 0f, V = 0f });

        predictor.Predict(0, new byte[] { 10 }); // +1.0 thrust units
        predictor.Predict(1, new byte[] { 10 });
        predictor.Predict(2, new byte[] { 5 });

        // Authoritative result after tick 0 only (same step rules).
        var auth = new State { X = 0f, V = 0f };
        Step(ref auth, new byte[] { 10 }, 0.1f);

        var result = predictor.Reconcile(
            auth,
            ackedTick: 0,
            softErrorThreshold: 0.001f,
            hardErrorThreshold: 1f,
            Error,
            Blend);

        Assert.Equal(CorrectionKind.None, result.Kind);
        Assert.True(result.Error < 0.001f);
        Assert.Equal(2, result.Replayed);
        Assert.Equal(2, predictor.Inputs.Count);
    }

    [Fact]
    public void Reconcile_LargeError_HardCorrectsAndReplays()
    {
        var predictor = new ClientPredictor<State>(Step, deltaTime: 0.1f);
        predictor.Reset(new State { X = 0f, V = 0f });

        predictor.Predict(0, new byte[] { 10 });
        predictor.Predict(1, new byte[] { 10 });

        // Server disagrees strongly at tick 0.
        var auth = new State { X = 5f, V = 0f };
        var before = predictor.Predicted;

        var result = predictor.Reconcile(
            auth,
            ackedTick: 0,
            softErrorThreshold: 0.05f,
            hardErrorThreshold: 0.5f,
            Error,
            Blend);

        Assert.Equal(CorrectionKind.Hard, result.Kind);
        Assert.True(result.Error > 0.5f);
        Assert.Equal(1, result.Replayed);

        // After hard correct + replay of tick 1 from auth base:
        var expected = auth;
        Step(ref expected, new byte[] { 10 }, 0.1f);
        Assert.Equal(expected.X, predictor.Predicted.X, precision: 4);
        Assert.NotEqual(before.X, predictor.Predicted.X);
    }

    [Fact]
    public void Reconcile_SmallError_SoftCorrects()
    {
        var predictor = new ClientPredictor<State>(Step, deltaTime: 1f);
        predictor.Reset(new State { X = 0f, V = 0f });
        predictor.Predict(0, new byte[] { 0 });

        var auth = new State { X = 0.02f, V = 0f };
        var result = predictor.Reconcile(
            auth,
            ackedTick: 0,
            softErrorThreshold: 0.05f,
            hardErrorThreshold: 1f,
            Error,
            Blend,
            softBlendAlpha: 0.5f);

        Assert.Equal(CorrectionKind.Soft, result.Kind);
        Assert.True(result.Error > 0f && result.Error <= 0.05f);
    }
}
