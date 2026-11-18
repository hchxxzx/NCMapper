using Xunit;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// T1-3: FreeSql ToChunkDto 分块查询测试
    /// 验证分块回调次数、数据正确性、边界条件
    /// </summary>
    public class FreeSqlChunkTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        public FreeSqlChunkTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // DTO 定义
        // ================================================================

        /// <summary>简单 DTO — 走投影路径</summary>
        [MapFrom(typeof(FsTopic))]
        public class ChunkTopicDto
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public int Clicks { get; set; }
        }

        /// <summary>含 ConvertUsing 的 DTO — 走降级路径</summary>
        [MapFrom(typeof(FsTopic))]
        public class ChunkTopicConvDto
        {
            public int Id { get; set; }
            public string? Title { get; set; }

            [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
            public string? Content { get; set; }
        }

        // ================================================================
        // 测试用例
        // ================================================================

        /// <summary>
        /// 基本分块 — 5 条数据分 2 块（size=3），回调 2 次，总数据完整
        /// </summary>
        [Fact]
        public void ToChunkDto_BasicChunk_CallbackCountCorrect()
        {
            var allDtos = new List<ChunkTopicDto>();
            int callbackCount = 0;

            _fsql.Select<FsTopic>()
                .OrderBy(t => t.Id)
                .AsDto()
                .ToChunkDto<ChunkTopicDto>(3, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                });

            Assert.Equal(2, callbackCount); // ceil(5/3) = 2
            Assert.Equal(5, allDtos.Count);
            Assert.Equal("NCMapper 入门", allDtos[0].Title);
            Assert.Equal(100, allDtos[0].Clicks);
        }

        /// <summary>
        /// 分块大小=总数 — 仅回调 1 次
        /// </summary>
        [Fact]
        public void ToChunkDto_ChunkSizeEqualsTotal_SingleCallback()
        {
            var allDtos = new List<ChunkTopicDto>();
            int callbackCount = 0;

            _fsql.Select<FsTopic>()
                .AsDto()
                .ToChunkDto<ChunkTopicDto>(5, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                });

            Assert.Equal(1, callbackCount);
            Assert.Equal(5, allDtos.Count);
        }

        /// <summary>
        /// 分块大小=1 — 每条数据一次回调
        /// </summary>
        [Fact]
        public void ToChunkDto_ChunkSizeOne_CallbackPerRow()
        {
            int callbackCount = 0;
            var allDtos = new List<ChunkTopicDto>();

            _fsql.Select<FsTopic>()
                .AsDto()
                .ToChunkDto<ChunkTopicDto>(1, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                });

            Assert.Equal(5, callbackCount);
            Assert.Equal(5, allDtos.Count);
            // 每次回调仅 1 条（callbackCount == allDtos.Count 证明每次 chunk.Size=1）
        }

        /// <summary>
        /// 空表 — T-F ③ 契约统一：空结果不回调（原 FreeSql ToChunk 空表回调 1 次的差异已消除）
        /// </summary>
        [Fact]
        public void ToChunkDto_EmptyTable_NoCallback()
        {
            int callbackCount = 0;
            int totalItems = 0;

            _fsql.Select<FsTopic>()
                .Where(t => t.Id == 99999)
                .AsDto()
                .ToChunkDto<ChunkTopicDto>(10, chunk =>
                {
                    callbackCount++;
                    totalItems += chunk.Count;
                });

            // T-F ③：空结果不回调（与 EFCore/Dapper 对齐）
            Assert.Equal(0, callbackCount);
            Assert.Equal(0, totalItems);
        }

        /// <summary>
        /// 降级路径分块 — ConvertUsing 触发降级后分块仍正确
        /// </summary>
        [Fact]
        public void ToChunkDto_Degradation_DataCorrect()
        {
            var allDtos = new List<ChunkTopicConvDto>();
            int callbackCount = 0;

            _fsql.Select<FsTopic>()
                .OrderBy(t => t.Id)
                .AsDto()
                .ToChunkDto<ChunkTopicConvDto>(2, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                });

            Assert.Equal(3, callbackCount); // ceil(5/2) = 3
            Assert.Equal(5, allDtos.Count);
            // ConvertUsing 加前缀 [Conv]
            Assert.Equal("[Conv]内容1", allDtos[0].Content);
            Assert.Equal("NCMapper 入门", allDtos[0].Title);
        }

        /// <summary>
        /// 分块大小远大于总数 — 仅回调 1 次
        /// </summary>
        [Fact]
        public void ToChunkDto_ChunkSizeLargerThanTotal_SingleCallback()
        {
            int callbackCount = 0;
            var allDtos = new List<ChunkTopicDto>();

            _fsql.Select<FsTopic>()
                .AsDto()
                .ToChunkDto<ChunkTopicDto>(1000, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                });

            Assert.Equal(1, callbackCount);
            Assert.Equal(5, allDtos.Count);
        }
    }
}
