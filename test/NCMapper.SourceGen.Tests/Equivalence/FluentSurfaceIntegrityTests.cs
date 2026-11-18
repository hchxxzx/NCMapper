using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// B-5 Fluent 公开表面 ↔ Fluent 出口锚点登记双向锁。
/// <para><b>契约</b>：反射枚举 5 个 Fluent 配置类（<c>FluentMappingConfig</c> / <c>FluentTypeConfig&lt;,&gt;</c> /
/// <c>MemberHead&lt;,,&gt;</c> / <c>MemberConfig&lt;,,&gt;</c> / <c>AfterIgnore&lt;,&gt;</c>）的公开实例方法，
/// 按 <c>(DeclaringType 简单名, MethodName)</c> 归集去重，与
/// <see cref="CapabilitySurfaceRegistry.FluentExitAnchors"/> 的同一投影双向比对——
/// 反射多出的对 = <c>UNREGISTERED</c> 红（新增终端方法 / 既有方法换类挂载未登记）；
/// 子表多出的对 = <c>STALE</c> 红（公开面已撤而登记未跟）。</para>
/// <para><b>枚举口径（Q1 PoC 实测复核）</b><c>BindingFlags.Public | Instance | FlattenHierarchy</c>
/// 开放泛型用类型定义直取（无需 MakeGenericType）；**按 <c>DeclaringType</c> 归集**（防 <c>MemberConfig</c>/
/// <c>AfterIgnore</c> 继承面重复计数）；排除 <c>DeclaringType == typeof(object)</c> 成员（实测每类恰多 4 名——
/// Equals/GetHashCode/GetType/ToString，必要排除项）与 <c>IsSpecialName</c> 访问器（当前表面实测 0 个，防御性保留）；
/// 构造器不计。<c>FlattenHierarchy</c> 对实例方法实测零增量（PoC [a]），保留属无害。</para>
/// <para><b>声明类简单名归一</b> = <c>Type.Name</c> 去 <c>`n</c> arity 后缀（四个开放泛型类的 <c>Type.Name</c>
/// 实为 <c>FluentTypeConfig`2</c> 等）——人工子表与反射投影必须走同一归一函数，否则双向锁首跑全表假红（001 建议-3）。</para>
/// <para><b>计数守门</b>pairs == 29 ∧ distinct 方法名 == 19 ∧ 类数 == 5（基准 = 表合计，
/// 非本类自证派生——护栏自身不得成为下一个 A-2，001）。</para>
/// <para><b>检出力边界（不夸大）</b>拦截新增公开方法名、既有方法换类挂载、锚点子表与公开面漂移、
/// 能力号失效；**不拦截**既有 (类, 方法) 对上的新增重载/参数形态扩展（同名对已存在）——该形态由
/// <c>PublicApiTests</c> + <c>PublicApi.NCMapper.approved.txt</c> 基线门与等价 fixture 承载。</para>
/// </summary>
public class FluentSurfaceIntegrityTests
{
    /// <summary>枚举对象（开放泛型以类型名字符串直取，PoC [c] 实测无需 MakeGenericType）。</summary>
    internal static readonly string[] FluentConfigTypeNames =
    {
        "NCMapper.FluentMappingConfig",
        "NCMapper.FluentTypeConfig`2",
        "NCMapper.MemberHead`3",
        "NCMapper.MemberConfig`3",
        "NCMapper.AfterIgnore`2",
    };

    // 计数守门基准（表合计：29 对 / 19 去重名 / 5 声明类）
    private const int ExpectedPairs = 29;
    private const int ExpectedDistinctMethodNames = 19;
    private const int ExpectedDeclaringTypeCount = 5;

    // ----------------------------------------------------------------
    // 真实核查
    // ----------------------------------------------------------------

