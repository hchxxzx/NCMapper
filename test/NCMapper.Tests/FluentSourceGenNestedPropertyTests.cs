// ============================================================================
// P1（TB-20261127-01 §4 卡 P1）—— 源生成器产物核验（执行前 POC）
// ----------------------------------------------------------------------------
// 核验对象：真实 cfg.For<TSource,TTarget>() 经源生成器编译后，
//           RuntimeMapperMetadata.Properties 是否已包含嵌套属性条目。
// 用途：据此确定批次 2（卡 C2）的修法落点——运行时补递归例外 / 注册侧补发射逻辑。
//
// ★ 工程选择（任务书写死，不得改）：本文件必须落在 test/NCMapper.Tests/。
//   实测依据：NCMapper.IntegrationTests.csproj 只引 4 个库项目，未把 NCMapper.SourceGen
//   作 Analyzer 引用 → 放那里等于没有源生成器，测不到真实产物。
//   只有 NCMapper.Tests.csproj 含
//     <ProjectReference Include="..\..\src\NCMapper.SourceGen\NCMapper.SourceGen.csproj"
//                       OutputItemType="Analyzer" />
//   同工程还声明 <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>，
//   产物落 obj/Generated/NCMapper.SourceGen/NCMapper.SourceGen.RuntimeSnapshotGenerator/。
//
// 取数口径（关键）：读的是生成代码 [ModuleInitializer]（RuntimeSnapshotMetadataInitializer）
//   注册进 RuntimeSnapshotStore 的**真实产物条目**，不是手工构造的 meta。
//   同进程内其他用例会 Clear 注册表，故本类每条用例开头都重跑同一登记入口
//   （RuntimeSnapshotMetadataInitializer.Initialize），把注册表恢复到「模块加载态」，
//   避免跨用例串扰（沿经验库 #60/#65/#366 隔离范式）。
//
// 已知观测量（本卡实测，供后续卡对照）：
//   · Fluent 通道：Site→SiteNestedDto 的 Properties 含 3 条，其中一条 Target=Dept
//     （SourceName=Dept、SourcePropertyTypeName=Dept 实体类型、slot TypeName=DeptDto）
//     —— 即「嵌套属性条目」由自动匹配（同名同形）建立，注册侧无需补发射。
//   · 特性通道：[MapFrom(typeof(AttrSite))] 同形状 DTO 的 Properties 同样含 Dept 条目。
//   · 属性级 miss 形态（目标属性在 AllTargetSlots 但不在 Properties）：单表链
//     ProjectionExpressionFactory.cs:1199-1214 会进「嵌套/集合 DTO 属性除外」递归入口，
//     Compose 腿 ComposeProjectionKernel.cs:300-308 直接跳过 → 缺陷④的落点在运行时。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// P1 源生成器产物核验：真实 <c>cfg.For&lt;TSource,TTarget&gt;()</c> 产物里
/// <see cref="RuntimeMapperMetadata.Properties"/> 是否含嵌套属性条目。
/// <para>类名匹配 verify 命令的 <c>--filter-class "*FluentSourceGenNestedProperty*"</c>。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class FluentSourceGenNestedPropertyTests
{
    // ========================================================================
    // 夹具 ①：Fluent 通道，主对含嵌套属性（形状对齐任务书 §4 卡 P1：cfg.For<Site, SiteNestedDto>）
    // ========================================================================

    public class Site
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Dept Dept { get; set; } = new();
    }

    public class Dept
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
    }

    public class SiteNestedDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DeptDto Dept { get; set; } = new();
    }

    public class DeptDto
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// Fluent 声明：主对只显式声明标量成员 <c>Name</c>（嵌套 Dept **不显式声明**——
    /// 这正是要核验的形态：嵌套条目是否仍出现在 Properties 里）；嵌套对自成一条声明。
    /// </summary>
    [MapperConfig]
    internal class SiteFluentMapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<Site, SiteNestedDto>()
               .Member(d => d.Name).From(s => s.Name);

            cfg.For<Dept, DeptDto>()
               .Member(d => d.Label).From(s => s.Label);
        }
    }

    // ========================================================================
    // 夹具 ②：特性通道，同形状 DTO（验收 3 的对照面）
    // ========================================================================

    public class AttrSite
    {
        public int Id { get; set; }
        public AttrDept Dept { get; set; } = new();
    }

    public class AttrDept
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
    }

    [MapFrom(typeof(AttrSite))]
    public class AttrSiteDto
    {
        public int Id { get; set; }
        public AttrDeptDto Dept { get; set; } = new();
    }

    public class AttrDeptDto
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
    }

    // ========================================================================
    // 夹具 ③：U 组（§6A.2 U 组）——cfg.For 声明后可读到 Properties
    // ========================================================================

    public class UCfgForSource
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    public class UCfgForDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    [MapperConfig]
    internal class UCfgForMapper
    {
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<UCfgForSource, UCfgForDto>()
               .Member(d => d.Title).From(s => s.Title);
        }
    }

    // ========================================================================
    // 夹具 ④：负向——不声明任何 cfg.For、也无 [MapFrom]
    // ========================================================================

    public class UndeclearedSource
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class UndeclearedDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    // ========================================================================
    // 基础设施
    // ========================================================================

    /// <summary>
    /// 把注册表恢复到「生成产物模块加载态」：先清空（隔离其他用例遗留的手工注册），
    /// 再跑生成器产物登记入口——与编译产物里的 [ModuleInitializer] 是同一入口、同一批条目。
    /// </summary>
    private static void ResetToGeneratedState()
    {
        RuntimeSnapshotStore.Clear();
        RuntimeSnapshotMetadataInitializer.Initialize();
    }

    /// <summary>
    /// 取某 (源, 目标) 对的真实产物条目。
    /// 未命中时**不**返回空 Properties 兜底，而是显式报「未找到映射元数据」
    /// （验收 4 的负向语义：不得静默返回空 Properties 列表）。
    /// </summary>
    private static RuntimeMapperMetadata RequireGeneratedPair(Type source, Type target)
    {
        var meta = RuntimeSnapshotStore.FindByPair(source, target);
        if (meta == null)
        {
            throw new InvalidOperationException(
                $"未找到映射元数据：({source.FullName}, {target.FullName}) 没有生成产物条目。" +
                "若本异常出现在正向用例上，说明源生成器未运行" +
                "（NCMapper.Tests.csproj 的 SourceGen Analyzer 引用缺失或被总开关关闭）");
        }
        return meta;
    }

    /// <summary>Properties 的目标属性名列表（打印与断言共用同一份投影）。</summary>
    private static List<string> TargetPropertyNames(RuntimeMapperMetadata meta)
        => meta.Properties
               .Select(p => p.Target?.Name ?? "<null>")
               .ToList();

    /// <summary>
    /// 打印 Properties 的目标属性名列表并返回。
    /// <para>MTP（Microsoft.Testing.Platform）下 <c>dotnet test</c> 会吞掉通过用例的 stdout，
    /// 故用 <c>--show-live-output on</c> 复跑展示（见交付说明 evidence）。</para>
    /// </summary>
    private static List<string> ReportProperties(RuntimeMapperMetadata meta, string label)
    {
        var names = TargetPropertyNames(meta);
        var line = $"[P1] {label} Properties 目标属性名列表（{names.Count} 条）：{string.Join(", ", names)}";

        Console.WriteLine(line);
        foreach (var n in names) Console.WriteLine($"[P1]   - {n}");

        return names;
    }

    // ========================================================================
    // 验收 1 —— NestedProperty_ReportedInProperties
    // ========================================================================

    /// <summary>
    /// 验收 1：真实 <c>cfg.For&lt;Site, SiteNestedDto&gt;()</c> 产物里，Properties 含嵌套属性条目
    /// （Target=<c>Dept</c>），并打印 Properties 的目标属性名列表。
    /// <para>本用例是 P1 结论的主依据：Properties 已含嵌套 ⇒ C2 落点=运行时（注册侧无需补发射）。</para>
    /// </summary>
    [Fact]
    public void NestedProperty_ReportedInProperties()
    {
        ResetToGeneratedState();

        var meta = RequireGeneratedPair(typeof(Site), typeof(SiteNestedDto));

        // 前置：条目来自生成产物且走 Fluent 通道
        Assert.Equal(RuntimeMappingChannel.Fluent, meta.Channel);
        Assert.True(meta.IsFluentForward, "Fluent 正向条目（IsFluentForward=true）");

        // 核心断言：Properties 非空，且含嵌套属性的目标条目
        Assert.NotEmpty(meta.Properties);
        var names = ReportProperties(meta, "Fluent cfg.For<Site, SiteNestedDto>");

        Assert.Contains("Dept", names);

        // 条目形态核对：嵌套条目的目标 slot 类型为 DTO、源侧类型为实体
        var deptPm = meta.Properties.Single(p => p.Target?.Name == "Dept");
        Assert.Equal(nameof(DeptDto), deptPm.Target!.TypeName?.Split('.').Last());
        Assert.Equal("Dept", deptPm.SourceName);
        Assert.Contains(nameof(Dept), deptPm.SourcePropertyTypeName ?? "");

        // 嵌套对自身也作为独立 (S,T) 对注册（递归入口的另一半前提）
        var nestedMeta = RequireGeneratedPair(typeof(Dept), typeof(DeptDto));
        Assert.NotEmpty(nestedMeta.Properties);
        Assert.Contains("Label", nestedMeta.Properties.Select(p => p.Target?.Name));
    }

    // ========================================================================
    // 验收 3 —— AttributeChannel_NestedInProperties
    // ========================================================================

    /// <summary>
    /// 验收 3：对照 <c>[MapFrom]</c> 特性通道同形状 DTO，断言 Properties 含 <c>Dept</c>
    /// 且条目数 ≥ 1。
    /// </summary>
    [Fact]
    public void AttributeChannel_NestedInProperties()
    {
        ResetToGeneratedState();

        var meta = RequireGeneratedPair(typeof(AttrSite), typeof(AttrSiteDto));

        // 特性通道标识
        Assert.Equal(RuntimeMappingChannel.MapFrom, meta.Channel);
        Assert.False(meta.IsFluentForward);

        var names = ReportProperties(meta, "[MapFrom(typeof(AttrSite))] 特性通道");

        // 验收指定：含 Dept 且条目数 ≥ 1
        Assert.True(names.Count >= 1, $"Properties 条目数须 ≥ 1，实为 {names.Count}");
        Assert.Contains("Dept", names);

        // 嵌套条目的目标 slot 为 DTO 形态（与 Fluent 通道同形状，供 N/S 组对照）
        var deptPm = meta.Properties.Single(p => p.Target?.Name == "Dept");
        Assert.Equal(nameof(AttrDeptDto), deptPm.Target!.TypeName?.Split('.').Last());
    }

    // ========================================================================
    // 验收 4（负向）—— Undecleared_ReportsNoMetadata
    // ========================================================================

    /// <summary>
    /// 验收 4（负向）：不声明任何 <c>cfg.For</c> 且无 <c>[MapFrom]</c> 的 DTO，
    /// 查询必须显式报「未找到映射元数据」，
    /// **不得**静默返回空 <see cref="RuntimeMapperMetadata.Properties"/> 列表。
    /// </summary>
    [Fact]
    public void Undecleared_ReportsNoMetadata()
    {
        ResetToGeneratedState();

        // ① 按对查询必须 miss（不是"命中但 Properties 空"）
        Assert.Null(RuntimeSnapshotStore.FindByPair(typeof(UndeclearedSource), typeof(UndeclearedDto)));

        // ② 按目标查询也必须无条目（防"空数组兜底"）
        Assert.Empty(RuntimeSnapshotStore.FindByTarget(typeof(UndeclearedDto)));
        Assert.False(RuntimeSnapshotStore.IsRegisteredDto(typeof(UndeclearedDto)));

        // ③ 统一门面也要 miss（防门面静默给空视图）
        Assert.False(RuntimeSnapshotStore.TryGetMappingMeta(typeof(UndeclearedDto), out _));

        // ④ 取数入口必须抛「未找到映射元数据」，而不是返回空 Properties
        var ex = Assert.Throws<InvalidOperationException>(
            () => RequireGeneratedPair(typeof(UndeclearedSource), typeof(UndeclearedDto)));
        Assert.Contains("未找到映射元数据", ex.Message);
        Console.WriteLine($"[P1] 负向确认：{ex.Message}");
    }

    // ========================================================================
    // 验收 5（回归）—— U_CfgFor_ReportsProperties（§6A.2 U 组）
    // ========================================================================

    /// <summary>
    /// 验收 5（回归，§6A.2 U 组）：<c>cfg.For</c> 声明后能读到 Properties。
    /// <para>★ 若源生成器未运行则判失败，不得跳过——本用例不含任何 Skip 分支：
    /// 找不到产物条目会直接 Assert 失败（RequireGeneratedPair 抛异常即红）。</para>
    /// </summary>
    [Fact]
    public void U_CfgFor_ReportsProperties()
    {
        ResetToGeneratedState();

        // 源生成器必须在跑：产物文件存在（编译期 EmitCompilerGeneratedFiles 落 obj/Generated）
        var generatedRoot = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "obj", "Generated",
            "NCMapper.SourceGen", "NCMapper.SourceGen.RuntimeSnapshotGenerator");
        var generatedAll = Path.GetFullPath(generatedRoot);
        var hasMetadataFile = Directory.Exists(generatedAll)
            && Directory.EnumerateFiles(generatedAll, "NCMapper_RuntimeSnapshotMetadata.g.cs",
                   SearchOption.AllDirectories).Any();
        Assert.True(hasMetadataFile,
            $"源生成器未运行：未找到产物文件（{generatedAll}\\**\\NCMapper_RuntimeSnapshotMetadata.g.cs）。" +
            "本用例按任务书要求判失败，不得跳过");

        var meta = RequireGeneratedPair(typeof(UCfgForSource), typeof(UCfgForDto));

        // cfg.For 声明后能读到 Properties，且含声明成员
        Assert.True(meta.Properties.Length > 0,
            "cfg.For 声明后 Properties 必须有条目（U 组回归要点）");
        var names = ReportProperties(meta, "U 组 cfg.For<UCfgForSource, UCfgForDto>");
        Assert.Contains("Title", names);
    }
}
