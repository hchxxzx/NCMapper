// 同名校验测试网 FreeSql ToTreeList DTO 子集合名与实体自引用导航名一致校验
// 矩阵：同名通过（回归锁定）/ 异名抛异常（消息断言）/ 双语（zh/en）/ [MapTree] 无效性 /
//       实体无自引用导航跳过（F1）/ 多自引用边界（Q1/D3 任一命中定案）/ 异步路径（B-2）
using System.Globalization;
using FreeSql;
using FreeSql.DataAnnotations;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    // ================================================================
    // Q8 实体定义（Q8 前缀，与现有实体隔离）
    // ================================================================

    /// <summary>
    /// Q8 单导航树实体 — 自引用导航名 SubCategories
    /// <para>三层树种子：根节点 → 子节点A/子节点B → 孙节点</para>
    /// </summary>
    public class Q8Category
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>父节点 Id（null 表示根节点）</summary>
        public int? ParentId { get; set; }

        /// <summary>子节点导航（一对多）— 名称与 DTO 子集合 Children 异名（异名场景构造用）</summary>
        [Navigate(nameof(ParentId))]
        public List<Q8Category> SubCategories { get; set; } = new();
    }

    /// <summary>
    /// Q8 多候选导航实体 — 反射层面有两个自引用集合属性（SubCategories 标 [Navigate]、Nodes 不标注）
    /// <para>校验按反射实际属性类型判定（元素类型 == 实体自身）：sourceNavNames = { SubCategories, Nodes }；
    /// FreeSql 建树仅用 [Navigate] 的 SubCategories——校验判定与建树配置解耦</para>
    /// </summary>
    public class Q8MultiNavNode
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>父节点 Id</summary>
        public int? ParentId { get; set; }

        /// <summary>子节点导航（[Navigate] 建树用）</summary>
        [Navigate(nameof(ParentId))]
        public List<Q8MultiNavNode> SubCategories { get; set; } = new();

        /// <summary>第二自引用集合（无 [Navigate]，仅作校验候选名——多导航任一命中场景构造用）</summary>
        public List<Q8MultiNavNode> Nodes { get; set; } = new();
    }

    /// <summary>
    /// Q8 无导航实体 — 无任何自引用集合属性（F1 兜底场景构造用）
    /// </summary>
    public class Q8FlatNode
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>父节点 Id</summary>
        public int? ParentId { get; set; }
    }

    // ================================================================
    // Q8 DTO 定义
    // ================================================================

    /// <summary>
    /// Q8 同名 DTO — 子集合名 SubCategories 与实体导航同名（回归锁定场景）
    /// </summary>
    [MapFrom(typeof(Q8Category))]
    public class Q8CategoryMatchDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>子节点集合 — 与实体导航 SubCategories 同名（正常建树）</summary>
        public List<Q8CategoryMatchDto> SubCategories { get; set; } = new();
    }

    /// <summary>
    /// Q8 异名 DTO — 子集合名 Children 与实体导航 SubCategories 异名（Q8 主场景：应抛异常）
    /// </summary>
    [MapFrom(typeof(Q8Category))]
    public class Q8CategoryChildrenDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>子节点集合 — 异名（Children ≠ SubCategories），现状静默丢子树，Q8 后应抛异常</summary>
        public List<Q8CategoryChildrenDto> Children { get; set; } = new();
    }

    /// <summary>
    /// Q8 [MapTree] 标注 DTO — 异名 + [MapTree(childrenProperty)] 自定义（[MapTree] 无效性锁定场景）
    /// <para>FreeSql 不读取 [MapTree]：校验按反射实际属性名（Children）判定，仍应抛异常</para>
    /// </summary>
    [MapFrom(typeof(Q8Category))]
    [MapTree(childrenProperty: nameof(Children))]
    public class Q8CategoryMapTreeDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>子节点集合 — 异名 + [MapTree] 自定义名（对 FreeSql 路径无效）</summary>
        public List<Q8CategoryMapTreeDto> Children { get; set; } = new();
    }

    /// <summary>
    /// Q8 部分匹配 DTO — 双自引用集合 { Children(不匹配), SubCategories(匹配) }（Q1/D3 定案锁定场景）
    /// <para>任一命中即通过：SubCategories 正常建树；未匹配的 Children 静默为空（残留边界 R5 显式契约）</para>
    /// </summary>
    [MapFrom(typeof(Q8Category))]
    public class Q8PartialMatchDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>子节点集合 — 不匹配实体导航（静默为空，R5 残留边界锁定）</summary>
        public List<Q8PartialMatchDto> Children { get; set; } = new();

        /// <summary>子节点集合 — 匹配实体导航 SubCategories（任一命中即通过）</summary>
        public List<Q8PartialMatchDto> SubCategories { get; set; } = new();
    }

    /// <summary>
    /// Q8 多导航匹配 DTO — 单集合 Nodes 匹配实体多候选导航之一（多导航任一命中场景）
    /// </summary>
    [MapFrom(typeof(Q8MultiNavNode))]
    public class Q8NodesDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>子节点集合 — 匹配实体第二候选导航名 Nodes（任一命中即通过）</summary>
        public List<Q8NodesDto> Nodes { get; set; } = new();
    }

    /// <summary>
    /// Q8 无导航实体 DTO — 子集合 Children（实体无自引用导航 → 校验跳过，F1 兜底场景）
    /// </summary>
    [MapFrom(typeof(Q8FlatNode))]
    public class Q8FlatDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>子节点集合 — 实体无自引用导航时校验跳过（不因同名校验抛错）</summary>
        public List<Q8FlatDto> Children { get; set; } = new();
    }
    /// <summary>
    /// Q8 多导航全不匹配 DTO — 子集合 Children 不匹配 {SubCategories, Nodes} 任一（多导航边界：应抛异常）
    /// </summary>
    [MapFrom(typeof(Q8MultiNavNode))]
    public class Q8MultiNavChildrenDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";

        /// <summary>子节点集合 — 与实体两个候选导航名均异名（全不匹配 → 抛异常，消息列全部候选名）</summary>
        public List<Q8MultiNavChildrenDto> Children { get; set; } = new();
    }

    // ================================================================
    // Q8 Fixture — 独立 SQLite 临时文件（与 PoCTreeFixture/共享 FreeSqlTestFixture 隔离）
    // ================================================================

    /// <summary>
    /// Q8 同名校验专用 Fixture — 独立 SQLite 临时文件
    /// <para>种子：Q8Category 三层树（SubCategories 导航）+ Q8MultiNavNode 三层树（多候选导航）+ Q8FlatNode 平铺（无导航）</para>
    /// </summary>
    public class Q8TreeFixture : IDisposable
    {
        /// <summary>
        /// FreeSql 实例
        /// </summary>
        public IFreeSql Fsql { get; }

        private readonly string _dbPath;

        /// <summary>
        /// 构造 — 创建独立 SQLite 并植入种子数据
        /// </summary>
        public Q8TreeFixture()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_q8_{Guid.NewGuid():N}.db");

            Fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            SeedData();
        }

        /// <summary>
        /// 释放 FreeSql 并删除临时数据库文件
        /// </summary>
        public void Dispose()
        {
            Fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        /// <summary>
        /// 种子数据 — Q8Category 三层树 + Q8MultiNavNode 三层树 + Q8FlatNode 平铺
        /// </summary>
        private void SeedData()
        {
            // Q8Category 三层树：根(1) → 子A(2)/子B(3) → 孙(4)
            Fsql.Insert(new List<Q8Category>
            {
                new() { Id = 1, Name = "根",   ParentId = null },
                new() { Id = 2, Name = "子A", ParentId = 1 },
                new() { Id = 3, Name = "子B", ParentId = 1 },
                new() { Id = 4, Name = "孙",  ParentId = 2 },
            }).ExecuteAffrows();

            // Q8MultiNavNode 三层树（多候选导航 SubCategories/Nodes 同构）
            Fsql.Insert(new List<Q8MultiNavNode>
            {
                new() { Id = 1, Name = "根",   ParentId = null },
                new() { Id = 2, Name = "子A", ParentId = 1 },
                new() { Id = 3, Name = "孙",  ParentId = 2 },
            }).ExecuteAffrows();

            // Q8FlatNode 平铺数据（无自引用导航）
            Fsql.Insert(new List<Q8FlatNode>
            {
                new() { Id = 1, Name = "节点1", ParentId = null },
                new() { Id = 2, Name = "节点2", ParentId = 1 },
            }).ExecuteAffrows();
        }
    }

    // ================================================================
    // Q8 测试 — 矩阵：同名通过 / 异名抛异常 / 异步 / 双语 / [MapTree] 无效 /
    //               无导航跳过 / 部分匹配任一命中 / 多导航任一命中 / 多导航全不匹配
    // ================================================================

    /// <summary>
    /// 同名校验测试 — FreeSql ToTreeList DTO 子集合名与实体自引用导航名一致校验
    /// <para>回归锁定：同名场景三层树结构正确；行为变更：异名场景由静默丢子树转为显式 MapConfigurationException</para>
    /// </summary>
    public class FreeSqlTreeNameMismatchTests : IClassFixture<Q8TreeFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造 — 注入 fixture
        /// </summary>
        /// <param name="fixture">Q8 树形测试夹具</param>
        public FreeSqlTreeNameMismatchTests(Q8TreeFixture fixture) => _fsql = fixture.Fsql;

        // ------------------------------------------------------------
        // 场景 1：同名通过（回归锁定）— SubCategories 同名，三层树完整
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-1 同名通过 — DTO 子集合 SubCategories 与实体导航同名，三层树结构完整（回归锁定）
        /// <para>验证点：根节点数=1 / SubCategories 递归两层 / 字段值传递 / 树未扁平化（总节点数经遍历=4）</para>
        /// </summary>
        [Fact]
        public void Q8_1_同名通过_三层树结构完整()
        {
            var dtoTree = _fsql.Select<Q8Category>().AsDto().ToTreeList<Q8CategoryMatchDto>();

            // 根节点（ParentId=null 仅 1 个）
            Assert.Single(dtoTree);
            Assert.Equal("根", dtoTree[0].Name);

            // 第二层：子A/子B
            Assert.Equal(2, dtoTree[0].SubCategories.Count);
            Assert.Equal("子A", dtoTree[0].SubCategories[0].Name);
            Assert.Equal("子B", dtoTree[0].SubCategories[1].Name);

            // 第三层：子A 下有孙
            Assert.Single(dtoTree[0].SubCategories[0].SubCategories);
            Assert.Equal("孙", dtoTree[0].SubCategories[0].SubCategories[0].Name);

            // 反向验证：树未扁平化 — 遍历全树节点总数=4（静默扁平会丢 3 个子节点）
            Assert.Equal(4, CountNodes(dtoTree));
        }

        // ------------------------------------------------------------
        // 场景 2：异名抛异常（中文消息）— Children ≠ SubCategories
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-2 异名抛异常 — DTO 子集合 Children 与实体导航 SubCategories 异名，抛 MapConfigurationException
        /// <para>消息断言（防恒真，固定措辞）：含实体名/DTO 名/两侧属性名 + "自引用集合属性名"/"静默丢弃" 措辞</para>
        /// </summary>
        [Fact]
        public void Q8_2_异名抛异常_中文消息断言()
        {
            var ex = Assert.Throws<MapConfigurationException>(() =>
                _fsql.Select<Q8Category>().AsDto().ToTreeList<Q8CategoryChildrenDto>());

            // 消息含实体/DTO/两侧属性名（迁移定位信息）
            Assert.Contains(nameof(Q8Category), ex.Message);
            Assert.Contains(nameof(Q8CategoryChildrenDto), ex.Message);
            Assert.Contains(nameof(Q8Category.SubCategories), ex.Message);
            Assert.Contains(nameof(Q8CategoryChildrenDto.Children), ex.Message);

            // 消息模板固定措辞（当前文化中文分支，防恒真断言）
            if (!Q8MessageText.IsEnglish)
            {
                Assert.Contains("自引用集合属性名", ex.Message);
                Assert.Contains("静默丢弃", ex.Message);
                Assert.Contains("仅返回根节点", ex.Message);
                // 迁移引导
                Assert.Contains("重命名", ex.Message);
            }
        }

        // ------------------------------------------------------------
        // 场景 3：异步路径（B-2）— ToTreeListAsync 同样校验
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-3 异步路径 — ToTreeListAsync 入口同样执行同名校验，异名抛 MapConfigurationException
        /// </summary>
        [Fact]
        public async Task Q8_3_异步路径_异名同样抛异常()
        {
            var ex = await Assert.ThrowsAsync<MapConfigurationException>(() =>
                _fsql.Select<Q8Category>().AsDto().ToTreeListAsync<Q8CategoryChildrenDto>(TestContext.Current.CancellationToken));

            Assert.Contains(nameof(Q8Category.SubCategories), ex.Message);
            Assert.Contains(nameof(Q8CategoryChildrenDto.Children), ex.Message);
        }

        // ------------------------------------------------------------
        // 场景 4：双语 — en-US 文化下英文消息
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-4 双语英文消息 — CurrentUICulture 切 en-US 后消息为英文模板（finally 恢复原文化）
        /// </summary>
        [Fact]
        public void Q8_4_双语_英文文化下消息为英文()
        {
            var original = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = new CultureInfo("en-US");

                var ex = Assert.Throws<MapConfigurationException>(() =>
                    _fsql.Select<Q8Category>().AsDto().ToTreeList<Q8CategoryChildrenDto>());

                // 英文模板固定措辞（防恒真）
                Assert.Contains("self-referencing collection property name(s)", ex.Message);
                Assert.Contains("do not match self-referencing navigation property name(s)", ex.Message);
                Assert.Contains("silently lost", ex.Message);
                Assert.Contains("only root nodes returned", ex.Message);
                // 迁移引导 + [MapTree] 提示
                Assert.Contains("Rename the DTO collection property", ex.Message);
                Assert.Contains("FreeSql does not read [MapTree]", ex.Message);
            }
            finally
            {
                CultureInfo.CurrentUICulture = original;
            }
        }

        // ------------------------------------------------------------
        // 场景 5：[MapTree] 无效性 — FreeSql 不读取 [MapTree]，异名仍抛异常
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-5 [MapTree] 无效性 — [MapTree(childrenProperty)] 自定义名对 FreeSql 路径无效，
        /// 校验按反射实际属性名（Children）判定，仍抛 MapConfigurationException
        /// </summary>
        [Fact]
        public void Q8_5_MapTree标注无效_异名仍抛异常()
        {
            var ex = Assert.Throws<MapConfigurationException>(() =>
                _fsql.Select<Q8Category>().AsDto().ToTreeList<Q8CategoryMapTreeDto>());

            // 消息仍按反射实际属性名判定（SubCategories vs Children），[MapTree] 未参与
            Assert.Contains(nameof(Q8Category.SubCategories), ex.Message);
            Assert.Contains(nameof(Q8CategoryMapTreeDto.Children), ex.Message);
        }

        // ------------------------------------------------------------
        // 场景 6：实体无自引用导航跳过（F1 兜底）— Q8FlatNode 无导航，同名校验不介入
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-6 实体无自引用导航跳过 — Q8FlatNode 无自引用集合属性，同名校验跳过（F1 兜底：普通非树场景零影响）
        /// <para>FreeSql 原生对无导航实体的行为（平铺返回或原生异常）不属于同名校验范畴，
        /// 断言仅要求：不因本校验抛 MapConfigurationException</para>
        /// </summary>
        [Fact]
        public void Q8_6_实体无自引用导航_校验跳过不介入()
        {
            var ex = Record.Exception(() =>
                _fsql.Select<Q8FlatNode>().AsDto().ToTreeList<Q8FlatDto>());

            // F1 兜底：同名校验不介入 —— 若抛 MapConfigurationException 只能来自本校验（Q8FlatDto 有自引用集合，前置 DTO 校验通过）
            if (ex is MapConfigurationException)
                Assert.Fail($"F1 兜底失效：实体无自引用导航时同名校验不应介入（{ex.Message}）");
        }

        // ------------------------------------------------------------
        // 场景 7：部分匹配任一命中（Q1/D3 定案）— {Children(不匹配), SubCategories(命中)}
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-7 部分匹配任一命中通过 — Q8PartialMatchDto 双自引用集合 {Children, SubCategories}，
        /// SubCategories 命中即通过（任一命中判定，与 MapList 按名映射实际行为对齐）
        /// <para>残留边界 R5 显式契约：未命中的 Children 静默为空（非空集，且子树数据在 SubCategories）</para>
        /// </summary>
        [Fact]
        public void Q8_7_部分匹配_任一命中通过()
        {
            var dtoTree = _fsql.Select<Q8Category>().AsDto().ToTreeList<Q8PartialMatchDto>();

            // 命中的 SubCategories 正常建树（三层）
            Assert.Single(dtoTree);
            Assert.Equal(2, dtoTree[0].SubCategories.Count);
            Assert.Equal("孙", dtoTree[0].SubCategories[0].SubCategories.Single().Name);

            // 未命中的 Children 静默为空（R5 残留边界锁定：空集合 ≠ null ≠ 异常）
            Assert.NotNull(dtoTree[0].Children);
            Assert.Empty(dtoTree[0].Children);
        }

        // ------------------------------------------------------------
        // 场景 8：多导航实体任一命中 — 实体 {SubCategories, Nodes}，DTO 单集合 Nodes
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-8 多导航实体任一命中通过 — Q8MultiNavNode 反射候选 {SubCategories, Nodes}，
        /// DTO 单集合 Nodes 命中任一即通过（校验判定与 FreeSql 建树配置解耦）
        /// <para>注意：FreeSql 建树仅用 [Navigate] 的 SubCategories，Nodes 命中但建树数据落在 SubCategories → DTO.Nodes 为空；
        /// 校验层面不抛（任一命中），此为校验宽松侧的显式边界</para>
        /// </summary>
        [Fact]
        public void Q8_8_多导航实体_任一命中通过()
        {
            var dtoTree = _fsql.Select<Q8MultiNavNode>().AsDto().ToTreeList<Q8NodesDto>();

            // 校验通过（未抛异常）且根节点正确 —— Nodes 命中候选导航名之一
            Assert.Single(dtoTree);
            Assert.Equal("根", dtoTree[0].Name);
        }

        // ------------------------------------------------------------
        // 场景 9：多导航全不匹配 — 实体 {SubCategories, Nodes}，DTO Children 均不命中
        // ------------------------------------------------------------

        /// <summary>
        /// Q8-9 多导航全不匹配抛异常 — Q8MultiNavChildrenDto.Children 与 {SubCategories, Nodes} 均异名，
        /// 抛 MapConfigurationException，消息列出全部候选导航名（多导航迁移引导）
        /// </summary>
        [Fact]
        public void Q8_9_多导航全不匹配_消息列全部候选名()
        {
            var ex = Assert.Throws<MapConfigurationException>(() =>
                _fsql.Select<Q8MultiNavNode>().AsDto().ToTreeList<Q8MultiNavChildrenDto>());

            // 消息列全部候选导航名（逗号连接，便于迁移定位）
            Assert.Contains(nameof(Q8MultiNavNode.SubCategories), ex.Message);
            Assert.Contains(nameof(Q8MultiNavNode.Nodes), ex.Message);
            Assert.Contains(nameof(Q8MultiNavChildrenDto.Children), ex.Message);
        }

        // ------------------------------------------------------------
        // 辅助
        // ------------------------------------------------------------

        /// <summary>
        /// 深度优先遍历统计全树节点数 — 反向验证树未扁平化（静默扁平会丢子节点）
        /// </summary>
        /// <param name="tree">DTO 树根列表</param>
        /// <returns>全树节点总数</returns>
        private static int CountNodes(IEnumerable<Q8CategoryMatchDto> tree)
        {
            var count = 0;
            foreach (var node in tree)
            {
                count++;
                count += CountNodes(node.SubCategories);
            }
            return count;
        }
    }

    /// <summary>
    /// 消息文化判定 — 当前 CurrentUICulture 是否英文分支（与 ErrorMessages.UseEnglish 同规则）
    /// <para>Q8-2 中文措辞断言仅在非英文文化下执行，避免 CI 环境文化干扰</para>
    /// </summary>
    internal static class Q8MessageText
    {
        /// <summary>
        /// 当前文化是否英文（en 开头）
        /// </summary>
        public static bool IsEnglish =>
            CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }
}
