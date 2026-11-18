using System.Text;
using Microsoft.CodeAnalysis;
using NCMapper.Shared;
namespace NCMapper.SourceGen;

/// <summary>
/// MapWhen 条件守卫发射 partial
/// 双包裹点（赋值层 if 守卫 / 构造器层与 init-only 初始化器三元）+ B 形态谓词方法本体发射。
/// 独立成文件以守卫 CodeGenerator.Mapping.cs 主文件行数承诺线（W3_1 附录 B v1.4 ≤2460，
/// 预算预案：helper 外移为默认实施路径；RenderNullableConversion 为预算补偿外移）。
/// <para><b>字节不变门</b>：无 MapWhen 声明（ConditionMethodRef null）零输出——守卫路径与既有
/// 路径产物逐字节一致（既有生成基线 diff=0，G3/#78）。</para>
/// <para><b>语义链路</b>：条件 → 转换 → 赋值（转换前求值——条件假时转换不执行）；
/// 跳过值语义：新建保持初始化器值 / 更新保留 dst 既有值 / 构造器参数显式 default；
/// null 抑制先行——条件看到"实际将赋的值"（嵌套路径中段 null → 赋值整体跳过、条件不求值）。</para>
/// <para><b>T1 局部变量承载</b>（源值一次求值）：字段值形态发射
/// <c>var __when_src_X = &lt;srcAccess&gt;; if (M(__when_src_X)) { &lt;既有赋值&gt; }</c>——
/// 源访问一次求值，条件与既有赋值共用（赋值内部 srcAccess 经 override 复用同一局部）。</para>
/// </summary>
internal static partial class CodeGenerator
{
    /// <summary>
    /// 赋值层守卫包裹入口（赋值层包裹）— 有守卫时发射
    /// <c>if (&lt;ConditionRef&gt;(&lt;param&gt;)) { &lt;既有赋值语句&gt; }</c>；
    /// 无守卫直通（GenerateSinglePropertyAssignment 原样调用——字节不变门）。
    /// <para>调用点：GeneratePropertyAssignments（Core/_Update/MapListInto/List 内联 8 调用点
    /// 自动继承——三形态一致）。</para>
    /// </summary>
    private static void EmitGuardedSinglePropertyAssignment(StringBuilder sb, PropertyMappingSnapshot pm, string indent,
        Dictionary<TypeRef, string> typeNameMap, string methodSuffix = "", MappingSnapshot? mapping = null, GenerationContext? ctx = null)
    {
        // 字节不变门：无守卫直通（产物与既有路径逐字节一致）
        if (pm.ConditionMethodRef is not { Length: > 0 } || pm.ConditionShape == ConditionShape.None)
        {
            GenerateSinglePropertyAssignment(sb, pm, indent, typeNameMap, methodSuffix, mapping, ctx);
            return;
        }

        // T1 字段值形态：局部变量承载（源值一次求值，条件与转换共用；
        // 多段路径中段 null → 局部变量 null → 条件看到实际将赋的值）
        // T1 无源属性（[Convert] 无匹配源）场景已由物化期 F43 门拦截（MappingSnapshotBuilder）
        string? srcOverride = null;
        if (pm.ConditionShape == ConditionShape.FieldValue && pm.Source != null)
        {
            srcOverride = $"__when_src_{SanitizeIdentifier(pm.Target.Name)}";
            sb.AppendLine($"{indent}var {srcOverride} = {BuildSourceAccess(pm)};");
            sb.AppendLine($"{indent}if ({RenderWhenCall(pm.ConditionMethodRef, srcOverride)})");
        }
        else if (pm.ConditionShape == ConditionShape.FieldValue)
        {
            // B3-04 渲染端补齐（测试暴露，阻塞性连带修改 B6）：
            // T1 无源场景（提取端口径"无源场景即目标属性类型"）——谓词按目标属性类型基准
            // 对 null 源值求值；原实现落 T2 分支传整源 → 谓词签名类型不匹配 CS1503
            sb.AppendLine($"{indent}if ({RenderWhenCall(pm.ConditionMethodRef, $"default({pm.Target.Type.FullName})")})");
        }
        else
        {
            // T2 整源形态（实参恒 src）——条件在转换与赋值之前求值
            sb.AppendLine($"{indent}if ({RenderWhenCall(pm.ConditionMethodRef, "src")})");
        }
        sb.AppendLine($"{indent}{{");
        GenerateSinglePropertyAssignment(sb, pm, indent + "    ", typeNameMap, methodSuffix, mapping, ctx, srcOverride);
        sb.AppendLine($"{indent}}}");
    }

