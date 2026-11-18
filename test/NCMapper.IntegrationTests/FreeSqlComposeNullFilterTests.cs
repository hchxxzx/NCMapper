using System.IO;
using Xunit;
using FreeSql;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// Compose 降级路径 null 过滤回归测试
    /// <para>覆盖（路径矩阵全 6 类）：双表/三表 Compose 降级（ConvertUsing 触发）时，Left/Right/Full Join 产生的 null 实体行被过滤，
    /// 且 MapTracer 告知"降级路径过滤了 N 条含 null 实体的行"或 First 分支"遇到含 null 实体的行"。
    /// 各入口（ToList / ToListAsync / First / FirstAsync）的过滤逻辑独立存在，本类逐一覆盖。</para>
    /// <para>数据：FreeSqlNullFilterFixture（独立 SQLite）植入 6 Topic，其中 Id=6 orphan（CategoryId=999 无匹配 Category）
    /// 在双表 LeftJoin 产生 null T2 行；Id=5 IsDeleted=true。Active 5 条中仅 orphan 产生 null T2。</para>
    /// </summary>
    [Collection("MapTracerGlobalState")]
    public class FreeSqlComposeNullFilterTests : IClassFixture<FreeSqlNullFilterFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造 — 从 NT-001 提供的独立 Fixture 获取 FreeSql 实例
        /// </summary>
        /// <param name="fixture">null 过滤专用 Fixture</param>
        public FreeSqlComposeNullFilterTests(FreeSqlNullFilterFixture fixture) => _fsql = fixture.Fsql;

        /// <summary>
        /// 双表 Compose 降级 — null 过滤移除 orphan（产生 null T2 的行）。
        /// 验证：DB 共 6 Topic / Active 5；降级后结果 4（orphan 被过滤）；Trace 告知"过滤了 1 条含 null 实体的行"。
        /// </summary>
        [Fact]
        public void NT002_双表降级_null过滤移除orphan且Trace告知()
        {
            // 基线确认：orphan 确实存在于库且为 Active，排除"被 Where 过滤"的干扰
            Assert.Equal(6, _fsql.Select<FsTopic>().Count());
            Assert.Equal(5, _fsql.Select<FsTopic>().Where(t => !t.IsDeleted).Count());

            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = _fsql.Select<FsTopic, FsCategory>()
                    .LeftJoin((a, b) => a.CategoryId == b.Id)
                    .Where((a, b) => !a.IsDeleted)
                    .OrderBy((a, b) => a.Id)
                    .AsDto()
                    .ToList<FsTopicFullConvDto>();

                // Active 5 条中仅 orphan(Id=6) 产生 null T2 → 过滤后剩 4 条
                Assert.Equal(4, dtos.Count);
                Assert.DoesNotContain(dtos, d => d.Id == 6);
                // 结果均含有效 T1 主键，无 null 实体
                Assert.All(dtos, d => Assert.True(d.Id > 0));
                // 过滤行为 + Trace 告知
                var output = sw.ToString();
                Assert.Contains("降级路径过滤了 1 条含 null 实体的行", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// 三表 Compose 降级 — null 过滤移除 orphan（产生 null T2 的行，T3 过滤同为 true 不影响结论）。
        /// 验证：降级后结果 4（orphan 被过滤）；Trace 告知"过滤了 1 条含 null 实体的行"。
        /// </summary>
        [Fact]
        public void NT002_三表降级_null过滤移除orphan且Trace告知()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                    .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                    .LeftJoin((a, b, c) => b.ParentId == c.Id)
                    .Where((a, b, c) => !a.IsDeleted)
                    .OrderBy((a, b, c) => a.Id)
                    .AsDto()
                    .ToList<FsTopicFullConvDto>();

                Assert.Equal(4, dtos.Count);
                Assert.DoesNotContain(dtos, d => d.Id == 6);
                Assert.All(dtos, d => Assert.True(d.Id > 0));

                var output = sw.ToString();
                Assert.Contains("降级路径过滤了 1 条含 null 实体的行", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// 双表 Compose 降级 — First 命中 orphan（null T2）时返回 null 且 Trace 告知。
        /// 覆盖路径矩阵"双表 First"分支（NCMapperFreeSqlExtensions.cs:412-432）。
        /// </summary>
        [Fact]
        public void NT002_双表降级_First命中orphan返回null且Trace告知()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dto = _fsql.Select<FsTopic, FsCategory>()
                    .LeftJoin((a, b) => a.CategoryId == b.Id)
                    .Where((a, b) => a.Id == 6)  // 精准锁定 orphan（CategoryId=999 产生 null T2）
                    .OrderBy((a, b) => a.Id)
                    .AsDto()
                    .First<FsTopicFullConvDto>();

                // orphan 产生 null T2 → 降级 First 分支返回 null + Trace
                Assert.Null(dto);
                var output = sw.ToString();
                Assert.Contains("降级路径 First 遇到含 null 实体的行", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// 三表 Compose 降级 — First 命中 orphan（null T2）时返回 null 且 Trace 告知。
        /// 覆盖路径矩阵"三表 First"分支（NCMapperFreeSqlExtensions.cs:696-716）。
        /// </summary>
        [Fact]
        public void NT002_三表降级_First命中orphan返回null且Trace告知()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dto = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                    .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                    .LeftJoin((a, b, c) => b.ParentId == c.Id)
                    .Where((a, b, c) => a.Id == 6)
                    .OrderBy((a, b, c) => a.Id)
                    .AsDto()
                    .First<FsTopicFullConvDto>();

                Assert.Null(dto);
                var output = sw.ToString();
                Assert.Contains("降级路径 First 遇到含 null 实体的行", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// 双表 Compose 降级 — ToListAsync 异步路径 null 过滤与 Trace 告知。
        /// 覆盖路径矩阵"双表 ToListAsync"分支（NCMapperFreeSqlExtensions.cs:387-406）。
        /// </summary>
        [Fact]
        public async Task NT002_双表降级_ToListAsync_null过滤()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = await _fsql.Select<FsTopic, FsCategory>()
                    .LeftJoin((a, b) => a.CategoryId == b.Id)
                    .Where((a, b) => !a.IsDeleted)
                    .OrderBy((a, b) => a.Id)
                    .AsDto()
                    .ToListAsync<FsTopicFullConvDto>(cancellationToken: TestContext.Current.CancellationToken);

                Assert.Equal(4, dtos.Count);
                Assert.DoesNotContain(dtos, d => d.Id == 6);
                Assert.All(dtos, d => Assert.True(d.Id > 0));
                var output = sw.ToString();
                Assert.Contains("降级路径过滤了 1 条含 null 实体的行", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// 三表 Compose 降级 — ToListAsync 异步路径 null 过滤与 Trace 告知。
        /// 覆盖路径矩阵"三表 ToListAsync"分支（NCMapperFreeSqlExtensions.cs:671-690）。
        /// </summary>
        [Fact]
        public async Task NT002_三表降级_ToListAsync_null过滤()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = await _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                    .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                    .LeftJoin((a, b, c) => b.ParentId == c.Id)
                    .Where((a, b, c) => !a.IsDeleted)
                    .OrderBy((a, b, c) => a.Id)
                    .AsDto()
                    .ToListAsync<FsTopicFullConvDto>(cancellationToken: TestContext.Current.CancellationToken);

                Assert.Equal(4, dtos.Count);
                Assert.DoesNotContain(dtos, d => d.Id == 6);
                Assert.All(dtos, d => Assert.True(d.Id > 0));
                var output = sw.ToString();
                Assert.Contains("降级路径过滤了 1 条含 null 实体的行", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }
        /// <summary>
        /// 回归降级路径 ToChunkDto 终止条件改用过滤前 tuples.Count
        /// null 实体行位于中间页时，原实现（validTuples.Count < size 提前 break）静默丢失后续页数据。
        /// 数据：6 Topic（5 有效，Id=6 orphan 产生 null T2）；OrderByDescending 使 orphan 位于第 1 页；
        /// size=2 → 页1=(6,null)+(4) 过滤后 1 条 → 原实现 break 仅收 1 条；修复后完整 4 条（4,3,2,1）。
        /// </summary>
        [Fact]
        public void H01_降级ToChunkDto_中间页null行_不丢后续页()
        {
            var chunkResults = new List<FsTopicFullConvDto>();

            _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderByDescending((a, b) => a.Id)
                .AsDto()
                .ToChunkDto<FsTopicFullConvDto>(2, chunk => chunkResults.AddRange(chunk));

            Assert.Equal(4, chunkResults.Count); // 修复前仅 1 条（Id=4）
            Assert.Equal(new[] { 4, 3, 2, 1 }, chunkResults.Select(d => d.Id).ToArray());
        }

        /// <summary>
        /// 回归（三表版）：同双表场景，FilterValid3 过滤 null 行后
        /// 中间页提前触底的丢页缺陷；OrderByDescending 使 orphan 位于第 1 页。
        /// </summary>
        [Fact]
        public void H01_三表降级ToChunkDto_中间页null行_不丢后续页()
        {
            var chunkResults = new List<FsTopicFullConvDto>();

            _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderByDescending((a, b, c) => a.Id)
                .AsDto()
                .ToChunkDto<FsTopicFullConvDto>(2, chunk => chunkResults.AddRange(chunk));

            Assert.Equal(4, chunkResults.Count);
            Assert.Equal(new[] { 4, 3, 2, 1 }, chunkResults.Select(d => d.Id).ToArray());
        }
    }
}
