using NCMapper;
using NCMapper.SourceGen.Tests.Models;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// T1 巩固测试 — 源生成路径（层 1）下的循环引用检测
    /// <para>所有测试模型均标 [MapFrom]，源生成器为其生成并注册 DeepCopy 委托到
    /// GeneratedDeepCopyCache&lt;T&gt;.Func。调用 Mapper.DeepCopy 时进入层 1 分支，
    /// 直接触达 Mapper.cs 中 T1 修复的 s_deepCopyInProgress / s_deepCopyDepth 检测逻辑。</para>
    /// <para>若 T1 修复的循环检测逻辑正确，自引用（A.Self = A）和 A↔B 双向引用
    /// 都应能正确打破循环，返回有效深拷贝而非栈溢出。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class DeepCopyCircularRefTests
    {
        /// <summary>
        /// 自引用场景 — A.Self = A，验证 T1 修复后 Mapper.DeepCopy 不栈溢出
        /// <para>期望行为：copy.Self == copy（循环被打破，指向拷贝自身）</para>
        /// </summary>
        [Fact]
        public void GeneratedDeepCopy_SelfReference_NoStackOverflow()
        {
            var node = new SgSelfRefDto { Value = 1 };
            node.Self = node; // 自引用循环

            var copy = Mapper.DeepCopy(node)!;

            Assert.Equal(1, copy.Value);
            // 循环应被打破：copy.Self 指向 copy 自身（而非原 node）
            Assert.NotNull(copy.Self);
            Assert.Same(copy, copy.Self);
            Assert.NotSame(node, copy.Self);
        }

        /// <summary>
        /// A↔B 双向引用场景 — A.B = B, B.A = A，验证 T1 修复后 Mapper.DeepCopy 不栈溢出
        /// <para>期望行为：copyA.B.A == copyA（循环被打破）</para>
        /// </summary>
        [Fact]
        public void GeneratedDeepCopy_AB_Circular_Handled()
        {
            var a = new SgCircularADto { Id = 1 };
            var b = new SgCircularBDto { Id = 2 };
            a.B = b;
            b.A = a; // A↔B 循环

            var copyA = Mapper.DeepCopy(a)!;

            Assert.Equal(1, copyA.Id);
            Assert.NotNull(copyA.B);
            Assert.Equal(2, copyA.B!.Id);
            // 循环应被打破：copyA.B.A 指向 copyA（而非原 a）
            Assert.NotNull(copyA.B!.A);
            Assert.Same(copyA, copyA.B!.A);
            Assert.NotSame(a, copyA.B!.A);
        }

        /// <summary>
        /// 源生成路径正确性 — 非循环场景下 Mapper.DeepCopy 仍正常工作
        /// <para>验证 T1 修复没有破坏非循环场景的功能</para>
        /// </summary>
        [Fact]
        public void GeneratedDeepCopy_NonCircular_Still_Works()
        {
            var node = new SgSelfRefDto { Value = 42, Self = new SgSelfRefDto { Value = 100 } };

            var copy = Mapper.DeepCopy(node)!;

            Assert.Equal(42, copy.Value);
            Assert.NotNull(copy.Self);
            Assert.Equal(100, copy.Self!.Value);
            Assert.NotSame(node.Self, copy.Self); // 引用独立
        }

        /// <summary>
        /// 验证源生成器确实为 SgSelfRefDto 注册了 DeepCopy 委托（走层 1 路径）
        /// <para>否则上述循环检测测试无法触达 T1 修复代码</para>
        /// </summary>
        [Fact]
        public void SgSelfRefDto_HasGeneratedDeepCopy_Registered()
        {
            Assert.True(Mapper.HasGeneratedDeepCopy<SgSelfRefDto>(),
                "SgSelfRefDto 应有源生成 DeepCopy 委托，否则循环检测测试未触达层 1 路径");
            Assert.True(Mapper.HasGeneratedDeepCopy<SgCircularADto>(),
                "SgCircularADto 应有源生成 DeepCopy 委托");
            Assert.True(Mapper.HasGeneratedDeepCopy<SgCircularBDto>(),
                "SgCircularBDto 应有源生成 DeepCopy 委托");
        }
    }
}
