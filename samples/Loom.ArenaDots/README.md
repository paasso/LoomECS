# Arena Dots

Authoritative multiplayer “dots in an arena” demo on top of `Loom.Net`
(`AuthoritativeServer` + `NetClient` + `LoopbackTransport`).

## What it shows

- 1–4 clients (default **2**) on an in-process loopback mesh
- Each join spawns a `Pos`/`Vel`/`PlayerOwner` entity on the server; join snapshot, then per-tick deltas
- Clients send opaque `Move` / `Fire` command bytes; server applies thrust only to that peer’s dot
- Velocity → position with friction and arena bounds
- Console mode prints server vs each client position (convergence)
- `--visual` opens a Raylib top-down view (same stack as `Loom.SpeedDemo`)

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
| `ArenaSession.cs` | Loopback host + clients, join/spawn, tick pump |
| `ArenaSim.cs` | `IAuthoritativeSimulation` — commands + integrate |
| `Components.cs` | `Pos` / `Vel` / `PlayerOwner` / `Lifetime` |
| `Program.cs` | CLI + console / Raylib front-ends |

Automated coverage lives in `tests/Loom.Net.Tests/ArenaDotsSessionTests.cs`.
