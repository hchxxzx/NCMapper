using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// 内联表达式转录引擎
/// 将 Fluent 配置类中的内联 lambda 谓词/转换器（表达式体、无捕获）经符号级限定名重写
/// 转录为生成私有静态方法的规范化表达式文本。
/// <para><b>名称可解析性红线</b>生成文件不继承用户配置文件的 using/别名
/// lambda 参数及其成员链段外的一切标识符（枚举成员/静态成员/BCL 类型与调用/泛型方法/nameof 产物）
/// 一律经 <see cref="SemanticModel"/> 符号解析重写为 <c>global::</c> 限定名；"原样转录"仅限
/// lambda 参数及其成员链段（成员查找按接收者类型解析，不依赖 using，名称可解析安全）。</para>
/// <para><b>禁令</b>（F41/F42，精确 span 定位——非整体拒绝）：捕获标识符（外层局部变量/参数/
/// 本地函数/this）/私有与受保护成员访问 → F41；语句体/dynamic/unsafe/await/查询语法等
/// 白名单外节点 → F42。using 别名经符号解析自然展开为 FQN（用户零感知）。</para>
/// <para>消费者：MapWhen 谓词（B 形态）+ Convert/ConvertBack 内联（F26 收窄后）。</para>
/// </summary>
internal static class InlineTranscriber
{
    /// <summary>
    /// 转录 lambda 表达式体 → 规范化表达式文本（符号级限定名重写后）。
    /// 失败时诊断已发（F41/F42，精确 span），返回 null。
    /// <para>形参支持 0/1/2 参：MapWhen 谓词恒单参；
    /// Convert 族 T0（0 参常量）/T1·T2（单参）/T3（双参）——参数名组承载于
    /// <see cref="TranscriptionResult.ParamNames"/>（体内标识符按名组原样放行）。</para>
    /// </summary>
    /// <param name="lambda">内联 lambda 语法节点（Simple/Parenthesized，0~2 参）</param>
    /// <param name="sm">语义模型</param>
    /// <param name="diagnostics">诊断收集（F41/F42）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>转录结果（参数名组 + 表达式体文本）；失败 null</returns>
    public static TranscriptionResult? TryTranscribe(
        LambdaExpressionSyntax lambda,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        // 形参提取（0~2 参门——MapWhen 谓词恒单参；Convert 族 T0/T1/T2/T3 委托 0/1/2 参；
        // 3 参及以上由编译器签名检查兜底——Func 委托族无此形态）
        string[] paramNames;
        if (lambda is SimpleLambdaExpressionSyntax sl)
        {
            paramNames = new[] { sl.Parameter.Identifier.Text };
        }
        else if (lambda is ParenthesizedLambdaExpressionSyntax pl)
        {
            if (pl.ParameterList.Parameters.Count > 2)
            {
                EmitF42(diagnostics, lambda, "lambda 形参数目不支持（转录引擎仅支持 0~2 参表达式 lambda）");
                return null;
            }
            paramNames = pl.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToArray();
        }
        else
        {
            EmitF42(diagnostics, lambda, "lambda 形态不可识别（转录引擎仅支持 Simple/Parenthesized 表达式 lambda）");
            return null;
        }

        // 体形态门（步骤 1）：语句体 → F42 拒绝
        if (lambda.Body is BlockSyntax block)
        {
            EmitF42(diagnostics, block, "语句体 lambda 不可转录（仅表达式体；块体量级另评）");
            return null;
        }
        if (lambda.Body is not ExpressionSyntax body)
        {
            EmitF42(diagnostics, lambda, "lambda 体形态不可识别");
            return null;
        }

        if (!TryRewriteExpression(body, sm, paramNames, diagnostics, ct, out var text))
            return null;

        return new TranscriptionResult(paramNames, text);
    }

