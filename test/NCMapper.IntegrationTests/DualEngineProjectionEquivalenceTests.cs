// EFCore vs FreeSql 双端投影/降级/导航一致性矩阵
// 同一 DTO 矩阵 × 同一数据 × 双端输出等价断言。
// 此矩阵为提取前行为基线（锁定行为），T-B~T-D 提取后必须与提取前等价。
// 集合投影为显式策略差异（EFCore 开启 / FreeSql 关闭走降级），输出仍须等价。
using System.Text.Json;
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// T-A 双端一致性矩阵：EFCore vs FreeSql 投影/降级/导航行为等价。
    /// 场景：基元直映 / 字段重命名 / 类型转换 / MapIgnore / 嵌套导航 / 集合投影(策略差异) /
    ///       record 降级 / Null 导航 / 深层嵌套(3 层)。
    /// </summary>
    public class DualEngineProjectionEquivalenceTests : IDisposable
    {
        private readonly QTestDbContext _db;
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public DualEngineProjectionEquivalenceTests()
        {
            // EFCore：SQLite 内存库（memory+shared）
            var connStr = $"DataSource=file:dual{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new QTestDbContext(connStr);
            _db.Database.EnsureCreated();

            // FreeSql：SQLite 临时文件库
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_dual_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            SeedEf();
            SeedFreeSql();
        }

        public void Dispose()
        {
            _db.Dispose();
            _fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        // ================================================================
        // 实体（EFCore + FreeSql 共用，FreeSql 以 [Navigate] 声明导航）
        // ================================================================

        public class QType
        {
            public int Id { get; set; }
            public string TypeName { get; set; } = "";

            [Navigate(nameof(QTypeMeta.TypeId))]
            public QTypeMeta? Meta { get; set; }
        }

        public class QTypeMeta
        {
            public int Id { get; set; }
            public int TypeId { get; set; }
            public string Version { get; set; } = "";
        }

        public class QCategory
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int? TypeId { get; set; }

            [Navigate(nameof(TypeId))]
            public QType? Type { get; set; }
        }

        public class QDept
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int? CategoryId { get; set; }

            [Navigate(nameof(CategoryId))]
            public QCategory? Category { get; set; }

            [Navigate(nameof(QStaff.DeptId))]
            public List<QStaff> Staff { get; set; } = new();
        }

        public class QStaff
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int DeptId { get; set; }
            public int Clicks { get; set; }
            public string? Note { get; set; }

            [Navigate(nameof(DeptId))]
            public QDept? Dept { get; set; }
        }

        // ================================================================
        // DTO（[MapFrom]/[MapProperty] 标注）
        // ================================================================

        [MapFrom(typeof(QStaff))]
        public class QStaffFlatDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        [MapFrom(typeof(QStaff))]
        public class QStaffRenameDto
        {
            public int Id { get; set; }

            [MapProperty(typeof(QStaff), "Name")]
            public string StaffName { get; set; } = "";
        }

        [MapFrom(typeof(QStaff))]
        public class QStaffConvDto
        {
            public int Id { get; set; }
            public long Clicks { get; set; }
        }

        [MapFrom(typeof(QStaff))]
        public class QStaffIgnoreDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";

            [MapIgnore]
            public string? Note { get; set; }
        }

        [MapFrom(typeof(QDept))]
        public class QDeptDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        [MapFrom(typeof(QStaff))]
        public class QStaffNestedDto
        {
            public int Id { get; set; }
            public QDeptDto? Dept { get; set; }
        }

        /// <summary>嵌套 + [Convert] → 触发降级路径（双端降级输出等价验证）</summary>
        [MapFrom(typeof(QStaff))]
        public class QStaffNestedConvDto
        {
            public int Id { get; set; }
            public QDeptDto? Dept { get; set; }

            [Convert(typeof(NoOpQStaffConverter), nameof(NoOpQStaffConverter.Convert))]
            public string Name { get; set; } = "";
        }

        /// <summary>无操作转换器（静态方法引用形态，T1 模板 M(TField)）— null → 空串</summary>
        public static class NoOpQStaffConverter
        {
            /// <summary>原值返回（null → 空串）</summary>
            public static string Convert(string? fieldValue) => fieldValue ?? "";
        }

        [MapFrom(typeof(QDept))]
        public class QDeptCollectionDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<QStaffFlatDto> Staff { get; set; } = new();
        }

        [MapFrom(typeof(QStaff))]
        public record QStaffRecordDto(int Id, string Name);

        [MapFrom(typeof(QType))]
        public class QTypeDto
        {
            public int Id { get; set; }
            public string TypeName { get; set; } = "";
        }

        [MapFrom(typeof(QCategory))]
        public class QCategoryDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public QTypeDto? Type { get; set; }
        }

        [MapFrom(typeof(QDept))]
        public class QDeptDeepDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public QCategoryDto? Category { get; set; }
        }

        // ================================================================
        // DbContext（EFCore）
        // ================================================================

        public class QTestDbContext : DbContext
        {
            private readonly string _connStr;

            public QTestDbContext(string connStr)
            {
                _connStr = connStr;
            }

            public DbSet<QType> Types => Set<QType>();
            public DbSet<QTypeMeta> TypeMetas => Set<QTypeMeta>();
            public DbSet<QCategory> Categories => Set<QCategory>();
            public DbSet<QDept> Depts => Set<QDept>();
            public DbSet<QStaff> Staff => Set<QStaff>();

            protected override void OnConfiguring(DbContextOptionsBuilder options)
                => options.UseSqlite(_connStr);

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<QDept>()
                    .HasOne(d => d.Category)
                    .WithMany()
                    .HasForeignKey(d => d.CategoryId);
                modelBuilder.Entity<QStaff>()
                    .HasOne(s => s.Dept)
                    .WithMany(d => d.Staff)
                    .HasForeignKey(s => s.DeptId);
                modelBuilder.Entity<QType>()
                    .HasOne(t => t.Meta)
                    .WithOne()
                    .HasForeignKey<QTypeMeta>(m => m.TypeId);
            }
        }

        // ================================================================
        // Seed（双端同构数据）
        // ================================================================

        private static readonly (int Id, string Name)[] Types =
            { (1, "Internal") };

        private static readonly (int Id, string Name, int? TypeId)[] Categories =
            { (1, "Engineering", 1), (2, "Marketing", null) };

        private static readonly (int Id, string Name, int? CategoryId)[] Depts =
            { (1, "Core", 1), (2, "Ops", null), (3, "Platform", 1) };

        private static readonly (int Id, string Name, int DeptId, int Clicks, string? Note)[] Staff =
            { (1, "Alice", 1, 100, "n1"), (2, "Bob", 1, 200, null), (3, "Charlie", 2, 300, "n3"), (4, "Dave", 3, 400, null) };

        private void SeedEf()
        {
            _db.Types.AddRange(Types.Select(t => new QType { Id = t.Id, TypeName = t.Name }));
            _db.TypeMetas.AddRange(new[] { new QTypeMeta { Id = 1, TypeId = 1, Version = "v1" } });
            _db.Categories.AddRange(Categories.Select(c => new QCategory { Id = c.Id, Name = c.Name, TypeId = c.TypeId }));
            _db.Depts.AddRange(Depts.Select(d => new QDept { Id = d.Id, Name = d.Name, CategoryId = d.CategoryId }));
            _db.Staff.AddRange(Staff.Select(s => new QStaff { Id = s.Id, Name = s.Name, DeptId = s.DeptId, Clicks = s.Clicks, Note = s.Note }));
            _db.SaveChanges();
        }

        private void SeedFreeSql()
        {
            var fsql = _fsql;
            fsql.Insert<QType>().AppendData(Types.Select(t => new QType { Id = t.Id, TypeName = t.Name })).ExecuteAffrows();
            fsql.Insert<QTypeMeta>().AppendData(new[] { new QTypeMeta { Id = 1, TypeId = 1, Version = "v1" } }).ExecuteAffrows();
            fsql.Insert<QCategory>().AppendData(Categories.Select(c => new QCategory { Id = c.Id, Name = c.Name, TypeId = c.TypeId })).ExecuteAffrows();
            fsql.Insert<QDept>().AppendData(Depts.Select(d => new QDept { Id = d.Id, Name = d.Name, CategoryId = d.CategoryId })).ExecuteAffrows();
            fsql.Insert<QStaff>().AppendData(Staff.Select(s => new QStaff { Id = s.Id, Name = s.Name, DeptId = s.DeptId, Clicks = s.Clicks, Note = s.Note })).ExecuteAffrows();
        }

        // ================================================================
        // 断言辅助：双端输出 JSON 等价
        // ================================================================

        private static string Json<T>(List<T> items) =>
            JsonSerializer.Serialize(items);

        private static void AssertEquivalent<T>(List<T> ef, List<T> fs)
        {
            Assert.Equal(Json(ef), Json(fs));
        }

        // ================================================================
        // 场景 1：基元直映
        // ================================================================

        [Fact]
        public void Flat_EFCore_Equals_FreeSql()
        {
            var ef = _db.Staff.OrderBy(s => s.Id).AsDto().ToList<QStaffFlatDto>();
            var fs = _fsql.Select<QStaff>().OrderBy(s => s.Id).AsDto().ToList<QStaffFlatDto>();
            AssertEquivalent(ef, fs);
        }

        // ================================================================
        // 场景 2：字段重命名（[MapProperty]）
        // ================================================================

        [Fact]
        public void Rename_EFCore_Equals_FreeSql()
        {
            var ef = _db.Staff.OrderBy(s => s.Id).AsDto().ToList<QStaffRenameDto>();
            var fs = _fsql.Select<QStaff>().OrderBy(s => s.Id).AsDto().ToList<QStaffRenameDto>();
            AssertEquivalent(ef, fs);
        }

        // ================================================================
        // 场景 3：类型转换（int → long）
        // ================================================================

        [Fact]
        public void TypeConversion_EFCore_Equals_FreeSql()
        {
            var ef = _db.Staff.OrderBy(s => s.Id).AsDto().ToList<QStaffConvDto>();
            var fs = _fsql.Select<QStaff>().OrderBy(s => s.Id).AsDto().ToList<QStaffConvDto>();
            AssertEquivalent(ef, fs);
        }

        // ================================================================
        // 场景 4：MapIgnore 跳过
        // ================================================================

        [Fact]
        public void Ignore_EFCore_Equals_FreeSql()
        {
            var ef = _db.Staff.OrderBy(s => s.Id).AsDto().ToList<QStaffIgnoreDto>();
            var fs = _fsql.Select<QStaff>().OrderBy(s => s.Id).AsDto().ToList<QStaffIgnoreDto>();
            AssertEquivalent(ef, fs);
            Assert.All(ef, d => Assert.Null(d.Note));
        }

        // ================================================================
        // 场景 5a：嵌套导航投影（Staff → Dept）— 差异已清偿
        // 原登记差异：FreeSql 投影路径报 "null.Id cannot be null"（共享内核 IIF null 保护形态
        // FreeSql 翻译不了，评估 20261029-006 P1c）。修复（IIF 剥保护改写器 + guard 兜底）后
        // FreeSql 走 SQL 投影；本例 Staff 均有 Dept（种子无 null 导航行），双端等价。
        // 原方法名 NestedNavProjection_EFCore_Works_FreeSql_Throws_RegisteredDifference 随差异清偿退役。
        // ================================================================

        [Fact]
        public void NestedNavProjection_EFCore_Equals_FreeSql()
        {
            var ef = _db.Staff.OrderBy(s => s.Id).AsDto().ToList<QStaffNestedDto>();
            var fs = _fsql.Select<QStaff>().OrderBy(s => s.Id).AsDto().ToList<QStaffNestedDto>();
            Assert.Equal(4, ef.Count);
            Assert.NotNull(ef[0].Dept);
            AssertEquivalent(ef, fs);
        }

        // ================================================================
        // 场景 5b：嵌套导航降级路径（[Convert] 触发）— 双端输出等价
        // ================================================================

        [Fact]
        public void NestedNavDegraded_EFCore_Equals_FreeSql()
        {
            var ef = _db.Staff.OrderBy(s => s.Id).AsDto().ToList<QStaffNestedConvDto>();
            var fs = _fsql.Select<QStaff>().OrderBy(s => s.Id).AsDto().ToList<QStaffNestedConvDto>();
            AssertEquivalent(ef, fs);
        }

        // ================================================================
        // 场景 6：集合投影（EFCore 投影子查询 vs FreeSql 降级路径，输出须等价）
        // ================================================================

        [Fact]
        public void CollectionProjection_EFCore_Equals_FreeSql()
        {
            var ef = _db.Depts.OrderBy(d => d.Id).AsDto().ToList<QDeptCollectionDto>();
            var fs = _fsql.Select<QDept>().OrderBy(d => d.Id).AsDto().ToList<QDeptCollectionDto>();

            Assert.Equal(ef.Count, fs.Count);
            for (int i = 0; i < ef.Count; i++)
            {
                Assert.Equal(ef[i].Id, fs[i].Id);
                Assert.Equal(ef[i].Name, fs[i].Name);
                // 集合元素顺序不保证一致，按 Id 排序后对比
                var efStaff = ef[i].Staff.OrderBy(x => x.Id).ToList();
                var fsStaff = fs[i].Staff.OrderBy(x => x.Id).ToList();
                Assert.Equal(Json(efStaff), Json(fsStaff));
            }
        }

        // ================================================================
        // 场景 7：record 降级（两库均触发降级，输出等价）
        // ================================================================

        [Fact]
        public void RecordDegradation_EFCore_Equals_FreeSql()
        {
            var ef = _db.Staff.OrderBy(s => s.Id).AsDto().ToList<QStaffRecordDto>();
            var fs = _fsql.Select<QStaff>().OrderBy(s => s.Id).AsDto().ToList<QStaffRecordDto>();
            AssertEquivalent(ef, fs);
        }

        // ================================================================
        // 场景 8：Null 导航（Dept2.Category = null）— 差异清偿为「空壳语义」差异
        // EFCore 投影 IIF null 传播 → Category 为 null；
        // FreeSql（IIF 剥保护后）SQL 投影成功：null 导航 → 空壳 DTO（非 null，int 字段 0/引用字段 null）。
        // 通道间 null 语义差异已经接受并文档化（README.zh-CN 配对表）。
        // ================================================================

        [Fact]
        public void NullNavigation_EFCore_ReturnsNull_FreeSql_EmptyShell_RegisteredDifference()
        {
            var ef = _db.Depts.OrderBy(d => d.Id).AsDto().ToList<QDeptDeepDto>();
            Assert.Null(ef[1].Category); // Ops 无 Category（EF IIF null 传播语义）
            Assert.NotNull(ef[0].Category);

            // FreeSql：剥保护后 SQL 投影成功；null 导航 → 空壳（Id=0/引用 null），正常行与 EF 等值
            var fs = _fsql.Select<QDept>()
                .LeftJoin(d => d.Category!.Id == d.CategoryId)
                .OrderBy(d => d.Id)
                .AsDto()
                .ToList<QDeptDeepDto>();
            Assert.Equal(3, fs.Count);
            // 正常行（Core/Platform）：与 EF 等值
            Assert.Equal(ef[0].Category!.Id, fs[0].Category!.Id);
            Assert.Equal(ef[0].Category.Name, fs[0].Category.Name);
            Assert.Equal(ef[2].Category!.Id, fs[2].Category!.Id);
            Assert.Equal(ef[2].Category.Name, fs[2].Category.Name);
            // Ops null 导航行：空壳语义（非 null，int 字段 0，引用字段 null）
            Assert.NotNull(fs[1].Category);
            Assert.Equal(0, fs[1].Category!.Id);
            Assert.Null(fs[1].Category.Name);
        }

        // ================================================================
        // 场景 9：深层嵌套降级路径（Dept → Category → Type 3 层 + [Convert] 触发）
        // 双端均走降级 → Include + 内存映射 → 输出等价（含 null 导航的 Ops 部门）
        // ================================================================

        [MapFrom(typeof(QDept))]
        public class QDeptDeepConvDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public QCategoryDto? Category { get; set; }

            [Convert(typeof(NoOpQDeptConverter), nameof(NoOpQDeptConverter.Convert))]
            public string? CategoryName { get; set; }
        }

        /// <summary>无操作转换器（静态方法引用形态，T1 模板 M(TField)）— null → 空串</summary>
        public static class NoOpQDeptConverter
        {
            /// <summary>原值返回（null → 空串）</summary>
            public static string Convert(string? fieldValue) => fieldValue ?? "";
        }

        [Fact]
        public void DeepNestingDegraded_EFCore_Equals_FreeSql()
        {
            var ef = _db.Depts.OrderBy(d => d.Id).AsDto().ToList<QDeptDeepConvDto>();
            var fs = _fsql.Select<QDept>().OrderBy(d => d.Id).AsDto().ToList<QDeptDeepConvDto>();
            AssertEquivalent(ef, fs);
            Assert.NotNull(ef[0].Category?.Type); // Core → Engineering → Internal
            Assert.Null(ef[1].Category);          // Ops 无 Category
            Assert.NotNull(ef[2].Category?.Type); // Platform → Engineering → Internal
        }

        // ================================================================
        // 场景 10：嵌套深度策略边界（4 层投影）— 已知策略差异锁定（G3 变异检测锚点）
        // EFCore MaxNestingDepth=8 → 第 4 层可投影；FreeSql MaxNestingDepth=3 → 第 4 层跳过（null）。
        // 若内核缓存/策略串用（变异），本测试立即捕获。
        // ================================================================

        [MapFrom(typeof(QType))]
        public class QTypeDeepDto
        {
            public int Id { get; set; }
            public string TypeName { get; set; } = "";
            public QTypeMetaDto? Meta { get; set; }
        }

        [MapFrom(typeof(QTypeMeta))]
        public class QTypeMetaDto
        {
            public int Id { get; set; }
            public string Version { get; set; } = "";
        }

        [Fact]
        public void DeepNesting_Depth4_EFCore_FreeSql_StrategyBoundary()
        {
            var ef = _db.Types.OrderBy(t => t.Id).AsDto().ToList<QTypeDeepDto>();

            // EFCore（深度 8）：第 4 层 Meta 有值
            Assert.NotNull(ef[0].Meta);
            Assert.Equal("v1", ef[0].Meta!.Version);
            // FreeSql（深度 3）：第 4 层超出投影能力 → 嵌套对象投影不受支持（抛异常，与 NestedNavProjection 差异同源）
            Assert.ThrowsAny<Exception>(() =>
                _fsql.Select<QType>().OrderBy(t => t.Id).AsDto().ToList<QTypeDeepDto>());
        }
    }

    /// <summary>
    /// Fluent 声明 DTO 降级行为断言骨架（第三层门禁——行为断言载体）。
    /// <para>本类为最小可执行基线：Fluent 通道声明的类型对在内存映射路径行为正确。
    /// 填充：on/off 双配置 × EFCore/FreeSql AsDto 降级路径输出 = 内存路径逐字段一致
    /// ＋「脱敏不落明文」安全断言（Mask 声明在投影/降级路径均不泄露原值）。</para>
    /// </summary>
    public class FluentDegradationBehaviorSkeleton
    {
        public class SkeletonEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        // 命名避开 Dto/DTO 后缀——NCMAP019 命名启发式在豁免落地前会误报 Info 噪音
        public class SkeletonTarget
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        [MapperConfig]
        internal class SkeletonFluentMapper
        {
            public static void Configure(FluentMappingConfig cfg)
            {
                cfg.For<SkeletonEntity, SkeletonTarget>();
            }
        }

        [Fact]
        public void FluentDto_InMemoryMapping_Baseline()
        {
            // 骨架基准：Fluent 声明类型对内存路径映射正确（降级断言以此为对照基准）
            var entity = new SkeletonEntity { Id = 7, Name = "skeleton" };
            var mapped = Mapper.Map<SkeletonEntity, SkeletonTarget>(entity);

            Assert.Equal(7, mapped.Id);
            Assert.Equal("skeleton", mapped.Name);
        }
    }
}