    /// <summary>
    /// 守卫谓词调用渲染— ConditionMethodRef（"FQN::方法名"，A/B 双形态同格式）
    /// → <c>global::FQN.方法名(argExpr)</c>。
    /// <para>B 形态 ref 为 <c>NCMapper.Generated.GeneratedMapper::Map_When_*</c>——
    /// 生成代码同类直呼（global:: 限定形态统一，名称可解析零 using 依赖，B10）。</para>
    /// </summary>
    private static string RenderWhenCall(string conditionMethodRef, string argExpr)
    {
        var sep = conditionMethodRef.LastIndexOf("::", System.StringComparison.Ordinal);
        // Substring 形态（netstandard2.0 无 System.Index/Range——range 语法编译不过）
        var methodName = sep >= 0 ? conditionMethodRef.Substring(sep + 2) : conditionMethodRef;
        var typeName = sep >= 0 ? conditionMethodRef.Substring(0, sep) : "";
        return $"global::{typeName}.{methodName}({argExpr})";
    }

    /// <summary>
    /// 构造器实参守卫三元— 有守卫时实参改
    /// <c>M(&lt;arg&gt;) ? &lt;rawArg&gt; : default(paramType)</c>（表达式列表上下文无法 if 化，
    /// 三元形态—— 风险点② / 经验 #6.2 表达式内 if 化限制）。
    /// <para>T1 谓词实参 = BuildSourceAccess(pm)（源字段值）；T2 = src。构造器路径无法发射
    /// 局部变量承载（表达式上下文），谓词实参与值实参各自求值（属性访问幂等，行为等价）。</para>
    /// </summary>
    private static string ApplyWhenToConstructorArg(PropertyMappingSnapshot pm, string rawArg, TypeRef paramType)
    {
        if (pm.ConditionMethodRef is not { Length: > 0 } || pm.ConditionShape == ConditionShape.None)
            return rawArg;
        var arg = pm.ConditionShape == ConditionShape.WholeSource
            ? "src"
            : pm.Source != null
                ? BuildSourceAccess(pm)
                : $"default({paramType.FullName})"; // 渲染端补齐：无源 T1 按目标类型基准
        return $"{RenderWhenCall(pm.ConditionMethodRef, arg)} ? {rawArg} : default({paramType.FullName})";
    }

    /// <summary>
    /// init-only 初始化器成员守卫三元— 对象初始化器成员（"Prop = expr"）右侧
    /// 改 <c>M(&lt;arg&gt;) ? expr : default(T)</c>（初始化器成员为赋值上下文表达式，三元形态）。
    /// </summary>
    private static string ApplyWhenToInitOnlyExpr(PropertyMappingSnapshot pm, string rawExpr)
    {
        if (pm.ConditionMethodRef is not { Length: > 0 } || pm.ConditionShape == ConditionShape.None)
            return rawExpr;
        var arg = pm.ConditionShape == ConditionShape.WholeSource
            ? "src"
            : pm.Source != null
                ? BuildSourceAccess(pm)
                : $"default({pm.Target.Type.FullName})"; // 渲染端补齐：无源 T1 按目标类型基准
        return $"{RenderWhenCall(pm.ConditionMethodRef, arg)} ? {rawExpr} : default({pm.Target.Type.FullName})";
    }

