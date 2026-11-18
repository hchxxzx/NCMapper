// Fixture case: HISTORICAL anchor. The reference carries a `原...` qualifier, which marks a location
// that has since moved. It must be split into the "historical references" section and must never be
// judged as a mismatch, even when the line number is out of range.
namespace Fixtures.Anchors.Cases;

public static class CaseHistory
{
    // 原硬编码于 target_sample.cs:9999
    public static void Touch()
    {
    }
}
