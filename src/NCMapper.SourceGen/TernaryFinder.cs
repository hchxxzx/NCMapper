using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>三元表达式语法 walker（F08 黑名单——配置层三元：<c>cond ? .Map(...) : .Ignore(...)</c> 等
/// 配置流分支形态）。：lambda 实参内三元豁免（不下降）——转录引擎落地后
/// lambda 体为可转录表达式数据（转录矩阵含"三元"维度），非配置流分支；
/// 原口径（spike 8/8"含 lambda 实参内三元"）为转录引擎前置期的保守全禁，随 B 形态启用收窄。</summary>
internal sealed class TernaryFinder : CSharpSyntaxWalker
{
    /// <summary>是否命中三元表达式</summary>
    public bool Found { get; private set; }

    /// <inheritdoc />
    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        Found = true;
    }

    /// <summary>lambda 体不下降（转录引擎消费域——三元在 MapWhen/Convert 内联谓词内合法）</summary>
    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        // 故意不调用 base.Visit——跳过 lambda 体整棵子树
    }

    /// <summary>lambda 体不下降（同 <see cref="VisitSimpleLambdaExpression"/>——括号形态）</summary>
    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        // 故意不调用 base.Visit——跳过 lambda 体整棵子树
    }
}
