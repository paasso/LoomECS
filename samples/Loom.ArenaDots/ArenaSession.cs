using Loom;
using Loom.Net;

namespace Loom.ArenaDots;

/// <summary>
/// Loopback multiplayer host: one <see cref="AuthoritativeServer"/> + N <see cref="NetClient"/>s.
/// Spawns a player entity per join; later joins reach earlier clients via delta spawn.
/// Each client gets an <see cref="ArenaClientView"/> for local prediction + remote interpolation.
/// </summary>
sealed class ArenaSession : IDisposable
{
    public const float TickHz = 20f;
    public const float TickDt = 1f / TickHz;

    readonly ArenaSim _sim = new();
    readonly List<NetClient> _clients = new();
    readonly List<LoopbackTransport> _clientNets = new();
    readonly List<ArenaClientView> _views = new();
    readonly LoopbackTransport _serverNet;
    readonly AuthoritativeServer _server;
    readonly SnapshotSync _snapshots;
    readonly DeltaSync _deltas;
    int _joined;

    public ArenaSession(int playerCount)
    {
        if (playerCount < 1 || playerCount > 4)
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Use 1–4 players.");

        var serializer = new WorldSerializer()
            .Register<Pos>()
            .Register<Vel>()
            .Register<PlayerOwner>()
            .Register<Lifetime>();

        _snapshots = new SnapshotSync(serializer);
        _deltas = new DeltaSync()
            .Register<Pos>()
            .Register<Vel>()
            .Register<PlayerOwner>()
            .Register<Lifetime>();

        var serverWorld = new World();
        _serverNet = new LoopbackTransport(localId: 1);
        _server = new AuthoritativeServer(
            serverWorld,
            _serverNet,
            _snapshots,
            _deltas,
            TickDt,
            _sim);

        for (int i = 0; i < playerCount; i++)
        {
            var clientNet = new LoopbackTransport(localId: 2 + i);
            LoopbackTransport.Connect(_serverNet, clientNet);
            var clientWorld = new World();
            var client = new NetClient(clientWorld, clientNet, _snapshots, _deltas, serverPeer: _serverNet.LocalId);
            _clientNets.Add(clientNet);
            _clients.Add(client);
            _views.Add(new ArenaClientView(client, clientNet.LocalId.Value));
        }
    }

    public AuthoritativeServer Server => _server;
    public World ServerWorld => _server.World;
    public IReadOnlyList<NetClient> Clients => _clients;
    public IReadOnlyList<ArenaClientView> Views => _views;
    public ArenaSim Sim => _sim;

    /// <summary>
    /// Spawns the next player's dot and unicasts a join snapshot.
    /// Call once per client (in order). Existing peers learn new spawns on the next tick delta.
    /// </summary>
    public NetClient JoinNext()
    {
        if (_joined >= _clients.Count)
            throw new InvalidOperationException("All clients already joined.");

        var client = _clients[_joined];
        var clientNet = _clientNets[_joined];
        int peerId = clientNet.LocalId.Value;
        var (x, y) = ArenaSim.SpawnPoint(_joined, _clients.Count);
        ArenaSim.SpawnPlayer(ServerWorld, peerId, x, y);

        _server.SendSnapshot(clientNet.LocalId);
        client.Poll();
        _joined++;
        return client;
    }

    public void JoinAll()
    {
        while (_joined < _clients.Count)
            JoinNext();

        // Early clients only saw a partial world on their join snapshot — resync everyone,
        // then clear dirty/lifecycle so the first gameplay tick starts clean (tick index 0).
        for (int i = 0; i < _clients.Count; i++)
        {
            _server.SendSnapshot(_clientNets[i].LocalId);
            _clients[i].Poll();
        }

        ServerWorld.ClearComponentChanges();
    }

    public NetPeerId ClientPeerId(int clientIndex) => _clientNets[clientIndex].LocalId;

    public void PollAllClients()
    {
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

    /// <summary>One authoritative tick + client apply (reconcile + snapshot buffer push).</summary>
    public void Tick()
    {
        _server.TickOnce();
        PollAllClients();
    }

    public void Dispose()
    {
        // Loopback holds no unmanaged resources; worlds are GC'd with the session.
    }
}
