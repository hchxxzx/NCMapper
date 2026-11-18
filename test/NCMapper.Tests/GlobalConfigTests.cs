using System.Collections.Concurrent;
using NCMapper;
using Xunit;

namespace NCMapper.Tests;

/// <summary>
/// 全局映射配置冻结语义测试集合 — 该集合内用例共享进程级冻结状态，禁止并行
/// </summary>
[CollectionDefinition("GlobalConfigState", DisableParallelization = true)]
public class GlobalConfigStateCollection { }

/// <summary>
/// 全局映射配置测试 — 两段式注册语义、并发冻结安全、冲突检测、越界校验、内建默认等价
/// <para>对应任务验收标准 3/4/5。</para>
/// </summary>
[Collection("GlobalConfigState")]
public class GlobalConfigTests : IDisposable
{
    /// <summary>
    /// 每个测试前重置全局配置冻结状态（测试隔离；生产场景禁止 ClearCache 修改冻结配置）
    /// </summary>
    public GlobalConfigTests()
    {
        Mapper.ClearCache();        // 迁移：Reset 不再清注册表，测试隔离需显式全清
        GlobalMapConfig.ResetForTests();
    }

    /// <summary>
    /// 类级清理 — 消除集合结束后冻结配置残留（进程级单例，
    /// 残留会污染并行/后续执行的未感知测试类，差异 #12 同类项）
    /// </summary>
    public void Dispose() { Mapper.ClearCache(); GlobalMapConfig.ResetForTests(); } // 迁移

    /// <summary>
    /// 验证：未注册任何配置时，冻结结果为内建默认（NamingStrategy.Default + 空清单，等价现状）
    /// <para>口径注：本 assembly 现可能含一个常驻注入的 <c>MapDefaults</c> 探针程序集
    /// （见 <c>GlobalMapConfigAssemblyAttributeTests</c>，动态程序集进程级不可卸载）。本例先于该类执行时走
    /// 内建默认字段；后于其执行时走反射回退取该常驻特性——该特性 11 项内容按构造与内建默认逐项等价，
    /// 故 11 项断言结果不变。断言未改，仅口径注记。</para>
    /// </summary>
    [Fact]
    public void NoRegistration_Freeze_ReturnsBuiltInDefault()
    {
        var view = Mapper.GetGlobalConfig();

        Assert.NotNull(view);
        Assert.Equal(NamingStrategy.Default, view!.NamingStrategy);
        Assert.Empty(view.Prefixes);
        Assert.Empty(view.Postfixes);
        Assert.Empty(view.IgnoreTypeFullNames);
        Assert.Empty(view.IgnoreNames);
        Assert.Empty(view.IgnoreNamePrefixes);
        Assert.Empty(view.IgnoreNameSuffixes);
        Assert.Empty(view.IgnoreTypeFullNamesToEntity);
        Assert.Empty(view.IgnoreNamesToEntity);
        Assert.Empty(view.IgnoreNamePrefixesToEntity);
        Assert.Empty(view.IgnoreNameSuffixesToEntity);
    }

