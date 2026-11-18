using Microsoft.EntityFrameworkCore;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// EF Core 测试用 DbContext — SQLite 内存数据库
    /// </summary>
    public class TestDbContext : DbContext
    {
        /// <summary>
        /// 用户表
        /// </summary>
        public DbSet<UserEntity> Users { get; set; } = null!;
        /// <summary>
        /// 订单表
        /// </summary>
        public DbSet<OrderEntity> Orders { get; set; } = null!;
        /// <summary>
        /// 客户表
        /// </summary>
        public DbSet<CustomerEntity> Customers { get; set; } = null!;
        /// <summary>
        /// 可空测试表
        /// </summary>
        public DbSet<NullableEntity> Nullables { get; set; } = null!;
        /// <summary>
        /// 产品表（含枚举字段）
        /// </summary>
        public DbSet<ProductEntity> Products { get; set; } = null!;
        /// <summary>
        /// 博客表（含嵌套对象）
        /// </summary>
        public DbSet<BlogEntity> Blogs { get; set; } = null!;
        /// <summary>
        /// 作者表
        /// </summary>
        public DbSet<AuthorEntity> Authors { get; set; } = null!;

        private readonly string _connectionString;

        /// <summary>
        /// 构造函数 — 接收 SQLite 连接字符串
        /// </summary>
        /// <param name="connectionString">数据库连接字符串</param>
        public TestDbContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// 配置 SQLite 数据库连接
        /// </summary>
        /// <param name="optionsBuilder">EF Core 配置构建器</param>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(_connectionString);
        }

        /// <summary>
        /// 配置实体映射关系（导航属性、表名）
        /// </summary>
        /// <param name="modelBuilder">EF Core 模型构建器</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Order → Customer 导航关系
            modelBuilder.Entity<OrderEntity>()
                .HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId);

            modelBuilder.Entity<UserEntity>().ToTable("Users");
            modelBuilder.Entity<CustomerEntity>().ToTable("Customers");
            modelBuilder.Entity<OrderEntity>().ToTable("Orders");
            modelBuilder.Entity<NullableEntity>().ToTable("Nullables");
            modelBuilder.Entity<ProductEntity>().ToTable("Products");
            modelBuilder.Entity<BlogEntity>().ToTable("Blogs");
            modelBuilder.Entity<AuthorEntity>().ToTable("Authors");

            // Blog → Author 导航关系
            modelBuilder.Entity<BlogEntity>()
                .HasOne(b => b.Author)
                .WithMany()
                .HasForeignKey(b => b.AuthorId);
        }

        /// <summary>
        /// 初始化测试数据
        /// </summary>
        public void SeedData()
        {
            Database.EnsureCreated();

            // Users
            Users.AddRange(
                new UserEntity { Id = 1, Name = "Alice", Age = 30, Email = "alice@test.com", CreatedAt = new DateTime(2024, 1, 1), IsActive = true, Salary = 5000m },
                new UserEntity { Id = 2, Name = "Bob", Age = 25, Email = "bob@test.com", CreatedAt = new DateTime(2024, 2, 1), IsActive = false, Salary = 6000m },
                new UserEntity { Id = 3, Name = "Charlie", Age = 35, Email = "charlie@test.com", CreatedAt = new DateTime(2024, 3, 1), IsActive = true, Salary = 7000m }
            );

            // Customers
            Customers.AddRange(
                new CustomerEntity { Id = 1, Name = "ZhangSan", Level = "VIP", Phone = "13800000001" },
                new CustomerEntity { Id = 2, Name = "LiSi", Level = "Normal", Phone = "13800000002" }
            );

            // Orders
            Orders.AddRange(
                new OrderEntity { Id = 1, OrderNo = "ORD-001", Amount = 299.99m, CustomerId = 1 },
                new OrderEntity { Id = 2, OrderNo = "ORD-002", Amount = 1500m, CustomerId = 2 },
                new OrderEntity { Id = 3, OrderNo = "ORD-003", Amount = 50.5m, CustomerId = 1 }
            );

            // Nullables
            Nullables.AddRange(
                new NullableEntity { Id = 1, Score = 95, Birthday = new DateTime(1990, 5, 15) },
                new NullableEntity { Id = 2, Score = null, Birthday = null }
            );

            // Products（含枚举字段）
            Products.AddRange(
                new ProductEntity { Id = 1, Name = "Laptop", Price = 5999.99m, Status = OrderStatus.Confirmed, OptionalStatus = OrderStatus.Pending },
                new ProductEntity { Id = 2, Name = "Phone", Price = 3999m, Status = OrderStatus.Completed, OptionalStatus = null },
                new ProductEntity { Id = 3, Name = "Tablet", Price = 2500m, Status = OrderStatus.Cancelled, OptionalStatus = OrderStatus.Completed }
            );

            // Authors
            Authors.AddRange(
                new AuthorEntity { Id = 1, Name = "Alice Writer", Email = "alice@blog.com" },
                new AuthorEntity { Id = 2, Name = "Bob Blogger", Email = "bob@blog.com" }
            );

            // Blogs（含嵌套 Author）
            Blogs.AddRange(
                new BlogEntity { Id = 1, Title = "NCMapper 入门指南", AuthorId = 1 },
                new BlogEntity { Id = 2, Title = "EF Core 最佳实践", AuthorId = 2 },
                new BlogEntity { Id = 3, Title = "FreeSql 实战", AuthorId = 1 }
            );

            SaveChanges();
        }
    }
}
