using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// Fluent 入口方法定位器（step 1 / A1.5 决策）
/// 在 [MapperConfig] 类内定位所有 <c>static void M(FluentMappingConfig cfg)</c> 方法（方法名任意）。
/// <para>入口锚点：类级 [MapperConfig] 特性（GeneratorAttributeSyntaxContext 由
/// ForAttributeWithMetadataName 按特性命中）；方法签名约定：static + void + 恰好 1 参数 +
/// 参数类型 FQN 精确匹配<c>NCMapper.FluentMappingConfig</c>（S-2b——防自定义同名类型误命中）。</para>
/// <para>类内没有任何匹配签名方法 → F15 启发式诊断（与 NCMAP019 同口径：基于显式声明信号）。</para>
/// <para>partial 类支持：经符号 GetMembers() 迭代（跨 partial 部分合并视图），方法语法经
/// DeclaringSyntaxReferences 取回——不依赖 TargetNode 单部分视图。</para>
/// </summary>
internal static class ConfigureMethodLocator
{
    /// <summary>Fluent 配置根参数类型 FQN（S-2b 精确匹配——不匹配用户自定义同名类型）</summary>
    internal const string FluentMappingConfigFqn = "NCMapper.FluentMappingConfig";

    /// <summary>
    /// 在 [MapperConfig] 类符号内定位所有匹配签名的 Configure 方法
    /// </summary>
    /// <param name="typeSymbol">[MapperConfig] 类符号（含 partial 合并视图）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>匹配方法声明语法列表（可能为空 → 调用方发 F15）</returns>
    public static List<MethodDeclarationSyntax> Locate(INamedTypeSymbol typeSymbol, CancellationToken ct)
    {
        var results = new List<MethodDeclarationSyntax>();
        foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();
            if (!IsMatchingConfigureMethod(method))
                continue;
            // partial/分部声明：每处声明语法都处理（正常单实现仅 1 处）
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (reference.GetSyntax(ct) is MethodDeclarationSyntax syntax)
                    results.Add(syntax);
            }
        }
        return results;
    }

    /// <summary>
    /// 判定方法符号是否符合 Fluent Configure 签名约定：static void + 单参数 +
    /// 参数类型 FQN 精确匹配 NCMapper.FluentMappingConfig（方法名任意，A1.5）
    /// </summary>
    /// <param name="method">方法符号</param>
    /// <returns>符合返回 true</returns>
    public static bool IsMatchingConfigureMethod(IMethodSymbol method)
    {
        // 必须为 static（源生成器静态扫描，无运行时实例化）
        if (!method.IsStatic) return false;
        // 必须返回 void
        if (method.ReturnsVoid == false) return false;
        // 必须恰好 1 个参数
        if (method.Parameters.Length != 1) return false;
        // 参数类型 FQN 精确匹配（S-2b）
        var paramTypeName = method.Parameters[0].Type.ToDisplayString();
        return paramTypeName == FluentMappingConfigFqn;
    }
}
