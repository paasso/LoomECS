# Arena Dots

Authoritative multiplayer “dots in an arena” demo on top of `Loom.Net`
(`AuthoritativeServer` + `NetClient`), with **client prediction** for the local
player and **interpolation** for remotes / projectiles.

Transports:

| Mode | CLI | Transport |
|------|-----|-----------|
| Default | (none) | In-process `LoopbackTransport` |
| Delayed loopback | `--latency N` | `DelayedTransport` over loopback |
| UDP host | `--listen PORT` | `LiteNetLibTransport` (`Loom.Net.LiteNetLib`) |
| UDP client | `--connect host:port` | same |

## What it shows

- 1–4 clients on loopback (default **2**), or 1+ remotes over UDP
- Join snapshot, then per-tick deltas (`Pos` / `Vel` / `PlayerOwner` / `Lifetime`)
- Local player: `ClientPredictor` + reconcile (soft / hard); remotes via `SnapshotBuffer`
- **Metrics** each few ticks / visual overlay: prediction error, soft·hard corrects,
  replayed inputs, approx tx/rx Mbps (framed payload bytes), tick + interp buffer depth
- `--latency 100` makes reconcile error / replay counts non-trivial on loopback

## Run

```bash
# Console convergence (scripted inputs)
dotnet run --project samples/Loom.ArenaDots -- --players 2

# Exercise prediction / reconcile with 100 ms one-way delay
dotnet run --project samples/Loom.ArenaDots -- --latency 100 --players 2

# Optional: treat --latency as RTT (half per direction), add jitter
dotnet run --project samples/Loom.ArenaDots -- --latency 100 --rtt --jitter 20

# Raylib view
dotnet run --project samples/Loom.ArenaDots -- --visual --latency 100
```

### Two-process UDP (LiteNetLib)

```bash
# Terminal A — host (waits for 1 remote; authoritative view with --visual)
dotnet run --project samples/Loom.ArenaDots -- --listen 9050 --players 1

# Terminal B — client (predicted local player)
dotnet run --project samples/Loom.ArenaDots -- --connect 127.0.0.1:9050 --visual
```

Use the same `--players N` on the host as the number of `--connect` processes you will start.

## Metrics meaning

| Field | Meaning |
|-------|---------|
| `err` / prediction error | Distance between predicted and auth sample at reconcile |
| `soft` / `hard` | Cumulative soft vs hard corrections from `ClientPredictor` |
| `replay` | Inputs replayed after the last reconcile (summed in counters) |
| `tx` / `rx` Mbps | Approx application throughput from framed payload sizes |
| `tick` | Last completed server tick (host) or last applied tick (client) |
| `buf` | Distinct ticks retained in the remote `SnapshotBuffer` |

## Layout

| File | Role |
|------|------|
| `ArenaSession.cs` | Loopback / delayed / UDP session factory + tick pump + metrics |
| `ArenaClientView.cs` | Prediction + interpolation + `PredictionCounters` |
| `ArenaSim.cs` | Shared integrate + commands |
| `Components.cs` | `Pos` / `Vel` / `PlayerOwner` / `Lifetime` |
| `Program.cs` | CLI |

## Limitations

- UDP host is dedicated (no local predicted player in the listen process)
- Fire / projectile spawn is not predicted
- Loopback without `--latency` keeps reconcile error near zero
- Not a full DOTS NetCode ghost / rollback stack

Automated coverage: `tests/Loom.Net.Tests` (`DelayedTransportTests`, prediction/session tests).
