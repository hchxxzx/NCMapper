using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// W3_1 TG-P：发射前准备段产物等价测试 — GenerationPreparation.Prepare 前移
/// （上提三方法 + C1 反向表 + C3 环检测 + C4 深拷贝分类）后，产物与逐点独立调用等价
/// （构造前移不改变结果）。
/// <para>两级验证：</para>
/// <list type="bullet">
/// <item>逐点直调（白盒，NestedCycleDegradationTests 先例）：上提三方法对同一手工 mappings 的产物值断言</item>
/// <item>管线投影（端到端，TestHelper.GenerateMapper）：prep 三产物
/// （ReverseSnapshotTable / DeepCopyGraphs / CycleDetectionPrepared）在生成代码与诊断中的下游投影</item>
/// </list>
/// <para>注：Prepare(spc,...) 的 SourceProductionContext 无法在单测构造（Roslyn 无公开构造），
/// 编排等价由端到端投影断言 + 逐字节基线（GeneratedSnapshotBaselineTests）共同锁定。</para>
/// </summary>
public class GenerationPreparationTests
{
    /// <summary>
    /// 白盒辅助：构造类型快照
    /// <para>EqualityKey 必填——TypeRef 字典相等键（IsSameType 判定基准；
    /// 缺省时全部实例坍缩同一字典键，NestedCycleDegradationTests.MakeType 同先例）</para>
    /// </summary>
    private static TypeRef MakeType(string fullName, string simpleName)
        => new() { FullName = fullName, EqualityKey = fullName, SimpleName = simpleName, Kind = TypeKind.Class };

    /// <summary>
    /// 白盒辅助：构造映射快照
    /// </summary>
    private static MappingSnapshot MakeMapping(TypeRef src, TypeRef tgt, string srcClassName, string tgtClassName)
        => new()
        {
            SourceType = src,
            TargetType = tgt,
            SourceClassName = srcClassName,
            TargetClassName = tgtClassName,
        };

    /// <summary>
    /// TG-P-1：上提三方法逐点独立调用 — 同简单名异命名空间后缀分配 + 类型名映射 + 互指对检出
    /// （原 GenerateMapperCode :43/:44/:48 构造点，前移后方法本体逐行不动、同输入同输出）
    /// </summary>
    [Fact]
    public void LiftedThreeMethods_DirectCall_ProductsUnchanged()
    {
        // 两对同名类型（Ns1/Ns2 各一对 User→UserDto）触发基础方法名冲突（H-05 按 FullName 排序分配后缀）
        var ns1User = MakeType("Ns1.User", "User");
        var ns1UserDto = MakeType("Ns1.UserDto", "UserDto");
        var ns2User = MakeType("Ns2.User", "User");
        var ns2UserDto = MakeType("Ns2.UserDto", "UserDto");
        var m1 = MakeMapping(ns1User, ns1UserDto, "User", "UserDto");
        var m2 = MakeMapping(ns2User, ns2UserDto, "User", "UserDto");
        var mappings = ImmutableArray.Create<MappingSnapshot?>(m1, m2);

        // ① AssignMethodSuffixes（原 :43）：冲突组首个无后缀、后续 _2（键 = SrcFullName|TgtFullName）
        var suffixMap = CodeGenerator.AssignMethodSuffixes(mappings);
        Assert.Equal(2, suffixMap.Count);
        Assert.Equal("", suffixMap["Ns1.User\u0001Ns1.UserDto"]);
        Assert.Equal("_2", suffixMap["Ns2.User\u0001Ns2.UserDto"]);

        // ② BuildTypeNameMap（原 :44）：类型名 = 类名 + 后缀（DeepCopy/嵌套引用方法名）
        var typeNameMap = CodeGenerator.BuildTypeNameMap(mappings, suffixMap);
        Assert.Equal("User", typeNameMap[ns1User]);
        Assert.Equal("UserDto", typeNameMap[ns1UserDto]);
        Assert.Equal("User_2", typeNameMap[ns2User]);
        Assert.Equal("UserDto_2", typeNameMap[ns2UserDto]);

        // ③ DetectMutualPairs（原 :48）：互指对 (A.Source==B.Target && A.Target==B.Source) 双向键检出
        var e1 = MakeType("Ns1.E1", "E1");
        var e2 = MakeType("Ns1.E2", "E2");
        var mA = MakeMapping(e1, e2, "E1", "E2");
        var mB = MakeMapping(e2, e1, "E2", "E1");
        var mutualPairs = CodeGenerator.DetectMutualPairs(ImmutableArray.Create<MappingSnapshot?>(mA, mB));
        Assert.True(mutualPairs.ContainsKey(e1), "互指对应含 E1→E2 方向键");
        Assert.True(mutualPairs.ContainsKey(e2), "互指对应含 E2→E1 方向键（双向键）");
        // IsMutualPair 同口径（ReversePlanner/主循环消费判定）
        Assert.True(CodeGenerator.IsMutualPair(mA, mutualPairs));
        Assert.True(CodeGenerator.IsMutualPair(mB, mutualPairs));
        // 非互指输入零检出
        var noPairs = CodeGenerator.DetectMutualPairs(mappings);
        Assert.Empty(noPairs);
    }

