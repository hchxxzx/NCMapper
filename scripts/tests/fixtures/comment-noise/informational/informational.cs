// Fixture for scripts/check-comment-process-noise.ps1 - INFORMATIONAL sample.
// Carries only the two informational classes (date / status words): the gate must list them
// and still exit 0, proving the informational tier never fails the build.
// Not compiled (scripts/ is outside every project), it is gate input data.

/// <summary>
/// Historical note: this path was consolidated during the 2026-01-02 review round (收口),
/// the earlier variant is described in the history record (历史记录) of the design notes.
/// </summary>
public static class InformationalSample
{
    // status word only: 本任务 / 整改 / 批阅 / 此前 / 曾通过
    public static void Run() { }
}
