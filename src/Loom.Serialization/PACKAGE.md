# LoomECS.Serialization

Optional **JSON** and **MemoryPack** world snapshots for [LoomECS](https://www.nuget.org/packages/LoomECS),
including per-type data migrations and document format migrations.

**Target:** `netstandard2.1` · **License:** MIT · **Repo:** [paasso/LoomECS](https://github.com/paasso/LoomECS)

## Install

```bash
dotnet add package LoomECS
dotnet add package LoomECS.Serialization
```

## Quick start

```csharp
using Loom;

var serializer = new WorldSerializer()
    .Register<Position>()
    .Register<Velocity>()
    .RegisterSingleton<FrameTime>();

string json = serializer.SaveToJson(world);
serializer.LoadFromJson(new World(), json);
```

Types stay in the `Loom` namespace (`WorldSerializer`, `RegisterMigration`, …). Full docs:
[github.com/paasso/LoomECS](https://github.com/paasso/LoomECS#serialization-snapshot-v1).
