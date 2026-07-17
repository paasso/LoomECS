# Loom for Unity

Thin embedding layer: a `MonoBehaviour` that owns a `Runtime` (and its `World`) and ticks it each frame, plus an optional GameObject ↔ entity link.

## Install

1. **Core DLL** — build Loom (`netstandard2.1`) and drop `Loom.dll` into your Unity project's `Assets/Plugins/` (or any folder with a matching asmdef named `Loom` that only wraps the DLL).

   ```
   dotnet build src/Loom/Loom.csproj -c Release
   ```

2. **This package** — in Unity Package Manager → *Add package from disk…* → select this folder (`com.loom.ecs`), **or** add a git/local `file:` entry in the project's `manifest.json`:

   ```json
   "com.loom.ecs": "file:../../integrations/Unity/com.loom.ecs"
   ```

Unity **2021.3+** recommended (`netstandard2.1`).

## Quick start

```csharp
using Loom;
using Loom.Systems;
using Loom.Unity;
using UnityEngine;

public sealed class GameRunner : LoomRunner
{
    protected override void OnRuntimeCreated(Runtime runtime)
    {
        Systems.Add(new TransformSyncSystem());
        // Systems.Add(... your systems ...);
    }
}
```

1. Add `GameRunner` to a GameObject in the scene.
2. Spawn entities in `OnRuntimeCreated` (or later via `World` / `Runtime`).
3. For view sync: add `EntityBehaviour` on a prefab, `Bind(runner, entity)`, register with `TransformSyncSystem`, and keep an `UnityPosition` component updated by your simulation.

`LoomRunner` owns a default `Systems` group and runs `Runtime.Run(Systems)` + `EndFrame` each `Update`. It optionally writes `UnityEngine.Time.deltaTime` into a `FrameDelta` singleton before systems run.

## Entity Debugger

Play Mode inspector for live Loom worlds:

1. Enter Play Mode with a `LoomRunner` in the scene.
2. Open **Window → Loom → Entity Debugger**.
3. **Entities** tab — filter / select, edit component & singleton fields (writes back to the World), ECS father/child, ping bound GameObjects.
4. **Archetypes** tab — entity counts, chunk counts, dense component type lists.
5. **Systems** tab — per-system `Update` timings (last / avg / max ms) for the runner's `Systems` group; toggle profiling, sort by time.
6. Selecting an entity draws a Scene-view marker (`UnityPosition` or bound Transform).

From an `EntityBehaviour` inspector in Play Mode: **Open in Loom Entity Debugger**.

## Samples

Import from Package Manager → Loom → Samples:

| Sample | What it shows |
|--------|----------------|
| **Basic Runner** | `UnityPosition` motion + optional `EntityBehaviour` sync |
| **Flappy Bird** | Full mini-game: systems, `CommandBuffer`, events, singletons; textured draw |
| **Speed Demo** | Stress test: `ParallelEach`, sparse churn, `DrawMeshInstancedIndirect` particles |
| **HordeRush** | Twin-stick horde: ParallelEach, sparse TTL/flash, shared enemy kinds, CommandBuffer combat |

**Basic Runner** — add `SampleGameRunner` to a GameObject; optional EntityBehaviour prefab.

**Flappy Bird** — add `FlappyBirdRunner` to a GameObject, enter Play Mode. Space / click to flap; R to retry after death.

**Speed Demo** — add `SpeedDemoRunner`; Space pause, P parallel, C sparse, D draw, R `ClearEntities`+respawn.

**HordeRush** — add `HordeRushRunner` (or open `HordeRush.unity`); WASD + mouse twin-stick, P toggles `ParallelEach`.

Rebuild `Loom.dll` after pulling so Unity picks up inspection / `TrySetComponent` APIs.

## Notes

- **No Burst / no Unity Jobs integration.** Loom runs managed C# on the main thread (plus optional `Query.ParallelEach` on the .NET thread pool). Do not put Loom structural APIs inside Burst jobs; keep simulation in `ISystem` / `LoomRunner.Update`.
- Systems stay single-threaded by default; use `Query.ParallelEach` only for pure value work with no structural changes.
- Source generators (`Loom.Generators`) work in Unity if you reference the analyzer from a Roslyn-capable toolchain; many teams generate accessors in a separate net SDK project and commit / copy the output. Optional for Unity.
- This package does **not** ship rendering, physics, or a full Unity ECS replacement — only the bridge.
