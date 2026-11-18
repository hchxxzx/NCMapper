// ============================================================================
// 联合回归矩阵（M1~M12）
//
// 覆盖矩阵（横切维度：接口字典声明 × 派生集合声明 ×
// 深拷贝三 Provider × 映射路径）——硬约束（006 硬约束 3）：
// 共享 CollectionTypeDetector 判定面，须同批联合回归锁定组合行为，
// 避免"各自验收通过但组合分叉"。
//
// 非 JSON 断言实现说明（任务书 M1/M5 口径，"探明既有捕获惯例后落"）：
//   深拷贝侧（M1）：MapTracer 全局输出捕获（既有惯例 dotpathgoldentripletests/
//   AuditAcceptanceTests）—— JSON 回退的前兆 trace（CreateInstanceCore 的
//   「FormatterServices.GetUninitializedObject 失败」）不出现。反向验证：
// 禁用 接口转发（变异）→ 接口类型 GetUninitializedObject 抛
//   MemberAccessException → trace 写入 → 本断言必红（断言对 JSON 路径真实敏感）。
//   映射侧（M5）：行为断言（映射成功 + 元素转换正确 + 产物为具体字典实例）；
//   元素级复杂 K/V 映射（M6）与 M1 Trace 断言共同构成非 JSON 路径证明——
//  JSON 往返无法完成自定义类元素级映射（差异登记 D2：M5 断言强度
//   受映射侧无可观测 JSON 检测点限制，由 M1/M6 承接）。
//
// 三 Provider 环值契约（M2）：Json Provider 不支持循环引用（既有契约，
// JsonDeepCopyProvider 类注释）——环值输入抛 MapException（inner
// JsonException），不参与 Fast/Expression 一致性断言（矩阵登记口径）。
//
// M12 判定面三方对账的已知语义差异（D3 登记）：
//   NavigationResolver.IsDictionaryType（private，经反射对账）仅识别泛型字典
//   （IDictionary<,>/IReadOnlyDictionary<,>/Dictionary<,> 自身或接口链），
//   非泛型 IDictionary 形态（Hashtable/SortedList）判 false —— 该方法服务于
//   GetCollectionElementType 的字典排除（元素/KV 解包），非泛型字典无元素
// 类型可提取（GetCollectionElementType 返回 null → 落 兜底守卫），
//   语义自洽；detector（收敛 IsDictionaryShape）与验证器口径一致判 true。
//   差异为判定面职责差异（元素解包 vs 字典分派），非缺陷。
//
// M11 具体类回归锁定：Dictionary<K,V> 具体类声明的全量行为由既有套件锁定
//   （本批次全量 1900+ 用例全绿），此处对照"接口声明 vs 具体类声明"行为
// 分叉消除。
// ============================================================================

using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using NCMapper.Shared;
using Xunit;

namespace NCMapper.Tests.Tests;

public class DeepCopyB04B05JointMatrixTests
{
    // ================================================================
    // M1：IDictionary<K,V> × 深拷贝（Fast/Expression/Json）× 简单 K/V
    // ================================================================

