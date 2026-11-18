using Xunit;
using Microsoft.EntityFrameworkCore;
using FreeSql;
using FreeSql.DataAnnotations;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// T2-1: Pipeline PostMap 精确类型对触发降级测试
    /// 验证 EFCore/FreeSql 降级矩阵 #6：PostMap 注册后 AsDto 走降级路径
    /// </summary>
    [Collection("PipelineGlobalState")]
    public class PipelineDegradationTests : IDisposable
    {
        private readonly DegradationTestDbContext _db;
        private readonly IFreeSql _fsql;

        public PipelineDegradationTests()
        {
            // EFCore fixture
            var connStr = $"DataSource=file:pipedeg{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new DegradationTestDbContext(connStr);
            _db.Database.EnsureCreated();
            _db.Departments.AddRange(
                new DegradationTestDbContext.DeptEntity { Id = 1, Name = "Engineering" },
                new DegradationTestDbContext.DeptEntity { Id = 2, Name = "Marketing" });
            _db.SaveChanges();

            // FreeSql fixture
            var dbPath = Path.Combine(Path.GetTempPath(), $"pipedeg_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
            _fsql.Insert(new PipeTopic { Id = 1, Title = "Hello", Clicks = 10 }).ExecuteAffrows();
            _fsql.Insert(new PipeTopic { Id = 2, Title = "World", Clicks = 20 }).ExecuteAffrows();
        }

        public void Dispose()
        {
            Mapper.Pipeline.Clear();
            _db.Dispose();
            _fsql.Dispose();
        }

        // ================================================================
        // 实体/DTO 定义
        // ================================================================

        public class PipeTopic
        {
            [Column(IsIdentity = false, IsPrimary = true)]
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public int Clicks { get; set; }
        }

        [MapFrom(typeof(PipeTopic))]
        public class PipeTopicDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public int Clicks { get; set; }
        }

        // ================================================================
        // EFCore PostMap 降级
        // ================================================================

        [MapFrom(typeof(DegradationTestDbContext.DeptEntity))]
        public class PipeDeptDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// EFCore: 注册 PostMap 后 AsDto 触发降级，PostMap 回调生效
        /// </summary>
        [Fact]
        public void EFCore_PostMap_TriggersDegradation_CallbackApplied()
        {
            Mapper.Pipeline.AddPostMap<DegradationTestDbContext.DeptEntity, PipeDeptDto>(
                (src, tgt) => tgt.Name += " [PM]");

            var dtos = _db.Departments.AsDto()
                .ToList<PipeDeptDto>();

            Assert.Equal(2, dtos.Count);
            Assert.All(dtos, d => Assert.EndsWith(" [PM]", d.Name));
        }

        /// <summary>
        /// EFCore: Clear 后恢复投影路径，PostMap 不再生效
        /// </summary>
        [Fact]
        public void EFCore_PostMap_Clear_RestoresProjection()
        {
            Mapper.Pipeline.AddPostMap<DegradationTestDbContext.DeptEntity, PipeDeptDto>(
                (src, tgt) => tgt.Name += " [PM]");
            Mapper.Pipeline.Clear();

            var dtos = _db.Departments.AsDto()
                .ToList<PipeDeptDto>();

            Assert.Equal(2, dtos.Count);
            Assert.DoesNotContain(dtos, d => d.Name.Contains("[PM]"));
            Assert.Contains(dtos, d => d.Name == "Engineering");
        }

        // ================================================================
        // FreeSql PostMap 降级
        // ================================================================

        /// <summary>
        /// FreeSql: 注册 PostMap 后 AsDto 触发降级，PostMap 回调生效
        /// </summary>
        [Fact]
        public void FreeSql_PostMap_TriggersDegradation_CallbackApplied()
        {
            Mapper.Pipeline.AddPostMap<PipeTopic, PipeTopicDto>(
                (src, tgt) => tgt.Title = tgt.Title.ToUpper());

            var dtos = _fsql.Select<PipeTopic>().AsDto().ToList<PipeTopicDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.Title == "HELLO");
            Assert.Contains(dtos, d => d.Title == "WORLD");
        }

        /// <summary>
        /// FreeSql: Clear 后恢复投影路径
        /// </summary>
        [Fact]
        public void FreeSql_PostMap_Clear_RestoresProjection()
        {
            Mapper.Pipeline.AddPostMap<PipeTopic, PipeTopicDto>(
                (src, tgt) => tgt.Title = tgt.Title.ToUpper());
            Mapper.Pipeline.Clear();

            var dtos = _fsql.Select<PipeTopic>().AsDto().ToList<PipeTopicDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.Title == "Hello");
            Assert.Contains(dtos, d => d.Title == "World");
        }

        // ================================================================
        // DbContext (复用 EFCoreDegradationBoundaryTests 的结构)
        // ================================================================

        public class DegradationTestDbContext : DbContext
        {
            public DbSet<DeptEntity> Departments { get; set; } = null!;

            private readonly string _connectionString;
            public DegradationTestDbContext(string connectionString) => _connectionString = connectionString;

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlite(_connectionString);

            public class DeptEntity
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
        }
    }
}
