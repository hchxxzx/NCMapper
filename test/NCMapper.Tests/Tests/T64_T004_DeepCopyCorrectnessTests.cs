// ============================================================================
// T64 / B5-5 Batch-4 — 深拷贝正确性红测
//（A1-07 手工委托环登记 / A3-07 ConcurrentDictionary Comparer 透传 / A3-08 含可变
//   引用成员 struct 非叶改判）
//
// 裁决（Q9=甲）：A3-08 仅运行时侧三处消费点改判（DeepCopyCompiler :653 /
// CollectionCopier :22 / DeepCopyTypeClassifier :343），共享规则 DeepCopyGraphRules 与
// SourceGen 物化期第四处不动（生成码零变化，G3 维持 IDENTICAL；生成期残留由
// B5-4/收尾批承接）。
//
// 缺陷承载（审计 20260917-001 -B5-5）：
//   A1-07  Mapper.cs:1109-1113 根登记在 genDeepCopy 返回后——手工注册委托
//          （RegisterGeneratedDeepCopy）递归重入时环回边不可见 → RecursionDepthExceeded；
//   A3-07  CollectionCopier.cs:273-288 无 Comparer 透传（OrdinalIgnoreCase 键语义丢）+
//          :285 TryAdd 静默丢键（深拷贝键等价类塌缩无观测面）；
//   A3-08  IsSimpleType = IsValueType || string 把含可变引用成员的 struct 当叶子浅赋值
//          （Items 引用共享）。
// ============================================================================

using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

[Collection("GlobalConfigState")]
public class T64_T004_DeepCopyCorrectnessTests
{
    // ============================== 测试承载类型（未映射 → 运行时路径） ==============================

    /// <summary>A1-07 环承载：自引用链</summary>
    private sealed class MNode { public MNode? Child { get; set; } }

    /// <summary>A3-08 直连：含可变引用成员的 struct（auto-prop → 非 readonly backing 字段）</summary>
    private struct SRef { public List<int> Items { get; set; } }

    /// <summary>A3-08 属性承载</summary>
    private sealed class BoxHolder { public SRef Box { get; set; } }

    /// <summary>A3-08 构造器承载（ctor 参数走 DeepCopyCompiler :260 构参发射）</summary>
    private sealed class CtorHolder
    {
        public SRef Box { get; }
        public CtorHolder(SRef box) => Box = box;
    }

    /// <summary>A3-08 环回承载：struct 引用成员反指外层对象</summary>
    private sealed class CycNode { public SBack Box { get; set; } }

    private struct SBack { public CycNode? Back { get; set; } }

    /// <summary>A3-08 边界锁定：readonly 引用字段 + string 字段——裁决不排除（保持值语义直拷）</summary>
    private struct ROLock
    {
        public readonly List<int>? RO;
        public string Name;
        public ROLock(List<int>? ro, string name) { RO = ro; Name = name; }
    }

    private sealed class BoxHolder2 { public ROLock Box { get; set; } }

    /// <summary>A3-07 键碰撞承载</summary>
    private sealed class CKey { public int Id { get; set; } }

    private sealed class ByIdComparer : IEqualityComparer<CKey>
    {
        public bool Equals(CKey? x, CKey? y) => x?.Id == y?.Id;
        public int GetHashCode(CKey obj) => obj.Id;
    }

    /// <summary>
    /// A3-07 碰撞构造用 stub provider——对 CKey 恒返同一固定实例（深拷贝键等价类
    /// 人为塌缩；正常组件下深拷贝保值语义、碰撞不可自然构造——构造分析见任务书
    /// B-B4 探索①），驱动 TryAdd 碰撞分支。
    /// </summary>
    private sealed class FixedKeyProvider : DeepCopyProviderBase
    {
        private readonly CKey _fixed;
        public FixedKeyProvider(CKey fixedKey) => _fixed = fixedKey;

        internal override T? DeepCopy<T>(T source, DeepCopyContext ctx) where T : class
            => typeof(T).IsInstanceOfType(_fixed) ? (T?)(object)_fixed : base.DeepCopy(source, ctx);

        protected override T CreateInstance<T>(T source) where T : class => throw new NotSupportedException();
        protected override void FillProperties<T>(T source, T target) where T : class => throw new NotSupportedException();
    }

    // 注：不设构造器 Mapper.ClearCache()——本类测试自足（私有类型对冷编译 + finally 显式
    // 注销委托/恢复 Provider），而 ClearCache 会清空 s_generatedValidateFuncs，与并行集合
    // （DeepCopyGlobalState）中 MapValidatorTests.ValidateOrThrow 的"注册→校验"窗口构成
    // 既有竞态（b24 门禁实测放大命中）。全局清理仅由 GlobalConfigState 序列化集合内
    // 既有测试承担。

    // ============================== A1-07 ==============================

