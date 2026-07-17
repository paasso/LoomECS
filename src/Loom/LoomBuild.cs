namespace Loom
{
    /// <summary>
    /// Compile-time capability flags. The library is always verifiably safe (no <c>unsafe</c>).
    /// Optional mask SIMD: <c>dotnet build -p:LoomSimd=true</c>.
    /// </summary>
    public static class LoomBuild
    {
        public static bool SimdEnabled =>
#if LOOM_SIMD
            true;
#else
            false;
#endif
    }
}
