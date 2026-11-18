// PoC 验证：FreeSql ToTreeList + AsTreeCte + NCMapper MapList 树形映射可行性
// 验证目标：
//   POC-1 三层树映射正确性（ToTreeList 产出实体树 → MapList 产出 DTO 树）
//   POC-2 AsTreeCte 递归查询 + ToTreeList 组合可行性
//   POC-3 Include(跨表导航) × AsTreeCte 兼容性（决定 ApplyFreeSqlIncludes 去留）
// 实体约定参照 FreeSql 官方树形文档：Childs 集合 + [Navigate(nameof(ParentId))]
using FreeSql;
using FreeSql.DataAnnotations;
using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.IntegrationTests
{
    // ================================================================
    // PoC 实体定义（PoC 前缀，与现有实体隔离）
    // ================================================================

    #region 实体

    /// <summary>
    /// PoC 树形区域实体 — ParentId 自引用 + Childs 子集合 + Category 跨表导航
    /// <para>FreeSql 树形约定：Childs 集合标注 [Navigate(nameof(ParentId))]，ToTreeList 据此构建树</para>
    /// </summary>
    public class PoCArea
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>区域名称</summary>
        public string Name { get; set; } = "";

        /// <summary>父节点 Id（null 表示根节点）</summary>
        public int? ParentId { get; set; }

        /// <summary>
        /// 父节点导航属性（多对一）
        /// </summary>
        [Navigate(nameof(ParentId))]
        public PoCArea? Parent { get; set; }

        /// <summary>
        /// 子节点集合（一对多）— FreeSql ToTreeList 据此字段在内存构建树
        /// </summary>
        [Navigate(nameof(ParentId))]
        public List<PoCArea> Childs { get; set; } = new();

        /// <summary>区域分类外键（跨表导航用）</summary>
        public int CategoryId { get; set; }

        /// <summary>
        /// 区域分类导航属性（多对一）— 验证 Include × AsTreeCte 兼容性
        /// </summary>
        [Navigate(nameof(CategoryId))]
        public PoCCategory? Category { get; set; }
    }

    /// <summary>
    /// PoC 区域分类实体 — 跨表导航目标
    /// </summary>
    public class PoCCategory
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>分类名称（国家/省级/市级/区级）</summary>
        public string Name { get; set; } = "";
    }

    #endregion

    #region DTO

    /// <summary>
    /// PoC 区域 DTO — 自引用树形 + 跨表 CategoryName
    /// <para>标注 [MapFrom(typeof(PoCArea))]，Childs 元素类型为自身（递归映射）</para>
    /// <para>CategoryName 经 [MapProperty(typeof(PoCCategory), ...)] 跨表映射，依赖 PoCArea.Category 导航填充</para>
    /// </summary>
    [MapFrom(typeof(PoCArea))]
    public class PoCAreaDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }

        /// <summary>区域名称</summary>
        public string Name { get; set; } = "";

        /// <summary>父节点 Id</summary>
        public int? ParentId { get; set; }

        /// <summary>
        /// 子节点集合 — 与实体 Childs 同名，NCMapper 按名匹配 + 元素 [MapFrom] 递归映射
        /// </summary>
        public List<PoCAreaDto> Childs { get; set; } = new();

        /// <summary>
        /// 分类名称 — 跨表映射自 PoCCategory.Name（经 PoCArea.Category 导航）
        /// </summary>
        [MapProperty(typeof(PoCCategory), nameof(PoCCategory.Name))]
        public string? CategoryName { get; set; }
    }

    #endregion

    // ================================================================
    // PoC Fixture — 独立 SQLite 临时文件，三层树种子数据
    // ================================================================

    /// <summary>
    /// PoC 树形映射专用 Fixture — 独立 SQLite 临时文件，与共享 FreeSqlTestFixture 隔离
    /// <para>种子数据：三层区域树（中国→北京/上海→东城区/浦东新区）+ 4 个分类</para>
    /// </summary>
    public class PoCTreeFixture : IDisposable
    {
        /// <summary>
        /// FreeSql 实例
        /// </summary>
        public IFreeSql Fsql { get; }

        private readonly string _dbPath;

        /// <summary>
        /// 构造 — 创建独立 SQLite 并植入三层树种子数据
        /// </summary>
        public PoCTreeFixture()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_poctree_{Guid.NewGuid():N}.db");

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
        /// 种子数据植入 — 三层树 + 4 个分类
        /// <para>树结构：中国(国家) → 北京(省级)→东城区(区级)；上海(省级)→浦东新区(区级)</para>
        /// </summary>
        private void SeedData()
        {
            // Category（4 个分类）
            Fsql.Insert(new List<PoCCategory>
            {
                new() { Id = 1, Name = "国家" },
                new() { Id = 2, Name = "省级" },
                new() { Id = 3, Name = "市级" },
                new() { Id = 4, Name = "区级" },
            }).ExecuteAffrows();

            // Area（三层树，每个节点带 CategoryId）
            Fsql.Insert(new List<PoCArea>
            {
                new() { Id = 1, Name = "中国",     ParentId = null, CategoryId = 1 },
                new() { Id = 2, Name = "北京",     ParentId = 1,    CategoryId = 2 },
                new() { Id = 3, Name = "东城区",   ParentId = 2,    CategoryId = 4 },
                new() { Id = 4, Name = "上海",     ParentId = 1,    CategoryId = 2 },
                new() { Id = 5, Name = "浦东新区", ParentId = 4,    CategoryId = 4 },
            }).ExecuteAffrows();
        }
    }

    // ================================================================
    // PoC 测试
    // ================================================================

    /// <summary>
    /// PoC 验证 — FreeSql ToTreeList + AsTreeCte + NCMapper MapList 树形映射可行性
    /// <para>三个验证维度：三层树映射正确性 / AsTreeCte 递归 / Include × AsTreeCte 兼容性</para>
    /// </summary>
    public class FreeSqlTreeProjectionPoCTests : IClassFixture<PoCTreeFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="fixture">PoC 树形测试 Fixture</param>
        public FreeSqlTreeProjectionPoCTests(PoCTreeFixture fixture)
        {
            _fsql = fixture.Fsql;
        }

        // ================================================================
        // POC-1：三层树映射正确性 — ToTreeList + MapList 产出正确 DTO 树
        // 验证：NCMapper.MapList 能正确递归映射 FreeSql ToTreeList 产出的实体树
        // ================================================================

        /// <summary>
        /// POC-1：ToTreeList 产出实体树 → MapList 产出 DTO 树，三层结构正确
        /// <para>验证点：根节点数 / Childs 递归层级 / 字段值传递</para>
        /// </summary>
        [Fact]
        public void POC1_ToTreeList_Plus_MapList_三层树映射正确()
        {
            // Step 1: FreeSql 原生 ToTreeList（全量，无 AsTreeCte 过滤）
            var entityTree = _fsql.Select<PoCArea>().ToTreeList();

            // 断言实体树结构（FreeSql 原生能力，预期正常）
            Assert.Single(entityTree);
            Assert.Equal("中国", entityTree[0].Name);
            Assert.Equal(2, entityTree[0].Childs.Count);
            Assert.Equal("北京", entityTree[0].Childs[0].Name);
            Assert.Equal("上海", entityTree[0].Childs[1].Name);
            Assert.Single(entityTree[0].Childs[0].Childs);
            Assert.Equal("东城区", entityTree[0].Childs[0].Childs[0].Name);
            Assert.Single(entityTree[0].Childs[1].Childs);
            Assert.Equal("浦东新区", entityTree[0].Childs[1].Childs[0].Name);

            // Step 2: NCMapper MapList 映射实体树 → DTO 树
            var dtoTree = Mapper.MapList<PoCArea, PoCAreaDto>(entityTree)!;

            // 断言 DTO 树结构（NCMapper 递归映射能力，核心验证点）
            Assert.NotNull(dtoTree);
            Assert.Single(dtoTree);
            Assert.Equal("中国", dtoTree[0].Name);
            Assert.Equal(1, dtoTree[0].Id);
            Assert.Equal(2, dtoTree[0].Childs.Count);

            // 第二层
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

            // ParentId 传递
            Assert.Null(dtoTree[0].ParentId);
            Assert.Equal(1, dtoTree[0].Childs[0].ParentId);
        }

        // ================================================================
        // POC-2：AsTreeCte 递归查询 + ToTreeList 组合可行性
        // 验证：AsTreeCte(Where 中国) 递归查子树 + ToTreeList 组装树不报错
        // ================================================================

        /// <summary>
        /// POC-2：AsTreeCte 递归查询"中国"子树 + ToTreeList 组装树形
        /// <para>验证点：AsTreeCte 与 ToTreeList 链式组合无异常，递归结果树结构正确</para>
        /// </summary>
        [Fact]
        public void POC2_AsTreeCte_Plus_ToTreeList_递归子树组装正确()
        {
            // AsTreeCte 递归查询"中国"下所有子孙 + ToTreeList 组装树
            var entityTree = _fsql.Select<PoCArea>()
                .Where(a => a.Name == "中国")
                .AsTreeCte()
                .OrderBy(a => a.Id)
                .ToTreeList();

            // 断言：递归查出"中国"及其全部子孙，组装为树
            Assert.Single(entityTree);
            Assert.Equal("中国", entityTree[0].Name);
            Assert.Equal(2, entityTree[0].Childs.Count);
            Assert.Equal("北京", entityTree[0].Childs[0].Name);
            Assert.Equal("上海", entityTree[0].Childs[1].Name);
            Assert.Single(entityTree[0].Childs[0].Childs);
            Assert.Equal("东城区", entityTree[0].Childs[0].Childs[0].Name);
            Assert.Single(entityTree[0].Childs[1].Childs);
            Assert.Equal("浦东新区", entityTree[0].Childs[1].Childs[0].Name);

            // 叠加 MapList 映射
            var dtoTree = Mapper.MapList<PoCArea, PoCAreaDto>(entityTree)!;
            Assert.Single(dtoTree);
            Assert.Equal("中国", dtoTree[0].Name);
            Assert.Equal(2, dtoTree[0].Childs.Count);
            Assert.Equal("东城区", dtoTree[0].Childs[0].Childs[0].Name);
        }

        // ================================================================
        // POC-3：Include(跨表导航) × AsTreeCte 兼容性
        // 验证：AsTreeCte 修饰的 ISelect 是否仍支持 Include 填充跨表导航
        // 决定 ApplyFreeSqlIncludes 在树形路径的去留
        // ================================================================

        /// <summary>
        /// POC-3a：Include × AsTreeCte × ToTreeList — 跨表导航是否被填充
        /// <para>验证点：AsTreeCte 修饰后 Include(Category) 是否正常工作（不抛异常 + Category 非空）</para>
        /// <para>结论：若通过 → ApplyFreeSqlIncludes 可调用；若失败 → 跨表降级</para>
        /// </summary>
        [Fact]
        public void POC3a_Include_CrossTable_With_AsTreeCte_导航填充()
        {
            // AsTreeCte + Include(Category) + ToTreeList
            var entityTree = _fsql.Select<PoCArea>()
                .Where(a => a.Name == "中国")
                .AsTreeCte()
                .Include(a => a.Category)
                .OrderBy(a => a.Id)
                .ToTreeList();

            // 断言：Include 在 AsTreeCte 后仍生效，Category 导航被填充
            Assert.Single(entityTree);
            Assert.NotNull(entityTree[0].Category);
            Assert.Equal("国家", entityTree[0].Category!.Name);

            Assert.Equal(2, entityTree[0].Childs.Count);
            Assert.NotNull(entityTree[0].Childs[0].Category);
            Assert.Equal("省级", entityTree[0].Childs[0].Category!.Name);
            Assert.NotNull(entityTree[0].Childs[1].Category);
            Assert.Equal("省级", entityTree[0].Childs[1].Category!.Name);

            Assert.Single(entityTree[0].Childs[0].Childs);
            Assert.NotNull(entityTree[0].Childs[0].Childs[0].Category);
            Assert.Equal("区级", entityTree[0].Childs[0].Childs[0].Category!.Name);
        }

        /// <summary>
        /// POC-3b：Include × AsTreeCte × ToTreeList × MapList — 跨表 MapProperty 端到端
        /// <para>验证点：跨表 CategoryName 经 MapList 映射后是否正确填充到 DTO 树</para>
        /// <para>完整链路验证：AsTreeCte + Include + ToTreeList + MapList</para>
        /// </summary>
        [Fact]
        public void POC3b_Include_CrossTable_EndToEnd_DTO树跨表字段填充()
        {
            // 完整链路：AsTreeCte + Include + ToTreeList + MapList
            var entityTree = _fsql.Select<PoCArea>()
                .Where(a => a.Name == "中国")
                .AsTreeCte()
                .Include(a => a.Category)
                .OrderBy(a => a.Id)
                .ToTreeList();

            var dtoTree = Mapper.MapList<PoCArea, PoCAreaDto>(entityTree)!;

            // 断言：DTO 树结构 + 跨表 CategoryName 填充
            Assert.Single(dtoTree);
            Assert.Equal("中国", dtoTree[0].Name);
            Assert.Equal("国家", dtoTree[0].CategoryName);

            Assert.Equal(2, dtoTree[0].Childs.Count);
            Assert.Equal("北京", dtoTree[0].Childs[0].Name);
            Assert.Equal("省级", dtoTree[0].Childs[0].CategoryName);
            Assert.Equal("上海", dtoTree[0].Childs[1].Name);
            Assert.Equal("省级", dtoTree[0].Childs[1].CategoryName);

            Assert.Single(dtoTree[0].Childs[0].Childs);
            Assert.Equal("东城区", dtoTree[0].Childs[0].Childs[0].Name);
            Assert.Equal("区级", dtoTree[0].Childs[0].Childs[0].CategoryName);

            Assert.Single(dtoTree[0].Childs[1].Childs);
            Assert.Equal("浦东新区", dtoTree[0].Childs[1].Childs[0].Name);
            Assert.Equal("区级", dtoTree[0].Childs[1].Childs[0].CategoryName);
        }

        /// <summary>
        /// POC-3c：对照实验 — 不 Include 时跨表 CategoryName 为 null
        /// <para>验证点：不调 Include 时，Category 导航为 null，MapList 后 CategoryName 也为 null</para>
        /// <para>证明 Include 是跨表 MapProperty 生效的必要条件</para>
        /// </summary>
        [Fact]
        public void POC3c_NoInclude_CrossTable_CategoryName为null()
        {
            // 不 Include，直接 AsTreeCte + ToTreeList
            var entityTree = _fsql.Select<PoCArea>()
                .Where(a => a.Name == "中国")
                .AsTreeCte()
                .OrderBy(a => a.Id)
                .ToTreeList();

            // 实体层：Category 导航未被填充
            Assert.Single(entityTree);
            Assert.Null(entityTree[0].Category);

            var dtoTree = Mapper.MapList<PoCArea, PoCAreaDto>(entityTree)!;

            // DTO 层：跨表 CategoryName 为 null（因导航未填充）
            Assert.Single(dtoTree);
            Assert.Equal("中国", dtoTree[0].Name);
            Assert.Null(dtoTree[0].CategoryName);
        }
    }
}
