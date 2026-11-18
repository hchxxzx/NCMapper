using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// T11 准入裁决探针（批次 4，2026-08-28）
/// <para>12 项 P2 中正确性/计数/构建期类 8 项的现状实证（性能/分配类见
/// benchmarks/NCMapper.Benchmarks/T11_AdmissionBench.cs）。</para>
/// <para><b>断言策略（红测资产口径）</b>：本文件所有断言锁定【现状缺陷行为】——
/// 测试绿 = 缺陷实证成立（可判定、对实现敏感）。修复落地时，
/// 按各用例"反转指引"注释将断言反转为期望行为（B7 交接：反转须随修复同 commit，
/// 不得提前或遗漏）。</para>
/// </summary>
public class T11AdmissionProbeTests
{
    private readonly ITestOutputHelper _out;
    public T11AdmissionProbeTests(ITestOutputHelper output) => _out = output;

    // ================================================================
    // Compose 主源匹配双引擎分叉（正确性类 ——修复即准入）
    // 审计验收点：双引擎对照传派生类主源断言一致（运行时改 IsInstanceOfType）
    // ================================================================

    private class T11ComposeSource { public string A { get; set; } = "a"; }
    private class T11ComposeDerivedSource : T11ComposeSource { }
    private class T11ComposeOther { public string B { get; set; } = "b"; }

    [MapFrom(typeof(T11ComposeSource))]
    private class T11ComposeDto
    {
        public string A { get; set; } = "";
        public string B { get; set; } = "";
    }

    [Fact]
    public void S02_Compose_DerivedPrimarySource_RejectedByRuntimeEngine()
    {
        // 已修复：主源匹配放宽为 IsInstanceOfType（本型或派生均可命中），
        // 与生成引擎 `src is T primary` 语义对齐——派生类主源映射成功且字段值正确。
        var dto = Mapper.Compose<T11ComposeDto>(new T11ComposeDerivedSource(), new T11ComposeOther());
        Assert.NotNull(dto);
        Assert.Equal("a", dto!.A);
        _out.WriteLine("修复验证：派生类主源被运行时引擎接受（IsInstanceOfType 类型兼容匹配）。");
    }

    // ================================================================
    // PropertyIgnoreChecker 四缓存无动态类型守卫（正确性类 ——修复即准入）
    // 审计验收点：复用验收模式（动态类型不入缓存 → 可收集程序集可卸载）
    // ================================================================

