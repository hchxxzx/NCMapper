// ============================================================================
// §6A.1 落盘：T、V、W 组（归属卡 C3）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// T（4 例）单表链 vs Compose 腿不一致 —— 缺陷④的现场
// V（5 例）IIF 能力边界（机理）—— 为什么必须剥除
// W（6 例）只剥 IIF 不足（防回退到路线一）—— 为什么还必须改指 join 源参数
//
// ★ 本文件是「为什么修法必须是路线四」的完整机理链：
//   V 证明 IIF 包对象不可译 → 必须剥；
//   W 证明剥了之后三个业务问题仍在 → 光剥不够；
//   T 证明单表与 Compose 对同一份元数据结果不同 → 必须让两边一致。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using NCMapper.FreeSql.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// POC 回归 T/V/W 组 —— 缺陷④现场 ＋ IIF 能力边界 ＋ 「只剥不够」的反证。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    public class FreeSqlComposePocRegressionTVWTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposePocRegressionTVWTests(PocFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // T 组 —— 单表链 vs Compose 腿不一致（缺陷④）
        // ================================================================

        /// <summary>
        /// T 组（T1~T4）：同一份「未声明嵌套」的元数据，单表链与 Compose 腿的结果必须一致。
        /// <para>POC T1（单表，未声明 Dept）：<c>含IIF=True 含Dept=True | src =&gt; new FluentSiteNestedDto()
        /// {Id = src.Id, Name = src.Name, Dept = IIF((src.Dept == null), null, new FluentDeptDto(){...})}</c>；
        /// T2（Compose，同一份元数据）：<c>含IIF=False 含Dept=False | (t1, t2) =&gt; {Id = t1.Id, Name = t1.Name}</c>
        /// —— **同一份元数据，单表出嵌套、Compose 不出**，这就是缺陷④。
        /// T3/T4：单表实跑 3 行、特性通道单表对照 3 行。</para>
        /// <para>★ 本用例的断言方向：单表链**必须**含 Dept（这是既有正确行为，是「参照系」）；
        /// Compose 腿在 C2 之后，当嵌套 DTO 已在注册表时也**必须**含 Dept。
        /// 两侧一致即为缺陷④消除的直接证据。</para>
        /// </summary>
        [Fact]
        public void T_SingleVsCompose_MustBeConsistent()
        {
            // 前提：嵌套对已注册（「未在 Properties 声明」但「DTO 已注册」的形态 —— 缺陷④的触发条件）
            RegisterDeptSnapshotForSingleVsCompose();

            // T1：单表链 —— 参照系，含 Dept 与 IIF（src.Dept 导航）
            var single = FreeSqlProjectionBuilder.Build<PocSite, PocCompanyNestedDto>();
            var singleText = single.ToString();
            Assert.Contains("Dept", singleText);
            Assert.Contains("IIF", singleText);

            // T2：Compose 腿 —— 同一份元数据，C2 修复后也必须含 Dept
            var compose = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocCompanyNestedDto>();
            var composeText = compose.ToString();
            Assert.Contains("Dept", composeText);
            Assert.Contains("IIF", composeText);

            // ★ 两侧都进嵌套 = 缺陷④（单表出、Compose 不出）已消除
            // 差异只在源表达式：单表取 src.Dept 导航，Compose 取用户 join 的 t2（路线四）
            Assert.Contains("t2", composeText);
            Assert.DoesNotContain("t1.Dept", composeText);

            // T3/T4：单表实跑与 Compose 实跑，行数都必须等于原生
            var native = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id).Count();
            var singleRows = _fsql.Select<PocSite>().OrderBy(s => s.Id).AsDto().ToList<PocCompanyNestedDto>();
            var composeRows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocCompanyNestedDto>();

            Assert.Equal(4, singleRows.Count);
            Assert.Equal(native, composeRows.Count);

            // 同一条站点在两路上的嵌套值必须一致（第 1 行 IT）
            Assert.Equal(singleRows[0].Dept?.Name, composeRows[0].Dept?.Name);
            Assert.Equal("IT", composeRows[0].Dept?.Name);
        }

        // ================================================================
        // V 组 —— IIF 能力边界（机理）
        // ================================================================

        /// <summary>
        /// V 组（V1/V2）：纯 MemberInit 可译；IIF 包**对象**（内核产物原形态）不可译。
        /// <para>POC V1：<c>①纯 MemberInit（无 IIF）→ 可译 | 3 行 | 1:F-IT, 2:&lt;empty&gt;, 3:&lt;empty&gt;</c>；
        /// V2：<c>②IIF(t2==null, null, MemberInit) ← 内核产物 → 炸 Exception: FreeSql: null.Id cannot be null</c>。</para>
        /// <para>结论：爆炸与 IIF 本身无关，而与「IIF 包裹对象」有关 —— 这是剥除器存在的前提。</para>
        /// </summary>
        [Fact]
        public void V_IIFWithObject_NotTranslatable()
        {
            // V1：纯 MemberInit（无 IIF）→ 可译且实跑 4 行
            var pureMemberInit = BuildManualNested(lambdaUsesIif: false);
            var rows1 = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList(pureMemberInit);
            Assert.Equal(4, rows1.Count);
            Assert.Equal("IT", rows1[0].Dept?.Name);

            // V2：IIF 包对象 → FreeSql 翻译失败（异常信息即 POC 记录的那条）
            var withIif = BuildManualNested(lambdaUsesIif: true);
            var ex = Assert.ThrowsAny<Exception>(() =>
                _fsql.Select<PocSite, PocDept>()
                    .LeftJoin((a, b) => a.DeptId == b.Id)
                    .ToList(withIif));
            Assert.Contains("null.Id cannot be null", ex.Message);

            // 对照：把 V2 那份表达式剥掉 IIF 后又可译（V1 形态），证明「炸点就是 IIF 包对象」
            var stripped = FreeSqlNullGuardStripVisitor.Strip(withIif);
            var rows2 = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList(stripped);
            Assert.Equal(4, rows2.Count);
            Assert.Equal("IT", rows2[0].Dept?.Name);
        }

        /// <summary>
        /// V 组（V4/V5）：反转形态与两层嵌套同样不可译 —— 证明判据必须**对称**处理
        /// （剥除器同时覆盖 IfTrue/IfFalse 两种常量 null 位置）。
        /// <para>POC V4：<c>④反转 IIF(t2==null, MemberInit, null) → 炸</c>；
        /// V5：<c>⑤两层 IIF 嵌套 → 炸</c>。</para>
        /// <para>★ 剥除器对 V4 的「防御性对称形态」有专门分支（产物的 IfTrue 分支），
        /// 本用例把该分支纳入回归覆盖。</para>
        /// </summary>
        [Fact]
        public void V_IIFNestedForms_NotTranslatable()
        {
            // V4：反转形态（MemberInit 在 IfTrue，null 在 IfFalse）
            var reversed = BuildManualReversedIif();
            var v = new ConditionalCounter();
            v.Visit(reversed.Body);
            Assert.Equal(1, v.Count);   // 确有 1 个 IIF

            var ex4 = Assert.ThrowsAny<Exception>(() =>
                _fsql.Select<PocSite, PocDept>()
                    .LeftJoin((a, b) => a.DeptId == b.Id)
                    .ToList(reversed));
            Assert.Contains("null.Id cannot be null", ex4.Message);

            // 剥除器必须也覆盖反转形态（防御性对称分支）
            var strippedReversed = FreeSqlNullGuardStripVisitor.Strip(reversed);
            Assert.DoesNotContain("IIF", strippedReversed.ToString());
            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList(strippedReversed);
            Assert.Equal(4, rows.Count);

            // V5：两层 IIF 嵌套（内外各一层）→ 未剥时同样不可译；剥 2 处后可译
            var twoLayer = BuildManualTwoLayerIif();
            var v5 = new ConditionalCounter();
            v5.Visit(twoLayer.Body);
            Assert.Equal(2, v5.Count);

            var ex5 = Assert.ThrowsAny<Exception>(() =>
                _fsql.Select<PocSite, PocDept>()
                    .LeftJoin((a, b) => a.DeptId == b.Id)
                    .ToList(twoLayer));
            Assert.Contains("null.Id cannot be null", ex5.Message);

            // ★ 两层形态必须剥 2 处（每层独立累加），剥完不含 IIF
            var strippedTwo = FreeSqlNullGuardStripVisitor.Strip(twoLayer);
            Assert.DoesNotContain("IIF", strippedTwo.ToString());
        }

        // ================================================================
        // W 组 —— 只剥 IIF 不够（防回退到路线一）
        // ================================================================

        /// <summary>
        /// W 组（W1~W3）：**只剥 IIF 不接改指**，三个业务问题逐条仍在。
        /// <para>POC W1（join 带软删过滤）：<c>剥1 | 实际=1:IT, 2:, 3:, 4:HR-DELETED |
        /// 按join应为=1:IT, 2:&lt;null&gt;, 3:&lt;null&gt;, 4:&lt;null&gt;</c> —— 软删部门泄漏；
        /// W2（T1 无导航）：<c>剥0 | 实际=1:&lt;null&gt; | 期望含 CUST-1 而非 &lt;null&gt;</c>；
        /// W3（非主键 join）：<c>剥1 | 实际=1:区A | 按join应为=1:区B</c>。</para>
        /// <para>★ 本用例的断言方式是「用剥除后的**旧形态**表达式（嵌套源仍取 t1.Dept 导航）
        /// 实跑，断言三个问题仍复现」—— 从而证明：光剥不够，必须同时改指 join 源参数。
        /// 若有人把实现改回「只剥 IIF」，产品路径的行为就会退化成这里的读数，
        /// 而 B/H 组（正向）立即变红。</para>
        /// </summary>
        [Fact]
        public void W_StripOnly_AllThreeProblemsRemain()
        {
            // W1：软删泄漏 —— 嵌套源取 t1.Dept 导航（自建 JOIN），join 上的 !b.IsDeleted 对它无效
            var stripOnly = BuildManualNavSourceNested();
            Assert.DoesNotContain("IIF", stripOnly.ToString());   // 已剥（无 IIF）

            var leakRows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id && !b.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .ToList(stripOnly);

            Assert.Equal(4, leakRows.Count);
            // ★ 问题复现：软删部门 HR-DELETED 仍然出现（应不出现）
            Assert.Equal("HR-DELETED", leakRows[3].Dept?.Name);

            // W3：非主键 join —— 业务码 join 到区B，但导航外键指向区A
            var storeStripOnly = BuildManualStoreNavNested();
            var storeRows = _fsql.Select<PocStore, PocRegion>()
                .LeftJoin((a, b) => a.RegionCode == b.Code)
                .OrderBy((a, b) => a.Id)
                .ToList(storeStripOnly);
            Assert.Single(storeRows);
            // ★ 问题复现：取到区A（应取区B）
            Assert.Equal("区A", storeRows[0].Region?.Label);
        }

        /// <summary>
        /// W 组（W4）：只剥 IIF 时 SQL 会出现**两个** LEFT JOIN（自建的那条仍在）。
        /// <para>POC W4：<c>LEFT JOIN 数=2 → 多出了自建 JOIN</c>。</para>
        /// <para>★ 这是最机械的判据：只数 SQL 里的 LEFT JOIN 个数即可区分「路线一」与「路线四」。
        /// 路线四正确实现只有 1 个（用户写的那个，见 B_NestedSelfJoin_CountMustBeOne）。</para>
        /// </summary>
        [Fact]
        public void W_StripOnly_StillTwoJoins()
        {
            // 旧形态（嵌套源取 t1.Dept 导航，已剥 IIF）→ SQL 里有 2 个 LEFT JOIN
            var stripOnly = BuildManualNavSourceNested();
            var sqlOld = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToSql(stripOnly);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sqlOld, "LEFT JOIN").Count);
            Assert.Contains("__Dept", sqlOld);   // 自建 JOIN 的别名

            // 路线四正确实现（产品路径）→ 只有 1 个 LEFT JOIN
            var correct = FreeSqlNullGuardStripVisitor.Strip(
                FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>());
            var sqlNew = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToSql(correct);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(sqlNew, "LEFT JOIN").Count);
            Assert.DoesNotContain("__Dept", sqlNew);
        }

        /// <summary>
        /// W 组（W5/W6）：只剥 IIF 的空壳语义与行数一致性。
        /// <para>POC W5：<c>1:DeptNull=False,Name='IT', 2:DeptNull=False,Name='', 3:DeptNull=False,Name='',
        /// 4:DeptNull=False,Name='HR-DELETED'</c>；
        /// W6：<c>原生join=4 投影后=4 差=0</c>。</para>
        /// <para>★ W5 揭示了剥除路线的**本质缺陷**：空壳让调用方无法用 <c>Dept != null</c> 判断有无关联
        /// （第 2/3 行都有非 null 的空壳），这才是必须改指 join 源参数的业务动因。</para>
        /// </summary>
        [Fact]
        public void W_StripOnly_EmptyShellSemantics()
        {
            var stripOnly = BuildManualNavSourceNested();
            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList(stripOnly);

            // W6：行数一致（剥除路线本身不丢行）
            Assert.Equal(4, rows.Count);

            // W5：空壳语义 —— 全部 4 行 Dept 都非 null；无匹配行是 Id=0/Name 空 的空壳
            Assert.All(rows, r => Assert.NotNull(r.Dept));
            Assert.Equal("IT", rows[0].Dept!.Name);
            Assert.Equal(0, rows[1].Dept!.Id);
            Assert.Equal(0, rows[2].Dept!.Id);
            Assert.Equal("HR-DELETED", rows[3].Dept!.Name);

            // ★ 业务后果：用 Dept != null 判断「有无部门」会误判（4 行全算有，实际只有 2 行有真部门）
            var falsePositives = rows.Count(r => r.Dept != null);
            Assert.Equal(4, falsePositives);
            var trulyHasDept = rows.Count(r => r.Dept != null && r.Dept.Id != 0);
            Assert.Equal(2, trulyHasDept);
            Assert.NotEqual(falsePositives, trulyHasDept);   // 误判 2 个

            // 对照：产品路径（路线四）同样给空壳语义（provider 固有，见 X/Y 组），
            // 但取值来源正确（软删不泄漏、非主键取对行、T1 无导航也有值）
            var product = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.All(product, r => Assert.NotNull(r.Dept));
            Assert.Equal("IT", product[0].Dept!.Name);
        }

        // ================================================================
        // 手工表达式构造（复刻「只剥 IIF」的旧形态，供反证用）
        // ================================================================

        /// <summary>构造双表嵌套投影：<paramref name="lambdaUsesIif"/> 决定是否包 IIF（V1 vs V2）。</summary>
        private static Expression<Func<PocSite, PocDept, PocSiteNestedDto>> BuildManualNested(bool lambdaUsesIif)
        {
            var t1 = Expression.Parameter(typeof(PocSite), "t1");
            var t2 = Expression.Parameter(typeof(PocDept), "t2");

            var deptInit = Expression.MemberInit(
                Expression.New(typeof(PocDeptDto)),
                Expression.Bind(typeof(PocDeptDto).GetProperty(nameof(PocDeptDto.Id))!,
                    Expression.Property(t2, nameof(PocDept.Id))),
                Expression.Bind(typeof(PocDeptDto).GetProperty(nameof(PocDeptDto.Name))!,
                    Expression.Property(t2, nameof(PocDept.Name))));

            Expression deptValue = lambdaUsesIif
                ? Expression.Condition(
                    Expression.Equal(t2, Expression.Constant(null, typeof(PocDept))),
                    Expression.Constant(null, typeof(PocDeptDto)),
                    deptInit)
                : deptInit;

            var outer = Expression.MemberInit(
                Expression.New(typeof(PocSiteNestedDto)),
                Expression.Bind(typeof(PocSiteNestedDto).GetProperty(nameof(PocSiteNestedDto.Id))!,
                    Expression.Property(t1, nameof(PocSite.Id))),
                Expression.Bind(typeof(PocSiteNestedDto).GetProperty(nameof(PocSiteNestedDto.Dept))!, deptValue));

            return Expression.Lambda<Func<PocSite, PocDept, PocSiteNestedDto>>(outer, t1, t2);
        }

        /// <summary>构造反转形态：<c>IIF(t2 == null, MemberInit, null)</c>（V4）。</summary>
        private static Expression<Func<PocSite, PocDept, PocSiteNestedDto>> BuildManualReversedIif()
        {
            var t1 = Expression.Parameter(typeof(PocSite), "t1");
            var t2 = Expression.Parameter(typeof(PocDept), "t2");

            var deptInit = Expression.MemberInit(
                Expression.New(typeof(PocDeptDto)),
                Expression.Bind(typeof(PocDeptDto).GetProperty(nameof(PocDeptDto.Id))!,
                    Expression.Property(t2, nameof(PocDept.Id))),
                Expression.Bind(typeof(PocDeptDto).GetProperty(nameof(PocDeptDto.Name))!,
                    Expression.Property(t2, nameof(PocDept.Name))));

            var reversed = Expression.Condition(
                Expression.Equal(t2, Expression.Constant(null, typeof(PocDept))),
                deptInit,
                Expression.Constant(null, typeof(PocDeptDto)));

            var outer = Expression.MemberInit(
                Expression.New(typeof(PocSiteNestedDto)),
                Expression.Bind(typeof(PocSiteNestedDto).GetProperty(nameof(PocSiteNestedDto.Id))!,
                    Expression.Property(t1, nameof(PocSite.Id))),
                Expression.Bind(typeof(PocSiteNestedDto).GetProperty(nameof(PocSiteNestedDto.Dept))!, reversed));

            return Expression.Lambda<Func<PocSite, PocDept, PocSiteNestedDto>>(outer, t1, t2);
        }

        /// <summary>构造两层 IIF 嵌套（V5）：外层判 t2，内层判 t2.Id。</summary>
        private static Expression<Func<PocSite, PocDept, PocSiteNestedDto>> BuildManualTwoLayerIif()
        {
            var t1 = Expression.Parameter(typeof(PocSite), "t1");
            var t2 = Expression.Parameter(typeof(PocDept), "t2");
            var nullT2 = Expression.Equal(t2, Expression.Constant(null, typeof(PocDept)));

            var deptInit = Expression.MemberInit(
                Expression.New(typeof(PocDeptDto)),
                Expression.Bind(typeof(PocDeptDto).GetProperty(nameof(PocDeptDto.Id))!,
                    Expression.Property(t2, nameof(PocDept.Id))),
                Expression.Bind(typeof(PocDeptDto).GetProperty(nameof(PocDeptDto.Name))!,
                    Expression.Property(t2, nameof(PocDept.Name))));

            var innerIif = Expression.Condition(
                Expression.Equal(Expression.Property(t2, nameof(PocDept.Id)), Expression.Constant(0)),
                Expression.Constant(null, typeof(PocDeptDto)),
                deptInit);

            var outerIif = Expression.Condition(
                nullT2,
                Expression.Constant(null, typeof(PocDeptDto)),
                innerIif);

            var outer = Expression.MemberInit(
                Expression.New(typeof(PocSiteNestedDto)),
                Expression.Bind(typeof(PocSiteNestedDto).GetProperty(nameof(PocSiteNestedDto.Id))!,
                    Expression.Property(t1, nameof(PocSite.Id))),
                Expression.Bind(typeof(PocSiteNestedDto).GetProperty(nameof(PocSiteNestedDto.Dept))!, outerIif));

            return Expression.Lambda<Func<PocSite, PocDept, PocSiteNestedDto>>(outer, t1, t2);
        }

        /// <summary>
        /// 构造「只剥 IIF」的旧形态：嵌套源取 <c>t1.Dept</c> **导航**（不改指 t2），无 IIF。
        /// <para>这正是路线一（只剥 IIF）的产物形状 —— 用它实跑即可复现三个业务问题。</para>
        /// </summary>
        private static Expression<Func<PocSite, PocDept, PocSiteNestedDto>> BuildManualNavSourceNested()
        {
            var t1 = Expression.Parameter(typeof(PocSite), "t1");
            var t2 = Expression.Parameter(typeof(PocDept), "t2");

            var navDept = Expression.Property(t1, nameof(PocSite.Dept));
            var deptInit = Expression.MemberInit(
                Expression.New(typeof(PocDeptDto)),
                Expression.Bind(typeof(PocDeptDto).GetProperty(nameof(PocDeptDto.Id))!,
                    Expression.Property(navDept, nameof(PocDept.Id))),
                Expression.Bind(typeof(PocDeptDto).GetProperty(nameof(PocDeptDto.Name))!,
                    Expression.Property(navDept, nameof(PocDept.Name))));

            var outer = Expression.MemberInit(
                Expression.New(typeof(PocSiteNestedDto)),
                Expression.Bind(typeof(PocSiteNestedDto).GetProperty(nameof(PocSiteNestedDto.Id))!,
                    Expression.Property(t1, nameof(PocSite.Id))),
                Expression.Bind(typeof(PocSiteNestedDto).GetProperty(nameof(PocSiteNestedDto.Dept))!, deptInit));

            return Expression.Lambda<Func<PocSite, PocDept, PocSiteNestedDto>>(outer, t1, t2);
        }

        /// <summary>构造门店的「只剥 IIF」旧形态：区域取 <c>t1.Region</c> 导航，不取 t2。</summary>
        private static Expression<Func<PocStore, PocRegion, PocStoreDto>> BuildManualStoreNavNested()
        {
            var t1 = Expression.Parameter(typeof(PocStore), "t1");
            var t2 = Expression.Parameter(typeof(PocRegion), "t2");

            var navRegion = Expression.Property(t1, nameof(PocStore.Region));
            var regionInit = Expression.MemberInit(
                Expression.New(typeof(PocRegionDto)),
                Expression.Bind(typeof(PocRegionDto).GetProperty(nameof(PocRegionDto.Id))!,
                    Expression.Property(navRegion, nameof(PocRegion.Id))),
                Expression.Bind(typeof(PocRegionDto).GetProperty(nameof(PocRegionDto.Label))!,
                    Expression.Property(navRegion, nameof(PocRegion.Label))));

            var outer = Expression.MemberInit(
                Expression.New(typeof(PocStoreDto)),
                Expression.Bind(typeof(PocStoreDto).GetProperty(nameof(PocStoreDto.Id))!,
                    Expression.Property(t1, nameof(PocStore.Id))),
                Expression.Bind(typeof(PocStoreDto).GetProperty(nameof(PocStoreDto.Region))!, regionInit));

            return Expression.Lambda<Func<PocStore, PocRegion, PocStoreDto>>(outer, t1, t2);
        }

        /// <summary>
        /// 登记 T 组用的快照：<c>PocDept→PocDeptDto</c> 与 <c>PocCompany→PocCompanyDto</c> 已注册，
        /// 但主对 <c>PocSite→PocCompanyNestedDto</c> 的 Properties 只有标量、AllTargetSlots 含 Dept
        /// —— 即「嵌套已注册、但主对明细里没有该属性」的缺陷④触发形态。
        /// </summary>
        private static void RegisterDeptSnapshotForSingleVsCompose()
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocDept),
                TargetType = typeof(PocDeptDto),
                SourceTypeFqn = typeof(PocDept).FullName!,
                TargetTypeFqn = typeof(PocDeptDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
            });
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocSite),
                TargetType = typeof(PocCompanyNestedDto),
                SourceTypeFqn = typeof(PocSite).FullName!,
                TargetTypeFqn = typeof(PocCompanyNestedDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String"), Slot("Dept", nameof(PocDeptDto)) },
            });
        }

        private static RuntimeTargetPropertySlot Slot(string name, string typeName)
            => new() { Name = name, TypeName = typeName };

        private static RuntimePropertyMapping Pm(string name, string typeName) => new()
        {
            Target = Slot(name, typeName),
            SourceName = name,
            SourcePathNames = new[] { name },
        };

        /// <summary>IIF（ConditionalExpression）节点计数器。</summary>
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
