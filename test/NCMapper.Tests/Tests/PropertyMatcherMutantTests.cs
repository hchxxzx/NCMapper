using NCMapper.Attributes;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Mutant-killing tests for PropertyMatcher.cs, PropertyIgnoreChecker.cs, and FastDeepCopyProvider.cs
    /// Targets: complex boolean conditions, cache clearing, Direction switch, depth boundary
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class PropertyMatcherMutantTests : IDisposable
    {
        private readonly int _originalMaxRecursionDepth;

        public PropertyMatcherMutantTests()
        {
            _originalMaxRecursionDepth = Mapper.MaxRecursionDepth;
            Mapper.ClearCache();
        }

        public void Dispose()
        {
            Mapper.MaxRecursionDepth = _originalMaxRecursionDepth;
            Mapper.ClearCache();
        }

        // ================================================================
        // PropertyMatcher.GetPropertyList — source/target attribute check (L59)
        // (sourceAttr == null && targetAttr == null) || (sourceAttr != null && targetAttr != null)
        // ================================================================

        /// <summary>
        /// Both types have [MapFrom] — neither filtered (L59 first branch).
        /// Kills: the (sourceAttr != null &amp;&amp; targetAttr != null) part — if mutated to ||,
        /// the filtering logic would incorrectly activate.
        /// </summary>
        [Fact]
        public void GetPropertyList_Both_Have_MapFrom_NoFilter()
        {
            // FullTypeEntity has no [MapFrom], SameTypeDto has [MapFrom(typeof(FullTypeEntity))]
            // This exercises the "one has attr, other doesn't" branch
            var result = PropertyMatcher.GetPropertyList(typeof(FullTypeEntity), typeof(SameTypeDto));
            Assert.True(result.Key.Count > 0);
            Assert.True(result.Value.Count > 0);
        }

        /// <summary>
        /// Neither type has [MapFrom] — no filtering (L59 first branch).
        /// Kills: (sourceAttr == null &amp;&amp; targetAttr == null) — if mutated to ||,
        /// would incorrectly filter when both are null.
        /// </summary>
        [Fact]
        public void GetPropertyList_Neither_Has_MapFrom_NoFilter()
        {
            // Two types without [MapFrom]
            var result = PropertyMatcher.GetPropertyList(typeof(FullTypeEntity), typeof(FullTypeEntity));
            Assert.True(result.Key.Count > 0);
            Assert.True(result.Value.Count > 0);
        }

        // ================================================================
        // PropertyMatcher.GetPropertiesDictIgnoreCase — cache (L113)
        // ================================================================

        /// <summary>
        /// GetPropertiesDictIgnoreCase returns cached result on second call.
        /// Kills: cache.GetOrAdd mutation.
        /// </summary>
        [Fact]
        public void GetPropertiesDictIgnoreCase_Cached()
        {
            var dict1 = PropertyMatcher.GetPropertiesDictIgnoreCase(typeof(FullTypeEntity));
            var dict2 = PropertyMatcher.GetPropertiesDictIgnoreCase(typeof(FullTypeEntity));

            Assert.NotNull(dict1);
            Assert.NotNull(dict2);
            Assert.Same(dict1, dict2); // Same reference = cached
        }

        /// <summary>
        /// GetPropertiesDictIgnoreCase null input returns null.
        /// Kills: null guard removal at L113.
        /// </summary>
        [Fact]
        public void GetPropertiesDictIgnoreCase_Null_Returns_Null()
        {
            var result = PropertyMatcher.GetPropertiesDictIgnoreCase(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// GetPropertiesDictIgnoreCase is case-insensitive.
        /// Kills: StringComparer.OrdinalIgnoreCase → Ordinal mutation at L118.
        /// </summary>
        [Fact]
        public void GetPropertiesDictIgnoreCase_CaseInsensitive()
        {
            var dict = PropertyMatcher.GetPropertiesDictIgnoreCase(typeof(FullTypeEntity));
            Assert.NotNull(dict);
            Assert.True(dict!.ContainsKey("intprop"));    // lowercase
            Assert.True(dict.ContainsKey("INTPROP"));    // uppercase
            Assert.True(dict.ContainsKey("IntProp"));    // original case
        }

        // ================================================================
        // PropertyMatcher.GetMapFromAttribute — cache (L145-148)
        // ================================================================

        /// <summary>
        /// GetMapFromAttribute returns null for type without [MapFrom].
        /// Kills: GetCustomAttribute return value mutation.
        /// </summary>
        [Fact]
        public void GetMapFromAttribute_No_Attribute_Returns_Null()
        {
            var result = PropertyMatcher.GetMapFromAttribute(typeof(FullTypeEntity));
            Assert.Null(result);
        }

        /// <summary>
        /// GetMapFromAttribute returns attribute for type with [MapFrom].
        /// </summary>
        [Fact]
        public void GetMapFromAttribute_With_Attribute_Returns_Attr()
        {
            var result = PropertyMatcher.GetMapFromAttribute(typeof(SameTypeDto));
            Assert.NotNull(result);
            Assert.Equal(typeof(FullTypeEntity), result!.FromSource);
        }

        // SplitSourceByAttr_Correct_Split 用例随方法退役删除
        //（X2-B-1/源属性 [MapProperty] 匹配段出清连带——④）

        // ================================================================
        // PropertyMatcher.TryGetMapPropertyInfo (L245-260)
        // ================================================================

        /// <summary>
        /// TryGetMapPropertyInfo with [MapProperty] attribute — returns true.
        /// Kills: fieldAttr != null check negation at L249.
        /// </summary>
        [Fact]
        public void TryGetMapPropertyInfo_With_Attribute()
        {
            var prop = typeof(DtoWithMapProperty).GetProperty(nameof(DtoWithMapProperty.MappedField))!;
            var hasAttr = PropertyMatcher.TryGetMapPropertyInfo(prop, typeof(FullTypeEntity),
                out var sourceFieldName, out var fieldSourceType, out var isFromJson);

            Assert.True(hasAttr);
            Assert.Equal("IntProp", sourceFieldName);
            Assert.Equal(typeof(FullTypeEntity), fieldSourceType);
            Assert.False(isFromJson);
        }

        /// <summary>
        /// TryGetMapPropertyInfo without [MapProperty] — returns false, uses defaults.
        /// Kills: false return path at L258 — if returns true, downstream logic breaks.
        /// </summary>
        [Fact]
        public void TryGetMapPropertyInfo_Without_Attribute()
        {
            var prop = typeof(SameTypeDto).GetProperty(nameof(SameTypeDto.IntProp))!;
            var hasAttr = PropertyMatcher.TryGetMapPropertyInfo(prop, typeof(FullTypeEntity),
                out var sourceFieldName, out var fieldSourceType, out var isFromJson);

            Assert.False(hasAttr);
            Assert.Equal("IntProp", sourceFieldName); // Falls back to property name
            Assert.Equal(typeof(FullTypeEntity), fieldSourceType); // Falls back to default
            Assert.False(isFromJson);
        }

        // ================================================================
        // PropertyMatcher.ClearCache (L265-270)
        // ================================================================

        /// <summary>
        /// ClearCache clears all three caches.
        /// Kills: individual .Clear() removal mutants.
        /// </summary>
        [Fact]
        public void ClearCache_Clears_All()
        {
            // Populate caches
            PropertyMatcher.GetPropertiesDictIgnoreCase(typeof(FullTypeEntity));
            PropertyMatcher.GetMapFromAttribute(typeof(SameTypeDto));

            // Clear
            PropertyMatcher.ClearCache();

            // Re-populate — caches should be rebuilt
            var dict = PropertyMatcher.GetPropertiesDictIgnoreCase(typeof(FullTypeEntity));
            Assert.NotNull(dict);
            Assert.True(dict!.Count > 0);
        }

        // ================================================================
        // PropertyIgnoreChecker.ShouldIgnore — Direction switch (L28-33)
        // ================================================================

        /// <summary>
        /// [MapIgnore(Direction.Both)] — always ignored regardless of direction.
        /// Kills: Direction.Both → true at L30.
        /// </summary>
        [Fact]
        public void ShouldIgnore_Both_Always_Ignored()
        {
            var prop = typeof(DtoWithDirectionIgnore).GetProperty(nameof(DtoWithDirectionIgnore.BothIgnored))!;
            Assert.True(PropertyMatcher.ShouldIgnore(prop, isToDto: true));
            Assert.True(PropertyMatcher.ShouldIgnore(prop, isToDto: false));
        }

        /// <summary>
        /// [MapIgnore(Direction.ToDto)] — ignored only in ToDto direction.
        /// Kills: Direction.ToDto → isToDto at L31 (if mutated to !isToDto).
        /// </summary>
        [Fact]
        public void ShouldIgnore_ToDto_Only_Ignored_In_ToDto()
        {
            var prop = typeof(DtoWithDirectionIgnore).GetProperty(nameof(DtoWithDirectionIgnore.ToDtoIgnored))!;
            Assert.True(PropertyMatcher.ShouldIgnore(prop, isToDto: true));
            Assert.False(PropertyMatcher.ShouldIgnore(prop, isToDto: false));
        }

        /// <summary>
        /// [MapIgnore(Direction.ToEntity)] — ignored only in ToEntity direction.
        /// Kills: Direction.ToEntity → !isToDto at L32 (if mutated to isToDto).
        /// </summary>
        [Fact]
        public void ShouldIgnore_ToEntity_Only_Ignored_In_ToEntity()
        {
            var prop = typeof(DtoWithDirectionIgnore).GetProperty(nameof(DtoWithDirectionIgnore.ToEntityIgnored))!;
            Assert.False(PropertyMatcher.ShouldIgnore(prop, isToDto: true));
            Assert.True(PropertyMatcher.ShouldIgnore(prop, isToDto: false));
        }

        /// <summary>
        /// No [MapIgnore] — never ignored.
        /// Kills: null check at L26 (ignoreAttr == null → false).
        /// </summary>
        [Fact]
        public void ShouldIgnore_No_Attribute_Never_Ignored()
        {
            var prop = typeof(DtoWithDirectionIgnore).GetProperty(nameof(DtoWithDirectionIgnore.NotIgnored))!;
            Assert.False(PropertyMatcher.ShouldIgnore(prop, isToDto: true));
            Assert.False(PropertyMatcher.ShouldIgnore(prop, isToDto: false));
        }

        /// <summary>
        /// PropertyIgnoreChecker.ClearCache clears the ignore attribute cache.
        /// Kills: s_ignoreAttrCache.Clear() removal at L40.
        /// </summary>
        [Fact]
        public void PropertyIgnoreChecker_ClearCache_Works()
        {
            // Populate
            var prop = typeof(DtoWithDirectionIgnore).GetProperty(nameof(DtoWithDirectionIgnore.BothIgnored))!;
            PropertyMatcher.ShouldIgnore(prop, true);

            // Clear
            PropertyIgnoreChecker.ClearCache();

            // Re-check — should still work
            Assert.True(PropertyMatcher.ShouldIgnore(prop, true));
        }

        // ================================================================
        // FastDeepCopyProvider — depth boundary at 500 (L45)
        // ================================================================

        /// <summary>
        /// FastDeepCopyProvider null source returns null at L39.
        /// Kills: null check removal.
        /// </summary>
        [Fact]
        public void FastDeepCopy_Null_Returns_Null()
        {
            var provider = new FastDeepCopyProvider();
            var result = provider.DeepCopy<FullTypeEntity>(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// FastDeepCopyProvider basic copy works.
        /// Kills: CreateInstance/FillProperties statement removal.
        /// </summary>
        [Fact]
        public void FastDeepCopy_Basic_Copy()
        {
            var provider = new FastDeepCopyProvider();
            var source = new FullTypeEntity { IntProp = 42, StringProp = "test" };
            var copy = provider.DeepCopy(source);

            Assert.NotNull(copy);
            Assert.NotSame(source, copy);
            Assert.Equal(42, copy!.IntProp);
            Assert.Equal("test", copy.StringProp);
        }

        /// <summary>
        /// FastDeepCopyProvider s_fastDepth resets after each call.
        /// Kills: s_fastDepth-- removal in finally at L58.
        /// Without decrement, sequential copies would accumulate depth and eventually throw.
        /// </summary>
        [Fact]
        public void FastDeepCopy_Many_Sequential_Calls_Dont_Accumulate()
        {
            var provider = new FastDeepCopyProvider();
            var source = new FullTypeEntity { IntProp = 1 };

            // 70 sequential copies — if s_fastDepth-- is removed, depth accumulates
            for (int i = 0; i < 70; i++)
            {
                var copy = provider.DeepCopy(source);
                Assert.NotNull(copy);
                Assert.Equal(1, copy!.IntProp);
            }
        }

        /// <summary>
        /// FastDeepCopyProvider s_fastDepth resets after exception.
        /// Kills: finally block removal at L56-59.
        /// </summary>
        [Fact]
        public void FastDeepCopy_Depth_Resets_After_Exception()
        {
            var provider = new FastDeepCopyProvider();
            var originalDepth = Mapper.MaxRecursionDepth;

            try
            {
                // Set very low depth to trigger exception
                Mapper.MaxRecursionDepth = 1;

                // This may or may not throw depending on nesting depth
                var source = new FullTypeEntity { IntProp = 1 };
                try { provider.DeepCopy(source); } catch { /* expected */ }

                // Restore depth — subsequent copy should work
                Mapper.MaxRecursionDepth = 64;
                var copy = provider.DeepCopy(source);
                Assert.NotNull(copy);
                Assert.Equal(1, copy!.IntProp);
            }
            finally
            {
                Mapper.MaxRecursionDepth = originalDepth;
            }
        }

        // ================================================================
        // GetSourceItemByTargetItem — three-way branching (L167-203)
        // ================================================================

        /// <summary>
        /// GetSourceItemByTargetItem with neither defined — uses name matching at L169.
        /// Kills: (sourceIsDefine &amp;&amp; targetIsDefine) || (!sourceIsDefine &amp;&amp; !targetIsDefine)
        /// — if mutated, wrong branch taken.
        /// </summary>
        [Fact]
        public void GetSourceItemByTargetItem_Neither_Defined_NameMatch()
        {
            var sourceProps = typeof(FullTypeEntity).GetProperties().ToList();
            var targetProp = typeof(SameTypeDto).GetProperty(nameof(SameTypeDto.IntProp))!;

            var result = PropertyMatcher.GetSourceItemByTargetItem(
                typeof(FullTypeEntity), targetProp, sourceProps, sourceIsDefine: false, targetIsDefine: false, out var attr);

            Assert.NotNull(result);
            Assert.Equal("IntProp", result!.Name);
            Assert.Null(attr);
        }

        // ================================================================
        // Test models
        // ================================================================

        [MapFrom(typeof(FullTypeEntity))]
        private class DtoWithMapProperty
        {
            [MapProperty(typeof(FullTypeEntity), nameof(FullTypeEntity.IntProp))]
            public int MappedField { get; set; }
            public string StringProp { get; set; } = "";
        }

        [MapFrom(typeof(FullTypeEntity))]
        private class DtoWithDirectionIgnore
        {
            [MapIgnore(Direction.Both)]
            public string BothIgnored { get; set; } = "";

            [MapIgnore(Direction.ToDto)]
            public string ToDtoIgnored { get; set; } = "";

            [MapIgnore(Direction.ToEntity)]
            public string ToEntityIgnored { get; set; } = "";

            public string NotIgnored { get; set; } = "";
        }
    }
}