    [Fact]
    public void FluentPublicSurface_MatchesExitAnchorSubtable_BothDirections()
    {
        var surface = EnumerateFluentSurface();
        var anchors = ProjectAnchors(CapabilitySurfaceRegistry.FluentExitAnchors);

        var failures = RunSurfaceLockCheck(surface, anchors);

        Assert.True(failures.Count == 0,
            "B-5 双向锁 FAILED（Fluent 公开面 ↔ Fluent 出口锚点登记）:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void FluentSurfaceAndAnchors_CountGate_MatchesBaseline()
    {
        var surface = EnumerateFluentSurface();
        var anchors = ProjectAnchors(CapabilitySurfaceRegistry.FluentExitAnchors);

        var failures = RunCountGate(surface, anchors, ExpectedPairs, ExpectedDistinctMethodNames, ExpectedDeclaringTypeCount);

        Assert.True(failures.Count == 0,
            "B-5 计数守门 FAILED:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void FluentExitAnchors_CapabilityIds_AllExistInEquivalenceRegistry()
    {
        var failures = RunCapabilityIdCheck(
            CapabilitySurfaceRegistry.FluentExitAnchors,
            EquivalenceRegistry.AllCapabilities.Select(c => c.Id).ToList());

        Assert.True(failures.Count == 0,
            "B-5 能力号有效性 FAILED:\n" + string.Join("\n", failures));
    }

    // ----------------------------------------------------------------
    // 自证三例（与真实核查共用同一纯函数——#38 同一代码路径自证）
    // ----------------------------------------------------------------

    /// <summary>自证 ①：锚点子表副本删一行 → 该对成为反射独有 → UNREGISTERED 红。</summary>
    [Fact]
    public void SelfProof1_AnchorRowRemoved_DetectedAsUnregistered()
    {
        var surface = EnumerateFluentSurface();
        var mutated = ProjectAnchors(CapabilitySurfaceRegistry.FluentExitAnchors)
            .Where(p => p != ("MemberHead", "From"))
            .ToList();

        var failures = RunSurfaceLockCheck(surface, mutated);

        Assert.Contains(failures, f => f.Contains("UNREGISTERED") && f.Contains("MemberHead.From"));
    }

    /// <summary>自证 ②：锚点子表副本加假对 → 反射独有缺失 → STALE 红。</summary>
    [Fact]
    public void SelfProof2_FakeAnchorPairAdded_DetectedAsStale()
    {
        var surface = EnumerateFluentSurface();
        var mutated = ProjectAnchors(CapabilitySurfaceRegistry.FluentExitAnchors);
        mutated.Add(("MemberHead", "NoSuchTerminal"));

        var failures = RunSurfaceLockCheck(surface, mutated);

        Assert.Contains(failures, f => f.Contains("STALE") && f.Contains("MemberHead.NoSuchTerminal"));
    }

    /// <summary>自证 ③：假能力号 → 有效性断言红（与真实核查共用 RunCapabilityIdCheck）。</summary>
    [Fact]
    public void SelfProof3_UnknownCapabilityId_Detected()
    {
        var mutated = CapabilitySurfaceRegistry.FluentExitAnchors
            .Select(r => r.MethodName == "For" ? r with { CapabilityIds = "A-99" } : r)
            .ToList();

        var failures = RunCapabilityIdCheck(mutated, EquivalenceRegistry.AllCapabilities.Select(c => c.Id).ToList());

        Assert.Contains(failures, f => f.Contains("A-99"));
    }

    // ----------------------------------------------------------------
    // 机械枚举（真实核查入口）
    // ----------------------------------------------------------------

    /// <summary>反射枚举 5 配置类公开实例方法 → (声明类简单名, 方法名) 去重对集（口径见类头注）。</summary>
    internal static List<(string DeclaringType, string MethodName)> EnumerateFluentSurface()
    {
        var assembly = typeof(NCMapper.FluentMappingConfig).Assembly;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        var pairs = new HashSet<(string DeclaringType, string MethodName)>();
        foreach (var typeName in FluentConfigTypeNames)
        {
            var type = assembly.GetType(typeName)
                ?? throw new InvalidOperationException($"Fluent 配置类解析失败: {typeName}（枚举源失效——B-5 锁面对象缺失）");

            foreach (var method in type.GetMethods(flags))
            {
                var declaring = method.DeclaringType;
                if (declaring == null || declaring == typeof(object)) continue; // Equals/GetHashCode/GetType/ToString
                if (method.IsSpecialName) continue;                            // 属性/索引器访问器（当前实测 0，防御性保留）
                pairs.Add((SimpleTypeName(declaring.Name), method.Name));
            }
        }

        return pairs.OrderBy(p => p.DeclaringType, StringComparer.Ordinal)
                    .ThenBy(p => p.MethodName, StringComparer.Ordinal)
                    .ToList();
    }

    /// <summary>声明类简单名归一：<c>Type.Name</c> 去 <c>`n</c> arity 后缀（人工表与反射投影同一函数）。</summary>
    internal static string SimpleTypeName(string typeName)
    {
        var i = typeName.IndexOf('`');
        return i < 0 ? typeName : typeName.Substring(0, i);
    }

    private static List<(string DeclaringType, string MethodName)> ProjectAnchors(
        IEnumerable<CapabilitySurfaceRegistry.FluentExitAnchorRow> rows)
        => rows.Select(r => (r.DeclaringType, r.MethodName))
               .OrderBy(p => p.DeclaringType, StringComparer.Ordinal)
               .ThenBy(p => p.MethodName, StringComparer.Ordinal)
               .ToList();

    // ----------------------------------------------------------------
    // 纯核查逻辑（真实核查与自证共用）
    // ----------------------------------------------------------------

    /// <summary>双向锁纯核查：反射 ⊖ 锚点 = UNREGISTERED；锚点 ⊖ 反射 = STALE。</summary>
    internal static List<string> RunSurfaceLockCheck(
        IEnumerable<(string DeclaringType, string MethodName)> surface,
        IEnumerable<(string DeclaringType, string MethodName)> anchors)
    {
        var surfaceSet = surface.Distinct().ToList();
        var anchorSet = anchors.Distinct().ToList();

        var failures = new List<string>();

        foreach (var pair in surfaceSet.Except(anchorSet).OrderBy(p => p.DeclaringType, StringComparer.Ordinal).ThenBy(p => p.MethodName, StringComparer.Ordinal))
            failures.Add($"UNREGISTERED: Fluent 公开面存在 ({pair.DeclaringType}.{pair.MethodName}) 但 CapabilitySurfaceRegistry.FluentExitAnchors 无登记行——新增终端方法或换类挂载须同批登记出口锚点（绑能力号）");

        foreach (var pair in anchorSet.Except(surfaceSet).OrderBy(p => p.DeclaringType, StringComparer.Ordinal).ThenBy(p => p.MethodName, StringComparer.Ordinal))
            failures.Add($"STALE: FluentExitAnchors 登记了 ({pair.DeclaringType}.{pair.MethodName}) 但 Fluent 公开面不存在——公开面已撤/改名，须同批撤登或改挂载");

        return failures;
    }

    /// <summary>计数守门纯核查（基准由调用方传入方案书定案值，不由输入派生）。</summary>
    internal static List<string> RunCountGate(
        IEnumerable<(string DeclaringType, string MethodName)> surface,
        IEnumerable<(string DeclaringType, string MethodName)> anchors,
        int expectedPairs,
        int expectedDistinctMethodNames,
        int expectedDeclaringTypeCount)
    {
        var failures = new List<string>();

        void Check(string label, int actual, int expected)
        {
            if (actual != expected)
                failures.Add($"计数守门: {label} = {actual} ≠ 基准 {expected}（表合计）——公开面或登记面已漂移，须同批对账，护栏自身不得成为下一个 A-2");
        }

        Check("反射面对数", surface.Distinct().Count(), expectedPairs);
        Check("锚点子表对数", anchors.Distinct().Count(), expectedPairs);
        Check("去重方法名数", surface.Select(p => p.MethodName).Distinct().Count(), expectedDistinctMethodNames);
        Check("声明类数", surface.Select(p => p.DeclaringType).Distinct().Count(), expectedDeclaringTypeCount);

        return failures;
    }

    /// <summary>能力号有效性纯核查：每行 CapabilityIds 以 "/" 拆分后逐个须存在于 EquivalenceRegistry。</summary>
    internal static List<string> RunCapabilityIdCheck(
        IEnumerable<CapabilitySurfaceRegistry.FluentExitAnchorRow> rows,
        ICollection<string> registeredCapabilityIds)
    {
        var registered = new HashSet<string>(registeredCapabilityIds, StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var row in rows)
        {
            var ids = row.CapabilityIds.Split('/')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            if (ids.Count == 0)
            {
                failures.Add($"能力号缺失: ({row.DeclaringType}.{row.MethodName}) CapabilityIds 为空——每个出口须可追溯到能力条目");
                continue;
            }

            foreach (var id in ids)
            {
                if (!registered.Contains(id))
                    failures.Add($"能力号失效: ({row.DeclaringType}.{row.MethodName}) 引用 '{id}' 不在 EquivalenceRegistry.AllCapabilities——须改挂既有编号或按能力登记流程立项");
            }
        }

        return failures;
    }
}
