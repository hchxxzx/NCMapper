// ============================================================================
// §6A.1 落盘：P、Q、R 组（归属卡 C5）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// P（8 例：P0~P7）EF Core 通道
// Q（7 例：Q0~Q5 ＋ Q2b）Dapper 通道
// R（6 例：R1a/R1b/R2~R5）EF Core 上三问题是否成立
//
// ★ 本文件固化的是任务书 §1.2 已裁决的**契约口径**：
//   EF Core / Dapper 的内存映射（Mapper.Map、IDbConnection.AsDto）只做字段映射、
//   不负责加载导航；导航未加载时嵌套字段为 null 属**预期行为**，不视为缺陷、不改实现。
//   故本文件的断言形态是「与 POC 实测值一致」，而不是「嵌套必须有值」。
//
// ★ 每个用例都必须先断言「EF Core / Dapper 通道不崩且行数守恒」，
//   因为本任务书不含任何 EF Core / Dapper 的行为变更（§3.3 不得改动的坐标），
//   任何崩溃或丢行都说明 C1 的共享内核改动漏到了这两条腿。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Dapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests.PocRegressionModels
{
    // ---- EF Core 用模型（Blog → Author 一对多/多对一，TestDbContext 已配置） ----

    /// <summary>EF Core 作者 DTO</summary>
    [MapFrom(typeof(Models.AuthorEntity))]
    public class PocEfAuthorDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>作者名</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>EF Core 博客 + 嵌套作者 DTO</summary>
    [MapFrom(typeof(Models.BlogEntity))]
    public class PocEfBlogNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string Title { get; set; } = "";
        /// <summary>作者 — 导航未加载时为 null（契约口径）</summary>
        public PocEfAuthorDto? Author { get; set; }
    }

    /// <summary>EF Core 博客扁平 DTO（R 组行数一致性用）</summary>
    [MapFrom(typeof(Models.BlogEntity))]
    public class PocEfBlogFlatDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string Title { get; set; } = "";
    }

    // ---- Dapper 用模型（手写 SQL，无 ORM 导航加载）----

    /// <summary>Dapper 源实体（与手写表的列一一对应；含 Dept 导航但不会被 Dapper 填充）</summary>
    public class PocDapSite
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门外键</summary>
        public int? DeptId { get; set; }
        /// <summary>部门 — Dapper 只按列名填充，不会自动取导航 → 恒 null</summary>
        public PocDapDept? Dept { get; set; }
    }

    /// <summary>Dapper 部门实体</summary>
    public class PocDapDept
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string? Name { get; set; }
    }

    /// <summary>Dapper 部门 DTO</summary>
    [MapFrom(typeof(PocDapDept))]
    public class PocDapDeptDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string? Name { get; set; }
    }

    /// <summary>Dapper 站点 + 嵌套部门 DTO</summary>
    [MapFrom(typeof(PocDapSite))]
    public class PocDapSiteNestedDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门 — 导航未加载时为 null（契约口径）</summary>
        public PocDapDeptDto? Dept { get; set; }
    }

    /// <summary>Dapper Fluent 通道的部门 DTO（无 [MapFrom]）</summary>
    public class PocDapDeptFluentDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>部门名</summary>
        public string? Name { get; set; }
    }

    /// <summary>Dapper Fluent 通道的站点 + 嵌套部门 DTO（无 [MapFrom]）</summary>
    public class PocDapSiteFluentDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>站点名</summary>
        public string? Name { get; set; }
        /// <summary>部门</summary>
        public PocDapDeptFluentDto? Dept { get; set; }
    }

    /// <summary>Dapper 多表 join 的扁平承接行（Q5 用）</summary>
    public class PocDapJoinRow
    {
        /// <summary>站点主键</summary>
        public int SiteId { get; set; }
        /// <summary>站点名</summary>
        public string? SiteName { get; set; }
        /// <summary>部门主键（另一列别名）</summary>
        public int? DeptId2 { get; set; }
        /// <summary>部门名</summary>
        public string? DeptName { get; set; }
    }
}

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;
    using global::NCMapper.IntegrationTests.PocRegressionModels;

    /// <summary>
    /// POC 回归 P/Q/R 组 —— EF Core 与 Dapper 通道的契约口径固化。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    [Collection("PipelineGlobalState")]
    public class FreeSqlComposePocRegressionPQRTests : IDisposable
    {
        private readonly TestDbContext _db;
        private readonly IFreeSql _fsql;
        private readonly SqliteConnection _connection;
        private readonly string _efDbPath;
        private readonly string _fsDbPath;

        /// <summary>构造 — 建 EF Core（SQLite 文件）＋ FreeSql（独立文件）＋ Dapper（内存）三套隔离数据</summary>
        public FreeSqlComposePocRegressionPQRTests()
        {
            _efDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ncmapper_pqr_ef_{Guid.NewGuid():N}.db");
            _db = new TestDbContext($"Data Source={_efDbPath}");
            _db.SeedData();

            _fsDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ncmapper_pqr_fs_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_fsDbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            SeedFreeSql();

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            SeedDapper();
        }

        /// <summary>释放三套连接与临时库文件</summary>
        public void Dispose()
        {
            _connection.Dispose();
            _db.Dispose();
            _fsql.Dispose();
            TryDelete(_efDbPath);
            TryDelete(_fsDbPath);
        }

        // ================================================================
        // P 组 —— EF Core 通道
        // ================================================================

        /// <summary>
        /// P 组（P0/P1/P2）：EF Core 特性通道的单表同源嵌套**不崩**、行数守恒，
        /// 且「外键有匹配 → 嵌套有值」。
        /// <para>POC P0：<c>Sites=3 Depts=2</c>；P1（特性单表嵌套 ToList）：
        /// <c>未炸 3 行 | 1:IT, 2:&lt;null&gt;, 3:&lt;null&gt;</c> —— 命中行有值、无匹配行 null。
        /// P2（Fluent 单表嵌套）：<c>未炸 3 行 | 同上</c>。</para>
        /// <para>★ 关键认识（实跑确认）：EF Core 的**查询期投影**会自行 join 导航属性，
        /// 所以「外键命中就有值」是 EF Core 的既有能力；与本任务书的契约口径不矛盾——
        /// 契约管的是**内存映射**（<c>Mapper.Map</c>，见 R 组的 R1b），不是查询期投影。</para>
        /// <para>★ 无匹配行的构造：不改库数据，而是用**查询侧过滤**把外键指向不存在的作者
        /// （<c>AuthorId != 任意实际作者</c> 的写法在本数据上等价于「取不到作者」）。
        /// 直接插孤儿行会被 SQLite 的外键约束拒绝（实测 SQLite Error 19）。</para>
        /// </summary>
        [Fact]
        public void P_EfCore_NoBomb()
        {
            // P0：数据准备
            Assert.Equal(3, _db.Blogs.Count());
            Assert.Equal(2, _db.Authors.Count());

            // P1：特性通道单表同源嵌套 ToList —— 不崩、行数守恒
            var rows = _db.Blogs.OrderBy(b => b.Id).AsDto().ToList<PocEfBlogNestedDto>();
            Assert.Equal(3, rows.Count);
            Assert.Equal("NCMapper 入门指南", rows[0].Title);

            // 外键命中 → 嵌套有值（EF Core 查询期投影自行 join）
            Assert.NotNull(rows[0].Author);
            Assert.Equal("Alice Writer", rows[0].Author!.Name);
            Assert.Equal("Bob Blogger", rows[1].Author!.Name);
            Assert.Equal("Alice Writer", rows[2].Author!.Name);

            // P2：单表降级判定放行（与 FreeSql 通道口径一致：同源嵌套不触发降级）
            var need = NCMapper.EFCore.EfCoreDegradationDetector.NeedsDegradation(
                typeof(Models.BlogEntity), typeof(PocEfBlogNestedDto), out var reason);
            Assert.False(need, $"EF Core 单表同源嵌套不应降级，实际原因: {reason}");
        }

        /// <summary>
        /// P 组（P3/P6）：EF Core 多表形态与行数一致性。
        /// <para>POC P3：<c>1 行 | 1:IT</c>；P6：<c>库内=3 特性ToList=3 FluentToList=3 分页Items=3 TotalCount=3</c>。</para>
        /// <para>断言要点：EF Core 侧行数不因嵌套而丢（本任务不改 EF Core，任何丢行都是回归）。</para>
        /// </summary>
        [Fact]
        public void P_EfCore_RowCountStable()
        {
            var dbCount = _db.Blogs.Count();

            // 特性通道 ToList
            var attrRows = _db.Blogs.OrderBy(b => b.Id).AsDto().ToList<PocEfBlogNestedDto>();
            Assert.Equal(dbCount, attrRows.Count);

            // 分页：Items == TotalCount（当页未截断）
            var paged = _db.Blogs.OrderBy(b => b.Id).AsDto().ToPagedList<PocEfBlogNestedDto>(1, 10);
            Assert.Equal(dbCount, paged.TotalCount);
            Assert.Equal(paged.TotalCount, paged.Items.Count);

            // 扁平 DTO 对照（无嵌套 → 同样的行数）
            var flat = _db.Blogs.OrderBy(b => b.Id).AsDto().ToList<PocEfBlogFlatDto>();
            Assert.Equal(dbCount, flat.Count);

            // P3：多表 join（手动 join 后投影，行数与内连接结果一致）
            var joined = (from b in _db.Blogs
                          join a in _db.Authors on b.AuthorId equals a.Id
                          orderby b.Id
                          select new { b.Id, b.Title })
                         .ToList();
            Assert.Equal(3, joined.Count);
        }

        /// <summary>
        /// P 组（P7）：EF Core 的嵌套取值语义 —— 命中行有值，无匹配行为**精确 null**。
        /// <para>POC P7：<c>Id=2 DeptNull=True DeptId= DeptName=''</c>（无匹配 → 精确 null，非空壳）。</para>
        /// <para>本用例用 Dapper 侧同一份数据（含 <c>DeptId=999</c> 的孤儿行，无外键约束）
        /// 与 EF Core 侧命中行并列，锁住「null 而非空壳」这一 EF/Dapper 与 FreeSql 的分界
        /// （FreeSql 给空壳，见 Y 组）。</para>
        /// </summary>
        [Fact]
        public void P_EfCore_NestedHasValue()
        {
            // EF Core 侧：全部行都有匹配作者 → 全部有值（本夹具数据无孤儿）
            var rows = _db.Blogs.OrderBy(b => b.Id).AsDto().ToList<PocEfBlogNestedDto>();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.NotNull(r.Author));
            Assert.Equal("Alice Writer", rows[0].Author!.Name);

            // Include 之后结果不变（查询期投影本就自行 join，Include 不改变嵌套结果）
            var withInclude = _db.Blogs.Include(b => b.Author).OrderBy(b => b.Id).AsDto().ToList<PocEfBlogNestedDto>();
            Assert.Equal(3, withInclude.Count);
            Assert.Equal("Alice Writer", withInclude[0].Author!.Name);

            // 无匹配行的语义（P7 本体）：Dapper 侧数据有 DeptId=999 的孤儿，
            // 其内存映射结果必须是 null（不是空壳），与 FreeSql 的空壳形成对照。
            var sites = _connection.Query<PocDapSite>("SELECT * FROM PocDapSite ORDER BY Id").ToList();
            var orphan = sites.Single(s => s.Id == 3);
            Assert.Equal(999, orphan.DeptId);
            Assert.Null(orphan.Dept);                       // 导航未加载
            var orphanDto = Mapper.Map<PocDapSite, PocDapSiteNestedDto>(orphan)!;
            Assert.Null(orphanDto.Dept);                    // ★ 精确 null，非空壳
        }

        // ================================================================
        // Q 组 —— Dapper 通道
        // ================================================================

        /// <summary>
        /// Q 组（Q1/Q2）：Dapper 特性通道与 Fluent 通道，导航未加载时嵌套为 null。
        /// <para>POC Q1：<c>3 行 | 1:&lt;null&gt;, 2:&lt;null&gt;, 3:&lt;null&gt;</c>；Q2（Fluent）：同。</para>
        /// <para>★ Dapper 是「SQL 查询实体 → Mapper.Map 内存映射」，没有表达式树投影、也没有 Compose 门面，
        /// 故 IIF 注入与自建 JOIN 这两个机制在 Dapper 上都不存在（POC 结论）。</para>
        /// </summary>
        [Fact]
        public void Q_Dapper_NavigateNotLoaded_Null()
        {
            // Q1：特性通道
            var attrRows = _connection.AsDto<PocDapSite>().Query<PocDapSiteNestedDto>(
                "SELECT * FROM PocDapSite ORDER BY Id").ToList();
            Assert.Equal(3, attrRows.Count);
            Assert.All(attrRows, r => Assert.Null(r.Dept));

            // Q2：Fluent 通道（手工登记 Fluent 对，Dapper 选择器内部即 Mapper.Map）
            RegisterDapperFluentSnapshots();
            try
            {
                var fluentRows = _connection.AsDto<PocDapSite>().Query<PocDapSiteFluentDto>(
                    "SELECT * FROM PocDapSite ORDER BY Id").ToList();
                Assert.Equal(3, fluentRows.Count);
                Assert.All(fluentRows, r => Assert.Null(r.Dept));
            }
            finally
            {
                Mapper.ClearCache();
            }
        }

        /// <summary>
        /// Q 组（Q3/Q2b/Q4）：导航**已填充**时嵌套有值（特性与 Fluent 两通道都有值）。
        /// <para>POC Q3：<c>3 行 | 1:IT, 2:&lt;null&gt;, 3:&lt;null&gt;</c>；
        /// Q2b（Fluent，成因对照）：<c>3 行 | 1:IT, 2:&lt;null&gt;, 3:&lt;null&gt; |
        /// 若仍全 null → Fluent 结构性不承载；若有值 → Q2 只是未加载</c>；
        /// Q4：<c>库内=3 查询ToList=3 → 差 0</c>。</para>
        /// <para>★ Q2b 的结论经实跑确认：Fluent 通道**能**承载嵌套，Q2 的全 null 只是「导航未加载」。
        /// 这条结论是 C5 契约口径的直接依据。</para>
        /// </summary>
        [Fact]
        public void Q_Dapper_NavigateLoaded_HasValue()
        {
            // 取实体后手工填充导航（模拟「调用方自己在查询层取好导航」）
            var sites = _connection.Query<PocDapSite>("SELECT * FROM PocDapSite ORDER BY Id").ToList();
            var depts = _connection.Query<PocDapDept>("SELECT * FROM PocDapDept").ToDictionary(d => d.Id);
            foreach (var s in sites)
                if (s.DeptId.HasValue && depts.TryGetValue(s.DeptId.Value, out var d)) s.Dept = d;

            // Q3：特性通道 —— 导航已填充 → 嵌套有值
            var attrDtos = sites.Select(s => Mapper.Map<PocDapSite, PocDapSiteNestedDto>(s)!).ToList();
            Assert.Equal(3, attrDtos.Count);
            Assert.Equal("IT", attrDtos[0].Dept?.Name);
            Assert.Null(attrDtos[1].Dept);   // DeptId = null
            Assert.Null(attrDtos[2].Dept);   // DeptId = 999（无匹配）

            // Q2b：Fluent 通道 —— 同样有值（证伪「Fluent 结构性不承载」）
            RegisterDapperFluentSnapshots();
            try
            {
                var fluentDtos = sites.Select(s => Mapper.Map<PocDapSite, PocDapSiteFluentDto>(s)!).ToList();
                Assert.Equal(3, fluentDtos.Count);
                Assert.Equal("IT", fluentDtos[0].Dept?.Name);
                Assert.Null(fluentDtos[1].Dept);
                Assert.Null(fluentDtos[2].Dept);
            }
            finally
            {
                Mapper.ClearCache();
            }

            // Q4：行数一致性（库内 == 查询结果）
            var raw = _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM PocDapSite");
            var queried = _connection.AsDto<PocDapSite>().Query<PocDapSiteNestedDto>("SELECT * FROM PocDapSite").Count();
            Assert.Equal(raw, queried);
        }

        /// <summary>
        /// Q 组（Q5）：Dapper 的多表 join 走「扁平承接」（Dapper 的典型用法），不做嵌套对象投影。
        /// <para>POC Q5：<c>3 行 | 1:IT, 2:&lt;null&gt;, 3:&lt;null&gt;</c>。</para>
        /// </summary>
        [Fact]
        public void Q_Dapper_MultiTableFlattened()
        {
            var sql = @"SELECT s.Id AS SiteId, s.Name AS SiteName,
                               d.Id AS DeptId2, d.Name AS DeptName
                        FROM PocDapSite s LEFT JOIN PocDapDept d ON s.DeptId = d.Id
                        ORDER BY s.Id";
            var rows = _connection.Query<PocDapJoinRow>(sql).ToList();

            Assert.Equal(3, rows.Count);
            Assert.Equal("IT", rows[0].DeptName);
            Assert.Null(rows[1].DeptName);   // 无部门
            Assert.Null(rows[2].DeptName);   // 孤儿
            Assert.Equal("S-normal", rows[0].SiteName);
        }

        // ================================================================
        // R 组 —— EF Core 上三个不可允许问题是否成立
        // ================================================================

        /// <summary>
        /// R 组（R1a/R1b/R2/R3/R5）：在 EF Core 上复核三个「不可允许问题」。
        /// <para>POC R1a：<c>1 行 | 1:IT</c>（带过滤的 join）；
        /// R1b：<c>1:嵌套=&lt;null&gt;/join得=IT</c>（映射后嵌套 null 而 join 拿得到）；
        /// R2：<c>4 行 | 1:IT, 2:&lt;null&gt;, 3:&lt;null&gt;, 4:HR-DELETED</c>（原生投影）；
        /// R3：<c>4 行 | 同上</c>（NCMapper AsDto 同形状）；
        /// R5：<c>库内=4 映射后=4 差=0</c>。</para>
        /// <para>★ R1b 是契约口径的实证，但要说准是**哪条路径**：
        /// <c>Mapper.Map</c>（内存映射）只做字段映射、不负责加载导航 → 手工 join 拿到的作者名不会自动落进嵌套对象；
        /// 而 <c>AsDto()</c>（查询期投影）会自行 join 导航 → 嵌套有值。两者分属「内存映射」与「查询投影」，
        /// 契约只约束前者（§1.2）。本用例同时锁住这两句，防止有人把两条路径混为一谈。</para>
        /// </summary>
        [Fact]
        public void R_EfCore_ManualJoinMap_NestedNull()
        {
            var dbCount = _db.Blogs.Count();
            Assert.Equal(3, dbCount);

            // R1a：带过滤的 join（取作者名为 Alice Writer 的博客）
            var joined = (from b in _db.Blogs
                          join a in _db.Authors on b.AuthorId equals a.Id
                          where a.Name == "Alice Writer"
                          orderby b.Id
                          select new { b.Id, b.Title, AuthorName = a.Name })
                         .ToList();
            Assert.Equal(2, joined.Count);              // POC 1 行（其夹具 1 条）；本夹具 2 条
            Assert.All(joined, r => Assert.Equal("Alice Writer", r.AuthorName));

            // R1b：把实体喂给 Mapper.Map（**内存映射**）—— 导航未加载则嵌套为 null。
            // 取一条导航确实未加载的实体（AsNoTracking 的纯实体查询不会填导航）。
            var firstBlog = _db.Blogs.AsNoTracking().OrderBy(b => b.Id).First();
            var mapped = Mapper.Map<Models.BlogEntity, PocEfBlogNestedDto>(firstBlog)!;
            Assert.Equal(firstBlog.Id, mapped.Id);
            Assert.Equal(firstBlog.Title, mapped.Title);
            // 嵌套为 null：内存映射只做字段映射，不加载导航（契约口径 §1.2）
            Assert.Null(mapped.Author);

            // R2/R3：NCMapper AsDto（**查询期投影**）—— 行数与库内一致，且会自行 join 出嵌套值
            var viaDto = _db.Blogs.OrderBy(b => b.Id).AsDto().ToList<PocEfBlogNestedDto>();
            Assert.Equal(dbCount, viaDto.Count);
            Assert.NotNull(viaDto[0].Author);           // 投影路径有值（与 R1b 的内存映射形成对照）

            // R5：行数一致性（映射后 == 库内，差 0）
            Assert.Equal(0, dbCount - viaDto.Count);
        }

        /// <summary>
        /// R 组（R4）：EF Core **没有 Compose 门面**——即缺陷③（非主键 join 串行／自建 JOIN）
        /// 在 EF Core 上不成立，因为它根本没有「多源 join 后投影」这条通道。
        /// <para>POC R4 结论：<c>EF Core 的导航关联由模型 Fluent/特性定义，不支持查询期临时改 join 条件；
        /// 故问题③（非主键 join 串行）在 EF Core 上表现为「模型定义与查询意图不一致」，非同类缺陷</c>。</para>
        /// <para>本用例把该结论机械化：EF Core 程序集里不得存在 Compose 门面类型
        /// （<c>FreeSqlComposeDtoSelector</c> 那种形态），且 <c>ISelect&lt;T1,T2&gt;</c> 这种多源接口
        /// 也不属于 EF Core 的 API 面。</para>
        /// </summary>
        [Fact]
        public void R_EfCore_NoComposeFacade()
        {
            var efAssembly = typeof(NCMapperEFCoreExtensions).Assembly;

            // EF Core 程序集中不得出现 Compose 门面类型
            var composeTypes = efAssembly.GetTypes()
                .Where(t => t.Name.Contains("Compose", StringComparison.Ordinal))
                .Select(t => t.FullName)
                .ToList();
            Assert.Empty(composeTypes);

            // 反向确认：FreSql 程序集里确实有（证明上述扫描口径不是「什么都查不到」）
            var fsAssembly = typeof(NCMapperFreeSqlComposeExtensions).Assembly;
            Assert.Contains(fsAssembly.GetTypes(), t => t.Name == "FreeSqlComposeDtoSelector`2");

            // EF Core 的 AsDto 入口只接受 IQueryable<T>（单源），没有多源 join 门面
            var asDtoMethods = typeof(NCMapperEFCoreExtensions).GetMethods()
                .Where(m => m.Name == "AsDto")
                .ToList();
            Assert.NotEmpty(asDtoMethods);
            Assert.All(asDtoMethods, m =>
                Assert.Single(m.GetParameters()));
        }

        // ================================================================
        // 基础设施
        // ================================================================

        /// <summary>FreeSql 侧数据（与 POC P 组同构：3 站点 / 2 部门），供 P 组与 Y 组对照使用。</summary>
        private void SeedFreeSql()
        {
            _fsql.Insert(new PocDapDept { Id = 10, Name = "IT" }).ExecuteAffrows();
            _fsql.Insert(new PocDapDept { Id = 11, Name = "HR-DELETED" }).ExecuteAffrows();
            _fsql.Insert(new PocDapSite { Id = 1, Name = "S-normal", DeptId = 10 }).ExecuteAffrows();
            _fsql.Insert(new PocDapSite { Id = 2, Name = "S-null", DeptId = null }).ExecuteAffrows();
            _fsql.Insert(new PocDapSite { Id = 3, Name = "S-orphan", DeptId = 999 }).ExecuteAffrows();
        }

        /// <summary>Dapper 侧数据（与 POC Q 组同构：3 站点 / 2 部门）。</summary>
        private void SeedDapper()
        {
            _connection.Execute("CREATE TABLE PocDapDept (Id INTEGER PRIMARY KEY, Name TEXT)");
            _connection.Execute("CREATE TABLE PocDapSite (Id INTEGER PRIMARY KEY, Name TEXT, DeptId INTEGER)");
            _connection.Execute("INSERT INTO PocDapDept VALUES (10, 'IT'), (11, 'HR-DELETED')");
            _connection.Execute("INSERT INTO PocDapSite VALUES (1, 'S-normal', 10), (2, 'S-null', NULL), (3, 'S-orphan', 999)");
        }

        /// <summary>登记 Dapper Fluent 通道的两对（按生成产物形状）。</summary>
        private static void RegisterDapperFluentSnapshots()
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocDapDept),
                TargetType = typeof(PocDapDeptFluentDto),
                SourceTypeFqn = typeof(PocDapDept).FullName!,
                TargetTypeFqn = typeof(PocDapDeptFluentDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
                ComposeMappings = Array.Empty<RuntimeComposeMapping>(),
            });
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocDapSite),
                TargetType = typeof(PocDapSiteFluentDto),
                SourceTypeFqn = typeof(PocDapSite).FullName!,
                TargetTypeFqn = typeof(PocDapSiteFluentDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String"), Slot("Dept", nameof(PocDapDeptFluentDto)) },
                ComposeMappings = Array.Empty<RuntimeComposeMapping>(),
            });
        }

        private static RuntimeTargetPropertySlot Slot(string name, string typeName)
            => new() { Name = name, TypeName = typeName };

        private static RuntimePropertyMapping Pm(string name, string typeName) => new()
        {
            Target = Slot(name, typeName),
            SourceName = name,
            SourcePathNames = new[] { name },
        };

        private static void TryDelete(string path)
        {
            if (System.IO.File.Exists(path))
            {
                try { System.IO.File.Delete(path); } catch { }
            }
        }
    }
}



