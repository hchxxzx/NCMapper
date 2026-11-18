using System.Reflection;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// MapValidator.IsTypeCompatible 值类型兼容性测试 — 035 修复验证
    /// <para>IsTypeCompatible 是 private 方法，通过反射调用测试</para>
    /// </summary>
    public class TypeCompatibilityTests
    {
        /// <summary>
        /// 通过反射调用 IsTypeCompatible
        /// </summary>
        private static bool IsTypeCompatible(Type sourceType, Type targetType)
        {
            var method = typeof(MapValidator).GetMethod("IsTypeCompatible",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (bool)method!.Invoke(null, new object[] { sourceType, targetType })!;
        }

        // ================================================================
        // 隐式转换 — 应兼容
        // ================================================================

        [Theory]
        [InlineData(typeof(int), typeof(long))]
        [InlineData(typeof(int), typeof(float))]
        [InlineData(typeof(int), typeof(double))]
        [InlineData(typeof(int), typeof(decimal))]
        [InlineData(typeof(byte), typeof(decimal))]
        [InlineData(typeof(short), typeof(int))]
        [InlineData(typeof(uint), typeof(long))]
        [InlineData(typeof(float), typeof(double))]
        [InlineData(typeof(char), typeof(int))]
        /// <summary>
        /// 验证有隐式数值转换的值类型对视为兼容
        /// </summary>
        /// <param name="src">源类型</param>
        /// <param name="tgt">目标类型</param>
        public void ImplicitConversion_Compatible(Type src, Type tgt)
        {
            Assert.True(IsTypeCompatible(src, tgt));
        }

        // ================================================================
        // 无隐式转换的值类型对 — 不应兼容
        // ================================================================

        [Theory]
        [InlineData(typeof(int), typeof(uint))]
        [InlineData(typeof(uint), typeof(int))]
        [InlineData(typeof(int), typeof(ulong))]
        [InlineData(typeof(float), typeof(decimal))]
        [InlineData(typeof(decimal), typeof(double))]
        [InlineData(typeof(double), typeof(float))]
        [InlineData(typeof(long), typeof(int))]
        [InlineData(typeof(ulong), typeof(int))]
        /// <summary>
        /// 验证无隐式转换但有 IConvertible 兜底的值类型对仍视为兼容
        /// </summary>
        /// <param name="src">源类型</param>
        /// <param name="tgt">目标类型</param>
        public void NoImplicitConversion_StillCompatible(Type src, Type tgt)
        {
            // Phase 1 修复前：值类型一律返回 true（int→DateTime 也通过）
            // Phase 1 修复后：使用 TypeConverter.IsImplicitConvertible 精确判断
            // 但 IConvertible 兜底仍允许 int→uint（Convert.ChangeType 可处理）
            // 所以这里验证的是"无隐式转换但有 IConvertible"仍返回 true
            Assert.True(IsTypeCompatible(src, tgt));
        }

        // ================================================================
        // 完全不兼容的值类型 — 不应兼容
        // ================================================================

        [Theory]
        [InlineData(typeof(int), typeof(DateTime))]
        [InlineData(typeof(Guid), typeof(TimeSpan))]
        [InlineData(typeof(DateTime), typeof(Guid))]
        [InlineData(typeof(TimeSpan), typeof(Guid))]
        /// <summary>
        /// 完全不兼容的值类型对均判定为不相容
        /// <para> 收敛（判定权威 TypeCompatView，与生成侧 HasMappingPath 三向一致）：
        /// 判定语义 = "存在映射/转换路径"——DateTime/Guid 仅与 string 互转（裁决），
        /// 数值族互转、string?IConvertible、enum?数值之外均视为不相容。旧规则表对
        /// "双 IConvertible 值类型"全放行（int→DateTime true）系"校验通过但映射失败"的误判
        /// （映射侧 ConvertValue 实际抛 MapException）——收敛后校验与映射同源（变化登记）。</para>
        /// </summary>
        /// <param name="src">源类型</param>
        /// <param name="tgt">目标类型</param>
        public void IncompatibleValueTypes_IConvertible_Determines_Compatibility(Type src, Type tgt)
        {
            var result = IsTypeCompatible(src, tgt);
            Assert.False(result);
        }

        // ================================================================
        // 同类型 — 兼容
        // ================================================================

        [Theory]
        [InlineData(typeof(int), typeof(int))]
        [InlineData(typeof(string), typeof(string))]
        [InlineData(typeof(DateTime), typeof(DateTime))]
        /// <summary>
        /// 验证同类型视为兼容
        /// </summary>
        /// <param name="src">源类型</param>
        /// <param name="tgt">目标类型</param>
        public void SameType_Compatible(Type src, Type tgt)
        {
            Assert.True(IsTypeCompatible(src, tgt));
        }

        // ================================================================
        // 可空类型 — 兼容
        // ================================================================

        [Theory]
        [InlineData(typeof(int?), typeof(int))]
        [InlineData(typeof(int), typeof(int?))]
        [InlineData(typeof(DateTime?), typeof(DateTime))]
        /// <summary>
        /// 验证可空类型与其底层类型之间的映射视为兼容
        /// </summary>
        /// <param name="src">源类型</param>
        /// <param name="tgt">目标类型</param>
        public void NullableTypes_Compatible(Type src, Type tgt)
        {
            Assert.True(IsTypeCompatible(src, tgt));
        }

        // ================================================================
        // 枚举 — 兼容
        // ================================================================

        [Fact]
        public void Enum_To_Int_Compatible()
        {
            Assert.True(IsTypeCompatible(typeof(TestEnum), typeof(int)));
        }

        [Fact]
        public void Int_To_Enum_Compatible()
        {
            Assert.True(IsTypeCompatible(typeof(int), typeof(TestEnum)));
        }

        [Fact]
        public void Enum_To_String_Compatible()
        {
            Assert.True(IsTypeCompatible(typeof(TestEnum), typeof(string)));
        }

        // ================================================================
        // string ↔ 基元类型 — 兼容
        // ================================================================

        [Theory]
        [InlineData(typeof(string), typeof(int))]
        [InlineData(typeof(int), typeof(string))]
        [InlineData(typeof(string), typeof(DateTime))]
        [InlineData(typeof(string), typeof(Guid))]
        /// <summary>
        /// 验证 string 与基元类型之间的映射视为兼容
        /// </summary>
        /// <param name="src">源类型</param>
        /// <param name="tgt">目标类型</param>
        public void StringAndPrimitive_Compatible(Type src, Type tgt)
        {
            Assert.True(IsTypeCompatible(src, tgt));
        }

        // ================================================================
        // Guid ↔ string — 兼容
        // ================================================================

        [Fact]
        public void Guid_To_String_Compatible()
        {
            Assert.True(IsTypeCompatible(typeof(Guid), typeof(string)));
        }

        [Fact]
        public void String_To_Guid_Compatible()
        {
            Assert.True(IsTypeCompatible(typeof(string), typeof(Guid)));
        }

        // ================================================================
        // class 之间 — 兼容
        // ================================================================

        [Fact]
        public void Class_To_Class_Compatible()
        {
            Assert.True(IsTypeCompatible(typeof(TestClassA), typeof(TestClassB)));
        }

        // 测试辅助类型
        private enum TestEnum { A, B, C }
        private class TestClassA { public int X { get; set; } }
        private class TestClassB { public int Y { get; set; } }
    }
}
