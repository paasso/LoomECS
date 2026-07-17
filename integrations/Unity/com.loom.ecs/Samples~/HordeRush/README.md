# Horde Rush

Twin-stick horde survival sample: sparse `Health` / `HitFlash`, shared `EnemyKind`, `ParallelEach`, `CommandBuffer`, `CreateMany`, and textured GPU instancing.

## Import

1. **Window → Package Manager → Loom ECS → Samples → Horde Rush → Import**
2. Open `Samples/Loom ECS/HordeRush/HordeRush.unity` (or add `HordeRushRunner` + `HordeRushIndirectRenderer` to any scene)
3. Enter Play Mode

Textures are assigned on `HordeRushIndirectRenderer` in the sample scene (from `Resources/HordeRush/`). If fields are empty, the same paths load via `Resources.Load`.

## Controls

| Input | Action |
|-------|--------|
| WASD / arrows | Move |
| Mouse | Aim |
| LMB | Shoot |

## What it shows

| Feature | Where |
|---------|--------|
| Sparse `Health` / `HitFlash` | damage + cleanup; flash only while active |
| Shared `EnemyKind` | `SharedStore` / `GetShared` on enemies |
| `ParallelEach` | enemy chase + bullet flight |
| `CommandBuffer` | deferred destroys after parallel passes |
| `CreateMany` | wave spawns |
| Enemy separation | `EnemySeparationSystem` keeps the horde from stacking |
| GPU instancing | `Graphics.DrawMeshInstanced` + `Loom/HordeRushEnvironment` (Built-in & URP) |

## Tunables

On `HordeRushRunner`: `GameConfig` (waves, difficulty slope, spawn rate, HP, speed). Difficulty scales linearly with wave for enemy count, speed, HP, score, and collision radius.

Camera follows the player (orthographic).
