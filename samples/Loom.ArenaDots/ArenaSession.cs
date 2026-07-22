using Loom;
using Loom.Net;
using Loom.Net.LiteNetLib;

namespace Loom.ArenaDots;

/// <summary>
/// Multiplayer host/client session for Arena Dots.
/// Loopback (optional delayed) for in-process demos; LiteNetLib for two-process UDP.
/// </summary>
sealed class ArenaSession : IDisposable
{
    public const float TickHz = 20f;
    public const float TickDt = 1f / TickHz;

    readonly ArenaSim _sim;
    readonly List<NetClient> _clients = new();
    readonly List<ArenaClientView> _views = new();
    readonly List<DelayedTransport> _delayed = new();
    readonly List<MeteredTransport> _meters = new();
    readonly List<LiteNetLibTransport> _liteNets = new();

    readonly AuthoritativeServer? _server;
    readonly World _serverWorld;
    readonly bool _isHost;
    readonly int _latencyMs;
    int _joined;

    ArenaSession(
        bool isHost,
        AuthoritativeServer? server,
        World serverWorld,
        ArenaSim? sim = null,
        int latencyMs = 0)
    {
        _isHost = isHost;
        _server = server;
        _serverWorld = serverWorld;
        _sim = sim ?? new ArenaSim();
        _latencyMs = latencyMs;
    }

    /// <summary>In-process loopback mesh (optional one-way latency + jitter on every link).</summary>
    public static ArenaSession CreateLoopback(
        int playerCount,
        int latencyMs = 0,
        int jitterMs = 0,
        LatencyMode latencyMode = LatencyMode.OneWay)
    {
        if (playerCount < 1 || playerCount > 4)
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Use 1–4 players.");

        var (snapshots, deltas) = CreateSync();
        var serverWorld = new World();
        var rawServer = new LoopbackTransport(localId: 1);
        INetTransport serverNet = Wrap(rawServer, latencyMs, jitterMs, latencyMode, out var delayed, out var meter);

        var sim = new ArenaSim();
        var session = new ArenaSession(
            isHost: true,
            new AuthoritativeServer(serverWorld, serverNet, snapshots, deltas, TickDt, sim),
            serverWorld,
            sim,
            latencyMs);

        if (delayed != null)
            session._delayed.Add(delayed);
        if (meter != null)
            session._meters.Add(meter);

        for (int i = 0; i < playerCount; i++)
        {
            var rawClient = new LoopbackTransport(localId: 2 + i);
            LoopbackTransport.Connect(rawServer, rawClient);
            INetTransport clientNet = Wrap(rawClient, latencyMs, jitterMs, latencyMode, out var d, out var m);
            if (d != null)
                session._delayed.Add(d);
            if (m != null)
                session._meters.Add(m);

            var client = new NetClient(new World(), clientNet, snapshots, deltas, serverPeer: rawServer.LocalId);
            session._clients.Add(client);
            session._views.Add(new ArenaClientView(client, rawClient.LocalId.Value));
        }

        return session;
    }

    /// <summary>
    /// UDP host: waits for <paramref name="remotePlayers"/> LiteNetLib clients, spawns them, runs the server.
    /// </summary>
    public static ArenaSession CreateListen(int port, int remotePlayers = 1)
    {
        if (remotePlayers < 1 || remotePlayers > 4)
            throw new ArgumentOutOfRangeException(nameof(remotePlayers));

        var (snapshots, deltas) = CreateSync();
        var serverWorld = new World();
        var lite = LiteNetLibTransport.StartHost(port);
        var meter = new MeteredTransport(lite);
        var sim = new ArenaSim();
        var server = new AuthoritativeServer(serverWorld, meter, snapshots, deltas, TickDt, sim);

        var session = new ArenaSession(isHost: true, server, serverWorld, sim);
        session._liteNets.Add(lite);
        session._meters.Add(meter);

        Console.WriteLine($"Listening on UDP :{port}, waiting for {remotePlayers} client(s)…");
        lite.WaitForRemotes(remotePlayers);

        for (int i = 0; i < remotePlayers; i++)
        {
            var peer = lite.ConnectedRemotes[i];
            var (x, y) = ArenaSim.SpawnPoint(i, remotePlayers);
            ArenaSim.SpawnPlayer(serverWorld, peer.Value, x, y);
            server.SendSnapshot(peer);
            session._joined++;
        }

        serverWorld.ClearComponentChanges();
        Console.WriteLine("All clients joined.");
        return session;
    }

