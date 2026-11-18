using Xunit;
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    // ================================================================
    // 共享：Mask 脱敏转换器 + 含 [Convert] 的 DTO
    // <para>自定义脱敏方法是 CLR 委托，ORM 无法翻译为 SQL；
    // AsDto 检测到 [Convert] 后自动降级为「SQL 查实体 → MapList」，
    // 脱敏逻辑在内存映射阶段生效。FreeSql 与 EF Core 可行性测试共用此 DTO。</para>
    // ================================================================

    /// <summary>
    /// 电话脱敏转换器（静态方法引用形态，T1 模板 M(TField)）：13800000001 → 138****0001
    /// </summary>
    public static class PhoneMaskConverter
    {
        /// <summary>手机号中间 4 位替换为 ****（长度不足 7 位原样返回，null → 空串）</summary>
        public static string Convert(string? fieldValue)
            => fieldValue is { Length: >= 7 } ? $"{fieldValue[..3]}****{fieldValue[^4..]}" : fieldValue ?? "";
    }

    /// <summary>
    /// 含 [Convert] 脱敏属性的 DTO — AsDto 路径会自动降级
    /// </summary>
    [MapFrom(typeof(CustomerEntity))]
    public class CustomerMaskedDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [Convert(typeof(PhoneMaskConverter), nameof(PhoneMaskConverter.Convert))]
        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// EF Core Projection 中自定义脱敏方法（模拟 [Mask]）的处理验证。
    /// <para>历史结论：自定义方法无法翻译为 SQL（EF Core 抛 client projection 异常）。</para>
    /// <para>当前方案：AsDto 检测到 [Convert] 后自动降级为二步映射，脱敏在内存映射阶段生效。
    /// 本测试类由原"可行性探测"重写为"降级路径回归测试"。</para>
    /// </summary>
    public class MaskProjectionFeasibilityTests : IDisposable
    {
        private readonly TestDbContext _db;

        public MaskProjectionFeasibilityTests()
        {
            var connStr = $"DataSource=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new TestDbContext(connStr);
            _db.SeedData();
        }

        public void Dispose() => _db.Dispose();

        /// <summary>
        /// AsDto().ToList() 检测到 [Convert] 自动降级 — 脱敏正确生效
        /// </summary>
        [Fact]
        public void EFCore_AsDto_Mask_ConvertUsing_Downgrade_Works()
        {
            var dtos = _db.Customers
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
        public void EFCore_AsDto_Mask_First_Downgrade_Works()
        {
            var dto = _db.Customers
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
        public void EFCore_AsDto_Mask_NeedsTwoStepMapping_True()
        {
            var needs = EfCoreDegradationDetector.NeedsDegradation(
                typeof(CustomerEntity), typeof(CustomerMaskedDto), out var reason);

            Assert.True(needs);
            Assert.Contains("ConvertAttribute", reason);
        }
    }
}
