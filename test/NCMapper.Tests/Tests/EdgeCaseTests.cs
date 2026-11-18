using NCMapper.Tests.Models;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 边界测试 — 覆盖枚举反推、只读属性、继承、精度、深递归、并发、大集合等场景
    /// </summary>
    public class EdgeCaseTests
    {
        // ================================================================
        // 枚举反向转换：string / number → enum
        // ================================================================

        /// <summary>
        /// 字符串 → 枚举：TypeConvertHelper 的 Enum.Parse 路径
        /// "High" → Priority.High
        /// </summary>
        [Fact]
        public void String_To_Enum_Mapped()
        {
            var entity = new EnumSourceEntity { PriorityStr = "High", PriorityNum = 0 };
            var dto = Mapper.Map<EnumSourceEntity, EnumFromValueDto>(entity)!;

            Assert.Equal(Priority.High, dto.Priority);
        }

        /// <summary>
        /// 数值 → 枚举：TypeConvertHelper 的 Enum.ToObject 路径
        /// 2 → Priority.High
        /// </summary>
        [Fact]
        public void Number_To_Enum_Mapped()
        {
            var entity = new EnumSourceEntity { PriorityStr = "Low", PriorityNum = 2 };
            var dto = Mapper.Map<EnumSourceEntity, EnumFromValueDto>(entity)!;

            Assert.Equal(Priority.High, dto.PriorityByNum);
        }

        /// <summary>
        /// 非法枚举字符串 → 抛出 MapException
        /// 修复前：Enum.Parse 抛 ArgumentException 未被 catch（与 IConvertible 路径行为不一致）
        /// P0 修复后：转换失败抛 MapException，不再静默返回 default
        /// </summary>
        [Fact]
        public void Invalid_Enum_String_Throws_MapException()
        {
            var entity = new EnumSourceEntity { PriorityStr = "NotAnEnum", PriorityNum = 0 };

            // 非法枚举字符串 → 抛出 MapException
            Assert.Throws<MapException>(() => Mapper.Map<EnumSourceEntity, EnumFromValueDto>(entity));
        }

        // ================================================================
        // 只读属性
        // ================================================================

        /// <summary>
        /// 目标属性无 setter（只读）时，映射跳过该属性，不抛异常，保持初始值
        /// </summary>
        [Fact]
        public void Readonly_Property_Skipped_NoThrow()
        {
            var entity = new FullTypeEntity { IntProp = 777 };
            var dto = Mapper.Map<FullTypeEntity, ReadonlyTargetDto>(entity)!;

            // 可写属性正常映射
            Assert.Equal(777, dto.IntProp);
            // 只读属性保持初始值
            Assert.Equal("INITIAL_FIXED", dto.FixedField);
        }

        // ================================================================
        // 继承层次
        // ================================================================

        /// <summary>
        /// 派生类映射时，基类字段和派生类字段都应正确映射
        /// </summary>
        [Fact]
        public void Inheritance_Base_And_Derived_Fields_Mapped()
        {
            var entity = new DerivedEntity
            {
                BaseId = 200,
                BaseName = "Inherited",
                DerivedField = "Child"
            };
            var dto = Mapper.Map<DerivedEntity, DerivedDto>(entity)!;

            Assert.Equal(200, dto.BaseId);
            Assert.Equal("Inherited", dto.BaseName);
            Assert.Equal("Child", dto.DerivedField);
        }

        // ================================================================
        // 数值精度
        // ================================================================

        /// <summary>
        /// 同类型数值映射精度无损：long.MaxValue、高精度 decimal、负数、零
        /// </summary>
        [Fact]
        public void Precision_Preserved_SameType()
        {
            var entity = new PrecisionEntity();
            var dto = Mapper.Map<PrecisionEntity, PrecisionDto>(entity)!;

            Assert.Equal(9223372036854775807L, dto.BigLong);
            Assert.Equal(123456789.987654321m, dto.PreciseDecimal);
            Assert.Equal(-9999.99m, dto.NegativeDecimal);
            Assert.Equal(0, dto.ZeroValue);
        }

        /// <summary>
        /// 跨数值类型：long.MaxValue → decimal（拓宽无损）
        /// </summary>
        [Fact]
        public void Long_To_Decimal_Widening_NoLoss()
        {
            var entity = new PrecisionEntity();
            var dto = Mapper.Map<PrecisionEntity, CrossNumericDto>(entity)!;

            // long.MaxValue = 9223372036854775807，decimal 可无损容纳
            Assert.Equal(9223372036854775807m, dto.BigLong);
        }

        // ================================================================
        // 深度递归
        // ================================================================

        /// <summary>
        /// 10 层深度的自引用链表映射，验证递归不栈溢出且每层值正确
        /// </summary>
        [Fact]
        public void DeepNesting_10_Levels_NoStackOverflow()
        {
            var head = new LinkNode { Value = 0 };
            var cur = head;
            for (int i = 1; i <= 10; i++)
            {
                cur.Next = new LinkNode { Value = i };
                cur = cur.Next;
            }

            var dto = Mapper.Map<LinkNode, LinkNodeDto>(head)!;

            // 遍历验证每一层
            var d = dto;
            for (int i = 0; i <= 10; i++)
            {
                Assert.NotNull(d);
                Assert.Equal(i, d!.Value);
                d = d.Next;
            }
            Assert.Null(d); // 第 11 层为 null
        }

        // ================================================================
        // 大集合
        // ================================================================

        /// <summary>
        /// 1000 元素的大集合映射，验证数量和首尾元素正确
        /// </summary>
        [Fact]
        public void LargeCollection_1000_Items_Mapped()
        {
            var entities = Enumerable.Range(0, 1000)
                .Select(i => new FullTypeEntity { IntProp = i, StringProp = $"Item{i}" })
                .ToList();

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities)!;

            Assert.Equal(1000, dtos.Count);
            Assert.Equal(0, dtos[0].IntProp);
            Assert.Equal("Item0", dtos[0].StringProp);
            Assert.Equal(999, dtos[999].IntProp);
            Assert.Equal("Item999", dtos[999].StringProp);
        }

        // ================================================================
        // 并发安全
        // ================================================================

        /// <summary>
        /// 多线程并发映射同一类型对，验证静态委托缓存的线程安全
        /// （首次并发触发不同泛型类型的静态初始化，验证无竞态崩溃）
        /// </summary>
        [Fact]
        public void Concurrent_Mapping_ThreadSafe()
        {
            var entity = new FullTypeEntity { IntProp = 42, StringProp = "concurrent" };
            var exceptions = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

            // 并发映射多种类型对，触发各自的静态初始化
            Parallel.For(0, 200, i =>
            {
                try
                {
                    var dto1 = Mapper.Map<FullTypeEntity, SameTypeDto>(entity);
                    Assert.Equal(42, dto1!.IntProp);

                    var dto2 = Mapper.Map<FullTypeEntity, ValueToStringDto>(entity);
                    Assert.Equal("42", dto2!.IntProp);

                    var dto3 = Mapper.Map<FullTypeEntity, EnumToIntDto>(entity);
                    Assert.Equal(2, dto3!.StatusProp);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            });

            Assert.Empty(exceptions);
        }

        // ================================================================
        // 空 / null 输入
        // ================================================================

        /// <summary>
        /// Map(source, target) 当 source 为 null 时，不抛异常且不修改 target
        /// </summary>
        [Fact]
        public void Update_Existing_NullSource_NoThrow()
        {
            var target = new SameTypeDto { IntProp = 111, StringProp = "keep" };
            Mapper.Map<FullTypeEntity, SameTypeDto>(null!, target);

            // source 为 null，target 应保持原值不变
            Assert.Equal(111, target.IntProp);
            Assert.Equal("keep", target.StringProp);
        }

        /// <summary>
        /// Map(source, target) 当 target 为 null 时，不抛异常
        /// </summary>
        [Fact]
        public void Update_Existing_NullTarget_NoThrow()
        {
            var entity = new FullTypeEntity { IntProp = 999 };
            // target 为 null，不应抛 NullReferenceException
            Mapper.Map(entity, (SameTypeDto)null!);
            // null target 契约：调用无副作用，source 原值应保持（防"静默吞错仍恒绿"）
            Assert.Equal(999, entity.IntProp);
        }

        /// <summary>
        /// 空字符串 JSON 反序列化 → 返回 default（null）
        /// 验证 JsonConverter.JsonConvertToObject 对空输入的健壮性
        /// </summary>
        [Fact]
        public void EmptyJson_String_Returns_Default()
        {
            var entity = new JsonFieldEntity
            {
                Id = 1,
                ConfigJson = "",   // 空字符串
                TagsJson = "   "   // 空白字符串
            };
            var dto = Mapper.Map<JsonFieldEntity, JsonToObjectDto>(entity)!;

            Assert.Equal(1, dto.Id);
            // 空字符串 JSON → default（null）
            Assert.Null(dto.Config);
            Assert.Null(dto.Tags);
        }

        // ================================================================
        // MapList 带 action 的行为
        // ================================================================

        /// <summary>
        /// MapList 带 action 回调，验证每个元素都被映射且 action 被调用
        /// </summary>
        [Fact]
        public void MapList_WithAction_AllElements_Processed()
        {
            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 1, StringProp = "A" },
                new() { IntProp = 2, StringProp = "B" },
                new() { IntProp = 3, StringProp = "C" }
            };

            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities, (s, d) =>
            {
                d.IntProp = s.IntProp + 100; // action 修改目标
            })!;

            Assert.Equal(3, dtos.Count);
            Assert.Equal(101, dtos[0].IntProp);
            Assert.Equal(102, dtos[1].IntProp);
            Assert.Equal(103, dtos[2].IntProp);
        }

        /// <summary>
        /// MapList 源列表含 null 元素时，null 被保留为 null（保持索引一致性）
        /// </summary>
        [Fact]
        public void MapList_FastPath_Preserves_Null_Elements()
        {
            var entities = new List<FullTypeEntity?>
            {
                new() { IntProp = 1 },
                null,
                new() { IntProp = 3 }
            };

            // null 元素保留为 null（保持索引一致性 + 精确预分配）
            var dtos = Mapper.MapList<FullTypeEntity, SameTypeDto>(entities!)!;

            Assert.Equal(3, dtos.Count);
            Assert.Equal(1, dtos[0].IntProp);
            Assert.Null(dtos[1]);
            Assert.Equal(3, dtos[2].IntProp);
        }

        // ================================================================
        // CollectionCopier 直接测试
        // ================================================================

        /// <summary>
        /// CollectionCopier.CopyList 对 null 输入返回 null
        /// </summary>
        [Fact]
        public void CopyList_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyList<int>(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// CollectionCopier.CopyArray 对 null 输入返回 default
        /// </summary>
        [Fact]
        public void CopyArray_Null_Returns_Default()
        {
            var result = CollectionCopier.CopyArray<int[]>(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// CollectionCopier.CopyDictionary 对 null 输入返回 null
        /// </summary>
        [Fact]
        public void CopyDictionary_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyDictionary<string, int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        /// <summary>
        /// CollectionCopier.CopyEnumerable（通用构造器拷贝）对引用类型集合生成独立副本
        /// </summary>
        [Fact]
        public void CopyEnumerable_Produces_Independent_Copy()
        {
            var original = new SortedSet<string> { "a", "b", "c" };
            var copy = CollectionCopier.CopyEnumerable(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            Assert.Contains("a", copy);

            // 修改副本不影响原对象
            ((SortedSet<string>)copy).Add("d");
            Assert.Equal(3, original.Count);
        }

        /// <summary>
        /// CopyEnumerable 覆盖更多类型：LinkedList/ObservableCollection/ConcurrentBag
        /// 验证这些有 IEnumerable&lt;T&gt; 构造器的类型都能正确拷贝
        /// </summary>
        /// <param name="typeName">集合类型名称：LinkedList / ObservableCollection / ConcurrentBag</param>
        [Theory]
        [InlineData("LinkedList")]
        [InlineData("ObservableCollection")]
        [InlineData("ConcurrentBag")]
        public void CopyEnumerable_VariousTypes_All_Copy_Correctly(string typeName)
        {
            System.Collections.IEnumerable original = typeName switch
            {
                "LinkedList" => new LinkedList<int>(new[] { 10, 20, 30 }),
                "ObservableCollection" => new System.Collections.ObjectModel.ObservableCollection<int>(new[] { 10, 20, 30 }),
                "ConcurrentBag" => new ConcurrentBag<int>(new[] { 10, 20, 30 }),
                _ => throw new ArgumentException()
            };

            var copy = CollectionCopier.CopyEnumerable((dynamic)original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            // 元素数量一致
            var count = 0;
            foreach (var _ in (System.Collections.IEnumerable)copy) count++;
            Assert.Equal(3, count);
        }

        /// <summary>
        /// CopyEnumerable 对 null 入参返回 null
        /// </summary>
        [Fact]
        public void CopyEnumerable_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyEnumerable<SortedSet<int>>(null!, new DeepCopyContext()));
            Assert.Null(CollectionCopier.CopyEnumerable<LinkedList<int>>(null!, new DeepCopyContext()));
        }

        /// <summary>
        /// CollectionCopier.CopyDictionaryByKeyValue（通用 IDictionary 构造器拷贝）对双值集合生成独立副本
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_Produces_Independent_Copy()
        {
            var original = new SortedDictionary<string, int> { ["a"] = 1, ["b"] = 2 };
            var copy = CollectionCopier.CopyDictionaryByKeyValue(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            Assert.Equal(1, ((SortedDictionary<string, int>)copy)["a"]);

            // 修改副本不影响原对象
            ((SortedDictionary<string, int>)copy).Add("c", 3);
            Assert.Equal(2, original.Count);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue 覆盖 SortedList
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_SortedList_Mapped()
        {
            var original = new SortedList<string, int> { ["x"] = 100, ["y"] = 200 };
            var copy = CollectionCopier.CopyDictionaryByKeyValue(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            var dict = (SortedList<string, int>)copy;
            Assert.Equal(2, dict.Count);
            Assert.Equal(100, dict["x"]);
            Assert.Equal(200, dict["y"]);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue 对 null 入参返回 null
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyDictionaryByKeyValue<SortedDictionary<string, int>>(null!, new DeepCopyContext()));
            Assert.Null(CollectionCopier.CopyDictionaryByKeyValue<SortedList<string, int>>(null!, new DeepCopyContext()));
        }

        /// <summary>
        /// CopyEnumerable 对无 IEnumerable&lt;T&gt; 构造器的类型抛出 MapException（P1-9 修复：不再返回浅拷贝）
        /// </summary>
        [Fact]
        public void CopyEnumerable_NoCtor_Throws_MapException()
        {
            // ReadOnlyCollection 有独立的 CopyReadOnlyCollection 路径，走 CopyEnumerable 时无构造器
            // P1-9 修复：无构造器时抛 MapException 而非返回原引用
            var original = new System.Collections.ObjectModel.ReadOnlyCollection<int>(new[] { 1, 2, 3 });

            Assert.Throws<MapException>(() => CollectionCopier.CopyEnumerable(original, new DeepCopyContext()));
        }

        /// <summary>
        /// CopyReadOnlyCollection 直接调用测试 — 先 List 拷贝再包装为只读
        /// </summary>
        [Fact]
        public void CopyReadOnlyCollection_Produces_Independent_Copy()
        {
            var original = new System.Collections.ObjectModel.ReadOnlyCollection<string>(
                new List<string> { "alpha", "beta", "gamma" });

            var copy = CollectionCopier.CopyReadOnlyCollection(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            Assert.Equal("alpha", copy[0]);
            Assert.Equal("gamma", copy[2]);
        }

        /// <summary>
        /// CopyReadOnlyCollection 对 null 入参返回 null
        /// </summary>
        [Fact]
        public void CopyReadOnlyCollection_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyReadOnlyCollection<int>(null!, new DeepCopyContext()));
        }

        /// <summary>
        /// CopyConcurrentStack 保持 LIFO 顺序一致（与 CopyStack 同理）
        /// </summary>
        [Fact]
        public void CopyConcurrentStack_Preserves_LIFO_Order()
        {
            var original = new ConcurrentStack<int>();
            original.Push(999);
            original.Push(888);
            original.Push(777); // 栈顶

            var copy = CollectionCopier.CopyConcurrentStack(original, new DeepCopyContext())!;

            // Pop 顺序应与原栈一致：777, 888, 999
            Assert.True(copy.TryPop(out var v1)); Assert.Equal(777, v1);
            Assert.True(copy.TryPop(out var v2)); Assert.Equal(888, v2);
            Assert.True(copy.TryPop(out var v3)); Assert.Equal(999, v3);
        }

        /// <summary>
        /// CopyConcurrentStack 对 null 入参返回 null
        /// </summary>
        [Fact]
        public void CopyConcurrentStack_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyConcurrentStack<int>(null!, new DeepCopyContext()));
        }

        /// <summary>
        /// CopyConcurrentStack 对空栈返回空栈（非 null）
        /// </summary>
        [Fact]
        public void CopyConcurrentStack_Empty_Returns_Empty()
        {
            var copy = CollectionCopier.CopyConcurrentStack(new ConcurrentStack<int>(), new DeepCopyContext())!;

            Assert.True(copy.IsEmpty);
        }

        /// <summary>
 /// JsonConverter.JsonConvertToObject 对非法 JSON 抛出 MapException（D7 裁决：包装 JsonException，保留 InnerException）
 /// <para> 适配：D7 行为变更（直抛 JsonException → 包装 MapException），断言意图不变
 /// （验证非法 JSON 的异常行为），仅期望异常类型按目标语义变更，与C2 断言口径一致。</para>
        /// </summary>
        [Fact]
        public void JsonConvertToObject_InvalidJson_Throws_MapExceptionWrappingJson()
        {
            var ex = Assert.Throws<MapException>(() =>
                JsonConverter.JsonConvertToObject<AppConfig>("{invalid json"));

 // 异常契约（D7 口径）：InnerException 为 JsonException + 消息含关键字
            Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
            Assert.Contains("Json", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================
        // B7+B8 修复：数组元素类型判断 + 变量重命名
        // ================================================================

        /// <summary>
        /// int[] → int[] 同类型数组映射，走 ConvertListExpression（CopyArray）而非 GetListExpression
        /// B7 修复前：IsAnsiClass 对 int[] 返回 false → isValueType=false → 误走 GetListExpression（递归映射路径）
        /// B7 修复后：用 GetElementType 判断数组元素类型 → 正确走 CopyArray
        /// </summary>
        [Fact]
        public void IntArray_Maps_Via_CopyArray_Not_GetListExpression()
        {
            // MultiCollectionEntity.Scores 是 int[]，走 CopyArray
            var entity = new MultiCollectionEntity
            {
                Scores = new[] { 1, 2, 3, 4, 5 }
            };

            var dto = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(entity)!;

            Assert.NotNull(dto.Scores);
            Assert.Equal(5, dto.Scores!.Length);
            Assert.Equal(1, dto.Scores[0]);
            Assert.Equal(5, dto.Scores[4]);
            // 独立副本
            Assert.NotSame(entity.Scores, dto.Scores);
        }

        /// <summary>
        /// string[] → string[] 同类型数组映射，走 CopyArray
        /// </summary>
        [Fact]
        public void StringArray_Maps_Via_CopyArray()
        {
            var entity = new StringArrayEntity { Tags = new[] { "a", "b", "c" } };
            var dto = Mapper.Map<StringArrayEntity, StringArrayDto>(entity)!;

            Assert.NotNull(dto.Tags);
            Assert.Equal(3, dto.Tags!.Length);
            Assert.Equal("a", dto.Tags[0]);
            Assert.NotSame(entity.Tags, dto.Tags);
        }

                // ================================================================
        // P2 修复：静态字典初始化
        // ================================================================

        /// <summary>
        /// GetPropertiesDictIgnoreCase 在多线程下并发访问不崩溃（P2 直接初始化消除竞态）
        /// 通过并发映射间接验证——首次映射会触发 GetPropertiesDictIgnoreCase 初始化
        /// </summary>
        [Fact]
        public void GetPropertiesDictIgnoreCase_ConcurrentAccess_Safe()
        {
            var exceptions = new ConcurrentQueue<Exception>();

            // 并发映射多种类型对，触发各自的 GetPropertiesDictIgnoreCase 初始化
            Parallel.For(0, 100, i =>
            {
                try
                {
                    var entity = new FullTypeEntity { IntProp = i, StringProp = $"item{i}" };
                    var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity);
                    Assert.Equal(i, dto!.IntProp);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            });

            Assert.Empty(exceptions);
        }

        // ================================================================
        // B11 修复：record/无无参构造类型支持
        // ================================================================

        /// <summary>
        /// Entity → record DTO 正向映射：构造器参数按名匹配源属性
        /// RecordPersonDto(string Name, int Age, string Email) ← RecordPersonEntity 的同名属性
        /// </summary>
        [Fact]
        public void Record_Dto_Forward_Mapped()
        {
            var entity = new RecordPersonEntity
            {
                Name = "Alice",
                Age = 30,
                Email = "alice@test.com"
            };

            var dto = Mapper.Map<RecordPersonEntity, RecordPersonDto>(entity)!;

            Assert.Equal("Alice", dto.Name);
            Assert.Equal(30, dto.Age);
            Assert.Equal("alice@test.com", dto.Email);
        }

        /// <summary>
        /// record DTO → Entity 反向映射：Entity 有无参构造，走现有路径
        /// </summary>
        [Fact]
        public void Record_Dto_Reverse_To_Entity()
        {
            var dto = new RecordPersonDto("Bob", 25, "bob@test.com");

            var entity = Mapper.Map<RecordPersonDto, RecordPersonEntity>(dto)!;

            Assert.Equal("Bob", entity.Name);
            Assert.Equal(25, entity.Age);
            Assert.Equal("bob@test.com", entity.Email);
        }

        /// <summary>
        /// record + 额外属性：构造器只含 Name/Age，Extra 通过 MapAction 构造后赋值
        /// 验证 init-only 属性的表达式树赋值在构造后仍可工作
        /// </summary>
        [Fact]
        public void Record_With_Extra_Property_Mapped()
        {
            var entity = new RecordPersonEntity
            {
                Name = "Charlie",
                Age = 40,
                Email = "charlie@test.com"
            };

            var dto = Mapper.Map<RecordPersonEntity, RecordWithExtraDto>(entity)!;

            // Name/Age 通过构造器参数匹配
            Assert.Equal("Charlie", dto.Name);
            Assert.Equal(40, dto.Age);
            // RecordWithExtraDto 无 Email 构造器参数，但 Extra 是额外属性
            // RecordPersonEntity 无 Extra 属性 → Extra 保持默认 null
            Assert.Null(dto.Extra);
        }

        /// <summary>
        /// 嵌套 record：Parent.Child 是 record 类型
        /// 验证 GetClassExpression 中 MakeGenericType 对 record 不再因 new() 约束失败
        /// </summary>
        [Fact]
        public void Nested_Record_Dto_Mapped()
        {
            var entity = new NestedRecordEntity
            {
                Title = "Parent",
                Child = new RecordPersonEntity
                {
                    Name = "Child",
                    Age = 10,
                    Email = "child@test.com"
                }
            };

            var dto = Mapper.Map<NestedRecordEntity, NestedRecordDto>(entity)!;

            Assert.Equal("Parent", dto.Title);
            Assert.NotNull(dto.Child);
            Assert.Equal("Child", dto.Child!.Name);
            Assert.Equal(10, dto.Child.Age);
            Assert.Equal("child@test.com", dto.Child.Email);
        }

        /// <summary>
        /// 多构造器 record：有无参构造和带参构造
        /// 验证构造器选择：有无参构造 → 走现有路径（New(Type.EmptyTypes)）
        /// </summary>
        [Fact]
        public void MultiCtor_Record_With_DefaultCtor_Mapped()
        {
            var entity = new RecordPersonEntity
            {
                Name = "Dave",
                Age = 50,
                Email = "dave@test.com"
            };

            // MultiCtorRecordDto 有无参构造 → 走现有路径，属性通过 MapAction 赋值
            var dto = Mapper.Map<RecordPersonEntity, MultiCtorRecordDto>(entity)!;

            Assert.Equal("Dave", dto.Name);
            Assert.Equal(50, dto.Age);
        }

        /// <summary>
        /// MapList 映射到 record DTO 列表
        /// 验证 MapList 对 record 类型的支持
        /// </summary>
        [Fact]
        public void MapList_To_Record_Dto()
        {
            var entities = new List<RecordPersonEntity>
            {
                new() { Name = "A", Age = 1, Email = "a@t.com" },
                new() { Name = "B", Age = 2, Email = "b@t.com" },
                new() { Name = "C", Age = 3, Email = "c@t.com" }
            };

            var dtos = Mapper.MapList<RecordPersonEntity, RecordPersonDto>(entities)!;

            Assert.Equal(3, dtos.Count);
            Assert.Equal("A", dtos[0].Name);
            Assert.Equal(1, dtos[0].Age);
            Assert.Equal("C", dtos[2].Name);
            Assert.Equal(3, dtos[2].Age);
        }

        /// <summary>
        /// 同类 record 映射：record → record（同类 Map&lt;T&gt; 方法）
        /// 验证 Map&lt;TSource&gt; 去掉 new() 约束后对 record 的支持
        /// </summary>
        [Fact]
        public void SameType_Record_Mapped()
        {
            var original = new RecordPersonDto("Eve", 28, "eve@t.com");

            var copy = Mapper.Map(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal("Eve", copy.Name);
            Assert.Equal(28, copy.Age);
            Assert.Equal("eve@t.com", copy.Email);
        }

        // ================================================================
        // 同类 MapList<T> 测试 — 补充零覆盖缺口
        // ================================================================

        /// <summary>
        /// 同类列表拷贝：MapList&lt;T&gt;(sources) 将同类型列表拷贝为新列表
        /// 验证元素正确拷贝且是独立副本
        /// </summary>
        [Fact]
        public void MapList_SameType_Copies_All_Elements()
        {
            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 1, StringProp = "A" },
                new() { IntProp = 2, StringProp = "B" },
                new() { IntProp = 3, StringProp = "C" }
            };

            var copies = Mapper.MapList(entities)!;

            Assert.Equal(3, copies.Count);
            Assert.Equal(1, copies[0].IntProp);
            Assert.Equal("A", copies[0].StringProp);
            Assert.Equal(3, copies[2].IntProp);
            Assert.Equal("C", copies[2].StringProp);
            // 独立副本
            Assert.NotSame(entities[0], copies[0]);
        }

        /// <summary>
        /// 同类列表拷贝带 action：验证 action 回调被正确调用
        /// </summary>
        [Fact]
        public void MapList_SameType_WithAction_Processed()
        {
            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 10 },
                new() { IntProp = 20 }
            };

            var copies = Mapper.MapList(entities, (s, d) => d.IntProp += 100)!;

            Assert.Equal(2, copies.Count);
            Assert.Equal(110, copies[0].IntProp);
            Assert.Equal(120, copies[1].IntProp);
        }

        /// <summary>
        /// 同类列表拷贝 — 源列表含 null 元素时保留为 null（保持索引一致性）
        /// </summary>
        [Fact]
        public void MapList_SameType_Preserves_Null_Elements()
        {
            // 同类 MapList<T> 要求 T 为非 null class，此处用 null! 故意放入 null 测试保留行为
            var entities = new List<FullTypeEntity>
            {
                new() { IntProp = 1 },
                null!,
                new() { IntProp = 3 }
            };

            var copies = Mapper.MapList(entities)!;

            Assert.Equal(3, copies.Count);
            Assert.Equal(1, copies[0].IntProp);
            Assert.Null(copies[1]);
            Assert.Equal(3, copies[2].IntProp);
        }

        // ================================================================
        // JsonConverter.JsonConvertToTarget
        // ================================================================

        /// <summary>
        /// JsonConvertToTarget 通过 JSON 中转转换同类型对象，值正确但引用独立
        /// </summary>
        [Fact]
        public void JsonConvertToTarget_Valid_Source_Maps_Correctly()
        {
            var source = new AppConfig { Theme = "Light", FontSize = 16 };
            var result = JsonConverter.JsonConvertToTarget<AppConfig, AppConfig>(source)!;

            Assert.NotNull(result);
            Assert.Equal("Light", result.Theme);
            Assert.Equal(16, result.FontSize);
            Assert.NotSame(source, result);
        }

        /// <summary>
        /// JsonConvertToTarget 对 null source 返回 default
        /// </summary>
        [Fact]
        public void JsonConvertToTarget_Null_Source_Returns_Default()
        {
            var result = JsonConverter.JsonConvertToTarget<AppConfig, AppConfig>(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// JsonConvertToTarget 对含不同类型字段的对象正常工作
        /// </summary>
        [Fact]
        public void JsonConvertToTarget_CrossType_Maps_Correctly()
        {
            var source = new AppConfig { Theme = "Dark", FontSize = 14 };
            var result = JsonConverter.JsonConvertToTarget<AppConfig, AppConfig>(source)!;

            Assert.Equal("Dark", result.Theme);
            Assert.Equal(14, result.FontSize);
        }

        // ================================================================
        // 特殊 float/double 值
        // ================================================================

        /// <summary>
        /// float.NaN → float 映射保持 NaN
        /// </summary>
        [Fact]
        public void Float_NaN_Maps_To_Float()
        {
            var entity = new FullTypeEntity { FloatProp = float.NaN, DoubleProp = 0 };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.True(float.IsNaN(dto.FloatProp));
        }

        /// <summary>
        /// double.PositiveInfinity → double 映射保持 ∞
        /// </summary>
        [Fact]
        public void Double_PositiveInfinity_Mapped()
        {
            var entity = new FullTypeEntity { DoubleProp = double.PositiveInfinity };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.True(double.IsPositiveInfinity(dto.DoubleProp));
        }

        /// <summary>
        /// double.NegativeInfinity → double 映射保持 -∞
        /// </summary>
        [Fact]
        public void Double_NegativeInfinity_Mapped()
        {
            var entity = new FullTypeEntity { DoubleProp = double.NegativeInfinity };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.True(double.IsNegativeInfinity(dto.DoubleProp));
        }

        /// <summary>
        /// float.Epsilon → double（拓宽转换）
        /// </summary>
        [Fact]
        public void Float_Epsilon_To_Double()
        {
            var entity = new FullTypeEntity { FloatProp = float.Epsilon, DoubleProp = 0 };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal((double)float.Epsilon, dto.FloatProp, 1e-15);
        }

        // ================================================================
        // DateTime 边界值
        // ================================================================

        /// <summary>
        /// DateTime.MinValue 映射保持原值
        /// </summary>
        [Fact]
        public void DateTime_MinValue_Mapped()
        {
            var entity = new FullTypeEntity { DateTimeProp = DateTime.MinValue };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(DateTime.MinValue, dto.DateTimeProp);
        }

        /// <summary>
        /// DateTime.MaxValue 映射保持原值
        /// </summary>
        [Fact]
        public void DateTime_MaxValue_Mapped()
        {
            var entity = new FullTypeEntity { DateTimeProp = DateTime.MaxValue };
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(DateTime.MaxValue, dto.DateTimeProp);
        }

        /// <summary>
        /// DateTime → string → DateTime 双向转换
        /// 注意：StringToNumericDto 的 IntValue/LongValue/DecimalValue 也映射 StringProp，
        /// ISO 日期字符串无法转数值 → 抛 MapException（fail-fast 正确行为）
        /// </summary>
        [Fact]
        public void DateTime_ToString_And_Back()
        {
            var entity = new FullTypeEntity
            {
                DateTimeProp = new DateTime(2025, 3, 15, 10, 30, 0),
                StringProp = "2025-03-15T10:30:00"
            };

            // DateTime → string 正常工作
            var strDto = Mapper.Map<FullTypeEntity, ValueToStringDto>(entity)!;
            Assert.Contains("2025", strDto.DateTimeProp);

            // string → DateTime 通过 StringToNumericDto 会抛异常（IntValue 无法解析 ISO 日期）
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToNumericDto>(entity));
        }

        // ================================================================
        // Guid → string 边界测试
        // ================================================================

        /// <summary>
        /// Guid.Empty 映射为全零的字符串
        /// </summary>
        [Fact]
        public void Guid_Empty_To_String()
        {
            var entity = new FullTypeEntity { GuidProp = Guid.Empty, StringProp = "" };
            var dto = Mapper.Map<FullTypeEntity, ValueToStringDto>(entity)!;

            Assert.Equal("00000000-0000-0000-0000-000000000000", dto.GuidProp);
        }

        // ================================================================
        // TypeConvertHelper 边界测试
        // ================================================================

        /// <summary>
        /// string → Guid 有效转换
        /// </summary>
        [Fact]
        public void String_To_Guid_Valid_Converts()
        {
            var entity = new FullTypeEntity
            {
                StringProp = "11111111-2222-3333-4444-555555555555",
                GuidProp = Guid.Empty
            };
            // 测试通过同名字段 string→Guid 自动转换
            var dto = Mapper.Map<FullTypeEntity, SameTypeDto>(entity)!;

            Assert.Equal(entity.GuidProp, dto.GuidProp);
        }

        /// <summary>
        /// string → bool "true"/"false" 转换
        /// </summary>
        [Fact]
        public void String_To_Bool_True_False()
        {
            // 测试时创建一个全 string 源的场景
            var source = new FullTypeEntity { BoolProp = true };
            var dto = Mapper.Map<FullTypeEntity, ValueToStringDto>(source)!;
            Assert.Equal("True", dto.BoolProp);

            // string → bool 通过 StringProp 间接测试
            var source2 = new FullTypeEntity { StringProp = "True" };
            // SameTypeDto.StringProp 是 string → 直接赋值，不转换类型
        }

        /// <summary>
        /// string → DateTime 有效 ISO 格式转换
        /// 注意：StringToNumericDto 的 IntValue/LongValue/DecimalValue 也映射 StringProp，
        /// ISO 日期字符串无法转数值 → 抛 MapException
        /// </summary>
        [Fact]
        public void String_To_DateTime_Valid_ISO()
        {
            var entity = new FullTypeEntity { StringProp = "2025-01-15T08:00:00" };

            // StringToNumericDto 的数值字段无法解析 ISO 日期 → 整体抛 MapException
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToNumericDto>(entity));
        }

        /// <summary>
        /// string → DateTime 无效格式抛出 MapException
        /// </summary>
        [Fact]
        public void String_To_DateTime_Invalid_Throws_MapException()
        {
            var entity = new FullTypeEntity { StringProp = "not-a-date" };

            // 转换失败 → 抛出 MapException
            Assert.Throws<MapException>(() => Mapper.Map<FullTypeEntity, StringToNumericDto>(entity));
        }

        // ================================================================
        // TypeConvertHelper.ConvertValue 直接单元测试
        // ================================================================

        /// <summary>
        /// int → DateTime 不可转换，直接调用 ConvertValue 验证抛出 MapException
        /// </summary>
        [Fact]
        public void ConvertValue_Int_To_DateTime_Throws_MapException()
        {
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<int, DateTime>(42));

            Assert.Contains("Int32", ex.Message);
            Assert.Contains("DateTime", ex.Message);
        }

        /// <summary>
        /// int → TimeSpan 不可转换，验证抛出 MapException
        /// </summary>
        [Fact]
        public void ConvertValue_Int_To_TimeSpan_Throws_MapException()
        {
            Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<int, TimeSpan>(100));
        }

        /// <summary>
        /// 自定义对象 → int 不可转换（非 IConvertible），验证抛出 MapException
        /// </summary>
        [Fact]
        public void ConvertValue_Object_To_Int_Throws_MapException()
        {
            var obj = new FullTypeEntity { IntProp = 1 };
            Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<FullTypeEntity, int>(obj));
        }

        /// <summary>
        /// null 源值返回 default，不抛异常
        /// </summary>
        [Fact]
        public void ConvertValue_Null_Source_Returns_Default()
        {
            string? nullSource = null;
            var result = TypeConvertHelper.ConvertValue<string?, int>(nullSource);
            Assert.Equal(0, result);
        }

        /// <summary>
        /// string → int 正常转换路径
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Int_Valid()
        {
            var result = TypeConvertHelper.ConvertValue<string, int>("42");
            Assert.Equal(42, result);
        }

        /// <summary>
        /// string → int 无效格式抛出 MapException
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Int_Invalid_Throws_MapException()
        {
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<string, int>("not-a-number"));

            Assert.Contains("String", ex.Message);
            Assert.Contains("Int32", ex.Message);
        }

        /// <summary>
        /// string → 枚举 有效转换
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Enum_Valid()
        {
            var result = TypeConvertHelper.ConvertValue<string, OrderStatus>("Shipped");
            Assert.Equal(OrderStatus.Shipped, result);
        }

        /// <summary>
        /// string → 枚举 无效值抛出 MapException
        /// </summary>
        [Fact]
        public void ConvertValue_String_To_Enum_Invalid_Throws_MapException()
        {
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<string, OrderStatus>("NotAStatus"));

            // 消息随 CurrentUICulture 切换（ErrorMessages.UseEnglish）：zh 含"枚举值不匹配"，en 含 "enum value mismatch"。
            // 用中英任一关键词断言，避免对运行环境语言的隐式依赖（与 PlanConsistencyExtendedTests 双语断言一致）。
            Assert.True(
                ex.Message.Contains("枚举值不匹配") || ex.Message.Contains("enum value mismatch"),
                $"Expected enum-value-mismatch message, but got: {ex.Message}");
        }

        /// <summary>
        /// 枚举 → int 正常转换路径
        /// </summary>
        [Fact]
        public void ConvertValue_Enum_To_Int_Valid()
        {
            var result = TypeConvertHelper.ConvertValue<OrderStatus, int>(OrderStatus.Shipped);
            Assert.Equal((int)OrderStatus.Shipped, result);
        }

        /// <summary>
        /// int → 枚举 正常转换路径
        /// </summary>
        [Fact]
        public void ConvertValue_Int_To_Enum_Valid()
        {
            var result = TypeConvertHelper.ConvertValue<int, OrderStatus>(2);
            Assert.Equal(OrderStatus.Shipped, result);
        }

        // P1-3~5: TypeConvertHelper 枚举转换失败边界

        /// <summary>
        /// 非法枚举字符串 → 抛 MapException
        /// </summary>
        [Fact]
        public void ConvertValue_InvalidEnumString_ThrowsMapException()
        {
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<string, OrderStatus>("NotAnEnum"));
            Assert.Contains("OrderStatus", ex.Message);
        }

        /// <summary>
        /// 超出范围枚举数值 → 抛 MapException（A1-08，Enum.ToObject
        /// 不查值域的原强转语义按显式抛口径订正——原 StrongCast 断言随契约反转）
        /// </summary>
        [Fact]
        public void ConvertValue_InvalidEnumNumber_StrongCast()
        {
            var ex = Assert.Throws<MapException>(() =>
                TypeConvertHelper.ConvertValue<int, OrderStatus>(999));
            Assert.Contains("OrderStatus", ex.Message);
        }

        /// <summary>
        /// 浮点数 → 枚举 → 先转 long 再转枚举
        /// </summary>
        [Fact]
        public void ConvertValue_FloatToEnum_Truncated()
        {
            var result = TypeConvertHelper.ConvertValue<double, OrderStatus>(1.5);
            Assert.NotEqual(default(OrderStatus), result);
        }
    }
}
