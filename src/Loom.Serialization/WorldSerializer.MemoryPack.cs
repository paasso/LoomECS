using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Loom.Internal;
using MemoryPack;
using MemoryPack.Compression;

namespace Loom
{
    public sealed partial class WorldSerializer
    {
        /// <summary>Saves a world snapshot using MemoryPack for component/singleton payloads
        /// (magic <c>LCMP</c>, or <c>LCMB</c> when <paramref name="compress"/> is true).
        /// Faster and more compact than JSON snapshots for unmanaged component structs.
        /// <see cref="RegisterMigration{T}"/> and <see cref="RegisterFormatMigration"/> do not apply —
        /// payload versions must already match the registered target version.</summary>
        /// <remarks>
        /// Unmanaged component structs work without <c>[MemoryPackable]</c>. Managed shapes need
        /// MemoryPack annotations on the component type. Load/save keep component values typed —
        /// no boxing through <c>object</c>.
        /// <para>
        /// When <paramref name="compress"/> is true, the LCMP body is compressed with MemoryPack's
        /// <see cref="BrotliCompressor"/> (<see cref="CompressionLevel.Fastest"/>).
        /// </para>
        /// </remarks>
        public byte[] SaveToMemoryPack(World world, bool compress = false)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            byte[] raw = SaveToMemoryPackCore(world);
            if (!compress)
                return raw;

            using var compressor = new BrotliCompressor(CompressionLevel.Fastest);
            // Official MemoryPack path: serialize the LCMP blob into the compressor buffer-writer.
            MemoryPackSerializer.Serialize(compressor, raw);
            byte[] compressed = compressor.ToArray();

            var result = new byte[4 + compressed.Length];
            MemoryPackBrotliMagic.CopyTo(result, 0);
            Buffer.BlockCopy(compressed, 0, result, 4, compressed.Length);
            return result;
        }

