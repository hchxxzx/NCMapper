// Fixture for scripts/check-comment-process-noise.ps1 - PROBE sample (non-vacuity self-check).
// The marker id below must never exist in real code: it is the planted probe used to prove the
// gate is not permanently green (experience #169 discipline).
// Not compiled (scripts/ is outside every project), it is gate input data.

/// <summary>Planted probe marker - the gate must name this file and exit 1 (TASK-99999999).</summary>
public static class ProbeSample
{
    public static void Run() { }
}
