using Xunit;
using Microsoft.EntityFrameworkCore;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests.Models
{
    // ================================================================
    // 嵌套跨表 [MapProperty] 集成测试 — 实体
    // <para>链路：EfOrder → EfDetail → EfProduct / EfSubDetail → EfExtra</para>
    // <para>投影路径：ORM 将 src.Detail.Product.Sku 翻译为多级 LEFT JOIN；
    // MapList 路径：内存 null 安全导航。两条路径结果应一致。</para>
    // ================================================================

    /// <summary>订单实体（含 Detail 一对一导航）</summary>
    public class EfOrder
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单编号</summary>
        public string? OrderNo { get; set; }
        /// <summary>明细外键（可空 — 支持一级 null 场景）</summary>
        public int? DetailId { get; set; }
        /// <summary>明细导航属性</summary>
        public EfDetail? Detail { get; set; }
    }

    /// <summary>订单明细实体（含 Product / SubDetail 导航）</summary>
    public class EfDetail
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>备注</summary>
        public string? Remark { get; set; }
        /// <summary>产品外键（可空 — 支持末级 null 场景）</summary>
        public int? ProductId { get; set; }
        /// <summary>产品导航属性</summary>
        public EfProduct? Product { get; set; }
        /// <summary>子明细外键（可空）</summary>
        public int? SubDetailId { get; set; }
        /// <summary>子明细导航属性</summary>
        public EfSubDetail? SubDetail { get; set; }
    }

    /// <summary>产品实体</summary>
    public class EfProduct
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>SKU 编码</summary>
        public string? Sku { get; set; }
        /// <summary>价格（值类型）</summary>
        public int Price { get; set; }
    }

    /// <summary>子明细实体（含 Extra 导航 — 第三层嵌套）</summary>
    public class EfSubDetail
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>注释</summary>
        public string? Note { get; set; }
        /// <summary>附加信息外键（可空）</summary>
        public int? ExtraId { get; set; }
        /// <summary>附加信息导航属性</summary>
        public EfExtra? Extra { get; set; }
    }

    /// <summary>附加信息实体（链路最深层）</summary>
    public class EfExtra
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标签</summary>
        public string? Tag { get; set; }
    }

    // ================================================================
    // DTO — 嵌套跨表 [MapProperty]
    // ================================================================

    /// <summary>订单 DTO — 含嵌套 DetailDto</summary>
    [MapFrom(typeof(EfOrder))]
    public class EfOrderDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单编号</summary>
        public string? OrderNo { get; set; }
        /// <summary>嵌套明细 DTO</summary>
        public EfDetailDto? Detail { get; set; }
    }

    /// <summary>明细 DTO — 含跨表 [MapProperty]（Product.Sku/Price）+ 嵌套 SubDetailDto</summary>
    [MapFrom(typeof(EfDetail))]
    public class EfDetailDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>备注</summary>
        public string? Remark { get; set; }

        /// <summary>产品 SKU — 跨表映射自 EfProduct.Sku（经 Detail.Product 导航）</summary>
        [MapProperty(typeof(EfProduct), nameof(EfProduct.Sku))]
        public string? ProductSku { get; set; }

        /// <summary>产品价格 — 跨表映射自 EfProduct.Price（int 目标，null 导航 → 默认 0）</summary>
        [MapProperty(typeof(EfProduct), nameof(EfProduct.Price))]
        public int ProductPrice { get; set; }

        /// <summary>嵌套子明细 DTO</summary>
        public EfSubDetailDto? SubDetail { get; set; }
    }

    /// <summary>子明细 DTO — 含跨表 [MapProperty]（Extra.Tag，第三层）</summary>
    [MapFrom(typeof(EfSubDetail))]
    public class EfSubDetailDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>注释</summary>
        public string? Note { get; set; }

        /// <summary>附加标签 — 跨表映射自 EfExtra.Tag（经 SubDetail.Extra 导航）</summary>
        [MapProperty(typeof(EfExtra), nameof(EfExtra.Tag))]
        public string? ExtraTag { get; set; }
    }

    /// <summary>明细 DTO（可空值类型变体）— ProductPrice 为 int?，null 导航 → null</summary>
    [MapFrom(typeof(EfDetail))]
    public class EfDetailNullableDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>备注</summary>
        public string? Remark { get; set; }

        /// <summary>产品价格（可空）— 跨表映射自 EfProduct.Price（int? 目标，null 导航 → null）</summary>
        [MapProperty(typeof(EfProduct), nameof(EfProduct.Price))]
        public int? ProductPrice { get; set; }
    }

    /// <summary>订单 DTO（可空值类型变体）— 嵌套 EfDetailNullableDto</summary>
    [MapFrom(typeof(EfOrder))]
    public class EfOrderNullableDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单编号</summary>
        public string? OrderNo { get; set; }
        /// <summary>嵌套明细 DTO（可空值类型）</summary>
        public EfDetailNullableDto? Detail { get; set; }
    }

    // ================================================================
    // DTO — 回归：纯嵌套投影（无跨表 [MapProperty]）
    // ================================================================

    /// <summary>订单 DTO（纯嵌套，无 [MapProperty]）— 回归既有嵌套投影能力</summary>
    [MapFrom(typeof(EfOrder))]
    public class EfOrderPlainDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单编号</summary>
        public string? OrderNo { get; set; }
        /// <summary>嵌套明细 DTO（纯同名映射）</summary>
        public EfDetailPlainDto? Detail { get; set; }
    }

    /// <summary>明细 DTO（纯嵌套，无 [MapProperty]）</summary>
    [MapFrom(typeof(EfDetail))]
    public class EfDetailPlainDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>备注</summary>
        public string? Remark { get; set; }
        /// <summary>嵌套子明细 DTO（纯同名映射）</summary>
        public EfSubDetailPlainDto? SubDetail { get; set; }
    }

    /// <summary>子明细 DTO（纯嵌套，无 [MapProperty]）</summary>
    [MapFrom(typeof(EfSubDetail))]
    public class EfSubDetailPlainDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>注释</summary>
        public string? Note { get; set; }
    }

    // ================================================================
    // DTO — 降级路径（嵌套 [Convert] 触发 AsDto 二步映射）
    // ================================================================

    /// <summary>SKU 大写转换器（静态方法引用形态，T1 模板 M(TField)）— CLR 委托，ORM 无法翻译为 SQL</summary>
    /// <remarks>
    /// 跨表 [Convert] 的实参基准 = 子源（FromSource=EfProduct）
    /// B-4 后跨表分支传入 fieldValue=末段 Product.Sku 真实值（source=导航对象 EfProduct 不再入参，
    /// T1 单值形态仅取字段值）；旧接口形态（TSource=宿主 EfDetail + source.Product 兜底取值）为
    /// 前"无源属性路径"语义（source=当前映射源），与跨表消费新语义冲突（TSource 错配抛
    /// MapException，F3 回归）。
    /// </remarks>
    public static class EfSkuUpperConverter
    {
        /// <summary>
        /// fieldValue = 跨表末段 Product.Sku 值（B-4 框架取值），null → 空串
        /// </summary>
        public static string Convert(string? fieldValue)
        {
            return fieldValue?.ToUpperInvariant() ?? "";
        }
    }

    /// <summary>明细 DTO（含嵌套 [Convert]）— 触发整体降级</summary>
    [MapFrom(typeof(EfDetail))]
    public class EfDetailConvDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>备注</summary>
        public string? Remark { get; set; }

        /// <summary>产品 SKU — [Convert] 大写转换（触发降级）+ [MapProperty] 跨表取值</summary>
        [MapProperty(typeof(EfProduct), nameof(EfProduct.Sku))]
        [Convert(typeof(EfSkuUpperConverter), nameof(EfSkuUpperConverter.Convert))]
        public string? ProductSku { get; set; }
    }

    /// <summary>订单 DTO（嵌套含 [Convert] 的 DetailConvDto）</summary>
    [MapFrom(typeof(EfOrder))]
    public class EfOrderConvDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>订单编号</summary>
        public string? OrderNo { get; set; }
        /// <summary>嵌套明细 DTO（含 [Convert]）</summary>
        public EfDetailConvDto? Detail { get; set; }
    }
}

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// 嵌套跨表投影测试用 EF Core DbContext — SQLite 内存数据库
    /// <para>链路：EfOrder → EfDetail → EfProduct / EfSubDetail → EfExtra</para>
    /// </summary>
    public class NestedCrossTableDbContext : DbContext
    {
        /// <summary>订单表</summary>
        public DbSet<EfOrder> Orders { get; set; } = null!;
        /// <summary>明细表</summary>
        public DbSet<EfDetail> Details { get; set; } = null!;
        /// <summary>产品表</summary>
        public DbSet<EfProduct> Products { get; set; } = null!;
        /// <summary>子明细表</summary>
        public DbSet<EfSubDetail> SubDetails { get; set; } = null!;
        /// <summary>附加信息表</summary>
        public DbSet<EfExtra> Extras { get; set; } = null!;

        private readonly string _connectionString;

        /// <summary>构造函数 — 接收 SQLite 连接字符串</summary>
        public NestedCrossTableDbContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <inheritdoc/>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connectionString);

        /// <inheritdoc/>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfOrder>(b =>
            {
                b.ToTable("EfOrders");
                b.HasOne(o => o.Detail).WithMany().HasForeignKey(o => o.DetailId);
            });
            modelBuilder.Entity<EfDetail>(b =>
            {
                b.ToTable("EfDetails");
                b.HasOne(d => d.Product).WithMany().HasForeignKey(d => d.ProductId);
                b.HasOne(d => d.SubDetail).WithMany().HasForeignKey(d => d.SubDetailId);
            });
            modelBuilder.Entity<EfProduct>().ToTable("EfProducts");
            modelBuilder.Entity<EfSubDetail>(b =>
            {
                b.ToTable("EfSubDetails");
                b.HasOne(s => s.Extra).WithMany().HasForeignKey(s => s.ExtraId);
            });
            modelBuilder.Entity<EfExtra>().ToTable("EfExtras");
        }

        /// <summary>
        /// 植入测试数据：
        /// <para>Order 1 — 完整链路（Detail/Product/SubDetail/Extra 全有）</para>
        /// <para>Order 2 — 一级 null（DetailId 为 null）</para>
        /// <para>Order 3 — 末级 null（Detail 有，Product 为 null）</para>
        /// </summary>
        public void SeedData()
        {
            Database.EnsureCreated();

            Extras.AddRange(
                new EfExtra { Id = 1, Tag = "TAG-1" },
                new EfExtra { Id = 2, Tag = "TAG-2" });

            Products.AddRange(
                new EfProduct { Id = 1, Sku = "SKU-001", Price = 100 },
                new EfProduct { Id = 2, Sku = "SKU-002", Price = 250 });

            SubDetails.AddRange(
                new EfSubDetail { Id = 1, Note = "NOTE-1", ExtraId = 1 },
                new EfSubDetail { Id = 2, Note = "NOTE-2", ExtraId = null });

            Details.AddRange(
                new EfDetail { Id = 1, Remark = "REMARK-1", ProductId = 1, SubDetailId = 1 },
                new EfDetail { Id = 2, Remark = "REMARK-2", ProductId = null, SubDetailId = 2 },
                new EfDetail { Id = 3, Remark = "REMARK-3", ProductId = 2, SubDetailId = null });

            Orders.AddRange(
                new EfOrder { Id = 1, OrderNo = "ORD-001", DetailId = 1 },
                new EfOrder { Id = 2, OrderNo = "ORD-002", DetailId = null },
                new EfOrder { Id = 3, OrderNo = "ORD-003", DetailId = 2 });

            SaveChanges();
        }
    }

    // ================================================================
    // EF Core 集成测试（EF-01 ~ EF-14）
    // ================================================================

    /// <summary>
    /// EF Core + NCMapper 嵌套跨表 [MapProperty] 投影集成测试
    /// <para>使用 SQLite 内存数据库验证多级 LEFT JOIN 翻译正确性 + null 传播</para>
    /// </summary>
    [Collection("PipelineGlobalState")]
    public class NestedCrossTableProjectionTests : IDisposable
    {
        private readonly NestedCrossTableDbContext _db;

        /// <summary>初始化 SQLite 内存数据库并植入测试数据</summary>
        public NestedCrossTableProjectionTests()
        {
            var connStr = $"DataSource=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new NestedCrossTableDbContext(connStr);
            _db.SeedData();
        }

        /// <inheritdoc/>
        public void Dispose() => _db.Dispose();

        // ----------------------------------------------------------------
        // EF-01: 完整链路投影 — 所有跨表字段正确
        // ----------------------------------------------------------------

        /// <summary>EF-01: 完整链路 — Detail.Product.Sku/Price + Detail.SubDetail.Extra.Tag 全部映射</summary>
        [Fact]
        public void EF01_NestedCrossTable_FullChain_AllFields()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderDto>())
                .First();

            Assert.Equal(1, dto.Id);
            Assert.Equal("ORD-001", dto.OrderNo);
            Assert.NotNull(dto!.Detail);
            Assert.Equal(1, dto.Detail!.Id);
            Assert.Equal("REMARK-1", dto.Detail.Remark);
            Assert.Equal("SKU-001", dto.Detail.ProductSku);   // 跨表 Product.Sku
            Assert.Equal(100, dto.Detail.ProductPrice);        // 跨表 Product.Price
            Assert.NotNull(dto.Detail.SubDetail);
            Assert.Equal("NOTE-1", dto.Detail.SubDetail!.Note);
            Assert.Equal("TAG-1", dto.Detail.SubDetail.ExtraTag); // 第三层跨表 Extra.Tag
        }

        // ----------------------------------------------------------------
        // EF-02: AsDto().First 完整链路
        // ----------------------------------------------------------------

        /// <summary>EF-02: AsDto().First — 终端操作走投影路径，跨表字段正确</summary>
        [Fact]
        public void EF02_AsDto_First_FullChain()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .AsDto()
                .First<EfOrderDto>();

            Assert.NotNull(dto);
            Assert.Equal("ORD-001", dto!.OrderNo);
            Assert.Equal("SKU-001", dto.Detail!.ProductSku);
            Assert.Equal(100, dto.Detail.ProductPrice);
            Assert.Equal("TAG-1", dto.Detail.SubDetail!.ExtraTag);
        }

        // ----------------------------------------------------------------
        // EF-03: 一级 null（Detail 为 null）
        // ----------------------------------------------------------------

        /// <summary>EF-03: 一级 null — Detail 为 null 时嵌套 DTO 整体为 null</summary>
        [Fact]
        public void EF03_FirstLevelNull_Detail_IsNull()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 2)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderDto>())
                .First();

            Assert.Equal(2, dto.Id);
            Assert.Equal("ORD-002", dto.OrderNo);
            Assert.Null(dto.Detail); // DetailId 为 null → LEFT JOIN 无匹配 → null
        }

        // ----------------------------------------------------------------
        // EF-04: 末级 null（Product 为 null，值类型目标 → 默认 0）
        // ----------------------------------------------------------------

        /// <summary>EF-04: 末级 null — Product 为 null 时 string 字段为 null、int 字段为默认 0</summary>
        [Fact]
        public void EF04_LastLevelNull_Product_ValueTypeDefault()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 3)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderDto>())
                .First();

            Assert.Equal(3, dto.Id);
            Assert.NotNull(dto!.Detail);
            Assert.Equal("REMARK-2", dto.Detail!.Remark);
            Assert.Null(dto.Detail.ProductSku);   // 引用类型 → null
            Assert.Equal(0, dto.Detail.ProductPrice); // 值类型 int → 默认 0
        }

        // ----------------------------------------------------------------
        // EF-05: 混合列表 — 多条记录不同 null 场景
        // ----------------------------------------------------------------

        /// <summary>EF-05: 混合列表 — 完整/一级null/末级null 三条记录均正确映射</summary>
        [Fact]
        public void EF05_MixedList_AllScenarios()
        {
            var dtos = _db.Orders
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderDto>())
                .OrderBy(d => d.Id)
                .ToList();

            Assert.Equal(3, dtos.Count);

            // Order 1 — 完整链路
            Assert.Equal("SKU-001", dtos[0].Detail!.ProductSku);
            Assert.Equal(100, dtos[0].Detail!.ProductPrice);
            Assert.Equal("TAG-1", dtos[0].Detail!.SubDetail!.ExtraTag);

            // Order 2 — 一级 null
            Assert.Null(dtos[1].Detail);

            // Order 3 — 末级 null（Product 为 null）
            Assert.NotNull(dtos[2].Detail);
            Assert.Null(dtos[2].Detail!.ProductSku);
            Assert.Equal(0, dtos[2].Detail!.ProductPrice);
        }

        // ----------------------------------------------------------------
        // EF-06: 值类型 int 目标 — null 导航 → 默认 0
        // ----------------------------------------------------------------

        /// <summary>EF-06: 值类型 int 目标 — Product 为 null 时 ProductPrice 为 0（非异常）</summary>
        [Fact]
        public void EF06_ValueType_IntTarget_Default_Zero()
        {
            var dtos = _db.Orders
                .Where(o => o.Id == 3)
                .AsDto()
                .ToList<EfOrderDto>();

            Assert.Single(dtos);
            Assert.Equal(0, dtos[0].Detail!.ProductPrice);
        }

        // ----------------------------------------------------------------
        // EF-07: 值类型 int? 目标 — null 导航 → null
        // ----------------------------------------------------------------

        /// <summary>EF-07: 值类型 int? 目标 — Product 为 null 时 ProductPrice 为 null</summary>
        [Fact]
        public void EF07_ValueType_NullableIntTarget_Null()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 3)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderNullableDto>())
                .First();

            Assert.NotNull(dto!.Detail);
            Assert.Null(dto.Detail!.ProductPrice); // int? 目标 → null
        }

        /// <summary>EF-07b: 值类型 int? 目标 — Product 有值时正确映射</summary>
        [Fact]
        public void EF07b_ValueType_NullableIntTarget_HasValue()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderNullableDto>())
                .First();

            Assert.NotNull(dto!.Detail);
            Assert.Equal(100, dto.Detail!.ProductPrice);
        }

        // ----------------------------------------------------------------
        // EF-08: 多层嵌套 — SubDetail.Extra.Tag（第三层跨表）
        // ----------------------------------------------------------------

        /// <summary>EF-08: 多层嵌套 — 第三层跨表 Extra.Tag 经多级 LEFT JOIN 正确映射</summary>
        [Fact]
        public void EF08_MultiLevel_ThreeLevels()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .AsDto()
                .First<EfOrderDto>();

            Assert.NotNull(dto!.Detail);
            Assert.NotNull(dto.Detail!.SubDetail);
            Assert.Equal(1, dto.Detail.SubDetail!.Id);
            Assert.Equal("NOTE-1", dto.Detail.SubDetail.Note);
            Assert.Equal("TAG-1", dto.Detail.SubDetail.ExtraTag);
        }

        // ----------------------------------------------------------------
        // EF-09: 多层嵌套末级 null — Extra 为 null
        // ----------------------------------------------------------------

        /// <summary>EF-09: 多层嵌套末级 null — SubDetail 有但 Extra 为 null 时 ExtraTag 为 null</summary>
        [Fact]
        public void EF09_MultiLevel_LastLevelNull()
        {
            // Order 3 → Detail 2 → SubDetail 2（ExtraId 为 null）
            var dto = _db.Orders
                .Where(o => o.Id == 3)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderDto>())
                .First();

            // Detail 2 的 SubDetailId = 2，SubDetail 2 的 ExtraId = null
            Assert.NotNull(dto!.Detail);
            Assert.NotNull(dto.Detail!.SubDetail);
            Assert.Equal("NOTE-2", dto.Detail.SubDetail!.Note);
            Assert.Null(dto.Detail.SubDetail.ExtraTag); // Extra 为 null → null
        }

        // ----------------------------------------------------------------
        // EF-10: 中间层 null — SubDetail 为 null
        // ----------------------------------------------------------------

        /// <summary>EF-10: 中间层 null — Detail 有但 SubDetail 为 null 时嵌套 SubDetailDto 为 null</summary>
        [Fact]
        public void EF10_MiddleLevelNull_SubDetail_IsNull()
        {
            // 构造一条 Detail 有、SubDetail 为 null 的数据（Detail 3：ProductId=2, SubDetailId=null）
            _db.Orders.Add(new EfOrder { Id = 4, OrderNo = "ORD-004", DetailId = 3 });
            _db.SaveChanges();

            var dto = _db.Orders
                .Where(o => o.Id == 4)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderDto>())
                .First();

            Assert.NotNull(dto!.Detail);
            Assert.Equal("REMARK-3", dto.Detail!.Remark);
            Assert.Equal("SKU-002", dto.Detail.ProductSku); // Product 有值
            Assert.Null(dto.Detail.SubDetail);              // SubDetail 为 null
        }

        // ----------------------------------------------------------------
        // EF-11: 投影与运行时 Mapper 一致性
        // ----------------------------------------------------------------

        /// <summary>EF-11: 投影路径与运行时 Mapper.Map 结果一致（完整链路）</summary>
        [Fact]
        public void EF11_Projection_Matches_Runtime_Mapper()
        {
            // 查出实体（含完整导航链）
            var entity = _db.Orders
                .Include(o => o.Detail!).ThenInclude(d => d.Product)
                .Include(o => o.Detail!).ThenInclude(d => d.SubDetail!).ThenInclude(s => s.Extra)
                .First(o => o.Id == 1);

            // 投影方式
            var projDto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderDto>())
                .First();

            // 运行时 Mapper 方式
            var mapDto = Mapper.Map<EfOrder, EfOrderDto>(entity)!;

            Assert.Equal(mapDto.Id, projDto.Id);
            Assert.Equal(mapDto.OrderNo, projDto.OrderNo);
            Assert.NotNull(mapDto.Detail);
            Assert.NotNull(projDto.Detail);
            Assert.Equal(mapDto.Detail!.Remark, projDto.Detail!.Remark);
            Assert.Equal(mapDto.Detail.ProductSku, projDto.Detail.ProductSku);
            Assert.Equal(mapDto.Detail.ProductPrice, projDto.Detail.ProductPrice);
            Assert.Equal(mapDto.Detail.SubDetail!.Note, projDto.Detail.SubDetail!.Note);
            Assert.Equal(mapDto.Detail.SubDetail.ExtraTag, projDto.Detail.SubDetail.ExtraTag);
        }

        // ----------------------------------------------------------------
        // EF-12: 回归 — 纯嵌套投影（无 [MapProperty]）
        // ----------------------------------------------------------------

        /// <summary>EF-12: 回归 — 无跨表 [MapProperty] 的纯嵌套投影仍正常工作</summary>
        [Fact]
        public void EF12_Regression_PlainNestedProjection_NoMapProperty()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<EfOrder, EfOrderPlainDto>())
                .First();

            Assert.Equal(1, dto.Id);
            Assert.Equal("ORD-001", dto.OrderNo);
            Assert.NotNull(dto!.Detail);
            Assert.Equal("REMARK-1", dto.Detail!.Remark);
            Assert.NotNull(dto.Detail.SubDetail);
            Assert.Equal("NOTE-1", dto.Detail.SubDetail!.Note);
        }

        // ----------------------------------------------------------------
        // EF-13: 降级路径 — 嵌套 [Convert] 触发 AsDto 二步映射
        // ----------------------------------------------------------------

        /// <summary>EF-13: 嵌套 [Convert] — AsDto 检测后降级为 MapList，转换在内存生效</summary>
        [Fact]
        public void EF13_Downgrade_NestedConvertUsing()
        {
            try
            {
                // 检测：嵌套 [Convert] 触发整体降级
                var needs = EfCoreDegradationDetector.NeedsDegradation(
                    typeof(EfOrder), typeof(EfOrderConvDto), out var reason);
                Assert.True(needs);
                Assert.Contains("ConvertAttribute", reason);

                // 植入小写 SKU 种子数据 — 只有转换器 ToUpperInvariant() 生效才能断言通过
                _db.Products.Add(new EfProduct { Id = 90, Sku = "sku-001", Price = 100 });
                _db.Details.Add(new EfDetail { Id = 90, Remark = "CONV-TEST", ProductId = 90, SubDetailId = null });
                _db.Orders.Add(new EfOrder { Id = 90, OrderNo = "ORD-CONV", DetailId = 90 });
                _db.SaveChanges();

                // E2E：AsDto 自动降级 → MapList → ConvertUsing 大写转换生效
                var dto = _db.Orders
                    .Where(o => o.Id == 90)
                    .AsDto()
                    .First<EfOrderConvDto>();

                Assert.NotNull(dto);
                Assert.NotNull(dto!.Detail);
                Assert.Equal("SKU-001", dto.Detail!.ProductSku); // 经 ConvertUsing ToUpperInvariant（原值小写 sku-001）
            }
            finally
            {
                Mapper.Pipeline.Clear();

            }
        }
    }

    // ================================================================
    // FreeSql 集成测试（FS-01 ~ FS-14）
    // ================================================================

    /// <summary>
    /// FreeSql + NCMapper 嵌套跨表 [MapProperty] 投影集成测试
    /// <para>使用 SQLite 临时文件数据库验证多级 LEFT JOIN 翻译正确性 + null 传播</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class NestedCrossTableFreeSqlProjectionTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        /// <summary>初始化 FreeSql SQLite 临时文件数据库并植入测试数据</summary>
        public NestedCrossTableFreeSqlProjectionTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_nested_{Guid.NewGuid():N}.db");

            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            SeedData();
        }

        /// <summary>释放 FreeSql 连接并删除临时数据库文件</summary>
        public void Dispose()
        {
            _fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        private void SeedData()
        {
            _fsql.Insert(new List<EfExtra>
            {
                new() { Id = 1, Tag = "TAG-1" },
                new() { Id = 2, Tag = "TAG-2" }
            }).ExecuteAffrows();

            _fsql.Insert(new List<EfProduct>
            {
                new() { Id = 1, Sku = "SKU-001", Price = 100 },
                new() { Id = 2, Sku = "SKU-002", Price = 250 }
            }).ExecuteAffrows();

            _fsql.Insert(new List<EfSubDetail>
            {
                new() { Id = 1, Note = "NOTE-1", ExtraId = 1 },
                new() { Id = 2, Note = "NOTE-2", ExtraId = null }
            }).ExecuteAffrows();

            _fsql.Insert(new List<EfDetail>
            {
                new() { Id = 1, Remark = "REMARK-1", ProductId = 1, SubDetailId = 1 },
                new() { Id = 2, Remark = "REMARK-2", ProductId = null, SubDetailId = 2 },
                new() { Id = 3, Remark = "REMARK-3", ProductId = 2, SubDetailId = null }
            }).ExecuteAffrows();

            _fsql.Insert(new List<EfOrder>
            {
                new() { Id = 1, OrderNo = "ORD-001", DetailId = 1 },
                new() { Id = 2, OrderNo = "ORD-002", DetailId = null },
                new() { Id = 3, OrderNo = "ORD-003", DetailId = 2 },
                new() { Id = 4, OrderNo = "ORD-004", DetailId = 3 }
            }).ExecuteAffrows();
        }

        // ----------------------------------------------------------------
        // FS-01 ~ FS-14: FreeSql Include + MapList integration tests
        // FreeSql supports nested MemberInit and multi-level nav auto-JOIN,
        // but cannot translate Conditional wrapping an entire MemberInit
        // (NCMapper BuildNestedProjection generates src.Nav == null ? null :
        // new SubDto{...} for null guards; FreeSql ExpressionLambdaToSql has
        // no MemberInit case, producing invalid SQL). Use Include + MapList.
        // ----------------------------------------------------------------

        /// <summary>FS-01: Full chain - Include loads nav graph + MapList resolves cross-table</summary>
        [Fact]
        public void FS01_NestedCrossTable_FullChain_AllFields()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.Product)
                .Include(o => o.Detail!.SubDetail).Include(o => o.Detail!.SubDetail!.Extra)
                .Where(o => o.Id == 1).ToList();
            var dto = Mapper.MapList<EfOrder, EfOrderDto>(entities)![0];
            Assert.Equal(1, dto.Id);
            Assert.Equal("ORD-001", dto.OrderNo);
            Assert.NotNull(dto!.Detail);
            Assert.Equal("REMARK-1", dto.Detail!.Remark);
            Assert.Equal("SKU-001", dto.Detail.ProductSku);
            Assert.Equal(100, dto.Detail.ProductPrice);
            Assert.NotNull(dto.Detail.SubDetail);
            Assert.Equal("NOTE-1", dto.Detail.SubDetail!.Note);
            Assert.Equal("TAG-1", dto.Detail.SubDetail.ExtraTag);
        }

        /// <summary>FS-02: Mapper.Map single entity - cross-table fields correct</summary>
        [Fact]
        public void FS02_Map_Single_FullChain()
        {
            var entity = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.Product)
                .Include(o => o.Detail!.SubDetail).Include(o => o.Detail!.SubDetail!.Extra)
                .Where(o => o.Id == 1).First();
            var dto = Mapper.Map<EfOrder, EfOrderDto>(entity)!;
            Assert.Equal("ORD-001", dto.OrderNo);
            Assert.Equal("SKU-001", dto.Detail!.ProductSku);
            Assert.Equal(100, dto.Detail.ProductPrice);
            Assert.Equal("TAG-1", dto.Detail.SubDetail!.ExtraTag);
        }

        /// <summary>FS-03: First level null - DetailId null means Detail is null</summary>
        [Fact]
        public void FS03_FirstLevelNull_Detail_IsNull()
        {
            var entities = _fsql.Select<EfOrder>().Include(o => o.Detail).Where(o => o.Id == 2).ToList();
            var dto = Mapper.MapList<EfOrder, EfOrderDto>(entities)![0];
            Assert.Equal(2, dto.Id);
            Assert.Null(dto.Detail);
        }

        /// <summary>FS-04: Last level null - Product null means string=null, int=0</summary>
        [Fact]
        public void FS04_LastLevelNull_Product_ValueTypeDefault()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.Product)
                .Where(o => o.Id == 3).ToList();
            var dto = Mapper.MapList<EfOrder, EfOrderDto>(entities)![0];
            Assert.NotNull(dto!.Detail);
            Assert.Null(dto.Detail!.ProductSku);
            Assert.Equal(0, dto.Detail.ProductPrice);
        }

        /// <summary>FS-05: Mixed list - full/first-null/last-null all correct</summary>
        [Fact]
        public void FS05_MixedList_AllScenarios()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.Product)
                .Include(o => o.Detail!.SubDetail).Include(o => o.Detail!.SubDetail!.Extra)
                .Where(o => o.Id <= 3).OrderBy(o => o.Id).ToList();
            var dtos = Mapper.MapList<EfOrder, EfOrderDto>(entities)!;
            Assert.Equal(3, dtos.Count);
            Assert.Equal("SKU-001", dtos[0].Detail!.ProductSku);
            Assert.Equal("TAG-1", dtos[0].Detail!.SubDetail!.ExtraTag);
            Assert.Null(dtos[1].Detail);
            Assert.Null(dtos[2].Detail!.ProductSku);
            Assert.Equal(0, dtos[2].Detail!.ProductPrice);
        }

        /// <summary>FS-06: Value type int target - Product null gives ProductPrice=0</summary>
        [Fact]
        public void FS06_ValueType_IntTarget_Default_Zero()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.Product)
                .Where(o => o.Id == 3).ToList();
            Assert.Equal(0, Mapper.MapList<EfOrder, EfOrderDto>(entities)![0].Detail!.ProductPrice);
        }

        /// <summary>FS-07: Nullable int? target - Product null gives ProductPrice=null</summary>
        [Fact]
        public void FS07_ValueType_NullableIntTarget_Null()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.Product)
                .Where(o => o.Id == 3).ToList();
            Assert.Null(Mapper.MapList<EfOrder, EfOrderNullableDto>(entities)![0].Detail!.ProductPrice);
        }

        /// <summary>FS-07b: Nullable int? target - Product has value gives that value</summary>
        [Fact]
        public void FS07b_ValueType_NullableIntTarget_HasValue()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.Product)
                .Where(o => o.Id == 1).ToList();
            Assert.Equal(100, Mapper.MapList<EfOrder, EfOrderNullableDto>(entities)![0].Detail!.ProductPrice);
        }

        /// <summary>FS-08: Three-level nesting - SubDetail.Extra.Tag mapped correctly</summary>
        [Fact]
        public void FS08_MultiLevel_ThreeLevels()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.SubDetail)
                .Include(o => o.Detail!.SubDetail!.Extra)
                .Where(o => o.Id == 1).ToList();
            var dto = Mapper.MapList<EfOrder, EfOrderDto>(entities)![0];
            Assert.NotNull(dto.Detail!.SubDetail);
            Assert.Equal("NOTE-1", dto.Detail.SubDetail!.Note);
            Assert.Equal("TAG-1", dto.Detail.SubDetail.ExtraTag);
        }

        /// <summary>FS-09: Last level null - Extra null gives ExtraTag=null</summary>
        [Fact]
        public void FS09_MultiLevel_LastLevelNull()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.SubDetail)
                .Include(o => o.Detail!.SubDetail!.Extra)
                .Where(o => o.Id == 3).ToList();
            var dto = Mapper.MapList<EfOrder, EfOrderDto>(entities)![0];
            Assert.NotNull(dto.Detail!.SubDetail);
            Assert.Null(dto.Detail.SubDetail!.ExtraTag);
        }

        /// <summary>FS-10: Middle level null - SubDetail null means whole SubDetail is null</summary>
        [Fact]
        public void FS10_MiddleLevelNull_SubDetail_IsNull()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.SubDetail)
                .Where(o => o.Id == 4).ToList();
            var dto = Mapper.MapList<EfOrder, EfOrderDto>(entities)![0];
            Assert.NotNull(dto!.Detail);
            Assert.Null(dto.Detail!.SubDetail);
        }

        /// <summary>FS-11: MapList matches Map single result</summary>
        [Fact]
        public void FS11_MapList_Matches_Map()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.Product)
                .Include(o => o.Detail!.SubDetail).Include(o => o.Detail!.SubDetail!.Extra)
                .Where(o => o.Id == 1).ToList();
            var listDto = Mapper.MapList<EfOrder, EfOrderDto>(entities)![0];
            var mapDto = Mapper.Map<EfOrder, EfOrderDto>(entities[0])!;
            Assert.Equal(listDto.Detail!.ProductSku, mapDto.Detail!.ProductSku);
            Assert.Equal(listDto.Detail.ProductPrice, mapDto.Detail.ProductPrice);
            Assert.Equal(listDto.Detail.SubDetail!.ExtraTag, mapDto.Detail.SubDetail!.ExtraTag);
        }

        /// <summary>FS-12: Regression - plain nested DTO without cross-table maps normally</summary>
        [Fact]
        public void FS12_Regression_PlainNestedProjection_NoMapProperty()
        {
            var entities = _fsql.Select<EfOrder>()
                .Include(o => o.Detail).Include(o => o.Detail!.SubDetail)
                .Where(o => o.Id == 1).ToList();
            var dto = Mapper.MapList<EfOrder, EfOrderPlainDto>(entities)![0];
            Assert.NotNull(dto!.Detail);
            Assert.Equal("REMARK-1", dto.Detail!.Remark);
            Assert.NotNull(dto.Detail.SubDetail);
            Assert.Equal("NOTE-1", dto.Detail.SubDetail!.Note);
        }

        /// <summary>FS-13: Nested [Convert] - MapList path converter works</summary>
        [Fact]
        public void FS13_Downgrade_NestedConvertUsing()
        {
            try
            {
                var needs = EfCoreDegradationDetector.NeedsDegradation(
                    typeof(EfOrder), typeof(EfOrderConvDto), out var reason);
                Assert.True(needs);
                Assert.Contains("ConvertAttribute", reason);

                // 植入小写 SKU 种子数据 — 只有转换器 ToUpperInvariant() 生效才能断言通过
                _fsql.Insert(new EfProduct { Id = 90, Sku = "sku-001", Price = 100 }).ExecuteAffrows();
                _fsql.Insert(new EfDetail { Id = 90, Remark = "CONV-TEST", ProductId = 90, SubDetailId = null }).ExecuteAffrows();
                _fsql.Insert(new EfOrder { Id = 90, OrderNo = "ORD-CONV", DetailId = 90 }).ExecuteAffrows();

                var entities = _fsql.Select<EfOrder>()
                    .Include(o => o.Detail).Include(o => o.Detail!.Product)
                    .Where(o => o.Id == 90).ToList();
                var dto = Mapper.MapList<EfOrder, EfOrderConvDto>(entities)![0];
                Assert.NotNull(dto!.Detail);
                Assert.Equal("SKU-001", dto.Detail!.ProductSku); // 经 ConvertUsing ToUpperInvariant（原值小写 sku-001）
            }
            finally { Mapper.Pipeline.Clear();  }
        }

    }

    /// <summary>
    /// 收集 Trace 输出的监听器 — 用于验证投影深度警告
    /// </summary>
    internal sealed class CollectingTraceListener : System.Diagnostics.TraceListener
    {
        /// <summary>收集到的消息列表</summary>
        public List<string> Messages { get; } = new();

        /// <inheritdoc/>
        public override void Write(string? message) { }

        /// <inheritdoc/>
        public override void WriteLine(string? message)
        {
            if (message != null) Messages.Add(message);
        }
    }
}
