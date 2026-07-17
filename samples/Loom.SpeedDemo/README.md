# Loom Speed Demo

Interactive workload for measuring update throughput on large entity sets.

## Run (window)

```bash
dotnet run -c Release --project samples/Loom.SpeedDemo
```

HUD shows **tick ms**, **sim FPS**, and **M entity·updates/s**. Particles are drawn with GPU
**instancing** (`DrawMeshInstanced`, one disc mesh + transform batches). Press **D** to disable
drawing for a pure sim readout.

| Key | Action |
|-----|--------|
| Space | Pause / resume |
| `+` / `-` | Spawn / despawn 10 000 entities |
| P | Toggle `Query.ParallelEach` |
| C | Toggle sparse `Pulse` churn (no archetype moves) |
| D | Toggle particle drawing |
| R | Reset to 50 000 entities |

## Run (headless numbers)

```bash
dotnet run -c Release --project samples/Loom.SpeedDemo -- --headless --entities 100000 --seconds 3 --parallel
```

Useful for README snippets and CI-style smoke checks (no window).
