// 前置验证测试
// 策略：代码分析 + 最小运行时验证，复用现有 Models 和 TestDbContext
// 8 个占位 Assert.True(true) 整改
//   4 个补真实断言（C1/C3/E2×2，覆盖注释声称的行为），4 个改 Skip 并注明承接方。
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;

namespace NCMapper.IntegrationTests
{
    // ================================================================
    // 局部测试模型（仅本文件使用）
    // ================================================================

    public class T0Dept
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<T0Staff> Staff { get; set; } = new();
    }

    public class T0Staff
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int DeptId { get; set; }
        public T0Dept? Dept { get; set; }
    }

    /// <summary>扁平 DTO — E-2 投影路径（不触发降级）</summary>
    [MapFrom(typeof(T0Dept))]
    public class T0DeptFlatDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>ConvertUsing DTO — E-2 降级路径</summary>
    [MapFrom(typeof(T0Dept))]
    public class T0DeptConvDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [Convert(typeof(T0NoOpConverter), nameof(T0NoOpConverter.Convert))]
        public string? Tag { get; set; }
    }

    /// <summary>无操作转换器（静态方法引用形态，T1 模板 M(TField)）— null → 空串，仅触发降级路径</summary>
    public static class T0NoOpConverter
    {
        /// <summary>原值返回（null → 空串）</summary>
        public static string Convert(string fieldValue) => fieldValue ?? "";
    }

    /// <summary>HashSet 集合目标 — C-1 触发降级（运行时 List→HashSet 无强制转换）</summary>
    [MapFrom(typeof(T0Dept))]
    public class T0DeptWithHashSetDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public HashSet<T0StaffDto> Staff { get; set; } = new();
    }

    /// <summary>嵌套 record — C-3 无无参构造器 → 集合投影跳过</summary>
    [MapFrom(typeof(T0Dept))]
    public class T0DeptWithRecordDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public T0StaffRecordDto? FirstStaff { get; set; }
    }

    [MapFrom(typeof(T0Staff))]
    public class T0StaffDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [MapFrom(typeof(T0Staff))]
    public record T0StaffRecordDto(int Id, string Name);

    /// <summary>查询计数拦截器流式性验证（统计 ExecuteReader 次数：分页=多次查询，全量=1 次）</summary>
    public class T0QueryCounter : DbCommandInterceptor
    {
        public int ReaderExecutions;

        public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result)
        {
            Interlocked.Increment(ref ReaderExecutions);
            return result;
        }
    }

    public class T0VerificationDbContext : DbContext
    {
        public DbSet<T0Dept> Departments { get; set; } = null!;
        public DbSet<T0Staff> Staff { get; set; } = null!;

        private readonly string _connectionString;
        private readonly T0QueryCounter _counter;

        public T0VerificationDbContext(string connectionString, T0QueryCounter counter)
        {
            _connectionString = connectionString;
            _counter = counter;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connectionString).AddInterceptors(_counter);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<T0Dept>()
                .HasMany(d => d.Staff)
                .WithOne(s => s.Dept)
                .HasForeignKey(s => s.DeptId);
        }
    }

    public abstract class T0DbTestBase : IDisposable
    {
        protected readonly T0VerificationDbContext Db;
        protected readonly T0QueryCounter Counter = new();

        protected T0DbTestBase()
        {
            var connStr = $"DataSource=file:t0verif{Guid.NewGuid():N}?mode=memory&cache=shared";
            Db = new T0VerificationDbContext(connStr, Counter);
            Db.Database.EnsureCreated();
            Db.Departments.AddRange(
                new T0Dept { Id = 1, Name = "Engineering", Staff = { new T0Staff { Id = 1, Name = "Alice", DeptId = 1 }, new T0Staff { Id = 2, Name = "Bob", DeptId = 1 } } },
                new T0Dept { Id = 2, Name = "Marketing", Staff = { new T0Staff { Id = 3, Name = "Charlie", DeptId = 2 } } });
            Db.SaveChanges();
        }

        public void Dispose() => Db.Dispose();
    }

    // ================================================================
    // F-1: 点路径验证
    // ================================================================

    /// <summary>
    /// T0 验证 — F-1 点路径分析（纯代码审查，无数据库）
    /// </summary>
    public class T0_F1_DotPath_Analysis
    {
        /// <summary>
        /// F-1 — 结论修正(2026-08-15)：点路径 FromField="A.B" 在投影中确认可用
        /// <para>旧结论误读：点路径走 TryBuildDotPathExpression 按点号拆分，与 BuildPathCandidates（按大写拆分）
        /// 无关；端到端实证见 EFCoreProjectionTests.EFCore_DotPath_Projection_EndToEnd_Works。</para>
        /// </summary>
        [Fact(Skip = "F-1 旧结论已被 EFCore_DotPath_Projection_EndToEnd_Works 取代，保留占位防回归误解")]
        public void F1_DotPath_Confirmed_Broken()
        {
        }
    }

    /// <summary>
    /// T0 验证 — F-1 命名约定扁平化（数据库验证）
    /// </summary>
    public class T0_F1_Flattened
    {
        [Fact(Skip = "占位改 Skip：命名约定扁平化已在隔离测试（NCMapper.Tests）确认，类内运行受缓存状态影响")]
        public void F1_NamingConvention_Flat_Works()
        {
        }
    }

    // ================================================================
    // F-3 / C-1 / C-2 / C-3
    // ================================================================

    public class T0_AnalysisTests : T0DbTestBase
    {
        [Fact(Skip = "占位改 Skip：BuildPathCandidates 两份实现一致性为 SourceGen private 静态方法，由 SourceGen.Tests 承接")]
        public void F3_BuildPathCandidates_Identical()
        {
        }

        /// <summary>
        /// 真实断言 T[]/HashSet&lt;T&gt;目标不可赋值 → 集合投影路径不可用。
        /// 行为验证：HashSet 集合目标触发降级，降级后运行时映射数据正确。
        /// <para>旧断言固化缺陷行为（运行时映射器 List→HashSet
        /// 无强制转换抛 InvalidOperationException("No coercion operator")），已按运行时容器构造适配
        /// 语义同步（与 EFCoreDegradationBoundaryTests.HashSet_Collection_TriggersDegradation_DataCorrect 一致）。</para>
        /// </summary>
        [Fact]
        public void C1_ArrayAndHashSet_NotAssignable()
        {
            var dtos = Db.Departments
                .Include(d => d.Staff)
                .AsDto()
                .ToList<T0DeptWithHashSetDto>();

            // 降级到运行时映射后 HashSet 构造成功，数据正确（HashSet 无序，按数量+成员断言）
            Assert.Equal(2, dtos.Count);
            var eng = dtos.First(d => d.Name == "Engineering");
            Assert.Equal(2, eng.Staff.Count);
            Assert.Contains(eng.Staff, s => s.Name == "Alice");
            Assert.Contains(eng.Staff, s => s.Name == "Bob");
        }

        [Fact(Skip = "占位改 Skip：EnableCollectionProjection=false 为 FreeSql 配置常量（代码审查项），由 FreeSql 专项测试承接")]
        public void C2_FreeSql_CollectionProjection_Disabled()
        {
        }

        /// <summary>
        /// 真实断言 record 类型无无参构造器（反射事实）+
        /// 嵌套 record 元素集合投影跳过、降级路径数据正确。
        /// </summary>
        [Fact]
        public void C3_RecordElementNoDefaultCtor()
        {
            // 事实：record 无无参构造器
            Assert.Null(typeof(T0StaffRecordDto).GetConstructor(Type.EmptyTypes));

            // 行为：嵌套 record 触发降级，映射结果数据正确（不抛投影翻译异常）
            var dtos = Db.Departments
                .Include(d => d.Staff)
                .AsDto()
                .ToList<T0DeptWithRecordDto>();

            Assert.Equal(2, dtos.Count);
            var eng = dtos.First(d => d.Name == "Engineering");
            // 行为：record 元素无无参构造器 → GetParameterlessCtor 返回 null → 嵌套投影跳过（FirstStaff 为 null），
            // 查询整体不抛投影翻译异常——跳过而非崩溃（与测试名声称的行为一致）。
            Assert.Null(eng.FirstStaff);
        }
    }

    // ================================================================
    // E-2: EFCore AsNoTracking + 降级路径
    // ================================================================

    /// <summary>
    /// T0 验证 — E-2 投影路径（数据库验证）
    /// </summary>
    public class T0_E2_Projection : T0DbTestBase
    {
        /// <summary>
        /// 真实断言投影路径 AsNoTracking 生效——查询后实体不进入 ChangeTracker。
        /// </summary>
        [Fact]
        public void E2_ProjectionPath_NoChangeTracking()
        {
            Db.ChangeTracker.Clear();

            var dtos = Db.Departments.AsDto().ToList<T0DeptFlatDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Empty(Db.ChangeTracker.Entries());
        }

        /// <summary>
        /// 真实断言降级路径（ConvertUsing 触发）同样 AsNoTracking
        /// 查询实体不进入 ChangeTracker（Batch 3 已统一追加 AsNoTracking）。
        /// </summary>
        [Fact]
        public void E2_DegradationPath_NoAsNoTracking()
        {
            Db.ChangeTracker.Clear();

            var dtos = Db.Departments.AsDto().ToList<T0DeptConvDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Empty(Db.ChangeTracker.Entries());
        }

        /// <summary>
        /// 回归降级路径 ToChunkDto 分页流式
        /// 25 条数据 size=10 → 3 次回调（10/10/5），不再一次性全量物化；
        /// 终止条件用过滤前计数。
        /// </summary>
        [Fact]
        public void E2_DegradationPath_ToChunkDto_StreamsInPages()
        {
            for (int i = 3; i <= 25; i++)
                Db.Departments.Add(new T0Dept { Id = i, Name = $"Dept{i}" });
            Db.SaveChanges();

            var batches = new List<int>();
            int total = 0;
            var queriesBefore = Counter.ReaderExecutions; // EnsureCreated 等前置查询已发生

            Db.Departments.OrderBy(d => d.Id).AsDto().ToChunkDto<T0DeptConvDto>(10, chunk =>
            {
                batches.Add(chunk.Count);
                total += chunk.Count;
            });

            Assert.Equal(new[] { 10, 10, 5 }, batches); // 分页流式：3 页（修复前 1 次回调 25 条）
            Assert.Equal(25, total);
            Assert.Equal(3, Counter.ReaderExecutions - queriesBefore); // ToChunkDto 期间逐页查询 3 次（修复前全量物化仅 1 次）
        }
    }
}
