using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

internal static partial class FluentExtractor
{
    /// <summary>
    /// 解析 FromSource&lt;TSubSource&gt; 的 TSubSource 泛型实参（1 参
    /// 显式，CS0305/CS0411 由编译器先挡，Q3）；不可解析返回 null。
    /// </summary>
    private static INamedTypeSymbol? ParseFromSourceTypeArg(
        InvocationExpressionSyntax inv, SemanticModel sm, CancellationToken ct)
    {
        var args = GetGenericArguments(inv, out var hasGeneric);
        if (!hasGeneric || args.Count != 1) return null;
        return sm.GetSymbolInfo(args[0], ct).Symbol as INamedTypeSymbol;
    }

    /// <summary>
    /// 解析 overrideIgnore 豁免门实参
    /// 命名实参（<c>overrideIgnore: true</c>）优先，无命名实参时按位置索引取值
    ///（Map/ConvertBack/Convert=2、Mask=1）；GetConstantValue 覆盖字面量与 const bool。
    /// 解析失败返回 false（编译器已报实参类型错误—— / 静默）。
    /// </summary>
    private static bool ParseOverrideIgnoreArg(
        InvocationExpressionSyntax inv, SemanticModel sm, int positionalIndex, List<DiagnosticInfo> diagnostics)
    {
        var args = inv.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].NameColon is { } nameColon
                && nameColon.Name.Identifier.ValueText == "overrideIgnore")
            {
                // 命名实参形态同在检测面
                // 求值无值（变量等）→ 显式诊断；处置 = 声明照常生成 + 默认值生效（不跳过）。
                var namedCv = sm.GetConstantValue(args[i].Expression);
                if (!namedCv.HasValue)
                {
                    AddNonStaticArgDiagnostic(args[i].Expression, diagnostics);
                    return false;
                }
                return namedCv.Value is true;
            }
        }
        if (args.Count > positionalIndex && args[positionalIndex].NameColon == null)
        {
            var cv = sm.GetConstantValue(args[positionalIndex].Expression);
            if (!cv.HasValue)
            {
                AddNonStaticArgDiagnostic(args[positionalIndex].Expression, diagnostics);
                return false;
            }
            return cv.Value is true;
        }
        return false;
    }

    /// <summary>
    /// 实参不可静态求值（变量等）→ 显式诊断。
    /// 判定面 = 语义常量（字面量/枚举成员/const 标识符，GetConstantValue 有值）放行、求值无值 → 诊断；
    /// 处置 = 声明照常生成 + 无效实参按默认值生效（不跳过声明）——映射行为与修复前逐字节一致。
    /// </summary>
    private static void AddNonStaticArgDiagnostic(ExpressionSyntax argExpr, List<DiagnosticInfo> diagnostics)
    {
        diagnostics.Add(new DiagnosticInfo
        {
            Descriptor = DiagnosticDescriptorsFluent.NonStaticEvaluableArgIgnoredRule,
            Args = new object[] { argExpr.ToString() },
            Location = argExpr.GetLocation(),
        });
    }

    /// <summary>
    /// 解析 Direction 方向实参——枚举底层 int
    ///（0=Both/1=ToDto/2=ToEntity，与 PropertyRefFromSymbol MapIgnoreDirection 采集同口径）。
    /// GetConstantValue 对枚举成员返回装箱枚举值，经 Convert.ToInt32 解底层；
    /// 解析失败返回 null（编译器已报—— / 静默，null = 双向默认语义）。
    /// </summary>
    private static int? ParseDirectionArg(
        InvocationExpressionSyntax inv, SemanticModel sm, int positionalIndex, CancellationToken ct,
        List<DiagnosticInfo> diagnostics)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count <= positionalIndex) return null;
        // （-）：命名实参前置检查——Direction 乱序实参
        // （如 Ignore(direction: dir, dest: d => d.X)）会使位置索引取到非方向实参，
        // 若直接判定将把"现状静默取默认"恶化为"误报 Error"。带 NameColon 的实参不在本解析面
        //（先例：ParseOverrideIgnoreArg 的位置分支 NameColon == null 门）。
        if (args[positionalIndex].NameColon != null) return null;
        var cv = sm.GetConstantValue(args[positionalIndex].Expression);
        if (!cv.HasValue || cv.Value == null)
        {
            // Direction 实参不可静态求值——方向意图静默丢失
            // → 显式诊断；处置 = 声明照常生成 + 默认值（Both）生效（不跳过声明）。
            AddNonStaticArgDiagnostic(args[positionalIndex].Expression, diagnostics);
            return null;
        }
        // 枚举装箱值（Direction.ToDto → Direction 装箱）与 int 常量统一解底层 int
        if (cv.Value is int direct) return direct;
        if (cv.Value is Enum) return Convert.ToInt32(cv.Value, System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>解析双 lambda 实参（Map/Convert：dest + src/converter）——实参不足或非 lambda 静默跳过</summary>
    private static (LambdaParseResult? dest, LambdaParseResult? src) ParseTwoLambdaArgs(
        InvocationExpressionSyntax inv, SemanticModel sm, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count < 2) return (null, null);
        return (ParseLambdaArg(args[0].Expression, sm, diagnostics, ct),
            ParseLambdaArg(args[ConverterArgIndex].Expression, sm, diagnostics, ct));
    }

    /// <summary>解析单 lambda 实参（Ignore/Mask/MapTree：dest）——实参不足或非 lambda 静默跳过</summary>
    private static LambdaParseResult? ParseSingleLambdaArg(
        InvocationExpressionSyntax inv, SemanticModel sm, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count < 1) return null;
        return ParseLambdaArg(args[0].Expression, sm, diagnostics, ct);
    }

    private static LambdaParseResult? ParseLambdaArg(
        ExpressionSyntax expr, SemanticModel sm, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        if (expr is not LambdaExpressionSyntax lambda)
        {
            // .3：dest/src 位实参为非 lambda 形态（方法组、委托变量
            // 等可编译但无法确定属性路径的写法）——原静默返回 null 致整条声明消失 → 显式拒绝。
            // 对照 F26/F40 实参形态族（Error）；本函数为全部终端调用位的公共判定点。
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.NonLambdaArgNotSupportedRule,
                Args = new object[] { expr.ToString() },
                Location = expr.GetLocation(),
            });
            return null;
        }
        var result = LambdaParser.Parse(lambda, sm, ct);
        diagnostics.AddRange(result.Diagnostics);
        return result;
    }

    /// <summary>解析 For&lt;S,T&gt; 泛型实参（TypeSyntax GetSymbolInfo；本期最小实现仅 INamedTypeSymbol——X4 边界）</summary>
    private static (INamedTypeSymbol? source, INamedTypeSymbol? target) ParseForTypeArgs(
        InvocationExpressionSyntax forInvocation, SemanticModel sm, CancellationToken ct)
    {
        var args = GetGenericArguments(forInvocation, out _);
        if (args.Count != 2) return (null, null);
        var srcSym = sm.GetSymbolInfo(args[0], ct).Symbol as INamedTypeSymbol;
        var tgtSym = sm.GetSymbolInfo(args[1], ct).Symbol as INamedTypeSymbol; // B2-26：Target 泛型实参位（For<S,T> 第 2 位）
        return (srcSym, tgtSym);
    }

    /// <summary>解析 Mask&lt;TMask, TProp&gt; 的 TMask 泛型实参（S-2a 必带 mask type；不可解析返回 null）</summary>
    private static INamedTypeSymbol? ParseMaskTypeArgument(
        InvocationExpressionSyntax inv, SemanticModel sm, CancellationToken ct)
    {
        if (inv.Expression is MemberAccessExpressionSyntax mae
            && mae.Name is GenericNameSyntax gns
            && gns.TypeArgumentList.Arguments.Count >= 1)
        {
            return sm.GetSymbolInfo(gns.TypeArgumentList.Arguments[0], ct).Symbol as INamedTypeSymbol;
        }
        return null;
    }
}
