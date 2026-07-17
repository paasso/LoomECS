using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Loom.Internal;
using MemoryPack;

namespace Loom
{
    /// <summary>
    /// JSON snapshot of a <see cref="World"/>'s entities, dense/sparse components, father links,
    /// and optionally registered singletons.
    /// Component / singleton types must be registered before save/load so the snapshot can use
    /// stable CLR type names instead of per-world runtime ids.
    /// </summary>
    /// <remarks>
    /// Scope of format version 1: entity storage + relations + opt-in singletons, as JSON
    /// (<see cref="SaveToJson"/>) or MemoryPack payloads (<see cref="SaveToMemoryPack"/>). Systems, event
    /// subscriptions, and command buffers are not serialized.
    /// <para>
    /// Older snapshots can be upgraded via <see cref="RegisterFormatMigration"/> (document shape)
    /// and <see cref="RegisterMigration{T}"/> (per-type data version for components or singletons).
    /// </para>
    /// </remarks>
    public sealed partial class WorldSerializer
    {
        public const int FormatVersion = 1;

        /// <summary>ASCII magic at the start of <see cref="SaveToMemoryPack"/> payloads: "LCMP".</summary>
        private static readonly byte[] MemoryPackMagic = { (byte)'L', (byte)'C', (byte)'M', (byte)'P' };

        /// <summary>ASCII magic for Brotli-compressed <see cref="SaveToMemoryPack"/> payloads: "LCMB".
        /// Payload after the magic is MemoryPack <c>BrotliCompressor</c> output of a full LCMP snapshot.</summary>
        private static readonly byte[] MemoryPackBrotliMagic = { (byte)'L', (byte)'C', (byte)'M', (byte)'B' };

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly Dictionary<string, RegisteredType> _byName =
            new Dictionary<string, RegisteredType>(StringComparer.Ordinal);
        private readonly Dictionary<Type, RegisteredType> _byClrType = new Dictionary<Type, RegisteredType>();
        private readonly Dictionary<string, RegisteredType> _singletonByName =
            new Dictionary<string, RegisteredType>(StringComparer.Ordinal);
        private readonly Dictionary<Type, RegisteredType> _singletonByClrType = new Dictionary<Type, RegisteredType>();
        private readonly Dictionary<string, string> _typeAliases =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // Key: (stable type name, fromVersion) → migrates data to fromVersion+1.
        private readonly Dictionary<(string Name, int FromVersion), Func<JsonElement, JsonElement>> _componentMigrations =
            new Dictionary<(string, int), Func<JsonElement, JsonElement>>();
        // Key: fromFormatVersion → upgrades document to from+1.
        private readonly Dictionary<int, Func<JsonNode, JsonNode>> _formatMigrations =
            new Dictionary<int, Func<JsonNode, JsonNode>>();

        // Scratch buffers reused across Apply entities (not thread-safe by design — one serializer, one load).
        private readonly List<RegisteredType> _denseRestoreTypes = new List<RegisteredType>();
        private readonly List<object?> _denseRestoreValues = new List<object?>();
        private readonly List<byte[]> _denseRestorePayloads = new List<byte[]>();
        private readonly List<int> _denseRestoreIds = new List<int>();
        /// <summary>Registers a component type for snapshot save/load.
        /// <paramref name="version"/> is written on save and is the target version after load-time
        /// migrations have run.</summary>
        public WorldSerializer Register<T>(int version = 1) where T : struct
        {
            RegisterComponentType<T>(version);
            return this;
        }

        /// <summary>Registers a singleton type for snapshot save/load. Only singletons present in the
        /// world and registered here are written; load restores them via <see cref="World.SetSingleton{T}"/>.</summary>
        public WorldSerializer RegisterSingleton<T>(int version = 1) where T : struct
        {
            RegisterSingletonType<T>(version);
            return this;
        }

        private void RegisterComponentType<T>(int version) where T : struct
        {
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version), "Component version must be >= 1.");