    /// <summary>
    /// M1 — 接口字典属性（简单 K/V）深拷贝：三 Provider 结果内容一致、
    /// 产物为独立实例；Trace 断言 JSON 回退前兆（GetUninitializedObject 失败）
    /// 不出现（非 JSON 路径）。
    /// </summary>
    [Fact]
    public void M1_接口字典深拷贝_简单KV_三Provider一致_非JSON路径()
    {
        var src = new JmNode { Items = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" } };

        var originalEnabled = MapTracer.GloballyEnabled;
        var originalOutput = MapTracer.GlobalOutput;
        MapTracer.GloballyEnabled = true;
        var trace = new StringWriter();
        MapTracer.GlobalOutput = trace;
        try
        {
            var copyFast = RunWithProvider(new FastDeepCopyProvider(), () => Mapper.DeepCopy(src))!;
            var copyExpr = RunWithProvider(new ExpressionTreeDeepCopyProvider(), () => Mapper.DeepCopy(src))!;
            var copyJson = RunWithProvider(DeepCopyProviderBase.JsonProvider, () => Mapper.DeepCopy(src))!;

            // 三 Provider 结果内容一致（元素集合相等 + 独立实例 + 产物为字典形态）
            foreach (var copy in new[] { copyFast, copyExpr, copyJson })
            {
                Assert.NotSame(src, copy);
                Assert.Equal(2, copy.Items.Count);
                Assert.Equal("1", copy.Items["a"]);
                Assert.Equal("2", copy.Items["b"]);
                Assert.IsAssignableFrom<IDictionary<string, string>>(copy.Items);
            }
            Assert.NotSame(copyFast.Items, copyExpr.Items);
            Assert.NotSame(src.Items, copyFast.Items);

            // 非 JSON 路径（Trace 断言）：JSON 回退前兆 trace 不出现
            // （接口类型经 GetUninitializedObject 兜底时触发；接口转发修复后必不出现）
            Assert.DoesNotContain("GetUninitializedObject 失败", trace.ToString());
        }
        finally
        {
            MapTracer.GloballyEnabled = originalEnabled;
            MapTracer.GlobalOutput = originalOutput;
        }
    }

    // ================================================================
    // M2：IDictionary<K,V> × 深拷贝 × 复杂 K/V（Child 引用值 + 环引用值）
    // ================================================================

    /// <summary>
    /// M2 — 接口字典属性（复杂 K/V + 环引用值）深拷贝：Fast/Expression 值深拷贝
    /// （引用不等）、环回边引用一致、不栈溢出；Json Provider 环值抛 MapException
    /// （inner JsonException，既有契约——不支持循环引用）。
    /// </summary>
    [Fact]
    public void M2_接口字典深拷贝_复杂KV环值_值深拷贝_环保持_不栈溢出()
    {
        var a = new JmChild { Name = "a" };
        var b = new JmChild { Name = "b" };
        a.Next = b;
        b.Next = a; // 循环引用值
        var src = new JmComplexIfNode { Items = new Dictionary<string, JmChild> { ["x"] = a } };

        var copyFast = RunWithProvider(new FastDeepCopyProvider(), () => Mapper.DeepCopy(src))!;
        var copyExpr = RunWithProvider(new ExpressionTreeDeepCopyProvider(), () => Mapper.DeepCopy(src))!;

        foreach (var copy in new[] { copyFast, copyExpr })
        {
            Assert.Single(copy.Items);
            var value = copy.Items["x"];
            Assert.NotSame(a, value);              // 值深拷贝（引用不等）
            Assert.Equal("a", value.Name);
            Assert.Equal("b", value.Next!.Name);
            Assert.Same(value, value.Next!.Next);  // 环回边引用一致（环保持，不栈溢出）
        }

        // Json Provider 既有契约：环值不支持（序列化遇环抛 JsonException，包装为 MapException）
        var jsonEx = Assert.Throws<MapException>(() =>
        {
            RunWithProvider(DeepCopyProviderBase.JsonProvider, () => Mapper.DeepCopy(src));
        });
        Assert.IsType<JsonException>(jsonEx.InnerException);
    }

    // ================================================================
    // M3：IDictionary<K,V> × 深拷贝 × 自定义类键
    // ================================================================

    /// <summary>
    /// M3 — 接口字典属性（自定义类键）深拷贝：键深拷贝（新实例）且键等价
    /// （字段守恒），值内容完整（CopyDictionaryByKeyValue 复杂 K/V 路径）。
    /// </summary>
    [Fact]
    public void M3_接口字典深拷贝_自定义类键_键深拷贝后键等价()
    {
        var key = new JmKey { Id = 7 };
        var src = new JmKeyNode { Items = new Dictionary<JmKey, string> { [key] = "v" } };

        var copy = Mapper.DeepCopy(src)!;

        Assert.Single(copy.Items);
        var copiedKey = copy.Items.Keys.Single();
        Assert.NotSame(key, copiedKey);   // 键深拷贝（引用不等）
        Assert.Equal(7, copiedKey.Id);    // 键等价（字段守恒）
        Assert.Equal("v", copy.Items[copiedKey]);
    }

    // ================================================================
    // M4：IDictionary<K,V> × 映射（同型）
    // ================================================================

    /// <summary>
    /// M4 — 接口字典属性同型映射：走字典表达式（GetDictionaryExpression）成功，
    /// 无 NotSupportedException；产物为具体字典实例，内容完整。
    /// </summary>
    [Fact]
    public void M4_接口字典属性_同型映射_走字典表达式成功()
    {
        var node = new JmChild { Name = "n1" };
        var src = new JmMapSrc { Items = new Dictionary<string, JmChild> { ["a"] = node } };

        var dto = Mapper.Map<JmMapSrc, JmMapDto>(src)!;

        Assert.NotNull(dto.Items);
        Assert.Single(dto.Items!);
        Assert.Equal("n1", dto.Items!["a"].Name);
        Assert.IsType<Dictionary<string, JmChild>>(dto.Items);  // 字典表达式路径产物（原缺陷：NotSupportedException 构建期失败）

        // 值实例语义对照：具体类跨型源（SortedDictionary→Dictionary，同为 cross-type 字典
        // 路径）与接口声明源经同一 GetDictionaryExpression→ConvertDictionary 路径，语义一致
        //。同型复杂 K/V 值实例共享为 ConvertDictEntry
        // 既有契约（同型值直接复用，异型值元素级映射），非本任务范围
        var srcCt = new JmSortedCtSrc { Items = new SortedDictionary<string, JmChild> { ["a"] = node } };
        var dtoCt = Mapper.Map<JmSortedCtSrc, JmMapDto>(srcCt)!;
        Assert.Equal("n1", dtoCt.Items!["a"].Name);
        Assert.Equal(
            ReferenceEquals(node, dto.Items!["a"]),
            ReferenceEquals(node, dtoCt.Items!["a"]));
    }

    // ================================================================
    // M5：IDictionary<K,V> × 映射（异型：K/V 简单）
    // ================================================================

    /// <summary>
    /// M5 — 接口字典属性异型映射（K/V 简单：string→int 值元素转换）：映射成功、
    /// 元素转换正确、产物为具体字典实例（非 JSON 劣化——证明由 M6 元素级复杂
    /// 映射与 M1 Trace 断言承接，见文件头非 JSON 断言说明）。
    /// </summary>
    [Fact]
    public void M5_接口字典属性_异型映射_KV简单_映射成功且元素转换正确()
    {
        var src = new JmSimpleSrc { Items = new Dictionary<string, string> { ["n"] = "42" } };

        var dto = Mapper.Map<JmSimpleSrc, JmSimpleDto>(src)!;

        Assert.NotNull(dto.Items);
        Assert.Single(dto.Items!);
        Assert.Equal(42, dto.Items!["n"]);  // 值元素 string → int 转换（元素级）
        Assert.IsType<Dictionary<string, int>>(dto.Items);
    }

    // ================================================================
    // M6：IDictionary<K,V> × 映射（异型：K/V 复杂）
    // ================================================================

    /// <summary>
    /// M6 — 接口字典属性异型映射（K/V 复杂：JmA→JmB 元素级映射）：映射成功、
    /// 元素属性完整——元素级自定义类映射为字典表达式路径的决定性证明
    /// （JSON 往返无法完成元素级映射）。
    /// </summary>
    [Fact]
    public void M6_接口字典属性_异型映射_KV复杂_元素级映射成功()
    {
        var src = new JmComplexSrc { Items = new Dictionary<string, JmA> { ["a"] = new JmA { Name = "x", Tag = 1 } } };

        var dto = Mapper.Map<JmComplexSrc, JmComplexDto>(src)!;

        Assert.NotNull(dto.Items);
        Assert.Single(dto.Items!);
        Assert.Equal("x", dto.Items!["a"].Name);  // 元素级映射（JmA → JmB）
        Assert.Equal(1, dto.Items!["a"].Tag);
    }

    // ================================================================
    // M7：IReadOnlyDictionary<K,V> × 深拷贝/映射（收敛口径行为一致）
    // ================================================================

    /// <summary>
    /// M7 — 只读接口字典（IReadOnlyDictionary<K,V>）深拷贝与映射：与 IDictionary
    /// 行为一致（收敛后 IsDictionary 均判 true → 同走字典路径；深拷贝经接口转发
    /// → 运行时具体字典）。
    /// </summary>
    [Fact]
    public void M7_只读接口字典_深拷贝与映射_与IDictionary行为一致()
    {
        // 深拷贝：IReadOnlyDictionary 接口声明属性（接口转发 → 运行时具体字典路径）
        var src = new JmRoNode { Items = new Dictionary<string, string> { ["k"] = "v" } };
        var copy = Mapper.DeepCopy(src)!;
        Assert.Single(copy.Items);
        Assert.Equal("v", copy.Items["k"]);
        Assert.NotSame(src.Items, copy.Items);

        // 映射：IReadOnlyDictionary 源属性走字典表达式（收敛口径，与 IDictionary 同路径）
        var msrc = new JmRoMapSrc { Items = new Dictionary<string, JmA> { ["a"] = new JmA { Name = "n" } } };
        var dto = Mapper.Map<JmRoMapSrc, JmMapDto>(msrc)!;
        Assert.Single(dto.Items!);
        Assert.Equal("n", dto.Items!["a"].Name);
    }

    // ================================================================
    // M8：OrderList : List<string> × 深拷贝（Fast/Expression/Json）
    // ================================================================

    /// <summary>
    /// M8 — 非泛型集合派生类（OrderList 形态）深拷贝：三 Provider 均内容守恒
    /// （Count==2 + 元素一致）且后续 Add 不抛 NRE（原缺陷：空壳内部数组
    /// 未初始化 → NRE）。
    /// </summary>
    [Fact]
    public void M8_派生集合OrderList_三Provider深拷贝_内容守恒且Add不NRE()
    {
        var src = new JmOrderList { "o1", "o2" };

        var copyFast = RunWithProvider(new FastDeepCopyProvider(), () => Mapper.DeepCopy(src))!;
        var copyExpr = RunWithProvider(new ExpressionTreeDeepCopyProvider(), () => Mapper.DeepCopy(src))!;
        var copyJson = RunWithProvider(DeepCopyProviderBase.JsonProvider, () => Mapper.DeepCopy(src))!;

        foreach (var copy in new[] { copyFast, copyExpr, copyJson })
        {
            Assert.Equal(2, copy.Count);
            Assert.Equal("o1", copy[0]);
            Assert.Equal("o2", copy[1]);
 copy.Add("o3"); // 不抛 NRE
            Assert.Equal(3, copy.Count);
        }
        Assert.NotSame(copyFast, copyExpr);
        Assert.NotSame(src, copyFast);
    }

    // ================================================================
    // M9：Collection<T> 派生 × 深拷贝
    // ================================================================

    /// <summary>
    /// M9 — Collection&lt;string&gt; 派生类深拷贝：内容守恒（Count==2）且后续
    /// Add 不抛 NRE（与 M8 同为 两阶段路径，覆盖另一基类形态）。
    /// </summary>
    [Fact]
    public void M9_Collection派生类_深拷贝_内容守恒且Add不NRE()
    {
        var src = new JmCollDerived { "c1", "c2" };

        var copy = Mapper.DeepCopy(src)!;

        Assert.Equal(2, copy.Count);
        Assert.Equal("c1", copy[0]);
        Assert.Equal("c2", copy[1]);
        copy.Add("c3");                  // 不抛 NRE
        Assert.Equal(3, copy.Count);
    }

    // ================================================================
    // M10：OrderList × 映射（元素级）
    // ================================================================

    /// <summary>
    /// M10 — 派生集合属性元素级映射（OrderObjList : List&lt;JmA&gt; → List&lt;JmB&gt）：
    /// GetListExpression 元素级映射成功，元素完整。字符串元素形态（List&lt;string&gt; 派生 →
    /// List&lt;string&gt）实测触发预存缺陷（string 目标元素 MapList 构造崩溃，Q1
    /// 登记——范围外），元素级映射以复杂元素形态承载（矩阵 M10 意图）。
    /// </summary>
    [Fact]
    public void M10_OrderList映射_元素级映射成功()
    {
        var src = new JmListMapSrc
        {
            Items = new JmOrderObjList
            {
                new JmA { Name = "a", Tag = 1 },
                new JmA { Name = "b", Tag = 2 },
            }
        };

        var dto = Mapper.Map<JmListMapSrc, JmListMapDto>(src)!;

        Assert.NotNull(dto.Items);
        Assert.Equal(2, dto.Items!.Count);
        Assert.Equal("a", dto.Items[0].Name);  // 元素级映射（JmA → JmB）
        Assert.Equal(1, dto.Items[0].Tag);
        Assert.Equal("b", dto.Items[1].Name);
        Assert.Equal(2, dto.Items[1].Tag);
        Assert.IsType<List<JmB>>(dto.Items);    // 元素级映射产物
    }

    // ================================================================
    // M11：Dictionary<K,V> 具体类声明对照（回归锁定）
    // ================================================================

    /// <summary>
    /// M11 — 具体类字典声明与接口声明行为一致；
    /// 具体类全量行为由既有套件锁定（本批次全量 1900+ 用例全绿），此处锁定
    /// 同输入对照一致性。
    /// </summary>
    [Fact]
    public void M11_具体类字典声明_与接口声明行为一致_回归锁定()
    {
        var a = new JmChild { Name = "a" };
        var srcIf = new JmComplexIfNode { Items = new Dictionary<string, JmChild> { ["k"] = a } };
        var srcCt = new JmConcreteNode { Items = new Dictionary<string, JmChild> { ["k"] = a } };

        var copyIf = Mapper.DeepCopy(srcIf)!;
        var copyCt = Mapper.DeepCopy(srcCt)!;

        Assert.Equal(1, copyIf.Items.Count);
        Assert.Equal(1, copyCt.Items.Count);
        Assert.Equal("a", copyIf.Items["k"].Name);
        Assert.Equal("a", copyCt.Items["k"].Name);
        Assert.NotSame(a, copyIf.Items["k"]);   // 接口声明：值深拷贝
        Assert.NotSame(a, copyCt.Items["k"]);   // 具体类声明：值深拷贝（行为一致）
    }

    // ================================================================
    // M12：判定面三方对账（detector vs IsDictionaryShape vs NavigationResolver）
    // ================================================================

    /// <summary>
    /// M12 — 判定面三方对账：CollectionTypeDetector.IsDictionary（收敛后）与
    /// TypeCompatAdapter.IsDictionaryShape 在判定矩阵语料上全一致（含自定义
    /// 同名类型不误判）；NavigationResolver.IsDictionaryType（private，反射对账）
    /// 泛型字典语料一致，非泛型 IDictionary 形态（Hashtable/SortedList）差异为
    /// 职责差异（见文件头 D3 登记说明）。
    /// </summary>
    [Fact]
    public void M12_判定面三方对账_全语料口径一致_差异逐条定性()
    {
        // 字典语料（detector 与 IsDictionaryShape 应一致判 true）
        var dictCorpus = new[]
        {
 typeof(IDictionary<string, object>), // 接口自身
            typeof(IReadOnlyDictionary<string, object>),  // 只读接口自身（收敛口径，原 false）
            typeof(Dictionary<string, object>),           // 具体泛型字典
            typeof(SortedDictionary<string, object>),     // SortedDictionary（接口链命中）
            typeof(JmReadOnlyDictImpl),                   // 仅实现 IReadOnlyDictionary 的自定义类（D1.3 #6 收敛变化点）
            typeof(Hashtable),                            // 非泛型字典（IsDictionaryShape 含非泛型 IDictionary）
            typeof(SortedList),
        };
        // 非字典语料（应一致判 false）
        var nonDictCorpus = new[]
        {
            typeof(List<string>),                 // 普通集合
            typeof(string),                       // string（IsCollectionType 特判排除）
            typeof(JmNode),                       // 普通类
            typeof(int),                          // 值类型
            typeof(JmShadow.IDictionary<,>),      // 自定义同名 IDictionary<,>（L-05 命名空间守卫）
        };

        // ① detector（收敛后）与 IsDictionaryShape 全语料一致
        foreach (var t in dictCorpus)
        {
            Assert.True(CollectionTypeDetector.IsDictionary(t), $"字典语料误判 false: {t}");
            Assert.True(TypeCompatAdapter.IsDictionaryShape(t), $"IsDictionaryShape 误判 false: {t}");
        }
        foreach (var t in nonDictCorpus)
        {
            Assert.False(CollectionTypeDetector.IsDictionary(t), $"非字典语料误判 true: {t}");
            Assert.False(TypeCompatAdapter.IsDictionaryShape(t), $"IsDictionaryShape 误判 true: {t}");
        }

        // ② NavigationResolver.IsDictionaryType（反射对账）：泛型字典语料一致；
        //    非泛型 IDictionary 形态（Hashtable/SortedList）判 false —— 职责差异
        //    （元素解包口径，见文件头说明），非缺陷
        var navExpectTrue = new[]
        {
            typeof(IDictionary<string, object>), typeof(IReadOnlyDictionary<string, object>),
            typeof(Dictionary<string, object>), typeof(SortedDictionary<string, object>), typeof(JmReadOnlyDictImpl),
        };
        var navExpectFalse = new[]
        {
            typeof(Hashtable), typeof(SortedList),  // 已知差异（D3 登记）
            typeof(List<string>), typeof(string), typeof(JmNode), typeof(int),
            typeof(JmShadow.IDictionary<,>),
        };
        foreach (var t in navExpectTrue)
            Assert.True(NavIsDictionary(t), $"NavigationResolver 字典语料误判 false: {t}");
        foreach (var t in navExpectFalse)
            Assert.False(NavIsDictionary(t), $"NavigationResolver 非字典语料误判 true: {t}");
    }

    // ================================================================
    // 辅助
    // ================================================================

    /// <summary>在指定 Provider 下执行动作（保存/恢复 Current，既有惯例）。</summary>
    private static T RunWithProvider<T>(DeepCopyProviderBase provider, Func<T> action)
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = provider;
            return action();
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    /// <summary>反射调用 NavigationResolver.IsDictionaryType（private，不触碰 src/，平移 CrashProbe 反射惯例）。</summary>
    private static bool NavIsDictionary(Type type)
    {
        var method = typeof(NavigationResolver).GetMethod("IsDictionaryType", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NavigationResolver.IsDictionaryType 未找到");
        return (bool)method.Invoke(null, new object[] { type })!;
    }

    // ================================================================
    // 模型类型（单文件自含）
    // ================================================================

    /// <summary>M1 模型：接口字典属性（简单 K/V）。</summary>
    private sealed class JmNode
    {
        public IDictionary<string, string> Items { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>复杂值模型：Name + Next（环引用用）。</summary>
    private sealed class JmChild
    {
        public string Name { get; set; } = "";
        public JmChild? Next { get; set; }
    }

    /// <summary>M2/M11 模型：接口字典属性（复杂 K/V，值为 JmChild）。</summary>
    private sealed class JmComplexIfNode
    {
        public IDictionary<string, JmChild> Items { get; set; } = new Dictionary<string, JmChild>();
    }

    /// <summary>M3 模型：接口字典属性（自定义类键）。</summary>
    private sealed class JmKeyNode
    {
        public IDictionary<JmKey, string> Items { get; set; } = new Dictionary<JmKey, string>();
    }

    /// <summary>M3 自定义类键。</summary>
    private sealed class JmKey
    {
        public int Id { get; set; }
    }

    /// <summary>M4/M6/M7 映射源：接口字典属性（复杂 K/V）。</summary>
    private sealed class JmMapSrc
    {
        public IDictionary<string, JmChild> Items { get; set; } = new Dictionary<string, JmChild>();
    }

    /// <summary>M4/M6/M7 映射目标：具体字典属性。</summary>
    private sealed class JmMapDto
    {
        public Dictionary<string, JmChild>? Items { get; set; }
    }

    /// <summary>M4 对照源：具体类跨型字典属性（SortedDictionary，cross-type 路径对照）。</summary>
    private sealed class JmSortedCtSrc
    {
        public SortedDictionary<string, JmChild> Items { get; set; } = new SortedDictionary<string, JmChild>();
    }

    /// <summary>M5 映射源：接口字典属性（简单 K/V）。</summary>
    private sealed class JmSimpleSrc
    {
        public IDictionary<string, string> Items { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>M5 映射目标：具体字典属性（值元素 string → int 转换）。</summary>
    private sealed class JmSimpleDto
    {
        public Dictionary<string, int>? Items { get; set; }
    }

    /// <summary>M6 异型复杂 K/V 源元素类型（public 嵌套：作 public 派生类 JmOrderObjList 的基类泛型参数，CS9338 可访问性一致）。</summary>
    public sealed class JmA
    {
        public string Name { get; set; } = "";
        public int Tag { get; set; }
    }

    /// <summary>M6 异型复杂 K/V 目标元素类型（元素级映射）。</summary>
    private sealed class JmB
    {
        public string Name { get; set; } = "";
        public int Tag { get; set; }
    }

    /// <summary>M6 映射源：接口字典属性（复杂 K/V，JmA 值）。</summary>
    private sealed class JmComplexSrc
    {
        public IDictionary<string, JmA> Items { get; set; } = new Dictionary<string, JmA>();
    }

    /// <summary>M6 映射目标：具体字典属性（JmB 值）。</summary>
    private sealed class JmComplexDto
    {
        public Dictionary<string, JmB>? Items { get; set; }
    }

    /// <summary>M7 深拷贝模型：只读接口字典属性。</summary>
    private sealed class JmRoNode
    {
        public IReadOnlyDictionary<string, string> Items { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>M7 映射源：只读接口字典属性。</summary>
    private sealed class JmRoMapSrc
    {
        public IReadOnlyDictionary<string, JmA> Items { get; set; } = new Dictionary<string, JmA>();
    }

    /// <summary>M8 非泛型集合派生类（OrderList 形态：继承泛型基类、自身非泛型，string 元素）。</summary>
    private sealed class JmOrderList : List<string>;

    /// <summary>M9 Collection 派生类形态。</summary>
    private sealed class JmCollDerived : Collection<string>;

    /// <summary>M10 映射源：派生集合属性（复杂元素）。</summary>
    private sealed class JmListMapSrc
    {
        public JmOrderObjList Items { get; set; } = new();
    }

    /// <summary>M10 映射目标：List 属性（元素级映射）。</summary>
    private sealed class JmListMapDto
    {
        public List<JmB>? Items { get; set; }
    }

    /// <summary>M10 非泛型集合派生类（复杂元素形态：List&lt;JmA&gt; 派生）。</summary>
    /// <para>public 嵌套：映射路径要求属性类型可见（GetPropertyList 过滤非 public 属性类型——
    /// private 嵌套类型属性被滤除即"源属性未找到"跳过）；深拷贝路径（M8）无此过滤。</para>
    /// <para>字符串元素形态（List&lt;string&gt; 派生 → List&lt;string&gt）实测触发预存缺陷：
    /// MapperBase&lt;string,string&gt;.MapList 的 BuildConstructorArgs 对 string 的
    /// string(char*) 构造参数生成 Default(char*) 抛 ArgumentException
    /// （Q1 登记，范围外）；元素级映射以复杂元素形态承载。</para>
    public sealed class JmOrderObjList : List<JmA>;

    /// <summary>M11 具体类声明对照模型。</summary>
    private sealed class JmConcreteNode
    {
        public Dictionary<string, JmChild> Items { get; set; } = new Dictionary<string, JmChild>();
    }

    /// <summary>M12 语料：仅实现 IReadOnlyDictionary 的自定义类（D1.3 #6 收敛变化点形态）。</summary>
    private sealed class JmReadOnlyDictImpl : IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, string> _inner = new();
        public string this[string key] => _inner[key];
        public IEnumerable<string> Keys => _inner.Keys;
        public IEnumerable<string> Values => _inner.Values;
        public int Count => _inner.Count;
        public bool ContainsKey(string key) => _inner.ContainsKey(key);
        public bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => _inner.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>M12 语料：自定义同名 IDictionary&lt;,&gt;（L-05 命名空间守卫——非 System.Collections 命名空间不误判）。</summary>
    private static class JmShadow
    {
        internal interface IDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
        {
        }
    }
}
