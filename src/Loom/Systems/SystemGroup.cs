using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace Loom.Systems
{
    /// <summary>
    /// Ordered list of <see cref="ISystem"/> instances. <see cref="Run"/> invokes every enabled
    /// system against the given <see cref="Runtime"/>. Dependency attributes
    /// (<see cref="UpdateAfterAttribute"/>, <see cref="UpdateBeforeAttribute"/>,
    /// <see cref="OrderFirstAttribute"/>, <see cref="OrderLastAttribute"/>) reorder siblings
    /// topologically. Independent <see cref="IParallelSystem"/> peers in one wave may run
    /// concurrently. A group also implements <see cref="ISystem"/> so groups nest inside groups
    /// or stages.
    /// </summary>
    public sealed class SystemGroup : ISystem
    {
        private const double EmaAlpha = 0.15;

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<Runtime, HashSet<ISystem>> _createdByRuntime =
            new Dictionary<Runtime, HashSet<ISystem>>();
        private bool _orderDirty = true;
        private List<int>? _waveStarts; // parallel array lengths encoded as start indices into sorted entries
        private ProfileState[] _profiles = Array.Empty<ProfileState>();
        private readonly object _profileLock = new object();

        public SystemGroup() : this(null)
        {
        }

        public SystemGroup(string? name) => Name = name;

        /// <summary>Optional label for nested groups / debugging.</summary>
        public string? Name { get; }

        public int Count => _entries.Count;

        /// <summary>
        /// When true, <see cref="Run"/> records per-system wall times for
        /// <see cref="CollectProfileInfos"/>. Cheap enough for editor tools; leave off in shipping builds.
        /// </summary>
        public bool ProfilingEnabled { get; set; }

        /// <summary>Appends <paramref name="system"/> at the end of the registration list. The same
        /// instance may only be registered once.</summary>
        public SystemGroup Add(ISystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));
            if (ReferenceEquals(system, this))
                throw new InvalidOperationException("A SystemGroup cannot contain itself.");
            if (IndexOf(system) >= 0)
                throw new InvalidOperationException("System is already registered in this group.");

            _entries.Add(new Entry(system, enabled: true));
            _orderDirty = true;
            return this;
        }

        /// <summary>Removes <paramref name="system"/> if it was registered. Invokes
        /// <see cref="ISystemLifecycle.OnDestroy"/> for every world that had created it.</summary>
        public bool Remove(ISystem system)
        {
            int index = IndexOf(system);
            if (index < 0)
                return false;

            DestroyLifecycle(system);
            _entries.RemoveAt(index);
            _orderDirty = true;
            return true;
        }

        public bool Contains(ISystem system) => IndexOf(system) >= 0;

        /// <summary>Returns the first registered system of type <typeparamref name="T"/>.</summary>
        public T Get<T>() where T : class, ISystem
        {
            if (!TryGet<T>(out var system))
                throw new InvalidOperationException($"No system of type {typeof(T).Name} is registered.");
            return system;
        }

        public bool TryGet<T>(out T system) where T : class, ISystem
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].System is T typed)
                {
                    system = typed;
                    return true;
                }
            }

            system = null!;
            return false;
        }

        public bool IsEnabled(ISystem system)
        {
            int index = RequireIndex(system);
            return _entries[index].Enabled;
        }

        /// <summary>Enables or disables <paramref name="system"/> without removing it from the
        /// group. Disabled systems are skipped by <see cref="Run"/>.</summary>
        public void SetEnabled(ISystem system, bool enabled)
        {
            int index = RequireIndex(system);
            var entry = _entries[index];
            entry.Enabled = enabled;
            _entries[index] = entry;
        }

        /// <summary>Clears accumulated last/avg/max timings without removing systems.</summary>
        public void ResetProfiling()
        {
            lock (_profileLock)
            {
                for (int i = 0; i < _profiles.Length; i++)
                    _profiles[i] = default;
            }
        }

        /// <summary>
        /// Appends one <see cref="SystemProfileInfo"/> per registered system (run order).
        /// Nested <see cref="SystemGroup"/> children are included when <paramref name="includeNested"/>
        /// is true.
        /// </summary>
        public void CollectProfileInfos(string stage, List<SystemProfileInfo> into, bool includeNested = true)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            EnsureProfileCapacity();
            lock (_profileLock)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    var system = entry.System;
                    var type = system.GetType();
                    var profile = i < _profiles.Length ? _profiles[i] : default;
                    into.Add(new SystemProfileInfo(
                        stage,
                        FormatSystemName(system),
                        type,
                        entry.Enabled,
                        system is IParallelSystem,
                        system is SystemGroup,
                        profile.LastMs,
                        profile.EmaMs,
                        profile.MaxMs,
                        profile.Samples));

                    if (includeNested && system is SystemGroup nested)
                        nested.CollectProfileInfos($"{stage}/{FormatSystemName(nested)}", into, includeNested: true);
                }
            }
        }

        /// <summary>Recomputes run order and parallel waves from ordering attributes.
        /// Normally called automatically by <see cref="Run"/>.</summary>
        public void SortByDependencies()
        {
            if (_entries.Count <= 1)
            {
                _orderDirty = false;
                _waveStarts = _entries.Count == 0 ? new List<int>() : new List<int> { 0 };
                return;
            }

            int n = _entries.Count;
            var indegree = new int[n];
            var successors = new List<int>[n];
            var orderFirst = new bool[n];
            var orderLast = new bool[n];
            var edges = new HashSet<(int From, int To)>();
            for (int i = 0; i < n; i++)
                successors[i] = new List<int>();

            void Link(int from, int to)
            {
                if (from == to || !edges.Add((from, to)))
                    return;
                successors[from].Add(to);
                indegree[to]++;
            }

            var typeToIndices = new Dictionary<Type, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var type = _entries[i].System.GetType();
                if (!typeToIndices.TryGetValue(type, out var list))
                {
                    list = new List<int>();
                    typeToIndices[type] = list;
                }

                list.Add(i);
                orderFirst[i] = type.GetCustomAttribute<OrderFirstAttribute>(inherit: true) != null;
                orderLast[i] = type.GetCustomAttribute<OrderLastAttribute>(inherit: true) != null;
                if (orderFirst[i] && orderLast[i])
                {
                    throw new InvalidOperationException(
                        $"{type.Name} cannot be both [OrderFirst] and [OrderLast].");
                }
            }

            for (int i = 0; i < n; i++)
            {
                var type = _entries[i].System.GetType();
                foreach (var attr in type.GetCustomAttributes<UpdateAfterAttribute>(inherit: true))
                    AddEdgesFromType(typeToIndices, attr.SystemType, toIndex: i, Link);
                foreach (var attr in type.GetCustomAttributes<UpdateBeforeAttribute>(inherit: true))
                    AddEdgesToType(typeToIndices, attr.SystemType, fromIndex: i, Link);
            }

            // OrderFirst → everyone else (except other OrderFirst); everyone else → OrderLast.
            for (int i = 0; i < n; i++)
            {
                if (!orderFirst[i])
                    continue;
                for (int j = 0; j < n; j++)
                {
                    if (!orderFirst[j])
                        Link(i, j);
                }
            }

            for (int j = 0; j < n; j++)
            {
                if (!orderLast[j])
                    continue;
                for (int i = 0; i < n; i++)
                {
                    if (!orderLast[i])
                        Link(i, j);
                }
            }

            var ready = new SortedSet<int>();
            for (int i = 0; i < n; i++)
            {
                if (indegree[i] == 0)
                    ready.Add(i);
            }

            var sorted = new List<Entry>(n);
            var waveStarts = new List<int>();
            EnsureProfileCapacity();
            var sortedProfiles = new ProfileState[n];

            while (ready.Count > 0)
            {
                // One wave = current ready set (mutually independent after prior waves drained).
                var wave = new List<int>(ready);
                waveStarts.Add(sorted.Count);
                ready.Clear();

                wave.Sort(); // stable: original registration index
                for (int w = 0; w < wave.Count; w++)
                {
                    int i = wave[w];
                    sortedProfiles[sorted.Count] = i < _profiles.Length ? _profiles[i] : default;
                    sorted.Add(_entries[i]);
                    var next = successors[i];
                    for (int s = 0; s < next.Count; s++)
                    {
                        int j = next[s];
                        indegree[j]--;
                        if (indegree[j] == 0)
                            ready.Add(j);
                    }
                }
            }

            if (sorted.Count != n)
            {
                throw new InvalidOperationException(
                    "System dependency cycle detected among UpdateAfter/UpdateBefore/OrderFirst/OrderLast.");
            }

            _entries.Clear();
            _entries.AddRange(sorted);
            _profiles = sortedProfiles;
            _waveStarts = waveStarts;
            _orderDirty = false;
        }

        /// <summary>Runs every enabled system in dependency order. After each sequential system
        /// (or parallel wave), command buffers are played back — see <see cref="ISystem"/>.</summary>
        public void Run(Runtime runtime)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));

            if (_orderDirty)
                SortByDependencies();

            EnsureCreated(runtime);
            if (ProfilingEnabled)
                EnsureProfileCapacity();

            var commands = runtime.SystemCommandBuffer;
            var waveStarts = _waveStarts!;
            int entryCount = _entries.Count;

            for (int w = 0; w < waveStarts.Count; w++)
            {
                int start = waveStarts[w];
                int end = w + 1 < waveStarts.Count ? waveStarts[w + 1] : entryCount;
                RunWave(runtime, commands, start, end);
            }
        }

        /// <summary>Nested-group entry point when this group is registered as an <see cref="ISystem"/>.</summary>
        public void Update(Runtime runtime, CommandBuffer commands) => Run(runtime);

        public override string ToString() => Name != null ? $"SystemGroup({Name})" : "SystemGroup";

        private void RunWave(Runtime runtime, CommandBuffer sharedCommands, int start, int end)
        {
            // Collect enabled systems in [start, end).
            var enabled = new List<int>(end - start);
            for (int i = start; i < end; i++)
            {
                if (_entries[i].Enabled)
                    enabled.Add(i);
            }

            if (enabled.Count == 0)
                return;

            bool profile = ProfilingEnabled;

            if (enabled.Count > 1 && AllParallel(enabled))
            {
                var buffers = new CommandBuffer[enabled.Count];
                Parallel.For(0, enabled.Count, i =>
                {
                    var buffer = runtime.World.CreateCommandBuffer();
                    buffers[i] = buffer;
                    int entryIndex = enabled[i];
                    if (profile)
                    {
                        var system = _entries[entryIndex].System;
                        if (system is SystemGroup nested)
                            nested.ProfilingEnabled = true;
                        var sw = Stopwatch.StartNew();
                        system.Update(runtime, buffer);
                        sw.Stop();
                        RecordSample(entryIndex, sw.Elapsed.TotalMilliseconds);
                    }
                    else
                    {
                        _entries[entryIndex].System.Update(runtime, buffer);
                    }
                });

                for (int i = 0; i < buffers.Length; i++)
                    buffers[i].Playback();
                return;
            }

            for (int e = 0; e < enabled.Count; e++)
            {
                int entryIndex = enabled[e];
                if (profile)
                {
                    var system = _entries[entryIndex].System;
                    if (system is SystemGroup nested)
                        nested.ProfilingEnabled = true;
                    var sw = Stopwatch.StartNew();
                    system.Update(runtime, sharedCommands);
                    sw.Stop();
                    RecordSample(entryIndex, sw.Elapsed.TotalMilliseconds);
                }
                else
                {
                    _entries[entryIndex].System.Update(runtime, sharedCommands);
                }

                sharedCommands.Playback();
            }
        }

        private void RecordSample(int index, double ms)
        {
            lock (_profileLock)
            {
                if ((uint)index >= (uint)_profiles.Length)
                    return;

                ref var p = ref _profiles[index];
                p.LastMs = ms;
                p.EmaMs = p.Samples == 0 ? ms : p.EmaMs + (ms - p.EmaMs) * EmaAlpha;
                if (ms > p.MaxMs)
                    p.MaxMs = ms;
                p.Samples++;
            }
        }

        private void EnsureProfileCapacity()
        {
            if (_profiles.Length == _entries.Count)
                return;
            var next = new ProfileState[_entries.Count];
            int copy = Math.Min(_profiles.Length, next.Length);
            for (int i = 0; i < copy; i++)
                next[i] = _profiles[i];
            _profiles = next;
        }

        private static string FormatSystemName(ISystem system)
        {
            if (system is SystemGroup group && !string.IsNullOrEmpty(group.Name))
                return group.Name!;
            return system.GetType().Name;
        }

        private bool AllParallel(List<int> indices)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                if (_entries[indices[i]].System is not IParallelSystem)
                    return false;
            }

            return true;
        }

        private void EnsureCreated(Runtime runtime)
        {
            if (!_createdByRuntime.TryGetValue(runtime, out var created))
            {
                created = new HashSet<ISystem>();
                _createdByRuntime[runtime] = created;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                var system = _entries[i].System;
                if (!created.Add(system))
                    continue;
                if (system is ISystemLifecycle life)
                    life.OnCreate(runtime);
            }
        }

        private void DestroyLifecycle(ISystem system)
        {
            if (system is not ISystemLifecycle life)
            {
                foreach (var created in _createdByRuntime.Values)
                    created.Remove(system);
                return;
            }

            var runtimes = new List<Runtime>();
            foreach (var kv in _createdByRuntime)
            {
                if (kv.Value.Remove(system))
                    runtimes.Add(kv.Key);
            }

            for (int i = 0; i < runtimes.Count; i++)
                life.OnDestroy(runtimes[i]);
        }

        private static void AddEdgesFromType(
            Dictionary<Type, List<int>> typeToIndices,
            Type fromType,
            int toIndex,
            Action<int, int> link)
        {
            if (!typeToIndices.TryGetValue(fromType, out var fromIndices))
                return;

            for (int f = 0; f < fromIndices.Count; f++)
                link(fromIndices[f], toIndex);
        }

        private static void AddEdgesToType(
            Dictionary<Type, List<int>> typeToIndices,
            Type beforeType,
            int fromIndex,
            Action<int, int> link)
        {
            if (!typeToIndices.TryGetValue(beforeType, out var toIndices))
                return;

            for (int t = 0; t < toIndices.Count; t++)
                link(fromIndex, toIndices[t]);
        }

        private int IndexOf(ISystem system)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (ReferenceEquals(_entries[i].System, system))
                    return i;
            }
            return -1;
        }

        private int RequireIndex(ISystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));
            int index = IndexOf(system);
            if (index < 0)
                throw new InvalidOperationException("System is not registered in this group.");
            return index;
        }

        private struct Entry
        {
            public readonly ISystem System;
            public bool Enabled;

            public Entry(ISystem system, bool enabled)
            {
                System = system;
                Enabled = enabled;
            }
        }

        private struct ProfileState
        {
            public double LastMs;
            public double EmaMs;
            public double MaxMs;
            public int Samples;
        }
    }
}
