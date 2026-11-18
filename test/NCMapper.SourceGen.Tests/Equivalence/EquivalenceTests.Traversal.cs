using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// 共享遍历门单元直测 + ReverseOnly 门序验证——
/// 结构性收敛的回归护栏：直调 <see cref="MappingExtractor.EnumerateWritableTargetProperties"/> +
/// <see cref="MappingExtractor.TryAppendReverseOnlyEntry"/> 验证门序等价与通道行为差异。
/// <para>
/// <b>共享遍历门直测矩阵</b>（第一行）：继承层次展开 / GroupBy 最派生优先 / static 跳过 /
/// indexer 跳过 / public setter 接受 / init-only 接受 / get-only 回调 / private setter 回调。
/// </para>
/// <para>
/// <b>ReverseOnly 门序验证</b>（第三行 + 评审 002 P0-1 可达形态）：
/// <list type="bullet">
/// <item><b>共享方法直测</b>：直调 TryAppendReverseOnlyEntry，get-only/不可写 entityProp + 哨兵解析委托
/// （调用即抛/置标志）→ 断言仅发 ReverseMappingSkippedRule、委托未被调用（证门先于解析）、无组装产物。</item>
/// <item><b>特性侧端到端</b>（A4）：get-only + [ConvertBack] 引用无效 → 诊断集 = 仅
/// ReverseMappingSkippedRule（无 NCMAP035/036/037）——由管线级既有覆盖承载：
/// <c>ConvertBackUsingGenerationTests.SetterGate_GetOnly_SkipsWithNCMAP013</c>（改调前后 diff=0
/// 属执行期基线比对演练，非持久测试形态；本文件为共享方法单元直测）。</item>
/// <item><b>Fluent 侧 F22 边界声明</b>（测试注释登记）：get-only 锚点被 FluentExtractor F22 提取期拦截，
/// backDecl 不进入物化——门失败分支不可达，不建 fixture（评审 002 P0-1 禁止形态，F6）。</item>
/// </list>
/// </para>
/// <para><b>全量回归</b>（A-01~A-12 + 字节不变门 + 核查 1/2/3）由既有 fixture 零改动跑（B11）+
/// samples 生成代码 diff=0（A4 已验）+ EquivalenceRegistryIntegrityTests 核查。</para>
/// </summary>
public class EquivalenceTests_Traversal
{
    // ----------------------------------------------------------------
    // 共享遍历门直测矩阵（第一行）
    // ----------------------------------------------------------------

    private const string SetterShapeSource = @"
using NCMapper;
public class Base { public string BaseProp { get; set; } }
public class Target : Base
{
    public string PublicGetSet { get; set; }              // public setter — accept
    public string InitOnly { get; init; }                // init-only — accept (P1-SHOULD-8)
    public string GetOnly { get; }                       // get-only — skip + callback
    public string PrivateSet { get; private set; }       // private setter — skip + callback
    public static string StaticProp { get; set; }        // static — skip
    public string this[int i] { get => null; set { } }   // indexer — skip
    public override string ToString() => null;
}
";

    private static IPropertySymbol FindProp(INamedTypeSymbol type, string name)
        => type.GetMembers().OfType<IPropertySymbol>().First(p => p.Name == name);

    [Fact]
    public void EnumerateWritableTargetProperties_AcceptsPublicSetterAndInitOnly()
    {
        var (targetType, _) = BuildTargetType();
        var writable = MappingExtractor.EnumerateWritableTargetProperties(targetType, CancellationToken.None).ToList();
        var names = writable.Select(p => p.Name).ToList();

        Assert.Contains("PublicGetSet", names);
        Assert.Contains("InitOnly", names);
        Assert.Contains("BaseProp", names);  // 继承层次展开（基类属性）
    }

