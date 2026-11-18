// Fixture for scripts/check-comment-process-noise.ps1 - CLEAN sample.
// This file must produce zero hard hits: it carries contract prose only.
// Not compiled (scripts/ is outside every project), it is gate input data.

namespace NCMapperGateFixtures
{
    /// <summary>
    /// Resets every observability counter (TotalMapCalls / CacheHits / CompilationCount)
    /// and cascades the reset into the cache-level hit/miss registry.
    /// </summary>
    /// <remarks>
    /// Thread safety: the counters are Interlocked-updated, so concurrent readers may observe
    /// a partially reset state; call this only on a quiesced mapper.
    /// </remarks>
    public static class CleanSample
    {
        // Local trap kept on purpose: the registry is a generic static field and cannot be
        // cleared through the instance-level ClearCache path.
        public static void Run()
        {
            // trailing comment with contract semantics, no process noise
        }
    }
}
