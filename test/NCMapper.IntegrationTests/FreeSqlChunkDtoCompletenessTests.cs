using Xunit;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql 多表 ToChunkDto 完整性/一致性测试
    /// <para>验证修复后 T1、T2、T3 字段均正确填充，与 ToList 结果一致，空表 0 次回调</para>
    /// <para>使用 IClassFixture 共享 FreeSqlTestFixture，按需创建独立数据库实例（test-flag #FreeSqlChunkDtoCompletenessTests）</para>
    /// </summary>
    public class FreeSqlChunkDtoCompletenessTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造 — 从共享 Fixture 获取 FreeSql 实例
        /// </summary>
        /// <param name="fixture">共享测试 Fixture</param>
        public FreeSqlChunkDtoCompletenessTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // 双表 ToChunkDto 一致性
        // ================================================================

        /// <summary>
        /// 双表 ToChunkDto 与 ToList 结果一致 — 使用 FsTopicCategoryDto（无 ConvertUsing，降级路径）
        /// <para>验证：累计条数相同 + 逐条 Id/Title/CategoryName 完全一致</para>
        /// </summary>
        [Fact]
        public void T001_双表ToChunkDto_与ToList一致性()
        {
            var chunkResults = new List<FsTopicCategoryDto>();
            int callbackCount = 0;

            _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToChunkDto<FsTopicCategoryDto>(2, chunk =>
                {
                    callbackCount++;
                    chunkResults.AddRange(chunk);
                });

            var toListResults = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToList<FsTopicCategoryDto>();

            Assert.Equal(2, callbackCount); // ceil(4/2) = 2
            Assert.Equal(toListResults.Count, chunkResults.Count);

            for (int i = 0; i < toListResults.Count; i++)
            {
                Assert.Equal(toListResults[i].Id, chunkResults[i].Id);
                Assert.Equal(toListResults[i].Title, chunkResults[i].Title);
                Assert.Equal(toListResults[i].CategoryName, chunkResults[i].CategoryName);
            }
        }

        /// <summary>
        /// 双表 ToChunkDto 跨表字段正确值 — T2 字段 CategoryName 与数据库预期一致
        /// </summary>
        [Fact]
        public void T001_双表ToChunkDto_跨表字段CategoryName正确()
        {
            var all = new List<FsTopicCategoryDto>();

            _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToChunkDto<FsTopicCategoryDto>(3, chunk => all.AddRange(chunk));

            Assert.Equal(4, all.Count);
            // T1 字段
            Assert.Equal("NCMapper 入门", all[0].Title);
            // T2 字段 — Compose 后直接取值
            var expectedCategoryNames = new[] { ".NET", ".NET", "Python", "旅游" };
            Assert.Equal(expectedCategoryNames, all.Select(d => d.CategoryName).ToArray());
        }

        /// <summary>
        /// 双表 ToChunkDto 空表 0 次回调
        /// </summary>
        [Fact]
        public void T001_双表ToChunkDto_空表零次回调()
        {
            int callbackCount = 0;

            _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => a.Id == 999)
                .AsDto()
                .ToChunkDto<FsTopicCategoryDto>(2, _ => callbackCount++);

            Assert.Equal(0, callbackCount);
        }

        // ================================================================
        // 三表 ToChunkDto 一致性
        // ================================================================

        /// <summary>
        /// 三表 ToChunkDto 与 ToList 结果一致 — 使用 FsTopicFullDto（无 ConvertUsing，投影路径）
        /// <para>验证：累计条数相同 + 逐条 Id/Title/CategoryName/CategoryTypeName 完全一致</para>
        /// </summary>
        [Fact]
        public void T001_三表ToChunkDto_与ToList一致性()
        {
            var chunkResults = new List<FsTopicFullDto>();
            int callbackCount = 0;

            _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToChunkDto<FsTopicFullDto>(3, chunk =>
                {
                    callbackCount++;
                    chunkResults.AddRange(chunk);
                });

            var toListResults = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToList<FsTopicFullDto>();

            Assert.Equal(2, callbackCount); // ceil(4/3) = 2
            Assert.Equal(toListResults.Count, chunkResults.Count);

            for (int i = 0; i < toListResults.Count; i++)
            {
                Assert.Equal(toListResults[i].Id, chunkResults[i].Id);
                Assert.Equal(toListResults[i].Title, chunkResults[i].Title);
                Assert.Equal(toListResults[i].CategoryName, chunkResults[i].CategoryName);
                Assert.Equal(toListResults[i].CategoryTypeName, chunkResults[i].CategoryTypeName);
            }
        }

        /// <summary>
        /// 三表 ToChunkDto 跨表字段正确值 — T2/T3 字段 CategoryName/CategoryTypeName 与数据库预期一致
        /// </summary>
        [Fact]
        public void T001_三表ToChunkDto_跨表字段CategoryName和TypeName正确()
        {
            var all = new List<FsTopicFullDto>();

            _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToChunkDto<FsTopicFullDto>(2, chunk => all.AddRange(chunk));

            Assert.Equal(4, all.Count);
            Assert.Equal("NCMapper 入门", all[0].Title);
            // T2 字段
            var expectedCategoryNames = new[] { ".NET", ".NET", "Python", "旅游" };
            Assert.Equal(expectedCategoryNames, all.Select(d => d.CategoryName).ToArray());
            // T3 字段
            var expectedCategoryTypeNames = new[] { "技术", "技术", "技术", "生活" };
            Assert.Equal(expectedCategoryTypeNames, all.Select(d => d.CategoryTypeName).ToArray());
        }

        /// <summary>
        /// 三表 ToChunkDto 空表 0 次回调
        /// </summary>
        [Fact]
        public void T001_三表ToChunkDto_空表零次回调()
        {
            int callbackCount = 0;

            _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => a.Id == 999)
                .AsDto()
                .ToChunkDto<FsTopicFullDto>(2, _ => callbackCount++);

            Assert.Equal(0, callbackCount);
        }
    }

    // ================================================================
    // ToChunkDto 两路径公共前置 Include
    // 批次三修复 da65665：ApplyFreeSqlIncludes 提升至 ToChunkDto 两条路径公共前置——
    // 原实现仅降级分支 Include，正常路径（编译投影客户端映射）导航属性静默 null。
    // 敏感性由 回退演练承担（回退 Include 提升行 → 导航非 null 断言红）。
    // 独立 SQLite 实例（ConvertUsingProjectionBehaviorTests 先例形态，不依赖共享 Fixture 种子）。
    // ================================================================

    public class T7ChunkCust
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class T7ChunkOrder
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public T7ChunkCust? Customer { get; set; }
    }

    /// <summary>导航 DTO（Collector 据此收集 "Customer" Include 路径；Include 生效时非 null 且 Name 填充）。</summary>
    [MapFrom(typeof(T7ChunkCust))]
    public class T7ChunkCustDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>单源 DTO——Customer 为导航 DTO 属性（FreeSqlNavigationCollector 收集 Include 路径的判定面）。</summary>
    [MapFrom(typeof(T7ChunkOrder))]
    public class T7ChunkOrderDto
    {
        public int Id { get; set; }
        public T7ChunkCustDto? Customer { get; set; }
    }

    public class T7ChunkDtoNavigationTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public T7ChunkDtoNavigationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_t7chunk_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            _fsql.Insert(new List<T7ChunkCust>
            {
                new() { Id = 1, Name = "Alice" },
                new() { Id = 2, Name = "Bob" },
            }).ExecuteAffrows();
            _fsql.Insert(new List<T7ChunkOrder>
            {
                new() { Id = 1, CustomerId = 1, Customer = new T7ChunkCust { Id = 1, Name = "Alice" } },
                new() { Id = 2, CustomerId = 2, Customer = new T7ChunkCust { Id = 2, Name = "Bob" } },
            }).ExecuteAffrows();
        }

        public void Dispose()
        {
            _fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); }
                catch { }
            }
        }

        /// <summary>
        /// C-01 核心断言：ToChunkDto 正常路径（编译投影客户端映射）导航属性非 null——
        /// Include 提升至公共前置后，Customer 导航加载且嵌套映射填充。
        /// 回退修复行（V9）时正常路径不 Include → Customer = null 红。
        /// </summary>
        [Fact]
        public void ToChunkDto_NormalPath_IncludeNavigations()
        {
            var collected = new List<T7ChunkOrderDto>();
            _fsql.Select<T7ChunkOrder>()
                .AsDto()
                .ToChunkDto<T7ChunkOrderDto>(100, chunk => collected.AddRange(chunk));

            Assert.Equal(2, collected.Count);
            Assert.Equal("Alice", collected.Single(d => d.Id == 1).Customer!.Name);
            Assert.Equal("Bob", collected.Single(d => d.Id == 2).Customer!.Name);
        }
    }
}
