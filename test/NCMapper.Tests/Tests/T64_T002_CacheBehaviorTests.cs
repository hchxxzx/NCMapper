// ============================================================================
// T64 / B5-5 Batch-2 — 缓存与并发红测
//（A1-12 Compose 动态键 / A3-05 占位缓存 / A2-04 多源键 / A2-08 清理异常可见性）
//
// 缺陷承载（审计 20260917-001 -B5-5）：
//   A1-12  Compose 更新委托缓存键含 source.GetType()（Mapper.Compose.cs:179-183）——
//          动态派生类型键空间无界，与 6 处「动态类型直算不入缓存」策略未对齐；
//   A3-05  record 占位入 CycleCache 后 CreateInstance 返 null 未清缓存
//          （DeepCopyProviderBase.cs:225-232）→ 同 ctx 内后续同 source 引用返回
//          未初始化空壳；
//   A2-04  多源导航收集缓存键只承载前 3 源（NavigationTreeCollector.cs:66-68）——
//          Length>3 键碰撞 + Length<2 越界；
//   A2-08  清理委托异常仅 System.Diagnostics.Trace 无 MapTracer 留痕、无计数
//          （CacheRegistry.cs:62-66，X3 弱信号收窄：N-05 已隔离主害）。
// ============================================================================

using System.Reflection;
using System.Reflection.Emit;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Internal;
using Xunit;

namespace NCMapper.Tests.Tests;

[Collection("GlobalConfigState")]
public class T64_T002_CacheBehaviorTests
{
    public T64_T002_CacheBehaviorTests()
    {
        Mapper.ClearCache();
    }

    // ============================== A1-12 ==============================

    /// <summary>
    /// A1-12 红测：动态派生类型（Reflection.Emit）循环参与 Compose 更新路径——
    /// 现状 s_composeUpdateDelegateCache 键含 source.GetType()（动态键）逐条缓存
    /// （红：条目数增长 N）；修复后动态类型键直算不入缓存（条目数有界）。
    /// 观测经反射读取私有缓存字段（不新增 API 面）。
    /// </summary>
    [Fact]
    public void A1_12_Compose更新委托缓存_动态源类型键不增长()
    {
        Mapper.ClearCache();
        var before = ComposeUpdateCacheEntryCount();

        const int pairCount = 20;
        for (var i = 0; i < pairCount; i++)
        {
            var dynType = CreateBareDynamicType(i);
            var dynInstance = Activator.CreateInstance(dynType)!;
            var dto = Mapper.Compose<A1_12_Dto>(new A1_12_Primary { Name = "p" }, dynInstance);
            Assert.NotNull(dto);
        }

        var after = ComposeUpdateCacheEntryCount();
        Assert.Equal(before, after);
    }

    /// <summary>
    /// A1-12 反幻觉锚点：静态源类型的更新委托仍缓存（同键二次调用命中同一委托）。
    /// </summary>
    [Fact]
    public void A1_12_Compose更新委托缓存_静态源类型仍缓存()
    {
        Mapper.ClearCache();
        var before = ComposeUpdateCacheEntryCount();

        for (var i = 0; i < 3; i++)
        {
            var dto = Mapper.Compose<A1_12_Dto>(new A1_12_Primary { Name = "p" }, new A1_12_Extra { Tag = i });
            Assert.NotNull(dto);
        }

        // 静态类型键 (A1_12_Extra, A1_12_Dto) 仅 1 条缓存
        Assert.Equal(before + 1, ComposeUpdateCacheEntryCount());
    }

