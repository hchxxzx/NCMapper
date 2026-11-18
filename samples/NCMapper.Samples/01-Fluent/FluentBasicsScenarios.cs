using System.Globalization;
using Dapper;
using NCMapper.Attributes;
using NCMapper.Dapper;
using NCMapper.Runtime;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// Fluent 基础链场景集 — 链式声明基础终端演示（Map/Ignore/Mask/MapTree/NoAutoInclude/AfterMapping/Tree）。
/// <para>Fluent 通道：在 [MapperConfig] 配置类内声明 <c>static void M(FluentMappingConfig cfg)</c>，
/// 源生成器编译期生成与 [MapFrom] 通道相同的 GeneratedMapper，运行时零差异（how-to/fluent/basics.md）。</para>
/// </summary>
public static class FluentBasicsScenarios
{
    /// <summary>
    /// 场景 1 — 基础链五终端连写：Map（异名/嵌套源路径）+ Ignore + Mask + AfterMapping。
    /// <para>对应 how-to/fluent/basics.md「概述」示例块（ChainOrderMapper）。</para>
    /// </summary>
    /// <param name="src">源订单实体</param>
    /// <returns>映射后的订单 DTO（手机号已脱敏、备注被忽略、总重量已汇总）</returns>
    public static ChainOrderDto BasicChain(ChainOrderEntity src)
        => Mapper.Map<ChainOrderEntity, ChainOrderDto>(src)!;

    /// <summary>
    /// 场景 2 — MapTree 递归映射：Children 声明递归映射属性，嵌套树整图转换。
    /// </summary>
    /// <param name="root">三层树根节点</param>
    /// <returns>递归转换后的 DTO 树</returns>
    public static CategoryNodeDto MapTreeRecursive(CategoryNodeEntity root)
        => Mapper.Map<CategoryNodeEntity, CategoryNodeDto>(root)!;

    /// <summary>
    /// 场景 3 — NoAutoInclude 对级：该 (S,T) 对禁用自动匹配，仅映射显式 Map 声明的属性。
    /// </summary>
    /// <param name="src">源审计实体</param>
    /// <returns>仅 Id 被填充的 DTO（Name/Secret 保持默认）</returns>
    public static AuditDto NoAutoIncludePairLevel(AuditEntity src)
        => Mapper.Map<AuditEntity, AuditDto>(src)!;

    /// <summary>
    /// 场景 4 — AfterMapping 后置回调：映射完成后注入派生字段。
    /// </summary>
    /// <param name="src">源报表实体</param>
    /// <returns>带 ReportTag 派生标签的 DTO</returns>
    public static ReportDto AfterMappingStamp(ReportEntity src)
        => Mapper.Map<ReportEntity, ReportDto>(src)!;

    /// <summary>
    /// 场景 5 — Tree 自定义三元组（Key/ParentKey/Nodes）：声明被注册表承载，
    /// Dapper ToTreeList 按自定义三元组挂载（对比默认 Id/ParentId/Children）。
    /// </summary>
    /// <param name="connection">已打开的 SQLite 连接</param>
    /// <returns>挂载后的树根列表（root → child-a → grandchild 三层）</returns>
    public static List<TreeNodeDto> TreeCustomTriplet(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        connection.Execute("CREATE TABLE TreeNodes (Key INTEGER PRIMARY KEY, ParentKey INTEGER, Name TEXT)");
        connection.Execute("INSERT INTO TreeNodes VALUES (1,NULL,'root'),(2,1,'child-a'),(3,2,'grandchild')");
        return connection.AsDto<TreeNodeEntity>()
            .ToTreeList<TreeNodeDto>("SELECT * FROM TreeNodes", null)
            .ToList();
    }

    /// <summary>
    /// 场景 6 — 属性级 NoAutoInclude(dest)：仅该属性跳过 ORM 降级路径导航树自动收集；
    /// 注册表承载经 RuntimeSnapshotStore.IsPropertyNoAutoInclude 门面可查。
    /// </summary>
    /// <returns>门面查询结果（AuditLogs=true 命中 / Title=false 未命中）</returns>
    public static (bool AuditLogsSuppressed, bool TitleSuppressed) PropertyNoAutoIncludeProbe()
        => (RuntimeSnapshotStore.IsPropertyNoAutoInclude(typeof(ArticleDto), "AuditLogs"),
            RuntimeSnapshotStore.IsPropertyNoAutoInclude(typeof(ArticleDto), "Title"));