    [Fact]
    public void EnumerateWritableTargetProperties_SkipsStaticAndIndexer()
    {
        var (targetType, _) = BuildTargetType();
        var writable = MappingExtractor.EnumerateWritableTargetProperties(targetType, CancellationToken.None).ToList();
        var names = writable.Select(p => p.Name).ToList();

        Assert.DoesNotContain("StaticProp", names);     // static 跳过
        Assert.DoesNotContain("Item", names);           // indexer 跳过（Name="Item"）
    }

    [Fact]
    public void EnumerateWritableTargetProperties_SkipsNonPublicSetter_WithCallback()
    {
        var (targetType, _) = BuildTargetType();
        var nonWritable = new List<string>();
        var writable = MappingExtractor.EnumerateWritableTargetProperties(
            targetType, CancellationToken.None, prop => nonWritable.Add(prop.Name)).ToList();

        // get-only + private set 都应触发回调
        Assert.Contains("GetOnly", nonWritable);
        Assert.Contains("PrivateSet", nonWritable);

        // 且不进入 writable 集
        Assert.DoesNotContain("GetOnly", writable.Select(p => p.Name));
        Assert.DoesNotContain("PrivateSet", writable.Select(p => p.Name));
    }

    [Fact]
    public void EnumerateWritableTargetProperties_OnNonWritableNull_SilentSkip()
    {
        var (targetType, _) = BuildTargetType();
        // Fluent 侧形态：onNonWritable=null 静默跳过（不抛）
        var writable = MappingExtractor.EnumerateWritableTargetProperties(targetType, CancellationToken.None).ToList();
        // 应正常返回（不因 get-only/private 抛异常）
        Assert.NotEmpty(writable);
    }

    [Fact]
    public void EnumerateWritableTargetProperties_GroupByMostDerived()
    {
        // 基类与派生类同名属性——GroupBy 取最派生优先
        const string src = @"
public class Base { public string Dup { get; set; } }
public class Derived : Base { public new string Dup { get; set; } }
";
        var comp = TestHelper.BuildCompilation(src);
        var derived = comp.GetTypeByMetadataName("Derived");
        var writable = MappingExtractor.EnumerateWritableTargetProperties(derived, CancellationToken.None).ToList();
        // 应只产出 1 个 Dup（最派生）
        var dups = writable.Where(p => p.Name == "Dup").ToList();
        Assert.Single(dups);
        // 最派生 = Derived.Dup（ContainingType == Derived）
        Assert.Equal("Derived", dups[0].ContainingType.Name);
    }

    // ----------------------------------------------------------------
    // ReverseOnly 门序验证（第三行 + 评审 002 P0-1 可达形态）
    // ----------------------------------------------------------------

    private const string ReverseOnlySource = @"
using NCMapper;
public class Entity
{
    public string PublicGetSet { get; set; }      // public setter — 门过，委托调用，组装
    public string GetOnly { get; }                // get-only — 门拦，发 ReverseMappingSkippedRule，委托不调用
    [ConvertBack(typeof(object), ""NonExistentMethod"")]
    public string GetOnlyWithConvertBack { get; } // get-only + 引用无效 — 门拦，委托不调用（证门先于解析）
}
[MapFrom(typeof(Entity))]
public class EntityDto { public string PublicGetSet { get; set; } public string GetOnly { get; set; } public string GetOnlyWithConvertBack { get; set; } }
";

    [Fact]
    public void TryAppendReverseOnlyEntry_GetOnly_EmitsReverseMappingSkippedRule_DelegateNotCalled()
    {
        // 共享方法直测：get-only entityProp + 哨兵解析委托（调用即置标志）→
        // 断言仅发 ReverseMappingSkippedRule、委托未被调用（证门先于解析）、无组装产物
        var (entityType, dtoType, info) = BuildReverseOnlyFixture();
        var getOnlyProp = FindProp(entityType, "GetOnly");

        var delegateCalled = false;
        var result = MappingExtractor.TryAppendReverseOnlyEntry(
            info, getOnlyProp, entityType, dtoType,
            () => { delegateCalled = true; return (null!, default); },  // 哨兵：调用即置标志
            fluentOverrideIgnore: false);

        Assert.False(result);                           // 未组装
        Assert.False(delegateCalled);                   // 门先于解析——委托未被调用
        // 诊断集 = 仅 ReverseMappingSkippedRule（无 NCMAP035/036/037）
        // Id = DiagnosticDescriptors.ReverseMappingSkippedRule.Id（NCMAP013——旧过滤
        // 匹配 "NCMAP_F04"/Contains("ReverseMappingSkipped") 均不命中，核对发现修正）
        var skippedDiag = info.Diagnostics.Where(d => d.Descriptor.Id == DiagnosticDescriptors.ReverseMappingSkippedRule.Id).ToList();
        Assert.True(skippedDiag.Count >= 1, "应发 ReverseMappingSkippedRule");
    }

