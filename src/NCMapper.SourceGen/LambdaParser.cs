using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// Fluent lambda 表达式树解析器（step 2.iv / 难点②）
/// 把 <c>dest =&gt; dest.Address.City</c> / <c>src =&gt; src.Name</c> 等表达式解析为属性路径段列表。
/// <para>支持形态：单层属性 / 嵌套属性 / null 抑制 <c>!</c>（递归 PostfixUnaryExpression.Operand）/
/// SimpleLambda 与单参 ParenthesizedLambda 两种形参写法。</para>
/// <para>拒绝形态（诊断码见各分支）：根非参数/ null 条件 <c>?.</c>/ 索引器/
/// 方法调用/ 成员无法解析；成员非属性/字段按 / 拍板静默跳过（F03 保留位不启用）。</para>
/// </summary>
internal static class LambdaParser
{
    /// <summary>
    /// 成员访问链深度上限：段数 ≤ 16 合法、≥ 17 拒绝。
    /// <para>计数口径 = TryWalkMemberAccess 递归深度（整链入口 0，根参数层 = 段数）；
    /// <c>!</c> 抑制与索引段各占一层（保守偏差——防御门目的是有界解析，非精确段数语义）。
    /// 校准证据：F62_SixteenLevelChain_LegalBoundary（16 段合法零诊断）。</para>
    /// </summary>
    private const int MaxMemberAccessDepth = 16;

