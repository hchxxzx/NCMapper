using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 回归测试 — 专项 1（映射正确性）审计中发现的 Bug，修复后转为回归保护。
    /// B1/B3/B12 已修复并启用；B2/B4 仍未修复，保留 Skip 文档化。
    /// </summary>
    public class KnownIssueTests
    {
        // ================================================================
        // Regression: Issue #B1
        // B1: MapList 快速路径与慢速路径对 null 元素处理不一致 [已修复]
        // ================================================================

        /// <summary>
        /// 【B1 回归】MapList 带 action 时，源列表中的 null 元素应被过滤，
        /// null 元素保留为 null（保持索引一致性），带 action 和不带 action 行为一致。
        /// 修改前：null 被过滤，返回 2 项。
        /// 修改后：null 保留为 null，返回 3 项，索引与源列表一致。
        /// </summary>
        // Regression: Issue #B1 — MapList null element handling consistency
        [Fact]
        public void MapList_WithAction_Should_Filter_Null_Elements()
        {
            var entities = new List<FullTypeEntity?>
            {
                new() { IntProp = 1 },
                null,
                new() { IntProp = 3 }
            };

            // 带 action：null 保留为 null，结果 3 项
            var dtosWithAction = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities!, (s, d) => d.IntProp += 1)!;

            Assert.Equal(3, dtosWithAction.Count);
            Assert.Equal(2, dtosWithAction[0].IntProp);
            Assert.Null(dtosWithAction[1]);
            Assert.Equal(4, dtosWithAction[2].IntProp);

            // 不带 action（快速路径）：同样保留 null，结果 3 项
            var dtosNoAction = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities!)!;

            Assert.Equal(3, dtosNoAction.Count);
            Assert.Equal(1, dtosNoAction[0].IntProp);
            Assert.Null(dtosNoAction[1]);
            Assert.Equal(3, dtosNoAction[2].IntProp);

            // 两条路径行为一致
            Assert.Equal(dtosWithAction.Count, dtosNoAction.Count);
        }

        // ================================================================
        // Regression: Issue #B3
        // B3: List/集合 → Dictionary 目标静默返回 null [已修复]
        // ================================================================

        /// <summary>
        /// 【B3 回归】当源属性是 List、目标属性是 Dictionary（类型不兼容的集合转换）时，
        /// Mapper 应抛出 NotSupportedException 明确告知不支持，而非静默返回 null 导致数据丢失。
        /// 修复前：GetListExpression 的 IDictionary 分支返回 Constant(null)，目标静默置 null。
        /// 修复后：抛 NotSupportedException，暴露配置错误。
        /// </summary>
        // Regression: Issue #B3 — List to Dictionary should throw NotSupportedException
        [Fact]
        public void List_To_Dictionary_Should_Throw_NotSupported()
        {
            var source = new ListToDictSource
            {
                Codes = new List<string> { "A", "B", "C" }
            };

            // List<string> → Dictionary<string,string> 类型不兼容，应抛 NotSupportedException
            var ex = Assert.Throws<NotSupportedException>(() =>
                Mapper.Map<ListToDictSource, ListToDictDto>(source));

            // 验证异常消息包含类型信息，便于排查
            Assert.Contains("List`1", ex.Message);
            Assert.Contains("Dictionary`2", ex.Message);
        }

        // ================================================================
        // Regression: Issue #B12
        // B12: 自引用循环结构导致无限递归栈溢出 [已修复]
        // ================================================================

        /// <summary>
        /// 【B12 回归】自引用图结构（非树）存在循环引用时，Mapper 应检测并抛出
        /// InvalidOperationException，而非无限递归导致 StackOverflowException 进程崩溃。
        /// 修复前：GetClassExpression/GetListExpression 无条件递归，循环引用 → 栈溢出。
        /// 修复后：[ThreadStatic] 递归深度计数器，超过 MaxRecursionDepth(64) 抛异常。
        /// </summary>
        // Regression: Issue #B12 — Circular reference should throw InvalidOperationException
        [Fact]
        public void CircularReference_Should_Throw_InvalidOperationException()
        {
            // 构造循环：A.Children = [B], B.Children = [A]
            var a = new Category { Name = "A" };
            var b = new Category { Name = "B" };
            a.Children = new List<Category> { b };
            b.Children = new List<Category> { a };

            // 应抛 InvalidOperationException（循环引用检测），而非 StackOverflowException
            var ex = Assert.ThrowsAny<InvalidOperationException>(() => Mapper.Map<Category, CategoryDto>(a));

            Assert.True(ex.Message.Contains("循环引用") || ex.Message.Contains("circular reference"),
                $"Expected '循环引用' or 'circular reference' in: {ex.Message}");
            Assert.Contains("64", ex.Message);
        }

        /// <summary>
        /// 【B12 补充】单对象自引用（非集合）循环也应被检测。
        /// Node.Self → Node.Self → ... 无限递归。
        /// </summary>
        // Regression: Issue #B12 — Single object self-reference circular detection
        [Fact]
        public void CircularReference_SingleObject_Should_Throw()
        {
            // 构造自引用循环：node.Self = node
            var node = new SelfRefNode { Value = 1 };
            node.Self = node;

            var ex = Assert.ThrowsAny<InvalidOperationException>(() => Mapper.Map<SelfRefNode, SelfRefNodeDto>(node));

            Assert.True(ex.Message.Contains("循环引用") || ex.Message.Contains("circular reference"),
                $"Expected '循环引用' or 'circular reference' in: {ex.Message}");
        }

        // ================================================================
        // Regression: Issue #B4
        // B4: ConvertDictionary 的 valueMapper 缓存只基于第一个 value 的运行时类型 [已修复]
        // ================================================================

        /// <summary>
        /// 【B4 回归】多表合成 DTO 的混合类型字典映射。
        ///
        /// DictTargetDto 标注 [MapFrom(typeof(DictSourceA))]，属性分别标注：
        ///   [MapProperty(typeof(A), "NameA")] / [MapProperty(typeof(A), "ValueA")] — 从 A 映射
        ///   [MapProperty(typeof(B), "NameB")] / [MapProperty(typeof(B), "ValueB")] — 从 B 映射
        ///
        /// 当字典 value 的运行时类型不一致（A 和 B 交替）时，ConvertDictionary 应对每个 value
        /// 按其实际类型用对应的 MapperBase&lt;A,DTO&gt; / MapperBase&lt;B,DTO&gt; 映射。
        ///
        /// 修复前：valueMapper 单值缓存，首个 value 类型决定 mapper，后续不同类型误用。
        /// 修复后：valueMappers 按 Type 独立缓存，每种运行时类型用对应 mapper。
        /// </summary>
        // Regression: Issue #B4 — ConvertDictionary mixed value type mapper caching
        [Fact]
        public void ConvertDictionary_MixedValueTypes_Should_Map_Each_Correctly()
        {
            // 混合类型 value 字典：A 和 B 交替排列
            var source = new Dictionary<string, object>
            {
                ["a1"] = new DictSourceA { NameA = "A1", ValueA = 10 },
                ["b1"] = new DictSourceB { NameB = "B1", ValueB = 20 },
                ["a2"] = new DictSourceA { NameA = "A2", ValueA = 30 },
                ["b2"] = new DictSourceB { NameB = "B2", ValueB = 40 }
            };

            var result = CollectionCopier.ConvertDictionary<string, DictTargetDto>(source);

            Assert.Equal(4, result.Count);

            // A 类 value → DTO 的 NameA/ValueA 有值，NameB/ValueB 保持默认
            Assert.Equal("A1", result["a1"].NameA);
            Assert.Equal(10, result["a1"].ValueA);
            Assert.Equal("", result["a1"].NameB);   // B 字段未映射，保持默认
            Assert.Equal(0, result["a1"].ValueB);

            // B 类 value → DTO 的 NameB/ValueB 有值，NameA/ValueA 保持默认
            Assert.Equal("", result["b1"].NameA);   // A 字段未映射，保持默认
            Assert.Equal(0, result["b1"].ValueA);
            Assert.Equal("B1", result["b1"].NameB);
            Assert.Equal(20, result["b1"].ValueB);

            // 第二轮交替验证
            Assert.Equal("A2", result["a2"].NameA);
            Assert.Equal(30, result["a2"].ValueA);
            Assert.Equal("B2", result["b2"].NameB);
            Assert.Equal(40, result["b2"].ValueB);
        }

        /// <summary>
        /// 【B4 补充】多表合成 DTO 的正向单对象映射验证：
        /// A → DTO 只映射 A 字段，B → DTO 只映射 B 字段，互不干扰。
        /// </summary>
        // Regression: Issue #B4 — Multi-source DTO A-only field mapping
        [Fact]
        public void MultiSourceDto_A_Maps_A_Fields_Only()
        {
            var a = new DictSourceA { NameA = "FromA", ValueA = 100 };
            var dto = Mapper.Map<DictSourceA, DictTargetDto>(a)!;

            Assert.Equal("FromA", dto.NameA);
            Assert.Equal(100, dto.ValueA);
            Assert.Equal("", dto.NameB);   // B 字段保持默认
            Assert.Equal(0, dto.ValueB);
        }

        /// <summary>
        /// 【B4 补充】B → DTO 只映射 B 字段
        /// </summary>
        // Regression: Issue #B4 — Multi-source DTO B-only field mapping
        [Fact]
        public void MultiSourceDto_B_Maps_B_Fields_Only()
        {
            var b = new DictSourceB { NameB = "FromB", ValueB = 200 };
            var dto = Mapper.Map<DictSourceB, DictTargetDto>(b)!;

            Assert.Equal("", dto.NameA);   // A 字段保持默认
            Assert.Equal(0, dto.ValueA);
            Assert.Equal("FromB", dto.NameB);
            Assert.Equal(200, dto.ValueB);
        }

        /// <summary>
        /// 【B4 补充】DTO → A 反向填充：只回填 A 字段
        /// </summary>
        // Regression: Issue #B4 — Multi-source DTO reverse mapping to A
        [Fact]
        public void MultiSourceDto_Reverse_To_A()
        {
            var dto = new DictTargetDto { NameA = "ToA", ValueA = 50, NameB = "Ignore", ValueB = 99 };
            var a = Mapper.Map<DictTargetDto, DictSourceA>(dto)!;

            Assert.Equal("ToA", a.NameA);
            Assert.Equal(50, a.ValueA);
        }

        /// <summary>
        /// 【B4 补充】DTO → B 反向填充：只回填 B 字段
        /// </summary>
        // Regression: Issue #B4 — Multi-source DTO reverse mapping to B
        [Fact]
        public void MultiSourceDto_Reverse_To_B()
        {
            var dto = new DictTargetDto { NameA = "Ignore", ValueA = 99, NameB = "ToB", ValueB = 77 };
            var b = Mapper.Map<DictTargetDto, DictSourceB>(dto)!;

            Assert.Equal("ToB", b.NameB);
            Assert.Equal(77, b.ValueB);
        }

        // ================================================================
        // Regression: Issue #B2
        // B2: 泛型分支与非泛型分支属性匹配大小写敏感性不一致 [已修复]
        // ================================================================

        /// <summary>
        /// 【B2 回归】源属性名与目标属性名大小写不一致时应正确匹配。
        /// 修复前：泛型分支用 string.Compare(..., false) 大小写敏感，
        ///         非泛型分支用 GetPropertiesDictIgnoreCase 大小写不敏感，行为不一致。
        /// 修复后：GetSourceItemByTargetItem 统一为 OrdinalIgnoreCase，两分支行为一致。
        /// </summary>
        // Regression: Issue #B2 — Case-insensitive property matching consistency
        [Fact]
        public void CaseSensitivity_MixedCase_Should_Match()
        {
            // FullTypeEntity 有 StringProp，CaseMismatchDto 有 stringprop（全小写）
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "Hello" };
            var dto = Mapper.Map<FullTypeEntity, CaseMismatchDto>(entity)!;

            Assert.Equal(42, dto.intprop);     // IntProp → intprop（大小写不敏感匹配）
            Assert.Equal("Hello", dto.stringprop);
        }
    }
}
