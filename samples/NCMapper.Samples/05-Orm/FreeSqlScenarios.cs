using FreeSql;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.Orm;

/// <summary>
/// FreeSql 投影场景集 — 单表/多表 Compose/Include/Filter/WithSql/GroupBy/BuildProjection。
/// </summary>
public static class FreeSqlScenarios
{
    // ===== 单表投影 =====

    /// <summary>
    /// 单表 AsDto 投影：UserEntity → UserDto，SQL 侧列裁剪。
    /// </summary>
    /// <param name="fsql">FreeSql 实例（内存模式，已播种）</param>
    /// <returns>投影后的 DTO 列表</returns>
    #region OrmProjection_SingleTableAsDto
    public static List<UserDto> SingleTableAsDto(IFreeSql fsql)
        => fsql.Select<UserEntity>()
            .Where(u => u.Age > 20)
            .OrderBy(u => u.Age)
            .AsDto()
            .ToList<UserDto>();
    #endregion


    /// <summary>
    /// First + AsDto：单行投影。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>第一条匹配的 DTO</returns>
    #region OrmProjection_SingleTableAsDto_SingleTableFirst
    public static UserDto? SingleTableFirst(IFreeSql fsql)
        => fsql.Select<UserEntity>()
            .Where(u => u.Id == 1)
            .AsDto()
            .First<UserDto>();
    #endregion

    /// <summary>
    /// ToPagedList + AsDto：SQL 级分页。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>分页结果</returns>
    #region OrmProjection_SingleTablePaged
    public static PagedList<UserDto> SingleTablePaged(IFreeSql fsql)
        => fsql.Select<UserEntity>()
            .OrderBy(u => u.Id)
            .AsDto()
            .ToPagedList<UserDto>(1, 2);
    #endregion

    /// <summary>
    /// ToChunkDto：分批回调读取。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>全部批次合并</returns>
    #region OrmProjection_ToChunkDto
    public static List<UserDto> ToChunkDto(IFreeSql fsql)
    {
        var all = new List<UserDto>();
        fsql.Select<UserEntity>()
            .OrderBy(u => u.Id)
            .AsDto()
            .ToChunkDto<UserDto>(2, chunk => all.AddRange(chunk));
        return all;
    }
    #endregion

    // ===== 多表 Compose 投影 =====

    /// <summary>
    /// 双表 Join + AsDto Compose：ISelect&lt;T1,T2&gt; + LeftJoin → ComposeDto。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>双表组合投影结果</returns>
    #region OrmProjection_MultiTableTwoWay
    public static List<WikiOrderCustomerDto> MultiTableTwoWay(IFreeSql fsql)
        => fsql.Select<WikiOrder, WikiCustomer>()
            .LeftJoin((o, c) => o.CustomerId == c.Id)
            .AsDto()
            .ToList<WikiOrderCustomerDto>();
    #endregion

    /// <summary>
    /// 三表 Join + AsDto Compose：ISelect&lt;T1,T2,T3&gt; + 两次 LeftJoin → ComposeDto。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>三表组合投影结果</returns>
    #region OrmProjection_MultiTableThreeWay
    public static List<WikiOrderFullDto> MultiTableThreeWay(IFreeSql fsql)
        => fsql.Select<WikiOrder, WikiCustomer, WikiOrderDetail>()
            .LeftJoin((o, c, od) => o.CustomerId == c.Id)
            .LeftJoin((o, c, od) => o.Id == od.OrderId)
            .AsDto()
            .ToList<WikiOrderFullDto>();
    #endregion

    // ===== Include 导航 =====

    /// <summary>
    /// Include 导航属性自动加载：导航路径 Parent.Name 自动 Include。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>含父分类名的 DTO 列表</returns>
    #region OrmProjection_IncludeNavigation
    public static List<WikiCategoryDto> IncludeNavigation(IFreeSql fsql)
        => fsql.Select<WikiCategory>()
            .AsDto()
            .ToList<WikiCategoryDto>();
    #endregion

    // ===== GlobalFilter =====

    /// <summary>
    /// AsDto 时 GlobalFilter 自动传播（IsDeleted = false 等）。
    /// </summary>
    /// <param name="fsql">带 GlobalFilter 的 FreeSql 实例</param>
    /// <returns>过滤后的 DTO 列表</returns>
    public static List<UserDto> GlobalFilterAsDto(IFreeSql fsql)
        => fsql.Select<UserEntity>()
            .AsDto()
            .ToList<UserDto>();

    // ===== WithSql =====

    /// <summary>
    /// WithSql 替换 SQL 片段后 AsDto 投影。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>WithSql 后的投影结果</returns>
    #region OrmProjection_WithSqlAsDto
    public static List<UserDto> WithSqlAsDto(IFreeSql fsql)
        => fsql.Select<UserEntity>()
            .WithSql("SELECT * FROM UserEntity WHERE Age > 0")
            .AsDto()
            .ToList<UserDto>();
    #endregion


