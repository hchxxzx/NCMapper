using System.Text;
using Microsoft.CodeAnalysis;
namespace NCMapper.SourceGen;

/// <summary>
/// 代码生成器（Compose 合成生成 + 扩展方法生成）— 部分更新方法、Compose 合成方法、零委托直调扩展方法
/// </summary>
internal static partial class CodeGenerator
{
    #region 扩展方法生成

    /// <summary>
    /// 生成扩展方法类 — 管道为空时零委托直调，管道启用时自动触发 PostMap
    /// <para>用户可直接调用<c>src.MapToTarget()</c>无需关心 Pipeline 状态；互指对跳过</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mappings">映射信息集合</param>
    /// <param name="mutualPairs">互指有向对字典</param>
    /// <param name="suffixMap">方法后缀表（输出期查表）</param>
    /// <param name="reverseSnapshotTable"> 唯一构造点表（mapping → 过滤后反向快照；null = 无反向）</param>
    /// <param name="generationContext">生成期上下文（G4 跳过清单来源；可为 null）</param>
    private static void GenerateExtensionClass(StringBuilder sb, System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings, Dictionary<TypeRef, TypeRef> mutualPairs, Dictionary<string, string>? suffixMap = null, Dictionary<MappingSnapshot, MappingSnapshot?>? reverseSnapshotTable = null, GenerationContext? generationContext = null)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 扩展方法 — 管道为空时直调 Core 方法（零委托），管道启用时自动触发 PostMap");
        sb.AppendLine("    /// <para>用法：<c>var dto = entity.MapToTarget();</c></para>");
        sb.AppendLine("    /// </summary>");

        sb.AppendLine("    internal static class GeneratedExtensions");
        sb.AppendLine("    {");