    /// <summary>
    /// TG-P-2：上提三方法管线投影 — 同一后缀冲突源码经全管线生成，方法名后缀与逐点直调产物一致
    /// （Prepare 上提后 MethodSuffixMap/TypeNameMap 经 prep 携带，发射端查表渲染同一后缀）
    /// </summary>
    [Fact]
    public void LiftedThreeMethods_PipelineProjection_ConsistentWithDirectCall()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Ns1 { public class User { public int Id { get; set; } } }
            namespace Ns2 { public class User { public int Id { get; set; } } }
            namespace Ns1 { [MapFrom(typeof(User))] public class UserDto { public int Id { get; set; } } }
            namespace Ns2 { [MapFrom(typeof(User))] public class UserDto { public int Id { get; set; } } }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 冲突组后缀：首对无后缀 + 第二对 _2（后缀同时加在源/目标类名上——与 TG-P-1 直调产物同构，
        // 构造前移不改变结果；方法名 = Map_{srcName}_To_{tgtName}，srcName/tgtName 均为"类名+后缀"）
        result.Should().HaveMethod("Map_User_To_UserDto");
        result.Should().HaveMethod("Map_User_2_To_UserDto_2");
        // TypeNameMap 投影：DeepCopy 方法名带后缀（User_2 / UserDto_2）
        Assert.True(result.Methods.ContainsKey("DeepCopy_User"), "首对类型 DeepCopy 方法名无后缀");
        Assert.True(result.Methods.ContainsKey("DeepCopy_User_2"), "第二对类型 DeepCopy 方法名带 _2 后缀");
    }

    /// <summary>
    /// TG-P-3：prep 三产物管线投影 — ReverseSnapshotTable（反向扩展生成）+ DeepCopyGraphs
    /// （DeepCopy 方法 + acyclic 字面量）经端到端生成代码实证（准备段构造 → 发射端纯渲染）
    /// </summary>
    [Fact]
    public void PrepareProducts_ReverseTableAndDeepCopyGraphs_ProjectedInGeneratedCode()
    {
        var source = """
            using NCMapper.Attributes;
            public class PrepEntity { public int Id { get; set; } public PrepChild Child { get; set; } = new(); }
            public class PrepChild { public string Name { get; set; } = ""; }
            [MapFrom(typeof(PrepEntity))]
            public class PrepDto { public int Id { get; set; } public PrepChild Child { get; set; } = new(); }
            """;

        var result = TestHelper.GenerateMapper(source);

        // ① C4 投影：DeepCopy 方法生成 + 注册行 acyclic 字面量（叶子链 PrepEntity→PrepChild → Acyclic）；
        // 注册行仅覆盖映射端点类型（PrepEntity/PrepDto——键收集为 mapping.SourceType/TargetType；
        // PrepChild 为嵌套节点非端点，不注册——与原主循环键收集口径一致）
        Assert.True(result.Methods.ContainsKey("DeepCopy_PrepEntity"), "DeepCopyGraphs 产物应投影为 DeepCopy 方法");
        Assert.Contains("RegisterGeneratedDeepCopy<PrepEntity>(GeneratedMapper.DeepCopy_PrepEntity, acyclic: true)", result.GeneratedCode);
        Assert.Contains("RegisterGeneratedDeepCopy<PrepDto>(GeneratedMapper.DeepCopy_PrepDto, acyclic: true)", result.GeneratedCode);

        // ② C1 投影：ReverseSnapshotTable 产物应投影为反向扩展（MapToPrepEntity 6 重载族）
        Assert.True(result.Methods.ContainsKey("MapToPrepEntity"), "ReverseSnapshotTable 产物应投影为反向扩展");
        Assert.True(result.Methods.ContainsKey("MapToPrepEntityList"), "反向列表扩展应生成");
    }

    /// <summary>
    /// TG-P-4：CycleDetectionPrepared 投影 — 嵌套互调环场景管线行为与基线一致
    /// （C3 前移后环检测仍在发射前执行，IsDegraded 查询点语义不变）
    /// <para>基线口径（SgCircularA/B 同构，Models.cs 既有编译警告）：嵌套注册对属性在 transform 期
    /// 类型兼容判定即降级（NCMAP002 属性级）；环检测器仍发射 NCMAP029（Info 级降级提示）。</para>
    /// <para>B4-01 修复后口径修订：此前 BuildById 反查表漏登记
    /// NestedCycleDegradedRule.Id，FromId 查空致发射期回落 NCMAP099（Warning"生成失败"误报）——
    /// 本用例旧断言（NCMAP099 存在 + NCMAP029 不存在）编码的正是该回落缺陷形态
    /// （修复后全量 G4 红暴露，实态：环上两侧各一条 Info NCMAP029、零 NCMAP099）。
    /// 口径回归降级语义：NCMAP029 存在且 Info 级 + NCMAP099 误报消除。</para>
    /// </summary>
    [Fact]
    public void CycleDetectionPrepared_NestedCycle_NCMAP029Projected()
    {
        // 嵌套互调环：User→UserDto 嵌套 Address→AddressDto；Address→AddressDto 嵌套 User→UserDto
        var source = """
            using NCMapper.Attributes;
            public class CycUser { public int Id { get; set; } public CycAddress Address { get; set; } = new(); }
            public class CycAddress { public int Id { get; set; } public CycUser Owner { get; set; } = new(); }
            [MapFrom(typeof(CycUser))]
            public class CycUserDto { public int Id { get; set; } public CycAddressDto Address { get; set; } = new(); }
            [MapFrom(typeof(CycAddress))]
            public class CycAddressDto { public int Id { get; set; } public CycUserDto Owner { get; set; } = new(); }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 属性级降级（transform 期类型兼容判定，基线行为）：嵌套注册对两侧各一条 NCMAP002
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP002"
            && d.GetMessage().Contains("Address"));
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP002"
            && d.GetMessage().Contains("Owner"));
        // B4-01 修复后：环上两映射对 NCMAP029（Info 级降级提示）正常发射（口径回归降级语义——
        // 旧断言的 NCMAP099 系 FromId 查空回落误报，不得作为基线保留）
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP029"
            && d.Severity == DiagnosticSeverity.Info);
        // NCMAP099 误报消除（B4-01 修复面）：不再回落"生成失败"语义
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP099");
    }
}
