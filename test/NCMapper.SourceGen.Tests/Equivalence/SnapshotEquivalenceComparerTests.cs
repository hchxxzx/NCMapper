using System;
using System.Linq;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// acceptance tests: denylist registration validation (#2) + coverage self-check (#3).
/// </summary>
public class SnapshotEquivalenceComparerTests
{
    // ----------------------------------------------------------------
    // Acceptance #2: denylist registration validation — typo => Assert.Fail
    // Proof that registration validation is not a no-op
    // ----------------------------------------------------------------

    [Fact]
    public void DenyList_AllFieldsExistInReflectionPropertySet()
    {
        // The comparer's ValidateRegistration runs on first Compare call.
        // If any denylist field name doesn't exist in MappingSnapshot's reflection
        // property set, it throws Assert.Fail — this test proves all entries are valid.
        // (TopLevelDenyList 现 4 项——符号内部改读注册表自动适配)
        var nullResult = SnapshotEquivalenceComparer.Compare(null, null);
        Assert.True(nullResult.AreEquivalent);

        // Also verify denylist entries match expected field names (typo detection)
        var propNames = typeof(MappingSnapshot)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        foreach (var denied in SnapshotEquivalenceComparer.TopLevelDenyList)
        {
            Assert.Contains(denied, propNames);
        }
    }

    // ----------------------------------------------------------------
    // Acceptance #3: coverage self-check — projection-cover ∪ stripped = full-set
    // Prep for second-phase frozen assertion
    // ----------------------------------------------------------------

    [Fact]
    public void PropertyMappingSnapshot_CoverageProjectionUnionStripped_EqualsFullSet()
    {
        // 迁移改读 SnapshotFieldRegistry（注册表为唯一数据源，无双源——评审 001 P1-1）
        var regFields = SnapshotFieldRegistry.PropertyMappingSnapshot;
        var covered = regFields.Where(r => r.Role == FieldRole.Compared).Select(r => r.Field).ToList();
        var stripped = regFields.Where(r => r.Role == FieldRole.Stripped).Select(r => r.Field).ToList();

        var allProps = typeof(PropertyMappingSnapshot)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // PropertyMappingSnapshot: 20 init storage props + 2 expression-bodied derived (stripped)
        // (ConditionMethodRef/ConditionMethodBody/ConditionMethodParamName/ConditionShape 4 守卫字段)
        // (ConverterMethodBody/ConverterMethodParamTypes/ConverterMethodParamNames 3 B 形态转换器字段)
        // (FormatString 格式化串承载)
        Assert.Equal(20, covered.Count);
        Assert.Equal(2, stripped.Count);  // HasIndex + IsReverseWriteBack (expression-bodied derived)

        var coverUnionStripped = covered.ToList();
        coverUnionStripped.AddRange(stripped);
        coverUnionStripped.Sort(StringComparer.Ordinal);

        Assert.Equal(allProps, coverUnionStripped);
    }

    [Fact]
    public void PropertyRef_CoverageProjectionUnionStripped_EqualsFullSet()
    {
        // 迁移改读 SnapshotFieldRegistry（注册表为唯一数据源，无双源）
        var regFields = SnapshotFieldRegistry.PropertyRef;
        var covered = regFields.Where(r => r.Role == FieldRole.Compared).Select(r => r.Field).ToList();
        var stripped = regFields.Where(r => r.Role == FieldRole.Stripped).Select(r => r.Field).ToList();

        var allProps = typeof(PropertyRef)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // PropertyRef: 10 symbol structure + 23 attr-snapshot stripped + 1 Location stripped = 34
        // (HasFluentExplicit 通道输入镜像)
        // (ConverterBackMethodBody/ParamTypes/ParamNames 3 B 形态反向转换器镜像)
        // (HasMapFormat 通道输入镜像)
        Assert.Equal(10, covered.Count);
        Assert.Equal(24, stripped.Count);  // 23 attr + 1 Location

        var coverUnionStripped = covered.ToList();
        coverUnionStripped.AddRange(stripped);
        coverUnionStripped.Sort(StringComparer.Ordinal);

        Assert.Equal(allProps, coverUnionStripped);
    }

    [Fact]
    public void PropertyRef_StrippedFieldsMatchTable4AttrSnapshotGroup()
    {
        // 迁移改读 SnapshotFieldRegistry Stripped 项（注册表为唯一数据源）
        var stripped = SnapshotFieldRegistry.PropertyRef
            .Where(r => r.Role == FieldRole.Stripped)
            .Select(r => r.Field)
            .ToList();

        var expectedStripped = new[]
        {
            // Attribute snapshot group (18 + 3 B 形态反向转换器镜像
            //  + 1 HasMapFormat 通道输入镜像)
            "HasMapIgnore", "MapIgnoreDirection", "HasMapProperty", "IsFromJson", "HasOverrideIgnore",
            "HasConvertUsing", "ConverterDeclaringType", "ConverterMethodName", "ConverterShape",
            "HasConvertBackUsing", "ConverterBackDeclaringType", "ConverterBackMethodName", "ConverterBackShape",
                  "HasMask", "HasMapFormat", "MaskType", "MapPropertyFromField", "MapPropertyFromSource", "HasMapNoAutoInclude", "HasFluentExplicit",
            "ConverterBackMethodBody", "ConverterBackMethodParamTypes", "ConverterBackMethodParamNames",
            // Location group (1)
            "Location"
        };

        Assert.Equal(expectedStripped.Length, stripped.Count);
        foreach (var field in expectedStripped)
        {
            Assert.Contains(field, stripped);
        }
    }
}
