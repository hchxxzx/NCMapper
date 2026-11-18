using Microsoft.EntityFrameworkCore;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Orm;

/// <summary>
/// EF Core 投影场景集 — AsDto、ToChunkDto、ToLookup、嵌套跨表投影。
/// </summary>
public static class EFCoreScenarios
{
    // ===== 单表投影 =====

    /// <summary>
    /// 单表 AsDto 投影，SQL 侧列裁剪。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>投影后的 DTO 列表</returns>
    #region OrmProjection_SingleTableAsDto
    public static List<UserDto> SingleTableAsDto(DbContext db)
        => db.Set<UserEntity>()
            .Where(u => u.Age > 20)
            .OrderBy(u => u.Age)
            .AsDto()
            .ToList<UserDto>();
    #endregion


    /// <summary>
    /// First + AsDto：单行投影。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>第一条匹配的 DTO</returns>
    #region OrmProjection_SingleTableAsDto_SingleTableFirst
    public static UserDto? SingleTableFirst(DbContext db)
        => db.Set<UserEntity>()
            .Where(u => u.Id == 1)
            .AsDto()
            .First<UserDto>();
    #endregion


    /// <summary>
    /// Single + AsDto：单行投影（严格模式，多条则抛异常）。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>唯一匹配的 DTO</returns>
    #region OrmProjection_SingleTableAsDto_SingleTableSingle
    public static UserDto? SingleTableSingle(DbContext db)
        => db.Set<UserEntity>()
            .Where(u => u.Id == 1)
            .AsDto()
            .Single<UserDto>();
    #endregion


    /// <summary>
    /// ToChunkDto：分批回调读取。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>全部批次合并</returns>
    #region OrmProjection_SingleTableAsDto_ToChunkDto
    public static List<UserDto> ToChunkDto(DbContext db)
    {
        var all = new List<UserDto>();
        db.Set<UserEntity>()
            .OrderBy(u => u.Id)
            .AsDto()
            .ToChunkDto<UserDto>(2, chunk => all.AddRange(chunk));
        return all;
    }

    #endregion
    // ===== 嵌套跨表投影 =====

    /// <summary>
    /// 嵌套跨表投影：EF Core 自动 LEFT JOIN 嵌套实体。
    /// </summary>
    /// <typeparam name="TContext">DbContext 类型（需含 DemoOrder/DemoOrderDetail/DemoProduct DbSet）</typeparam>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>嵌套投影后的 DTO 列表</returns>
    #region OrmProjection_NestedCrossTable
    public static List<DemoOrderDto> NestedCrossTable<TContext>(TContext db)
        where TContext : DbContext
        => db.Set<DemoOrder>()
            .AsDto()
            .ToList<DemoOrderDto>();
    #endregion

    // ===== ToLookup =====

    /// <summary>
    /// ToLookup：按年龄分组查找。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>按 Age 分组的 Lookup</returns>
    /// <summary>
    /// EFCore ToTreeList：平铺自引用表 → 嵌套树 DTO（树挂载位三元组由 [MapTree] 声明）。
    /// </summary>
    /// <param name="db">EFCore 上下文（已播种平铺分类数据）</param>
    /// <returns>树形 DTO（Children 由 ToTreeList 组装）</returns>
    #region OrmProjection_TreeList
    public static List<CategoryDto> ToTreeListFlat(DbContext db)
        => db.Set<CategoryEntity>().AsDto().ToTreeList<CategoryDto>();
    #endregion

    /// <summary>
    /// Fluent 树查询：Fluent DTO（Tree() 对级构建配置，注册表承载）+ ToTreeList。
    /// 树挂载位判定优先读 Fluent 注册表（GetTreeConfig），与 [MapTree] 特性等价。
    /// </summary>
    /// <param name="db">EFCore 上下文（已播种平铺树数据）</param>
    /// <returns>Fluent 树形 DTO</returns>
    #region OrmProjection_FluentTree
    public static List<FxTreeDto> ToTreeListFluent(DbContext db)
        => db.Set<FxTreeEntity>().AsDto().ToTreeList<FxTreeDto>();
    #endregion

    #region OrmProjection_ToLookup
    public static ILookup<int, UserDto> ToLookup(DbContext db)
        => db.Set<UserEntity>()
            .OrderBy(u => u.Id)
            .AsDto()
            .ToLookup<int, UserDto>(u => u.Age / 10);
    #endregion


    /// <summary>
    /// ToDictionary：同步字典投影（Key 由实体属性选择）。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>以 Id 为 Key 的字典</returns>
    #region OrmProjection_ToDictionary
    public static Dictionary<int, UserDto> ToDictionary(DbContext db)
        => db.Set<UserEntity>()
            .OrderBy(u => u.Id)
            .AsDto()
            .ToDictionary<int, UserDto>(u => u.Id);
    #endregion


    /// <summary>
    /// NoAutoIncludeManual：[MapNoAutoInclude] 排除引用导航自动 Include 后手动补 Include。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>手动 Include 导航后的扁平 DTO 列表</returns>
    #region OrmProjection_NoAutoIncludeManual
    public static List<DemoOrderFlatNoIncludeDto> NoAutoIncludeManual(DbContext db)
        => db.Set<DemoOrder>()
            .Include(o => o.Detail)   // manual include after auto-include excluded
            .AsDto()
            .ToList<DemoOrderFlatNoIncludeDto>();
    #endregion


