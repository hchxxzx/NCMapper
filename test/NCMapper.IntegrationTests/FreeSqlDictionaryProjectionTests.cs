// 集成测试：FreeSqlDtoSelector.ToDictionary/Async 带映射转手字典映射
// 覆盖维度：基础字典 / 跨表 / 异步 / 空结果
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql ToDictionary 带映射转手集成测试
    /// <para>复用 PoCTreeFixture + PoCArea/PoCCategory/PoCAreaFlatDto 实体</para>
    /// </summary>
    public class FreeSqlDictionaryProjectionTests : IClassFixture<PoCTreeFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="fixture">PoC 树形测试 Fixture</param>
        public FreeSqlDictionaryProjectionTests(PoCTreeFixture fixture)
        {
            _fsql = fixture.Fsql;
        }

        /// <summary>
        /// T-D01：AsDto().ToDictionary 基础字典映射
        /// </summary>
        [Fact]
        public void ToDictionary_基础字典映射正确()
        {
            var dict = _fsql.Select<PoCArea>().AsDto().ToDictionary<int, PoCAreaFlatDto>(a => a.Id);

            Assert.Equal(5, dict.Count);
            Assert.Equal("中国", dict[1].Name);
            Assert.Equal("北京", dict[2].Name);
            Assert.Equal("东城区", dict[3].Name);
            Assert.Equal("上海", dict[4].Name);
            Assert.Equal("浦东新区", dict[5].Name);
        }

        /// <summary>
        /// T-D02：AsDto().ToDictionary 跨表 CategoryName 自动 Include + 映射
        /// </summary>
        [Fact]
        public void ToDictionary_跨表CategoryName填充()
        {
            // ApplyFreeSqlIncludes 自动 Include Category
            var dict = _fsql.Select<PoCArea>().AsDto().ToDictionary<int, PoCAreaFlatDto>(a => a.Id);

            Assert.Equal(5, dict.Count);
            Assert.Equal("国家", dict[1].CategoryName);
            Assert.Equal("省级", dict[2].CategoryName);
            Assert.Equal("区级", dict[3].CategoryName);
            Assert.Equal("省级", dict[4].CategoryName);
            Assert.Equal("区级", dict[5].CategoryName);
        }

        /// <summary>
        /// T-D03：AsDto().ToDictionary 字符串键类型
        /// </summary>
        [Fact]
        public void ToDictionary_字符串键类型()
        {
            var dict = _fsql.Select<PoCArea>().AsDto().ToDictionary<string, PoCAreaFlatDto>(a => a.Name);

            Assert.Equal(5, dict.Count);
            Assert.Equal(1, dict["中国"].Id);
            Assert.Equal(2, dict["北京"].Id);
        }

        /// <summary>
        /// T-D04：AsDto().ToDictionaryAsync 异步版本行为一致
        /// </summary>
        [Fact]
        public async Task ToDictionaryAsync_异步字典映射正确()
        {
            var dict = await _fsql.Select<PoCArea>().AsDto()
                .ToDictionaryAsync<int, PoCAreaFlatDto>(a => a.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(5, dict.Count);
            Assert.Equal("中国", dict[1].Name);
            Assert.Equal("国家", dict[1].CategoryName);
        }

        /// <summary>
        /// T-D05：AsDto().ToDictionary 空结果集返回空字典
        /// </summary>
        [Fact]
        public void ToDictionary_空结果集_返回空字典()
        {
            var dict = _fsql.Select<PoCArea>()
                .Where(a => a.Id < 0)
                .AsDto()
                .ToDictionary<int, PoCAreaFlatDto>(a => a.Id);

            Assert.NotNull(dict);
            Assert.Empty(dict);
        }

        /// <summary>
        /// T-D06：AsDto().ToDictionary 跨表 + 异步 + 取消令牌
        /// <para>FreeSql 取消令牌抛出 Exception（内层 TaskCanceledException），非 OperationCanceledException</para>
        /// </summary>
        [Fact]
        public async Task ToDictionaryAsync_跨表异步取消令牌()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAsync<Exception>(
                () => _fsql.Select<PoCArea>().AsDto()
                    .ToDictionaryAsync<int, PoCAreaFlatDto>(a => a.Id, cts.Token));
        }
    }
}
