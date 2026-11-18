using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
namespace NCMapper.SourceGen;
    /// <summary>
    /// NCMapper 增量源生成器
    /// 自动扫描 [MapFrom(typeof(Source))] 标注的 DTO 类，编译时生成等价映射方法
    /// 无需额外标注 [MapperGenerator]，只要有 [MapFrom] 就自动生成
    /// 生成的代码放在 NCMapper.Generated.GeneratedMapper 静态类中
    /// </summary>
    [Generator]
    public class NCMapperSourceGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// 初始化增量生成器管线 — 注册语法过滤器、语义转换和输出阶段
        /// </summary>
        /// <param name="context">增量生成器初始化上下文</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // （判据单点化）：原六段装配（[MapFrom] 扫描 / Fluent 扫描 /
            // MapSubclass 合成并入 / 双通道合并仲裁 / 全局配置三层链 / ApplyGlobalConfig 配置应用）
            // 已收敛至 SourceGenPipeline.Build 单点——原 2 处重复（本文件与 RuntimeSnapshotGenerator.cs）。
            // 段级溯源（两处注释并集）见 SourceGenPipeline.cs 头注。
            // 三产物在本生成器各有消费点：mergedMappings → 下方 NCMAP019 独立输出节点输入（fluentTargetClassNames）；
            // mergedConfig → 主输出链 combinedWithDiag 与 NCMAP020 上移段；configuredMappings → 主输出链。
            var (mergedMappings, mergedConfig, configuredMappings) = SourceGenPipeline.Build(context);

            var globalOptions = context.AnalyzerConfigOptionsProvider
                .Select((provider, ct) => provider.GlobalOptions);

            // NCMAP019: 检测有映射特性但缺少 [MapFrom] 的类型
            var missingMapFromDiagnostics = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, ct) => MappingExtractor.HasMappingAttributesWithoutMapFrom(node, ct),
                    transform: (ctx, ct) => MappingExtractor.ExtractMissingMapFromDiagnostic(ctx, ct))
                .Where(d => d != null)
                .Collect();

            // 主 SourceOutput 链剔除 missingDiags 与 CompilationProvider
            // NCMAP019 的 CreateSyntaxProvider 对无关语法树变化结构性重跑（Collect 引用比较 Modified），
            // 拆独立输出节点；源输出链仅保留配置合并所需的 CompilationProvider 消费（mergedConfig），位置重建与 DeepCopy 值域分类零反查。
            // 无关编辑时主链输入全 Unchanged/Cached → 输出级缓存命中（回调不执行、源文本复用）
            var combinedWithDiag = configuredMappings.Combine(globalOptions)
                .Combine(mergedConfig);

            context.RegisterSourceOutput(combinedWithDiag, (spc, source) =>
            {
                var ((merged, globalOpts), globalConfig) = source;
                var mappings = merged.Mappings;

                // NCMapperSourceGenEnabled=false 时同时抑制生成和诊断
                var sourceGenEnabled = true;
                if (globalOpts.TryGetValue(
                    "build_property.NCMapperSourceGenEnabled", out var enabledStr))
                {
                    sourceGenEnabled = !string.Equals(enabledStr, "false", System.StringComparison.OrdinalIgnoreCase);
                }
                if (!sourceGenEnabled) return;

                // Fluent 通道独立诊断流发射（F02/F04~F08/F12~F15 +
                // F11 仲裁产物）——先于空映射早退（F15-only 场景 mappings 为空仍须发射）；
                // 快照内诊断（NCMAP001/002 等）仍走既有 EmitDiagnostics 合并消费
                EmitFluentDiagnostics(spc, merged.ExtraDiagnostics);

                // 全局配置非法值诊断（NCMAP020）上移至空映射
                // 早退之前——缺陷态：空映射（无任何 [MapFrom]/Fluent 声明的工程）+ 非法全局配置
                // （如 string[] 清单项含反引号走 Q3 边界降级）→ 早退吞掉 NCMAP020；与 L-09 开关门
                // :125 的"关闭即抑制"相对顺序保持（globalConfig 于 解构，作用域可达）
                if (globalConfig != null)
                {
                    foreach (var diag in globalConfig.Diagnostics)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(diag.Descriptor, Location.None, diag.Args));
                    }
                }

                if (mappings.IsDefaultOrEmpty) return;

                // per-run 生成期诊断收集上下文（缓存快照只读）
                var generationContext = new GenerationContext(spc.CancellationToken);
                // 发射前准备段 — 输出回调内、GenerateMapperCode 之前
                // 一次构造（上提三方法 + C1 反向表 + C3 环检测 + C4 深拷贝分类），
                // 发射期零决策、零回调 transform 期类；不进增量管线 step（step 划分零变化）
                var prep = GenerationPreparation.Prepare(spc, mappings, globalConfig, generationContext, generationContext.Diagnostics);
                // 输出期零 Resolver 依赖（Combine 已解除）；Compilation 仅经 mergedConfig 配置合并消费
                CodeGenerator.GenerateMapperCode(spc, mappings, prep, globalConfig, generationContext);
                EmitDiagnostics(spc, mappings, generationContext);

                // 映射关系报告开关 — 默认关闭
                if (globalOpts.TryGetValue(
                    "build_property.NCMapperGenerateReport", out var reportStr)
                    && System.String.Equals(reportStr, "true", System.StringComparison.OrdinalIgnoreCase))
                {
                    CodeGenerator.GenerateReport(spc, mappings);
                }
            });

            // NCMAP019 独立输出节点（脱离主链，防无关编辑破坏输出级缓存）
            // 节点输入从「缺失诊断数组」扩为
            //（缺失诊断数组, Fluent 目标类名集合 snapshot）——集合为值对象（TargetClassName 序），
            // 编辑 MapperConfig 增/删 For<S,T> → 集合值变化 → 节点值不等 → 输出级缓存失效重算。
            // 谓词语法级快速否定保留（MappingExtractor.HasMappingAttributesWithoutMapFrom 不动）；
            // 豁免在输出期对命中集合的类型跳过 ReportDiagnostic——019 语义针对「无任何通道声明」的类型，
            // Fluent 通道声明（cfg.For<S,T>()）同样构成映射声明，纯语法级谓词不可感知故产生误报。
            var fluentTargetClassNames = mergedMappings
                .Select((result, ct) => (System.Collections.Immutable.ImmutableHashSet<string>)result.Mappings
                    .Where(m => m != null && m.IsFluentForward && !m.SkipCodeGeneration)
                    .Select(m => m!.TargetClassName)
                    .ToImmutableHashSet(StringComparer.Ordinal));

            // 节点输入接入 globalOptions——原输入不含全局选项，
            // NCMapperSourceGenEnabled=false 时主链 抑制但 019 仍持续报出（抑制语义不一致）。
            // 接入后开关关闭 → 节点输入值变化触发重算 + 输出期同口径门禁（下方回调控）。
            var missingMapFromWithFluent = missingMapFromDiagnostics
                .Combine(fluentTargetClassNames)
                .Combine(globalOptions);

            context.RegisterSourceOutput(missingMapFromWithFluent, (spc, source) =>
            {
                var ((missingDiags, fluentTargets), globalOpts) = source;
                // L-09 同口径：开关关闭时与主链一致抑制诊断（B4-05——对照 主链门禁）
                if (globalOpts.TryGetValue(
                    "build_property.NCMapperSourceGenEnabled", out var enabledStr))
                {
                    var sourceGenEnabled = !string.Equals(enabledStr, "false", System.StringComparison.OrdinalIgnoreCase);
                    if (!sourceGenEnabled) return;
                }
                if (missingDiags.IsDefaultOrEmpty) return;
                foreach (var loc in missingDiags)
                {
                    if (loc == null) continue;
                    // D2 豁免：Fluent 通道声明的目标类型跳过（豁免在输出期——谓词不动）。
                    // 豁免守卫对 / 两码同用（语义 = 无生成代码 →
                    // AOT/Trimmed 反射回退不可用的提示；Fluent For<S,T>() 目标经 Fluent 通道生成
                    // 代码，提示前提不成立—— 显式决策，与 豁免同源）
                    if (fluentTargets.Contains(loc.Value.ClassName)) continue;
                    // T1 拆码分派：场景①（映射配置特性无锚）→ NCMAP019 Error；
                    // 场景②（纯 Dto 命名）→ NCMAP042 Info
                    var descriptor = loc.Value.IsDtoNamingOnly
                        ? DiagnosticDescriptors.DtoNamingWithoutMapFromRule
                        : DiagnosticDescriptors.MissingMapFromWithRelationConfigRule;
                    spc.ReportDiagnostic(Diagnostic.Create(
                        descriptor,
                        RebuildLocation(loc.Value.Location), loc.Value.ClassName));
                }
            });
        }

        /// <summary>
        /// 输出 Fluent 通道独立诊断流— 快照诊断重建 Location
        ///（与 EmitDiagnostics 同口径：external location + FromId 反查 + GenerationFailedRule 兜底）
        /// </summary>
        /// <param name="spc">源生产上下文，用于报告诊断</param>
        /// <param name="diagnostics">通道级诊断快照集合（F02/F04~F08/F12~F15 + F11 仲裁产物）</param>
        private static void EmitFluentDiagnostics(SourceProductionContext spc, System.Collections.Immutable.ImmutableArray<DiagnosticSnapshot> diagnostics)
        {
            if (diagnostics.IsDefaultOrEmpty) return;
            foreach (var diag in diagnostics)
            {
                var location = RebuildLocation(diag.Location);
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.FromId(diag.DescriptorId) ?? DiagnosticDescriptors.GenerationFailedRule,
                    location,
                    diag.Args.ToArray()));
            }
        }

        /// <summary>
        /// 输出诊断信息 — 快照诊断重建 Location
        /// <para>(FilePath, TextSpan) → 当前编译树匹配 → span 边界校验（Start&gt;=0 &amp;&amp; End&lt;=tree.Length，
        /// 越界回退 Location.None——增量重编译时快照 span 来自旧树，新树可能更短）→ Location.Create。</para>
        /// </summary>
        /// <param name="spc">源生产上下文，用于报告诊断</param>
        /// <param name="mappings">映射信息集合，包含待输出的诊断信息</param>
        /// <param name="generationContext">生成期上下文（per-run 追加诊断收集器）</param>
        private static void EmitDiagnostics(SourceProductionContext spc, ImmutableArray<MappingSnapshot?> mappings, GenerationContext generationContext)
        {
            foreach (var mapping in mappings)
            {
                if (mapping == null) continue;
                // 合并消费「transform 初始诊断 + per-run 追加诊断」，缓存快照只读
                foreach (var diag in mapping.Diagnostics.Concat(generationContext.Diagnostics.GetAppended(mapping)))
                {
                    // NCMAP001/NCMAP007 按注入后选项过滤（类级 > 程序集级 > 默认优先级在
                    // ApplyGlobalConfig 注入后落定于快照选项字段）
                    // 显式 [MapProperty] FromField 错误（IsExplicit）始终报，不受选项抑制（保真）
                    if (diag.DescriptorId == "NCMAP001" && !mapping.ReportUnmappedTargetMembers && !diag.IsExplicit) continue;
                    if (diag.DescriptorId == "NCMAP007" && !mapping.ReportUnmappedSourceMembers) continue;
                    var location = RebuildLocation(diag.Location);
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.FromId(diag.DescriptorId) ?? DiagnosticDescriptors.GenerationFailedRule,
                        location,
                        diag.Args.ToArray()));
                }
            }
        }

        /// <summary>
        /// 重建诊断位置 — LocationSnapshot → external Location（S3，：不依赖 Compilation；
        /// 物化 LineSpan 承载跨编译行号； 树匹配/span 校验语义由物化期保证，差异登记）
        /// </summary>
        private static Location RebuildLocation(LocationSnapshot? snapshot)
        {
            if (snapshot == null) return Location.None;
            return Location.Create(snapshot.Value.FilePath, snapshot.Value.Span, snapshot.Value.LineSpan);
        }
    }
