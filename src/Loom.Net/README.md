# LoomECS.Net

MVP multiplayer foundation for [LoomECS](https://www.nuget.org/packages/LoomECS): authoritative fixed-tick helpers,
MemoryPack full-world snapshots, optional `TrackChanges` dirty deltas (including spawn/despawn), and transport/command abstractions.

**Not** a DOTS NetCode / rollback clone. No sockets are hardcoded — plug LiteNetLib, Mirror, Steam, etc.

**Target:** `netstandard2.1` + `net10.0` · **License:** MIT · **Package:** `LoomECS.Net`

## Architecture

```
Client                         Server (authoritative)
  |                              |
  |  NetCommand (input bytes)    |
  |----------------------------->|  NetCommandBuffer.DrainForTick
  |                              |  NetworkClock fixed tick → simulate
  |  Snapshot (LCMP) or Delta    |
  |<-----------------------------|  SnapshotSync / DeltaSync.Capture
  |  Apply / ReplaceFromMemoryPack
```

| Type | Role |
|------|------|
| `INetTransport` | Byte send/receive/broadcast seam |
| `LoopbackTransport` | In-process duplex for tests/samples |
| `NetSessionRole` | Server / Client |
| `NetworkClock` / `NetworkTick` | Fixed-tick accumulator |
| `NetCommandBuffer` | Client→server commands ordered by tick |
| `SnapshotSync` | Full-world MemoryPack capture + live apply |
| `DeltaSync` | Spawn/despawn + dirty component ops via `TrackEntityLifecycle` / `TrackChanges` (wire type ids = `DeterministicHash`) |
| `NetMessage` | Optional kind+tick framing (`Snapshot`, `Delta`, `Command`, `SnapshotRequest`) |
| `AuthoritativeServer` | Thin host: poll → tick → apply commands → sim → broadcast delta/snapshot |
| `NetClient` | Send commands, request/apply snapshot, apply deltas |

## Snapshot apply on live clients

`WorldSerializer.LoadFromMemoryPack` still requires a pristine world. For resync:

- `World.Reset()` returns the world to pristine (ids reset; keeps archetype pools)
- `WorldSerializer.ReplaceFromMemoryPack` / `SnapshotSync.Apply` call Reset then load

## Quick start — session helpers

```csharp
using Loom;
using Loom.Net;

var serializer = new WorldSerializer()
    .Register<Position>()
    .Register<Velocity>();

var snapshots = new SnapshotSync(serializer);
var deltas = new DeltaSync().Register<Position>().Register<Velocity>();

var serverWorld = new World();
var clientWorld = new World();

var serverNet = new LoopbackTransport(localId: 1);
var clientNet = new LoopbackTransport(localId: 2);
LoopbackTransport.Connect(serverNet, clientNet);

var server = new AuthoritativeServer(
    serverWorld,
    serverNet,
    snapshots,
    deltas,
    tickDurationSeconds: 1f / 60f,
    simulate: (world, tick) => { /* your fixed-step sim */ },
    applyCommand: (world, cmd) => { /* decode cmd.Payload */ });

var client = new NetClient(clientWorld, clientNet, snapshots, deltas, serverPeer: serverNet.LocalId);

// Join: server pushes snapshot (or client.RequestSnapshot → server PollInbound).
server.SendSnapshot(clientNet.LocalId);
client.Poll();

client.SendCommand(tick: 0, payload: /* opaque bytes */);

// Host frame:
server.Update(dt);   // or server.TickOnce() in tests
client.Poll();       // apply snapshot/delta frames
```

Each `AuthoritativeServer` tick: drop stale commands → drain/apply for that tick (stable client-id order) →
user sim → periodic snapshot **or** non-empty delta broadcast → `ClearComponentChanges`.

## Quick start — manual pieces

```csharp
using Loom;
using Loom.Net;

var serializer = new WorldSerializer()
    .Register<Position>()
    .Register<Velocity>();

var snapshots = new SnapshotSync(serializer);
var deltas = new DeltaSync().Register<Position>().Register<Velocity>();

// Server setup
var serverWorld = new World();
deltas.EnableTracking(serverWorld); // TrackEntityLifecycle + TrackChanges for registered types
var clock = new NetworkClock(tickDurationSeconds: 1f / 60f);
var commands = new NetCommandBuffer();
var serverTransport = new LoopbackTransport(localId: 1);
var clientTransport = new LoopbackTransport(localId: 2);
LoopbackTransport.Connect(serverTransport, clientTransport);

// Each authoritative tick:
while (clock.TryAdvance(dt, out var tick))
{
    foreach (var cmd in commands.DrainForTick(tick.Index))
        ApplyInput(serverWorld, cmd); // your game code

    Simulate(serverWorld, tick.DeltaTime); // your game code

    // Join / periodic correction:
    serverTransport.Broadcast(snapshots.CaptureFramed(serverWorld, tick.Index));

    // Ongoing dirty + structural path (after the client has joined via snapshot):
    // if (deltas.TryCapture(serverWorld, out var delta) && delta.Length > 0)
    //     serverTransport.Broadcast(NetMessage.Pack(NetMessageKind.Delta, tick.Index, delta));
    // serverWorld.ClearComponentChanges();
}

// Client:
while (clientTransport.TryReceive(out var packet))
{
    if (!NetMessage.TryUnpack(packet.Payload, out var kind, out _, out _))
        continue;
    if (kind == NetMessageKind.Snapshot)
        snapshots.ApplyFramed(clientWorld, packet.Payload, out _);
    else if (kind == NetMessageKind.Delta)
        deltas.ApplyFramed(clientWorld, packet.Payload, out _);
}
```

## Limitations

- **No rollback / prediction** — clients apply authoritative state; prediction is your job if needed
- **No interest management / AOI** — snapshots and deltas are full-world
- **No sockets** — `INetTransport` only; bring your own reliability/ordering if the wire needs it
- **`Parallel*` / nondeterminism** — parallel iteration order is not a sync contract; keep authoritative sim deterministic
- **Shared components** — interned values restore correctly via snapshots; delta path uses `AddOrSet` (fine for value equality)
- **First join still needs SnapshotSync** — subsequent entity create/destroy can go via DeltaSync (v3 spawn/despawn)
- **In-place `Get` edits** are not tracked — use `Set` / `MarkChanged`
- Capture deltas **before** `ClearComponentChanges` / end of `Tick` (also clears entity lifecycle lists)
- **Empty deltas** — `TryCapture` returns false / `Capture` returns empty; skip the send (idle ≈ 0 B). `AuthoritativeServer` does this for you
- **Delta type ids** are `ComponentTypeTraits<T>.DeterministicHash` (int32). Snapshots stay name-based via WorldSerializer
- **Adaptive compression** — `compress: true` means *allow* Brotli when payload ≥ threshold (default 256 B); tiny payloads stay LDLT / LCMP. Apply accepts both magics
- Optional float3 wire packing: `NetFloat3Quantize` (3×Int16 or Half) at encode/decode boundary

## Install

```bash
dotnet add package LoomECS
dotnet add package LoomECS.Serialization
dotnet add package LoomECS.Net
```

Sample: `samples/Loom.NetDemo` (`--session` for `AuthoritativeServer` / `NetClient`, `--demo` for the manual loop).
