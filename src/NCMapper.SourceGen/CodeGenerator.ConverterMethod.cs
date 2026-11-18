using System.Collections.Generic;
using System.Text;
namespace NCMapper.SourceGen;

/// <summary>
/// B 形态转换器方法发射 partial
/// Fluent .Convert/.ConvertBack（含 FromSource 块内 Convert）内联 lambda 经 InlineTranscriber
/// 转录的产物物化为 GeneratedMapper 私有静态方法（<c>private static TTarget Map_Cv_*(T v) =&gt; body;</c>）。
/// 独立成文件以守卫 CodeGenerator.Mapping.cs 主文件行数承诺线（W3_1 附录 B v1.4 ≤2460——
/// 预算预案先例：WhenGuard/ConverterCall 同款外移）。
/// <para><b>字节不变门</b>：无 B 形态声明（ConverterMethodBody 恒 null）零输出——产物与既有路径
/// 逐字节一致（既有生成基线 diff=0，G3/#78）。</para>
/// <para><b>调用链零分叉</b>：物化期三元组注入生成类型限定名（NCMapper.Generated.GeneratedMapper）+
/// 生成方法名——调用点 BuildStaticConverterCall（正向赋值/Compose/构造器实参/反向写回）零改动复用
/// （global::FQN.M(args) 渲染天然解析生成代码同类成员）。</para>
/// <para><b>发射序</b>：GenerateWhenPredicateMethods 之后——与映射方法族（正向 + 反向 + Compose）
/// 同文件同类，private static 同类直呼（运行时无反射消费——Fluent 转换器仅生成代码通道生效，
/// 与 MapWhen B 形态谓词的 NonPublic 反射回退不同）。</para>
/// </summary>
internal static partial class CodeGenerator
{
    /// <summary>
    /// B 形态转换器方法本体发射— 遍历全部映射快照（正向 pm + Compose cm + 反向 pm
    /// ［含 ReverseOnly 条目——经 ReversePlanner 复制进反向快照］），ConverterMethodBody 非 null 的
    /// 声明发射为 <c>private static {返回类型} {方法名}({形参列表}) =&gt; {转录体};</c>。
    /// <para>返回类型 = 目标属性类型（lambda 返回类型与目标恒赋值兼容——形态匹配门保证相等或
    /// 非空→可空提升，方法体隐式转换零警告）；形参类型组 = lambda 绑定后委托签名（提取期承载，
    /// 含 T1/T3 守卫变体的非可空形参形态）。</para>
    /// <para>方法名按 ConverterMethodName 去重（生成名 FNV 含转录体——同名必同体同签名，
    /// dedup 安全；互指对/跳过生成的映射同步跳过——孤儿方法防御，与 When 谓词同口径）。</para>
    /// </summary>
    private static void GenerateConverterMethods(StringBuilder sb,
        System.Collections.Immutable.ImmutableArray<MappingSnapshot?> mappings,
        Dictionary<TypeRef, TypeRef> mutualPairs,
        Dictionary<MappingSnapshot, MappingSnapshot?>? reverseSnapshotTable)
    {
        var emitted = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            if (mapping == null || mapping.SkipCodeGeneration) continue;
            if (IsMutualPair(mapping, mutualPairs)) continue;

            foreach (var pm in mapping.PropertyMappings)
            {
                if (pm.ConverterMethodBody is not { Length: > 0 } || pm.ConverterMethodName == null) continue;
                EmitConverterMethod(sb, pm.ConverterMethodName, pm.Target.Type.FullName,
                    pm.ConverterMethodParamTypes, pm.ConverterMethodParamNames, pm.ConverterMethodBody, emitted);
            }

            foreach (var cm in mapping.ComposeMappings)
            {
                if (cm.ConverterMethodBody is not { Length: > 0 } || cm.ConverterMethodName == null) continue;
                EmitConverterMethod(sb, cm.ConverterMethodName, cm.Target.Type.FullName,
                    cm.ConverterMethodParamTypes, cm.ConverterMethodParamNames, cm.ConverterMethodBody, emitted);
            }

            // 反向快照（ConvertBack B 形态——ReversePlanner 复制进反向 pm 的 ConverterMethod*；
            // ReverseOnly 条目同经反向快照消费）；互指对反向整体跳过（主循环同守卫，孤儿方法防御）
            if (reverseSnapshotTable != null && reverseSnapshotTable.TryGetValue(mapping, out var reverseMapping)
                && reverseMapping != null)
            {
                foreach (var pm in reverseMapping.PropertyMappings)
                {
                    if (pm.ConverterMethodBody is not { Length: > 0 } || pm.ConverterMethodName == null) continue;
                    EmitConverterMethod(sb, pm.ConverterMethodName, pm.Target.Type.FullName,
                        pm.ConverterMethodParamTypes, pm.ConverterMethodParamNames, pm.ConverterMethodBody, emitted);
                }
            }
        }
    }

    /// <summary>B 形态转换器方法单条发射（方法名 emitted 去重——同名必同体同签名，FNV 含转录体保证）</summary>
    private static void EmitConverterMethod(StringBuilder sb, string methodName, string returnType,
        string[]? paramTypes, string[]? paramNames, string body, HashSet<string> emitted)
    {
        if (!emitted.Add(methodName)) return;

        // 形参列表（T0 空组；类型组与名组等长——提取期同源构建；防御性 Min 收敛）
        var paramList = "";
        if (paramTypes is { Length: > 0 } && paramNames is { Length: > 0 })
        {
            var count = System.Math.Min(paramTypes.Length, paramNames.Length);
            var parts = new string[count];
            for (var i = 0; i < count; i++)
                parts[i] = paramTypes[i] + " " + paramNames[i];
            paramList = string.Join(", ", parts);
        }

        sb.AppendLine();
        sb.AppendLine("        /// <summary>Convert 转换器（B 形态内联转录——");
        sb.AppendLine("        /// Fluent .Convert/.ConvertBack 内联 lambda 经符号级限定名重写物化为私有静态方法；");
        sb.AppendLine("        /// 生成赋值代码同类直呼）</summary>");
        sb.AppendLine($"        private static {returnType} {methodName}({paramList}) => {body};");
    }
}
