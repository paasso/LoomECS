using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Loom;
using Loom.Entities;
using MemoryPack;
using MemoryPack.Compression;

namespace Loom.Net
{
    /// <summary>
    /// Thin dirty-component delta sync built on <see cref="World.TrackChanges{T}"/> /
    /// <see cref="World.Set{T}"/> / <see cref="World.MarkChanged{T}"/>.
    /// Captures Added / Changed / Removed for registered component types as MemoryPack payloads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entity spawn/despawn is out of scope for this MVP — use <see cref="SnapshotSync"/> for
    /// joins, periodic corrections, and structural changes. Delta apply requires the target entity
    /// to already be alive with a matching id/version (from a prior snapshot).
    /// </para>
    /// <para>
    /// Call <see cref="Capture"/> before <see cref="World.ClearComponentChanges"/> / end of
    /// <c>Tick</c>, otherwise the dirty lists are empty. Prefer mutating via <c>Set</c> /
    /// <c>MarkChanged</c> so in-place <c>Get</c> edits are recorded.
    /// </para>
    /// <para>
    /// When constructed with <c>compress: true</c>, Capture wraps the LDLT body with MemoryPack's
    /// <see cref="BrotliCompressor"/> (<see cref="CompressionLevel.Fastest"/>) under magic
    /// <c>LDLB</c> (same approach as SnapshotSync / LCMB). Apply accepts both LDLT and LDLB.
    /// </para>
    /// </remarks>
    public sealed class DeltaSync
    {
        private const int FormatVersion = 1;
        private static readonly byte[] Magic = { (byte)'L', (byte)'D', (byte)'L', (byte)'T' };
        /// <summary>ASCII magic for Brotli-compressed deltas: "LDLB".
        /// Payload after the magic is MemoryPack <c>BrotliCompressor</c> output of a full LDLT delta.</summary>
        private static readonly byte[] BrotliMagic = { (byte)'L', (byte)'D', (byte)'L', (byte)'B' };

        private readonly List<IDeltaHandler> _handlers = new List<IDeltaHandler>();
        private readonly Dictionary<string, IDeltaHandler> _byName =
            new Dictionary<string, IDeltaHandler>(StringComparer.Ordinal);
        private readonly bool _compress;

        public DeltaSync(bool compress = false)
        {
            _compress = compress;
        }

        /// <summary>Registers <typeparamref name="T"/> for dirty capture/apply. Call
        /// <see cref="World.TrackChanges{T}"/> on the server world during setup.</summary>
        public DeltaSync Register<T>() where T : struct
        {
            string name = typeof(T).FullName
                          ?? throw new InvalidOperationException($"Type '{typeof(T).Name}' has no FullName.");

            if (_byName.ContainsKey(name))
                return this;

            var handler = new DeltaHandler<T>(name);
            _byName[name] = handler;
            _handlers.Add(handler);
            return this;
        }

        /// <summary>Ensures the server world tracks every registered type.</summary>
        public void EnableTracking(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            for (int i = 0; i < _handlers.Count; i++)
                _handlers[i].EnableTracking(world);
        }

        /// <summary>Serializes dirty component ops since the last change clear.
        /// Returns LDLT (raw) or LDLB (Brotli) depending on construction.</summary>
        public byte[] Capture(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (_handlers.Count == 0)
                throw new InvalidOperationException("Register at least one component type before Capture.");

            byte[] raw = CaptureRaw(world);
            if (!_compress)
                return raw;

            using var compressor = new BrotliCompressor(CompressionLevel.Fastest);
            MemoryPackSerializer.Serialize(compressor, raw);
            byte[] compressed = compressor.ToArray();

            var result = new byte[4 + compressed.Length];
            BrotliMagic.CopyTo(result, 0);
            Buffer.BlockCopy(compressed, 0, result, 4, compressed.Length);
            return result;
        }

        /// <summary>Applies a delta produced by <see cref="Capture"/> onto a peer world.
        /// Accepts uncompressed (<c>LDLT</c>) or Brotli-wrapped (<c>LDLB</c>) payloads.</summary>
        public void Apply(World world, byte[] delta)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (delta == null)
                throw new ArgumentNullException(nameof(delta));

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
            if (version != FormatVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported delta formatVersion {version}; this build understands {FormatVersion}.");
            }

            int typeCount = reader.ReadInt32();
            if (typeCount < 0)
                throw new InvalidOperationException("Delta has a negative type count.");

            for (int t = 0; t < typeCount; t++)
            {
                string typeName = reader.ReadString();
                if (!_byName.TryGetValue(typeName, out var handler))
                {
                    throw new InvalidOperationException(
                        $"Unknown delta component type '{typeName}'. Register<>() it on DeltaSync before Apply.");
                }

                handler.Read(reader, world);
            }
        }

        public byte[] CaptureFramed(World world, long tick) =>
            NetMessage.Pack(NetMessageKind.Delta, tick, Capture(world));

        public void ApplyFramed(World world, ReadOnlySpan<byte> packet, out long tick)
        {
            if (!NetMessage.TryUnpack(packet, out var kind, out tick, out var payload) ||
                kind != NetMessageKind.Delta)
            {
                throw new InvalidOperationException("Packet is not a framed Delta message.");
            }

            Apply(world, payload.ToArray());
        }

        private byte[] CaptureRaw(World world)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(_handlers.Count);
                for (int i = 0; i < _handlers.Count; i++)
                    _handlers[i].Write(writer, world);
            }

            return ms.ToArray();
        }

        private static bool HasMagic(byte[] bytes, byte[] magic) =>
            bytes.Length >= 4 &&
            bytes[0] == magic[0] && bytes[1] == magic[1] &&
            bytes[2] == magic[2] && bytes[3] == magic[3];

        private interface IDeltaHandler
        {
            void EnableTracking(World world);
            void Write(BinaryWriter writer, World world);
            void Read(BinaryReader reader, World world);
        }

        private sealed class DeltaHandler<T> : IDeltaHandler where T : struct
        {
            private readonly string _name;
            private readonly List<Entity> _scratch = new List<Entity>();
            private readonly bool _isEmpty;

            public DeltaHandler(string name)
            {
                _name = name;
                // Matches Loom.Internal.ComponentTypeTraits<T>.IsEmpty (no InternalsVisibleTo needed).
                _isEmpty = typeof(T).GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0;
            }

            public void EnableTracking(World world) => world.TrackChanges<T>();

            public void Write(BinaryWriter writer, World world)
            {
                writer.Write(_name);

                world.CopyAddedTo<T>(_scratch);
                WriteOps(writer, world, _scratch, includePayload: true);

                world.CopyChangedTo<T>(_scratch);
                WriteOps(writer, world, _scratch, includePayload: true);

                world.CopyRemovedTo<T>(_scratch);
                WriteOps(writer, world, _scratch, includePayload: false);
            }

            public void Read(BinaryReader reader, World world)
            {
                ApplyOps(reader, world, addOrSet: true);
                ApplyOps(reader, world, addOrSet: true);
                ApplyOps(reader, world, addOrSet: false);
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
                                "Send a full SnapshotSync first after structural creates.");
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
                                $"Component '{_name}' requires a MemoryPack payload in the delta.");
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
        }
    }
}
