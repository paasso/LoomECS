# LoomECS.Net

MVP multiplayer foundation for [LoomECS](https://www.nuget.org/packages/LoomECS): authoritative fixed-tick helpers,
MemoryPack full-world snapshots, optional `TrackChanges` dirty deltas, and transport/command abstractions.

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
| `DeltaSync` | Dirty component ops via `TrackChanges` (wire type ids = `DeterministicHash`) |
| `NetMessage` | Optional kind+tick framing |

## Snapshot apply on live clients

`WorldSerializer.LoadFromMemoryPack` still requires a pristine world. For resync:

- `World.Reset()` returns the world to pristine (ids reset; keeps archetype pools)
- `WorldSerializer.ReplaceFromMemoryPack` / `SnapshotSync.Apply` call Reset then load

## Quick start

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
deltas.EnableTracking(serverWorld);
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

    // Or thin dirty path (entities must already exist on clients):
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
- **No interest management** — snapshots are full-world
- **`Parallel*` / nondeterminism** — parallel iteration order is not a sync contract; keep authoritative sim deterministic
- **Shared components** — interned values restore correctly via snapshots; delta path uses `AddOrSet` (fine for value equality)
- **Delta is not structural** — entity create/destroy needs a snapshot (or your own spawn messages)
- **In-place `Get` edits** are not tracked — use `Set` / `MarkChanged`
- Capture deltas **before** `ClearComponentChanges` / end of `Tick`
- **Empty deltas** — `TryCapture` returns false / `Capture` returns empty; skip the send (idle ≈ 0 B)
- **Delta type ids** are `ComponentTypeTraits<T>.DeterministicHash` (int32). Snapshots stay name-based via WorldSerializer
- Optional float3 wire packing: `NetFloat3Quantize` (3×Int16 or Half) at encode/decode boundary

## Install

```bash
dotnet add package LoomECS
dotnet add package LoomECS.Serialization
dotnet add package LoomECS.Net
```

Sample: `samples/Loom.NetDemo`.