    /// <summary>发射 F42（不可转录节点）——定位到精确子表达式 span</summary>
    private static void EmitF42(List<DiagnosticInfo> diagnostics, SyntaxNode node, string reason)
    {
        diagnostics.Add(new DiagnosticInfo
        {
            Descriptor = DiagnosticDescriptorsFluent.InlineNotTranscribableRule,
            Args = new object[] { node.ToString(), reason },
            Location = node.GetLocation(),
        });
    }

    /// <summary>发射 F41（捕获/私有访问）——定位到精确子表达式 span</summary>
    private static void EmitF41(List<DiagnosticInfo> diagnostics, SyntaxNode node, string what)
    {
        diagnostics.Add(new DiagnosticInfo
        {
            Descriptor = DiagnosticDescriptorsFluent.InlineCaptureOrInaccessibleRule,
            Args = new object[] { what, node.ToString() },
            Location = node.GetLocation(),
        });
    }

    /// <summary>
    /// 递归重写表达式（步骤 2 标识符分类遍历 + 步骤 3 规范文本化）。
    /// 失败时诊断已发（F41/F42 精确 span），返回 false。
    /// </summary>
    private static bool TryRewriteExpression(
        ExpressionSyntax expr,
        SemanticModel sm,
        string[] paramNames,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        out string text)
    {
        ct.ThrowIfCancellationRequested();
        text = "";

        switch (expr)
        {
            case LiteralExpressionSyntax:
                // 字面量（数值/字符串/bool/null/default/字符）自包含——原样
                text = expr.ToString();
                return true;

            case IdentifierNameSyntax ins:
                return TryRewriteIdentifier(ins, sm, paramNames, diagnostics, ct, out text);

            case MemberAccessExpressionSyntax mae:
                return TryRewriteMemberAccess(mae, sm, paramNames, diagnostics, ct, out text);

            case InvocationExpressionSyntax inv:
                return TryRewriteInvocation(inv, sm, paramNames, diagnostics, ct, out text);

            case BinaryExpressionSyntax bin:
            {
                if (!TryRewriteExpression(bin.Left, sm, paramNames, diagnostics, ct, out var left)) return false;
                if (!TryRewriteExpression(bin.Right, sm, paramNames, diagnostics, ct, out var right)) return false;
                text = $"{left} {bin.OperatorToken.Text} {right}";
                return true;
            }

            case PrefixUnaryExpressionSyntax pre:
            {
                if (!TryRewriteExpression(pre.Operand, sm, paramNames, diagnostics, ct, out var operand)) return false;
                text = $"{pre.OperatorToken.Text}{operand}";
                return true;
            }

            case PostfixUnaryExpressionSyntax post when post.IsKind(SyntaxKind.SuppressNullableWarningExpression):
            {
                // null 抑制 !（语义等同直接访问——LambdaParser 同口径）
                if (!TryRewriteExpression(post.Operand, sm, paramNames, diagnostics, ct, out var operand)) return false;
                text = $"{operand}!";
                return true;
            }

            case ParenthesizedExpressionSyntax par:
            {
                if (!TryRewriteExpression(par.Expression, sm, paramNames, diagnostics, ct, out var inner)) return false;
                text = $"({inner})";
                return true;
            }

            case ConditionalExpressionSyntax ternary:
            {
                // 三元 ?:（转录矩阵维度）——三段递归（条件/真/假）
                if (!TryRewriteExpression(ternary.Condition, sm, paramNames, diagnostics, ct, out var condText)) return false;
                if (!TryRewriteExpression(ternary.WhenTrue, sm, paramNames, diagnostics, ct, out var trueText)) return false;
                if (!TryRewriteExpression(ternary.WhenFalse, sm, paramNames, diagnostics, ct, out var falseText)) return false;
                text = $"{condText} ? {trueText} : {falseText}";
                return true;
            }

            case ConditionalAccessExpressionSyntax cae:
            {
                // s?.A / s?.A.B（?. 链——矩阵维度；null 条件段成员经 MemberBinding 承载）
                if (!TryRewriteExpression(cae.Expression, sm, paramNames, diagnostics, ct, out var receiver)) return false;
                if (!TryRewriteBinding(cae.WhenNotNull, sm, paramNames, diagnostics, ct, out var binding)) return false;
                text = $"{receiver}?.{binding}";
                return true;
            }

            case ElementAccessExpressionSyntax eae:
            {
                // 索引访问（常量/变量索引均放行——参数段索引在生成作用域同名可解析）
                if (!TryRewriteExpression(eae.Expression, sm, paramNames, diagnostics, ct, out var target)) return false;
                var args = new List<string>();
                foreach (var arg in eae.ArgumentList.Arguments)
                {
                    if (!TryRewriteExpression(arg.Expression, sm, paramNames, diagnostics, ct, out var argText)) return false;
                    args.Add(argText);
                }
                text = $"{target}[{string.Join(", ", args)}]";
                return true;
            }

            case CastExpressionSyntax cast when cast.Type is PredefinedTypeSyntax:
            {
                // 预定义类型转换（(int)x 等）——类型关键字自包含
                if (!TryRewriteExpression(cast.Expression, sm, paramNames, diagnostics, ct, out var operand)) return false;
                text = $"({cast.Type.ToString()}){operand}";
                return true;
            }

            default:
                EmitF42(diagnostics, expr, $"不可转录节点形态（{expr.Kind()}）——白名单外");
                return false;
        }
    }