    [Fact]
    public void S11_IgnoreCheckerCaches_RetainDynamicPropertyInfo()
    {
        var (weak, cachedHit) = FeedDynamicPropertyIntoCaches();

        // 已修复：四方法入口 IsDynamicType 守卫——动态属性直算不入缓存
        Assert.False(cachedHit, "动态 PropertyInfo 仍入缓存 ——守卫未生效，请复核探针喂入路径");

        // 动态类型经 GC 后回收（缓存无强引用滞留，RunAndCollect 程序集可卸载）
        for (int i = 0; i < 2; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(weak.IsAlive,
            "动态类型未被回收 ——修复未生效（缓存键 PropertyInfo→Type 强引用应已消除）");
        _out.WriteLine("修复验证：动态属性不入四缓存，可收集程序集 GC 后正常卸载。");
    }

    /// <summary>喂入动态 PropertyInfo 到 PropertyIgnoreChecker 四缓存，返回（动态类型弱引用，是否命中缓存）</summary>
    private static (WeakReference Weak, bool CachedHit) FeedDynamicPropertyIntoCaches()
    {
        // RunAndCollect 动态程序集（Assembly.IsDynamic == true → IsDynamicType 判定条件，对照 PropertyMatcher.cs:126 已治理口径）
        // 注：DefineProperty 必须挂 getter（否则 GetProperty 返回 null —— 无 accessor 属性不入反射可见集）
        var asm = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("T11S11Probe"), AssemblyBuilderAccess.RunAndCollect);
        var mod = asm.DefineDynamicModule("T11S11ProbeMod");
        var tb = mod.DefineType("T11ProbeType", TypeAttributes.Public);
        var propBuilder = tb.DefineProperty("P", PropertyAttributes.None, typeof(string), null);
        var getter = tb.DefineMethod("get_P", MethodAttributes.Public, typeof(string), Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        propBuilder.SetGetMethod(getter);
        var type = tb.CreateType();
        var prop = type.GetProperty("P")!;

        // PropertyIgnoreChecker 为 internal —— 反射调四 public 入口（先例：CacheReleaseTests NonPublic 反射）
        var checker = typeof(Mapper).Assembly.GetType("NCMapper.PropertyIgnoreChecker")!;
        checker.GetMethod("ShouldIgnoreAttributeOnly", new[] { typeof(PropertyInfo), typeof(bool) })!
            .Invoke(null, new object[] { prop, false });
        checker.GetMethod("GetConvertAttribute", new[] { typeof(PropertyInfo) })!.Invoke(null, new object[] { prop });
        checker.GetMethod("GetMaskAttribute", new[] { typeof(PropertyInfo) })!.Invoke(null, new object[] { prop });
        checker.GetMethod("GetConvertBackAttribute", new[] { typeof(PropertyInfo) })!.Invoke(null, new object[] { prop });

        bool cachedHit = false;
        foreach (var fieldName in new[] { "s_ignoreAttrCache", "s_convertUsingAttrCache", "s_maskAttrCache", "s_convertBackUsingAttrCache" })
        {
            var cache = (System.Collections.IDictionary)checker.GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            if (cache.Contains(prop)) cachedHit = true;
        }

        return (new WeakReference(type), cachedHit);
    }

    // ================================================================
    // null 安全链 O(d²) 前缀重复求值（计数探针 ——getter 调用数）
    // 审计验收点：计数 getter 的 5 段路径总调用 15→5（d≥3 改 Block + 临时变量）
    // ================================================================

    private static class S21Count { public static int GetterCalls; }

    private sealed class L5 { public string E { get; set; } = "leaf"; }
    private sealed class L4 { private readonly L5 _e = new(); public L5? D { get { S21Count.GetterCalls++; return _e; } } }
    private sealed class L3 { private readonly L4 _d = new(); public L4? C { get { S21Count.GetterCalls++; return _d; } } }
    private sealed class L2 { private readonly L3 _c = new(); public L3? B { get { S21Count.GetterCalls++; return _c; } } }
    private sealed class L1 { private readonly L2 _b = new(); public L2? A { get { S21Count.GetterCalls++; return _b; } } }

    private class T11PathSource { public L1? Root { get; set; } = new(); }

    [MapFrom(typeof(T11PathSource))] // 运行时路径（private 嵌套类 SourceGen 跳过 NCMAP011，范式同 DotPathGoldenTripleTests）
    private class T11PathDto
    {
        [MapProperty(typeof(T11PathSource), "Root.A.B.C.D.E")]
        public string? Leaf { get; set; }
    }

    [Fact]
    public void S21_Flatten5Segments_PrefixGetterCallsAreQuadratic()
    {
        S21Count.GetterCalls = 0;
        var dto = Mapper.Map<T11PathSource, T11PathDto>(new T11PathSource());
        Assert.NotNull(dto);
        Assert.Equal("leaf", dto!.Leaf); // 路径映射语义正常（只测访问次数，不测正确性）

        // 已修复：null 安全链 Block 优化——d≥3（无值类型索引
        // 中间段）时临时变量缓存各段访问，每段 getter 恰一次（计数模型 A/B/C/D 四层：原 14 → 4）
        _out.WriteLine($"修复验证：5 段路径 getter 总调用 = {S21Count.GetterCalls}（每段恰一次）");
        Assert.Equal(4, S21Count.GetterCalls);
    }

    // ================================================================
    // MaskProvider.Resolve(Type) 无实例缓存（计数探针 ——实例化数）
    // 审计验收点：连续两次 Map 仅一次实例化（加实例缓存后）
    // ================================================================

    private sealed class T11CountingMask : IMask
    {
        public static int Instantiations;
        public T11CountingMask() => Instantiations++;
        public string Mask(string? value) => "MASKED";
    }

    private class T11MaskCountSource { public string Phone { get; set; } = "13812345678"; }
    [MapFrom(typeof(T11MaskCountSource))] // T5 复核：Plan 直调输入的承载侧合法形态
    private class T11MaskCountDto
    {
        [Mask(typeof(T11CountingMask))]
        public string? Phone { get; set; }
    }

    [Fact]
    public void S22_RuntimeMaskPath_InstantiatesMaskPerMap()
    {
        T11CountingMask.Instantiations = 0;
        var src = new T11MaskCountSource();
        var dto1 = Mapper.Map<T11MaskCountSource, T11MaskCountDto>(src);
        var dto2 = Mapper.Map<T11MaskCountSource, T11MaskCountDto>(src);
        Assert.NotNull(dto1);
        Assert.NotNull(dto2);
        Assert.Equal("MASKED", dto2!.Phone); // Mask 语义正常（只测实例化次数）

        // 已修复：MaskProvider.Resolve(Type) 实例缓存——连续两次 Map 仅一次实例化
        _out.WriteLine($"修复验证：连续 2 次 Map 实例化 {T11CountingMask.Instantiations} 次");
        Assert.Equal(1, T11CountingMask.Instantiations);
    }

    // ================================================================
    // MapTracer.Trace 污染入参 options + 并发写无同步（正确性探针）
    // 审计验收点：Trace 后 options.Output 引用不变；100 线程并发 Trace 输出行完整
    // ================================================================

    private class T11TraceSource { public string Name { get; set; } = "s"; }
    private class T11TraceTarget { public string Name { get; set; } = ""; }

    [Fact]
    public void S27_Trace_PollutesCallerOptionsOutput()
    {
        var callerWriter = new StringWriter();
        var opts = new MapTraceOptions { Enabled = true, Output = callerWriter };

        _ = MapTracer.Trace<T11TraceSource, T11TraceTarget>(new T11TraceSource(), opts);

        // 已修复：Trace 内部浅拷贝 options，入参 Output 引用不再被覆写
        Assert.Same(callerWriter, opts.Output);
        _out.WriteLine("修复验证：Trace 后调用方 options.Output 引用保持不变（入参不污染）。");
    }

    [Fact]
    public async Task S27_Trace_ConcurrentWriters_LineIntegrityBaseline()
    {
        // 记录型探针（M12 flaky 概率性实证 —— 不做损坏硬断言，输出基线供裁决表引用）
        // 现状实测（2026-08-28 net8.0）：共享 StringWriter 的 StringBuilder 并发 Append 直接抛
        // ArgumentException("Destination is too short") —— 并发不安全的强实证（比行交错丢失更严重）。
        var sb = new StringBuilder();
        var shared = new StringWriter(sb);
        var saved = MapTracer.GlobalOutput;
        Exception? crash = null;
        try
        {
            MapTracer.GlobalOutput = shared;
            MapTracer.GloballyEnabled = true;
            var tasks = Enumerable.Range(1, 100).Select(i => Task.Run(() =>
                MapTracer.Write($"line-{i:D3}"), TestContext.Current.CancellationToken)).ToArray();
            try { await Task.WhenAll(tasks); }
            catch (Exception ex) { crash = ex; }
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
            MapTracer.GlobalOutput = saved;
        }

        var lines = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        _out.WriteLine($"修复验证：100 线程 ×1 行，实际完整行 = {lines.Length}；异常 = {crash?.GetType().Name ?? "无"}");
        // 已修复：进程级写锁保证并发输出行完整（修复前实测 97/100 + StringBuilder 崩溃）
        Assert.Null(crash);
        Assert.Equal(100, lines.Length);
    }

    // ================================================================
    // ScanAssembly 缓存强引用已卸载 ALC 的 Type（正确性类 ——修复即准入）
    // 审计验收点：ALC 卸载 + GC 后 WeakReference 断言释放
    // ================================================================

    [Fact]
    public void S29_ValidatorScanCache_RetainsCollectibleAssemblyType()
    {
        var weak = FeedCollectibleAssemblyIntoValidator();
        for (int i = 0; i < 2; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

        // 已修复：s_assemblyScanCache 改 ConditionalWeakTable
        // 弱键——ALC 卸载后条目随 GC 失效，动态类型正常回收
        Assert.False(weak.IsAlive,
            "动态类型未被回收 ——修复未生效（CWT 弱键应使条目随 ALC 卸载失效）");
        _out.WriteLine("修复验证：Validate 后动态程序集经 CWT 弱键随 GC 正常回收（ALC 可卸载）。");
    }

    /// <summary>构造带 [MapFrom] 的可收集动态程序集 → 喂入 MapValidator.Validate → 返回动态类型弱引用</summary>
    private static WeakReference FeedCollectibleAssemblyIntoValidator()
    {
        var asm = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("T11S29Probe"), AssemblyBuilderAccess.RunAndCollect);
        var mod = asm.DefineDynamicModule("T11S29ProbeMod");
        var tb = mod.DefineType("T29Dto", TypeAttributes.Public);
        var mapFromCtor = typeof(MapFromAttribute).GetConstructor(new[] { typeof(Type) })!;
        tb.SetCustomAttribute(new CustomAttributeBuilder(mapFromCtor, new object[] { typeof(T11ComposeSource) }));
        var type = tb.CreateType();

        _ = MapValidator.Validate(asm); // ScanAssembly GetOrAdd(asm, ...) → 键+值强引用滞留

        return new WeakReference(type);
    }

    // ================================================================
    // 构建期属性匹配/特性反射无缓存（构建期耗时类 ——3 轮中位数基线）
    // 审计验收点：Validate(Assembly[]) 大程序集耗时基准 30%+ 缩短（修复后对照）
    // ================================================================

    [Fact]
    public void S12_ValidateBigAssembly_Baseline3Rounds()
    {
        var asm = typeof(T11AdmissionProbeTests).Assembly; // 测试程序集（千级类型 + 大量 [MapFrom] 对）
        var rounds = new long[3];
        for (int r = 0; r < 3; r++)
        {
            Mapper.ClearCache(); // 清 ScanAssembly/反射缓存（CacheRegistry Full 层）—— 每轮测"冷扫描"成本
            var sw = Stopwatch.StartNew();
            _ = MapValidator.Validate(asm);
            rounds[r] = sw.ElapsedMilliseconds;
        }
        var median = rounds.OrderBy(x => x).ElementAt(1);
        _out.WriteLine($"基线（3 轮 ms）：[{rounds[0]}, {rounds[1]}, {rounds[2]}] 中位数 = {median}ms");
        Assert.InRange(median, 1, 120_000); // 活性 + 防挂死（合理上限 2 分钟）
    }
}
