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

## Results

Ryzen 5 7500F, .NET 10, BenchmarkDotNet Mean / Allocated.

### Dense iteration (100k)

| ECS | Mean | Allocated |
| --- | ---: | ---: |
| Friflo | **246.6 µs** | 88 B |
| Loom | 250.7 µs | 88 B |
| Leo | 254.7 µs | 0 B |
| DefaultEcs | 287.4 µs | 0 B |
| Arch | 792.3 µs | 32 B |

### Filtered query

| ECS | Mean | Allocated |
| --- | ---: | ---: |
| DefaultEcs | **182.8 µs** | 0 B |
| Friflo | 184.7 µs | 88 B |
| Leo | 186.2 µs | 0 B |
| Loom | 190.3 µs | 88 B |
| Arch | 509.9 µs | 32 B |

### Bulk create ×3 (100k)

| ECS | Mean | Allocated |
| --- | ---: | ---: |
| Friflo | **3.50 ms** | 16.0 MB |
| Loom | 3.70 ms | **6.6 MB** |
| Leo | 3.84 ms | 17.0 MB |
| Arch | 7.14 ms | 7.3 MB |
| DefaultEcs | 11.6 ms | 25.1 MB |

### Dense `Status` toggle (10k)

| ECS | Mean | Allocated |
| --- | ---: | ---: |
| Leo* | **91.3 µs** | 0 B |
| DefaultEcs* | 170.8 µs | 0 B |
| Loom | 211.9 µs | 0 B |
| Friflo | 290.4 µs | 0 B |
| Arch | 591.4 µs | 0 B |

\*No archetype move (sparse-set style).

### Sparse `Status` churn (10k)

| ECS | Mean | Allocated |
| --- | ---: | ---: |
| Leo | **92.1 µs** | 0 B |
| Loom | 159.0 µs | 0 B |
| DefaultEcs | 176.0 µs | 0 B |