    /// <summary>
    /// 场景 7 — 索引段 lambda：Map 源路径 Items[0].Title（常量整数索引段，
    /// ElementAtOrDefault 语义——空集合/越界取 null 不抛异常）。
    /// </summary>
    /// <param name="src">源容器实体</param>
    /// <returns>首条明细标题已填充的 DTO</returns>
    public static IdxDto IndexSegment(IdxEntity src)
        => Mapper.Map<IdxEntity, IdxDto>(src)!;

    /// <summary>
    /// 场景调度 — 顺序执行全部场景并输出。
    /// </summary>
    public static void Run()
    {
    // 1. 基础链
        var order = new ChainOrderEntity
        {
            Id = 1001,
            Customer = new ChainCustomerInfo { Name = "ZhangSan" },
            CustomerPhone = "13912345678",
            InternalNote = "internal-only",
            Items = [new ChainOrderItemEntity { Weight = 1.5 }, new ChainOrderItemEntity { Weight = 2.5 }],
        };
        var orderDto = BasicChain(order);
        OutputHelper.Dump("101-basic-chain", order, orderDto);
        if (orderDto.OrderId != 1001)
            OutputHelper.Error($"101-basic-chain: OrderId renamed Map (Id → OrderId) failed (actual: {orderDto.OrderId})");
        // 安全断言：Mask 脱敏不落明文（MaskPhone 保留前3后4，中段 ****）
        if (orderDto.CustomerPhone.Contains("13912345678") || !orderDto.CustomerPhone.Contains("****"))
            OutputHelper.Error("101-basic-chain: CustomerPhone not masked (plaintext leak)");
        if (orderDto.InternalNote != "")
            OutputHelper.Error($"101-basic-chain: InternalNote should be ignored (actual: {orderDto.InternalNote})");
        if (orderDto.TotalWeight != 4.0)
            OutputHelper.Error($"101-basic-chain: TotalWeight AfterMapping should be 4.0 (actual: {orderDto.TotalWeight})");

        // 2. MapTree 递归映射
        var tree = new CategoryNodeEntity
        {
            Id = 1, Name = "root",
            Children = [new CategoryNodeEntity
            {
                Id = 2, Name = "child",
                Children = [new CategoryNodeEntity { Id = 3, Name = "grandchild" }],
            }],
        };
        var treeDto = MapTreeRecursive(tree);
        OutputHelper.Dump("101-map-tree-recursive", tree, treeDto);
        if (treeDto.Children is not { Count: 1 } || treeDto.Children[0].Children[0].Name != "grandchild")
            OutputHelper.Error("101-map-tree-recursive: recursive subgraph not fully converted");

        // 3. NoAutoInclude 对级
        var audit = NoAutoIncludePairLevel(new AuditEntity { Id = 7, Name = "audit-name", Secret = "top-secret" });
        OutputHelper.Dump("101-no-auto-include", "only explicit Map(Id) mapped", audit);
        if (audit.Id != 7 || audit.Name != "" || audit.Secret != "")
            OutputHelper.Error("101-no-auto-include: pair-level semantics failed (only Id should be mapped)");

        // 4. AfterMapping 独立场景
        var report = AfterMappingStamp(new ReportEntity { Title = "Q3" });
        OutputHelper.Dump("101-after-mapping", "Title=Q3", report);
        if (report.ReportTag != "[report:Q3]")
            OutputHelper.Error($"101-after-mapping: ReportTag should be [report:Q3] (actual: {report.ReportTag})");

        // 5. Tree 自定义三元组（注册表承载 + Dapper ToTreeList 挂载）
        var treeCfg = RuntimeSnapshotStore.GetTreeConfig(typeof(TreeNodeDto));
        OutputHelper.Dump("101-tree-config", "GetTreeConfig(TreeNodeDto)", treeCfg);
        if (treeCfg is null || treeCfg.IdProperty != "Key" || treeCfg.ParentIdProperty != "ParentKey" || treeCfg.ChildrenProperty != "Nodes")
            OutputHelper.Error("101-tree-config: Tree(Key,ParentKey,Nodes) not carried by registry");
        using (var connection = DbSeedData.CreateInMemoryConnection())
        {
            var roots = TreeCustomTriplet(connection);
            OutputHelper.Dump("101-tree-to-treelist", "flat 3 rows → mounted via custom triplet", roots);
            if (roots.Count != 1 || roots[0].Name != "root"
                || roots[0].Nodes is not { Count: 1 } || roots[0].Nodes[0].Name != "child-a"
                || roots[0].Nodes[0].Nodes is not { Count: 1 } || roots[0].Nodes[0].Nodes[0].Name != "grandchild")
                OutputHelper.Error("101-tree-to-treelist: custom triplet mounting failed (expected root→child-a→grandchild)");
        }

        // 6. 属性级 NoAutoInclude(dest)（注册表承载门面探针）
        var (auditLogsSuppressed, titleSuppressed) = PropertyNoAutoIncludeProbe();
        OutputHelper.Dump("101-property-no-auto-include", ".Member(d => d.AuditLogs).NoAutoInclude()", $"AuditLogs={auditLogsSuppressed}, Title={titleSuppressed}");
        if (!auditLogsSuppressed || titleSuppressed)
            OutputHelper.Error("101-property-no-auto-include: property-level registry registration failed (AuditLogs should match, Title should not)");

        // 7. 索引段 lambda（含空集合越界安全语义）
        var idx = IndexSegment(new IdxEntity
        {
            Id = 1,
            Items = [new IdxItemEntity { Title = "first-item" }],
        });
        OutputHelper.Dump("101-index-segment", "Items[0].Title", idx);
        if (idx.FirstTitle != "first-item")
            OutputHelper.Error($"101-index-segment: FirstTitle should be first-item (actual: {idx.FirstTitle})");
        var idxEmpty = IndexSegment(new IdxEntity { Id = 2, Items = [] });
        OutputHelper.Dump("101-index-segment-empty", "empty collection → ElementAtOrDefault null, no throw", idxEmpty);

        // 期望值：NoAutoMatch 白名单语义——AuditEntity→AuditDto 仅显式 Id Member 映射（FluentModels 契约：
        // NoAutoMatch 下未列入白名单的 Name 不映射，保持默认）
        var auditDto = Mapper.Map<AuditEntity, AuditDto>(new AuditEntity { Id = 5, Name = "Should-Not-Map" })!;
        if (auditDto.Id != 5 || auditDto.Name != "")
            OutputHelper.Error($"101-no-auto-match: NoAutoMatch should map only explicit members (Id=5, Name empty) (actual: Id={auditDto.Id}, Name={auditDto.Name})");
    }
}

