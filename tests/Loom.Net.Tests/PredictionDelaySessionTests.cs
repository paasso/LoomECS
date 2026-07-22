using Loom.Net;

namespace Loom.Net.Tests;

/// <summary>
/// Optional: client predicts ahead of a late authoritative ack, then reconcile+replay converges.
/// </summary>
public class PredictionDelaySessionTests
{
    struct State
    {
        public float X;
    }

    static void Step(ref State state, ReadOnlySpan<byte> payload, float dt)
    {
        float thrust = payload.Length > 0 ? payload[0] : 0f;
        state.X += thrust * dt;
    }

    [Fact]
    public void PredictAhead_ThenLateAck_ConvergesAfterReplay()
    {
        const float dt = 0.05f;
        var predictor = new ClientPredictor<State>(Step, dt);
        predictor.Reset(new State { X = 0f });

        // Client predicts ticks 0..3 immediately.
        for (byte t = 0; t <= 3; t++)
            predictor.Predict(t, new byte[] { 2 });

        // Server auth arrives late: only through tick 1.
        var auth = new State { X = 0f };
        Step(ref auth, new byte[] { 2 }, dt); // tick 0
        Step(ref auth, new byte[] { 2 }, dt); // tick 1

        var result = predictor.Reconcile(
            auth,
            ackedTick: 1,
            softErrorThreshold: 0.0001f,
            hardErrorThreshold: 1f,
            static (in State a, in State b) => MathF.Abs(a.X - b.X));

        Assert.Equal(2, result.Replayed);
        Assert.True(result.Error < 0.0001f);

        // Pure forward sim of 0..3 from zero should match predicted.
        var expected = new State { X = 0f };
        for (int i = 0; i < 4; i++)
            Step(ref expected, new byte[] { 2 }, dt);
        Assert.Equal(expected.X, predictor.Predicted.X, precision: 4);
    }
}
