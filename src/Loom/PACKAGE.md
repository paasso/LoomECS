# LoomECS

A minimal C# ECS that combines **archetype** storage (cache-friendly dense columns) with
opt-in **sparse-set** components (`ISparseComponent`) so high-churn tags do not thrash the
archetype graph. Also includes shared/interned components, owned system groups, command
buffers, events, and a Roslyn accessor source generator. Optional JSON and MemoryPack snapshots live in
the separate [LoomECS.Serialization](https://www.nuget.org/packages/LoomECS.Serialization) package.

**Target:** `netstandard2.1` · **License:** MIT · **Repo:** [paasso/LoomECS](https://github.com/paasso/LoomECS)

## Install

```bash
dotnet add package LoomECS
```

The package includes the `Loom.Generators` analyzer. Mark components with `[EcsComponent]` to
get typed accessors:

```csharp
[EcsComponent] public struct Position { public float X, Y; }

ref Position p = ref world.Get(entity).Position;
```

## Quick start

```csharp
using Loom;
using Loom.Components;
using Loom.Entities;
using Loom.Queries;
using Loom.Systems;
using Loom.Commands;

var world = new World();
var runtime = new Runtime(world);
var systems = new SystemGroup();
var e = world.Create(new Position { X = 0, Y = 0 }, new Velocity { X = 1, Y = 0 });

world.Query().Each<Position, Velocity>((Entity ent, ref Position p, ref Velocity v) =>
{
    p.X += v.X;
    p.Y += v.Y;
});

runtime.Run(systems);
runtime.EndFrame();
```

Unity uses the separate UPM package under `integrations/Unity/com.loom.ecs` (not this NuGet).

Full docs and samples: [github.com/paasso/LoomECS](https://github.com/paasso/LoomECS).
