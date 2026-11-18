// ============================================================================
// RuntimeSnapshotGenerator — 快照运行时可达形态发射（独立增量生成器）
// ----------------------------------------------------------------------------
// 与 NCMapperSourceGenerator 并存（同程序集、独立增量管线节点）：
//   - 复用开源仓既有物化入口（零物化改动）：[MapFrom] 通道 MappingExtractor.ExtractMappingInfo
//     + Fluent 通道 FluentExtractor.Extract + DualDriverArbiter.MergeAndArbitrate 双通道合并
//     （与主生成器管线同构——NCMapperSourceGenerator.cs； 起六段装配已收敛至
//     SourceGenPipeline.cs 单点：原 2 处重复，本文件改为薄壳调用）；
//   - 只做「快照 → 发射」新段（SnapshotMetadataEmitter），不触碰派生内核/运行时引擎
//     
//   - 发射开关：build_property.NCMapperEmitRuntimeSnapshot（Directory.Build.props:79 /
//     NCMapper.targets:39 未显式设置时默认置 true—— Batch-3 切 on；
//     生成器侧仅当属性字面值非 "true" 时不注册任何输出节点产物：零文件、零诊断
//     → 基线项目生成产物字节不变，D5 门）。
//
// 评估结论：选定「独立生成器」形态而非「并入主链下游 emitter」
// ① spike 先例即此形态，F1 证伪（主生成器产物 on/off 字节不变）风险更低
//   （并入主链可能引入字节差异，违反 D5 字节不变门）；
// ② 双物化语义分叉风险（独立生成器与主链 MappingExtractor/FluentExtractor 各跑一遍，
//   结果可能不一致）由 T7 双路物化一致性测试覆盖；
// ③ 独立 AddSource 不触碰主生成器输出文本——L-1 物化复用零侵入。
// 评估登记： 已登记 选定结论 + 风险缓解指向 T7 双路物化一致性测试。
// ============================================================================
using System;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen;

/// <summary>
/// 快照运行时可达形态发射生成器（L-1 主件——挂载 SnapshotMetadataEmitter）
/// </summary>
[Generator]
public class RuntimeSnapshotGenerator : IIncrementalGenerator
{
    /// <summary>发射开关（build_property；Directory.Build.props/NCMapper.targets 未显式设置时默认置 true，生成器侧非 "true" 字面值时零产物，主生成器字节不变 D5 门）</summary>
    private const string EmitSwitchKey = "build_property.NCMapperEmitRuntimeSnapshot";

    /// <summary>
    /// 源生成总开关（口径——NCMapperSourceGenerator.cs:101-104 同名属性； 身份登记节点同读）
    /// </summary>
    private const string SourceGenEnabledSwitchKey = "build_property.NCMapperSourceGenEnabled";

    /// <summary>
    /// 初始化增量管线——与主生成器同构的双通道物化 + L-1 发射段
    /// </summary>
    /// <param name="context">增量生成器初始化上下文</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // （判据单点化）：原六段装配——[MapFrom] 通道（MappingExtractor.ExtractMappingInfo）
        // + Fluent 通道（FluentExtractor.Extract）+ 特性侧 [MapSubclass] 合成并入（SubclassSyntheticPipeline.Extract）
        // + 双通道合并仲裁（DualDriverArbiter.MergeAndArbitrate）+ 全局配置三层链（GlobalConfigReader）
        // + ApplyGlobalConfig 配置应用——已收敛至 SourceGenPipeline.Build 单点（原 2 处重复：
        // 本文件与 NCMapperSourceGenerator.cs），本文件改为薄壳调用：不重复装配任何管线段。
        // 段级溯源（两处注释并集）见 SourceGenPipeline.cs 头注。
        // 本生成器（含身份登记段）仅消费配置应用后的统一产物；其余两产物不消费（弃元）。
        var (_, _, configuredMappings) = SourceGenPipeline.Build(context);

        // 开关（GlobalOptions——MSBuild 属性注入； NCMapper.targets 声明）
        var emitSwitch = context.AnalyzerConfigOptionsProvider
            .Select((provider, ct) =>
                provider.GlobalOptions.TryGetValue(EmitSwitchKey, out var value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        var input = configuredMappings.Combine(emitSwitch);

        context.RegisterSourceOutput(input, (spc, source) =>
        {
            var (merged, emitEnabled) = source;
            // 开关关闭：零输出（不 AddSource、不报诊断）——基线产物字节不变
            if (!emitEnabled) return;

            var code = SnapshotMetadataEmitter.Emit(merged.Mappings);
            if (code != null)
            {
                spc.AddSource(SnapshotMetadataEmitter.HintName, code);
            }
        });

        // ---- 身份登记输出节点----
        // 不受 NCMapperEmitRuntimeSnapshot 开关控制（G1-① 承诺域：源生成启用前提下的 EmitRuntimeSnapshot
        // on/off 双配置——显式 off 态注册表须有 Fluent 身份键兜底）；同读 L-09 总开关
        // NCMapperSourceGenEnabled（复审 R-A：总开关关闭时身份文件与 GeneratedMapper 同消，与
        // NCMapperSourceGenerator.cs:101-104 同口径）。与明细段同源（同一 mergedMappings）——
        // 同生成器内拆双输出而非新增第三生成器（不放大 双物化分叉风险面）。
        var sourceGenEnabledSwitch = context.AnalyzerConfigOptionsProvider
            .Select((provider, ct) =>
                !(provider.GlobalOptions.TryGetValue(SourceGenEnabledSwitchKey, out var enabledValue)
                  && string.Equals(enabledValue, "false", StringComparison.OrdinalIgnoreCase)));

        // 身份登记与明细段同源（同一 configuredMappings——含两环补齐后的统一产物）
        var identityInput = configuredMappings.Combine(sourceGenEnabledSwitch);

        context.RegisterSourceOutput(identityInput, (spc, source) =>
        {
            var (merged, sourceGenEnabled) = source;
            // 总开关关闭：身份登记与主链同消（L-09 口径——源生成整体关闭，Fluent 声明回落运行时反射形态）
            if (!sourceGenEnabled) return;

            var identityCode = SnapshotMetadataEmitter.EmitIdentity(merged.Mappings);
            if (identityCode != null)
            {
                spc.AddSource(SnapshotMetadataEmitter.IdentityHintName, identityCode);
            }
        });
    }
}