    /// <summary>
    /// A1-07 红测：手工注册含环委托（root.Child 反指 root）——现状根登记在 genDeepCopy
    /// 返回后（Mapper.cs :1111-1113），递归重入 TryGetValue 落空 → 委托无限自递归 →
    /// RecursionDepthExceeded（红：抛异常）；修复后运行时兜底登记（哨兵 + 懒占位 + 回拷，
    /// provider "占位即最终对象"同款协议）→ 断言共享拷贝（copy.Child == copy）。
    /// </summary>
    [Fact]
    public void A1_07_手工环委托_递归重入共享拷贝()
    {
        Mapper.RegisterGeneratedDeepCopy<MNode>((src, ctx) =>
        {
            var d = new MNode();
            d.Child = Mapper.DeepCopy(src.Child!, ctx); // DeepCopy 内部对 null 源显式返回 null
            return d;
        });
        try
        {
            var root = new MNode();
            root.Child = root;

            var copy = Mapper.DeepCopy(root);

            Assert.NotNull(copy);
            Assert.NotSame(root, copy);
            // 环回边命中拷贝自身（共享拷贝语义）
            Assert.Same(copy, copy!.Child);
        }
        finally
        {
            Mapper.RegisterGeneratedDeepCopy<MNode>(null, false);
        }
    }

    /// <summary>
    /// A1-07 行为锁定：手工无环委托——返回委托自建实例（非源、非占位），无环回时
    /// 兜底登记不改变既有语义（哨兵被委托返回值原位替换）。
    /// </summary>
    [Fact]
    public void A1_07_手工无环委托_返回委托自建实例()
    {
        Mapper.RegisterGeneratedDeepCopy<MNode>((s, _) =>
        {
            var d = new MNode();
            if (s.Child != null)
                d.Child = new MNode { Child = s.Child.Child };
            return d;
        });
        try
        {
            var source = new MNode { Child = new MNode() };

            var copy = Mapper.DeepCopy(source);

            Assert.NotNull(copy);
            // 返回委托自建实例（非源、非占位）；一层子节点亦委托自建（二层为委托自留别名，不作断言）
            Assert.NotSame(source, copy);
            Assert.NotSame(source.Child, copy!.Child);
            Assert.NotNull(copy.Child);
        }
        finally
        {
            Mapper.RegisterGeneratedDeepCopy<MNode>(null, false);
        }
    }

    // ============================== A3-07 ==============================

    /// <summary>
    /// A3-07 红测：OrdinalIgnoreCase 源并发字典深拷贝——现状 new ConcurrentDictionary()
    /// 默认比较器（键查语义丢失：copy["ABC"] 落空，红）；修复后透传 sourceItems.Comparer
    ///（254 CopyDictionary 同款）。
    /// </summary>
    [Fact]
    public void A3_07_并发字典Comparer透传_键语义保持()
    {
        var src = new ConcurrentDictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["abc"] = new List<int> { 1, 2 },
        };

        var copy = CollectionCopier.CopyConcurrentDictionary(src, new DeepCopyContext());

