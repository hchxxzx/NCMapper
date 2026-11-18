namespace NCMapper.SourceGen;

/// <summary>
/// 转换器调用渲染 partial— 静态方法引用三字段
/// → global::FQN.M(args) 内联调用。独立成文件以守卫 CodeGenerator.Mapping.cs
/// 主文件行数承诺线（W3_1 附录 B v1.4 ≤2460）。
/// </summary>
internal static partial class CodeGenerator
{
    /// <summary>
    /// 静态转换方法调用渲染— 三字段（FQN/方法名/Shape）→ global::FQN.M(args) 内联调用
    /// <para>四模板 + 守卫变体（R6 口径 3，与运行时 AssembleTemplateCall 同语义）：
    /// T0 M() / T1 M(字段) / T2 M(源) / T3 M(源, 字段)；守卫变体生成
    /// 「(字段).HasValue ? M((字段).Value) : default」三元（null 不调用、目标保持默认， null 语义表；
    /// default 字面量经 target-typed conditional 推断，消费方 net8+）。字段基准 T + 参数 Nullable&lt;T&gt;
    /// 与返回提升由 C# 隐式转换天然承载（值等价）。</para>
    /// <para>fieldAccess=null（R6-b 无匹配源属性）→ 字段实参 default 字面量（实参类型推断，四模板
    /// T1 M(default)/T3 M(源, default)；守卫变体仅在字段类型基准已知的场景编码，无源不出现）。</para>
    /// </summary>
    /// <param name="declaringType">锚定类型 FQN（物化期 ToDisplayString，含命名空间/嵌套/泛型实参）</param>
    /// <param name="methodName">方法名</param>
    /// <param name="shape">签名模板（含守卫变体）</param>
    /// <param name="fieldAccess">字段值访问表达式；null = R6-b 无匹配源属性</param>
    /// <param name="sourceExpr">源对象表达式（主源/子源/反向 DTO，按路径传入）</param>
    private static string BuildStaticConverterCall(string declaringType, string methodName, ConverterShape shape,
        string? fieldAccess, string sourceExpr)
    {
        var call = $"global::{declaringType}.{methodName}";
        switch (shape)
        {
            case ConverterShape.T0Constant:
                return $"{call}()";
            case ConverterShape.T2MultiField:
                return $"{call}({sourceExpr})";
            case ConverterShape.T3Combined:
                return $"{call}({sourceExpr}, {fieldAccess ?? "default"})";
            case ConverterShape.T1Guarded:
                return $"({fieldAccess!}).HasValue ? {call}(({fieldAccess}).Value) : default";
            case ConverterShape.T3Guarded:
                return $"({fieldAccess!}).HasValue ? {call}({sourceExpr}, ({fieldAccess}).Value) : default";
            default:
                return $"{call}({fieldAccess ?? "default"})";
        }
    }
}
