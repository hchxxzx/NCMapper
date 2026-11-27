// ============================================================================
//  NCMapper · FreeSql Compose 通道 · REQ-20261127-04 防回退专项
//  ---------------------------------------------------------------------------
//  缺陷：两表 + 双层嵌套 DTO → FreeSql 翻译器在 t2.Company 这类导航链上无限递归
//        → StackOverflowException（进程级崩溃、catch 不住）。
//  实测原文：Stack overflow.
//            Repeated 27 times:
//              FreeSql.Internal.CommonExpression.ExpressionLambdaToSql(...)
//              FreeSql.Internal.CommonExpression.ExpressionBinary(...)
//  成因：嵌套投影入口 RedirectNestedSource 在作用域内匹配不到 join 源参数时
//        静默保留导航链（Soft 回退），交给 provider 自建 JOIN；多层嵌套下即无限递归。
//  修复：该情形改为 fail-closed 抛 MapConfigurationException（可诊断、可捕获），
//        提示用户把第二跳源类型加入 Select 泛型参数并显式 join。
//  正确形态：三表（把 Company 作为 t3 join 进来）→ 正常返回 4 行、内层 ACME 有值。
// ============================================================================
using System;
using System.Linq;
using FreeSql;
using NCMapper;
using NCMapper.FreeSql;
using NCMapper.IntegrationTests.PocModels;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// REQ-20261127-04 防回退：两表双层嵌套必须 fail-closed，不得栈溢出；
    /// 同时间接锁定「三表形态仍正常工作」，防止修复把合法形态一起拦掉。
    /// <para>本文件独立 fixture（不复用 PocFixture），避免与 POC 回归组的状态互相干扰。</para>
    /// </summary>
    public class DoubleNestedFailClosedTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly IFreeSql _fsql;

        public DoubleNestedFailClosedTests()
        {
            _dbPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"ncmapper_req04_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            var company = new PocCompany { Id = 1, Name = "ACME" };
            var dept = new PocDept { Id = 10, Name = "IT", IsDeleted = false, CompanyId = 1 };
            _fsql.Insert(company).ExecuteAffrows();
            _fsql.Insert(dept).ExecuteAffrows();
            _fsql.Insert(new PocSite { Id = 1, Name = "S1", DeptId = 10 }).ExecuteAffrows();
        }

        public void Dispose()
        {
            _fsql.Dispose();
            if (System.IO.File.Exists(_dbPath))
            {
                try { System.IO.File.Delete(_dbPath); } catch { }
            }
        }

        /// <summary>
        /// 构建期 fail-closed：两表 + 双层嵌套，内层 Company 不在 join 参数集 → 抛配置异常。
        /// <para>★ 这条是防回退核心：若有人把 fail-closed 改回「静默保留导航链」，
        /// 此处会从「抛异常」退化成「构建成功」，用例即红。</para>
        /// </summary>
        [Fact]
        public void TwoTable_DoubleNested_BuildMustFailClosed()
        {
            var ex = Assert.Throws<MapConfigurationException>(
                () => FreeSqlProjectionBuilder.BuildCompose<PocSite, PocDept, PocSiteDoubleNestedDto>());

            Assert.Contains("PocCompany", ex.Message);
            Assert.Contains("不在本次查询的用户 join 源参数集", ex.Message);
        }

        /// <summary>
        /// 实跑级验证：即便绕过构建期检查直接查询，也必须抛可捕获异常而非栈溢出。
        /// <para>栈溢出是进程级崩溃，Record.Exception 捕不到（测试进程直接死掉，
        /// 表现为整个 test run 失败并留下 Stack overflow 日志）。所以这条用例
        /// 能跑完并断言到异常类型，本身就证明没有栈溢出。</para>
        /// </summary>
        [Fact]
        public void TwoTable_DoubleNested_RunMustThrowNotCrash()
        {
            var ex = Record.Exception(() =>
                _fsql.Select<PocSite, PocDept>()
                    .LeftJoin((a, b) => a.DeptId == b.Id)
                    .OrderBy((a, b) => a.Id)
                    .AsDto()
                    .ToList<PocSiteDoubleNestedDto>());

            Assert.NotNull(ex);
            Assert.IsType<MapConfigurationException>(ex);
        }

        /// <summary>
        /// 对照：三表形态（Company 作为 t3 join）必须正常返回，内层 ACME 有值。
        /// <para>★ 防止修复过度——不能把「把源 join 进来」这个正确用法也拦掉。</para>
        /// </summary>
        [Fact]
        public void ThreeTable_DoubleNested_MustStillWork()
        {
            var rows = _fsql.Select<PocSite, PocDept, PocCompany>()
                .LeftJoin((a, b, c) => a.DeptId == b.Id)
                .LeftJoin((a, b, c) => b.CompanyId == c.Id)
                .OrderBy((a, b, c) => a.Id)
                .AsDto()
                .ToList<PocSiteDoubleNestedDto>();

            Assert.Single(rows);
            Assert.Equal("IT", rows[0].Dept?.Name);
            Assert.Equal("ACME", rows[0].Dept?.Company?.Name);
        }

        /// <summary>
        /// ★ 防「修复过度」的关键用例（复审 finding #1）：两表 + **单层**嵌套。
        /// <para>单层嵌套时源在 join 参数集中可直接命中；即便未命中，FreeSql 对单层导航
        /// 自建 JOIN 也能正常工作（实测返回业务值 IT）。fail-closed 只应对**多层**嵌套生效，
        /// 不得把单层可用形态一并拦掉——否则用户既拿不到投影、也拿不到降级，只剩异常。</para>
        /// </summary>
        [Fact]
        public void TwoTable_SingleNested_MustKeepWorking()
        {
            _fsql.Insert(new PocSite { Id = 2, Name = "S2", DeptId = null }).ExecuteAffrows();

            var rows = _fsql.Select<PocSite, PocDept>()
                .LeftJoin((a, b) => a.DeptId == b.Id)
                .OrderBy((a, b) => a.Id)
                .AsDto()
                .ToList<PocSiteNestedDto>();

            Assert.Equal(2, rows.Count);
            Assert.Equal("IT", rows[0].Dept?.Name);
        }

        /// <summary>
        /// ★ 防「整条查询失败」的关键用例（复审 finding #2）：
        /// 主源与泛型参数不匹配（声明错误）时，抛出的是既有的
        /// <see cref="InvalidOperationException"/>（内核对主源一致性的权威报错），
        /// **不是**本修复引入的 <see cref="MapConfigurationException"/>。
        /// <para>验证顺序约束：主源一致性校验必须先于 fail-closed 执行。
        /// 若顺序颠倒，异常类型会变成 MapConfigurationException（「源不在参数集」），
        /// 掩盖掉真正的原因（主源配错）。</para>
        /// </summary>
        [Fact]
        public void MismatchedPrimarySource_ReportsKernelErrorNotEmptySourceSetError()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => FreeSqlProjectionBuilder.BuildCompose<PocCompany, PocDept, PocSiteNestedDto>());

            // 断言异常来自「主源不匹配」这条权威校验，而非嵌套源参数集缺失
            Assert.Contains("主映射源类型", ex.Message);
            Assert.DoesNotContain("不在本次查询的用户 join 源参数集", ex.Message);
        }
    }
}
