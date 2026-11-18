// 锚门与缓存族红测套件（新增测试矩阵 12 行对位）
// <para>断言形态总览（评审 003 N-2 口径延伸）：CWT 无 Count/枚举面——凡"条目计数"类断言
// 一律以可判定替代形态承载（弱键回收 WeakReference / 反射腿构造计数探针 / 分配字节测量 /
// 版本条目重算探针 / 重载形态反射读取）；防漏必红由 CacheRegistryTests 变异闭环（
// 各一次）+ 本套件 RegistryDrivenClear 清理实效断言共同承载。</para>
// <para>实测定型的三条测试纪律（2026-09-26 执行期，AlcProbeTests 探针实证——探针已退役）：
// ① ALC 回收断言必须把"动态类型构造 + 库调用"封装进独立静态方法、主测试方法只拿 WeakReference
//   （同方法内局部变量在 Debug 构建下被 GC 根延长——同方法形态必假红，T11 S29 先例同构）；
// ② 反射腿计数探针（CountingMapFrom）必须以动态 ALC 类型承载——测试程序集内的 [MapFrom] 类型
//   会被 SourceGen 发射 ModuleInitializer 注册进 RuntimeSnapshotStore，HasAnchor 走注册表腿
//   不触发反射腿（计数恒 0）；
// ③ 弱键写入不得经 MakeGenericMethod 闭合库泛型方法（CLR 闭合方法缓存钉住类型）——
//   CWT 经反射索引器（Item）写入，字段类型已闭合（Type 键固定）。</para>
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Tests.Runtime;

/// <summary>
/// 反射腿计数探针特性（public 命名空间级——动态程序集特性解析需可公开解析的类型；
/// 私有 nested 类型经 CustomAttributeBuilder 打到动态类型后 GetCustomAttribute 读不到，实测踩坑）。
/// GetCustomAttribute 每次调用构造新实例——构造数 = 反射读取数。
/// </summary>
public sealed class CountingMapFromAttribute : MapFromAttribute
{
    public static int Constructions;
    public CountingMapFromAttribute(Type fromSource) : base(fromSource)
        => System.Threading.Interlocked.Increment(ref Constructions);
}

[Collection("GlobalConfigState")]
public class AnchorGateCacheTests
{
    private readonly ITestOutputHelper _out;
    public AnchorGateCacheTests(ITestOutputHelper output) => _out = output;

