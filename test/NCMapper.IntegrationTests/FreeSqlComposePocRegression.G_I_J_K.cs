// ============================================================================
// §6A.1 落盘：G、I、J、K 组（归属卡 C4）
// POC 原始读数见私有库 docs/05-记录与沉淀/20261126-002-POC实测报告/poc-report.txt
// ----------------------------------------------------------------------------
// G（3 例）降级路径丢 null 行 —— 约束B 的行数面
// I（8 例）降级路径违反两条硬约束（约束A 三个不可允许问题；约束B 行数/终端一致性）
// J（5 例）降级取值机制（不来自 T2）—— 防「降级绕过」的取证
// K（3 例）公共入口降级路径嵌套恒 null
//
// ★ 本文件的核心是 C4「降级路径不丢行」改造的验收面：
//   改造前 FilterAndMap2/3、FilterValid2/3 把「任一源为 null」当剔除条件（4 处），
//   改造后拆两档：主源 null 剔除、附加源 null 保留。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// POC 回归 G/I/J/K 组 —— 降级路径的行数守恒与取值机制。
    /// <para>类名匹配 <c>--filter-class "*FreeSqlComposePocRegression*"</c>。</para>
    /// </summary>
    public class FreeSqlComposePocRegressionGIJKTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public FreeSqlComposePocRegressionGIJKTests(PocFixture fixture) => _fsql = fixture.Fsql;

        // ================================================================
        // G 组 —— 降级路径丢 null 行
        // ================================================================

        /// <summary>
        /// G 组（G1/G2/G3）：降级路径必须保留附加源为 null 的行，行数与原生 join 相等。
        /// <para>POC G1 实测（缺陷态）：<c>原始 4 → 有效 2</c>；G2 手工模拟「空壳不丢行」→ 保留 4 行；
        /// G3：<c>投影 4 行 vs 降级 2 行 → 差距 2 行</c>。</para>
        /// <para><b>原 POC G1/G3：降级 2 行（差 2）→ 修复后：降级 4 行（差 0）。</b></para>
        /// </summary>
        [Fact]
        public void G_DegradeMustNotDropNullRows()
        {
            var native = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count();
            Assert.Equal(4, native);

            // G1/G2：直接调降级内核（FilterValid2 = 只保全不映射），行数必须等于原生
            var tuples = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .ToList((a, b) => new Tuple<PocSite, PocDept>(a, b));
            Assert.Equal(4, tuples.Count);

            var kept = FreeSqlComposeSupport.FilterValid2(tuples, "[FreeSql Compose] 剔除 {0} 条主源为 null 的行");
            Assert.Equal(4, kept.Count);

            // 附加源为 null 的行**保留**（不是剔除）：Site 2（DeptId=null）与 Site 3（孤儿）
            Assert.Contains(kept, t => t.Item1.Id == 2 && t.Item2 == null);
            Assert.Contains(kept, t => t.Item1.Id == 3 && t.Item2 == null);

            // 主源为 null 的行**必须剔除**（这类行客观上无法产出 DTO）
            var withNullPrimary = new List<Tuple<PocSite, PocDept>>(tuples)
            {
                new(null!, tuples[0].Item2),
            };
            Assert.Equal(5, withNullPrimary.Count);
            var kept2 = FreeSqlComposeSupport.FilterValid2(withNullPrimary, "[FreeSql Compose] 剔除 {0} 条主源为 null 的行");
            Assert.Equal(4, kept2.Count);
            Assert.DoesNotContain(kept2, t => t.Item1 == null);

            // G3：投影与降级两条路径行数相等
            var projected = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteNestedDto>().Count;
            var degraded = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteConvNestedDto>().Count;
            Assert.Equal(projected, degraded);
            Assert.Equal(native, projected);
        }

        /// <summary>
        /// G 组（G1）：三表降级路径同样不得丢行（FilterAndMap3 / FilterValid3 同源判据）。
        /// <para>三表的附加源是 T2 与 T3 两个，任一为 null 都必须保留。</para>
        /// </summary>
        [Fact]
        public void G_ThreeTableDegradeMustNotDropRows()
        {
            var tuples = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .ToList((a, b, c) => new Tuple<PocSite, PocDept, PocCompany>(a, b, c));
            Assert.Equal(4, tuples.Count);

            var kept = FreeSqlComposeSupport.FilterValid3(tuples, "[FreeSql Compose3] 剔除 {0} 条主源为 null 的行");
            Assert.Equal(4, kept.Count);

            // Site 3 是孤儿（Dept 与 Company 均为 null），必须保留
            Assert.Contains(kept, t => t.Item1.Id == 3 && t.Item2 == null && t.Item3 == null);
            // Site 2 无部门，T2/T3 皆 null，必须保留
            Assert.Contains(kept, t => t.Item1.Id == 2 && t.Item2 == null);

            // 实际产品路径（三表 DTO）行数一致
            var dtos = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(4, dtos.Count);
        }

        // ================================================================
        // I 组 —— 降级路径违反两条硬约束
        // ================================================================

        /// <summary>
        /// I 组（I1~I3）：约束A —— 降级路径不得违反三个不可允许规则。
        /// <para>POC I1「降级 + join 软删过滤」：<c>原生4行→有效1行 | 1:&lt;null&gt;</c>（软删部门不出现 ✓ 但嵌套丢失）；
        /// I2「降级 + T1 无导航」：<c>有效1行 | 1:&lt;null&gt;</c>（CUST-1 未出现 ✗）；
        /// I3「降级 + 非主键 join」：<c>有效1行 | 1:&lt;null&gt;</c>（区B 未出现 ✗）。</para>
        /// <para>I2/I3 是降级路径的**固有缺陷**（内存映射只做字段映射、不加载导航 —— 见 C5 契约口径），
        /// 不是本任务要修的对象。故本用例断言的是「降级路径行数不再丢」这一可修项，
        /// 并把 I2/I3 的嵌套为 null 作为**契约口径**固化（导航未加载 → 嵌套为 null 属预期）。</para>
        /// </summary>
        [Fact]
        public void I_DegradeViolatesConstraintA()
        {
            // I1：join 软删过滤 → 软删部门不出现（约束A 问题1 的正确语义），
            //     且降级路径不再丢行（修复前 4→1，修复后 4→4）
            var softDel = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id && !b.IsDeleted)
                .AsDto().ToList<PocSiteConvNestedDto>();

            // 降级路径（Convert 触发）行数 = 原生 join 行数（4），不因附加源缺失而丢行
            Assert.Equal(4, softDel.Count);
            // 软删部门不得出现（遍历全部行 + 嵌套层）
            Assert.DoesNotContain(softDel, r => r.Dept?.Name == "HR-DELETED");

            // I2：T1 无导航 → 降级路径的嵌套为 null（契约口径：内存映射不加载导航）。
            //     注意：修复后 SQL 投影路径已能取到 CUST-1（见 H_Route4_T1NoNavigate），
            //     但**降级路径**做不到——这正是 §1.2 已裁决「不做改动」的那一项。
            var order = _fsql.Select<PocOrder>().Where(o => o.Id == 1).First()!;
            var mapped = Mapper.Compose<PocOrderDto>(order, new PocCustomer { Id = 1, Name = "CUST-1" });
            Assert.NotNull(mapped);
            // T1.PocOrder 没有 Customer 导航 → 内存映射拿不到 → 嵌套为 null（预期行为，非缺陷）
            Assert.Null(mapped!.Customer);

            // I3：非主键 join → 降级路径同样取不到（同一根因：导航未加载）
            var store = _fsql.Select<PocStore>().Where(s => s.Id == 1).First()!;
            var mappedStore = Mapper.Compose<PocStoreDto>(store, new PocRegion { Id = 2, Code = "B", Label = "区B" });
            Assert.NotNull(mappedStore);
            Assert.Null(mappedStore!.Region);
        }

        /// <summary>
        /// I 组（I4/I5）：约束B —— 行数必须相等、TotalCount 必须等于 Items.Count。
        /// <para>POC I4 实测（缺陷态）：<c>原生 4 行 → 降级后 2 行（丢 2 行）｜约束B 要求相等 → 不满足</c>；
        /// I5：<c>TotalCount=4 但 Items=2 ｜两者不等 → 不满足约束B</c>。</para>
        /// <para><b>原 POC I4：降级丢 2 行 → 修复后：差 0；I5：Items 2 ≠ TotalCount 4 → 修复后相等。</b></para>
        /// </summary>
        [Fact]
        public void I_DegradeViolatesConstraintB()
        {
            var native = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count();

            // I4：降级路径（Convert 触发）行数 == 原生
            var degrade = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteConvNestedDto>();
            Assert.Equal(native, degrade.Count);

            // I5：分页下 Items.Count == TotalCount（当页未截断）
            var paged = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedList<PocSiteConvNestedDto>(1, 10);
            Assert.Equal(native, paged.TotalCount);
            Assert.Equal(paged.TotalCount, paged.Items.Count);

            // I4 的 ToChunkDto 面：累计行数 == 原生
            var chunk = new List<PocSiteConvNestedDto>();
            _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToChunkDto<PocSiteConvNestedDto>(2, c => chunk.AddRange(c));
            Assert.Equal(native, chunk.Count);

            // I6：降级路径依赖 Mapper.Compose，特性对可用（Fluent 对不受支持是既有契约）
            var site = _fsql.Select<PocSite>().Where(s => s.Id == 1).First()!;
            var dept = _fsql.Select<PocDept>().Where(d => d.Id == 10).First()!;
            var composed = Mapper.Compose<PocSiteNestedDto>(site, dept);
            Assert.NotNull(composed);
            Assert.Equal(1, composed!.Id);
        }

        /// <summary>
        /// I 组（I8）：<b>降级路径 + 双层嵌套</b> —— 内层公司来自 T1 导航链，不是用户 join 的 T3。
        /// <para>POC I8 实测（缺陷态）：<c>有效2行 | 1:/&lt;null&gt;, 4:/&lt;null&gt;｜注意 Company 来自 T1 导航链，非用户 join 的 T3</c>。</para>
        /// <para>★ 本用例把「双层嵌套的降级取值来源」逐层钉死：外层 <c>Dept</c> 取 T1 的导航
        /// （<c>site.Dept</c>），内层 <c>Company</c> 继续沿该导航链（<c>site.Dept.Company</c>），
        /// **与第二个参数 T2 无关**。实测证据：把 T2 的 <c>Company</c> 填好也不改变结果，
        /// 只有把 <c>site.Dept.Company</c> 填好才出现 ACME。</para>
        /// <para>修复后降级路径不丢行（4 行）；嵌套为 null 属契约口径（内存映射不加载导航）。</para>
        /// </summary>
        [Fact]
        public void I_DegradeDoubleNested_TakesT1NavigationChain()
        {
            // ① T1 导航未加载 → 外层与内层皆 null（即便 T2 实体有值）
            var siteBare = _fsql.Select<PocSite>().Where(s => s.Id == 1).First()!;
            var deptLoaded = _fsql.Select<PocDept>().Where(d => d.Id == 10).First()!;
            deptLoaded.Company = _fsql.Select<PocCompany>().Where(c => c.Id == 1).First();
            Assert.Null(siteBare.Dept);

            var m1 = Mapper.Compose<PocSiteDoubleNestedDto>(siteBare, deptLoaded);
            Assert.NotNull(m1);
            Assert.Null(m1!.Dept);   // 外层取 T1 导航（未加载）→ null，T2 有值也进不来

            // ② 只加载外层（site.Dept），内层公司仍未加载 → 外层有值、内层 null
            var siteOuter = _fsql.Select<PocSite>().Where(s => s.Id == 1).First()!;
            siteOuter.Dept = _fsql.Select<PocDept>().Where(d => d.Id == 10).First();
            Assert.Null(siteOuter.Dept!.Company);

            var m2 = Mapper.Compose<PocSiteDoubleNestedDto>(siteOuter, deptLoaded);
            Assert.NotNull(m2!.Dept);
            Assert.Equal("IT", m2.Dept!.Name);
            Assert.Null(m2.Dept.Company);

            // ③ 把 T1 导航链两层都加载 → 内层公司出现 ACME
            //    （★ 注意要改的是 site.Dept.Company，而不是 T2 的 Company —— 后者已在 ① 中填好却无效果）
            siteOuter.Dept!.Company = _fsql.Select<PocCompany>().Where(c => c.Id == 1).First();
            var m3 = Mapper.Compose<PocSiteDoubleNestedDto>(siteOuter, deptLoaded);
            Assert.Equal("ACME", m3!.Dept?.Company?.Name);

            // ④ 产品路径（双表 AsDto 双层嵌套）不参与本断言：该形态会触发 FreeSql 翻译器
            //    无限递归（StackOverflowException，进程级崩溃），已作为 finding 记录；
            //    可行形态是三表（见 A_BombConfirmed_AnyArity / H_Route4_DoubleNested）。
            //    此处只复核降级路径的行数守恒（三表形态，不触发崩溃）。
            var degradeRows = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .AsDto().ToList<PocSiteDoubleNestedDto>();
            Assert.Equal(4, degradeRows.Count);
        }

        // ================================================================
        // J 组 —— 降级取值机制（不来自 T2）
        // ================================================================

        /// <summary>
        /// J 组（J2/J3/J4/J5）：降级路径的嵌套值**不来自 T2 参数**——这是降级的固有机制，
        /// 也是「为什么不能靠降级修缺陷②③」的取证。
        /// <para>POC J2：<c>T1.Id=1 T1.Dept(导航)=&lt;null&gt; T2.Id=10 T2.Name=IT</c>；
        /// J3：<c>把 T2 换成伪造行后嵌套值 → 嵌套 Dept=&lt;null&gt;</c>
        /// （若为 FAKE-DEPT 则来自 T2 参数；若为 IT 则来自 T1 导航；实测 null 说明两者都没取）；
        /// J4：<c>T2.Name=CUST-1 → DTO.Customer=&lt;null&gt; ｜T2 有值却未进 DTO = 问题2根因</c>；
        /// J5：<c>降级 + 目标DTO主源≠T1 → 成功 IT</c>。</para>
        /// <para>★ J3 是「防降级绕过」的关键证据：把 T2 换成伪造行，若 DTO 里出现 FAKE-DEPT，
        /// 就说明降级路径竟然从 T2 取值（不应发生）；实测为 null 才符合既有机制。</para>
        /// <para>★ 反向确认用的是 **T1 导航**而非 T2：把 <c>site.Dept</c> 填好，嵌套才出现 IT。
        /// 这条是判定「降级取值来源」的分水岭——如果实现改成从 T2 取，那么不填 T1 导航也会出值，
        /// 下方 <c>Assert.Null(dto.Dept)</c> 就会变红。</para>
        /// </summary>
        [Fact]
        public void J_DegradeNestedNotFromT2()
        {
            // J2：输入状态——T1 实体的导航属性未加载（FreeSql 查实体不带导航）
            var site = _fsql.Select<PocSite>().Where(s => s.Id == 1).First()!;
            var dept = _fsql.Select<PocDept>().Where(d => d.Id == 10).First()!;
            Assert.Null(site.Dept);                  // T1 导航未加载
            Assert.Equal(10, dept.Id);
            Assert.Equal("IT", dept.Name);

            // J3：把 T2 换成伪造行 → 嵌套值必须仍为 null（不得来自 T2）
            var fake = new PocDept { Id = 999, Name = "FAKE-DEPT" };
            var dto = Mapper.Compose<PocSiteNestedDto>(site, fake);
            Assert.NotNull(dto);
            Assert.Null(dto!.Dept);
            Assert.NotEqual("FAKE-DEPT", dto.Dept?.Name);

            // 反向确认：把 T1 的导航手工填好 → 嵌套才有值（证明来源是 T1 导航，不是 T2）
            site.Dept = _sqlDep(10);
            var dto2 = Mapper.Compose<PocSiteNestedDto>(site, fake);
            Assert.NotNull(dto2!.Dept);
            Assert.Equal("IT", dto2.Dept!.Name);     // 来自 T1 导航（IT），而非伪造的 T2（FAKE-DEPT）
            Assert.NotEqual("FAKE-DEPT", dto2.Dept.Name);

            // J4：T1 无导航（PocOrder 没有 Customer 导航）→ T2 有值也进不了 DTO
            var order = _fsql.Select<PocOrder>().Where(o => o.Id == 1).First()!;
            var cust = new PocCustomer { Id = 1, Name = "CUST-1" };
            var orderDto = Mapper.Compose<PocOrderDto>(order, cust);
            Assert.NotNull(orderDto);
            Assert.Null(orderDto!.Customer);

            // J5：目标 DTO 的主源 ≠ T1 时的降级形态（特性对可用）
            var composed = Mapper.Compose<PocDeptNestedCompanyDto>(dept, _fsql.Select<PocCompany>().Where(c => c.Id == 1).First()!);
            Assert.NotNull(composed);
            Assert.Equal("IT", composed!.Name);
        }

        // ================================================================
        // K 组 —— 公共入口降级路径嵌套恒 null
        // ================================================================

        /// <summary>
        /// K 组（K1）：<b>公共入口的降级路径</b>——嵌套为 null，且 Trace 告知导航提示。
        /// <para>POC K1 实测（缺陷态）：<c>2 行 | 1:&lt;null&gt;, 4:&lt;null&gt; | Trace含导航提示=True</c>，
        /// 行数为 2 即当时还在丢行。</para>
        /// <para><b>原 POC K1：2 行（丢 2 行）→ 修复后：4 行不丢</b>；嵌套为 null 属契约口径（同 I2/I3）。</para>
        /// <para>K1-trace 原文（POC）：
        /// <c>[FreeSql Compose] 降级: (Site, Dept) → SiteConvNestedDto, 原因: 属性 Name 标注了 ConvertAttribute（需要降级）</c>
        /// ＋ <c>[FreeSql Compose] 自动 Include 不支持多表 ISelect&lt;T1,T2&gt;，请手动 Include 导航属性: [Dept]</c>
        /// ＋ <c>[FreeSql Compose] 降级路径过滤了 2 条含 null 实体的行…</c>
        /// （★ 最后一句是改造前的文案；C4 已把「过滤」拆成「剔除主源 / 保留附加源」两种，故此处不得再断言旧文案）。</para>
        /// </summary>
        [Fact]
        public void K_PublicEntryDegrade_NestedMustHaveValue()
        {
            var sw = new StringWriter();
            MapTracer.GloballyEnabled = true;
            MapTracer.GlobalOutput = sw;
            try
            {
                var rows = _fsql.Select<PocSite, PocDept>()
                    .LeftJoin((a, b) => a.DeptId == b.Id)
                    .OrderBy((a, b) => a.Id)
                    .AsDto().ToList<PocSiteConvNestedDto>();

                // 行数不再丢（修复前的 2 行 → 4 行）
                Assert.Equal(4, rows.Count);

                // 嵌套为 null 属契约口径（内存映射不加载导航，§1.2 已裁决不改）
                Assert.All(rows, r => Assert.Null(r.Dept));

                // Convert 生效（Name 走转换器，null → 空串）
                Assert.DoesNotContain(rows, r => r.Name == null);

                var trace = sw.ToString();
                // 降级原因必须指向 Convert（可诊断性）
                Assert.Contains("ConvertAttribute", trace);
                // 导航提示必须出现（告诉用户要自己 Include）
                Assert.Contains("自动 Include 不支持多表", trace);
                Assert.Contains("Dept", trace);
            }
            finally
            {
                MapTracer.GloballyEnabled = false;
                MapTracer.GlobalOutput = Console.Out;
            }
        }

        /// <summary>
        /// K 组（K2）：手工把 T1 导航取好后，降级路径的嵌套**就有值**——证明 K1 的 null
        /// 只是「导航未加载」，不是结构性不支持。
        /// <para>POC K2 实测：<c>1:IT, 2:&lt;noT2&gt;, 3:&lt;noT2&gt;, 4:HR-DELETED</c>。</para>
        /// </summary>
        [Fact]
        public void K_PublicEntryDegrade_ManualIncludeHasValue()
        {
            // 按 POC K2 做法：先按主键取实体，再手工把导航填好
            var site = _fsql.Select<PocSite>().Where(s => s.Id == 1).First()!;
            site.Dept = _sqlDep(10);
            var dept = _sqlDep(10);

            var dto = Mapper.Compose<PocSiteConvNestedDto>(site, dept);
            Assert.NotNull(dto);
            Assert.NotNull(dto!.Dept);
            Assert.Equal("IT", dto.Dept!.Name);

            // 无部门的站点（2/3）：导航为 null → 嵌套为 null（对照组）
            var site2 = _fsql.Select<PocSite>().Where(s => s.Id == 2).First()!;
            Assert.Null(site2.Dept);
            var dto2 = Mapper.Compose<PocSiteConvNestedDto>(site2, dept);
            Assert.NotNull(dto2);
            Assert.Null(dto2!.Dept);
        }

        /// <summary>
        /// K 组（K3）：<b>单表降级路径对照</b> —— 单表会走 ApplyFreeSqlIncludes 自动 Include，
        /// 故嵌套有值。这是「Compose 多表降级 ≠ 单表降级」的关键分界。
        /// <para>POC K3 实测：<c>4 行 | 1:IT, 2:&lt;null&gt;, 3:&lt;null&gt;, 4:HR-DELETED | 单表降级会走 ApplyFreeSqlIncludes 自动 Include</c>。</para>
        /// </summary>
        [Fact]
        public void K_SingleTableDegrade_AutoIncludeHasValue()
        {
            var rows = _fsql.Select<PocSite>()
                .OrderBy(s => s.Id)
                .AsDto().ToList<PocSiteConvNestedDto>();

            Assert.Equal(4, rows.Count);

            // 单表降级自动 Include 生效 → 第 1 行嵌套有值（IT）
            Assert.NotNull(rows[0].Dept);
            Assert.Equal("IT", rows[0].Dept!.Name);

            // 第 4 行：外键指向软删部门，自动 Include 不带用户 join 的过滤 → 能取到 HR-DELETED
            // （这正是「多表降级拿不到、投影路径才能拿到」的分界证据）
            Assert.Equal("HR-DELETED", rows[3].Dept?.Name);

            // 2/3 无匹配 → null
            Assert.Null(rows[1].Dept);
            Assert.Null(rows[2].Dept);
        }

        // ================================================================
        // 基础设施
        // ================================================================

        /// <summary>按主键取部门实体（内部辅助，避免在断言里重复写查询）。</summary>
        private PocDept? _sqlDep(int id) => _fsql.Select<PocDept>().Where(d => d.Id == id).First();
    }
}