    /// <summary>?.-链的成员绑定段（.A / .A.B 形态——接收者由 ConditionalAccess.Expression 承载）</summary>
    private static bool TryRewriteBinding(
        ExpressionSyntax binding,
        SemanticModel sm,
        string[] paramNames,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        out string text)
    {
        // MemberBindingExpressionSyntax：.A（直接绑定）；嵌套 .A.B 为 MemberAccess(memberBinding, .B)
        switch (binding)
        {
            case MemberBindingExpressionSyntax mb:
            {
                if (mb.Name is IdentifierNameSyntax name)
                {
                    text = name.Identifier.ValueText; // 绑定段成员按接收者类型解析（不依赖 using）
                    return true;
                }
                if (mb.Name is GenericNameSyntax gns)
                {
                    // 泛型绑定段类型实参递归校验（复用 F41/F42
                    // 校验路径，取消"从简"豁免）——方法符号或任一类型实参不可解析 → F42 拒绝；
                    // 可解析 → 方法名 + 实参 global:: 限定重写（对齐 GlobalQualifiedType 形态）
                    var gmSymbol = sm.GetSymbolInfo(gns).Symbol as IMethodSymbol;
                    if (gmSymbol == null)
                    {
                        text = "";
                        EmitF42(diagnostics, gns, "泛型方法绑定段符号不可解析");
                        return false;
                    }
                    var rewrittenArgs = new List<string>();
                    foreach (var ta in gns.TypeArgumentList.Arguments)
                    {
                        var taSymbol = sm.GetSymbolInfo(ta).Symbol as ITypeSymbol;
                        if (taSymbol == null)
                        {
                            text = "";
                            EmitF42(diagnostics, ta, "泛型类型实参不可解析");
                            return false;
                        }
                        rewrittenArgs.Add(GlobalQualifiedType(taSymbol));
                    }
                    text = $"{gmSymbol.Name}<{string.Join(", ", rewrittenArgs)}>";
                    return true;
                }
                text = "";
                EmitF42(diagnostics, binding, "?. 绑定段形态不支持");
                return false;
            }
            case MemberAccessExpressionSyntax mae2
                when mae2.Expression is MemberBindingExpressionSyntax innerMb:
            {
                // .A.B 嵌套绑定链——逐段原样（成员查找按接收者类型解析）
                if (!TryRewriteBinding(innerMb, sm, paramNames, diagnostics, ct, out var head))
                {
                    text = "";
                    return false;
                }
                text = $"{head}.{mae2.Name}";
                return true;
            }
            default:
                // ?.[...] 索引绑定 / ?.Invoke() 等罕见形态
                text = "";
                EmitF42(diagnostics, binding, $"?. 绑定段形态不支持（{binding.Kind()}）");
                return false;
        }
    }

