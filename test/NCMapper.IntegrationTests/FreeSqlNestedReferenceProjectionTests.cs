// FreeSql 引用导航嵌套投影：SQL 投影恢复 + 空壳语义锁定
// 模型自包含（Nfr* 前缀，不与 夹具交叉）
// [Collection("PipelineGlobalState")] 串行纪律沿用（MapTracer 全局态操作兼容）。
// 断言配方 MapTracer.GloballyEnabled=true + GlobalOutput=StringWriter 捕获
// finally 还原（TraceCapture）；断言「无降级行」=走 SQL 投影，「含降级+原始异常」=兜底链路。
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.FreeSql.Internal;
using Xunit;

namespace NCMapper.IntegrationTests.Models
{
    public class NfrCompany
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class NfrDept
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int OrgId { get; set; }
        public int? CompanyId { get; set; }

        [global::FreeSql.DataAnnotations.Navigate(nameof(CompanyId))]
        public NfrCompany? Company { get; set; }
    }

    public class NfrSite
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int? DeptId { get; set; }

        [global::FreeSql.DataAnnotations.Navigate(nameof(DeptId))]
        public NfrDept? Dept { get; set; }
    }

    /// <summary>纯同源嵌套 DTO（无跨表字段）——修复前公共入口触发即炸（评估 P1c），修复后 SQL 投影恢复载体</summary>
    [MapFrom(typeof(NfrDept))]
    public class NfrDeptDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [MapFrom(typeof(NfrSite))]
    public class NfrSiteDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public NfrDeptDto? Dept { get; set; }
    }

    /// <summary>跨表形态（既有降级网守卫载体——C2：判据不动，行为不变）</summary>
    [MapFrom(typeof(NfrDept))]
    public class NfrDeptCrossDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(NfrCompany), nameof(NfrCompany.Name))]
        public string? CompanyName { get; set; }
    }

    [MapFrom(typeof(NfrSite))]
    public class NfrSiteCrossDto
    {
        public int Id { get; set; }
        public NfrDeptCrossDto? Dept { get; set; }
    }
}

namespace NCMapper.IntegrationTests
{
    using global::NCMapper.IntegrationTests.Models;