        Assert.NotNull(copy);
        Assert.Equal(1, copy!.Count);
        // 大小写不敏感键语义保持（现状红：默认 Ordinal 比较器）
        Assert.True(copy!.ContainsKey("ABC"));
        Assert.True(copy.ContainsKey("aBc"));
        // 值深拷贝既有语义不回归
        Assert.NotSame(src["abc"], copy["abc"]);
    }

    /// <summary>
    /// A3-07 红测：默认比较器源双键不误并（条目保全——透传后无伪碰撞/静默丢键）
    /// + 碰撞分支显式策略（首值保留 + MapTracer 留痕，不再静默）——碰撞经
    /// FixedKeyProvider 塌缩键等价类构造。
    /// </summary>
    [Fact]
    public void A3_07_并发字典碰撞_首值保留且留痕()
    {
        // 1) 默认比较器：双键并存（条目保全锁定）
        var plain = new ConcurrentDictionary<string, int> { ["abc"] = 1, ["ABC"] = 2 };
        var plainCopy = CollectionCopier.CopyConcurrentDictionary(plain, new DeepCopyContext());
        Assert.NotNull(plainCopy);
        Assert.Equal(2, plainCopy!.Count);

        // 2) 碰撞分支：键深拷贝塌缩到同一实例 → TryAdd 落空
        var sw = new StringWriter();
        var oldEnabled = MapTracer.GloballyEnabled;
        var oldOutput = MapTracer.GlobalOutput;
        var oldProvider = DeepCopyProviderBase.Current;
        MapTracer.GloballyEnabled = true;
        MapTracer.GlobalOutput = sw;
        DeepCopyProviderBase.Current = new FixedKeyProvider(new CKey { Id = 0 });
        try
        {
            var src = new ConcurrentDictionary<CKey, string>(new ByIdComparer())
            {
                [new CKey { Id = 1 }] = "a",
                [new CKey { Id = 2 }] = "b",
            };

            var copy = CollectionCopier.CopyConcurrentDictionary(src, new DeepCopyContext());

            Assert.NotNull(copy);
            // 键塌缩到同等价类 → 碰撞后首值保留（现状红于"静默"：count/首值两态同值，
            // 透传 + 碰撞显式策略 = count 1 + 首值 + 留痕三断言联合锁定）
            Assert.Equal(1, copy!.Count);
            Assert.Equal("a", copy.Values.Single());
            // 碰撞显式留痕（不再静默丢键）
            Assert.Contains("碰撞", sw.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = oldEnabled;
            MapTracer.GlobalOutput = oldOutput;
            DeepCopyProviderBase.Current = oldProvider;
        }
    }

    // ============================== A3-08 ==============================

    /// <summary>
    /// A3-08 红测：类属性为含可变引用成员的 struct——现状 IsSimpleType=值类型→叶子浅赋值
    ///（copy.Box.Items 与源同一 List 引用，红）；修复后属性发射改判走值类型分支逐成员
    /// 深拷贝（引用成员经对象通道），断言 Items 引用独立 + 值相等。
    /// </summary>
    [Fact]
    public void A3_08_含可变引用字段struct属性_逐成员深拷贝()
    {
        var src = new BoxHolder { Box = new SRef { Items = new List<int> { 1, 2, 3 } } };

        var copy = Mapper.DeepCopy(src);

        Assert.NotNull(copy);
        Assert.NotSame(src.Box.Items, copy!.Box.Items);
        Assert.Equal(new List<int> { 1, 2, 3 }, copy.Box.Items);
    }

    /// <summary>
    /// A3-08 红测：构造器参数为含可变引用成员的 struct（DeepCopyCompiler :260 构参发射）
    ///——现状构参直传共享引用（红）；修复后构参值类型分支逐成员深拷贝。
    /// </summary>
    [Fact]
    public void A3_08_含可变引用字段struct构参_逐成员深拷贝()
    {
        var src = new CtorHolder(new SRef { Items = new List<int> { 7, 8 } });

        var copy = Mapper.DeepCopy(src);

        Assert.NotNull(copy);
        Assert.NotSame(src.Box.Items, copy!.Box.Items);
        Assert.Equal(new List<int> { 7, 8 }, copy.Box.Items);
    }

    /// <summary>
    /// A3-08 红测：集合元素为含可变引用成员的 struct——现状 IsValueType&lt;T&gt;=值类型→
    /// 元素直拷（Items 引用共享，红）；修复后元素经对象通道（CopyStructBoxed）逐成员深拷贝。
    /// </summary>
    [Fact]
    public void A3_08_集合元素struct_深拷贝独立()
    {
        var src = new List<SRef>
        {
            new() { Items = new List<int> { 1 } },
            new() { Items = new List<int> { 2 } },
        };

        var copy = Mapper.DeepCopy(src);

        Assert.NotNull(copy);
        Assert.Equal(2, copy!.Count);
        Assert.NotSame(src[0].Items, copy[0].Items);
        Assert.NotSame(src[1].Items, copy[1].Items);
        Assert.Equal(1, copy[0].Items.Single());
        Assert.Equal(2, copy[1].Items.Single());
    }

    /// <summary>
    /// A3-08 红测：struct 引用成员环回反指外层对象——现状 struct 叶子浅赋值 →
    /// copy.Box.Back == 源对象（跨拷贝共享，红）；修复后 struct 非叶逐成员深拷贝，
    /// 引用成员经 Fast 环形缓存命中外层拷贝 → copy.Box.Back == copy。
    /// </summary>
    [Fact]
    public void A3_08_struct引用成员环回_命中外层拷贝()
    {
        var a = new CycNode();
        a.Box = new SBack { Back = a };

        var copy = Mapper.DeepCopy(a);

        Assert.NotNull(copy);
        Assert.Same(copy, copy!.Box.Back);
    }

    /// <summary>
    /// A3-08 边界锁定（Q9=甲裁决口径）：readonly 引用字段与 string 字段不触发排除——
    /// 该类 struct 保持叶子值语义直拷（RO/Name 与源同引用），既有行为不变。
    /// </summary>
    [Fact]
    public void A3_08_边界锁定_readonly引用与string字段struct保持直拷()
    {
        var ro = new List<int> { 9 };
        var src = new BoxHolder2 { Box = new ROLock(ro, "n") };

        var copy = Mapper.DeepCopy(src);

        Assert.NotNull(copy);
        // readonly 引用字段：裁决不排除 → 值语义直拷（同引用锁定）
        Assert.Same(ro, copy!.Box.RO);
        Assert.Same("n", copy.Box.Name);
    }
}
