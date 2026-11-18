using Microsoft.EntityFrameworkCore;
using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Orm;

/// <summary>
/// 最小化的内存 EF Core DbContext，供投影场景使用。
/// </summary>
internal sealed class InMemoryDbContext : DbContext
{
    /// <summary>
    /// Users 表。
    /// </summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

    /// <summary>
    /// Blogs 表（集合索引路径投影演示）。
    /// </summary>
    public DbSet<Blog> Blogs => Set<Blog>();

    /// <summary>
    /// Posts 表（集合索引路径投影演示）。
    /// </summary>
    public DbSet<Post> Posts => Set<Post>();

    /// <summary>
    /// FxWiki 表（Runtime×ORM Fluent 纯可翻译投影演示）。
    /// </summary>
    public DbSet<FxWikiEntity> FxWikis => Set<FxWikiEntity>();

    /// <summary>
    /// FxMask 表（Runtime×ORM Fluent Mask 强制降级演示）。
    /// </summary>
    public DbSet<FxMaskEntity> FxMasks => Set<FxMaskEntity>();

    /// <summary>
    /// 平铺分类树。
    /// </summary>
    public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();

    /// <summary>
    /// Fluent 树查询演示实体。
    /// </summary>
    public DbSet<FxTreeEntity> FxTrees => Set<FxTreeEntity>();

    /// <summary>
    /// 带 Options 的构造函数。
    /// </summary>
    /// <param name="options">DbContext 配置选项</param>
    public InMemoryDbContext(DbContextOptions<InMemoryDbContext> options) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasKey(u => u.Id);
            e.Ignore(u => u.Address);
        });

        // Blog 1→N Post 关系（集合索引路径投影 Posts[0].Title 依赖此导航）
        modelBuilder.Entity<Blog>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasMany(b => b.Posts)
                .WithOne()
                .HasForeignKey(p => p.BlogId);
        });
        modelBuilder.Entity<Post>(e => e.HasKey(p => p.Id));

        // Runtime×ORM Fluent 演示实体（简单主键）
        modelBuilder.Entity<FxWikiEntity>(e => e.HasKey(x => x.Id));
        modelBuilder.Entity<FxMaskEntity>(e => e.HasKey(x => x.Id));
    }
}
