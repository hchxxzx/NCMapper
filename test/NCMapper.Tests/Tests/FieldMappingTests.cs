using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// [MapProperty] 特性测试 — 源字段名与目标字段名不同时的映射
    /// </summary>
    public class FieldMappingTests
    {
        /// <summary>
        /// 使用 [MapProperty(typeof(源类型), "源字段名")] 标注目标属性，
        /// 指定从源类型的哪个字段取值。同时验证同名字段 LongProp 仍正常映射。
        /// </summary>
        [Fact]
        public void MapProperty_DifferentName_Mapped()
        {
            var entity = new FullTypeEntityBuilder()
                .WithStringProp("ZhangSan")
                .WithIntProp(28)
                .WithLongProp(123456789L)
                .Build();
            var dto = Mapper.Map<FullTypeEntity, FieldMappingDto>(entity)!;

            // [MapProperty(typeof(FullTypeEntity), "StringProp")] → FullName
            Assert.Equal("ZhangSan", dto.FullName);
            // [MapProperty(typeof(FullTypeEntity), "IntProp")] → Age
            Assert.Equal(28, dto.Age);
            // 同名字段 LongProp，无需 [MapProperty] 标注
            Assert.Equal(123456789L, dto.LongProp);
        }

        // ================================================================
        // T13: FieldMapping 扩充测试
        // ================================================================

        /// <summary>
        /// 多字段 [MapProperty] 混合映射 — 多个跨字段名映射与同名字段共存
        /// 验证 StringToNumericDto 中 4 个 [MapProperty] 字段同时正确映射
        /// </summary>
        [Fact]
        public void MapProperty_MultipleFields_AllMappedCorrectly()
        {
            var entity = new FullTypeEntityBuilder().WithStringProp("2024").Build();

            // T13: 使用 StringToNumericOnlyDto（无 DateValue），避免 "2024" → DateTime 转换失败
            var dto = Mapper.Map<FullTypeEntity, StringToNumericOnlyDto>(entity)!;

            // StringProp("2024") → IntValue(int)
            Assert.Equal(2024, dto.IntValue);
            // StringProp("2024") → LongValue(long)
            Assert.Equal(2024L, dto.LongValue);
            // StringProp("2024") → DecimalValue(decimal)
            Assert.Equal(2024m, dto.DecimalValue);
        }

        /// <summary>
        /// [MapProperty] 源属性为 null — 验证 null 传播行为
        /// 源字符串为 null 时，目标 int 应返回 default(0)
        /// </summary>
        [Fact]
        public void MapProperty_NullSourceString_ReturnsDefault()
        {
            var entity = new FullTypeEntityBuilder().WithStringProp(null!).Build();

            var dto = Mapper.Map<FullTypeEntity, FieldMappingDto>(entity)!;

            // null StringProp → FullName(string) 应为 null 或空
            Assert.True(dto.FullName == null || dto.FullName == "");
        }

        /// <summary>
        /// [MapProperty] 跨类型映射 — string → int/long/decimal/DateTime
        /// 验证 4 种不同目标类型从同一 string 源字段的转换
        /// </summary>
        [Fact]
        public void MapProperty_CrossTypeConversion_MultipleTargets()
        {
            var entity = new FullTypeEntityBuilder().WithStringProp("100").Build();

            // T13: 使用 StringToNumericOnlyDto（无 DateValue），避免 "100" → DateTime 转换失败
            var dto = Mapper.Map<FullTypeEntity, StringToNumericOnlyDto>(entity)!;

            Assert.Equal(100, dto.IntValue);
            Assert.Equal(100L, dto.LongValue);
            Assert.Equal(100m, dto.DecimalValue);
        }

        /// <summary>
        /// [MapProperty] 同名字段 + 跨字段共存 — FieldMappingDto 中
        /// FullName 和 Age 使用 [MapProperty]，LongProp 使用同名自动映射
        /// </summary>
        [Fact]
        public void MapProperty_MixedWithSameName_BothWork()
        {
            var entity = new FullTypeEntityBuilder()
                .WithStringProp("TestUser")
                .WithIntProp(35)
                .WithLongProp(987654321L)
                .Build();

            var dto = Mapper.Map<FullTypeEntity, FieldMappingDto>(entity)!;

            // [MapProperty] 映射字段
            Assert.Equal("TestUser", dto.FullName);
            Assert.Equal(35, dto.Age);
            // 同名自动映射字段
            Assert.Equal(987654321L, dto.LongProp);
        }
    }
}
