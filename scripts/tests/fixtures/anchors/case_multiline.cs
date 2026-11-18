// Fixture case: MULTI LINE-NUMBER form. One reference carries three line numbers separated by '/',
// each segment is judged on its own (7 and 11 clean, 24 soft).
namespace Fixtures.Anchors.Cases;

public static class CaseMultiLine
{
    // F: multi line numbers -> target_sample.cs:7/11/24
    public static void Touch()
    {
    }
}