            Type clr = typeof(T);
            string name = StableName(clr);
            if (_byClrType.TryGetValue(clr, out var existing))
            {
                if (existing.Version != version)
                {
                    throw new InvalidOperationException(
                        $"Component '{name}' is already registered at version {existing.Version}; cannot re-register as {version}.");
                }
                return;
            }

            if (_byName.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"Cannot register '{clr.FullName}': stable name '{name}' is already used by another component type.");
            }

            bool isSparse = ComponentTypeTraits<T>.UsesSparseMask;
            bool isEmpty = ComponentTypeTraits<T>.IsEmpty;

            Action<World, Entity, object?> add = isEmpty
                ? (world, entity, _) => world.Add(entity, default(T))
                : (world, entity, value) => world.Add(entity, (T)value!);

            Func<World, Entity, JsonElement>? capture = isEmpty
                ? null
                : (world, entity) => JsonSerializer.SerializeToElement(world.Get<T>(entity), JsonOptions);

            Func<JsonElement, object?> deserialize = isEmpty
                ? _ => null
                : data => JsonSerializer.Deserialize<T>(data, JsonOptions);

            Func<World, Entity, byte[]>? captureMemoryPack = isEmpty
                ? null
                : (world, entity) => MemoryPackSerializer.Serialize(world.Get<T>(entity));

            // MemoryPack apply stays fully typed — never boxes T into object.
            Action<World, Entity, byte[]> applyMemoryPack;
            if (isEmpty)
            {
                applyMemoryPack = isSparse
                    ? (world, entity, _) => world.Add(entity, default(T))
                    : (_, _, _) => { };
            }
            else if (isSparse)
            {
                applyMemoryPack = (world, entity, payload) =>
                    world.Add(entity, MemoryPackSerializer.Deserialize<T>(payload));
            }
            else
            {
                applyMemoryPack = (world, entity, payload) =>
                {
                    world.Get<T>(entity) = MemoryPackSerializer.Deserialize<T>(payload);
                };
            }

            var registered = new RegisteredType(
                name, clr, isSparse, isEmpty, version,
                add, capture, deserialize,
                world => world.EnsureComponentId<T>(),
                setSingleton: null,
                captureMemoryPack,
                applyMemoryPack,
                applySingletonMemoryPack: null,
                tryWriteSingletonMemoryPack: null);
            _byName[name] = registered;
            _byClrType[clr] = registered;
        }

        private void RegisterSingletonType<T>(int version) where T : struct
        {
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version), "Singleton version must be >= 1.");

            Type clr = typeof(T);
            string name = StableName(clr);
            if (_singletonByClrType.TryGetValue(clr, out var existing))
            {
                if (existing.Version != version)
                {
                    throw new InvalidOperationException(
                        $"Singleton '{name}' is already registered at version {existing.Version}; cannot re-register as {version}.");
                }
                return;
            }

            if (_singletonByName.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"Cannot register '{clr.FullName}': stable name '{name}' is already used by another singleton type.");
            }

            string stableName = name;
            int registeredVersion = version;
            var registered = new RegisteredType(
                name, clr, isSparse: false, isEmpty: false, version,
                add: null,
                capture: null,
                deserialize: data => JsonSerializer.Deserialize<T>(data, JsonOptions),
                getComponentId: null,
                setSingleton: (world, value) => world.SetSingleton((T)value),
                captureMemoryPack: null,
                applyMemoryPack: null,
                applySingletonMemoryPack: (world, payload) =>
                    world.SetSingleton(MemoryPackSerializer.Deserialize<T>(payload)),
                tryWriteSingletonMemoryPack: (world, writer) =>
                {
                    if (!world.HasSingleton<T>())
                        return false;
                    writer.Write(stableName);
                    writer.Write(registeredVersion);
                    byte[] payload = MemoryPackSerializer.Serialize(world.GetSingleton<T>());
                    writer.Write(payload.Length);
                    writer.Write(payload);
                    return true;
                });
            _singletonByName[name] = registered;
            _singletonByClrType[clr] = registered;
        }

        /// <summary>Maps an old stable type name in a snapshot to a currently
        /// <see cref="Register{T}"/>'d name (rename without data shape change).</summary>
        public WorldSerializer RegisterTypeAlias(string oldStableName, string currentStableName)
        {
            if (string.IsNullOrEmpty(oldStableName))
                throw new ArgumentException("Old type name is required.", nameof(oldStableName));
            if (string.IsNullOrEmpty(currentStableName))
                throw new ArgumentException("Current type name is required.", nameof(currentStableName));

            _typeAliases[oldStableName] = currentStableName;
            return this;
        }

        /// <summary>Registers a one-step data migration from
        /// <paramref name="fromVersion"/> to <paramref name="fromVersion"/>+1 for <typeparamref name="T"/>
        /// (component or singleton). On load, steps run until the registered target version.</summary>
        public WorldSerializer RegisterMigration<T>(int fromVersion, Func<JsonElement, JsonElement> migrate)
            where T : struct
        {
            if (fromVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(fromVersion));
            if (migrate == null)
                throw new ArgumentNullException(nameof(migrate));
            if (!_byClrType.TryGetValue(typeof(T), out var registered) &&
                !_singletonByClrType.TryGetValue(typeof(T), out registered))
            {
                throw new InvalidOperationException(
                    $"Register<{typeof(T).Name}>() or RegisterSingleton<{typeof(T).Name}>() " +
                    $"before RegisterMigration<{typeof(T).Name}>().");
            }

            var key = (registered.Name, fromVersion);
            if (_componentMigrations.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"A migration for '{registered.Name}' from version {fromVersion} is already registered.");
            }

            _componentMigrations[key] = migrate;
            return this;
        }

        /// <summary>Convenience overload: migrate old JSON into a <typeparamref name="T"/> value at
        /// <paramref name="fromVersion"/>+1 (serialized back to JSON for any further steps).</summary>
        public WorldSerializer RegisterMigration<T>(int fromVersion, Func<JsonElement, T> migrate)
            where T : struct
        {
            if (migrate == null)
                throw new ArgumentNullException(nameof(migrate));

            return RegisterMigration<T>(fromVersion, data =>
            {
                var value = migrate(data);
                return JsonSerializer.SerializeToElement(value, JsonOptions);
            });
        }

        /// <summary>Registers a one-step document migration from <paramref name="fromVersion"/> to
        /// <paramref name="fromVersion"/>+1. Applied before the snapshot is deserialized into entities.</summary>
        public WorldSerializer RegisterFormatMigration(int fromVersion, Func<JsonNode, JsonNode> migrate)
        {
            if (fromVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(fromVersion));
            if (migrate == null)
                throw new ArgumentNullException(nameof(migrate));
            if (_formatMigrations.ContainsKey(fromVersion))
            {
                throw new InvalidOperationException(
                    $"A format migration from version {fromVersion} is already registered.");
            }

            _formatMigrations[fromVersion] = migrate;
            return this;
        }

        public string SaveToJson(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            var snapshot = Capture(world);
            return JsonSerializer.Serialize(snapshot, JsonOptions);
        }

        /// <summary>Loads a snapshot into a pristine (never-used) <paramref name="world"/>,
        /// restoring entity ids and versions. Runs format and component migrations as needed.</summary>
        public void LoadFromJson(World world, string json)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            EnsurePristine(world, nameof(LoadFromJson));

            string upgraded = UpgradeFormat(json);
            var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(upgraded, JsonOptions)
                           ?? throw new InvalidOperationException("Snapshot JSON deserialized to null.");

            if (snapshot.FormatVersion != FormatVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported snapshot formatVersion {snapshot.FormatVersion} after migrations; " +
                    $"this build understands {FormatVersion}. RegisterFormatMigration for missing steps.");
            }

            Apply(world, snapshot);
        }

        private static void EnsurePristine(World world, string apiName)
        {
            if (!world.IsPristine)
            {
                throw new InvalidOperationException(
                    $"{apiName} requires a pristine World (no entities created or destroyed yet). " +
                    "Construct a new World before loading.");
            }
        }

        private string UpgradeFormat(string json)
        {
            var node = JsonNode.Parse(json)
                       ?? throw new InvalidOperationException("Snapshot JSON parsed to null.");

            int version = node["FormatVersion"]?.GetValue<int>()
                          ?? throw new InvalidOperationException("Snapshot is missing FormatVersion.");

            if (version > FormatVersion)
            {
                throw new InvalidOperationException(
                    $"Snapshot formatVersion {version} is newer than this build ({FormatVersion}).");
            }

            while (version < FormatVersion)
            {
                if (!_formatMigrations.TryGetValue(version, out var migrate))
                {
                    throw new InvalidOperationException(
                        $"No format migration registered from version {version} to {version + 1}.");
                }

                node = migrate(node) ?? throw new InvalidOperationException(
                    $"Format migration from {version} returned null.");
                version++;
                node["FormatVersion"] = version;
            }

            return node.ToJsonString();
        }

        private WorldSnapshot Capture(World world)
        {
            var entities = new List<EntitySnapshot>();
            var registry = world.ComponentTypes;

            for (int id = 1; id < world.NextEntityIdExclusive; id++)
            {
                if (!world.TryGetAliveEntity(id, out var entity))
                    continue;

                List<ComponentSnapshot>? dense = null;
                var archetype = world.GetArchetype(entity);
                if (archetype != null && archetype.ComponentIds.Length > 0)
                {
                    dense = new List<ComponentSnapshot>(archetype.ComponentIds.Length);
                    foreach (int componentId in archetype.ComponentIds)
                    {
                        var info = registry.Get(componentId);
                        dense.Add(CaptureComponent(world, entity, info));
                    }
                }

                List<ComponentSnapshot>? sparse = null;
                var sparseMask = world.GetSparseMaskForSerialization(entity.Id);
                if (!sparseMask.Equals(ComponentMask.Empty))
                {
                    sparse = new List<ComponentSnapshot>();
                    foreach (int sparseId in sparseMask)
                    {
                        var info = registry.GetSparse(sparseId);
                        sparse.Add(CaptureComponent(world, entity, info));
                    }
                }

                var father = world.GetFatherForSerialization(entity);
                entities.Add(new EntitySnapshot
                {
                    Id = entity.Id,
                    Version = entity.Version,
                    Dense = dense,
                    Sparse = sparse,
                    FatherId = father.IsNull ? null : father.Id,
                });
            }

            var singletons = CaptureSingletons(world);

            return new WorldSnapshot
            {
                FormatVersion = FormatVersion,
                Entities = entities,
                Singletons = singletons.Count > 0 ? singletons : null,
            };
        }

        private List<ComponentSnapshot> CaptureSingletons(World world)
        {
            var singletons = new List<ComponentSnapshot>();
            world.ForEachSingleton((clrType, value) =>
            {
                if (!_singletonByClrType.TryGetValue(clrType, out var registered))
                {
                    throw new InvalidOperationException(
                        $"Singleton type '{clrType.FullName}' is present in the world but was not " +
                        $"RegisterSingleton<>()'d on this WorldSerializer. Register every singleton type before Save.");
                }

                singletons.Add(new ComponentSnapshot
                {
                    Type = registered.Name,
                    Version = registered.Version,
                    Data = JsonSerializer.SerializeToElement(value, clrType, JsonOptions),
                });
            });
            return singletons;
        }

        private ComponentSnapshot CaptureComponent(World world, Entity entity, ComponentTypeInfo info)
        {
            if (!_byClrType.TryGetValue(info.ClrType, out var registered))
            {
                throw new InvalidOperationException(
                    $"Component type '{info.ClrType.FullName}' is present in the world but was not " +
                    $"Register<>()'d on this WorldSerializer. Register every component type before Save.");
            }

            JsonElement? data = null;
            if (registered.Capture != null)
                data = registered.Capture(world, entity);

            return new ComponentSnapshot
            {
                Type = registered.Name,
                Version = registered.Version,
                Data = data,
            };
        }

        private void Apply(World world, WorldSnapshot snapshot)
        {
            var entities = snapshot.Entities ?? new List<EntitySnapshot>();
            var pendingFathers = new List<(int ChildId, int FatherId)>();

            foreach (var entry in entities)
            {
                _denseRestoreTypes.Clear();
                _denseRestoreValues.Clear();
                _denseRestoreIds.Clear();

                ComponentMask denseMask = ComponentMask.Empty;
                if (entry.Dense != null)
                {
                    foreach (var component in entry.Dense)
                    {
                        var registered = ResolveComponent(component);
                        if (registered.IsSparse)
                        {
                            throw new InvalidOperationException(
                                $"Component '{component.Type}' is sparse/shared but listed under Dense.");
                        }

                        int componentId = registered.GetComponentId!(world);
                        denseMask = denseMask.With(componentId);
                        _denseRestoreTypes.Add(registered);
                        _denseRestoreIds.Add(componentId);
                        _denseRestoreValues.Add(DeserializeComponentValue(registered, component));
                    }
                }

                var entity = world.RestoreEntity(entry.Id, entry.Version, denseMask);

                for (int i = 0; i < _denseRestoreTypes.Count; i++)
                {
                    var registered = _denseRestoreTypes[i];
                    if (registered.IsEmpty)
                        continue;
                    world.SetDenseBoxed(entity, _denseRestoreIds[i], _denseRestoreValues[i]!);
                }

                if (entry.Sparse != null)
                {
                    foreach (var component in entry.Sparse)
                    {
                        var registered = ResolveComponent(component);
                        if (!registered.IsSparse)
                        {
                            throw new InvalidOperationException(
                                $"Component '{component.Type}' is dense but listed under Sparse.");
                        }

                        object? value = DeserializeComponentValue(registered, component);
                        registered.Add!(world, entity, value);
                    }
                }

                if (entry.FatherId is int fatherId)
                    pendingFathers.Add((entry.Id, fatherId));
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

            if (snapshot.Singletons != null)
            {
                foreach (var singleton in snapshot.Singletons)
                    ApplySingleton(world, singleton);
            }
        }

        private RegisteredType ResolveComponent(ComponentSnapshot component)
        {
            if (component.Type == null)
                throw new InvalidOperationException("Component snapshot is missing Type.");

            string typeName = ResolveTypeName(component.Type);
            if (!_byName.TryGetValue(typeName, out var registered))
            {
                throw new InvalidOperationException(
                    $"Unknown component type '{component.Type}' in snapshot. " +
                    "Register<>() it (or RegisterTypeAlias) on the WorldSerializer before Load.");
            }

            return registered;
        }

        private object? DeserializeComponentValue(RegisteredType registered, ComponentSnapshot component)
        {
            if (registered.IsEmpty)
            {
                if (component.Version > 0 && component.Version != registered.Version)
                {
                    throw new InvalidOperationException(
                        $"Empty component '{registered.Name}' snapshot version {component.Version} " +
                        $"does not match registered version {registered.Version}.");
                }

                return null;
            }

            if (component.Data == null || component.Data.Value.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidOperationException(
                    $"Component '{component.Type}' requires data in the snapshot.");
            }

            JsonElement data = MigrateComponentData(registered, component.Version, component.Data.Value);
            return registered.Deserialize!(data)
                   ?? throw new InvalidOperationException($"Failed to deserialize '{component.Type}'.");
        }

        private void ApplySingleton(World world, ComponentSnapshot singleton)
        {
            if (singleton.Type == null)
                throw new InvalidOperationException("Singleton snapshot is missing Type.");

            string typeName = ResolveTypeName(singleton.Type);
            if (!_singletonByName.TryGetValue(typeName, out var registered))
            {
                throw new InvalidOperationException(
                    $"Unknown singleton type '{singleton.Type}' in snapshot. " +
                    "RegisterSingleton<>() it (or RegisterTypeAlias) on the WorldSerializer before Load.");
            }

            if (singleton.Data == null || singleton.Data.Value.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidOperationException(
                    $"Singleton '{singleton.Type}' requires data in the snapshot.");
            }

            JsonElement data = MigrateComponentData(registered, singleton.Version, singleton.Data.Value);
            object value = registered.Deserialize!(data)
                           ?? throw new InvalidOperationException($"Failed to deserialize singleton '{singleton.Type}'.");
            registered.SetSingleton!(world, value);
        }

        private JsonElement MigrateComponentData(RegisteredType registered, int snapshotVersion, JsonElement data)
        {
            // Missing Version in old snapshots means version 1.
            int version = snapshotVersion > 0 ? snapshotVersion : 1;

            if (version > registered.Version)
            {
                throw new InvalidOperationException(
                    $"Component '{registered.Name}' snapshot version {version} is newer than " +
                    $"registered version {registered.Version}.");
            }

            while (version < registered.Version)
            {
                if (!_componentMigrations.TryGetValue((registered.Name, version), out var migrate))
                {
                    throw new InvalidOperationException(
                        $"No migration registered for '{registered.Name}' from version {version} " +
                        $"to {version + 1} (target is {registered.Version}).");
                }

                data = migrate(data);
                version++;
            }

            return data;
        }

        private string ResolveTypeName(string name) =>
            _typeAliases.TryGetValue(name, out var aliased) ? aliased : name;

        private static string StableName(Type type) =>
            type.FullName ?? throw new InvalidOperationException($"Type '{type.Name}' has no FullName.");

        private sealed class RegisteredType
        {
            public readonly string Name;
            public readonly Type ClrType;
            public readonly bool IsSparse;
            public readonly bool IsEmpty;
            public readonly int Version;
            public readonly Action<World, Entity, object?>? Add;
            public readonly Func<World, Entity, JsonElement>? Capture;
            public readonly Func<JsonElement, object?>? Deserialize;
            public readonly Func<World, int>? GetComponentId;
            public readonly Action<World, object>? SetSingleton;
            public readonly Func<World, Entity, byte[]>? CaptureMemoryPack;
            public readonly Action<World, Entity, byte[]>? ApplyMemoryPack;
            public readonly Action<World, byte[]>? ApplySingletonMemoryPack;
            public readonly Func<World, BinaryWriter, bool>? TryWriteSingletonMemoryPack;

            public RegisteredType(
                string name,
                Type clrType,
                bool isSparse,
                bool isEmpty,
                int version,
                Action<World, Entity, object?>? add,
                Func<World, Entity, JsonElement>? capture,
                Func<JsonElement, object?>? deserialize,
                Func<World, int>? getComponentId,
                Action<World, object>? setSingleton,
                Func<World, Entity, byte[]>? captureMemoryPack,
                Action<World, Entity, byte[]>? applyMemoryPack,
                Action<World, byte[]>? applySingletonMemoryPack,
                Func<World, BinaryWriter, bool>? tryWriteSingletonMemoryPack)
            {
                Name = name;
                ClrType = clrType;
                IsSparse = isSparse;
                IsEmpty = isEmpty;
                Version = version;
                Add = add;
                Capture = capture;
                Deserialize = deserialize;
                GetComponentId = getComponentId;
                SetSingleton = setSingleton;
                CaptureMemoryPack = captureMemoryPack;
                ApplyMemoryPack = applyMemoryPack;
                ApplySingletonMemoryPack = applySingletonMemoryPack;
                TryWriteSingletonMemoryPack = tryWriteSingletonMemoryPack;
            }
        }

        private sealed class WorldSnapshot
        {
            public int FormatVersion { get; set; }
            public List<EntitySnapshot>? Entities { get; set; }
            public List<ComponentSnapshot>? Singletons { get; set; }
        }

        private sealed class EntitySnapshot
        {
            public int Id { get; set; }
            public int Version { get; set; }
            public List<ComponentSnapshot>? Dense { get; set; }
            public List<ComponentSnapshot>? Sparse { get; set; }
            public int? FatherId { get; set; }
        }

        private sealed class ComponentSnapshot
        {
            public string? Type { get; set; }
            public int Version { get; set; }
            public JsonElement? Data { get; set; }
        }
    }
}