    /// <summary>
    /// 验证：冻结并发安全 — 多线程首次同时触发冻结，无异常且结果一致
    /// <para>Lazy + Interlocked 双重保护，多线程首次竞争安全。</para>
    /// </summary>
    [Fact]
    public void Freeze_ConcurrentFirstUse_IsThreadSafe()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase, IgnoreNames = ["RowVersion"] },
            "ConcurrentAsm");

        var errors = new ConcurrentQueue<Exception>();
        var views = new ConcurrentBag<GlobalConfigView?>();

        Parallel.For(0, 16, _ =>
        {
            try
            {
                views.Add(Mapper.GetGlobalConfig());
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
            }
        });

        Assert.Empty(errors);
        Assert.All(views, v => Assert.Equal(NamingStrategy.SnakeCase, v!.NamingStrategy));
        Assert.All(views, v => Assert.Equal(["RowVersion"], v!.IgnoreNames));
    }

    /// <summary>
    /// Register / GetGlobalConfig(冻结) / ClearCache(重置) 混合并发 — 零异常 + 冻结读取可用（C8 补测）
    /// <para>断言形态（报告 §五 C8）：零异常 + 冻结结果幂等或 MapConfigurationException。
    /// 合法异常集合：<see cref="MapConfigurationException"/>（冻结后注册不同内容 / 冻结前多程序集冲突
    /// 均为既有语义，见 Register_DifferentContent_* 测试）；其余任何异常均视为并发缺陷。
    /// frozenViews 计数确保冻结读取确实发生（防测试空转）。</para>
    /// </summary>
    [Fact]
    public async Task Register_GetEffective_Reset_MixedConcurrent_NoUnexpectedException()
    {
        var errors = new ConcurrentQueue<Exception>();
        int frozenViews = 0;

        using var cts = new CancellationTokenSource();

        // 线程组1: 并发 Register（内容固定 → 冻结后幂等或合法冲突）
        var register = Task.Run(() =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    Mapper.RegisterGeneratedConfig(
                        new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase, IgnoreNames = ["RowVersion"] },
                        $"MixedAsm{i++ % 4}");
                }
                catch (MapConfigurationException) { /* 冻结冲突 = 合法既有语义 */ }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        }, TestContext.Current.CancellationToken);

        // 线程组2: 并发 GetGlobalConfig（触发/读取冻结视图）
        // do-while 保证至少执行一次读取——CI 低核高负载下线程可能在整个时窗内未获调度，
        // while 前置检查在 Cancel 后直接退出导致空转误报（AuditAcceptanceTests C07 D6 修复同型）。
        // 计数语义：记录"读取尝试"而非"非 null 视图"——reset 线程持续 ClearCache 会重置冻结
        // 状态使 view 合法为 null，防空转的本意是证明读取动作发生过，而非视图必然可读。
        var freeze = Task.Run(() =>
        {
            do
            {
                try
                {
                    _ = Mapper.GetGlobalConfig();
                    Interlocked.Increment(ref frozenViews);
                }
                catch (MapConfigurationException) { /* 冲突 = 合法既有语义 */ }
                catch (Exception ex) { errors.Enqueue(ex); }
            } while (!cts.Token.IsCancellationRequested);
        }, TestContext.Current.CancellationToken);

        // 线程组3: 并发 ClearCache（重置冻结状态，测试隔离语义）
        var reset = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try { Mapper.ClearCache(); }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        }, TestContext.Current.CancellationToken);

        Thread.Sleep(30);
        cts.Cancel();
        await Task.WhenAll(register, freeze, reset);

        // 零异常（MapConfigurationException 已按合法语义捕获，其余零容忍）
        Assert.Empty(errors);
        // 冻结读取确实发生（防空转）
        Assert.True(frozenViews > 0, "冻结读取空转（未获取任何视图）");
    }

    /// <summary>
    /// 验证：未冻结多次注册相同内容（含清单顺序差异）→ 冻结成功且视图清单有序规范化
    /// </summary>
    [Fact]
    public void Register_SameContentDifferentOrder_BeforeFreeze_FreezeSucceeds()
    {
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["a", "b"], Prefixes = ["Get"] }, "Asm1");
        // 清单顺序差异不误判冲突（v1.2 G4：幂等比较前清单排序规范化）
        Mapper.RegisterGeneratedConfig(
            new GeneratedMapConfig { IgnoreNames = ["b", "a"], Prefixes = ["Get"] }, "Asm2");

        var view = Mapper.GetGlobalConfig();

        Assert.NotNull(view);
        Assert.Equal(["a", "b"], view!.IgnoreNames);
        Assert.Equal(["Get"], view.Prefixes);
    }

    /// <summary>
    /// 验证：冻结后重复注册相同内容 → 幂等无操作（不抛异常，配置不变）
    /// </summary>
    [Fact]
    public void Register_SameContent_AfterFreeze_IsIdempotent()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "Asm1");
        _ = Mapper.GetGlobalConfig();

        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "Asm2");

        var view = Mapper.GetGlobalConfig();
        Assert.Equal(NamingStrategy.SnakeCase, view!.NamingStrategy);
    }

    /// <summary>
    /// 验证：冻结后注册不同内容 → 当场抛 MapConfigurationException 且消息含冲突双方程序集名
    /// </summary>
    [Fact]
    public void Register_DifferentContent_AfterFreeze_ThrowsImmediately()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "Asm1");
        _ = Mapper.GetGlobalConfig();

        var ex = Assert.Throws<MapConfigurationException>(() =>
            Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.Default }, "Asm2"));

        Assert.Contains("Asm1", ex.Message);
        Assert.Contains("Asm2", ex.Message);
    }

    /// <summary>
    /// 验证：冻结前多程序集注册不同内容 → 首次冻结抛 MapConfigurationException 且消息含全部冲突程序集名
    /// </summary>
    [Fact]
    public void Register_DifferentContent_BeforeFreeze_ThrowsOnFreezeWithAssemblyList()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["a"] }, "AsmA");
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["b"] }, "AsmB");

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.GetGlobalConfig());

        Assert.Contains("AsmA", ex.Message);
        Assert.Contains("AsmB", ex.Message);
    }

    /// <summary>
    /// 验证：NamingStrategy 越界值（99）冻结时抛 MapConfigurationException（防御手工注册越界值静默降级）
    /// </summary>
    [Fact]
    public void Register_InvalidNamingStrategy_FreezeThrows()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = 99 }, "Asm1");

        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.GetGlobalConfig());

        Assert.Contains("99", ex.Message);
    }

    /// <summary>
    /// 验证：GlobalConfigView 为 defensive copy — 冻结后修改外部原数组不影响视图
    /// </summary>
    [Fact]
    public void GetGlobalConfig_View_IsDefensiveCopy()
    {
        var names = new[] { "a", "b" };
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = names }, "Asm1");
        _ = Mapper.GetGlobalConfig();

        names[0] = "mutated";

        var view = Mapper.GetGlobalConfig();
        Assert.Equal("a", view!.IgnoreNames[0]);
        Assert.Equal("b", view.IgnoreNames[1]);
    }

    /// <summary>
    /// 验证：ClearCache 重置冻结状态后，可重新注册新配置并冻结（测试场景专用）
    /// </summary>
    [Fact]
    public void ClearCache_ResetsFreezeState_ForTestIsolation()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.SnakeCase }, "Asm1");
        Assert.Equal(NamingStrategy.SnakeCase, Mapper.GetGlobalConfig()!.NamingStrategy);

        // 迁移：ClearCache 不再清注册表——测试隔离的全清改用显式 API
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests();
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { NamingStrategy = (int)NamingStrategy.UpperSnakeCase }, "Asm2");

        Assert.Equal(NamingStrategy.UpperSnakeCase, Mapper.GetGlobalConfig()!.NamingStrategy);
    }

    // ===== S4：deep-immutability（v1.1 I1）=====

    /// <summary>
    /// 深不可变实体（独立类型对 — 运行时映射委托缓存于泛型静态字段，测试间不可复用类型对）
    /// </summary>
    private class DiEntity
    {
        public int Id { get; set; }

        public string Password { get; set; } = "";
    }

    /// <summary>
    /// 深不可变目标
    /// </summary>
    private class DiDto
    {
        public int Id { get; set; }

        public string Password { get; set; } = "";
    }

    /// <summary>
    /// 验证：冻结后修改注册源数组 → 判定行为不变（冻结时 Normalize 防御性复制，引用不外泄）
    /// <para>捕获"字段是引用类型且未 defensive copy"的实现缺陷（I1）。</para>
    /// </summary>
    [Fact]
    public void DeepImmutability_RegisterArrayMutatedAfterFreeze_Unaffected()
    {
        var names = new[] { "Password" };
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = names }, "S4Test");
        _ = Mapper.GetGlobalConfig(); // 冻结

        // 修改注册源数组（外部攻击面）——冻结内部应为独立副本
        names[0] = "mutated";

        var dto = Mapper.Map<DiEntity, DiDto>(new DiEntity { Id = 1, Password = "pwd" });

        // 原忽略清单仍生效：Password 留空（若实现未防御性复制，此处将映射出 "pwd"）
        Assert.Equal(1, dto!.Id);
        Assert.Equal("", dto.Password);
    }

    /// <summary>
    /// 验证：白盒修改视图内部数组 → 判定行为不变（视图与冻结实例为独立副本）
    /// <para>API 面（IReadOnlyList）已不可修改（"冻结后无修改入口（API 面不存在即验证）"）；
    /// 反射攻击面经 defensive copy 隔离。</para>
    /// </summary>
    [Fact]
    public void DeepImmutability_ViewArrayMutatedAfter_Unaffected()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["Password"] }, "S4Test");
        var view = Mapper.GetGlobalConfig()!; // 冻结

        // 白盒修改视图内部数组（record 属性 backing field；视图应为独立副本，不影响冻结实例）
        var field = typeof(GlobalConfigView).GetField("<IgnoreNames>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(view, new[] { "mutated" });

        var dto = Mapper.Map<DiEntity, DiDto>(new DiEntity { Id = 1, Password = "pwd" });

        // 原忽略清单仍生效（冻结实例持有独立副本）
        Assert.Equal(1, dto!.Id);
        Assert.Equal("", dto.Password);
    }
}
