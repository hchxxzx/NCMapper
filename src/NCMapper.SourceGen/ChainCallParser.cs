using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// 链式调用展开器（step 4 / 难点①③）— 把 Fluent 链式调用
/// 的语法树反向展开为方法名序列。
/// <para>链式调用在语法树上形如 invocation 套 invocation：最外层 Ignore invocation 的 Expression 是
/// MemberAccessExpression（receiver = 内层链，Name = Ignore）；沿 MemberAccessExpression.Expression
/// 递归取 receiver 即反向展开：<c>cfg.For&lt;S,T&gt;().Map(...).Ignore(...)</c> → [Ignore, Map, For]，
/// 调用方反转得语义顺序 [For, Map, Ignore]。</para>
/// <para>配置项按调用顺序入快照（难点③顺序依赖：不重排、不丢失）。</para>
/// </summary>
internal static class ChainCallParser
{
    /// <summary>
    /// 展开链式调用 → 按"外层到内层"顺序的调用节点序列（调用语义顺序的逆序）
    /// </summary>
    /// <param name="expr">链最外层表达式（InvocationExpressionSyntax）</param>
    /// <returns>外→内的调用节点列表（[最外层方法, ..., 最内层方法]）</returns>
    public static List<InvocationChainNode> UnwrapChain(ExpressionSyntax expr)
    {
        var chain = new List<InvocationChainNode>();
        var current = expr;
        while (current is InvocationExpressionSyntax invocation)
        {
            var methodName = ExtractMethodName(invocation);
            chain.Add(new InvocationChainNode(methodName, invocation));
            // 推进到 invocation 的接收者（receiver）——即 MemberAccess 点号左侧表达式；
            // 非 MemberAccess 形态（IdentifierName/GenericName 直接调用）无 receiver，链终止
            current = GetReceiver(invocation);
        }
        return chain;
    }

    /// <summary>
    /// 反转调用序列得到语义顺序（外→内逆转后为 [For, Map, Ignore]）
    /// </summary>
    /// <param name="unwrapped">UnwrapChain 产物（外→内）</param>
    /// <returns>语义顺序列表（内→外 = 调用顺序）</returns>
    public static List<InvocationChainNode> ReverseToSemanticOrder(List<InvocationChainNode> unwrapped)
    {
        // in-place 反转免复制表——消费面实证（两处调用
        // FluentExtractor :289/:1059 均只消费语义序，外→内原始序无消费者）→ 原地反转安全；
        // 链解析单表两趟反转替代原"复制构造 + Reverse"双分配
        unwrapped.Reverse();
        return unwrapped;
    }

    /// <summary>
    /// 从 invocation 提取方法名 — 支持三形态：
    /// ①MemberAccess（<c>cfg.For&lt;S,T&gt;().Map(...)</c> 的 .Name）；
    /// ②IdentifierName（<c>Map(...)</c> 直接调用）；
    /// ③GenericName（<c>For&lt;S,T&gt;(...)</c> 的 .Identifier）
    /// </summary>
    /// <param name="invocation">调用表达式</param>
    /// <returns>方法名；无法提取返回 null</returns>
    private static string? ExtractMethodName(InvocationExpressionSyntax invocation)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax mae:
                // mae.Name 为 IdentifierNameSyntax 或 GenericNameSyntax（均派生自 SimpleNameSyntax）
                return mae.Name.Identifier.Text;
            case IdentifierNameSyntax idn:
                return idn.Identifier.Text;
            case GenericNameSyntax gns:
                return gns.Identifier.Text;
            default:
                return null;
        }
    }

    /// <summary>
    /// 取 invocation 的接收者（receiver）——链中下一个内层表达式。
    /// <para><c>cfg.For&lt;...&gt;().Map(...)</c> 中 .Map 的 receiver 是 <c>cfg.For&lt;...&gt;()</c>；
    /// 无 receiver（如 <c>Map(...)</c> 直接调用）返回 null，链终止（正常边界，非错误）。</para>
    /// </summary>
    private static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax mae)
            return mae.Expression;
        return null;
    }
}

/// <summary>
/// 链中一个调用节点（解析期符号态——含原始 invocation 供参数/位置解析，不进增量缓存）
/// </summary>
internal sealed class InvocationChainNode
{
    /// <summary>方法名（For/Map/Ignore/Mask/Convert/MapTree/NoAutoInclude/AfterMapping/BeforeMapping；无法提取为 null）</summary>
    public string? MethodName { get; }

    /// <summary>原始 invocation 节点（供参数解析与位置信息）</summary>
    public InvocationExpressionSyntax Invocation { get; }

    public InvocationChainNode(string? methodName, InvocationExpressionSyntax invocation)
    {
        MethodName = methodName;
        Invocation = invocation;
    }
}
