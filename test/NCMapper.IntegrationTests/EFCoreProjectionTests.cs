using Xunit;
using Microsoft.EntityFrameworkCore;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    [MapFrom(typeof(BlogEntity))]
    internal class BlogDotPathDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(BlogEntity), "Author.Name")]
        public string? AuthorName { get; set; }
    }

    /// <summary>
    /// EF Core + NCMapper Projection 集成测试
    /// 使用 SQLite 内存数据库验证 SQL 翻译正确性
    /// </summary>
    public class EFCoreProjectionTests : IDisposable
    {
        private readonly TestDbContext _db;

        /// <summary>
        /// 初始化 SQLite 内存数据库并植入测试数据
        /// </summary>
        public EFCoreProjectionTests()
        {
            var connStr = $"DataSource=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new TestDbContext(connStr);
            _db.SeedData();
        }

        public void Dispose() => _db.Dispose();

        // ================================================================
        // 单表测试
        // ================================================================

        /// <summary>
        /// 简单扁平映射 — 所有字段正确映射
        /// </summary>
        [Fact]
        public void EFCore_SimpleFlat_All_Fields_Correct()
        {
            var dtos = _db.Users
                .Select(EfCoreProjectionBuilder.Build<UserEntity, UserDto>())
                .OrderBy(d => d.Id)
                .ToList();

            Assert.Equal(3, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal(30, dtos[0].Age);
            Assert.Equal("alice@test.com", dtos[0].Email);
            Assert.True(dtos[0].IsActive);
            Assert.Equal(5000m, dtos[0].Salary);
        }

        /// <summary>
        /// 异名字段映射 — [MapProperty] 标注字段正确，int→long 隐式转换
        /// </summary>
        [Fact]
        public void EFCore_NameDiff_All_Fields_Correct()
        {
            var dtos = _db.Users
                .Where(u => u.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<UserEntity, UserNameDiffDto>())
                .ToList();

            Assert.Single(dtos);
            var dto = dtos[0];
            Assert.Equal(1, dto.Id);
            Assert.Equal("Alice", dto.FullName);       // Name → FullName
            Assert.Equal(30, dto.UserAge);              // Age → UserAge
            Assert.Equal(1L, dto.AccountId);            // Id(int) → AccountId(long)
            Assert.Equal("alice@test.com", dto.ContactEmail);  // Email → ContactEmail
        }

        /// <summary>
        /// [MapIgnore] 属性被跳过，保持默认值
        /// </summary>
        [Fact]
        public void EFCore_MapIgnore_Skipped()
        {
            var dtos = _db.Users
                .Where(u => u.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<UserEntity, UserPartialDto>())
                .ToList();

            Assert.Single(dtos);
            Assert.Equal(1, dtos[0].Id);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Null(dtos[0].Computed);  // [MapIgnore] → 不查询，保持默认 null
        }

        /// <summary>
        /// Where 过滤 + Projection 组合生效
        /// </summary>
        [Fact]
        public void EFCore_Where_Filter_Works()
        {
            var dtos = _db.Users
                .Where(u => u.IsActive)
                .Select(EfCoreProjectionBuilder.Build<UserEntity, UserDto>())
                .OrderBy(d => d.Id)
                .ToList();

            Assert.Equal(2, dtos.Count);  // Alice + Charlie
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal("Charlie", dtos[1].Name);
        }

        /// <summary>
        /// 空结果集返回空列表
        /// </summary>
        [Fact]
        public void EFCore_Empty_Result()
        {
            var dtos = _db.Users
                .Where(u => u.Id == 99999)
                .Select(EfCoreProjectionBuilder.Build<UserEntity, UserDto>())
                .ToList();

            Assert.Empty(dtos);
        }

        // ================================================================
        // 可空类型测试
        // ================================================================

        /// <summary>
        /// 可空类型有值场景
        /// </summary>
        [Fact]
        public void EFCore_Nullable_HasValue()
        {
            var dtos = _db.Nullables
                .Where(n => n.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<NullableEntity, NullableDto>())
                .ToList();

            Assert.Single(dtos);
            Assert.Equal(95, dtos[0].Score);
            Assert.Equal(new DateTime(1990, 5, 15), dtos[0].Birthday);
        }

        /// <summary>
        /// 可空类型 null 值场景
        /// </summary>
        [Fact]
        public void EFCore_Nullable_IsNull()
        {
            var dtos = _db.Nullables
                .Where(n => n.Id == 2)
                .Select(EfCoreProjectionBuilder.Build<NullableEntity, NullableDto>())
                .ToList();

            Assert.Single(dtos);
            Assert.Null(dtos[0].Score);
            Assert.Null(dtos[0].Birthday);
        }

        // ================================================================
        // 多表（导航属性）测试
        // ================================================================

        /// <summary>
        /// 多表导航属性 JOIN — CustomerName/CustomerLevel 从导航属性映射
        /// </summary>
        [Fact]
        public void EFCore_MultiTable_Navigation_Join()
        {
            var dtos = _db.Orders
                .Select(EfCoreProjectionBuilder.Build<OrderEntity, OrderDetailDto>())
                .OrderBy(d => d.OrderId)
                .ToList();

            Assert.Equal(3, dtos.Count);

            // Order 1 → Customer 1 (ZhangSan, VIP)
            Assert.Equal("ORD-001", dtos[0].OrderNumber);
            Assert.Equal(299.99m, dtos[0].TotalAmount);
            Assert.Equal("ZhangSan", dtos[0].CustomerName);
            Assert.Equal("VIP", dtos[0].CustomerLevel);
            Assert.Equal("13800000001", dtos[0].CustomerPhone);

            // Order 2 → Customer 2 (LiSi, Normal)
            Assert.Equal("ORD-002", dtos[1].OrderNumber);
            Assert.Equal("LiSi", dtos[1].CustomerName);
            Assert.Equal("Normal", dtos[1].CustomerLevel);
        }

        /// <summary>
        /// 多表 + Where 过滤组合
        /// </summary>
        [Fact]
        public void EFCore_MultiTable_With_Where()
        {
            var dtos = _db.Orders
                .Where(o => o.Customer!.Level == "VIP")
                .Select(EfCoreProjectionBuilder.Build<OrderEntity, OrderDetailDto>())
                .OrderBy(d => d.OrderId)
                .ToList();

            Assert.Equal(2, dtos.Count);  // Order 1 + Order 3 都属于 ZhangSan(VIP)
            Assert.Equal("ZhangSan", dtos[0].CustomerName);
            Assert.Equal("ZhangSan", dtos[1].CustomerName);
        }

        /// <summary>
        /// 多表单条记录查询
        /// </summary>
        [Fact]
        public void EFCore_MultiTable_Single_Record()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 2)
                .Select(EfCoreProjectionBuilder.Build<OrderEntity, OrderDetailDto>())
                .FirstOrDefault();

            Assert.NotNull(dto);
            Assert.Equal("ORD-002", dto!.OrderNumber);
            Assert.Equal(1500m, dto.TotalAmount);
            Assert.Equal("LiSi", dto.CustomerName);
        }

        // ================================================================
        // Projection 与运行时 Mapper 一致性
        // ================================================================

        /// <summary>
        /// EF Core Projection 结果与运行时 Mapper 一致
        /// </summary>
        [Fact]
        public void EFCore_Projection_Matches_Runtime_Mapper()
        {
            // 先查出实体
            var entity = _db.Users.First(u => u.Id == 1);

            // Projection 方式
            var projDto = _db.Users
                .Where(u => u.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<UserEntity, UserDto>())
                .First();

            // 运行时 Mapper 方式
            var mapDto = Mapper.Map<UserEntity, UserDto>(entity)!;

            Assert.Equal(mapDto.Id, projDto.Id);
            Assert.Equal(mapDto.Name, projDto.Name);
            Assert.Equal(mapDto.Age, projDto.Age);
            Assert.Equal(mapDto.Email, projDto.Email);
            Assert.Equal(mapDto.IsActive, projDto.IsActive);
            Assert.Equal(mapDto.Salary, projDto.Salary);
        }

        // ================================================================
        // AsDto 扩展方法测试
        // ================================================================

        /// <summary>
        /// AsDto().ToList — 列表查询直接映射为 DTO
        /// </summary>
        [Fact]
        public void EFCore_AsDto_ToList_Works()
        {
            var dtos = _db.Users
                .Where(u => u.IsActive)
                .OrderBy(d => d.Id)
                .AsDto()
                .ToList<UserDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal("Charlie", dtos[1].Name);
        }

        /// <summary>
        /// AsDto().First — 单条查询直接映射为 DTO
        /// </summary>
        [Fact]
        public void EFCore_AsDto_First_Works()
        {
            var dto = _db.Users
                .Where(u => u.Id == 1)
                .AsDto()
                .First<UserDto>();

            Assert.NotNull(dto);
            Assert.Equal(1, dto!.Id);
            Assert.Equal("Alice", dto.Name);
            Assert.Equal(30, dto.Age);
            Assert.Equal("alice@test.com", dto.Email);
        }

        /// <summary>
        /// AsDto().First 无记录时返回 null
        /// </summary>
        [Fact]
        public void EFCore_AsDto_First_NotFound_Returns_Null()
        {
            var dto = _db.Users
                .Where(u => u.Id == 99999)
                .AsDto()
                .First<UserDto>();

            Assert.Null(dto);
        }

        /// <summary>
        /// AsDto().First 异名字段映射 — [MapProperty] 生效
        /// </summary>
        [Fact]
        public void EFCore_AsDto_First_NameDiff_Works()
        {
            var dto = _db.Users
                .Where(u => u.Id == 1)
                .AsDto()
                .First<UserNameDiffDto>();

            Assert.NotNull(dto);
            Assert.Equal(1, dto!.Id);
            Assert.Equal("Alice", dto.FullName);
            Assert.Equal(30, dto.UserAge);
            Assert.Equal(1L, dto.AccountId);
        }

        /// <summary>
        /// AsDto().First 多表导航属性 JOIN
        /// </summary>
        [Fact]
        public void EFCore_AsDto_First_MultiTable_Works()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .AsDto()
                .First<OrderDetailDto>();

            Assert.NotNull(dto);
            Assert.Equal("ORD-001", dto!.OrderNumber);
            Assert.Equal(299.99m, dto.TotalAmount);
            Assert.Equal("ZhangSan", dto.CustomerName);
            Assert.Equal("VIP", dto.CustomerLevel);
        }

        /// <summary>
        /// AsDto().ToListAsync 异步扩展方法
        /// </summary>
        [Fact]
        public async Task EFCore_AsDto_ToListAsync_Works()
        {
            var dtos = await _db.Users
                .OrderBy(d => d.Id)
                .AsDto()
                .ToListAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
        }

        /// <summary>
        /// AsDto().FirstAsync 异步扩展方法
        /// </summary>
        [Fact]
        public async Task EFCore_AsDto_FirstAsync_Works()
        {
            var dto = await _db.Users
                .Where(u => u.Id == 2)
                .AsDto()
                .FirstAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(dto);
            Assert.Equal("Bob", dto!.Name);
        }

        // ================================================================
        // AsDto 终端操作测试（替代已删除的 ProjectTo）
        // ================================================================

        /// <summary>
        /// AsDto — Where 过滤在 AsDto 之前完成
        /// </summary>
        [Fact]
        public void EFCore_AsDto_Where_Before_Terminal_Works()
        {
            var dtos = _db.Users
                .Where(u => u.Id > 1)
                .OrderBy(u => u.Id)
                .AsDto()
                .ToList<UserDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Bob", dtos[0].Name);
            Assert.Equal("Charlie", dtos[1].Name);
        }

        /// <summary>
        /// AsDto — OrderBy + Skip + Take 分页在 AsDto 之前完成
        /// </summary>
        [Fact]
        public void EFCore_AsDto_OrderBy_Skip_Take_Before_Terminal_Works()
        {
            var dtos = _db.Users
                .OrderBy(u => u.Id)
                .Skip(1)
                .Take(1)
                .AsDto()
                .ToList<UserDto>();

            Assert.Single(dtos);
            Assert.Equal("Bob", dtos[0].Name);
        }

        /// <summary>
        /// AsDto — IsActive 过滤 + 排序
        /// </summary>
        [Fact]
        public void EFCore_AsDto_IsActive_Filter_Works()
        {
            var dtos = _db.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.Id)
                .AsDto()
                .ToList<UserDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal("Charlie", dtos[1].Name);
        }

        /// <summary>
        /// AsDto — 异步查询
        /// </summary>
        [Fact]
        public async Task EFCore_AsDto_Async_Works()
        {
            var dtos = await _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToListAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
        }

        // ================================================================
        // P1-8~9: ORM 异常/边界路径
        // ================================================================

        /// <summary>
        /// AsDto().ToListAsync — 异步列表不带 Where 过滤，返回全部数据
        /// </summary>
        [Fact]
        public async Task AsDto_ToListAsync_WithNoFilter_ReturnsAll()
        {
            var dtos = await _db.Users
                .AsDto()
                .ToListAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, dtos.Count);
            Assert.Contains(dtos, d => d.Name == "Alice");
            Assert.Contains(dtos, d => d.Name == "Bob");
            Assert.Contains(dtos, d => d.Name == "Charlie");
        }

        /// <summary>
        /// AsDto — 无过滤条件直接 ToList，验证默认行为不崩溃
        /// </summary>
        [Fact]
        public void AsDto_ToList_NoFilter_DefaultBehavior()
        {
            var dtos = _db.Users
                .AsDto()
                .ToList<UserDto>();

            Assert.Equal(3, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
        }

        // ================================================================
        // 分页查询测试（ToPagedList / ToPagedListAsync）
        // ================================================================

        /// <summary>
        /// T1-05: EF Core 第 1 页，3 条数据每页 2 条
        /// </summary>
        [Fact]
        public void EFCore_ToPagedList_FirstPage()
        {
            var page = _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToPagedList<UserDto>(page: 1, pageSize: 2);

            Assert.Equal(2, page.Items.Count);
            Assert.Equal(3, page.TotalCount);
            Assert.Equal(2, page.TotalPages);
            Assert.True(page.HasNext);
            Assert.False(page.HasPrev);
            Assert.Equal("Alice", page.Items[0].Name);
            Assert.Equal("Bob", page.Items[1].Name);
        }

        /// <summary>
        /// T1-06: EF Core 最后一页（第 2 页），剩余 1 条
        /// </summary>
        [Fact]
        public void EFCore_ToPagedList_LastPage()
        {
            var page = _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToPagedList<UserDto>(page: 2, pageSize: 2);

            Assert.Single(page.Items);
            Assert.Equal(3, page.TotalCount);
            Assert.False(page.HasNext);
            Assert.True(page.HasPrev);
            Assert.Equal("Charlie", page.Items[0].Name);
        }

        /// <summary>
        /// T1-07: EF Core Where 条件无匹配 — 空结果集
        /// </summary>
        [Fact]
        public void EFCore_ToPagedList_EmptyResult()
        {
            var page = _db.Users
                .Where(u => u.Id == 99999)
                .AsDto()
                .ToPagedList<UserDto>(page: 1, pageSize: 2);

            Assert.Empty(page.Items);
            Assert.Equal(0, page.TotalCount);
            Assert.Equal(0, page.TotalPages);
            Assert.False(page.HasNext);
            Assert.False(page.HasPrev);
        }

        /// <summary>
        /// T1-08: EF Core 异步结果与同步一致
        /// </summary>
        [Fact]
        public async Task EFCore_ToPagedListAsync_SameAsSync()
        {
            var syncPage = _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToPagedList<UserDto>(page: 1, pageSize: 2);

            var asyncPage = await _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToPagedListAsync<UserDto>(page: 1, pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(syncPage.Items.Count, asyncPage.Items.Count);
            Assert.Equal(syncPage.TotalCount, asyncPage.TotalCount);
            Assert.Equal(syncPage.Page, asyncPage.Page);
            Assert.Equal(syncPage.PageSize, asyncPage.PageSize);
            Assert.Equal(syncPage.Items[0].Name, asyncPage.Items[0].Name);
        }

        // ================================================================
        // T02: 复杂场景测试 — 枚举投影、嵌套对象投影
        // ================================================================

        /// <summary>
        /// T02-01: 枚举字段投影 — 普通枚举和可空枚举都正确映射
        /// </summary>
        [Fact]
        public void EFCore_Enum_Projection_Works()
        {
            var dtos = _db.Products
                .OrderBy(p => p.Id)
                .Select(EfCoreProjectionBuilder.Build<ProductEntity, ProductDto>())
                .ToList();

            Assert.Equal(3, dtos.Count);

            // Product 1: Status = Confirmed, OptionalStatus = Pending
            Assert.Equal("Laptop", dtos[0].Name);
            Assert.Equal(OrderStatus.Confirmed, dtos[0].Status);
            Assert.Equal(OrderStatus.Pending, dtos[0].OptionalStatus);

            // Product 2: Status = Completed, OptionalStatus = null
            Assert.Equal("Phone", dtos[1].Name);
            Assert.Equal(OrderStatus.Completed, dtos[1].Status);
            Assert.Null(dtos[1].OptionalStatus);

            // Product 3: Status = Cancelled, OptionalStatus = Completed
            Assert.Equal("Tablet", dtos[2].Name);
            Assert.Equal(OrderStatus.Cancelled, dtos[2].Status);
            Assert.Equal(OrderStatus.Completed, dtos[2].OptionalStatus);
        }

        /// <summary>
        /// T02-02: 枚举字段 Where 过滤 — 按枚举值筛选
        /// </summary>
        [Fact]
        public void EFCore_Enum_Where_Filter_Works()
        {
            var dtos = _db.Products
                .Where(p => p.Status == OrderStatus.Completed)
                .Select(EfCoreProjectionBuilder.Build<ProductEntity, ProductDto>())
                .ToList();

            Assert.Single(dtos);
            Assert.Equal("Phone", dtos[0].Name);
            Assert.Equal(OrderStatus.Completed, dtos[0].Status);
        }

        /// <summary>
        /// T02-03: 嵌套对象投影 — Blog 包含 Author 嵌套 DTO
        /// </summary>
        [Fact]
        public void EFCore_NestedObject_Projection_Works()
        {
            var dtos = _db.Blogs
                .OrderBy(b => b.Id)
                .Select(EfCoreProjectionBuilder.Build<BlogEntity, BlogWithAuthorDto>())
                .ToList();

            Assert.Equal(3, dtos.Count);

            // Blog 1: Author = Alice Writer
            Assert.Equal("NCMapper 入门指南", dtos[0].Title);
            Assert.NotNull(dtos[0].Author);
            Assert.Equal("Alice Writer", dtos[0].Author!.Name);
            Assert.Equal("alice@blog.com", dtos[0].Author!.Email);

            // Blog 2: Author = Bob Blogger
            Assert.Equal("EF Core 最佳实践", dtos[1].Title);
            Assert.NotNull(dtos[1].Author);
            Assert.Equal("Bob Blogger", dtos[1].Author!.Name);
        }

        /// 投影点路径端到端(2026-08-15 实证):[MapProperty(typeof(BlogEntity), "Author.Name")] 经 EF Core 翻译 SQL 可用
        /// <para>修正 T0 F-1 旧结论(误读为 BuildPathCandidates 按大写拆分):点路径走 TryBuildDotPathExpression 按点号拆分</para>
        /// </summary>
        [Fact]
        public void EFCore_DotPath_Projection_EndToEnd_Works()
        {
            var dtos = _db.Blogs
                .OrderBy(b => b.Id)
                .Select(EfCoreProjectionBuilder.Build<BlogEntity, BlogDotPathDto>())
                .ToList();
            Assert.Equal("Alice Writer", dtos[0].AuthorName);
        }

        /// <summary>
        /// G7: 扁平化端到端 — 命名约定扁平化（AuthorName → Author.Name）经 EF Core 翻译为 SQL
        /// <para>验证投影表达式中的嵌套 MemberExpression（src.Author.Name）能被 EF Core 正确翻译并返回展平字段</para>
        /// </summary>
        [Fact]
        public void EFCore_Flattened_Projection_EndToEnd_Works()
        {
            var dtos = _db.Blogs
                .OrderBy(b => b.Id)
                .Select(EfCoreProjectionBuilder.Build<BlogEntity, BlogFlattenedDto>())
                .ToList();

            Assert.Equal(3, dtos.Count);

            // Blog 1: Author = Alice Writer
            Assert.Equal("NCMapper 入门指南", dtos[0].Title);
            Assert.Equal("Alice Writer", dtos[0].AuthorName);
            Assert.Equal("alice@blog.com", dtos[0].AuthorEmail);

            // Blog 2: Author = Bob Blogger
            Assert.Equal("EF Core 最佳实践", dtos[1].Title);
            Assert.Equal("Bob Blogger", dtos[1].AuthorName);
            Assert.Equal("bob@blog.com", dtos[1].AuthorEmail);

            // Blog 3: Author = Alice Writer（与 Blog 1 同作者）
            Assert.Equal("Alice Writer", dtos[2].AuthorName);
        }

        /// <summary>
        /// T02-04: 嵌套对象运行时映射 — BlogWithAuthorDto 通过 Mapper.Map 正确映射
        /// </summary>
        [Fact]
        public void EFCore_NestedObject_Runtime_Map_Works()
        {
            // 从 DB 查出实体（含导航属性）
            var entity = _db.Blogs
                .Include(b => b.Author)
                .First(b => b.Id == 1);

            // 运行时 Mapper 方式
            var dto = Mapper.Map<BlogEntity, BlogWithAuthorDto>(entity)!;

            Assert.Equal("NCMapper 入门指南", dto.Title);
            Assert.NotNull(dto.Author);
            Assert.Equal("Alice Writer", dto.Author!.Name);
            Assert.Equal("alice@blog.com", dto.Author.Email);
        }

        /// <summary>
        /// T02-05: 嵌套对象 + Where 过滤组合
        /// </summary>
        [Fact]
        public void EFCore_NestedObject_With_Where_Works()
        {
            var dtos = _db.Blogs
                .Where(b => b.Author!.Name == "Alice Writer")
                .Select(EfCoreProjectionBuilder.Build<BlogEntity, BlogWithAuthorDto>())
                .OrderBy(b => b.Id)
                .ToList();

            Assert.Equal(2, dtos.Count); // Blog 1 和 Blog 3 都是 Alice Writer
            Assert.All(dtos, d => Assert.Equal("Alice Writer", d.Author!.Name));
        }

        /// <summary>
        /// T02-06: 嵌套对象投影与运行时 Mapper 一致性
        /// </summary>
        [Fact]
        public void EFCore_NestedObject_Projection_Matches_Runtime_Mapper()
        {
            // 先查出实体（含导航属性）
            var entity = _db.Blogs
                .Include(b => b.Author)
                .First(b => b.Id == 1);

            // Projection 方式
            var projDto = _db.Blogs
                .Where(b => b.Id == 1)
                .Select(EfCoreProjectionBuilder.Build<BlogEntity, BlogWithAuthorDto>())
                .First();

            // 运行时 Mapper 方式
            var mapDto = Mapper.Map<BlogEntity, BlogWithAuthorDto>(entity)!;

            Assert.Equal(mapDto.Id, projDto.Id);
            Assert.Equal(mapDto.Title, projDto.Title);
            Assert.NotNull(mapDto.Author);
            Assert.NotNull(projDto.Author);
            Assert.Equal(mapDto.Author!.Name, projDto.Author!.Name);
            Assert.Equal(mapDto.Author.Email, projDto.Author.Email);
        }
    }
}
