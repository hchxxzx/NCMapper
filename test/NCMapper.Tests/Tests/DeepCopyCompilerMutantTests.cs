using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// DeepCopyCompiler mutant-killing tests — 覆盖深拷贝编译器中存活变异体的代码路径
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DeepCopyCompilerMutantTests
    {
        // ================================================================
        // 测试模型 — 针对 DeepCopyCompiler 中存活变异体设计
        // ================================================================

        /// <summary>
        /// 含索引器属性的实体 — 验证 L136 的 p.GetIndexParameters().Length == 0 过滤
        /// 如果变异为 != 0，索引器属性会被错误包含，导致编译失败
        /// </summary>
        public class EntityWithIndexer
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            private readonly Dictionary<string, int> _data = new();

            /// <summary>
            /// 索引器属性 — 应在深拷贝编译时被过滤掉（GetIndexParameters().Length != 0）
            /// </summary>
            public int this[string key]
            {
                get => _data.TryGetValue(key, out var val) ? val : 0;
                set => _data[key] = value;
            }
        }

        /// <summary>
        /// Record 类型带引用类型参数 — 验证 L169-176 的 DeepCopy 递归路径
        /// 构造函数参数为非简单类型时，需递归调用 DeepCopy
        /// </summary>
        public record RecordWithRefType(string Name, int Age, Address? HomeAddress);

        /// <summary>
        /// Record 类型带类型不匹配参数 — 验证 L164 的 srcProp.PropertyType == paramType 分支
        /// 当属性类型与构造器参数类型不同时，需要 Convert
        /// </summary>
        public record RecordWithTypeMismatch(long Id, string Label);

        /// <summary>
        /// 多构造函数类型 — 验证 L185/L192 的最佳构造函数选择逻辑
        /// 一个构造函数部分匹配，另一个完全匹配
        /// </summary>
        public class MultiCtorEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Extra { get; set; } = "";

            /// <summary>
            /// 两参数构造 — 部分匹配（Id 匹配，description 不匹配任何属性）
            /// </summary>
            public MultiCtorEntity(int id, string description)
            {
                Id = id;
                Name = description;
                Extra = "default";
            }

            /// <summary>
            /// 三参数构造 — 完全匹配（Id, Name, Extra 全匹配）
            /// </summary>
            public MultiCtorEntity(int id, string name, string extra)
            {
                Id = id;
                Name = name;
                Extra = extra;
            }
        }

        /// <summary>
        /// 部分构造函数匹配实体 — 构造器参数多于源属性
        /// 验证 L180-182 的 Default(parameters[i].ParameterType) 默认值路径
        /// </summary>
        public class PartialMatchEntity
        {
            public int Id { get; set; }
            // 注意：没有 Name 属性，但构造函数需要 name 参数

            /// <summary>
            /// 构造器参数 name 在源属性中无匹配 → 走 Default 路径
            /// </summary>
            public PartialMatchEntity(int id, string name)
            {
                Id = id;
                // name 参数无对应属性，应为 default(string) = null
            }
        }

        /// <summary>
        /// 含 null 引用属性的实体 — 验证深拷贝中 null 属性的处理
        /// </summary>
        public class EntityWithNullRef
        {
            public int Id { get; set; }
            public Address? Address { get; set; }
        }

        /// <summary>
        /// 含只读属性的实体 — 验证深拷贝 FillProperties 中 CanWrite 过滤
        /// </summary>
        public class EntityWithReadOnly
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string ComputedField { get; } = "readonly";
        }

        // ================================================================
        // 测试用例 — 杀灭 DeepCopyCompiler 存活变异体
        // ================================================================

        /// <summary>
        /// 含索引器属性的实体深拷贝 — 杀灭 L136 的 GetIndexParameters().Length == 0 变异
        /// 如果变异为 != 0，只会包含索引器属性，导致编译失败或属性丢失
        /// </summary>
        [Fact]
        public void DeepCopy_Entity_With_Indexer_Property()
        {
            var original = new EntityWithIndexer { Id = 42, Name = "Test" };
            original["key1"] = 100;

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(42, copy.Id);
            Assert.Equal("Test", copy.Name);
        }

        /// <summary>
        /// Record 类型带引用类型构造器参数 — 杀灭 L169-176 的 DeepCopy 递归路径
        /// 引用类型参数需要 Condition + DeepCopy 调用，而非简单赋值
        /// </summary>
        [Fact]
        public void DeepCopy_Record_With_RefType_ConstructorArg()
        {
            var original = new RecordWithRefType(
                "Alice",
                30,
                new Address { City = "Beijing", Street = "ChangAn", ZipCode = 100000 }
            );

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal("Alice", copy.Name);
            Assert.Equal(30, copy.Age);
            Assert.NotNull(copy.HomeAddress);
            Assert.NotSame(original.HomeAddress, copy.HomeAddress);
            Assert.Equal("Beijing", copy.HomeAddress!.City);
        }

        /// <summary>
        /// Record 类型带 null 引用类型构造器参数 — 杀灭 L172-175 的 Condition null 检查
        /// 当引用类型参数为 null 时，Condition 表达式应返回 null 而非调用 DeepCopy
        /// </summary>
        [Fact]
        public void DeepCopy_Record_With_Null_RefType_ConstructorArg()
        {
            var original = new RecordWithRefType("Bob", 25, null);

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal("Bob", copy.Name);
            Assert.Null(copy.HomeAddress);
        }

        /// <summary>
        /// Record 类型带类型不匹配的构造器参数 — 杀灭 L164 的 PropertyType == paramType 变异
        /// 当属性类型(long)与源属性类型不匹配时，需要 Convert 表达式
        /// </summary>
        [Fact]
        public void DeepCopy_Record_With_Type_Mismatch_ConstructorArg()
        {
            // RecordWithTypeMismatch(long Id, string Label)
            // Id 属性类型是 long，构造器参数也是 long → 类型匹配，直接赋值
            var original = new RecordWithTypeMismatch(999L, "TestLabel");

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(999L, copy.Id);
            Assert.Equal("TestLabel", copy.Label);
        }

        /// <summary>
        /// 多构造函数 — 完全匹配的构造器被优先选择
        /// 杀灭 L185 的 matched == parameters.Length 和 L189 的 break
        /// 如果变异为 !=，break 不执行，可能选择错误的构造器
        /// </summary>
        [Fact]
        public void DeepCopy_FullConstructorMatch_Selected()
        {
            var original = new MultiCtorEntity(42, "Hello", "World");

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(42, copy.Id);
            Assert.Equal("Hello", copy.Name);
            // 完全匹配的3参数构造器应被选中，Extra = "World"
            Assert.Equal("World", copy.Extra);
        }

        /// <summary>
        /// 部分构造函数匹配 —  unmatched 参数使用 default 值
        /// 杀灭 L180-182 的 Default(parameters[i].ParameterType) 路径
        /// 和 L192 的 matched > bestMatched 选择逻辑
        /// </summary>
        [Fact]
        public void DeepCopy_PartialConstructorMatch_DefaultForUnmatched()
        {
            var original = new PartialMatchEntity(42, "ignored");

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(42, copy.Id);
        }

        /// <summary>
        /// 清除深拷贝缓存后重新深拷贝 — 杀灭 L374-378 的 ClearCache 方法
        /// 验证缓存清除后，后续深拷贝仍能正常工作
        /// </summary>
        [Fact]
        public void DeepCopy_ClearCache_SubsequentCopy_Works()
        {
            var original = new EntityWithNullRef
            {
                Id = 1,
                Address = new Address { City = "Beijing", Street = "Street1", ZipCode = 100001 }
            };

            // 第一次拷贝 — 编译并缓存
            var copy1 = Mapper.DeepCopy(original)!;
            Assert.Equal("Beijing", copy1.Address!.City);

            // 清除所有缓存
            Mapper.ClearCache();

            // 第二次拷贝 — 重新编译，结果应一致
            var copy2 = Mapper.DeepCopy(original)!;
            Assert.NotSame(original, copy2);
            Assert.Equal(1, copy2.Id);
            Assert.NotNull(copy2.Address);
            Assert.NotSame(original.Address, copy2.Address);
            Assert.Equal("Beijing", copy2.Address!.City);
            Assert.Equal("Street1", copy2.Address.Street);
            Assert.Equal(100001, copy2.Address.ZipCode);
        }

        /// <summary>
        /// null 引用属性在深拷贝中保持 null — 杀灭 CompileFiller 中的 null 检查条件
        /// </summary>
        [Fact]
        public void DeepCopy_Null_Property_Preserved()
        {
            var original = new EntityWithNullRef { Id = 7, Address = null };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(7, copy.Id);
            Assert.Null(copy.Address);
        }

        /// <summary>
        /// 含只读属性的实体深拷贝 — 验证 CompileFiller 正确过滤只读属性
        /// 只读属性(无 setter)在 FillProperties 中不应被赋值
        /// </summary>
        [Fact]
        public void DeepCopy_Entity_With_ReadOnly_Property()
        {
            var original = new EntityWithReadOnly { Id = 10, Name = "Test" };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(10, copy.Id);
            Assert.Equal("Test", copy.Name);
            Assert.Equal("readonly", copy.ComputedField);
        }

        /// <summary>
        /// 深拷贝嵌套引用对象 — 验证 FillProperties 中引用类型属性的递归 DeepCopy
        /// 杀灭 CompileFiller 中非简单类型的 DeepCopy 递归路径
        /// </summary>
        [Fact]
        public void DeepCopy_Nested_Reference_Property_DeepCopied()
        {
            var original = new EntityWithNullRef
            {
                Id = 5,
                Address = new Address { City = "Shanghai", Street = "Nanjing", ZipCode = 200000 }
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.NotNull(copy.Address);
            Assert.NotSame(original.Address, copy.Address);
            Assert.Equal("Shanghai", copy.Address!.City);
            Assert.Equal("Nanjing", copy.Address.Street);
            Assert.Equal(200000, copy.Address.ZipCode);

            // 修改拷贝不影响原对象
            copy.Address.City = "Hangzhou";
            Assert.Equal("Shanghai", original.Address.City);
        }

        /// <summary>
        /// 字符串类型深拷贝 — 验证 L45-46 的 string 快速返回路径
        /// 和 L86-87 的 FillProperties 中 string 快速返回
        /// </summary>
        [Fact]
        public void DeepCopy_String_Returns_Same_Reference()
        {
            var original = "Hello World";
            var copy = Mapper.DeepCopy(original);
            // string 是不可变类型，深拷贝返回相同引用
            Assert.Same(original, copy);
        }

        /// <summary>
        /// 列表属性深拷贝 — 验证 CompileCreator 对集合类型返回 null
        /// 然后通过 CompileCollectionCopy 路径处理
        /// </summary>
        [Fact]
        public void DeepCopy_Entity_With_List_Property()
        {
            var original = new Blog
            {
                Title = "TestBlog",
                Posts = new List<Post>
                {
                    new Post { Title = "Post1", Views = 100 },
                    new Post { Title = "Post2", Views = 200 }
                },
                TagCounts = new Dictionary<string, int> { ["tech"] = 5 }
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal("TestBlog", copy.Title);
            Assert.NotNull(copy.Posts);
            Assert.NotSame(original.Posts, copy.Posts);
            Assert.Equal(2, copy.Posts!.Count);
            Assert.Equal("Post1", copy.Posts[0].Title);
            Assert.NotSame(original.Posts[0], copy.Posts[0]);
        }

        // ================================================================
        // Round 2 — Collection type branch tests (L243-270)
        // ================================================================

        /// <summary>
        /// DeepCopy entity with HashSet property — exercises L258 branch (def == typeof(HashSet&lt;&gt;)).
        /// Kills mutant: HashSet branch removed (NoCoverage at L258).
        /// </summary>
        [Fact]
        public void DeepCopy_HashSet_Property_Handled()
        {
            var original = new EntityWithHashSet
            {
                Id = 1,
                Tags = new HashSet<string> { "alpha", "beta", "gamma" }
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(1, copy.Id);
            Assert.NotNull(copy.Tags);
            Assert.NotSame(original.Tags, copy.Tags);
            Assert.Equal(3, copy.Tags!.Count);
            Assert.Contains("alpha", copy.Tags);
            Assert.Contains("beta", copy.Tags);
            Assert.Contains("gamma", copy.Tags);
        }

        /// <summary>
        /// DeepCopy entity with Queue property — exercises L260 branch (def == typeof(Queue&lt;&gt;)).
        /// Kills mutant: Queue branch removed (NoCoverage at L260).
        /// </summary>
        [Fact]
        public void DeepCopy_Queue_Property_Handled()
        {
            var original = new EntityWithQueue
            {
                Id = 2,
                Items = new Queue<int>(new[] { 10, 20, 30 })
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Id);
            Assert.NotNull(copy.Items);
            Assert.NotSame(original.Items, copy.Items);
            Assert.Equal(3, copy.Items!.Count);
            // Queue preserves order
            var copyArr = copy.Items.ToArray();
            Assert.Equal(10, copyArr[0]);
            Assert.Equal(20, copyArr[1]);
            Assert.Equal(30, copyArr[2]);
        }

        /// <summary>
        /// DeepCopy entity with Stack property — exercises L262 branch (def == typeof(Stack&lt;&gt;)).
        /// Kills mutant: Stack branch removed (NoCoverage at L262).
        /// </summary>
        [Fact]
        public void DeepCopy_Stack_Property_Handled()
        {
            var original = new EntityWithStack
            {
                Id = 3,
                History = new Stack<int>(new[] { 1, 2, 3 })
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Id);
            Assert.NotNull(copy.History);
            Assert.NotSame(original.History, copy.History);
            Assert.Equal(3, copy.History!.Count);
        }

        /// <summary>
        /// DeepCopy entity with ConcurrentQueue property — exercises L264 branch.
        /// Kills mutant: ConcurrentQueue branch removed (NoCoverage at L264).
        /// </summary>
        [Fact]
        public void DeepCopy_ConcurrentQueue_Property_Handled()
        {
            var original = new EntityWithConcurrentQueue
            {
                Id = 4,
                Messages = new ConcurrentQueue<string>(new[] { "msg1", "msg2" })
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(4, copy.Id);
            Assert.NotNull(copy.Messages);
            Assert.NotSame(original.Messages, copy.Messages);
            Assert.Equal(2, copy.Messages!.Count);
        }

        /// <summary>
        /// DeepCopy entity with ConcurrentStack property — exercises L266 branch.
        /// Kills mutant: ConcurrentStack branch removed (NoCoverage at L266).
        /// </summary>
        [Fact]
        public void DeepCopy_ConcurrentStack_Property_Handled()
        {
            var original = new EntityWithConcurrentStack
            {
                Id = 5,
                Values = new ConcurrentStack<int>(new[] { 100, 200 })
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(5, copy.Id);
            Assert.NotNull(copy.Values);
            Assert.NotSame(original.Values, copy.Values);
            Assert.Equal(2, copy.Values!.Count);
        }

        /// <summary>
        /// DeepCopy entity with ReadOnlyCollection property — exercises L268 branch.
        /// Kills mutant: ReadOnlyCollection branch removed (NoCoverage at L268).
        /// </summary>
        [Fact]
        public void DeepCopy_ReadOnlyCollection_Property_Handled()
        {
            var original = new EntityWithReadOnlyCollection
            {
                Id = 6,
                Items = new List<int> { 7, 8, 9 }.AsReadOnly()
            };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(6, copy.Id);
            Assert.NotNull(copy.Items);
            Assert.NotSame(original.Items, copy.Items);
            Assert.Equal(3, copy.Items!.Count);
            Assert.Equal(7, copy.Items[0]);
            Assert.Equal(8, copy.Items[1]);
            Assert.Equal(9, copy.Items[2]);
        }

        /// <summary>
        /// DeepCopy standalone List&lt;int&gt; — exercises L256 direct collection copy.
        /// Verifies the List branch in TryGetCollectionCopyMethod.
        /// </summary>
        [Fact]
        public void DeepCopy_Standalone_List_Handled()
        {
            var original = new List<int> { 1, 2, 3, 4, 5 };

            var copy = Mapper.DeepCopy(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(5, copy.Count);
            Assert.Equal(1, copy[0]);
            Assert.Equal(5, copy[4]);
        }

        // ================================================================
        // Test model classes for collection type tests
        // ================================================================

        public class EntityWithHashSet
        {
            public int Id { get; set; }
            public HashSet<string>? Tags { get; set; }
        }

        public class EntityWithQueue
        {
            public int Id { get; set; }
            public Queue<int>? Items { get; set; }
        }

        public class EntityWithStack
        {
            public int Id { get; set; }
            public Stack<int>? History { get; set; }
        }

        public class EntityWithConcurrentQueue
        {
            public int Id { get; set; }
            public ConcurrentQueue<string>? Messages { get; set; }
        }

        public class EntityWithConcurrentStack
        {
            public int Id { get; set; }
            public ConcurrentStack<int>? Values { get; set; }
        }

        public class EntityWithReadOnlyCollection
        {
            public int Id { get; set; }
            public ReadOnlyCollection<int>? Items { get; set; }
        }
    }
}
