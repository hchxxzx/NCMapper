using FreeSql;
using NCMapper.FreeSql;
using NCMapper.Samples.Helpers;
using NCMapper.Samples.Models.Dtos;

namespace NCMapper.Samples.Orm;

/// <summary>
/// 场景 503 — FreeSql 多表与高级终端：
/// 双表 JOIN 投影 / IncludeMany 一对多导航 / ToChunkDtoAsync 异步分批 /
/// ToLookup 分组 / Fluent DTO 投影。全部内存 SQLite 形态，不依赖外部数据库。
/// </summary>
public static class FreeSqlMultiTableScenarios
{
    /// <summary>场景 503 入口（ScenarioRegistry 注册）。</summary>
    public static void Run()
    {
        // ===== 片段 1+4+5：目录/商品两表演示数据 =====
        using var fsql = DbSeedData.CreateInMemoryFreeSql();
        SeedCatalogData(fsql);

        TwoTableJoin(fsql);
        IncludeManyNavigation(fsql);
        ToChunkDtoAsyncDemo(fsql);
        ToLookupDemo(fsql);
        FluentProjection(fsql);
    }

    /// <summary>播种目录/商品两表演示数据（AutoSyncStructure 自动建表）。</summary>
    /// <param name="fsql">FreeSql 实例（内存模式）</param>
    public static void SeedCatalogData(IFreeSql fsql)
    {
        fsql.Insert(new List<CatalogEntity>
        {
            new() { Id = 1, Name = "电子产品" },
            new() { Id = 2, Name = "图书" },
        }).ExecuteAffrows();

        fsql.Insert(new List<CatalogProductEntity>
        {
            new() { Id = 1, CatalogId = 1, Title = "机械键盘", Price = 399m },
            new() { Id = 2, CatalogId = 1, Title = "无线鼠标", Price = 199m },
            new() { Id = 3, CatalogId = 1, Title = "4K 显示器", Price = 2199m },
            new() { Id = 4, CatalogId = 2, Title = "C# 入门", Price = 59m },
        }).ExecuteAffrows();
    }

    /// <summary>
    /// 双表 JOIN 投影：ISelect&lt;T1, T2&gt; + LeftJoin + AsDto → SQL 侧完成 JOIN 与列裁剪。
    /// 跨表字段（CatalogName）由 [MapProperty(typeof(CatalogEntity), ...)] 声明取数来源。
    /// </summary>
    /// <param name="fsql">FreeSql 实例（已播种目录/商品）</param>
    /// <returns>双表组合投影结果</returns>
    #region FreeSqlMultiTable_TwoTableJoin
    public static List<ProductCatalogDto> TwoTableJoin(IFreeSql fsql)
    {
        var dtos = fsql.Select<CatalogProductEntity, CatalogEntity>()
            .LeftJoin((p, c) => p.CatalogId == c.Id)
            .AsDto()
            .ToList<ProductCatalogDto>();

        OutputHelper.Dump("503-two-table-join", "Product LEFT JOIN Catalog (SQL-side JOIN + column pruning)", dtos);
        // Assert: row count after JOIN = product count, and cross-table fields are non-empty (failure -> Error Summary -> exit code 1)
        if (dtos.Count != 4)
            OutputHelper.Error("503-two-table-join: JOIN projection should return 4 rows (one per product)");
        if (dtos.Any(d => d.CatalogName.Length == 0))
            OutputHelper.Error("503-two-table-join: cross-table field CatalogName got no value (JOIN or MapProperty declaration failed)");
        return dtos;
    }
    #endregion

    /// <summary>
    /// 一对多导航集合投影：IncludeMany(c => c.Products) 先加载子表集合，
    /// AsDto 后导航集合逐元素走 CatalogProductEntity → CatalogProductDto 同名映射（嵌套集合非空）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例（已播种目录/商品）</param>
    /// <returns>含商品集合的目录 DTO</returns>
    #region FreeSqlMultiTable_IncludeManyNavigation
    public static List<CatalogDto> IncludeManyNavigation(IFreeSql fsql)
    {
        var catalogs = fsql.Select<CatalogEntity>()
            .IncludeMany(c => c.Products)
            .AsDto()
            .ToList<CatalogDto>();

        OutputHelper.Dump("503-include-many-navigation", "Catalog + IncludeMany(Products) (one-to-many navigation collection projection)", catalogs);
        // Assert: nested collections non-empty with correct content (Electronics catalog 3 items, Books catalog 1 item)
        var electronics = catalogs.FirstOrDefault(c => c.Name == "电子产品");
        if (electronics == null || electronics.Products.Count != 3)
            OutputHelper.Error("503-include-many-navigation: Electronics catalog should have 3 products after navigation collection projection");
        if (catalogs.Any(c => c.Products.Any(p => p.Title.Length == 0)))
            OutputHelper.Error("503-include-many-navigation: nested element mapping has an empty title (CatalogProductEntity → CatalogProductDto failed)");
        return catalogs;
    }
    #endregion

