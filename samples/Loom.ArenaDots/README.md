# Arena Dots

Authoritative multiplayer “dots in an arena” demo on top of `Loom.Net`
(`AuthoritativeServer` + `NetClient` + `LoopbackTransport`), with **client prediction**
for the local player and **interpolation** for remotes / projectiles.

## What it shows

- 1–4 clients (default **2**) on an in-process loopback mesh
- Each join spawns a `Pos`/`Vel`/`PlayerOwner` entity on the server; join snapshot, then per-tick deltas
- Clients send opaque `Move` / `Fire` command bytes; server applies thrust only to that peer’s dot
- Velocity → position with friction and arena bounds (`ArenaSim.IntegratePlayer` is shared with prediction)
- Local player: `ClientPredictor` applies the same integrate step immediately, then reconciles on each applied server tick
- Remotes + projectiles: `SnapshotBuffer` + `StateInterpolator` (~1.25 tick delay)
- Console mode prints server vs predicted/interpolated samples
- `--visual` opens a Raylib top-down view (predicted local highlighted)

## Run

```bash
# Console convergence log (scripted inputs, ~40 ticks)
dotnet run --project samples/Loom.ArenaDots -- --players 2

# Raylib view: P1 WASD + Space (fire), P2 arrows + Enter
dotnet run --project samples/Loom.ArenaDots -- --visual --players 2
```

## Layout

| File | Role |
|------|------|
| `ArenaSession.cs` | Loopback host + clients + `ArenaClientView`s, join/spawn, tick pump |
| `ArenaClientView.cs` | Per-client prediction + interpolation presentation layer |
| `ArenaSim.cs` | `IAuthoritativeSimulation` — commands + shared `IntegratePlayer` |
| `Components.cs` | `Pos` / `Vel` / `PlayerOwner` / `Lifetime` |
| `Program.cs` | CLI + console / Raylib front-ends |

## Limitations

- Server stays authoritative; prediction is client-only cosmetic for responsiveness
- Fire / projectile spawn is not predicted (appears after the server delta)
- Interpolation needs ≥2 buffered ticks before remotes look smooth (falls back to sim pos)
- Loopback has no latency; reconciliation error stays near zero unless you inject delay yourself
- Not a full DOTS NetCode ghost / rollback stack

Automated coverage lives in `tests/Loom.Net.Tests` (`PredictionInterpolationTests`, `ArenaDotsSessionTests`).
