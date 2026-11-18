// Fixture case: HARD mismatches (L1). Both anchors below must be reported on the gate list and must
// make the script exit non-zero unless --report-only is used.
namespace Fixtures.Anchors.Cases;

public static class CaseHard
{
    // D: target file does not exist anywhere -> no_such_file.cs:10
    // E: target exists but the line number exceeds its total line count -> target_sample.cs:9999
    public static void Touch()
    {
    }
}
