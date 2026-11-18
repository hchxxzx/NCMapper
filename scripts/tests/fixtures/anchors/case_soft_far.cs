// Fixture case: L2 BOUNDARY. The anchor resolves (L1 pass) but points deep into a long method body,
// so no declaration line exists within the look-back window above it. Expected: L2 soft observation
// section, NEVER an L1 hard mismatch.
namespace Fixtures.Anchors.Cases;

public static class CaseSoftFar
{
    // C: statement line, no declaration within 5 lines above -> target_sample.cs:24
    public static void Touch()
    {
    }
}