    /// <summary>RunAndCollect 动态程序集内构造带属性类型（T11 S11 先例同构）。</summary>
    private static Type BuildCollectibleType(string name, bool isStruct,
        (string PropName, Type PropType)[] props, CustomAttributeBuilder? attr)
    {
        var asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AGCProbe_" + name),
            AssemblyBuilderAccess.RunAndCollect);
        var mod = asm.DefineDynamicModule(name + "Mod");
        var tb = isStruct
            ? mod.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof(ValueType))
            : mod.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        if (attr != null) tb.SetCustomAttribute(attr);
        foreach (var (pn, pt) in props)
        {
            var pb = tb.DefineProperty(pn, PropertyAttributes.None, pt, null);
            var getter = tb.DefineMethod("get_" + pn, MethodAttributes.Public, pt, Type.EmptyTypes);
            var il = getter.GetILGenerator();
            if (pt.IsValueType) il.Emit(OpCodes.Ldc_I4_0); else il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            pb.SetGetMethod(getter);
            if (isStruct) continue; // struct 探针只读
            var setter = tb.DefineMethod("set_" + pn, MethodAttributes.Public, null, new[] { pt });
            var sil = setter.GetILGenerator();
            sil.Emit(OpCodes.Ret);
            pb.SetSetMethod(setter);
        }
        return tb.CreateType()!;
    }

    private static CustomAttributeBuilder CountingMapFrom(Type fromSource)
        => new(typeof(CountingMapFromAttribute).GetConstructor(new[] { typeof(Type) })!, new object[] { fromSource });

    /// <summary>
    /// RunAndCollect 动态程序集内构造多类型（探针族共享同一程序集——特性 blob 引用同程序集类型，
    /// 分程序集时特性参数类型解析 FileNotFoundException，实测踩坑）。
    /// </summary>
    private static AssemblyBuilder BuildCollectibleAssembly(string name)
        => AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect);

    private static TypeBuilder DefineCollectibleType(ModuleBuilder mod, string name, bool isStruct,
        (string PropName, Type PropType)[] props, CustomAttributeBuilder? attr)
    {
        var tb = isStruct
            ? mod.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof(ValueType))
            : mod.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        if (attr != null) tb.SetCustomAttribute(attr);
        foreach (var (pn, pt) in props)
        {
            var pb = tb.DefineProperty(pn, PropertyAttributes.None, pt, null);
            var getter = tb.DefineMethod("get_" + pn, MethodAttributes.Public, pt, Type.EmptyTypes);
            var il = getter.GetILGenerator();
            if (pt.IsValueType) il.Emit(OpCodes.Ldc_I4_0); else il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            pb.SetGetMethod(getter);
            if (isStruct) continue; // struct 探针只读
            var setter = tb.DefineMethod("set_" + pn, MethodAttributes.Public, null, new[] { pt });
            var sil = setter.GetILGenerator();
            sil.Emit(OpCodes.Ret);
            pb.SetSetMethod(setter);
        }
        return tb;
    }

    private static void FullGc()
    {
        for (var i = 0; i < 2; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    }

    // ---- 快照存储快照-恢复（经验 #76：Clear 不可逆）----

    private static IReadOnlyList<RuntimeMapperMetadata> CaptureStore() => RuntimeSnapshotStore.All;

    private static void RestoreStore(IReadOnlyList<RuntimeMapperMetadata> snapshot)
    {
        RuntimeSnapshotStore.Clear();
        foreach (var m in snapshot) RuntimeSnapshotStore.Register(m);
    }

    // ================================================================
    // 矩阵行 3（C-4 反射腿缓存）：正缓存腿——同一 Type 连续 N 次 HasAnchor 反射计数 == 1
    // ================================================================

    /// <summary>喂入：动态 DTO 打 CountingMapFrom（与锚源类型同程序集——特性参数引用同程序集类型）→
    /// ClearCache → HasAnchor×5（正缓存）→ 清 → 再 HasAnchor。</summary>
    private static (WeakReference Weak, int FirstRound, int AfterClear, bool AnchorHit, int AttrDataCount) FeedPositiveCacheProbe()
    {
        var asm = BuildCollectibleAssembly("AGCProbeM14B");
        var mod = asm.DefineDynamicModule("M14BMod"); // 一程序集一动态模块（运行时限）
        // 特性参数用静态程序集类型（typeof(object)）——动态程序集不可按名解析（GetCustomAttribute
        // 的 requesting assembly 为 NCMapper，跨上下文解析动态程序集 FileNotFoundException，实测踩坑）
        var dto = DefineCollectibleType(mod, "M14BDto", false, new[] { ("A", typeof(int)) }, CountingMapFrom(typeof(object))).CreateType()!;

        Mapper.ClearCache();
        CountingMapFromAttribute.Constructions = 0;
        var attrDataCount = dto.GetCustomAttributesData().Count;
        var hit = RuntimeAnchorGate.HasAnchor(dto); // 首查（反射腿应构造 CountingMapFrom）
        for (var i = 0; i < 4; i++) RuntimeAnchorGate.HasAnchor(dto);
        var first = CountingMapFromAttribute.Constructions;

        Mapper.ClearCache(); // 失效口径：runtime-snapshot-probe 登记清理 → 缓存重置
        RuntimeAnchorGate.HasAnchor(dto);
        var after = CountingMapFromAttribute.Constructions;
        return (new WeakReference(dto), first, after, hit, attrDataCount);
    }

    [Fact]
    public void M14B_PositiveCache_SameTypeReflectionOnce()
    {
        var (weak, first, after, hit, attrDataCount) = FeedPositiveCacheProbe();
        FullGc();
        Assert.False(weak.IsAlive, "探针类型滞留——CWT 弱键未生效");
        _out.WriteLine($"M14B 探针：HasAnchor={hit} attrDataCount={attrDataCount} first={first} after={after}");
        Assert.True(hit, "前置锚：动态类型带 CountingMapFrom 特性，HasAnchor 应为 true");
        Assert.Equal(1, attrDataCount); // 前置锚：确有 1 条特性数据（CR #8——观测值落断言）
        Assert.Equal(1, first);  // 反射腿正缓存：同 Type 至多一次 GetCustomAttribute（单线程语义）
        Assert.Equal(2, after);  // ClearCache 后重反射（清理链覆盖实效）
    }

    // ================================================================
    // 矩阵行 4（C-3 负缓存腿）：无锚 Type 负缓存条目建立后稳态零分配零锁
    // ================================================================

    /// <summary>喂入：动态无锚类型 → 首查建负缓存条目 → 预热 → 精确测量 20 次稳态调用分配。</summary>
    private static (WeakReference Weak, long AllocDelta) FeedNegativeCacheProbe()
    {
        var type = BuildCollectibleType("M14BNeg", false, new[] { ("A", typeof(int)) }, null);
        Mapper.ClearCache();
        Assert.False(RuntimeAnchorGate.HasAnchor(type)); // 首查：负结果 + 建条目
        for (var i = 0; i < 5; i++) RuntimeAnchorGate.HasAnchor(type); // 预热（JIT/缓存就位）

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) RuntimeAnchorGate.HasAnchor(type);
        var delta = GC.GetAllocatedBytesForCurrentThread() - before;
        return (new WeakReference(type), delta);
    }

    [Fact]
    public void M14B_NegativeCache_SteadyStateZeroAllocation()
    {
        var (weak, delta) = FeedNegativeCacheProbe();
        FullGc();
        Assert.False(weak.IsAlive, "探针类型滞留——CWT 弱键未生效");
        Assert.Equal(0, delta); // C-3：负缓存腿零堆分配（若重反射必 >0——反射构造语义保证）
        _out.WriteLine($"负缓存腿 20 次稳态调用分配字节 = {delta}");
    }

    // ================================================================
    // 矩阵行 1/2（C-1/C-2）：并发 Register × 并发读（快照完整性 + 三容器终态一致）
    // ================================================================

    private sealed class G1Src { public int A { get; set; } }
    private sealed class G1Dto { public int A { get; set; } }

    [Fact]
    public async Task C1C2_ConcurrentRegisterReaders_SnapshotIntegrity_TerminalConsistency()
    {
        var snapshot = CaptureStore();
        try
        {
            RuntimeSnapshotStore.Clear();
            var pairs = new (Type S, Type T)[8];
            for (var i = 0; i < 8; i++)
            {
                pairs[i] = (BuildCollectibleType($"C1Src{i}", false, new[] { ("A", typeof(int)) }, null),
                    BuildCollectibleType($"C1Dto{i}", false, new[] { ("A", typeof(int)) }, null));
            }

            var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            var writeDone = new System.Threading.ManualResetEventSlim(false);

            // 写侧：并发 Register（新键混合 + 同键幂等合并两路径）
            var writers = Enumerable.Range(0, 4).Select(w => System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    for (var round = 0; round < 50; round++)
                    {
                        foreach (var (s, t) in pairs)
                        {
                            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
                            {
                                SourceType = s, TargetType = t,
                                SourceTypeFqn = s.FullName ?? s.Name, TargetTypeFqn = t.FullName ?? t.Name,
                                Channel = RuntimeMappingChannel.MapFrom,
                                Properties = Array.Empty<RuntimePropertyMapping>(),
                                AllTargetSlots = Array.Empty<RuntimeTargetPropertySlot>(),
                            });
                        }
                    }
                }
                catch (Exception ex) { errors.Enqueue(ex); }
                finally { if (w == 3) writeDone.Set(); }
            }, TestContext.Current.CancellationToken)).ToArray();

            // 读侧：6 出口并发读（含 HasAnchor 门面）——断言无撕裂异常
            var readers = Enumerable.Range(0, 4).Select(_ => System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    while (!writeDone.IsSet)
                    {
                        _ = RuntimeSnapshotStore.All.Count;
                        foreach (var (_, t) in pairs)
                        {
                            _ = RuntimeSnapshotStore.FindByTarget(t).Count;
                            RuntimeSnapshotStore.IsFluentRegisteredDto(t);
                            RuntimeSnapshotStore.IsPropertyNoAutoInclude(t, "A");
                            RuntimeSnapshotStore.TryGetFluentDetailMeta(t, out var metaProbe);
                            RuntimeAnchorGate.HasAnchor(t);
                        }
                    }
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            }, TestContext.Current.CancellationToken)).ToArray();

            await System.Threading.Tasks.Task.WhenAll(writers.Concat(readers).ToArray());
            Assert.Empty(errors); // C-1：无撕裂/无 InvalidOperationException（完整历史快照语义）

            // C-2 终态一致：三容器无孤儿条目
            var all = RuntimeSnapshotStore.All;
            Assert.Equal(8, all.Count);
            foreach (var (s, t) in pairs)
            {
                Assert.Single(RuntimeSnapshotStore.FindByTarget(t));
                Assert.NotNull(RuntimeSnapshotStore.FindByPair(s, t));
            }
        }
        finally { RestoreStore(snapshot); }
    }

    /// <summary>矩阵行 12（并发与一致性）：Clear 与并发读交错无异常。</summary>
    [Fact]
    public async Task Concurrency_ClearInterleavedReads_NoThrow()
    {
        var snapshot = CaptureStore();
        try
        {
            RuntimeSnapshotStore.Clear();
            var s = BuildCollectibleType("G3Src", false, new[] { ("A", typeof(int)) }, null);
            var t = BuildCollectibleType("G3Dto", false, new[] { ("A", typeof(int)) }, null);
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = s, TargetType = t,
                SourceTypeFqn = s.Name, TargetTypeFqn = t.Name,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = Array.Empty<RuntimePropertyMapping>(),
                AllTargetSlots = Array.Empty<RuntimeTargetPropertySlot>(),
            });

            var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            var stop = new System.Threading.CancellationTokenSource(3000);
            var readers = Enumerable.Range(0, 4).Select(_ => System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        _ = RuntimeSnapshotStore.All.Count;
                        _ = RuntimeSnapshotStore.FindByTarget(t).Count;
                        RuntimeAnchorGate.HasAnchor(t);
                    }
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            }, TestContext.Current.CancellationToken)).ToArray();
            var clearer = System.Threading.Tasks.Task.Run(() =>
            {
                try { while (!stop.IsCancellationRequested) RuntimeSnapshotStore.Clear(); }
                catch (Exception ex) { errors.Enqueue(ex); }
            }, TestContext.Current.CancellationToken);
            await System.Threading.Tasks.Task.WhenAll(readers.Append(clearer).ToArray());
            Assert.Empty(errors);
        }
        finally { RestoreStore(snapshot); }
    }

    // ================================================================
    // 矩阵行 5（M-17 跨 ORM 隔离）：EFCore（深度 8）与 FreeSql（深度 3）对同一类型对结论独立
    //（集合元素链承载——FreeSql"集合元素即降级"在 3 层集合链触发、EFCore 可投影集合不触发）
    // ================================================================

    private sealed class X1 { public string V { get; set; } = ""; }
    private sealed class X2 { public List<X1> Items { get; set; } = new(); }
    private sealed class X3 { public List<X2> Items { get; set; } = new(); }
    private sealed class X4 { public List<X3> Items { get; set; } = new(); }

    [MapFrom(typeof(X1))] private sealed class D1 { public string V { get; set; } = ""; }
    [MapFrom(typeof(X2))] private sealed class D2 { public List<D1> Items { get; set; } = new(); }
    [MapFrom(typeof(X3))] private sealed class D3 { public List<D2> Items { get; set; } = new(); }
    [MapFrom(typeof(X4))] private sealed class D4 { public List<D3> Items { get; set; } = new(); }

    [Fact]
    public void M17_CrossOrm_ConclusionsIndependent()
    {
        Mapper.ClearCache();
        // 诊断：D3 锚门可达性（嵌套递归入口 TryGetMappingMeta(D3) 命中才进 ORM 注入点）
        var d3Anchor = RuntimeAnchorGate.HasAnchor(typeof(D3));
        var d3View = RuntimeSnapshotStore.TryGetMappingMeta(typeof(D3), out var d3Meta);
        _out.WriteLine($"诊断：HasAnchor(D3)={d3Anchor} TryGetMappingMeta(D3)={d3View} PrimarySource={d3Meta?.PrimarySource?.Name}");

        // 嵌套元素须过锚门（TryGetMappingMeta 命中才进 ORM 注入点）——DTO 链全打 [MapFrom]：
        // FreeSql"集合元素即降级"在 3 层集合链触发（深度上限 3）；EFCore 纯 DTO 集合可投影不降级（上限 8）。
        // 同一 (X4, D4) 两 ORM 结论不同 = 实例级隔离直接证据（若共享缓存则后查者复用先查者结论）。
        var ef1 = EfCoreDegradationDetector.NeedsDegradation(typeof(X4), typeof(D4), out var efReason);
        var fs1 = FreeSqlDegradationDetector.NeedsDegradation(typeof(X4), typeof(D4), out var fsReason);
        _out.WriteLine($"EFCore={ef1}（{efReason}） FreeSql={fs1}（{fsReason}）");
        Assert.True(d3View, "前置锚：D3 带特性锚，锚门应命中");
        Assert.NotEqual(ef1, fs1); // 结论分叉 = 隔离成立

        // 交替复判：各自结论稳定（无串扰）
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(ef1, EfCoreDegradationDetector.NeedsDegradation(typeof(X4), typeof(D4), out _));
            Assert.Equal(fs1, FreeSqlDegradationDetector.NeedsDegradation(typeof(X4), typeof(D4), out _));
        }
    }

    // ================================================================
    // 矩阵行 6（M-16/17/18/19 ALC 回收）——S29 族扩展（喂入 helper 隔离 GC 根）
    // ================================================================

    /// <summary>M-16：双层 CWT 弱键——经反射索引器写入（不经 MakeGenericMethod 闭合库泛型方法，
    /// CLR 闭合方法缓存会钉住动态类型——探针实证；CWT 索引器 getter 不自动建内层——显式 new 后 set）。</summary>
    private static (WeakReference WeakS, WeakReference WeakT) FeedDelegateRegistryProbe()
    {
        var s = BuildCollectibleType("M16Src", false, new[] { ("A", typeof(int)) }, null);
        var t = BuildCollectibleType("M16Dto", false, new[] { ("A", typeof(int)) }, null);

        var registry = typeof(MapperDelegateRegistry)
            .GetField("s_registry", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var inner = new ConditionalWeakTable<Type, IMapperBaseDelegate>();
        // 反射走 AddOrUpdate 方法（CWT 索引器属性名非 "Item"，GetProperty("Item") 为 null——实测踩坑）
        registry.GetType().GetMethod("AddOrUpdate")!.Invoke(registry, new object[] { s, inner });
        inner.GetType().GetMethod("AddOrUpdate")!.Invoke(inner, new object[] { t, new M16StubDelegate() });

        // 前置锚：CWT 条目确实存在（TryGetValue 反射读回）
        var args = new object?[] { t, null };
        var hit = (bool)inner.GetType().GetMethod("TryGetValue")!.Invoke(inner, args)!;
        Assert.True(hit, "前置锚：写入后 CWT 条目应可读回");
        Assert.NotNull(args[1]);

        return (new WeakReference(s), new WeakReference(t));
    }

    [Fact]
    public void M16_DelegateRegistry_WeakKey_ReleasesCollectibleTypes()
    {
        Mapper.ClearCache();
        var (weakS, weakT) = FeedDelegateRegistryProbe();
        FullGc();
        Assert.False(weakS.IsAlive, "M-16：动态源类型被 s_registry 钉住——CWT 弱键未生效");
        Assert.False(weakT.IsAlive, "M-16：动态目标类型被 s_registry 钉住——CWT 弱键未生效");
    }

    private sealed class M16StubDelegate : IMapperBaseDelegate
    {
        public (ConstructorInfo Ctor, Expression[] Args) BuildConstructorArgs(Type targetType, Type sourceType, Expression sourceParam)
            => throw new NotSupportedException("probe stub");
        public Expression MapFallback(Expression sourceExpr) => throw new NotSupportedException("probe stub");
        public Expression MapList(Expression sourceExpr) => throw new NotSupportedException("probe stub");
    }

    /// <summary>M-17：实例级 CWT（测试侧子类探针实例）——动态类型对随 GC 回收。</summary>
    private static (WeakReference WeakS, WeakReference WeakT) FeedDetectorProbe()
    {
        var detector = new ProbeDetector();
        var s = BuildCollectibleType("M17Src", false, new[] { ("A", typeof(int)) }, null);
        var t = BuildCollectibleType("M17Dto", false, new[] { ("A", typeof(int)) }, null);
        Assert.False(detector.Evaluate(s, t, out _)); // 填充实例缓存
        return (new WeakReference(s), new WeakReference(t));
    }

    [Fact]
    public void M17_InstanceCWT_WeakKey_ReleasesCollectibleTypes()
    {
        Mapper.ClearCache();
        var (weakS, weakT) = FeedDetectorProbe();
        FullGc();
        Assert.False(weakS.IsAlive, "M-17：动态源类型被实例缓存钉住——CWT 弱键未生效");
        Assert.False(weakT.IsAlive, "M-17：动态目标类型被实例缓存钉住——CWT 弱键未生效");
    }

    /// <summary>M-18：单源导航树缓存弱键——动态类型对随 GC 回收。</summary>
    private static (WeakReference WeakS, WeakReference WeakT) FeedNavigationProbe()
    {
        var s = BuildCollectibleType("M18Src", false, new[] { ("A", typeof(int)) }, null);
        var t = BuildCollectibleType("M18Dto", false, new[] { ("A", typeof(int)) }, null);
        var tree = NavigationTreeCollector.Collect(s, t); // 空导航 → 空树（条目入缓存）
        Assert.True(tree.IsEmpty);
        return (new WeakReference(s), new WeakReference(t));
    }

    [Fact]
    public void M18_NavigationTree_WeakKey_ReleasesCollectibleTypes()
    {
        Mapper.ClearCache();
        var (weakS, weakT) = FeedNavigationProbe();
        FullGc();
        Assert.False(weakS.IsAlive, "M-18：动态源类型被单源缓存钉住——CWT 弱键未生效");
        Assert.False(weakT.IsAlive, "M-18：动态目标类型被单源缓存钉住——CWT 弱键未生效");
    }

    /// <summary>M-19：动态类型直算不缓存——强键 s_cache 无条目（三分法第二行）+ 类型随 GC 回收。</summary>
    private static (WeakReference Weak, int CacheCountAfter) FeedLeafGuardProbe()
    {
        var s = BuildCollectibleType("M19Struct", true, Array.Empty<(string, Type)>(), null); // 动态 struct
        var cache = (System.Collections.IDictionary)typeof(LeafStructGuard)
            .GetField("s_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var before = cache.Count;
        var result = LeafStructGuard.IsLeafSafe(s); // IsDynamicType(动态)=true → 直算
        Assert.True(result); // 空引用字段 struct = 叶子安全（Compute 语义）
        var after = cache.Count;
        return (new WeakReference(s), after - before);
    }

    [Fact]
    public void M19_DynamicType_DirectCompute_BypassesStrongCache()
    {
        Mapper.ClearCache();
        var (weak, delta) = FeedLeafGuardProbe();
        Assert.Equal(0, delta); // 直算不入缓存（强键 CD 无动态类型条目）
        FullGc();
        Assert.False(weak.IsAlive, "M-19：动态类型被 s_cache 钉住——直算分支未生效");
    }

    // ================================================================
    // 矩阵行 7（M-17/18 版本淘汰）：测试侧子类探针重算断言 + M-18 版本变更语义一致
    // ================================================================

    private sealed class ProbeDetector : DegradationDetectorBase
    {
        public int ExtraCalls;
        protected override int MaxNestingDepth => 8;
        protected override bool RunExtraChecks(Type sourceType, Type targetType, out string reason)
        {
            ExtraCalls++;
            reason = "";
            return false;
        }
        protected override bool RunNestedCollectionChecks(Type nestedType, Type nestedSourceType, bool isCollectionElement, out string reason)
        {
            reason = "";
            return false;
        }
    }

    [Fact]
    public void M17_VersionInEntry_RecomputeOncePerVersion()
    {
        Mapper.ClearCache();
        var detector = new ProbeDetector();
        var savedVersion = Mapper.Pipeline._version;
        try
        {
            Assert.False(detector.Evaluate(typeof(X1), typeof(D1), out _));
            Assert.Equal(1, detector.ExtraCalls); // 首查重算
            Assert.False(detector.Evaluate(typeof(X1), typeof(D1), out _));
            Assert.Equal(1, detector.ExtraCalls); // 同版本命中（不再重算）

            Mapper.Pipeline._version++; // 版本推进（条目内校验失配 → 重算）
            Assert.False(detector.Evaluate(typeof(X1), typeof(D1), out _));
            Assert.Equal(2, detector.ExtraCalls); // 新版本首查重算
            Assert.False(detector.Evaluate(typeof(X1), typeof(D1), out _));
            Assert.Equal(2, detector.ExtraCalls); // 新版本命中——每 (S,T) 恒一条目（键结构不变量）
        }
        finally { Mapper.Pipeline._version = savedVersion; }
    }

    [MapFrom(typeof(M18Order))]
    private class M18OrderDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(M18Customer), "Name")]
        public string CustomerName { get; set; } = "";
    }

    private class M18Order
    {
        public int Id { get; set; }
        public M18Customer? Customer { get; set; }
    }

    private class M18Customer { public string Name { get; set; } = ""; }

    [Fact]
    public void M18_VersionBump_CollectSemanticsPreserved()
    {
        Mapper.ClearCache();
        var savedVersion = Mapper.Pipeline._version;
        try
        {
            var tree1 = NavigationTreeCollector.Collect(typeof(M18Order), typeof(M18OrderDto));
            Assert.False(tree1.IsEmpty, "前置：载体类型对确有跨表导航");
            Assert.Single(tree1.Roots);
            Assert.Equal("Customer", tree1.Roots[0].Name);

            Mapper.Pipeline._version++;
            var tree2 = NavigationTreeCollector.Collect(typeof(M18Order), typeof(M18OrderDto));
            Assert.Equal(tree1.Roots.Count, tree2.Roots.Count); // 版本变更前后树内容一致
            Assert.Equal(tree1.Roots[0].Name, tree2.Roots[0].Name);
        }
        finally { Mapper.Pipeline._version = savedVersion; }
    }

    // ================================================================
    // 矩阵行 8（CacheRegistry 覆盖）：Mapper.ClearCache 全链清空两新登记缓存
    // ================================================================

    private struct M19PlainStruct { } // 空引用字段 → Compute 恒叶子安全（GetFields 空集）

    /// <summary>喂入：动态锚类型建负缓存条目 + 静态 struct 建守卫缓存条目。
    /// 返回 (WeakReference, Type 强引用) 双值——强引用保活至断言结束（消除 GC 竞态假红，
    /// CR #3），“条目键一致性”目的不变。</summary>
    private static (WeakReference WeakAnchor, Type Anchor, int GuardCountBefore) FillProbeCaches()
    {
        var anchor = BuildCollectibleType("M8Anchor", false, new[] { ("A", typeof(int)) }, null);
        Mapper.ClearCache();
        RuntimeAnchorGate.HasAnchor(anchor); // 建负缓存条目（s_mapFromProbe）
        _ = LeafStructGuard.IsLeafSafe(typeof(M19PlainStruct)); // 建强键条目（s_cache）
        var guardCache = (System.Collections.IDictionary)typeof(LeafStructGuard)
            .GetField("s_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return (new WeakReference(anchor), anchor, guardCache.Count);
    }

    [Fact]
    public void RegistryDrivenClear_EmptiesProbeAndGuardCaches()
    {
        var (weakAnchor, anchor, guardBefore) = FillProbeCaches();

        var probeTable = typeof(RuntimeSnapshotStore)
            .GetField("s_mapFromProbe", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var hitBefore = (bool)probeTable.GetType().GetMethod("TryGetValue")!.Invoke(probeTable, new object?[] { anchor, null })!;
        Assert.True(hitBefore, "前置锚：HasAnchor 后负缓存条目应存在");
        var guardCache = (System.Collections.IDictionary)typeof(LeafStructGuard)
            .GetField("s_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.True(guardBefore > 0, "前置锚：IsLeafSafe 后守卫缓存应非空");

        Mapper.ClearCache(); // 登记表驱动全链清理（runtime-snapshot-probe + leaf-struct-guard）

        var hitAfter = (bool)probeTable.GetType().GetMethod("TryGetValue")!.Invoke(probeTable, new object?[] { anchor, null })!;
        Assert.False(hitAfter, "M-14-B 清理实效：Mapper.ClearCache 后 s_mapFromProbe 条目应被清空");
        Assert.Empty(guardCache); // M-19 清理实效
        // GC 面回收断言不在此处（同方法 GC 根延长会假红——M14B_PositiveCache 已独立覆盖弱键回收终态）
    }

    // ================================================================
    // 矩阵行 9（M-15 循环不变量上移）：动态类型计数探针 + 返回值对照 + 包装器透传
    //（动态承载原因：测试程序集 [MapFrom] 类型被 SourceGen 注册走注册表腿——反射计数恒 0，
    //  探针实证；循环内重算语义在 PropertyMatcher 层即完全可观测——split 参数直证 + 包装器透传直证）
    // ================================================================

    /// <summary>喂入：动态源类（[CountingMapFrom] 锚 + A/B 属性）+ 动态目标类。</summary>
    private static (Type Src, Type Dto, List<PropertyInfo> SourceProps) BuildM15Probe()
    {
        // 同程序集三类型（M15SrcF 特性参数引用 M15Src——分程序集时回收后解析失败，实测踩坑）
        var asm = BuildCollectibleAssembly("AGCProbeM15");
        var mod = asm.DefineDynamicModule("M15Mod"); // 一程序集一动态模块（运行时限）
        // 特性参数用静态类型（typeof(object)）——动态程序集不可按名解析（同 M14B 踩坑记录）
        var srcFinal = DefineCollectibleType(mod, "M15SrcF", false, new[] { ("A", typeof(int)), ("B", typeof(int)) }, CountingMapFrom(typeof(object))).CreateType()!;
        var dto = DefineCollectibleType(mod, "M15Dto", false, new[] { ("A", typeof(int)), ("B", typeof(int)) }, null).CreateType()!;
        var sourceProps = srcFinal.GetProperties(BindingFlags.Public | BindingFlags.Instance).ToList();
        return (srcFinal, dto, sourceProps);
    }

    /// <summary>喂入实验（同一方法内完成以共享动态类型引用；返回各阶段计数）。</summary>
    private static (int SplitCost, int PassthroughCost, int SelfComputeCost, int WrapperSplitCost, int WrapperSelfCost)
    RunM15CountingProbe()
    {
        var (src, dto, sourceProps) = BuildM15Probe();
        var targetProps = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).ToList();

        // ① split 自身成本（上移后循环外一次）：2 属性 → 2 次锚门反射
        Mapper.ClearCache();
        CountingMapFromAttribute.Constructions = 0;
        var split = PropertyMatcher.SplitAttrsByAnchor(sourceProps);
        var splitCost = CountingMapFromAttribute.Constructions;

        // ② split 传入：循环内复用（零重算零锚门）
        CountingMapFromAttribute.Constructions = 0;
        _ = PropertyMatcher.GetSourceItemByTargetItem(src, targetProps[0], sourceProps, true, false, out _, split);
        _ = PropertyMatcher.GetSourceItemByTargetItem(src, targetProps[1], sourceProps, true, false, out _, split);
        var passthroughCost = CountingMapFromAttribute.Constructions;

        // ③ split=null：分支内自算（清缓存后 2 属性同 DeclaringType → M-14-B 缓存使反射 1 次）
        Mapper.ClearCache();
        CountingMapFromAttribute.Constructions = 0;
        _ = PropertyMatcher.GetSourceItemByTargetItem(src, targetProps[0], sourceProps, true, false, out _, null);
        var selfComputeCost = CountingMapFromAttribute.Constructions;

        // ④ 包装器透传（′）：经 MapperBase<,> private 包装器 split 送达 → 零重算
        var wrapper = typeof(MapperBase<,>).MakeGenericType(src, dto)
            .GetMethod("GetSourceItemByTargetItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        Mapper.ClearCache();
        CountingMapFromAttribute.Constructions = 0;
        wrapper.Invoke(null, new object?[] { src, targetProps[0], sourceProps, true, false, null, split });
        var wrapperSplitCost = CountingMapFromAttribute.Constructions;

        // ⑤ 包装器 null：自算路径（透传断裂则此值也为 0 且 ④ 增长——双向可判）
        Mapper.ClearCache();
        CountingMapFromAttribute.Constructions = 0;
        wrapper.Invoke(null, new object?[] { src, targetProps[0], sourceProps, true, false, null, null });
        var wrapperSelfCost = CountingMapFromAttribute.Constructions;

        return (splitCost, passthroughCost, selfComputeCost, wrapperSplitCost, wrapperSelfCost);
    }

    [Fact]
    public void M15_CountingProbe_SplitReusedWithinLoop()
    {
        Mapper.ClearCache();
        var (splitCost, passthroughCost, selfComputeCost, wrapperSplitCost, wrapperSelfCost) = RunM15CountingProbe();
        _out.WriteLine($"split={splitCost} passthrough={passthroughCost} self={selfComputeCost} wrapperSplit={wrapperSplitCost} wrapperSelf={wrapperSelfCost}");

        // 计数语义：2 属性同 DeclaringType —— M-14-B 负/正缓存使同 Type 反射恰 1 次（双效验证）；
        // 改造前语义（无缓存）为每属性 1 次 = 2。核心断言 = passthrough/wrapperSplit 的 0（M-15 复用实效）。
        Assert.Equal(1, splitCost);        // 前置：split 计算对 2 属性（同 Type）恰 1 次锚门反射（M-14-B 缓存实效）
        Assert.Equal(0, passthroughCost);  // 核心：split 传入 → 循环内零重算（M-15 实效）
        Assert.Equal(1, selfComputeCost);  // 对照：null = 自算（清缓存后同 Type 1 次反射）
        Assert.Equal(0, wrapperSplitCost); // ′：包装器透传实效（split 送达 PropertyMatcher）
        Assert.Equal(1, wrapperSelfCost);  // 包装器 null 自算正常（透传通道存在性双向证）
    }

    /// <summary>返回值对照静态载体（对照实验不涉 GC/计数——静态类 [MapProperty] 标注可直接标注）。</summary>
    [MapFrom(typeof(M15StaticSrc))]
    private class M15StaticSrc
    {
        public int A { get; set; }

        [MapProperty(typeof(M15StaticSrc), "B")]
        public int B { get; set; }
    }

    private class M15StaticDto
    {
        public int A { get; set; }
        public int B { get; set; }
    }

    /// <summary>喂入：返回值对照——split 传入与 null 自算对同一输入产出逐位一致（含 attr）。</summary>
    private static (PropertyInfo? ItemWithSplit, string? FromFieldWithSplit, PropertyInfo? ItemSelf, string? FromFieldSelf, int SrcPropCount, int WithCount)
    RunM15ReturnValueProbe()
    {
        // S12i2 口径：sourceIsDefine 分支的 sourceTypes = 承载侧（有锚类）属性——直接手工构造
        //（GetPropertyList 会按 S 侧退役语义过滤 [MapFrom] 类属性，与被测分支的构造无关——实测踩坑）
        var src = typeof(M15StaticSrc);
        var targetB = typeof(M15StaticDto).GetProperty("B")!;
        var sourceProps = new List<PropertyInfo>
        {
            src.GetProperty("A")!,
            src.GetProperty("B")!,
        };

        Mapper.ClearCache();
        var split = PropertyMatcher.SplitAttrsByAnchor(sourceProps);
        var withCount = split.WithAttr.Count;
        var item1 = PropertyMatcher.GetSourceItemByTargetItem(src, targetB, sourceProps, true, false, out var attr1, split);

        Mapper.ClearCache(); // 清缓存后自算路径走同样反射（结果应逐位一致）
        var item2 = PropertyMatcher.GetSourceItemByTargetItem(src, targetB, sourceProps, true, false, out var attr2, null);
        return (item1, attr1?.FromField, item2, attr2?.FromField, sourceProps.Count, withCount);
    }

    [Fact]
    public void M15_ReturnValue_BytewiseConsistent_SplitVsSelfCompute()
    {
        var (item1, field1, item2, field2, srcPropCount, withCount) = RunM15ReturnValueProbe();
        _out.WriteLine($"M15Return 诊断：srcProps={srcPropCount} withCount={withCount} item1={(item1?.Name ?? "null")} field1={field1 ?? "null"}");
        Assert.NotNull(item1);
        Assert.NotNull(field1); // 前置：with 分支确实消费标注（FromField="B" 匹配 targetItem.Name）
        Assert.Same(item1, item2);       // 返回 PropertyInfo 同一实例
        Assert.Equal(field1, field2);    // attr.FromField 一致
    }

    // ================================================================
    // 矩阵行 10（M-20 反射枚举消除）：静态 MethodInfo 重载形态断言（双处消歧生效）
    // ================================================================

    [Fact]
    public void M20_StaticMethodInfo_OverloadShapes()
    {
        var factory = typeof(ProjectionExpressionFactory);
        var selectDef = (MethodInfo)factory.GetField("s_selectDef", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var elemDef = (MethodInfo)factory.GetField("s_elementAtOrDefaultDef", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var toListDef = (MethodInfo)factory.GetField("s_toListDef", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        // Select：非索引 Func<,> 形态（当前实际选中——消歧生效）
        Assert.Equal(2, selectDef.GetParameters().Length);
        Assert.Equal(typeof(Func<,>), selectDef.GetParameters()[1].ParameterType.GetGenericTypeDefinition());
        // ElementAtOrDefault：Int32 形态（非 Index）
        Assert.Equal(2, elemDef.GetParameters().Length);
        Assert.Equal(typeof(int), elemDef.GetParameters()[1].ParameterType);
        // ToList：单参（匹配唯一，条件逐字保持）
        Assert.Single(toListDef.GetParameters());
    }

    // ================================================================
    // 矩阵行 11（边界场景）：FindByTarget(null) 异常行为与改造前一致（CD null 键契约）+
    // 空注册表空集
    // ================================================================

    [Fact]
    public void Boundary_FindByTarget_NullAndEmptyRegistry()
    {
        var snapshot = CaptureStore();
        try
        {
            RuntimeSnapshotStore.Clear();
            var t = BuildCollectibleType("BndDto", false, new[] { ("A", typeof(int)) }, null);
            Assert.Empty(RuntimeSnapshotStore.FindByTarget(t)); // 空注册表 → 空集
            Assert.Null(RuntimeSnapshotStore.GetPrimarySource(t));
            Assert.Throws<ArgumentNullException>(() => RuntimeSnapshotStore.FindByTarget(null!)); // CD null 键契约（改造前后同形态）
        }
        finally { RestoreStore(snapshot); }
    }
}