    /// <summary>
    /// Compose 块内守卫包裹（发射落点 = Update_{Sub}_To_{Target} 赋值层）。
    /// T1 时发射局部变量承载行 + if 守卫开行（out srcOverride 供调用方替换赋值源访问）；
    /// T2 仅发射 if 守卫开行（srcOverride null——赋值源访问保持原表达式）。
    /// 调用方在各赋值分支结束（含 continue 前）与循环体尾以
    /// <c>if (whenOpened) sb.AppendLine($"{indent}}}")</c> 闭合。
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="cm">Compose 属性映射（条件四字段承载）</param>
    /// <param name="rawAccess">子源源访问表达式（src.X / BuildComposeSourceAccess 产物）</param>
    /// <param name="indent">缩进</param>
    /// <param name="srcOverride">T1 局部变量名（已发射承载行）；T2/无守卫 null</param>
    /// <returns>守卫是否已开启（true = 调用方须闭合 }）</returns>
    private static bool EmitComposeWhenGuardOpen(StringBuilder sb, ComposeMappingSnapshot cm, string rawAccess, string indent, out string? srcOverride)
    {
        srcOverride = null;
        if (cm.ConditionMethodRef is not { Length: > 0 } || cm.ConditionShape == ConditionShape.None)
            return false;

        if (cm.ConditionShape == ConditionShape.FieldValue)
        {
            srcOverride = $"__when_src_{SanitizeIdentifier(cm.Target.Name)}";
            sb.AppendLine($"{indent}var {srcOverride} = {rawAccess};");
            sb.AppendLine($"{indent}if ({RenderWhenCall(cm.ConditionMethodRef, srcOverride)})");
            sb.AppendLine($"{indent}{{");
            return true;
        }

        // T2 整源（Update_{Sub} 方法 src 形参即子源对象）
        sb.AppendLine($"{indent}if ({RenderWhenCall(cm.ConditionMethodRef, "src")})");
        sb.AppendLine($"{indent}{{");
        return true;
    }