    /// <summary>UDP client: connects to host and applies join snapshot.</summary>
    public static ArenaSession CreateConnect(string host, int port)
    {
        var (snapshots, deltas) = CreateSync();
        Console.WriteLine($"Connecting to {host}:{port}…");
        var lite = LiteNetLibTransport.Connect(host, port);
        var meter = new MeteredTransport(lite);

        var client = new NetClient(new World(), meter, snapshots, deltas, lite.ServerPeerId);
        var view = new ArenaClientView(client, lite.LocalId.Value);

        var session = new ArenaSession(isHost: false, server: null, serverWorld: new World());
        session._liteNets.Add(lite);
        session._meters.Add(meter);
        session._clients.Add(client);
        session._views.Add(view);
        session._joined = 1;

        long deadline = Environment.TickCount64 + 10000;
        while (!client.HasSnapshot)
        {
            session.PollTransports();
            client.Poll();
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Timed out waiting for join snapshot.");
            Thread.Sleep(5);
        }

        Console.WriteLine($"Joined as peer={lite.LocalId.Value}, entities={client.World.EntityCount}");
        return session;
    }

    public bool IsHost => _isHost;
    public int LatencyMilliseconds => _latencyMs;

    /// <summary>
    /// Extra command tick offset so delayed inputs still land on the server before
    /// <see cref="AuthoritativeServer"/> drops them as stale.
    /// </summary>
    public int InputDelayTicks =>
        _latencyMs <= 0 ? 0 : (int)Math.Ceiling(_latencyMs / (TickDt * 1000.0)) + 1;

    public AuthoritativeServer Server =>
        _server ?? throw new InvalidOperationException("Client-only session has no server.");
    public World ServerWorld => _serverWorld;
    public IReadOnlyList<NetClient> Clients => _clients;
    public IReadOnlyList<ArenaClientView> Views => _views;
    public ArenaSim Sim => _sim;
    public IReadOnlyList<MeteredTransport> Meters => _meters;

    public void JoinAll()
    {
        if (!_isHost || _server == null)
            throw new InvalidOperationException("JoinAll is for loopback host sessions.");
        if (_clients.Count == 0)
            return; // UDP listen already spawned remotes in CreateListen.

        while (_joined < _clients.Count)
            JoinNextLoopback();

        for (int i = 0; i < _clients.Count; i++)
        {
            FlushDelayed();
            _server.SendSnapshot(ClientPeerId(i));
        }

        PumpUntil(() =>
        {
            for (int i = 0; i < _clients.Count; i++)
            {
                if (!Clients[i].HasSnapshot || Clients[i].World.EntityCount < _clients.Count)
                    return false;
            }

            return true;
        }, timeoutMs: Math.Max(2000, _latencyMs * 4 + 500));

        _serverWorld.ClearComponentChanges();
    }

    /// <summary>
    /// Advances wall time enough for delayed packets (when latency &gt; 0), then runs one tick.
    /// </summary>
    public void TickWithPacing()
    {
        if (_latencyMs > 0)
            Thread.Sleep(Math.Max(1, (int)(TickDt * 1000)));
        Tick();
    }

    void PumpUntil(Func<bool> ready, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (!ready())
        {
            FlushDelayed();
            PollTransports();
            for (int i = 0; i < _clients.Count; i++)
                _clients[i].Poll();
            if (_server != null)
                _server.PollInbound();

            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Timed out pumping delayed/queued net traffic.");
            Thread.Sleep(5);
        }
    }

    public NetPeerId ClientPeerId(int clientIndex)
    {
        if (_views.Count > clientIndex)
            return new NetPeerId(_views[clientIndex].LocalPeerId);
        throw new ArgumentOutOfRangeException(nameof(clientIndex));
    }

    public void PollAllClients()
    {
        PollTransports();
        for (int i = 0; i < _clients.Count; i++)
            _clients[i].Poll();
    }

    public void SendMove(int clientIndex, long tick, float dx, float dy)
    {
        _views[clientIndex].SendMoveAndPredict(tick, dx, dy);
    }

    public void SendFire(int clientIndex, long tick)
    {
        _views[clientIndex].SendFire(tick);
    }

    /// <summary>One authoritative tick (host) + client apply, or client-only poll (remote).</summary>
    public void Tick()
    {
        FlushDelayed();
        PollTransports();

        if (_server != null)
            _server.TickOnce();

        FlushDelayed();
        PollAllClients();
    }

    /// <summary>Host realtime step; client-only sessions just poll.</summary>
    public int Update(float dt)
    {
        FlushDelayed();
        PollTransports();

        int ticks = 0;
        if (_server != null)
            ticks = _server.Update(dt);

        FlushDelayed();
        PollAllClients();
        return ticks;
    }

