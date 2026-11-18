// 集成测试：Dapper-EFCore 树一致性 + FreeSql-[MapTree] 回归锁定
// 覆盖矩阵同 DTO 同契约两库嵌套结果一致 / FreeSql ToTreeList + [MapTree] DTO
// 树结构完整（B-1 消解实证：映射层零改动 → MapList 递归建树通道不受投影侧跳过影响）
using Dapper;
using FreeSql;
using Microsoft.Data.Sqlite;
using NCMapper.Attributes;
using NCMapper.Dapper;
using NCMapper.EFCore;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>FreeSql 回归锁定 DTO——带 [MapTree]（B-1 场景：挂载位名 Childs 与实体同名）</summary>
    [MapFrom(typeof(PoCArea))]
    [MapTree("Id", "ParentId", "Childs")]
    public class MapTreeAreaDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<MapTreeAreaDto> Childs { get; set; } = new();
    }

    /// <summary>
    /// Dapper-EFCore 树一致性测试（TreeBuilder 同构内核实证）+ FreeSql [MapTree] 回归锁定
    /// </summary>
    public class DapperEfCoreTreeConsistencyTests : IDisposable
    {
        private readonly SqliteConnection _dapperConn;
        private readonly TreeEfDbContext _efDb;

        public DapperEfCoreTreeConsistencyTests()
        {
            // Dapper 侧：SQLite 内存 + TreeNode 数据（对齐 DapperAsDtoTerminalTests 形态）
            _dapperConn = new SqliteConnection("Data Source=:memory:");
            _dapperConn.Open();
            _dapperConn.Execute("""
                CREATE TABLE TreeNode (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    ParentId INTEGER
                )
                """);
            _dapperConn.Execute("""
                INSERT INTO TreeNode (Id, Name, ParentId) VALUES
                (1, 'Root',    NULL),
                (2, 'Child1',  1),
                (3, 'Child2',  1),
                (4, 'GrandChild', 2)
                """);

            // EFCore 侧：同构数据（TreeEfDbContext.TreeNodes 同 seed）
            var cs = $"DataSource=file:consist{Guid.NewGuid():N}?mode=memory&cache=shared";
            _efDb = new TreeEfDbContext(cs);
            _efDb.SeedData();
        }

        public void Dispose()
        {
            _dapperConn.Dispose();
            _efDb.Dispose();
        }

        /// <summary>递归断言两棵树结构一致（Id/Name/子数）</summary>
        private static void AssertTreesEqual(
            List<DapperAsDtoTerminalTests.TreeNodeDto> expected,
            List<DapperAsDtoTerminalTests.TreeNodeDto> actual, string path)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i].Id == actual[i].Id, $"{path}[{i}].Id: {expected[i].Id} != {actual[i].Id}");
                Assert.True(expected[i].Name == actual[i].Name, $"{path}[{i}].Name: {expected[i].Name} != {actual[i].Name}");
                AssertTreesEqual(expected[i].Children, actual[i].Children, $"{path}[{i}].");
            }
        }

        /// <summary>同 DTO（TreeNodeDto 默认契约）同数据：Dapper ToTreeList 与 EFCore ToTreeList 嵌套结果一致</summary>
        [Fact]
        public void Dapper_EFCore_同Dto同数据_嵌套结果一致()
        {
            var dapperRoots = _dapperConn.AsDto<DapperAsDtoTerminalTests.TreeNode>()
                .ToTreeList<DapperAsDtoTerminalTests.TreeNodeDto>(
                "SELECT * FROM TreeNode ORDER BY Id");
            var efRoots = _efDb.TreeNodes.AsDto().ToTreeList<DapperAsDtoTerminalTests.TreeNodeDto>();

            AssertTreesEqual(dapperRoots, efRoots, "roots");
        }

        /// <summary>FreeSql ToTreeList + [MapTree] DTO 回归锁定——树结构完整（B-1 消解：映射层零改动，Childs 递归建树不变）</summary>
        [Fact]
        public void FreeSql_ToTreeList_MapTreeDto_树结构完整回归锁定()
        {
            using var fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
                .Build();
            fsql.CodeFirst.SyncStructure(typeof(PoCArea), typeof(PoCCategory));
            SeedFreeSqlTree(fsql);

            var tree = fsql.Select<PoCArea>().AsDto().ToTreeList<MapTreeAreaDto>();

            Assert.Single(tree);
            Assert.Equal("中国", tree[0].Name);
            Assert.Equal(2, tree[0].Childs.Count);
            Assert.Equal("北京", tree[0].Childs[0].Name);
            Assert.Equal("上海", tree[0].Childs[1].Name);
            Assert.Single(tree[0].Childs[0].Childs);
            Assert.Equal("东城区", tree[0].Childs[0].Childs[0].Name);
        }

        /// <summary>FreeSql 侧树数据（对齐 PoCTreeFixture 形态：中国→北京→东城区/上海→浦东新区）</summary>
        private static void SeedFreeSqlTree(IFreeSql fsql)
        {
            var areas = new[]
            {
                new PoCArea { Id = 1, Name = "中国", ParentId = null },
                new PoCArea { Id = 2, Name = "北京", ParentId = 1 },
                new PoCArea { Id = 3, Name = "东城区", ParentId = 2 },
                new PoCArea { Id = 4, Name = "上海", ParentId = 1 },
                new PoCArea { Id = 5, Name = "浦东新区", ParentId = 4 }
            };
            fsql.Insert<PoCArea>().AppendData(areas).ExecuteAffrows();
        }
    }
}