    /// <summary>
    /// ToDictionary：字典投影（keySelector 作用于源实体 TSource）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>以实体 Id 为 Key 的字典</returns>
    #region OrmProjection_ToDictionary
    public static Dictionary<int, UserDto> ToDictionary(IFreeSql fsql)
        => fsql.Select<UserEntity>()
            .AsDto()
            .ToDictionary<int, UserDto>(u => u.Id);
    #endregion


    /// <summary>
    /// TwoTableWithSql：多表 WithSql 双数据源替换（sqlT1 + sqlT2 均必填）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>双表 WithSql 后的 Compose 投影结果</returns>
    #region OrmProjection_TwoTableWithSql
    public static List<WikiOrderCustomerDto> TwoTableWithSql(IFreeSql fsql)
        => fsql.Select<WikiOrder, WikiCustomer>()
            .WithSql(
                "SELECT * FROM WikiOrder WHERE Amount > 1000",
                "SELECT * FROM WikiCustomer")
            .LeftJoin((o, c) => o.CustomerId == c.Id)
            .AsDto()
            .ToList<WikiOrderCustomerDto>();
    #endregion


    /// <summary>
    /// WhereIfFilter：FreeSql 兼容查询模式 — WhereIf 动态条件拼装后 AsDto 投影。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <param name="minAge">可选年龄下限（null 时不追加条件）</param>
    /// <returns>条件过滤后的 DTO 列表</returns>
    #region OrmProjection_WhereIfFilter
    public static List<UserDto> WhereIfFilter(IFreeSql fsql, int? minAge)
        => fsql.Select<UserEntity>()
            .WhereIf(minAge.HasValue, u => u.Age >= minAge!.Value)
            .AsDto()
            .ToList<UserDto>();
    #endregion


    /// <summary>
    /// AnySubquery：Exists 子查询过滤（Where + 内层 Select.Any）后 AsDto 投影。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>存在子分类的分类 DTO 列表</returns>
    #region OrmProjection_AnySubquery
    public static List<WikiCategoryDto> AnySubquery(IFreeSql fsql)
        => fsql.Select<WikiCategory>()
            .Where(cat => fsql.Select<WikiCategory>().Any(sub => sub.ParentId == cat.Id))
            .AsDto()
            .ToList<WikiCategoryDto>();
    #endregion


    /// <summary>
    /// NoAutoIncludeGroup：[MapNoAutoInclude] 排除集合导航自动 Include 后手动 Include 补载。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>手动 Include 成员集合后的分组 DTO 列表</returns>
    #region OrmProjection_NoAutoIncludeGroup
    public static List<WikiGroupFlatNoIncludeDto> NoAutoIncludeGroup(IFreeSql fsql)
        => fsql.Select<WikiGroup>()
            .Include(g => g.Members)   // manual include after auto-include excluded
            .AsDto()
            .ToList<WikiGroupFlatNoIncludeDto>();
    #endregion

    // ===== GroupBy 编译期守护 =====

    /// <summary>
    /// 【推荐路径】GroupBy 替代方案 A — FreeSql 原生聚合，在 SQL 侧完成 GROUP BY。
    /// <para>GroupBy 后返回 ISelectGrouping，不再是实体查询，AsDto 被 [Obsolete(error:true)] 编译期拦截</para>
    /// <para>聚合可用 SQL 表达时（计数/求和/均值等），应优先走本路径，避免明细全量物化</para>
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>按状态分组的计数结果</returns>
    public static List<(UserStatus Status, int Count)> GroupByNativeAggregate(IFreeSql fsql)
        => fsql.Select<UserEntity>()
            .GroupBy(u => u.Status)
            .ToList(g => new { Status = g.Key, Count = g.Count() })
            .Select(x => (x.Status, x.Count))
            .ToList();

    /// <summary>
    /// 【不推荐路径】GroupBy 替代方案 B — 先 AsDto().ToList() 取到内存，再用 LINQ 分组。
    /// <para>需先把全量（或过滤后）明细物化到内存，失去 SQL 侧 GROUP BY 下推与索引加速，性能损失较大</para>
    /// <para>仅在分组聚合确实无法用 SQL 表达时使用，请结合实际性能预算审慎选用</para>
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>按年龄分组的计数结果</returns>
    public static List<(int Age, int Count)> GroupByInMemory(IFreeSql fsql)
        => fsql.Select<UserEntity>()
            .AsDto()
            .ToList<UserDto>()
            .GroupBy(d => d.Age)
            .Select(g => (Age: g.Key, Count: g.Count()))
            .ToList();

    /// <summary>
    /// ToTreeList + AsDto：自引用树形结构投影。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>树形结构 DTO</returns>
    #region OrmProjection_ToTreeList
    public static List<WikiTreeNodeDto> ToTreeList(IFreeSql fsql)
        => fsql.Select<WikiTreeNode>()
            .AsDto()
            .ToTreeList<WikiTreeNodeDto>();
    #endregion

