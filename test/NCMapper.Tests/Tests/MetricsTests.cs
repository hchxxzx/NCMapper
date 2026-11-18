using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 性能指标 Metrics 测试
    /// <para>串行化集合 — 修改 Mapper.MetricsEnabled 全局状态的测试类必须串行执行</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MetricsTests : IDisposable
    {
        /// <summary>
        /// 测试后重置状态，避免影响其他测试
        /// </summary>
        public void Dispose()
        {
            Mapper.MetricsEnabled = false;
            Mapper.ResetMetrics();
        }

        // ================================================================
        // P0-8: MetricsEnabled 性能指标测试
        // ================================================================

        /// <summary>
        /// MetricsEnabled=false 时 TotalMapCalls 不增长
        /// </summary>
        [Fact]
        public void MetricsEnabled_False_DoesNotIncrement()
        {
            Mapper.MetricsEnabled = false;
            Mapper.ResetMetrics();

            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity { IntProp = 1 });

            Assert.Equal(0, Mapper.TotalMapCalls);
        }

        /// <summary>
        /// MetricsEnabled=true 时 TotalMapCalls 正确递增
        /// </summary>
        [Fact]
        public void MetricsEnabled_True_IncrementsTotalMapCalls()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity { IntProp = 1 });
            Assert.True(Mapper.TotalMapCalls >= 1, $"Expected >=1, actual: {Mapper.TotalMapCalls}");

            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity { IntProp = 2 });
            Assert.True(Mapper.TotalMapCalls >= 2, $"Expected >=2, actual: {Mapper.TotalMapCalls}");
        }

        /// <summary>
        /// ResetMetrics 将所有计数器归零
        /// </summary>
        [Fact]
        public void ResetMetrics_ResetsAllCounters()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity { IntProp = 1 });
            Mapper.Map<FullTypeEntity, SameTypeDto>(new FullTypeEntity { IntProp = 2 });

            Assert.True(Mapper.TotalMapCalls > 0);

            Mapper.ResetMetrics();
            Assert.Equal(0, Mapper.TotalMapCalls);
        }

        /// <summary>
        /// 首次映射后 CompilationCount 反映表达式树编译
        /// </summary>
        [Fact]
        public void MetricsEnabled_CompilationCount_AfterFirstMap()
        {
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();

            Mapper.Map<NoMapFromMetricClass, NoMapFromMetricDto>(new NoMapFromMetricClass { Value = 5 });

            // 首次映射应该触发编译
            Assert.True(Mapper.CompilationCount > 0, $"Expected >0, actual: {Mapper.CompilationCount}");
        }
    }

    /// <summary>
    /// 无 [MapFrom] 标注的类 — 走表达式树路径（Metrics专用）
    /// </summary>
    public class NoMapFromMetricClass
    {
        /// <summary>
        /// 数值属性
        /// </summary>
        public int Value { get; set; }
    }

    /// <summary>
    /// 无 [MapFrom] 标注的 Dto（Metrics专用）
    /// </summary>
    public class NoMapFromMetricDto
    {
        /// <summary>
        /// 数值属性
        /// </summary>
        public int Value { get; set; }
    }
}
