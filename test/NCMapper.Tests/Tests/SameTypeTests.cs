using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 同类型映射测试 — 实体字段名与DTO字段名、类型完全一致，直接赋值
    /// </summary>
    public class SameTypeTests
    {
        /// <summary>
        /// 验证所有基元类型字段（int/long/decimal/float/double/string/bool/DateTime/Guid）的正确映射
        /// </summary>
        [Fact]
        public void All_SameType_Fields_Mapped_Correctly()
        {
            // T16: 使用 Builder 模式构造测试数据
            var entity = new FullTypeEntityBuilder().Build();
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            // 整型
            Assert.Equal(42, dto.IntProp);
            // 长整型
            Assert.Equal(9999999999L, dto.LongProp);
            // 高精度小数
            Assert.Equal(123.45m, dto.DecimalProp);
            // 单精度浮点
            Assert.Equal(3.14f, dto.FloatProp, 0.001f);
            // 双精度浮点
            Assert.Equal(2.71828, dto.DoubleProp, 0.0001);
            // 字符串
            Assert.Equal("hello", dto.StringProp);
            // 布尔
            Assert.True(dto.BoolProp);
            // 日期时间
            Assert.Equal(2024, dto.DateTimeProp.Year);
            Assert.Equal(6, dto.DateTimeProp.Month);
            // GUID
            Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), dto.GuidProp);
        }

        /// <summary>
        /// 源对象为 null 时，应返回 null 而非抛出异常
        /// </summary>
        [Fact]
        public void Null_Source_Returns_Null()
        {
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(null!);
            Assert.Null(dto);
        }
    }
}