    /// <summary>
    /// 异步分批读取：ToChunkDtoAsync 真异步实现——逐页 Skip/Take + ToListAsync(ct)，
    /// 每轮取数前检查取消令牌。分批读取要求**稳定排序**（OrderBy）作为分页前提，
    /// 否则各页边界在无序结果集上不可复现。
    /// </summary>
    /// <param name="fsql">FreeSql 实例（已播种商品）</param>
    #region FreeSqlAdvanced_ToChunkDtoAsync
    public static void ToChunkDtoAsyncDemo(IFreeSql fsql)
    {
        var batches = new List<List<CatalogProductDto>>();
        fsql.Select<CatalogProductEntity>()
            .OrderBy(p => p.Id)   // stable ordering is the prerequisite of chunked reading
            .AsDto()
            .ToChunkDtoAsync<CatalogProductDto>(2, batch =>
            {
                batches.Add(batch);
                return Task.CompletedTask;
            })
            .GetAwaiter()
            .GetResult();

        OutputHelper.Dump("503-to-chunk-dto-async",
            $"Async chunked read with OrderBy(Id), 2 rows per batch, {batches.Count} batches total", batches);
        // Assert: 4 rows at 2 per batch = 2 batches, total count conserved
        if (batches.Count != 2 || batches.Sum(b => b.Count) != 4)
            OutputHelper.Error("503-to-chunk-dto-async: expected 2 batches with 4 items total");
    }
    #endregion

    /// <summary>
    /// 分组查找表：ToLookup 按 keySelector 分组并批量映射 DTO
    /// （FreeSql 3.5.103 无原生 ToLookup，实现为取实体 + 内存分组 + 每组 MapList）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例（已播种商品）</param>
    #region FreeSqlAdvanced_ToLookup
    public static void ToLookupDemo(IFreeSql fsql)
    {
        var lookup = fsql.Select<CatalogProductEntity>()
            .AsDto()
            .ToLookup<int, CatalogProductDto>(p => p.CatalogId);

        var groups = lookup.ToDictionary(g => g.Key, g => g.ToList());
        OutputHelper.Dump("503-to-lookup", $"Grouped by CatalogId, {groups.Count} groups total", groups.Keys);
        // Assert: two groups (catalog 1/2), catalog 1 group holds 3 products
        if (groups.Count != 2)
            OutputHelper.Error("503-to-lookup: expected 2 groups by catalog");
        if (!groups.TryGetValue(1, out var g1) || g1.Count != 3)
            OutputHelper.Error("503-to-lookup: catalog 1 group should contain 3 products");
    }
    #endregion

    /// <summary>
    /// Fluent DTO 参与投影：CatalogFluentDto 由 [MapperConfig] 配置类声明映射，
    /// 源生成器登记后 AsDto 走逐声明翻译判定（纯可翻译声明 → SQL 侧投影）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例（已播种目录）</param>
    /// <returns>Fluent DTO 投影结果</returns>
    #region FreeSqlMultiTable_FluentProjection
    public static List<CatalogFluentDto> FluentProjection(IFreeSql fsql)
    {
        var dtos = fsql.Select<CatalogEntity>()
            .AsDto()
            .ToList<CatalogFluentDto>();

        OutputHelper.Dump("503-fluent-projection", "Fluent DTO ([MapperConfig] declaration) AsDto projection", dtos);
        // Assert: the Fluent DTO projection output is isomorphic with the attribute-channel one
        if (dtos.Count != 2 || dtos.Any(d => d.Name.Length == 0))
            OutputHelper.Error("503-fluent-projection: Fluent DTO projection should return 2 catalogs with non-empty names");
        return dtos;
    }
    #endregion

    /// <summary>
    /// 多表 First 一行腿 — Select&lt;A,B&gt; + AsDto + First（投影即查询即映射，巡览不挂 Run）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例（已播种目录）</param>
    /// <returns>首行跨表 DTO（无记录返回 null）</returns>
    #region FreeSqlMultiTable_TwoTableFirst
    public static ProductCatalogDto? TwoTableFirst(IFreeSql fsql)
        => fsql.Select<CatalogProductEntity, CatalogEntity>()
            .AsDto()
            .First<ProductCatalogDto>();
    #endregion
}