    /// <summary>
    /// 解析 lambda 表达式 → 属性路径段列表（从根到叶，按访问顺序）
    /// </summary>
    /// <param name="lambda">lambda 表达式语法节点</param>
    /// <param name="semanticModel">语义模型（成员符号解析）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>解析结果（路径段列表 + 诊断列表；解析失败时诊断非空）</returns>
    public static LambdaParseResult Parse(
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        // 形参名提取：SimpleLambda: x => ...；ParenthesizedLambda: (x) => ...（单参）
        string? paramName = null;
        if (lambda is SimpleLambdaExpressionSyntax sl)
            paramName = sl.Parameter.Identifier.Text;
        else if (lambda is ParenthesizedLambdaExpressionSyntax pl
                 && pl.ParameterList.Parameters.Count == 1)
            paramName = pl.ParameterList.Parameters[0].Identifier.Text;

        var segments = new List<PropertyPathSegment>();
        var diagnostics = new List<DiagnosticInfo>();
        if (lambda.Body is not ExpressionSyntax body)
        {
            // .4：语句块 lambda 体无法归约为属性路径
            // 原静默空段返回 → 显式诊断（F08 同族：动态形态黑名单，Warning）。
            // 触发面仅解析消费位；MapWhen/Convert 内联谓词经 InlineTranscriber 转录、不经本解析器。
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.BlockOrTernaryLambdaNotSupportedRule,
                Args = new object[] { lambda.ToString() },
                Location = lambda.GetLocation(),
            });
            return new LambdaParseResult(segments, diagnostics);
        }

        if (!TryWalkMemberAccess(body, semanticModel, paramName, segments, diagnostics, ct)
            && diagnostics.Count == 0)
        {
            // .4：解析消费位含三元表达式 → 显式诊断；
            // 其余非成员访问形态（二元表达式/常量等，诊断集未覆盖）维持 / 静默
            //（Convert converter 场景由调用方静默丢弃，同型 Map 口径）。
            foreach (var node in body.DescendantNodesAndSelf())
            {
                if (node is ConditionalExpressionSyntax)
                {
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.BlockOrTernaryLambdaNotSupportedRule,
                        Args = new object[] { body.ToString() },
                        Location = body.GetLocation(),
                    });
                    break;
                }
            }
        }

        return new LambdaParseResult(segments, diagnostics);
    }

    /// <summary>
    /// 递归走成员访问链（a.b.c 自顶向下入段）。返回 false 表示不是合法属性路径形态
    /// （诊断已按分支填充；二元表达式等未覆盖形态不填诊断——调用方按需处理）。
    /// </summary>
    /// <param name="depth">递归深度（入口 0；B2-13 深度门计数口径，超限发 F62）</param>
    private static bool TryWalkMemberAccess(
        ExpressionSyntax expr,
        SemanticModel sm,
        string? paramName,
        List<PropertyPathSegment> segments,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        int depth = 0)
    {
        ct.ThrowIfCancellationRequested();

        // 入口单点深度门——成员访问链段数超上限
        // （根参数层 depth = 段数 > 16）→ 拒绝整条链（诊断后 false 冒泡，段全部不入）。
        // 缺陷态：源链深度无界（深链静默落快照 + 无界递归）；16 段以内合法面零变化。
        if (depth > MaxMemberAccessDepth)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FluentLambdaDepthExceededRule,
                Args = new object[] { MaxMemberAccessDepth },
                Location = expr.GetLocation(),
            });
            return false;
        }

        // d.Address.City → MemberAccessExpressionSyntax（先递归左侧再处理当前段）
        if (expr is MemberAccessExpressionSyntax mae)
        {
            if (!TryWalkMemberAccess(mae.Expression, sm, paramName, segments, diagnostics, ct, depth + 1))
                return false;

            var name = mae.Name.Identifier.Text;
            var symbolInfo = sm.GetSymbolInfo(mae, ct);
            var sym = symbolInfo.Symbol;
            if (sym is null)
            {
                // 属性不存在（编译期 GetSymbolInfo 为 null/error——CS1061 已由编译器报告，
                // F02 提供 Fluent 语义定位与修复指引）
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.FluentPropertyNotFoundRule,
                    // 第二实参 = 收方类型显示名（缺陷态 = 硬编码
                    // "" → 消息 "does not exist on type ''"）；符号解析 null 时回退空串（注释声明——
                    // 收方表达式类型理论可得，null 属防御面）
                    Args = new object[]
                    {
                        name,
                        sm.GetTypeInfo(mae.Expression, ct).Type?.ToDisplayString() ?? "",
                    },
                    Location = mae.GetLocation(),
                });
                return false;
            }

            IPropertySymbol? propertySym = null;
            IFieldSymbol? fieldSym = null;
            switch (sym.Kind)
            {
                case SymbolKind.Property:
                    propertySym = (IPropertySymbol)sym;
                    break;
                case SymbolKind.Field:
                    fieldSym = (IFieldSymbol)sym;
                    break;
                default:
                    // 成员非属性/字段（方法/类型/事件等）—— / 拍板：静默跳过（F03 保留位不启用，
                    // 该场景视作结构错误由编译器兜底）
                    return false;
            }

            var segment = propertySym != null
                ? new PropertyPathSegment(propertySym)
                : new PropertyPathSegment(fieldSym!);
            segments.Add(segment);
            return true;
        }

        // 根参数形态：dest
        if (expr is IdentifierNameSyntax ins)
        {
            if (paramName is not null && ins.Identifier.Text == paramName)
                return true; // 根参数，无 segment
            // 非根参数的 IdentifierName：局部变量/静态类引用（F04 白名单禁止）
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FluentLambdaRootNotParameterRule,
                Location = ins.GetLocation(),
            });
            return false;
        }

        // dest.Address!.City → null 抑制（PostfixUnaryExpression.Operand 递归；语义等同直接访问）
        if (expr is PostfixUnaryExpressionSyntax pue
            && pue.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            return TryWalkMemberAccess(pue.Operand, sm, paramName, segments, diagnostics, ct, depth + 1);
        }

        // dest?.Address → null 条件访问（F05 拒绝）
        if (expr is ConditionalAccessExpressionSyntax)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FluentNullConditionalRule,
                Args = new object[] { expr.ToString() },
                Location = expr.GetLocation(),
            });
            return false;
        }

        // dest.Items[0] → 索引器访问（常量整数索引段放行；变量/非常量仍 F06 拒绝）
        if (expr is ElementAccessExpressionSyntax eae)
        {
            // 索引实参须恰一且为常量整数（GetConstantValue——变量/方法调用结果/多参 → F06 拒绝）
            var indexArg = eae.ArgumentList.Arguments.Count == 1
                ? eae.ArgumentList.Arguments[0].Expression
                : null;
            var constant = indexArg != null ? sm.GetConstantValue(indexArg, ct) : default;
            if (!constant.HasValue || constant.Value is not int indexValue)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                    Args = new object[] { expr.ToString() },
                    Location = expr.GetLocation(),
                });
                return false;
            }

            // 递归被索引表达式（s.Items / s.A.B）——索引记到末段（Items[0] → Items 段 IndexAfter=0）
            if (!TryWalkMemberAccess(eae.Expression, sm, paramName, segments, diagnostics, ct, depth + 1))
                return false;
            if (segments.Count == 0) return false; // 根参数直接索引（s[0]）——非属性路径形态
            // 多级索引拒绝门——WithIndexAfter 拷贝构造仅设单值
            // IndexAfter，段已有索引再遇索引会被外层覆盖（x[0][1] → SourceIndexes=[1] 索引错 +
            // 段类型未降级元素类型错，静默错代码）。显式拒绝（复用 F06，Warning）——与
            // dest 索引段拒绝口径同族；单级索引承载不变。
            if (segments[segments.Count - 1].IndexAfter != null)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                    Args = new object[] { expr.ToString() },
                    Location = expr.GetLocation(),
                });
                return false;
            }
            segments[segments.Count - 1] = segments[segments.Count - 1].WithIndexAfter(indexValue);
            return true;
        }

        // dest.Method() → 方法调用（F07 拒绝）
        if (expr is InvocationExpressionSyntax)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FluentMethodCallRule,
                Args = new object[] { expr.ToString() },
                Location = expr.GetLocation(),
            });
            return false;
        }

        // 其余形态（二元表达式/常量等）：非成员访问链，由调用方决定处置（Convert converter → 静默丢弃，同型 Map 口径；
        // 其他 lambda → / 静默跳过）
        return false;
    }
}

