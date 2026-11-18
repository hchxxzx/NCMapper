using System;
using System.Collections.Generic;
using System.Linq;
using FreeSql;
using FreeSql.DataAnnotations;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql Wiki 转测试 — 导航与降级回归（差异记录固化）
    /// <para>覆盖 select-include.md 与 filters.md 转 NCMapper 的固化项：</para>
    /// <para>投影自动 LEFT JOIN（含多级导航）/ IncludeMany 降级数据正确性 / 降级自动 Include /</para>
    /// <para>Compose 降级导航丢失（F-01）/ GlobalFilter 投影·降级·Compose 传播（W-9）/ DisableGlobalFilter / ApplyIf。</para>
    /// <para>与 Samples 演示文件实体隔离（Wiki_IT_* 表名），全局状态在每个测试方法内 Apply + finally 清理。</para>
    /// </summary>
    [Collection("FreeSqlGlobalState")]
    public class FreeSqlWikiIncludeFilterTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造 — 创建独立 SQLite InMemory 实例并灌入种子数据。
        /// </summary>
        public FreeSqlWikiIncludeFilterTests()
        {
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
                .UseAutoSyncStructure(true)
                .UseNoneCommandParameter(true)
                .Build();
            SeedData(_fsql);
        }

        /// <summary>
        /// 释放 — 清理 IFreeSql 实例（GlobalFilter 随实例释放）。
        /// </summary>
        public void Dispose()
        {
            _fsql.Dispose();
            GC.SuppressFinalize(this);
        }

        // ================================================================
        // 单级导航投影 — 投影路径自动 LEFT JOIN
        // ================================================================

        /// <summary>
        /// 单级导航经 [MapProperty] 映射，投影路径自动生成 LEFT JOIN。
        /// <para>固化：带不带显式 Include 结果一致，CategoryName 均正确填充。</para>
        /// </summary>
        [Fact]
        public void T_Include投影导航_自动LEFTJOIN()
        {
            var withInclude = _fsql.Select<WiItem>().Include(i => i.Category).AsDto().ToList<WiItemFlatDto>();
            var withoutInclude = _fsql.Select<WiItem>().AsDto().ToList<WiItemFlatDto>();

            Assert.Equal(5, withInclude.Count);
            Assert.Equal(5, withoutInclude.Count);
            Assert.All(withInclude, d => Assert.False(string.IsNullOrEmpty(d.CategoryName)));
            Assert.All(withoutInclude, d => Assert.False(string.IsNullOrEmpty(d.CategoryName)));
        }

        // ================================================================
        // 多级导航投影 — 两级 LEFT JOIN
        // ================================================================

        /// <summary>
        /// 多级导航路径 Category.Parent.Name 被翻译为两级 LEFT JOIN。
        /// <para>固化：上级分类名称全部填充（与 FreeSql 原生 ToList(true) 等价）。</para>
        /// </summary>
        [Fact]
        public void T_多级导航投影_两级LEFTJOIN()
        {
            var dtos = _fsql.Select<WiItem>().AsDto().ToList<WiItemDeepNavDto>();

            Assert.Equal(5, dtos.Count);
            Assert.All(dtos, d => Assert.False(string.IsNullOrEmpty(d.ParentCategoryName)));
            Assert.Contains(dtos, d => d.Name == "笔记本电脑" && d.ParentCategoryName == "全部");
        }

        // ================================================================
        // IncludeMany 降级 — 子项数据完整性
        // ================================================================

        /// <summary>
        /// 含集合导航的 DTO 触发降级路径，IncludeMany 二次查询结果被完整保留。
        /// <para>固化：降级后子项数量与字段（Price）与 FreeSql 原生一致。</para>
        /// </summary>
        [Fact]
        public void T_IncludeMany降级_子项数据完整()
        {
            var dtos = _fsql.Select<WiCategory>()
                .Where(c => c.ParentId == 1)
                .IncludeMany(c => c.Items)
                .AsDto()
                .ToList<WiCategoryWithItemsDto>();

            Assert.Equal(2, dtos.Count);
            var elec = dtos.First(c => c.Name == "电子产品");
            Assert.Equal(3, elec.Items.Count);
            Assert.Contains(elec.Items, i => i.Name == "笔记本电脑" && i.Price == 5999m);
            Assert.Contains(elec.Items, i => i.Name == "手机" && i.Price == 3999m);
        }

        // ================================================================
        // IncludeMany 字段裁剪 — 降级路径裁剪生效
        // ================================================================

        /// <summary>
        /// IncludeMany 子集合 Select 裁剪在降级路径下生效，未 Select 的字段保持 CLR 默认值。
        /// <para>固化 W-6 边界：裁剪字段（Price）为 CLR 默认值（0），NCMapper 降级映射如实反映 FreeSql 的部分字段加载语义。</para>
        /// </summary>
        [Fact]
        public void T_IncludeMany子集合_字段裁剪生效_裁剪字段为CLR默认值()
        {
            var dtos = _fsql.Select<WiCategory>()
                .Where(c => c.ParentId == 1)
                .IncludeMany(c => c.Items!.Select(i => new WiItem { Id = i.Id, Name = i.Name }))
                .AsDto()
                .ToList<WiCategoryWithItemsDto>();

            var all = dtos.SelectMany(c => c.Items).ToList();
            Assert.Equal(5, all.Count);
            // W-6：裁剪字段（Price）为 CLR 默认值 0，FreeSql IncludeMany Select 仅加载 Id/Name
            Assert.All(all, i => Assert.Equal(0m, i.Price));
        }

        // ================================================================
        // 降级路径自动 Include 集合
        // ================================================================

        /// <summary>
        /// 不显式写 IncludeMany，降级路径 ApplyFreeSqlIncludes 自动补齐集合导航。
        /// <para>固化：子项总数与手写 IncludeMany 一致（5 个）。</para>
        /// </summary>
        [Fact]
        public void T_降级自动Include_集合导航自动加载()
        {
            var dtos = _fsql.Select<WiCategory>()
                .Where(c => c.ParentId == 1)
                .AsDto()
                .ToList<WiCategoryWithItemsDto>();

            var total = dtos.Sum(c => c.Items?.Count ?? 0);
            Assert.Equal(5, total);
        }

        // ================================================================
        // Compose 降级导航丢失
        // ================================================================

        /// <summary>
        /// Compose 双表降级路径不自动 Include 导航属性。
        /// <para>固化 F-01：T2 直取字段（CategoryName）正常填充，T1 导航字段（NavCategoryName）为 null。</para>
        /// </summary>
        [Fact]
        public void T_Compose降级_导航字段为Null_T2直取正常()
        {
            var dtos = _fsql.Select<WiItem, WiCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .AsDto()
                .ToList<WiItemComposeDto>();

            Assert.Equal(5, dtos.Count);
            // T2 直取字段填充
            Assert.All(dtos, d => Assert.False(string.IsNullOrEmpty(d.CategoryName)));
            // T1 导航字段为 null（F-01 确认）
            Assert.All(dtos, d => Assert.True(string.IsNullOrEmpty(d.NavCategoryName)));
            Assert.Contains(dtos, d => d.CategoryName == "电子产品");
        }

        // ================================================================
        // GlobalFilter + 投影路径
        // ================================================================

        /// <summary>
        /// GlobalFilter 在 AsDto 投影路径生效（作用于 SQL where）。
        /// <para>固化：返回 3 条（排除已删除），与映射路径无关。</para>
        /// </summary>
        [Fact]
        public void T_GlobalFilter_投影路径生效()
        {
            try
            {
                _fsql.GlobalFilter.Apply<WfProduct>("softDelete", p => p.IsDeleted == false);
                var dtos = _fsql.Select<WfProduct>().AsDto().ToList<WfProductDto>();

                Assert.Equal(3, dtos.Count);
                Assert.DoesNotContain(dtos, d => d.Id == 3);
            }
            finally
            {
                _fsql.GlobalFilter.Remove("softDelete");
            }
        }

        // ================================================================
        // GlobalFilter + 降级路径
        // ================================================================

        /// <summary>
        /// GlobalFilter 在 AsDto 降级路径同样生效，两条路径行为一致。
        /// <para>固化：WfProductConvDto 含 [Convert] 触发降级，返回 3 条。</para>
        /// </summary>
        [Fact]
        public void T_GlobalFilter_降级路径生效()
        {
            try
            {
                _fsql.GlobalFilter.Apply<WfProduct>("softDelete", p => p.IsDeleted == false);
                var dtos = _fsql.Select<WfProduct>().AsDto().ToList<WfProductConvDto>();

                Assert.Equal(3, dtos.Count);
                Assert.DoesNotContain(dtos, d => d.Id == 3);
            }
            finally
            {
                _fsql.GlobalFilter.Remove("softDelete");
            }
        }

        // ================================================================
        // DisableGlobalFilter
        // ================================================================

        /// <summary>
        /// DisableGlobalFilter 在 AsDto 之前调用可正常禁用过滤器。
        /// <para>固化：按名禁用与全部禁用均返回 4 条全量。</para>
        /// </summary>
        [Fact]
        public void T_DisableGlobalFilter_可禁用()
        {
            try
            {
                _fsql.GlobalFilter.Apply<WfProduct>("softDelete", p => p.IsDeleted == false);
                var byName = _fsql.Select<WfProduct>().DisableGlobalFilter("softDelete").AsDto().ToList<WfProductDto>();
                var all = _fsql.Select<WfProduct>().DisableGlobalFilter().AsDto().ToList<WfProductDto>();

                Assert.Equal(4, byName.Count);
                Assert.Equal(4, all.Count);
            }
            finally
            {
                _fsql.GlobalFilter.Remove("softDelete");
            }
        }

        // ================================================================
        // ApplyIf 条件过滤器
        // ================================================================

        /// <summary>
        /// ApplyIf 条件开关与 AsDto 组合正常。
        /// <para>固化：开关关闭 3 条、开启 2 条（softDelete + tenant 叠加）。</para>
        /// </summary>
        [Fact]
        public void T_ApplyIf_条件开关生效()
        {
            var tenantEnabled = false;
            try
            {
                _fsql.GlobalFilter.Apply<WfProduct>("softDelete", p => p.IsDeleted == false);
                _fsql.GlobalFilter.ApplyIf<WfProduct>("tenant", () => tenantEnabled, p => p.TenantId == 1);

                var off = _fsql.Select<WfProduct>().AsDto().ToList<WfProductDto>();
                tenantEnabled = true;
                var on = _fsql.Select<WfProduct>().AsDto().ToList<WfProductDto>();

                Assert.Equal(3, off.Count);
                Assert.Equal(2, on.Count);
            }
            finally
            {
                _fsql.GlobalFilter.Remove("softDelete");
                _fsql.GlobalFilter.Remove("tenant");
            }
        }

        // ================================================================
        // GlobalFilter 在 Compose 多表查询传播
        // ================================================================

        /// <summary>
        /// GlobalFilter 在 ISelect&lt;T1,T2&gt;多表查询中对主表生效。
        /// <para>固化：返回 3 条，且跨表租户名称正确填充。</para>
        /// </summary>
        [Fact]
        public void T_GlobalFilter_Compose多表传播()
        {
            try
            {
                _fsql.GlobalFilter.Apply<WfProduct>("softDelete", p => p.IsDeleted == false);
                var dtos = _fsql.Select<WfProduct, WfTenant>()
                    .LeftJoin((a, b) => a.TenantId == b.Id)
                    .AsDto()
                    .ToList<WfProductTenantDto>();

                Assert.Equal(3, dtos.Count);
                Assert.DoesNotContain(dtos, d => d.Id == 3);
                Assert.Contains(dtos, d => d.Id == 1 && d.TenantName == "租户A");
            }
            finally
            {
                _fsql.GlobalFilter.Remove("softDelete");
            }
        }

        #region 种子数据

        /// <summary>
        /// 灌入演示种子：3 个分类（自引用）+ 5 个商品。
        /// </summary>
        /// <param name="fsql">FreeSql 实例</param>
        private static void SeedData(IFreeSql fsql)
        {
            fsql.Insert(new List<WiCategory>
            {
                new() { Id = 1, Name = "全部", ParentId = 0 },
                new() { Id = 2, Name = "电子产品", ParentId = 1 },
                new() { Id = 3, Name = "图书", ParentId = 1 },
            }).ExecuteAffrows();

            fsql.Insert(new List<WiItem>
            {
                new() { Id = 1, Name = "笔记本电脑", Price = 5999m, CategoryId = 2 },
                new() { Id = 2, Name = "手机", Price = 3999m, CategoryId = 2 },
                new() { Id = 3, Name = "平板", Price = 2500m, CategoryId = 2 },
                new() { Id = 4, Name = "C#高级编程", Price = 89m, CategoryId = 3 },
                new() { Id = 5, Name = "设计模式", Price = 69m, CategoryId = 3 },
            }).ExecuteAffrows();

            fsql.Insert(new List<WfTenant>
            {
                new() { Id = 1, Name = "租户A" },
                new() { Id = 2, Name = "租户B" },
            }).ExecuteAffrows();

            fsql.Insert(new List<WfProduct>
            {
                new() { Id = 1, Name = "在售商品1", Price = 100m, IsDeleted = false, TenantId = 1 },
                new() { Id = 2, Name = "在售商品2", Price = 200m, IsDeleted = false, TenantId = 1 },
                new() { Id = 3, Name = "已删除商品", Price = 300m, IsDeleted = true, TenantId = 1 },
                new() { Id = 4, Name = "租户B商品", Price = 400m, IsDeleted = false, TenantId = 2 },
            }).ExecuteAffrows();
        }

        #endregion

        #region 实体

        /// <summary>
        /// 演示用分类实体（自引用父级 + 一对多子项）。
        /// </summary>
        [Table(Name = "Wiki_IT_Inc_Category")]
        public class WiCategory
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>父级分类外键（0 表示根）</summary>
            public int ParentId { get; set; }

            /// <summary>父级分类导航属性（自引用）</summary>
            [Navigate(nameof(ParentId))]
            public WiCategory? Parent { get; set; }

            /// <summary>子项集合导航属性（一对多）</summary>
            [Navigate(nameof(WiItem.CategoryId))]
            public List<WiItem>? Items { get; set; }
        }

        /// <summary>
        /// 演示用商品实体。
        /// </summary>
        [Table(Name = "Wiki_IT_Inc_Item")]
        public class WiItem
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>价格</summary>
            public decimal Price { get; set; }

            /// <summary>分类外键</summary>
            public int CategoryId { get; set; }

            /// <summary>分类导航属性（多对一）</summary>
            [Navigate(nameof(CategoryId))]
            public WiCategory? Category { get; set; }
        }

        /// <summary>
        /// 演示用商品实体（含软删除标记与租户字段）。
        /// </summary>
        [Table(Name = "Wiki_IT_Flt_Product")]
        public class WfProduct
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>价格</summary>
            public decimal Price { get; set; }

            /// <summary>软删除标记</summary>
            public bool IsDeleted { get; set; }

            /// <summary>租户标识</summary>
            public int TenantId { get; set; }
        }

        /// <summary>
        /// 演示用租户实体。
        /// </summary>
        [Table(Name = "Wiki_IT_Flt_Tenant")]
        public class WfTenant
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";
        }

        #endregion

        #region DTO

        /// <summary>
        /// 商品基础 DTO — 集合导航的元素类型。
        /// </summary>
        [MapFrom(typeof(WiItem))]
        public class WiItemDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>价格</summary>
            public decimal Price { get; set; }
        }

        /// <summary>
        /// 商品扁平 DTO — 含一级导航跨表字段。
        /// </summary>
        [MapFrom(typeof(WiItem))]
        public class WiItemFlatDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>分类名称 — 经 WiItem.Category 导航取值</summary>
            [MapProperty(typeof(WiCategory), nameof(WiCategory.Name))]
            public string? CategoryName { get; set; }
        }

        /// <summary>
        /// 商品多级导航 DTO — 两级导航路径 Category.Parent.Name。
        /// </summary>
        [MapFrom(typeof(WiItem))]
        public class WiItemDeepNavDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>上级分类名称 — 两级导航路径 Category.Parent.Name</summary>
            [MapProperty(typeof(WiItem), "Category.Parent.Name")]
            public string? ParentCategoryName { get; set; }
        }

        /// <summary>
        /// 分类 + 子项集合 DTO — 集合导航触发 FreeSql 降级路径。
        /// </summary>
        [MapFrom(typeof(WiCategory))]
        public class WiCategoryWithItemsDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>子项集合 — 元素标注 [MapFrom]，触发集合导航降级</summary>
            public List<WiItemDto> Items { get; set; } = new();
        }

        /// <summary>
        /// Compose 双表 DTO — 同时含"T2 直取字段"与"T1 导航依赖字段"，用于验证 F-01。
        /// </summary>
        [MapFrom(typeof(WiItem))]
        public class WiItemComposeDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称 — [Convert] 触发 Compose 降级路径</summary>
            [Convert(typeof(WiNameConverter), nameof(WiNameConverter.Convert))]
            public string Name { get; set; } = "";

            /// <summary>分类名称 — 从 Compose 第二源 WiCategory 直接取值</summary>
            [MapProperty(typeof(WiCategory), nameof(WiCategory.Name))]
            public string? CategoryName { get; set; }

            /// <summary>分类名称 — 经 WiItem.Category 导航取值（降级路径不自动 Include）</summary>
            [MapProperty(typeof(WiItem), "Category.Name")]
            public string? NavCategoryName { get; set; }
        }

        /// <summary>
        /// 名称转换器（静态方法引用形态，T1 模板 M(TField)）— 加前缀标记，用于触发降级路径。
        /// </summary>
        public static class WiNameConverter
        {
            /// <summary>
            /// 执行转换。
            /// </summary>
            /// <param name="fieldValue">源字段值。</param>
            /// <returns>加前缀后的名称。</returns>
            public static string Convert(string? fieldValue)
                => fieldValue == null ? "" : "[Conv]" + fieldValue;
        }

        /// <summary>
        /// 商品 DTO — 无降级触发器，走 SQL 侧投影路径。
        /// </summary>
        [MapFrom(typeof(WfProduct))]
        public class WfProductDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>价格</summary>
            public decimal Price { get; set; }
        }

        /// <summary>
        /// 商品 DTO — 含 [Convert]，强制走降级路径。
        /// </summary>
        [MapFrom(typeof(WfProduct))]
        public class WfProductConvDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称 — ConvertUsing 触发降级</summary>
            [Convert(typeof(WfNameConverter), nameof(WfNameConverter.Convert))]
            public string Name { get; set; } = "";

            /// <summary>价格</summary>
            public decimal Price { get; set; }
        }

        /// <summary>
        /// 商品 + 租户 Compose DTO — 验证过滤器在多表查询中的传播。
        /// </summary>
        [MapFrom(typeof(WfProduct))]
        public class WfProductTenantDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>租户名称 — 从 Compose 第二源直接取值</summary>
            [MapProperty(typeof(WfTenant), nameof(WfTenant.Name))]
            public string? TenantName { get; set; }
        }

        /// <summary>
        /// 名称转换器（静态方法引用形态，T1 模板 M(TField)）— 返回原值，用于触发降级路径。
        /// </summary>
        public static class WfNameConverter
        {
            /// <summary>
            /// 执行转换。
            /// </summary>
            /// <param name="fieldValue">源字段值。</param>
            /// <returns>原值。</returns>
            public static string Convert(string? fieldValue)
                => fieldValue == null ? "" : fieldValue;
        }

        #endregion
    }
}
