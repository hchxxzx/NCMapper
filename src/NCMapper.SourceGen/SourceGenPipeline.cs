// ============================================================================
// SourceGenPipeline — NCMapper 源生成器「六段管线装配」共享函数
// ----------------------------------------------------------------------------
// 收敛说明：原 2 处重复（RuntimeSnapshotGenerator.cs / NCMapperSourceGenerator.cs），
// 收敛于此；结构债源流 = （快照管线补两环）
// → 「判据单点化结构债归批次 V 」
// → 归并表（并入；登记脱节经 补登订正为「同族不同实体」）
//   → 清点文档 20260921- / → 承接核销。
// 等价承诺：本函数为六段装配的语义不变归位（跨文件归位 + 二合一去重），不改变任何
//   transform / 谓词 / Combine 结构与节点语义；改造前后生成产物字节一致，由字节门 8 腿
//   与 PublicApiTests 快照门守门——不得在本函数内顺手改写语义。
// 段级溯源（两处注释并集保留，TASK-ID 逐条留痕，禁为减行数删注释）：
//   ① 主 （transform 出口值化）＋ ② 主 （Fluent 通道入口锚点）
//   ③ 主 （方案 1 / F49 孤儿诊断）＋ 缺环①
//   ④ 统一合并产物 ＋ ⑤ 主 （三层优先链）＋ 缺环②
//   ⑥ 主 （配置感知修正）＋ （改喂双通道合并产物）
// ============================================================================
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// 源生成器管线装配共享函数（六段：扫描 ×2 + 合成并入 + 双通道仲裁 + 配置三层链 + 配置应用）
/// <para>调用方：NCMapperSourceGenerator（三产物全需）/ RuntimeSnapshotGenerator（仅需配置应用产物）。</para>
/// </summary>
internal static class SourceGenPipeline
{
    /// <summary>
    /// 构造六段装配管线并返回两生成器薄壳所需的全部产物
    /// </summary>
    /// <param name="context">增量生成器初始化上下文</param>
    /// <returns>
    /// (mergedMappings, mergedConfig, configuredMappings)——
    /// 主生成器三样全需（mergedMappings 供 NCMAP019 独立输出节点输入、mergedConfig 供主输出链与
    /// NCMAP020 上移段、configuredMappings 供主输出链）；快照生成器仅需 configuredMappings。
    /// 不得以 configuredMappings 反推替代前两者（节点输入取值时点与输出级缓存失效时机属设计点）。
    /// </returns>
    internal static (
        IncrementalValueProvider<ChannelMergeResult> MergedMappings,
        IncrementalValueProvider<GlobalConfigSnapshot> MergedConfig,
        IncrementalValueProvider<ChannelMergeResult> ConfiguredMappings) Build(
            IncrementalGeneratorInitializationContext context)
    {
        // 扫描标注了 [MapFrom] 的 DTO 类
        // transform 出口已值化——ExtractMappingInfo 内部完成符号解析，
        // 返回前 Materialize 为纯值 MappingSnapshot（缓存值零符号持有）
        // （原快照侧注释：[MapFrom] 通道（与主生成器同款物化调用））
        var mapFromClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.MapFrom,
                predicate: (node, ct) => node is TypeDeclarationSyntax,
                transform: (ctx, ct) => MappingExtractor.ExtractMappingInfo(ctx, ct))
            .Where(m => m != null)
            .Collect();

