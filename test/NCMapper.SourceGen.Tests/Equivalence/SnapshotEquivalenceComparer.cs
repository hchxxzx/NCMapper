using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Equivalence comparison result — carries the structured difference list (field-path level,
/// string list → EquivalenceDifference records; legacy shape kept via ToString).
/// </summary>
internal sealed class EquivalenceResult
{
    public bool AreEquivalent { get; }
    public IReadOnlyList<EquivalenceDifference> Differences { get; }

    public EquivalenceResult(bool areEquivalent, IReadOnlyList<EquivalenceDifference> differences)
    {
        AreEquivalent = areEquivalent;
        Differences = differences;
    }

    public static EquivalenceResult Equivalent => new(true, Array.Empty<EquivalenceDifference>());
}

/// <summary>
/// Dual-channel MappingSnapshot equivalence comparer — top-level reflection enumeration + denylist;
/// nested per-field hand-written projection (scheme caliber tables 1-6).
/// <para>Design basis: scheme 20260903-003 (comparer contract) / (impl steps) / (field caliber tables — sole definition of comparer semantics).</para>
/// <para>Anti-whitelist-rot: top-level new fields auto-included via reflection (default must be equivalent);
/// exemption requires explicit denylist registration (registrable/verifiable).
/// Nested new fields do NOT auto-include (hand-written projection) — covered by table review.</para>
/// </summary>
internal static class SnapshotEquivalenceComparer
{
    // Table 1: top-level denylist (explicit exemption; field name MUST exist in reflection property set — typo => Assert.Fail)
    // 剥离集改读 SnapshotFieldRegistry（注册表为唯一数据源，
    // 符号保留——#1 DenyList_AllFieldsExistInReflectionPropertySet 读本符号自动适配）
    internal static readonly IReadOnlyList<string> TopLevelDenyList = SnapshotFieldRegistry.MappingSnapshot
        .Where(r => r.Role == FieldRole.Stripped)
        .Select(r => r.Field)
        .ToArray();

    // Lazily cached reflection property set (sorted by Name, denylist excluded)
    private static PropertyInfo[]? s_topLevelProperties;
    private static bool s_registrationValidated;

    /// <summary>
    /// Compare two MappingSnapshot instances — top-level reflection enumeration dispatches to nested projection methods.
    /// Returns EquivalenceResult with field-path-level difference list.
    /// </summary>
    public static EquivalenceResult Compare(MappingSnapshot? expected, MappingSnapshot? actual)
    {
        // Null handling
        if (expected is null && actual is null) return EquivalenceResult.Equivalent;
        if (expected is null || actual is null)
            return new EquivalenceResult(false, new[] { new EquivalenceDifference("MappingSnapshot", expected != null ? "non-null" : "null", actual != null ? "non-null" : "null", "Table 1", "one side null") });

        // Registration validation (lazy, once)
        ValidateRegistration();

        var differences = new List<EquivalenceDifference>();

        foreach (var prop in TopLevelProperties)  // sorted by Name, denylist excluded
        {
            var expectedVal = prop.GetValue(expected);
            var actualVal = prop.GetValue(actual);
            CompareTopLevelField(prop.Name, expectedVal, actualVal, differences, prop.Name);
        }

        return new EquivalenceResult(differences.Count == 0, differences);
    }

    // ----------------------------------------------------------------
    // Registration validation (lazy, once, cached)
    // denylist field name must exist in reflection property set => Assert.Fail if not
    // ----------------------------------------------------------------
    private static void ValidateRegistration()
    {
        if (s_registrationValidated) return;

        var propNames = typeof(MappingSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var denied in TopLevelDenyList)
        {
            if (!propNames.Contains(denied))
            {
                Assert.Fail($"[SnapshotEquivalenceComparer] denylist field '{denied}' not found in MappingSnapshot reflection property set — typo or field renamed. Registration validation failed (scheme).");
            }
        }

        s_registrationValidated = true;
    }

    private static PropertyInfo[] TopLevelProperties
    {
        get
        {
            if (s_topLevelProperties == null)
            {
                s_topLevelProperties = typeof(MappingSnapshot)
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => !TopLevelDenyList.Contains(p.Name))
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .ToArray();
            }
            return s_topLevelProperties;
        }
    }

    // ----------------------------------------------------------------
    // Top-level field dispatch (by value type)
    // 顶层 Compared = 反射全集 − denylist（denylist 读 SnapshotFieldRegistry.MappingSnapshot 的 Stripped 行，
    // 单一数据源）；字段数不在此登记——由 SnapshotFieldRegistryTests.MappingSnapshot_Stripped_HasSixEntries_AfterA25Migration
    // 与 ComparerFieldCoverageTests（键集 + 计数守门）钉死。
    // （历史计数注记已删：陈旧数字本身即本批治理对象，订正过程见归档）
    // ----------------------------------------------------------------
    private static void CompareTopLevelField(string fieldName, object? expectedVal, object? actualVal, List<EquivalenceDifference> differences, string path)
    {
        // Null handling
        if (expectedVal == null && actualVal == null) return;
        if (expectedVal == null || actualVal == null)
        {
            differences.Add(new EquivalenceDifference(path, FormatVal(expectedVal), FormatVal(actualVal), "Table 1", "one side null"));
            return;
        }

        // Dispatch by type
        switch (expectedVal)
        {
            case IReadOnlyList<PropertyMappingSnapshot> expList:
                CompareList(fieldName, expList, actualVal as IReadOnlyList<PropertyMappingSnapshot>, differences, path, ComparePropertyMapping);
                break;
            case IReadOnlyList<PropertyRef> expPropRefList:
                CompareList(fieldName, expPropRefList, actualVal as IReadOnlyList<PropertyRef>, differences, path, ComparePropertyRef);
                break;
            case IReadOnlyList<DeepCopyNodeShape> expDeepList:
                CompareList(fieldName, expDeepList, actualVal as IReadOnlyList<DeepCopyNodeShape>, differences, path, CompareDeepCopyNode);
                break;
            case IReadOnlyList<ComposeMappingSnapshot> expComposeList:
                CompareList(fieldName, expComposeList, actualVal as IReadOnlyList<ComposeMappingSnapshot>, differences, path, CompareComposeMapping);
                break;
            // SubclassPairs 逐项比较（A-22 等价对 L1 面——
            // 落 default 分支将走数组引用比较恒不等；SubclassPairSnapshot 逐字段：
            // SourceType/TargetType（TypeRef Equals）+ SourceClassName/TargetClassName（序数字符串））
            case IReadOnlyList<SubclassPairSnapshot> expSubList:
                CompareList(fieldName, expSubList, actualVal as IReadOnlyList<SubclassPairSnapshot>, differences, path, CompareSubclassPair);
                break;
            case IReadOnlyList<DiagnosticSnapshot> expDiagList:
                CompareList(fieldName, expDiagList, actualVal as IReadOnlyList<DiagnosticSnapshot>, differences, path, CompareDiagnostic);
                break;
            case TypeRef expTypeRef:
                if (!Equals(expTypeRef, actualVal))
                    differences.Add(new EquivalenceDifference(path, expTypeRef.FullName, FormatVal(actualVal), "Table 1", "TypeRef differs"));
                break;
            case string expStr:
                if (!string.Equals(expStr, actualVal as string, StringComparison.Ordinal))
                    differences.Add(new EquivalenceDifference(path, "\"" + expStr + "\"", FormatVal(actualVal), "Table 1", "string differs"));
                break;
            default:
                // bool / enum / value types — use default Equals
                if (!Equals(expectedVal, actualVal))
                    differences.Add(new EquivalenceDifference(path, FormatVal(expectedVal), FormatVal(actualVal), "Table 1", "value differs"));
                break;
        }
    }

    // SubclassPairSnapshot 逐字段比较（A-22 L1 面——
    // SourceType/TargetType 经 TypeRef.Equals（值等）；ClassName 序数字符串）
    private static void CompareSubclassPair(SubclassPairSnapshot expected, SubclassPairSnapshot actual, List<EquivalenceDifference> differences, string path)
    {
        if (!Equals(expected.SourceType, actual.SourceType))
            differences.Add(new EquivalenceDifference($"{path}.SourceType", expected.SourceType.FullName, actual.SourceType.FullName, "A-22", "SubclassPair SourceType differs"));
        if (!Equals(expected.TargetType, actual.TargetType))
            differences.Add(new EquivalenceDifference($"{path}.TargetType", expected.TargetType.FullName, actual.TargetType.FullName, "A-22", "SubclassPair TargetType differs"));
        if (!string.Equals(expected.SourceClassName, actual.SourceClassName, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference($"{path}.SourceClassName", expected.SourceClassName, actual.SourceClassName, "A-22", "SubclassPair SourceClassName differs"));
        if (!string.Equals(expected.TargetClassName, actual.TargetClassName, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference($"{path}.TargetClassName", expected.TargetClassName, actual.TargetClassName, "A-22", "SubclassPair TargetClassName differs"));
    }

    // Generic list comparison helper
    private static void CompareList<T>(string fieldName, IReadOnlyList<T>? expected, IReadOnlyList<T>? actual, List<EquivalenceDifference> differences, string path, Action<T, T, List<EquivalenceDifference>, string> itemComparer)
    {
        if (expected == null && actual == null) return;
        if (expected == null || actual == null)
        {
            differences.Add(new EquivalenceDifference(path, (expected?.Count ?? 0).ToString(), (actual?.Count ?? 0).ToString(), null, "list one side null"));
            return;
        }
        if (expected.Count != actual.Count)
        {
            differences.Add(new EquivalenceDifference(path, expected.Count.ToString(), actual.Count.ToString(), null, "list count differs"));
            // Still compare up to min count for detailed diffs
        }
        var min = Math.Min(expected.Count, actual.Count);
        for (int i = 0; i < min; i++)
        {
            itemComparer(expected[i], actual[i], differences, $"{path}[{i}]");
        }
    }

    // ----------------------------------------------------------------
    // Table 2: PropertyMappingSnapshot — init 存储位逐字段投影
    // （字段数不在此登记——由 ComparerFieldCoverageTests 双向臂 +
    //  SnapshotEquivalenceComparerTests.PropertyMappingSnapshot_CoverageProjectionUnionStripped_EqualsFullSet 钉死）
    // Implementation: ComparePropertyMapping
    // ----------------------------------------------------------------

    /// <summary>
    /// Table 2: PropertyMappingSnapshot 12-field projection
    /// (声明位 = init 存储位 + 表达式体派生位〔HasIndex / IsReverseWriteBack，登记为 Stripped、不参与比较——
    ///  角色与"不现于比较位"由 ComparerFieldCoverageTests 反向臂钉死，计数不在本注登记〕)
    /// </summary>
    private static void ComparePropertyMapping(PropertyMappingSnapshot? expected, PropertyMappingSnapshot? actual, List<EquivalenceDifference> differences, string path)
    {
        if (expected == null && actual == null) return;
        if (expected == null || actual == null)
        {
            differences.Add(new EquivalenceDifference(path, null, null, "Table 2", "one side null"));
            return;
        }

        // Target / Source — PropertyRef projection (Table 4)
        ComparePropertyRef(expected.Target, actual.Target, differences, $"{path}.Target");
        ComparePropertyRef(expected.Source, actual.Source, differences, $"{path}.Source");

        // SourcePath — per-segment PropertyRef projection
        CompareList("SourcePath", expected.SourcePath, actual.SourcePath, differences, $"{path}.SourcePath", ComparePropertyRef);

        // SourceIndexes — direct SequenceEqual (Table 2: A-class no-index scenarios both sides empty)
        // Normalize: if both sides HasIndex=false (no actual index segments), treat as equivalent
        if (expected.HasIndex || actual.HasIndex)
        {
            if (!expected.SourceIndexes.SequenceEqual(actual.SourceIndexes))
                differences.Add(new EquivalenceDifference(path + ".SourceIndexes", "Table 2 SequenceEqual(normalized)"));
        }

        // ReverseWritePath — per-segment PropertyRef projection (nullable)
        if (expected.ReverseWritePath == null && actual.ReverseWritePath == null) { /* OK */ }
        else if (expected.ReverseWritePath == null || actual.ReverseWritePath == null)
            differences.Add(new EquivalenceDifference(path + ".ReverseWritePath", "Table 2", "one side null"));
        else
            CompareList("ReverseWritePath", expected.ReverseWritePath, actual.ReverseWritePath, differences, $"{path}.ReverseWritePath", ComparePropertyRef);

        // IsFlattened — direct
        if (expected.IsFlattened != actual.IsFlattened)
            differences.Add(new EquivalenceDifference(path + ".IsFlattened", expected.IsFlattened.ToString(), actual.IsFlattened.ToString(), "Table 2"));

        // PathOrigin — direct (assumption (a) — confirmed by A-class fixtures)
        if (expected.PathOrigin != actual.PathOrigin)
            differences.Add(new EquivalenceDifference(path + ".PathOrigin", expected.PathOrigin.ToString(), actual.PathOrigin.ToString(), "Table 2"));

        // ConverterDeclaringType / ConverterMethodName — Ordinal string
        if (!string.Equals(expected.ConverterDeclaringType, actual.ConverterDeclaringType, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConverterDeclaringType", "\"" + expected.ConverterDeclaringType + "\"", "\"" + actual.ConverterDeclaringType + "\"", "Table 2 Ordinal string"));
        if (!string.Equals(expected.ConverterMethodName, actual.ConverterMethodName, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConverterMethodName", "\"" + expected.ConverterMethodName + "\"", "\"" + actual.ConverterMethodName + "\"", "Table 2 Ordinal string"));

        // ConverterShape — direct
        if (expected.ConverterShape != actual.ConverterShape)
            differences.Add(new EquivalenceDifference(path + ".ConverterShape", expected.ConverterShape.ToString(), actual.ConverterShape.ToString(), "Table 2"));

        // MaskType — TypeRef native Equals
        if (!Equals(expected.MaskType, actual.MaskType))
            differences.Add(new EquivalenceDifference(path + ".MaskType", expected.MaskType?.FullName, actual.MaskType?.FullName, "Table 2", "TypeRef differs"));

        // FormatString — Ordinal string（A-1 补齐——实现注册表既有声明
        // SnapshotFieldRegistry.PropertyMappingSnapshot.Compared（登记），
        // 此前比较器全文零比较 = 账实不符实锤。H1：不触碰任何既有字段比较行为，非新增语义。
        // 位置与注册表字段序对齐（MaskType 与 IsFromJson 之间）；补丁形态沿同方法内既有 Ordinal 串比较。）
        if (!string.Equals(expected.FormatString, actual.FormatString, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".FormatString",
                "\"" + expected.FormatString + "\"", "\"" + actual.FormatString + "\"",
                "SnapshotFieldRegistry.PropertyMappingSnapshot.Compared（声明）"));

        // IsFromJson — direct
        if (expected.IsFromJson != actual.IsFromJson)
            differences.Add(new EquivalenceDifference(path + ".IsFromJson", expected.IsFromJson.ToString(), actual.IsFromJson.ToString(), "Table 2"));

        // 条件守卫四字段（Compared——双侧同承载 P-e：Fluent .Member(dest).MapWhen 与
        // 特性 [MapWhen] 填同一字段，值语义双通道一致；B 形态 ConditionMethodBody/ParamName 为
        // Fluent 独有（表达力不对称 D-n）——特性侧恒 null，null==null 比较 Through）
        if (!string.Equals(expected.ConditionMethodRef, actual.ConditionMethodRef, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConditionMethodRef", expected.ConditionMethodRef, actual.ConditionMethodRef, "Ordinal string"));
        if (!string.Equals(expected.ConditionMethodBody, actual.ConditionMethodBody, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConditionMethodBody", expected.ConditionMethodBody, actual.ConditionMethodBody, "Ordinal string"));
        if (!string.Equals(expected.ConditionMethodParamName, actual.ConditionMethodParamName, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConditionMethodParamName", expected.ConditionMethodParamName, actual.ConditionMethodParamName, "Ordinal string"));
        if (expected.ConditionShape != actual.ConditionShape)
            differences.Add(new EquivalenceDifference(path + ".ConditionShape", expected.ConditionShape.ToString(), actual.ConditionShape.ToString(), "Direct"));

        // B 形态转换器方法体三字段（Compared——与条件守卫 B 形态字段同口径：
        // 三元组双侧同承载，方法体三字段为 Fluent 独有（D-n），特性侧恒 null——null==null Through；
        // string[] 形参组 SequenceEqual Ordinal，null 与 null 等价、单侧 null 不等）
        if (!string.Equals(expected.ConverterMethodBody, actual.ConverterMethodBody, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConverterMethodBody", expected.ConverterMethodBody, actual.ConverterMethodBody, "Ordinal string"));
        if (!SequenceEqualOrdinal(expected.ConverterMethodParamTypes, actual.ConverterMethodParamTypes))
            differences.Add(new EquivalenceDifference(path + ".ConverterMethodParamTypes", "[" + string.Join(", ", expected.ConverterMethodParamTypes ?? Array.Empty<string>()) + "]", "[" + string.Join(", ", actual.ConverterMethodParamTypes ?? Array.Empty<string>()) + "]", "SequenceEqual(Ordinal)"));
        if (!SequenceEqualOrdinal(expected.ConverterMethodParamNames, actual.ConverterMethodParamNames))
            differences.Add(new EquivalenceDifference(path + ".ConverterMethodParamNames", "[" + string.Join(", ", expected.ConverterMethodParamNames ?? Array.Empty<string>()) + "]", "[" + string.Join(", ", actual.ConverterMethodParamNames ?? Array.Empty<string>()) + "]", "SequenceEqual(Ordinal)"));
    }

    // ----------------------------------------------------------------
    // Table 4: PropertyRef — 符号结构位（direct）+ attr-snapshot 通道输入镜像组（strip）+ Location（strip）
    // Implementation: ComparePropertyRef
    // 字段数不在此登记——由 ComparerFieldCoverageTests（双向臂 + 总计数守门）与
    // SnapshotEquivalenceComparerTests.PropertyRef_CoverageProjectionUnionStripped_EqualsFullSet /
    // PropertyRef_StrippedFieldsMatchTable4AttrSnapshotGroup 钉死
    // ConvertBack quartet stays in stripped group (Q3 final decision — see ComparePropertyRef comment)
    // ----------------------------------------------------------------

    /// <summary>
    /// Table 4: PropertyRef field projection
    /// Symbol structure group (10): direct comparison
    /// Attribute snapshot group (18): STRIPPED (channel-input mirror, not decision product)
    /// Location group (1): STRIPPED (Table 5)
    /// </summary>
    private static void ComparePropertyRef(PropertyRef? expected, PropertyRef? actual, List<EquivalenceDifference> differences, string path)
    {
        if (expected == null && actual == null) return;
        if (expected == null || actual == null)
        {
            differences.Add(new EquivalenceDifference(path, null, null, "Table 4", "one side null"));
            return;
        }

        // Symbol structure group (10) — direct comparison
        if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".Name", "\"" + expected.Name + "\"", "\"" + actual.Name + "\"", "Table 4 Ordinal string"));
        if (!Equals(expected.Type, actual.Type))
            differences.Add(new EquivalenceDifference(path + ".Type", expected.Type?.FullName, actual.Type?.FullName, "Table 4", "TypeRef differs"));
        if (expected.IsStatic != actual.IsStatic)
            differences.Add(new EquivalenceDifference(path + ".IsStatic", expected.IsStatic.ToString(), actual.IsStatic.ToString(), "Table 4"));
        if (expected.IsIndexer != actual.IsIndexer)
            differences.Add(new EquivalenceDifference(path + ".IsIndexer", expected.IsIndexer.ToString(), actual.IsIndexer.ToString(), "Table 4"));
        if (expected.DeclaredAccessibility != actual.DeclaredAccessibility)
            differences.Add(new EquivalenceDifference(path + ".DeclaredAccessibility", expected.DeclaredAccessibility.ToString(), actual.DeclaredAccessibility.ToString(), "Table 4"));
        if (expected.GetMethodAccessibility != actual.GetMethodAccessibility)
            differences.Add(new EquivalenceDifference(path + ".GetMethodAccessibility", expected.GetMethodAccessibility.ToString(), actual.GetMethodAccessibility.ToString(), "Table 4"));
        if (expected.SetMethodAccessibility != actual.SetMethodAccessibility)
            differences.Add(new EquivalenceDifference(path + ".SetMethodAccessibility", expected.SetMethodAccessibility.ToString(), actual.SetMethodAccessibility.ToString(), "Table 4"));
        if (expected.IsInitOnly != actual.IsInitOnly)
            differences.Add(new EquivalenceDifference(path + ".IsInitOnly", expected.IsInitOnly.ToString(), actual.IsInitOnly.ToString(), "Table 4"));
        if (expected.IsImplicitlyDeclared != actual.IsImplicitlyDeclared)
            differences.Add(new EquivalenceDifference(path + ".IsImplicitlyDeclared", expected.IsImplicitlyDeclared.ToString(), actual.IsImplicitlyDeclared.ToString(), "Table 4"));
        if (!Equals(expected.ContainingType, actual.ContainingType))
            differences.Add(new EquivalenceDifference(path + ".ContainingType", expected.ContainingType?.FullName, actual.ContainingType?.FullName, "Table 4", "TypeRef differs"));

        // ConvertBack quartet (4) — STRIPPED, Q3 final decision :
        // migration OUT of stripped set was attempted and empirically falsified —
        // AllTargetProperties[] entries carry attr-snapshot mirror on the attribute side
        // (HasConvertBackUsing=true from [ConvertBack]) but not on the Fluent side
        // (independent enumeration is materialized without fluent triple injection):
        // "AllTargetProperties[0].HasConvertBackUsing: differs (expected=True, actual=False)".
        // The pm.Target quartet IS value-consistent across channels, but the comparer projects
        // PropertyRef uniformly (cannot compare per-occurrence), and the AllTargetProperties
        // asymmetry is channel-input mirror (not decision product) — same nature as the rest of
        // this stripped group. Decision: KEEP STRIPPED; converter drift detection stays L2-only
        // (text-level, verified by mutation ③). Revisit condition: AllTargetProperties mirror
        // unification (separate initiative; current shared traversal gate does NOT unify AllTargetProperties mirror).
        // HasConvertBackUsing / ConverterBackDeclaringType / ConverterBackMethodName / ConverterBackShape

        // Attribute snapshot group (14) — STRIPPED (channel-input mirror, not decision product)
        // HasMapIgnore / MapIgnoreDirection / HasMapProperty / IsFromJson / HasOverrideIgnore /
        // HasConvertUsing / ConverterDeclaringType / ConverterMethodName / ConverterShape /
        // HasMask / MaskType / MapPropertyFromField / MapPropertyFromSource / HasMapNoAutoInclude

        // Location group (1) — STRIPPED (Table 5 #3)
    }

    // ----------------------------------------------------------------
    // Table 5: DiagnosticSnapshot — DescriptorId + Args + IsExplicit (Location stripped)
    // Implementation: CompareDiagnostic
    // N1 conclusion: Location stripped; Args comparability guaranteed by S/T class name convention
    // ----------------------------------------------------------------

    /// <summary>
    /// Table 5: DiagnosticSnapshot projection
    /// DescriptorId / Args / IsExplicit — direct comparison (Location stripped)
    /// Q4 open question: if Diagnostics order unstable, switch to sort-by-(DescriptorId+Args) normalization
    /// </summary>
    private static void CompareDiagnostic(DiagnosticSnapshot? expected, DiagnosticSnapshot? actual, List<EquivalenceDifference> differences, string path)
    {
        if (expected == null && actual == null) return;
        if (expected == null || actual == null)
        {
            differences.Add(new EquivalenceDifference(path, null, null, "Table 5", "one side null"));
            return;
        }

        // DescriptorId — Ordinal string
        if (!string.Equals(expected.DescriptorId, actual.DescriptorId, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".DescriptorId", "\"" + expected.DescriptorId + "\"", "\"" + actual.DescriptorId + "\"", "Table 5 Ordinal string"));

        // Args — sequence equal (Q4: if order unstable, normalize by sorting)
        if (!expected.Args.SequenceEqual(actual.Args, StringComparer.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".Args", "[" + string.Join(", ", expected.Args) + "]", "[" + string.Join(", ", actual.Args) + "]", "Table 5 SequenceEqual(Ordinal)"));

        // IsExplicit — direct
        if (expected.IsExplicit != actual.IsExplicit)
            differences.Add(new EquivalenceDifference(path + ".IsExplicit", expected.IsExplicit.ToString(), actual.IsExplicit.ToString(), "Table 5"));

        // Location — STRIPPED (Table 5 #2)
    }

    // ----------------------------------------------------------------
    // Table 6: DeepCopyNodeShape — TypeRef recursive + direct (Location stripped)
    // Implementation: CompareDeepCopyNode
    // ConstructorRef / DeepCopyPropertyShape / UnknownEdgeSnapshot recursive with same rules
    // ----------------------------------------------------------------

    /// <summary>
    /// Table 6: DeepCopyNodeShape projection
    /// TypeRef recursive + direct comparison (Location stripped)
    /// </summary>
    private static void CompareDeepCopyNode(DeepCopyNodeShape? expected, DeepCopyNodeShape? actual, List<EquivalenceDifference> differences, string path)
    {
        if (expected == null && actual == null) return;
        if (expected == null || actual == null)
        {
            differences.Add(new EquivalenceDifference(path, null, null, "Table 6", "one side null"));
            return;
        }

        // Type — TypeRef native Equals (recursive; no Location field)
        if (!Equals(expected.Type, actual.Type))
            differences.Add(new EquivalenceDifference(path + ".Type", expected.Type?.FullName, actual.Type?.FullName, "Table 6", "TypeRef differs"));

        // Location — STRIPPED (Table 5 #5)

        // HasPublicParameterlessCtor — direct
        if (expected.HasPublicParameterlessCtor != actual.HasPublicParameterlessCtor)
            differences.Add(new EquivalenceDifference(path + ".HasPublicParameterlessCtor", expected.HasPublicParameterlessCtor.ToString(), actual.HasPublicParameterlessCtor.ToString(), "Table 6"));

        // PublicConstructors — recursive (ConstructorRef pure value type, native Equals)
        CompareList("PublicConstructors", expected.PublicConstructors, actual.PublicConstructors, differences, $"{path}.PublicConstructors",
            (e, a, diffs, p) => { if (!Equals(e, a)) diffs.Add(new EquivalenceDifference(p, FormatVal(e), FormatVal(a), "Table 6", "ConstructorRef differs")); });

        // Properties — recursive (DeepCopyPropertyShape: direct + Location stripped)
        CompareList("Properties", expected.Properties, actual.Properties, differences, $"{path}.Properties", CompareDeepCopyProperty);

        // Leaf/container flags
        if (expected.IsLeaf != actual.IsLeaf)
            differences.Add(new EquivalenceDifference(path + ".IsLeaf", expected.IsLeaf.ToString(), actual.IsLeaf.ToString(), "Table 6"));
        if (expected.IsCollectionLike != actual.IsCollectionLike)
            differences.Add(new EquivalenceDifference(path + ".IsCollectionLike", expected.IsCollectionLike.ToString(), actual.IsCollectionLike.ToString(), "Table 6"));
        if (expected.IsDictionary != actual.IsDictionary)
            differences.Add(new EquivalenceDifference(path + ".IsDictionary", expected.IsDictionary.ToString(), actual.IsDictionary.ToString(), "Table 6"));
        if (!Equals(expected.KeyType, actual.KeyType))
            differences.Add(new EquivalenceDifference(path + ".KeyType", expected.KeyType?.FullName, actual.KeyType?.FullName, "Table 6", "TypeRef differs"));
        if (!Equals(expected.ValueType, actual.ValueType))
            differences.Add(new EquivalenceDifference(path + ".ValueType", expected.ValueType?.FullName, actual.ValueType?.FullName, "Table 6", "TypeRef differs"));
        if (!Equals(expected.ElementType, actual.ElementType))
            differences.Add(new EquivalenceDifference(path + ".ElementType", expected.ElementType?.FullName, actual.ElementType?.FullName, "Table 6", "TypeRef differs"));
        if (expected.IsNonGenericEnumerable != actual.IsNonGenericEnumerable)
            differences.Add(new EquivalenceDifference(path + ".IsNonGenericEnumerable", expected.IsNonGenericEnumerable.ToString(), actual.IsNonGenericEnumerable.ToString(), "Table 6"));
        if (expected.IsArray != actual.IsArray)
            differences.Add(new EquivalenceDifference(path + ".IsArray", expected.IsArray.ToString(), actual.IsArray.ToString(), "Table 6"));
        if (!Equals(expected.ArrayElementType, actual.ArrayElementType))
            differences.Add(new EquivalenceDifference(path + ".ArrayElementType", expected.ArrayElementType?.FullName, actual.ArrayElementType?.FullName, "Table 6", "TypeRef differs"));
        if (expected.IsNullableValueType != actual.IsNullableValueType)
            differences.Add(new EquivalenceDifference(path + ".IsNullableValueType", expected.IsNullableValueType.ToString(), actual.IsNullableValueType.ToString(), "Table 6"));
        if (!Equals(expected.UnderlyingType, actual.UnderlyingType))
            differences.Add(new EquivalenceDifference(path + ".UnderlyingType", expected.UnderlyingType?.FullName, actual.UnderlyingType?.FullName, "Table 6", "TypeRef differs"));
        if (expected.IsInterface != actual.IsInterface)
            differences.Add(new EquivalenceDifference(path + ".IsInterface", expected.IsInterface.ToString(), actual.IsInterface.ToString(), "Table 6"));
        if (expected.IsTypeParameter != actual.IsTypeParameter)
            differences.Add(new EquivalenceDifference(path + ".IsTypeParameter", expected.IsTypeParameter.ToString(), actual.IsTypeParameter.ToString(), "Table 6"));
        if (expected.IsPointer != actual.IsPointer)
            differences.Add(new EquivalenceDifference(path + ".IsPointer", expected.IsPointer.ToString(), actual.IsPointer.ToString(), "Table 6"));
        if (expected.IsValueType != actual.IsValueType)
            differences.Add(new EquivalenceDifference(path + ".IsValueType", expected.IsValueType.ToString(), actual.IsValueType.ToString(), "Table 6"));
    }

    /// <summary>
    /// DeepCopyPropertyShape projection: direct comparison（Location 登记为 Stripped——剥离表）
    /// 字段集 = <see cref="SnapshotFieldRegistry.DeepCopyPropertyShape"/> 行（本方法按其逐字段投影）；
    /// 计数与"Stripped 不现于比较位"由 ComparerFieldCoverageTests 钉死，不在本注登记数字
    /// </summary>
    private static void CompareDeepCopyProperty(DeepCopyPropertyShape? expected, DeepCopyPropertyShape? actual, List<EquivalenceDifference> differences, string path)
    {
        if (expected == null && actual == null) return;
        if (expected == null || actual == null)
        {
            differences.Add(new EquivalenceDifference(path, null, null, "Table 6", "one side null"));
            return;
        }

        if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".Name", "\"" + expected.Name + "\"", "\"" + actual.Name + "\"", "Table 6 Ordinal string"));
        if (!Equals(expected.Type, actual.Type))
            differences.Add(new EquivalenceDifference(path + ".Type", expected.Type?.FullName, actual.Type?.FullName, "Table 6", "TypeRef differs"));
        if (expected.GetterIsPublic != actual.GetterIsPublic)
            differences.Add(new EquivalenceDifference(path + ".GetterIsPublic", expected.GetterIsPublic.ToString(), actual.GetterIsPublic.ToString(), "Table 6"));
        if (expected.GetterIsVirtual != actual.GetterIsVirtual)
            differences.Add(new EquivalenceDifference(path + ".GetterIsVirtual", expected.GetterIsVirtual.ToString(), actual.GetterIsVirtual.ToString(), "Table 6"));
        if (expected.GetterIsSealed != actual.GetterIsSealed)
            differences.Add(new EquivalenceDifference(path + ".GetterIsSealed", expected.GetterIsSealed.ToString(), actual.GetterIsSealed.ToString(), "Table 6"));
        if (expected.ContainingTypeIsSealed != actual.ContainingTypeIsSealed)
            differences.Add(new EquivalenceDifference(path + ".ContainingTypeIsSealed", expected.ContainingTypeIsSealed.ToString(), actual.ContainingTypeIsSealed.ToString(), "Table 6"));
        if (expected.HasSetter != actual.HasSetter)
            differences.Add(new EquivalenceDifference(path + ".HasSetter", expected.HasSetter.ToString(), actual.HasSetter.ToString(), "Table 6"));
        if (expected.SetterIsPublic != actual.SetterIsPublic)
            differences.Add(new EquivalenceDifference(path + ".SetterIsPublic", expected.SetterIsPublic.ToString(), actual.SetterIsPublic.ToString(), "Table 6"));
        if (expected.SetterIsInitOnly != actual.SetterIsInitOnly)
            differences.Add(new EquivalenceDifference(path + ".SetterIsInitOnly", expected.SetterIsInitOnly.ToString(), actual.SetterIsInitOnly.ToString(), "Table 6"));
        // HasMapIgnore — STRIPPED (channel-input mirror: attr reads [MapIgnore], fluent doesn't; same caliber as PropertyRef attr-snapshot group Table 4)

        // Location — STRIPPED (Table 5 #4)
    }

    // ----------------------------------------------------------------
    // Table 6: ComposeMappingSnapshot — TypeRef/PropertyRef projection + direct
    // Implementation: CompareComposeMapping
    // C-03 attribute-only; A-class fixtures both sides empty (structural fallback)
    // ----------------------------------------------------------------

    /// <summary>
    /// Table 6: ComposeMappingSnapshot projection
    /// SubSource / Source / SourcePath / SourceIndexes / Target / Converter triplet
    /// </summary>
    private static void CompareComposeMapping(ComposeMappingSnapshot? expected, ComposeMappingSnapshot? actual, List<EquivalenceDifference> differences, string path)
    {
        if (expected == null && actual == null) return;
        if (expected == null || actual == null)
        {
            differences.Add(new EquivalenceDifference(path, null, null, "Table 6", "one side null"));
            return;
        }

        // SubSource — TypeRef native Equals
        if (!Equals(expected.SubSource, actual.SubSource))
            differences.Add(new EquivalenceDifference(path + ".SubSource", expected.SubSource?.FullName, actual.SubSource?.FullName, "Table 6", "TypeRef differs"));

        // Source — PropertyRef projection
        ComparePropertyRef(expected.Source, actual.Source, differences, $"{path}.Source");

        // SourcePath — per-segment PropertyRef projection
        CompareList("SourcePath", expected.SourcePath, actual.SourcePath, differences, $"{path}.SourcePath", ComparePropertyRef);

        // SourceIndexes — direct SequenceEqual (Table 2: A-class no-index scenarios both sides empty)
        // Normalize: if both sides HasIndex=false (no actual index segments), treat as equivalent
        if (expected.HasIndex || actual.HasIndex)
        {
            if (!expected.SourceIndexes.SequenceEqual(actual.SourceIndexes))
                differences.Add(new EquivalenceDifference(path + ".SourceIndexes", "Table 6 SequenceEqual(normalized)"));
        }

        // Target — PropertyRef projection
        ComparePropertyRef(expected.Target, actual.Target, differences, $"{path}.Target");

        // Converter triplet — direct
        if (!string.Equals(expected.ConverterDeclaringType, actual.ConverterDeclaringType, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConverterDeclaringType", "\"" + expected.ConverterDeclaringType + "\"", "\"" + actual.ConverterDeclaringType + "\"", "Table 6 Ordinal string"));
        if (!string.Equals(expected.ConverterMethodName, actual.ConverterMethodName, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConverterMethodName", "\"" + expected.ConverterMethodName + "\"", "\"" + actual.ConverterMethodName + "\"", "Table 6 Ordinal string"));
        if (expected.ConverterShape != actual.ConverterShape)
            differences.Add(new EquivalenceDifference(path + ".ConverterShape", expected.ConverterShape.ToString(), actual.ConverterShape.ToString(), "Table 6"));

        // 已校验镜像通道两字段（registry Compared——与 PropertyMappingSnapshot
        // 的 MaskType/IsFromJson 比较同构；Target.PropertyRef 原始值按 Channel-input mirror Stripped 不比）
        if (!Equals(expected.MaskType, actual.MaskType))
            differences.Add(new EquivalenceDifference(path + ".MaskType", expected.MaskType?.FullName, actual.MaskType?.FullName, "Table 6", "TypeRef differs"));

        // FormatString — Ordinal string（A-1 补齐——实现注册表既有声明
        // SnapshotFieldRegistry.ComposeMappingSnapshot.Compared（登记），
        // 与主源 PropertyMappingSnapshot.FormatString 比较同构（已校验镜像通道，行为驱动字段）。
        // 位置与注册表字段序对齐（MaskType 与 IsFromJson 之间）。）
        if (!string.Equals(expected.FormatString, actual.FormatString, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".FormatString",
                "\"" + expected.FormatString + "\"", "\"" + actual.FormatString + "\"",
                "SnapshotFieldRegistry.ComposeMappingSnapshot.Compared（声明）"));

        if (expected.IsFromJson != actual.IsFromJson)
            differences.Add(new EquivalenceDifference(path + ".IsFromJson", expected.IsFromJson.ToString(), actual.IsFromJson.ToString(), "Table 6"));

        // 块内 MapWhen 条件守卫四字段（Compared——与
        // PropertyMappingSnapshot 条件四字段比较同构）
        if (!string.Equals(expected.ConditionMethodRef, actual.ConditionMethodRef, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConditionMethodRef", expected.ConditionMethodRef, actual.ConditionMethodRef, "Ordinal string"));
        if (!string.Equals(expected.ConditionMethodBody, actual.ConditionMethodBody, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConditionMethodBody", expected.ConditionMethodBody, actual.ConditionMethodBody, "Ordinal string"));
        if (!string.Equals(expected.ConditionMethodParamName, actual.ConditionMethodParamName, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConditionMethodParamName", expected.ConditionMethodParamName, actual.ConditionMethodParamName, "Ordinal string"));
        if (expected.ConditionShape != actual.ConditionShape)
            differences.Add(new EquivalenceDifference(path + ".ConditionShape", expected.ConditionShape.ToString(), actual.ConditionShape.ToString(), "Direct"));

        // 块内 Convert B 形态转换器方法体三字段（Compared——块形态为
        // Fluent 独有承载位，特性对 ComposeMappings 恒无该值——null==null Through）
        if (!string.Equals(expected.ConverterMethodBody, actual.ConverterMethodBody, StringComparison.Ordinal))
            differences.Add(new EquivalenceDifference(path + ".ConverterMethodBody", expected.ConverterMethodBody, actual.ConverterMethodBody, "Ordinal string"));
        if (!SequenceEqualOrdinal(expected.ConverterMethodParamTypes, actual.ConverterMethodParamTypes))
            differences.Add(new EquivalenceDifference(path + ".ConverterMethodParamTypes", "[" + string.Join(", ", expected.ConverterMethodParamTypes ?? Array.Empty<string>()) + "]", "[" + string.Join(", ", actual.ConverterMethodParamTypes ?? Array.Empty<string>()) + "]", "SequenceEqual(Ordinal)"));
        if (!SequenceEqualOrdinal(expected.ConverterMethodParamNames, actual.ConverterMethodParamNames))
            differences.Add(new EquivalenceDifference(path + ".ConverterMethodParamNames", "[" + string.Join(", ", expected.ConverterMethodParamNames ?? Array.Empty<string>()) + "]", "[" + string.Join(", ", actual.ConverterMethodParamNames ?? Array.Empty<string>()) + "]", "SequenceEqual(Ordinal)"));
    }

    // ----------------------------------------------------------------
    // Coverage self-check (acceptance #3)
    // Reflect-enumerate PropertyMappingSnapshot / PropertyRef all property names;
    // projection-cover-set ∪ stripped-set = full-set (prep for second-phase frozen assertion)
    // ----------------------------------------------------------------

    /// <summary>
    /// Coverage self-check: verify that hand-written projections cover all init storage properties
    /// (except explicitly stripped Location / attr-snapshot fields).
    /// Called by EquivalenceRegistryIntegrityTests / dedicated test.
    /// </summary>
    internal static (IReadOnlyList<string> CoveredByProjection, IReadOnlyList<string> Stripped, IReadOnlyList<string> AllProps) GetPropertyMappingCoverage()
    {
        // 剥离集改读 SnapshotFieldRegistry（注册表为唯一数据源，B12 比较语义零变更）
        var allProps = typeof(PropertyMappingSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var regFields = SnapshotFieldRegistry.PropertyMappingSnapshot;
        var coveredByProjection = regFields.Where(r => r.Role == FieldRole.Compared).Select(r => r.Field).ToArray();
        var stripped = regFields.Where(r => r.Role == FieldRole.Stripped).Select(r => r.Field).ToArray();

        return (coveredByProjection, stripped, allProps);
    }

    internal static (IReadOnlyList<string> CoveredByProjection, IReadOnlyList<string> Stripped, IReadOnlyList<string> AllProps) GetPropertyRefCoverage()
    {
        var allProps = typeof(PropertyRef)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var regFields = SnapshotFieldRegistry.PropertyRef;
        var coveredByProjection = regFields.Where(r => r.Role == FieldRole.Compared).Select(r => r.Field).ToArray();
        var stripped = regFields.Where(r => r.Role == FieldRole.Stripped).Select(r => r.Field).ToArray();

        return (coveredByProjection, stripped, allProps);
    }

    // ----------------------------------------------------------------
    // Utilities
    // ----------------------------------------------------------------

    /// <summary>string[] 形参组 SequenceEqual Ordinal（null 与 null 等价、单侧 null 不等）</summary>
    private static bool SequenceEqualOrdinal(string[]? expected, string[]? actual)
    {
        if (expected == null && actual == null) return true;
        if (expected == null || actual == null) return false;
        return expected.SequenceEqual(actual, StringComparer.Ordinal);
    }

    private static string FormatVal(object? val)
    {
        if (val == null) return "null";
        if (val is string s) return $"\"{s}\"";
        return val.ToString() ?? val.GetType().Name;
    }
}
