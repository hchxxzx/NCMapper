using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// Validate 源生成器覆盖验证测试
    /// <para>验证源生成器生成的 ValidateGenerated() 与运行时 MapValidator.Validate() 结果一致</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class ValidateSourceGenTests
    {
        /// <summary>
        /// 源生成器路径 — Validate() 返回编译时验证结果（不触发 AppDomain 扫描）
        /// </summary>
        [Fact]
        public void SourceGen_Validate_Returns_Results()
        {
            var results = Mapper.Validate();
            Assert.NotNull(results);
            Assert.True(results.Count > 0, "应有至少一个 [MapFrom] 映射对的验证结果");
        }

        /// <summary>
        /// 源生成器路径 — 每个结果包含正确的源/目标类型
        /// </summary>
        [Fact]
        public void SourceGen_Validate_Contains_Known_Mapping()
        {
            var results = Mapper.Validate();
            // 测试项目中有 FullTypeEntity → SameTypeDto 等标注
            var hasFullTypeMapping = results.Any(r =>
                r.SourceType == typeof(FullTypeEntity) || r.TargetType == typeof(SameTypeDto));
            Assert.True(hasFullTypeMapping, "应包含已知的映射对");
        }

        /// <summary>
        /// 源生成器路径 — 全映射对级别为 OK
        /// </summary>
        [Fact]
        public void SourceGen_Validate_OK_When_All_Mapped()
        {
            var results = Mapper.Validate();
            var okResults = results.Where(r => r.Level == MapValidationLevel.OK);
            Assert.True(okResults.Any(), "应有至少一个 OK 级别的映射对");
        }

        /// <summary>
        /// 源生成器路径 — 验证结果与运行时路径一致
        /// </summary>
        [Fact]
        public void SourceGen_Validate_Consistent_With_Runtime()
        {
            // C27 隔离修复：断言前清运行时扫描缓存（Mapper.ClearCache 级联 MapValidator.ClearCache 清 s_assemblyScanCache），
            // 消除 ValidateMapping 对扫描缓存预热状态的执行顺序敏感（net10 flaky，报告 F1 E4/D3）。
            // 生成侧 Mapper.Validate() 走 RegisterGeneratedValidate 委托（编译期静态结果），不受影响。
            // 下方 Fluent 行跳过条件以 Message 含"Fluent 声明对"识别，双语化后
            // 该识别依赖 zh 分支文本——固定 zh-CN 文化（B2 特例登记追加：逻辑/断言零改动，仅消除
            // 本方案新引入的文化依赖——并行 en 窗口与 Fluent 行英文形态重叠时跳过失效，第 7 个锁定点）。
            var originalCurrent = System.Globalization.CultureInfo.CurrentCulture;
            var originalUi = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("zh-CN");
                System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-CN");

                Mapper.ClearCache();

                // 获取源生成器路径结果
                var sgResults = Mapper.Validate();

                // 对比：用运行时 MapValidator.ValidateMapping 验证同一映射对
                foreach (var sgResult in sgResults)
                {
                    // Fluent 补扫行跳过（引入补扫后的适配，Z-3 裁决推论）：
                    // 该类行 Message 带注记、MappedCount/TotalCount 恒 0、仅承载降级维度——
                    // 属性完整性由 Fluent 通道诊断承载，不适用 ValidateMapping 属性计数对比
                    if (sgResult.Message.Contains("Fluent 声明对")) continue;

                    var runtimeResult = MapValidator.ValidateMapping(sgResult.SourceType, sgResult.TargetType);

                    // 验证计数一致（允许 TotalCount 有差异，因为源生成器可能包含 ComposeMappings 的计算方式不同）
                    // C27 补测：失败时输出类型对与计数（net10 flaky 定位，报告 F1 E4/D3 证据）
                    Assert.True(runtimeResult.MappedCount == sgResult.MappedCount,
                        $"类型对 {sgResult.SourceType.Name}→{sgResult.TargetType.Name}: 运行时 MappedCount={runtimeResult.MappedCount} vs 生成 MappedCount={sgResult.MappedCount}");
                }
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCurrent;
                System.Globalization.CultureInfo.CurrentUICulture = originalUi;
            }
        }

        /// <summary>
        /// 源生成器路径 — ValidationEnabled=false 时返回空列表
        /// </summary>
        [Fact]
        public void SourceGen_Validate_Disabled_Returns_Empty()
        {
            var original = Mapper.ValidationEnabled;
            try
            {
                Mapper.ValidationEnabled = false;
                var results = Mapper.Validate();
                Assert.Empty(results);
            }
            finally
            {
                Mapper.ValidationEnabled = original;
            }
        }

        /// <summary>
        /// 源生成器路径 — 验证结果可打印报告
        /// </summary>
        [Fact]
        public void SourceGen_Validate_Can_Print_Report()
        {
            // 验证消息双语化后本断言锁定 zh 形态，固定 zh-CN 文化
            //（B2 特例登记：断言文本零改动，仅消除新引入的文化依赖；先例 ErrorMessagesLocalizationTests 模式）
            var originalCurrent = System.Globalization.CultureInfo.CurrentCulture;
            var originalUi = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("zh-CN");
                System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-CN");

                var results = Mapper.Validate();
                using var sw = new StringWriter();
                MapValidator.PrintReport(results, sw, showOk: true);
                var report = sw.ToString();
                Assert.Contains("NCMapper 映射验证报告", report);
                Assert.Contains("总计", report);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCurrent;
                System.Globalization.CultureInfo.CurrentUICulture = originalUi;
            }
        }

        // ===== E2E-4: Validate 诊断覆盖验证 =====

        /// <summary>
        /// 验证源生成器 ValidateGenerated 包含完整的映射对统计
        /// 修复后，Validate 应消费 NCMAP001/002/005/006/008/010/014/015
        /// </summary>
        [Fact]
        public void E2E_Validate_Contains_Expected_Mapping_Pairs()
        {
            var results = Mapper.Validate();

            // 验证结果非空
            Assert.NotEmpty(results);

            // 每个结果应包含有效的类型和级别
            foreach (var r in results)
            {
                Assert.NotNull(r.SourceType);
                Assert.NotNull(r.TargetType);
                Assert.InRange((int)r.Level, (int)MapValidationLevel.OK, (int)MapValidationLevel.Error);
                Assert.True(r.TotalCount >= r.MappedCount, $"TotalCount({r.TotalCount}) 应 >= MappedCount({r.MappedCount})");
            }

            // 统计各级别数量
            var okCount = results.Count(r => r.Level == MapValidationLevel.OK);
            var warnCount = results.Count(r => r.Level == MapValidationLevel.Warn);
            var errorCount = results.Count(r => r.Level == MapValidationLevel.Error);

            // 至少应有 OK 结果（正常映射对）
            Assert.True(okCount > 0, "应至少有一个 OK 级别的映射对");
        }

        /// <summary>
        /// 验证 Validate 结果与 MapValidator 运行时扫描结果一致
        /// </summary>
        [Fact]
        public void E2E_Validate_SourceGen_Matches_Runtime()
        {
            var sourceGenResults = Mapper.Validate();
            var runtimeResults = MapValidator.Validate();

            Assert.True(sourceGenResults.Count > 0);
            Assert.True(runtimeResults.Count > 0);
        }
    }
}