    private static int ComposeUpdateCacheEntryCount()
    {
        var field = typeof(Mapper).GetField("s_composeUpdateDelegateCache",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var dict = (System.Collections.IDictionary)field.GetValue(null)!;
        return dict.Count;
    }

    // ============================== A3-05 ==============================

    /// <summary>
    /// A3-05 红测：record 占位入 CycleCache 后 CreateInstance 返 null 未清缓存——
    /// 同一深拷贝上下文内对同一 record 实例的第二次引用命中未初始化占位空壳
    /// （红：第二引用非 null）；修复后 null 返回前清占位，二次引用重走占位路径仍 null。
    /// 自定义 Provider 对 record 类型模拟 CreateInstance 返 null（JSON 反序列化 null 同族形态）。
    /// </summary>
    [Fact]
    public void A3_05_占位未清_同ctx二次引用返回未初始化空壳()
    {
        var provider = new A3_05_NullCreateForRecordProvider();
        var rec = new A3_05_Rec(42);
        var holder = new A3_05_Holder { Rec1 = rec, Rec2 = rec }; // 共享引用

        var copy = provider.DeepCopy(holder);

        // 首引用：CreateInstance null → 属性为 null（既有语义锚点）
        Assert.Null(copy!.Rec1);
        // 次引用：现状命中残留占位 → 未初始化空壳（红）；修复后清缓存 → 仍 null
        Assert.Null(copy.Rec2);
    }

    /// <summary>
    /// A3-05 反幻觉锚点：CreateInstance 正常的 record 深拷贝行为不变
    ///（占位-回拷语义， 既有契约）。
    /// </summary>
    [Fact]
    public void A3_05_正常record深拷贝_行为不变()
    {
        var rec = new A3_05_Rec(42);
        var copy = Mapper.DeepCopy(rec);
        Assert.Equal(42, copy!.Value);
        Assert.NotSame(rec, copy);
    }

    // ============================== A2-04 ==============================

    /// <summary>
    /// A2-04 红测①：多源缓存键只承载前 3 源——4 源数组 (A,B,C,D) 与 (A,B,C,E) 同键
    /// 碰撞，后者命中前者缓存（红：E 组树被 D 组缓存污染，非空树返回）；修复后键按
    /// 全量源类型序列化，两组各得其所。
    /// </summary>
    [Fact]
    public void A2_04_多源缓存键_第四源碰撞_返回错误缓存()
    {
        NavigationTreeCollector.ClearCache();
        try
        {
            var a = typeof(A2_04_SrcPlain);
            var b = typeof(A2_04_SrcPlain2);
            var c = typeof(A2_04_SrcPlain3);
            var d = typeof(A2_04_SrcWithNav);   // 含导航 Logs → 非空树
            var e = typeof(A2_04_SrcPlain4);    // 无导航 → 空贡献

            var treeWithD = NavigationTreeCollector.Collect(new[] { a, b, c, d }, typeof(A2_04_Dto));
            Assert.False(treeWithD.IsEmpty, "D 组含导航——vacuous 防护（证明收集器工作正常）");
            Assert.Contains("Logs", treeWithD.FlattenPaths());

            // 同前三源 + 第四源换成无导航的 E——现状键碰撞命中 D 组缓存（红：非空）
            var treeWithE = NavigationTreeCollector.Collect(new[] { a, b, c, e }, typeof(A2_04_Dto));
            Assert.True(treeWithE.IsEmpty, "E 组四源均无导航——多源树应为空（现状碰撞返回 D 组缓存）");
        }
        finally
        {
            NavigationTreeCollector.ClearCache();
        }
    }

    /// <summary>
    /// A2-04 红测②：Length&lt;2 越界——现状 sourceTypes[1] 抛 IndexOutOfRangeException；
    /// 修复后显式 ArgumentException（含参数名与最小源数说明）。
    /// </summary>
    [Fact]
    public void A2_04_多源收集_源数不足2_显式ArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            NavigationTreeCollector.Collect(Array.Empty<Type>(), typeof(A2_04_Dto)));
        Assert.Throws<ArgumentException>(() =>
            NavigationTreeCollector.Collect(new[] { typeof(A2_04_SrcPlain) }, typeof(A2_04_Dto)));
    }

    /// <summary>
    /// A2-04 反幻觉锚点：同源集二次调用命中缓存（ReferenceEquals）——多源缓存语义保留。
    /// </summary>
    [Fact]
    public void A2_04_多源缓存_同源集二次调用命中缓存()
    {
        NavigationTreeCollector.ClearCache();
        try
        {
            var sources = new[] { typeof(A2_04_SrcPlain), typeof(A2_04_SrcWithNav) };
            var t1 = NavigationTreeCollector.Collect(sources, typeof(A2_04_Dto));
            var t2 = NavigationTreeCollector.Collect(sources, typeof(A2_04_Dto));
            Assert.Same(t1, t2);
        }
        finally
        {
            NavigationTreeCollector.ClearCache();
        }
    }

    // ============================== A2-08 ==============================

    /// <summary>
    /// A2-08 红测：清理委托抛异常——现状仅 System.Diagnostics.Trace.WriteLine，
    /// MapTracer 通道零留痕（红：断言 MapTracer 可见失败）；修复后异常计数 + 结构化
    /// Trace 输出（其余条目仍清理——N-05 隔离语义锚点）。
    /// <para>B2 门禁复验（跟进）：独有名临时条目 finally 注销（CacheRegistry.Unregister）——
    /// 原残留进程全程，污染 CacheRegistryTests.Snapshot 全等守卫（23→25 实测必红）。</para>
    /// </summary>
    [Fact]
    public void A2_08_清理委托异常_其余条目仍清理且MapTracer可见()
    {
        var otherCleared = false;
        CacheRegistry.Register("t64-a2-08-throwing", CacheLayer.Full,
            () => throw new InvalidOperationException("A2-08 injected clear failure"));
        CacheRegistry.Register("t64-a2-08-normal", CacheLayer.Full, () => otherCleared = true);

        var sw = new System.IO.StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        try
        {
            var failuresBefore = CacheRegistry.ClearFailureCount;
            CacheRegistry.ClearFull();

            // N-05 隔离语义锚点：异常条目不阻断其余条目清理
            Assert.True(otherCleared);
            // A2-08 缺陷面：MapTracer 结构化留痕（现状仅 System.Diagnostics.Trace → 红）
            Assert.Contains("t64-a2-08-throwing", sw.ToString());
            Assert.Contains("A2-08 injected clear failure", sw.ToString());
            // A2-08 观测面：异常计数（随修复落地的观测钩子）
            Assert.Equal(failuresBefore + 1, CacheRegistry.ClearFailureCount);
        }
        finally
        {
            MapTracer.GloballyEnabled = oldEnabled;
            MapTracer.GlobalOutput = oldOutput;
            // B2 门禁复验：临时条目注销（防 Snapshot 全等守卫污染；断言中途抛出亦不残留）
            CacheRegistry.Unregister("t64-a2-08-throwing");
            CacheRegistry.Unregister("t64-a2-08-normal");
        }
    }

    // ============================== 动态类型工厂 ==============================

    private static Type CreateBareDynamicType(int seed)
    {
        var asmName = new AssemblyName($"T64A112_{seed}_{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");
        var tb = module.DefineType($"DynSrc{seed}", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Serializable);
        _ = tb.DefineDefaultConstructor(MethodAttributes.Public);
        return tb.CreateType()!;
    }

    // ============================== 模型 ==============================

    private sealed class A1_12_Primary { public string? Name { get; set; } }

    [MapFrom(typeof(A1_12_Primary))]
    private sealed class A1_12_Dto { public string? Name { get; set; } }

    private sealed class A1_12_Extra { public int Tag { get; set; } }

    // ----- A3-05 模型 -----

    private sealed record A3_05_Rec(int Value);

    private sealed class A3_05_Holder
    {
        public A3_05_Rec? Rec1 { get; set; }
        public A3_05_Rec? Rec2 { get; set; }
    }

    /// <summary>
    /// A3-05 红测载体：对 record 类型 CreateInstance 返 null（模拟 JSON 反序列化 null
    /// 等极端场景），其余类型正常创建；FillProperties 递归复用 ctx（保持循环引用检测）。
    /// </summary>
    private sealed class A3_05_NullCreateForRecordProvider : DeepCopyProviderBase
    {
        protected override T CreateInstance<T>(T source)
            => typeof(T) == typeof(A3_05_Rec)
                ? null!
                : (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

        protected override void FillProperties<T>(T source, T target)
        {
        }

        internal override void FillProperties<T>(T source, T target, DeepCopyContext ctx)
        {
            if (source is A3_05_Holder h && target is A3_05_Holder th)
            {
                th.Rec1 = (A3_05_Rec?)DeepCopy(h.Rec1!, ctx);
                th.Rec2 = (A3_05_Rec?)DeepCopy(h.Rec2!, ctx);
            }
        }
    }

    // ----- A2-04 模型（镜像 A19 导航收集范式） -----

    private sealed class A2_04_Log { public int Id { get; set; } }

    private sealed class A2_04_SrcWithNav
    {
        public int Id { get; set; }
        public List<A2_04_Log> Logs { get; set; } = new();
    }

    private sealed class A2_04_SrcPlain { public int Id { get; set; } }
    private sealed class A2_04_SrcPlain2 { public int Id { get; set; } }
    private sealed class A2_04_SrcPlain3 { public int Id { get; set; } }
    private sealed class A2_04_SrcPlain4 { public int Id { get; set; } }

    [MapFrom(typeof(A2_04_Log))] // 补锚——[MapProperty] 消费以有锚为前提
    private sealed class A2_04_Dto
    {
        public int Id { get; set; }
        [MapProperty(typeof(A2_04_Log), "Logs")]
        public List<string> AuditLogs { get; set; } = new();
    }
}