    /// <summary>标识符分类重写（步骤 2：参数段原样 / 别名 FQN 展开 / 捕获 F41 / 类型限定名）</summary>
    private static bool TryRewriteIdentifier(
        IdentifierNameSyntax ins,
        SemanticModel sm,
        string[] paramNames,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        out string text)
    {
        text = "";

        // 2a. lambda 参数（及根标识符与形参同名）——原样转录（唯一合法"原样"段；0 参 lambda 名组空）
        if (paramNames.Length > 0 && Array.IndexOf(paramNames, ins.Identifier.ValueText) >= 0)
        {
            text = ins.Identifier.ValueText;
            return true;
        }

        // 2c. using 别名 → GetAliasInfo 命中 → 按符号解析自然展开为 FQN
        var alias = sm.GetAliasInfo(ins, ct);
        if (alias != null)
        {
            // 别名目标（命名空间/类型）——限定名展开（用户零感知）
            text = QualifiedNameOf(alias.Target, ins);
            return true;
        }

        // 其余标识符：符号分类
        var symbolInfo = sm.GetSymbolInfo(ins, ct);
        var sym = symbolInfo.Symbol;

        // dynamic 标识符 → F42（B10/dynamic 禁令）
        if (symbolInfo.CandidateReason == CandidateReason.LateBound)
        {
            EmitF42(diagnostics, ins, "dynamic 绑定标识符不可转录");
            return false;
        }

        switch (sym)
        {
            case ILocalSymbol:
            case IRangeVariableSymbol:
                EmitF41(diagnostics, ins, "捕获局部变量");
                return false;

            case IParameterSymbol:
                EmitF41(diagnostics, ins, "捕获外层参数");
                return false;

            case IMethodSymbol ms:
                // 裸方法组标识符（无调用点/无接收者前缀的罕见形态）——按静态成员限定名重写
                if (ms.MethodKind == MethodKind.LambdaMethod || ms.MethodKind == MethodKind.LocalFunction)
                {
                    EmitF41(diagnostics, ins, ms.MethodKind == MethodKind.LocalFunction ? "局部函数" : "lambda 方法");
                    return false;
                }
                if (!IsAccessibleFromGeneratedCode(ms, out var offender))
                {
                    EmitF41(diagnostics, ins, $"私有/受保护成员访问（{offender}）");
                    return false;
                }
                text = $"{GlobalQualifiedType(ms.ContainingType)}.{ms.Name}";
                return true;

            case IFieldSymbol f:
                if (f.ContainingType == null) goto default;
                if (!IsAccessibleFromGeneratedCode(f, out var fOffender))
                {
                    EmitF41(diagnostics, ins, $"私有/受保护成员访问（{fOffender}）");
                    return false;
                }
                // 实例字段裸标识符（this.X 简写）——this 捕获 → F41
                if (!f.IsStatic)
                {
                    EmitF41(diagnostics, ins, "实例成员访问（this 捕获）");
                    return false;
                }
                text = $"{GlobalQualifiedType(f.ContainingType)}.{f.Name}";
                return true;

            case IPropertySymbol p:
                if (p.ContainingType == null) goto default;
                if (!IsAccessibleFromGeneratedCode(p, out var pOffender))
                {
                    EmitF41(diagnostics, ins, $"私有/受保护成员访问（{pOffender}）");
                    return false;
                }
                if (!p.IsStatic)
                {
                    EmitF41(diagnostics, ins, "实例成员访问（this 捕获）");
                    return false;
                }
                text = $"{GlobalQualifiedType(p.ContainingType)}.{p.Name}";
                return true;

            case INamedTypeSymbol type:
                // 类型标识符（静态类引用/泛型类型名）——限定名
                text = GlobalQualifiedType(type);
                return true;

            case INamespaceSymbol ns:
                // 命名空间根标识符（System.Linq 等——成员访问左侧；裸出现按限定名）
                // global:: 全限定（对齐 GlobalQualifiedType 形态——防御归一；直调
                // 验证现行转录路径类型段吸收该段、无可观测触发面，弱先证登记）
                text = "global::" + ns.ToDisplayString();
                return true;

            case null:
                EmitF42(diagnostics, ins, "标识符无法解析");
                return false;

            default:
                EmitF42(diagnostics, ins, $"不可转录符号形态（{sym?.Kind}）");
                return false;
        }
    }

