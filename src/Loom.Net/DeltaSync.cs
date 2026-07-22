using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Loom;
using Loom.Entities;
using Loom.Internal;
using MemoryPack;
using MemoryPack.Compression;

namespace Loom.Net
{
    /// <summary>
    /// Thin dirty-component delta sync built on <see cref="World.TrackChanges{T}"/> /
    /// <see cref="World.TrackEntityLifecycle"/> / <see cref="World.Set{T}"/> /
    /// <see cref="World.MarkChanged{T}"/>.
    /// Captures entity spawn/despawn plus Added / Changed / Removed for registered component types
    /// as MemoryPack payloads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// First join / full resync still needs <see cref="SnapshotSync"/>. Subsequent structural
    /// creates and destroys can go over the delta path (format version 3).
    /// </para>
    /// <para>
    /// Call <see cref="Capture"/> / <see cref="TryCapture"/> before
    /// <see cref="World.ClearComponentChanges"/> / end of <c>Tick</c>, otherwise the dirty lists
    /// are empty. Prefer mutating via <c>Set</c> / <c>MarkChanged</c> so in-place <c>Get</c> edits
    /// are recorded.
    /// </para>
    /// <para>
    /// Wire format version 3 identifies component types by
    /// <see cref="ComponentTypeTraits{T}.DeterministicHash"/> (<see cref="int"/>), not CLR
    /// FullName strings, and prefixes spawn/despawn ops before per-type dirty lists.
    /// SnapshotSync / WorldSerializer savegames formats are unchanged (still name-based
    /// MemoryPack/JSON). Apply still accepts v2 payloads (component ops only, no structural).
    /// </para>
    /// <para>
    /// When there are zero structural and zero dirty ops, <see cref="TryCapture"/> returns
    /// <c>false</c> and <see cref="Capture"/> / <see cref="CaptureFramed"/> return
    /// <see cref="Array.Empty{T}"/> — callers should skip the send (idle ≈ 0 B on the wire).
    /// </para>
    /// <para>
    /// When constructed with <c>compress: true</c>, Capture <em>allows</em> Brotli wrapping of the
    /// LDLT body (MemoryPack <see cref="BrotliCompressor"/>, <see cref="CompressionLevel.Fastest"/>)
    /// under magic <c>LDLB</c> only when the uncompressed payload length is ≥
    /// <see cref="DefaultCompressThreshold"/> (or the configured threshold). Smaller payloads stay
    /// raw LDLT. Apply accepts both LDLT and LDLB.
    /// </para>
    /// </remarks>
    public sealed class DeltaSync
    {
        /// <summary>Default minimum uncompressed LDLT size before Brotli wrapping is applied when
        /// <c>compress: true</c>.</summary>
        public const int DefaultCompressThreshold = 256;

        /// <summary>LDLT format version: v3 adds spawn/despawn; type ids remain DeterministicHash.</summary>
        private const int FormatVersion = 3;
        private const int MinReadableFormatVersion = 2;
        private static readonly byte[] Magic = { (byte)'L', (byte)'D', (byte)'L', (byte)'T' };
        /// <summary>ASCII magic for Brotli-compressed deltas: "LDLB".
        /// Payload after the magic is MemoryPack <c>BrotliCompressor</c> output of a full LDLT delta.</summary>
        private static readonly byte[] BrotliMagic = { (byte)'L', (byte)'D', (byte)'L', (byte)'B' };

        private readonly List<IDeltaHandler> _handlers = new List<IDeltaHandler>();
        private readonly Dictionary<int, IDeltaHandler> _byHash = new Dictionary<int, IDeltaHandler>();
        private readonly List<Entity> _createdScratch = new List<Entity>();
        private readonly List<Entity> _destroyedScratch = new List<Entity>();
        private readonly HashSet<int> _spawnedIds = new HashSet<int>();
        private readonly HashSet<int> _destroyedIds = new HashSet<int>();
        private readonly List<IDeltaHandler> _spawnComponentScratch = new List<IDeltaHandler>();
        private readonly bool _compress;
        private readonly int _compressThreshold;

        public DeltaSync(bool compress = false, int compressThreshold = DefaultCompressThreshold)
        {
            if (compressThreshold < 0)
                throw new ArgumentOutOfRangeException(nameof(compressThreshold));

            _compress = compress;
            _compressThreshold = compressThreshold;
        }

