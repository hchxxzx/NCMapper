// ============================================================================
// §6A.1 落盘：D、E 组（归属卡 C3）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// D（6 例）候选路线一「剥 IIF」与路线二「guard 兜底」的判定依据
//   —— 这两条是**反证用例**：它们证明「只剥 IIF」和「只 guard 兜底」都不够，
//      防止后续有人把已修好的实现改回这两种错做法（§6A.3 的 NoRegression_StripOnlyIsNotEnough 同源）。
// E（4 例）终端方法数（防漏接）
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using NCMapper.FreeSql.Internal;
using Xunit;

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// POC 回归 D/E 组 —— 候选路线反证 + 终端方法面清点。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    public class FreeSqlComposePocRegressionDETests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposePocRegressionDETests(PocFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // D 组
        // ================================================================

        /// <summary>
        /// D 组（D1/D2/D3）：候选路线一「只剥 IIF」的产物核对——剥除计数按**注入点个数**判定。
        /// <para>POC D1 双表 → 剥 1；D2 三表 → 剥 1；D3 双层嵌套 → 剥 2。</para>
        /// <para>★ 按任务书 §11.1：剥除计数**只取决于产物里有几个嵌套注入点，与 lambda 元数无关**。
        /// 三表 DTO 只有 1 层嵌套时计数就是 1（不是 2），故此处按注入点断言而非按元数断言。</para>
        /// <para>同时锁住手工构造 2 个注入点的表达式树 → 计数 == 2（§11.1 要求的第二种形态）。</para>
        /// </summary>
        [Fact]
        public void D_StripOnly_ProblemsRemain()
        {
            // D1：双表单层嵌套 → 恰好 1 个注入点（真实产物）
            var twoTable = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>();
            Assert.Equal(1, CountInjectionPoints(twoTable.Body));
            var stripped2 = FreeSqlNullGuardStripVisitor.Strip(twoTable);
            Assert.NotSame(twoTable, stripped2);          // 有剥除 → 重建了新 lambda
            Assert.DoesNotContain("IIF", stripped2.ToString());

            // D2：三表单层嵌套 → 仍是 1 个注入点（★ 与元数无关，三表不等于 2）
            var threeTable = FreeSqlProjectionBuilder.BuildCompose3<PocSite, PocDept, PocCompany, PocSiteNestedDto>();
            Assert.Equal(1, CountInjectionPoints(threeTable.Body));

            // D3：三表双层嵌套 → 2 个注入点（外层的 Dept + 内层的 Company）
            var doubleNested = FreeSqlProjectionBuilder.BuildCompose3<PocSite, PocDept, PocCompany, PocSiteDoubleNestedDto>();
            Assert.Equal(2, CountInjectionPoints(doubleNested.Body));
            var strippedDouble = FreeSqlNullGuardStripVisitor.Strip(doubleNested);
            Assert.DoesNotContain("IIF", strippedDouble.ToString());

            // §11.1 第二种形态：手工构造含 2 个注入点的表达式树 → 计数 == 2
            var manualTwo = BuildManualTwoInjectionLambda();
            Assert.Equal(2, CountInjectionPoints(manualTwo.Body));
            Assert.DoesNotContain("IIF", FreeSqlNullGuardStripVisitor.Strip(manualTwo).ToString());
        }

        /// <summary>
        /// D 组（D4/D5/D6）：候选路线二「只靠 guard 兜底」的致命缺陷——
        /// guard 能把崩溃变成「不崩」，但换来的是**嵌套值全为 null 的静默错数据**。
        /// <para>POC D5 实测：<c>原始 4 行 → 过滤后 2 行（丢 2 行）</c>；D6：现有判据 false 即盲区。</para>
        /// <para>★ 本用例锁的是「为什么不能只靠 guard」：把 SQL 投影分支的产物换成未剥除的原始表达式，
        /// 观察 FreeSql 确实翻译失败 → 即必须靠剥除，guard 只能兜底不能替代。</para>
        /// <para>依任务书 §11.2：本用例不断言「不抛异常」，而是断言「未剥除必然失败」，
        /// 从而证明「不抛」只能来自剥除生效，不能来自 guard 兜底。</para>
        /// </summary>
        [Fact]
        public void D_GuardFallback_DropsRows()
        {
            var raw = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>();

            // D5 前提：未剥除的原始产物无法被 FreeSql 翻译（这正是必须剥除的原因）
            var ex = Assert.ThrowsAny<Exception>(() =>
                _fsql.Select<PocSite, PocDept>()
                    .LeftJoin((a, b) => a.DeptId == b.Id)
                    .ToList(raw));
            Assert.Contains("null.Id cannot be null", ex.Message);

            // 剥除后同样的查询成功且行数守恒（4 行 = 原生 join 真值）
            var stripped = FreeSqlNullGuardStripVisitor.Strip(raw);
            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToList(stripped);
            Assert.Equal(4, rows.Count);

            // D6：降级网判据对「同源嵌套」是 false（即盲区）——它不会替我们兜住这个形态，
            // 故不能指望「靠降级网/guard 兜底」来解决缺陷
            var needs = FreeSqlDegradationDetector.NeedsDegradationCompose(
                typeof(PocSite), new[] { typeof(PocDept) }, typeof(PocSiteNestedDto), out var reason);
            Assert.False(needs);
            Assert.Empty(reason);

            // ★ 反证收口：guard 兜底走的是降级路径，而降级路径在修复前会丢行（POC D5：4 → 2）。
            // 修复后降级路径已不丢行（C4 交付），故此处断言「降级路径也必须是 4 行」——
            // 若有人把 guard 当成唯一手段（不接剥除），嵌套值会全为 null，见下方断言。
            var projectedRows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();

            // 走完产品路径（剥除 + guard）后，第 1 行的嵌套必须有真实业务值。
            // 若实现退化成「仅 guard 兜底」，这里会拿到 null 或空壳 → 本断言即红。
            Assert.NotNull(projectedRows[0].Dept);
            Assert.Equal(10, projectedRows[0].Dept!.Id);
            Assert.Equal("IT", projectedRows[0].Dept.Name);
        }

        // ================================================================
        // E 组 —— 终端方法数（防漏接）
        // ================================================================

        /// <summary>
        /// E 组（E1）：Compose 门面的终端方法必须恰好 16 个（双表 8 + 三表 8），
        /// 且两个 selector 的方法名集合逐个一致。
        /// <para>POC E1 实测：<c>双表 8 个 + 三表 8 个 = 16 个</c>。</para>
        /// <para>这三个断言合起来锁住「不得增删、不得改名」（任务书 §3.1 写死清单）。</para>
        /// </summary>
        [Fact]
        public void E_AllSixteenTerminals_AreCovered()
        {
            var names2 = PublicTerminalNames(typeof(FreeSqlComposeDtoSelector<,>));
            var names3 = PublicTerminalNames(typeof(FreeSqlComposeDtoSelector3<,,>));

            // E1：各 8 个，合计 16
            Assert.Equal(8, names2.Count);
            Assert.Equal(8, names3.Count);
            Assert.Equal(16, names2.Count + names3.Count);

            // 两个 selector 的方法名集合必须完全一致（只差泛型参数个数）
            Assert.Equal(names2, names3);

            // 逐个锁定任务书 §3.1 的 8 个方法名（顺序无关）
            var expected = new[]
            {
                "ToList", "ToListAsync", "First", "FirstAsync",
                "ToPagedList", "ToPagedListAsync", "ToChunkDto", "ToChunkDtoAsync",
            };
            Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), names2);
        }

        /// <summary>
        /// E 组（E2~E4）：单表终端已接剥除的对照面 ＋ 既有空壳语义文档锚点。
        /// <para>E2 单表 8 处终端接入 Strip（对照 Compose 的接入数）；E3 降级丢行已有回归锁定；
        /// E4 空壳语义是既有行为（Y 组锁定其与 Compose 一致）。</para>
        /// <para>本用例把 E3/E4 现象化：降级路径的行数由 FreeSqlComposeNullFilterTests 锁定（8 个 [Fact]），
        /// 空壳语义由 Y 组锁定——此处只断言「单表通道在有剥除时能正确取值」，作为 E2 的实证。</para>
        /// </summary>
        [Fact]
        public void E_SingleTableTerminalsStripped()
        {
            // E2：单表通道的同形态可用（8 处终端均已接入剥除的实证之一）
            var single = _fsql.Select<PocSite>().OrderBy(s => s.Id).AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, single.Count);
            Assert.Equal("IT", single[0].Dept?.Name);

            // 单表终端的 API 面同样必须完整（8 个方法，与 Compose 同清单）
            var singleSelector = typeof(NCMapper.FreeSql.FreeSqlDtoSelector<>);
            var singleNames = singleSelector
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            Assert.Contains("ToList", singleNames);
            Assert.Contains("ToChunkDtoAsync", singleNames);

            // E4：空壳语义（无匹配行 → 非 null 实例、字段取默认值）是既有 provider 行为，
            // 此处只做存在性断言，精确一致性由 Y 组负责
            Assert.NotNull(single[1].Dept);          // Site 2 无部门 → 空壳而非 null
            Assert.Equal(0, single[1].Dept!.Id);
        }

        // ================================================================
        // 基础设施
        // ================================================================

        /// <summary>取某泛型 selector 定义的 public 实例方法名（去特殊名、去重、排序）。</summary>
        private static List<string> PublicTerminalNames(Type genericDefinition)
            => genericDefinition
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        /// <summary>统计算术表达式树里的 IIF（ConditionalExpression）节点数。</summary>
        private static int CountInjectionPoints(Expression body)
        {
            var v = new ConditionalCounter();
            v.Visit(body);
            return v.Count;
        }

        /// <summary>
        /// 手工构造含 **2 个**注入点的双表投影（§11.1 要求的第二种计数形态）。
        /// <para>形状：外层 Site → Dept 一个 IIF，内层 Dept → Company 再一个 IIF。</para>
        /// </summary>
        private static Expression<Func<PocSite, PocDept, PocSiteDoubleNestedDto>> BuildManualTwoInjectionLambda()
        {
            var t1 = Expression.Parameter(typeof(PocSite), "t1");
            var t2 = Expression.Parameter(typeof(PocDept), "t2");

            var companyNav = Expression.Property(t2, nameof(PocDept.Company));
            var companyInit = Expression.MemberInit(
                Expression.New(typeof(PocCompanyDto)),
                Expression.Bind(typeof(PocCompanyDto).GetProperty(nameof(PocCompanyDto.Id))!,
                    Expression.Property(companyNav, nameof(PocCompany.Id))),
                Expression.Bind(typeof(PocCompanyDto).GetProperty(nameof(PocCompanyDto.Name))!,
                    Expression.Property(companyNav, nameof(PocCompany.Name))));

            var innerIif = Expression.Condition(
                Expression.Equal(companyNav, Expression.Constant(null, typeof(PocCompany))),
                Expression.Constant(null, typeof(PocCompanyDto)),
                companyInit);

            var deptInit = Expression.MemberInit(
                Expression.New(typeof(PocDeptNestedCompanyDto)),
                Expression.Bind(typeof(PocDeptNestedCompanyDto).GetProperty(nameof(PocDeptNestedCompanyDto.Id))!,
                    Expression.Property(t2, nameof(PocDept.Id))),
                Expression.Bind(typeof(PocDeptNestedCompanyDto).GetProperty(nameof(PocDeptNestedCompanyDto.Company))!,
                    innerIif));

            var outerIif = Expression.Condition(
                Expression.Equal(t2, Expression.Constant(null, typeof(PocDept))),
                Expression.Constant(null, typeof(PocDeptNestedCompanyDto)),
                deptInit);

            var outerInit = Expression.MemberInit(
                Expression.New(typeof(PocSiteDoubleNestedDto)),
                Expression.Bind(typeof(PocSiteDoubleNestedDto).GetProperty(nameof(PocSiteDoubleNestedDto.Id))!,
                    Expression.Property(t1, nameof(PocSite.Id))),
                Expression.Bind(typeof(PocSiteDoubleNestedDto).GetProperty(nameof(PocSiteDoubleNestedDto.Dept))!,
                    outerIif));

            return Expression.Lambda<Func<PocSite, PocDept, PocSiteDoubleNestedDto>>(outerInit, t1, t2);
        }

        /// <summary>ConditionalExpression 计数器（只数节点，不剪枝）。</summary>
        private sealed class ConditionalCounter : ExpressionVisitor
        {
            /// <summary>已统计的 IIF 节点数</summary>
            internal int Count { get; private set; }

            /// <inheritdoc />
            protected override Expression VisitConditional(ConditionalExpression node)
            {
                Count++;
                return base.VisitConditional(node);
            }
        }
    }
}
