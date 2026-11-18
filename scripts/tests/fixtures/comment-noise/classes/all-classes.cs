// Fixture for scripts/check-comment-process-noise.ps1 - NOISE sample (probe).
// Every line below is deliberately dirty; the gate must report one hard hit per class id.
// Not compiled (scripts/ is outside every project), it is gate input data.

/// <summary>Resets counters (TASK-20260101-001 T-P2) - H01.</summary>
/// <para>Requirement id REQ-20260101-01 closes it - H02.</para>
/// <para>Depth guard introduced while T-005 was open - H03.</para>
/// <para>Slot semantics (F-B9): one slot per assembly - H04.</para>
/// <para>Wave marker W3E-1 - H05.</para>
/// <para>Chinese wave marker 波3-E - H06.</para>
/// <para>Review label （评审 S-6） - H07.</para>
/// <para>Review label S-16 alone - H08.</para>
/// <para>Decision label Q2 定案 - H09a; Q3 关闭声明 - H09b; Q4 closure - H09c.</para>
/// <para>Section reference 方案书 §5.3 - H10.</para>
/// <para>English introduction marker (introduced in T-003) - H11.</para>
/// <para>Internal document reference 任务书 §10.5 / ADR-002 §决策 3 - H12.</para>
/// <para>Bare section reference §5.2-6 - H13.</para>
public static class NoiseSample
{
    // whole-line comment carrying a task id TASK-20260101-002
    public static void Run()
    {
        var s = "emitted header TASK-20260101-003";   // trailing comment carries 波1-E
    }
}