    [Collection("PipelineGlobalState")]
    public class FreeSqlNestedReferenceProjectionTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public FreeSqlNestedReferenceProjectionTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_nfr_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"Data Source={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            _fsql.Insert(new NfrCompany { Id = 1, Name = "ACME" }).ExecuteAffrows();
            _fsql.Insert(new NfrDept { Id = 10, Name = "IT", OrgId = 1, CompanyId = 1 }).ExecuteAffrows();
            _fsql.Insert(new NfrSite { Id = 1, Name = "SITE-1", DeptId = 10 }).ExecuteAffrows();
            _fsql.Insert(new NfrSite { Id = 2, Name = "SITE-NULL", DeptId = null }).ExecuteAffrows();
        }

        public void Dispose()
        {
            _fsql.Dispose();
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        /// <summary>Trace 断言配方全局开关 + StringWriter 捕获，Dispose 还原全局态</summary>
        private sealed class TraceCapture : IDisposable
        {
            private readonly bool _prevEnabled;
            private readonly System.IO.TextWriter _prevOutput;

            internal System.IO.StringWriter Writer { get; } = new();

            internal TraceCapture()
            {
                _prevEnabled = MapTracer.GloballyEnabled;
                _prevOutput = MapTracer.GlobalOutput;
                MapTracer.GloballyEnabled = true;
                MapTracer.GlobalOutput = Writer;
            }

            public void Dispose()
            {
                MapTracer.GloballyEnabled = _prevEnabled;
                MapTracer.GlobalOutput = _prevOutput;
            }
        }

        /// <summary>
        /// 例①+③（P1c 转正）：纯同源嵌套公共入口走 SQL 投影，值正确且 Trace 无降级行。
        /// 修复前：FreeSql 内部异常「null.Id cannot be null」（评估 20261029-006 P1c 实证）。
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_PureRefNested_SqlProjection_ValuesCorrect_NoDegrade()
        {
            string trace;
            List<NfrSiteDto> dtos;
            using (var tc = new TraceCapture())
            {
                dtos = _fsql.Select<NfrSite>().Where(s => s.Id == 1).AsDto().ToList<NfrSiteDto>();
                trace = tc.Writer.ToString();
            }

            var dto = Assert.Single(dtos);
            Assert.Equal(1, dto.Id);
            Assert.Equal("SITE-1", dto.Name);
            Assert.NotNull(dto.Dept);
            Assert.Equal(10, dto.Dept!.Id);
            Assert.Equal("IT", dto.Dept.Name);
            // Trace 断言：走 SQL 投影而非降级（配方）
            Assert.DoesNotContain("降级", trace);
            Assert.DoesNotContain("SQL 投影失败", trace);
        }

        /// <summary>
        /// 例②（空壳语义锁定）：null 导航 → 空壳 DTO 逐字段（Dept!=null / int 字段 0 / 引用字段 null）。
        /// 语义变化已经接受并文档化。
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_PureRefNested_NullNavigation_EmptyShellSemantics()
        {
            var dtos = _fsql.Select<NfrSite>().Where(s => s.Id == 2).AsDto().ToList<NfrSiteDto>();
            var dto = Assert.Single(dtos);
            Assert.Equal(2, dto.Id);
            Assert.Equal("SITE-NULL", dto.Name);
            Assert.NotNull(dto.Dept);
            Assert.Equal(0, dto.Dept!.Id);
            Assert.Null(dto.Dept.Name);
        }

        /// <summary>
        /// 例④（chunk 零波及守卫，验收标准 5）：chunk 内存编译路径不剥不包（C3），
        /// null 导航仍精确 null（原版 IIF 语义），正常行值全对。
        /// </summary>
        [Fact]
        public void FreeSql_ToChunkDto_MemoryPath_KeepsExactNullSemantics()
        {
            List<NfrSiteDto> received = new();
            _fsql.Select<NfrSite>().OrderBy(s => s.Id).AsDto().ToChunkDto<NfrSiteDto>(10, batch => received.AddRange(batch));

            Assert.Equal(2, received.Count);
            var nullRow = received.Single(d => d.Id == 2);
            Assert.Null(nullRow.Dept); // 内存原版语义：精确 null（非空壳）——若 Build 被误剥此断言变红
            var normalRow = received.Single(d => d.Id == 1);
            Assert.NotNull(normalRow.Dept);
            Assert.Equal(10, normalRow.Dept!.Id);
            Assert.Equal("IT", normalRow.Dept.Name);
        }

        /// <summary>
        /// 例⑤（guard 兜底链路基础）：异常注入 → Trace 含完整原始异常 → degrade 执行返回。
        /// 模拟异常注入路径（「模拟异常注入或不可译形态触发」之注入腿）。
        /// </summary>
        [Fact]
        public void FreeSqlSqlProjectionGuard_Exception_TracesOriginalAndDegrades()
        {
            string trace;
            string result;
            using (var tc = new TraceCapture())
            {
                result = FreeSqlSqlProjectionGuard.Run(
                    "[FreeSql AsDto] GuardInjectTest",
                    () => throw new InvalidOperationException("模拟投影失败"),
                    () => "降级结果");
                trace = tc.Writer.ToString();
            }

            Assert.Equal("降级结果", result);
            Assert.Contains("SQL 投影失败", trace);
            Assert.Contains("InvalidOperationException", trace); // 原始异常类型不丢失（诊断不静默）
            Assert.Contains("模拟投影失败", trace);              // 原始异常消息不丢失
        }

        /// <summary>
        /// 例⑥（既有降级网守卫，验收标准 4 / C2）：跨表嵌套走公共入口仍降级内存映射，
        /// 值正确、降级原因文案不变——证明本册改动未触碰 HasNestedCrossTableNavigation 行为。
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_CrossTableNested_ExistingDegradeNetUnchanged()
        {
            string trace;
            List<NfrSiteCrossDto> dtos;
            using (var tc = new TraceCapture())
            {
                dtos = _fsql.Select<NfrSite>().Where(s => s.Id == 1).AsDto().ToList<NfrSiteCrossDto>();
                trace = tc.Writer.ToString();
            }

            var dto = Assert.Single(dtos);
            Assert.NotNull(dto.Dept);
            Assert.Equal("ACME", dto.Dept!.CompanyName); // 降级内存映射值仍正确
            Assert.Contains("降级", trace);
            Assert.Contains("嵌套 DTO 含跨表", trace);   // 降级原因文案（既有判据语义不变）
        }

        /// <summary>
        /// 例⑦（兜底端到端）：guard 异常注入后 degrade 走<strong>真实降级链</strong>
        /// （自动 Include → 查实体 → Mapper.MapList）产出与正常路径等值的 DTO，
        /// 验证兜底降级不是空壳 promise（catch 行为契约：Trace 完整原始异常 + degrade 完整执行）。
        /// </summary>
        [Fact]
        public void FreeSqlSqlProjectionGuard_Degrade_RunsRealMapListChain()
        {
            string trace;
            List<NfrSiteDto>? degraded;
            using (var tc = new TraceCapture())
            {
                degraded = FreeSqlSqlProjectionGuard.Run<List<NfrSiteDto>>(
                    "[FreeSql AsDto] DegradeE2ETest",
                    sqlProject: () => throw new InvalidOperationException("模拟 SQL 投影失败"),
                    degrade: () =>
                    {
                        // 真实降级链（与终端方法 degrade 同构）：自动 Include → 查全量实体 → 内存映射
                        var entities = _fsql.Select<NfrSite>().OrderBy(s => s.Id).ToList();
                        return Mapper.MapList<NfrSite, NfrSiteDto>(entities)!;
                    });
                trace = tc.Writer.ToString();
            }

            // 降级结果与 SQL 投影应有等值的数据（2 行，含 null 导航行）
            Assert.Equal(2, degraded!.Count);
            var normal = degraded.Single(d => d.Id == 1);
            // 降级路径（内存 MapList）null 导航为精确 null（原版语义，与 chunk 同）
            Assert.Null(normal.Dept);
            // Trace 三件：降级动作 + 原始异常类型 + 原始异常消息（诊断不静默）
            Assert.Contains("SQL 投影失败", trace);
            Assert.Contains("InvalidOperationException", trace);
            Assert.Contains("模拟 SQL 投影失败", trace);
        }
    }
}