        // Fluent 通道扫描——[MapperConfig] 类内 Configure 方法
        // 链式声明提取 + 完整物化（与 [MapFrom] 通道同模式 ForAttributeWithMetadataName 增量锚点，
        // Q6/A1.5；S-8a；transform 出口值化——FluentExtractionResult 零符号持有）
        // （原快照侧注释：Fluent 通道（与主生成器同款物化调用））
        var fluentClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.MapperConfig,
                predicate: (node, ct) => node is TypeDeclarationSyntax,
                transform: (ctx, ct) => FluentExtractor.Extract(ctx, ct))
            .Where(f => f != null)
            .Collect();

        // 特性侧 [MapSubclass] 合成管道（方案 1——扫描全部
        // [MapSubclass] 标注类：有 [MapFrom] 锚定 → 未独立声明子类对合成快照（SyntheticDeclarations
        // 平行流）；无锚定 → F49 孤儿诊断。产物融入 fluentResults 流（伪 FluentExtractionResult，
        // Declarations 恒空不进冲突面——Arbiter 三查去重天然覆盖）
        // （缺环①）：特性侧 [MapSubclass] 合成管道并入——主生成器
        // NCMapperSourceGenerator.cs:57-63 / 同款（伪 FluentExtractionResult，Declarations 恒空，
        // 合成对经 Arbiter 三查去重）。原实现缺本环 → 快照元数据缺 MapSubclass 合成对，
        // 与生成代码取值不同源（:44 注释自称"同构"而实现缺两环）。
        var subclassAttrClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.MapSubclass,
                predicate: (node, ct) => node is TypeDeclarationSyntax,
                transform: (ctx, ct) => SubclassSyntheticPipeline.Extract(ctx, ct))
            .Where(f => f != null)
            .Collect();

        // fluentResults 流并入特性侧 [MapSubclass] 合成管道产物
        //（伪结果——Declarations 恒空，合成对经 Arbiter 三查去重）
        var mergedFluentResults = fluentClasses
            .Combine(subclassAttrClasses)
            .Select((pair, ct) => pair.Left.AddRange(pair.Right));

        // 双通道合并仲裁（与主生成器同款——L-1 消费统一合并产物）
        var mergedMappings = mapFromClasses
            .Combine(mergedFluentResults)
            .Select((pair, ct) => DualDriverArbiter.MergeAndArbitrate(pair.Left, pair.Right, ct));

        // 全局映射配置：三层优先链 — MSBuild 属性 + [assembly: MapDefaults] 特性
        // 逐项非空覆盖合并（特性 > MSBuild > 内建默认）；快照为值对象，配置变化自动触发重生成
        // （缺环②）：全局配置三层链 + ApplyGlobalConfig——主生成器
        // NCMapperSourceGenerator.cs:78-85 / 同款（MSBuild 属性 + [assembly: MapDefaults]
        // 逐项非空覆盖合并，特性 > MSBuild > 内建默认）。ApplyGlobalConfig 实质修正快照内嵌
        // Diagnostics（全局忽略加 NCMAP021、抑制 NCMAP007 等）——原实现缺本环。
        var msbuildConfig = context.AnalyzerConfigOptionsProvider
            .Select((provider, ct) => GlobalConfigReader.ReadFromMSBuild(provider.GlobalOptions));

        var assemblyConfig = context.CompilationProvider
            .Select((compilation, ct) => GlobalConfigReader.ReadFromAssemblyAttribute(compilation, ct));

        var mergedConfig = msbuildConfig.Combine(assemblyConfig)
            .Select((pair, ct) => GlobalConfigReader.Merge(pair.Right, pair.Left));

        // 配置感知修正：配置非默认时对快照副本执行策略/前后缀匹配 + 全局忽略 + NCMAP021/ 诊断
        // （值模型不可变：修正产生新快照实例，上游 transform 缓存天然不受污染；配置空时原样返回，增量缓存零开销）
        // 改喂双通道合并产物（[MapFrom] + Fluent 统一经 DualDriverArbiter
        // 合并后进入配置修正——两通道统一消费全局配置；ExtraDiagnostics 透传不参与修正）
        var configuredMappings = mergedMappings
            .Combine(mergedConfig)
            .Select((pair, ct) => new ChannelMergeResult(
                MappingExtractor.ApplyGlobalConfig(pair.Left.Mappings, pair.Right, ct),
                pair.Left.ExtraDiagnostics));

        return (mergedMappings, mergedConfig, configuredMappings);
    }
}
