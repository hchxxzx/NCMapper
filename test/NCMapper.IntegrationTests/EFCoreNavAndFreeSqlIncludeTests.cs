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
    /// T2-7: EFCore NavigationCollector Include 失败兜底 + 深度保护
    /// </summary>
    public class EFCoreNavigationCollectorTests : IDisposable
    {
        private readonly NavTestDbContext _db;

        public EFCoreNavigationCollectorTests()
        {
            var connStr = $"DataSource=file:navtest{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new NavTestDbContext(connStr);
            _db.Database.EnsureCreated();
            _db.Categories.AddRange(
                new NavCategory { Id = 1, Name = "Tech" },
                new NavCategory { Id = 2, Name = "Science" });
            _db.Articles.AddRange(
                new NavArticle { Id = 1, Title = "AI", CategoryId = 1 },
                new NavArticle { Id = 2, Title = "Physics", CategoryId = 2 });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        // ================================================================
        // 实体
        // ================================================================

        public class NavCategory
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<NavArticle> Articles { get; set; } = new();
        }

        public class NavArticle
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public int CategoryId { get; set; }
            public NavCategory? Category { get; set; }
        }

        // ================================================================
        // DTO — 导航属性无对应源（解析失败兜底）
        // ================================================================

        /// <summary>
        /// DTO 含 "PhantomNav" 属性标注 [MapFrom]，但源实体无此导航
        /// → NavigationResolver 返回 null → 收集器跳过 → 不抛异常
        /// </summary>
        [MapFrom(typeof(NavArticle))]
        public class ArticleWithPhantomNavDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";

            /// <summary>源实体无 "PhantomNav" 导航 → 解析失败应被兜底</summary>
            public PhantomDto? PhantomNav { get; set; }
        }

        [MapFrom(typeof(NavCategory))]
        public class PhantomDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        // ================================================================
        // DTO — 循环引用（深度保护）
        // ================================================================

        [MapFrom(typeof(NavCategory))]
        public class CircularCatDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<CircularArticleDto> Articles { get; set; } = new();
        }

        [MapFrom(typeof(NavArticle))]
        public class CircularArticleDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            /// <summary>循环引用回 Category → visited 集合应阻止无限递归</summary>
            public CircularCatDto? Category { get; set; }
        }

        // ================================================================
        // DbContext
        // ================================================================

        public class NavTestDbContext : DbContext
        {
            public DbSet<NavCategory> Categories { get; set; } = null!;
            public DbSet<NavArticle> Articles { get; set; } = null!;

            private readonly string _connectionString;
            public NavTestDbContext(string connectionString) => _connectionString = connectionString;

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlite(_connectionString);

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<NavCategory>()
                    .HasMany(c => c.Articles)
                    .WithOne(a => a.Category)
                    .HasForeignKey(a => a.CategoryId);
            }
        }

        // ================================================================
        // 测试
        // ================================================================

        /// <summary>
        /// 导航属性解析失败（源实体无对应导航）→ 兜底不抛异常，顶层数据正确
        /// </summary>
        [Fact]
        public void PhantomNavigation_ResolutionFails_GracefulFallback()
        {
            var dtos = _db.Articles.AsDto().ToList<ArticleWithPhantomNavDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.Title == "AI");
            Assert.Contains(dtos, d => d.Title == "Physics");
            // PhantomNav 无法解析 → 应为 null（兜底跳过）
            Assert.All(dtos, d => Assert.Null(d.PhantomNav));
        }

        /// <summary>
        /// 循环引用 DTO（Category→Articles→Category）→ visited 集合阻止无限递归，不抛异常
        /// </summary>
        [Fact]
        public void CircularReference_DepthProtection_NoStackOverflow()
        {
            var dtos = _db.Categories
                .Include(c => c.Articles)
                .AsDto()
                .ToList<CircularCatDto>();

            Assert.Equal(2, dtos.Count);
            var tech = dtos.First(d => d.Name == "Tech");
            Assert.Single(tech.Articles);
            Assert.Equal("AI", tech.Articles[0].Title);
        }
    }

    /// <summary>
    /// T2-8: FreeSql [MapNoAutoInclude] 过滤测试
    /// 验证降级路径中 MapNoAutoInclude 标注的属性不被 Include
    /// </summary>
    public class FreeSqlMapNoAutoIncludeTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        public FreeSqlMapNoAutoIncludeTests()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"noauto_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            _fsql.Insert(new NaiBlog { Id = 1, Title = "Blog1" }).ExecuteAffrows();
            _fsql.Insert(new NaiComment { Id = 1, BlogId = 1, Text = "Nice!" }).ExecuteAffrows();
            _fsql.Insert(new NaiComment { Id = 2, BlogId = 1, Text = "Great!" }).ExecuteAffrows();
        }

        public void Dispose() => _fsql.Dispose();

        // ================================================================
        // 实体
        // ================================================================

        public class NaiBlog
        {
            [Column(IsIdentity = false, IsPrimary = true)]
            public int Id { get; set; }
            public string Title { get; set; } = "";

            [Navigate(nameof(NaiComment.BlogId))]
            public List<NaiComment> Comments { get; set; } = new();
        }

        public class NaiComment
        {
            [Column(IsIdentity = false, IsPrimary = true)]
            public int Id { get; set; }
            public int BlogId { get; set; }
            public string Text { get; set; } = "";
        }

        // ================================================================
        // DTO
        // ================================================================

        /// <summary>含 [MapNoAutoInclude] 的 DTO — Comments 不被 Include</summary>
        [MapFrom(typeof(NaiBlog))]
        public class BlogNoIncludeDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";

            [MapNoAutoInclude]
            public List<CommentItemDto>? Comments { get; set; }
        }

        /// <summary>无 [MapNoAutoInclude] 的 DTO — Comments 正常 Include（对照）</summary>
        [MapFrom(typeof(NaiBlog))]
        public class BlogWithIncludeDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public List<CommentItemDto>? Comments { get; set; }
        }

        [MapFrom(typeof(NaiComment))]
        public class CommentItemDto
        {
            public int Id { get; set; }
            public string Text { get; set; } = "";
        }

        // ================================================================
        // 测试
        // ================================================================

        /// <summary>
        /// [MapNoAutoInclude] 属性在降级路径中不被 Include → 值为空集合（源实体导航未加载）
        /// </summary>
        [Fact]
        public void MapNoAutoInclude_Property_NotIncluded_IsEmpty()
        {
            var dtos = _fsql.Select<NaiBlog>().AsDto().ToList<BlogNoIncludeDto>();

            Assert.Single(dtos);
            Assert.Equal("Blog1", dtos[0].Title);
            // MapNoAutoInclude → 不 Include → 源实体 Comments 未加载 → 映射后为空集合
            Assert.Empty(dtos[0].Comments!);
        }

        /// <summary>
        /// 无 [MapNoAutoInclude] 标注 → 正常 Include → Comments 有数据（对照）
        /// </summary>
        [Fact]
        public void NoAttribute_Property_Included_HasData()
        {
            var dtos = _fsql.Select<NaiBlog>().AsDto().ToList<BlogWithIncludeDto>();

            Assert.Single(dtos);
            Assert.Equal("Blog1", dtos[0].Title);
            // 无 MapNoAutoInclude → 正常 Include → Comments 有数据
            Assert.NotNull(dtos[0].Comments);
            Assert.Equal(2, dtos[0].Comments!.Count);
        }
    }
}
