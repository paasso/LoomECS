using System.Text;
using Loom;
using Loom.Entities;
using Loom.Net;

/// <summary>
/// Measures MemoryPack snapshot / delta wire sizes for 100 entities with Pos+Vel (float3 each).
/// Run: <c>dotnet run --project samples/Loom.NetDemo</c>
/// </summary>
static class TrafficProbe
{
    public const int EntityCount = 100;

    public static void Run()
    {
        var serializer = new WorldSerializer()
            .Register<Pos>()
            .Register<Vel>();

        var snapshots = new SnapshotSync(serializer, compress: false);
        var snapshotsBrotli = new SnapshotSync(serializer, compress: true);
        var deltas = new DeltaSync(compress: false)
            .Register<Pos>()
            .Register<Vel>();
        var deltasBrotli = new DeltaSync(compress: true)
            .Register<Pos>()
            .Register<Vel>();

        var world = new World();
        deltas.EnableTracking(world);

        var entities = new Entity[EntityCount];
        for (int i = 0; i < EntityCount; i++)
        {
            entities[i] = world.Create(
                new Pos { X = i, Y = i * 0.1f, Z = i * 0.01f },
                new Vel { X = 1f, Y = 0f, Z = 0f });
        }

        // Baseline full snapshot (join / resync). Clear create-time dirty lists first.
        world.ClearComponentChanges();

        byte[] raw = snapshots.Capture(world);
        byte[] framed = snapshots.CaptureFramed(world, tick: 0);
        byte[] brotli = snapshotsBrotli.Capture(world);
        byte[] brotliFramed = snapshotsBrotli.CaptureFramed(world, tick: 0);
        string json = serializer.SaveToJson(world);
        int jsonBytes = Encoding.UTF8.GetByteCount(json);

        PrintHeader();
        PrintSize("Snapshot raw (LCMP, uncompressed)", raw.Length);
        PrintSize("Snapshot framed NetMessage (+9 B)", framed.Length);
        PrintSize("Snapshot Brotli (LCMB)", brotli.Length);
        PrintSize("Snapshot Brotli framed", brotliFramed.Length);
        PrintSize("Snapshot JSON (UTF-8)", jsonBytes);
        Console.WriteLine($"  magic={Encoding.ASCII.GetString(raw.AsSpan(0, 4))}  " +
                          $"brotliMagic={Encoding.ASCII.GetString(brotli.AsSpan(0, 4))}");
        Console.WriteLine();

        // --- Delta scenarios ---
        // 1) Idle: no mutations after clear
        byte[] idle = deltas.Capture(world);
        byte[] idleFramed = deltas.CaptureFramed(world, tick: 1);
        byte[] idleBrotli = deltasBrotli.Capture(world);
        byte[] idleBrotliFramed = deltasBrotli.CaptureFramed(world, tick: 1);
        PrintDeltaRow("Delta idle / no changes", idle, idleFramed, idleBrotli, idleBrotliFramed);

        world.ClearComponentChanges();

        // 2) All 100 entities: Pos + Vel dirty via Set
        for (int i = 0; i < EntityCount; i++)
        {
            world.Set(entities[i], new Pos { X = i + 1f, Y = i * 0.2f, Z = i * 0.02f });
            world.Set(entities[i], new Vel { X = 2f, Y = 1f, Z = 0.5f });
        }

        byte[] allDirty = deltas.Capture(world);
        byte[] allDirtyFramed = deltas.CaptureFramed(world, tick: 2);
        byte[] allDirtyBrotli = deltasBrotli.Capture(world);
        byte[] allDirtyBrotliFramed = deltasBrotli.CaptureFramed(world, tick: 2);
        PrintDeltaRow("Delta all 100 Pos+Vel dirty", allDirty, allDirtyFramed, allDirtyBrotli, allDirtyBrotliFramed);

        world.ClearComponentChanges();

        // 3) Subset: 10 of 100
        const int subset = 10;
        for (int i = 0; i < subset; i++)
        {
            world.Set(entities[i], new Pos { X = i + 10f, Y = 1f, Z = 2f });
            world.Set(entities[i], new Vel { X = 3f, Y = 3f, Z = 3f });
        }

        byte[] subsetDirty = deltas.Capture(world);
        byte[] subsetFramed = deltas.CaptureFramed(world, tick: 3);
        byte[] subsetBrotli = deltasBrotli.Capture(world);
        byte[] subsetBrotliFramed = deltasBrotli.CaptureFramed(world, tick: 3);
        PrintDeltaRow($"Delta {subset}/100 Pos+Vel dirty", subsetDirty, subsetFramed, subsetBrotli, subsetBrotliFramed);

        Console.WriteLine();
        Console.WriteLine("Mbps if sending THAT payload every tick (payload*8*Hz/1e6):");
        PrintMbps("Snapshot framed (uncompressed)", framed.Length);
        PrintMbps("Snapshot Brotli framed", brotliFramed.Length);
        PrintMbps("Delta all-dirty framed", allDirtyFramed.Length);
        PrintMbps("Delta all-dirty Brotli framed", allDirtyBrotliFramed.Length);
        PrintMbps("Delta 10/100 framed", subsetFramed.Length);
        PrintMbps("Delta 10/100 Brotli framed", subsetBrotliFramed.Length);
        PrintMbps("Delta idle framed", idleFramed.Length);
        PrintMbps("Delta idle Brotli framed", idleBrotliFramed.Length);

        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - Components: Pos/Vel each float3 (X,Y,Z = 12 B value), matching NetDemo.");
        Console.WriteLine("  - SnapshotSync default compress=false → LCMP; compress=true → LCMB.");
        Console.WriteLine("  - DeltaSync default compress=false → LDLT; compress=true → LDLB.");
        Console.WriteLine("  - Brotli = MemoryPack BrotliCompressor(CompressionLevel.Fastest).");
        Console.WriteLine("  - NetMessage framing = 1 byte kind + 8 byte tick = +9 bytes.");
        Console.WriteLine("  - DeltaSync LDLT: type names + entity id/version + MemoryPack payloads;");
        Console.WriteLine("    idle still emits headers for each registered type with zero ops.");
        Console.WriteLine("  - Entity spawn/despawn not in delta MVP (use SnapshotSync for joins).");
    }

