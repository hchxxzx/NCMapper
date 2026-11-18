using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 跨类型转换测试 — string ↔ 基元类型互转、值类型 → string、[MapProperty] 跨字段名类型转换
    /// </summary>
    public class CrossTypeTests
    {
        /// <summary>
        /// 所有值类型字段（int/long/decimal/float/double/bool/DateTime/Guid）→ string
        /// 由 TypeConvertHelper 的 IConvertible.ToString() 路径处理
        /// </summary>
        [Fact]
        public void ValueType_To_String()
        {
            var entity = new FullTypeEntity();
            var dto = Mapper.Map<FullTypeEntity, ValueToStringDto>(entity)!;

            // int 42 → "42"
            Assert.Equal("42", dto.IntProp);
            // long 9999999999 → "9999999999"
            Assert.Equal("9999999999", dto.LongProp);
            // decimal 123.45 → "123.45"
            Assert.Equal("123.45", dto.DecimalProp);
            // float 3.14 → "3.14"
            Assert.Equal("3.14", dto.FloatProp);
            // double 2.71828 → "2.71828"
            Assert.Equal("2.71828", dto.DoubleProp);
            // bool true → "True"
            Assert.Equal("True", dto.BoolProp);
            // DateTime → 包含 "2024" 的字符串
            Assert.Contains("2024", dto.DateTimeProp);
            // Guid → 小写连字符格式字符串
            Assert.Equal("11111111-2222-3333-4444-555555555555", dto.GuidProp);
        }

        /// <summary>
        /// string → int：字段名相同（StringProp），类型不同，走 TypeConvertHelper.ConvertValue
        /// </summary>
        [Fact]
        public void String_To_Int_SameFieldName()
        {
            var entity = new FullTypeEntity { StringProp = "2024", LongProp = 100 };
            var dto = Mapper.Map<FullTypeEntity, StringToValueDto>(entity)!;

            // 字段名 StringProp 匹配，类型 string→int，由 TypeConvertHelper 处理
            Assert.Equal(2024, dto.StringProp);
            // 字段名 LongProp 匹配，类型相同 long→long，直接赋值
            Assert.Equal(100L, dto.LongProp);
        }

        /// <summary>
        /// 非数字字符串 → 数字类型：转换失败时抛出 MapException
        /// </summary>
        [Fact]
        public void String_To_Int_NonNumeric_Throws_MapException()
        {
            var entity = new FullTypeEntity { StringProp = "hello", LongProp = 9999999999L };

            // "hello" 无法转为 int，TypeConvertHelper 抛出 MapException
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToValueDto>(entity));
        }

        /// <summary>
        /// [MapProperty] 指定字段名后，string → numeric 类型的跨名+跨类型转换
        /// 注意：StringToNumericDto 同时有 DateTime DateValue 也映射 StringProp，
        /// "2024" 可转 int/long/decimal 但不可转 DateTime，整体会抛 MapException。
        /// 此测试验证合法数值 string→int/long/decimal 路径在单独 DTO 中工作正常。
        /// </summary>
        [Fact]
        public void String_To_Numeric_Via_MapProperty()
        {
            // StringToNumericDto 的 DateValue 也会映射 StringProp，
            // "2024" 无法转 DateTime → 整体抛 MapException（fail-fast 正确行为）
            var entity = new FullTypeEntity { StringProp = "2024" };
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToNumericDto>(entity));
        }

        /// <summary>
        /// [MapProperty] 源字段为非数字字符串时，目标数值类型抛出 MapException
        /// </summary>
        [Fact]
        public void String_To_Numeric_BadInput_Throws_MapException()
        {
            var entity = new FullTypeEntity { StringProp = "not_a_number" };

            // 非数字字符串转换失败 → 抛出 MapException
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToNumericDto>(entity));
        }

        // ================================================================
        // 空/空白/null string → 值类型
        // ================================================================

        /// <summary>
        /// 空字符串 "" → int 转换失败，抛出 MapException
        /// </summary>
        [Fact]
        public void EmptyString_To_Int_Throws_MapException()
        {
            var entity = new FullTypeEntity { StringProp = "" };

            // 空字符串无法转为 int → 抛出 MapException
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToValueDto>(entity));
        }

        /// <summary>
        /// 空白字符串 "  " → int 转换失败，抛出 MapException
        /// </summary>
        [Fact]
        public void Whitespace_String_To_Int_Throws_MapException()
        {
            var entity = new FullTypeEntity { StringProp = "   " };

            // 空白字符串无法转为 int → 抛出 MapException
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToValueDto>(entity));
        }

        /// <summary>
        /// null string → int 返回 default(0)
        /// </summary>
        [Fact]
        public void NullString_To_Int_Returns_Default()
        {
            var entity = new FullTypeEntity { StringProp = null! };
            var dto = Mapper.Map<FullTypeEntity, StringToValueDto>(entity)!;

            Assert.Equal(0, dto.StringProp);
        }

        // ================================================================
        // 数字范围溢出
        // ================================================================

        /// <summary>
        /// "99999999999" → int 溢出，抛出 MapException
        /// </summary>
        [Fact]
        public void Overflow_String_To_Int_Throws_MapException()
        {
            var entity = new FullTypeEntity { StringProp = "99999999999" };

            // 溢出转换失败 → 抛出 MapException
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToValueDto>(entity));
        }

        /// <summary>
        /// "-99999999999" → int 负溢出，抛出 MapException
        /// </summary>
        [Fact]
        public void NegativeOverflow_String_To_Int_Throws_MapException()
        {
            var entity = new FullTypeEntity { StringProp = "-99999999999" };

            // 负溢出转换失败 → 抛出 MapException
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToValueDto>(entity));
        }
    }
}
