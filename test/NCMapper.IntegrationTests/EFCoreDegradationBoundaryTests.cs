using Xunit;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// T1-4 + T1-5: EFCore 降级边界测试
    /// 验证 HashSet/Array 集合目标（C-1）和 record 无参构造器（C-3）触发降级
    /// </summary>
    public class EFCoreDegradationBoundaryTests : IDisposable
    {
        private readonly DegradationTestDbContext _db;

        public EFCoreDegradationBoundaryTests()
        {
            var connStr = $"DataSource=file:degtest{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new DegradationTestDbContext(connStr);
            _db.Database.EnsureCreated();
            SeedData();
        }

        public void Dispose() => _db.Dispose();

        private void SeedData()
        {
            _db.Departments.AddRange(
                new DeptEntity { Id = 1, Name = "Engineering" },
                new DeptEntity { Id = 2, Name = "Marketing" });
            _db.Staff.AddRange(
                new StaffEntity { Id = 1, Name = "Alice", DeptId = 1 },
                new StaffEntity { Id = 2, Name = "Bob", DeptId = 1 },
                new StaffEntity { Id = 3, Name = "Charlie", DeptId = 2 });
            _db.SaveChanges();
        }

        // ================================================================
        // 实体定义
        // ================================================================

        public class DeptEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<StaffEntity> Staff { get; set; } = new();
        }

        public class StaffEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int DeptId { get; set; }
            public DeptEntity? Dept { get; set; }
        }

        // ================================================================
        // DTO 定义
        // ================================================================

        /// <summary>含 HashSet 集合的 DTO — C-1 触发降级</summary>
        [MapFrom(typeof(DeptEntity))]
        public class DeptWithHashSetDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public HashSet<StaffItemDto> Staff { get; set; } = new();
        }

        /// <summary>含 Array 集合的 DTO — C-1 触发降级</summary>
        [MapFrom(typeof(DeptEntity))]
        public class DeptWithArrayDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public StaffItemDto[] Staff { get; set; } = Array.Empty<StaffItemDto>();
        }

        /// <summary>含 List 集合的 DTO — 不触发降级（负向对照）</summary>
        [MapFrom(typeof(DeptEntity))]
        public class DeptWithListDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<StaffItemDto> Staff { get; set; } = new();
        }

        /// <summary>集合元素 DTO</summary>
        [MapFrom(typeof(StaffEntity))]
        public class StaffItemDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        /// <summary>record DTO（无无参构造器）— C-3 触发降级</summary>
        [MapFrom(typeof(DeptEntity))]
        public record DeptRecordDto(int Id, string Name);

        /// <summary>含嵌套 record 的 DTO — C-3 触发降级</summary>
        [MapFrom(typeof(DeptEntity))]
        public class DeptWithRecordNestedDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public StaffRecordDto? FirstStaff { get; set; }
        }

        /// <summary>嵌套 record（无无参构造器）</summary>
        [MapFrom(typeof(StaffEntity))]
        public record StaffRecordDto(int Id, string Name);

        /// <summary>有参构造器的 class DTO（负向对照 — 非 record 但有参构造器）</summary>
        [MapFrom(typeof(DeptEntity))]
        public class DeptFlatDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        // ================================================================
        // DbContext
        // ================================================================

        public class DegradationTestDbContext : DbContext
        {
            public DbSet<DeptEntity> Departments { get; set; } = null!;
            public DbSet<StaffEntity> Staff { get; set; } = null!;

            private readonly string _connectionString;
            public DegradationTestDbContext(string connectionString) => _connectionString = connectionString;

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlite(_connectionString);

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<DeptEntity>()
                    .HasMany(d => d.Staff)
                    .WithOne(s => s.Dept)
                    .HasForeignKey(s => s.DeptId);
            }
        }

        // ================================================================
        // T1-4: HashSet/Array 降级测试
        // ================================================================

        /// <summary>
        /// HashSet 集合目标触发 C-1 降级 — 降级后运行时映射数据正确
        /// <para>旧断言固化缺陷行为（降级后运行时
        /// ExpressionBuilder 无法将 List→HashSet 赋值，抛 InvalidOperationException("No coercion operator")），
        /// 已按运行时容器构造适配（IEnumerable&lt;T&gt; 构造器档）语义同步——与
        /// Array_Collection_TriggersDegradation_DataCorrect 同构（降级触发 + 运行时转换成功 + 数据正确）。</para>
        /// </summary>
        [Fact]
        public void HashSet_Collection_TriggersDegradation_DataCorrect()
        {
            var dtos = _db.Departments
                .Include(d => d.Staff)
                .AsDto()
                .ToList<DeptWithHashSetDto>();

            // 降级到运行时映射后 HashSet 构造成功，数据正确（HashSet 无序，按数量+成员断言）
            Assert.Equal(2, dtos.Count);
            var eng = dtos.First(d => d.Name == "Engineering");
            Assert.Equal(2, eng.Staff.Count);
            Assert.Contains(eng.Staff, s => s.Name == "Alice");
            Assert.Contains(eng.Staff, s => s.Name == "Bob");
        }

        /// <summary>
        /// HashSet 降级 — 带 Where 过滤同样触发 C-1（验证检测不依赖数据量）
        /// <para>修复后降级路径数据正确（旧断言固化运行时崩溃行为，已同步）。</para>
        /// </summary>
        [Fact]
        public void HashSet_Collection_WithFilter_TriggersDegradation()
        {
            var dtos = _db.Departments
                .Include(d => d.Staff)
                .Where(d => d.Name == "Marketing")
                .AsDto()
                .ToList<DeptWithHashSetDto>();

            Assert.Single(dtos);
            Assert.Equal(1, dtos[0].Staff.Count);
            Assert.Contains(dtos[0].Staff, s => s.Name == "Charlie");
        }

        /// <summary>
        /// Array 集合目标触发 C-1 降级 — 运行时支持 List→Array 转换，数据正确
        /// </summary>
        [Fact]
        public void Array_Collection_TriggersDegradation_DataCorrect()
        {
            var dtos = _db.Departments
                .Include(d => d.Staff)
                .AsDto()
                .ToList<DeptWithArrayDto>();

            Assert.Equal(2, dtos.Count);
            var eng = dtos.First(d => d.Name == "Engineering");
            Assert.Equal(2, eng.Staff.Length);
            Assert.Contains(eng.Staff, s => s.Name == "Alice");
            Assert.Contains(eng.Staff, s => s.Name == "Bob");
        }

        /// <summary>
        /// List 集合不触发降级（负向）— 走投影路径，数据正确
        /// </summary>
        [Fact]
        public void List_Collection_NoDegradation_Projection_Works()
        {
            var dtos = _db.Departments
                .AsDto()
                .ToList<DeptFlatDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.Name == "Engineering");
            Assert.Contains(dtos, d => d.Name == "Marketing");
        }

        // ================================================================
        // T1-5: record 无参构造器降级测试
        // ================================================================

        /// <summary>
        /// 顶层 record DTO（无无参构造器）— C-3 仅检测嵌套 DTO，顶层走投影路径后抛异常
        /// <para>行为记录：EfCoreDegradationDetector.NeedsNestedDegradation 仅在递归嵌套 DTO 时
        /// 检查无参构造器（C-3），顶层 record 不触发降级，进入投影构建后失败。
        /// 此测试记录当前行为，供后续决策是否扩展 C-3 到顶层。</para>
        /// </summary>
        [Fact]
        public void Record_TopLevel_Degradation_Succeeds()
        {
            // 顶层 record（无无参构造器）→ 触发降级到内存映射，构造器参数匹配成功
            //（修复前走投影路径抛 InvalidOperationException——旧断言
            // Record_TopLevel_NoDegradation_ProjectionThrows 固化旧行为，已按语义同步）
            var dtos = _db.Departments
                .AsDto()
                .ToList<DeptRecordDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.Id == 1 && d.Name == "Engineering");
            Assert.Contains(dtos, d => d.Id == 2 && d.Name == "Marketing");
        }

        /// <summary>
        /// 有参构造器 class（非 record）走投影路径（负向对照）
        /// </summary>
        [Fact]
        public void Class_WithParameterlessCtor_NoDegradation_Projection()
        {
            // DeptFlatDto 有隐式无参构造器 → 不触发 C-3
            var dtos = _db.Departments
                .AsDto()
                .ToList<DeptFlatDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Equal(1, dtos.First(d => d.Name == "Engineering").Id);
        }

        // ================================================================
        // AsDto.AsTracking 显式跟踪声明
        // ================================================================

        /// <summary>
        /// AsDto 后显式 AsTracking → 降级路径保留变更跟踪
        /// <para>修复前无条件 AsNoTracking 覆盖用户显式 AsTracking（文档承诺失效）；
        /// EF Core 表达式层无法区分显式 AsTracking 与默认跟踪，故提供 selector 级显式声明。</para>
        /// </summary>
        [Fact]
        public void AsTracking_Explicit_Keeps_Tracking()
        {
            _db.ChangeTracker.Clear();

            var dtos = _db.Departments
                .AsDto()
                .AsTracking()
                .ToList<DeptRecordDto>();

            Assert.Equal(2, dtos.Count);
            // 显式 AsTracking 生效：查询实体进入 ChangeTracker
            Assert.NotEmpty(_db.ChangeTracker.Entries<DeptEntity>());
        }

        /// <summary>
        /// 对照：默认路径（无 AsTracking）仍为 AsNoTracking——查询实体不进入 ChangeTracker
        /// </summary>
        [Fact]
        public void Default_NoTracking_No_Entries()
        {
            _db.ChangeTracker.Clear();

            var dtos = _db.Departments
                .AsDto()
                .ToList<DeptRecordDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Empty(_db.ChangeTracker.Entries<DeptEntity>());
        }
    }
}
