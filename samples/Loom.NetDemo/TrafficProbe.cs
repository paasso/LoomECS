using System.Text;
using Loom;
using Loom.Entities;
using Loom.Net;

/// <summary>
/// Measures MemoryPack snapshot / delta wire sizes for 100 entities with Position+Velocity.
/// Run: <c>dotnet run --project samples/Loom.NetDemo</c>
/// </summary>
static class TrafficProbe
{
    public const int EntityCount = 100;

    public static void Run()
    {
        var serializer = new WorldSerializer()
            .Register<Position>()
            .Register<Velocity>();

        var snapshots = new SnapshotSync(serializer, compress: false);
        var snapshotsBrotli = new SnapshotSync(serializer, compress: true);
        var deltas = new DeltaSync()
            .Register<Position>()
            .Register<Velocity>();

        var world = new World();
        deltas.EnableTracking(world);

        var entities = new Entity[EntityCount];
        for (int i = 0; i < EntityCount; i++)
        {
            entities[i] = world.Create(
                new Position { X = i, Y = i * 0.5f, Z = i * 0.25f },
                new Velocity { X = 1f, Y = 0f, Z = -0.1f });
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
        PrintSize("Delta idle / no changes (raw)", idle.Length);
        PrintSize("Delta idle framed", idleFramed.Length);

        world.ClearComponentChanges();

        // 2) All 100 entities: Position + Velocity dirty via Set
        for (int i = 0; i < EntityCount; i++)
        {
            world.Set(entities[i], new Position { X = i + 1f, Y = i * 0.5f, Z = i * 0.25f });
            world.Set(entities[i], new Velocity { X = 2f, Y = 0.1f, Z = -0.2f });
        }

        byte[] allDirty = deltas.Capture(world);
        byte[] allDirtyFramed = deltas.CaptureFramed(world, tick: 2);
        PrintSize("Delta all 100 Pos+Vel dirty (raw)", allDirty.Length);
        PrintSize("Delta all 100 framed", allDirtyFramed.Length);

        world.ClearComponentChanges();

        // 3) Subset: 10 of 100
        const int subset = 10;
        for (int i = 0; i < subset; i++)
        {
            world.Set(entities[i], new Position { X = i + 10f, Y = 1f, Z = 2f });
            world.Set(entities[i], new Velocity { X = 3f, Y = 0f, Z = 0f });
        }

        byte[] subsetDirty = deltas.Capture(world);
        byte[] subsetFramed = deltas.CaptureFramed(world, tick: 3);
        PrintSize($"Delta {subset}/100 Pos+Vel dirty (raw)", subsetDirty.Length);
        PrintSize($"Delta {subset}/100 framed", subsetFramed.Length);

        Console.WriteLine();
        Console.WriteLine("Mbps if sending THAT payload every tick (payload*8*Hz/1e6):");
        PrintMbps("Snapshot framed (uncompressed)", framed.Length);
        PrintMbps("Delta all-dirty framed", allDirtyFramed.Length);
        PrintMbps("Delta 10/100 framed", subsetFramed.Length);
        PrintMbps("Delta idle framed", idleFramed.Length);

        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - Components: Position/Velocity each float X,Y,Z (12 B value).");
        Console.WriteLine("  - SnapshotSync default compress=false → LCMP (uncompressed MemoryPack).");
        Console.WriteLine("  - NetMessage framing = 1 byte kind + 8 byte tick = +9 bytes.");
        Console.WriteLine("  - DeltaSync LDLT: type names + entity id/version + MemoryPack payloads;");
        Console.WriteLine("    idle still emits headers for each registered type with zero ops.");
        Console.WriteLine("  - Entity spawn/despawn not in delta MVP (use SnapshotSync for joins).");
    }

    static void PrintHeader()
    {
        Console.WriteLine($"=== Loom.Net traffic probe: {EntityCount} entities × Position+Velocity (float3) ===");
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

struct Position
{
    public float X, Y, Z;
}

struct Velocity
{
    public float X, Y, Z;
}
