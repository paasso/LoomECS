using System.IO;
using System.IO.Compression;
using System.Text;
using Loom;
using Loom.Entities;
using Loom.Internal;
using Loom.Net;
using MemoryPack;
using MemoryPack.Compression;

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

        world.ClearComponentChanges();

        // Baseline full snapshot (join / resync).
        byte[] raw = snapshots.Capture(world);
        byte[] framed = snapshots.CaptureFramed(world, tick: 0);
        byte[] brotli = snapshotsBrotli.Capture(world);
        byte[] brotliFramed = snapshotsBrotli.CaptureFramed(world, tick: 0);
        string json = serializer.SaveToJson(world);
        int jsonBytes = Encoding.UTF8.GetByteCount(json);

        PrintHeader();
        PrintSize("Snapshot raw (LCMP, uncompressed)", raw.Length);
        PrintSize("Snapshot framed NetMessage (+9 B)", framed.Length);
        PrintSize("Snapshot Brotli allowed (adaptive)", brotli.Length);
        PrintSize("Snapshot Brotli framed", brotliFramed.Length);
        PrintSize("Snapshot JSON (UTF-8)", jsonBytes);
        Console.WriteLine($"  magic={Encoding.ASCII.GetString(raw.AsSpan(0, 4))}  " +
                          $"compressMagic={Encoding.ASCII.GetString(brotli.AsSpan(0, 4))}  " +
                          $"(compress:true → LCMB only if ≥{DeltaSync.DefaultCompressThreshold} B)");
        Console.WriteLine();

        // Tiny world: compress:true but below threshold → stays LCMP
        {
            var tiny = new World();
            tiny.Create(new Pos { X = 1, Y = 2, Z = 3 }, new Vel { X = 0, Y = 0, Z = 0 });
            byte[] tinyRaw = snapshots.Capture(tiny);
            byte[] tinyAllow = snapshotsBrotli.Capture(tiny);
            Console.WriteLine("Tiny snapshot (1 entity, compress:true adaptive)");
            PrintSize("  raw / allow-compress", tinyRaw.Length);
            Console.WriteLine($"  magic raw={Encoding.ASCII.GetString(tinyRaw.AsSpan(0, 4))}  " +
                              $"allow={Encoding.ASCII.GetString(tinyAllow.AsSpan(0, 4))}  " +
                              $"(expect both LCMP when under threshold)");
            Console.WriteLine();
        }

        // --- Delta scenarios (raw MemoryPack float3 payloads) ---
        // 1) Idle: no mutations after clear → empty capture (skip send)
        bool idleHas = deltas.TryCapture(world, out byte[] idle);
        byte[] idleFramed = deltas.CaptureFramed(world, tick: 1);
        bool idleBrotliHas = deltasBrotli.TryCapture(world, out byte[] idleBrotli);
        byte[] idleBrotliFramed = deltasBrotli.CaptureFramed(world, tick: 1);
        PrintDeltaRow("Delta idle / no changes (skip send)", idle, idleFramed, idleBrotli, idleBrotliFramed,
            idleHas, idleBrotliHas);

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
        PrintDeltaRow("Delta all 100 Pos+Vel dirty (raw float)", allDirty, allDirtyFramed, allDirtyBrotli, allDirtyBrotliFramed);

        byte[] allQ = CaptureQuantizedDelta(world, compress: false);
        byte[] allQBrotli = CaptureQuantizedDelta(world, compress: true);
        PrintQuantizedRow("  quantized Int16 (±Brotli)", allQ, allQBrotli, allDirty.Length);

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
        PrintDeltaRow($"Delta {subset}/100 Pos+Vel dirty (raw float)", subsetDirty, subsetFramed, subsetBrotli, subsetBrotliFramed);

        byte[] subsetQ = CaptureQuantizedDelta(world, compress: false);
        byte[] subsetQBrotli = CaptureQuantizedDelta(world, compress: true);
        PrintQuantizedRow("  quantized Int16 (±Brotli)", subsetQ, subsetQBrotli, subsetDirty.Length);

        world.ClearComponentChanges();

        // 4) Tiny dirty (1 entity) with compress:true → stays LDLT under threshold
        world.Set(entities[0], new Pos { X = 99f, Y = 0f, Z = 0f });
        byte[] tinyDirty = deltas.Capture(world);
        byte[] tinyDirtyBrotli = deltasBrotli.Capture(world);
        world.ClearComponentChanges();
        Console.WriteLine("Delta 1/100 Pos dirty (adaptive compress:true)");
        PrintSize("  raw (LDLT)", tinyDirty.Length);
        PrintSize("  allow-compress", tinyDirtyBrotli.Length);
        Console.WriteLine($"  magic raw={Encoding.ASCII.GetString(tinyDirty.AsSpan(0, 4))}  " +
                          $"allow={Encoding.ASCII.GetString(tinyDirtyBrotli.AsSpan(0, 4))}  " +
                          $"(expect LDLT when under {DeltaSync.DefaultCompressThreshold} B)");
        Console.WriteLine();

        // 5) Spawn 5 entities via delta (no snapshot)
        for (int i = 0; i < 5; i++)
        {
            world.Create(
                new Pos { X = 1000 + i, Y = 0f, Z = 0f },
                new Vel { X = 0f, Y = 1f, Z = 0f });
        }

        byte[] spawnDelta = deltas.Capture(world);
        byte[] spawnBrotli = deltasBrotli.Capture(world);
        world.ClearComponentChanges();
        PrintDeltaRow("Delta spawn 5 entities (Pos+Vel)", spawnDelta, spawnDelta, spawnBrotli, spawnBrotli);

        Console.WriteLine();
        Console.WriteLine("Mbps if sending THAT payload every tick (payload*8*Hz/1e6):");
        PrintMbps("Snapshot framed (uncompressed)", framed.Length);
        PrintMbps("Snapshot Brotli framed", brotliFramed.Length);
        PrintMbps("Delta all-dirty framed", allDirtyFramed.Length);
        PrintMbps("Delta all-dirty Brotli framed", allDirtyBrotliFramed.Length);
        PrintMbps("Delta all-dirty quantized", allQ.Length);
        PrintMbps("Delta all-dirty quant+Brotli", allQBrotli.Length);
        PrintMbps("Delta 10/100 framed", subsetFramed.Length);
        PrintMbps("Delta 10/100 Brotli framed", subsetBrotliFramed.Length);
        PrintMbps("Delta 10/100 quantized", subsetQ.Length);
        PrintMbps("Delta 10/100 quant+Brotli", subsetQBrotli.Length);
        PrintMbps("Delta 1/100 (tiny)", tinyDirty.Length);
        PrintMbps("Delta spawn 5", spawnDelta.Length);
        PrintMbps("Delta idle framed", idleFramed.Length);
        PrintMbps("Delta idle Brotli framed", idleBrotliFramed.Length);

        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - Components: Pos/Vel each float3 (X,Y,Z = 12 B value), matching NetDemo.");
        Console.WriteLine("  - SnapshotSync still name-based via WorldSerializer (LCMP/LCMB/JSON unchanged).");
        Console.WriteLine("  - DeltaSync v3: DeterministicHash type ids + spawn/despawn ops.");
        Console.WriteLine("  - First join still needs SnapshotSync; later creates/destroys can use delta.");
        Console.WriteLine($"  - compress:true = allow Brotli when payload ≥ {DeltaSync.DefaultCompressThreshold} B; else stay LDLT/LCMP.");
        Console.WriteLine("  - Empty dirty lists → Capture/TryCapture return 0 B (skip send; idle ≈ 0 B).");
        Console.WriteLine("  - Quantized probe: same LDLT layout, MemoryPack float3 replaced by 3×Int16 (cm).");
        Console.WriteLine("  - Brotli = MemoryPack BrotliCompressor(CompressionLevel.Fastest).");
        Console.WriteLine("  - NetMessage framing = 1 byte kind + 8 byte tick = +9 bytes (only when non-empty).");
    }

    /// <summary>
    /// Builds an LDLT-shaped delta using DeterministicHash type ids and Int16-quantized float3
    /// payloads (probe-only; not applied by DeltaSync.Apply).
    /// </summary>
    static byte[] CaptureQuantizedDelta(World world, bool compress)
    {
        var scratch = new List<Entity>();
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(new byte[] { (byte)'L', (byte)'D', (byte)'L', (byte)'T' });
            writer.Write(3); // formatVersion matching DeltaSync v3
            writer.Write(0); // despawnCount
            writer.Write(0); // spawnCount

            int posOps = CountOpsFor<Pos>(world, scratch);
            int velOps = CountOpsFor<Vel>(world, scratch);
            int active = (posOps > 0 ? 1 : 0) + (velOps > 0 ? 1 : 0);
            if (active == 0)
                return Array.Empty<byte>();

            writer.Write(active);

            if (posOps > 0)
                WriteQuantizedType<Pos>(writer, world, scratch, p => (p.X, p.Y, p.Z));
            if (velOps > 0)
                WriteQuantizedType<Vel>(writer, world, scratch, v => (v.X, v.Y, v.Z));
        }

        byte[] raw = ms.ToArray();
        if (!compress || raw.Length < DeltaSync.DefaultCompressThreshold)
            return raw;

        using var compressor = new BrotliCompressor(CompressionLevel.Fastest);
        MemoryPackSerializer.Serialize(compressor, raw);
        byte[] compressed = compressor.ToArray();
        var result = new byte[4 + compressed.Length];
        result[0] = (byte)'L';
        result[1] = (byte)'D';
        result[2] = (byte)'L';
        result[3] = (byte)'B';
        Buffer.BlockCopy(compressed, 0, result, 4, compressed.Length);
        return result;
    }

    static int CountOpsFor<T>(World world, List<Entity> scratch) where T : struct
    {
        int n = 0;
        world.CopyAddedTo<T>(scratch);
        n += scratch.Count;
        world.CopyChangedTo<T>(scratch);
        n += scratch.Count;
        world.CopyRemovedTo<T>(scratch);
        n += scratch.Count;
        return n;
    }

    static void WriteQuantizedType<T>(BinaryWriter writer, World world, List<Entity> scratch,
        Func<T, (float x, float y, float z)> xyz) where T : struct
    {
        writer.Write(typeof(T).ComputeDeterministicHash());

        world.CopyAddedTo<T>(scratch);
        WriteQuantizedOps(writer, world, scratch, xyz, includePayload: true);
        world.CopyChangedTo<T>(scratch);
        WriteQuantizedOps(writer, world, scratch, xyz, includePayload: true);
        world.CopyRemovedTo<T>(scratch);
        WriteQuantizedOps(writer, world, scratch, xyz, includePayload: false);
    }

    static void WriteQuantizedOps<T>(BinaryWriter writer, World world, List<Entity> entities,
        Func<T, (float x, float y, float z)> xyz, bool includePayload) where T : struct
    {
        writer.Write(entities.Count);
        Span<byte> q = stackalloc byte[NetFloat3Quantize.EncodedByteCount];
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            writer.Write(entity.Id);
            writer.Write(entity.Version);
            if (!includePayload)
                continue;

            var (x, y, z) = xyz(world.Get<T>(entity));
            NetFloat3Quantize.WriteInt16(q, x, y, z);
            writer.Write(NetFloat3Quantize.EncodedByteCount);
            writer.Write(q);
        }
    }

    static void PrintHeader()
    {
        Console.WriteLine($"=== Loom.Net traffic probe: {EntityCount} entities × Pos+Vel (float3) ===");
        Console.WriteLine();
    }

    static void PrintDeltaRow(string label, byte[] raw, byte[] framed, byte[] brotli, byte[] brotliFramed,
        bool? rawHas = null, bool? brotliHas = null)
    {
        Console.WriteLine(label);
        PrintSize("  raw (LDLT)", raw.Length);
        PrintSize("  framed", framed.Length);
        PrintSize("  Brotli (LDLB)", brotli.Length);
        PrintSize("  Brotli framed", brotliFramed.Length);
        if (raw.Length >= 4 && brotli.Length >= 4)
        {
            Console.WriteLine($"  magic={Encoding.ASCII.GetString(raw.AsSpan(0, 4))}  " +
                              $"brotliMagic={Encoding.ASCII.GetString(brotli.AsSpan(0, 4))}  " +
                              $"ratio={100.0 * brotli.Length / raw.Length:F1}% of raw");
        }
        else
        {
            string has = rawHas.HasValue ? $" TryCapture={rawHas}/{brotliHas}" : "";
            Console.WriteLine($"  (empty — skip send){has}");
        }

        Console.WriteLine();
    }

    static void PrintQuantizedRow(string label, byte[] raw, byte[] brotli, int floatRawBytes)
    {
        Console.WriteLine(label);
        PrintSize("  raw quantized", raw.Length);
        PrintSize("  Brotli quantized", brotli.Length);
        if (floatRawBytes > 0 && raw.Length > 0)
            Console.WriteLine($"  vs float raw: {raw.Length - floatRawBytes:+#;-#;0} B  " +
                              $"({100.0 * raw.Length / floatRawBytes:F1}% of float)");
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
