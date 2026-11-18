// Fixture case: VALID anchors (L1 pass, L2 pass).
// Anchor A points at a declaration line of the self-contained target.
// Anchor B points into a method body but keeps a declaration inside the soft window, so it must
// stay clean on both levels.
namespace Fixtures.Anchors.Cases;

public static class CaseValidAndNear
{
    // A: declaration line -> target_sample.cs:7
    // B: statement line, declaration within the look-back window -> target_sample.cs:11
    public static void Touch()
    {
    }
}
