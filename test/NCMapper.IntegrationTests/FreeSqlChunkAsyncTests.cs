using Xunit;
using FreeSql;
using NCMapper.Attributes;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql ToChunkDtoAsync / ToLookup 集成测试
    /// <para>覆盖：投影/降级双分支真异步分块（单表/Compose 双表/三表）、已取消令牌传播（X4）、
    /// 执行中取消后续页不再取数、跨页完整性不漏行不重行（A7）、ToLookup 分组行为</para>
    /// <para>真连库 Provider（FreeSql.Provider.Sqlite，仅本工程引用）——行为断言不落 NCMapper.Tests</para>
    /// </summary>
    public class FreeSqlChunkAsyncTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        public FreeSqlChunkAsyncTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // 测试专用 DTO（单表投影/降级 + Compose 双表降级）
        // ================================================================

        /// <summary>简单 DTO — 走投影路径</summary>
        [MapFrom(typeof(FsTopic))]
        public class ChunkAsyncDto
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public int Clicks { get; set; }
        }

        /// <summary>含 ConvertUsing 的 DTO — 走降级路径</summary>
        [MapFrom(typeof(FsTopic))]
        public class ChunkAsyncConvDto
        {
            public int Id { get; set; }
            public string? Title { get; set; }

            [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
            public string? Content { get; set; }
        }

        /// <summary>Compose 双表降级 DTO — ConvertUsing 触发 NeedsTwoStepMapping</summary>
        [MapFrom(typeof(FsTopic))]
        public class ComposeCategoryConvDto
        {
            public int Id { get; set; }
            public string? Title { get; set; }

            [Convert(typeof(NoOpTopicConverter), nameof(NoOpTopicConverter.Convert))]
            public string? Content { get; set; }

            [MapProperty(typeof(FsCategory), nameof(FsCategory.Name))]
            public string? CategoryName { get; set; }
        }

        // ================================================================
        // 单表 ToChunkDtoAsync — 投影/降级/契约/空表
        // ================================================================

        /// <summary>
        /// 投影路径分块正确 — 5 条数据 size=2 → 3 次回调，总数据完整
        /// </summary>
        [Fact]
        public async Task ToChunkDtoAsync_投影路径_分块正确()
        {
            var allDtos = new List<ChunkAsyncDto>();
            int callbackCount = 0;

            await _fsql.Select<FsTopic>()
                .OrderBy(t => t.Id)
                .AsDto()
                .ToChunkDtoAsync<ChunkAsyncDto>(2, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                    return Task.CompletedTask;
                });

            Assert.Equal(3, callbackCount); // ceil(5/2) = 3
            Assert.Equal(5, allDtos.Count);
            Assert.Equal("NCMapper 入门", allDtos[0].Title);
            Assert.Equal(100, allDtos[0].Clicks);
        }

        /// <summary>
        /// 降级路径分块正确 — ConvertUsing DTO 逐页查实体 → MapList
        /// </summary>
        [Fact]
        public async Task ToChunkDtoAsync_降级路径_分块正确()
        {
            var allDtos = new List<ChunkAsyncConvDto>();
            int callbackCount = 0;

            await _fsql.Select<FsTopic>()
                .OrderBy(t => t.Id)
                .AsDto()
                .ToChunkDtoAsync<ChunkAsyncConvDto>(2, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                    return Task.CompletedTask;
                });

            Assert.Equal(3, callbackCount); // ceil(5/2) = 3
            Assert.Equal(5, allDtos.Count);
            // ConvertUsing 生效（降级路径 MapList 保留转换语义）
            Assert.Equal("[Conv]内容1", allDtos[0].Content);
        }

        /// <summary>
        /// 参数校验 — size &lt; 1 抛 ArgumentOutOfRangeException（与同步版契约一致）
        /// </summary>
        [Fact]
        public async Task ToChunkDtoAsync_参数校验_Size小于1抛异常()
        {
            var selector = _fsql.Select<FsTopic>().AsDto();

            var ex0 = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => selector.ToChunkDtoAsync<ChunkAsyncDto>(0, _ => Task.CompletedTask));
            Assert.Equal("size", ex0.ParamName);

            var exNeg = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => selector.ToChunkDtoAsync<ChunkAsyncDto>(-1, _ => Task.CompletedTask));
            Assert.Equal("size", exNeg.ParamName);
        }

        /// <summary>
        /// 空表 — 0 次回调
        /// </summary>
        [Fact]
        public async Task ToChunkDtoAsync_空表零次回调()
        {
            int callbackCount = 0;

            await _fsql.Select<FsTopic>()
                .Where(t => t.Id == 999)
                .AsDto()
                .ToChunkDtoAsync<ChunkAsyncDto>(2, _ =>
                {
                    callbackCount++;
                    return Task.CompletedTask;
                });

            Assert.Equal(0, callbackCount);
        }

        // ================================================================
        // X4 红线 — CancellationToken 取消传播
        // ================================================================

        /// <summary>
        /// 已取消令牌 — 首页取数前抛 OperationCanceledException，零回调
        /// </summary>
        [Fact]
        public async Task ToChunkDtoAsync_已取消令牌_首页取数前抛OCE()
        {
            int callbackCount = 0;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _fsql.Select<FsTopic>()
                    .OrderBy(t => t.Id)
                    .AsDto()
                    .ToChunkDtoAsync<ChunkAsyncDto>(2, _ =>
                    {
                        callbackCount++;
                        return Task.CompletedTask;
                    }, cts.Token);
            });

            Assert.Equal(0, callbackCount);
        }

        /// <summary>
        /// 执行中取消 — 首页回调后取消，下一轮取数前终止（逐轮取消检查在位）
        /// </summary>
        [Fact]
        public async Task ToChunkDtoAsync_执行中取消_后续页不再取数()
        {
            int callbackCount = 0;

            using var cts = new CancellationTokenSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _fsql.Select<FsTopic>()
                    .OrderBy(t => t.Id)
                    .AsDto()
                    .ToChunkDtoAsync<ChunkAsyncDto>(1, _ =>
                    {
                        callbackCount++;
                        if (callbackCount == 1) cts.Cancel(); // 首条回调后取消
                        return Task.CompletedTask;
                    }, cts.Token);
            });

            Assert.Equal(1, callbackCount); // 后续页不再触发回调
        }

        // ================================================================
        // A7 — 跨页完整性（带 OrderBy 不漏行不重行）
        // ================================================================

        /// <summary>
        /// 跨页完整性 — OrderBy(Id) + size=2 逐页取数，Id 集合与全集一致且无重复
        /// </summary>
        [Fact]
        public async Task ToChunkDtoAsync_跨页完整性_不漏行不重行()
        {
            var ids = new List<int>();

            await _fsql.Select<FsTopic>()
                .OrderBy(t => t.Id)
                .AsDto()
                .ToChunkDtoAsync<ChunkAsyncDto>(2, chunk =>
                {
                    ids.AddRange(chunk.Select(d => d.Id));
                    return Task.CompletedTask;
                });

            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, ids); // 全量且有序
            Assert.Equal(5, ids.Distinct().Count());    // 无重行
        }

        // ================================================================
        // ToLookup — 分组行为
        // ================================================================

        /// <summary>
        /// ToLookup 分组正确 — 按 CategoryId 分组（种子 1/1/2/3/1 → 3 组），每组 MapList 映射
        /// </summary>
        [Fact]
        public void ToLookup_分组映射正确()
        {
            var lookup = _fsql.Select<FsTopic>()
                .OrderBy(t => t.Id)
                .AsDto()
                .ToLookup<int, ChunkAsyncDto>(t => t.CategoryId);

            Assert.Equal(3, lookup.Count); // CategoryId: 1, 2, 3
            Assert.Equal(3, lookup[1].Count()); // CategoryId=1 → Topic 1/2/5
            Assert.Equal(1, lookup[2].Count());
            Assert.Equal(1, lookup[3].Count());
            // 组内映射正确（MapList 生效）
            var group1 = lookup[1].OrderBy(d => d.Id).ToList();
            Assert.Equal(1, group1[0].Id);
            Assert.Equal("NCMapper 入门", group1[0].Title);
            Assert.Equal(5, group1[2].Id);
        }

        /// <summary>
        /// ToLookup 键不存在 — 索引器返回空序列（ILookup 语义）
        /// </summary>
        [Fact]
        public void ToLookup_键不存在返回空序列()
        {
            var lookup = _fsql.Select<FsTopic>()
                .AsDto()
                .ToLookup<int, ChunkAsyncDto>(t => t.CategoryId);

            Assert.Empty(lookup[999]);
        }

        // ================================================================
        // Compose 双表/三表 ToChunkDtoAsync
        // ================================================================

        /// <summary>
        /// 双表 Compose 投影路径 — 4 条（排除软删）size=3 → 2 次回调
        /// </summary>
        [Fact]
        public async Task Compose双表_ToChunkDtoAsync_投影路径()
        {
            var allDtos = new List<FsTopicCategoryDto>();
            int callbackCount = 0;

            await _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToChunkDtoAsync<FsTopicCategoryDto>(3, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                    return Task.CompletedTask;
                });

            Assert.Equal(2, callbackCount); // ceil(4/3) = 2
            Assert.Equal(4, allDtos.Count);
            var first = allDtos.OrderBy(d => d.Id).First();
            Assert.Equal("NCMapper 入门", first.Title);
            Assert.Equal(".NET", first.CategoryName);
        }

        /// <summary>
        /// 双表 Compose 降级路径 — ConvertUsing 触发，逐页取元组 → 过滤 → Compose
        /// </summary>
        [Fact]
        public async Task Compose双表_ToChunkDtoAsync_降级路径()
        {
            var allDtos = new List<ComposeCategoryConvDto>();
            int callbackCount = 0;

            await _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToChunkDtoAsync<ComposeCategoryConvDto>(3, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                    return Task.CompletedTask;
                });

            Assert.Equal(2, callbackCount);
            Assert.Equal(4, allDtos.Count);
            var first = allDtos.OrderBy(d => d.Id).First();
            Assert.Equal("[Conv]内容1", first.Content);
            Assert.Equal(".NET", first.CategoryName);
        }

        /// <summary>
        /// 三表 Compose 投影路径 — 4 条 size=3 → 2 次回调，三表字段均映射
        /// </summary>
        [Fact]
        public async Task Compose三表_ToChunkDtoAsync_投影路径()
        {
            var allDtos = new List<FsTopicFullDto>();
            int callbackCount = 0;

            await _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToChunkDtoAsync<FsTopicFullDto>(3, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                    return Task.CompletedTask;
                });

            Assert.Equal(2, callbackCount);
            Assert.Equal(4, allDtos.Count);
            var first = allDtos.OrderBy(d => d.Id).First();
            Assert.Equal("NCMapper 入门", first.Title);
            Assert.Equal(".NET", first.CategoryName);
            Assert.Equal("技术", first.CategoryTypeName);
        }

        /// <summary>
        /// 三表 Compose 降级路径 — ConvertUsing 触发，三表字段均映射
        /// </summary>
        [Fact]
        public async Task Compose三表_ToChunkDtoAsync_降级路径()
        {
            var allDtos = new List<FsTopicFullConvDto>();
            int callbackCount = 0;

            await _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToChunkDtoAsync<FsTopicFullConvDto>(3, chunk =>
                {
                    callbackCount++;
                    allDtos.AddRange(chunk);
                    return Task.CompletedTask;
                });

            Assert.Equal(2, callbackCount);
            Assert.Equal(4, allDtos.Count);
            var first = allDtos.OrderBy(d => d.Id).First();
            Assert.Equal("[Conv]内容1", first.Content);
            Assert.Equal(".NET", first.CategoryName);
            Assert.Equal("技术", first.CategoryTypeName);
        }

        /// <summary>
        /// Compose 已取消令牌 — 取消传播到 Compose 路径（双表）
        /// </summary>
        [Fact]
        public async Task Compose双表_ToChunkDtoAsync_已取消令牌抛OCE()
        {
            int callbackCount = 0;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _fsql.Select<FsTopic, FsCategory>()
                    .LeftJoin((a, b) => a.CategoryId == b.Id)
                    .Where((a, b) => !a.IsDeleted)
                    .AsDto()
                    .ToChunkDtoAsync<FsTopicCategoryDto>(3, _ =>
                    {
                        callbackCount++;
                        return Task.CompletedTask;
                    }, cts.Token);
            });

            Assert.Equal(0, callbackCount);
        }
    }
}
