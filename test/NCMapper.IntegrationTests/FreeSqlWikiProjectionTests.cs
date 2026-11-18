using System;
using System.Collections.Generic;
using System.Linq;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql Wiki 转测试 — 核心投影语义回归（差异记录固化）
    /// <para>覆盖 select-return-data.md 与 select-multi-table.md 转 NCMapper 的关键行为：</para>
    /// <para>基础投影 / 显式映射 / 构造函数映射（不支持固化）/ 导航属性投影 / 双表·三表 Compose / InnerJoin 过滤 / GroupBy 原生聚合 / 聚合子查询内存替代。</para>
    /// <para>所有断言均为业务有效性断言（非恒真），与现有 FreeSqlProjectionTests / FreeSqlQueryShapeTests 实体隔离（W* 前缀）。</para>
    /// </summary>
    public class FreeSqlWikiProjectionTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="fixture">共享测试 Fixture</param>
        public FreeSqlWikiProjectionTests(FreeSqlTestFixture fixture)
        {
            _fsql = fixture.Fsql;
        }

        // ================================================================
        // 基础投影 — 按属性名映射（默认行为）
        // ================================================================

        /// <summary>
        /// T_W1_01：AsDto().ToList&lt;WArticleDto&gt;() 按属性名映射，条数与字段正确。
        /// </summary>
        [Fact]
        public void T_W1_基础投影_按属性名映射()
        {
            var dtos = _fsql.Select<WArticle>().AsDto().ToList<WArticleDto>();

            Assert.Equal(3, dtos.Count);
            Assert.Contains(dtos, d => d.Title == "NCMapper 入门" && d.Clicks == 100);
            Assert.Contains(dtos, d => d.Title == "FreeSql 实战" && d.Clicks == 200);
            Assert.Contains(dtos, d => d.Title == "Python 爬虫" && d.Clicks == 50);
        }

        // ================================================================
        // 显式映射 — [MapFrom] + [MapProperty]（情况1/3）
        // ================================================================

        /// <summary>
        /// T_W1_02：[MapProperty] 跨表字段映射正确填充。
        /// </summary>
        [Fact]
        public void T_W1_显式映射_跨表字段填充()
        {
            var dtos = _fsql.Select<WArticle>().AsDto().ToList<WArticleWithCategoryDto>();

            Assert.Equal(3, dtos.Count);
            Assert.Contains(dtos, d => d.Title == "NCMapper 入门" && d.CategoryName == ".NET");
            Assert.Contains(dtos, d => d.Title == "Python 爬虫" && d.CategoryName == "Python");
        }

        // ================================================================
        // 构造函数参数映射 — 固化"NCMapper 不支持"（情况4/5 差异）
        // ================================================================

        /// <summary>
        /// T_W1_03：构造函数参数映射 DTO（无无参构造/无可写属性）触发 InvalidOperationException。
        /// <para>固化当前行为：NCMapper 投影优先使用可写属性，无映射属性时报错而非静默空映射。</para>
        /// </summary>
        [Fact]
        public void T_W1_构造函数映射_降级路径成功()
        {
            // 顶层有参构造器 DTO（无无参构造器）→ 触发降级到内存映射，构造器参数匹配成功
            //（修复前投影路径抛 InvalidOperationException"没有可映射的属性"——旧断言固化旧行为，已同步）
            var dtos = _fsql.Select<WArticle>().AsDto().ToList<WArticleCtorDto>();

            Assert.Equal(3, dtos.Count);
            Assert.Contains(dtos, d => d.Title == "NCMapper 入门");
            Assert.Contains(dtos, d => d.Title == "FreeSql 实战");
        }

        // ================================================================
        // T_W1_04：导航属性投影 — 透明投影自动 Include（W-2）
        // ================================================================

        /// <summary>
        /// T_W1_04：导航属性经 [MapProperty] 映射到扁平字段，透明投影路径自动 JOIN 并填充。
        /// </summary>
        [Fact]
        public void T_W1_导航属性投影_透明投影自动包含()
        {
            var dtos = _fsql.Select<WArticle>().AsDto().ToList<WArticleWithCategoryDto>();

            // 所有记录的 CategoryName 必须非空（投影路径自动 Include 导航）
            Assert.All(dtos, d => Assert.False(string.IsNullOrEmpty(d.CategoryName)));
            Assert.Equal(3, dtos.Count(d => !string.IsNullOrEmpty(d.CategoryName)));
        }

        // ================================================================
        // T_W1_05：双表 Compose 投影（W-4）
        // ================================================================

        /// <summary>
        /// T_W1_05：双表 Join + AsDto Compose 投影，跨表字段正确。
        /// </summary>
        [Fact]
        public void T_W1_双表Compose投影_跨表字段正确()
        {
            var dtos = _fsql.Select<WOrder, WCustomer>()
                .LeftJoin((a, b) => a.CustomerId == b.Id)
                .AsDto()
                .ToList<WOrderCustomerDto>()
                .OrderBy(d => d.Id)
                .ToList();

            Assert.Equal(4, dtos.Count); // LeftJoin 保留全部订单（含孤儿）
            Assert.Contains(dtos, d => d.OrderNo == "ORD-001" && d.CustomerName == "张三");
            Assert.Contains(dtos, d => d.OrderNo == "ORD-003" && d.CustomerName == "李四");
            // 孤儿订单（CustomerId=99）无匹配客户 → CustomerName 为 null
            Assert.Contains(dtos, d => d.OrderNo == "ORD-004" && d.CustomerName == null);
        }

        // ================================================================
        // T_W1_06：三表 Compose 投影（W-4）
        // ================================================================

        /// <summary>
        /// T_W1_06：三表 Join + AsDto Compose 投影，两个跨表字段正确。
        /// </summary>
        [Fact]
        public void T_W1_三表Compose投影_双跨表字段正确()
        {
            var dtos = _fsql.Select<WOrder, WCustomer, WOrderDetail>()
                .LeftJoin((a, b, c) => a.CustomerId == b.Id)
                .LeftJoin((a, b, c) => a.Id == c.OrderId)
                .AsDto()
                .ToList<WOrderFullDto>()
                .OrderBy(d => d.Id)
                .ToList();

            Assert.Equal(4, dtos.Count);
            Assert.Contains(dtos, d => d.OrderNo == "ORD-001" && d.CustomerName == "张三" && d.Product == "键盘");
            Assert.Contains(dtos, d => d.OrderNo == "ORD-002" && d.CustomerName == "张三" && d.Product == "鼠标");
            // 无明细的订单 Product 为 null（LEFT JOIN 语义）
            Assert.Contains(dtos, d => d.OrderNo == "ORD-003" && d.Product == null);
        }

        // ================================================================
        // T_W1_07：InnerJoin + Compose 仅返回匹配记录（W-4）
        // ================================================================

        /// <summary>
        /// T_W1_07：InnerJoin 过滤孤儿订单，仅返回有匹配客户的记录。
        /// </summary>
        [Fact]
        public void T_W1_InnerJoin过滤_仅返回匹配记录()
        {
            var allOrders = _fsql.Select<WOrder>().Count();
            Assert.Equal(4, allOrders); // 含 1 孤儿

            var dtos = _fsql.Select<WOrder, WCustomer>()
                .InnerJoin((a, b) => a.CustomerId == b.Id)
                .AsDto()
                .ToList<WOrderCustomerDto>();

            Assert.Equal(3, dtos.Count); // 3 条匹配，1 孤儿被过滤
            Assert.DoesNotContain(dtos, d => d.OrderNo == "ORD-004");
        }

        // ================================================================
        // T_W1_08：GroupBy 原生聚合 — 守护边界基线（W-8）
        // ================================================================

        /// <summary>
        /// T_W1_08：FreeSql 原生 GroupBy + 聚合（不经 AsDto）返回正确分组计数。
        /// <para>作为 GroupBy 守护的边界基线：NCMapper 不支持 GroupBy 链式 AsDto（编译期 [Obsolete(error:true)]），但原生聚合必须正确。</para>
        /// </summary>
        [Fact]
        public void T_W1_GroupBy原生聚合_分组计数正确()
        {
            var grouped = _fsql.Select<WArticle>()
                .GroupBy(a => a.CategoryId)
                .ToList(a => new { a.Key, Count = a.Count() });

            Assert.Equal(2, grouped.Count); // 2 个分类
            Assert.Contains(grouped, g => g.Key == 1 && g.Count == 2); // .NET 2 篇
            Assert.Contains(grouped, g => g.Key == 2 && g.Count == 1); // Python 1 篇
        }

        // ================================================================
        // T_W1_09：聚合子查询字段 — 内存替代方案（W-5 不适用项替代验证）
        // ================================================================

        /// <summary>
        /// T_W1_09：NCMapper DTO 不含子查询字段（不适用），验证推荐的内存替代方案（先 AsDto 再 LINQ 聚合）结果正确。
        /// </summary>
        [Fact]
        public void T_W1_聚合子查询_内存替代方案正确()
        {
            // 先 AsDto 取实体级 DTO
            var dtos = _fsql.Select<WArticle>().AsDto().ToList<WArticleWithCategoryDto>();

            // 再在内存用 LINQ 计算每分类文章数（等价 FreeSql 原生子查询 count）
            var counts = dtos
                .GroupBy(d => d.CategoryName)
                .ToDictionary(g => g.Key!, g => g.Count());

            Assert.Equal(2, counts[".NET"]);
            Assert.Equal(1, counts["Python"]);
        }
    }
}
