// ============================================================================
// §6A.1 落盘：U 组（归属卡 P1）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// U（3 例）真实 cfg.For 产物核验 —— P1 卡的结论面
//
// ★ 工程选择（写死，不得改）：本文件必须落在 test/NCMapper.Tests/。
//   实测依据：NCMapper.IntegrationTests.csproj 只引 4 个库项目，**未把 NCMapper.SourceGen
//   作 Analyzer 引用** → 放那里等于没有源生成器，测不到真实产物。
//   只有 NCMapper.Tests.csproj 含
//     <ProjectReference Include="..\..\src\NCMapper.SourceGen\NCMapper.SourceGen.csproj"
//                       OutputItemType="Analyzer" />
//   且声明 <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>。
//
// ★ 与 P1 卡 FluentSourceGenNestedPropertyTests.cs 的关系（不重复造轮子）：
//   P1 卡已交付 4 条用例（NestedProperty_ReportedInProperties / AttributeChannel_NestedInProperties /
//   Undecleared_ReportsNoMetadata / U_CfgFor_ReportsProperties），其中 U_CfgFor_ReportsProperties
//   就是 §6A.2 的 U 组用例名。本文件**不重复实现**该用例，而是：
//     ① 把 U 组要求的「cfg.For 声明后能读到 Properties」做成**不再依赖单条用例**的独立回归；
//     ② 补 U1/U2/U3 三条 POC 现象（真实 cfg.For 后注册表是否命中、触发 Map 后再查、核心程序集类型面），
//        这三条在 P1 卡里只出现在结论文档，没有可执行的回归锚。
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
/// POC 回归 U 组 —— 真实 <c>cfg.For</c> 的源生成器产物核验。
/// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>（本工程）。</para>
/// <para>取数口径：读生成代码 <c>[ModuleInitializer]</c>（<c>RuntimeSnapshotMetadataInitializer</c>）
/// 注册进 <see cref="RuntimeSnapshotStore"/> 的**真实产物条目**，不是手工构造的 meta。</para>
/// <para>★ 隔离纪律：同进程内其它用例会 Clear 注册表，故每条用例开头都重跑同一登记入口，
/// 把注册表恢复到「模块加载态」（沿经验库 #60/#65/#366 隔离范式）。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class FreeSqlComposePocRegressionUTests
{
    // ================================================================
    // 夹具：真实 cfg.For 声明（经源生成器编译）
    // ================================================================

    /// <summary>U 组的源实体（站点）</summary>
    public class USite
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string Name { get; set; } = "";
        /// <summary>部门（嵌套）</summary>
        public UDept Dept { get; set; } = new();
    }

    /// <summary>U 组的部门实体</summary>
    public class UDept
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string Label { get; set; } = "";
    }

    /// <summary>U 组目标 DTO —— 含嵌套 Dept</summary>
    public class USiteNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string Name { get; set; } = "";
        /// <summary>部门（嵌套 DTO）</summary>
        public UDeptDto Dept { get; set; } = new();
    }

    /// <summary>U 组嵌套部门 DTO</summary>
    public class UDeptDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string Label { get; set; } = "";
    }

    /// <summary>U 组的 Fluent 声明 —— 主对只声明标量，嵌套对自成一条（真实 cfg.For 形态）</summary>
    [MapperConfig]
    internal class USiteFluentMapper
    {
        /// <summary>Fluent 配置入口（源生成器据此发射快照条目）</summary>
        /// <param name="cfg">Fluent 配置</param>
        public static void Configure(FluentMappingConfig cfg)
        {
            cfg.For<USite, USiteNestedDto>()
               .Member(d => d.Name).From(s => s.Name);

            cfg.For<UDept, UDeptDto>()
               .Member(d => d.Label).From(s => s.Label);
        }
    }

    // ================================================================
    // U 组用例
    // ================================================================

    /// <summary>
    /// U 组（U1/U2）：真实 <c>cfg.For</c> 声明后，注册表能按 (Source,Target) 命中条目，
    /// 且触发一次映射后再查仍然命中（注册不依赖「是否已映射过」）。
    /// <para>POC U1：<c>真实 cfg.For 后注册表主对 → 未命中</c>（POC 结论：当时缺源生成链路）；
    /// U2：<c>触发一次 Mapper.Map 后再查注册表 → 未命中 | DTO.Dept=&lt;null&gt;</c>。</para>
    /// <para><b>原 POC U1/U2：未命中（无生成产物）→ 修复后：命中，且 Properties 含嵌套 Dept</b>。
    /// 这是 P1 卡（t18）交付的源生成器产物核验结论的执行化锚点。</para>
    /// </summary>
    [Fact]
    public void U1_RealCfgFor_ProducesRegisteredPair()
    {
        ResetToGeneratedState();

        // U1：真实 cfg.For 产物必须命中注册表
        var meta = RuntimeSnapshotStore.FindByPair(typeof(USite), typeof(USiteNestedDto));
        Assert.NotNull(meta);
        Assert.Equal(RuntimeMappingChannel.Fluent, meta!.Channel);
        Assert.True(meta.IsFluentForward, "Fluent 正向条目（IsFluentForward=true）");
        Assert.NotEmpty(meta.Properties);

        // U2：触发一次映射后再查 —— 仍命中（注册来自生成产物，不依赖映射副作用）
        var mapped = Mapper.Map<USite, USiteNestedDto>(new USite
        {
            Id = 1,
            Name = "S1",
            Dept = new UDept { Id = 10, Label = "IT" },
        });
        Assert.NotNull(mapped);
        Assert.Equal("S1", mapped!.Name);

        var afterMap = RuntimeSnapshotStore.FindByPair(typeof(USite), typeof(USiteNestedDto));
        Assert.NotNull(afterMap);
        Assert.Equal(meta.Properties.Length, afterMap!.Properties.Length);
    }

    /// <summary>
    /// U 组（U3）：核心程序集里必须存在 Fluent 配置类型（源生成链路的入口面）。
    /// <para>POC U3：<c>核心程序集中 Fluent 相关类型 → NCMapper.FluentMappingConfig | NCMapper.FluentTypeConfig`2</c>。</para>
    /// <para>本用例锁住这两个类型不得被改名/删除（它们是 <c>cfg.For&lt;S,T&gt;()</c> 的 API 面）。</para>
    /// </summary>
    [Fact]
    public void U3_FluentConfigTypesPresent()
    {
        var coreAssembly = typeof(Mapper).Assembly;

        var fluentConfigType = coreAssembly.GetType("NCMapper.FluentMappingConfig");
        Assert.NotNull(fluentConfigType);

        // cfg.For<TSource,TTarget>() 的返回类型是 FluentTypeConfig`2
        var fluentTypeConfig = coreAssembly.GetType("NCMapper.FluentTypeConfig`2");
        Assert.NotNull(fluentTypeConfig);

        // For 方法必须存在于 FluentMappingConfig 上（泛型 2 参形态）
        var forMethods = fluentConfigType!.GetMethods()
            .Where(m => m.Name == "For" && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 2)
            .ToList();
        Assert.NotEmpty(forMethods);
    }

    /// <summary>
    /// U 组（对 P1 卡 U_CfgFor_ReportsProperties 的**交叉复核**）：
    /// 同一结论在 §6A 的 U 组口径下再验一次 —— <c>cfg.For</c> 声明后 Properties 可读且含嵌套条目。
    /// <para>★ 为什么不直接复写 P1 卡的用例：§6A.4 第 5 条要求「每条用例必须在 deliverables 中有对应文件」，
    /// 而 U_CfgFor_ReportsProperties 的文件是 P1 卡的 <c>FluentSourceGenNestedPropertyTests.cs</c>
    /// （在 P1 卡 deliverables 内）。本用例与它**同结论不同断言面**：
    /// P1 卡断言「能读到 Properties」，本用例额外断言「嵌套条目 Dept 也在」（U 组在 §6A.2 的断言要点）。</para>
    /// <para>若源生成器未运行则判失败，不得跳过（与 P1 卡同纪律）。</para>
    /// </summary>
    [Fact]
    public void U_CfgFor_ReportsNestedProperties()
    {
        ResetToGeneratedState();

        // 源生成器必须在跑：产物文件存在（编译期 EmitCompilerGeneratedFiles 落 obj/Generated）
        var generatedRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "obj", "Generated",
            "NCMapper.SourceGen", "NCMapper.SourceGen.RuntimeSnapshotGenerator"));
        var hasMetadataFile = Directory.Exists(generatedRoot)
            && Directory.EnumerateFiles(generatedRoot, "NCMapper_RuntimeSnapshotMetadata.g.cs",
                   SearchOption.AllDirectories).Any();
        Assert.True(hasMetadataFile,
            $"源生成器未运行：未找到产物文件（{generatedRoot}\\**\\NCMapper_RuntimeSnapshotMetadata.g.cs）。" +
            "本用例按任务书要求判失败，不得跳过");

        var meta = RuntimeSnapshotStore.FindByPair(typeof(USite), typeof(USiteNestedDto));
        Assert.NotNull(meta);

        // cfg.For 声明后 Properties 非空
        Assert.True(meta!.Properties.Length > 0, "cfg.For 声明后 Properties 必须有条目");

        var names = meta.Properties.Select(p => p.Target?.Name ?? "<null>").ToList();
        Assert.Contains("Name", names);       // 显式声明的标量

        // ★ U 组的断言要点：嵌套条目也在（同名同形的自动匹配建立）
        Assert.Contains("Dept", names);

        // 嵌套条目的形态：目标 slot 为 DTO、源侧为实体类型
        var deptPm = meta.Properties.Single(p => p.Target?.Name == "Dept");
        Assert.Contains(nameof(UDeptDto), deptPm.Target!.TypeName ?? "");
        Assert.Contains(nameof(UDept), deptPm.SourcePropertyTypeName ?? "");
    }

    // ================================================================
    // 隔离基础设施
    // ================================================================

    /// <summary>
    /// 把注册表恢复到「生成产物模块加载态」：先清空（隔离其它用例遗留的手工注册），
    /// 再跑生成器产物登记入口 —— 与编译产物里的 <c>[ModuleInitializer]</c> 同一入口、同一批条目。
    /// </summary>
    private static void ResetToGeneratedState()
    {
        RuntimeSnapshotStore.Clear();
        RuntimeSnapshotMetadataInitializer.Initialize();
    }
}
