// 深拷贝运行时腿测试（动作 3，2 例）
// 锚点：src/NCMapper/DeepCopyCompiler.cs（仅被 ExpressionTreeDeepCopyProvider 调用——
// "两条腿必须分清"：本文件与 SourceGen.Tests 生成腿是两条独立代码路径）
//   :412  ConcurrentDictionary<,> → CopyConcurrentDictionary 具名分支
//   :429-433 通用回退：单值泛型 + IEnumerable → CopyEnumerable（SortedSet 不在 :414-427 具名分支内）
// 本项目模型无 [MapFrom]（无生成注册）→ Mapper.DeepCopy 走 DeepCopyProviderBase.Current；
// DeepCopyCompiler 引擎仅 ExpressionTree provider 触达——RunWithProvider 显式切换（先例
// DeepCopyB04B05JointMatrixTests.cs:447 保存/恢复惯例）。
using System.Collections.Concurrent;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests
{
    public class DeepCopyRuntimeLegT5Tests
    {
        // ================================================================
        // 运行时腿模型（无 [MapFrom]——不触发生成注册）
        // ================================================================

        private sealed class Rt5Leaf : IComparable<Rt5Leaf>
        {
            public int N { get; set; }
            public string? Tag { get; set; }
            public int CompareTo(Rt5Leaf? other) => N - (other?.N ?? 0);
        }

        private sealed class Rt5Host
        {
            public int Id { get; set; }
            /// <summary>ConcurrentDictionary 支（DeepCopyCompiler.cs:412）</summary>
            public ConcurrentDictionary<string, Rt5Leaf> Cd { get; set; } = new();
            /// <summary>SortedSet——不在具名分支 → :429-433 单值泛型通用回退（CopyEnumerable）</summary>
            public SortedSet<Rt5Leaf> Sorted { get; set; } = new();
        }

        /// <summary>在指定 Provider 下执行动作（保存/恢复 Current——先例同款惯例）。</summary>
        private static T RunWithProvider<T>(DeepCopyProviderBase provider, Func<T> action)
        {
            var original = DeepCopyProviderBase.Current;
            try
            {
                DeepCopyProviderBase.Current = provider;
                return action();
            }
            finally
            {
                DeepCopyProviderBase.Current = original;
            }
        }

        // ================================================================
        // 动作 3（2 例）
        // ================================================================

        [Fact]
        public void Runtime_ConcurrentDictionary_ElementsDeepCopied()
        {
            // 例 1：ConcurrentDictionary<string, Rt5Leaf> → DeepCopyCompiler.cs:412 具名分支
            var src = new Rt5Host { Id = 1 };
            src.Cd["a"] = new Rt5Leaf { N = 1, Tag = "t1" };
            src.Cd["b"] = new Rt5Leaf { N = 2, Tag = "t2" };

            var copy = RunWithProvider(new ExpressionTreeDeepCopyProvider(), () => Mapper.DeepCopy(src))!;

            Assert.False(ReferenceEquals(src, copy), "Rt5Host 拷贝应为新实例");
            Assert.Equal(1, copy.Id);
            Assert.False(ReferenceEquals(src.Cd, copy.Cd), "ConcurrentDictionary 应深拷贝");
            Assert.Equal(2, copy.Cd.Count);
            Assert.False(ReferenceEquals(src.Cd["a"], copy.Cd["a"]), "字典值元素应深拷贝");
            Assert.Equal(1, copy.Cd["a"].N);
            Assert.Equal("t1", copy.Cd["a"].Tag);
            Assert.Equal(2, copy.Cd["b"].N);
        }

        [Fact]
        public void Runtime_SortedSet_GenericEnumerableFallback_ElementsDeepCopied()
        {
            // 例 2：SortedSet<Rt5Leaf> 顶层属性——不在 :414-427 具名分支 →
            // :429-433 单值泛型+IEnumerable 通用回退（CopyEnumerable）
            var src = new Rt5Host { Id = 2 };
            src.Sorted.Add(new Rt5Leaf { N = 3, Tag = "t3" });
            src.Sorted.Add(new Rt5Leaf { N = 5, Tag = "t5" });

            var copy = RunWithProvider(new ExpressionTreeDeepCopyProvider(), () => Mapper.DeepCopy(src))!;

            Assert.False(ReferenceEquals(src, copy), "Rt5Host 拷贝应为新实例");
            Assert.Equal(2, copy.Id);
            Assert.False(ReferenceEquals(src.Sorted, copy.Sorted), "SortedSet 应深拷贝");
            Assert.Equal(2, copy.Sorted.Count);
            // 元素独立（引用判等——SortedSet.Contains 走 IComparable 比较器，不能用于引用断言）
            var refSet = new System.Collections.Generic.HashSet<Rt5Leaf>(src.Sorted, System.Collections.Generic.ReferenceEqualityComparer.Instance);
            Assert.All(copy.Sorted, e => Assert.False(refSet.Contains(e), $"SortedSet 元素应独立拷贝（N={e.N}）"));
            // 排序语义保持 + 元素值正确
            Assert.Equal(new[] { 3, 5 }, copy.Sorted.Select(x => x.N).ToArray());
            Assert.Equal("t3", copy.Sorted.First().Tag);
        }
    }
}
