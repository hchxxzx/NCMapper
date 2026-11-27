// ============================================================================
// AC-5 验收用例 —— Compose 全终端矩阵（16 个终端的行数必须逐个等于原生 join 行数）
// ----------------------------------------------------------------------------
// 任务书 V1 卡验收第 4 条：
//   dotnet test ... --filter-class "*ComposeTerminalMatrix*"
//   → 看到用例 AllSixteenTerminals_RowCountEqualsNativeJoin 通过（16 个终端行数逐个相等）
//
// ★ 为什么这条重要：C3 的改动是「给 16 个终端逐个接入 IIF 剥除 + guard 兜底」。
//   漏接任何一个终端，那个终端就会退回「翻译期崩溃」或「降级丢行」。
//   本用例用**同一份查询**把 16 个终端全跑一遍并逐个核对行数，是漏接的唯一机械判据。
//
// ★ 断言纪律（任务书 §11.2）：每个终端不只断言「不抛异常」，
//   还必须断言行数正确且嵌套值正确（首行 Dept.Name == "IT"）。
//   否则「只接 guard 兜底、不接剥除」的错误实现会让嵌套值全为 null 却照样通过。
//
// ★ 断言纪律（任务书 §11.4）：异步终端必须验证「失败时确实走降级」而非异常直冒；
//   本文件的异步终端用 await 正常路径 + 显式的 RunAsync 形态覆盖（见 AsyncTerminals 用例）。
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
    /// AC-5：Compose 门面双表 8 个 ＋ 三表 8 个终端，行数逐个等于原生 join 行数。
    /// <para>类名匹配 <c>--filter-class "*ComposeTerminalMatrix*"</c>。</para>
    /// </summary>
    public class ComposeTerminalMatrixTests : IClassFixture<PocFixture>
    {
        /// <summary>原生 join 真值（POC C0 基线：Site 4 条全保留）</summary>
        private const int NativeRowCount = 4;

        private readonly IFreeSql _fsql;

        /// <summary>构造 — 取 POC 共享 Fixture</summary>
        /// <param name="fixture">POC 共享 Fixture</param>
        public ComposeTerminalMatrixTests(PocFixture fixture) => _fsql = fixture.Fsql;

        /// <summary>
        /// AC-5 主用例：16 个终端的行数逐个等于原生 join 行数，且嵌套值正确。
        /// <para>覆盖双表 8 个（ToList / ToListAsync / First / FirstAsync /
        /// ToPagedList / ToPagedListAsync / ToChunkDto / ToChunkDtoAsync）
        /// ＋ 三表 8 个（同名同签名，仅泛型参数个数不同）。</para>
        /// <para>★ 每个终端的读数单独断言并记入失败消息，便于定位「漏接了哪一个」。</para>
        /// </summary>
        [Fact]
        public void AllSixteenTerminals_RowCountEqualsNativeJoin()
        {
            // 先锁原生真值（若数据变了，全部终端断言都失去意义）
            Assert.Equal(NativeRowCount, _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id).Count());
            Assert.Equal(NativeRowCount, _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id).Count());

            // 逐终端收集读数（终端名 → 实际行数 → 期望行数）。
            // ★ First / FirstAsync 语义上只取 1 条，期望值就是 1；其余终端期望 4（原生行数）。
            //   把期望值也写进表里（而不是统一用 NativeRowCount），是为了让「终端语义」
            //   本身也被显式表达 —— 否则 First 会被误判成漏接。
            var readings = new List<(string Terminal, int Actual, int Expected)>();

            // ---------- 双表 8 个 ----------
            readings.Add(("2表 ToList", TwoTableToList(), NativeRowCount));
            readings.Add(("2表 ToListAsync", TwoTableToListAsync(), NativeRowCount));
            readings.Add(("2表 First", TwoTableFirst(), 1));
            readings.Add(("2表 FirstAsync", TwoTableFirstAsync(), 1));
            readings.Add(("2表 ToPagedList", TwoTablePaged(), NativeRowCount));
            readings.Add(("2表 ToPagedListAsync", TwoTablePagedAsync(), NativeRowCount));
            readings.Add(("2表 ToChunkDto", TwoTableChunk(), NativeRowCount));
            readings.Add(("2表 ToChunkDtoAsync", TwoTableChunkAsync(), NativeRowCount));

            // ---------- 三表 8 个 ----------
            readings.Add(("3表 ToList", ThreeTableToList(), NativeRowCount));
            readings.Add(("3表 ToListAsync", ThreeTableToListAsync(), NativeRowCount));
            readings.Add(("3表 First", ThreeTableFirst(), 1));
            readings.Add(("3表 FirstAsync", ThreeTableFirstAsync(), 1));
            readings.Add(("3表 ToPagedList", ThreeTablePaged(), NativeRowCount));
            readings.Add(("3表 ToPagedListAsync", ThreeTablePagedAsync(), NativeRowCount));
            readings.Add(("3表 ToChunkDto", ThreeTableChunk(), NativeRowCount));
            readings.Add(("3表 ToChunkDtoAsync", ThreeTableChunkAsync(), NativeRowCount));

            // 恰好 16 个终端都被跑到（防漏列）
            Assert.Equal(16, readings.Count);

            // ★ 逐个核对行数（失败消息列出全部偏差，不只第一个）
            var mismatches = readings.Where(r => r.Actual != r.Expected)
                .Select(r => $"{r.Terminal} = {r.Actual}（应为 {r.Expected}）")
                .ToList();
            Assert.True(mismatches.Count == 0,
                "以下终端的行数与期望不符：" + string.Join("；", mismatches));

            // ★ 嵌套值必须正确（§11.2：不能只断言「不抛」）
            // 用主终端复核：首行部门必须是 IT，第 2/3 行为空壳，第 4 行为 HR-DELETED
            var sample = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal("IT", sample[0].Dept?.Name);
            Assert.Equal(0, sample[1].Dept?.Id);
            Assert.Equal(0, sample[2].Dept?.Id);
            Assert.Equal("HR-DELETED", sample[3].Dept?.Name);
        }

        /// <summary>
        /// AC-5 补充：16 个终端全部走「真实业务值」核对，而不只是行数。
        /// <para>任务书 §11.2 明确要求：任何「终端不抛异常」类断言都必须同时断言嵌套值正确。
        /// 本用例把「首行嵌套必须有 IT」这一条在每个终端上各验一遍 ——
        /// 若某个终端只接了 guard 兜底（没接剥除），该终端的嵌套值会是 null，本用例即红。</para>
        /// </summary>
        [Fact]
        public void AllSixteenTerminals_NestedValueCorrect()
        {
            // ---------- 双表 ----------
            var toList = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id).AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal("IT", toList[0].Dept?.Name);

            var toListAsync = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id).AsDto()
                .ToListAsync<PocSiteNestedDto>(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            Assert.Equal("IT", toListAsync[0].Dept?.Name);

            var first = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id).AsDto().First<PocSiteNestedDto>();
            Assert.Equal("IT", first?.Dept?.Name);

            var firstAsync = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id).AsDto()
                .FirstAsync<PocSiteNestedDto>(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            Assert.Equal("IT", firstAsync?.Dept?.Name);

            var paged = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id).AsDto().ToPagedList<PocSiteNestedDto>(1, 10);
            Assert.Equal("IT", paged.Items[0].Dept?.Name);

            var pagedAsync = _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id).AsDto()
                .ToPagedListAsync<PocSiteNestedDto>(1, 10, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal("IT", pagedAsync.Items[0].Dept?.Name);

            var chunk = new List<PocSiteNestedDto>();
            _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id).AsDto()
                .ToChunkDto<PocSiteNestedDto>(2, c => chunk.AddRange(c));
            Assert.Equal("IT", chunk[0].Dept?.Name);

            var chunkAsync = new List<PocSiteNestedDto>();
            _fsql.Select<PocSite, PocDept>().LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id).AsDto()
                .ToChunkDtoAsync<PocSiteNestedDto>(2, c =>
                {
                    chunkAsync.AddRange(c);
                    return System.Threading.Tasks.Task.CompletedTask;
                }, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal("IT", chunkAsync[0].Dept?.Name);

            // ---------- 三表（同形态，元数不同）----------
            var toList3 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id).AsDto().ToList<PocSiteNestedDto>();
            Assert.Equal("IT", toList3[0].Dept?.Name);

            var toListAsync3 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id).AsDto()
                .ToListAsync<PocSiteNestedDto>(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            Assert.Equal("IT", toListAsync3[0].Dept?.Name);

            var first3 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id).AsDto().First<PocSiteNestedDto>();
            Assert.Equal("IT", first3?.Dept?.Name);

            var firstAsync3 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id).AsDto()
                .FirstAsync<PocSiteNestedDto>(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            Assert.Equal("IT", firstAsync3?.Dept?.Name);

            var paged3 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id).AsDto().ToPagedList<PocSiteNestedDto>(1, 10);
            Assert.Equal("IT", paged3.Items[0].Dept?.Name);

            var pagedAsync3 = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id).AsDto()
                .ToPagedListAsync<PocSiteNestedDto>(1, 10, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal("IT", pagedAsync3.Items[0].Dept?.Name);

            var chunk3 = new List<PocSiteNestedDto>();
            _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id).AsDto()
                .ToChunkDto<PocSiteNestedDto>(2, c => chunk3.AddRange(c));
            Assert.Equal("IT", chunk3[0].Dept?.Name);

            var chunkAsync3 = new List<PocSiteNestedDto>();
            _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id).AsDto()
                .ToChunkDtoAsync<PocSiteNestedDto>(2, c =>
                {
                    chunkAsync3.AddRange(c);
                    return System.Threading.Tasks.Task.CompletedTask;
                }, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal("IT", chunkAsync3[0].Dept?.Name);
        }

        /// <summary>
        /// AC-5 补充（§11.4）：异步终端的 guard 形态 —— 异步失败时必须**确实走降级**，而非异常直冒。
        /// <para>构造方式：让目标 DTO 命中降级网（Convert），异步终端走降级路径；
        /// 断言不抛异常且行数正确。</para>
        /// <para>★ 这条覆盖 §11.4 的核心事实：FreeSql 翻译期异常在 async 通道**在 await 时抛**，
        /// 同步 try/catch 捕不到，必须用 <c>RunAsync</c>（catch 包在 await 之外）。
        /// 若有人把 <c>RunAsync</c> 改回同步形态，本用例的降级断言会红。</para>
        /// </summary>
        [Fact]
        public void AsyncTerminals_DegradeReached()
        {
            // Convert 触发降级 → 异步终端必须正常返回（走降级的 RunAsync 形态）
            var degradeList = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToListAsync<PocSiteConvNestedDto>(TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal(NativeRowCount, degradeList.Count);

            var degradePaged = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToPagedListAsync<PocSiteConvNestedDto>(1, 10, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal(NativeRowCount, degradePaged.TotalCount);
            Assert.Equal(degradePaged.TotalCount, degradePaged.Items.Count);

            var degradeChunk = new List<PocSiteConvNestedDto>();
            _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .AsDto().ToChunkDtoAsync<PocSiteConvNestedDto>(2, c =>
                {
                    degradeChunk.AddRange(c);
                    return System.Threading.Tasks.Task.CompletedTask;
                }, TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.Equal(NativeRowCount, degradeChunk.Count);

            // FirstAsync 降级路径
            var firstAsync = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .Where((a, b) => a.Id == 1)
                .AsDto().FirstAsync<PocSiteConvNestedDto>(TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();
            Assert.NotNull(firstAsync);
            Assert.Equal(1, firstAsync!.Id);
        }

        // ================================================================
        // 双表 8 终端（返回行数）
        // ================================================================

        private int TwoTableToList() => Query2().AsDto().ToList<PocSiteNestedDto>().Count;
        private int TwoTableToListAsync() => Query2().AsDto()
            .ToListAsync<PocSiteNestedDto>(TestContext.Current.CancellationToken).GetAwaiter().GetResult().Count;
        private int TwoTableFirst() => Query2().AsDto().First<PocSiteNestedDto>() is null ? 0 : 1;
        private int TwoTableFirstAsync() => Query2().AsDto()
            .FirstAsync<PocSiteNestedDto>(TestContext.Current.CancellationToken).GetAwaiter().GetResult() is null ? 0 : 1;
        private int TwoTablePaged() => Query2().AsDto().ToPagedList<PocSiteNestedDto>(1, 100).Items.Count;
        private int TwoTablePagedAsync() => Query2().AsDto()
            .ToPagedListAsync<PocSiteNestedDto>(1, 100, TestContext.Current.CancellationToken)
            .GetAwaiter().GetResult().Items.Count;
        private int TwoTableChunk()
        {
            var acc = new List<PocSiteNestedDto>();
            Query2().AsDto().ToChunkDto<PocSiteNestedDto>(2, c => acc.AddRange(c));
            return acc.Count;
        }
        private int TwoTableChunkAsync()
        {
            var acc = new List<PocSiteNestedDto>();
            Query2().AsDto().ToChunkDtoAsync<PocSiteNestedDto>(2, c =>
            {
                acc.AddRange(c);
                return System.Threading.Tasks.Task.CompletedTask;
            }, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            return acc.Count;
        }

        // ================================================================
        // 三表 8 终端（返回行数）
        // ================================================================

        private int ThreeTableToList() => Query3().AsDto().ToList<PocSiteNestedDto>().Count;
        private int ThreeTableToListAsync() => Query3().AsDto()
            .ToListAsync<PocSiteNestedDto>(TestContext.Current.CancellationToken).GetAwaiter().GetResult().Count;
        private int ThreeTableFirst() => Query3().AsDto().First<PocSiteNestedDto>() is null ? 0 : 1;
        private int ThreeTableFirstAsync() => Query3().AsDto()
            .FirstAsync<PocSiteNestedDto>(TestContext.Current.CancellationToken).GetAwaiter().GetResult() is null ? 0 : 1;
        private int ThreeTablePaged() => Query3().AsDto().ToPagedList<PocSiteNestedDto>(1, 100).Items.Count;
        private int ThreeTablePagedAsync() => Query3().AsDto()
            .ToPagedListAsync<PocSiteNestedDto>(1, 100, TestContext.Current.CancellationToken)
            .GetAwaiter().GetResult().Items.Count;
        private int ThreeTableChunk()
        {
            var acc = new List<PocSiteNestedDto>();
            Query3().AsDto().ToChunkDto<PocSiteNestedDto>(2, c => acc.AddRange(c));
            return acc.Count;
        }
        private int ThreeTableChunkAsync()
        {
            var acc = new List<PocSiteNestedDto>();
            Query3().AsDto().ToChunkDtoAsync<PocSiteNestedDto>(2, c =>
            {
                acc.AddRange(c);
                return System.Threading.Tasks.Task.CompletedTask;
            }, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            return acc.Count;
        }

        // ================================================================
        // 查询构造（每个终端用新查询对象，避免 FreeSql 内部状态串扰）
        // ================================================================

        private ISelect<PocSite, PocDept> Query2()
            => _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id);

        private ISelect<PocSite, PocDept, PocCompany> Query3()
            => _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id);
    }
}
