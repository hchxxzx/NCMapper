using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// CollectionTypeDetector mutant-killing tests — targets surviving Stryker mutants in CollectionTypeDetector.cs
    /// Covers: IsGenericDictionary (interface scan), IsNonGenericDictionary,
    /// IsDictionary (|| to ||), IsEnumerable (array + IEnumerable)
    /// </summary>
    public class CollectionTypeDetectorMutantTests
    {
        // ================================================================
        // IsGenericDictionary — L17-21
        // Kills: interface scan, IsGenericType check, GetGenericTypeDefinition ==
        // ================================================================

        /// <summary>
        /// Dictionary&lt;string, int&gt; is a generic dictionary.
        /// Kills mutant: return value negation.
        /// </summary>
        [Fact]
        public void IsGenericDictionary_DictionaryStringInt_True()
        {
            Assert.True(CollectionTypeDetector.IsGenericDictionary(typeof(Dictionary<string, int>)));
        }

        /// <summary>
        /// ConcurrentDictionary&lt;string, int&gt; implements IDictionary&lt;K,V&gt;.
        /// Kills mutant: interface scan only checks direct type.
        /// </summary>
        [Fact]
        public void IsGenericDictionary_ConcurrentDictionary_True()
        {
            Assert.True(CollectionTypeDetector.IsGenericDictionary(typeof(ConcurrentDictionary<string, int>)));
        }

        /// <summary>
        /// List&lt;int&gt; is NOT a generic dictionary.
        /// Kills mutant: IsGenericDictionary always returns true.
        /// </summary>
        [Fact]
        public void IsGenericDictionary_ListOfInt_False()
        {
            Assert.False(CollectionTypeDetector.IsGenericDictionary(typeof(List<int>)));
        }

        /// <summary>
        /// string is NOT a generic dictionary.
        /// Kills mutant: no interface check, always true.
        /// </summary>
        [Fact]
        public void IsGenericDictionary_String_False()
        {
            Assert.False(CollectionTypeDetector.IsGenericDictionary(typeof(string)));
        }

        /// <summary>
        /// int is NOT a generic dictionary.
        /// </summary>
        [Fact]
        public void IsGenericDictionary_Int_False()
        {
            Assert.False(CollectionTypeDetector.IsGenericDictionary(typeof(int)));
        }

        /// <summary>
        /// SortedDictionary&lt;string, int&gt; implements IDictionary&lt;K,V&gt;.
        /// </summary>
        [Fact]
        public void IsGenericDictionary_SortedDictionary_True()
        {
            Assert.True(CollectionTypeDetector.IsGenericDictionary(typeof(SortedDictionary<string, int>)));
        }

        // ================================================================
        // IsNonGenericDictionary — L28-31
        // Kills: GetInterface("System.Collections.IDictionary") null check
        // ================================================================

        /// <summary>
        /// Hashtable implements non-generic IDictionary.
        /// Kills mutant: return value negation.
        /// </summary>
        [Fact]
        public void IsNonGenericDictionary_Hashtable_True()
        {
            Assert.True(CollectionTypeDetector.IsNonGenericDictionary(typeof(Hashtable)));
        }

        /// <summary>
        /// Dictionary&lt;string, int&gt; also implements non-generic IDictionary.
        /// Kills mutant: GetInterface always returns null.
        /// </summary>
        [Fact]
        public void IsNonGenericDictionary_GenericDict_True()
        {
            Assert.True(CollectionTypeDetector.IsNonGenericDictionary(typeof(Dictionary<string, int>)));
        }

        /// <summary>
        /// List&lt;int&gt; is NOT a non-generic dictionary.
        /// Kills mutant: always returns true.
        /// </summary>
        [Fact]
        public void IsNonGenericDictionary_ListOfInt_False()
        {
            Assert.False(CollectionTypeDetector.IsNonGenericDictionary(typeof(List<int>)));
        }

        /// <summary>
        /// string is NOT a non-generic dictionary.
        /// </summary>
        [Fact]
        public void IsNonGenericDictionary_String_False()
        {
            Assert.False(CollectionTypeDetector.IsNonGenericDictionary(typeof(string)));
        }

        // ================================================================
        // IsDictionary — L38-41
        // Kills: || to && mutation (both conditions must pass for &&)
        // ================================================================

        /// <summary>
        /// Dictionary&lt;string, int&gt; is a dictionary (both generic and non-generic).
        /// Kills mutant: || to && (would still work, but need non-generic-only case).
        /// </summary>
        [Fact]
        public void IsDictionary_GenericDict_True()
        {
            Assert.True(CollectionTypeDetector.IsDictionary(typeof(Dictionary<string, int>)));
        }

        /// <summary>
        /// Hashtable is a dictionary (non-generic only, NOT generic).
        /// Kills mutant: || to && (&& would fail since IsGenericDictionary returns false).
        /// This is the critical test for the || to && mutation.
        /// </summary>
        [Fact]
        public void IsDictionary_Hashtable_True()
        {
            Assert.True(CollectionTypeDetector.IsDictionary(typeof(Hashtable)));
        }

        /// <summary>
        /// List&lt;int&gt; is NOT a dictionary.
        /// Kills mutant: always returns true.
        /// </summary>
        [Fact]
        public void IsDictionary_ListOfInt_False()
        {
            Assert.False(CollectionTypeDetector.IsDictionary(typeof(List<int>)));
        }

        /// <summary>
        /// int is NOT a dictionary.
        /// </summary>
        [Fact]
        public void IsDictionary_Int_False()
        {
            Assert.False(CollectionTypeDetector.IsDictionary(typeof(int)));
        }

        /// <summary>
        /// string is NOT a dictionary.
        /// </summary>
        [Fact]
        public void IsDictionary_String_False()
        {
            Assert.False(CollectionTypeDetector.IsDictionary(typeof(string)));
        }

        // ================================================================
        // IsEnumerable — L48-51
        // Kills: IsArray check, IEnumerable.IsAssignableFrom, || to &&
        // ================================================================

        /// <summary>
        /// int[] is enumerable (via IsArray).
        /// Kills mutant: type.IsArray check removal.
        /// </summary>
        [Fact]
        public void IsEnumerable_IntArray_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(int[])));
        }

        /// <summary>
        /// string[] is enumerable (via IsArray).
        /// </summary>
        [Fact]
        public void IsEnumerable_StringArray_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(string[])));
        }

        /// <summary>
        /// List&lt;int&gt; is enumerable (via IEnumerable.IsAssignableFrom).
        /// Kills mutant: IEnumerable.IsAssignableFrom check removal.
        /// </summary>
        [Fact]
        public void IsEnumerable_ListOfInt_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(List<int>)));
        }

        /// <summary>
        /// HashSet&lt;string&gt; is enumerable.
        /// </summary>
        [Fact]
        public void IsEnumerable_HashSet_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(HashSet<string>)));
        }

        /// <summary>
        /// Queue&lt;int&gt; is enumerable.
        /// </summary>
        [Fact]
        public void IsEnumerable_Queue_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(Queue<int>)));
        }

        /// <summary>
        /// Stack&lt;int&gt; is enumerable.
        /// </summary>
        [Fact]
        public void IsEnumerable_Stack_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(Stack<int>)));
        }

        /// <summary>
        /// Dictionary&lt;string, int&gt; is enumerable (implements IEnumerable).
        /// Kills mutant: || to && (both IsArray and IEnumerable must be true for &&).
        /// Dictionary is NOT an array but IS IEnumerable; with &&, it would return false.
        /// </summary>
        [Fact]
        public void IsEnumerable_Dictionary_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(Dictionary<string, int>)));
        }

        /// <summary>
        /// int is NOT enumerable.
        /// Kills mutant: always returns true.
        /// </summary>
        [Fact]
        public void IsEnumerable_Int_False()
        {
            Assert.False(CollectionTypeDetector.IsEnumerable(typeof(int)));
        }

        /// <summary>
        /// string IS enumerable (implements IEnumerable&lt;char&gt;).
        /// Note: string implements IEnumerable, so this returns true.
        /// Kills mutant: IsArray removal (string is not array but is IEnumerable).
        /// </summary>
        [Fact]
        public void IsEnumerable_String_True()
        {
            // string implements IEnumerable (of char), so IsEnumerable returns true
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(string)));
        }

        /// <summary>
        /// ReadOnlyCollection&lt;int&gt; is enumerable.
        /// </summary>
        [Fact]
        public void IsEnumerable_ReadOnlyCollection_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(ReadOnlyCollection<int>)));
        }

        /// <summary>
        /// double (value type) is NOT enumerable.
        /// </summary>
        [Fact]
        public void IsEnumerable_Double_False()
        {
            Assert.False(CollectionTypeDetector.IsEnumerable(typeof(double)));
        }

        // ================================================================
        // Custom types for edge cases
        // ================================================================

        /// <summary>
        /// Custom class implementing IEnumerable — IsEnumerable true.
        /// </summary>
        public class CustomEnumerable : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// Custom class NOT implementing any collection interface.
        /// </summary>
        public class PlainClass { public int Id { get; set; } }

        /// <summary>
        /// Custom IEnumerable class is detected as enumerable.
        /// </summary>
        [Fact]
        public void IsEnumerable_CustomEnumerable_True()
        {
            Assert.True(CollectionTypeDetector.IsEnumerable(typeof(CustomEnumerable)));
        }

        /// <summary>
        /// Plain class is NOT enumerable.
        /// </summary>
        [Fact]
        public void IsEnumerable_PlainClass_False()
        {
            Assert.False(CollectionTypeDetector.IsEnumerable(typeof(PlainClass)));
        }

        /// <summary>
        /// Plain class is NOT a dictionary.
        /// </summary>
        [Fact]
        public void IsDictionary_PlainClass_False()
        {
            Assert.False(CollectionTypeDetector.IsDictionary(typeof(PlainClass)));
        }

        /// <summary>
        /// Plain class is NOT a generic dictionary.
        /// </summary>
        [Fact]
        public void IsGenericDictionary_PlainClass_False()
        {
            Assert.False(CollectionTypeDetector.IsGenericDictionary(typeof(PlainClass)));
        }
    }
}
