using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Threading;

namespace NCMapper.SourceGen;

/// <summary>
/// 检测表达式树 lambda 中 T[].Contains() 绑定到 MemoryExtensions.Contains 的情况（NCMAP018）
/// <para>.NET 10/C# 14 重载决议破坏性变更：T[].Contains() 在 Expression&lt;Func&lt;...&gt;&gt; lambda 中
/// 改绑 MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T)，而 FreeSql/EF Core 等表达式树解析器
/// 仅识别 Enumerable.Contains(IEnumerable&lt;T&gt;, T)，将抛 NotSupportedException。</para>
/// <para>net8/net9（C# 13）下 T[].Contains 仍绑定 Enumerable.Contains，不触发本诊断——
/// 本诊断通过方法符号判定实现自动 TFM 适配，无需显式判断目标框架。</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArrayContainsInExpressionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// 本分析器支持的诊断规则集合
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(DiagnosticDescriptors.ArrayContainsInExpressionRule);

    /// <summary>
    /// 初始化分析器，注册 InvocationExpression 语法节点动作
    /// </summary>
    /// <param name="context">分析上下文</param>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    /// <summary>
    /// 分析 InvocationExpression 节点，检测表达式树 lambda 内的 MemoryExtensions.Contains 调用
    /// </summary>
    /// <param name="context">语法节点分析上下文</param>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // ① 获取调用符号，判定是否为 MemoryExtensions.Contains
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method) return;
        if (!IsMemoryExtensionsContains(method)) return;

        // ② 判定是否处于表达式树 lambda 上下文（最近祖先 lambda 转换目标为 Expression<>）
        var lambda = invocation.FirstAncestorOrSelf<LambdaExpressionSyntax>();
        if (lambda == null) return;
        var lambdaTypeInfo = semanticModel.GetTypeInfo(lambda, context.CancellationToken);
        if (!IsExpressionTreeType(lambdaTypeInfo.ConvertedType)) return;

        // ③ 提取 receiver 数组元素类型名，用于诊断消息
        var elementType = GetReceiverElementType(invocation, semanticModel, context.CancellationToken);
        var typeName = elementType != null
            ? $"{elementType.Name}[]"
            : "T[]";

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ArrayContainsInExpressionRule,
            invocation.GetLocation(),
            typeName));
    }

    /// <summary>
    /// 判断方法符号是否为 MemoryExtensions.Contains（扩展方法，第一个参数为 ReadOnlySpan&lt;T&gt;）
    /// </summary>
    /// <param name="method">方法符号</param>
    /// <returns>是 MemoryExtensions.Contains 则返回 true</returns>
    private static bool IsMemoryExtensionsContains(IMethodSymbol method)
    {
        if (method.Name != "Contains") return false;
        // H-09 修复补充：扩展方法调用语法下 GetSymbolInfo 返回 reduced symbol
        // （receiver 被剥离进第一个参数），Parameters[0] 为 value 而非 ReadOnlySpan<T>——必须从 ReducedFrom 取原始定义检查。
        var definition = method.ReducedFrom ?? method;
        if (definition.ContainingType is not { Name: "MemoryExtensions" }) return false;
        if (definition.ContainingType.ContainingNamespace is not { Name: "System" }) return false;
        if (definition.Parameters.Length < 1) return false;
        // 第一个参数为 ReadOnlySpan<T>（扩展方法 receiver，由 T[] 经 op_Implicit 转换）
        var firstParamType = definition.Parameters[0].Type;
        return firstParamType is INamedTypeSymbol { Name: "ReadOnlySpan", IsGenericType: true };
    }

    /// <summary>
    /// 判断类型符号是否为 Expression&lt;TDelegate&gt;（表达式树类型）
    /// </summary>
    /// <param name="type">类型符号</param>
    /// <returns>是表达式树类型则返回 true</returns>
    private static bool IsExpressionTreeType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType) return false;
        if (!namedType.IsGenericType) return false;
        if (namedType.Name != "Expression") return false;
        // Expression<TDelegate>位于 System.Linq.Expressions，
        // ContainingNamespace.Name 仅返回末段（"Expressions"），原检查 Name == "System" 恒不满足导致诊断永不触发（死代码）。
        if (namedType.ContainingNamespace.ToDisplayString() != "System.Linq.Expressions") return false;
        // 泛型实参应为委托类型（Func<...> 或自定义 delegate）
        var delegateArg = namedType.TypeArguments.FirstOrDefault();
        return delegateArg is { TypeKind: TypeKind.Delegate };
    }

    /// <summary>
    /// 获取 Contains 调用 receiver 的数组元素类型
    /// <para>语法形式 array.Contains(value) 中，MemberAccess 的 Expression 即 receiver 数组，
    /// 其 TypeInfo.Type 为数组类型（op_Implicit 转换发生在语义层，语法层 receiver 仍为 T[]）。</para>
    /// </summary>
    /// <param name="invocation">调用表达式</param>
    /// <param name="semanticModel">语义模型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>数组元素类型符号；无法确定时返回 null</returns>
    private static ITypeSymbol? GetReceiverElementType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return null;
        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        return receiverType is IArrayTypeSymbol arrayType ? arrayType.ElementType : null;
    }
}
