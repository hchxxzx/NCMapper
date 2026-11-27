// ============================================================================
// 缺陷④ 验收用例 —— 单表链与 Compose 腿对同一份元数据的一致性
// ----------------------------------------------------------------------------
// 缺陷④：「同一份注册数据，单表能投影嵌套、Compose 不出」。
// 修法（C2）：在 ComposeProjectionKernel 的「属性级 miss」分支补嵌套/集合 DTO 递归例外，
//   对齐单表链 ProjectionExpressionFactory 的同名分支。
//
// ★ 本文件的核心不是「某条用例通过」，而是**把两条腿放在一起对账**：
//   同一份快照 → 单表表达式 与 Compose 表达式 都必须包含嵌套绑定，
//   差异只允许在「源表达式」（单表 src.Dept 导航 vs Compose t2 参数）。
//
// ★ 「属性级 miss」是可复现的形态（P1 卡 t18 已实测确认）：
//   目标属性在 AllTargetSlots 但在 Properties（源侧异名 / 自动匹配收敛）。
//   此时单表链会进递归入口，Compose 腿若直接跳过就断链 —— 这正是要锁的点。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 缺陷④验收：同一份元数据在单表链与 Compose 腿上的嵌套投影必须一致。
    /// <para>类名匹配 <c>--filter-class "*ComposeKernelNestedParity*"</c>。</para>
    /// </summary>
    public class ComposeKernelNestedParityTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public ComposeKernelNestedParityTests(PocFixture fixture) => _fsql = fixture.Fsql;

        /// <summary>
        /// 缺陷④ 主用例：属性级 miss 形态下，两条腿都必须把嵌套纳入投影。
        /// <para>构造：主对 <c>PocSite→PocKernelParityDto</c> 的 Properties 只有标量，
        /// AllTargetSlots 含 Dept（嵌套对 <c>PocDept→PocDeptDto</c> 已注册）。
        /// 单表链会进「嵌套/集合 DTO 属性除外」递归入口；Compose 腿在 C2 前会直接跳过。</para>
        /// <para>断言：两侧产物都含 Dept 绑定，且剥除后都不含 IIF；实跑数据一致。</para>
        /// </summary>
        [Fact]
        public void PropertyMiss_NestedMustBeProjectedOnBothLegs()
        {
            RegisterKernelParitySnapshot();

            // 单表腿（参照系：一直是正确的）
            var single = FreeSqlProjectionBuilder.Build<PocSite, PocKernelParityDto>();
            // Compose 腿（C2 修复对象）
            var compose = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocKernelParityDto>();

            // ① 两条腿都必须含 Dept 绑定（缺陷④消除的直接证据）
            Assert.Contains("Dept", single.ToString());
            Assert.Contains("Dept", compose.ToString());

            // ② 两条腿都必须含内核注入的 IIF（说明确实走了递归入口，而非碰巧同形）
            Assert.Contains("IIF", single.ToString());
            Assert.Contains("IIF", compose.ToString());

            // ③ 差异只允许在源表达式
            Assert.Contains("t2", compose.ToString());            // Compose 取用户 join 参数
            Assert.DoesNotContain("t1.Dept", compose.ToString());  // 不得退回 t1 导航（会自建 JOIN）

            // ④ 实跑数据一致（同一份快照、同一条数据）
            var singleRows = _fsql.Select<PocSite>().OrderBy(s => s.Id).AsDto().ToList<PocKernelParityDto>();
            var composeRows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocKernelParityDto>();

            Assert.Equal(singleRows.Count, composeRows.Count);
            Assert.Equal(4, composeRows.Count);
            for (var i = 0; i < composeRows.Count; i++)
            {
                Assert.Equal(singleRows[i].Id, composeRows[i].Id);
                Assert.Equal(singleRows[i].Dept?.Id, composeRows[i].Dept?.Id);
                Assert.Equal(singleRows[i].Dept?.Name, composeRows[i].Dept?.Name);
            }
            Assert.Equal("IT", composeRows[0].Dept?.Name);
        }

        /// <summary>
        /// 缺陷④ 补充：SQL 的 JOIN 数必须只由「源表达式的形态」决定，不得因嵌套多出自建 JOIN。
        /// <para>★ 两条腿的 JOIN 数不同，且这个差异是**预期的**：
        /// 单表查询本身没有用户 join，嵌套取 <c>src.Dept</c> 导航 → FreeSql 为其生成 1 个 LEFT JOIN；
        /// Compose 双表查询已有用户写的 1 个 join，嵌套改指 t2 → 不再多生成 → 仍是 1 个。
        /// 故两侧都是 1 个，但语义不同（前者是嵌套必需的、后者是用户自己写的）。
        /// 关键否定断言：Compose 侧不得出现 <c>__Dept</c> 这类自建别名（那才是缺陷③）。</para>
        /// </summary>
        [Fact]
        public void PropertyMiss_NoExtraJoinOnEitherLeg()
        {
            RegisterKernelParitySnapshot();

            // 单表腿：嵌套取 src.Dept 导航 → FreeSql 生成 1 个 LEFT JOIN（单表嵌套投影的正常形态）
            var singleStripped = FreeSqlNullGuardStripVisitor.Strip(
                FreeSqlProjectionBuilder.Build<PocSite, PocKernelParityDto>());
            var singleSql = _fsql.Select<PocSite>().ToSql(singleStripped);
            Assert.Equal(1, CountJoins(singleSql));

            // Compose 腿：只有用户写的 1 个 LEFT JOIN，且不得有自建别名
            var composeStripped = FreeSqlNullGuardStripVisitor.Strip(
                FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocKernelParityDto>());
            var composeSql = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToSql(composeStripped);
            Assert.Equal(1, CountJoins(composeSql));
            Assert.DoesNotContain("__Dept", composeSql);
        }

        /// <summary>
        /// 缺陷④ 补充：三表形态下同样成立（元数无关 —— 递归入口与元数无关，只在属性面判定）。
        /// </summary>
        [Fact]
        public void PropertyMiss_ThreeTableAlsoConsistent()
        {
            RegisterKernelParitySnapshot();

            var compose3 = FreeSqlProjectionBuilder.BuildCompose3<PocSite, PocDept, PocCompany, PocKernelParityDto>();
            Assert.Contains("Dept", compose3.ToString());
            Assert.Contains("IIF", compose3.ToString());
            Assert.Contains("t2", compose3.ToString());

            // 三表：2 个 LEFT JOIN（用户写的两个）
            var stripped = FreeSqlNullGuardStripVisitor.Strip(compose3);
            var sql = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .ToSql(stripped);
            Assert.Equal(2, CountJoins(sql));

            // 实跑一致
            var rows = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id)
                .AsDto().ToList<PocKernelParityDto>();
            Assert.Equal(4, rows.Count);
            Assert.Equal("IT", rows[0].Dept?.Name);
        }

        // ================================================================
        // 基础设施
        // ================================================================

        private static int CountJoins(string sql)
            => System.Text.RegularExpressions.Regex.Matches(sql, "LEFT JOIN").Count;

        /// <summary>
        /// 登记「属性级 miss」形态的快照：嵌套对已注册，主对的 Properties 只有标量、
        /// AllTargetSlots 含 Dept —— 即缺陷④的触发条件（P1 卡 t18 实测确认的真因）。
        /// </summary>
        private static void RegisterKernelParitySnapshot()
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocDept),
                TargetType = typeof(PocDeptDto),
                SourceTypeFqn = typeof(PocDept).FullName!,
                TargetTypeFqn = typeof(PocDeptDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[]
                {
                    Pm("Id", "Int32"), Pm("Name", "String"),
                },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
            });

            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocSite),
                TargetType = typeof(PocKernelParityDto),
                SourceTypeFqn = typeof(PocSite).FullName!,
                TargetTypeFqn = typeof(PocKernelParityDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                // ★ 只有标量 —— Dept「在 AllTargetSlots 但不在 Properties」
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[]
                {
                    Slot("Id", "Int32"), Slot("Name", "String"), Slot("Dept", nameof(PocDeptDto)),
                },
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

        /// <summary>本文件专属目标 DTO（与其它组隔离，避免快照按对合并互相干扰）</summary>
        [MapFrom(typeof(PocSite))]
        public class PocKernelParityDto
        {
            /// <summary>主键</summary>
            public int Id { get; set; }
            /// <summary>站点名</summary>
            public string? Name { get; set; }
            /// <summary>部门（嵌套）</summary>
            public PocDeptDto? Dept { get; set; }
        }
    }
}