    /// <summary>成员访问重写——参数根链原样 / 静态成员与枚举成员 FQN 限定 / 非参数根接收者递归</summary>
    private static bool TryRewriteMemberAccess(
        MemberAccessExpressionSyntax mae,
        SemanticModel sm,
        string[] paramNames,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        out string text)
    {
        text = "";

        // 参数根成员链（s.X / s.X.Y——成员查找按接收者类型解析，不依赖 using，原样安全）
        if (IsParamRooted(mae, paramNames))
        {
            if (!TryRewriteExpression(mae.Expression, sm, paramNames, diagnostics, ct, out var receiver)) return false;
            text = $"{receiver}.{mae.Name}";
            return true;
        }

        // 静态成员访问（Rules.M / Kind.A / int.MaxValue / string.Empty——含枚举成员/常量/静态字段属性）
        var symbolInfo = sm.GetSymbolInfo(mae, ct);
        var sym = symbolInfo.Symbol;
        if (sym is IFieldSymbol or IPropertySymbol or IEventSymbol)
        {
            if (sym is IEventSymbol)
            {
                EmitF42(diagnostics, mae, "事件成员访问不可转录");
                return false;
            }
            if (!IsAccessibleFromGeneratedCode(sym, out var offender))
            {
                EmitF41(diagnostics, mae, $"私有/受保护成员访问（{offender}）");
                return false;
            }
            // 实例成员非参数根（capturedThis.X / localVar.X）——接收者递归会在根处触发 F41；
            // 此处直接按符号容器限定名重写仅对静态成立，实例成员走接收者递归：
            if (!sym.IsStatic)
            {
                return TryRewriteReceiver(mae, sm, paramNames, diagnostics, ct, out text);
            }
            text = $"{GlobalQualifiedType(sym.ContainingType!)}.{mae.Name.Identifier.ValueText}";
            return true;
        }

        // 方法组（静态调用左侧 M(...) 的 mae 解析为方法符号——由 Invocation 层处理；此处兜底递归接收者）
        return TryRewriteReceiver(mae, sm, paramNames, diagnostics, ct, out text);
    }

    /// <summary>调用重写——nameof 常量化 / 静态调用 FQN（扩展方法静态化）/ 实例调用按接收者递归</summary>
    private static bool TryRewriteInvocation(
        InvocationExpressionSyntax inv,
        SemanticModel sm,
        string[] paramNames,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        out string text)
    {
        text = "";

        // nameof(X)——常量值直取（生成作用域等价字符串字面量，行为相同且零名称依赖）
        var cv = sm.GetConstantValue(inv, ct);
        if (cv.HasValue && cv.Value is string nameOfValue)
        {
            text = SymbolDisplay.FormatLiteral(nameOfValue, quote: true);
            return true;
        }

        var symbolInfo = sm.GetSymbolInfo(inv, ct);
        var ms = symbolInfo.Symbol as IMethodSymbol
                 ?? (symbolInfo.CandidateSymbols.Length == 1
                     ? symbolInfo.CandidateSymbols[0] as IMethodSymbol
                     : null);

        if (ms == null)
        {
            if (symbolInfo.CandidateReason == CandidateReason.LateBound)
            {
                EmitF42(diagnostics, inv, "dynamic 绑定调用不可转录");
                return false;
            }
            EmitF42(diagnostics, inv, "调用目标无法解析");
            return false;
        }

        if (ms.MethodKind is MethodKind.LambdaMethod or MethodKind.LocalFunction)
        {
            EmitF41(diagnostics, inv, ms.MethodKind == MethodKind.LocalFunction ? "局部函数调用" : "lambda 调用");
            return false;
        }

        if (!IsAccessibleFromGeneratedCode(ms, out var offender))
        {
            EmitF41(diagnostics, inv, $"私有/受保护成员访问（{offender}）");
            return false;
        }

        // 实参递归
        var args = new List<string>();
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (!TryRewriteExpression(arg.Expression, sm, paramNames, diagnostics, ct, out var argText)) return false;
            args.Add(argText);
        }

