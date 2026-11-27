// FreeSql Compose 通道：纯同源嵌套引用 DTO 的运行期判别
// 结论（实跑确证）：双表/三表 Compose 门面均抛 FreeSql 内部异常「null.Id cannot be null」，
//   且 Compose 通道零 guard 兜底 —— 异常原样冒到调用方（单表通道同形态已被剥除器修复，见对照用例）。
// 成因：共享内核 ProjectionExpressionFactory 的嵌套投影 null 传播 IIF 注入点对 Compose 产物同样生效，
//   而 Compose 门面未接入 FreeSqlNullGuardStripVisitor（单表 8 处终端接入、Compose 零接入）。
// 本文件两类用例：
//   ① 前置条件 + 对照（恒绿）：证明「降级网放行」「单表同形态可用」「两通道分叉」三件事实；
//   ② 缺陷复现（Skip 待修复）：保留可直接启用的回归锚，修复落地后去掉 Skip 即转绿。
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.FreeSql.Internal;
using Xunit;

namespace NCMapper.IntegrationTests.ComposeNestedModels
{
    /// <summary>
    /// 嵌套引用 DTO —— 纯同源（主源 = 导航目标类型 FsCategory）。
    /// <para>必须标注 [MapFrom]：共享内核嵌套投影有主源一致性校验，
    /// 未标注或主源不匹配时嵌套投影被静默跳过（不炸，但也就测不到该形态）。</para>
    /// <para>不得标注任何跨表 [MapProperty]：一旦带跨表字段即被既有降级网拦截，
    /// 测到的是内存合成路径而非 Compose 投影路径，结论无效。</para>
    /// </summary>
    [MapFrom(typeof(FsCategory))]
    public class FsComposeNestedCategoryDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>分类名</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// Compose 目标 DTO —— 含一个纯同源嵌套引用 DTO 属性，无跨表 [MapProperty]。
    /// <para>这是唯一能穿透 Compose 降级网、进入嵌套投影构建的形态。</para>
    /// </summary>
    [MapFrom(typeof(FsTopic))]
    public class FsComposeNestedTargetDto
    {
        /// <summary>主键</summary>
        public int Id { get; set; }
        /// <summary>标题</summary>
        public string Title { get; set; } = "";
        /// <summary>分类 — 取自 FsTopic.Category 引用导航，同源嵌套投影</summary>
        public FsComposeNestedCategoryDto? Category { get; set; }
    }
}

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.ComposeNestedModels;

    /// <summary>
    /// Compose 通道纯同源嵌套投影判别 —— 前置条件、通道对照与缺陷复现。
    /// <para>数据基线来自共享 FreeSqlTestFixture：5 条 Topic（4 条 IsDeleted=false）＋ 5 条 Category。</para>
    /// </summary>
    public class FreeSqlComposeNestedSameSourceTests : IClassFixture<FreeSqlTestFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 从共享 Fixture 获取 FreeSql 实例</summary>
        /// <param name="fixture">共享测试 Fixture</param>
        public FreeSqlComposeNestedSameSourceTests(FreeSqlTestFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // 一、前置条件与通道对照（恒绿）
        // ================================================================

        /// <summary>
        /// 前置条件：该形态必须穿透 Compose 降级网。
        /// <para>若此处为 true，后续用例测到的就是内存合成路径，判别结论无效——
        /// 故本断言是全部结论的成立前提，不做则不成立。</para>
        /// </summary>
        [Fact]
        public void Compose_同源嵌套_未被降级网拦截()
        {
            var needs = FreeSqlDegradationDetector.NeedsDegradationCompose(
                typeof(FsTopic), new[] { typeof(FsCategory) }, typeof(FsComposeNestedTargetDto), out var reason);

            Assert.False(needs, $"纯同源嵌套引用 DTO 不应触发 Compose 降级，实际原因: {reason}");
            Assert.Empty(reason);
        }

        /// <summary>
        /// 对照：同一目标 DTO 走单表通道正常。
        /// <para>单表终端已接入 IIF 剥除（8 处），故同形态可用；这与 Compose 通道的表现相反，
        /// 即「同一病在两通道一治一未治」的实证。</para>
        /// </summary>
        [Fact]
        public void 单表通道_同源嵌套_正常取值()
        {
            // 单表同样不得被降级网拦截，否则对照不成立
            var needsDegrade = FreeSqlDegradationDetector.NeedsDegradation(
                typeof(FsTopic), typeof(FsComposeNestedTargetDto), out var reason);
            Assert.False(needsDegrade, $"单表对照不应降级，实际原因: {reason}");

            var dtos = _fsql.Select<FsTopic>()
                .Where(t => !t.IsDeleted)
                .OrderBy(t => t.Id)
                .AsDto()
                .ToList<FsComposeNestedTargetDto>();

            Assert.Equal(4, dtos.Count);
            Assert.Equal("NCMapper 入门", dtos[0].Title);
            Assert.NotNull(dtos[0].Category);
            Assert.Equal(".NET", dtos[0].Category!.Name);
        }

        /// <summary>
        /// 对照：剥除逻辑对多参表达式同样成立（修复方向的可行性证据）。
        /// <para>本用例不改产品代码，仅在测试侧把剥除判据施加到双参 Compose lambda 的 Body 上，
        /// 验证「剥掉 IIF 后 FreeSql 可正常翻译并返回正确数据」。结论：剥除判据本身与元数无关
        /// （只遍历 Body），但现有剥除入口是单参强类型签名，需另增双参/三参重载。</para>
        /// </summary>
        [Fact]
        public void 对照实验_剥除后双表Compose可翻译()
        {
            var expr = FreeSqlProjectionBuilder.BuildCompose<FsTopic, FsCategory, FsComposeNestedTargetDto>();

            var stripper = new TestSideNullGuardStripper();
            var strippedBody = stripper.Visit(expr.Body)!;
            Assert.Equal(1, stripper.StrippedCount); // 恰好剥掉注入点那一处 IIF

            var delegateType = System.Linq.Expressions.Expression.GetDelegateType(
                typeof(FsTopic), typeof(FsCategory), typeof(FsComposeNestedTargetDto));
            var stripped = (System.Linq.Expressions.Expression<Func<FsTopic, FsCategory, FsComposeNestedTargetDto>>)
                System.Linq.Expressions.Expression.Lambda(delegateType, strippedBody, expr.Parameters);

            var dtos = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .ToList(stripped);

            Assert.Equal(4, dtos.Count);
            Assert.Equal(".NET", dtos[0].Category?.Name);
            Assert.Equal("Python", dtos[2].Category?.Name);
        }

        /// <summary>
        /// 测试侧复刻：与 FreeSqlNullGuardStripVisitor 同判据（Conditional + 空常量 + MemberInit），
        /// 用于验证剥除逻辑对双参表达式的适用性。产品代码零改动。
        /// </summary>
        private sealed class TestSideNullGuardStripper : System.Linq.Expressions.ExpressionVisitor
        {
            /// <summary>已剥除的 Conditional 节点数</summary>
            internal int StrippedCount { get; private set; }

            /// <inheritdoc />
            protected override System.Linq.Expressions.Expression VisitConditional(
                System.Linq.Expressions.ConditionalExpression node)
            {
                var visited = (System.Linq.Expressions.ConditionalExpression)base.VisitConditional(node);

                if (visited.IfTrue is System.Linq.Expressions.ConstantExpression t
                    && t.Value is null && t.Type == visited.Type
                    && visited.IfFalse is System.Linq.Expressions.MemberInitExpression)
                {
                    StrippedCount++;
                    return visited.IfFalse;
                }

                if (visited.IfFalse is System.Linq.Expressions.ConstantExpression f
                    && f.Value is null && f.Type == visited.Type
                    && visited.IfTrue is System.Linq.Expressions.MemberInitExpression)
                {
                    StrippedCount++;
                    return visited.IfTrue;
                }

                return visited;
            }
        }

        // ================================================================
        // 二、缺陷复现（Skip 待修复 —— 去掉 Skip 即成为修复验收用例）
        // ================================================================

        /// <summary>
        /// 双表 Compose：纯同源嵌套引用 DTO 正常实译实跑（TB-20261127-01 修复后转绿）。
        /// <para>原 POC A5：<c>System.Exception: FreeSql: null.Id cannot be null</c>（Compose 通道未接
        /// IIF 剥除且无兜底）→ 修复后：剥除器 2 参重载接入 + guard 兜底，正常返回 4 行。</para>
        /// <para>断言不只「不抛异常」——同时断言嵌套 DTO 的业务值（Category.Id / Category.Name），
        /// 依任务书 §11.2：只断言不抛会让「剥除没接、仅靠 guard 兜底」的退化实现过关。</para>
        /// </summary>
        [Fact]
        public void Compose_双表_同源嵌套_实译实跑()
        {
            var dtos = _fsql.Select<FsTopic, FsCategory>()
                .LeftJoin((a, b) => a.CategoryId == b.Id)
                .Where((a, b) => !a.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToList<FsComposeNestedTargetDto>();

            Assert.Equal(4, dtos.Count);
            Assert.NotNull(dtos[0].Category);
            Assert.Equal(1, dtos[0].Category!.Id);
            Assert.Equal(".NET", dtos[0].Category.Name);
            Assert.Equal("Python", dtos[2].Category!.Name);
        }

        /// <summary>
        /// 三表 Compose：同形态正常实译实跑（TB-20261127-01 修复后转绿，与双表同根因）。
        /// <para>原 POC A6：<c>System.Exception: FreeSql: null.Id cannot be null</c> → 修复后正常返回 4 行。</para>
        /// </summary>
        [Fact]
        public void Compose_三表_同源嵌套_实译实跑()
        {
            var dtos = _fsql.Select<FsTopic, FsCategory, FsCategoryType>()
                .LeftJoin((a, b, c) => a.CategoryId == b.Id)
                .LeftJoin((a, b, c) => b.ParentId == c.Id)
                .Where((a, b, c) => !a.IsDeleted)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToList<FsComposeNestedTargetDto>();

            Assert.Equal(4, dtos.Count);
            Assert.NotNull(dtos[0].Category);
            Assert.Equal(".NET", dtos[0].Category!.Name);
        }
    }
}
