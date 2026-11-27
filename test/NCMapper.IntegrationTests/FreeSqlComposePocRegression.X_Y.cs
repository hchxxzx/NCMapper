// ============================================================================
// §6A.1 落盘：X、Y 组（归属卡 C4）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// X（5 例）空壳语义是 provider 固有 —— 不是 NCMapper 引入的
// Y（5 例）单表 vs Compose 空壳一致
//
// ★ 本文件必须用**嵌套对象形态**的 DTO —— 这是 captain 专门提示过的方向陷阱：
//   FreeSqlComposeNullFilterTests 的 DTO 是扁平字段 `string? CategoryName`，
//   orphan 行实测就是 `<null>`，**观察不到空壳**。空壳只出现在嵌套对象形态：
//   `Dept != null` 但 `Dept.Id == 0`、`Dept.Name == ""`。
//
// ★ 空壳的成因（POC 结论）：剥除器的语义代价 —— 剥掉 IIF 后 null 导航不再短路，
//   MemberInit 无条件执行，于是「无匹配行」得到的是字段取默认值的实例，而非 null。
//   这是 provider 固有行为（原生 FreeSql 同样如此），不是本次修复引入的新问题。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using NCMapper.FreeSql.Internal;
using Xunit;

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// POC 回归 X/Y 组 —— 空壳语义的 provider 固有性，以及单表与 Compose 的一致性。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    public class FreeSqlComposePocRegressionXYTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposePocRegressionXYTests(PocFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // X 组 —— 空壳语义是 provider 固有
        // ================================================================

        /// <summary>
        /// X 组（X1/X3）：**原生 FreeSql**（不经 NCMapper）在同一份数据上给出同样的空壳语义。
        /// <para>POC X1：<c>原生 FreeSql 嵌套对象（无 IIF）→ 可译 4 行 | 1:[Ok:10/IT], 2:[Ok:0/''], 3:[Ok:0/''], 4:[Ok:11/HR-DELETED]</c>；
        /// X3：<c>id=1 DeptNull=False DeptId=10, id=2 DeptNull=False DeptId=0,
        /// id=3 DeptNull=False DeptId=0, id=4 DeptNull=False DeptId=11</c>。</para>
        /// <para>★ 结论：无匹配行得到「非 null 但 Id=0/Name=''」的空壳，是 **FreeSql 自身的语义**，
        /// 与 NCMapper 无关 —— 这排除了「空壳是本次修复引入的新问题」这一质疑。</para>
        /// </summary>
        [Fact]
        public void X_NativeFreeSql_AlsoEmptyShell()
        {
            // 原生 FreeSql：直接 new 嵌套对象（无 IIF 短路），由 FreeSql 自己翻译
            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList((a, b) => new PocNativeDto
                {
                    Id = a.Id,
                    Dept = new PocDeptDto { Id = b.Id, Name = b.Name },
                });

            Assert.Equal(4, rows.Count);

            // X3：逐行核对 —— 全部非 null（空壳），无匹配行 Id=0/Name=null
            Assert.All(rows, row => Assert.NotNull(row.Dept));
            Assert.Equal(10, rows[0].Dept!.Id);
            Assert.Equal("IT", rows[0].Dept.Name);
            Assert.Equal(0, rows[1].Dept!.Id);
            Assert.Null(rows[1].Dept.Name);
            Assert.Equal(0, rows[2].Dept!.Id);
            Assert.Null(rows[2].Dept.Name);
            Assert.Equal(11, rows[3].Dept!.Id);
            Assert.Equal("HR-DELETED", rows[3].Dept.Name);
        }

        /// <summary>
        /// X 组（X2/X4）：IIF 包对象在原生 FreeSql 上同样不可译（两种通道同一炸点）。
        /// <para>POC X2：<c>原生 FreeSql IIF 包对象 → 炸 Exception: SQL logic error near "as3": syntax error</c>；
        /// X4：<c>单表嵌套 null 语义 → 同样的 SQL logic error</c>。</para>
        /// <para>★ 注意异常形态与 NCMapper 路径不同（原生是 SQL 语法错、NCMapper 路径是 null.Id cannot be null），
        /// 但结论一致：**IIF 包对象在 FreeSql 上就是不可译的**，与是否经 NCMapper 无关。</para>
        /// </summary>
        [Fact]
        public void X_NativeIifWithObject_NotTranslatable()
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

            var cond = Expression.Condition(nullT2, Expression.Constant(null, typeof(PocDeptDto)), deptInit);
            var outer = Expression.MemberInit(
                Expression.New(typeof(PocNativeDto)),
                Expression.Bind(typeof(PocNativeDto).GetProperty(nameof(PocNativeDto.Id))!,
                    Expression.Property(t1, nameof(PocSite.Id))),
                Expression.Bind(typeof(PocNativeDto).GetProperty(nameof(PocNativeDto.Dept))!, cond));
            var lambda = Expression.Lambda<Func<PocSite, PocDept, PocNativeDto>>(outer, t1, t2);

            // X2：IIF 包对象 → 翻译失败（异常形态是 SQL 语法错，与 NCMapper 路径的 null.Id 不同但同样失败）
            var ex = Assert.ThrowsAny<Exception>(() =>
                _fsql.Select<PocSite, PocDept>()
                    .LeftJoin((a, b) => a.DeptId == b.Id)
                    .ToList(lambda));
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));

            // 剥除后可译（证明炸点就是 IIF 包对象，与 X1 同源）
            var stripped = FreeSqlNullGuardStripVisitor.Strip(lambda);
            var ok = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList(stripped);
            Assert.Equal(4, ok.Count);
            Assert.Equal(10, ok[0].Dept!.Id);
        }

        /// <summary>
        /// X 组（X5）：**两段式**可以还原精确 null —— 先取扁平（含 null 标识）再在内存里组装。
        /// <para>POC X5：<c>4 行 | id=1 DeptId=10, id=2 DeptId=NULL, id=3 DeptId=NULL, id=4 DeptId=11
        /// ← 证明精确 null 可还原</c>。</para>
        /// <para>★ 本用例证明「精确 null」并非做不到，只是需要两段式（一次查询 + 内存组装）；
        /// 本任务书选择不做（性能与语义权衡），故空壳语义被保留为既有行为。
        /// 这条也是「若将来要精确 null 该怎么做」的技术锚点。</para>
        /// </summary>
        [Fact]
        public void X_TwoPhase_CanRestoreNull()
        {
            // 第一段：取扁平行（含 T2 主键为 null 的标识）
            var flat = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList((a, b) => new PocFlatDeptIdRow { Id = a.Id, DeptId = b.Id, DeptName = b.Name });

            Assert.Equal(4, flat.Count);
            Assert.Equal(10, flat[0].DeptId);
            Assert.Null(flat[1].DeptId);          // 无部门 → 精确 null 标识
            Assert.Null(flat[2].DeptId);          // 孤儿 → 精确 null 标识
            Assert.Equal(11, flat[3].DeptId);

            // 第二段：内存组装 —— 用 DeptId == null 判定「无关联部门」
            var assembled = flat.Select(r => new PocSiteNestedDto
            {
                Id = r.Id,
                Name = null,
                Dept = r.DeptId == null ? null : new PocDeptDto { Id = r.DeptId.Value, Name = r.DeptName },
            }).ToList();

            // 精确 null 被还原：第 2/3 行为 null，第 1/4 行有值
            Assert.NotNull(assembled[0].Dept);
            Assert.Equal(10, assembled[0].Dept!.Id);
            Assert.Null(assembled[1].Dept);
            Assert.Null(assembled[2].Dept);
            Assert.NotNull(assembled[3].Dept);
            Assert.Equal(11, assembled[3].Dept!.Id);

            // ★ 对比：单段（投影）路径给出的是空壳，故 Dept != null 无法区分
            var onePhase = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.All(onePhase, r => Assert.NotNull(r.Dept));
            Assert.NotEqual(
                onePhase.Count(r => r.Dept == null),
                assembled.Count(r => r.Dept == null));
        }

        // ================================================================
        // Y 组 —— 单表 vs Compose 空壳一致
        // ================================================================

        /// <summary>
        /// Y 组（Y1/Y2/Y4）：单表通道与 Compose 通道的空壳语义**逐行一致**。
        /// <para>POC Y1（单表已修路径）：<c>4 行 | id=1 DeptNull=False DeptId=10; id=2 DeptNull=False DeptId=0;
        /// id=3 DeptNull=False DeptId=0; id=4 DeptNull=False DeptId=11</c>；
        /// Y2（双表 Compose + 剥除）：<c>同样 4 行读数</c>；
        /// Y4：<c>单表=1:S|2:S|3:S|4:S Compose=1:S|2:S|3:S|4:S 一致=True</c>（S = 空壳）。</para>
        /// <para>★ 这条一致性是路线四的必要条件：若 Compose 腿给 null 而单表给空壳，
        /// 调用方在两条通道上就要写两套判断逻辑 —— 那本身就是缺陷。</para>
        /// </summary>
        [Fact]
        public void Y_SingleAndCompose_SameEmptyShellSemantics()
        {
            var single = _fsql.Select<PocSite>()
                .OrderBy(s => s.Id)
                .AsDto().ToList<PocSiteNestedDto>();

            var compose = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();

            Assert.Equal(4, single.Count);
            Assert.Equal(4, compose.Count);

            // Y1/Y2：两侧逐行读数相同
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(single[i].Id, compose[i].Id);
                // 空壳判定：非 null（Y4 的 "S"）
                Assert.NotNull(single[i].Dept);
                Assert.NotNull(compose[i].Dept);
                // 字段读数逐个相等
                Assert.Equal(single[i].Dept!.Id, compose[i].Dept!.Id);
                Assert.Equal(single[i].Dept.Name, compose[i].Dept.Name);
            }

            // 具体值：1→10/IT；2→0/null；3→0/null；4→11/HR-DELETED
            Assert.Equal((10, "IT"), (single[0].Dept!.Id, single[0].Dept.Name));
            Assert.Equal((0, (string?)null), (single[1].Dept!.Id, single[1].Dept.Name));
            Assert.Equal((0, (string?)null), (single[2].Dept!.Id, single[2].Dept.Name));
            Assert.Equal((11, "HR-DELETED"), (single[3].Dept!.Id, single[3].Dept.Name));
        }

        /// <summary>
        /// Y 组（Y5）：调用方用 <c>Dept != null</c> 判断「有无关联部门」会**误判**——
        /// 这是空壳语义的业务代价，必须显式记录，供 API 使用者知晓。
        /// <para>POC Y5：<c>Dept!=null 计为有=4 个；实际有真部门=2 个 → 差 2 个误判</c>。</para>
        /// <para>★ 本用例不是「缺陷断言」而是**语义文档化**：它把误判数量钉死（4 vs 2，差 2），
        /// 使任何想改变空壳语义的改动都必须先改这里，从而无法悄悄变更对外契约。</para>
        /// </summary>
        [Fact]
        public void Y_NullCheckMisjudges_ByEmptyShell()
        {
            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();

            // Y5：按 Dept != null 判「有部门」→ 4 行全算有（含 2 个空壳）
            var judgedByNull = rows.Count(r => r.Dept != null);
            Assert.Equal(4, judgedByNull);

            // 事实：真正有部门的只有 2 行（Id=1 → 10/IT；Id=4 → 11/HR-DELETED）
            var trulyHasDept = rows.Count(r => r.Dept != null && r.Dept.Id != 0);
            Assert.Equal(2, trulyHasDept);

            // 误判 = 2 个（Id=2 与 Id=3 被算成「有部门」）
            Assert.Equal(2, judgedByNull - trulyHasDept);

            // 正确的判断方式：用主键是否为默认值（或改为两段式，见 X 组）
            Assert.Equal(2, rows.Count(r => r.Dept is { Id: > 0 }));
            // 空壳行：Id 为 0（值类型默认值），Name 为 null（引用类型默认值）
            Assert.Equal(2, rows.Count(r => r.Dept is { Id: 0 }));
        }

        /// <summary>
        /// Y 组（Y3）：原 POC Y3 记录「双表 Compose 公共入口 → 炸」—— 修复后必须不炸且行数正确。
        /// <para><b>原 POC Y3：崩（null.Id cannot be null）→ 修复后：4 行正常返回。</b></para>
        /// <para>这条是 Y 组的「修复效果」锚点，与 Y1/Y2 的「语义不变」锚点互补：
        /// 一行都不能少（行数不变）、空壳语义也不能变（逐行读数不变），但崩必须消除。</para>
        /// </summary>
        [Fact]
        public void Y_ComposePublicEntry_NoLongerBombs()
        {
            // 双表 Compose 公共入口（原 POC Y3 的炸点）
            var twoTable = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, twoTable.Count);
            Assert.Equal("IT", twoTable[0].Dept?.Name);

            // 三表 Compose 公共入口（同形态，元数不同）
            var threeTable = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, threeTable.Count);
            Assert.Equal("IT", threeTable[0].Dept?.Name);

            // 与单表对照一致（Y4 结论在「不崩」之后的延续）
            var single = _fsql.Select<PocSite>().OrderBy(s => s.Id).AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(single.Count, twoTable.Count);
        }

        // ================================================================
        // 辅助类型（本文件专属，避免与其它组共享而互相干扰）
        // ================================================================

        /// <summary>原生 FreeSql 投影用 DTO（不经 NCMapper 构建器）</summary>
        public class PocNativeDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }
            /// <summary>部门</summary>
            public PocDeptDto? Dept { get; set; }
        }

        /// <summary>两段式第一段的扁平行（含 T2 主键，用于判定精确 null）</summary>
        public class PocFlatDeptIdRow
        {
            /// <summary>站点主键</summary>
            public int Id { get; set; }
            /// <summary>部门主键（无匹配时为 null）</summary>
            public int? DeptId { get; set; }
            /// <summary>部门名</summary>
            public string? DeptName { get; set; }
        }
    }
}
