# Loom ECS benchmarks

Compare Loom with Arch, DefaultEcs, Friflo.Engine.ECS, and LeoECS Lite via BenchmarkDotNet.

```powershell
dotnet run -c Release --project benchmarks/Loom.Benchmarks -- --filter *DenseIteration*
```

Use BDN filters as usual. Always Release, no debugger.

## Packages

| ECS | Package | Version |
| --- | --- | --- |
| Loom | `src/Loom` | local |
| Arch | `Arch` | 2.1.0 |
| DefaultEcs | `DefaultEcs` | 0.17.2 |
| Friflo | `Friflo.Engine.ECS` | 3.6.0 |
| LeoECS Lite | `Leopotam.EcsLite` | 1.0.1 |

## What is measured

Same payloads and counts everywhere (`float3`-ish structs). Methods return a checksum/count so the JIT cannot wipe the work.

| Scenario | Count | Who runs it |
| --- | --- | --- |
| Dense iteration `Position`+`Velocity` | 100k | everyone |
| Filtered query (skip `Excluded`) | 100k, 4 groups | everyone |
| Bulk create with 3 components | 100k | everyone |
| Toggle dense `Status` on/off | 10k | everyone |
| Sparse `Status` add/remove | 10k | Loom, Leo, DefaultEcs |
| Shared `Material` interning | 10k | Loom only |

Sparse churn is only for ECS that store components outside archetypes (Loom `ISparseComponent`, Leo pools, DefaultEcs). Arch/Friflo would be measuring archetype moves there — that is already covered by the dense toggle.

Shared interning is a Loom feature; there is nothing equivalent to compare.
