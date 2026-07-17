# Loom console samples

Non-Unity demos under `samples/`. Each project focuses on one library feature.

| Sample | Feature |
|--------|---------|
| [Loom.Sample](Loom.Sample/) | Dense + sparse components, systems, CommandBuffer, events, singletons |
| [Loom.SpeedDemo](Loom.SpeedDemo/) | CreateMany, ParallelEach, stress HUD / headless throughput |
| [Loom.SharedComponents](Loom.SharedComponents/) | `ISharedComponent` interning |
| [Loom.Tags](Loom.Tags/) | Empty tag structs (`With` / `Without`) |
| [Loom.QueryFilters](Loom.QueryFilters/) | `WithAny`, `Enabled`, `QueryFilter`, `ClearEntities` |
| [Loom.Prefabs](Loom.Prefabs/) | `EntityPrefab` / `Prefab` instantiate & deferred spawn |
| [Loom.Hierarchy](Loom.Hierarchy/) | Father/child tree + cascade destroy |
| [Loom.Relations](Loom.Relations/) | Typed `IRelationComponent` links |
| [Loom.Systems](Loom.Systems/) | Owned groups, ordering, lifecycle, nested groups, `IParallelSystem` |
| [Loom.ScheduleRates](Loom.ScheduleRates/) | Init / simulation / presentation groups at different rates (`Run` + `EndFrame`) |
| [Loom.SerializationDemo](Loom.SerializationDemo/) | `WorldSerializer` JSON snapshots |
| [Loom.Accessors](Loom.Accessors/) | `[EcsComponent]` source-generator accessors |

```bash
dotnet run --project samples/Loom.Sample
dotnet run --project samples/Loom.SharedComponents
dotnet run --project samples/Loom.Tags
dotnet run --project samples/Loom.QueryFilters
dotnet run --project samples/Loom.Prefabs
dotnet run --project samples/Loom.Hierarchy
dotnet run --project samples/Loom.Relations
dotnet run --project samples/Loom.Systems
dotnet run --project samples/Loom.ScheduleRates
dotnet run --project samples/Loom.SerializationDemo
dotnet run --project samples/Loom.Accessors
dotnet run -c Release --project samples/Loom.SpeedDemo -- --headless --entities 100000 --seconds 3 --parallel
```

Unity samples live under `integrations/Unity/com.loom.ecs/Samples~/`.