    /// <summary>
    /// GroupByNativeAggregate：GroupBy 替代路径 A — EF Core 原生 Select 聚合，GROUP BY 下推 SQL 侧。
    /// <para>AsDto 链路不支持链式 GroupBy（[Obsolete(error:true)] 拦截），聚合可 SQL 表达时优先本路径。</para>
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>按年龄分组的计数</returns>
    #region OrmProjection_GroupByNativeAggregate
    public static List<(int Age, int Count)> GroupByNativeAggregate(DbContext db)
        => db.Set<UserEntity>()
            .GroupBy(u => u.Age)
            .Select(g => new { Age = g.Key, Count = g.Count() })
            .ToList()
            .Select(x => (x.Age, x.Count))
            .ToList();
    #endregion


    /// <summary>
    /// GroupByInMemoryLinq：GroupBy 替代路径 B（不推荐）— 先 AsDto().ToList() 取到内存再 LINQ 分组。
    /// <para>全量物化开销大且失去 SQL 侧 GROUP BY 下推，仅在聚合无法用 SQL 表达时使用。</para>
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>按年龄分组的计数</returns>
    #region OrmProjection_GroupByInMemoryLinq
    public static List<(int Age, int Count)> GroupByInMemoryLinq(DbContext db)
        => db.Set<UserEntity>()
            .AsDto()
            .ToList<UserDto>()
            .GroupBy(d => d.Age)
            .Select(g => (Age: g.Key, Count: g.Count()))
            .ToList();
    #endregion

    // ===== ToDictionary =====

    /// <summary>
    /// ToDictionaryAsync：异步映射为字典。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>以 Id 为 Key 的字典</returns>
    #region OrmProjection_ToDictionaryAsync
    public static async Task<Dictionary<int, UserDto>> ToDictionaryAsync(DbContext db)
        => await db.Set<UserEntity>()
            .OrderBy(u => u.Id)
            .AsDto()
            .ToDictionaryAsync<int, UserDto>(u => u.Id);
    #endregion

    /// <summary>
    /// 异步终端巡览 — ToListAsync 与 ToPagedListAsync（文档异步腿形态，巡览不挂 Run）。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>异步列表与异步分页结果对</returns>
    #region OrmProjection_EFAsyncTerminals
    public static async Task<(List<UserDto> All, PagedList<UserDto> Page)> EfAsyncTerminals(DbContext db)
    {
        // Async list terminal
        var all = await db.Set<UserEntity>()
            .Where(u => u.Age > 18)
            .AsDto()
            .ToListAsync<UserDto>();
        // Async paged terminal (SQL-side paging, dialect auto-detected)
        var page = await db.Set<UserEntity>()
            .Where(u => u.Age > 18)
            .AsDto()
            .ToPagedListAsync<UserDto>(page: 1, pageSize: 20);
        return (all, page);
    }
    #endregion

    /// <summary>
    /// AsTracking 显式跟踪腿 — 默认 AsDto 查询不跟踪；显式 AsTracking 后 ChangeTracker 可见（巡览不挂 Run）。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>不跟踪与显式跟踪两种投影结果对</returns>
    #region OrmProjection_AsTrackingExplicit
    public static (List<UserDto> NoTracking, List<UserDto> Tracked) AsTrackingExplicitProjection(DbContext db)
    {
        // Default: projection query is not tracked (ChangeTracker stays empty)
        var noTracking = db.Set<UserEntity>()
            .AsDto()
            .ToList<UserDto>();
        // Explicit AsTracking before AsDto (for later SaveChanges flows)
        var tracked = db.Set<UserEntity>()
            .AsTracking()
            .AsDto()
            .ToList<UserDto>();
        return (noTracking, tracked);
    }
    #endregion

    // ===== 集合索引路径投影=====

    /// <summary>
    /// 集合索引路径投影 — [MapProperty(typeof(Blog), "Posts[0].Title")]。
    /// <para>NCMapper 投影构建器生成 ElementAtOrDefault 形态，EF Core 翻译为 LIMIT 1 OFFSET 0
    /// 子查询（SQL 侧无 ORDER BY，语义为无序取一，非"第一条"）；空集合 → 子查询无匹配行 → NULL。</para>
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>投影 DTO 列表 + 等价索引查询的 SQL 形态</returns>
    public static (List<BlogDto> Dtos, string Sql) MapIndexPathProjection(DbContext db)
    {
        // NCMapper 投影路径真实执行（AsDto → 投影构建器 → SQL 侧 LIMIT 1 OFFSET 0）
        var dtos = db.Set<Blog>()
            .AsDto()
            .ToList<BlogDto>();

        // 等价原生查询展示 SQL 形态（与 NCMapper 投影构建器生成表达式同形态，PoC EF1b 实证可翻译）
        var sql = db.Set<Blog>()
            .Select(b => b.Posts[0].Title)
            .ToQueryString();

        return (dtos, sql);
    }
}

/// <summary>
/// 扁平订单 DTO — 演示 [MapNoAutoInclude] 排除引用导航自动 Include（NCMAP017 Info 级提示的合法载体）。
/// </summary>
[MapFrom(typeof(DemoOrder))]
public class DemoOrderFlatNoIncludeDto
{
    /// <summary>
    /// 订单 ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 明细导航 — [MapNoAutoInclude] 排除自动 Include，需手动 Include 才填充。
    /// </summary>
    [MapNoAutoInclude]
    public DemoOrderDetailDto? Detail { get; set; }
}