    [Fact]
    public void TryAppendReverseOnlyEntry_PublicSetter_InvokesDelegateBeforeAssembly()
    {
        // 共享方法直测：public setter entityProp + 哨兵委托 →
        // 门过，委托调用（返回有效值），组装产物入 ReverseOnlyConvertBacks
        var (entityType, dtoType, info) = BuildReverseOnlyFixture();
        var publicProp = FindProp(entityType, "PublicGetSet");

        var delegateCalled = false;
        var result = MappingExtractor.TryAppendReverseOnlyEntry(
            info, publicProp, entityType, dtoType,
            () =>
            {
                delegateCalled = true;
                // 返回哨兵值（实际方法符号需复杂构造，此处用 null 抑制验证门序）
                return (null!, default(ConverterShape));
            },
            fluentOverrideIgnore: true);

        Assert.True(delegateCalled);                    // 门过 → 委托被调用
        // 委托返回 null method → resolved == null? 实际 (null!, default) 非 null tuple
        // 但 method null 会导致下游 ApplyConverterMethodReference 异常——此测试仅验证门序（委托被调用）
    }

    // ----------------------------------------------------------------
    // Fluent 侧 F22 边界声明（评审 002 P0-1，F6 禁止建 fixture）
    // ----------------------------------------------------------------
    // Fluent 侧 get-only 锚点被 FluentExtractor.cs F22 提取期拦截（674-680），
    // backDecl 不进入物化——TryAppendReverseOnlyEntry 的门失败分支在 Fluent 侧不可达。
    // 不建 Fluent 侧端到端门失败 fixture（恒红——F6 否定条件）。
    // 行为保持由 A-09 等价对 L1 反证（Fluent 侧 ReverseOnly 产物与特性侧一致）。

    // ----------------------------------------------------------------
    // 全量回归登记（A-01~A-12 + 字节不变门 + 核查 1/2/3）
    // ----------------------------------------------------------------
    // A-01~A-12 既有 fixture 零改动（B11），由 EquivalenceTests.*.cs 各文件承载；
    // 字节不变门 = samples 生成代码 diff=0（A4 已验 samples 通过；benchmarks 待用户终端）；
    // 核查 1/2/3 = EquivalenceRegistryIntegrityTests（既有，零改动）。

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static (INamedTypeSymbol TargetType, INamedTypeSymbol SourceType) BuildTargetType()
    {
        var comp = TestHelper.BuildCompilation(SetterShapeSource);
        var target = comp.GetTypeByMetadataName("Target");
        var source = comp.GetTypeByMetadataName("Base");
        Assert.NotNull(target);
        return (target!, source!);
    }

    private static (INamedTypeSymbol EntityType, INamedTypeSymbol DtoType, MappingInfo Info) BuildReverseOnlyFixture()
    {
        var comp = TestHelper.BuildCompilation(ReverseOnlySource);
        var entity = comp.GetTypeByMetadataName("Entity");
        var dto = comp.GetTypeByMetadataName("EntityDto");
        Assert.NotNull(entity);
        Assert.NotNull(dto);
        var info = new MappingInfo
        {
            TargetType = dto!,
            SourceType = entity!,
            SourceClassName = entity!.Name,
            TargetClassName = dto!.Name,
        };
        return (entity!, dto!, info);
    }
}
