// （G2+D1）：四消费点注册表改读测试——
//   消费点一 TreeBuilder.ResolveConfig / 消费点二 ProjectionExpressionFactory.IsTreeChildrenSlot /
//   消费点三 EFCore ClearTreeChildrenSlot·IncludeExcludingTreeSlot（同口径双读，集成面由 ORM 套件承接）/
//   消费点四 NavigationTreeCollector 属性级 NoAutoInclude。
//   隔离：GlobalConfigState 集合 + 自足注册（RuntimeSnapshotStore.Register）+ Clear + try/finally。
using System.Linq.Expressions;
using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class TreeConfigConsumptionTests
{
    // ===== fixture：树构建配置（Fluent .Tree 承载，自引用树形）=====

    public class TreeEntity
    {
        public int Key { get; set; }
        public int? ParentKey { get; set; }
        public string Name { get; set; } = "";
    }

    public class TreeDto
    {
        public int Key { get; set; }
        public int? ParentKey { get; set; }
        public string Name { get; set; } = "";
        public List<TreeDto> Nodes { get; set; } = new();
    }

    /// <summary>opt-in 负向 fixture：未声明 .Tree()，但恰有同名自引用集合属性（场景）</summary>
    public class OptInEntity
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<OptInEntity> Children { get; set; } = new();
    }

    public class OptInDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public List<OptInDto> Children { get; set; } = new();
    }

    /// <summary>[MapTree] 特性侧回归 fixture（反射回退腿；T5 补锚——承载侧合法形态）</summary>
    [MapFrom(typeof(TreeDto))]
    [MapTree("Key", "ParentKey", "Nodes")]
    public class AttrTreeDto
    {
        public int Key { get; set; }
        public int? ParentKey { get; set; }
        public List<AttrTreeDto> Nodes { get; set; } = new();
    }

    // ===== fixture：属性级 NoAutoInclude（D1，导航收集）=====

    public class A19LogE { public string Action { get; set; } = ""; }

    /// <summary>源实体须含 LogE 导航属性（ResolveNavigationProperty 解析锚——防"树本就空"假绿）</summary>
    public class A19Entity
    {
        public int Id { get; set; }
        public List<A19LogE> Logs { get; set; } = new();
    }

    [MapFrom(typeof(A19Entity))] // 补锚——[MapProperty] 消费以有锚为前提（对照组）
    public class A19FluentDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(A19LogE), "Logs")]
        public List<string> AuditLogs { get; set; } = new();
    }

    [MapFrom(typeof(A19Entity))] // 补锚——[MapNoAutoInclude]/[MapProperty] 消费以有锚为前提
    public class A19AttrDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(A19LogE), "Logs")]
        [MapNoAutoInclude]
        public List<string> AuditLogs { get; set; } = new();
    }

    [MapFrom(typeof(A19Entity))] // 筛查翻正：原 fixture 为纯 Fluent 对——
    // 其对级 NoAutoInclude 用例的导航收集依赖 [MapProperty] 消费，正是本册修正的缺陷行为（007 双引擎分叉）。
    // 补特性锚（混合形态：[MapFrom] + Fluent 注册并存，第 4 行 = 读）使"对级 NoAutoInclude
    // 不抑制导航收集"的用例意图在新口径下继续成立；纯 Fluent 对不消费标注的正向断言归锚语义族。
    public class A19PairLevelDto
    {
        public int Id { get; set; }
        [MapProperty(typeof(A19LogE), "Logs")]
        public List<string> AuditLogs { get; set; } = new();
    }

    // ===== fixture：属性级 [MapNoAutoInclude] 锚语义（c 步）=====

    /// <summary>嵌套 DTO 承载（须有映射元数据——TryRecurseNestedDto 主源查询前置）。</summary>
    public class A19LogDto { public string Action { get; set; } = ""; }

    /// <summary>纯 Fluent 对（**无 [MapFrom]**）+ 属性级 [MapNoAutoInclude]：本册口径反射腿不消费。</summary>
    public class A19PurePairNoAutoDto
    {
        public int Id { get; set; }
        [MapNoAutoInclude]
        public List<A19LogDto> Logs { get; set; } = new();
    }

    /// <summary>特性对对照（有 [MapFrom]）+ 属性级 [MapNoAutoInclude]：照旧消费（零变化）。</summary>
    [MapFrom(typeof(A19Entity))]
    public class A19AttrNoAutoNestedDto
    {
        public int Id { get; set; }
        [MapNoAutoInclude]
        public List<A19LogDto> Logs { get; set; } = new();
    }

    // ===== 注册辅助 =====

    private static RuntimeMapperMetadata Meta(Type src, Type tgt, RuntimeTreeConfig? tree,
        string[]? noAutoIncludeTargets = null, bool pairNoAutoInclude = false)
        => new()
        {
            SourceType = src,
            TargetType = tgt,
            SourceTypeFqn = src.FullName!,
            TargetTypeFqn = tgt.FullName!,
            Channel = RuntimeMappingChannel.Fluent,
            IsFluentForward = true,
            NoAutoInclude = pairNoAutoInclude,
            TreeConfig = tree,
            PropertyNoAutoIncludeTargets = noAutoIncludeTargets ?? Array.Empty<string>(),
            Properties = Array.Empty<RuntimePropertyMapping>(),
        };

    private static void RegisterTreePairs()
    {
        RuntimeSnapshotStore.Register(Meta(typeof(TreeEntity), typeof(TreeDto),
            new RuntimeTreeConfig { IdProperty = "Key", ParentIdProperty = "ParentKey", ChildrenProperty = "Nodes" }));
        RuntimeSnapshotStore.Register(Meta(typeof(OptInEntity), typeof(OptInDto), tree: null));
    }

    private static void RegisterNoAutoIncludePairs()
    {
        RuntimeSnapshotStore.Register(Meta(typeof(A19Entity), typeof(A19FluentDto),
            tree: null, noAutoIncludeTargets: new[] { "AuditLogs" }));
        RuntimeSnapshotStore.Register(Meta(typeof(A19Entity), typeof(A19PairLevelDto),
            tree: null, pairNoAutoInclude: true));
    }

    // ===== 消费点一：TreeBuilder.ResolveConfig =====

    /// <summary>标准 1（注册表承载）：Fluent .Tree() 三元组经注册表命中，ResolveConfig 返回注册表值。</summary>
    [Fact]
    [Trait("Capability", "A-17")] // P5④ 回补——Fluent 树构建配置整机消费锚
    public void ResolveConfig_RegistryCarries_FluentTreeTriplet()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterTreePairs();

            var cfg = TreeBuilder.ResolveConfig(typeof(TreeDto));
            Assert.Equal("Key", cfg.IdProperty);
            Assert.Equal("ParentKey", cfg.ParentIdProperty);
            Assert.Equal("Nodes", cfg.ChildrenProperty);
            Assert.NotNull(RuntimeSnapshotStore.GetTreeConfig(typeof(TreeDto)));
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    /// <summary>端到端（标准 1）：Fluent 声明三元组 → ToTreeList 语义按自定义字段名挂载。</summary>
    [Fact]
    public void TreeBuilder_Build_MountsByCustomFieldNames()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterTreePairs();

            var flat = new List<TreeDto>
            {
                new() { Key = 1, ParentKey = null, Name = "root" },
                new() { Key = 2, ParentKey = 1, Name = "child-a" },
                new() { Key = 3, ParentKey = 2, Name = "grandchild" },
            };
            var roots = TreeBuilder.Build(flat, TreeBuilder.ResolveConfig(typeof(TreeDto)));

            var root = Assert.Single(roots);
            Assert.Equal("root", root.Name);
            var child = Assert.Single(root.Nodes);
            Assert.Equal("child-a", child.Name);
            var grand = Assert.Single(child.Nodes);
            Assert.Equal("grandchild", grand.Name);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    /// <summary>回归（反射回退腿/否定条件 F6）：注册表未命中时 [MapTree] 特性通道语义零变化；
    /// 无标注 DTO 取 Default（树构建路径既有语义）。</summary>
    [Fact]
    public void ResolveConfig_ReflectionFallback_Unchanged()
    {
        try
        {
            RuntimeSnapshotStore.Clear(); // 空注册表——反射回退腿

            var attrCfg = TreeBuilder.ResolveConfig(typeof(AttrTreeDto));
            Assert.Equal("Key", attrCfg.IdProperty);
            Assert.Equal("Nodes", attrCfg.ChildrenProperty);

            var defaultCfg = TreeBuilder.ResolveConfig(typeof(OptInDto));
            Assert.Equal("Id", defaultCfg.IdProperty);
            Assert.Equal("ParentId", defaultCfg.ParentIdProperty);
            Assert.Equal("Children", defaultCfg.ChildrenProperty);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ===== 消费点二：IsTreeChildrenSlot（经 Build 投影绑定面断言）=====

    /// <summary>
    /// 构建投影表达式并提取 MemberInit 绑定成员名列表（TreeChildrenSlotTests 同款辅助）
    /// </summary>
    private static List<string> BoundMemberNames<TSource, TTarget>()
        where TSource : class
        where TTarget : class
    {
        var strategy = new ProjectionStrategy(
            MaxNestingDepth: 8, WarningDepth: 5, EnableCollectionProjection: true, TracePrefix: "[test slot]");
        var expr = ProjectionExpressionFactory.Build<TSource, TTarget>(strategy);
        var init = (MemberInitExpression)expr.Body;
        return init.Bindings.Select(b => b.Member.Name).ToList();
    }

    /// <summary>标准 2（opt-in 负向）：未调 .Tree() 的同名自引用 Children → 树挂载位恒 false，
    /// 投影照常绑定（不跳过）——对照已声明 .Tree() 的挂载位被跳过（TreeBuilder 填充）。</summary>
    [Fact]
    public void OptIn_UndeclaredTree_SlotNeverTrue()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterTreePairs();

            // opt-in 负向：未声明 .Tree() 且无 [MapTree]——GetTreeConfig null → 恒 false → 绑定照常
            Assert.Null(RuntimeSnapshotStore.GetTreeConfig(typeof(OptInDto)));
            var optInBound = BoundMemberNames<OptInEntity, OptInDto>();
            Assert.Contains("Children", optInBound);

            // 对照：已声明 .Tree() 且 Nodes 为自引用集合——挂载位跳过（不绑定投影）
            var fluentBound = BoundMemberNames<TreeEntity, TreeDto>();
            Assert.DoesNotContain("Nodes", fluentBound);
            Assert.Contains("Key", fluentBound);
        }
        finally { RuntimeSnapshotStore.Clear(); }
    }

    // ===== 消费点四（D1）：NavigationTreeCollector 属性级 NoAutoInclude =====

    /// <summary>标准 4（属性级注册表消费）：.Member(dest).NoAutoInclude() 目标属性经注册表命中 → 导航树跳过。</summary>
    [Fact]
    public void PropertyNoAutoInclude_RegistryRead_SkipsNavigation()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterNoAutoIncludePairs();
            NavigationTreeCollector.ClearCache(); // 注册表变更不入 Collect 缓存键——测试隔离须显式清缓存

            var tree = NavigationTreeCollector.Collect(typeof(A19Entity), typeof(A19FluentDto));
            Assert.True(tree.IsEmpty, "属性级 NoAutoInclude 声明属性为唯一导航 → 收集树应为空（跳过）");
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            NavigationTreeCollector.ClearCache();
        }
    }

    /// <summary>回归（反射回退腿）：注册表未命中时 [MapNoAutoInclude] 特性通道语义零变化。</summary>
    [Fact]
    public void PropertyNoAutoInclude_ReflectionFallback_Unchanged()
    {
        try
        {
            RuntimeSnapshotStore.Clear(); // 空注册表——反射回退腿
            NavigationTreeCollector.ClearCache();

            var tree = NavigationTreeCollector.Collect(typeof(A19Entity), typeof(A19AttrDto));
            Assert.True(tree.IsEmpty, "[MapNoAutoInclude] 特性标注属性 → 导航树空（既有语义）");

            var plainTree = NavigationTreeCollector.Collect(typeof(A19Entity), typeof(A19FluentDto));
            Assert.False(plainTree.IsEmpty, "无声明 DTO 导航照常收集（回归对照）");
            Assert.Contains("Logs", plainTree.FlattenPaths());
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            NavigationTreeCollector.ClearCache();
        }
    }

    /// <summary>回归（对级 NoAutoInclude() 既有语义零变化）：对级声明不抑制导航收集。</summary>
    [Fact]
    [Trait("Capability", "B-02")] // P5② 回补——对级 NoAutoInclude（Fluent 独有）整机锚
    public void PairLevelNoAutoInclude_DoesNotSuppressNavigation()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RegisterNoAutoIncludePairs();
            NavigationTreeCollector.ClearCache();

            var tree = NavigationTreeCollector.Collect(typeof(A19Entity), typeof(A19PairLevelDto));
            Assert.False(tree.IsEmpty, "对级 NoAutoInclude 不影响导航收集（既有语义回归）");
            Assert.Contains("Logs", tree.FlattenPaths());
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            NavigationTreeCollector.ClearCache();
        }
    }

    // ===== 消费点四补锚（c）：[MapNoAutoInclude] 反射腿特性锚准入 =====

    /// <summary>
    /// 八族之 MapNoAutoInclude·纯 Fluent 对（无 [MapFrom]）：反射腿**不消费** → 嵌套 DTO 导航照常收集。
    /// <para>缺陷态（前，双锚准入）：标注被消费 → 导航被跳过 → 本用例红（反向验证锚 = 下一条对照）。</para>
    /// </summary>
    [Fact]
    public void MapNoAutoInclude_PureFluentPair_NotConsumed_NavigationCollected()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(A19LogE), typeof(A19LogDto), tree: null));
            RuntimeSnapshotStore.Register(Meta(typeof(A19Entity), typeof(A19PurePairNoAutoDto), tree: null));
            NavigationTreeCollector.ClearCache();

            var tree = NavigationTreeCollector.Collect(typeof(A19Entity), typeof(A19PurePairNoAutoDto));
            Assert.False(tree.IsEmpty, "Fluent 对不消费 [MapNoAutoInclude] 反射腿 → 导航照常收集");
            Assert.Contains("Logs", tree.FlattenPaths());
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            NavigationTreeCollector.ClearCache();
        }
    }

    /// <summary>八族之 MapNoAutoInclude·特性对对照（有 [MapFrom]）：反射腿照旧消费 → 导航跳过（零变化）。</summary>
    [Fact]
    public void MapNoAutoInclude_AttributeAnchoredPair_StillConsumed()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(Meta(typeof(A19LogE), typeof(A19LogDto), tree: null));
            NavigationTreeCollector.ClearCache();

            var tree = NavigationTreeCollector.Collect(typeof(A19Entity), typeof(A19AttrNoAutoNestedDto));
            Assert.True(tree.IsEmpty, "特性对 [MapNoAutoInclude] 消费 → 导航跳过（既有语义零变化）");
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
            NavigationTreeCollector.ClearCache();
        }
    }

    // ===== J-4：默认三元组三处一致（运行时两处；第三处 Fluent 提取默认经生成产物断言，
    // 见 test/NCMapper.SourceGen.Tests/TreeConfigEmissionTests.cs）=====

    [Fact]
    public void J4_DefaultTriplet_RuntimePlacesConsistent()
    {
        var attr = new MapTreeAttribute();
        var runtime = new RuntimeTreeConfig();
        Assert.Equal(attr.IdProperty, runtime.IdProperty);
        Assert.Equal(attr.ParentIdProperty, runtime.ParentIdProperty);
        Assert.Equal(attr.ChildrenProperty, runtime.ChildrenProperty);
        Assert.Equal("Id", runtime.IdProperty);
        Assert.Equal("ParentId", runtime.ParentIdProperty);
        Assert.Equal("Children", runtime.ChildrenProperty);
    }
}
