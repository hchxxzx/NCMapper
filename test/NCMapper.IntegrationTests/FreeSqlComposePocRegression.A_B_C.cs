// ============================================================================
// §6A.1 落盘：A、B、C 组（归属卡 C1）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// A（9 例）降级网对各类形态的判定 ＋ 炸点确证
// B（4 例）三个不可允许问题（剥 IIF 后暴露）
// C（7 例）数据分叉（约束B）—— 行数 / TotalCount 一致性
//
// ★ 修复后预期行为发生变化处，按任务书 §6A.4 第 4 条：断言**新行为**，并在方法注释中
//   写明「原 POC <例号>：<旧行为> → 修复后：<新行为>」。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// POC 回归 A 组 —— 降级网形态判定与炸点确证（9 例 → 2 个用例，断言条数不减）。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    public class FreeSqlComposePocRegressionABCTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposePocRegressionABCTests(PocFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // A 组
        // ================================================================

        /// <summary>
        /// A 组（A1/A2/A3）：降级网对三种形态的判定必须与 POC 一致。
        /// <para>A1 双表+同源嵌套 → 放行（不降级）；A2 双表+双层嵌套 → 放行；
        /// A3 双表+含 Convert+嵌套 → 降级（原因是 Convert）。</para>
        /// <para>A1/A2 判 <c>false</c> 是「缺陷根源」的固化——放行意味着走 SQL 投影路径，
        /// 而该路径正是缺陷①③④的发生地；若此处变成 true，说明降级网被误扩，
        /// 后续全部投影用例会退化成内存映射而静默通过，故必须锁死。</para>
        /// </summary>
        [Fact]
        public void A_NeedsDegradation_Matrix()
        {
            // A1：双表 + 同源嵌套 → 放行
            var need1 = FreeSqlDegradationDetector.NeedsDegradationCompose(
                typeof(PocSite), new[] { typeof(PocDept) }, typeof(PocSiteNestedDto), out var reason1);
            Assert.False(need1, $"A1 双表同源嵌套应放行（不降级），实际原因: {reason1}");
            Assert.Empty(reason1);

            // A2：双表 + 双层嵌套 → 放行
            var need2 = FreeSqlDegradationDetector.NeedsDegradationCompose(
                typeof(PocSite), new[] { typeof(PocDept) }, typeof(PocSiteDoubleNestedDto), out var reason2);
            Assert.False(need2, $"A2 双表双层嵌套应放行，实际原因: {reason2}");
            Assert.Empty(reason2);

            // A3：双表 + 含 Convert + 嵌套 → 降级（原因必须指向 Convert）
            var need3 = FreeSqlDegradationDetector.NeedsDegradationCompose(
                typeof(PocSite), new[] { typeof(PocDept) }, typeof(PocSiteConvNestedDto), out var reason3);
            Assert.True(need3, "A3 含 Convert 的嵌套 DTO 必须触发降级");
            Assert.Contains("ConvertAttribute", reason3);
        }

        /// <summary>
        /// A 组（A4~A9）：表达式形态与实跑结果的炸点确证。
        /// <para>A4 双表同源嵌套 BuildCompose 产物含 IIF（内核 null 传播注入）；
        /// A5/A6/A7 原 POC 全部炸 <c>null.Id cannot be null</c>；
        /// A8 无嵌套对照成功 4 行；A9 单表已修对照成功 4 行。</para>
        /// <para><b>原 POC A5/A6/A7：崩（FreeSql 翻译期 null.Id cannot be null）→ 修复后：正常返回 4 行。</b>
        /// 崩点消除是 C3（16 终端接入剥除 + guard 兜底）的产物。</para>
        /// </summary>
        [Fact]
        public void A_BombConfirmed_AnyArity()
        {
            // A4：产物含 IIF（这是「必须剥除」的前提，也是 W/F 组的共同基线）
            var expr2 = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>();
            Assert.Contains("IIF", expr2.ToString());

            var expr3 = FreeSqlProjectionBuilder.BuildCompose3<PocSite, PocDept, PocCompany, PocSiteNestedDto>();
            Assert.Contains("IIF", expr3.ToString());

            // A4-b：两表 + 双层嵌套 → 构建期 fail-closed（REQ-20261127-04）
            // 此前该形态能构建出表达式，但交给 FreeSql 翻译时在 t2.Company 导航链上无限递归，
            // 最终 StackOverflowException（进程级崩溃、catch 不住）。
            // 现改为构建期抛 MapConfigurationException：可诊断、可捕获、提示把 PocCompany 加入 join。
            var exDouble = Assert.Throws<MapConfigurationException>(
                () => FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteDoubleNestedDto>());
            Assert.Contains("PocCompany", exDouble.Message);
            Assert.Contains("不在本次查询的用户 join 源参数集", exDouble.Message);

            // A5：双表实跑（原炸点）→ 修复后 4 行，且嵌套值正确（§11.2：不得只断言「不抛」）
            var d5 = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, d5.Count);
            Assert.Equal("IT", d5[0].Dept?.Name);

            // A6：三表实跑（原炸点）→ 修复后 4 行
            var d6 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, d6.Count);
            Assert.Equal("IT", d6[0].Dept?.Name);

            // A7：双层嵌套实跑（原炸点）→ 修复后 4 行，内层公司 ACME 有值。
            // ★ 形态必须用**三表**：双层嵌套的内层（Dept.Company）是第二跳导航，
            // 只有把 PocCompany 作为 t3 join 进来，内层才能改指用户 join 的源参数（POC H4 的正确形态，
            // 实测 4 行 / 1:IT/ACME）。两表形态下内层没有对应的 join 参数，
            // 会退回 t2.Company 导航并让 FreeSql 翻译器无限递归（StackOverflowException，
            // 进程级崩溃、不可 catch）——该形态已作为 finding 记入交付说明，此处不写成断言。
            var d7 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id)
                .AsDto().ToList<PocSiteDoubleNestedDto>();
            Assert.Equal(4, d7.Count);
            Assert.Equal("IT", d7[0].Dept?.Name);
            Assert.Equal("ACME", d7[0].Dept?.Company?.Name);

            // A8：无嵌套对照 → 3 行（证明问题与嵌套有关，而不是与 join 有关）
            // 用主键过滤而非字符串 Contains：FreeSql 对双表 Select 的 `string.Contains` 生成 LIKE 时
            // 会栈溢出（ExpressionBinary ↔ ExpressionLambdaToSql 无限递归），与本缺陷无关，勿混入。
            var d8 = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .Where((a, b) => a.Id != 3)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteFlatDto>();
            Assert.Equal(3, d8.Count);
            Assert.Equal("S-normal", d8[0].Name);

            // A9：单表已修对照 → 4 行（同形态在单表通道一直可用，是 Compose 缺陷的对照面）
            var d9 = _fsql.Select<PocSite>().OrderBy(s => s.Id).AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, d9.Count);
            Assert.Equal("IT", d9[0].Dept?.Name);
        }

        // ================================================================
        // B 组 —— 三个不可允许问题
        // ================================================================

        /// <summary>
        /// B 组（B1）：用户 join 上的软删过滤必须生效——软删部门不得泄漏进 DTO。
        /// <para>POC B1 实测（缺陷态）：<c>1→IT, 2→, 3→, 4→HR-DELETED</c>，
        /// 即第 4 行拿到了 join 已过滤掉的 HR-DELETED（嵌套按 [Navigate] 自建了第二条 JOIN）。</para>
        /// <para><b>原 POC B1：4→HR-DELETED（软删泄漏）→ 修复后：4 的部门为空壳（Id=0、Name 空）</b>，
        /// 即 join 的 <c>!b.IsDeleted</c> 真正生效。</para>
        /// <para>断言取「部门名不得为 HR-DELETED」而非「必须为 null」：空壳语义是既有行为
        /// （见 Y 组），泄漏才是缺陷。两者都能区分泄漏与正确。</para>
        /// </summary>
        [Fact]
        public void B_JoinFilterIgnored_MustNotLeak()
        {
            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id && !b.IsDeleted)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();

            // 行数不受软删过滤影响（LEFT JOIN 保留全部 4 条站点）
            Assert.Equal(4, rows.Count);

            // 第 1 行：命中未删的 IT
            Assert.Equal("IT", rows[0].Dept?.Name);

            // ★ 第 4 行（外键指向软删部门 11）：join 已把它过滤掉 → 不得出现 HR-DELETED
            Assert.NotEqual("HR-DELETED", rows[3].Dept?.Name);
            Assert.DoesNotContain(rows, r => r.Dept?.Name == "HR-DELETED");

            // 且不得拿到软删部门的主键（防止「名字为空但 Id 是对的」这种半泄漏）
            Assert.NotEqual(11, rows[3].Dept?.Id);
        }

        /// <summary>
        /// B 组（B2）：T1 没有 [Navigate] 导航，但用户 join 了 T2 —— 嵌套值必须来自 T2。
        /// <para>POC B2 实测（缺陷态）：表达式 <c>(t1, t2) =&gt; new OrderDto() {Id, OrderNo}</c>（Dept 未进表达式），
        /// 实跑 <c>1→&lt;null&gt;</c>；按 join 应为 <c>1→CUST-1</c>。</para>
        /// <para><b>原 POC B2：嵌套恒 null（T2 有值却未进 DTO）→ 修复后：取到 CUST-1</b>。</para>
        /// <para>★ 场景前提：嵌套目标属性 <c>Customer</c> 必须在快照里存在一条「源类型 = PocCustomer」的映射对，
        /// 且主对 <c>PocOrder→PocOrderDto</c> 的 AllTargetSlots 含 Customer。本用例按源生成器产物形状
        /// 手工登记这两个快照（IntegrationTests 工程不挂 SourceGen Analyzer），故称「模拟生成产物」。</para>
        /// </summary>
        [Fact]
        public void B_T1NoNavigate_MustGetValue()
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

                // ★ 核心：T1 无导航，值只能来自 t2 参数
                Assert.NotNull(rows[0].Customer);
                Assert.Equal("CUST-1", rows[0].Customer!.Name);
                Assert.Equal(1, rows[0].Customer.Id);
            }
            finally
            {
                // 快照是进程级全局态：还原，避免污染其它用例（沿 #60/#65/#366 隔离范式）
                FreeSqlProjectionBuilder.ClearCache();
            }
        }

        /// <summary>
        /// B 组（B3）：用户 join 用业务码（与外键导航指向不同的行）—— 必须取用户 join 的那一行。
        /// <para>POC B3 实测（缺陷态）：<c>实际=1→区A | 按join应为=1→区B</c>。</para>
        /// <para><b>原 POC B3：区A（按外键导航自建 JOIN 取错行）→ 修复后：区B</b>。</para>
        /// </summary>
        [Fact]
        public void B_NonPkJoin_MustTakeJoinedRow()
        {
            // 前提复核：业务码指向区B，外键指向区A（错位是判定核心，若数据被改则本用例失去意义）
            var store = _fsql.Select<PocStore>().Where(s => s.Id == 1).First()!;
            Assert.Equal("B", store.RegionCode);
            Assert.Equal(1, store.RegionId);
            Assert.Equal("区A", _fsql.Select<PocRegion>().Where(r => r.Id == 1).First()!.Label);
            Assert.Equal("区B", _fsql.Select<PocRegion>().Where(r => r.Code == "B").First()!.Label);

            var rows = _fsql.Select<PocStore, PocRegion>()
                .LeftJoin((a, b) => a.RegionCode == b.Code)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocStoreDto>();

            Assert.Single(rows);
            // ★ 必须取业务码 join 到的区B，不得取外键导航的区A
            Assert.Equal("区B", rows[0].Region?.Label);
            Assert.NotEqual("区A", rows[0].Region?.Label);
        }

        /// <summary>
        /// B 组（B4）：嵌套 DTO 不得产生第二条 LEFT JOIN —— SQL 里只应有用户自己写的那一个。
        /// <para>POC B4 实测（缺陷态）：<c>LEFT JOIN 出现 2 次</c>，第二条
        /// <c>LEFT JOIN "Dept" a__Dept ON a__Dept."Id" = a."DeptId"</c> 是按 [Navigate] 自建的。</para>
        /// <para><b>原 POC B4：2 个 LEFT JOIN → 修复后：1 个</b>。</para>
        /// </summary>
        [Fact]
        public void B_NestedSelfJoin_CountMustBeOne()
        {
            var expr = FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteNestedDto>();

            // 先确认生效的是「剥除后」的表达式（SQL 投影分支实际执行的那份）
            var stripped = NCMapper.FreeSql.Internal.FreeSqlNullGuardStripVisitor.Strip(expr);

            var sql = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToSql(stripped);

            var joinCount = System.Text.RegularExpressions.Regex.Matches(sql, "LEFT JOIN").Count;
            Assert.Equal(1, joinCount);

            // 反向确认：不得出现自建 JOIN 的别名（POC 里的 a__Dept）
            Assert.DoesNotContain("a__Dept", sql);
            Assert.DoesNotContain("__Dept", sql);
        }

        // ================================================================
        // C 组 —— 约束B：行数与 TotalCount 一致性
        // ================================================================

        /// <summary>
        /// C 组（C1~C5、C7）：全部终端的行数必须等于原生 join 真值（4 行），
        /// 且分页的 <c>Items.Count == TotalCount</c>。
        /// <para>POC C1/C2/C4 实测（缺陷态）：降级 DTO 的 ToList / ToPagedList.Items / ToChunkDto 累计
        /// 都是 <b>2 行</b>，而 C3 的 TotalCount 是 <b>4</b> —— 即「Items 2 / TotalCount 4」的分叉。</para>
        /// <para><b>原 POC C1/C2/C4：2 行（丢行）→ 修复后：4 行；C3/C7 TotalCount 恒 4。</b></para>
        /// </summary>
        [Fact]
        public void C_TotalCountMustEqualItems()
        {
            const int nativeRows = 4;

            // 原生 join 真值（C0 基线，先把它锁住）
            Assert.Equal(nativeRows, _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count());

            // C6：纯可译 DTO 的 ToList（投影路径）
            var projected = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(nativeRows, projected.Count);

            // C3 + C7：TotalCount 必须等于真值
            var paged = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedList<PocSiteNestedDto>(1, 10);
            Assert.Equal(nativeRows, paged.TotalCount);

            // ★ 约束B 核心断言：Items.Count == TotalCount（当页未截断）
            Assert.Equal(paged.TotalCount, paged.Items.Count);

            // C1/C2 subset：降级 DTO（Convert 触发）同样要满足 Items == TotalCount
            var pagedDegrade = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedList<PocSiteConvNestedDto>(1, 10);
            Assert.Equal(nativeRows, pagedDegrade.TotalCount);
            Assert.Equal(pagedDegrade.TotalCount, pagedDegrade.Items.Count);

            // C4：ToChunkDto 累计行数
            var chunk = new List<PocSiteNestedDto>();
            _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToChunkDto<PocSiteNestedDto>(2, c => chunk.AddRange(c));
            Assert.Equal(nativeRows, chunk.Count);

            // C5：First 非空
            var first = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().First<PocSiteNestedDto>();
            Assert.NotNull(first);
            Assert.Equal(1, first!.Id);
        }

        /// <summary>
        /// C 组（C1~C4）：降级路径与投影路径的行数必须相等，且都等于原生 join 行数。
        /// <para>POC G3 同款对照（<c>投影 4 行 vs 降级 2 行 → 差距 2 行</c>），本用例从 C 组口径复验。</para>
        /// <para><b>原 POC C1：降级 2 行（差 2）→ 修复后：降级 4 行（差 0）。</b></para>
        /// </summary>
        [Fact]
        public void C_DegradeRowCountMustEqualNative()
        {
            var native = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count();

            // 投影路径
            var projectedCount = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteNestedDto>().Count;

            // 降级路径（Convert 触发降级网 → 内存映射）
            var degradeCount = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteConvNestedDto>().Count;

            Assert.Equal(native, projectedCount);
            Assert.Equal(native, degradeCount);
            Assert.Equal(4, native);
        }

        // ================================================================
        // 基础设施
        // ================================================================

        /// <summary>
        /// 按源生成器产物形状登记 <c>PocOrder→PocOrderDto</c> 与 <c>PocCustomer→PocCustomerDto</c> 两个快照。
        /// <para>目的：让「嵌套目标属性存在映射声明」这一前提成立，从而真正检验
        /// 「T1 无导航时嵌套改取 t2 参数」。不登记时内核拿不到嵌套主源，
        /// 测到的是「未声明形态」（另一种合法形态），无法判定缺陷②。</para>
        /// </summary>
        private static void RegisterOrderSnapshots()
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocCustomer),
                TargetType = typeof(PocCustomerDto),
                SourceTypeFqn = typeof(PocCustomer).FullName!,
                TargetTypeFqn = typeof(PocCustomerDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[]
                {
                    Pm("Id", "Int32"), Pm("Name", "String"),
                },
                AllTargetSlots = new[] { Slot("Id", "Int32"), Slot("Name", "String") },
            });

            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(PocOrder),
                TargetType = typeof(PocOrderDto),
                SourceTypeFqn = typeof(PocOrder).FullName!,
                TargetTypeFqn = typeof(PocOrderDto).FullName!,
                Channel = RuntimeMappingChannel.MapFrom,
                Properties = new[]
                {
                    Pm("Id", "Int32"), Pm("OrderNo", "String"),
                },
                AllTargetSlots = new[]
                {
                    Slot("Id", "Int32"), Slot("OrderNo", "String"), Slot("Customer", "PocCustomerDto"),
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
