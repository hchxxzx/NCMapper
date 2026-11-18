// 集成测试：EFCoreDtoSelector.ToTreeList/ToTreeListAsync
// 覆盖矩阵同步 project happy path / 空结果 / 多根 / [MapTree] 三属性自定义
// 无 [MapTree] 默认契约（TreeNode 平铺——D4）/ 异步一致性 / CancellationToken /
// 降级路径与投影一致 / 双挂载消解唯一引用（project + degrade）/ 契约错误三型
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>自引用导航实体（双挂载消解场景载体：实体 Children 导航 + DTO Children 同名）</summary>
    public class TreeCategoryEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<TreeCategoryEntity> Children { get; set; } = new();
    }

    /// <summary>树 DTO（默认 [MapTree] 契约——挂载位 Children 与实体导航同名）</summary>
    [MapFrom(typeof(TreeCategoryEntity))]
    [MapTree("Id", "ParentId", "Children")]
    public class TreeCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<TreeCategoryDto> Children { get; set; } = new();
    }

    /// <summary>三属性自定义树 DTO（挂载位 Nodes——[MapTree].ChildrenProperty 命中）</summary>
    [MapFrom(typeof(TreeCategoryEntity))]
    [MapTree("Id", "ParentId", "Nodes")]
    public class TreeCategoryCustomDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<TreeCategoryCustomDto> Nodes { get; set; } = new();
    }

    /// <summary>降级触发 DTO（Tags 为 HashSet + 带 [MapFrom] 元素 → HasNonProjectableCollection 触发 degrade）</summary>
    [MapFrom(typeof(TreeCategoryEntity))]
    [MapTree("Id", "ParentId", "Children")]
    public class TreeCategoryDegradeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<TreeCategoryDegradeDto> Children { get; set; } = new();
        public HashSet<TreeCategoryDto> Tags { get; set; } = new();
    }

    /// <summary>契约错误 DTO——缺 Children（TreeMissingChildrenCollectionProperty）</summary>
    [MapFrom(typeof(TreeCategoryEntity))]
    [MapTree]
    public class NoChildrenTreeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
    }

    /// <summary>契约错误 DTO——Children 非自引用（TreeChildrenPropertyInvalid）</summary>
    [MapFrom(typeof(TreeCategoryEntity))]
    [MapTree]
    public class BadTypeTreeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<string> Children { get; set; } = new();
    }

    /// <summary>契约错误 DTO——缺 [MapFrom]（project 路径 MapFromNotApplied，R1b）</summary>
    [MapTree]
    public class NoMapFromTreeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<NoMapFromTreeDto> Children { get; set; } = new();
    }

    /// <summary>EFCore 树测试 DbContext（SQLite 内存 + 自引用导航 + TreeNode 平铺表）</summary>
    public class TreeEfDbContext : DbContext
    {
        public DbSet<TreeCategoryEntity> Categories { get; set; } = null!;
        public DbSet<DapperAsDtoTerminalTests.TreeNode> TreeNodes { get; set; } = null!;

        private readonly string _cs;

        public TreeEfDbContext(string cs)
        {
            _cs = cs;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder ob) => ob.UseSqlite(_cs);

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // 自引用导航（Fluent：HasMany/WithOne 执行期定夺，登记）
            mb.Entity<TreeCategoryEntity>()
                .HasMany(c => c.Children)
                .WithOne()
                .HasForeignKey(c => c.ParentId);
        }

        /// <summary>植入三层树 + 多根 + TreeNode（对齐 DapperAsDtoTerminalTests 数据形态）</summary>
        public void SeedData()
        {
            Database.EnsureCreated();
            // TreeCategory：Root1→{Child1→GrandChild1, Child2→GrandChild2} + Root2（多根）
            Categories.AddRange(
                new TreeCategoryEntity { Id = 1, Name = "Root1", ParentId = null },
                new TreeCategoryEntity { Id = 2, Name = "Child1", ParentId = 1 },
                new TreeCategoryEntity { Id = 3, Name = "Child2", ParentId = 1 },
                new TreeCategoryEntity { Id = 4, Name = "GrandChild1", ParentId = 2 },
                new TreeCategoryEntity { Id = 5, Name = "GrandChild2", ParentId = 3 },
                new TreeCategoryEntity { Id = 6, Name = "Root2", ParentId = null });
            // TreeNode：无导航平铺（D4——"无 [MapTree] 默认契约"用例实体口径，对齐 Dapper 先例）
            TreeNodes.AddRange(
                new DapperAsDtoTerminalTests.TreeNode { Id = 1, Name = "Root", ParentId = null },
                new DapperAsDtoTerminalTests.TreeNode { Id = 2, Name = "Child1", ParentId = 1 },
                new DapperAsDtoTerminalTests.TreeNode { Id = 3, Name = "Child2", ParentId = 1 },
                new DapperAsDtoTerminalTests.TreeNode { Id = 4, Name = "GrandChild", ParentId = 2 });
            SaveChanges();
        }
    }

    /// <summary>
    /// EFCore ToTreeList/ToTreeListAsync 集成测试（双路径 + 树挂载位契约消解）
    /// </summary>
    public class EFCoreTreeTests : IDisposable
    {
        private readonly TreeEfDbContext _db;

        public EFCoreTreeTests()
        {
            var cs = $"DataSource=file:tree{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new TreeEfDbContext(cs);
            _db.SeedData();
        }

        public void Dispose() => _db.Dispose();

        /// <summary>全树遍历收集节点 Id（双挂载消解断言——重复即双挂载）</summary>
        private static List<int> CollectIds<T>(List<T> roots, Func<T, List<T>?> children) where T : class
        {
            var ids = new List<int>();
            void Walk(T node)
            {
                ids.Add((int)typeof(T).GetProperty("Id")!.GetValue(node)!);
                foreach (var child in children(node) ?? new List<T>())
                    Walk(child);
            }
            roots.ForEach(Walk);
            return ids;
        }

        private static List<TreeCategoryDto> ToDtos(List<TreeCategoryDegradeDto> degradeRoots) =>
            degradeRoots.Select(MapToDto).ToList();

        private static TreeCategoryDto MapToDto(TreeCategoryDegradeDto d) =>
            new()
            {
                Id = d.Id, Name = d.Name, ParentId = d.ParentId,
                Children = d.Children.Select(MapToDto).ToList()
            };

        // ===== ToTreeList 同步（project 路径）=====

        /// <summary>happy path：三层嵌套树结构正确 + 多根</summary>
        [Fact]
        public void ToTreeList_Project_HappyPath_三层嵌套与多根()
        {
            var roots = _db.Categories.AsDto().ToTreeList<TreeCategoryDto>();

            Assert.Equal(2, roots.Count);
            Assert.Equal("Root1", roots[0].Name);
            Assert.Equal(2, roots[0].Children.Count);
            Assert.Equal("Child1", roots[0].Children[0].Name);
            Assert.Equal("Child2", roots[0].Children[1].Name);
            Assert.Single(roots[0].Children[0].Children);
            Assert.Equal("GrandChild1", roots[0].Children[0].Children[0].Name);
            Assert.Single(roots[0].Children[1].Children);
            Assert.Equal("GrandChild2", roots[0].Children[1].Children[0].Name);
            Assert.Empty(roots[1].Children); // Root2 无子
        }

        /// <summary>空结果返回空列表</summary>
        [Fact]
        public void ToTreeList_Empty_返回空列表()
        {
            var roots = _db.Categories.Where(c => c.ParentId == 999).AsDto().ToTreeList<TreeCategoryDto>();

            Assert.Empty(roots);
        }

        /// <summary>[MapTree] 三属性自定义：Nodes 挂载位命中</summary>
        [Fact]
        public void ToTreeList_CustomTreeAttributes_Nodes挂载()
        {
            var roots = _db.Categories.AsDto().ToTreeList<TreeCategoryCustomDto>();

            Assert.Equal(2, roots.Count);
            Assert.Equal(2, roots[0].Nodes.Count);
            Assert.Equal("Child1", roots[0].Nodes[0].Name);
            Assert.Single(roots[0].Nodes[0].Nodes);
            Assert.Equal("GrandChild1", roots[0].Nodes[0].Nodes[0].Name);
        }

        /// <summary>无 [MapTree] 默认契约（TreeNode 平铺实体——D4 口径，对齐 Dapper 树语义）</summary>
        [Fact]
        public void ToTreeList_NoMapTree_默认契约()
        {
            var roots = _db.TreeNodes.AsDto().ToTreeList<DapperAsDtoTerminalTests.TreeNodeDto>();

            Assert.Single(roots);
            Assert.Equal("Root", roots[0].Name);
            Assert.Equal(2, roots[0].Children.Count);
            Assert.Equal("Child1", roots[0].Children[0].Name);
            Assert.Single(roots[0].Children[0].Children);
            Assert.Equal("GrandChild", roots[0].Children[0].Children[0].Name);
        }

        // ===== 双挂载消解（正确性断言：唯一引用）=====

        /// <summary>project 路径双挂载消解——全树 Id 无重复（预填实例与挂载实例不同引用则必红）</summary>
        [Fact]
        public void ToTreeList_Project_双挂载消解_唯一引用()
        {
            var roots = _db.Categories.AsDto().ToTreeList<TreeCategoryDto>();

            Assert.Equal(2, roots[0].Children.Count); // 挂载 2 子（预填未消解则计数异常/重复）
            var ids = CollectIds(roots, d => d.Children);
            Assert.Equal(new[] { 1, 2, 4, 3, 5, 6 }, ids); // 全树遍历序（Root1→Child1→GC1→Child2→GC2, Root2）
        }

        /// <summary>degrade 路径双挂载消解——挂载位导航不 Include + 防御清空，全树 Id 无重复</summary>
        [Fact]
        public void ToTreeList_Degrade_双挂载消解_唯一引用()
        {
            var roots = _db.Categories.AsDto().ToTreeList<TreeCategoryDegradeDto>();

            Assert.Equal(2, roots[0].Children.Count);
            var ids = CollectIds(roots, d => d.Children);
            Assert.Equal(new[] { 1, 2, 4, 3, 5, 6 }, ids);
        }

        /// <summary>最严苛场景：用户显式前置 Include 挂载位导航 → MapList 预填 → 防御清空消解（唯一引用）
        /// <para>证伪锚点（经验库 #13 防漏必红）：失活 ClearTreeChildrenSlot 时预填实例 + 挂载实例并存 → 本用例必红</para>
        /// </summary>
        [Fact]
        public void ToTreeList_Degrade_用户前置Include_防御清空消解_唯一引用()
        {
            var roots = _db.Categories
                .Include(c => c.Children)
                .AsDto()
                .ToTreeList<TreeCategoryDegradeDto>();

            // 未消解时 Root1.Children = 预填[Child1,Child2] + TreeBuilder 挂载[Child1,Child2] = 4
            Assert.Equal(2, roots[0].Children.Count);
            var ids = CollectIds(roots, d => d.Children);
            Assert.Equal(new[] { 1, 2, 4, 3, 5, 6 }, ids);
        }

        // ===== 降级路径 =====

        /// <summary>降级路径（HasNonProjectableCollection 触发）树结果与投影路径一致（同数据源）</summary>
        [Fact]
        public void ToTreeList_Degrade_树结果与投影一致()
        {
            var projectRoots = _db.Categories.AsDto().ToTreeList<TreeCategoryDto>();
            var degradeRoots = _db.Categories.AsDto().ToTreeList<TreeCategoryDegradeDto>();
            var degradeAsDto = ToDtos(degradeRoots);

            Assert.Equal(projectRoots.Count, degradeAsDto.Count);
            for (var i = 0; i < projectRoots.Count; i++)
            {
                Assert.Equal(projectRoots[i].Id, degradeAsDto[i].Id);
                Assert.Equal(projectRoots[i].Name, degradeAsDto[i].Name);
                Assert.Equal(projectRoots[i].Children.Count, degradeAsDto[i].Children.Count);
                for (var j = 0; j < projectRoots[i].Children.Count; j++)
                {
                    Assert.Equal(projectRoots[i].Children[j].Id, degradeAsDto[i].Children[j].Id);
                    Assert.Equal(
                        projectRoots[i].Children[j].Children.Count,
                        degradeAsDto[i].Children[j].Children.Count);
                }
            }
        }

        // ===== ToTreeListAsync =====

        /// <summary>异步 happy path：结果与同步一致</summary>
        [Fact]
        public async Task ToTreeListAsync_HappyPath_结果与同步一致()
        {
            var syncRoots = _db.Categories.AsDto().ToTreeList<TreeCategoryDto>();
            var asyncRoots = await _db.Categories.AsDto().ToTreeListAsync<TreeCategoryDto>(
                TestContext.Current.CancellationToken);

            Assert.Equal(syncRoots.Count, asyncRoots.Count);
            Assert.Equal("Root1", asyncRoots[0].Name);
            Assert.Equal(2, asyncRoots[0].Children.Count);
            Assert.Equal(syncRoots[0].Children[0].Children.Count, asyncRoots[0].Children[0].Children.Count);
        }

        /// <summary>异步空结果返回空列表</summary>
        [Fact]
        public async Task ToTreeListAsync_Empty_返回空列表()
        {
            var roots = await _db.Categories.Where(c => c.ParentId == 999).AsDto()
                .ToTreeListAsync<TreeCategoryDto>(TestContext.Current.CancellationToken);

            Assert.Empty(roots);
        }

        // ===== 契约错误 =====

        /// <summary>缺 Children → TreeMissingChildrenCollectionProperty（报期望名）</summary>
        [Fact]
        public void ToTreeList_缺Children_抛TreeMissing()
        {
            var ex = Assert.Throws<MapConfigurationException>(
                () => _db.Categories.AsDto().ToTreeList<NoChildrenTreeDto>());

            Assert.Contains("Children", ex.Message);
        }

        /// <summary>Children 类型非自引用 → TreeChildrenPropertyInvalid</summary>
        [Fact]
        public void ToTreeList_类型不符_抛TreeChildrenInvalid()
        {
            var ex = Assert.Throws<MapConfigurationException>(
                () => _db.Categories.AsDto().ToTreeList<BadTypeTreeDto>());

            Assert.Contains("Children", ex.Message);
        }

        /// <summary>缺 [MapFrom] → project 路径 MapFromNotApplied（R1b：与 ToList 一致）</summary>
        [Fact]
        public void ToTreeList_缺MapFrom_抛MapFromNotApplied()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => _db.Categories.AsDto().ToTreeList<NoMapFromTreeDto>());

            Assert.Contains(nameof(NoMapFromTreeDto), ex.Message);
        }
    }
}
