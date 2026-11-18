using Xunit;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// FreeSql Projection 中自定义脱敏方法（模拟 [Mask]）的处理验证。
    /// <para>历史结论：FreeSql 无法解析自定义方法表达式（"Function expression ... parsing not implemented"）。</para>
    /// <para>当前方案：AsDto 检测到 [Convert] 后自动降级为二步映射，脱敏在内存映射阶段生效。
    /// 本测试类由原"可行性探测"重写为"降级路径回归测试"。
    /// 共用 <see cref="CustomerMaskedDto"/> 与 <see cref="PhoneMaskConverter"/>（定义于 MaskProjectionFeasibilityTests.cs）。</para>
    /// </summary>
    public class MaskProjectionFreeSqlFeasibilityTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly string _dbPath;

        public MaskProjectionFreeSqlFeasibilityTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_mask_freesql_{Guid.NewGuid():N}.db");
            _fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, $"DataSource={_dbPath}")
                .UseAutoSyncStructure(true)
                .Build();

            _fsql.Insert(new List<CustomerEntity>
            {
                new() { Id = 1, Name = "ZhangSan", Level = "VIP", Phone = "13800000001" },
                new() { Id = 2, Name = "LiSi", Level = "Normal", Phone = "13800000002" }
            }).ExecuteAffrows();
        }

        public void Dispose()
        {
            _fsql.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }

        /// <summary>
        /// AsDto().ToList() 检测到 [Convert] 自动降级 — 脱敏正确生效
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_Mask_ConvertUsing_Downgrade_Works()
        {
            var dtos = _fsql.Select<CustomerEntity>()
                .AsDto()
                .ToList<CustomerMaskedDto>()
                .OrderBy(d => d.Id)
                .ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("ZhangSan", dtos[0].Name);   // 普通属性正常
            Assert.Equal("138****0001", dtos[0].Phone); // 脱敏经降级路径生效
            Assert.Equal("138****0002", dtos[1].Phone);
        }

        /// <summary>
        /// AsDto().First() 降级路径 — 单条脱敏正确
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_Mask_First_Downgrade_Works()
        {
            var dto = _fsql.Select<CustomerEntity>()
                .Where(c => c.Id == 1)
                .AsDto()
                .First<CustomerMaskedDto>();

            Assert.NotNull(dto);
            Assert.Equal("138****0001", dto!.Phone);
        }

        /// <summary>
        /// 降级检测：含 [Convert] 的 DTO 被判定为需要二步映射
        /// </summary>
        [Fact]
        public void FreeSql_AsDto_Mask_NeedsTwoStepMapping_True()
        {
            var needs = FreeSqlDegradationDetector.NeedsDegradation(
                typeof(CustomerEntity), typeof(CustomerMaskedDto), out var reason);

            Assert.True(needs);
            Assert.Contains("ConvertAttribute", reason);
        }
    }
}
