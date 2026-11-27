// ============================================================================
// §6A.1 落盘：L、N、S 组（归属卡 C2）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// L（4 例：L1/L2/L3/L5，无 L4）Fluent 对降级网与公共入口的判定
// N（5 例）特性 vs Fluent 同形状对照 —— 缺陷④的成因链
// S（5 例）Fluent 能否表达嵌套（根因）
//
// ★ 本文件对应缺陷④：「同一份元数据在单表能投影嵌套、Compose 整段跳过」。
//   C2 的修法是在 ComposeProjectionKernel 的属性级 miss 分支补嵌套/集合 DTO 递归例外，
//   对齐单表链 ProjectionExpressionFactory.cs:380-396 / 1278-1293。
//
// ★ 工程前提（关键）：NCMapper.IntegrationTests 工程**未把 SourceGen 作 Analyzer 引用**，
//   故 Fluent 对（cfg.For）在该工程内不会经源生成器注册。本文件的 Fluent 用例按
//   「源生成器产物形状」手工登记 RuntimeMapperMetadata（POC 的 L/N/S 组当时也是同一做法，
//   见 ProgramL.cs 的 RegisterFluent()），从而在运行时复现同一快照前提。
//
// ★ 为何「未声明」与「已声明」用两套 DTO 类型对：RuntimeSnapshotStore.Register 对同一
//   (Source,Target) 对做幂等合并（明细优先），同一对先后登记两种形态会互相覆盖/合并，
//   导致后一个用例实际测到前一个形态（本轮实测确实踩到）。用独立类型对隔离，
//   避免用 Clear() 清空注册表（会破坏其它用例依赖的生成产物条目）。
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
    /// POC 回归 L/N/S 组 —— Fluent 通道的嵌套投影判定与缺陷④成因链。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    public class FreeSqlComposePocRegressionLNSTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposePocRegressionLNSTests(PocFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // L 组 —— Fluent 对的降级网与公共入口
        // ================================================================

        /// <summary>
        /// L 组（L1/L2）：Fluent 对的降级网放行，且「未声明嵌套」形态的 Compose 产物无 IIF。
        /// <para>POC L1：<c>放行 reason=</c>；
        /// L2（Fluent 未声明 Dept）：<c>无IIF | (t1, t2) =&gt; new FluentSiteNestedDto() {Id = t1.Id, Name = t1.Name}</c>。</para>
        /// <para>★ L2 的「无 IIF」是缺陷②（下半句）与缺陷④的表征：嵌套对未注册时该属性根本不进表达式，
        /// 于是「不崩但字段恒 null」。C2 让「在 AllTargetSlots 但不在 Properties」的嵌套类型进递归入口，
        /// 从而在有映射声明时也能产出 IIF（进而在 Compose 通道被剥除）。</para>
        /// </summary>
        [Fact]
        public void L_Fluent_NeedsDegradation()
        {
            // L1：Fluent 对（无 [MapFrom] 锚）同样被降级网放行
            var need = FreeSqlDegradationDetector.NeedsDegradationCompose(
                typeof(PocSite), new[] { typeof(PocDept) }, typeof(PocFluentSiteUndeclaredDto), out var reason);
            Assert.False(need, $"Fluent 对不应触发降级，实际原因: {reason}");
            Assert.Empty(reason);

            // L2：未声明形态的 Compose 产物 —— 无 IIF、无 Dept 绑定
            RegisterUndeclaredFluentSnapshot();
            var expr = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocFluentSiteUndeclaredDto>();
            var text = expr.ToString();
            Assert.DoesNotContain("IIF", text);
            Assert.DoesNotContain("Dept", text);
            Assert.Contains("Id", text);
            Assert.Contains("Name", text);
        }

        /// <summary>
        /// L 组（L3/L5）：未声明形态走公共入口实跑 —— 不崩、行数守恒、嵌套恒 null。
        /// <para>POC L3：<c>未炸（3 行）| 1:&lt;null&gt;, 2:&lt;null&gt;, 3:&lt;null&gt;</c>；
        /// L5（剥 IIF 后，剥 0）：<c>可译 3 行 | 全 null</c>。</para>
        /// <para>POC 用 3 条站点，本夹具 4 条，故行数断言为 4；断言要点（不崩 + 行数守恒 +
        /// 未声明形态下嵌套为 null）不变。</para>
        /// </summary>
        [Fact]
        public void L_Fluent_PublicEntry()
        {
            RegisterUndeclaredFluentSnapshot();

            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocFluentSiteUndeclaredDto>();

            Assert.Equal(4, rows.Count);
            // 未声明嵌套 → 该属性不进表达式 → 恒 null（缺陷②「不崩但字段恒 null」的固化）
            Assert.All(rows, r => Assert.Null(r.Dept));

            // 标量字段正常
            Assert.Equal("S-normal", rows[0].Name);

            // L5：剥除对未声明形态是空操作（剥 0 处），产物可译
            var expr = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocFluentSiteUndeclaredDto>();
            var stripped = FreeSqlNullGuardStripVisitor.Strip(expr);
            Assert.Same(expr, stripped);   // 无匹配节点 → 原样返回同一引用（零重建）
        }

        // ================================================================
        // N 组 —— 特性 vs Fluent 同形状对照
        // ================================================================

        /// <summary>
        /// N 组（N1/N3/N5）：特性通道与 Fluent 通道对**同形状** DTO 产出不同形态的表达式。
        /// <para>POC N1 特性通道：<c>含IIF</c>（当时实跑炸）；N3：<c>特性声明=6 个绑定 | Fluent 声明=2 个绑定 | 差 4 个</c>；
        /// N5（Fluent 只声明标量）：<c>(t1, t2) =&gt; new FluentSiteNestedDto() {Id = t1.Id}</c>。</para>
        /// <para>★ 本用例是缺陷④的**成因证据**：同一形状的 DTO，特性通道会产出嵌套绑定（含 IIF），
        /// Fluent 未声明时不会。两者对同一份查询给出不同结果 —— 这个不一致正是 C2 要消除的。</para>
        /// <para>N4：<c>RuntimeComposeMapping</c> 的字段面必须能承载「嵌套对象」语义判定的输入，
        /// 此处锁住子源类型与源路径两组字段（防被删）。</para>
        /// </summary>
        [Fact]
        public void N_AttributeVsFluent_SameShape()
        {
            // N1：特性通道（[MapFrom] + 自动匹配）→ 产物含嵌套绑定与 IIF
            var attrExpr = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>();
            var attrText = attrExpr.ToString();
            Assert.Contains("IIF", attrText);
            Assert.Contains("Dept", attrText);
            Assert.Contains("PocDeptDto", attrText);

            // N5：Fluent 未声明 Dept → 产物不含 Dept 绑定、不含 IIF
            RegisterUndeclaredFluentSnapshot();
            var fluentExpr = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocFluentSiteUndeclaredDto>();
            var fluentText = fluentExpr.ToString();
            Assert.DoesNotContain("IIF", fluentText);
            Assert.DoesNotContain("Dept", fluentText);
            Assert.Contains("Id", fluentText);
            Assert.Contains("Name", fluentText);

            // 形态差异本身即结论：两个通道对同形状 DTO 产出不同（缺陷④）
            Assert.NotEqual(attrText, fluentText);

            // N4：RuntimeComposeMapping 的字段面
            var cmFields = typeof(RuntimeComposeMapping).GetProperties().Select(p => p.Name).ToList();
            Assert.Contains("TargetPropertyName", cmFields);
            Assert.Contains("SubSourceTypeRef", cmFields);
            Assert.Contains("SubSourceTypeName", cmFields);
            Assert.Contains("SourcePathNames", cmFields);
        }

        // ================================================================
        // S 组 —— Fluent 能否表达嵌套（根因）
        // ================================================================

        /// <summary>
        /// S 组（S0/S1/S4）：声明与不声明 Dept，产物形态的差异；声明后必然出 IIF。
        /// <para>POC S0（基线，不声明）：<c>含IIF=False | {Id, Name}</c>；
        /// S1（给 Dept 补 Properties 条目）：<c>含IIF=True | {... Dept = IIF((t1.Dept == null), null, new FluentDeptDto(){...})}</c>；
        /// S4（Dept 条目 Target 标注为嵌套类型）：<c>含IIF=True</c>。</para>
        /// <para>★ 结论：Fluent **能**表达嵌套，前提是 Dept 条目出现在 Properties / AllTargetSlots 里。
        /// 缺陷②的成因是「声明了则崩（IIF 无法翻译）」，缺陷④的成因是「没声明则整段跳过」。
        /// C2 让 Compose 在「在 AllTargetSlots 但不在 Properties」时也进递归入口，两者同时收口。</para>
        /// </summary>
        [Fact]
        public void S_DeclareDept_EmitsIIF()
        {
            // S0：不声明（嵌套对未注册）→ 无 IIF、无 Dept
            RegisterUndeclaredFluentSnapshot();
            var s0 = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocFluentSiteUndeclaredDto>();
            Assert.DoesNotContain("IIF", s0.ToString());
            Assert.DoesNotContain("Dept", s0.ToString());

            // S1/S4：把 Dept 放进 AllTargetSlots 与 Properties（Target 标注为嵌套 DTO 类型）→ 出 IIF
            RegisterDeclaredFluentSnapshot();
            var s1 = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocFluentSiteDeclaredDto>();
            var text = s1.ToString();
            Assert.Contains("IIF", text);
            Assert.Contains("Dept", text);
            Assert.Contains("PocFluentDeptDeclaredDto", text);

            // S4：嵌套条目的源侧必须是用户 join 的 t2（不是 t1.Dept 导航）——
            // 这是路线四的核心：IIF 里判的是 (t2 == null) 而非 (t1.Dept == null)
            Assert.Contains("t2", text);
            Assert.DoesNotContain("t1.Dept", text);
        }

        /// <summary>
        /// S 组（S3）：声明 Dept 后，**剥除**即可实跑（POC S3：<c>补 Dept + 剥 IIF（剥1）后实跑 → 3 行 | 1:F-IT</c>）。
        /// <para>★ 这条是「剥除 + 声明」组合的可行性证据：S2 证明声明后直接跑会崩，
        /// S3 证明剥除后能跑。C3 把剥除接到 16 个终端，正是基于这个结论。</para>
        /// <para>POC 用 3 条站点，本夹具 4 条；断言要点（不崩 + 行数守恒 + 首行嵌套有值）不变。</para>
        /// </summary>
        [Fact]
        public void S_StripAfterDeclare_Works()
        {
            RegisterDeclaredFluentSnapshot();
            var expr = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocFluentSiteDeclaredDto>();

            // S2 前提：原始产物含 IIF（未剥除翻译失败，见 D 组同款证据）
            Assert.Contains("IIF", expr.ToString());

            // S3：剥除后不含 IIF，且能实跑
            var stripped = FreeSqlNullGuardStripVisitor.Strip(expr);
            Assert.DoesNotContain("IIF", stripped.ToString());

            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .ToList(stripped);
            Assert.Equal(4, rows.Count);
            Assert.Equal("IT", rows[0].Dept?.Name);

            // 产品路径（走剥除 + guard）同样成立
            var viaDto = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocFluentSiteDeclaredDto>();
            Assert.Equal(4, viaDto.Count);
            Assert.Equal("IT", viaDto[0].Dept?.Name);
        }

        // ================================================================
        // 基础设施
        // ================================================================

        /// <summary>
        /// 登记「未声明嵌套」形态的 Fluent 主对（<c>PocFluentSiteUndeclaredDto</c>）：
        /// 只声明标量，嵌套对 <c>PocDept→PocFluentDeptUndeclaredDto</c> **不注册**。
        /// </summary>
        private static void RegisterUndeclaredFluentSnapshot()
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocSite),
                TargetType = typeof(PocFluentSiteUndeclaredDto),
                SourceTypeFqn = typeof(PocSite).FullName!,
                TargetTypeFqn = typeof(PocFluentSiteUndeclaredDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
                ComposeMappings = Array.Empty<RuntimeComposeMapping>(),
            });
        }

        /// <summary>
        /// 登记「已声明嵌套」形态的 Fluent 对（<c>PocFluentSiteDeclaredDto</c>）：
        /// 嵌套对已注册 ＋ 主对的 Properties 与 AllTargetSlots 都含 Dept（Target 标注为嵌套 DTO 类型）。
        /// </summary>
        private static void RegisterDeclaredFluentSnapshot()
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocDept),
                TargetType = typeof(PocFluentDeptDeclaredDto),
                SourceTypeFqn = typeof(PocDept).FullName!,
                TargetTypeFqn = typeof(PocFluentDeptDeclaredDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[] { Pm("Id", "Int32"), Pm("Name", "String") },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
                ComposeMappings = Array.Empty<RuntimeComposeMapping>(),
            });

            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocSite),
                TargetType = typeof(PocFluentSiteDeclaredDto),
                SourceTypeFqn = typeof(PocSite).FullName!,
                TargetTypeFqn = typeof(PocFluentSiteDeclaredDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    Pm("Id", "Int32"),
                    Pm("Name", "String"),
                    new RuntimePropertyMapping
                    {
                        Target = Slot("Dept", nameof(PocFluentDeptDeclaredDto)),
                        SourceName = nameof(PocSite.Dept),
                        SourcePathNames = new[] { nameof(PocSite.Dept) },
                        SourcePropertyTypeName = nameof(PocDept),
                    },
                },
                AllTargetSlots = new[]
                {
                    Slot("Id", "Int32"), Slot("Name", "String"), Slot("Dept", nameof(PocFluentDeptDeclaredDto)),
                },
                ComposeMappings = Array.Empty<RuntimeComposeMapping>(),
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
