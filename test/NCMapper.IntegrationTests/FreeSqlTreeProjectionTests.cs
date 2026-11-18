// 集成测试：FreeSqlDtoSelector.ToTreeList/Async 带映射转手树形映射
// 覆盖维度：基础树形 / AsTreeCte / 跨表 / 异步 / 入口校验失败
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql ToTreeList 带映射转手集成测试
    /// <para>复用 PoCTreeFixture（独立 SQLite）+ PoCArea/PoCCategory/PoCAreaDto 实体</para>
    /// </summary>
    public class FreeSqlTreeProjectionTests : IClassFixture<PoCTreeFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="fixture">PoC 树形测试 Fixture</param>
        public FreeSqlTreeProjectionTests(PoCTreeFixture fixture)
        {
            _fsql = fixture.Fsql;
        }

        /// <summary>
        /// T-T01：AsDto().ToTreeList 三层树映射 — 验证 NCMapper 递归映射正确性
        /// </summary>
        [Fact]
        public void ToTreeList_三层树映射正确()
        {
            var dtoTree = _fsql.Select<PoCArea>().AsDto().ToTreeList<PoCAreaDto>();

            Assert.Single(dtoTree);
            Assert.Equal("中国", dtoTree[0].Name);
            Assert.Equal(1, dtoTree[0].Id);

            // 第二层：北京、上海
            Assert.Equal(2, dtoTree[0].Childs.Count);
            Assert.Equal("北京", dtoTree[0].Childs[0].Name);
            Assert.Equal(2, dtoTree[0].Childs[0].Id);
            Assert.Equal("上海", dtoTree[0].Childs[1].Name);
            Assert.Equal(4, dtoTree[0].Childs[1].Id);

            // 第三层
            Assert.Single(dtoTree[0].Childs[0].Childs);
            Assert.Equal("东城区", dtoTree[0].Childs[0].Childs[0].Name);
            Assert.Equal(3, dtoTree[0].Childs[0].Childs[0].Id);
            Assert.Single(dtoTree[0].Childs[1].Childs);
            Assert.Equal("浦东新区", dtoTree[0].Childs[1].Childs[0].Name);
            Assert.Equal(5, dtoTree[0].Childs[1].Childs[0].Id);
        }

        /// <summary>
        /// T-T02：AsDto().ToTreeList + AsTreeCte 递归子树查询
        /// </summary>
        [Fact]
        public void ToTreeList_AsTreeCte_递归子树组装正确()
        {
            var dtoTree = _fsql.Select<PoCArea>()
                .Where(a => a.Name == "中国")
                .AsTreeCte()
                .OrderBy(a => a.Id)
                .AsDto()
                .ToTreeList<PoCAreaDto>();

            Assert.Single(dtoTree);
            Assert.Equal("中国", dtoTree[0].Name);
            Assert.Equal(2, dtoTree[0].Childs.Count);
            Assert.Equal("北京", dtoTree[0].Childs[0].Name);
            Assert.Equal("上海", dtoTree[0].Childs[1].Name);
            Assert.Single(dtoTree[0].Childs[0].Childs);
            Assert.Equal("东城区", dtoTree[0].Childs[0].Childs[0].Name);
            Assert.Single(dtoTree[0].Childs[1].Childs);
            Assert.Equal("浦东新区", dtoTree[0].Childs[1].Childs[0].Name);
        }

        /// <summary>
        /// T-T03：AsDto().ToTreeList 跨表 CategoryName 自动 Include + 映射
        /// </summary>
        [Fact]
        public void ToTreeList_跨表CategoryName填充()
        {
            // ApplyFreeSqlIncludes 自动 Include Category（经 Collector→ApplyFreeSqlIncludes）
            var dtoTree = _fsql.Select<PoCArea>()
                .Where(a => a.Name == "中国")
                .AsTreeCte()
                .OrderBy(a => a.Id)
                .AsDto()
                .ToTreeList<PoCAreaDto>();

            Assert.Single(dtoTree);
            Assert.Equal("中国", dtoTree[0].Name);
            Assert.Equal("国家", dtoTree[0].CategoryName);

            Assert.Equal("北京", dtoTree[0].Childs[0].Name);
            Assert.Equal("省级", dtoTree[0].Childs[0].CategoryName);
            Assert.Equal("上海", dtoTree[0].Childs[1].Name);
            Assert.Equal("省级", dtoTree[0].Childs[1].CategoryName);

            Assert.Equal("东城区", dtoTree[0].Childs[0].Childs[0].Name);
            Assert.Equal("区级", dtoTree[0].Childs[0].Childs[0].CategoryName);
            Assert.Equal("浦东新区", dtoTree[0].Childs[1].Childs[0].Name);
            Assert.Equal("区级", dtoTree[0].Childs[1].Childs[0].CategoryName);
        }

        /// <summary>
        /// T-T04：AsDto().ToTreeListAsync 异步版本行为一致
        /// </summary>
        [Fact]
        public async Task ToTreeListAsync_三层树映射正确()
        {
            var dtoTree = await _fsql.Select<PoCArea>().AsDto().ToTreeListAsync<PoCAreaDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(dtoTree);
            Assert.Equal("中国", dtoTree[0].Name);
            Assert.Equal(2, dtoTree[0].Childs.Count);
            Assert.Equal("北京", dtoTree[0].Childs[0].Name);
            Assert.Equal("上海", dtoTree[0].Childs[1].Name);
        }

        /// <summary>
        /// T-T05：AsDto().ToTreeListAsync 支持取消令牌
        /// </summary>
        [Fact]
        public async Task ToTreeListAsync_取消令牌校验()
        {
            using var cts = new CancellationTokenSource();
            // 已取消的令牌应在 Task.FromResult 前抛异常
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => _fsql.Select<PoCArea>().AsDto().ToTreeListAsync<PoCAreaDto>(cts.Token));
        }

        /// <summary>
        /// T-T06：ToTreeList 入口校验 — 无自引用集合属性的 DTO 抛 MapConfigurationException
        /// </summary>
        [Fact]
        public void ToTreeList_无自引用集合_抛MapConfigurationException()
        {
            // PoCAreaFlatDto 无自引用集合属性，应抛异常
            var ex = Assert.Throws<MapConfigurationException>(
                () => _fsql.Select<PoCArea>().AsDto().ToTreeList<PoCAreaFlatDto>());

            // 双语容忍（先例 KnownIssueTests）：zh"自引用集合" / en"self-referencing collection"
            Assert.True(ex.Message.Contains("自引用集合") || ex.Message.Contains("self-referencing collection"),
                $"Expected '自引用集合' or 'self-referencing collection' in: {ex.Message}");
            Assert.Contains(nameof(PoCAreaFlatDto), ex.Message);
        }

        /// <summary>
        /// T-T07：ToTreeList 空结果集（根节点无数据）
        /// </summary>
        [Fact]
        public void ToTreeList_空结果集_返回空列表()
        {
            var dtoTree = _fsql.Select<PoCArea>()
                .Where(a => a.Id < 0)
                .AsDto()
                .ToTreeList<PoCAreaDto>();

            Assert.NotNull(dtoTree);
            Assert.Empty(dtoTree);
        }

        /// <summary>
        /// T-T08：ToTreeList 单节点树（无子节点）
        /// </summary>
        [Fact]
        public void ToTreeList_单节点无子节点()
        {
            // 东城区没有子节点
            var dtoTree = _fsql.Select<PoCArea>()
                .Where(a => a.Name == "东城区")
                .AsDto()
                .ToTreeList<PoCAreaDto>();

            Assert.Single(dtoTree);
            Assert.Equal("东城区", dtoTree[0].Name);
            Assert.Empty(dtoTree[0].Childs);
        }
    }
}