/// <summary>
/// 属性路径段（解析期符号态——携带 IPropertySymbol/IFieldSymbol，物化时转值；不进增量缓存）
/// </summary>
internal sealed class PropertyPathSegment
{
    /// <summary>成员名</summary>
    public string Name { get; }

    /// <summary>属性符号（属性段非空）</summary>
    public IPropertySymbol? Property { get; }

    /// <summary>字段符号（字段段非空）</summary>
    public IFieldSymbol? Field { get; }

    /// <summary>
    /// 段后索引值（G7——常量整数索引段放行产物）：
    /// <c>Items[0]</c> → Items 段 IndexAfter=0（与特性通道 FromField "Items[0].Title" 的
    /// SourceIndexes 平行列表同承载：null=该段无索引访问）。
    /// </summary>
    public int? IndexAfter { get; }

    public PropertyPathSegment(IPropertySymbol property)
    {
        Name = property.Name;
        Property = property;
    }

    public PropertyPathSegment(IFieldSymbol field)
    {
        Name = field.Name;
        Field = field;
    }

    private PropertyPathSegment(PropertyPathSegment source, int indexAfter)
    {
        Name = source.Name;
        Property = source.Property;
        Field = source.Field;
        IndexAfter = indexAfter;
    }

    /// <summary>携带段后索引的副本（G7 索引段放行）。</summary>
    public PropertyPathSegment WithIndexAfter(int indexAfter) => new(this, indexAfter);
}

/// <summary>
/// lambda 解析结果（解析期符号态——路径段携带符号；Segments 为空且 Diagnostics 为空表示
/// body 不是支持的成员访问形态，调用方按 / 静默丢弃（同型 Map 口径）处置）
/// </summary>
internal sealed class LambdaParseResult
{
    /// <summary>属性路径段列表（根到叶）</summary>
    public IReadOnlyList<PropertyPathSegment> Segments { get; }

    /// <summary>解析诊断（F02/F04/F05/F06/F07）</summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    /// <summary>是否成功解析为非空属性路径</summary>
    public bool IsValidPath => Segments.Count > 0 && Diagnostics.Count == 0;

    public LambdaParseResult(
        IReadOnlyList<PropertyPathSegment> segments,
        IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        Segments = segments;
        Diagnostics = diagnostics;
    }
}
