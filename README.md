# Loom

<p align="center">
  <img src="docs/loom-logo.png" alt="Loom logo" width="160" />
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/LoomECS"><img alt="NuGet" src="https://img.shields.io/nuget/v/LoomECS?logo=nuget" /></a>
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-MIT-blue" /></a>
  <a href=".github/workflows/ci.yml"><img alt="CI" src="https://img.shields.io/github/actions/workflow/status/paasso/LoomECS/ci.yml?branch=main&label=CI" /></a>
</p>

Hybrid C# ECS with **archetype columns** for dense data, opt-in **sparse sets** for high-churn tags, and **shared/interned** components.

```bash
dotnet add package LoomECS
# optional snapshots:
dotnet add package LoomECS.Serialization
```

## Core capabilities

- **Hybrid storage** — dense archetypes stay stable; `ISparseComponent` add/remove never moves the entity
- **Fast iteration** — `Each` / struct `IJob` / `EachChunk` (span) / `ParallelEach` for archetype-column data
- **Gameplay loop** — owned `SystemGroup`s + `Runtime.Run` / `EndFrame`, ordering attributes, CommandBuffer, events, singletons
- **Structure** — prefabs, father/child hierarchy, typed relation links, query filters, change tracking
- **Tooling** — Roslyn accessors, optional mask SIMD, Unity UPM + Entity Debugger
- **Managed C#** — `netstandard2.1`, no `unsafe`

## Quickstart

Dense data stays on archetypes. Status effects that appear and disappear often go on
`ISparseComponent` — add/remove does **not** move the entity between tables.

```csharp
using Loom;
using Loom.Components;
using Loom.Entities;
using Loom.Queries;
using Loom.Systems;
using Loom.Commands;

public struct Position { public float X, Y; }          // dense
public struct Velocity { public float X, Y; }          // dense
public struct Health { public int Value; }             // dense
public struct Poisoned : ISparseComponent              // sparse: high-churn status
{
    public int DamagePerTick;
}

sealed class MotionSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        world.Query().EachChunk<Position, Velocity>((_, pos, vel) =>
        {
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i].X += vel[i].X;
                pos[i].Y += vel[i].Y;
            }
        });
    }
}

sealed class PoisonSystem : ISystem
{
    public void Update(Runtime runtime, CommandBuffer commands)
    {
        var world = runtime.World;
        // Only entities that currently have Poisoned — no archetype churn when it expires
        world.Query().With<Poisoned>().Each<Health>((Entity e, ref Health h) =>
        {
            h.Value -= world.Get<Poisoned>(e).DamagePerTick;
            if (h.Value <= 0)
                commands.Destroy(e);
        });
        // When the effect ends: world.Remove<Poisoned>(e) — O(1), dense table untouched
    }
}

var world = new World();
var sim = new Runtime(world);
var hero = world.Create(
    new Position(), new Velocity { X = 1, Y = 0 }, new Health { Value = 10 });
world.Add(hero, new Poisoned { DamagePerTick = 2 }); // apply DoT without an archetype move

var systems = new SystemGroup();
systems.Add(new MotionSystem()).Add(new PoisonSystem());
sim.Run(systems);
sim.EndFrame();
```

Struct jobs when you want the JIT to inline `Execute` (no per-entity delegate):

```csharp
struct MoveJob : IJob<Position, Velocity>
{
    public float Dt;
    public void Execute(Entity entity, ref Position p, ref Velocity v)
    {
        p.X += v.X * Dt;
        p.Y += v.Y * Dt;
    }
}

var job = new MoveJob { Dt = 1f / 60f };
world.Query().Each<MoveJob, Position, Velocity>(ref job);
```

## Features

- [x] Dense archetypes (chunked columns) + `ISparseComponent` + `ISharedComponent`
- [x] Empty tags, `With` / `Without` / `WithAny`, `Enabled` / `Disabled`, `QueryFilter`
- [x] `Create` / `CreateMany`, CommandBuffer, buffered events, singletons
- [x] Systems: owned `SystemGroup`s, `[UpdateAfter]` / `OrderFirst`, `IParallelSystem`, lifecycle
- [x] Opt-in component change tracking (`TrackChanges` / `ChangeQuery`)
- [x] Prefabs, father/child tree, `IRelationComponent` links
- [x] JSON / MemoryPack snapshots + migrations (`LoomECS.Serialization`)
- [x] `[EcsComponent]` source generator accessors
- [x] Unity package: runner, Entity Debugger, Flappy Bird / Speed Demo / HordeRush

## Benchmarks

[BenchmarkDotNet](benchmarks/Loom.Benchmarks) vs Arch, DefaultEcs, Friflo, LeoECS Lite (Ryzen 5 7500F, .NET 10). Mean / allocated.

| | Loom | Friflo | Leo | DefaultEcs | Arch |
|--|--:|--:|--:|--:|--:|
| Dense iteration (100k) | 251 µs / 88 B | **247 µs** / 88 B | 255 µs / 0 B | 287 µs / 0 B | 792 µs / 32 B |
| Filtered query | 190 µs / 88 B | 185 µs / 88 B | 186 µs / 0 B | **183 µs** / 0 B | 510 µs / 32 B |
| Bulk create ×3 | 3.70 ms / **6.6 MB** | **3.50 ms** / 16.0 MB | 3.84 ms / 17.0 MB | 11.6 ms / 25.1 MB | 7.14 ms / 7.3 MB |
| Dense `Status` toggle (10k) | 212 µs / 0 B | 290 µs / 0 B | 91 µs* / 0 B | 171 µs* / 0 B | 591 µs / 0 B |
| Sparse `Status` churn (10k) | 159 µs / 0 B | — | **92 µs** / 0 B | 176 µs / 0 B | — |

\*Leo / DefaultEcs toggle without archetype moves. Sparse churn is Loom / Leo / DefaultEcs only.

```bash
dotnet run -c Release --project benchmarks/Loom.Benchmarks -- --filter *DenseIteration*
```

## Samples & docs

| | |
|--|--|
| Console demos (per feature) | [samples/README.md](samples/README.md) |
| Benchmarks | [benchmarks/Loom.Benchmarks](benchmarks/Loom.Benchmarks/README.md) |
| Unity UPM | [integrations/Unity/com.loom.ecs](integrations/Unity/com.loom.ecs/README.md) |

```bash
dotnet run --project samples/Loom.Sample
dotnet run -c Release --project samples/Loom.SpeedDemo -- --headless --entities 100000 --seconds 3 --parallel
```

## Build

```bash
dotnet test
dotnet pack src/Loom/Loom.csproj -c Release -o artifacts/nuget
dotnet pack src/Loom.Serialization/Loom.Serialization.csproj -c Release -o artifacts/nuget
```

Optional mask SIMD (source builds): `-p:LoomSimd=true`. Publish by pushing a `v*` tag when `NUGET_API_KEY` is set — see [.github/workflows/release.yml](.github/workflows/release.yml).

**Scope:** API may still change before 1.0. Managed C# only (no Burst/Jobs bridge). MIT — [LICENSE](LICENSE).
