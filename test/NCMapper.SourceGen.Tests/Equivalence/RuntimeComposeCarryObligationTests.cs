// 等价门履约测试（五关联点条件锁定）：
// 承载面全部为运行时类型（RuntimeComposeMapping/ComposeMappings），编译期 Models.Snapshots
// 零字段改动（仅 :503 注释校准）——SnapshotFieldRegistry/SnapshotEquivalenceComparer 本期不触发
// 同步义务。本测试把该前提从「方案登记」升格为「机械断言」：ComposeMappingSnapshot 属性面
// 冻结在本批次基线清单——任何新增属性（含注释性改动伴随的意外字段）即红，逼按五关联点 SOP
// 同步 SnapshotFieldRegistry/SnapshotEquivalenceComparer/发射端。
// 与 A1 冻结断言（SnapshotFieldRegistryTests）的分工：A1 锁「注册表 vs 反射」一致性（注册表
// 可随类型同长仍绿）；本测试锁「反射 vs 批次冻结清单」（表面增长必红）——两轴正交。
using System;
using System.Linq;
using System.Reflection;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

public class RuntimeComposeCarryObligationTests
{
    /// <summary>ComposeMappingSnapshot 批次基线冻结属性清单（开工时点实读，
    /// Models.Snapshots.cs :854-949——19 项含计算属性 HasIndex）。</summary>
    private static readonly string[] FrozenProperties =
    {
        "ConditionMethodBody",
        "ConditionMethodParamName",
        "ConditionMethodRef",
        "ConditionShape",
        "ConverterDeclaringType",
        "ConverterMethodName",
        "ConverterMethodBody",
        "ConverterMethodParamNames",
        "ConverterMethodParamTypes",
        "ConverterShape",
        "FormatString",
        "HasIndex",
        "IsFromJson",
        "MaskType",
        "Source",
        "SourceIndexes",
        "SourcePath",
        "SubSource",
        "Target",
    };

    [Fact]
    public void ComposeMappingSnapshot_Surface_FrozenAtBatchBaseline()
    {
        var actual = typeof(ComposeMappingSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var added = actual.Except(FrozenProperties).ToArray();
        var removed = FrozenProperties.Except(actual).ToArray();

        Assert.True(
            added.Length == 0 && removed.Length == 0,
            "ComposeMappingSnapshot 表面相对批次基线漂移——" +
            $"新增 [{string.Join(", ", added)}] / 移除 [{string.Join(", ", removed)}]。" +
            "编译期快照类型变更有五关联点同步义务（SnapshotFieldRegistry/SnapshotEquivalenceComparer/发射端），" +
            "且发射面/基线再生预期 DIFF 须重新归因");
    }
}
