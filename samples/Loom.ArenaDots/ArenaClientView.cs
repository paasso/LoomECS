using Loom;
using Loom.Entities;
using Loom.Net;

namespace Loom.ArenaDots;

/// <summary>Predicted local player kinematics (matches <see cref="ArenaSim.IntegratePlayer"/>).</summary>
struct PredictedPlayerState
{
    public float PosX;
    public float PosY;
    public float VelX;
    public float VelY;

    public static PredictedPlayerState FromComponents(in Pos pos, in Vel vel) => new()
    {
        PosX = pos.X,
        PosY = pos.Y,
        VelX = vel.X,
        VelY = vel.Y,
    };

    public NetTransform ToTransform() => new(PosX, PosY, 0f, VelX, VelY, 0f);
}

/// <summary>
/// Per-client presentation layer: predict the local owned player; interpolate remotes + projectiles.
/// Sim world from <see cref="NetClient"/> stays authoritative; render samples come from here.
/// </summary>
sealed class ArenaClientView
{
    public const float SoftError = 0.05f;
    public const float HardError = 1.5f;
    public const float InterpolationDelayTicks = 1.25f;

    readonly NetClient _client;
    readonly int _localPeerId;
    readonly SnapshotBuffer _snapshots = new(48);
    readonly StateInterpolator _interpolator;
    readonly ClientPredictor<PredictedPlayerState> _predictor;

    long _lastReconciledTick = -1;
    ReconcileResult _lastReconcile;
    readonly PredictionCounters _counters = new();

    public ArenaClientView(NetClient client, int localPeerId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _localPeerId = localPeerId;
        _interpolator = new StateInterpolator(_snapshots);
        _predictor = new ClientPredictor<PredictedPlayerState>(
            PredictStep,
            ArenaSession.TickDt);

        _client.AfterStateApplied = OnStateApplied;
        SeedPredictorFromWorld();
    }

    public NetClient Client => _client;
    public int LocalPeerId => _localPeerId;
    public SnapshotBuffer Snapshots => _snapshots;
    public StateInterpolator Interpolator => _interpolator;
    public ClientPredictor<PredictedPlayerState> Predictor => _predictor;
    public ReconcileResult LastReconcile => _lastReconcile;
    public PredictionCounters Counters => _counters;

    public void SendMoveAndPredict(long tick, float dx, float dy)
    {
        EnsurePredictor();
        var payload = ArenaSim.EncodeMove(dx, dy);
        _client.SendCommand(tick, payload);
        _predictor.Predict(tick, payload);
    }

    public void SendFire(long tick)
    {
        // Fire is authoritative-only (projectile spawn); still send the command.
        _client.SendCommand(tick, ArenaSim.EncodeFire());
    }

    /// <summary>
    /// Sample a render pose. Local owned player uses prediction; everyone else uses interpolation.
    /// Falls back to the sim-world component when the buffer cannot sample yet.
    /// </summary>
    public bool TryGetRenderTransform(
        Entity entity,
        int ownerPeerId,
        float tickFraction,
        out NetTransform transform)
    {
        if (ownerPeerId == _localPeerId && _predictor.HasState)
        {
            transform = _predictor.Predicted.ToTransform();
            return true;
        }

        if (_interpolator.TrySampleDelayed(InterpolationDelayTicks, tickFraction, entity.Id, out transform))
            return true;

        if (_client.World.IsAlive(entity) && _client.World.Has<Pos>(entity))
        {
            var pos = _client.World.Get<Pos>(entity);
            var vel = _client.World.Has<Vel>(entity)
                ? _client.World.Get<Vel>(entity)
                : default;
            transform = new NetTransform(pos.X, pos.Y, pos.Z, vel.X, vel.Y, vel.Z);
            return true;
        }

        transform = default;
        return false;
    }