        foreach (var mapping in mappings)
        {
            if (mapping == null) continue;
            // NCMAP011：non-public 类型跳过扩展方法生成（引用 non-public 类型会 CS0122）
            if (mapping.SkipCodeGeneration) continue;
            // 互指对不生成扩展方法
            if (IsMutualPair(mapping, mutualPairs)) continue;
            GenerateExtensionMethod(sb, mapping, suffixMap);

            // 反向扩展生成 — 所有 [MapFrom] DTO（含 Compose 型）统一生成反向 6 重载，与正向对称；
            // 反向快照消费 唯一构造点表（过滤后同一实例——三消费方同源，防 .1）。
            // Compose 型 DTO 反向只覆盖主源字段（ComposeMappings 不参与反转——反向快照仅含
            // 主源 PropertyMappings），与普通 DTO 行为一致；子源字段不参与反向写回见 XML 注释。
            // 注：Mapper.Compose 仅正向合成（多源→DTO，TTarget 须 [MapFrom]，ComposeRuntime 校验）；
            // 反向直调（dto.MapTo主源Entity）是 [MapFrom] 统一反向能力，与 Compose 无关，统一生成。
            var reverseMapping = reverseSnapshotTable != null && reverseSnapshotTable.TryGetValue(mapping, out var rev)
                ? rev
                : null;
            if (reverseMapping != null)
            {
                var docNotes = new List<string>();
                // 含跨表子源字段的 DTO：反向扩展 XML 注释说明子源字段不参与反向写回
                if (mapping.ComposeMappings.Count > 0)
                {
                    var subSourceNames = string.Join("、",
                        mapping.ComposeMappings.Select(c => c.SubSource.SimpleName).Distinct());
                    docNotes.Add($"本 DTO 含 {mapping.ComposeMappings.Count} 个子源字段（来自 {subSourceNames}），不参与反向写回");
                }
                // 反向跳过清单 — 构造点 NCMAP013 登记的属性名（去重）拼入 XML 注释
                if (generationContext != null)
                {
                    var skipped = generationContext.Diagnostics.GetAppended(mapping)
                        .Where(d => d.DescriptorId == DiagnosticDescriptors.ReverseMappingSkippedRule.Id && d.Args.Count > 0)
                        .Select(d => d.Args[0])
                        .Distinct()
                        .ToList();
                    if (skipped.Count > 0)
                        docNotes.Add($"反向跳过属性：{string.Join("、", skipped)}");
                }
                GenerateExtensionMethod(sb, reverseMapping, suffixMap, docNotes.Count > 0 ? string.Join("；", docNotes) : null);
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>
    /// 为单个映射生成扩展方法（单对象无 Action + 单对象带 Action + 列表 + 更新）
    /// <para>Pipeline 守卫：管道启用时自动触发 PostMap，管道为空时仅一次 volatile 读取（~0.3ns）</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="suffixMap">方法后缀表（可为 null → 空后缀）</param>
    /// <param name="extraDocNote">附加到 XML 注释 summary 的说明行（
    /// Compose 型反向扩展的“子源字段不参与反向写回”说明；null 不追加）</param>
    private static void GenerateExtensionMethod(StringBuilder sb, MappingSnapshot mapping, Dictionary<string, string>? suffixMap = null, string? extraDocNote = null)
    {
        var sourceName = mapping.SourceType.FullName;
        var targetName = mapping.TargetType.FullName;
        var methodSuffix = suffixMap != null ? GetSuffix(suffixMap, mapping) : "";
        var coreMethodName = GetMethodNameWithSuffix(mapping, "Map", methodSuffix);
        var actionMethodName = $"{coreMethodName}_WithAction";
        var listMethodName = GetMethodNameWithSuffix(mapping, "MapList", methodSuffix);
        var updateMethodName = $"{coreMethodName}_Update";
        var targetClassWithSuffix = $"{mapping.TargetClassName}{methodSuffix}";
        var extraNoteLine = extraDocNote != null ? $"        /// <para>{extraDocNote}</para>" : null;

        // ── 单对象 — 无 Action ──
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 直接映射：{sourceName} → {targetName}（Pipeline 启用时自动触发 PostMap）");
        if (extraNoteLine != null) sb.AppendLine(extraNoteLine);
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public static {targetName}? MapTo{targetClassWithSuffix}(this {sourceName} src)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            if (!NCMapper.MapPipeline.IsPipelineEnabled)");
        sb.AppendLine($"                return GeneratedMapper.{coreMethodName}(src);");
            sb.AppendLine($"            var __dst = GeneratedMapper.{coreMethodName}(src);");
        sb.AppendLine($"            if (__dst != null) NCMapper.MapPipeline.RunPostMap(src, __dst);");
        sb.AppendLine($"            return __dst;");
        sb.AppendLine($"        }}");
        sb.AppendLine();

        // ── 单对象 — 带 Action ──
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 直接映射：{sourceName} → {targetName}（带 Action 回调，Pipeline 启用时自动触发 PostMap）");
        if (extraNoteLine != null) sb.AppendLine(extraNoteLine);
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public static {targetName}? MapTo{targetClassWithSuffix}(this {sourceName} src, System.Action<{sourceName}, {targetName}> action)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            if (!NCMapper.MapPipeline.IsPipelineEnabled)");
        sb.AppendLine($"                return GeneratedMapper.{actionMethodName}(src, action);");
            sb.AppendLine($"            var __dst = GeneratedMapper.{actionMethodName}(src, action);");
        sb.AppendLine($"            if (__dst != null) NCMapper.MapPipeline.RunPostMap(src, __dst);");
        sb.AppendLine($"            return __dst;");
        sb.AppendLine($"        }}");
        sb.AppendLine();

        // ── 列表 — 无 Action（Pipeline 启用时降级到 Mapper.MapList 逐元素触发）──
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 列表映射：IEnumerable&lt;{mapping.SourceClassName}&gt; → List&lt;{mapping.TargetClassName}&gt;（Pipeline 启用时逐元素触发 PostMap）");
        if (extraNoteLine != null) sb.AppendLine(extraNoteLine);
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public static System.Collections.Generic.List<{targetName}>? MapTo{targetClassWithSuffix}List(this System.Collections.Generic.IEnumerable<{sourceName}> sources)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            if (NCMapper.MapPipeline.IsPipelineEnabled)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                // Fallback path: Pipeline degradation to runtime MapList (per-element PostMap). May fail under NativeAOT; see docs 05-Compose");
        sb.AppendLine($"#pragma warning disable IL2026, IL3050");
        sb.AppendLine($"                return NCMapper.Mapper.MapList<{sourceName}, {targetName}>(sources);");
        sb.AppendLine($"#pragma warning restore IL2026, IL3050");
        sb.AppendLine($"            }}");
        sb.AppendLine($"            return GeneratedMapper.{listMethodName}(sources);");
        sb.AppendLine($"        }}");
        sb.AppendLine();

        // ── 列表 — 带 Action ──
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 列表映射（带 Action 回调）：IEnumerable&lt;{mapping.SourceClassName}&gt; → List&lt;{mapping.TargetClassName}&gt;（Pipeline 启用时逐元素触发 PostMap）");
        if (extraNoteLine != null) sb.AppendLine(extraNoteLine);
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public static System.Collections.Generic.List<{targetName}>? MapTo{targetClassWithSuffix}List(this System.Collections.Generic.IEnumerable<{sourceName}> sources, System.Action<{sourceName}, {targetName}> action)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            if (NCMapper.MapPipeline.IsPipelineEnabled)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                // Fallback path: Pipeline degradation to runtime MapList with action (per-element PostMap). May fail under NativeAOT; see docs 05-Compose");
        sb.AppendLine($"#pragma warning disable IL2026, IL3050");
        sb.AppendLine($"                return NCMapper.Mapper.MapList<{sourceName}, {targetName}>(sources, action);");
        sb.AppendLine($"#pragma warning restore IL2026, IL3050");
        sb.AppendLine($"            }}");
        sb.AppendLine($"            return GeneratedMapper.{listMethodName}(sources, action);");
        sb.AppendLine($"        }}");
        sb.AppendLine();

        // ── 更新已有对象 — 无 Action ──
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 更新映射：{sourceName} → {targetName}（更新已有目标对象，Pipeline 启用时自动触发 PostMap）");
        if (extraNoteLine != null) sb.AppendLine(extraNoteLine);
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public static void MapTo{targetClassWithSuffix}(this {sourceName} src, {targetName} dst)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            if (!NCMapper.MapPipeline.IsPipelineEnabled)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                GeneratedMapper.{updateMethodName}(src, dst, null);");
        sb.AppendLine($"                return;");
        sb.AppendLine($"            }}");
            sb.AppendLine($"            GeneratedMapper.{updateMethodName}(src, dst, null);");
        sb.AppendLine($"            NCMapper.MapPipeline.RunPostMap(src, dst);");
        sb.AppendLine($"        }}");
        sb.AppendLine();

        // ── 更新已有对象 — 带 Action ──
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 更新映射：{sourceName} → {targetName}（更新已有目标对象，带 Action 回调，Pipeline 启用时自动触发 PostMap）");
        if (extraNoteLine != null) sb.AppendLine(extraNoteLine);
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public static void MapTo{targetClassWithSuffix}(this {sourceName} src, {targetName} dst, System.Action<{sourceName}, {targetName}> action)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            if (!NCMapper.MapPipeline.IsPipelineEnabled)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                GeneratedMapper.{updateMethodName}(src, dst, action);");
        sb.AppendLine($"                return;");
        sb.AppendLine($"            }}");
            sb.AppendLine($"            GeneratedMapper.{updateMethodName}(src, dst, action);");
        sb.AppendLine($"            NCMapper.MapPipeline.RunPostMap(src, dst);");
        sb.AppendLine($"        }}");
        sb.AppendLine();
    }

    #endregion 扩展方法生成

    #region Compose 合成生成

    /// <summary>
    /// 为每个子源类型生成部分更新方法 — 只映射该子源的字段到已有 DTO
    /// <para>生成的代码零反射、零 MakeGenericMethod，完全 AOT 友好</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息（包含 ComposeMappings）</param>
    /// <param name="methodSuffix">方法名后缀</param>
    public static void GenerateComposeUpdateMethods(StringBuilder sb, MappingSnapshot mapping, string methodSuffix)
    {
        var targetName = mapping.TargetType.FullName;

        // 按子源类型分组
        var subSources = mapping.ComposeMappings
            .GroupBy(c => c.SubSource)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in subSources)
        {
            var subSourceSymbol = kvp.Key;
            var subMappings = kvp.Value;
            var subSourceName = subSourceSymbol.FullName;
            var subSourceClassName = subSourceSymbol.SimpleName;
            var methodName = $"Update_{subSourceClassName}_To_{mapping.TargetClassName}{methodSuffix}";

            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Compose 部分更新：{subSourceName} → {targetName}（仅映射该子源的字段）");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");

            sb.AppendLine($"        public static void {methodName}({subSourceName} src, {targetName} dst)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            if (src is null || dst is null) return;");

            // 缩进变量（方法体内 12 空格），供 TryGenerateBasicTypeConversion / GenerateConversionFallback 使用
            var indent = "            ";

            // 逐属性赋值
            foreach (var cm in subMappings)
            {
                // 跳过 init-only 属性 — Compose 更新方法操作已有对象，init-only 属性无法后续赋值
                if (cm.Target.IsInitOnly) continue;

                // 使用共用 TryGenerateBasicTypeConversion + GenerateConversionFallback
                // 显式点路径多段访问（src.A?.B 形态，与 BuildSourceAccess 同构；
                // cm.Source 为末段属性，Type/转换入参天然取末段类型——C7 null 安全语义由 ?. 链承载）
                // 触发条件含单段集合索引（"B[0]"，Count==1）；
                // 类型入参用有效源类型（末段索引=元素类型）；BuildComposeSourceAccess 内已含索引包装
                var rawAccess = cm.SourcePath.Count > 1 || cm.HasIndex
                    ? BuildComposeSourceAccess(cm)
                    : $"src.{SourceGenEscape.Identifier(cm.Source.Name)}";

                // 块内 MapWhen 守卫包裹——发射落点 = 本赋值层
                //（Update_{Sub}_To_{Target}）；T1 局部变量承载（源值一次求值）+ if 守卫开行，
                // srcAccess 换为局部变量名；各赋值分支 continue 前与循环体尾闭合 }。无守卫零输出（字节不变门）
                var whenOpened = EmitComposeWhenGuardOpen(sb, cm, rawAccess, indent, out var cmSrcOverride);
                var srcAccess = cmSrcOverride ?? rawAccess;
                var composeSrcType = cm.SourceIndexes.Count > 0 && cm.SourceIndexes[cm.SourceIndexes.Count - 1] is int
                    ? (cm.Source.Type.EnumerableElementType ?? cm.Source.Type)
                    : cm.Source.Type;

                // / 跨表字段消费 [Convert]（source=子源 src，优先于类型转换兜底）
                // 内联 global::FQN.M(args)（四模板 + 守卫变体；T1 M(末段值)/T2 M(子源)/T3 M(子源, 末段值)）；
                // 输出 null → 不写叶（N-1 语义，与反向写回同口径）；接口形态（退役期）经类型转换兜底
                // 四段序分支①：converter+mask 共存 → 先转换后脱敏（直包无守卫
                // 镜像运行时 ConvertUsingThenMaskStrategy/主源，null 转换输出 → Mask(null)
                // 而非"不写叶"；纯 converter 形维持既有 N-1 守卫不变）
                if (cm.ConverterDeclaringType != null)
                {
                    var convProp = SanitizeIdentifier(cm.Target.Name);
                    var convertExpr = BuildStaticConverterCall(cm.ConverterDeclaringType, cm.ConverterMethodName!,
                        cm.ConverterShape, srcAccess, "src");
                    if (cm.MaskType != null)
                    {
                        var maskClassName = cm.Target.ContainingType?.SimpleName ?? "Anon";
                        var maskFieldName = BuildMaskFieldName(maskClassName, methodSuffix, cm.Target.Name);
                        sb.AppendLine($"{indent}dst.{BuildMaskedAssignment(cm.Target.Name, maskFieldName, convertExpr)};");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}var __convResult_{convProp} = {convertExpr};");
                        sb.AppendLine($"{indent}if (__convResult_{convProp} != null)");
                        sb.AppendLine($"{indent}    dst.{SourceGenEscape.Identifier(cm.Target.Name)} = __convResult_{convProp};");
                    }
                    if (whenOpened) sb.AppendLine($"{indent}}}");
                    continue;
                }

                // 四段序分支②：纯 [Mask] 脱敏（直包，镜像主源 与
                // 运行时 MaskStrategy——两形独立，此形同样无 null 守卫；DTO 目标 string 已由
                // 提取端 NCMAP015 校验保证，仅校验通过才置位 MaskType）
                // FormatThenMask 双标分支插在纯 Mask 之前（镜像主源位置语义
                // 双标下源本非 string， 源 string 守卫不适用）；Format 单标分支随其后（无效源 fall through）
                if (cm.MaskType != null && cm.FormatString != null)
                {
                    var effFmtSrc = composeSrcType;
                    if (cm.Target.Type.SpecialType == SpecialType.System_String
                        && GenerationPreparation.IsFormattableTypeRef(effFmtSrc))
                    {
                        var maskClassName = cm.Target.ContainingType?.SimpleName ?? "Anon";
                        var maskFieldName = BuildMaskFieldName(maskClassName, methodSuffix, cm.Target.Name);
                        var formatted = BuildFormattedAssignment(srcAccess, cm.FormatString!, IsNullableFormattedAccess(effFmtSrc), IsEnumFormattedSource(effFmtSrc));
                        sb.AppendLine($"{indent}dst.{BuildMaskedAssignment(cm.Target.Name, maskFieldName, formatted)};");
                        if (whenOpened) sb.AppendLine($"{indent}}}");
                        continue;
                    }
                }

                if (cm.MaskType != null)
                {
                    // （生成侧守卫，Compose 通道）：有效源类型（末段，
                    // composeSrcType 口径）非 string 不发射 Mask 裸调用（CS1503），fall through
                    // 分支③④——运行时镜像 AssignmentPlanner.PlanCore :124-139。whenOpened 闭合责任随
                    // continue 转移，落空时由分支③④/循环尾闭合
                    if (composeSrcType.SpecialType == SpecialType.System_String)
                    {
                        var maskClassName = cm.Target.ContainingType?.SimpleName ?? "Anon";
                        var maskFieldName = BuildMaskFieldName(maskClassName, methodSuffix, cm.Target.Name);
                        sb.AppendLine($"{indent}dst.{BuildMaskedAssignment(cm.Target.Name, maskFieldName, srcAccess)};");
                        if (whenOpened) sb.AppendLine($"{indent}}}");
                        continue;
                    }
                }

                // Format 单标（Compose 通道——无效源 fall through 分支③④，
                // 生成侧守卫口径；whenOpened 闭合责任随 continue 转移）
                if (cm.FormatString != null)
                {
                    if (cm.Target.Type.SpecialType == SpecialType.System_String
                        && GenerationPreparation.IsFormattableTypeRef(composeSrcType))
                    {
                        var formatted = BuildFormattedAssignment(srcAccess, cm.FormatString!, IsNullableFormattedAccess(composeSrcType), IsEnumFormattedSource(composeSrcType));
                        sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(cm.Target.Name)} = {formatted};");
                        if (whenOpened) sb.AppendLine($"{indent}}}");
                        continue;
                    }
                }

                // 四段序分支③：[MapProperty(IsFromJson=true)] JSON 转换
                //（镜像主源 三形态；值类型守卫已由提取端 落地——非 string 值类型整条丢弃；
                // 源类型用有效源类型 composeSrcType——多段/末段索引取末段类型，与下方类型转换链入参一致）
                if (cm.IsFromJson)
                {
                    var jsonSrcIsString = composeSrcType.SpecialType == SpecialType.System_String;
                    var jsonTgtIsString = cm.Target.Type.SpecialType == SpecialType.System_String;

                    if (jsonSrcIsString && !jsonTgtIsString)
                    {
                        // string → 对象：反序列化
                        sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(cm.Target.Name)} = global::NCMapper.JsonConverter.JsonConvertToObject<{cm.Target.Type.FullName}>({srcAccess});");
                    }
                    else if (!jsonSrcIsString && jsonTgtIsString)
                    {
                        // 对象 → string：序列化（null 守卫与运行时 GetJsonByObjectExpression 对齐——null 源 → null）
                        sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(cm.Target.Name)} = {srcAccess} == null ? null : global::NCMapper.JsonConverter.ObjectConvertToJson({srcAccess});");
                    }
                    else
                    {
                        // string → string：直接赋值
                        sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(cm.Target.Name)} = {srcAccess};");
                    }
                    if (whenOpened) sb.AppendLine($"{indent}}}");
                    continue;
                }

                if (TryGenerateBasicTypeConversion(sb, indent, cm.Target.Name, composeSrcType, cm.Target.Type, srcAccess))
                {
                    if (whenOpened) sb.AppendLine($"{indent}}}");
                    continue;
                }

                // 非基础类型转换 → 枚举转换或 TypeConvertHelper 兜底
                // T2/Compose 路径无 mapping 上下文，退化诊断不输出（行为退化仍生效）；
                // B1-13联动：表达式版 TryBuildEnumConversionExpr 同样不发射
                // 退化诊断（NCMAP023/024）——表达式化路径预存差异，移 B5-4 预登记
                GenerateConversionFallback(sb, indent, cm.Source, cm.Target, srcAccess);
                if (whenOpened) sb.AppendLine($"{indent}}}");
            }

            sb.AppendLine($"        }}");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// 构建 Compose 多段源访问表达式— src.A?.B?.C 形态（与 BuildSourceAccess 同构）；
    /// 集合索引段生成 ElementAtOrDefault 形态（全限定静态调用 + null 守卫）
    /// </summary>
    private static string BuildComposeSourceAccess(ComposeMappingSnapshot cm)
    {
        var prefix = "src";
        for (var i = 0; i < cm.SourcePath.Count; i++)
        {
            var idx = cm.SourceIndexes.Count > i ? cm.SourceIndexes[i] : null;
            // 多段链各段成员名 @ 转义（用户属性名可为关键字）
            var segAccess = i == 0
                ? $"{prefix}.{SourceGenEscape.Identifier(cm.SourcePath[i].Name)}"
                : $"{prefix}?.{SourceGenEscape.Identifier(cm.SourcePath[i].Name)}";
            prefix = idx is int n
                ? $"({segAccess} != null ? System.Linq.Enumerable.ElementAtOrDefault({segAccess}, {n}) : default)"
                : segAccess;
        }
        return prefix;
    }

    /// <summary>
    /// 生成 Compose 合成方法 — 用 switch pattern matching 替代运行时 MakeGenericMethod 反射
    /// <para>生成的代码零反射、零 Compile()，完全 AOT 友好</para>
    /// <para>逻辑：遍历 sources → 主源创建 DTO → 非主源部分更新</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息（包含 ComposeMappings）</param>
    /// <param name="methodSuffix">方法名后缀</param>
    public static void GenerateComposeMethod(StringBuilder sb, MappingSnapshot mapping, string methodSuffix)
    {
        var targetName = mapping.TargetType.FullName;
        var targetClassName = mapping.TargetClassName;
        var primarySourceName = mapping.SourceType.FullName;
        var primarySourceClassName = mapping.SourceClassName;
        var coreMethodName = GetMethodNameWithSuffix(mapping, "Map", methodSuffix);
        var methodName = $"Compose_{targetClassName}{methodSuffix}";

        // 收集所有子源类型（去重）
        var subSourceTypes = mapping.ComposeMappings
            .Select(c => c.SubSource)
            .Distinct()
            .ToList();

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// Compose 合成：多源对象 → {targetName}（源生成器编译时代码，零反射，AOT 友好）");
        sb.AppendLine($"        /// </summary>");

        sb.AppendLine($"        public static {targetName}? {methodName}(object[] sources)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            if (sources is null || sources.Length == 0) return null;");
        sb.AppendLine($"            {targetName}? result = null;");
        sb.AppendLine($"            var pending = new System.Collections.Generic.List<object>();");
        sb.AppendLine();
        sb.AppendLine($"            foreach (var src in sources)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                if (src is null) continue;");
        sb.AppendLine();
        // 主源匹配
        sb.AppendLine($"                if (result is null && src is {primarySourceName} primary)");
        sb.AppendLine($"                {{");
        sb.AppendLine($"                    result = {coreMethodName}(primary);");
        sb.AppendLine($"                    continue;");
        sb.AppendLine($"                }}");
        sb.AppendLine();
        sb.AppendLine($"                pending.Add(src);");
        sb.AppendLine($"            }}");
        sb.AppendLine();
        sb.AppendLine($"            if (result is null)");
        sb.AppendLine($"                throw new System.InvalidOperationException(");
        sb.AppendLine($"                    NCMapper.ErrorMessages.ComposeSourceNotFound({SourceGenEscape.Literal(primarySourceClassName)}));");
        sb.AppendLine();
        sb.AppendLine($"            foreach (var src in pending)");
        sb.AppendLine($"            {{");

        // 生成 switch pattern matching（零反射，AOT 友好，支持任意数量子源）
        // 子源继承拓扑排序——派生 case 先于基类 case
        //（switch pattern subsumption：基类 case 在前吞掉派生 case → CS8120 编译失败）；
        // 与运行时 ComposeRuntime 两遍扫描语义对齐（运行时按类型精确匹配，派生实例落派生分支）。
        // 稳定排序：无继承关系的子源保持原声明序；基类链缺失（null/空）按无关系处理。
        subSourceTypes = OrderSubSourcesByInheritance(subSourceTypes);
        sb.AppendLine($"                switch (src)");
        sb.AppendLine($"                {{");
        // 范围扩大：重复主源覆盖顺序分叉
        // 运行时 ComposeRuntime 两遍扫描对 pending 中重复的主源对象也执行更新（"第一个创建、后续覆盖"），
        // 生成侧此前只更新非主源子源（subSourceTypes 不含主源）→ 重复主源被静默忽略
        // （实测 SourceGen_Compose_Duplicate_Sources：Expected "Second" Actual "First"）。
        // 修复：pending switch 补主源 case，用主映射的 Update 方法更新（与运行时语义对齐）；
        // 主源若同时是 ComposeMappings 子源（subSourceTypes 含主源）则跳过补 case（避免重复 case 编译错误）
        if (!subSourceTypes.Any(s => TypeRefHelpers.IsSameType(s, mapping.SourceType)))
        {
            var primaryUpdateMethodName = $"{GetMethodNameWithSuffix(mapping, "Map", methodSuffix)}_Update";
            sb.AppendLine($"                    case {primarySourceName} ps:");
            // 主映射 Update 方法签名为 (Source, Target, Action<Source,Target>?)，action 传 null（无回调）
            sb.AppendLine($"                        {primaryUpdateMethodName}(ps, result!, null);");
            sb.AppendLine($"                        break;");
        }
        foreach (var subSource in subSourceTypes)
        {
            var subSourceName = subSource.FullName;
            var subSourceClassName = subSource.SimpleName;
            var updateMethodName = $"Update_{subSourceClassName}_To_{targetClassName}{methodSuffix}";
            sb.AppendLine($"                    case {subSourceName} s:");
            sb.AppendLine($"                        {updateMethodName}(s, result!);");
            sb.AppendLine($"                        break;");
        }
        sb.AppendLine($"                }}");

        sb.AppendLine($"            }}");
        sb.AppendLine();
        sb.AppendLine($"            return result;");
        sb.AppendLine($"        }}");
        sb.AppendLine();
    }

    /// <summary>
    /// compose 子源按继承拓扑排序（派生先于基类）
    /// 比较器：A 严格派生自 B ⟺ B.FullName ∈ A.BaseTypeChainFullNames（Ordinal）；
    /// 两两无继承关系保持原相对序（稳定插入排序，子源量级小）。基类链缺失（null/空）
    /// 按无关系处理（深超限浅快照/非 named 分支未收集）。
    /// </summary>
    private static List<TypeRef> OrderSubSourcesByInheritance(List<TypeRef> sources)
    {
        var result = new List<TypeRef>(sources);
        for (int i = 1; i < result.Count; i++)
        {
            var cur = result[i];
            var chain = cur.BaseTypeChainFullNames;
            if (chain == null || chain.Count == 0) continue;
            int j = i - 1;
            while (j >= 0 && chain.Contains(result[j].FullName, StringComparer.Ordinal))
                j--;
            if (j < i - 1)
            {
                result.RemoveAt(i);
                result.Insert(j + 1, cur);
            }
        }
        return result;
    }

    #endregion Compose 合成生成
}
