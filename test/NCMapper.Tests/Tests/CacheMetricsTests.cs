using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 缓存级观测计数测试（F-5 观测扩展，功能层）
    /// <para>评审 A4 两层拆分：功能层（本文件）断言计数语义——开启时正确累加、
    /// 关闭时不累加、ResetMetrics 归零；性能层（零开销）以 W3E-B 基线对照数据支撑
    /// （Batch-3 基准采集，不以单测断言性能属性）。</para>
    /// <para>计数机制（聚合 + 按缓存类分组两级）由 Properties 端到端验证覆盖；
    /// NormalizedKeys 分组槽位写入正确性做 CacheMetrics 单元级断言（避免为触发
    /// 非默认命名配置引入程序集级 [assembly: MapDefaults] 全局副作用）。</para>
    /// <para>因触发 Mapper.ClearCache/ResetMetrics 全局状态，加入 GlobalConfigState
    /// 串行集合（DisableParallelization，与 CacheGovernanceTests 同族）。</para>
    /// </summary>
    [Collection("GlobalConfigState")]
    public class CacheMetricsTests : IDisposable
    {
        /// <summary>
        /// 每个测试用例前复位全局状态：清缓存、清计数、观测关闭
        /// </summary>
        public CacheMetricsTests()
        {
            Mapper.ClearCache();
            Mapper.ResetMetrics();
            Mapper.MetricsEnabled = false;
        }

        /// <summary>
        /// 测试结束后复位观测开关与全局缓存（进程级单例，防止污染其他测试类）
        /// </summary>
        public void Dispose()
        {
            Mapper.MetricsEnabled = false;
            Mapper.ClearCache();
        }

        /// <summary>
        /// T1 开启时：首次访问未命中、二次访问命中，聚合与分组两级均正确累加
        /// </summary>
        [Fact]
        public void MetricsEnabled_On_FirstAccessMiss_SecondAccessHit_CountsBothLevels()
        {
            Mapper.MetricsEnabled = true;
            var target = typeof(MetricsSrc);

            // 首次访问 → 未命中（聚合 + Properties 分组）
            _ = PropertyMatcher.GetPropertiesDictIgnoreCase(target);
            Assert.Equal(1, CacheMetrics.TotalMisses);
            Assert.Equal(0, CacheMetrics.TotalHits);
            Assert.Equal(1, CacheMetrics.MissesByKind[(int)CacheMetrics.CacheKind.Properties]);
            Assert.Equal(0, CacheMetrics.HitsByKind[(int)CacheMetrics.CacheKind.Properties]);

            // 二次访问 → 命中（聚合 + Properties 分组）
            _ = PropertyMatcher.GetPropertiesDictIgnoreCase(target);
            Assert.Equal(1, CacheMetrics.TotalMisses);
            Assert.Equal(1, CacheMetrics.TotalHits);
            Assert.Equal(1, CacheMetrics.HitsByKind[(int)CacheMetrics.CacheKind.Properties]);

            // MapFromAttr 缓存未触碰 → 分组零计数
            Assert.Equal(0, CacheMetrics.MissesByKind[(int)CacheMetrics.CacheKind.MapFromAttr]);
            Assert.Equal(0, CacheMetrics.HitsByKind[(int)CacheMetrics.CacheKind.MapFromAttr]);
        }

        /// <summary>
        /// T2 关闭时：缓存访问不产生任何计数（热路径零开销契约的行为面）
        /// </summary>
        [Fact]
        public void MetricsEnabled_Off_NoCountersIncrement()
        {
            Mapper.MetricsEnabled = false;
            var target = typeof(MetricsSrc);

            // 关闭状态下多次访问（含未命中与命中）
            _ = PropertyMatcher.GetPropertiesDictIgnoreCase(target);
            _ = PropertyMatcher.GetPropertiesDictIgnoreCase(target);
            _ = PropertyMatcher.GetMapFromAttribute(target);

            Assert.Equal(0, CacheMetrics.TotalMisses);
            Assert.Equal(0, CacheMetrics.TotalHits);
            Assert.All(CacheMetrics.HitsByKind, v => Assert.Equal(0, v));
            Assert.All(CacheMetrics.MissesByKind, v => Assert.Equal(0, v));
        }

        /// <summary>
        /// T3 ResetMetrics：入口级（TotalMapCalls/CacheHits/CompilationCount）与缓存级计数全部归零
        /// </summary>
        [Fact]
        public void ResetMetrics_Zeroes_EntryAndCacheCounters()
        {
            Mapper.MetricsEnabled = true;
            var target = typeof(MetricsSrc);

            // 制造计数：缓存访问 + Map 入口调用
            _ = PropertyMatcher.GetPropertiesDictIgnoreCase(target);
            _ = Mapper.Map<MetricsSrc, MetricsTgt>(new MetricsSrc { Id = 1, Name = "x" })!;
            Assert.True(CacheMetrics.TotalMisses > 0, "前置：开启后应已产生未命中计数");
            Assert.True(Mapper.TotalMapCalls > 0, "前置：Map 入口调用应已计数");

            Mapper.ResetMetrics();

            Assert.Equal(0, CacheMetrics.TotalHits);
            Assert.Equal(0, CacheMetrics.TotalMisses);
            Assert.All(CacheMetrics.HitsByKind, v => Assert.Equal(0, v));
            Assert.All(CacheMetrics.MissesByKind, v => Assert.Equal(0, v));
            Assert.Equal(0, Mapper.TotalMapCalls);
            Assert.Equal(0, Mapper.CacheHits);
            Assert.Equal(0, Mapper.CompilationCount);
        }

        /// <summary>
        /// T4 Map 端到端：开启观测时冷启动新类型对（委托未编译 + 缓存未构建）产生缓存未命中计数，
        /// 且映射结果与输入守恒（B3 红线不因观测改变）
        /// <para>注意：同类型对二次映射走 MapperBase 已编译委托（泛型静态 Lazy 不经 ClearCache 清除），
        /// 不触达 PropertyMatcher —— 故必须用全新类型对验证缓存级计数。</para>
        /// </summary>
        [Fact]
        public void MetricsEnabled_MapPath_CountsAndPreservesResult()
        {
            // 关闭态基线结果（同构类型对，仅作语义对照锚点）
            Mapper.MetricsEnabled = false;
            var baseline = Mapper.Map<MetricsSrc, MetricsTgt>(new MetricsSrc { Id = 42, Name = "守恒" })!;
            Assert.Equal(42, baseline.Id);
            Assert.Equal("守恒", baseline.Name);

            // 开启态：全新类型对冷启动（MetricsSrc2→MetricsTgt2，此前未映射 → 委托编译 + 属性匹配路径）
            Mapper.MetricsEnabled = true;
            Mapper.ResetMetrics();
            var observed = Mapper.Map<MetricsSrc2, MetricsTgt2>(new MetricsSrc2 { Id = 42, Name = "守恒" })!;

            // 结果守恒（观测开启不改变映射语义）
            Assert.Equal(42, observed.Id);
            Assert.Equal("守恒", observed.Name);

            // 入口级计数：Map 调用已累计
            Assert.True(Mapper.TotalMapCalls >= 1, "Map 入口调用应计数");
            // 缓存级未命中：全新类型对属性匹配构建 → Properties 分组未命中 ≥1（源+目标各一次字典构建）
            Assert.True(CacheMetrics.TotalMisses >= 1, "属性匹配缓存应产生未命中计数");
            Assert.True(CacheMetrics.MissesByKind[(int)CacheMetrics.CacheKind.Properties] >= 1,
                "Properties 分组未命中应 ≥1（源+目标各一次属性字典构建）");
        }

        /// <summary>
        /// T5 NormalizedKeys 分组槽位写入正确性（CacheMetrics 单元级）：
        /// RecordMiss/RecordHit 对 CacheKind.NormalizedKeys 写入下标 2 且不影响聚合口径
        /// </summary>
        [Fact]
        public void RecordMissAndHit_NormalizedKeys_WritesToKindSlot()
        {
            // 直接驱动计数 API（不触发非默认命名配置，规避程序集级全局副作用）
            CacheMetrics.RecordMiss(CacheMetrics.CacheKind.NormalizedKeys);
            CacheMetrics.RecordMiss(CacheMetrics.CacheKind.NormalizedKeys);
            CacheMetrics.RecordHit(CacheMetrics.CacheKind.NormalizedKeys);

            Assert.Equal(2, CacheMetrics.TotalMisses);
            Assert.Equal(1, CacheMetrics.TotalHits);
            Assert.Equal(2, CacheMetrics.MissesByKind[(int)CacheMetrics.CacheKind.NormalizedKeys]);
            Assert.Equal(1, CacheMetrics.HitsByKind[(int)CacheMetrics.CacheKind.NormalizedKeys]);
            Assert.Equal(0, CacheMetrics.MissesByKind[(int)CacheMetrics.CacheKind.Properties]);
            Assert.Equal(0, CacheMetrics.HitsByKind[(int)CacheMetrics.CacheKind.Properties]);
        }

        /// <summary>
        /// 观测测试用源类型
        /// </summary>
        private class MetricsSrc
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string? Name { get; set; }
        }

        /// <summary>
        /// 观测测试用目标类型
        /// </summary>
        private class MetricsTgt
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string? Name { get; set; }
        }

        /// <summary>
        /// 开启态冷启动源类型（与 MetricsSrc 同构但为独立类型，确保委托未编译）
        /// </summary>
        private class MetricsSrc2
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string? Name { get; set; }
        }

        /// <summary>
        /// 开启态冷启动目标类型（与 MetricsTgt 同构但为独立类型，确保委托未编译）
        /// </summary>
        private class MetricsTgt2
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string? Name { get; set; }
        }
    }
}
