// ============================================================================
// §6A.1 落盘：F、H 组（归属卡 C1）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// F（3 例）路线四可译性（无 IIF）—— 证明剥除后的产物能被 FreeSql 翻译
// H（5 例）路线四正确性（四个业务场景 ＋ 三表 ＋ 双层嵌套）
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// POC 回归 F/H 组 —— 路线四可译性 + 四个业务场景的正确性。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    public class FreeSqlComposePocRegressionFHTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposePocRegressionFHTests(PocFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // F 组 —— 路线四可译性
        // ================================================================

        /// <summary>
        /// F 组（F1~F3）：剥除后的表达式不得再含 IIF，且能被 FreeSql 翻译并实跑出正确数据。
        /// <para>POC F1：手工构造「嵌套取 t2 参数 + IIF」→ 炸 <c>null.Id cannot be null</c>；
        /// F2「嵌套取 t2 参数（无 IIF）」→ 可译且实跑 4 行；F3「t2 形态 + join 带软删过滤」→ 实跑 4 行。</para>
        /// <para>结论：可译性的关键是「去掉 IIF」，而值正确性的关键是「源指向 t2」——两件事必须同时成立。</para>
        /// </summary>
        [Fact]
        public void F_Route4_EmitsNoIIF()
        {
            // F1 前提：原始产物含 IIF（未剥除时不可译，已在 D 组锁定）
            var raw = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>();
            Assert.Contains("IIF", raw.ToString());

            // F2：剥除后不含 IIF，且能翻译 + 实跑
            var stripped = FreeSqlNullGuardStripVisitor.Strip(raw);
            Assert.DoesNotContain("IIF", stripped.ToString());

            var sql = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToSql(stripped);
            Assert.False(string.IsNullOrWhiteSpace(sql));

            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList(stripped);
            Assert.Equal(4, rows.Count);
            Assert.Equal("IT", rows[0].Dept?.Name);

            // F3：join 带软删过滤时同样可译（4 行，且软删部门不出现）
            var rows3 = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id && !b.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, rows3.Count);
            Assert.DoesNotContain(rows3, r => r.Dept?.Name == "HR-DELETED");

            // 三表与双层嵌套形态同样不得残留 IIF
            var stripped3 = FreeSqlNullGuardStripVisitor.Strip(
                FreeSqlProjectionBuilder.BuildCompose3<PocSite, PocDept, PocCompany, PocSiteNestedDto>());
            Assert.DoesNotContain("IIF", stripped3.ToString());

            var strippedDouble = FreeSqlNullGuardStripVisitor.Strip(
                FreeSqlProjectionBuilder.BuildCompose3<PocSite, PocDept, PocCompany, PocSiteDoubleNestedDto>());
            Assert.DoesNotContain("IIF", strippedDouble.ToString());
        }

        // ================================================================
        // H 组 —— 路线四正确性（四个业务场景逐条对齐 B 组的「应为」值）
        // ================================================================

        /// <summary>
        /// H 组（H1）：<b>T1 无 [Navigate]，但用户 join 了 T2</b> —— 嵌套必须取到 T2 的值。
        /// <para>POC H1 实测（修复方向）：<c>实跑 1 行 | 1:CUST-1</c>，而对照组 B2 是 <c>&lt;null&gt;</c>。</para>
        /// <para>本用例是 B2 的正向镜像：同一场景，断言「必须有值」。两份用例并存的意义是
        /// 「缺陷态」与「正确态」各有独立锚点，任一侧被改坏都能立刻发现。</para>
        /// </summary>
        [Fact]
        public void H_Route4_T1NoNavigate()
        {
            RegisterOrderSnapshots();
            try
            {
                var rows = _fsql.Select<PocOrder, PocCustomer>()
                    .LeftJoin((a, b) => a.CustomerId == b.Id)
                    .OrderBy((a, b) => a.Id)
                    .AsDto().ToList<PocOrderDto>();

                Assert.Single(rows);
                Assert.Equal("ORD-1", rows[0].OrderNo);

                // ★ 嵌套值来自用户 join 的 T2（不是 T1 的导航，T1 根本没有导航）
                Assert.NotNull(rows[0].Customer);
                Assert.Equal("CUST-1", rows[0].Customer!.Name);
            }
            finally
            {
                FreeSqlProjectionBuilder.ClearCache();
            }
        }

        /// <summary>
        /// H 组（H2）：<b>非主键 join</b> —— 嵌套必须取用户 join 的那一行（区B），不得取外键导航行（区A）。
        /// <para>POC H2 实测：<c>实跑 1 行 | 1:区B</c>（对照组 B3 是「区A」）。</para>
        /// </summary>
        [Fact]
        public void H_Route4_NonPkJoin()
        {
            var rows = _fsql.Select<PocStore, PocRegion>()
                .LeftJoin((a, b) => a.RegionCode == b.Code)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocStoreDto>();

            Assert.Single(rows);
            Assert.Equal("区B", rows[0].Region?.Label);

            // 对照：外键导航指向的「区A」不得出现（若出现即说明又自建了导航 JOIN）
            Assert.NotEqual("区A", rows[0].Region?.Label);
        }

        /// <summary>
        /// H 组（H3）：<b>三表</b> —— 嵌套改取 t2，SQL 只应有用户写的两个 JOIN。
        /// <para>POC H3 实测 SQL：
        /// <c>... FROM "Site" a LEFT JOIN "Dept" b ON a."DeptId" = b."Id" LEFT JOIN "Company" c ON b."CompanyId" = c."Id"</c>
        /// ——即恰好 2 个 LEFT JOIN，无自建 JOIN。</para>
        /// </summary>
        [Fact]
        public void H_Route4_ThreeTable()
        {
            var expr = FreeSqlProjectionBuilder.BuildCompose3<PocSite, PocDept, PocCompany, PocSiteNestedDto>();
            var stripped = FreeSqlNullGuardStripVisitor.Strip(expr);

            var sql = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .ToSql(stripped);

            // 恰好 2 个 LEFT JOIN（用户自己写的两个），无自建 JOIN
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sql, "LEFT JOIN").Count);
            Assert.DoesNotContain("__Dept", sql);

            // 实跑：4 行，嵌套取 t2 的 IT
            var rows = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, rows.Count);
            Assert.Equal("IT", rows[0].Dept?.Name);
        }

        /// <summary>
        /// H 组（H4）：<b>双层嵌套</b> —— 内层改取 t3 参数。
        /// <para>POC H4 实测：<c>实跑 4 行 | 1:IT/ACME, 2:/, 3:/, 4:HR-DELETED/ACME</c>。</para>
        /// <para>★ 形态必须是三表：内层的 <c>Dept.Company</c> 是第二跳，需要 PocCompany 作为 t3 参与 join。
        /// 两表形态下内层没有对应 join 参数，会退回 t2.Company 导航并让 FreeSql 翻译器无限递归
        /// （StackOverflowException，进程级崩溃）—— 这是本轮实测发现的独立问题，已记入交付说明 finding。</para>
        /// </summary>
        [Fact]
        public void H_Route4_DoubleNested()
        {
            var rows = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id)
                .AsDto().ToList<PocSiteDoubleNestedDto>();

            Assert.Equal(4, rows.Count);

            // 1：IT / ACME（两层都命中）
            Assert.Equal("IT", rows[0].Dept?.Name);
            Assert.Equal("ACME", rows[0].Dept?.Company?.Name);

            // 4：HR-DELETED / ACME（软删部门也参与左连接，但公司链仍在）
            Assert.Equal("HR-DELETED", rows[3].Dept?.Name);
            Assert.Equal("ACME", rows[3].Dept?.Company?.Name);

            // 2/3：无匹配部门 → 外层空壳；其内层公司也不得有值（空壳不得凭空长出数据）
            Assert.NotEqual("ACME", rows[1].Dept?.Company?.Name);
            Assert.NotEqual("ACME", rows[2].Dept?.Company?.Name);
        }

        /// <summary>
        /// H 组（H5）：<b>嵌套源类型不在源参数集中</b> —— 必须 fail-closed 抛异常，
        /// 不得给常量空壳、不得静默返回 null。
        /// <para>POC H5 实测（当时的手工表达式）：<c>实跑 4 行（当前手工表达式给了常量空壳，真实实现应判不可译）</c>
        /// —— 即 POC 阶段把这一条明确标为「真实实现应判不可译」。本用例就是对该期望的固化。</para>
        /// <para>★ 关键前提：嵌套 DTO 必须已在注册表里存在（否则内核走「未声明」分支，不构成 fail-closed 场景）。
        /// 故本用例手工登记 <c>PocDeptDto</c> 与主对，构成「声明了该嵌套、但查询没 join 该源类型」的配置错。</para>
        /// </summary>
        [Fact]
        public void H_Route4_UnresolvableNested_MustFailClosed()
        {
            RegisterFailClosedSnapshots();
            try
            {
                // 查询只 join 了 PocCustomer（不含 PocDept）→ 嵌套源 PocDept 不在参数集。
                // ★ 必须经 AsDto() 走 Compose 门面：直接 `.ToList<TDto>()` 是 FreeSql 自己的
                //   自动映射，根本不进 NCMapper，测不到 fail-closed。
                var ex = Assert.Throws<MapConfigurationException>(() =>
                    _fsql.Select<PocSite, PocCustomer>()
                        .LeftJoin((a, b) => b.Id == 1)
                        .AsDto()
                        .ToList<PocSiteFailClosedDto>());

                // 异常必须可定位：点名缺失的源类型 + 给出处置指引
                Assert.Contains("PocDept", ex.Message);
                Assert.Contains("不在本次查询的用户 join 源参数集", ex.Message);
                Assert.Contains("LeftJoin", ex.Message);
            }
            finally
            {
                FreeSqlProjectionBuilder.ClearCache();
            }
        }

        // ================================================================
        // 基础设施
        // ================================================================

        /// <summary>登记 <c>PocOrder/PocOrderDto</c> 与 <c>PocCustomer/PocCustomerDto</c> 两个快照（按生成产物形状）。</summary>
        private static void RegisterOrderSnapshots()
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
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("OrderNo", "String"), Slot("Customer", "PocCustomerDto") },
            });
        }

        /// <summary>
        /// 登记 fail-closed 场景的快照：<c>PocDept→PocDeptDto</c>（嵌套对被声明）
        /// ＋ 主对 <c>PocSite→PocSiteFailClosedDto</c>（Dept 在 AllTargetSlots 但不在 Properties，
        /// 即 §6A.2 里「属性级 miss」的形态）。
        /// </summary>
        private static void RegisterFailClosedSnapshots()
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
                TargetType = typeof(PocSiteFailClosedDto),
                SourceTypeFqn = typeof(PocSite).FullName!,
                TargetTypeFqn = typeof(PocSiteFailClosedDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
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
    }
}

