using Xunit;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// CollectionCopier 缺失方法覆盖测试
    /// </summary>
    public class CollectionCopierTests
    {
        // ================================================================
        // CopyConcurrentDictionary
        // ================================================================

        [Fact]
        public void CopyConcurrentDictionary_Copies_All_Entries()
        {
            var original = new ConcurrentDictionary<string, int>();
            original.TryAdd("a", 1);
            original.TryAdd("b", 2);
            original.TryAdd("c", 3);

            var copy = CollectionCopier.CopyConcurrentDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            Assert.Equal(1, copy["a"]);
            Assert.Equal(2, copy["b"]);
            Assert.Equal(3, copy["c"]);
        }

        [Fact]
        public void CopyConcurrentDictionary_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyConcurrentDictionary<string, int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        [Fact]
        public void CopyConcurrentDictionary_Empty_Returns_Empty()
        {
            var copy = CollectionCopier.CopyConcurrentDictionary(
                new ConcurrentDictionary<string, int>(), new DeepCopyContext())!;
            Assert.Empty(copy);
        }

        [Fact]
        public void CopyConcurrentDictionary_Value_Independent()
        {
            var person1 = new SimpleRef { Name = "Alice" };
            var person2 = new SimpleRef { Name = "Bob" };
            var original = new ConcurrentDictionary<int, SimpleRef>();
            original.TryAdd(1, person1);
            original.TryAdd(2, person2);

            var copy = CollectionCopier.CopyConcurrentDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original[1], copy[1]);
            Assert.Equal("Alice", copy[1].Name);
        }

        // ================================================================
        // CopyConcurrentQueue
        // ================================================================

        [Fact]
        public void CopyConcurrentQueue_Copies_All_Items_FIFO()
        {
            var original = new ConcurrentQueue<int>();
            original.Enqueue(10);
            original.Enqueue(20);
            original.Enqueue(30);

            var copy = CollectionCopier.CopyConcurrentQueue(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.True(copy.TryDequeue(out var v1)); Assert.Equal(10, v1);
            Assert.True(copy.TryDequeue(out var v2)); Assert.Equal(20, v2);
            Assert.True(copy.TryDequeue(out var v3)); Assert.Equal(30, v3);
            Assert.False(copy.TryDequeue(out _));
        }

        [Fact]
        public void CopyConcurrentQueue_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyConcurrentQueue<int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        [Fact]
        public void CopyConcurrentQueue_Empty_Returns_Empty()
        {
            var copy = CollectionCopier.CopyConcurrentQueue(
                new ConcurrentQueue<int>(), new DeepCopyContext())!;
            Assert.True(copy.IsEmpty);
        }

        [Fact]
        public void CopyConcurrentQueue_Reference_Type_DeepCopy()
        {
            var item = new SimpleRef { Name = "original" };
            var original = new ConcurrentQueue<SimpleRef>();
            original.Enqueue(item);

            var copy = CollectionCopier.CopyConcurrentQueue(original, new DeepCopyContext())!;

            Assert.True(copy.TryDequeue(out var copied));
            Assert.NotSame(item, copied);
            Assert.Equal("original", copied!.Name);
        }

        // ================================================================
        // ConvertConcurrentDictionaryFrom
        // ================================================================

        [Fact]
        public void ConvertConcurrentDictionaryFrom_Converts_IDictionary()
        {
            var source = new Dictionary<string, int> { ["x"] = 100, ["y"] = 200 };

            var result = CollectionCopier.ConvertConcurrentDictionaryFrom<string, int>(source);

            Assert.Equal(2, result.Count);
            Assert.Equal(100, result["x"]);
            Assert.Equal(200, result["y"]);
        }

        [Fact]
        public void ConvertConcurrentDictionaryFrom_Null_Returns_Empty()
        {
            var result = CollectionCopier.ConvertConcurrentDictionaryFrom<string, int>(null!);
            Assert.Empty(result);
        }

        [Fact]
        public void ConvertConcurrentDictionaryFrom_Empty_Returns_Empty()
        {
            var result = CollectionCopier.ConvertConcurrentDictionaryFrom<string, int>(
                new Dictionary<string, int>());
            Assert.Empty(result);
        }

        // ================================================================
        // DeepCopyList
        // ================================================================

        [Fact]
        public void DeepCopyList_Reference_Items_Have_Different_References()
        {
            var child1 = new DeepCopyTestChild { Value = 10 };
            var child2 = new DeepCopyTestChild { Value = 20 };
            var original = new List<DeepCopyTestChild> { child1, child2 };

            var copy = CollectionCopier.DeepCopyList(original, new DeepCopyContext())!;

            Assert.Equal(2, copy.Count);
            Assert.Equal(10, copy[0].Value);
            Assert.Equal(20, copy[1].Value);
            Assert.NotSame(original[0], copy[0]);
            Assert.NotSame(original[1], copy[1]);
        }

        [Fact]
        public void DeepCopyList_ValueType_Uses_ToList()
        {
            var original = new List<int> { 1, 2, 3 };
            var copy = CollectionCopier.DeepCopyList(original, new DeepCopyContext())!;

            Assert.Equal(3, copy.Count);
            Assert.Equal(1, copy[0]);
            Assert.NotSame(original, copy);
        }

        [Fact]
        public void DeepCopyList_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.DeepCopyList<int>(null!, new DeepCopyContext()));
        }

        // ================================================================
        // DeepCopyArray
        // ================================================================

        [Fact]
        public void DeepCopyArray_Reference_Items_Have_Different_References()
        {
            var child1 = new DeepCopyTestChild { Value = 100 };
            var child2 = new DeepCopyTestChild { Value = 200 };
            var original = new[] { child1, child2 };

            var copy = CollectionCopier.DeepCopyArray(original, new DeepCopyContext())!;

            Assert.Equal(2, copy.Length);
            Assert.Equal(100, copy[0].Value);
            Assert.Equal(200, copy[1].Value);
            Assert.NotSame(original[0], copy[0]);
            Assert.NotSame(original[1], copy[1]);
        }

        [Fact]
        public void DeepCopyArray_ValueType_Clone()
        {
            var original = new[] { 5, 10, 15 };
            var copy = CollectionCopier.DeepCopyArray(original, new DeepCopyContext())!;

            Assert.Equal(3, copy.Length);
            Assert.Equal(5, copy[0]);
            Assert.NotSame(original, copy);
        }

        [Fact]
        public void DeepCopyArray_Null_Returns_Default()
        {
            Assert.Null(CollectionCopier.DeepCopyArray<int[]>(null!, new DeepCopyContext()));
        }

        // ================================================================
        // CollectionCopier.DeepCopy (通用入口)
        // ================================================================

        [Fact]
        public void DeepCopy_Class_Returns_Independent_Copy()
        {
            var original = new DeepCopyTestChild { Value = 42 };
            var copy = CollectionCopier.DeepCopy(original, new DeepCopyContext())!;

            Assert.Equal(42, copy.Value);
            Assert.NotSame(original, copy);
        }

        [Fact]
        public void DeepCopy_ValueType_Returns_Same()
        {
            var result = CollectionCopier.DeepCopy(42, new DeepCopyContext());
            Assert.Equal(42, result);
        }

        [Fact]
        public void DeepCopy_String_Returns_Same_Reference()
        {
            var original = "hello world";
            var copy = CollectionCopier.DeepCopy(original, new DeepCopyContext());
            Assert.Same(original, copy);
        }

        [Fact]
        public void DeepCopy_Null_Returns_Default()
        {
            var result = CollectionCopier.DeepCopy<string>(default!, new DeepCopyContext());
            Assert.Null(result);
        }

        // ================================================================
        // DeepCopy — value type and string branch coverage (kills L33 mutants)
        // ================================================================

        [Fact]
        public void DeepCopy_Enum_Returns_Same()
        {
            var result = CollectionCopier.DeepCopy(DayOfWeek.Wednesday, new DeepCopyContext());
            Assert.Equal(DayOfWeek.Wednesday, result);
        }

        // ================================================================
        // DeepCopyList — null elements in reference-type list (kills L65 ternary)
        // ================================================================

        [Fact]
        public void DeepCopy_List_With_Null_Elements()
        {
            var child1 = new DeepCopyTestChild { Value = 10 };
            var child2 = new DeepCopyTestChild { Value = 30 };
            var original = new List<DeepCopyTestChild?> { child1, null, child2 };

            var copy = CollectionCopier.DeepCopyList(original, new DeepCopyContext())!;

            Assert.Equal(3, copy.Count);
            // Non-null items are deep-copied (different reference)
            Assert.NotNull(copy[0]);
            Assert.Equal(10, copy[0]!.Value);
            Assert.NotSame(child1, copy[0]);
            // Null item is preserved as null (kills false branch of L65 ternary)
            Assert.Null(copy[1]);
            // Second non-null item also deep-copied
            Assert.NotNull(copy[2]);
            Assert.Equal(30, copy[2]!.Value);
            Assert.NotSame(child2, copy[2]);
        }

        // ================================================================
        // DeepCopyArray — null elements (kills L103 ternary) and string branch (kills L95)
        // ================================================================

        [Fact]
        public void DeepCopy_Array_With_Null_Elements()
        {
            var child1 = new DeepCopyTestChild { Value = 100 };
            var child2 = new DeepCopyTestChild { Value = 200 };
            var original = new DeepCopyTestChild?[] { child1, null, child2 };

            var copy = CollectionCopier.DeepCopyArray(original, new DeepCopyContext())!;

            Assert.Equal(3, copy.Length);
            Assert.NotNull(copy[0]);
            Assert.Equal(100, copy[0]!.Value);
            Assert.NotSame(child1, copy[0]);
            // Null element preserved (kills false branch of L103 ternary)
            Assert.Null(copy[1]);
            Assert.NotNull(copy[2]);
            Assert.Equal(200, copy[2]!.Value);
            Assert.NotSame(child2, copy[2]);
        }

        [Fact]
        public void DeepCopy_Array_String_No_DeepCopy()
        {
            // String arrays should be cloned (not element-wise deep-copied) — kills L95 string branch
            var original = new[] { "alpha", "beta", "gamma" };
            var copy = CollectionCopier.DeepCopyArray(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Length);
            // Strings are reference types but treated as simple — same references
            Assert.Same(original[0], copy[0]);
            Assert.Same(original[1], copy[1]);
            Assert.Same(original[2], copy[2]);
        }

        [Fact]
        public void DeepCopy_Array_ValueType_Independent()
        {
            // Verifies that modifying the copy does not affect the original — kills L95 value type branch
            var original = new[] { 1, 2, 3 };
            var copy = CollectionCopier.DeepCopyArray(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            copy[0] = 999;
            Assert.Equal(1, original[0]); // original unchanged
            Assert.Equal(999, copy[0]);
        }

        // ================================================================
        // CopyDictionary — various key/value type combinations (kills L119, L126, L127)
        // ================================================================

        [Fact]
        public void CopyDictionary_BothValueTypes()
        {
            // Both key and value are "simple" types → fast path (L119 true branch)
            var original = new Dictionary<int, string> { [1] = "one", [2] = "two" };
            var copy = CollectionCopier.CopyDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            Assert.Equal("one", copy[1]);
            Assert.Equal("two", copy[2]);
        }

        [Fact]
        public void CopyDictionary_ComplexKey_SimpleValue()
        {
            // Complex key → deep copy key (kills L126 false branch)
            // Simple value → direct copy (kills L127 true branch)
            // Also kills L119 &&→|| mutant: IsValueType<RefKey>()=false, IsValueType<string>()=true
            //   original (&&): false && true = false → complex path
            //   mutant (||):   false || true = true  → fast path (wrong: key not deep-copied)
            var key1 = new RefKey { Id = 1 };
            var key2 = new RefKey { Id = 2 };
            var original = new Dictionary<RefKey, string> { [key1] = "hello", [key2] = "world" };

            var copy = CollectionCopier.CopyDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            // Keys must be deep-copied (different references, same values)
            var copiedKeys = copy.Keys.ToList();
            var ck1 = copiedKeys.First(k => k.Id == 1);
            var ck2 = copiedKeys.First(k => k.Id == 2);
            Assert.NotSame(key1, ck1);
            Assert.NotSame(key2, ck2);
        }

        [Fact]
        public void CopyDictionary_SimpleKey_ComplexValue()
        {
            // Simple key → direct copy (kills L126 true branch)
            // Complex value → deep copy value (kills L127 false branch)
            // Kills L119 &&→||: IsValueType<string>()=true, IsValueType<SimpleRef>()=false
            //   original (&&): true && false = false → complex path (values deep-copied)
            //   mutant (||):   true || false = true  → fast path (values NOT deep-copied → test fails)
            var val1 = new SimpleRef { Name = "Alice" };
            var val2 = new SimpleRef { Name = "Bob" };
            var original = new Dictionary<string, SimpleRef> { ["a"] = val1, ["b"] = val2 };

            var copy = CollectionCopier.CopyDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            // Values must be deep-copied (different references)
            Assert.NotSame(val1, copy["a"]);
            Assert.NotSame(val2, copy["b"]);
            Assert.Equal("Alice", copy["a"].Name);
            Assert.Equal("Bob", copy["b"].Name);
        }

        [Fact]
        public void CopyDictionary_BothComplex()
        {
            // Both key and value are complex → both deep-copied (kills L126 AND L127 false branches together)
            var key1 = new RefKey { Id = 10 };
            var val1 = new SimpleRef { Name = "Ten" };
            var original = new Dictionary<RefKey, SimpleRef> { [key1] = val1 };

            var copy = CollectionCopier.CopyDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Single(copy);
            var copiedKey = copy.Keys.First();
            Assert.NotSame(key1, copiedKey);
            Assert.Equal(10, copiedKey.Id);
            Assert.NotSame(val1, copy[copiedKey]);
            Assert.Equal("Ten", copy[copiedKey].Name);
        }

        [Fact]
        public void CopyDictionary_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyDictionary<string, int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        // ================================================================
        // CopyConcurrentDictionary — additional branch coverage (kills L143, L148, L149)
        // ================================================================

        [Fact]
        public void CopyConcurrentDictionary_ComplexKey()
        {
            // Complex key → keyComplex=true → key is deep-copied (kills L143 true branch, L148 true branch)
            var key1 = new RefKey { Id = 1 };
            var original = new ConcurrentDictionary<RefKey, string>();
            original.TryAdd(key1, "hello");

            var copy = CollectionCopier.CopyConcurrentDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Single(copy);
            var copiedKey = copy.Keys.First();
            Assert.NotSame(key1, copiedKey); // key was deep-copied
            Assert.Equal(1, copiedKey.Id);
            Assert.Equal("hello", copy[copiedKey]);
        }

        [Fact]
        public void CopyConcurrentDictionary_NullValues()
        {
            // valComplex=true, but some values are null → tests L149 `item.Value != null` branch
            var original = new ConcurrentDictionary<int, SimpleRef>();
            original.TryAdd(1, new SimpleRef { Name = "alive" });
            original.TryAdd(2, null!);

            var copy = CollectionCopier.CopyConcurrentDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            // Non-null value is deep-copied
            Assert.NotNull(copy[1]);
            Assert.NotSame(original[1], copy[1]);
            Assert.Equal("alive", copy[1].Name);
            // Null value is preserved (kills L149 false branch of `item.Value != null`)
            Assert.Null(copy[2]);
        }

        [Fact]
        public void CopyConcurrentDictionary_AllValueTypes()
        {
            // keyComplex=false AND valComplex=false → no deep copy at all (kills L143 false branch, L148 false branch)
            var original = new ConcurrentDictionary<int, int>();
            original.TryAdd(1, 10);
            original.TryAdd(2, 20);

            var copy = CollectionCopier.CopyConcurrentDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            Assert.Equal(10, copy[1]);
            Assert.Equal(20, copy[2]);
        }

        [Fact]
        public void CopyConcurrentDictionary_ComplexValue_SimpleKey()
        {
            // keyComplex=false, valComplex=true → only values deep-copied
            var val1 = new SimpleRef { Name = "X" };
            var original = new ConcurrentDictionary<int, SimpleRef>();
            original.TryAdd(1, val1);

            var copy = CollectionCopier.CopyConcurrentDictionary(original, new DeepCopyContext())!;

            Assert.NotSame(val1, copy[1]);
            Assert.Equal("X", copy[1].Name);
        }

        // ================================================================
        // ConvertDictionary — null source (kills L164 null coalescing)
        // ================================================================

        [Fact]
        public void ConvertDictionary_NullSourceItem_Returns_EmptyDict()
        {
            // null IDictionary → sourceItem?.Count ?? 0 → capacity 0, then returns empty (kills L164)
            var result = CollectionCopier.ConvertDictionary<string, int>(null!);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void ConvertDictionary_ValidSource_ConvertsEntries()
        {
            var source = new Hashtable { ["key1"] = 100, ["key2"] = 200 };

            var result = CollectionCopier.ConvertDictionary<string, int>(source);

            Assert.Equal(2, result.Count);
            Assert.Equal(100, result["key1"]);
            Assert.Equal(200, result["key2"]);
        }

        // ================================================================
        // CopyQueue — null elements (kills Queue null-element ternary)
        // ================================================================

        [Fact]
        public void CopyQueue_With_Null_Elements()
        {
            var item1 = new SimpleRef { Name = "first" };
            var item2 = new SimpleRef { Name = "third" };
            var original = new Queue<SimpleRef?>();
            original.Enqueue(item1);
            original.Enqueue(null);
            original.Enqueue(item2);

            var copy = CollectionCopier.CopyQueue(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            var c1 = copy.Dequeue();
            Assert.NotNull(c1);
            Assert.Equal("first", c1!.Name);
            Assert.NotSame(item1, c1);
            var c2 = copy.Dequeue();
            Assert.Null(c2); // null preserved
            var c3 = copy.Dequeue();
            Assert.NotNull(c3);
            Assert.Equal("third", c3!.Name);
            Assert.NotSame(item2, c3);
        }

        [Fact]
        public void CopyQueue_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyQueue<int>(null!, new DeepCopyContext()));
        }

        // ================================================================
        // CopyStack — preserves Pop order and deep-copies reference types (kills CopyStack logic)
        // ================================================================

        [Fact]
        public void CopyStack_PreservesOrder_ReferenceType()
        {
            var item1 = new SimpleRef { Name = "bottom" };
            var item2 = new SimpleRef { Name = "middle" };
            var item3 = new SimpleRef { Name = "top" };
            var original = new Stack<SimpleRef>();
            original.Push(item1); // bottom
            original.Push(item2);
            original.Push(item3); // top

            var copy = CollectionCopier.CopyStack(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            // Pop order must match original: top, middle, bottom
            var top = copy.Pop();
            Assert.Equal("top", top.Name);
            Assert.NotSame(item3, top); // deep-copied
            var middle = copy.Pop();
            Assert.Equal("middle", middle.Name);
            Assert.NotSame(item2, middle);
            var bottom = copy.Pop();
            Assert.Equal("bottom", bottom.Name);
            Assert.NotSame(item1, bottom);
        }

        [Fact]
        public void CopyStack_PreservesOrder_ValueType()
        {
            var original = new Stack<int>();
            original.Push(1);
            original.Push(2);
            original.Push(3);

            var copy = CollectionCopier.CopyStack(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Pop());
            Assert.Equal(2, copy.Pop());
            Assert.Equal(1, copy.Pop());
        }

        [Fact]
        public void CopyStack_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyStack<int>(null!, new DeepCopyContext()));
        }

        // ================================================================
        // CopyConcurrentStack — preserves Pop order (kills ConcurrentStack reverse logic)
        // ================================================================

        [Fact]
        public void CopyConcurrentStack_PreservesOrder_ReferenceType()
        {
            var item1 = new SimpleRef { Name = "bottom" };
            var item2 = new SimpleRef { Name = "middle" };
            var item3 = new SimpleRef { Name = "top" };
            var original = new ConcurrentStack<SimpleRef>();
            original.Push(item1); // bottom
            original.Push(item2);
            original.Push(item3); // top

            var copy = CollectionCopier.CopyConcurrentStack(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            Assert.True(copy.TryPop(out var top));
            Assert.Equal("top", top!.Name);
            Assert.NotSame(item3, top);
            Assert.True(copy.TryPop(out var middle));
            Assert.Equal("middle", middle!.Name);
            Assert.NotSame(item2, middle);
            Assert.True(copy.TryPop(out var bottom));
            Assert.Equal("bottom", bottom!.Name);
            Assert.NotSame(item1, bottom);
        }

        [Fact]
        public void CopyConcurrentStack_PreservesOrder_ValueType()
        {
            var original = new ConcurrentStack<int>();
            original.Push(10);
            original.Push(20);
            original.Push(30);

            var copy = CollectionCopier.CopyConcurrentStack(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.True(copy.TryPop(out var v1)); Assert.Equal(30, v1);
            Assert.True(copy.TryPop(out var v2)); Assert.Equal(20, v2);
            Assert.True(copy.TryPop(out var v3)); Assert.Equal(10, v3);
        }

        [Fact]
        public void CopyConcurrentStack_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyConcurrentStack<int>(null!, new DeepCopyContext()));
        }

        // ================================================================
        // CopyHashSet — reference type deep copy (kills HashSet null-element ternary)
        // ================================================================

        [Fact]
        public void CopyHashSet_ReferenceType_DeepCopy()
        {
            var item1 = new SimpleRef { Name = "a" };
            var item2 = new SimpleRef { Name = "b" };
            var original = new HashSet<SimpleRef> { item1, item2 };

            var copy = CollectionCopier.CopyHashSet(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            // Original references must NOT be in the copy (all deep-copied)
            Assert.DoesNotContain(item1, copy);
            Assert.DoesNotContain(item2, copy);
            // Copied items have correct values
            var copyList = copy.ToList();
            Assert.Contains(copyList, x => x.Name == "a");
            Assert.Contains(copyList, x => x.Name == "b");
        }

        [Fact]
        public void CopyHashSet_ValueType_DirectCopy()
        {
            var original = new HashSet<int> { 1, 2, 3 };
            var copy = CollectionCopier.CopyHashSet(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            Assert.Contains(1, copy);
            Assert.Contains(2, copy);
            Assert.Contains(3, copy);
        }

        [Fact]
        public void CopyHashSet_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyHashSet<int>(null!, new DeepCopyContext()));
        }

        // ================================================================
        // CopyReadOnlyCollection — reference type deep copy
        // ================================================================

        [Fact]
        public void CopyReadOnlyCollection_ReferenceType_DeepCopy()
        {
            var item1 = new SimpleRef { Name = "x" };
            var item2 = new SimpleRef { Name = "y" };
            var original = new ReadOnlyCollection<SimpleRef>(new List<SimpleRef> { item1, item2 });

            var copy = CollectionCopier.CopyReadOnlyCollection(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            Assert.NotSame(item1, copy[0]);
            Assert.NotSame(item2, copy[1]);
            Assert.Equal("x", copy[0].Name);
            Assert.Equal("y", copy[1].Name);
        }

        [Fact]
        public void CopyReadOnlyCollection_ValueType_DirectCopy()
        {
            var original = new ReadOnlyCollection<int>(new List<int> { 10, 20, 30 });
            var copy = CollectionCopier.CopyReadOnlyCollection(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            Assert.Equal(10, copy[0]);
            Assert.Equal(20, copy[1]);
            Assert.Equal(30, copy[2]);
        }

        [Fact]
        public void CopyReadOnlyCollection_Null_Returns_Null()
        {
            Assert.Null(CollectionCopier.CopyReadOnlyCollection<int>(null!, new DeepCopyContext()));
        }

        // ================================================================
        // CopyList — null input (kills L47 null check)
        // ================================================================

        [Fact]
        public void CopyList_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyList<int>(null!);
            Assert.Null(result);
        }

        [Fact]
        public void CopyList_Copies_All_Items()
        {
            var original = new List<string> { "a", "b", "c" };
            var copy = CollectionCopier.CopyList(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Count);
            Assert.Equal("a", copy[0]);
            Assert.Equal("b", copy[1]);
            Assert.Equal("c", copy[2]);
        }

        // ================================================================
        // CopyArray — null input
        // ================================================================

        [Fact]
        public void CopyArray_Null_Returns_Default()
        {
            var result = CollectionCopier.CopyArray<int[]>(null!);
            Assert.Null(result);
        }

        [Fact]
        public void CopyArray_Clones_Array()
        {
            var original = new[] { 1, 2, 3 };
            var copy = CollectionCopier.CopyArray(original)!;

            Assert.NotSame(original, copy);
            Assert.Equal(3, copy.Length);
            Assert.Equal(1, copy[0]);
        }

        // ================================================================
        // DeepCopyList — additional string type coverage (kills L60 IsValueType<string> branch)
        // ================================================================

        [Fact]
        public void DeepCopyList_String_Uses_ToList()
        {
            var original = new List<string> { "hello", "world" };
            var copy = CollectionCopier.DeepCopyList(original, new DeepCopyContext())!;

            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Count);
            Assert.Same(original[0], copy[0]); // strings share reference (simple type)
        }

        // ================================================================
        // CopyConcurrentQueue — null elements in reference-type queue
        // ================================================================

        [Fact]
        public void CopyConcurrentQueue_With_Null_Elements()
        {
            var original = new ConcurrentQueue<SimpleRef>();
            original.Enqueue(new SimpleRef { Name = "first" });
            original.Enqueue(null!);
            original.Enqueue(new SimpleRef { Name = "third" });

            var copy = CollectionCopier.CopyConcurrentQueue(original, new DeepCopyContext())!;

            Assert.Equal(3, copy.Count);
            Assert.True(copy.TryDequeue(out var c1));
            Assert.NotNull(c1);
            Assert.Equal("first", c1!.Name);
            Assert.True(copy.TryDequeue(out var c2));
            Assert.Null(c2);
            Assert.True(copy.TryDequeue(out var c3));
            Assert.NotNull(c3);
            Assert.Equal("third", c3!.Name);
        }
    }

    // ================================================================
    // 辅助测试类型
    // ================================================================

    public class SimpleRef
    {
        public string Name { get; set; } = "";
    }

    public class DeepCopyTestChild
    {
        public int Value { get; set; }
    }

    public class RefKey
    {
        public int Id { get; set; }
        public override bool Equals(object? o) => o is RefKey r && r.Id == Id;
        public override int GetHashCode() => Id;
    }
}