        // 扩展方法调用语法（receiver.Method()——ReducedFrom != null 即"缩减形态"标志）——
        // 生成作用域无 using 可见性，改静态调用形态 global::ExtType.Method(receiver, args)
        //（AOT/名称解析双安全）。静态调用形态 Type.Method(arg)（ReducedFrom == null）走下方
        // 常规静态分支——实参序即签名序，误插 receiver 会产出 Count(Enumerable, t) 形态
        if (ms.IsExtensionMethod && ms.ReducedFrom != null && inv.Expression is MemberAccessExpressionSyntax extMae)
        {
            if (!TryRewriteExpression(extMae.Expression, sm, paramNames, diagnostics, ct, out var receiver)) return false;
            args.Insert(0, receiver);
            text = $"{GlobalQualifiedType(ms.ContainingType!)}.{ms.Name}({string.Join(", ", args)})";
            return true;
        }

        // 参数根链实例调用（s.X.ToString() / s.X.Contains(...)——实例方法查找按接收者类型，原样安全）
        if (inv.Expression is MemberAccessExpressionSyntax mae)
        {
            if (IsParamRooted(mae, paramNames) || !ms.IsStatic)
            {
                // 接收者递归（参数根原样 / 其他接收者限定名重写——静态类引用等）。
                // 递归对象须为 mae.Expression（真正接收者）——整 mae 入手会把末段成员先行转录，
                // 再拼 .{mae.Name}(...) 双拼产出 s.Trim.Trim() 非法文本（验收探针实证）
                if (!TryRewriteExpression(mae.Expression, sm, paramNames, diagnostics, ct, out var receiver)) return false;
                text = $"{receiver}.{mae.Name}({string.Join(", ", args)})";
                return true;
            }

            // 静态调用（Type.M(args)——含 BCL/用户静态/泛型）
            var typeFqn = GlobalQualifiedType(ms.ContainingType!);
            var nameText = mae.Name is GenericNameSyntax gns ? gns.ToString() : mae.Name.Identifier.ValueText;
            // 用户泛型静态调用显式类型实参保留（gns 含 <T>；推断实参由生成作用域重推——参数类型不变结果不变）
            text = $"{typeFqn}.{nameText}({string.Join(", ", args)})";
            return true;
        }

        if (inv.Expression is IdentifierNameSyntax idn)
        {
            // 裸静态调用 M(args)（同类型内）——限定名展开
            text = $"{GlobalQualifiedType(ms.ContainingType!)}.{idn.Identifier.ValueText}({string.Join(", ", args)})";
            return true;
        }

