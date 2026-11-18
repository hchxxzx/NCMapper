// ============================================================================
// SourceGenEscape — 生成代码字面量转义单点
// ----------------------------------------------------------------------------
// 原 4 份实现/5 发射点（收敛于此）：
//   ① CodeGenerator.Literal(string)（原私有实现，删除改指）
//   ② CodeGenerator.FormatExplicitDefault string 分支（原内联五规则，改指）
//   ③ CodeGenerator.FormatExplicitDefault char 分支（原内联 ' 口径五规则，改指）
//   ④ SnapshotMetadataEmitter.Literal(string)（原私有实现，删除改指）
//   ⑤ CodeGenerator.Validate.cs Message 发射（原仅转义 "，升级为五规则改指——X5 零文本判定：
//     message 上游为标识符/数字/中文标点，五规则转义对既有文本零影响）
// 规则口径：字符串 `\\ \" \r \n \t`；字符 `\\ ' \n \r \t`（CWE-94 防代码注入）。
// 新增 Identifier——用户成员名引用关键字 @ 转义单点。
// ============================================================================
using Microsoft.CodeAnalysis.CSharp;
namespace NCMapper.SourceGen;

/// <summary>
/// 生成代码字符串/字符字面量转义单点（原 4 份实现/5 发射点收敛于此）
/// </summary>
internal static class SourceGenEscape
{
    /// <summary>
    /// 用户成员名引用转义——C# 保留关键字与上下文关键字
    /// （value/partial/when 等——属性名场景下上下文关键字亦触发编译问题）加 @ 前缀。
    /// <para>判定来源 = <see cref="SyntaxFacts.GetKeywordKind"/>（不维护静态表）；语义对齐 Roslyn
    /// <c>SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers</c>（Roslyn 枚举值，用例先例
    /// InlineTranscriber.cs:582，非本仓 API——文档口径"同语义"）。</para>
    /// <para>与 <c>CodeGenerator.SanitizeIdentifier</c> 分工：后者净化<b>生成标识符</b>（字段名/
    /// 方法名）的非法字符，本函数转义<b>引用用户成员名</b>的关键字——二者不合并、不互调。</para>
    /// </summary>
    /// <param name="name">用户成员名（属性名等）</param>
    /// <returns>关键字加 @ 前缀的引用安全形态</returns>
    internal static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>
    /// 生成 C# 字符串字面量（转义，防代码注入）
    /// </summary>
    /// <param name="value">字符串值</param>
    /// <returns>转义后的字面量（含外层双引号）</returns>
    internal static string Literal(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
        return "\"" + escaped + "\"";
    }

    /// <summary>
    /// 生成 C# 字符字面量（' 口径转义，防代码注入）
    /// </summary>
    /// <param name="value">字符值</param>
    /// <returns>转义后的字面量（含外层单引号）</returns>
    internal static string Literal(char value)
    {
        string escaped = value switch
        {
            '\\' => "\\\\",
            '\'' => "\\'",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => value.ToString(),
        };
        return "'" + escaped + "'";
    }
}
