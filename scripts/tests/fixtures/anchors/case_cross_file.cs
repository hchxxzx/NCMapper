// Fixture case: CROSS-FILE anchor (target lives in another file of the same fixture directory)
// plus the whitelist boundary check.
// G: cross-file, declaration line -> cross_target.cs:5
// The next line references a real file with a non-whitelisted extension. It must be ignored by the
// anchor whitelist and therefore must never show up in any of the report sections:
// H: cross_file_target.txt:3
namespace Fixtures.Anchors.Cases;

public static class CaseCrossFile
{
    public static void Touch()
    {
    }
}