    /// <summary>
    /// IncludeMany + AsDto：一对多导航自动降级。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>含 IncludeMany 降级的成员 DTO</returns>
    public static List<WikiMemberDto> IncludeMany(IFreeSql fsql)
        => fsql.Select<WikiGroup>()
            .IncludeMany(g => g.Members)
            .AsDto()
            .ToList<WikiMemberDto>();

    /// <summary>
    /// 异步终端巡览 — ToListAsync 与 FirstAsync（文档异步腿形态，巡览不挂 Run）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>异步列表与异步首条（无记录返回 null）对</returns>
    #region OrmProjection_FreeSqlAsyncTerminals
    public static async Task<(List<UserDto> All, UserDto? First)> FreeSqlAsyncTerminals(IFreeSql fsql)
    {
        // Async list terminal
        var all = await fsql.Select<UserEntity>()
            .Where(u => u.Age > 18)
            .AsDto()
            .ToListAsync<UserDto>();
        // Async first terminal (null when no rows)
        var first = await fsql.Select<UserEntity>()
            .Where(u => u.Id == 1)
            .AsDto()
            .FirstAsync<UserDto>();
        return (all, first);
    }
    #endregion

    /// <summary>
    /// ToPagedListAsync 异步分页腿 — SQL 级分页（巡览不挂 Run）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>异步分页结果</returns>
    #region OrmProjection_FreeSqlPagedAsync
    public static async Task<PagedList<UserDto>> FreeSqlPagedAsync(IFreeSql fsql)
    {
        var page = await fsql.Select<UserEntity>()
            .Where(u => u.Age > 18)
            .AsDto()
            .ToPagedListAsync<UserDto>(1, 20);
        return page;
    }
    #endregion
}

/// <summary>
/// 分组扁平 DTO — 演示 [MapNoAutoInclude] 排除集合导航自动 Include（关闭后需手动 Include）。
/// </summary>
[MapFrom(typeof(WikiGroup))]
public class WikiGroupFlatNoIncludeDto
{
    /// <summary>
    /// 分组 ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 分组名。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 成员集合 — [MapNoAutoInclude] 排除自动 Include，手动 Include 后才填充。
    /// </summary>
    [MapNoAutoInclude]
    public List<WikiMemberDto> Members { get; set; } = new();
}

/// <summary>
/// FreeSql 多表种子数据辅助方法。
/// </summary>
public static class WikiSeedData
{
    /// <summary>
    /// 播种 Wiki 多表演示数据。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    public static void SeedWikiData(IFreeSql fsql)
    {
        fsql.Insert(new List<WikiCustomer>
        {
            new() { Id = 1, Name = "张三", City = "北京" },
            new() { Id = 2, Name = "李四", City = "上海" },
        }).ExecuteAffrows();

        fsql.Insert(new List<WikiOrder>
        {
            new() { Id = 1, OrderNo = "ORD-001", CustomerId = 1, Amount = 100m },
            new() { Id = 2, OrderNo = "ORD-002", CustomerId = 1, Amount = 200m },
            new() { Id = 3, OrderNo = "ORD-003", CustomerId = 2, Amount = 150m },
        }).ExecuteAffrows();

        fsql.Insert(new List<WikiOrderDetail>
        {
            new() { Id = 1, OrderId = 1, Product = "笔记本", Quantity = 1 },
            new() { Id = 2, OrderId = 1, Product = "鼠标", Quantity = 2 },
            new() { Id = 3, OrderId = 2, Product = "键盘", Quantity = 1 },
        }).ExecuteAffrows();
    }

    /// <summary>
    /// 播种 Wiki 分类 Include 演示数据。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    public static void SeedCategoryData(IFreeSql fsql)
    {
        fsql.Insert(new List<WikiCategory>
        {
            new() { Id = 1, Name = "电子产品", ParentId = null },
            new() { Id = 2, Name = "电脑", ParentId = 1 },
            new() { Id = 3, Name = "手机", ParentId = 1 },
        }).ExecuteAffrows();
    }

    /// <summary>
    /// 播种树形/IncludeMany 演示数据。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    public static void SeedTreeData(IFreeSql fsql)
    {
        fsql.Insert(new List<WikiTreeNode>
        {
            new() { Id = 1, ParentId = null, Name = "Root" },
            new() { Id = 2, ParentId = 1, Name = "Child1" },
            new() { Id = 3, ParentId = 1, Name = "Child2" },
            new() { Id = 4, ParentId = 2, Name = "Grandchild" },
        }).ExecuteAffrows();

        fsql.Insert(new List<WikiGroup>
        {
            new() { Id = 1, Name = "GroupA" },
            new() { Id = 2, Name = "GroupB" },
        }).ExecuteAffrows();

        fsql.Insert(new List<WikiMember>
        {
            new() { Id = 1, GroupId = 1, Name = "Member1" },
            new() { Id = 2, GroupId = 1, Name = "Member2" },
            new() { Id = 3, GroupId = 2, Name = "Member3" },
        }).ExecuteAffrows();
    }
}
