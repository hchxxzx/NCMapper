using Xunit;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql Compose 投影集成测试 — 双表/三表 JOIN → AsDto
    /// <para>覆盖 T1-01 ~ T1-06：三表全路径 + 双表正常投影 + 异步降级</para>
    /// </summary>
    public class FreeSqlComposeProjectionTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造 — 从共享 Fixture 获取 FreeSql 实例
        /// </summary>
        /// <param name="fixture">共享测试 Fixture</param>
        public FreeSqlComposeProjectionTests(FreeSqlTestFixture fixture)
        {
            _fsql = fixture.Fsql;
        }

        /// <summary>
        /// Fixture 可独立初始化，种子数据正确
        /// </summary>
        [Fact]
        public void T001_Fixture_Initializes_Correctly()
        {
            // 验证 Topic 数据
            var topics = _fsql.Select<FsTopic>().ToList();
            Assert.Equal(5, topics.Count);
            Assert.Contains(topics, t => t.IsDeleted == true);
            Assert.Contains(topics, t => t.Title == "NCMapper 入门");

            // 验证 Category 数据
            var categories = _fsql.Select<FsCategory>().ToList();
            Assert.Equal(5, categories.Count);

            // 验证 CategoryType 数据
            var types = _fsql.Select<FsCategoryType>().ToList();
            Assert.Equal(3, types.Count);

            // 验证 Comment 数据
            var comments = _fsql.Select<FsComment>().ToList();
            Assert.Equal(4, comments.Count);
            Assert.Contains(comments, c => c.IsDeleted);
            Assert.Contains(comments, c => !c.IsApproved);

            // 验证 FsOtherEntity 数据
            var others = _fsql.Select<FsOtherEntity>().ToList();
            Assert.Equal(2, others.Count);
        }

        // ================================================================
        // 双表 Compose 正常投影
        // G-02：验证 BuildComposeProjection 表达式能被 FreeSql 翻译
        // 代码锚点：NCMapperFreeSqlExtensions.cs:281-282
        // ================================================================

        /// <summary>
        /// T1-04：双表 Compose 正常投影 — 走 BuildComposeProjection 路径
        /// <para>代码锚点：NCMapperFreeSqlExtensions.cs:281-282（ToList 正常路径）</para>
        /// <para>验证点：1) SQL SELECT 含具体列名（非 a.*/b.*） 2) 数据正确映射</para>
        /// </summary>
        [Fact]
        public void T1_04_双表Compose正常投影()
        {
            // Arrange — 双表 LeftJoin
            var query = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted);

            // Act — 通过 BuildComposeProjection 生成表达式，在原始 ISelect 上验证 SQL
            var expr = FreeSqlProjectionBuilder.BuildCompose<FsTopic, FsCategory, FsTopicCategoryDto>();
            var sql = query.ToSql(expr);
            Assert.NotNull(sql);
            Assert.NotEmpty(sql);

            // 验证 SELECT 子句含具体列名（正常投影路径，FreeSql 展开具体列）
            Assert.Contains("a.\"Title\"", sql);
            Assert.Contains("b.\"Name\"", sql);
            // 正常投影不应出现 a.* 或 b.*
            Assert.DoesNotContain("a.*", sql);
            Assert.DoesNotContain("b.*", sql);

            // Act — 通过 AsDto 执行查询
            var dtos = query.AsDto().ToList<FsTopicCategoryDto>()
                .OrderBy(d => d.Id).ToList();

            // Assert — 数据正确（排除 IsDeleted=true 的 #5）
            Assert.Equal(4, dtos.Count);
            Assert.Equal("NCMapper 入门", dtos[0].Title);
            Assert.Equal(".NET", dtos[0].CategoryName);
            Assert.Equal("FreeSql 实战", dtos[1].Title);
            Assert.Equal(".NET", dtos[1].CategoryName);
            Assert.Equal("Python 爬虫", dtos[2].Title);
            Assert.Equal("Python", dtos[2].CategoryName);
            Assert.Equal("旅行攻略", dtos[3].Title);
            Assert.Equal("旅游", dtos[3].CategoryName);
        }

        // ================================================================
        // 三表 Compose 全路径
        // G-01：FreeSqlComposeDtoSelector3 四个方法零覆盖 → 全量验证
        // 代码锚点：NCMapperFreeSqlExtensions.cs:350-451
        // ================================================================

        /// <summary>
        /// T1-01：三表 Compose 正常投影 — BuildComposeProjection&lt;T1,T2,T3,TTarget&gt; 路径
        /// <para>验证点：三表字段均映射、SQL 含具体列名、非降级</para>
        /// </summary>
        [Fact]
        public void T1_01_三表Compose正常投影()
        {
            // Arrange
            var query = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted);

            // Act — SQL 验证
            var expr = FreeSqlProjectionBuilder.BuildCompose3<FsTopic, FsCategory, FsCategoryType, FsTopicFullDto>();
            var sql = query.ToSql(expr);
            Assert.NotNull(sql);
            Assert.NotEmpty(sql);
            Assert.Contains("a.\"Title\"", sql);
            Assert.Contains("b.\"Name\"", sql);
            Assert.Contains("c.\"TypeName\"", sql);

            // Act — 执行
            var dtos = query.AsDto().ToList<FsTopicFullDto>()
                .OrderBy(d => d.Id).ToList();

            // Assert
            Assert.Equal(4, dtos.Count);
            // Topic 1 → Category ".NET" (Id=1) → CategoryType "技术" (Id=1)
            Assert.Equal("NCMapper 入门", dtos[0].Title);
            Assert.Equal(".NET", dtos[0].CategoryName);
            Assert.Equal("技术", dtos[0].CategoryTypeName);
            // Topic 3 → Category "Python" (Id=2) → CategoryType "技术" (Id=1)
            Assert.Equal("Python 爬虫", dtos[2].Title);
            Assert.Equal("Python", dtos[2].CategoryName);
            Assert.Equal("技术", dtos[2].CategoryTypeName);
            // Topic 4 → Category "旅游" (Id=3) → CategoryType "生活" (Id=2)
            Assert.Equal("旅行攻略", dtos[3].Title);
            Assert.Equal("旅游", dtos[3].CategoryName);
            Assert.Equal("生活", dtos[3].CategoryTypeName);
        }

        /// <summary>
        /// T1-02：三表 Compose 降级 — ConvertUsing 触发 NeedsTwoStepMapping
        /// <para>降级路径：查 Tuple → Mapper.Compose（内存映射）</para>
        /// </summary>
        [Fact]
        public void T1_02_三表Compose降级_ConvertUsing触发()
        {
            // Arrange
            var query = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted);

            // Act — 降级路径（FsTopicFullConvDto 含 ConvertUsing）
            var dtos = query.AsDto().ToList<FsTopicFullConvDto>()
                .OrderBy(d => d.Id).ToList();

            // Assert — 降级路径数据正确 + ConvertUsing 生效
            Assert.Equal(4, dtos.Count);
            Assert.Equal("NCMapper 入门", dtos[0].Title);
            // ConvertUsing 将 "内容1" 转为 "[Conv]内容1"
            Assert.Equal("[Conv]内容1", dtos[0].Content);
            Assert.Equal(".NET", dtos[0].CategoryName);
            Assert.Equal("技术", dtos[0].CategoryTypeName);
            Assert.Equal("[Conv]内容3", dtos[2].Content);
        }

        /// <summary>
        /// T1-02b：单表降级 + 手动 Include — 验证导航数据正确
        /// <para>代码锚点：ApplyFreeSqlComposeIncludes（:363-370）仅 Trace 提示，不自动 Include</para>
        /// </summary>
        [Fact]
        public void T1_02b_单表降级_手动Include后导航正确()
        {
            // Arrange — 在 AsDto 之前手动调用 IncludeByPropertyName
            var query = _fsql.Select<FsTopic>()
                .IncludeByPropertyName("Category")
                .Where(a => !a.IsDeleted);
            // 走单表降级路径验证 Include 效果
            var dtos = query.AsDto().ToList<FsTopicDto>()
                .OrderBy(d => d.Id).ToList();

            // Assert — Category 导航已 Include，嵌套 DTO 正确
            Assert.Equal(4, dtos.Count);
            var first = dtos.First(d => d.Title == "NCMapper 入门");
            Assert.NotNull(first.Category);
            Assert.Equal(".NET", first.Category!.Name);
        }

        /// <summary>
        /// T1-03：三表 First/FirstAsync — 正常 + 降级 + 空结果
        /// </summary>
        [Fact]
        public async Task T1_03_三表Compose_First与FirstAsync()
        {
            // Arrange — 正常路径查询
            var normalQuery = CreateThreeTableQuery(topicId: 1);

            // Act — 正常路径 First
            var dto = normalQuery.AsDto().First<FsTopicFullDto>();
            Assert.NotNull(dto);
            Assert.Equal("NCMapper 入门", dto.Title);
            Assert.Equal(".NET", dto.CategoryName);

            // Act — 正常路径 FirstAsync
            var dtoAsync = await normalQuery.AsDto().FirstAsync<FsTopicFullDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(dtoAsync);
            Assert.Equal("NCMapper 入门", dtoAsync.Title);

            // Act — 降级路径 First（FsTopicFullConvDto 含 ConvertUsing）
            var degradeQuery = CreateThreeTableQuery(topicId: 2);
            var degradeDto = degradeQuery.AsDto().First<FsTopicFullConvDto>();
            Assert.NotNull(degradeDto);
            Assert.Equal("FreeSql 实战", degradeDto.Title);
            Assert.Equal("[Conv]内容2", degradeDto.Content);

            // Act — 降级路径 FirstAsync
            var degradeDtoAsync = await degradeQuery.AsDto().FirstAsync<FsTopicFullConvDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(degradeDtoAsync);
            Assert.Equal("[Conv]内容2", degradeDtoAsync.Content);

            // Act — 空结果 First/FirstAsync/降级空结果
            var emptyQuery = CreateThreeTableQuery(topicId: 999);
            Assert.Null(emptyQuery.AsDto().First<FsTopicFullDto>());
            Assert.Null(await emptyQuery.AsDto().FirstAsync<FsTopicFullDto>(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(emptyQuery.AsDto().First<FsTopicFullConvDto>());
        }

        // ================================================================
        // 异步降级路径
        // G-03：异步降级路径（ToListAsync/FirstAsync/ToPagedListAsync）全缺
        // 代码锚点：NCMapperFreeSqlExtensions.cs:90-97,130-137,190-197
        // ================================================================

        /// <summary>
        /// T1-05：单表异步降级 — ToListAsync/FirstAsync/ToPagedListAsync + CancellationToken
        /// <para>使用 FsTopicDto（含 ConvertUsing）触发降级，验证异步路径与同步一致</para>
        /// </summary>
        [Fact]
        public async Task T1_05_单表异步降级_三方法()
        {
            // Arrange
            var query = _fsql.Select<FsTopic>().Where(a => !a.IsDeleted).OrderBy(a => a.Id);

            // Act — ToListAsync 降级
            var dtos = await query.AsDto().ToListAsync<FsTopicDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(4, dtos.Count);
            Assert.Equal("[Conv]内容1", dtos[0].Content);

            // Act — FirstAsync 降级
            var first = await query.AsDto().FirstAsync<FsTopicDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(first);
            Assert.Equal("NCMapper 入门", first.Title);

            // Act — ToPagedListAsync 降级
            var paged = await query.AsDto().ToPagedListAsync<FsTopicDto>(1, 2, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(4, paged.TotalCount);

            // Act — CancellationToken 取消（FreeSql 将 TaskCanceledException 包装为 Exception）
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ex = await Assert.ThrowsAsync<Exception>(
                () => query.AsDto().ToListAsync<FsTopicDto>(cts.Token));
            // 验证内层异常为 TaskCanceledException（防止未来 FreeSql 行为变化导致 false positive）
            Assert.IsType<TaskCanceledException>(ex.InnerException);
        }

        /// <summary>
        /// T1-06：双表 Compose 异步降级 — ToListAsync/FirstAsync
        /// <para>使用 FsTopicFullConvDto（含 ConvertUsing）触发降级</para>
        /// </summary>
        [Fact]
        public async Task T1_06_双表Compose异步降级()
        {
            // Arrange
            var query = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted);

            // Act — ToListAsync 降级
            var dtos = await query.AsDto().ToListAsync<FsTopicFullConvDto>(cancellationToken: TestContext.Current.CancellationToken);
            var sorted = dtos.OrderBy(d => d.Id).ToList();
            Assert.Equal(4, sorted.Count);
            Assert.Equal("[Conv]内容1", sorted[0].Content);
            Assert.Equal(".NET", sorted[0].CategoryName);

            // Act — FirstAsync 降级
            var first = await _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => a.Id == 3)
                .AsDto()
                .FirstAsync<FsTopicFullConvDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(first);
            Assert.Equal("[Conv]内容3", first.Content);
            Assert.Equal("Python", first.CategoryName);
        }

        /// <summary>
        /// T1-07：三表 Compose 异步 FirstAsync — <b>单链形态</b>（不经私有辅助方法持有查询对象）
        /// <para>存在理由：Check6 ORM 终端台账的计数口径为「接收者上下文链前缀 ＋ 终端方法名」的文本近似，
        /// 跨语句 selector 变量持有形态不被计入 → 三表用例经 <see cref="CreateThreeTableQuery"/> 构造查询时，
        /// <c>FreeSqlComposeDtoSelector3.FirstAsync</c> 在统一口径下为 0 值行。本例以单链形态行使同一终端，
        /// 使该终端在统一口径下可见，同时补上「三源异步 First 单链」这一此前无覆盖的组合。</para>
        /// </summary>
        [Fact]
        public async Task T1_07_三表Compose异步FirstAsync_单链形态()
        {
            var dtoAsync = await _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => a.Id == 1)
                .AsDto()
                .FirstAsync<FsTopicFullDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(dtoAsync);
            Assert.Equal("NCMapper 入门", dtoAsync.Title);
            Assert.Equal(".NET", dtoAsync.CategoryName);
        }

        /// <summary>
        /// 创建三表 LeftJoin×2 查询 — 减少 T1-01/03 等测试的重复构造代码
        /// </summary>
        /// <param name="topicId">可选的 Topic Id 过滤（null = 不过滤）</param>
        /// <returns>配置好的三表查询对象</returns>
        private ISelect<FsTopic, FsCategory, FsCategoryType> CreateThreeTableQuery(int? topicId = null)
        {
            var query = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id);

            return topicId.HasValue
                ? query.Where((a, b, c) => a.Id == topicId.Value)
                : query.Where((a, b, c) => !a.IsDeleted);
        }
    }
}