    public ArenaNetMetrics CollectMetrics()
    {
        long tx = 0, rx = 0;
        double txMbps = 0, rxMbps = 0;
        for (int i = 0; i < _meters.Count; i++)
        {
            tx += _meters[i].BytesSent;
            rx += _meters[i].BytesReceived;
            _meters[i].GetAverageRates(out _, out _, out var t, out var r);
            txMbps += t;
            rxMbps += r;
        }

        var view = _views.Count > 0 ? _views[0] : null;
        var counters = view?.Counters;
        int buffered = view?.Snapshots.TickCount ?? 0;
        long tick = _server?.LastCompletedTick
            ?? (view != null ? view.Client.LastAppliedTick : -1);

        return new ArenaNetMetrics(
            tick,
            buffered,
            counters?.LastError ?? 0f,
            counters?.LastKind ?? CorrectionKind.None,
            counters?.SoftCorrects ?? 0,
            counters?.HardCorrects ?? 0,
            counters?.TotalReplayed ?? 0,
            tx,
            rx,
            txMbps,
            rxMbps);
    }

    public void Dispose()
    {
        for (int i = 0; i < _delayed.Count; i++)
            _delayed[i].FlushAll();
        for (int i = 0; i < _liteNets.Count; i++)
            _liteNets[i].Dispose();
    }

    NetClient JoinNextLoopback()
    {
        if (_server == null)
            throw new InvalidOperationException();
        if (_joined >= _clients.Count)
            throw new InvalidOperationException("All clients already joined.");

        var client = _clients[_joined];
        int peerId = _views[_joined].LocalPeerId;
        var (x, y) = ArenaSim.SpawnPoint(_joined, _clients.Count);
        ArenaSim.SpawnPlayer(_serverWorld, peerId, x, y);

            FlushDelayed();
            _server.SendSnapshot(new NetPeerId(peerId));
            PumpUntil(() => client.HasSnapshot && client.World.EntityCount >= _joined + 1,
                timeoutMs: Math.Max(2000, _latencyMs * 4 + 500));
            _joined++;
            return client;
    }

    void FlushDelayed()
    {
        for (int i = 0; i < _delayed.Count; i++)
            _delayed[i].Flush();
    }

    void PollTransports()
    {
        for (int i = 0; i < _liteNets.Count; i++)
            _liteNets[i].Poll();
    }

    static (SnapshotSync, DeltaSync) CreateSync()
    {
        var serializer = new WorldSerializer()
            .Register<Pos>()
            .Register<Vel>()
            .Register<PlayerOwner>()
            .Register<Lifetime>();

        var snapshots = new SnapshotSync(serializer);
        var deltas = new DeltaSync()
            .Register<Pos>()
            .Register<Vel>()
            .Register<PlayerOwner>()
            .Register<Lifetime>();
        return (snapshots, deltas);
    }

    static INetTransport Wrap(
        INetTransport inner,
        int latencyMs,
        int jitterMs,
        LatencyMode mode,
        out DelayedTransport? delayed,
        out MeteredTransport? meter)
    {
        INetTransport current = inner;
        delayed = null;
        if (latencyMs > 0 || jitterMs > 0)
        {
            delayed = new DelayedTransport(current, latencyMs, jitterMs, mode);
            current = delayed;
        }

        meter = new MeteredTransport(current);
        return meter;
    }
}

readonly struct ArenaNetMetrics
{
    public ArenaNetMetrics(
        long tick,
        int bufferedInterpTicks,
        float predictionError,
        CorrectionKind correctionKind,
        int softCorrects,
        int hardCorrects,
        int replayedInputs,
        long txBytes,
        long rxBytes,
        double txMbps,
        double rxMbps)
    {
        Tick = tick;
        BufferedInterpTicks = bufferedInterpTicks;
        PredictionError = predictionError;
        CorrectionKind = correctionKind;
        SoftCorrects = softCorrects;
        HardCorrects = hardCorrects;
        ReplayedInputs = replayedInputs;
        TxBytes = txBytes;
        RxBytes = rxBytes;
        TxMbps = txMbps;
        RxMbps = rxMbps;
    }

    public long Tick { get; }
    public int BufferedInterpTicks { get; }
    public float PredictionError { get; }
    public CorrectionKind CorrectionKind { get; }
    public int SoftCorrects { get; }
    public int HardCorrects { get; }
    public int ReplayedInputs { get; }
    public long TxBytes { get; }
    public long RxBytes { get; }
    public double TxMbps { get; }
    public double RxMbps { get; }

    public override string ToString() =>
        $"tick={Tick} buf={BufferedInterpTicks} err={PredictionError:F3} {CorrectionKind} soft={SoftCorrects} hard={HardCorrects} replay={ReplayedInputs} tx={TxMbps:F3}Mbps rx={RxMbps:F3}Mbps";
}