        /// <summary>When true, Capture may wrap large enough payloads as LDLB.</summary>
        public bool Compress => _compress;

        /// <summary>Minimum uncompressed LDLT length required before Brotli wrapping.</summary>
        public int CompressThreshold => _compressThreshold;

        /// <summary>Registers <typeparamref name="T"/> for dirty capture/apply. Call
        /// <see cref="World.TrackChanges{T}"/> on the server world during setup
        /// (or <see cref="EnableTracking"/>).
        /// Type identity over the wire is <see cref="ComponentTypeTraits{T}.DeterministicHash"/>.</summary>
        /// <exception cref="ComponentHashCollisionException">Thrown when another registered type
        /// already owns the same DeterministicHash (also raised process-wide by traits init).</exception>
        public DeltaSync Register<T>() where T : struct
        {
            int hash = ComponentTypeTraits<T>.DeterministicHash;

            if (_byHash.TryGetValue(hash, out var existing))
            {
                if (existing.ClrType != typeof(T))
                {
                    throw new ComponentHashCollisionException(existing.ClrType, typeof(T), hash);
                }

                return this;
            }

            var handler = new DeltaHandler<T>(hash);
            _byHash[hash] = handler;
            _handlers.Add(handler);
            return this;
        }

        /// <summary>Ensures the server world tracks entity lifecycle and every registered type.</summary>
        public void EnableTracking(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            world.TrackEntityLifecycle();
            for (int i = 0; i < _handlers.Count; i++)
                _handlers[i].EnableTracking(world);
        }

        /// <summary>
        /// Attempts to serialize structural + dirty component ops since the last change clear.
        /// Returns <c>false</c> (and <see cref="Array.Empty{T}"/>) when there are zero ops —
        /// skip broadcasting in that case.
        /// </summary>
        public bool TryCapture(World world, out byte[] delta)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (_handlers.Count == 0)
                throw new InvalidOperationException("Register at least one component type before Capture.");

            if (!TryCaptureRaw(world, out byte[] raw))
            {
                delta = Array.Empty<byte>();
                return false;
            }

            if (!_compress || raw.Length < _compressThreshold)
            {
                delta = raw;
                return true;
            }

            using var compressor = new BrotliCompressor(CompressionLevel.Fastest);
            MemoryPackSerializer.Serialize(compressor, raw);
            byte[] compressed = compressor.ToArray();

            var result = new byte[4 + compressed.Length];
            BrotliMagic.CopyTo(result, 0);
            Buffer.BlockCopy(compressed, 0, result, 4, compressed.Length);
            delta = result;
            return true;
        }

        /// <summary>Serializes structural + dirty component ops since the last change clear.
        /// Returns LDLT (raw) or LDLB (Brotli when allowed and large enough).
        /// Returns <see cref="Array.Empty{T}"/> when there are zero ops (prefer
        /// <see cref="TryCapture"/> and skip the send).</summary>
        public byte[] Capture(World world)
        {
            TryCapture(world, out byte[] delta);
            return delta;
        }

        /// <summary>Applies a delta produced by <see cref="Capture"/> onto a peer world.
        /// Accepts uncompressed (<c>LDLT</c>) or Brotli-wrapped (<c>LDLB</c>) payloads.
        /// Empty payloads are a no-op.</summary>
        public void Apply(World world, byte[] delta)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (delta == null)
                throw new ArgumentNullException(nameof(delta));
            if (delta.Length == 0)
                return;

            if (HasMagic(delta, BrotliMagic))
            {
                using var decompressor = new BrotliDecompressor();
                ReadOnlySequence<byte> seq = decompressor.Decompress(delta.AsSpan(4));
                delta = MemoryPackSerializer.Deserialize<byte[]>(seq)
                        ?? throw new InvalidOperationException("Brotli delta decompressed to null.");
            }

