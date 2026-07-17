# Loom Speed Demo (Unity)

Stress test: tens of thousands of bouncing entities, optional `ParallelEach`, sparse `Pulse` churn.
Rendering uses **`Graphics.DrawMeshInstancedIndirect`**.

## Setup

1. Package Manager → Loom → Samples → **Speed Demo**.
2. Empty GameObject → add `SpeedDemoRunner`.
3. Play Mode (Game view).

## Controls

| Key | Action |
|-----|--------|
| Space | Pause |
| `+` / `-` | Spawn / despawn batch |
| P | Toggle parallel queries |
| C | Toggle sparse Pulse churn |
| D | Toggle drawing |
| R | `ClearEntities` + respawn default count |
