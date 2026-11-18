using Xunit;
using FreeSql;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// Compose 多表分页 + ToChunkDto 行为测试
    /// <para>覆盖：双表/三表 ToPagedList + ToPagedListAsync（投影/降级）、参数校验、空结果集、多表 ToChunkDto 行为约束</para>
    /// <para>数据基线（共享 FreeSqlTestFixture）：5 条 Topic，其中 4 条 IsDeleted=false（Id 1-4）</para>
    /// </summary>
    public class FreeSqlComposePagingTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造 — 从共享 Fixture 获取 FreeSql 实例
        /// </summary>
        /// <param name="fixture">共享测试 Fixture</param>
        public FreeSqlComposePagingTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // NT-004 双表分页
        // ================================================================

        /// <summary>
        /// 双表投影路径分页 — FsTopicCategoryDto 无 ConvertUsing，走 SQL 投影。
        /// 验证：第 1 页切片（Id 1,2）+ TotalCount=4 + Page/PageSize 回填 + 跨表字段值
        /// </summary>
        [Fact]
        public void NT004_双表投影分页_第一页切片与TotalCount正确()
        {
            var paged = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToPagedList<FsTopicCategoryDto>(1, 2);

            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(4, paged.TotalCount);
            Assert.Equal(1, paged.Page);
            Assert.Equal(2, paged.PageSize);
            // 第 1 页 = Id 1,2（SQL 侧 OrderBy Id）
            Assert.Equal("NCMapper 入门", paged.Items[0].Title);
            Assert.Equal(".NET", paged.Items[0].CategoryName);
            Assert.Equal("FreeSql 实战", paged.Items[1].Title);
            Assert.Equal(".NET", paged.Items[1].CategoryName);
        }

        /// <summary>
        /// 双表投影路径分页 — 第 2 页切片（Id 3,4），验证 Page 参数真实生效
        /// </summary>
        [Fact]
        public void NT004_双表投影分页_第二页切片正确()
        {
            var paged = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToPagedList<FsTopicCategoryDto>(2, 2);

            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(4, paged.TotalCount);
            Assert.Equal(2, paged.Page);
            // 第 2 页 = Id 3,4
            Assert.Equal("Python 爬虫", paged.Items[0].Title);
            Assert.Equal("Python", paged.Items[0].CategoryName);
            Assert.Equal("旅行攻略", paged.Items[1].Title);
            Assert.Equal("旅游", paged.Items[1].CategoryName);
        }

        /// <summary>
        /// 双表降级路径分页 — FsTopicFullConvDto 含 ConvertUsing 触发降级。
        /// 验证：切片行数 + TotalCount + ConvertUsing 生效（"[Conv]"前缀证明确实走了降级两步映射）
        /// </summary>
        [Fact]
        public void NT004_双表降级分页_切片与ConvertUsing正确()
        {
            var paged = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToPagedList<FsTopicFullConvDto>(1, 2);

            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(4, paged.TotalCount);
            Assert.Equal(1, paged.Page);
            Assert.Equal(2, paged.PageSize);
            Assert.Equal("NCMapper 入门", paged.Items[0].Title);
            // ConvertUsing 生效 = 确实走了降级路径（投影路径不会执行 Converter）
            Assert.Equal("[Conv]内容1", paged.Items[0].Content);
            Assert.Equal(".NET", paged.Items[0].CategoryName);
            Assert.Equal("[Conv]内容2", paged.Items[1].Content);
        }

        /// <summary>
        /// 双表异步分页 — 投影路径，断言与同步一致
        /// </summary>
        [Fact]
        public async Task NT004_双表异步分页_投影路径与同步一致()
        {
            var paged = await _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToPagedListAsync<FsTopicCategoryDto>(1, 3, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, paged.Items.Count);
            Assert.Equal(4, paged.TotalCount);
            Assert.Equal(1, paged.Page);
            Assert.Equal(3, paged.PageSize);
            Assert.Equal("NCMapper 入门", paged.Items[0].Title);
            Assert.Equal("Python 爬虫", paged.Items[2].Title);
        }

        /// <summary>
        /// 双表参数校验 — page&lt;1 / pageSize&lt;1 抛 ArgumentOutOfRangeException
        /// </summary>
        [Fact]
        public void NT004_双表参数校验_非法page或pageSize抛异常()
        {
            var selector = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .AsDto();

            var exPage = Assert.Throws<ArgumentOutOfRangeException>(
                () => selector.ToPagedList<FsTopicCategoryDto>(0, 2));
            Assert.Equal("page", exPage.ParamName);

            var exSize = Assert.Throws<ArgumentOutOfRangeException>(
                () => selector.ToPagedList<FsTopicCategoryDto>(1, 0));
            Assert.Equal("pageSize", exSize.ParamName);
        }

        /// <summary>
        /// 双表空结果集 — Where 无命中时 Items 空 + TotalCount=0
        /// </summary>
        [Fact]
        public void NT004_双表空结果集_Items空且TotalCount为零()
        {
            var paged = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => a.Id == 999)
                .AsDto()
                .ToPagedList<FsTopicCategoryDto>(1, 10);

            Assert.Empty(paged.Items);
            Assert.Equal(0, paged.TotalCount);
            Assert.Equal(1, paged.Page);
            Assert.Equal(10, paged.PageSize);
        }

        // ================================================================
        // NT-004 三表分页
        // ================================================================

        /// <summary>
        /// 三表投影路径分页 — FsTopicFullDto 无 ConvertUsing。
        /// 验证：切片 + TotalCount + 三表字段（CategoryName 来自 T2、CategoryTypeName 来自 T3）
        /// </summary>
        [Fact]
        public void NT004_三表投影分页_切片与三表字段正确()
        {
            var paged = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToPagedList<FsTopicFullDto>(1, 2);

            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(4, paged.TotalCount);
            Assert.Equal(1, paged.Page);
            Assert.Equal(2, paged.PageSize);
            // Topic1 → Category ".NET" → CategoryType "技术"
            Assert.Equal("NCMapper 入门", paged.Items[0].Title);
            Assert.Equal(".NET", paged.Items[0].CategoryName);
            Assert.Equal("技术", paged.Items[0].CategoryTypeName);
        }

        /// <summary>
        /// 三表降级路径分页 — FsTopicFullConvDto 含 ConvertUsing。
        /// 验证：第 2 页切片 + ConvertUsing 生效 + 三表字段
        /// </summary>
        [Fact]
        public void NT004_三表降级分页_第二页切片与ConvertUsing正确()
        {
            var paged = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToPagedList<FsTopicFullConvDto>(2, 2);

            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(4, paged.TotalCount);
            Assert.Equal(2, paged.Page);
            // 第 2 页 = Id 3,4
            Assert.Equal("Python 爬虫", paged.Items[0].Title);
            Assert.Equal("[Conv]内容3", paged.Items[0].Content);
            Assert.Equal("Python", paged.Items[0].CategoryName);
            Assert.Equal("技术", paged.Items[0].CategoryTypeName);
            Assert.Equal("旅行攻略", paged.Items[1].Title);
            Assert.Equal("生活", paged.Items[1].CategoryTypeName);
        }

        /// <summary>
        /// 三表异步分页 — 降级路径（ConvertUsing），断言与同步一致
        /// </summary>
        [Fact]
        public async Task NT004_三表异步分页_降级路径与同步一致()
        {
            var paged = await _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToPagedListAsync<FsTopicFullConvDto>(1, 2, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, paged.Items.Count);
            Assert.Equal(4, paged.TotalCount);
            Assert.Equal("NCMapper 入门", paged.Items[0].Title);
            Assert.Equal("[Conv]内容1", paged.Items[0].Content);
            Assert.Equal(".NET", paged.Items[0].CategoryName);
            Assert.Equal("技术", paged.Items[0].CategoryTypeName);
        }

        /// <summary>
        /// 三表参数校验 — page&lt;1 / pageSize&lt;1 抛 ArgumentOutOfRangeException
        /// </summary>
        [Fact]
        public void NT004_三表参数校验_非法page或pageSize抛异常()
        {
            var selector = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .AsDto();

            var exPage = Assert.Throws<ArgumentOutOfRangeException>(
                () => selector.ToPagedList<FsTopicFullDto>(0, 2));
            Assert.Equal("page", exPage.ParamName);

            var exSize = Assert.Throws<ArgumentOutOfRangeException>(
                () => selector.ToPagedList<FsTopicFullDto>(1, -1));
            Assert.Equal("pageSize", exSize.ParamName);
        }

        // ================================================================
        // NT-005 多表 ToChunkDto 行为约束
        // ================================================================

        /// <summary>
        /// 双表 ToChunkDto — Page+ToList 分轮实现完整 Compose 映射。
        /// 验证：累计 4 条 + 回调 ceil(4/2)=2 次 + Title/Content（T1，含 ConvertUsing）正确
        /// + CategoryName（T2.Category.Name 经 Compose 直接取值，非 null）。
        /// 说明：修复后，T2 字段经 Compose(T1, T2) 直接从第二源类型取值，不再依赖 T1 导航属性。
        /// CategoryTypeName（T3）在本双表场景不可达，保持 null。
        /// </summary>
        [Fact]
        public void NT005_双表ToChunkDto_所有跨表字段正确填充()
        {
            var all = new List<FsTopicFullConvDto>();
            int callbackCount = 0;

            _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToChunkDto<FsTopicFullConvDto>(2, chunk =>
                {
                    callbackCount++;
                    all.AddRange(chunk);
                });

            Assert.Equal(2, callbackCount); // ceil(4/2) = 2
            Assert.Equal(4, all.Count);
            // T1 字段正确填充（含 ConvertUsing 单源映射生效）
            Assert.Equal("NCMapper 入门", all[0].Title);
            Assert.Equal("[Conv]内容1", all[0].Content);
            // CategoryName 经 FsTopic.Category 导航类型匹配填充（按 Id 升序的预期分类名）
            var expectedCategoryNames = new[] { ".NET", ".NET", "Python", "旅游" };
            Assert.Equal(expectedCategoryNames, all.Select(d => d.CategoryName).ToArray());
            // T3 字段在本双表场景不可达，保持 null（降级路径仅 T1 源，无 FsCategoryType 导航）
            Assert.All(all, dto => Assert.Null(dto.CategoryTypeName));
        }

        /// <summary>
        /// 三表 ToChunkDto — Page+ToList 分轮实现完整 Compose 映射。
        /// 验证：累计 4 条 + 回调 ceil(4/3)=2 次 + Title/Content（T1，含 ConvertUsing）正确
        /// + CategoryName（T2 经 Compose 直接取值）+ CategoryTypeName（T3 经 Compose 直接取值）。
        /// 说明：修复后，T2、T3 字段经 Compose(T1, T2, T3) 分别从第二、第三源类型直接取值，不再依赖 T1 导航属性。
        /// </summary>
        [Fact]
        public void NT005_三表ToChunkDto_T2T3字段均正确填充()
        {
            var all = new List<FsTopicFullConvDto>();
            int callbackCount = 0;

            _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToChunkDto<FsTopicFullConvDto>(3, chunk =>
                {
                    callbackCount++;
                    all.AddRange(chunk);
                });

            Assert.Equal(2, callbackCount); // ceil(4/3) = 2
            Assert.Equal(4, all.Count);
            Assert.Equal("NCMapper 入门", all[0].Title);
            Assert.Equal("[Conv]内容1", all[0].Content);
            // CategoryName 经 Compose(T1, T2) 从第二源类型直接取值（预期分类名）
            var expectedCategoryNames = new[] { ".NET", ".NET", "Python", "旅游" };
            Assert.Equal(expectedCategoryNames, all.Select(d => d.CategoryName).ToArray());
            // CategoryTypeName 经 Compose(T1, T2, T3) 从第三源类型直接取值
            // Category 的 ParentId 关联 CategoryType：Id=1→"技术", Id=3→"生活"
            var expectedCategoryTypeNames = new[] { "技术", "技术", "技术", "生活" };
            Assert.Equal(expectedCategoryTypeNames, all.Select(d => d.CategoryTypeName).ToArray());
        }
    }
}