    static void PrintHeader()
    {
        Console.WriteLine($"=== Loom.Net traffic probe: {EntityCount} entities × Pos+Vel (float3) ===");
        Console.WriteLine();
    }

    static void PrintDeltaRow(string label, byte[] raw, byte[] framed, byte[] brotli, byte[] brotliFramed)
    {
        Console.WriteLine(label);
        PrintSize("  raw (LDLT)", raw.Length);
        PrintSize("  framed", framed.Length);
        PrintSize("  Brotli (LDLB)", brotli.Length);
        PrintSize("  Brotli framed", brotliFramed.Length);
        Console.WriteLine($"  magic={Encoding.ASCII.GetString(raw.AsSpan(0, 4))}  " +
                          $"brotliMagic={Encoding.ASCII.GetString(brotli.AsSpan(0, 4))}  " +
                          $"ratio={100.0 * brotli.Length / raw.Length:F1}% of raw");
        Console.WriteLine();
    }

    static void PrintSize(string label, int bytes)
    {
        Console.WriteLine($"{label,-42} {bytes,8} B  ({bytes / 1024.0:F3} KB)");
    }

    static void PrintMbps(string label, int bytes)
    {
        double b20 = bytes * 8.0 * 20 / 1_000_000.0;
        double b30 = bytes * 8.0 * 30 / 1_000_000.0;
        double b60 = bytes * 8.0 * 60 / 1_000_000.0;
        Console.WriteLine($"{label,-36} 20Hz={b20:F3}  30Hz={b30:F3}  60Hz={b60:F3} Mbps");
    }
}
