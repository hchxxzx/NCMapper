using System.IO;
using Xunit;
using FreeSql;
using NCMapper.FreeSql;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// Compose 降级路径导航依赖检测 Trace 守卫
    /// <para>覆盖：双表/三表 Compose 降级时 ApplyFreeSqlComposeIncludes 的 Trace 输出行为</para>
    /// <para>数据基线（共享 FreeSqlTestFixture）：5 条 Topic（4 条 IsDeleted=false）+ 对应 Category/CategoryType</para>
    /// <para>P1 修复说明：ApplyFreeSqlComposeIncludes 的精确检测触发条件已从 FromField.Contains('.')
    /// 改为 fieldSourceType != T1（与 FreeSqlNavigationCollector.Collect 一致）。修复后，合法跨表 DTO
    /// （如 FsTopicNavDepDto.CategoryName 经 [MapProperty(typeof(FsCategory),"Name")]）会触发精确依赖串；
    /// 纯 T1 字段 DTO（FsTopicPureT1Dto）因导航树为空、ApplyFreeSqlComposeIncludes 提前 return，不产生任何导航依赖 Trace。
    /// 本测试正向验证精确分支可达、反向验证纯 T1 DTO 不误报。</para>
    /// </summary>
    [Collection("MapTracerGlobalState")]
    public class FreeSqlComposeNavDetectTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 构造 — 从共享 Fixture 获取 FreeSql 实例
        /// </summary>
        /// <param name="fixture">共享测试 Fixture</param>
        public FreeSqlComposeNavDetectTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

        /// <summary>
        /// 双表 Compose 降级 — 合法跨表 DTO 触发精确依赖串（P1 修复验证：正向）。
        /// 验证：FsTopicNavDepDto.CategoryName（[MapProperty(typeof(FsCategory),"Name")]）经 FsTopic.Category 导航解析，
        /// 触发精确分支，输出"字段依赖源实体的导航属性"+ 精确依赖串"FsTopicNavDepDto.CategoryName 依赖 FsCategory.Name"；
        /// 降级路径确实执行（Content 经 ConvertUsing 带 [Conv] 前缀）；else 兜底串"自动 Include 不支持多表"不出现。
        /// </summary>
        [Fact]
        public void NT003_双表降级_合法跨表DTO触发精确依赖串()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = _fsql.Select<FsTopic, FsCategory>()
                    .LeftJoin((a, b) => a.CategoryId == b.Id)
                    .Where((a, b) => !a.IsDeleted)
                    .OrderBy((a, b) => a.Id)
                    .AsDto()
                    .ToList<FsTopicNavDepDto>();

                Assert.True(dtos.Count > 0);
                // 降级路径确实执行（ConvertUsing 生效）
                Assert.Contains("[Conv]", dtos[0].Content);

                var output = sw.ToString();
                // 精确分支触发：精确依赖串出现
                Assert.Contains("字段依赖源实体的导航属性", output);
                Assert.Contains("FsTopicNavDepDto.CategoryName 依赖 FsCategory.Name", output);
                // else 兜底分支不出现（证明走的是精确分支而非兜底）
                Assert.DoesNotContain("自动 Include 不支持多表", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// 双表 Compose 降级 — 纯 T1 字段 DTO 不产生任何导航依赖 Trace（P1 修复验证：反向）。
        /// 验证：FsTopicPureT1Dto 所有字段均来自 T1（FsTopic）本身，无跨表 [MapProperty]，导航树为空，
        /// ApplyFreeSqlComposeIncludes 提前 return；降级路径仍执行（Content 带 [Conv] 前缀），但精确依赖串与
        /// else 兜底串均不出现（证明修复精确，不误报纯 T1 字段）。
        /// </summary>
        [Fact]
        public void NT003_双表降级_纯T1字段DTO不触发依赖Trace()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = _fsql.Select<FsTopic, FsCategory>()
                    .LeftJoin((a, b) => a.CategoryId == b.Id)
                    .Where((a, b) => !a.IsDeleted)
                    .OrderBy((a, b) => a.Id)
                    .AsDto()
                    .ToList<FsTopicPureT1Dto>();

                Assert.True(dtos.Count > 0);
                // 降级路径确实执行（ConvertUsing 生效）
                Assert.Contains("[Conv]", dtos[0].Content);

                var output = sw.ToString();
                // 纯 T1 字段无跨表依赖 → 不输出任何导航依赖警告
                Assert.DoesNotContain("字段依赖源实体的导航属性", output);
                Assert.DoesNotContain("自动 Include 不支持多表", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// 三表 Compose 降级 — 合法跨表 DTO 触发精确依赖串（P1 修复验证：正向，三表变体）。
        /// </summary>
        [Fact]
        public void NT003_三表降级_合法跨表DTO触发精确依赖串()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                    .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                    .LeftJoin((a, b, c) => b.ParentId == c.Id)
                    .Where((a, b, c) => !a.IsDeleted)
                    .OrderBy((a, b, c) => a.Id)
                    .AsDto()
                    .ToList<FsTopicNavDepDto>();

                Assert.True(dtos.Count > 0);
                Assert.Contains("[Conv]", dtos[0].Content);

                var output = sw.ToString();
                Assert.Contains("字段依赖源实体的导航属性", output);
                Assert.Contains("FsTopicNavDepDto.CategoryName 依赖 FsCategory.Name", output);
                Assert.DoesNotContain("自动 Include 不支持多表", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// 三表 Compose 降级 — 纯 T1 字段 DTO 不产生任何导航依赖 Trace（P1 修复验证：反向，三表变体）。
        /// </summary>
        [Fact]
        public void NT003_三表降级_纯T1字段DTO不触发依赖Trace()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var dtos = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                    .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                    .LeftJoin((a, b, c) => b.ParentId == c.Id)
                    .Where((a, b, c) => !a.IsDeleted)
                    .OrderBy((a, b, c) => a.Id)
                    .AsDto()
                    .ToList<FsTopicPureT1Dto>();

                Assert.True(dtos.Count > 0);
                Assert.Contains("[Conv]", dtos[0].Content);

                var output = sw.ToString();
                Assert.DoesNotContain("字段依赖源实体的导航属性", output);
                Assert.DoesNotContain("自动 Include 不支持多表", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }
    }
}