    /// <summary>Projectiles have no owner — always interpolate (or fall back to sim pos).</summary>
    public bool TryGetProjectileTransform(Entity entity, float tickFraction, out NetTransform transform)
    {
        if (_interpolator.TrySampleDelayed(InterpolationDelayTicks, tickFraction, entity.Id, out transform))
            return true;

        if (_client.World.IsAlive(entity) && _client.World.Has<Pos>(entity))
        {
            var pos = _client.World.Get<Pos>(entity);
            var vel = _client.World.Has<Vel>(entity)
                ? _client.World.Get<Vel>(entity)
                : default;
            transform = new NetTransform(pos.X, pos.Y, pos.Z, vel.X, vel.Y, vel.Z);
            return true;
        }

        transform = default;
        return false;
    }

    void OnStateApplied(World world, long tick, NetMessageKind kind)
    {
        PushSnapshot(world, tick);

        if (!ArenaSim.TryFindPlayer(world, _localPeerId, out var local) || !world.Has<Pos>(local))
            return;

        var auth = PredictedPlayerState.FromComponents(world.Get<Pos>(local), world.Get<Vel>(local));

        if (!_predictor.HasState)
        {
            _predictor.Reset(auth);
            _lastReconciledTick = tick;
            return;
        }

        // Snapshots resync the predicted base. Do not mark this tick reconciled so a
        // following delta stamped with the same join tick can still reconcile.
        if (kind == NetMessageKind.Snapshot)
        {
            _predictor.Reset(auth);
            _lastReconciledTick = -1;
            return;
        }

        if (tick == _lastReconciledTick)
            return;

        _lastReconcile = _predictor.Reconcile(
            auth,
            tick,
            SoftError,
            HardError,
            static (in PredictedPlayerState a, in PredictedPlayerState b) =>
            {
                float dx = a.PosX - b.PosX;
                float dy = a.PosY - b.PosY;
                return MathF.Sqrt(dx * dx + dy * dy);
            },
            static (in PredictedPlayerState cur, in PredictedPlayerState rec, float alpha) => new PredictedPlayerState
            {
                PosX = cur.PosX + (rec.PosX - cur.PosX) * alpha,
                PosY = cur.PosY + (rec.PosY - cur.PosY) * alpha,
                VelX = cur.VelX + (rec.VelX - cur.VelX) * alpha,
                VelY = cur.VelY + (rec.VelY - cur.VelY) * alpha,
            });
        _counters.Record(_lastReconcile);
        _lastReconciledTick = tick;
    }

    void PushSnapshot(World world, long tick)
    {
        world.Query().Each<Pos, Vel>((Entity e, ref Pos pos, ref Vel vel) =>
        {
            // Skip local predicted player — remotes + projectiles only.
            if (world.Has<PlayerOwner>(e) && world.Get<PlayerOwner>(e).PeerId == _localPeerId)
                return;

            _snapshots.Push(tick, e.Id, new NetTransform(pos.X, pos.Y, pos.Z, vel.X, vel.Y, vel.Z));
        });
    }

    void SeedPredictorFromWorld()
    {
        if (ArenaSim.TryFindPlayer(_client.World, _localPeerId, out var local) &&
            _client.World.Has<Pos>(local))
        {
            _predictor.Reset(PredictedPlayerState.FromComponents(
                _client.World.Get<Pos>(local),
                _client.World.Get<Vel>(local)));
            _lastReconciledTick = _client.LastAppliedTick;
        }
    }

    void EnsurePredictor()
    {
        if (!_predictor.HasState)
            SeedPredictorFromWorld();
        if (!_predictor.HasState)
            throw new InvalidOperationException("Local player not present in client world yet.");
    }

    static void PredictStep(ref PredictedPlayerState state, ReadOnlySpan<byte> payload, float dt)
    {
        float dx = 0f, dy = 0f;
        ArenaSim.TryDecodeMove(payload, out dx, out dy);
        ArenaSim.IntegratePlayer(
            ref state.PosX, ref state.PosY,
            ref state.VelX, ref state.VelY,
            dx, dy, dt);
    }
}
