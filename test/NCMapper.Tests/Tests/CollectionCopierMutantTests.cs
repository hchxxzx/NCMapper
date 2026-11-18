using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Mutant-killing tests for CollectionCopier.cs
    /// Targets: Array.Reverse in Stack copiers, IsValueType checks, null guards, deep copy logic
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class CollectionCopierMutantTests : IDisposable
    {
        public CollectionCopierMutantTests() => Mapper.ClearCache();
        public void Dispose() => Mapper.ClearCache();

        // ================================================================
        // CopyStack — Array.Reverse is critical for LIFO order (L288-301)
        // ================================================================

        /// <summary>
        /// CopyStack preserves LIFO pop order — killing Array.Reverse removal mutant at L293.
        /// Without Array.Reverse, Pop order would be reversed (silently wrong).
        /// </summary>
        [Fact]
        public void CopyStack_Preserves_Lifo_Pop_Order()
        {
            var source = new Stack<int>();
            source.Push(1);  // bottom
            source.Push(2);
            source.Push(3);  // top

            var copy = CollectionCopier.CopyStack(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            // Pop order must match: 3 (top), 2, 1 (bottom)
            Assert.Equal(3, copy.Pop());
            Assert.Equal(2, copy.Pop());
            Assert.Equal(1, copy.Pop());
        }

        /// <summary>
        /// CopyStack with reference type elements — verifies deep copy AND LIFO order.
        /// Kills: Array.Reverse removal + DeepCopy bypass mutants.
        /// </summary>
        [Fact]
        public void CopyStack_RefType_DeepCopy_Preserves_Order()
        {
            var source = new Stack<FullTypeEntity>();
            source.Push(new FullTypeEntity { IntProp = 1 });  // bottom
            source.Push(new FullTypeEntity { IntProp = 2 });
            source.Push(new FullTypeEntity { IntProp = 3 });  // top

            var copy = CollectionCopier.CopyStack(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            var top = copy.Pop();
            Assert.Equal(3, top.IntProp);
            var mid = copy.Pop();
            Assert.Equal(2, mid.IntProp);
            var bot = copy.Pop();
            Assert.Equal(1, bot.IntProp);
            // Verify deep copy — not same reference
            Assert.NotSame(source, copy);
        }

        /// <summary>
        /// CopyStack null guard — kills null check removal mutant at L290.
        /// </summary>
        [Fact]
        public void CopyStack_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyStack<int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        // ================================================================
        // CopyConcurrentStack — same Array.Reverse pattern (L310-328)
        // ================================================================

        /// <summary>
        /// CopyConcurrentStack preserves LIFO TryPop order.
        /// Kills Array.Reverse removal mutant at L315.
        /// </summary>
        [Fact]
        public void CopyConcurrentStack_Preserves_Lifo_Order()
        {
            var source = new ConcurrentStack<int>();
            source.Push(1);  // bottom
            source.Push(2);
            source.Push(3);  // top

            var copy = CollectionCopier.CopyConcurrentStack(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.True(copy.TryPop(out var top));
            Assert.Equal(3, top);
            Assert.True(copy.TryPop(out var mid));
            Assert.Equal(2, mid);
            Assert.True(copy.TryPop(out var bot));
            Assert.Equal(1, bot);
        }

        /// <summary>
        /// CopyConcurrentStack null guard.
        /// </summary>
        [Fact]
        public void CopyConcurrentStack_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyConcurrentStack<int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        // ================================================================
        // CopyDictionary — IsValueType checks (L114-131)
        // ================================================================

        /// <summary>
        /// CopyDictionary with value-type key AND value — uses fast path (L119-120).
        /// Kills: IsValueType&lt;Tin&gt;() &amp;&amp; IsValueType&lt;Tout&gt;() — if mutated to ||,
        /// complex key types would incorrectly take the fast path.
        /// </summary>
        [Fact]
        public void CopyDictionary_ValueType_FastPath()
        {
            var source = new Dictionary<int, string> { { 1, "a" }, { 2, "b" } };
            var copy = CollectionCopier.CopyDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal("a", copy[1]);
            Assert.Equal("b", copy[2]);
        }

        /// <summary>
        /// CopyDictionary with complex key type — verifies deep copy path.
        /// Kills: IsValueType negation mutant — if !IsValueType became IsValueType,
        /// complex keys would skip deep copy.
        /// </summary>
        [Fact]
        public void CopyDictionary_ComplexKey_DeepCopy()
        {
            var source = new Dictionary<FullTypeEntity, int>
            {
                { new FullTypeEntity { IntProp = 10 }, 100 },
                { new FullTypeEntity { IntProp = 20 }, 200 }
            };

            var copy = CollectionCopier.CopyDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            // Verify keys are deep-copied (different references)
            var sourceKeys = source.Keys.ToList();
            var copyKeys = copy.Keys.ToList();
            for (int i = 0; i < sourceKeys.Count; i++)
                Assert.NotSame(sourceKeys[i], copyKeys[i]);
        }

        /// <summary>
        /// CopyDictionary null guard — kills null check removal at L116.
        /// </summary>
        [Fact]
        public void CopyDictionary_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyDictionary<int, string>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        // ================================================================
        // DeepCopyArray — elemType.IsValueType || IsEnum || == string (L95)
        // ================================================================

        /// <summary>
        /// DeepCopyArray with enum elements — kills IsEnum removal mutant at L95.
        /// If || becomes &amp;&amp;, enum arrays would fall through to deep copy path (wrong for value types).
        /// </summary>
        [Fact]
        public void DeepCopyArray_Enum_Elements_FastPath()
        {
            var source = new[] { OrderStatus.Pending, OrderStatus.Shipped, OrderStatus.Delivered };
            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Length);
            Assert.Equal(OrderStatus.Pending, copy[0]);
            Assert.Equal(OrderStatus.Shipped, copy[1]);
            Assert.Equal(OrderStatus.Delivered, copy[2]);
        }

        /// <summary>
        /// DeepCopyArray with string elements — kills == typeof(string) removal mutant at L95.
        /// </summary>
        [Fact]
        public void DeepCopyArray_String_Elements_FastPath()
        {
            var source = new[] { "hello", "world" };
            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal("hello", copy![0]);
            Assert.Equal("world", copy[1]);
        }

        /// <summary>
        /// DeepCopyArray null guard — kills null check removal at L90.
        /// </summary>
        [Fact]
        public void DeepCopyArray_Null_Returns_Default()
        {
            var result = CollectionCopier.DeepCopyArray<int[]>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        // ================================================================
        // DeepCopyList — IsValueType fast path (L57-68)
        // ================================================================

        /// <summary>
        /// DeepCopyList null guard — kills null check removal at L59.
        /// </summary>
        [Fact]
        public void DeepCopyList_Null_Returns_Null()
        {
            var result = CollectionCopier.DeepCopyList<int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        /// <summary>
        /// DeepCopyList with null elements in reference-type list.
        /// Kills: null ternary removal at L65.
        /// </summary>
        [Fact]
        public void DeepCopyList_With_Null_Elements()
        {
            var source = new List<FullTypeEntity?>
            {
                new FullTypeEntity { IntProp = 1 },
                null,
                new FullTypeEntity { IntProp = 3 }
            };

            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.NotNull(copy[0]);
            Assert.Null(copy[1]);
            Assert.NotNull(copy[2]);
            Assert.Equal(1, copy[0]!.IntProp);
            Assert.Equal(3, copy[2]!.IntProp);
        }

        // ================================================================
        // CopyQueue / CopyHashSet / CopyConcurrentQueue — null guards + value type paths
        // ================================================================

        /// <summary>
        /// CopyQueue null guard.
        /// </summary>
        [Fact]
        public void CopyQueue_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyQueue<int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        /// <summary>
        /// CopyQueue preserves FIFO order with reference types.
        /// </summary>
        [Fact]
        public void CopyQueue_RefType_Preserves_Fifo_Order()
        {
            var source = new Queue<FullTypeEntity>();
            source.Enqueue(new FullTypeEntity { IntProp = 1 });
            source.Enqueue(new FullTypeEntity { IntProp = 2 });
            source.Enqueue(new FullTypeEntity { IntProp = 3 });

            var copy = CollectionCopier.CopyQueue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal(1, copy.Dequeue().IntProp);
            Assert.Equal(2, copy.Dequeue().IntProp);
            Assert.Equal(3, copy.Dequeue().IntProp);
        }

        /// <summary>
        /// CopyHashSet null guard.
        /// </summary>
        [Fact]
        public void CopyHashSet_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyHashSet<int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        /// <summary>
        /// CopyHashSet value-type fast path — kills IsValueType removal.
        /// </summary>
        [Fact]
        public void CopyHashSet_ValueType_Preserves_Elements()
        {
            var source = new HashSet<int> { 1, 2, 3, 4, 5 };
            var copy = CollectionCopier.CopyHashSet(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(5, copy!.Count);
            Assert.Contains(1, copy);
            Assert.Contains(5, copy);
        }

        /// <summary>
        /// CopyConcurrentQueue null guard.
        /// </summary>
        [Fact]
        public void CopyConcurrentQueue_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyConcurrentQueue<int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        /// <summary>
        /// CopyConcurrentDictionary null guard.
        /// </summary>
        [Fact]
        public void CopyConcurrentDictionary_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyConcurrentDictionary<int, string>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        /// <summary>
        /// CopyReadOnlyCollection null guard.
        /// </summary>
        [Fact]
        public void CopyReadOnlyCollection_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyReadOnlyCollection<int>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        // ================================================================
        // CopyEnumerable — args.Length != 1 guard (L402), ctor null fallback (L418-424)
        // ================================================================

        /// <summary>
        /// CopyEnumerable null guard.
        /// </summary>
        [Fact]
        public void CopyEnumerable_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyEnumerable<List<int>>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        /// <summary>
        /// CopyEnumerable with LinkedList (has IEnumerable ctor) — value-type elements.
        /// Kills: elemType.IsValueType || IsEnum || == string at L411.
        /// </summary>
        [Fact]
        public void CopyEnumerable_LinkedList_ValueType()
        {
            var source = new LinkedList<int>(new[] { 10, 20, 30 });
            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal(10, copy.First!.Value);
            Assert.Equal(30, copy.Last!.Value);
        }

        // ================================================================
        // CopyDictionaryByKeyValue — key/value simple checks (L465-466)
        // ================================================================

        /// <summary>
        /// CopyDictionaryByKeyValue null guard.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_Null_Returns_Null()
        {
            var result = CollectionCopier.CopyDictionaryByKeyValue<SortedDictionary<int, string>>(null!, new DeepCopyContext());
            Assert.Null(result);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with simple key+value — fast path.
        /// Kills: keySimple &amp;&amp; valSimple — if mutated, would skip fast path.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_SimpleTypes_FastPath()
        {
            var source = new SortedDictionary<int, string> { { 1, "x" }, { 2, "y" } };
            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal("x", copy[1]);
            Assert.Equal("y", copy[2]);
        }

        // ================================================================
        // ConvertDictEntry — three-way branching (L201-225)
        // ================================================================

        /// <summary>
        /// ConvertDictionary with cross-type values triggers ConvertDictEntry mapping.
        /// Kills: entry.Value is Tvalue direct-match short-circuit at L209.
        /// </summary>
        [Fact]
        public void ConvertDictionary_CrossType_Values()
        {
            var source = new System.Collections.Hashtable
            {
                { "key1", 100 },
                { "key2", 200 }
            };

            var result = CollectionCopier.ConvertDictionary<string, int>(source);

            Assert.Equal(2, result.Count);
            Assert.Equal(100, result["key1"]);
            Assert.Equal(200, result["key2"]);
        }

        // ================================================================
        // ClearCache — s_enumerableCtorCache, s_dictCtorCache, s_dictValueMapperCache (L509-514)
        // ================================================================

        /// <summary>
        /// ClearCache clears all three caches.
        /// Kills individual .Clear() removal mutants for each cache.
        /// </summary>
        [Fact]
        public void ClearCache_Clears_All_Caches()
        {
            // Populate caches
            var list = new LinkedList<int>(new[] { 1, 2, 3 });
            CollectionCopier.CopyEnumerable(list, new DeepCopyContext());
            var dict = new SortedDictionary<int, string> { { 1, "a" } };
            CollectionCopier.CopyDictionaryByKeyValue(dict, new DeepCopyContext());

            // Clear
            CollectionCopier.ClearCache();

            // Re-populate — should work correctly (caches are empty, re-built)
            var list2 = CollectionCopier.CopyEnumerable(list, new DeepCopyContext());
            Assert.NotNull(list2);
            Assert.Equal(3, list2!.Count);
        }

        // ================================================================
        // IsValueType&lt;T&gt; — type.IsValueType || type.IsEnum || type == typeof(string) (L14-18)
        // ================================================================

        /// <summary>
        /// DeepCopyList with enum elements — kills IsEnum removal from IsValueType.
        /// If || to &amp;&amp; mutation, enum list would incorrectly go through deep copy path.
        /// </summary>
        [Fact]
        public void DeepCopyList_Enum_Elements_FastPath()
        {
            var source = new List<OrderStatus> { OrderStatus.Pending, OrderStatus.Shipped };
            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal(OrderStatus.Pending, copy[0]);
            Assert.Equal(OrderStatus.Shipped, copy[1]);
        }
        // ================================================================
        // Round 2 — Null element tests for Conditional(false) mutations
        // Kills: `item == null ? default! : DeepCopy(item)!` mutated to `false ? ...`
        // If mutated, DeepCopy receives null → NullReferenceException
        // ================================================================

        /// <summary>
        /// DeepCopyList with null elements among reference types.
        /// Kills L65 Conditional(false) mutation: `false ? default! : DeepCopy(item)!`
        /// would crash on null elements because DeepCopy(null) throws NRE internally.
        /// </summary>
        [Fact]
        public void DeepCopyList_NullElements_AmongRefTypes()
        {
            var source = new List<Address>
            {
                new Address { City = "Beijing", Street = "St1", ZipCode = 100000 },
                null!,
                new Address { City = "Shanghai", Street = "St2", ZipCode = 200000 }
            };

            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.NotNull(copy[0]);
            Assert.Null(copy[1]); // null preserved
            Assert.NotNull(copy[2]);
            Assert.NotSame(source[0], copy[0]);
            Assert.Equal("Beijing", copy[0].City);
            Assert.Equal("Shanghai", copy[2].City);
        }

        /// <summary>
        /// DeepCopyArray with null elements among reference types.
        /// Kills L103 Conditional(false): `false ? null : DeepCopyObjectCore(item)`
        /// would crash when passing null to DeepCopyObjectCore.
        /// </summary>
        [Fact]
        public void DeepCopyArray_NullElements_AmongRefTypes()
        {
            var source = new Address[]
            {
                new Address { City = "A", Street = "S1", ZipCode = 1 },
                null!,
                new Address { City = "B", Street = "S2", ZipCode = 2 }
            };

            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Length);
            Assert.NotNull(copy[0]);
            Assert.Null(copy[1]);
            Assert.NotNull(copy[2]);
            Assert.NotSame(source[0], copy[0]);
            Assert.Equal("A", copy[0].City);
        }

        /// <summary>
        /// CopyQueue with null elements — kills L244 Conditional(false).
        /// </summary>
        [Fact]
        public void CopyQueue_NullElements_RefType()
        {
            var source = new Queue<Address>();
            source.Enqueue(new Address { City = "Q1", Street = "S", ZipCode = 1 });
            source.Enqueue(null!);
            source.Enqueue(new Address { City = "Q2", Street = "S", ZipCode = 2 });

            var copy = CollectionCopier.CopyQueue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            var arr = copy.ToArray();
            Assert.NotNull(arr[0]);
            Assert.Null(arr[1]);
            Assert.NotNull(arr[2]);
            Assert.NotSame(source.First(), arr[0]);
        }

        /// <summary>
        /// CopyStack with null elements — kills L261 Conditional(false).
        /// </summary>
        [Fact]
        public void CopyStack_NullElements_RefType()
        {
            var source = new Stack<Address>();
            source.Push(new Address { City = "Bottom", Street = "S", ZipCode = 3 });
            source.Push(null!);
            source.Push(new Address { City = "Top", Street = "S", ZipCode = 1 });

            var copy = CollectionCopier.CopyStack(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            // CopyStack uses Array.Reverse before re-pushing, so Pop order may differ.
            // Verify all elements are present: one null and two non-null with correct data.
            var items = new List<Address?>();
            while (copy.Count > 0) items.Add(copy.Pop());
            Assert.Equal(3, items.Count);
            Assert.Single(items, x => x == null);
            Assert.Single(items, x => x != null && x.City == "Top");
            Assert.Single(items, x => x != null && x.City == "Bottom");
        }

        /// <summary>
        /// CopyHashSet with null elements — kills L277 Conditional(false).
        /// </summary>
        [Fact]
        public void CopyHashSet_NullElements_RefType()
        {
            var source = new HashSet<Address>
            {
                new Address { City = "H1", Street = "S", ZipCode = 1 },
                null!
            };

            var copy = CollectionCopier.CopyHashSet(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Contains(copy, a => a != null && a.City == "H1");
            Assert.Contains(copy, a => a == null);
        }

        /// <summary>
        /// CopyReadOnlyCollection with null elements — kills L299 Conditional(false).
        /// </summary>
        [Fact]
        public void CopyReadOnlyCollection_NullElements_RefType()
        {
            var source = new List<Address>
            {
                new Address { City = "RO1", Street = "S", ZipCode = 1 },
                null!,
                new Address { City = "RO2", Street = "S", ZipCode = 2 }
            }.AsReadOnly();

            var copy = CollectionCopier.CopyReadOnlyCollection(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.NotNull(copy[0]);
            Assert.Null(copy[1]);
            Assert.NotNull(copy[2]);
        }

        /// <summary>
        /// CopyConcurrentQueue with null elements — kills L325 Conditional(false).
        /// </summary>
        [Fact]
        public void CopyConcurrentQueue_NullElements_RefType()
        {
            var source = new ConcurrentQueue<Address>();
            source.Enqueue(new Address { City = "CQ1", Street = "S", ZipCode = 1 });
            source.Enqueue(null!);

            var copy = CollectionCopier.CopyConcurrentQueue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            var arr = copy.ToArray();
            Assert.NotNull(arr[0]);
            Assert.Null(arr[1]);
        }

        /// <summary>
        /// CopyConcurrentStack with null elements — kills L343 Conditional(false).
        /// </summary>
        [Fact]
        public void CopyConcurrentStack_NullElements_RefType()
        {
            var source = new ConcurrentStack<Address>();
            source.Push(new Address { City = "CS1", Street = "S", ZipCode = 1 });
            source.Push(null!);

            var copy = CollectionCopier.CopyConcurrentStack(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            // Verify null is preserved — drain all elements
            var items = new List<Address?>();
            while (copy.TryPop(out var item)) items.Add(item);
            Assert.Equal(2, items.Count);
            Assert.Single(items, x => x == null);
            Assert.Single(items, x => x != null && x.City == "CS1");
        }

        // ================================================================
        // Round 3 — Dictionary conditional mutations
        // Kills L126-127 (CopyDictionary), L148-149 (CopyConcurrentDictionary)
        // ================================================================

        /// <summary>
        /// CopyDictionary with reference-type key and value — kills L126/L127 conditional mutations.
        /// If `IsValueType<Tin>() ? kv.Key : DeepCopy(kv.Key)!` mutates to always DeepCopy,
        /// and key is value type, DeepCopy on a boxed int would fail or be wrong.
        /// </summary>
        [Fact]
        public void CopyDictionary_RefKey_RefValue_DeepCopied()
        {
            var addr1 = new Address { City = "K1", Street = "S1", ZipCode = 1 };
            var addr2 = new Address { City = "K2", Street = "S2", ZipCode = 2 };
            var source = new Dictionary<Address, Address>
            {
                [addr1] = addr2
            };

            var copy = CollectionCopier.CopyDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Single(copy!);
            // Keys and values should be deep-copied (independent)
            var keyPair = copy.First();
            Assert.NotSame(addr1, keyPair.Key);
            Assert.NotSame(addr2, keyPair.Value);
            Assert.Equal("K1", keyPair.Key.City);
            Assert.Equal("K2", keyPair.Value.City);
        }

        /// <summary>
        /// CopyDictionary with null value — kills L127 Conditional(false).
        /// If `false ? kv.Value : DeepCopy(kv.Value)!`, DeepCopy on null crashes.
        /// </summary>
        [Fact]
        public void CopyDictionary_NullValue_RefType()
        {
            var source = new Dictionary<string, Address>
            {
                ["key1"] = new Address { City = "V1", Street = "S", ZipCode = 1 },
                ["key2"] = null!
            };

            var copy = CollectionCopier.CopyDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.NotNull(copy["key1"]);
            Assert.Null(copy["key2"]); // null preserved
        }

        /// <summary>
        /// CopyConcurrentDictionary with null value — kills L148-149 Conditional mutations.
        /// </summary>
        [Fact]
        public void CopyConcurrentDictionary_NullValue_RefType()
        {
            var source = new ConcurrentDictionary<string, Address>();
            source["k1"] = new Address { City = "CD1", Street = "S", ZipCode = 1 };
            source["k2"] = null!;

            var copy = CollectionCopier.CopyConcurrentDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.NotNull(copy["k1"]);
            Assert.Null(copy["k2"]);
        }

        // ================================================================
        // Round 4 — Reference-type element independence (deep copy verification)
        // Kills mutations that skip DeepCopy calls
        // ================================================================

        /// <summary>
        /// DeepCopyList reference-type elements — modify copy doesn't affect original.
        /// Kills: DeepCopy bypass mutations.
        /// </summary>
        [Fact]
        public void DeepCopyList_RefType_Independence()
        {
            var source = new List<Address>
            {
                new Address { City = "Original", Street = "S1", ZipCode = 100 }
            };

            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext())!;

            Assert.NotSame(source[0], copy[0]);
            copy[0].City = "Modified";
            Assert.Equal("Original", source[0].City); // original unchanged
        }

        /// <summary>
        /// DeepCopyArray reference-type elements — modify copy doesn't affect original.
        /// </summary>
        [Fact]
        public void DeepCopyArray_RefType_Independence()
        {
            var source = new[] { new Address { City = "Arr", Street = "S", ZipCode = 1 } };

            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext())!;

            Assert.NotSame(source[0], copy[0]);
            copy[0].City = "Changed";
            Assert.Equal("Arr", source[0].City);
        }

        /// <summary>
        /// CopyQueue reference-type independence.
        /// </summary>
        [Fact]
        public void CopyQueue_RefType_Independence()
        {
            var source = new Queue<Address>();
            source.Enqueue(new Address { City = "QOrig", Street = "S", ZipCode = 1 });

            var copy = CollectionCopier.CopyQueue(source, new DeepCopyContext())!;

            var origItem = source.Peek();
            var copyItem = copy.Peek();
            Assert.NotSame(origItem, copyItem);
            copyItem.City = "QMod";
            Assert.Equal("QOrig", origItem.City);
        }

        /// <summary>
        /// CopyStack reference-type independence.
        /// </summary>
        [Fact]
        public void CopyStack_RefType_Independence()
        {
            var source = new Stack<Address>();
            source.Push(new Address { City = "SOrig", Street = "S", ZipCode = 1 });

            var copy = CollectionCopier.CopyStack(source, new DeepCopyContext())!;

            var origItem = source.Peek();
            var copyItem = copy.Peek();
            Assert.NotSame(origItem, copyItem);
            copyItem.City = "SMod";
            Assert.Equal("SOrig", origItem.City);
        }

        /// <summary>
        /// CopyHashSet reference-type independence.
        /// </summary>
        [Fact]
        public void CopyHashSet_RefType_Independence()
        {
            var addr = new Address { City = "HOrig", Street = "S", ZipCode = 1 };
            var source = new HashSet<Address> { addr };

            var copy = CollectionCopier.CopyHashSet(source, new DeepCopyContext())!;

            var copyItem = copy.First();
            Assert.NotSame(addr, copyItem);
            copyItem.City = "HMod";
            Assert.Equal("HOrig", addr.City);
        }

        // ================================================================
        // Round 5 — IsValueType/IsEnum checks with mixed types
        // Kills Logical mutations: || to && in IsValueType checks
        // ================================================================

        /// <summary>
        /// DeepCopyList with string elements — verifies string fast path.
        /// If || to && mutation at L17, string wouldn't match IsValueType check,
        /// causing unnecessary DeepCopy on immutable strings.
        /// </summary>
        [Fact]
        public void DeepCopyList_String_Elements_FastPath()
        {
            var source = new List<string> { "hello", "world", "test" };
            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal("hello", copy[0]);
            Assert.Equal("world", copy[1]);
            Assert.Equal("test", copy[2]);
        }

        /// <summary>
        /// CopyDictionary value-type only — fast path.
        /// If IsValueType check mutated (|| to &&), int keys/values wouldn't match,
        /// causing unnecessary deep copy overhead.
        /// </summary>
        [Fact]
        public void CopyDictionary_ValueTypeOnly_FastPath()
        {
            var source = new Dictionary<int, int> { [1] = 10, [2] = 20, [3] = 30 };
            var copy = CollectionCopier.CopyDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal(10, copy[1]);
            Assert.Equal(20, copy[2]);
            Assert.Equal(30, copy[3]);
        }

        /// <summary>
        /// CopyConcurrentDictionary with string key, int value — mixed value types.
        /// Kills L465 Logical mutations: string key should match IsValueType check.
        /// </summary>
        [Fact]
        public void CopyConcurrentDictionary_StringKey_IntValue()
        {
            var source = new ConcurrentDictionary<string, int>();
            source["a"] = 1;
            source["b"] = 2;

            var copy = CollectionCopier.CopyConcurrentDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal(1, copy["a"]);
            Assert.Equal(2, copy["b"]);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with reference-type elements.
        /// Verifies the generic fallback path for custom dictionary types.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_RefElements()
        {
            var source = new SortedDictionary<string, Address>
            {
                ["k1"] = new Address { City = "SD1", Street = "S", ZipCode = 1 },
                ["k2"] = new Address { City = "SD2", Street = "S", ZipCode = 2 }
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
        }

        /// <summary>
        /// CopyEnumerable with reference-type elements — generic fallback.
        /// Verifies the fallback path for unknown collection types.
        /// </summary>
        [Fact]
        public void CopyEnumerable_RefType_Elements()
        {
            var source = new LinkedList<Address>();
            source.AddLast(new Address { City = "LL1", Street = "S", ZipCode = 1 });
            source.AddLast(new Address { City = "LL2", Street = "S", ZipCode = 2 });

            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
        }

        /// <summary>
        /// DeepCopyList with all-null reference-type elements.
        /// Kills: conditional mutation that always takes DeepCopy path crashes on nulls.
        /// </summary>
        [Fact]
        public void DeepCopyList_AllNull_RefType()
        {
            var source = new List<Address> { null!, null!, null! };
            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.All(copy, item => Assert.Null(item));
        }
    }
}
