using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen.Tests.Helpers;

/// <summary>
/// 封装源生成器输出的单个方法，提供方法名、签名和规范化后的方法体
/// </summary>
public class GeneratedMethod
{
    /// <summary>
    /// 从方法声明语法节点构建 GeneratedMethod 实例
    /// </summary>
    /// <param name="declarationSyntax">方法声明语法节点</param>
    public GeneratedMethod(MethodDeclarationSyntax declarationSyntax)
    {
        Name = declarationSyntax.Identifier.ToString();
        Signature = $"{declarationSyntax.ReturnType.ToString()} {Name}{declarationSyntax.ParameterList.ToString().Trim()}";
        Body = ExtractBody(declarationSyntax);
    }

    /// <summary>
    /// 方法名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 方法签名（返回类型 + 名称 + 参数列表）
    /// </summary>
    public string Signature { get; }

    /// <summary>
    /// 规范化后的方法体（去大括号、统一换行、去除统一缩进）
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// 提取方法体并做规范化处理
    /// <para>支持两种方法体形式：</para>
    /// <para>- 块体（Block Body）：{ ... }，规范化步骤：提取文本 → 去首尾空白 → 去大括号 → 统一换行 → 去除统一缩进 → 重新拼接</para>
    /// <para>- 表达式体（Expression Body）：=> expr，提取 => 后的表达式文本</para>
    /// <para>参考 Mapperly GeneratedMethod.ExtractBody 实现</para>
    /// </summary>
    /// <param name="declarationSyntax">方法声明语法节点</param>
    /// <returns>规范化后的方法体文本；无方法体时返回空字符串</returns>
    private static string ExtractBody(MethodDeclarationSyntax declarationSyntax)
    {
        // 表达式体（=> expr）：投影方法等使用此形式
        if (declarationSyntax.Body == null)
        {
            if (declarationSyntax.ExpressionBody != null)
            {
                return declarationSyntax.ExpressionBody
                    .Expression.ToString()
                    .ReplaceLineEndings()
                    .Trim(' ', '\r', '\n');
            }
            return string.Empty;
        }

        // 步骤 1-3: 去除首尾空白、大括号、再次去除换行
        var body = declarationSyntax
            .Body.ToFullString()
            .Trim(' ', '\r', '\n')
            .TrimStart('{')
            .TrimEnd('}')
            .Trim('\r', '\n');

        // 步骤 4: 统一换行符
        body = body.ReplaceLineEndings();

        // 步骤 5: 计算首行缩进并批量去除
        var lines = body.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return string.Empty;

        var indentCount = lines[0].TakeWhile(c => c == ' ').Count();
        if (indentCount == 0)
            return string.Join(Environment.NewLine, lines).Trim(' ', '\r', '\n');

        var indent = lines[0][..indentCount];
        var normalizedLines = lines.Select(l =>
            l.StartsWith(indent) ? l[indentCount..] : l);

        // 步骤 6: 重新拼接并 Trim
        return string.Join(Environment.NewLine, normalizedLines).Trim(' ', '\r', '\n');
    }
}
