using System;
using System.Collections.Generic;
using NCMapper;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 批次三消费补齐测试。
    /// <para>A3-02：多维/非零基数组深拷贝重建（原实现一维化必失败）；
    /// A3-03：值类型元素接口集合的 List 收窄兜底可达性（原抛出先于收窄）。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class CollectionCopierB3SilentSemanticsTests : IDisposable
    {
        public CollectionCopierB3SilentSemanticsTests() => Mapper.ClearCache();

        public void Dispose() => Mapper.ClearCache();

        /// <summary>A3-02 红绿：二维引用元素数组深拷贝——修复前必抛（一维下标 + 长度语义错误）。</summary>
        [Fact]
        public void A3_02_TwoDimArray_DeepCopy_RebuiltByRankAndBounds()
        {
            var source = new Address[2, 2];
            source[0, 0] = new Address { City = "A", Street = "S0", ZipCode = 1 };
            source[1, 1] = new Address { City = "B", Street = "S1", ZipCode = 2 };

            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.GetLength(0));
            Assert.Equal(2, copy.GetLength(1));
            Assert.Equal("A", copy[0, 0].City);
            Assert.Equal("B", copy[1, 1].City);
            Assert.Null(copy[0, 1]);
            Assert.Null(copy[1, 0]);
            // 深拷贝语义：元素为独立副本
            Assert.NotSame(source[0, 0], copy[0, 0]);
        }

        /// <summary>A3-02 红绿：三维数组深拷贝（秩泛化，非特化二维）。</summary>
        [Fact]
        public void A3_02_ThreeDimArray_DeepCopy_Rebuilt()
        {
            var source = new Address[2, 1, 2];
            source[1, 0, 1] = new Address { City = "C", Street = "S", ZipCode = 3 };

            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Rank);
            Assert.Equal("C", copy[1, 0, 1].City);
            Assert.NotSame(source[1, 0, 1], copy[1, 0, 1]);
        }

        /// <summary>A3-02 红绿：非零基一维数组——下界不匹配时原实现越界。</summary>
        [Fact]
        public void A3_02_NonZeroBasedArray_DeepCopy_Rebuilt()
        {
            // 非零基数组的运行时类型为 Address[*]（不可 cast 到 Address[]）——以 Array 为载体传入
            Array source = Array.CreateInstance(typeof(Address), new[] { 2 }, new[] { 1 });
            source.SetValue(new Address { City = "X", Street = "S", ZipCode = 1 }, 1);
            source.SetValue(new Address { City = "Y", Street = "S", ZipCode = 2 }, 2);

            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(1, copy!.GetLowerBound(0));
            Assert.Equal(2, copy.GetUpperBound(0));
            Assert.Equal("X", ((Address)copy.GetValue(1)!).City);
            Assert.Equal("Y", ((Address)copy.GetValue(2)!).City);
            Assert.NotSame(source.GetValue(1), copy.GetValue(1));
        }

        /// <summary>A3-02 回归对照：一维零基引用数组（SZ 强类型内核路径）零翻转。</summary>
        [Fact]
        public void A3_02_OneDimZeroBased_UnchangedByFix()
        {
            var source = new[] { new Address { City = "A", Street = "S", ZipCode = 1 } };

            var copy = CollectionCopier.DeepCopyArray(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Single(copy!);
            Assert.Equal("A", copy[0].City);
            Assert.NotSame(source[0], copy[0]);
        }

        /// <summary>A3-03 红绿：值类型元素接口集合 List 收窄——修复前抛 MapException。</summary>
        [Fact]
        public void A3_03_ValueTypeElementInterfaceCollection_NarrowedToList()
        {
            IReadOnlyList<int> source = new List<int> { 1, 2, 3 };

            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(3, copy!.Count);
            Assert.Equal(1, copy[0]);
            Assert.Equal(3, copy[2]);
        }

        /// <summary>A3-03 红绿：string 元素接口集合同样收窄（值类型/枚举/string 快路径族）。</summary>
        [Fact]
        public void A3_03_StringElementInterfaceCollection_NarrowedToList()
        {
            IReadOnlyCollection<string> source = new List<string> { "a", "b" };

            var copy = CollectionCopier.CopyEnumerable(source, new DeepCopyContext());

            Assert.NotNull(copy);
            Assert.Equal(2, copy!.Count);
        }

        /// <summary>A3-03 负样例（002-P2-2）：接口不被 List&lt;T&gt; 实现时不误收窄，仍按原语义抛出。</summary>
        [Fact]
        public void A3_03_NonCompatibleInterface_StillThrows()
        {
            ICustomSet<int> source = new CustomSet<int>(new[] { 1, 2 });

            Assert.Throws<MapException>(
                () => CollectionCopier.CopyEnumerable(source, new DeepCopyContext()));
        }

        /// <summary>接口集合但不是 List&lt;T&gt; 可赋值形态——IsAssignableFrom 腿把守的负样例载体。</summary>
        public interface ICustomSet<T> : IEnumerable<T>
        {
        }

        /// <summary>自定义集合实现（不实现 ICustomSet&lt;T&gt; 的通用形态，供负样例构造源对象）。</summary>
        public sealed class CustomSet<T> : ICustomSet<T>
        {
            private readonly List<T> _items;

            public CustomSet(IEnumerable<T> items) => _items = new List<T>(items);

            public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        }
    }
}
