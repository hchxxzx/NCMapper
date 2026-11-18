using Xunit;
using NCMapper.SourceGen.Tests.Helpers;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 生成类可见性契约测试 — 验证 GeneratedMapper / GeneratedExtensions 类为 internal
    /// <para>internal 化消除跨程序集 CS0436。
    /// 防回归：若未来重构将生成类改回 public，本契约测试立即失败。</para>
    /// </summary>
    public class GeneratedClassVisibilityTests
    {
        /// <summary>
        /// 最小映射源码 — 单个 [MapFrom] 标注 DTO 触发源生成器
        /// </summary>
        private const string Source = """
            using NCMapper.Attributes;

            [MapFrom(typeof(Order))]
            public class OrderDto
            {
                public int Id { get; set; }
            }

            public class Order
            {
                public int Id { get; set; }
            }
            """;

        /// <summary>
        /// 生成的 GeneratedMapper 类声明为 internal（非 public）
        /// </summary>
        [Fact]
        public void GeneratedMapper_Is_Internal()
        {
            var result = TestHelper.GenerateMapper(Source);

            Assert.Contains("internal static class GeneratedMapper", result.GeneratedCode);
        }

        /// <summary>
        /// 生成代码中不存在 public static class GeneratedMapper 声明（防回归）
        /// </summary>
        [Fact]
        public void GeneratedMapper_Has_No_Public_Declaration()
        {
            var result = TestHelper.GenerateMapper(Source);

            Assert.DoesNotContain("public static class GeneratedMapper", result.GeneratedCode);
        }

        /// <summary>
        /// 生成的 GeneratedExtensions 类声明为 internal（非 public）
        /// </summary>
        [Fact]
        public void GeneratedExtensions_Is_Internal()
        {
            var result = TestHelper.GenerateMapper(Source);

            Assert.Contains("internal static class GeneratedExtensions", result.GeneratedCode);
        }

        /// <summary>
        /// 生成代码中不存在 public static class GeneratedExtensions 声明（防回归）
        /// </summary>
        [Fact]
        public void GeneratedExtensions_Has_No_Public_Declaration()
        {
            var result = TestHelper.GenerateMapper(Source);

            Assert.DoesNotContain("public static class GeneratedExtensions", result.GeneratedCode);
        }
    }
}