        /// <summary>Loads a <see cref="SaveToMemoryPack"/> snapshot into a pristine world.
        /// Accepts uncompressed (<c>LCMP</c>) or Brotli-wrapped (<c>LCMB</c>) payloads.
        /// Component data versions must match registration; JSON migrations are not run.</summary>
        public void LoadFromMemoryPack(World world, byte[] bytes)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            EnsurePristine(world, nameof(LoadFromMemoryPack));
            LoadFromMemoryPackAfterPristine(world, bytes);
        }

        /// <summary>Resets <paramref name="world"/> to pristine (clearing entities and singletons)
        /// then loads a MemoryPack snapshot. Intended for live client apply / net resync.</summary>
        public void ReplaceFromMemoryPack(World world, byte[] bytes)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            world.Reset(clearSingletons: true);
            LoadFromMemoryPackAfterPristine(world, bytes);
        }

        private void LoadFromMemoryPackAfterPristine(World world, byte[] bytes)
        {
            if (HasMagic(bytes, MemoryPackBrotliMagic))
            {
                using var decompressor = new BrotliDecompressor();
                ReadOnlySequence<byte> seq = decompressor.Decompress(bytes.AsSpan(4));
                bytes = MemoryPackSerializer.Deserialize<byte[]>(seq)
                        ?? throw new InvalidOperationException("Brotli MemoryPack snapshot decompressed to null.");
            }

            LoadFromMemoryPackCore(world, bytes);
        }

        private byte[] SaveToMemoryPackCore(World world)
        {
            var registry = world.ComponentTypes;
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(MemoryPackMagic);
                writer.Write(FormatVersion);

                long entityCountPos = ms.Position;
                writer.Write(0); // placeholder

                int entityCount = 0;
                for (int id = 1; id < world.NextEntityIdExclusive; id++)
                {
                    if (!world.TryGetAliveEntity(id, out var entity))
                        continue;

                    entityCount++;
                    writer.Write(entity.Id);
                    writer.Write(entity.Version);

                    var father = world.GetFatherForSerialization(entity);
                    writer.Write(father.IsNull ? -1 : father.Id);

                    var archetype = world.GetArchetype(entity);
                    if (archetype == null || archetype.ComponentIds.Length == 0)
                    {
                        writer.Write(0);
                    }
                    else
                    {
                        writer.Write(archetype.ComponentIds.Length);
                        foreach (int componentId in archetype.ComponentIds)
                        {
                            var info = registry.Get(componentId);
                            WriteMemoryPackComponent(writer, world, entity, info);
                        }
                    }

                    var sparseMask = world.GetSparseMaskForSerialization(entity.Id);
                    if (sparseMask.Equals(ComponentMask.Empty))
                    {
                        writer.Write(0);
                    }
                    else
                    {
                        int sparseCount = 0;
                        foreach (int _ in sparseMask)
                            sparseCount++;
                        writer.Write(sparseCount);
                        foreach (int sparseId in sparseMask)
                        {
                            var info = registry.GetSparse(sparseId);
                            WriteMemoryPackComponent(writer, world, entity, info);
                        }
                    }
                }

                long afterEntities = ms.Position;
                ms.Position = entityCountPos;
                writer.Write(entityCount);
                ms.Position = afterEntities;

                WriteMemoryPackSingletons(writer, world);
            }

            return ms.ToArray();
        }

        private void LoadFromMemoryPackCore(World world, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);

            var magic = reader.ReadBytes(4);
            if (magic.Length != 4 ||
                magic[0] != MemoryPackMagic[0] || magic[1] != MemoryPackMagic[1] ||
                magic[2] != MemoryPackMagic[2] || magic[3] != MemoryPackMagic[3])
            {
                throw new InvalidOperationException(
                    "MemoryPack snapshot is missing the LCMP magic header.");
            }

            int formatVersion = reader.ReadInt32();
            if (formatVersion != FormatVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported MemoryPack snapshot formatVersion {formatVersion}; " +
                    $"this build understands {FormatVersion}.");
            }

            int entityCount = reader.ReadInt32();
            if (entityCount < 0)
                throw new InvalidOperationException("MemoryPack snapshot has a negative entity count.");

            var pendingFathers = new List<(int ChildId, int FatherId)>();

            for (int e = 0; e < entityCount; e++)
            {
                int id = reader.ReadInt32();
                int version = reader.ReadInt32();
                int fatherId = reader.ReadInt32();

                _denseRestoreTypes.Clear();
                _denseRestorePayloads.Clear();
                _denseRestoreIds.Clear();

                ComponentMask denseMask = ComponentMask.Empty;
                int denseCount = reader.ReadInt32();
                if (denseCount < 0)
                    throw new InvalidOperationException("MemoryPack snapshot has a negative dense count.");

                for (int i = 0; i < denseCount; i++)
                {
                    var (registered, payload) = ReadMemoryPackComponentPayload(reader);
                    if (registered.IsSparse)
                    {
                        throw new InvalidOperationException(
                            $"Component '{registered.Name}' is sparse/shared but listed under Dense.");
                    }

                    int componentId = registered.GetComponentId!(world);
                    denseMask = denseMask.With(componentId);
                    _denseRestoreTypes.Add(registered);
                    _denseRestoreIds.Add(componentId);
                    _denseRestorePayloads.Add(payload);
                }

                var entity = world.RestoreEntity(id, version, denseMask);
                for (int i = 0; i < _denseRestoreTypes.Count; i++)
                    _denseRestoreTypes[i].ApplyMemoryPack!(world, entity, _denseRestorePayloads[i]);

                int sparseCount = reader.ReadInt32();
                if (sparseCount < 0)
                    throw new InvalidOperationException("MemoryPack snapshot has a negative sparse count.");

                for (int i = 0; i < sparseCount; i++)
                {
                    var (registered, payload) = ReadMemoryPackComponentPayload(reader);
                    if (!registered.IsSparse)
                    {
                        throw new InvalidOperationException(
                            $"Component '{registered.Name}' is dense but listed under Sparse.");
                    }

                    registered.ApplyMemoryPack!(world, entity, payload);
                }

                if (fatherId >= 0)
                    pendingFathers.Add((id, fatherId));
            }

            foreach (var (childId, fatherId) in pendingFathers)
            {
                if (!world.TryGetAliveEntity(childId, out var child) ||
                    !world.TryGetAliveEntity(fatherId, out var father))
                {
                    throw new InvalidOperationException(
                        $"Cannot restore father link child={childId} father={fatherId}: missing entity.");
                }

                world.SetFather(child, father);
            }

            int singletonCount = reader.ReadInt32();
            if (singletonCount < 0)
                throw new InvalidOperationException("MemoryPack snapshot has a negative singleton count.");

            for (int i = 0; i < singletonCount; i++)
            {
                string type = reader.ReadString();
                int version = reader.ReadInt32();
                int length = reader.ReadInt32();
                if (length < 0)
                    throw new InvalidOperationException("MemoryPack snapshot has a negative singleton payload length.");
                byte[] payload = reader.ReadBytes(length);
                if (payload.Length != length)
                    throw new InvalidOperationException("MemoryPack snapshot ended inside a singleton payload.");

                string typeName = ResolveTypeName(type);
                if (!_singletonByName.TryGetValue(typeName, out var registered))
                {
                    throw new InvalidOperationException(
                        $"Unknown singleton type '{type}' in MemoryPack snapshot. " +
                        "RegisterSingleton<>() it (or RegisterTypeAlias) before Load.");
                }

                EnsureExactVersion(registered, version);
                registered.ApplySingletonMemoryPack!(world, payload);
            }
        }

        private void WriteMemoryPackComponent(BinaryWriter writer, World world, Entity entity, ComponentTypeInfo info)
        {
            if (!_byClrType.TryGetValue(info.ClrType, out var registered))
            {
                throw new InvalidOperationException(
                    $"Component type '{info.ClrType.FullName}' is present in the world but was not " +
                    $"Register<>()'d on this WorldSerializer. Register every component type before Save.");
            }

            writer.Write(registered.Name);
            writer.Write(registered.Version);
            if (registered.IsEmpty || registered.CaptureMemoryPack == null)
            {
                writer.Write(0);
                return;
            }

            byte[] payload = registered.CaptureMemoryPack(world, entity);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        private void WriteMemoryPackSingletons(BinaryWriter writer, World world)
        {
            long countPos = writer.BaseStream.Position;
            writer.Write(0);
            int count = 0;
            foreach (var registered in _singletonByClrType.Values)
            {
                if (!registered.TryWriteSingletonMemoryPack!(world, writer))
                    continue;
                count++;
            }

            long after = writer.BaseStream.Position;
            writer.BaseStream.Position = countPos;
            writer.Write(count);
            writer.BaseStream.Position = after;
        }

        private (RegisteredType Registered, byte[] Payload) ReadMemoryPackComponentPayload(BinaryReader reader)
        {
            string type = reader.ReadString();
            int version = reader.ReadInt32();
            int length = reader.ReadInt32();
            if (length < 0)
                throw new InvalidOperationException("MemoryPack snapshot has a negative component payload length.");

            byte[] payload = length == 0 ? Array.Empty<byte>() : reader.ReadBytes(length);
            if (payload.Length != length)
                throw new InvalidOperationException("MemoryPack snapshot ended inside a component payload.");

            string typeName = ResolveTypeName(type);
            if (!_byName.TryGetValue(typeName, out var registered))
            {
                throw new InvalidOperationException(
                    $"Unknown component type '{type}' in MemoryPack snapshot. " +
                    "Register<>() it (or RegisterTypeAlias) before Load.");
            }

            EnsureExactVersion(registered, version);

            if (!registered.IsEmpty && length == 0)
            {
                throw new InvalidOperationException(
                    $"Component '{type}' requires a MemoryPack payload in the snapshot.");
            }

            return (registered, payload);
        }

        private static void EnsureExactVersion(RegisteredType registered, int snapshotVersion)
        {
            int version = snapshotVersion > 0 ? snapshotVersion : 1;
            if (version != registered.Version)
            {
                throw new InvalidOperationException(
                    $"MemoryPack component '{registered.Name}' snapshot version {version} does not match " +
                    $"registered version {registered.Version}. " +
                    "JSON migrations do not apply to MemoryPack snapshots; re-save or bump registration to match.");
            }
        }

        private static bool HasMagic(byte[] bytes, byte[] magic) =>
            bytes.Length >= 4 &&
            bytes[0] == magic[0] && bytes[1] == magic[1] &&
            bytes[2] == magic[2] && bytes[3] == magic[3];
    }
}
