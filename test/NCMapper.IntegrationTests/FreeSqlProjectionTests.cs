using Xunit;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSQL + NCMapper Projection 集成测试
    /// 使用 SQLite 验证 SQL 翻译正确性
    /// </summary>
    [Collection("PipelineGlobalState")]
    public class FreeSqlProjectionTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        /// <summary>
        /// 初始化 FreeSQL SQLite 临时文件数据库并植入测试数据
        /// <para>使用临时文件而非 URI 格式的内存共享模式，避免 .NET 9 下的兼容性问题</para>
        /// </summary>
        public FreeSqlProjectionTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_freesql_{Guid.NewGuid():N}.db");

            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            SeedData();
        }

        /// <summary>
        /// 释放 FreeSQL 连接并删除临时数据库文件
        /// </summary>
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
            // 同步表结构（UseAutoSyncStructure=true 已自动同步）
            // 插入数据
            _fsql.Insert(new List<UserEntity>
            {
                new() { Id = 1, Name = "Alice", Age = 30, Email = "alice@test.com", CreatedAt = new DateTime(2024, 1, 1), IsActive = true, Salary = 5000m },
                new() { Id = 2, Name = "Bob", Age = 25, Email = "bob@test.com", CreatedAt = new DateTime(2024, 2, 1), IsActive = false, Salary = 6000m },
                new() { Id = 3, Name = "Charlie", Age = 35, Email = "charlie@test.com", CreatedAt = new DateTime(2024, 3, 1), IsActive = true, Salary = 7000m }
            }).ExecuteAffrows();

            _fsql.Insert(new List<CustomerEntity>
            {
                new() { Id = 1, Name = "ZhangSan", Level = "VIP", Phone = "13800000001" },
                new() { Id = 2, Name = "LiSi", Level = "Normal", Phone = "13800000002" }
            }).ExecuteAffrows();

            _fsql.Insert(new List<OrderEntity>
            {
                new() { Id = 1, OrderNo = "ORD-001", Amount = 299.99m, CustomerId = 1 },
                new() { Id = 2, OrderNo = "ORD-002", Amount = 1500m, CustomerId = 2 },
                new() { Id = 3, OrderNo = "ORD-003", Amount = 50.5m, CustomerId = 1 }
            }).ExecuteAffrows();

            _fsql.Insert(new List<NullableEntity>
            {
                new() { Id = 1, Score = 95, Birthday = new DateTime(1990, 5, 15) },
                new() { Id = 2, Score = null, Birthday = null }
            }).ExecuteAffrows();

            // Products（含枚举字段）
            _fsql.Insert(new List<ProductEntity>
            {
                new() { Id = 1, Name = "Laptop", Price = 5999.99m, Status = OrderStatus.Confirmed, OptionalStatus = OrderStatus.Pending },
                new() { Id = 2, Name = "Phone", Price = 3999m, Status = OrderStatus.Completed, OptionalStatus = null },
                new() { Id = 3, Name = "Tablet", Price = 2500m, Status = OrderStatus.Cancelled, OptionalStatus = OrderStatus.Completed }
            }).ExecuteAffrows();

            // Authors
            _fsql.Insert(new List<AuthorEntity>
            {
                new() { Id = 1, Name = "Alice Writer", Email = "alice@blog.com" },
                new() { Id = 2, Name = "Bob Blogger", Email = "bob@blog.com" }
            }).ExecuteAffrows();

            // Blogs（含嵌套 Author）
            _fsql.Insert(new List<BlogEntity>
            {
                new() { Id = 1, Title = "NCMapper 入门指南", AuthorId = 1 },
                new() { Id = 2, Title = "EF Core 最佳实践", AuthorId = 2 },
                new() { Id = 3, Title = "FreeSql 实战", AuthorId = 1 }
            }).ExecuteAffrows();
        }

        // ================================================================
        // 单表测试
        // ================================================================

        /// <summary>
        /// 简单扁平映射 — 所有字段正确
        /// </summary>
        [Fact]
        public void FreeSql_SimpleFlat_All_Fields_Correct()
        {
            var expr = FreeSqlProjectionBuilder.Build<UserEntity, UserDto>();
            var dtos = _fsql.Select<UserEntity>().ToList(expr)
                .OrderBy(d => d.Id).ToList();

            Assert.Equal(3, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal(30, dtos[0].Age);
            Assert.Equal("alice@test.com", dtos[0].Email);
            Assert.True(dtos[0].IsActive);
            Assert.Equal(5000m, dtos[0].Salary);
        }

        /// <summary>
        /// 异名字段映射 — [MapProperty] 标注字段正确
        /// </summary>
        [Fact]
        public void FreeSql_NameDiff_All_Fields_Correct()
        {
            var expr = FreeSqlProjectionBuilder.Build<UserEntity, UserNameDiffDto>();
            var dtos = _fsql.Select<UserEntity>().Where(u => u.Id == 1).ToList(expr);

            Assert.Single(dtos);
            var dto = dtos[0];
            Assert.Equal(1, dto.Id);
            Assert.Equal("Alice", dto.FullName);
            Assert.Equal(30, dto.UserAge);
            Assert.Equal(1L, dto.AccountId);
            Assert.Equal("alice@test.com", dto.ContactEmail);
        }

        /// <summary>
        /// [MapIgnore] 属性被跳过
        /// </summary>
        [Fact]
        public void FreeSql_MapIgnore_Skipped()
        {
            var expr = FreeSqlProjectionBuilder.Build<UserEntity, UserPartialDto>();
            var dtos = _fsql.Select<UserEntity>().Where(u => u.Id == 1).ToList(expr);

            Assert.Single(dtos);
            Assert.Equal(1, dtos[0].Id);
            Assert.Equal("Alice", dtos[0].Name);
        }

        /// <summary>
        /// Where 过滤 + Projection 组合生效
        /// </summary>
        [Fact]
        public void FreeSql_Where_Filter_Works()
        {
            var expr = FreeSqlProjectionBuilder.Build<UserEntity, UserDto>();
            var dtos = _fsql.Select<UserEntity>().Where(u => u.IsActive).ToList(expr)
                .OrderBy(d => d.Id).ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal("Charlie", dtos[1].Name);
        }

        /// <summary>
        /// 空结果集返回空列表
        /// </summary>
        [Fact]
        public void FreeSql_Empty_Result()
        {
            var expr = FreeSqlProjectionBuilder.Build<UserEntity, UserDto>();
            var dtos = _fsql.Select<UserEntity>().Where(u => u.Id == 99999).ToList(expr);

            Assert.Empty(dtos);
        }

        // ================================================================
        // 可空类型测试
        // ================================================================

        /// <summary>
        /// 可空类型有值场景
        /// </summary>
        [Fact]
        public void FreeSql_Nullable_HasValue()
        {
            var expr = FreeSqlProjectionBuilder.Build<NullableEntity, NullableDto>();
            var dtos = _fsql.Select<NullableEntity>().Where(n => n.Id == 1).ToList(expr);

            Assert.Single(dtos);
            Assert.Equal(95, dtos[0].Score);
            Assert.Equal(new DateTime(1990, 5, 15), dtos[0].Birthday);
        }

        /// <summary>
        /// 可空类型 null 值场景
        /// </summary>
        [Fact]
        public void FreeSql_Nullable_IsNull()
        {
            var expr = FreeSqlProjectionBuilder.Build<NullableEntity, NullableDto>();
            var dtos = _fsql.Select<NullableEntity>().Where(n => n.Id == 2).ToList(expr);

            Assert.Single(dtos);
            Assert.Null(dtos[0].Score);
            Assert.Null(dtos[0].Birthday);
        }

        // ================================================================
        // 多表（导航属性）测试
        // ================================================================

        /// <summary>
        /// 多表 LeftJoin + 导航属性映射
        /// </summary>
        [Fact]
        public void FreeSql_MultiTable_Navigation_Join()
        {
            // FreeSQL 需要配置导航属性才能自动 JOIN
            // 这里用 LeftJoin + 导航属性表达式
            var expr = FreeSqlProjectionBuilder.Build<OrderEntity, OrderDetailDto>();
            var dtos = _fsql.Select<OrderEntity>()
                .LeftJoin(a => a.Customer!.Id == a.CustomerId)
                .ToList(expr)
                .OrderBy(d => d.OrderId).ToList();

            Assert.Equal(3, dtos.Count);

            Assert.Equal("ORD-001", dtos[0].OrderNumber);
            Assert.Equal(299.99m, dtos[0].TotalAmount);
            Assert.Equal("ZhangSan", dtos[0].CustomerName);
            Assert.Equal("VIP", dtos[0].CustomerLevel);
            Assert.Equal("13800000001", dtos[0].CustomerPhone);

            Assert.Equal("ORD-002", dtos[1].OrderNumber);
            Assert.Equal("LiSi", dtos[1].CustomerName);
        }

        /// <summary>
        /// 多表单条记录查询
        /// </summary>
        [Fact]
        public void FreeSql_MultiTable_Single_Record()
        {
            var expr = FreeSqlProjectionBuilder.Build<OrderEntity, OrderDetailDto>();
            var dto = _fsql.Select<OrderEntity>()
                .LeftJoin(a => a.Customer!.Id == a.CustomerId)
                .Where(a => a.Id == 2)
                .ToList(expr)
                .FirstOrDefault();

            Assert.NotNull(dto);
            Assert.Equal("ORD-002", dto!.OrderNumber);
            Assert.Equal(1500m, dto.TotalAmount);
            Assert.Equal("LiSi", dto.CustomerName);
        }

        // ================================================================
        // 多表 Compose 投影测试（BuildComposeProjection — 无需导航属性）
        // ================================================================

        /// <summary>
        /// 双表 Compose 投影 — 表达式编译并直接调用，验证字段映射正确
        /// </summary>
        [Fact]
        public void ComposeProjection_DualTable_ExpressionCompile_Invoke()
        {
            // BuildComposeProjection 生成 Expression<Func<T1, T2, TTarget>>，
            // 无需实体导航属性，直接从两个参数取值
            var expr = FreeSqlProjectionBuilder.BuildCompose<OrderEntity, CustomerEntity, OrderDetailDto>();
            var func = expr.Compile();

            var order = new OrderEntity { Id = 1, OrderNo = "ORD-001", Amount = 299.99m, CustomerId = 1 };
            var customer = new CustomerEntity { Id = 1, Name = "ZhangSan", Level = "VIP", Phone = "13800000001" };

            var dto = func(order, customer)!;

            Assert.Equal(1, dto.OrderId);
            Assert.Equal("ORD-001", dto.OrderNumber);
            Assert.Equal(299.99m, dto.TotalAmount);
            Assert.Equal("ZhangSan", dto.CustomerName);
            Assert.Equal("VIP", dto.CustomerLevel);
            Assert.Equal("13800000001", dto.CustomerPhone);
        }

        /// <summary>
        /// 双表 Compose 投影 — 内存 LINQ JOIN 验证多参数表达式
        /// </summary>
        [Fact]
        public void ComposeProjection_DualTable_LinqJoin_ToList()
        {
            // 准备内存数据
            var orders = new List<OrderEntity>
            {
                new() { Id = 1, OrderNo = "ORD-001", Amount = 299.99m, CustomerId = 1 },
                new() { Id = 2, OrderNo = "ORD-002", Amount = 1500m, CustomerId = 2 },
                new() { Id = 3, OrderNo = "ORD-003", Amount = 50.5m, CustomerId = 1 }
            };
            var customers = new List<CustomerEntity>
            {
                new() { Id = 1, Name = "ZhangSan", Level = "VIP", Phone = "13800000001" },
                new() { Id = 2, Name = "LiSi", Level = "Normal", Phone = "13800000002" }
            };

            // 使用 BuildComposeProjection 生成的多参数表达式进行内存 JOIN
            var expr = FreeSqlProjectionBuilder.BuildCompose<OrderEntity, CustomerEntity, OrderDetailDto>();
            var dtos = orders
                .Join(customers, o => o.CustomerId, c => c.Id, (o, c) => new { o, c })
                .Select(x => expr.Compile()(x.o, x.c)!)
                .OrderBy(d => d.OrderId).ToList();

            Assert.Equal(3, dtos.Count);

            Assert.Equal("ORD-001", dtos[0].OrderNumber);
            Assert.Equal(299.99m, dtos[0].TotalAmount);
            Assert.Equal("ZhangSan", dtos[0].CustomerName);
            Assert.Equal("VIP", dtos[0].CustomerLevel);
            Assert.Equal("13800000001", dtos[0].CustomerPhone);

            Assert.Equal("ORD-002", dtos[1].OrderNumber);
            Assert.Equal("LiSi", dtos[1].CustomerName);
            Assert.Equal("Normal", dtos[1].CustomerLevel);
        }

        /// <summary>
        /// 双表 Compose 投影 — Where 过滤后 JOIN 投影
        /// </summary>
        [Fact]
        public void ComposeProjection_DualTable_Filtered_LinqJoin()
        {
            var orders = new List<OrderEntity>
            {
                new() { Id = 1, OrderNo = "ORD-001", Amount = 299.99m, CustomerId = 1 },
                new() { Id = 2, OrderNo = "ORD-002", Amount = 1500m, CustomerId = 2 },
                new() { Id = 3, OrderNo = "ORD-003", Amount = 50.5m, CustomerId = 1 }
            };
            var customers = new List<CustomerEntity>
            {
                new() { Id = 1, Name = "ZhangSan", Level = "VIP", Phone = "13800000001" },
                new() { Id = 2, Name = "LiSi", Level = "Normal", Phone = "13800000002" }
            };

            var expr = FreeSqlProjectionBuilder.BuildCompose<OrderEntity, CustomerEntity, OrderDetailDto>();
            var compiled = expr.Compile();

            // 过滤 VIP 客户的订单
            var dtos = orders
                .Join(customers.Where(c => c.Level == "VIP"), o => o.CustomerId, c => c.Id, (o, c) => new { o, c })
                .Select(x => compiled(x.o, x.c)!)
                .OrderBy(d => d.OrderId).ToList();

            Assert.Equal(2, dtos.Count); // ZhangSan(VIP) 有 2 个订单
            Assert.Equal("ORD-001", dtos[0].OrderNumber);
            Assert.Equal("ORD-003", dtos[1].OrderNumber);
            Assert.All(dtos, d => Assert.Equal("VIP", d.CustomerLevel));
        }

        /// <summary>
        /// Compose 投影结果与导航属性投影一致 — 两种路径产出相同 DTO
        /// </summary>
        [Fact]
        public void ComposeProjection_Matches_NavigationProjection()
        {
            // 路径 A：导航属性投影（依赖 OrderEntity.Customer 导航属性）
            var navExpr = FreeSqlProjectionBuilder.Build<OrderEntity, OrderDetailDto>();
            var navDtos = _fsql.Select<OrderEntity>()
                .LeftJoin(a => a.Customer!.Id == a.CustomerId)
                .OrderBy(a => a.Id)
                .ToList(navExpr);

            // 路径 C：Compose 投影 — 从数据库查出 OrderEntity + CustomerEntity，内存 JOIN
            var orders = _fsql.Select<OrderEntity>().OrderBy(o => o.Id).ToList();
            var customers = _fsql.Select<CustomerEntity>().ToList();
            var composeExpr = FreeSqlProjectionBuilder.BuildCompose<OrderEntity, CustomerEntity, OrderDetailDto>();
            var compiled = composeExpr.Compile();
            var composeDtos = orders
                .Join(customers, o => o.CustomerId, c => c.Id, (o, c) => new { o, c })
                .Select(x => compiled(x.o, x.c)!)
                .ToList();

            Assert.Equal(navDtos.Count, composeDtos.Count);
            for (var i = 0; i < navDtos.Count; i++)
            {
                Assert.Equal(navDtos[i].OrderId, composeDtos[i].OrderId);
                Assert.Equal(navDtos[i].OrderNumber, composeDtos[i].OrderNumber);
                Assert.Equal(navDtos[i].TotalAmount, composeDtos[i].TotalAmount);
                Assert.Equal(navDtos[i].CustomerName, composeDtos[i].CustomerName);
                Assert.Equal(navDtos[i].CustomerLevel, composeDtos[i].CustomerLevel);
                Assert.Equal(navDtos[i].CustomerPhone, composeDtos[i].CustomerPhone);
            }
        }

        /// <summary>
        /// Compose 投影 — 表达式缓存命中（第二次调用返回缓存的表达式）
        /// </summary>
        [Fact]
        public void ComposeProjection_Cache_Hit()
        {
            var expr1 = FreeSqlProjectionBuilder.BuildCompose<OrderEntity, CustomerEntity, OrderDetailDto>();
            var expr2 = FreeSqlProjectionBuilder.BuildCompose<OrderEntity, CustomerEntity, OrderDetailDto>();

            // 同一类型对应返回缓存的表达式实例
            Assert.Same(expr1, expr2);
        }

        // ================================================================
        // Projection 与运行时 Mapper 一致性
        // ================================================================

        /// <summary>
        /// FreeSQL Projection 结果与运行时 Mapper 一致
        /// </summary>
        [Fact]
        public void FreeSql_Projection_Matches_Runtime_Mapper()
        {
            var entity = _fsql.Select<UserEntity>().Where(u => u.Id == 1).First();

            var expr = FreeSqlProjectionBuilder.Build<UserEntity, UserDto>();
            var projDto = _fsql.Select<UserEntity>().Where(u => u.Id == 1).ToList(expr).First();

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
        public void FreeSql_AsDto_ToList_Works()
        {
            var dtos = _fsql.Select<UserEntity>()
                .Where(u => u.IsActive)
                .AsDto()
                .ToList<UserDto>()
                .OrderBy(d => d.Id).ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal("Charlie", dtos[1].Name);
        }

        /// <summary>
        /// AsDto().First — 单条查询直接映射为 DTO
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_First_Works()
        {
            var dto = _fsql.Select<UserEntity>()
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
        public void FreeSql_AsDto_First_NotFound_Returns_Null()
        {
            var dto = _fsql.Select<UserEntity>()
                .Where(u => u.Id == 99999)
                .AsDto()
                .First<UserDto>();

            Assert.Null(dto);
        }

        /// <summary>
        /// AsDto().First 异名字段映射 — [MapProperty] 生效
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_First_NameDiff_Works()
        {
            var dto = _fsql.Select<UserEntity>()
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
        public void FreeSql_AsDto_First_MultiTable_Works()
        {
            var dto = _fsql.Select<OrderEntity>()
                .LeftJoin(a => a.Customer!.Id == a.CustomerId)
                .Where(a => a.Id == 1)
                .AsDto()
                .First<OrderDetailDto>();

            Assert.NotNull(dto);
            Assert.Equal("ORD-001", dto!.OrderNumber);
            Assert.Equal(299.99m, dto.TotalAmount);
            Assert.Equal("ZhangSan", dto.CustomerName);
            Assert.Equal("VIP", dto.CustomerLevel);
        }

        /// <summary>
        /// 场景 16：多表 Compose 投影 — 目标 DTO 含 [Convert] 时自动降级
        /// <para>降级路径：_select.ToList((a,b)=>Tuple) → Mapper.Compose，ConvertUsing 与跨表字段均生效</para>
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_Compose_ConvertUsing_Downgrades()
        {
            // 检测层：含 [Convert] 的 Compose DTO 被判定为需要二步映射
            var needs = FreeSqlDegradationDetector.NeedsDegradation(
                typeof(OrderEntity), typeof(OrderComposeConvDto), out var reason);
            Assert.True(needs);
            Assert.Contains("ConvertAttribute", reason);

            // 端到端：双表查询经 AsDto 多表入口自动降级
            var dtos = _fsql.Select<OrderEntity, CustomerEntity>()
                .Where((o, c) => o.CustomerId == c.Id)
                .AsDto()
                .ToList<OrderComposeConvDto>()
                .OrderBy(d => d.OrderId)
                .ToList();

            Assert.Equal(3, dtos.Count);
            Assert.Equal("ORD-001", dtos[0].OrderNumber);          // 普通字段（主源）正常
            Assert.Equal("¥299.99", dtos[0].FormattedAmount);      // ConvertUsing 经降级生效
            Assert.Equal("ZhangSan", dtos[0].CustomerName);        // 跨表字段经 Compose 填充
            Assert.Equal("¥1500.00", dtos[1].FormattedAmount);
            Assert.Equal("LiSi", dtos[1].CustomerName);
        }

        /// <summary>
        /// AsDto().ToListAsync 异步扩展方法
        /// </summary>
        [Fact]
        public async Task FreeSql_AsDto_ToListAsync_Works()
        {
            var dtos = await _fsql.Select<UserEntity>()
                .OrderBy(u => u.Id)
                .AsDto()
                .ToListAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
        }

        /// <summary>
        /// AsDto().FirstAsync 异步扩展方法
        /// </summary>
        [Fact]
        public async Task FreeSql_AsDto_FirstAsync_Works()
        {
            var dto = await _fsql.Select<UserEntity>()
                .Where(u => u.Id == 2)
                .AsDto()
                .FirstAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(dto);
            Assert.Equal("Bob", dto!.Name);
        }

        // ================================================================
        // 单泛型参数测试（从 [MapFrom] 自动推断源类型）
        // ================================================================

        /// <summary>
        /// 单泛型 ToListDto — 源类型从 ISelect 推断
        /// </summary>
        [Fact]
        public void FreeSql_ToListDto_SingleGeneric_InferSource()
        {
            // 源类型从 ISelect<UserEntity> 推断，只需指定 DTO 类型
            var dtos = _fsql.Select<UserEntity>()
                .Where(u => u.IsActive)
                .AsDto()
                .ToList<UserDto>()
                .OrderBy(d => d.Id).ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal("Charlie", dtos[1].Name);
        }

        /// <summary>
        /// 单泛型 FirstDto — 源类型从 ISelect 推断
        /// </summary>
        [Fact]
        public void FreeSql_FirstDto_SingleGeneric_InferSource()
        {
            var dto = _fsql.Select<UserEntity>()
                .Where(u => u.Id == 1)
                .AsDto()
                .First<UserDto>();

            Assert.NotNull(dto);
            Assert.Equal(1, dto!.Id);
            Assert.Equal("Alice", dto.Name);
        }

        /// <summary>
        /// 单泛型异名字段映射 — [MapProperty] 生效
        /// </summary>
        [Fact]
        public void FreeSql_AsdTo_NameDiff_Works()
        {
            var dto = _fsql.Select<UserEntity>()
                .Where(u => u.Id == 1)
                .AsDto()
                .First<UserNameDiffDto>();

            Assert.NotNull(dto);
            Assert.Equal(1, dto!.Id);
            Assert.Equal("Alice", dto.FullName);
            Assert.Equal(30, dto.UserAge);
            Assert.Equal(1L, dto.AccountId);
        }

        // ================================================================
        // ProjectTo 链式查询测试（通过 ToList+AsQueryable 内存路径）
        // 注意：FreeSql ISelect 不直接提供 IQueryable，链式投影走 EF Core 路径
        // ================================================================

        /// <summary>
        /// ProjectTo — FreeSql 不支持 IQueryable 链式，验证 AsDto 等价能力
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_EquivalentTo_ProjectTo()
        {
            // FreeSql 的 ISelect 不实现 IQueryable，无法用 ProjectTo 链式
            // 但 AsDto 提供了等价的查询能力：先 Where 过滤，再投影
            var dtos = _fsql.Select<UserEntity>()
                .Where(u => u.Id > 1)           // 在实体上过滤
                .OrderBy(u => u.Id)
                .AsDto()
                .ToList<UserDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Bob", dtos[0].Name);
            Assert.Equal("Charlie", dtos[1].Name);
        }

        // ================================================================
        // 分页查询测试（ToPagedList / ToPagedListAsync）
        // ================================================================

        /// <summary>
        /// T1-01: FreeSql 第 1 页，3 条数据每页 2 条
        /// </summary>
        [Fact]
        public void FreeSql_ToPagedList_FirstPage()
        {
            var page = _fsql.Select<UserEntity>()
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
        /// T1-02: FreeSql 最后一页（第 2 页），剩余 1 条
        /// </summary>
        [Fact]
        public void FreeSql_ToPagedList_LastPage()
        {
            var page = _fsql.Select<UserEntity>()
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
        /// T1-03: FreeSql Where 条件无匹配 — 空结果集
        /// </summary>
        [Fact]
        public void FreeSql_ToPagedList_EmptyResult()
        {
            var page = _fsql.Select<UserEntity>()
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
        /// T1-04: FreeSql 异步结果与同步一致
        /// </summary>
        [Fact]
        public async Task FreeSql_ToPagedListAsync_SameAsSync()
        {
            var syncPage = _fsql.Select<UserEntity>()
                .OrderBy(u => u.Id)
                .AsDto()
                .ToPagedList<UserDto>(page: 1, pageSize: 2);

            var asyncPage = await _fsql.Select<UserEntity>()
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
        // PagedList 计算属性测试（不依赖数据库）
        // ================================================================

        /// <summary>
        /// T1-09: 55 条 / 每页 20 → TotalPages=3
        /// </summary>
        [Fact]
        public void PagedList_TotalPages_Calculation()
        {
            var page = new PagedList<string>
            {
                Items = new List<string>(),
                TotalCount = 55,
                Page = 1,
                PageSize = 20
            };

            Assert.Equal(3, page.TotalPages);
            Assert.True(page.HasNext);
            Assert.False(page.HasPrev);
        }

        /// <summary>
        /// T1-10: 3 条 / 每页 20（一页装下）→ TotalPages=1
        /// </summary>
        [Fact]
        public void PagedList_SinglePage_FullSet()
        {
            var page = new PagedList<string>
            {
                Items = new List<string> { "a", "b", "c" },
                TotalCount = 3,
                Page = 1,
                PageSize = 20
            };

            Assert.Equal(1, page.TotalPages);
            Assert.False(page.HasNext);
            Assert.False(page.HasPrev);
        }

        // ================================================================
        // T02: 复杂场景测试 — 枚举投影、嵌套对象投影
        // ================================================================

        /// <summary>
        /// T02-01: 枚举字段投影 — 普通枚举和可空枚举都正确映射
        /// </summary>
        [Fact]
        public void FreeSql_Enum_Projection_Works()
        {
            var expr = FreeSqlProjectionBuilder.Build<ProductEntity, ProductDto>();
            var dtos = _fsql.Select<ProductEntity>()
                .OrderBy(p => p.Id)
                .ToList(expr);

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
        public void FreeSql_Enum_Where_Filter_Works()
        {
            var expr = FreeSqlProjectionBuilder.Build<ProductEntity, ProductDto>();
            var dtos = _fsql.Select<ProductEntity>()
                .Where(p => p.Status == OrderStatus.Completed)
                .ToList(expr);

            Assert.Single(dtos);
            Assert.Equal("Phone", dtos[0].Name);
            Assert.Equal(OrderStatus.Completed, dtos[0].Status);
        }

        /// <summary>
        /// T02-03: 枚举 + 可空类型组合映射 — 验证枚举在可空场景下的 ORM 投影
        /// </summary>
        [Fact]
        public void FreeSql_Enum_With_Nullable_Combination_Works()
        {
            var expr = FreeSqlProjectionBuilder.Build<ProductEntity, ProductDto>();

            // 查询 OptionalStatus 为 null 的产品
            var nullStatusDtos = _fsql.Select<ProductEntity>()
                .Where(p => p.OptionalStatus == null)
                .ToList(expr);

            Assert.Single(nullStatusDtos);
            Assert.Equal("Phone", nullStatusDtos[0].Name);
            Assert.Equal(OrderStatus.Completed, nullStatusDtos[0].Status);
            Assert.Null(nullStatusDtos[0].OptionalStatus);

            // 查询 OptionalStatus 有值的产品
            var hasStatusDtos = _fsql.Select<ProductEntity>()
                .Where(p => p.OptionalStatus != null)
                .OrderBy(p => p.Id)
                .ToList(expr);

            Assert.Equal(2, hasStatusDtos.Count);
            Assert.Equal(OrderStatus.Pending, hasStatusDtos[0].OptionalStatus);
            Assert.Equal(OrderStatus.Completed, hasStatusDtos[1].OptionalStatus);
        }

        /// <summary>
        /// T02-04: 嵌套对象运行时 MapList — 批量映射含嵌套对象的实体列表
        /// </summary>
        [Fact]
        public void FreeSql_NestedObject_MapList_Works()
        {
            // 从数据库查出所有博客和作者，手动组装导航属性
            var blogs = _fsql.Select<BlogEntity>().OrderBy(b => b.Id).ToList();
            var authors = _fsql.Select<AuthorEntity>().ToList();
            var authorMap = authors.ToDictionary(a => a.Id);
            foreach (var blog in blogs)
            {
                if (authorMap.TryGetValue(blog.AuthorId, out var author))
                    blog.Author = author;
            }

            // 批量映射
            var dtos = Mapper.MapList<BlogEntity, BlogWithAuthorDto>(blogs)!;

            Assert.Equal(3, dtos.Count);
            Assert.Equal("NCMapper 入门指南", dtos[0].Title);
            Assert.NotNull(dtos[0].Author);
            Assert.Equal("Alice Writer", dtos[0].Author!.Name);
            Assert.Equal("alice@blog.com", dtos[0].Author!.Email);
            Assert.Equal("EF Core 最佳实践", dtos[1].Title);
            Assert.Equal("Bob Blogger", dtos[1].Author!.Name);
        }

        /// <summary>
        /// T02-05: 嵌套对象投影与运行时 Mapper 一致性（内存路径）
        /// </summary>
        [Fact]
        public void FreeSql_NestedObject_Projection_Matches_Runtime_Mapper()
        {
            // 从数据库查出实体，手动组装导航属性
            var blogEntity = _fsql.Select<BlogEntity>().Where(b => b.Id == 1).First();
            var authorEntity = _fsql.Select<AuthorEntity>().Where(a => a.Id == blogEntity.AuthorId).First();
            blogEntity.Author = authorEntity;

            // 运行时 Mapper 方式
            var mapDto = Mapper.Map<BlogEntity, BlogWithAuthorDto>(blogEntity)!;

            Assert.Equal(1, mapDto.Id);
            Assert.Equal("NCMapper 入门指南", mapDto.Title);
            Assert.NotNull(mapDto.Author);
            Assert.Equal("Alice Writer", mapDto.Author!.Name);
            Assert.Equal("alice@blog.com", mapDto.Author.Email);
        }

        /// <summary>
        /// G7: 扁平化端到端 — 命名约定扁平化（AuthorName → Author.Name）经 FreeSql 翻译为 SQL JOIN
        /// <para>验证投影表达式中的嵌套 MemberExpression（src.Author.Name）能被 ORM 正确翻译并返回展平字段</para>
        /// </summary>
        [Fact]
        public void FreeSql_Flattened_Projection_EndToEnd_Works()
        {
            var expr = FreeSqlProjectionBuilder.Build<BlogEntity, BlogFlattenedDto>();
            var dtos = _fsql.Select<BlogEntity>()
                .OrderBy(b => b.Id)
                .ToList(expr);

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

        // ===== F-031 E2E-3: Compose PostMap 降级 E2E =====

        /// <summary>
        /// Compose 降级路径（含 ConvertUsing 触发）执行 PostMap 回调
        /// <para>Compose PostMap 按源实体类型匹配：注册 AddPostMap&lt;OrderEntity, Dto&gt; 对应主源触发</para>
        /// </summary>
        [Fact]
        public void E2E_ComposePostMap_Downgrade_PostMapExecuted()
        {
            Mapper.Pipeline.Clear();
            var postMapCalled = false;
            try
            {
                Mapper.Pipeline.AddPostMap<OrderEntity, OrderComposeConvDto>((src, dto) =>
                {
                    postMapCalled = true;
                    dto.FormattedAmount = "POSTMAP-OK";
                });

                Assert.True(FreeSqlDegradationDetector.NeedsDegradation(
                    typeof(OrderEntity), typeof(OrderComposeConvDto), out var reason));
                Assert.Contains("ConvertAttribute", reason);

                var dtos = _fsql.Select<OrderEntity, CustomerEntity>()
                    .LeftJoin((o, c) => o.CustomerId == c.Id)
                    .AsDto()
                    .ToList<OrderComposeConvDto>()
                    .OrderBy(d => d.OrderId)
                    .ToList();

                Assert.NotEmpty(dtos);
                Assert.True(postMapCalled, "PostMap 回调未被调用");
                Assert.All(dtos, dto => Assert.Equal("POSTMAP-OK", dto.FormattedAmount));
            }
            finally
            {
                Mapper.Pipeline.Clear();
            }
        }

        /// <summary>
        /// Compose PostMap — PostMap 内 Set/Get 上下文传递
        /// <para>Compose PostMap 按源实体类型匹配：注册 AddPostMap&lt;OrderEntity, Dto&gt; 对应主源触发</para>
        /// </summary>
        [Fact]
        public void E2E_ComposePostMap_ContextFlow()
        {
            Mapper.Pipeline.Clear();
            var postMapCalled = false;
            try
            {
                Mapper.Pipeline.AddPostMap<OrderEntity, OrderComposeConvDto>((src, dto) =>
                {
                    postMapCalled = true;
                    dto.FormattedAmount = "E2E3_Value";
                });

                var dtos = _fsql.Select<OrderEntity, CustomerEntity>()
                    .LeftJoin((o, c) => o.CustomerId == c.Id)
                    .AsDto()
                    .ToList<OrderComposeConvDto>()
                    .OrderBy(d => d.OrderId)
                    .ToList();

                Assert.True(postMapCalled, "PostMap 未被调用");
                Assert.NotEmpty(dtos);
                Assert.All(dtos, dto => Assert.Equal("E2E3_Value", dto.FormattedAmount));
            }
            finally
            {
                Mapper.Pipeline.Clear();
            }
        }
    }

    /// <summary>
    /// 场景 16 辅助 — 金额转换器（静态方法引用形态，T1 模板 M(TField)）：将 decimal 格式化为 "¥xxx.xx"（文化无关，避免千分位差异）
    /// </summary>
    public static class OrderAmountConverter
    {
        /// <summary>金额格式化为 "¥xxx.xx"（InvariantCulture）</summary>
        public static string Convert(decimal fieldValue)
            => "¥" + fieldValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 场景 16 辅助 — 多表 Compose 目标 DTO，含跨表字段（CustomerName）与 [Convert] 字段（FormattedAmount）
    /// <para>用于验证 Compose 降级路径同时支持 ConvertUsing 与跨表字段填充</para>
    /// </summary>
    [MapFrom(typeof(OrderEntity))]
    public class OrderComposeConvDto
    {
        [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Id))]
        public int OrderId { get; set; }

        [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderNo))]
        public string OrderNumber { get; set; } = "";

        [MapProperty(typeof(OrderEntity), nameof(OrderEntity.Amount))]
        [Convert(typeof(OrderAmountConverter), nameof(OrderAmountConverter.Convert))]
        public string FormattedAmount { get; set; } = "";

        [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.Name))]
        public string CustomerName { get; set; } = "";
    }
}
