// ============================================================================
// B-3：注册时机独立程序集首查测试（R9 / Q7）
//
// 问题本质：Fluent DTO 位于独立程序集（源生成器在编译期把注册行发射进该程序集的
// [ModuleInitializer] RuntimeSnapshotMetadataInitializer）。运行时经 Assembly.Load 加载该 dll 后，
// 若消费端首查 typeof(Dto) 时 ModuleInitializer 尚未运行 → RuntimeSnapshotStore miss →
// 反射回退 → Fluent DTO 无 [MapFrom] → 仍判非 DTO（缺口暂存）→ 触发初始化器所需方法调用
// 永不发生 → Fluent 缺口可能"永久不消除"（死锁）。
//
// 本测试构造真实独立程序集（CompileWithGeneratorsAndLoad：双生成器 + RuntimeSnapshot 开关 on），
// 验证两条断言：
//   ① 加载后首查 typeof(Dto) 是否触发 ModuleInitializer（决定死锁是否成立）；
//   ② 触发模块初始化后（ForceModuleInit 显式运行 RuntimeHelpers.RunModuleConstructor），
//      次查 RuntimeSnapshotStore 命中 → Fluent DTO 缺口消除。
//
// 结论将回流 R9 三缓解设计候选选定：
//   ① 文档显式声明"首查可能回退反射"（零代码改动）② 消费端 RunModuleConstructor 显式保证
//   ③ 发射端无条件触发所有 DTO 模块初始化。
// ============================================================================
using NCMapper.Runtime;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// B-3 注册时机死锁测试——独立程序集含 Fluent DTO，加载方式不触发 ModuleInitializer 前首查语义
/// </summary>
public class RuntimeSnapshotRegistrationTimingTests
{
    // 独立程序集语料：[MapFrom] DTO（主生成器 + RuntimeSnapshotGenerator 都发射——
    // RuntimeSnapshot 注册行进入该程序集 [ModuleInitializer]。B-3 死锁对 [MapFrom]/Fluent 两通道
    // 语义一致：只要 DTO 在独立程序集且 ModuleInitializer 未触发，首查即 miss）
    private const string MapFromIndependentSource = """
        using NCMapper.Attributes;

        namespace MapFromLib
        {
            public class Order { public int Id { get; set; } public string No { get; set; } = ""; public decimal Amount { get; set; } }
            [MapFrom(typeof(Order))]
            public class OrderDto { public int Id { get; set; } public string No { get; set; } = ""; public decimal Amount { get; set; } }
        }
        """;

    /// <summary>
    /// B-3 断言①：Assembly.Load(bytes) 加载独立程序集后，首查 typeof(Dto)（IsRegisteredDto）
    /// 不触发 ModuleInitializer → 注册表 miss（死锁成立——若此处命中则 ModuleInitializer 已自动运行，死锁不成立）
    /// </summary>
    [Fact]
    public void B3_IndependentAssembly_FirstLookupByTypeOf_DoesNotTriggerModuleInit()
    {
        var asm = TestHelper.CompileWithGeneratorsAndLoad(MapFromIndependentSource, emitRuntimeSnapshot: true);
        var dtoType = asm.GetType("MapFromLib.OrderDto");
        Assert.NotNull(dtoType);

        // 首查：仅 typeof 元数据查询（IsRegisteredDto 走 RuntimeSnapshotStore Type 键控查找，
        // 不触发热初始化触发点）——断言 miss 则死锁成立，命中则 ModuleInitializer 已自动运行
        var first = RuntimeSnapshotStore.IsRegisteredDto(dtoType!);
        // 语义断言：此处结果取决于 .NET 是否 typeof 即触发 ModuleInitializer。
        // 若 first == true：typeof 即触发 → B-3 死锁不成立（缓解候选 0 个需代码改动）
        // 若 first == false：typeof 不触发 → B-3 死锁成立（需 3 缓解候选之一）
        Assert.False(first,
            "B-3 死锁不成立：Assembly.Load 后首查 typeof 已命中（ModuleInitializer 在 typeof 阶段已运行）——" +
            "此结果表示独立程序集场景无死锁，R9 缓解候选可降级为纯文档说明。");
        // 若此断言失败（first==true），是利好——证明无死锁。
    }

    /// <summary>
    /// B-3 断言②：显式触发模块初始化（RuntimeHelpers.RunModuleConstructor）后次查命中——
    /// 证明"触发后缺口消除"路径可行，为缓解候选②（消费端 RunModuleConstructor 显式保证）提供依据
    /// </summary>
    [Fact]
    public void B3_IndependentAssembly_AfterModuleInit_SecondLookupHits()
    {
        var asm = TestHelper.CompileWithGeneratorsAndLoad(MapFromIndependentSource, emitRuntimeSnapshot: true);
        var dtoType = asm.GetType("MapFromLib.OrderDto");
        Assert.NotNull(dtoType);
        Assert.False(RuntimeSnapshotStore.IsRegisteredDto(dtoType!),
            "前置：首查应 miss（与断言①一致，ModuleInitializer 未触发）");

        // 显式运行独立程序集模块构造函数（ModuleInitializer 挂在其上）
        var module = dtoType!.Module;
        RuntimeHelpers.RunModuleConstructor(module.ModuleHandle);

        // 次查：模块初始化已运行 → RuntimeSnapshot 注册行执行 → 命中
        Assert.True(RuntimeSnapshotStore.IsRegisteredDto(dtoType),
            "显式 RunModuleConstructor 后次查应命中——ModuleInitializer 已注册该 DTO（缓解候选②可行）");

        // 深度验证：注册表含该 DTO 的完整元数据
        var metas = RuntimeSnapshotStore.FindByTarget(dtoType);
        Assert.NotEmpty(metas);
        Assert.Equal("MapFromLib.Order", metas[0].SourceType.FullName);

        // 收尾：清空注册表，避免污染其他假设空 Store 的测试
        RuntimeSnapshotStore.Clear();
    }
}