/// <summary>
/// Fluent 链演示订单（源）— Amount 经 .From+Format 转文本、Phone 脱敏、Secret 忽略。
/// </summary>
public class Order
{
    /// <summary>金额（经 Format("N2") 转文本）</summary>
    public decimal Amount { get; set; }

    /// <summary>电话（同名锚点，Mask 脱敏）</summary>
    public string Phone { get; set; } = "";

    /// <summary>内部机密字段（Ignore 忽略）</summary>
    public string Secret { get; set; } = "";
}

/// <summary>
/// Fluent 链演示订单 DTO（目标）— 声明在 OrderFluentChain 集中配置。
/// </summary>
public class OrderDto
{
    /// <summary>金额文本（锚定→显式来源→变换）</summary>
    public string AmountText { get; set; } = "";

    /// <summary>电话（省略 From：按目标字段名解析同名源属性）</summary>
    public string Phone { get; set; } = "";

    /// <summary>机密字段（Ignore 独唱终态，保持默认）</summary>
    public string Secret { get; set; } = "";
}

/// <summary>
/// Fluent 基础链四腿配置类 — 锚定→来源→变换 / 同名省略 From / Mask / Ignore（how-to/fluent/basics.md 概述块绑定源）。
/// <para>金额一条演示"两侧名字不同"的完整写法：正向显式来源（From）+ 格式化，反向也显式写出
/// 写回目标（lambda 形态 .ConvertBack，目标取自 lambda 体）——名字不同要显式写，名字相同可省略
/// （参见 Phone 一条：省略 From 即按同名解析）。</para>
/// </summary>
[MapperConfig]
public class OrderFluentChain
{
    /// <summary>反向：把 "1,234.00" 文本解析回 decimal（N2 单向输出不可反解析，反解析口径由用户自控）。</summary>
    /// <param name="amountText">DTO 侧金额文本</param>
    public static decimal ParseAmount(string amountText)
        => decimal.Parse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <summary>链式声明入口（方法名任意，签名固定）。</summary>
    /// <param name="cfg">Fluent 配置根</param>
    #region FluentBasics_OrderChain_Configure
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<Order, OrderDto>()
           .Member(d => d.AmountText)                                     // anchor
               .From(s => s.Amount)                                       // explicit source (names differ)
               .Format("N2")                                              // transform
               .ConvertBack<decimal>((dto, text) => ParseAmount(text))     // reverse: whole DTO + field value -> entity
                                                                          //   (write-back target resolved from the lambda body; names differ, so declared explicitly)
           .Member(d => d.Phone).Mask<MaskPhone>()                        // From omitted: same-name source property by target field name
           .Member(d => d.Secret).Ignore();                               // ignore-only terminal state
    }
    #endregion
}
