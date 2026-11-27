// ============================================================================
// AC-10 验收用例 —— 跨路径行数相等（投影路径 vs 降级路径 vs 原生 join）
// ----------------------------------------------------------------------------
// 任务书 V1 卡验收第 5 条：
//   dotnet test ... --filter-class "*DegradeRowParity*"
//   → 看到用例 ProjectionAndDegrade_MustReturnSameRowCount 通过
//
// ★ 本文件是 C4（降级路径不丢行）的正面验收面：
//   改造前 FilterAndMap2/3、FilterValid2/3 把「任一源为 null」当剔除条件，
//   于是 LEFT JOIN 产生 null 附加源的行被丢掉 —— 投影 4 行 / 降级 2 行的分叉（POC C/G/I 组）。
//   改造后两档判据：主源 null 剔除、附加源 null 保留。
//
// ★ 三路对账是本文件的核心方法：原生 join 行数（真值）↔ 投影路径 ↔ 降级路径，
//   三者必须相等。只对两路会漏掉「两路一起错」的情形（例如都少算同一条）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.PocModels;

    /// <summary>
    /// AC-10：投影路径与降级路径的行数必须相等，且都等于原生 join 行数。
    /// <para>类名匹配 <c>--filter-class "*DegradeRowParity*"</c>。</para>
    /// </summary>
    public class DegradeRowParityTests : IClassFixture<PocFixture>
    {
        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public DegradeRowParityTests(PocFixture fixture) => _fsql = fixture.Fsql;

        /// <summary>
        /// AC-10 主用例：三路行数必须相等。
        /// <para>原生 join = 4（POC C0 真值）；投影路径（可译 DTO）= 4；降级路径（Convert 触发 DTO）= 4。</para>
        /// <para><b>原 POC C1/G3：投影 4 行 vs 降级 2 行 → 差距 2 行；修复后差距 0。</b></para>
        /// </summary>
        [Fact]
        public void ProjectionAndDegrade_MustReturnSameRowCount()
        {
            // ① 真值：原生 join 行数
            var native = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count();
            Assert.Equal(4, native);

            // ② 投影路径（PocSiteNestedDto 可译，走 SQL 投影）
            var projected = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(native, projected.Count);

            // ③ 降级路径（PocSiteConvNestedDto 的 Name 标了 Convert → 降级网命中 → 内存映射）
            Assert.True(FreeSqlDegradationDetector.NeedsDegradationCompose(
                typeof(PocSite), new[] { typeof(PocDept) }, typeof(PocSiteConvNestedDto), out _),
                "前提：Convert 形态必须命中降级网，否则本用例测的仍是投影路径");

            var degraded = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToList<PocSiteConvNestedDto>();
            Assert.Equal(native, degraded.Count);

            // ④ 三路相等（★ 核心断言）
            Assert.Equal(projected.Count, degraded.Count);

            // ⑤ 被保留的正是「附加源为 null」的那两行（不是巧合凑数）
            Assert.Contains(degraded, r => r.Id == 2);   // DeptId = null
            Assert.Contains(degraded, r => r.Id == 3);   // DeptId = 999（孤儿）
        }

        /// <summary>
        /// AC-10 补充 1：分页场景 Items.Count == TotalCount（POC I5 的原始分叉：<c>TotalCount=4 但 Items=2</c>）。
        /// <para>投影与降级两条路径、同步与异步两套终端，逐个核对。</para>
        /// </summary>
        [Fact]
        public void PagedItemsMustEqualTotalCount()
        {
            var native = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count();

            // 投影路径（同步 + 异步）
            var p1 = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedList<PocSiteNestedDto>(1, 100);
            Assert.Equal(native, p1.TotalCount);
            Assert.Equal(p1.TotalCount, p1.Items.Count);

            var p2 = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedListAsync<PocSiteNestedDto>(1, 100, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal(native, p2.TotalCount);
            Assert.Equal(p2.TotalCount, p2.Items.Count);

            // 降级路径（同步 + 异步）
            var d1 = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedList<PocSiteConvNestedDto>(1, 100);
            Assert.Equal(native, d1.TotalCount);
            Assert.Equal(d1.TotalCount, d1.Items.Count);

            var d2 = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedListAsync<PocSiteConvNestedDto>(1, 100, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal(native, d2.TotalCount);
            Assert.Equal(d2.TotalCount, d2.Items.Count);
        }

        /// <summary>
        /// AC-10 补充 2：Convert 触发降级后，行数仍等于原生 —— 覆盖「降级不是靠丢行换来的正确性」。
        /// <para>同时核对 <c>ToChunkDto</c> 的累计行数（POC C4：降级 chunk 累计 2 行 → 修复后 4 行）。</para>
        /// </summary>
        [Fact]
        public void ConvertTriggeredDegrade_KeepsAllRows()
        {
            var native = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count();

            // ToChunkDto（降级路径）
            var chunk = new List<PocSiteConvNestedDto>();
            _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToChunkDto<PocSiteConvNestedDto>(2, c => chunk.AddRange(c));
            Assert.Equal(native, chunk.Count);

            // ToChunkDtoAsync（降级路径，§11.4 的异步形态）
            var chunkAsync = new List<PocSiteConvNestedDto>();
            _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToChunkDtoAsync<PocSiteConvNestedDto>(2, c =>
                {
                    chunkAsync.AddRange(c);
                    return System.Threading.Tasks.Task.CompletedTask;
                }, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal(native, chunkAsync.Count);

            // Convert 生效：Name 经转换器处理（null → 空串），全部行都不是 null
            Assert.DoesNotContain(chunk, r => r.Name == null);

            // 三表降级路径（FilterAndMap3 / FilterValid3 同源判据）
            var three = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal(native, three.Count);
        }
    }
}
