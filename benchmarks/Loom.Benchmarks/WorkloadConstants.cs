namespace Loom.Benchmarks;

internal static class WorkloadConstants
{
    public const int EntityCount = 100_000;
    public const int GroupCount = 4;
    public const int EntitiesPerGroup = EntityCount / GroupCount;
    public const int ToggleCount = EntityCount / 10;
}
