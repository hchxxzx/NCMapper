using Xunit;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql 边界与异常集成测试 — 空结果集、大量数据烟雾
    /// <para>覆盖 T3-01/02/03</para>
    /// </summary>
    public class FreeSqlEdgeCaseTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="fixture">共享测试 Fixture</param>
        public FreeSqlEdgeCaseTests(FreeSqlTestFixture fixture)
        {
            _fsql = fixture.Fsql;
        }

        // ================================================================
        // T3-01：空结果集 — 单表
        // ================================================================

        /// <summary>
        /// T3-01：空结果集 — ToList 返回空 List，First 返回 null，ToPagedList TotalCount=0
        /// </summary>
        [Fact]
        public async Task T3_01_空结果集_单表()
        {
            // Arrange — 查询不存在的 Id
            var query = _fsql.Select<FsTopic>().Where(a => a.Id == 999);

            // Act & Assert — ToList 正常路径
            var list = query.AsDto().ToList<FsTopicFullDto>();
            Assert.NotNull(list);
            Assert.Empty(list);

            // Act & Assert — ToList 降级路径（FsTopicDto 含 ConvertUsing）
            var degradeList = query.AsDto().ToList<FsTopicDto>();
            Assert.NotNull(degradeList);
            Assert.Empty(degradeList);

            // Act & Assert — First 正常路径
            var first = query.AsDto().First<FsTopicFullDto>();
            Assert.Null(first);

            // Act & Assert — First 降级路径
            var degradeFirst = query.AsDto().First<FsTopicDto>();
            Assert.Null(degradeFirst);

            // Act & Assert — ToPagedList 正常路径
            var paged = query.AsDto().ToPagedList<FsTopicFullDto>(1, 10);
            Assert.NotNull(paged);
            Assert.Equal(0, paged.TotalCount);
            Assert.Empty(paged.Items);

            // Act & Assert — ToPagedList 降级路径
            var degradePaged = query.AsDto().ToPagedList<FsTopicDto>(1, 10);
            Assert.Equal(0, degradePaged.TotalCount);
            Assert.Empty(degradePaged.Items);

            // Act & Assert — ToListAsync
            var asyncList = await query.AsDto().ToListAsync<FsTopicDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Empty(asyncList);

            // Act & Assert — FirstAsync
            var asyncFirst = await query.AsDto().FirstAsync<FsTopicDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(asyncFirst);
        }

        // ================================================================
        // T3-02：空结果集 — 三表 Compose
        // ================================================================

        /// <summary>
        /// T3-02：三表 Compose 空结果 — First 返回 null，ToList 返回空 List
        /// </summary>
        [Fact]
        public async Task T3_02_空结果集_三表Compose()
        {
            // Arrange
            var query = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => a.Id == 999);

            // Act & Assert — First 正常路径
            var first = query.AsDto().First<FsTopicFullDto>();
            Assert.Null(first);

            // Act & Assert — ToList 正常路径
            var list = query.AsDto().ToList<FsTopicFullDto>();
            Assert.Empty(list);

            // Act & Assert — FirstAsync
            var asyncFirst = await query.AsDto().FirstAsync<FsTopicFullDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(asyncFirst);

            // Act & Assert — 降级路径
            var degradeFirst = query.AsDto().First<FsTopicFullConvDto>();
            Assert.Null(degradeFirst);

            var degradeList = query.AsDto().ToList<FsTopicFullConvDto>();
            Assert.Empty(degradeList);
        }

        // ================================================================
        // T3-03：大量数据烟雾测试
        // ================================================================

        /// <summary>
        /// T3-03：大量数据烟雾 — 1000 Topic + 1000 Comment，降级 ToListAsync &lt; 5s
        /// </summary>
        [Fact]
        public async Task T3_03_大量数据烟雾测试()
        {
            // Arrange — 批量插入大量数据（使用 100000+ 范围避免与种子数据 Id 1-5 冲突）
            var topics = new List<FsTopic>();
            var comments = new List<FsComment>();
            for (int i = 100000; i < 101000; i++)
            {
                topics.Add(new FsTopic
                {
                    Id = i,
                    Title = $"SmokeTest_{i}",
                    Content = $"Content_{i}",
                    Clicks = i,
                    CreateTime = DateTime.Now,
                    CategoryId = 1,
                    IsDeleted = false
                });
                comments.Add(new FsComment
                {
                    Id = i,
                    TopicId = i,
                    Body = $"Comment_{i}",
                    IsDeleted = false,
                    IsApproved = true,
                    CreateTime = DateTime.Now
                });
            }
            _fsql.Insert(topics).ExecuteAffrows();
            _fsql.Insert(comments).ExecuteAffrows();

            try
            {
                // Act — 降级路径查询全部
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var dtos = await _fsql.Select<FsTopic>()
                    .Where(a => !a.IsDeleted)
                    .AsDto()
                    .ToListAsync<FsTopicDto>(cancellationToken: TestContext.Current.CancellationToken);
                sw.Stop();

                // Assert — 数据完整
                Assert.True(dtos.Count >= 1004, $"预期 >=1004 条，实际 {dtos.Count}");

                // Assert — 5s 内完成
                Assert.True(sw.Elapsed.TotalSeconds < 5,
                    $"烟雾测试超时：{sw.Elapsed.TotalSeconds:F1}s");
            }
            finally
            {
                // 清理烟雾数据（使用 Id >= 100000 确保不影响种子数据 Id 1-5）
                _fsql.Delete<FsTopic>().Where(a => a.Id >= 100000).ExecuteAffrows();
                _fsql.Delete<FsComment>().Where(a => a.Id >= 100000).ExecuteAffrows();
            }
        }
    }
}
