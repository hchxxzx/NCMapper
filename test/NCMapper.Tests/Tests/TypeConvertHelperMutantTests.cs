using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Mutant-killing tests for TypeConvertHelper.cs
    /// Targets: catch blocks (L36-51), float→enum path (L84-87), Guid parsing (L111-118),
    ///          IsNullOrEmpty negation (L75), enum→string (L58-59)
    /// </summary>
    public class TypeConvertHelperMutantTests
    {
        // ================================================================
        // Catch block coverage — IConvertible path (L30-51)
        // Each catch block must be individually exercised to kill removal mutants
        // ================================================================

        /// <summary>
        /// string→int valid conversion via IConvertible.ChangeType.
        /// Baseline: this path must succeed to verify catch blocks don't interfere.
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Int_Valid()
        {
            var result = TypeConvertHelper.ConvertValue<string, int>("42");
            Assert.Equal(42, result);
        }

        /// <summary>
        /// string→int invalid format — triggers FormatException catch at L40.
        /// After catch, falls through to targetType==typeof(string) check (false) then throws MapException.
        /// Kills: catch(FormatException) removal — without catch, FormatException would propagate
        /// instead of MapException.
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Int_Invalid_Throws_MapException()
        {
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<string, int>("not_a_number"));
            Assert.Contains("string", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// string→int overflow — triggers OverflowException catch at L44.
        /// Kills: catch(OverflowException) removal.
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Int_Overflow_Throws_MapException()
        {
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<string, int>("99999999999999"));
            Assert.Contains("string", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// string→Guid via Guid.TryParse path (L113).
        /// Verifies Guid string parsing works correctly.
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Guid_Valid()
        {
            var guid = Guid.NewGuid();
            var result = TypeConvertHelper.ConvertValue<string, Guid>(guid.ToString());
            Assert.Equal(guid, result);
        }

        /// <summary>
        /// string→Guid invalid format — Guid.TryParse returns false, falls through to throw.
        /// Kills: Guid.TryParse fallback path mutation at L113.
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Guid_Invalid_Throws()
        {
            Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<string, Guid>("not-a-guid"));
        }

        // ================================================================
        // Enum source path (L55-63)
        // ================================================================

        /// <summary>
        /// Enum→string conversion at L58-59.
        /// Kills: targetType == typeof(string) check removal.
        /// </summary>
        [Fact]
        public void ConvertValue_Enum_To_String()
        {
            var result = TypeConvertHelper.ConvertValue<DayOfWeek, string>(DayOfWeek.Monday);
            Assert.Equal("Monday", result);
        }

        /// <summary>
        /// Enum→int (underlying value) at L62-63.
        /// Kills: underlyingTarget.IsValueType check removal.
        /// </summary>
        [Fact]
        public void ConvertValue_Enum_To_Int()
        {
            var result = TypeConvertHelper.ConvertValue<DayOfWeek, int>(DayOfWeek.Wednesday);
            Assert.Equal(3, result);
        }

        // ================================================================
        // Target-is-enum path (L67-103)
        // ================================================================

        /// <summary>
        /// string→enum valid conversion at L76.
        /// Kills: string.IsNullOrEmpty negation at L75 — if IsNullOrEmpty becomes !IsNullOrEmpty,
        /// empty string would go to Enum.Parse instead of returning default.
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Enum_Valid()
        {
            var result = TypeConvertHelper.ConvertValue<string, DayOfWeek>("Friday");
            Assert.Equal(DayOfWeek.Friday, result);
        }

        /// <summary>
        /// Empty string→enum throws MapException（A1-08，原 :111
        /// 静默 return default 按显式抛口径订正——Enum.Parse ArgumentException → 统一转
        /// MapException；原"返回 default"断言随契约反转；mutant 口径：删空串哨兵即抛，
        /// 恢复哨兵即返 default——两态皆可杀）。
        /// </summary>
        [Fact]
        public void ConvertValue_EmptyString_To_Enum_Returns_Default()
        {
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<string, DayOfWeek>(""));
            Assert.Contains("DayOfWeek", ex.Message);
        }

        /// <summary>
        /// int→enum via Enum.ToObject at L80-81.
        /// Kills: sourceType != typeof(float) &amp;&amp; sourceType != typeof(double) at L80.
        /// If &amp;&amp; becomes ||, int would fall through to the float path.
        /// </summary>
        [Fact]
        public void ConvertValue_Int_To_Enum()
        {
            var result = TypeConvertHelper.ConvertValue<int, DayOfWeek>(5);
            Assert.Equal(DayOfWeek.Friday, result);  // DayOfWeek: Sunday=0, Monday=1, ..., Friday=5
        }

        /// <summary>
        /// float→enum via Convert.ToInt64 at L86-87.
        /// Kills: sourceType == typeof(float) || sourceType == typeof(double) at L84.
        /// If || becomes &amp;&amp;, float would NOT match (only double would), and fall through to throw.
        /// </summary>
        [Fact]
        public void ConvertValue_Float_To_Enum()
        {
            var result = TypeConvertHelper.ConvertValue<float, DayOfWeek>(3.0f);
            Assert.Equal(DayOfWeek.Wednesday, result);
        }

        /// <summary>
        /// double→enum via Convert.ToInt64 at L86-87.
        /// Companion test for the float→enum path — both float and double must work.
        /// </summary>
        [Fact]
        public void ConvertValue_Double_To_Enum()
        {
            var result = TypeConvertHelper.ConvertValue<double, DayOfWeek>(2.0);
            Assert.Equal(DayOfWeek.Tuesday, result);
        }

        /// <summary>
        /// Invalid string→enum throws MapException (catch at L96-103).
        /// Kills: catch(ArgumentException) removal at L97.
        /// </summary>
        [Fact]
        public void ConvertValue_Invalid_String_To_Enum_Throws()
        {
            Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<string, DayOfWeek>("NotADay"));
        }

        // ================================================================
        // Object→string via ToString (L107-108)
        // ================================================================

        /// <summary>
        /// Non-string object→string via ToString at L107-108.
        /// Kills: targetType == typeof(string) check removal.
        /// </summary>
        [Fact]
        public void ConvertValue_Object_To_String_Via_ToString()
        {
            // int is IConvertible and will go through ChangeType for string target
            var result = TypeConvertHelper.ConvertValue<int, string>(42);
            Assert.Equal("42", result);
        }

        // ================================================================
        // Guid paths (L110-118)
        // ================================================================

        /// <summary>
        /// Guid→Guid direct assignment at L116-117.
        /// Kills: source is Guid check removal at L116.
        /// </summary>
        [Fact]
        public void ConvertValue_Guid_To_Guid()
        {
            var guid = Guid.NewGuid();
            var result = TypeConvertHelper.ConvertValue<Guid, Guid>(guid);
            Assert.Equal(guid, result);
        }

        /// <summary>
        /// Guid→string via ToString path at L107-108 (not Guid-specific path).
        /// Verifies Guid can be converted to string representation.
        /// </summary>
        [Fact]
        public void ConvertValue_Guid_To_String()
        {
            var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var result = TypeConvertHelper.ConvertValue<Guid, string>(guid);
            Assert.Contains("aaaaaaaa", result);
        }

        // ================================================================
        // null source guard (L20)
        // ================================================================

        /// <summary>
        /// null source returns default at L20.
        /// Kills: null check removal.
        /// </summary>
        [Fact]
        public void ConvertValue_Null_Source_Returns_Default()
        {
            var result = TypeConvertHelper.ConvertValue<string, int>(null!);
            Assert.Equal(0, result);
        }

        // ================================================================
        // Direct type match (L27)
        // ================================================================

        /// <summary>
        /// Source is already TTarget — returns directly at L27.
        /// Kills: source is TTarget check removal.
        /// </summary>
        [Fact]
        public void ConvertValue_Same_Type_Returns_Directly()
        {
            var result = TypeConvertHelper.ConvertValue<int, int>(42);
            Assert.Equal(42, result);
        }

        // ================================================================
        // Nullable target type (L24)
        // ================================================================

        /// <summary>
        /// string→int? via Nullable unwrapping at L24.
        /// Kills: Nullable.GetUnderlyingType usage removal.
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_NullableInt()
        {
            var result = TypeConvertHelper.ConvertValue<string, int?>("99");
            Assert.Equal(99, result);
        }

        // ================================================================
        // All-paths-fail throw (L121-123)
        // ================================================================

        /// <summary>
        /// Completely incompatible types — throws MapException at L121.
        /// This verifies the final throw is reachable.
        /// </summary>
        [Fact]
        public void ConvertValue_Incompatible_Types_Throws_MapException()
        {
            // Guid→DateTime has no conversion path
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<Guid, DateTime>(Guid.NewGuid()));
            Assert.Contains("Guid", ex.Message);
        }

        // ================================================================
        // Case-insensitive enum parsing (L76 ignoreCase: true)
        // ================================================================

        /// <summary>
        /// Case-insensitive enum parsing at L76.
        /// Kills: ignoreCase: true → false mutation.
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Enum_CaseInsensitive()
        {
            var result = TypeConvertHelper.ConvertValue<string, DayOfWeek>("monday");
            Assert.Equal(DayOfWeek.Monday, result);
        }
    }
}
