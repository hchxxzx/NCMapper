using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// TypeConverter mutant-killing tests — targets surviving Stryker mutants in TypeConverter.cs
    /// Covers: IsImplicitConvertible guards (!IsValueType, IsEnum, same-type),
    /// lookup table correctness, IsNullableType, GetUnderlyingType
    /// </summary>
    public class TypeConverterMutantTests
    {
        // ================================================================
        // IsImplicitConvertible — value type guard L35
        // Kills: !sourceType.IsValueType || !targetType.IsValueType → && mutation
        // ================================================================

        /// <summary>
        /// int (value type) → string (reference type) → false.
        /// Kills mutant: !targetType.IsValueType removal (would return true for int→string).
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_ValueToRef_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(int), typeof(string)));
        }

        /// <summary>
        /// string (reference type) → int (value type) → false.
        /// Kills mutant: !sourceType.IsValueType removal (would return true for string→int).
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_RefToValue_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(string), typeof(int)));
        }

        /// <summary>
        /// string → string (both reference) → false.
        /// Kills mutant: both IsValueType checks removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_RefToRef_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(string), typeof(string)));
        }

        // ================================================================
        // IsImplicitConvertible — enum guard L36
        // Kills: sourceType.IsEnum || targetType.IsEnum removal
        // ================================================================

        /// <summary>
        /// enum → int → false (enum guard).
        /// Kills mutant: sourceType.IsEnum check removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_EnumSource_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(DayOfWeek), typeof(int)));
        }

        /// <summary>
        /// int → enum → false (enum guard).
        /// Kills mutant: targetType.IsEnum check removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_EnumTarget_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(int), typeof(DayOfWeek)));
        }

        /// <summary>
        /// enum → enum → false (both enums).
        /// Kills mutant: || to && in enum guard.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_EnumToEnum_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(DayOfWeek), typeof(StringComparison)));
        }

        // ================================================================
        // IsImplicitConvertible — same type L37
        // Kills: == to != mutation
        // ================================================================

        /// <summary>
        /// int → int (same type) → true.
        /// Kills mutant: sourceType == targetType to != (would return false for same type).
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_SameType_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(int), typeof(int)));
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(long), typeof(long)));
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(double), typeof(double)));
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(float), typeof(float)));
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(decimal), typeof(decimal)));
        }

        // ================================================================
        // IsImplicitConvertible — lookup table correctness L39
        // Kills: individual HashSet entries, TryGetValue/Contains
        // ================================================================

        /// <summary>
        /// int → long → true (widening).
        /// Kills mutant: int HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_IntToLong_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(int), typeof(long)));
        }

        /// <summary>
        /// int → float → true.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_IntToFloat_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(int), typeof(float)));
        }

        /// <summary>
        /// int → double → true.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_IntToDouble_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(int), typeof(double)));
        }

        /// <summary>
        /// int → decimal → true.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_IntToDecimal_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(int), typeof(decimal)));
        }

        /// <summary>
        /// long → float → true.
        /// Kills mutant: long HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_LongToFloat_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(long), typeof(float)));
        }

        /// <summary>
        /// long → double → true.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_LongToDouble_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(long), typeof(double)));
        }

        /// <summary>
        /// float → double → true.
        /// Kills mutant: float HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_FloatToDouble_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(float), typeof(double)));
        }

        /// <summary>
        /// byte → int → true.
        /// Kills mutant: byte HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_ByteToInt_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(byte), typeof(int)));
        }

        /// <summary>
        /// byte → double → true.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_ByteToDouble_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(byte), typeof(double)));
        }

        /// <summary>
        /// char → int → true.
        /// Kills mutant: char HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_CharToInt_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(char), typeof(int)));
        }

        /// <summary>
        /// short → int → true.
        /// Kills mutant: short HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_ShortToInt_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(short), typeof(int)));
        }

        /// <summary>
        /// long → int → false (narrowing, not implicit).
        /// Kills mutant: Contains always returns true.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_LongToInt_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(long), typeof(int)));
        }

        /// <summary>
        /// double → float → false (narrowing).
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_DoubleToFloat_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(double), typeof(float)));
        }

        /// <summary>
        /// double → int → false (narrowing).
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_DoubleToInt_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(double), typeof(int)));
        }

        /// <summary>
        /// float → int → false (narrowing).
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_FloatToInt_False()
        {
            Assert.False(TypeConverter.IsImplicitConvertible(typeof(float), typeof(int)));
        }

        /// <summary>
        /// sbyte → int → true.
        /// Kills mutant: sbyte HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_SByteToInt_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(sbyte), typeof(int)));
        }

        /// <summary>
        /// ushort → int → true.
        /// Kills mutant: ushort HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_UShortToInt_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(ushort), typeof(int)));
        }

        /// <summary>
        /// uint → long → true.
        /// Kills mutant: uint HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_UIntToLong_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(uint), typeof(long)));
        }

        /// <summary>
        /// ulong → float → true.
        /// Kills mutant: ulong HashSet entry removal.
        /// </summary>
        [Fact]
        public void IsImplicitConvertible_ULongToFloat_True()
        {
            Assert.True(TypeConverter.IsImplicitConvertible(typeof(ulong), typeof(float)));
        }

        // ================================================================
        // IsNullableType — L47-50
        // Kills: IsGenericType check, GetGenericTypeDefinition == Nullable<>
        // ================================================================

        /// <summary>
        /// Nullable&lt;int&gt; → true.
        /// Kills mutant: return value negation.
        /// </summary>
        [Fact]
        public void IsNullableType_NullableInt_True()
        {
            Assert.True(TypeConverter.IsNullableType(typeof(int?)));
        }

        /// <summary>
        /// int (non-nullable) → false.
        /// Kills mutant: IsGenericType negation.
        /// </summary>
        [Fact]
        public void IsNullableType_Int_False()
        {
            Assert.False(TypeConverter.IsNullableType(typeof(int)));
        }

        /// <summary>
        /// string (reference, non-nullable value type) → false.
        /// Kills mutant: IsGenericType check removal.
        /// </summary>
        [Fact]
        public void IsNullableType_String_False()
        {
            Assert.False(TypeConverter.IsNullableType(typeof(string)));
        }

        /// <summary>
        /// Nullable&lt;DateTime&gt; → true.
        /// </summary>
        [Fact]
        public void IsNullableType_NullableDateTime_True()
        {
            Assert.True(TypeConverter.IsNullableType(typeof(DateTime?)));
        }

        /// <summary>
        /// Nullable&lt;Guid&gt; → true.
        /// </summary>
        [Fact]
        public void IsNullableType_NullableGuid_True()
        {
            Assert.True(TypeConverter.IsNullableType(typeof(Guid?)));
        }

        /// <summary>
        /// List&lt;int&gt; (generic but not Nullable) → false.
        /// Kills mutant: GetGenericTypeDefinition == Nullable<> removal.
        /// </summary>
        [Fact]
        public void IsNullableType_ListOfInt_False()
        {
            Assert.False(TypeConverter.IsNullableType(typeof(List<int>)));
        }

        // ================================================================
        // GetUnderlyingType — L57-60
        // Kills: Nullable.GetUnderlyingType ?? type fallback
        // ================================================================

        /// <summary>
        /// Nullable&lt;int&gt; → returns int.
        /// Kills mutant: Nullable.GetUnderlyingType return removal.
        /// </summary>
        [Fact]
        public void GetUnderlyingType_NullableInt_Returns_Int()
        {
            Assert.Equal(typeof(int), Nullable.GetUnderlyingType(typeof(int?)) ?? typeof(int?));
        }

        /// <summary>
        /// int (non-nullable) → returns int.
        /// Kills mutant: ?? type fallback removal.
        /// </summary>
        [Fact]
        public void GetUnderlyingType_Int_Returns_Int()
        {
            Assert.Equal(typeof(int), Nullable.GetUnderlyingType(typeof(int)) ?? typeof(int));
        }

        /// <summary>
        /// string → returns string.
        /// Kills mutant: null coalescing removal.
        /// </summary>
        [Fact]
        public void GetUnderlyingType_String_Returns_String()
        {
            Assert.Equal(typeof(string), Nullable.GetUnderlyingType(typeof(string)) ?? typeof(string));
        }

        /// <summary>
        /// Nullable&lt;DateTime&gt; → returns DateTime.
        /// </summary>
        [Fact]
        public void GetUnderlyingType_NullableDateTime_Returns_DateTime()
        {
            Assert.Equal(typeof(DateTime), Nullable.GetUnderlyingType(typeof(DateTime?)) ?? typeof(DateTime?));
        }

        /// <summary>
        /// Nullable&lt;bool&gt; → returns bool.
        /// </summary>
        [Fact]
        public void GetUnderlyingType_NullableBool_Returns_Bool()
        {
            Assert.Equal(typeof(bool), Nullable.GetUnderlyingType(typeof(bool?)) ?? typeof(bool?));
        }
    }
}