        EmitF42(diagnostics, inv, $"调用目标形态不支持（{inv.Expression.Kind()}）");
        return false;
    }

    /// <summary>成员访问接收者递归（非参数根、非静态成员——局部变量/参数/类型链的成员访问）</summary>
    private static bool TryRewriteReceiver(
        MemberAccessExpressionSyntax mae,
        SemanticModel sm,
        string[] paramNames,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        out string text)
    {
        if (!TryRewriteExpression(mae.Expression, sm, paramNames, diagnostics, ct, out var receiver))
        {
            text = "";
            return false;
        }
        text = $"{receiver}.{mae.Name}";
        return true;
    }

    /// <summary>判定成员访问链是否以 lambda 参数为根（s.X.Y 的 s 段；双参 lambda 任一形参名均认根）</summary>
    private static bool IsParamRooted(MemberAccessExpressionSyntax mae, string[] paramNames)
    {
        if (paramNames.Length == 0) return false;
        var expr = mae.Expression;
        while (expr is MemberAccessExpressionSyntax inner)
            expr = inner.Expression;
        if (expr is not IdentifierNameSyntax root) return false;
        var name = root.Identifier.ValueText;
        // 与参数同名的类型/属性歧义兜底：符号解析确认根是 lambda 参数
        for (var i = 0; i < paramNames.Length; i++)
        {
            if (name == paramNames[i]) return true;
        }
        return false;
    }

    /// <summary>可见性契约——生成代码位于 internal static class GeneratedMapper（同程序集独立类型）：
    /// private/protected 跨类不可访问（public/internal/protected internal 可见）</summary>
    private static bool IsAccessibleFromGeneratedCode(ISymbol symbol, out string offender)
    {
        if (symbol is IMethodSymbol m && m.MethodKind is MethodKind.LambdaMethod or MethodKind.LocalFunction)
        {
            offender = m.Name;
            return false;
        }
        var acc = symbol.DeclaredAccessibility;
        if (acc is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
        {
            offender = $"{symbol.ContainingType?.ToDisplayString()}.{symbol.Name}";
            return false;
        }
        offender = "";
        return true;
    }

    /// <summary>限定名显示格式（B10 红线口径）——与 <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>
    /// 的差异：剔除 UseSpecialTypes（string/int 等语言关键字形态）——特殊类型同样落
    /// global:: 全限定名（与枚举/用户类型同构，名称可解析性零 using 依赖形态统一）。</summary>
    private static readonly SymbolDisplayFormat s_qualifiedFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    /// <summary>类型的 global:: 全限定显示名（生成代码名称可解析锚）</summary>
    private static string GlobalQualifiedType(ITypeSymbol? type)
        => type == null ? "" : type.ToDisplayString(s_qualifiedFormat);

    /// <summary>别名目标的限定名（命名空间/类型均覆盖）</summary>
    private static string QualifiedNameOf(ISymbol target, SyntaxNode node)
        => target switch
        {
            INamespaceSymbol ns => ns.ToDisplayString(),
            ITypeSymbol t => GlobalQualifiedType(t),
            _ => node.ToString(),
        };
}

/// <summary>转录产物（lambda 形参名组 + 重写后的表达式体文本）——生成私有静态方法的原料</summary>
internal sealed class TranscriptionResult
{
    /// <summary>lambda 形参名组（生成方法形参沿用——体内标识符已按此名组重写；0 参 lambda 为空组）</summary>
    public string[] ParamNames { get; }

    /// <summary>符号级限定名重写后的表达式体文本（非参数名称已全部限定化——B10 红线）</summary>
    public string Body { get; }

    /// <summary>单参便捷视图（MapWhen 谓词恒单参；0/2 参 lambda 返回 null）</summary>
    public string? ParamName => ParamNames.Length == 1 ? ParamNames[0] : null;

    /// <summary>单参构造（MapWhen 谓词先例形态）</summary>
    public TranscriptionResult(string paramName, string body)
    {
        ParamNames = new[] { paramName };
        Body = body;
    }

    /// <summary>名组构造（Convert 族 T0 空/T1·T2 单/T3 双）</summary>
    public TranscriptionResult(string[] paramNames, string body)
    {
        ParamNames = paramNames;
        Body = body;
    }
}
