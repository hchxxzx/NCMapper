using System.Text;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Structured difference record (design source: scheme 20260903-003) —
/// replaces the string difference list in <see cref="EquivalenceResult.Differences"/>.
/// <para>Three elements per，FieldPath (required) + Expected/Actual (stringified side values,
/// nullable per three-way fallback: split / capture / empty) + RuleRef (normalization rule
/// reference, e.g. " Table 2 SequenceEqual(normalized)", nullable — source: comparer segment
/// comments + SnapshotFieldRegistry Reason column Table references; do NOT invent table numbers).</para>
/// <para><see cref="Description"/> carries the legacy message phrase ("differs" / "one side null" /
/// "TypeRef differs" / "list count differs" / ...) so <see cref="ToString"/> keeps the exact legacy
/// string shape — string.Join consumers keep zero semantic change, failure-message readability
/// preserved (G2). Rendering appends " [{RuleRef}]" only when a rule reference is present.</para>
/// </summary>
internal sealed class EquivalenceDifference
{
    public string FieldPath { get; }
    public string? Expected { get; }
    public string? Actual { get; }
    public string? RuleRef { get; }

    /// <summary>Legacy phrase for rendering; not one of the three elements (render-only).</summary>
    internal string Description { get; }

    public EquivalenceDifference(string fieldPath, string? expected = null, string? actual = null, string? ruleRef = null, string? description = null)
    {
        FieldPath = fieldPath;
        Expected = expected;
        Actual = actual;
        RuleRef = ruleRef;
        Description = description ?? "differs";
    }

    /// <summary>
    /// Legacy-shape rendering: "{path}: {desc} (expected={E}, actual={A})" — value segment omitted
    /// when both sides are empty (one-side-null style points carry only one side or none);
    /// " [{rule}]" appended when RuleRef is present. Empty-side omission keeps legacy points that
    /// had no value interpolation byte-identical.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder(FieldPath);
        sb.Append(": ").Append(Description);
        bool hasExpected = !string.IsNullOrEmpty(Expected);
        bool hasActual = !string.IsNullOrEmpty(Actual);
        if (hasExpected || hasActual)
        {
            sb.Append(" (expected=").Append(Expected ?? "null");
            sb.Append(", actual=").Append(Actual ?? "null").Append(')');
        }
        if (!string.IsNullOrEmpty(RuleRef))
        {
            sb.Append(" [").Append(RuleRef).Append(']');
        }
        return sb.ToString();
    }
}