    /// <summary>
    /// B 形态谓词方法本体发射（转录产物落地）— 遍历全部映射快照，
    /// ConditionMethodBody 非 null 的声明（pm 通道 + Compose cm 通道）发射为
    /// <c>private static bool Map_When_*(&lt;paramType&gt; &lt;paramName&gt;) =&gt; &lt;body&gt;;</c>。
    /// <para>private static：生成代码同类直呼；运行时回退经 NonPublic 反射解析（B 形态
    /// 载体定位双规则——谓词方法被生成赋值代码直接引用，不会被 trimming 裁剪）。</para>
    /// <para>发射序：mapping 遍历序 + PropertyMappings/ComposeMappings 序；方法名按
    /// ConditionMethodRef 去重（(S,T,prop) 稳定哈希——F44 已保证同对唯一；哈希碰撞为理论
    /// 不可达防御分支：同名第二条跳过，生成代码引用同签名同体的首条，语义等价）。</para>
    /// <para>互指对跳过（映射方法族不生成，谓词方法同步不生成——孤儿方法防御）；
    /// 反向快照条件四字段已剥离，不参与发射。</para>
    /// </summary>
    private static void GenerateWhenPredicateMethods(StringBuilder sb,
        System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings, Dictionary<TypeRef, TypeRef> mutualPairs)
    {
        var emitted = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            if (mapping == null || mapping.SkipCodeGeneration) continue;
            if (IsMutualPair(mapping, mutualPairs)) continue;

            foreach (var pm in mapping.PropertyMappings)
            {
                if (pm.ConditionMethodRef is not { Length: > 0 } || pm.ConditionMethodBody == null) continue;
                // 谓词参数类型：T2 = 源实体类型；T1 = 有效源类型（末段索引=元素类型，与局部变量承载
                // var 推断类型一致）；T1 无源（物化期 F43 已拦截）防御跳过
                var paramType = pm.ConditionShape == ConditionShape.WholeSource
                    ? mapping.SourceType.FullName
                    : GetEffectiveSourceType(pm)?.FullName;
                if (paramType == null) continue;
                EmitWhenMethod(sb, pm.ConditionMethodRef, paramType, pm.ConditionMethodParamName ?? "v",
                    pm.ConditionMethodBody, emitted);
            }

            foreach (var cm in mapping.ComposeMappings)
            {
                if (cm.ConditionMethodRef is not { Length: > 0 } || cm.ConditionMethodBody == null) continue;
                // 谓词参数类型：T2 = 子源类型；T1 = 末段有效类型（索引段=元素类型，与 Compose 通道
                // composeSrcType 判定同口径）
                var paramType = cm.ConditionShape == ConditionShape.WholeSource
                    ? cm.SubSource.FullName
                    : (cm.SourceIndexes.Count > 0 && cm.SourceIndexes[cm.SourceIndexes.Count - 1] is int
                        ? (cm.Source.Type.EnumerableElementType ?? cm.Source.Type).FullName
                        : cm.Source.Type.FullName);
                EmitWhenMethod(sb, cm.ConditionMethodRef, paramType, cm.ConditionMethodParamName ?? "v",
                    cm.ConditionMethodBody, emitted);
            }
        }
    }

    /// <summary>B 形态谓词方法单条发射（方法名按 ConditionMethodRef 解析；emitted 去重）</summary>
    private static void EmitWhenMethod(StringBuilder sb, string methodRef, string paramType, string paramName, string body,
        HashSet<string> emitted)
    {
        var sep = methodRef.LastIndexOf("::", System.StringComparison.Ordinal);
        var methodName = sep >= 0 ? methodRef.Substring(sep + 2) : methodRef;
        if (!emitted.Add(methodName)) return;

        sb.AppendLine();
        sb.AppendLine("        /// <summary>MapWhen 谓词（B 形态内联转录——");
        sb.AppendLine("        /// Fluent .MapWhen 内联 lambda 经符号级限定名重写物化为私有静态方法；");
        sb.AppendLine("        /// 生成赋值代码同类直呼，运行时回退经 NonPublic 反射解析）</summary>");
        sb.AppendLine($"        private static bool {methodName}({paramType} {paramName}) => {body};");
    }

    // ==== 可空组合转换渲染（职责口径）====
    // 本区段承载 RenderNullableConversion —— Nullable 兼容（⑨⑩⑪）子分支的纯渲染辅助（W3_1 C2b 产物）。

    /// <summary>
    /// 可空组合转换渲染— ⑨⑩⑪ 子分支纯渲染。
    /// <para>组合形态由 plan.NullableCompatibility 承载（判定前移）；子分支（同底层直赋 / 隐式转换三元 /
    /// ConvertValue 回退）按底层类型渲染期判定——与改造前逐行等价（子分支表达式对应 +
    /// I-3 精确形态：⑨ 隐式子分支为 HasValue 三元非裸 cast）。</para>
    /// </summary>
    private static void RenderNullableConversion(StringBuilder sb, PropertyMappingSnapshot pm, string indent,
        TypeRef effectiveSrcType, string srcAccess, string srcPropType, string tgtPropType, NullableKind nullableKind)
    {
        switch (nullableKind)
        {
            case NullableKind.NullableToNullable:
                // ⑨ Nullable<T>→ Nullable<U>编译时处理（~1-2ns vs 运行时 ~50-100ns）
                var underlyingSrc = TypeRefHelpers.GetUnderlyingType(effectiveSrcType);
                var underlyingTgt = TypeRefHelpers.GetUnderlyingType(pm.Target.Type);
                if (TypeRefHelpers.IsSameType(underlyingSrc, underlyingTgt))
                {
                    // 同底层类型（int? → int?）直接赋值
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess};");
                }
                else if (TypeRefHelpers.IsImplicitConvertible(underlyingSrc, underlyingTgt))
                {
                    // 可隐式转换（int? → long?）：HasValue ? (U?)value : null
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess}.HasValue ? ({tgtPropType}){srcAccess}.Value : null;");
                }
                else
                {
                    // 不可转换 → 回退运行时
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = NCMapper.TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess})!;");
                }
                break;

            case NullableKind.NullableToNonNullable:
                // ⑩ 检查底层类型兼容性 — Nullable<T>的 T 与目标类型可能不同（如 long? → int）
                var underlyingSrcType = TypeRefHelpers.GetUnderlyingType(effectiveSrcType);
                if (TypeRefHelpers.IsSameType(underlyingSrcType, pm.Target.Type))
                {
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess}.HasValue ? {srcAccess}.Value : default;");
                }
                else if (TypeRefHelpers.IsImplicitConvertible(underlyingSrcType, pm.Target.Type))
                {
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess}.HasValue ? ({tgtPropType}){srcAccess}.Value : default;");
                }
                else
                {
                    // 底层类型不可转换 → 回退运行时
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = NCMapper.TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess})!;");
                }
                break;

            case NullableKind.NonNullableToNullable:
                // ⑪ 源非可空值类型 → 目标可空值类型：new Nullable<T>(value)
                // 检查底层类型兼容性 — 不可转换时回退运行时
                var underlyingTgtType = TypeRefHelpers.GetUnderlyingType(pm.Target.Type)!;
                if (TypeRefHelpers.IsSameType(effectiveSrcType, underlyingTgtType)
                    || TypeRefHelpers.IsImplicitConvertible(effectiveSrcType, underlyingTgtType))
                {
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = new {tgtPropType}(({underlyingTgtType.FullName}){srcAccess});");
                }
                else
                {
                    // 底层类型不可隐式/显式转换 → 回退运行时
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = NCMapper.TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess})!;");
                }
                break;
        }
    }
}
