using System;

namespace Loom.Systems
{
    /// <summary>Per-system timing sample collected when <see cref="SystemGroup.ProfilingEnabled"/> is on.</summary>
    public readonly struct SystemProfileInfo
    {
        public string Stage { get; }
        public string Name { get; }
        public Type Type { get; }
        public bool Enabled { get; }
        public bool IsParallel { get; }
        public bool IsGroup { get; }
        public double LastMilliseconds { get; }
        public double AverageMilliseconds { get; }
        public double MaxMilliseconds { get; }
        public int SampleCount { get; }

        public SystemProfileInfo(
            string stage,
            string name,
            Type type,
            bool enabled,
            bool isParallel,
            bool isGroup,
            double lastMilliseconds,
            double averageMilliseconds,
            double maxMilliseconds,
            int sampleCount)
        {
            Stage = stage ?? "";
            Name = name ?? type?.Name ?? "";
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Enabled = enabled;
            IsParallel = isParallel;
            IsGroup = isGroup;
            LastMilliseconds = lastMilliseconds;
            AverageMilliseconds = averageMilliseconds;
            MaxMilliseconds = maxMilliseconds;
            SampleCount = sampleCount;
        }

        public override string ToString() =>
            $"{Stage}/{Name}: last={LastMilliseconds:F3}ms avg={AverageMilliseconds:F3}ms max={MaxMilliseconds:F3}ms n={SampleCount}";
    }
}
