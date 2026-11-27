// ============================================================================
// §6A.3 「防回退」专项用例（★必做，独立于 §6A.2）
// ----------------------------------------------------------------------------
// 这些用例的存在目的：**防止后续有人把已修好的改回错的做法**。
// 每条都按任务书要求带 `// 防回退：<防什么>` 注释。
//
// 7 条清单（归属卡见括号）：
//   1. NoRegression_StripOnlyIsNotEnough          （C3）防只剥 IIF 就认为修完（W 组）
//   2. NoRegression_NoNestedSelfJoin              （C1）防嵌套重新自建 LEFT JOIN（W4/B4）
//   3. NoRegression_NoRowDroppingInDegrade        （C4）防降级路径重新丢行（I4/C1）
//   4. NoRegression_TotalCountEqualsItems         （C4）防 TotalCount != Items.Count（C3）
//   5. NoRegression_KernelExceptionParity         （C2）防单表与 Compose 腿再次不一致（T 组）
//   6. NoRegression_SixteenTerminalsIntact        （C3）防有人删除或改名终端方法（E 组）
//   7. NoRegression_NoAssertDeletion              （V1）防为过关删掉既有断言（AC-7）
//
// ★ 第 7 条（NoAssertDeletion）的特殊性：它防的不是产品代码回退，而是**测试代码被削弱**。
//   实现方式是机械核对 FreeSqlComposeNullFilterTests.cs 的 [Fact] 计数与关键断言的存活，
//   使「为过关而删断言」这一动作本身留下永久证据。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.FreeSql.Internal;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// §6A.3 防回退专项 —— 7 条用例，每条锁一个「已修好但容易被改回」的点。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    public class FreeSqlComposePocRegressionNoRegressionTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposePocRegressionNoRegressionTests(PocFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // 1（C3）防：只剥 IIF 就认为修完
        // ================================================================
        /// <summary>
        /// 防回退：只剥 IIF 就认为修完（W 组）。
        /// <para>机理：剥除只解决「可译性」；三个业务问题（软删泄漏／T1 无导航丢值／非主键取错行）
        /// 仍会复现（见 W 组读数）。本用例要求**产品路径**三条都对：
        /// 软删不泄漏 + T1 无导航有值 + 非主键取对行。若有人把改指逻辑去掉只留剥除，三条同时红。</para>
        /// </summary>
        [Fact]
        public void NoRegression_StripOnlyIsNotEnough()
        {
            // ① 软删不泄漏
            var softDel = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id && !b.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, softDel.Count);
            Assert.DoesNotContain(softDel, r => r.Dept?.Name == "HR-DELETED");

            // ② 非主键 join 取对行（区B，非区A）
            var nonPk = _fsql.Select<PocStore, PocRegion>()
                .LeftJoin((a, b) => a.RegionCode == b.Code)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocStoreDto>();
            Assert.Single(nonPk);
            Assert.Equal("区B", nonPk[0].Region?.Label);

            // ③ T1 无导航也有值（CUST-1）
            RegisterOrderSnapshot();
            var noNav = _fsql.Select<PocOrder, PocCustomer>()
                .LeftJoin((a, b) => a.CustomerId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocOrderDto>();
            Assert.Single(noNav);
            Assert.Equal("CUST-1", noNav[0].Customer?.Name);

            // ④ 同时，剥除必须仍在（去掉剥除会退回翻译期崩溃）
            var expr = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>();
            Assert.Contains("IIF", expr.ToString());                                  // 内核仍注入 IIF
            Assert.DoesNotContain("IIF", FreeSqlNullGuardStripVisitor.Strip(expr).ToString()); // 终端前必剥
        }

        // ================================================================
        // 2（C1）防：嵌套重新自建 LEFT JOIN
        // ================================================================
        /// <summary>
        /// 防回退：嵌套重新自建 LEFT JOIN（W4/B4）。
        /// <para>判据最机械：SQL 里 LEFT JOIN 的个数必须**等于用户自己写的个数**。
        /// 双表 1 个、三表 2 个；出现多余的 <c>__Dept</c> 别名即判红。</para>
        /// </summary>
        [Fact]
        public void NoRegression_NoNestedSelfJoin()
        {
            // 双表：1 个 LEFT JOIN
            var two = FreeSqlNullGuardStripVisitor.Strip(
                FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>());
            var sql2 = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToSql(two);
            Assert.Equal(1, CountJoins(sql2));
            Assert.DoesNotContain("__Dept", sql2);

            // 三表：2 个 LEFT JOIN
            var three = FreeSqlNullGuardStripVisitor.Strip(
                FreeSqlProjectionBuilder.BuildCompose3<PocSite, PocDept, PocCompany, PocSiteNestedDto>());
            var sql3 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .ToSql(three);
            Assert.Equal(2, CountJoins(sql3));
            Assert.DoesNotContain("__Dept", sql3);

            // 业务后果复核：软删过滤真正生效（自建 JOIN 会让它失效）
            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id && !b.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.DoesNotContain(rows, r => r.Dept?.Name == "HR-DELETED");
        }

        // ================================================================
        // 3（C4）防：降级路径重新丢行
        // ================================================================
        /// <summary>
        /// 防回退：降级路径重新丢行（I4/C1）。
        /// <para>锁三件事：① 降级 DTO 的 ToList 行数 == 原生 join 行数；
        /// ② 附加源为 null 的行仍在结果里；③ 主源为 null 的行仍被剔除（不得为了「不丢行」把主源 null 也留下）。</para>
        /// <para>★ ③ 是关键：如果实现改成「什么都不过滤」，主源为 null 的行会让 Mapper.Compose 抛异常。
        /// 故本用例同时锁住两档判据的边界。</para>
        /// </summary>
        [Fact]
        public void NoRegression_NoRowDroppingInDegrade()
        {
            var native = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count();
            Assert.Equal(4, native);

            // ① 降级 DTO 行数 == 原生
            var degrade = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteConvNestedDto>();
            Assert.Equal(native, degrade.Count);

            // ② 附加源为 null 的行仍在（Id=2 无部门、Id=3 孤儿）
            Assert.Contains(degrade, r => r.Id == 2);
            Assert.Contains(degrade, r => r.Id == 3);

            // ③ 主源为 null 的行仍被剔除（两档判据的边界，不得反向放宽）
            var tuples = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToList((a, b) => new Tuple<PocSite, PocDept>(a, b));
            var withPrimaryNull = new List<Tuple<PocSite, PocDept>>(tuples) { new(null!, tuples[0].Item2) };
            var kept = FreeSqlComposeSupport.FilterValid2(withPrimaryNull, "[FreeSql Compose] 剔除 {0} 条主源为 null 的行");
            Assert.Equal(tuples.Count, kept.Count);          // 多出的主源 null 行被剔除
            Assert.DoesNotContain(kept, t => t.Item1 == null);
        }

        // ================================================================
        // 4（C4）防：TotalCount != Items.Count
        // ================================================================
        /// <summary>
        /// 防回退：<c>TotalCount != Items.Count</c>（C3）。
        /// <para>POC C3/I5 的原始分叉：<c>TotalCount=4 但 Items=2</c>。本用例锁全部含分页的终端
        /// （ToPagedList / ToPagedListAsync）在投影与降级两条路径上都必须相等。</para>
        /// </summary>
        [Fact]
        public void NoRegression_TotalCountEqualsItems()
        {
            var native = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count();

            // 投影路径（同步 + 异步）
            var paged = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedList<PocSiteNestedDto>(1, 10);
            Assert.Equal(native, paged.TotalCount);
            Assert.Equal(paged.TotalCount, paged.Items.Count);

            var pagedAsync = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedListAsync<PocSiteNestedDto>(1, 10)
                .GetAwaiter().GetResult();
            Assert.Equal(native, pagedAsync.TotalCount);
            Assert.Equal(pagedAsync.TotalCount, pagedAsync.Items.Count);

            // 降级路径（Convert 触发，同步 + 异步）
            var pagedDegrade = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedList<PocSiteConvNestedDto>(1, 10);
            Assert.Equal(native, pagedDegrade.TotalCount);
            Assert.Equal(pagedDegrade.TotalCount, pagedDegrade.Items.Count);

            var pagedDegradeAsync = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedListAsync<PocSiteConvNestedDto>(1, 10)
                .GetAwaiter().GetResult();
            Assert.Equal(native, pagedDegradeAsync.TotalCount);
            Assert.Equal(pagedDegradeAsync.TotalCount, pagedDegradeAsync.Items.Count);

            // 分页截断场景：TotalCount 仍为全量、Items 为当页大小
            var page1 = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedList<PocSiteNestedDto>(1, 2);
            Assert.Equal(native, page1.TotalCount);
            Assert.Equal(2, page1.Items.Count);
        }

        // ================================================================
        // 5（C2）防：单表与 Compose 腿再次不一致
        // ================================================================
        /// <summary>
        /// 防回退：单表与 Compose 腿再次不一致（T 组）。
        /// <para>缺陷④的判据是「同一份元数据，单表能投影嵌套、Compose 整段跳过」。
        /// 本用例用**同一份快照形态**同时取两条腿的表达式，断言两侧都含 Dept 且都不含 IIF（剥除后），
        /// 并且差异只在源表达式（单表 <c>src.Dept</c> 导航 vs Compose <c>t2</c> 参数）。</para>
        /// <para>★ 这是 C2 的核心防回退点：若有人删掉 ComposeProjectionKernel 的嵌套递归例外，
        /// Compose 侧会退化成不含 Dept，本断言立即红。</para>
        /// </summary>
        [Fact]
        public void NoRegression_KernelExceptionParity()
        {
            RegisterParitySnapshot();

            // 单表腿
            var single = FreeSqlProjectionBuilder.Build<PocSite, PocParityDto>();

            // Compose 腿（同一份快照）
            var compose = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocParityDto>();

            // 两侧都必须把 Dept 纳入（缺陷④消除）
            Assert.Contains("Dept", single.ToString());
            Assert.Contains("Dept", compose.ToString());

            // 两侧都必须含 IIF（内核注入），且剥除后都不含
            Assert.Contains("IIF", single.ToString());
            Assert.Contains("IIF", compose.ToString());
            Assert.DoesNotContain("IIF", FreeSqlNullGuardStripVisitor.Strip(single).ToString());
            Assert.DoesNotContain("IIF", FreeSqlNullGuardStripVisitor.Strip(compose).ToString());

            // 差异只在源表达式：单表取导航，Compose 取用户 join 的 t2
            Assert.Contains("Dept", single.ToString());          // src.Dept 导航
            Assert.Contains("t2", compose.ToString());           // 用户 join 参数
            Assert.DoesNotContain("t1.Dept", compose.ToString()); // 不得退回导航

            // 实跑：两条腿给出相同的业务值
            var singleRows = _fsql.Select<PocSite>().OrderBy(s => s.Id).AsDto().ToList<PocParityDto>();
            var composeRows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocParityDto>();
            Assert.Equal(singleRows.Count, composeRows.Count);
            Assert.Equal(singleRows[0].Dept?.Name, composeRows[0].Dept?.Name);
            Assert.Equal("IT", composeRows[0].Dept?.Name);
        }

        // ================================================================
        // 6（C3）防：删除或改名终端方法
        // ================================================================
        /// <summary>
        /// 防回退：有人删除或改名终端方法（E 组）。
        /// <para>任务书 §3.1 写死 16 个终端（双表 8 + 三表 8）。本用例用反射逐个核对方法名，
        /// 且核对两个 selector 的方法名集合完全一致 —— 任何增删改名都会红。</para>
        /// </summary>
        [Fact]
        public void NoRegression_SixteenTerminalsIntact()
        {
            var expected = new[]
            {
                "ToList", "ToListAsync", "First", "FirstAsync",
                "ToPagedList", "ToPagedListAsync", "ToChunkDto", "ToChunkDtoAsync",
            }.OrderBy(x => x, StringComparer.Ordinal).ToList();

            foreach (var sel in new[]
                     {
                         typeof(FreeSqlComposeDtoSelector<,>),
                         typeof(FreeSqlComposeDtoSelector3<,,>),
                     })
            {
                var names = sel
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => m.Name)
                    .Distinct()
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                // 数量 8 且名字逐个一致
                Assert.Equal(8, names.Count);
                Assert.Equal(expected, names);
            }

            // 合计 16
            var total = new[] { typeof(FreeSqlComposeDtoSelector<,>), typeof(FreeSqlComposeDtoSelector3<,,>) }
                .Sum(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Count(m => !m.IsSpecialName));
            Assert.Equal(16, total);
        }

        // ================================================================
        // 7（V1）防：为过关删掉既有断言
        // ================================================================
        /// <summary>
        /// 防回退：为过关删掉既有断言（AC-7）。
        /// <para>★ 本用例防的不是产品代码，而是**测试代码被削弱**：
        /// ① <c>FreeSqlComposeNullFilterTests.cs</c> 的 <c>[Fact]</c> 计数必须仍为 8（不得删用例）；
        /// ② 该文件的关键断言文本必须仍在（不得把断言改成恒真/注释掉）；
        /// ③ <c>freesqlcomposesupporttests.cs</c> 的 <c>[Fact]</c> 计数必须仍为 3。</para>
        /// <para>判据来源：任务书 §12.3 的三条机械判据（[Fact] 计数未减少、断言改动是 -/+ 配对、
        /// 无删除整个断言或注释掉断言的 hunk）。其中「-/+ 配对」由 git diff 核验（见交付说明），
        /// 「计数」与「关键断言存活」由本用例在测试内自动核验。</para>
        /// <para>文件定位：从测试程序集往上找到仓库根，再按相对路径读源码 —— 源码随仓库走，
        /// 不受编译产物影响。</para>
        /// </summary>
        [Fact]
        public void NoRegression_NoAssertDeletion()
        {
            var repoRoot = FindRepositoryRoot();
            Assert.NotNull(repoRoot);

            // ① FreeSqlComposeNullFilterTests：8 个 [Fact]
            var nullFilterPath = Path.Combine(
                repoRoot!, "test", "NCMapper.IntegrationTests", "FreeSqlComposeNullFilterTests.cs");
            Assert.True(File.Exists(nullFilterPath), $"找不到被保护文件：{nullFilterPath}");

            var nullFilterText = File.ReadAllText(nullFilterPath);
            var nullFilterFacts = CountOccurrences(nullFilterText, "[Fact]");
            Assert.Equal(8, nullFilterFacts);

            // ② 关键断言必须仍在（防「删断言」与「改成恒真」两种削弱方式）
            //    这些是 C4 改造时同步过的断言，每一条都对应一个具体业务事实
            Assert.Contains("Assert.Equal(5, dtos.Count)", nullFilterText);          // Active 5 条保留
            Assert.Contains("Assert.Contains(dtos, d => d.Id == 6)", nullFilterText); // orphan 必须在
            Assert.Contains("降级路径保留 1 条附加源为 null 的行", nullFilterText);    // 保留语义 Trace
            Assert.Contains("Assert.Null(dto)", nullFilterText);                      // First 命中 null 行返回 null
            Assert.Contains("Assert.Equal(new[] { 6, 4, 3, 2, 1 }", nullFilterText);  // chunk 完整序列

            // ③ freesqlcomposesupporttests：3 个 [Fact]
            var supportPath = Path.Combine(
                repoRoot, "test", "NCMapper.IntegrationTests", "freesqlcomposesupporttests.cs");
            Assert.True(File.Exists(supportPath), $"找不到被保护文件：{supportPath}");

            var supportText = File.ReadAllText(supportPath);
            Assert.Equal(3, CountOccurrences(supportText, "[Fact]"));

            // 支持库的关键断言同样必须存活（C4 同步过的那条）
            Assert.Contains("Assert.Equal(2, valid2.Count)", supportText);
            Assert.Contains("Assert.Equal(3, valid3.Count)", supportText);
            Assert.Contains("Assert.Null(valid2[1].Item2)", supportText);
            Assert.Contains("Assert.Null(valid3[2].Item3)", supportText);
        }

        // ================================================================
        // 基础设施
        // ================================================================

        /// <summary>统计子串出现次数（区分大小写，用于 [Fact] 计数）。</summary>
        private static int CountOccurrences(string text, string token)
        {
            var count = 0;
            var idx = 0;
            while ((idx = text.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += token.Length;
            }
            return count;
        }

        /// <summary>
        /// 从测试程序集目录向上找到仓库根（含 <c>NCMapper.slnx</c> 或 <c>src/NCMapper</c> 的目录）。
        /// <para>从 <c>bin/Debug/netX.Y</c> 出发需要上溯 5~6 层，故设上限 12 层防止死循环。</para>
        /// </summary>
        private static string? FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "NCMapper.slnx"))
                    || Directory.Exists(Path.Combine(dir.FullName, "src", "NCMapper")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }

        private static int CountJoins(string sql)
            => System.Text.RegularExpressions.Regex.Matches(sql, "LEFT JOIN").Count;

        /// <summary>登记 T 组/防回退 5 用的快照（嵌套对已注册、主对明细无 Dept 但 slot 有）。</summary>
        private static void RegisterParitySnapshot()
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
                TargetType = typeof(PocParityDto),
                SourceTypeFqn = typeof(PocSite).FullName!,
                TargetTypeFqn = typeof(PocParityDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String"), Slot("Dept", nameof(PocDeptDto)) },
            });
        }

        /// <summary>登记 B2/H1/防回退 1 用的订单快照。</summary>
        private static void RegisterOrderSnapshot()
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocCustomer),
                TargetType = typeof(PocCustomerDto),
                SourceTypeFqn = typeof(PocCustomer).FullName!,
                TargetTypeFqn = typeof(PocCustomerDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
            });
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocOrder),
                TargetType = typeof(PocOrderDto),
                SourceTypeFqn = typeof(PocOrder).FullName!,
                TargetTypeFqn = typeof(PocOrderDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[] { Pm("Id", "Int32"), Pm("OrderNo", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("OrderNo", "String"), Slot("Customer", nameof(PocCustomerDto)) },
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

        /// <summary>防回退 5 用的目标 DTO（与其它组隔离，避免快照合并）</summary>
        [MapFrom(typeof(PocSite))]
        public class PocParityDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }
            /// <summary>站点名</summary>
            public string? Name { get; set; }
            /// <summary>部门</summary>
            public PocDeptDto? Dept { get; set; }
        }
    }
}

