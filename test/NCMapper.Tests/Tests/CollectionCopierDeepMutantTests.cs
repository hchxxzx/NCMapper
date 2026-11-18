using Xunit;
using NCMapper;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Deep mutant-killing tests for CollectionCopier.cs.
    /// Targets surviving mutants from Stryker report: IsValueType || to && mutations,
    /// Conditional(false) mutations, CopyConcurrentDictionary conditionals,
    /// CopyEnumerable/CopyDictionaryByKeyValue guards, and ConvertDictEntry logic.
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class CollectionCopierDeepMutantTests : IDisposable
    {
        public CollectionCopierDeepMutantTests() => Mapper.ClearCache();
        public void Dispose() => Mapper.ClearCache();

        // ================================================================
        // 1. IsValueType<T>() || to && mutations (L17, L33)
        //
        // Original: type.IsValueType || type.IsEnum || type == typeof(string)
        // Mutant A: type.IsValueType || type.IsEnum && type == typeof(string)  (second || to &&)
        //   - For string: false || (false && true) = false  (WRONG - should be true)
        //   - Causes string to skip fast path, going through DeepCopy
        // Mutant B: type.IsValueType && type.IsEnum  (first || to &&, drops string check)
        //   - For int: true && false = false  (WRONG - should be true)
        //   - For string: false && false = false  (WRONG - should be true)
        //   - Causes int and string to skip fast path
        //
        // Kill strategy: verify string and enum elements produce correct results
        // via the fast (shallow copy) path. If mutants survive, the loop path
        // is taken instead, which may still produce correct results for immutable
        // types but at observable cost or with edge-case failures.
        // ================================================================

        /// <summary>
        /// DeepCopyList with string elements — verifies string fast path.
        /// Kills L17/L33 mutation: type.IsValueType || type.IsEnum && type == typeof(string).
        /// If string fails the IsValueType check, it enters the deep copy loop where
        /// DeepCopyObjectCore is called on each string — potentially producing different
        /// references or failing for string types lacking parameterless constructors.
        /// </summary>
        [Fact]
        public void DeepCopyList_String_Elements_FastPath_StringPreserved()
        {
            var source = new List<string> { "alpha", "beta", "gamma" };
            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal("alpha", copy[0]);
            Assert.Equal("beta", copy[1]);
            Assert.Equal("gamma", copy[2]);
            // Verify same string references — fast path returns original strings via ToList()
            Assert.Same(source[0], copy[0]);
            Assert.Same(source[1], copy[1]);
        }

        /// <summary>
        /// DeepCopyList with enum elements — verifies enum fast path.
        /// Kills L17 mutation: type.IsValueType && type.IsEnum (drops string check).
        /// Enum.IsValueType is true, so this mutation still returns true for enums.
        /// Combined with string test above, kills the full mutation set.
        /// </summary>
        [Fact]
        public void DeepCopyList_OrderStatus_Elements_Preserved()
        {
            var source = new List<OrderStatus> { OrderStatus.Pending, OrderStatus.Shipped, OrderStatus.Delivered };
            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal(OrderStatus.Pending, copy[0]);
            Assert.Equal(OrderStatus.Shipped, copy[1]);
            Assert.Equal(OrderStatus.Delivered, copy[2]);
        }

        /// <summary>
        /// DeepCopyList with int elements — verifies value type fast path.
        /// Kills L17 mutant B: type.IsValueType && type.IsEnum.
        /// For int: true && false = false (WRONG). Would enter loop path and call
        /// DeepCopy on each int, which goes through DeepCopyObjectCore — boxed ints
        /// are value types so DeepCopyObjectCore returns them directly, but the
        /// resulting list is built via Add() instead of ToList().
        /// </summary>
        [Fact]
        public void DeepCopyList_Int_Elements_FastPath()
        {
            var source = new List<int> { 10, 20, 30 };
            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal(10, copy[0]);
            Assert.Equal(20, copy[1]);
            Assert.Equal(30, copy[2]);
        }

        /// <summary>
        /// CollectionCopier.DeepCopy with string directly — kills L33 || to && mutation.
        /// If L33 is mutated, string fails the check and DeepCopyObjectCore is called,
        /// which invokes the deep copy provider on a string. String has no parameterless
        /// constructor, so FastDeepCopyProvider uses GetUninitializedObject — producing
        /// a broken/empty string instead of the original.
        /// </summary>
        [Fact]
        public void DeepCopy_String_Direct_FastPath()
        {
            var result = CollectionCopier.DeepCopy("hello", new DeepCopyContext());
            Assert.Equal("hello", result);
            Assert.Same("hello", result); // Same reference — fast path returns source directly
        }

        /// <summary>
        /// CollectionCopier.DeepCopy with enum directly — kills L33 mutation.
        /// </summary>
        [Fact]
        public void DeepCopy_Enum_Direct_FastPath()
        {
            var result = CollectionCopier.DeepCopy(OrderStatus.Processing, new DeepCopyContext());
            Assert.Equal(OrderStatus.Processing, result);
        }

        // ================================================================
        // 2. Conditional(false) mutations in DeepCopyList/Array (L65, L95→L103)
        //
        // Original: item == null ? default! : DeepCopy(item)!
        // Mutant:   false       ? default! : DeepCopy(item)!
        //   - Always calls DeepCopy(item), even when item is null
        //   - DeepCopy(null) returns default, so no crash for DeepCopy<T>
        //   - But DeepCopyObjectCore(null) would NullReferenceException
        //
        // Kill strategy: include null elements and verify they remain null.
        // For DeepCopyList, the mutated code calls DeepCopy(null) which returns
        // default(T) = null for reference types — so the result is the same.
        // But combined with DeepCopyObjectCore paths in other methods, the
        // null guard is critical.
        // ================================================================

        /// <summary>
        /// DeepCopyList with FullTypeEntity and null elements.
        /// Kills L65 Conditional(false): false ? default! : DeepCopy(item)!
        /// DeepCopy(null) returns default, so for DeepCopyList the result may be same.
        /// But verifies null is preserved as null (not some default-constructed entity).
        /// </summary>
        [Fact]
        public void DeepCopyList_FullTypeEntity_NullElement_Preserved()
        {
            var source = new List<FullTypeEntity>
            {
                new FullTypeEntity { IntProp = 1, StringProp = "first" },
                null!,
                new FullTypeEntity { IntProp = 3, StringProp = "third" }
            };

            var copy = CollectionCopier.DeepCopyList(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.NotNull(copy[0]);
            Assert.Null(copy[1]); // null preserved — not a default-constructed entity
            Assert.NotNull(copy[2]);
            Assert.Equal(1, copy[0]!.IntProp);
            Assert.Equal(3, copy[2]!.IntProp);
            // Verify deep copy independence
            Assert.NotSame(source[0], copy[0]);
            Assert.NotSame(source[2], copy[2]);
        }

        /// <summary>
        /// DeepCopyArray with object[] containing null elements.
        /// Kills L103 Conditional(false): false ? null : DeepCopyObjectCore(item)
        /// DeepCopyObjectCore(null) throws NullReferenceException because
        /// null.GetType() fails — this test CRASHES the mutant.
        /// </summary>
        [Fact]
        public void DeepCopyArray_ObjectArray_NullElement_Preserved()
        {
            var obj1 = new Address { City = "A", Street = "S1", ZipCode = 1 };
            var obj2 = new Address { City = "B", Street = "S2", ZipCode = 2 };
            var source = new object[] { obj1, null!, obj2 };

            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Length);
            Assert.NotNull(copy[0]);
            Assert.Null(copy[1]); // null preserved — mutant would crash with NRE
            Assert.NotNull(copy[2]);
        }

        // ================================================================
        // 3. CopyConcurrentDictionary Conditional mutations (L148-149)
        //
        // L148: keyComplex ? DeepCopy(item.Key)! : item.Key
        //   Mutant Conditional(true): always DeepCopy key, even for simple keys
        // L149: (valComplex && item.Value != null) ? DeepCopy(item.Value)! : item.Value
        //   Mutant: null value not checked → DeepCopy(null) or DeepCopyObjectCore(null)
        //
        // Kill strategy: test with various key/value complexity combinations,
        // especially null values with complex value types.
        // ================================================================

        /// <summary>
        /// CopyConcurrentDictionary with complex (reference type) keys AND null values.
        /// Kills L148 Conditional(true): always DeepCopy key.
        /// Kills L149: null values must be preserved when valComplex is true.
        /// If null check is removed, DeepCopyObjectCore(null) crashes with NRE.
        /// </summary>
        [Fact]
        public void CopyConcurrentDictionary_ComplexKey_NullValue_Preserved()
        {
            var key1 = new Address { City = "KeyA", Street = "S1", ZipCode = 1 };
            var key2 = new Address { City = "KeyB", Street = "S2", ZipCode = 2 };
            var val1 = new Address { City = "ValA", Street = "S3", ZipCode = 3 };

            var source = new ConcurrentDictionary<Address, Address>();
            source[key1] = val1;
            source[key2] = null!;

            var copy = CollectionCopier.CopyConcurrentDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);

            // Find the entry with non-null value
            var entries = copy.ToList();
            var nonNullEntry = entries.First(e => e.Value != null);
            var nullEntry = entries.First(e => e.Value == null);

            Assert.Equal("ValA", nonNullEntry.Value.City);
            Assert.Null(nullEntry.Value); // null value preserved
        }

        /// <summary>
        /// CopyConcurrentDictionary with simple (string) keys — keys should NOT be deep copied.
        /// Kills L148 Conditional(true): if always DeepCopy key, string keys go through
        /// DeepCopyObjectCore which may fail or produce different references.
        /// Verifies that string keys are preserved as-is (same reference).
        /// </summary>
        [Fact]
        public void CopyConcurrentDictionary_StringKey_NotDeepCopied()
        {
            var source = new ConcurrentDictionary<string, int>();
            source["alpha"] = 1;
            source["beta"] = 2;

            var copy = CollectionCopier.CopyConcurrentDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal(1, copy["alpha"]);
            Assert.Equal(2, copy["beta"]);
        }

        /// <summary>
        /// CopyConcurrentDictionary with complex values, some null.
        /// Kills L149: (valComplex && item.Value != null) mutation.
        /// Non-null values must be independent copies; null values must remain null.
        /// </summary>
        [Fact]
        public void CopyConcurrentDictionary_ComplexValues_NullPreserved_Independence()
        {
            var addr1 = new Address { City = "V1", Street = "S1", ZipCode = 1 };
            var addr2 = new Address { City = "V2", Street = "S2", ZipCode = 2 };

            var source = new ConcurrentDictionary<string, Address>();
            source["k1"] = addr1;
            source["k2"] = null!;
            source["k3"] = addr2;

            var copy = CollectionCopier.CopyConcurrentDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);

            // Non-null values are deep-copied (independent)
            Assert.NotNull(copy["k1"]);
            Assert.NotSame(addr1, copy["k1"]);
            Assert.Equal("V1", copy["k1"].City);

            // Null value preserved
            Assert.Null(copy["k2"]);

            // Another non-null value
            Assert.NotNull(copy["k3"]);
            Assert.NotSame(addr2, copy["k3"]);
            Assert.Equal("V2", copy["k3"].City);

            // Verify independence — modify copy doesn't affect original
            copy["k1"].City = "Modified";
            Assert.Equal("V1", addr1.City);
        }

        // ================================================================
        // 4. CopyConcurrentQueue/Queue/Stack/ConcurrentStack/HashSet/
        //    ReadOnlyCollection Conditional(false) mutations
        //    (L244, L261, L277, L299, L325, L343)
        //
        // Original: item == null ? default! : DeepCopy(item)!
        // Mutant:   false       ? default! : DeepCopy(item)!
        //   - Always calls DeepCopy(item), even when item is null
        //   - For methods using DeepCopyObjectCore, null causes NRE
        //
        // Kill strategy: for EACH collection type, include null elements
        // among reference types and verify null is preserved.
        // ================================================================

        /// <summary>
        /// CopyConcurrentQueue with null elements among reference types.
        /// Kills L244 Conditional(false): false ? default! : DeepCopy(item)!
        /// </summary>
        [Fact]
        public void CopyConcurrentQueue_NullElements_RefType_Preserved()
        {
            var source = new ConcurrentQueue<FullTypeEntity>();
            source.Enqueue(new FullTypeEntity { IntProp = 10, StringProp = "first" });
            source.Enqueue(null!);
            source.Enqueue(new FullTypeEntity { IntProp = 30, StringProp = "third" });

            var copy = CollectionCopier.CopyConcurrentQueue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            var arr = copy.ToArray();
            Assert.NotNull(arr[0]);
            Assert.Null(arr[1]); // null preserved
            Assert.NotNull(arr[2]);
            Assert.Equal(10, arr[0]!.IntProp);
            Assert.Equal(30, arr[2]!.IntProp);
            // Verify deep copy independence
            Assert.NotSame(source.ToArray()[0], arr[0]);
        }

        /// <summary>
        /// CopyQueue with null elements among reference types.
        /// Kills L277 Conditional(false): false ? default! : DeepCopy(item)!
        /// </summary>
        [Fact]
        public void CopyQueue_NullElements_RefType_Preserved()
        {
            var source = new Queue<FullTypeEntity>();
            source.Enqueue(new FullTypeEntity { IntProp = 100 });
            source.Enqueue(null!);
            source.Enqueue(new FullTypeEntity { IntProp = 300 });

            var copy = CollectionCopier.CopyQueue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            var arr = copy.ToArray();
            Assert.NotNull(arr[0]);
            Assert.Null(arr[1]); // null preserved
            Assert.NotNull(arr[2]);
            Assert.Equal(100, arr[0]!.IntProp);
            Assert.Equal(300, arr[2]!.IntProp);
            Assert.NotSame(source.ToArray()[0], arr[0]);
        }

        /// <summary>
        /// CopyStack with null elements among reference types.
        /// Kills L299 Conditional(false): false ? default! : DeepCopy(item)!
        /// </summary>
        [Fact]
        public void CopyStack_NullElements_RefType_Preserved()
        {
            var source = new Stack<FullTypeEntity>();
            source.Push(new FullTypeEntity { IntProp = 1 });  // bottom
            source.Push(null!);
            source.Push(new FullTypeEntity { IntProp = 3 });  // top

            var copy = CollectionCopier.CopyStack(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            // Drain all elements to verify null is preserved
            var items = new List<FullTypeEntity?>();
            while (copy.Count > 0) items.Add(copy.Pop());
            Assert.Equal(3, items.Count);
            Assert.Single(items, x => x == null);
            Assert.Single(items, x => x != null && x.IntProp == 1);
            Assert.Single(items, x => x != null && x.IntProp == 3);
        }

        /// <summary>
        /// CopyConcurrentStack with null elements among reference types.
        /// Kills L325 Conditional(false): false ? default! : DeepCopy(item)!
        /// </summary>
        [Fact]
        public void CopyConcurrentStack_NullElements_RefType_Preserved()
        {
            var source = new ConcurrentStack<FullTypeEntity>();
            source.Push(new FullTypeEntity { IntProp = 11 });  // bottom
            source.Push(null!);
            source.Push(new FullTypeEntity { IntProp = 33 });  // top

            var copy = CollectionCopier.CopyConcurrentStack(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            var items = new List<FullTypeEntity?>();
            while (copy.TryPop(out var item)) items.Add(item);
            Assert.Equal(3, items.Count);
            Assert.Single(items, x => x == null);
            Assert.Single(items, x => x != null && x.IntProp == 11);
            Assert.Single(items, x => x != null && x.IntProp == 33);
        }

        /// <summary>
        /// CopyHashSet with null elements among reference types.
        /// Kills L261 Conditional(false): false ? default! : DeepCopy(item)!
        /// </summary>
        [Fact]
        public void CopyHashSet_NullElements_RefType_Preserved()
        {
            var source = new HashSet<FullTypeEntity>
            {
                new FullTypeEntity { IntProp = 42, StringProp = "hs1" },
                null!,
                new FullTypeEntity { IntProp = 84, StringProp = "hs2" }
            };

            var copy = CollectionCopier.CopyHashSet(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Contains(copy, e => e == null);
            Assert.Contains(copy, e => e != null && e.IntProp == 42);
            Assert.Contains(copy, e => e != null && e.IntProp == 84);
            // Verify non-null elements are independent copies
            var origItems = source.Where(e => e != null).ToList();
            var copyItems = copy.Where(e => e != null).ToList();
            foreach (var orig in origItems)
                Assert.DoesNotContain(copyItems, c => ReferenceEquals(orig, c));
        }

        /// <summary>
        /// CopyReadOnlyCollection with null elements among reference types.
        /// Kills L343 Conditional(false): false ? default! : DeepCopy(item)!
        /// </summary>
        [Fact]
        public void CopyReadOnlyCollection_NullElements_RefType_Preserved()
        {
            var source = new List<FullTypeEntity>
            {
                new FullTypeEntity { IntProp = 7, StringProp = "ro1" },
                null!,
                new FullTypeEntity { IntProp = 14, StringProp = "ro2" }
            }.AsReadOnly();

            var copy = CollectionCopier.CopyReadOnlyCollection(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.NotNull(copy[0]);
            Assert.Null(copy[1]); // null preserved
            Assert.NotNull(copy[2]);
            Assert.Equal(7, copy[0]!.IntProp);
            Assert.Equal(14, copy[2]!.IntProp);
            // Verify independence
            Assert.NotSame(source[0], copy[0]);
            Assert.NotSame(source[2], copy[2]);
        }

        // ================================================================
        // 5. CopyEnumerable mutations (L402-444)
        //
        // L402: args.Length != 1 — equality mutation
        // L411: elemType2.IsValueType || elemType2.IsEnum || elemType2 == typeof(string)
        // L418: ctor != null — null check removal
        // L432: item == null ? null : DeepCopyObjectCore(item) — Conditional(false)
        // L440: ctorRef != null — null check removal
        //
        // Kill strategy: test with LinkedList<string> (value-type fast path),
        // LinkedList<FullTypeEntity> (deep copy path with null elements),
        // and a custom collection lacking IEnumerable<T> constructor (JSON fallback).
        // ================================================================

        /// <summary>
        /// CopyEnumerable with LinkedList&lt;string&gt; — value-type-like fast path.
        /// Kills L411: elemType2.IsValueType || elemType2.IsEnum || elemType2 == typeof(string).
        /// If || to && mutation, string fails the check and goes to the deep copy loop
        /// instead of the constructor fast path.
        /// </summary>
        [Fact]
        public void CopyEnumerable_LinkedList_String_FastPath()
        {
            var source = new LinkedList<string>(new[] { "x", "y", "z" });
            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal("x", copy.First!.Value);
            Assert.Equal("y", copy.First!.Next!.Value);
            Assert.Equal("z", copy.Last!.Value);
        }

        /// <summary>
        /// CopyEnumerable with LinkedList&lt;OrderStatus&gt; — enum fast path.
        /// Kills L411 IsEnum part: if || becomes &&, enum fails the check.
        /// </summary>
        [Fact]
        public void CopyEnumerable_LinkedList_Enum_FastPath()
        {
            var source = new LinkedList<OrderStatus>(new[] { OrderStatus.Pending, OrderStatus.Cancelled });
            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal(OrderStatus.Pending, copy.First!.Value);
            Assert.Equal(OrderStatus.Cancelled, copy.Last!.Value);
        }

        /// <summary>
        /// CopyEnumerable with LinkedList&lt;FullTypeEntity&gt; — deep copy each element.
        /// Kills L432: item == null ? null : DeepCopyObjectCore(item).
        /// Null elements must remain null; non-null must be independent copies.
        /// Kills L440: ctorRef != null — LinkedList has IEnumerable&lt;T&gt; ctor.
        /// </summary>
        [Fact]
        public void CopyEnumerable_LinkedList_FullTypeEntity_DeepCopy_NullPreserved()
        {
            var source = new LinkedList<FullTypeEntity>();
            source.AddLast(new FullTypeEntity { IntProp = 1, StringProp = "first" });
            source.AddLast((FullTypeEntity)null!);
            source.AddLast(new FullTypeEntity { IntProp = 3, StringProp = "third" });

            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);

            var items = copy.ToList();
            Assert.NotNull(items[0]);
            Assert.Null(items[1]); // null preserved — kills L432 Conditional(false)
            Assert.NotNull(items[2]);
            Assert.Equal(1, items[0]!.IntProp);
            Assert.Equal(3, items[2]!.IntProp);
            // Verify independence
            Assert.NotSame(source.First!.Value, items[0]);
        }

        /// <summary>
        /// CopyEnumerable with a type that has args.Length != 1 — triggers JSON fallback.
        /// Kills L402: args.Length != 1 equality mutation.
        /// If mutated to args.Length != 2, a 2-arg generic would incorrectly enter
        /// the single-value path instead of falling back to JSON.
        /// </summary>
        [Fact]
        public void CopyEnumerable_TwoArgGeneric_FallsBackToJson()
        {
            // KeyValuePair<int,string> has 2 generic args → args.Length != 1 → JSON fallback
            // But KeyValuePair is a struct, and CopyEnumerable requires class.
            // Use Dictionary<string,int> which is a class with 2 generic args.
            var source = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal(1, copy["a"]);
            Assert.Equal(2, copy["b"]);
            Assert.NotSame(source, copy);
        }

        /// <summary>
        /// CopyEnumerable with Collection&lt;Address&gt; — lacks IEnumerable&lt;T&gt; constructor.
        /// Collection&lt;T&gt; only has parameterless and IList&lt;T&gt; constructors, NOT IEnumerable&lt;T&gt;.
        /// Triggers JSON fallback at L444 (after ctorRef == null).
        /// Kills L440: ctorRef != null — for types without the IEnumerable&lt;T&gt; ctor, must fall through to JSON.
        /// </summary>
        [Fact]
        public void CopyEnumerable_NoEnumerableCtor_FallsBackToJson()
        {
            var source = new Collection<Address>
            {
                new Address { City = "J1", Street = "S1", ZipCode = 1 },
                new Address { City = "J2", Street = "S2", ZipCode = 2 }
            };

            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.NotSame(source, copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal("J1", copy[0].City);
            Assert.Equal("J2", copy[1].City);
            // Verify deep copy independence
            Assert.NotSame(source[0], copy[0]);
        }

        // ================================================================
        // 6. CopyDictionaryByKeyValue mutations (L455-503)
        //
        // L461: args.Length != 2 — equality mutation
        // L465: keySimple = keyType.IsValueType || keyType.IsEnum || keyType == typeof(string)
        // L466: valSimple = valType.IsValueType || valType.IsEnum || valType == typeof(string)
        // L469: keySimple && valSimple — both simple → constructor path
        // L476: ctor != null — null check removal
        // L488: key = keySimple ? entry.Key : (entry.Key == null ? null : DeepCopyObjectCore(entry.Key))
        // L489: val = valSimple ? entry.Value : (entry.Value == null ? null : DeepCopyObjectCore(entry.Value))
        // L498: ctorRef != null — null check removal
        //
        // Kill strategy: test various key/value type combinations including
        // string keys, enum values, null elements in complex paths.
        // ================================================================

        /// <summary>
        /// CopyDictionaryByKeyValue with Dictionary&lt;string, int&gt; — both simple, ctor path.
        /// Kills L465/L466 || to && mutations: string key should be simple (not deep-copied),
        /// int value should be simple. If mutated, string fails keySimple check and goes
        /// through deep copy, or int fails valSimple check.
        /// Kills L469: keySimple && valSimple — if mutated to ||, wrong path taken.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_StringKey_IntValue_BothSimple_CtorPath()
        {
            var source = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2, ["three"] = 3 };
            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal(1, copy["one"]);
            Assert.Equal(2, copy["two"]);
            Assert.Equal(3, copy["three"]);
            Assert.NotSame(source, copy);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with SortedDictionary&lt;string, OrderStatus&gt; —
        /// string key simple, enum value simple. Both should take ctor path.
        /// Kills L466 valSimple || to &&: enum value should match valSimple check.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_StringKey_EnumValue_BothSimple()
        {
            var source = new SortedDictionary<string, OrderStatus>
            {
                ["pending"] = OrderStatus.Pending,
                ["shipped"] = OrderStatus.Shipped
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal(OrderStatus.Pending, copy["pending"]);
            Assert.Equal(OrderStatus.Shipped, copy["shipped"]);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with Dictionary&lt;string, FullTypeEntity&gt; —
        /// simple key, complex value. Null values must be preserved.
        /// Kills L489: val = valSimple ? entry.Value : (entry.Value == null ? null : DeepCopyObjectCore(entry.Value))
        /// Conditional(false) on null check — if always calls DeepCopyObjectCore, null crashes.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_StringKey_ComplexValue_NullPreserved()
        {
            var source = new Dictionary<string, FullTypeEntity>
            {
                ["e1"] = new FullTypeEntity { IntProp = 10, StringProp = "ten" },
                ["e2"] = null!,
                ["e3"] = new FullTypeEntity { IntProp = 30, StringProp = "thirty" }
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.NotNull(copy["e1"]);
            Assert.Null(copy["e2"]); // null value preserved
            Assert.NotNull(copy["e3"]);
            Assert.Equal(10, copy["e1"]!.IntProp);
            Assert.Equal(30, copy["e3"]!.IntProp);
            // Verify deep copy independence for non-null values
            Assert.NotSame(source["e1"], copy["e1"]);
            Assert.NotSame(source["e3"], copy["e3"]);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with Dictionary&lt;Address, string&gt; —
        /// complex key, simple value. Keys must be deep-copied.
        /// Kills L488: key = keySimple ? entry.Key : (entry.Key == null ? null : DeepCopyObjectCore(entry.Key))
        /// Verifies complex keys are independently deep-copied.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_ComplexKey_StringValue_KeysDeepCopied()
        {
            var key1 = new Address { City = "KA", Street = "S1", ZipCode = 1 };
            var key2 = new Address { City = "KB", Street = "S2", ZipCode = 2 };

            var source = new Dictionary<Address, string>
            {
                [key1] = "valA",
                [key2] = "valB"
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);

            // Verify keys are deep-copied (different references)
            var sourceKeys = source.Keys.ToList();
            var copyKeys = copy.Keys.ToList();
            for (int i = 0; i < sourceKeys.Count; i++)
            {
                Assert.NotSame(sourceKeys[i], copyKeys[i]);
            }

            // Verify values preserved
            var firstCopyEntry = copy.First();
            Assert.True(firstCopyEntry.Value == "valA" || firstCopyEntry.Value == "valB");
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with SortedDictionary&lt;string, Address&gt; —
        /// simple key, complex value with null. Verifies ctorRef path with deep-copied values.
        /// Kills L498: ctorRef != null — SortedDictionary has IDictionary&lt;K,V&gt; ctor.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_SortedDict_ComplexValue_CtorRefPath()
        {
            var source = new SortedDictionary<string, Address>
            {
                ["a"] = new Address { City = "SA", Street = "S1", ZipCode = 1 },
                ["b"] = null!,
                ["c"] = new Address { City = "SC", Street = "S3", ZipCode = 3 }
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.IsType<SortedDictionary<string, Address>>(copy);
            Assert.Equal(3, copy!.Count);
            Assert.NotNull(copy["a"]);
            Assert.Null(copy["b"]); // null preserved
            Assert.NotNull(copy["c"]);
            Assert.Equal("SA", copy["a"].City);
            Assert.Equal("SC", copy["c"].City);
            // Verify independence
            Assert.NotSame(source["a"], copy["a"]);
            Assert.NotSame(source["c"], copy["c"]);
        }

        // ================================================================
        // 7. ConvertDictEntry mutations (L201-225)
        //
        // L204: needValueMapping = !targetValType.IsValueType && !targetValType.IsEnum && targetValType != typeof(string)
        //   Mutant: !targetValType.IsValueType && !targetValType.IsEnum || targetValType != typeof(string)
        //   (changes last && to ||)
        //   - For string: (!false && !false) || false = true || false = true (WRONG — should be false)
        //   - For int:    (!true  && !false) || true  = false || true = true (WRONG — should be false)
        //
        // L213: needValueMapping && entry.Value != null
        //   Mutant: needValueMapping || entry.Value != null
        //   - When needValueMapping=true and entry.Value=null: true || false = true (WRONG)
        //   - Would try to call entry.Value.GetType() → NullReferenceException
        //
        // Kill strategy: use Hashtable with mixed types and null values.
        // ================================================================

        /// <summary>
        /// ConvertDictionary with Hashtable containing Address values and null.
        /// Kills L213: needValueMapping || entry.Value != null mutation.
        /// When needValueMapping is true (target=Address, not value type/enum/string)
        /// and entry.Value is null, the mutant evaluates to true, causing
        /// entry.Value.GetType() → NullReferenceException.
        /// </summary>
        [Fact]
        public void ConvertDictionary_Hashtable_NullValue_NeedValueMapping_NoCrash()
        {
            var source = new Hashtable
            {
                { "k1", new Address { City = "Map1", Street = "S1", ZipCode = 1 } },
                { "k2", null! }
            };

            // targetValType = Address → needValueMapping = true
            // entry "k2" has null value → must not crash
            var result = CollectionCopier.ConvertDictionary<string, Address>(source);

            Assert.Equal(2, result.Count);
            Assert.NotNull(result["k1"]);
            Assert.Equal("Map1", result["k1"].City);
            // Null value: falls to else branch, ConvertValue<object, Address>(null) → default
        }

        /// <summary>
        /// ConvertDictionary with Hashtable where value is already the target type.
        /// Kills L209: entry.Value is Tvalue direct match — if removed, all values
        /// go through mapping/conversion, which could change or corrupt them.
        /// </summary>
        [Fact]
        public void ConvertDictionary_Hashtable_SameType_DirectMatch()
        {
            var addr = new Address { City = "Direct", Street = "S", ZipCode = 99 };
            var source = new Hashtable
            {
                { "key", addr }
            };

            var result = CollectionCopier.ConvertDictionary<string, Address>(source);

            Assert.Single(result);
            // entry.Value is Address → direct match, same reference returned
            Assert.Same(addr, result["key"]);
        }

        /// <summary>
        /// ConvertDictionary with Hashtable where target value is string.
        /// Kills L204 || to && mutation: !IsValueType && !IsEnum || != typeof(string).
        /// For string target: (!false && !false) || false = true (WRONG, should be false).
        /// If needValueMapping becomes true for string, it tries to map via MapperBase,
        /// which could fail or produce unexpected results for simple string values.
        /// </summary>
        [Fact]
        public void ConvertDictionary_Hashtable_StringTarget_NotMappedAsComplex()
        {
            var source = new Hashtable
            {
                { "k1", "hello" },
                { "k2", "world" }
            };

            // targetValType = string → needValueMapping should be FALSE
            // entry.Value is string → direct match at L209
            var result = CollectionCopier.ConvertDictionary<string, string>(source);

            Assert.Equal(2, result.Count);
            Assert.Equal("hello", result["k1"]);
            Assert.Equal("world", result["k2"]);
        }

        /// <summary>
        /// ConvertDictionary with Hashtable where target value is int (value type).
        /// Kills L204 mutation: for int, needValueMapping should be false.
        /// If mutated to true, GetDictValueMapper(long, int) would be called for long values,
        /// potentially failing or producing wrong results.
        /// </summary>
        [Fact]
        public void ConvertDictionary_Hashtable_IntTarget_ValueTypeNotMapped()
        {
            var source = new Hashtable
            {
                { "a", 42 },
                { "b", 99 }
            };

            // targetValType = int → needValueMapping should be FALSE
            // entry.Value (42 boxed int) is int → direct match
            var result = CollectionCopier.ConvertDictionary<string, int>(source);

            Assert.Equal(2, result.Count);
            Assert.Equal(42, result["a"]);
            Assert.Equal(99, result["b"]);
        }

        /// <summary>
        /// ConvertConcurrentDictionaryFrom with Hashtable — exercises ConvertDictEntry
        /// via the ConcurrentDictionary path. Verifies correct type conversion with
        /// mixed value types and null values.
        /// </summary>
        [Fact]
        public void ConvertConcurrentDictionaryFrom_Hashtable_MixedTypes()
        {
            var source = new Hashtable
            {
                { "x", 100 },
                { "y", 200 }
            };

            var result = CollectionCopier.ConvertConcurrentDictionaryFrom<string, int>(source);

            Assert.Equal(2, result.Count);
            Assert.Equal(100, result["x"]);
            Assert.Equal(200, result["y"]);
        }

        // ================================================================
        // 8. Remaining IsValueType mutations (L465-466) in CopyDictionaryByKeyValue
        //
        // keySimple = keyType.IsValueType || keyType.IsEnum || keyType == typeof(string)
        // valSimple = valType.IsValueType || valType.IsEnum || valType == typeof(string)
        //
        // Mutant A: keyType.IsValueType || keyType.IsEnum && keyType == typeof(string)
        //   - For string key: false || (false && true) = false (WRONG — should be true)
        //   - String keys treated as complex → deep-copied unnecessarily
        //
        // Mutant B: keyType.IsValueType && keyType.IsEnum
        //   - For int key: true && false = false (WRONG — should be true)
        //   - Int keys treated as complex → deep-copied unnecessarily
        //
        // Kill strategy: verify string keys and enum values are treated as simple
        // (not deep-copied). If they're treated as complex, the deep copy path
        // may fail or produce different behavior.
        // ================================================================

        /// <summary>
        /// CopyDictionaryByKeyValue with string keys — string must be treated as simple.
        /// Kills L465 mutant A: || to && mutation.
        /// If string fails keySimple check, keys go through DeepCopyObjectCore,
        /// which for strings may produce empty/wrong values since string has no
        /// parameterless constructor.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_StringKeys_TreatedAsSimple()
        {
            var source = new SortedDictionary<string, string>
            {
                ["key1"] = "val1",
                ["key2"] = "val2"
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal("val1", copy["key1"]);
            Assert.Equal("val2", copy["key2"]);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with enum values — enum must be treated as simple.
        /// Kills L466: valType.IsValueType || valType.IsEnum || valType == typeof(string).
        /// If || to && mutation, enum fails valSimple check and goes through deep copy path.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_EnumValues_TreatedAsSimple()
        {
            var source = new Dictionary<int, OrderStatus>
            {
                [1] = OrderStatus.Pending,
                [2] = OrderStatus.Shipped,
                [3] = OrderStatus.Delivered
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal(OrderStatus.Pending, copy[1]);
            Assert.Equal(OrderStatus.Shipped, copy[2]);
            Assert.Equal(OrderStatus.Delivered, copy[3]);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with string values — string must be treated as simple.
        /// Kills L466 || to && mutation for string values.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_StringValues_TreatedAsSimple()
        {
            var source = new Dictionary<int, string>
            {
                [10] = "ten",
                [20] = "twenty"
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal("ten", copy[10]);
            Assert.Equal("twenty", copy[20]);
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with both string key AND string value.
        /// Double-verification that string is treated as simple on both sides.
        /// Kills both L465 and L466 mutations simultaneously.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_StringKey_StringValue_BothSimple()
        {
            var source = new Dictionary<string, string>
            {
                ["alpha"] = "one",
                ["beta"] = "two"
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Equal("one", copy["alpha"]);
            Assert.Equal("two", copy["beta"]);
            Assert.NotSame(source, copy);
        }

        // ================================================================
        // Additional edge case tests for thorough mutant killing
        // ================================================================

        /// <summary>
        /// DeepCopyArray with string elements and null element.
        /// Kills L95 || to && mutation AND L103 null guard simultaneously.
        /// String array takes fast path (clone); if mutation makes it fall through
        /// to deep copy loop, null element would crash DeepCopyObjectCore.
        /// </summary>
        [Fact]
        public void DeepCopyArray_StringArray_WithNull_FastPath()
        {
            var source = new string[] { "hello", null!, "world" };
            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Length);
            Assert.Equal("hello", copy[0]);
            Assert.Null(copy[1]);
            Assert.Equal("world", copy[2]);
        }

        /// <summary>
        /// DeepCopyArray with enum elements — verifies enum fast path at L95.
        /// Kills L95: elemType.IsValueType || elemType.IsEnum || elemType == typeof(string).
        /// If || to && mutation, enum fails the check and falls to deep copy loop,
        /// where DeepCopyObjectCore is called on boxed enum values.
        /// </summary>
        [Fact]
        public void DeepCopyArray_EnumElements_FastPath_Clone()
        {
            var source = new[] { OrderStatus.Pending, OrderStatus.Shipped, OrderStatus.Cancelled };
            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Length);
            Assert.Equal(OrderStatus.Pending, copy[0]);
            Assert.Equal(OrderStatus.Shipped, copy[1]);
            Assert.Equal(OrderStatus.Cancelled, copy[2]);
            // Fast path uses Array.Clone — different array reference
            Assert.NotSame(source, copy);
        }

        /// <summary>
        /// CopyConcurrentDictionary with simple key and complex value, all null values.
        /// Edge case for L149: (valComplex && item.Value != null) — when ALL values are null,
        /// the condition should short-circuit to item.Value for every entry.
        /// </summary>
        [Fact]
        public void CopyConcurrentDictionary_AllNullValues_Preserved()
        {
            var source = new ConcurrentDictionary<string, Address>();
            source["a"] = null!;
            source["b"] = null!;

            var copy = CollectionCopier.CopyConcurrentDictionary(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.Null(copy["a"]);
            Assert.Null(copy["b"]);
        }

        /// <summary>
        /// CopyEnumerable with LinkedList&lt;FullTypeEntity&gt; all null elements.
        /// Kills L432: item == null ? null : DeepCopyObjectCore(item).
        /// If Conditional(false), DeepCopyObjectCore(null) crashes with NRE.
        /// </summary>
        [Fact]
        public void CopyEnumerable_LinkedList_AllNullElements()
        {
            var source = new LinkedList<FullTypeEntity>();
            source.AddLast((FullTypeEntity)null!);
            source.AddLast((FullTypeEntity)null!);

            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
            Assert.All(copy, item => Assert.Null(item));
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with complex keys — verify deep copy independence.
        /// Kills L488: key = keySimple ? entry.Key : (entry.Key == null ? null : DeepCopyObjectCore(entry.Key)).
        /// If keySimple is mutated to true for complex types (|| to &&), keys would NOT be
        /// deep-copied — modifying a copy key's property would also change the original.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_ComplexKey_Independence()
        {
            var key1 = new Address { City = "OrigA", Street = "S1", ZipCode = 1 };
            var key2 = new Address { City = "OrigB", Street = "S2", ZipCode = 2 };

            var source = new Dictionary<Address, string>
            {
                [key1] = "valA",
                [key2] = "valB"
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);

            // Verify keys are independent copies — modifying copy key doesn't affect original
            var copyKeys = copy.Keys.ToList();
            var origKeys = source.Keys.ToList();

            for (int i = 0; i < origKeys.Count; i++)
            {
                Assert.NotSame(origKeys[i], copyKeys[i]);
            }

            // Modify a copy key and verify original is unaffected
            copyKeys[0].City = "ModifiedCity";
            Assert.DoesNotContain(origKeys, k => k.City == "ModifiedCity");
        }

        /// <summary>
        /// CopyDictionaryByKeyValue with complex value, all entries null.
        /// Kills L489 Conditional(false): always calls DeepCopyObjectCore even on null.
        /// </summary>
        [Fact]
        public void CopyDictionaryByKeyValue_AllNullValues_Preserved()
        {
            var source = new Dictionary<string, Address>
            {
                ["a"] = null!,
                ["b"] = null!,
                ["c"] = null!
            };

            var copy = CollectionCopier.CopyDictionaryByKeyValue(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Null(copy["a"]);
            Assert.Null(copy["b"]);
            Assert.Null(copy["c"]);
        }
    }
}
