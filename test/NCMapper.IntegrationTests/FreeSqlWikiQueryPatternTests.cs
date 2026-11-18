using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FreeSql;
using FreeSql.DataAnnotations;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql Wiki 转测试 — 查询组合模式回归（差异记录固化）
    /// <para>覆盖 withsql.md、paging.md、select-group-by.md、select-single-table.md 转 NCMapper 的固化项：</para>
    /// <para>WithSql 四形态与多表 sqlT2 必填约束（F-06）/ AsTable 分表 / ToPagedList 投影·降级·Compose（F-03）/</para>
    /// <para>分页参数守护与越界页码 / GroupBy 编译期守护（W-8）/ 聚合后内存映射 / IN 查询 List 与数组差异 /</para>
    /// <para>ToChunkDto 投影·降级分块与空结果集回调（GAP-1）。</para>
    /// <para>与 Samples 演示文件实体隔离（Wiki_QP_* 表名），每个测试使用独立 SQLite InMemory 实例。</para>
    /// </summary>
    [Collection("FreeSqlGlobalState")]
    public class FreeSqlWikiQueryPatternTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造 — 创建独立 SQLite InMemory 实例并灌入种子数据。
        /// </summary>
        public FreeSqlWikiQueryPatternTests()
        {
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
                .UseAutoSyncStructure(true)
                .UseNoneCommandParameter(true)
                .Build();
            SeedData(_fsql);
        }

        /// <summary>
        /// 释放 — 清理 IFreeSql 实例。
        /// </summary>
        public void Dispose()
        {
            _fsql.Dispose();
            GC.SuppressFinalize(this);
        }

        // ================================================================
        // withsql.md 固化项
        // ================================================================

        /// <summary>
        /// WithSql 作为数据源时，AsDto 投影路径的字段裁剪与行过滤均正常。
        /// <para>固化：WithSql 只改写 FROM 子查询，与 AsDto 的投影判定正交。</para>
        /// </summary>
        [Fact]
        public void T_WithSql投影_数据源限定与字段裁剪()
        {
            var dtos = _fsql.Select<QpOrder>()
                .WithSql("select * from Wiki_QP_Order where Amount >= 800")
                .OrderBy(o => o.Id)
                .AsDto()
                .ToList<QpOrderDto>();

            Assert.Equal(3, dtos!.Count);
            Assert.All(dtos, d => Assert.True(d.Amount >= 800m));
            Assert.Equal(new[] { 8, 9, 10 }, dtos.Select(d => d.Id).ToArray());
        }

        /// <summary>
        /// WithSql 在降级路径下同样生效（数据源限定发生在 SQL 侧，早于内存映射）。
        /// </summary>
        [Fact]
        public void T_WithSql降级_数据源限定依然生效()
        {
            var dtos = _fsql.Select<QpOrder>()
                .WithSql("select * from Wiki_QP_Order where Amount >= 800")
                .OrderBy(o => o.Id)
                .AsDto()
                .ToList<QpOrderConvDto>();

            Assert.Equal(3, dtos!.Count);
            Assert.All(dtos, d => Assert.False(string.IsNullOrEmpty(d.Code)));
        }

        /// <summary>
        /// 多次调用 WithSql 生成 UNION ALL 子查询，AsDto 在其外层投影。
        /// </summary>
        [Fact]
        public void T_WithSql_UnionAll_多次调用()
        {
            var dtos = _fsql.Select<QpOrder>()
                .WithSql("select * from Wiki_QP_Order where Id = 1")
                .WithSql("select * from Wiki_QP_Order where Id = 2")
                .AsDto()
                .ToList<QpOrderDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Equal(new[] { 1, 2 }, dtos.Select(d => d.Id).OrderBy(x => x).ToArray());
        }

        /// <summary>
        /// WithSql 数据源上的 Count + Page 与 AsDto 分页协同正常。
        /// </summary>
        [Fact]
        public void T_WithSql分页_Count与Page协同()
        {
            var paged = _fsql.Select<QpOrder>()
                .WithSql("select * from Wiki_QP_Order")
                .OrderBy(o => o.Id)
                .AsDto()
                .ToPagedList<QpOrderDto>(2, 3);

            Assert.Equal(10, paged.TotalCount);
            Assert.Equal(4, paged.TotalPages);
            Assert.Equal(3, paged.Items.Count);
            Assert.Equal(new[] { 4, 5, 6 }, paged.Items.Select(i => i.Id).ToArray());
        }

        /// <summary>
        /// 多表 WithSql 的 sqlT2 为必填参数（API 约束），补齐后字段填充正常。
        /// <para>固化：ISelect&lt;T1,T2&gt;.WithSql(sqlT1, sqlT2, parms) 无法只替换 T1 数据源。</para>
        /// </summary>
        [Fact]
        public void T_WithSql_Compose双表_T2字段正常填充()
        {
            var dtos = _fsql.Select<QpOrder, QpRegion>()
                .LeftJoin((o, r) => o.RegionId == r.Id)
                .WithSql("select * from Wiki_QP_Order where Amount >= 800", "select * from Wiki_QP_Region")
                .OrderBy((o, r) => o.Id)
                .AsDto()
                .ToList<QpOrderRegionDto>();

            Assert.Equal(3, dtos!.Count);
            Assert.All(dtos, d => Assert.False(string.IsNullOrEmpty(d.Name)));

            // API 约束固化：多表 WithSql 的第二参数为必填（非可选），签名中不存在单参数重载
            var singleArg = typeof(ISelect<QpOrder, QpRegion>)
                .GetMethods()
                .Where(m => m.Name == "WithSql")
                .Any(m => m.GetParameters().Count(p => !p.IsOptional) == 1);
            Assert.False(singleArg);
        }

        /// <summary>
        /// AsTable 分表切换与 AsDto 投影可组合。
        /// </summary>
        [Fact]
        public void T_AsTable分表_与AsDto组合()
        {
            var jan = _fsql.Select<QpLog>().AsTable((_, _) => "Wiki_QP_Log_202601").AsDto().ToList<QpLogDto>();
            var feb = _fsql.Select<QpLog>().AsTable((_, _) => "Wiki_QP_Log_202602").AsDto().ToList<QpLogDto>();

            Assert.Single(jan);
            Assert.Single(feb);
            Assert.Equal("一月日志", jan[0].Message);
            Assert.Equal("二月日志", feb[0].Message);
        }

        // ================================================================
        // paging.md 固化项
        // ================================================================

        /// <summary>
        /// Wiki 原生 Count(out total) + Page 与 AsDto 组合，分页语义不变。
        /// </summary>
        [Fact]
        public void T_Wiki式分页_CountOut与Page()
        {
            var dtos = _fsql.Select<QpOrder>()
                .OrderBy(o => o.Id)
                .Count(out var total)
                .Page(2, 3)
                .AsDto()
                .ToList<QpOrderDto>();

            Assert.Equal(10, total);
            Assert.Equal(3, dtos!.Count);
            Assert.Equal(4, dtos[0].Id);
        }

        /// <summary>
        /// ToPagedList 投影路径一次完成 Count + Page + 映射，元数据完整。
        /// </summary>
        [Fact]
        public void T_ToPagedList投影_元数据完整()
        {
            var paged = _fsql.Select<QpOrder>().OrderBy(o => o.Id).AsDto().ToPagedList<QpOrderDto>(2, 3);

            Assert.Equal(2, paged.Page);
            Assert.Equal(3, paged.PageSize);
            Assert.Equal(10, paged.TotalCount);
            Assert.Equal(4, paged.TotalPages);
            Assert.True(paged.HasPrev);
            Assert.True(paged.HasNext);
            Assert.Equal(new[] { 4, 5, 6 }, paged.Items.Select(i => i.Id).ToArray());
        }

        /// <summary>
        /// 降级路径下分页仍在 SQL 侧完成，元数据与投影路径一致。
        /// </summary>
        [Fact]
        public void T_ToPagedList降级_元数据与投影一致()
        {
            var projected = _fsql.Select<QpOrder>().OrderBy(o => o.Id).AsDto().ToPagedList<QpOrderDto>(2, 3);
            var degraded = _fsql.Select<QpOrder>().OrderBy(o => o.Id).AsDto().ToPagedList<QpOrderConvDto>(2, 3);

            Assert.Equal(projected.TotalCount, degraded.TotalCount);
            Assert.Equal(projected.TotalPages, degraded.TotalPages);
            Assert.Equal(projected.Items.Select(i => i.Id), degraded.Items.Select(i => i.Id));
            Assert.All(degraded.Items, d => Assert.False(string.IsNullOrEmpty(d.Code)));
        }

        /// <summary>
        /// Compose 双表分页可用且字段填充正常（闭环）。
        /// </summary>
        [Fact]
        public void T_Compose分页_F03闭环()
        {
            var paged = _fsql.Select<QpOrder, QpRegion>()
                .LeftJoin((o, r) => o.RegionId == r.Id)
                .OrderBy((o, r) => o.Id)
                .AsDto()
                .ToPagedList<QpOrderRegionDto>(2, 3);

            Assert.Equal(10, paged.TotalCount);
            Assert.Equal(4, paged.TotalPages);
            Assert.Equal(3, paged.Items.Count);
            Assert.All(paged.Items, i => Assert.False(string.IsNullOrEmpty(i.Name)));
        }

        /// <summary>
        /// 分页参数守护 — page 与 pageSize 非正数时抛 ArgumentOutOfRangeException。
        /// </summary>
        [Fact]
        public void T_分页参数守护_非法入参抛异常()
        {
            var ex1 = Assert.Throws<ArgumentOutOfRangeException>(
                () => _fsql.Select<QpOrder>().AsDto().ToPagedList<QpOrderDto>(0, 3));
            Assert.Equal("page", ex1.ParamName);

            var ex2 = Assert.Throws<ArgumentOutOfRangeException>(
                () => _fsql.Select<QpOrder>().AsDto().ToPagedList<QpOrderDto>(1, 0));
            Assert.Equal("pageSize", ex2.ParamName);

            var ex3 = Assert.Throws<ArgumentOutOfRangeException>(
                () => _fsql.Select<QpOrder>().AsDto().ToPagedList<QpOrderDto>(-1, 3));
            Assert.Equal("page", ex3.ParamName);
        }

        /// <summary>
        /// 越界页码返回空 Items，但保留正确的总数元数。
        /// </summary>
        [Fact]
        public void T_越界页码_空Items保留元数据()
        {
            var paged = _fsql.Select<QpOrder>().OrderBy(o => o.Id).AsDto().ToPagedList<QpOrderDto>(99, 3);

            Assert.Empty(paged.Items);
            Assert.Equal(10, paged.TotalCount);
            Assert.Equal(4, paged.TotalPages);
            Assert.False(paged.HasNext);
        }

        /// <summary>
        /// Skip/Take 偏移分页与 AsDto 组合，Take 超出剩余量按实际返回。
        /// </summary>
        [Fact]
        public void T_SkipTake_偏移分页()
        {
            var dtos = _fsql.Select<QpOrder>().OrderBy(o => o.Id).Skip(8).Take(5).AsDto().ToList<QpOrderDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Equal(9, dtos[0].Id);
        }

        // ================================================================
        // select-group-by.md 固化项
        // ================================================================

        /// <summary>
        /// GroupBy + AsDto 的编译期守护存在且为 error 级。
        /// <para>固化：守护扩展方法标记 [Obsolete(error: true)]，调用点产生 CS0619 而非运行时异常。</para>
        /// </summary>
        [Fact]
        public void T_GroupBy守护_Obsolete为error级()
        {
            var method = typeof(NCMapperFreeSqlGuardExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(m => m.Name == "AsDto" && m.IsGenericMethodDefinition);

            Assert.NotNull(method);

            var obsolete = method!.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(obsolete);
            Assert.True(obsolete!.IsError);

            // 守护目标必须是分组序列类型 ISelectGrouping<,>
            var firstParam = method.GetParameters()[0].ParameterType;
            Assert.StartsWith("ISelectGrouping", firstParam.Name, StringComparison.Ordinal);
        }

        /// <summary>
        /// 推荐替代路径 — FreeSql 完成聚合后由 NCMapper 做内存映射，职责分离。
        /// </summary>
        [Fact]
        public void T_聚合后内存映射_职责分离()
        {
            var stats = _fsql.Select<QpSale>()
                .GroupBy(s => s.Category)
                .ToList(g => new QpCategoryStat
                {
                    Category = g.Key,
                    Count = g.Count(),
                    Total = g.Sum(g.Value.Amount),
                });

            var dtos = Mapper.MapList<QpCategoryStat, QpCategoryStatDto>(stats);

            Assert.Equal(3, dtos!.Count);
            var home = dtos.Single(d => d.Category == "家电");
            Assert.Equal(3, home.Count);
            Assert.Equal(600m, home.Total);
            Assert.Equal(200m, home.Average);
        }

        // ================================================================
        // select-single-table.md 固化项
        // ================================================================

        /// <summary>
        /// WhereIf 动态条件拼装与 AsDto 组合，过滤语义不变。
        /// </summary>
        [Fact]
        public void T_WhereIf_动态条件拼装()
        {
            int? minAmount = 800;
            string? codePrefix = null;

            var dtos = _fsql.Select<QpOrder>()
                .WhereIf(minAmount.HasValue, o => o.Amount >= minAmount!.Value)
                .WhereIf(!string.IsNullOrEmpty(codePrefix), o => o.Code.StartsWith(codePrefix!))
                .OrderBy(o => o.Id)
                .AsDto()
                .ToList<QpOrderDto>();

            Assert.Equal(3, dtos!.Count);
            Assert.All(dtos, d => Assert.True(d.Amount >= 800m));
        }

        /// <summary>
        /// IN 查询 — List&lt;T&gt;.Contains 在全部目标框架下均可翻译为 SQL IN。
        /// </summary>
        [Fact]
        public void T_In查询_List_Contains翻译为SQL_IN()
        {
            var idList = new List<int> { 2, 4, 6 };
            var dtos = _fsql.Select<QpOrder>()
                .Where(o => idList.Contains(o.Id))
                .OrderBy(o => o.Id)
                .AsDto()
                .ToList<QpOrderDto>();

            Assert.Equal(3, dtos!.Count);
            Assert.Equal(new[] { 2, 4, 6 }, dtos.Select(d => d.Id).ToArray());
        }

        /// <summary>
        /// IN 查询的 TFM 行为差异 — 数组 Contains 在 net10.0 下的重载解析变化导致 FreeSql 无法翻译。
        /// <para>根因：.NET 10 起 <c>T[].Contains(T)</c> 优先绑定到 <c>MemoryExtensions.Contains&lt;T&gt;(ReadOnlySpan&lt;T&gt;, T)</c>，
        /// 而 net8.0/net9.0 仍绑定到 <c>Enumerable.Contains&lt;T&gt;(IEnumerable&lt;T&gt;, T)</c>；
        /// FreeSql 3.5.103 的表达式解析仅识别后者，故 net10.0 下抛 NotSupportedException。</para>
        /// <para>结论：属上游框架与 ORM 的兼容性边界（非 NCMapper 引入），使用方应统一用 List&lt;T&gt;.Contains。</para>
        /// </summary>
        [Fact]
        public void T_In查询_数组Contains的TFM行为差异()
        {
            var idArray = new[] { 2, 4, 6 };

            // 归因固化：断言当前 TFM 下数组 Contains 实际绑定的扩展方法宿主类型
            System.Linq.Expressions.Expression<Func<QpOrder, bool>> expr = o => idArray.Contains(o.Id);
            var call = Assert.IsAssignableFrom<System.Linq.Expressions.MethodCallExpression>(expr.Body);
            var declaringType = call.Method.DeclaringType!.Name;

            if (declaringType == "Enumerable")
            {
                // net8.0 / net9.0：绑定 Enumerable.Contains，FreeSql 可翻译为 SQL IN
                var dtos = _fsql.Select<QpOrder>()
                    .Where(o => idArray.Contains(o.Id))
                    .OrderBy(o => o.Id)
                    .AsDto()
                    .ToList<QpOrderDto>();

                Assert.Equal(3, dtos!.Count);
                Assert.Equal(new[] { 2, 4, 6 }, dtos.Select(d => d.Id).ToArray());
            }
            else
            {
                // net10.0：绑定 MemoryExtensions.Contains（span 重载），FreeSql 不识别
                Assert.Equal("MemoryExtensions", declaringType);
                Assert.Throws<NotSupportedException>(() =>
                    _fsql.Select<QpOrder>()
                        .Where(o => idArray.Contains(o.Id))
                        .AsDto()
                        .ToList<QpOrderDto>());
            }
        }

        /// <summary>
        /// First 命中返回 DTO，未命中返回 null（不抛异常）。
        /// </summary>
        [Fact]
        public void T_First_命中与未命中()
        {
            var hit = _fsql.Select<QpOrder>().Where(o => o.Id == 10).AsDto().First<QpOrderDto>();
            var miss = _fsql.Select<QpOrder>().Where(o => o.Id == 9999).AsDto().First<QpOrderDto>();

            Assert.NotNull(hit);
            Assert.Equal("SO-010", hit!.Code);
            Assert.Null(miss);
        }

        /// <summary>
        /// ToChunkDto 投影路径分块 — 批次切分正确且累计条数无遗漏。
        /// </summary>
        [Fact]
        public void T_ToChunkDto投影分块_批次与总量()
        {
            var batches = new List<int>();
            var ids = new List<int>();

            _fsql.Select<QpOrder>()
                .OrderBy(o => o.Id)
                .AsDto()
                .ToChunkDto<QpOrderDto>(3, chunk =>
                {
                    batches.Add(chunk.Count);
                    ids.AddRange(chunk.Select(x => x.Id));
                });

            Assert.Equal(new[] { 3, 3, 3, 1 }, batches.ToArray());
            Assert.Equal(10, ids.Count);
            Assert.Equal(Enumerable.Range(1, 10), ids);
        }

        /// <summary>
        /// ToChunkDto 降级路径分块 — 逐批内存映射，转换器对每条记录生效。
        /// </summary>
        [Fact]
        public void T_ToChunkDto降级分块_转换器逐条生效()
        {
            var batches = new List<int>();
            var codes = new List<string?>();

            _fsql.Select<QpOrder>()
                .OrderBy(o => o.Id)
                .AsDto()
                .ToChunkDto<QpOrderConvDto>(4, chunk =>
                {
                    batches.Add(chunk.Count);
                    codes.AddRange(chunk.Select(x => x.Code));
                });

            Assert.Equal(new[] { 4, 4, 2 }, batches.ToArray());
            Assert.Equal(10, codes.Count);
            Assert.All(codes, c => Assert.False(string.IsNullOrEmpty(c)));
        }

        /// <summary>
        /// 空结果集边界 — ToList 返回空集合；ToChunkDto 空结果不回调（③ 契约统一，与 EFCore/Dapper 对齐）。
        /// </summary>
        [Fact]
        public void T_空结果集_ToList空集合与ToChunk空结果不回调()
        {
            var list = _fsql.Select<QpOrder>().Where(o => o.Amount > 100000m).AsDto().ToList<QpOrderDto>();
            Assert.NotNull(list);
            Assert.Empty(list);

            var callbackCount = 0;
            var itemCount = 0;
            _fsql.Select<QpOrder>()
                .Where(o => o.Amount > 100000m)
                .AsDto()
                .ToChunkDto<QpOrderDto>(3, chunk =>
                {
                    callbackCount++;
                    itemCount += chunk.Count;
                });

            Assert.Equal(0, callbackCount);
            Assert.Equal(0, itemCount);
        }

        // ================================================================
        // 种子数据
        // ================================================================

        private static void SeedData(IFreeSql fsql)
        {
            fsql.Insert(new List<QpRegion>
            {
                new() { Id = 1, Name = "华东" },
                new() { Id = 2, Name = "华南" },
            }).ExecuteAffrows();

            var orders = Enumerable.Range(1, 10)
                .Select(i => new QpOrder
                {
                    Id = i,
                    Code = $"SO-{i:D3}",
                    Amount = i * 100m,
                    RegionId = i % 2 == 0 ? 2 : 1,
                })
                .ToList();
            fsql.Insert(orders).ExecuteAffrows();

            fsql.Insert(new List<QpSale>
            {
                new() { Id = 1, Category = "家电", Amount = 100m },
                new() { Id = 2, Category = "家电", Amount = 200m },
                new() { Id = 3, Category = "家电", Amount = 300m },
                new() { Id = 4, Category = "食品", Amount = 50m },
                new() { Id = 5, Category = "食品", Amount = 70m },
                new() { Id = 6, Category = "服饰", Amount = 400m },
            }).ExecuteAffrows();

            fsql.CodeFirst.SyncStructure(typeof(QpLog), "Wiki_QP_Log_202601");
            fsql.CodeFirst.SyncStructure(typeof(QpLog), "Wiki_QP_Log_202602");
            fsql.Insert(new List<QpLog> { new() { Id = 1, Message = "一月日志" } })
                .AsTable("Wiki_QP_Log_202601").ExecuteAffrows();
            fsql.Insert(new List<QpLog> { new() { Id = 2, Message = "二月日志" } })
                .AsTable("Wiki_QP_Log_202602").ExecuteAffrows();
        }
    }

    #region 测试实体（Wiki_QP_* 隔离表名）

    /// <summary>
    /// 查询组合测试用订单实体。
    /// </summary>
    [Table(Name = "Wiki_QP_Order")]
    public class QpOrder
    {
        /// <summary>
        /// 主键。
        /// </summary>
        [Column(IsPrimary = true)]
        public int Id { get; set; }

        /// <summary>
        /// 订单编号。
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 订单金额。
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// 所属区域外键。
        /// </summary>
        public int RegionId { get; set; }
    }

    /// <summary>
    /// 查询组合测试用区域实体。
    /// </summary>
    [Table(Name = "Wiki_QP_Region")]
    public class QpRegion
    {
        /// <summary>
        /// 主键。
        /// </summary>
        [Column(IsPrimary = true)]
        public int Id { get; set; }

        /// <summary>
        /// 区域名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// 查询组合测试用日志实体（分表验证）。
    /// </summary>
    [Table(Name = "Wiki_QP_Log")]
    public class QpLog
    {
        /// <summary>
        /// 主键。
        /// </summary>
        [Column(IsPrimary = true)]
        public int Id { get; set; }

        /// <summary>
        /// 日志内容。
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 查询组合测试用销售记录实体（聚合验证）。
    /// </summary>
    [Table(Name = "Wiki_QP_Sale")]
    public class QpSale
    {
        /// <summary>
        /// 主键。
        /// </summary>
        [Column(IsPrimary = true)]
        public int Id { get; set; }

        /// <summary>
        /// 商品类目。
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// 销售金额。
        /// </summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 聚合中间类型（承接 FreeSql 分组结果）。
    /// </summary>
    public class QpCategoryStat
    {
        /// <summary>
        /// 商品类目。
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// 分组内记录笔数。
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 分组内金额合计。
        /// </summary>
        public decimal Total { get; set; }
    }

    #endregion

    #region 测试 DTO

    /// <summary>
    /// 订单投影 DTO（无降级触发器）。
    /// </summary>
    [MapFrom(typeof(QpOrder))]
    public class QpOrderDto
    {
        /// <summary>
        /// 主键。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 订单编号。
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 订单金额。
        /// </summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 订单降级 DTO（含 ConvertUsing，强制内存映射路径）。
    /// </summary>
    [MapFrom(typeof(QpOrder))]
    public class QpOrderConvDto
    {
        /// <summary>
        /// 主键。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 订单编号（经转换器处理）。
        /// </summary>
        [Convert(typeof(QpCodeConverter), nameof(QpCodeConverter.Convert))]
        public string? Code { get; set; }
    }

    /// <summary>
    /// 订单 + 区域 Compose DTO。
    /// </summary>
    [MapFrom(typeof(QpOrder))]
    public class QpOrderRegionDto
    {
        /// <summary>
        /// 订单主键。
        /// </summary>
        [MapProperty(typeof(QpOrder), "Id")]
        public int Id { get; set; }

        /// <summary>
        /// 订单编号。
        /// </summary>
        [MapProperty(typeof(QpOrder), "Code")]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 区域名称（T2 直取）。
        /// </summary>
        [MapProperty(typeof(QpRegion), "Name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// 日志投影 DTO。
    /// </summary>
    [MapFrom(typeof(QpLog))]
    public class QpLogDto
    {
        /// <summary>
        /// 主键。
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 日志内容。
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 类目统计 DTO（由聚合中间类型映射）。
    /// </summary>
    [MapFrom(typeof(QpCategoryStat))]
    public class QpCategoryStatDto
    {
        /// <summary>
        /// 商品类目。
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// 分组内记录笔数。
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 分组内金额合计。
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        /// 分组内平均金额（DTO 侧派生，不参与映射）。
        /// </summary>
        [MapIgnore]
        public decimal Average => Count == 0 ? 0m : Total / Count;
    }

    /// <summary>
    /// 订单编号转换器（静态方法引用形态，T1 模板 M(TField)）— 原样返回，仅用于触发降级路径。
    /// </summary>
    public static class QpCodeConverter
    {
        /// <summary>
        /// 执行转换。
        /// </summary>
        /// <param name="fieldValue">源字段值。</param>
        /// <returns>订单编号原值。</returns>
        public static string? Convert(string? fieldValue) => fieldValue;
    }

    #endregion
}