            using var ms = new MemoryStream(delta, writable: false);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);

            var magic = reader.ReadBytes(4);
            if (magic.Length != 4 ||
                magic[0] != Magic[0] || magic[1] != Magic[1] ||
                magic[2] != Magic[2] || magic[3] != Magic[3])
            {
                throw new InvalidOperationException("Delta payload is missing the LDLT magic header.");
            }

            int version = reader.ReadInt32();
            if (version < MinReadableFormatVersion || version > FormatVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported delta formatVersion {version}; this build understands " +
                    $"{MinReadableFormatVersion}..{FormatVersion} (DeterministicHash type ids; " +
                    "v3 adds spawn/despawn).");
            }

            if (version >= 3)
            {
                // Despawn before spawn so same-tick id recycle can replace an occupied slot.
                ApplyDespawns(reader, world);
                ApplySpawns(reader, world);
            }

            int typeCount = reader.ReadInt32();
            if (typeCount < 0)
                throw new InvalidOperationException("Delta has a negative type count.");

            for (int t = 0; t < typeCount; t++)
            {
                int typeHash = reader.ReadInt32();
                if (!_byHash.TryGetValue(typeHash, out var handler))
                {
                    throw new InvalidOperationException(
                        $"Unknown delta component type hash 0x{typeHash:X8}. Register<>() it on DeltaSync before Apply.");
                }

                handler.Read(reader, world);
            }
        }

        /// <summary>Capture + optional <see cref="NetMessage"/> framing.
        /// Returns empty when there are zero ops (skip send).</summary>
        public byte[] CaptureFramed(World world, long tick)
        {
            if (!TryCapture(world, out byte[] payload) || payload.Length == 0)
                return Array.Empty<byte>();
            return NetMessage.Pack(NetMessageKind.Delta, tick, payload);
        }

        public void ApplyFramed(World world, ReadOnlySpan<byte> packet, out long tick)
        {
            if (packet.Length == 0)
            {
                tick = 0;
                return;
            }

            if (!NetMessage.TryUnpack(packet, out var kind, out tick, out var payload) ||
                kind != NetMessageKind.Delta)
            {
                throw new InvalidOperationException("Packet is not a framed Delta message.");
            }

            Apply(world, payload.ToArray());
        }

        private bool TryCaptureRaw(World world, out byte[] raw)
        {
            world.CopyCreatedEntitiesTo(_createdScratch);
            world.CopyDestroyedEntitiesTo(_destroyedScratch);

            _spawnedIds.Clear();
            _destroyedIds.Clear();
            for (int i = 0; i < _createdScratch.Count; i++)
                _spawnedIds.Add(_createdScratch[i].Id);
            for (int i = 0; i < _destroyedScratch.Count; i++)
                _destroyedIds.Add(_destroyedScratch[i].Id);

            int activeCount = 0;
            for (int i = 0; i < _handlers.Count; i++)
            {
                if (_handlers[i].HasOps(world, _spawnedIds, _destroyedIds))
                    activeCount++;
            }

            if (_createdScratch.Count == 0 && _destroyedScratch.Count == 0 && activeCount == 0)
            {
                raw = Array.Empty<byte>();
                return false;
            }

            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);

                WriteDespawns(writer);
                WriteSpawns(writer, world);

                writer.Write(activeCount);
                for (int i = 0; i < _handlers.Count; i++)
                {
                    if (!_handlers[i].HasOps(world, _spawnedIds, _destroyedIds))
                        continue;
                    _handlers[i].Write(writer, world, _spawnedIds, _destroyedIds);
                }
            }

            raw = ms.ToArray();
            return true;
        }

        private void WriteSpawns(BinaryWriter writer, World world)
        {
            writer.Write(_createdScratch.Count);
            for (int i = 0; i < _createdScratch.Count; i++)
            {
                var entity = _createdScratch[i];
                writer.Write(entity.Id);
                writer.Write(entity.Version);

                _spawnComponentScratch.Clear();
                for (int h = 0; h < _handlers.Count; h++)
                {
                    if (_handlers[h].EntityHasComponent(world, entity))
                        _spawnComponentScratch.Add(_handlers[h]);
                }

                writer.Write(_spawnComponentScratch.Count);
                for (int h = 0; h < _spawnComponentScratch.Count; h++)
                    _spawnComponentScratch[h].WriteSpawnComponent(writer, world, entity);
            }
        }

        private void WriteDespawns(BinaryWriter writer)
        {
            writer.Write(_destroyedScratch.Count);
            for (int i = 0; i < _destroyedScratch.Count; i++)
            {
                var entity = _destroyedScratch[i];
                writer.Write(entity.Id);
                writer.Write(entity.Version);
            }
        }

        private void ApplySpawns(BinaryReader reader, World world)
        {
            int spawnCount = reader.ReadInt32();
            if (spawnCount < 0)
                throw new InvalidOperationException("Delta has a negative spawn count.");

            for (int i = 0; i < spawnCount; i++)
            {
                int id = reader.ReadInt32();
                int version = reader.ReadInt32();
                int componentCount = reader.ReadInt32();
                if (componentCount < 0)
                    throw new InvalidOperationException("Delta has a negative spawn component count.");

                Entity entity;
                if (world.TryGetAliveEntity(id, out var existing))
                {
                    if (existing.Version != version)
                    {
                        throw new InvalidOperationException(
                            $"Cannot spawn entity id={id} version={version}: slot is alive at version={existing.Version}.");
                    }

                    entity = existing;
                }
                else
                {
                    entity = world.RestoreEntity(id, version);
                }

                for (int c = 0; c < componentCount; c++)
                {
                    int typeHash = reader.ReadInt32();
                    if (!_byHash.TryGetValue(typeHash, out var handler))
                    {
                        throw new InvalidOperationException(
                            $"Unknown spawn component type hash 0x{typeHash:X8}. Register<>() it on DeltaSync before Apply.");
                    }

                    handler.ReadSpawnComponent(reader, world, entity);
                }
            }
        }

        private static void ApplyDespawns(BinaryReader reader, World world)
        {
            int despawnCount = reader.ReadInt32();
            if (despawnCount < 0)
                throw new InvalidOperationException("Delta has a negative despawn count.");

            for (int i = 0; i < despawnCount; i++)
            {
                int id = reader.ReadInt32();
                int version = reader.ReadInt32();

                if (world.TryGetAliveEntity(id, out var entity) && entity.Version == version)
                    world.Destroy(entity);
            }
        }

        private static bool HasMagic(byte[] bytes, byte[] magic) =>
            bytes.Length >= 4 &&
            bytes[0] == magic[0] && bytes[1] == magic[1] &&
            bytes[2] == magic[2] && bytes[3] == magic[3];

        private interface IDeltaHandler
        {
            Type ClrType { get; }
            void EnableTracking(World world);
            bool HasOps(World world, HashSet<int> spawnedIds, HashSet<int> destroyedIds);
            void Write(BinaryWriter writer, World world, HashSet<int> spawnedIds, HashSet<int> destroyedIds);
            void Read(BinaryReader reader, World world);
            bool EntityHasComponent(World world, Entity entity);
            void WriteSpawnComponent(BinaryWriter writer, World world, Entity entity);
            void ReadSpawnComponent(BinaryReader reader, World world, Entity entity);
        }

        private sealed class DeltaHandler<T> : IDeltaHandler where T : struct
        {
            private readonly int _hash;
            private readonly List<Entity> _scratch = new List<Entity>();
            private readonly bool _isEmpty;

            public DeltaHandler(int hash)
            {
                _hash = hash;
                _isEmpty = ComponentTypeTraits<T>.IsEmpty;
            }

            public Type ClrType => typeof(T);

            public void EnableTracking(World world) => world.TrackChanges<T>();

            public bool EntityHasComponent(World world, Entity entity) => world.Has<T>(entity);

            public bool HasOps(World world, HashSet<int> spawnedIds, HashSet<int> destroyedIds)
            {
                world.CopyAddedTo<T>(_scratch);
                FilterOut(_scratch, spawnedIds);
                if (_scratch.Count > 0)
                    return true;
                world.CopyChangedTo<T>(_scratch);
                FilterOut(_scratch, spawnedIds);
                if (_scratch.Count > 0)
                    return true;
                world.CopyRemovedTo<T>(_scratch);
                FilterOut(_scratch, destroyedIds);
                return _scratch.Count > 0;
            }

            public void Write(BinaryWriter writer, World world, HashSet<int> spawnedIds, HashSet<int> destroyedIds)
            {
                writer.Write(_hash);

                world.CopyAddedTo<T>(_scratch);
                FilterOut(_scratch, spawnedIds);
                WriteOps(writer, world, _scratch, includePayload: true);

                world.CopyChangedTo<T>(_scratch);
                FilterOut(_scratch, spawnedIds);
                WriteOps(writer, world, _scratch, includePayload: true);

                world.CopyRemovedTo<T>(_scratch);
                FilterOut(_scratch, destroyedIds);
                WriteOps(writer, world, _scratch, includePayload: false);
            }

            public void Read(BinaryReader reader, World world)
            {
                ApplyOps(reader, world, addOrSet: true);
                ApplyOps(reader, world, addOrSet: true);
                ApplyOps(reader, world, addOrSet: false);
            }

            public void WriteSpawnComponent(BinaryWriter writer, World world, Entity entity)
            {
                writer.Write(_hash);
                if (_isEmpty)
                {
                    writer.Write(0);
                    return;
                }

                byte[] payload = MemoryPackSerializer.Serialize(world.Get<T>(entity));
                writer.Write(payload.Length);
                writer.Write(payload);
            }

            public void ReadSpawnComponent(BinaryReader reader, World world, Entity entity)
            {
                int length = reader.ReadInt32();
                if (length < 0)
                    throw new InvalidOperationException("Delta has a negative spawn payload length.");

                byte[] payload = length == 0 ? Array.Empty<byte>() : reader.ReadBytes(length);
                if (payload.Length != length)
                    throw new InvalidOperationException("Delta ended inside a spawn component payload.");

                if (_isEmpty)
                {
                    if (!world.Has<T>(entity))
                        world.Add(entity, default(T));
                    return;
                }

                if (length == 0)
                {
                    throw new InvalidOperationException(
                        $"Component hash 0x{_hash:X8} requires a MemoryPack payload in the spawn.");
                }

                T value = MemoryPackSerializer.Deserialize<T>(payload);
                world.AddOrSet(entity, value);
            }

            private void WriteOps(BinaryWriter writer, World world, List<Entity> entities, bool includePayload)
            {
                writer.Write(entities.Count);
                for (int i = 0; i < entities.Count; i++)
                {
                    var entity = entities[i];
                    writer.Write(entity.Id);
                    writer.Write(entity.Version);
                    if (!includePayload)
                        continue;

                    if (_isEmpty)
                    {
                        writer.Write(0);
                        continue;
                    }

                    byte[] payload = MemoryPackSerializer.Serialize(world.Get<T>(entity));
                    writer.Write(payload.Length);
                    writer.Write(payload);
                }
            }

            private void ApplyOps(BinaryReader reader, World world, bool addOrSet)
            {
                int count = reader.ReadInt32();
                if (count < 0)
                    throw new InvalidOperationException("Delta has a negative op count.");

                for (int i = 0; i < count; i++)
                {
                    int id = reader.ReadInt32();
                    int version = reader.ReadInt32();

                    if (addOrSet)
                    {
                        int length = reader.ReadInt32();
                        if (length < 0)
                            throw new InvalidOperationException("Delta has a negative payload length.");

                        byte[] payload = length == 0 ? Array.Empty<byte>() : reader.ReadBytes(length);
                        if (payload.Length != length)
                            throw new InvalidOperationException("Delta ended inside a component payload.");

                        if (!world.TryGetAliveEntity(id, out var entity) || entity.Version != version)
                        {
                            throw new InvalidOperationException(
                                $"Cannot apply delta for entity id={id} version={version}: " +
                                "entity is not alive on the peer (or version mismatch). " +
                                "Send a full SnapshotSync first after join, or ensure spawn ops precede component dirty ops.");
                        }

                        if (_isEmpty)
                        {
                            if (!world.Has<T>(entity))
                                world.Add(entity, default(T));
                            continue;
                        }

                        if (length == 0)
                        {
                            throw new InvalidOperationException(
                                $"Component hash 0x{_hash:X8} requires a MemoryPack payload in the delta.");
                        }

                        T value = MemoryPackSerializer.Deserialize<T>(payload);
                        world.AddOrSet(entity, value);
                    }
                    else if (world.TryGetAliveEntity(id, out var removed) &&
                             removed.Version == version &&
                             world.Has<T>(removed))
                    {
                        world.Remove<T>(removed);
                    }
                }
            }

            private static void FilterOut(List<Entity> entities, HashSet<int> ids)
            {
                if (ids.Count == 0 || entities.Count == 0)
                    return;

                int write = 0;
                for (int i = 0; i < entities.Count; i++)
                {
                    if (ids.Contains(entities[i].Id))
                        continue;
                    entities[write++] = entities[i];
                }

                if (write < entities.Count)
                    entities.RemoveRange(write, entities.Count - write);
            }
        }
    }
}
