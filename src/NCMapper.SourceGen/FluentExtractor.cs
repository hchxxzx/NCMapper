using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// Fluent 扫描器主入口（管线）— 从 [MapperConfig] 特性上下文
/// 提取 Fluent 通道映射意图并完整物化为 <see cref="MappingSnapshot"/>。
/// <para>管线（transform 阶段内一次完成，出口零符号持有）：</para>
/// <list type="number">
/// <item><see cref="ConfigureMethodLocator"/> 定位 <c>static void M(FluentMappingConfig cfg)</c> 方法（无 → F15）</item>
/// <item>ExtractMethod 解析方法体（block / expression-bodied / 无方法体三形态）</item>
/// <item>CheckDynamicRejection 拒绝动态配置（F08 黑名单）</item>
/// <item>ChainCallParser.UnwrapChain 反向展开链式调用</item>
/// <item>ExtractChainStatement 分发各链节点（Map/Ignore/Mask/Convert/MapTree/NoAutoInclude/AfterMapping）</item>
/// <item><see cref="MappingSnapshotBuilder"/> 完整物化（复用 MappingExtractor.Materialize 符号遍历）</item>
/// </list>
/// <para> / 拍板：诊断集未覆盖的结构性错误场景（链未以 For 起头 / For 泛型解析失败 /
/// 未知链方法名 / lambda 参数非表达式等）静默跳过——用户代码本身是 C# 编译错误，源生成器不重复报。</para>
/// </summary>
internal static partial class FluentExtractor
{
    // ---- Fluent 系实参位具名化——原 9 处 args[1] 魔法索引消除 ----

    /// <summary>converter 实参位（Map/Convert/ConvertBack 族第 2 实参——dest 之后；ParseTwoLambdaArgs 同位）</summary>
    private const int ConverterArgIndex = 1;

    /// <summary>源实参位（FromSource 速记 src / CtorParamBinding 源实参——lambda 或属性名字符串）</summary>
    private const int SourceArgIndex = 1;

    /// <summary>谓词实参位（MapWhen 第 2 实参——dest 之后）</summary>
    private const int PredicateArgIndex = 1;

    /// <summary>
    /// 从 [MapperConfig] 特性语法上下文提取 Fluent 通道产物（transform 出口——纯值，缓存零符号持有）
    /// </summary>
    /// <param name="ctx">特性语法上下文（[MapperConfig] 类）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>提取结果（完整物化快照 + For 位置 + 诊断）；非类目标返回 null</returns>
    public static FluentExtractionResult? Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var typeSymbol = ctx.TargetSymbol as INamedTypeSymbol;
        if (typeSymbol == null) return null;

        return ExtractCore(ctx.SemanticModel.Compilation, typeSymbol, ctx.TargetNode as TypeDeclarationSyntax, ct);
    }

    /// <summary>
    /// [K2 拆分 ] Extract 符号级内核——承接 ctx 解包后的 Fluent 通道完整物化逻辑
    /// <para>薄壳保留 ctx 依赖校验，内核入参不重复校验</para>
    /// <para>纯移动纪律：语句序列、局部变量名、诊断构造原样搬移自原 Extract</para>
    /// <para>直调入口——测试侧经 BuildCompilation + 符号定位直调，绕过 ForAttributeWithMetadataName 管线</para>
    /// </summary>
    /// <param name="compilation">编译上下文（语义模型来源，替代 ctx.SemanticModel.Compilation）</param>
    /// <param name="typeSymbol">[MapperConfig] 类符号（薄壳已非空校验）</param>
    /// <param name="classSyntax">类声明语法（ctx.TargetNode 转型，可空——partial 方法树可能异于锚点树）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Fluent 通道提取结果（完整物化快照 + For 位置 + 诊断）</returns>
    public static FluentExtractionResult? ExtractCore(Compilation compilation, INamedTypeSymbol typeSymbol, TypeDeclarationSyntax? classSyntax, CancellationToken ct)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var configureMethods = ConfigureMethodLocator.Locate(typeSymbol, ct);

        // step 1b：F15 启发式诊断——[MapperConfig] 类内没有任何匹配签名方法
        //（与 NCMAP019 同口径：用户已显式声明入口意图，非越界猜意图）
        if (configureMethods.Count == 0)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.MapperConfigNoMatchingMethodRule,
                Args = new object[] { typeSymbol.ToDisplayString() },
                Location = typeSymbol.Locations.FirstOrDefault(),
            });
            return new FluentExtractionResult(typeSymbol.Name, new List<FluentDeclaration>(), ToDiagnostics(diagnostics));
        }

        var declarations = new List<FluentDeclaration>();
        // MapSubclass 合成子类对（前置 1——登记即声明，
        // 未独立声明的子类对复用既有单对生成：合成 FluentSnapshot（纯自动匹配，零显式声明）
        // → MappingSnapshotBuilder.Build 全复用 → 平行流 SyntheticDeclarations（Arbiter 三查
        // 去重：显式声明对优先，命中即弃——"已在 mappings → 引用不重发，不在 → 生成"的
        // FQN 二元组查表语义在合并层值化实现）。同 Configure 内显式 For<S,T> 链对集合
        // 预收集，合成前本地去重（Arbiter 层还有跨通道/跨类二道防线）。
        var syntheticDeclarations = new List<FluentDeclaration>();
        foreach (var method in configureMethods)
        {
            ct.ThrowIfCancellationRequested();
            // partial 方法语法与特性锚点树可能不同——经 Compilation 取对应树语义模型
            var sm = compilation.GetSemanticModel(method.SyntaxTree);
            var methodSnapshots = ExtractMethod(method, sm, diagnostics, ct);
            // 本 Configure 内显式声明的 (S,T) FQN 集合（合成去重第一道——显式优先）
            // 惰性化——唯一消费点为下方 SubclassDeclarations>0
            // 分支（显式优先查表），无子类声明的链（多数）免两次全链 ToDisplayString 物化；
            // 首次消费时构造一次，键集 = 全 methodSnapshots（跨迭代消费语义保持——与原 eager
            // 形态逐位一致）
            HashSet<(string, string)>? declaredKeys = null;
            foreach (var fs in methodSnapshots)
            {
                var materialized = MappingSnapshotBuilder.Build(fs, classSyntax, ct);
                if (materialized == null) continue;
                var forLocation = MappingExtractor.SnapshotLocationInternal(fs.ForLocation);
                declarations.Add(new FluentDeclaration(materialized, forLocation));

                // 合成注入：符号态直接遍历（复刻 Build 拦截条件——基类自身登记跳过；
                // FQN 同对去重；declaredKeys 显式优先）；合成 fs 零显式声明 → 纯自动匹配
                //（含子类专有属性）
                if (fs.SubclassDeclarations.Count > 0)
                {
                    if (declaredKeys == null)
                    {
                        declaredKeys = new HashSet<(string, string)>();
                        foreach (var snap in methodSnapshots)
                            declaredKeys.Add((snap.SourceType.ToDisplayString(), snap.TargetType.ToDisplayString()));
                    }
                    var syntheticSeen = new HashSet<(string, string)>();
                    foreach (var sd in fs.SubclassDeclarations)
                    {
                        if (SymbolEqualityComparer.Default.Equals(sd.SourceType, fs.SourceType))
                            continue; // 基类自身登记（与 Build 拦截同条件，F47 已在 Build 报）
                        var subKey = (sd.SourceType.ToDisplayString(), sd.TargetType.ToDisplayString());
                        if (declaredKeys.Contains(subKey)) continue; // 显式声明优先
                        if (!syntheticSeen.Add(subKey)) continue; // 同链同对重复
                        var subFs = new FluentSnapshot(sd.SourceType, sd.TargetType, fs.ForLocation);
                        var subSnapshot = MappingSnapshotBuilder.Build(subFs, classSyntax, ct);
                        // 落空即诊断——合成快照被跳过（非 public 等）
                        // 或零可映射属性时，登记按落空上报（防静默丢子类字段；发射端 generatableKeys
                        // 查表防线同步丢弃该分派条目，防 CS0103 引用断裂）
                        if (subSnapshot == null || subSnapshot.SkipCodeGeneration
                            || subSnapshot.PropertyMappings.Count == 0)
                        {
                            diagnostics.Add(new DiagnosticInfo
                            {
                                Descriptor = DiagnosticDescriptorsFluent.SubclassTargetEmptyRule,
                                Args = new object[]
                                {
                                    sd.SourceType.ToDisplayString(), sd.TargetType.ToDisplayString(),
                                    fs.SourceType.ToDisplayString(), fs.TargetType.ToDisplayString(),
                                },
                                Location = sd.Location,
                            });
                            continue;
                        }
                        syntheticDeclarations.Add(new FluentDeclaration(subSnapshot, forLocation));
                    }
                }
            }
        }
        return new FluentExtractionResult(typeSymbol.Name, declarations, ToDiagnostics(diagnostics), syntheticDeclarations);
    }

    /// <summary>
    /// 解析单个 Configure 方法体（三形态：block body / expression-bodied / 无方法体跳过）
    /// </summary>
    /// <returns>解析出的 FluentSnapshot 列表（每条链 1 个）</returns>
    private static List<FluentSnapshot> ExtractMethod(
        MethodDeclarationSyntax method,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var snapshots = new List<FluentSnapshot>();
        var chainRoots = new List<InvocationExpressionSyntax>();

        if (method.Body != null)
        {
            foreach (var stmt in method.Body.Statements)
            {
                ct.ThrowIfCancellationRequested();
                // 难点⑥：动态配置黑名单——不通过则该语句被拒绝，其余语句继续
                if (!CheckDynamicRejection(stmt, diagnostics)) continue;
                if (stmt is ExpressionStatementSyntax ess
                    && ess.Expression is InvocationExpressionSyntax inv)
                    chainRoots.Add(inv);
            }
        }
        else if (method.ExpressionBody != null)
        {
            // expression-bodied：=> cfg.For<...>()...（直接处理原树节点，不合成 ExpressionStatement）
            if (method.ExpressionBody.Expression is InvocationExpressionSyntax inv)
                chainRoots.Add(inv);
        }
        else
        {
            // 抽象/接口声明——无方法体，跳过（非错误）
            return snapshots;
        }

        foreach (var chainRoot in chainRoots)
        {
            ct.ThrowIfCancellationRequested();
            ExtractChainStatement(chainRoot, sm, snapshots, diagnostics, ct);
        }
        return snapshots;
    }

    /// <summary>
    /// 难点⑥：动态配置拒绝（F08 黑名单）——if/for/foreach/while/switch/try 控制流、
    /// 局部变量声明、三元 ?:、赋值 = 任一出现即拒；白名单：纯链式静态调用。
    /// </summary>
    /// <param name="stmt">语句节点</param>
    /// <param name="diagnostics">诊断收集</param>
    /// <returns>true = 语句通过（进入链解析）；false = 拒绝（该语句不参与物化）</returns>
    private static bool CheckDynamicRejection(StatementSyntax stmt, List<DiagnosticInfo> diagnostics)
    {
        switch (stmt)
        {
            case IfStatementSyntax:
            case ForStatementSyntax:
            case ForEachStatementSyntax:
            case WhileStatementSyntax:
            case SwitchStatementSyntax:
            case TryStatementSyntax:
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.FluentDynamicConfigRule,
                    Args = new object[] { DescribeStatementKind(stmt) },
                    Location = stmt.GetLocation(),
                });
                return false;
            case LocalDeclarationStatementSyntax:
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.FluentDynamicConfigRule,
                    Args = new object[] { "local variable declaration" },
                    Location = stmt.GetLocation(),
                });
                return false;
            case ExpressionStatementSyntax ess:
                // 三元表达式（含 lambda 实参内的三元——spike 8/8 验证口径）
                var ternaryWalker = new TernaryFinder();
                ess.Accept(ternaryWalker);
                if (ternaryWalker.Found)
                {
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.FluentDynamicConfigRule,
                        Args = new object[] { "ternary (?:)" },
                        Location = stmt.GetLocation(),
                    });
                    return false;
                }
                // 变量重新赋值（cfg = cfg.For<...>()）
                if (ess.Expression is AssignmentExpressionSyntax)
                {
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.FluentDynamicConfigRule,
                        Args = new object[] { "assignment (=)" },
                        Location = stmt.GetLocation(),
                    });
                    return false;
                }
                return true;
            default:
                return true;
        }
    }

    /// <summary>控制流语句种类名（F08 消息参数——语言中立 token）</summary>
    private static string DescribeStatementKind(StatementSyntax stmt) => stmt switch
    {
        IfStatementSyntax => "if",
        ForStatementSyntax => "for",
        ForEachStatementSyntax => "foreach",
        WhileStatementSyntax => "while",
        SwitchStatementSyntax => "switch",
        TryStatementSyntax => "try",
        _ => stmt.Kind().ToString(),
    };

    /// <summary>
    /// 解析单条链式调用语句 → 0..1 个 FluentSnapshot（链结构错误按 / 静默跳过整链）
    /// </summary>
    private static void ExtractChainStatement(
        InvocationExpressionSyntax chainRoot,
        SemanticModel sm,
        List<FluentSnapshot> snapshots,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        // 反向展开 → 反转得语义顺序 [For, Map, Ignore, ...]
        var ordered = ChainCallParser.ReverseToSemanticOrder(ChainCallParser.UnwrapChain(chainRoot));

        // 链首必须 For
        if (ordered.Count == 0 || ordered[0].MethodName != "For") return;

        // 难点⑤：For 泛型类型参数解析
        var (sourceType, targetType) = ParseForTypeArgs(ordered[0].Invocation, sm, ct);
        if (sourceType == null || targetType == null) return;

        var fs = new FluentSnapshot(sourceType, targetType, ordered[0].Invocation.GetLocation());

        // 字段上下文状态机（Member 三段式消费）——段锚（dest 解析产物）+
        // 段内容标志随第一遍推进；段边界 = 下一个 Member（case 内先收口旧段）/ 链尾（循环后收口），
        // 裸段（无任何跟随声明/配置）F68 提醒。MemberHead 不继承对级（编译期强制：裸段仅可接
        // MemberHead 签名族——旧形态对级方法不可达），段内旧形态继承调用仅在头声明后可达
        //（内容已置位——计数仅为稳健位）。memberAnchors 缓存段锚解析产物——第二遍（转换器）
        // 按节点复取，免重复解析与重复诊断；MapWhen 段锚经 PendingWhenInvocation.MemberDest
        // 直携（第三遍消费）。三遍循环结构与"链序无关"设计不动。
        var memberAnchors = new Dictionary<InvocationExpressionSyntax, LambdaParseResult?>();
        LambdaParseResult? memberDest = null;
        InvocationExpressionSyntax? memberInvocation = null;
        var memberHasContent = false;
        // F69 去重面（链内同属性只报一次——变换四类共享同一"省略来源"事实）
        var f69Reported = new HashSet<string>(StringComparer.Ordinal);

        // （v1.4 修订）：链解析两遍循环——第一遍收集路径/对级声明
        //（Map/Ignore/Mask/MapTree/FromJson/NoAutoInclude/AfterMapping），第二遍处理
        // 转换器声明（ConvertBack/Convert—— 由 ConvertUsing 更名）。
        // 动机：Convert 步骤 4 源侧解析需感知同链
        // 先行声明的 Map src（特性侧 [MapProperty].FromField 优先语义的 Fluent 等价物——
        // 两遍保证链序无关：.Convert(...).Map(...) 与 .Map(...).Convert(...) 同结果）。
        // ConvertBack 实体侧解析不读 Declarations（按名独立），挪第二遍零行为变化（诊断顺序不变）。
        // Skip(1) 枚举器分配 → 索引循环（两遍循环为设计所需，
        // 免分配不涉行为；下方第二遍同款）
        for (var i = 1; i < ordered.Count; i++)
        {
            var node = ordered[i];
            ct.ThrowIfCancellationRequested();
            switch (node.MethodName)
            {
                case "Member":
                {
                    // 字段段开段——上一段先收口（裸段 F68），再解析本段 dest 锚。
                    // dest 门镜像 AddPathDeclaration；
                    // G3（注释校准）：**全无效锚形态**（空段 / 多段 / 单段带索引）
                    // 段内后续操作一律静默跳过——判定单点 = HasValidSegmentAnchor，写入侧同口径
                    // （无效锚声明不写入）；"段开已诊断不重复报"只约束诊断发射。
                    if (memberInvocation != null && memberDest != null && !memberHasContent)
                    {
                        diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.BareMemberNoFollowUpRule,
                            Args = new object[] { DescribePath(memberDest) },
                            Location = memberInvocation.GetLocation(),
                        });
                    }
                    var memberParsed = ParseSingleLambdaArg(node.Invocation, sm, diagnostics, ct);
                    if (memberParsed != null && memberParsed.IsValidPath && memberParsed.Segments.Count > 1)
                    {
                        diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.MultiSegmentDestNotSupportedRule,
                            Args = new object[] { DescribePath(memberParsed) },
                            Location = node.Invocation.GetLocation(),
                        });
                    }
                    else if (memberParsed != null && memberParsed.IsValidPath
                             && memberParsed.Segments.Any(s => s.IndexAfter != null))
                    {
                        diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                            Args = new object[] { DescribePath(memberParsed) },
                            Location = node.Invocation.GetLocation(),
                        });
                    }
                    memberAnchors[node.Invocation] = memberParsed;
                    memberDest = memberParsed;
                    memberInvocation = node.Invocation;
                    memberHasContent = false;
                    break;
                }
                case "From":
                {
                    // 头声明来源槽（新形态唯一来源位——From 仅存在于 MemberHead；
                    // 无段锚 = 编译外形态， / 静默）
                    if (memberDest == null) break;
                    memberHasContent = true;
                    ProcessMemberFrom(fs, node, memberDest, sm, diagnostics, ct);
                    break;
                }
                case "Map":
                    // 旧对级 Map(dest, src) 形态：公共面无入口（FluentMappingConfig / FluentTypeConfig /
                    // MemberHead / MemberConfig 均无 Map 重载）——不可编译形态由编译器报错，生成器不叠加诊断
                    break;
                case "Ignore":
                {
                    // 新形态 .Ignore() / .Ignore(Direction)——忽略出口，段锚 dest（AfterIgnore 编译期语义）
                    //（旧 args[0] lambda 形态公共面无入口，编译器已报，不进入本分派）
                    var igArgs = node.Invocation.ArgumentList.Arguments;
                    if (memberDest != null)
                    {
                        memberHasContent = true;
                        var igDirection = igArgs.Count >= 1
                            ? ParseDirectionArg(node.Invocation, sm, 0, ct, diagnostics)
                            : null;
                        WriteFluentDeclaration(fs, memberDest, FluentDeclarationKind.Ignore,
                            null, null, null, igDirection, false,
                            node.Invocation.GetLocation(), diagnostics);
                    }
                    break;
                }
                case "MapTree":
                {
                    var mtArgs = node.Invocation.ArgumentList.Arguments;
                    if (memberDest != null && mtArgs.Count == 0)
                    {
                        // 新形态：变换槽树递归（来源可省 = 同名默认 → F69 检查）
                        memberHasContent = true;
                        CheckMemberOmittedSource(fs, fs.SourceType, memberDest,
                            node.Invocation.GetLocation(), diagnostics, f69Reported);
                        WriteFluentDeclaration(fs, memberDest, FluentDeclarationKind.MapTree,
                            null, null, null, null, false,
                            node.Invocation.GetLocation(), diagnostics);
                    }
                    break;
                }
                case "Mask":
                {
                    // 新形态 .Mask<TMask>(overrideIgnore)——首参非 lambda（公共面唯一重载位为 bool）
                    if (memberDest != null)
                    {
                        memberHasContent = true;
                        ProcessMemberMask(fs, node, memberDest, sm, diagnostics, ct, f69Reported);
                    }
                    break;
                }
                case "Format":
                {
                    // 新形态 .Format(format, overrideIgnore)——首参为 string（公共面唯一重载位）
                    //（旧 lambda 首参形态公共面无入口；F64/F65 仍在物化期发射，口径不变）
                    if (memberDest != null)
                    {
                        memberHasContent = true;
                        ProcessMemberFormat(fs, node, memberDest, sm, diagnostics, ct, f69Reported);
                    }
                    break;
                }
                case "FromJson":
                {
                    var fjArgs = node.Invocation.ArgumentList.Arguments;
                    if (memberDest != null && fjArgs.Count == 0)
                    {
                        // 新形态：变换槽 JSON 展开（来源可省 → F69 检查）
                        memberHasContent = true;
                        CheckMemberOmittedSource(fs, fs.SourceType, memberDest,
                            node.Invocation.GetLocation(), diagnostics, f69Reported);
                        WriteFluentDeclaration(fs, memberDest, FluentDeclarationKind.FromJson,
                            null, null, null, null, false,
                            node.Invocation.GetLocation(), diagnostics);
                    }
                    break;
                }
                case "NoAutoInclude":
                    if (memberDest != null) memberHasContent = true; // 段内=字段版；无段=对级 legacy（ProcessChainNoAutoInclude 乙分派）
                    ProcessChainNoAutoInclude(fs, node, sm, diagnostics, ct, memberDest);
                    break;
                case "NoAutoMatch":
                    // 对级改名承接（原 NoAutoInclude() 对级"禁自动匹配"位——fs.NoAutoInclude 置位）
                    fs.NoAutoInclude = true;
                    break;
                case "Tree":
                    ProcessChainTree(fs, node, sm, diagnostics, ct);
                    break;
                case "AfterMapping":
                    fs.AfterMappingMethodRef = ResolveAfterMapping(node.Invocation, sm, diagnostics, ct);
                    break;
                case "BeforeMapping":
                    // 前置生命周期钩子声明——镜像 AfterMapping 提取（第一遍
                    // 对级声明族；F35/F36 在此发射，F37 构造器一体化形态归 发射层判定——发射形态
                    // 复核 + 双侧同报只有生成层可达双通道）
                    fs.BeforeMappingMethodRef = ResolveBeforeMapping(node.Invocation, sm, diagnostics, ct);
                    break;
                case "MapSubclass":
                    ProcessChainMapSubclass(fs, node, sm, diagnostics, ct);
                    break;
                case "MapCtorParam":
                    // 显式构造器形参绑定声明
                    // 形态 1（paramName + 源 lambda）/ 形态 2（paramName + 源属性名字符串）双形态统一
                    // 登记 CtorParamBindings；提取期单点解析源属性符号。形态 1 非纯路径沿用
                    // LambdaParser 既有诊断机制（B12——不新占码位）；paramName 非常量串/空静默跳过
                    ExtractCtorParamBinding(fs, node.Invocation, sm, diagnostics, ct);
                    break;
                case "MapWhen":
                {
                    // 属性级条件守卫声明
                    // 第三遍延迟解析（PendingWhenInvocations）：谓词形态判定需感知先行 Map src
                    // （块内 Map 的 ComposeDeclarations 同理），链序无关（两遍保证同款动机）。
                    // 新形态（混合载谓词单参）：段锚经 MemberDest 直携（第三遍 F71 类型参门）。
                    var mwArgs = node.Invocation.ArgumentList.Arguments;
                    if (mwArgs.Count == 1 && memberDest != null)
                    {
                        memberHasContent = true;
                        fs.PendingWhenInvocations.Add(new PendingWhenInvocation(
                            node.Invocation, fs.SourceType, isBlock: false, blockSubSource: null, memberDest: memberDest));
                    }
                    else
                    {
                        if (memberDest != null) memberHasContent = true;
                        fs.PendingWhenInvocations.Add(new PendingWhenInvocation(
                            node.Invocation, fs.SourceType, isBlock: false, blockSubSource: null));
                    }
                    break;
                }
                case "FromSource":
                {
                    // 并存期形态分派：单 lambda + 绑定于 MemberHead → 新形态头速记（段锚 dest）；
                    // 其余（双 lambda 旧速记 / 单 lambda 旧块形态）走原分派。绑定判定走乙路线（符号级）——
                    // 块形态 Action lambda 与速记 Func lambda 均可单参出现，纯语法形态不可分；
                    // MemberHead.FromSource 仅段头可达（配置段后无速记），符号分流为零歧义判定。
                    var fsArgs = node.Invocation.ArgumentList.Arguments;
                    if (fsArgs.Count == 1 && fsArgs[0].Expression is LambdaExpressionSyntax
                        && IsBoundOnMemberHead(node.Invocation, sm, ct))
                    {
                        memberHasContent = true;
                        ExtractFromSourceShorthand(fs, node.Invocation, sm, diagnostics, ct, memberDest);
                    }
                    else
                    {
                        if (memberDest != null) memberHasContent = true;
                        ProcessChainFromSource(fs, node, sm, diagnostics, ct);
                    }
                    break;
                }
                case "ConvertBack":
                case "Convert":
                    // 转换器声明（静态方法组族）——第二遍处理（见下；case 空过防 default 拦截）。
                    // 段内出现即内容计数（裸段仅可达新形态——MemberHead 无旧形态转换器签名）
                    if (memberDest != null) memberHasContent = true;
                    break;
                default:
                    // 未知链方法名（含 object 继承成员/无关扩展方法
                    // 语法可编译但非 Fluent 终端）原静默丢整链 → F60 显式化；丢弃语义本身不变
                    //（行为扩展评估出口登记 R-5）
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.UnknownChainTerminalRule,
                        Args = new object[] { node.MethodName! },
                        Location = node.Invocation.GetLocation(),
                    });
                    return;
            }
        }

        // 链尾段收口——最后一个 Member 段裸段 F68（段边界=下一 Member 已在 case 内收口）
        if (memberInvocation != null && memberDest != null && !memberHasContent)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.BareMemberNoFollowUpRule,
                Args = new object[] { DescribePath(memberDest) },
                Location = memberInvocation.GetLocation(),
            });
        }

        // 第二遍：转换器声明（ConvertBack/Convert——可感知第一遍的 Map src 声明）
        // 同款索引循环（免 Skip 枚举器分配）
        // 新形态转换器（converter 实参在 args[0]、无 dest）段锚复取 memberAnchors
        //（第一遍解析产物缓存——免重复解析/重复诊断；旧形态 dest 内携不受影响）
        LambdaParseResult? convertMemberDest = null;
        for (var i = 1; i < ordered.Count; i++)
        {
            var node = ordered[i];
            ct.ThrowIfCancellationRequested();
            switch (node.MethodName)
            {
                case "Member":
                    convertMemberDest = memberAnchors.TryGetValue(node.Invocation, out var convAnchor) ? convAnchor : null;
                    break;
                case "ConvertBack":
                    ProcessChainConvertBack(fs, node, sm, diagnostics, ct, convertMemberDest);
                    break;
                case "Convert":
                    ProcessChainConvert(fs, node, sm, diagnostics, ct, convertMemberDest);
                    break;
            }
        }

        // 第三遍：MapWhen 条件守卫声明——延迟解析动机：
        // ① 谓词入参形态判定（T1 字段值/T2 整源）与源侧类型校验需感知同链先行的 Map src
        //    （块内 Map 的 ComposeDeclarations 同理）——链序无关；
        // ② 块内声明的 ComposeDeclarations 在块语句遍历中逐条累积，链尾统一解析保证全量可见。
        foreach (var pending in fs.PendingWhenInvocations)
        {
            ct.ThrowIfCancellationRequested();
            ResolveMapWhen(pending, fs, sm, diagnostics, ct);
        }

        snapshots.Add(fs);
    }


    /// <summary>
    /// 格式串常量实参解析——取指位实参的常量字符串值；
    /// 缺失/非常量（插值、变量、方法调用等）返回 null（声明静默跳过， / 口径）。
    /// 位置参数化——旧形态 dest 后第 2 位（索引 1）默认保持；
    /// 新形态 .Format(format[, oi]) 格式串前移索引 0。
    /// </summary>
    private static string? ParseFormatStringArgument(
        InvocationExpressionSyntax inv, SemanticModel sm, CancellationToken ct, int positionalIndex = 1)
    {
        ct.ThrowIfCancellationRequested();
        var args = inv.ArgumentList.Arguments;
        if (args.Count <= positionalIndex) return null;
        var constant = sm.GetConstantValue(args[positionalIndex].Expression);
        return constant.HasValue && constant.Value is string s ? s : null;
    }

    /// <summary>
    /// ExtractChainStatement case "NoAutoInclude" 体位移拆分
    /// 求值顺序与诊断发射顺序逐字保持；缩进保持 switch 内原样（行级等价证明）。
    /// </summary>
    private static void ProcessChainNoAutoInclude(
        FluentSnapshot fs,
        InvocationChainNode node,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        LambdaParseResult? memberDest = null)
    {
                    // 两形态以实参区分——无参 = 对级（既有语义零变化）；
                    // 1 lambda 实参 = 属性级（导航收集跳过标记，独立平行承载 NoAutoIncludeTargets）。
                    // 解析失败静默跳过。
                    // （三分规则）：零参分流改乙路线（符号级）——甲（语法
                    // 上下文）在"段内继承旧形态对级方法后接零参 NoAutoInclude"场景误判（该调用点
                    // 静态类型已回 FluentTypeConfig、绑定对级方法——甲会误写字段清单）；乙按绑定方法
                    // ContainingType 分流：MemberHead/MemberConfig/AfterIgnore → 字段版（写
                    // NoAutoIncludeTargets——段锚位）；FluentTypeConfig/解析失败兜底 → 旧对级置位
                    //（存量 ≥8 处行为逐字保持——错误恢复链不产生新语义）。NoAutoMatch 承接对级
                    // 置位（独立 case）；1 lambda 旧字段版路径原样。
                    if (node.Invocation.ArgumentList.Arguments.Count == 0)
                    {
                        var boundNoAutoInclude = sm.GetSymbolInfo(node.Invocation, ct).Symbol as IMethodSymbol;
                        if (boundNoAutoInclude?.ContainingType is { Name: "MemberHead" or "MemberConfig" or "AfterIgnore" })
                        {
                            if (memberDest != null && memberDest.IsValidPath && memberDest.Segments.Count == 1)
                                fs.NoAutoIncludeTargets.Add(memberDest.Segments[0].Name);
                            return; // 段锚无效——段开已诊断
                        }
                        fs.NoAutoInclude = true;
                    }
                    else
                    {
                        var naDest = ParseSingleLambdaArg(node.Invocation, sm, diagnostics, ct);
                        // .1/2（NoAutoInclude 站点）：索引段 → F06；多段 → F55
                        //（原静默跳过；本站点为分支 if 条件形态）
                        if (naDest != null && naDest.IsValidPath && naDest.Segments.Any(s => s.IndexAfter != null))
                        {
                            diagnostics.Add(new DiagnosticInfo
                            {
                                Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                                Args = new object[] { DescribePath(naDest) },
                                Location = node.Invocation.GetLocation(),
                            });
                            return;
                        }
                        if (naDest != null && naDest.IsValidPath && naDest.Segments.Count > 1)
                        {
                            diagnostics.Add(new DiagnosticInfo
                            {
                                Descriptor = DiagnosticDescriptorsFluent.MultiSegmentDestNotSupportedRule,
                                Args = new object[] { DescribePath(naDest) },
                                Location = node.Invocation.GetLocation(),
                            });
                            return;
                        }
                        if (naDest != null && naDest.IsValidPath && naDest.Segments.Count == 1
                            && naDest.Segments[0].Property != null)
                        {
                            fs.NoAutoIncludeTargets.Add(naDest.Segments[0].Property!.Name);
                        }
                    }
    }

    /// <summary>
    /// ExtractChainStatement case "Tree" 体位移拆分
    /// 求值顺序与诊断发射顺序逐字保持；缩进保持 switch 内原样（行级等价证明）。
    /// </summary>
    private static void ProcessChainTree(
        FluentSnapshot fs,
        InvocationChainNode node,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
                    // 树构建配置三元组——0 参默认（与 [MapTree]
                    // 无参/RuntimeTreeConfig init 三处一致）/ 3 参 string 常量自定义；非常量或实参
                    // 形态不符静默跳过。
                    // 重复调用后写覆盖（对级配置惯例）。
                    {
                        var treeArgs = node.Invocation.ArgumentList.Arguments;
                        if (treeArgs.Count == 0)
                        {
                            fs.TreeConfig = ("Id", "ParentId", "Children");
                        }
                        else if (treeArgs.Count == 3)
                        {
                            var idVal = sm.GetConstantValue(treeArgs[0].Expression, ct);
                            var parentVal = sm.GetConstantValue(treeArgs[1].Expression, ct);
                            var childrenVal = sm.GetConstantValue(treeArgs[2].Expression, ct);
                            if (idVal.HasValue && idVal.Value is string idName
                                && parentVal.HasValue && parentVal.Value is string parentName
                                && childrenVal.HasValue && childrenVal.Value is string childrenName)
                            {
                                fs.TreeConfig = (idName, parentName, childrenName);
                            }
                        }
                    }
    }

    /// <summary>
    /// ExtractChainStatement case "MapSubclass" 体位移拆分
    /// 求值顺序与诊断发射顺序逐字保持；缩进保持 switch 内原样（行级等价证明）。
    /// </summary>
    private static void ProcessChainMapSubclass(
        FluentSnapshot fs,
        InvocationChainNode node,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
                    // 多态子类型对登记。
                    // 泛型实参符号解析（GetGenericArguments 双参基座 = ParseForTypeArgs 同内核）；
                    // 继承约束由泛型 where TSrcDerived : TSource / where TTargetDerived : TTarget
                    // 调用点编译期拦截（CS0311/CS0314——生成器不可达非法实参， 定案 F46 不挂
                    // Fluent）；实参形态异常（非泛型调用等）编译器已报，静默跳过。
                    {
                        var subArgs = GetGenericArguments(node.Invocation, out var subHasGeneric);
                        if (subHasGeneric && subArgs.Count == 2)
                        {
                            var subSrc = sm.GetSymbolInfo(subArgs[0], ct).Symbol as INamedTypeSymbol;
                            var subTgt = sm.GetSymbolInfo(subArgs[1], ct).Symbol as INamedTypeSymbol;
                            if (subSrc != null && subTgt != null)
                                fs.SubclassDeclarations.Add(new FluentSubclassDeclaration(
                                    subSrc, subTgt, node.Invocation.GetLocation()));
                        }
                    }
    }

    /// <summary>
    /// ExtractChainStatement case "FromSource" 体位移拆分
    /// 求值顺序与诊断发射顺序逐字保持；缩进保持 switch 内原样（行级等价证明）。
    /// </summary>
    private static void ProcessChainFromSource(
        FluentSnapshot fs,
        InvocationChainNode node,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
                    // /：FromSource 补充源绑定（原名 Compose，
                    // 更名对齐特性侧 [MapProperty(FromSource)]）
                    // 按实参数分派两形态：2 lambda → 速记；1 Action lambda → 分组块下钻
                    var args = node.Invocation.ArgumentList.Arguments;
                    if (args.Count == 2 && args[0].Expression is LambdaExpressionSyntax
                        && args[SourceArgIndex].Expression is LambdaExpressionSyntax)
                    {
                        ExtractFromSourceShorthand(fs, node.Invocation, sm, diagnostics, ct);
                    }
                    else if (args.Count == 1 && args[0].Expression is LambdaExpressionSyntax)
                    {
                        ExtractFromSourceBlock(fs, node.Invocation, sm, diagnostics, ct);
                    }
                    // 实参形态异常（非 lambda 等）→ / 静默跳过（编译器已报实参类型错误）
    }

    /// <summary>
    /// ExtractChainStatement case "ConvertBack" 体位移拆分
    /// 求值顺序与诊断发射顺序逐字保持；缩进保持 switch 内原样（行级等价证明）。
    /// </summary>
    private static void ProcessChainConvertBack(
        FluentSnapshot fs,
        InvocationChainNode node,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        LambdaParseResult? memberDest = null)
    {
                    // 反向自定义转换声明——dest 单段门同 AddPathDeclaration
                    //（多段/字段段/超限沿用 F02/F04~F07）；方法组/形态/实体侧解析单点
                    //（ResolveConvertBackMethod 七步， v1.3 提取期单点版）。
                    // G2（②）：并存期形态分派残留清扫
                    // 旧形态 (destλ, converter[, oi]) 的 25 签名已随 （b28b3ac）物理删除，
                    // 旧分支对可编译代码的唯一可达路径 = 布尔常量探测误判（非恒定 overrideIgnore 实参
                    // 被吞 → 转换声明静默丢失）；分支退役 + IsBoolConstantArg 删除，单形态直入新解。
                    var cbArgs = node.Invocation.ArgumentList.Arguments;
                    if (memberDest != null && cbArgs.Count is 1 or 2)
                    {
                        // 新形态：段锚 dest（第二遍经 memberAnchors 复取）+ converter 前移 args[0]
                        var backDecl = ResolveConvertBackMethod(node.Invocation, memberDest, fs, sm, diagnostics, ct,
                            overrideIgnore: ParseOverrideIgnoreArg(node.Invocation, sm, 1, diagnostics),
                            memberConverterExpr: cbArgs[0].Expression);
                        if (memberDest.IsValidPath && memberDest.Segments.Count == 1 && backDecl != null)
                            fs.BackDeclarations[memberDest.Segments[0].Name] = backDecl;
                    }
                    // 其余实参形态（memberDest == null 或 count 越界）——编译器已报， / 静默
                    //（解析失败兜底链保留：ResolveConvertBackMethod 七步内部诊断口径不变）
    }


    /// <summary>
    /// ExtractChainStatement case "Convert" 体位移拆分
    /// 求值顺序与诊断发射顺序逐字保持；缩进保持 switch 内原样（行级等价证明）。
    /// </summary>
    private static void ProcessChainConvert(
        FluentSnapshot fs,
        InvocationChainNode node,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        LambdaParseResult? memberDest = null)
    {
                    // （更名 ConvertUsing→Convert）：
                    // 正向自定义转换声明——dest 单段门同 ConvertBack（多段/字段段/超限沿用 F02/F04~F07）；
                    // 方法组/形态/源侧解析单点（ResolveConvertMethod 七步， 提取期单点版—— 反向七步换向复用）。
                    // G2（②）：分派残留清扫——同
                    // ProcessChainConvertBack（旧形态无编译来源 + 布尔常量探测误判 = 声明静默丢失根因）。
                    var cvArgs = node.Invocation.ArgumentList.Arguments;
                    if (memberDest != null && cvArgs.Count is 1 or 2)
                    {
                        // 新形态：段锚 dest + converter 前移 args[0]（豁免门位 args[1]）
                        var fwdDecl = ResolveConvertMethod(node.Invocation, memberDest, fs, sm, diagnostics, ct,
                            memberConverterExpr: cvArgs[0].Expression,
                            memberOverrideIgnore: ParseOverrideIgnoreArg(node.Invocation, sm, 1, diagnostics));
                        if (memberDest.IsValidPath && memberDest.Segments.Count == 1 && fwdDecl != null)
                            fs.ForwardDeclarations[memberDest.Segments[0].Name] = fwdDecl;
                    }
                    // 其余实参形态（memberDest == null 或 count 越界）——编译器已报， / 静默
                    //（解析失败兜底链保留：ResolveConvertMethod 七步内部诊断口径不变）
    }

    /// <summary>
    /// 追加带路径声明（Map/Ignore/MapTree/FromJson 共用）——dest 须解析为单段有效路径；
    /// Map 需 src 有效路径（同型/跨类型均支持），Ignore/MapTree/FromJson 无 src（物化时按名自动匹配）。
    /// 声明按目标属性名入表（同属性重复声明后写覆盖——builder 惯例）。
    /// <para>：direction/overrideIgnore 可选参（方向性忽略 +
    /// Q2 豁免门——默认值语义与既有调用逐字节一致）。</para>
    /// </summary>
    /// <summary>
    /// 解析结果 → 诊断显示名（段链 + 段后索引，如 <c>Address.City</c> / <c>Items[0]</c>）。
    /// F06（"indexer access '{0}'"）与 F55（"multi-segment path '{0}'"）共用。
    /// </summary>
    private static string DescribePath(LambdaParseResult path)
    {
        return string.Join(
            ".",
            path.Segments.Select(s => s.IndexAfter is { } index ? $"{s.Name}[{index}]" : s.Name));
    }

    /// <summary>
    /// T-003槽位合并写入核心——声明按维度分槽合入 Declarations：
    /// 来源槽（Map/From——<paramref name="srcSegments"/>）、变换槽（Mask/Format/FromJson/MapTree
    /// ——MaskType/FormatString/FromJsonDeclared/MapTreeDeclared）、忽略出口（Ignore——direction）。
    /// 合并纪律：
    /// <list type="bullet">
    /// <item>Kind = 本次写入种类（末写承载——MappingSnapshotBuilder 单分派各分支只读本槽字段，
    /// 与既有 last-wins 产物逐字一致；003-P2-1"真冲突场景诊断叠加、产物逻辑不动"）</item>
    /// <item>他槽字段跨写存续（From+Mask 等合法组合的承载面—— 组合构建消费）</item>
    /// <item>OverrideIgnore 取 OR（豁免门为属性级语义——任一声明豁免即属性豁免）</item>
    /// <item>Location = 本次写入位置（诊断定位随末写——last-wins 同口径）</item>
    /// </list>
    /// 真冲突检测（F59， 清单①-④：Ignore×映射操作 / 脱敏|格式化×树递归 /
    /// 脱敏×JSON / 格式化×JSON）与同维度重复（F70，⑤：同槽/同 Kind 重复——今日静默
    /// last-wins 处，预置"跨段同槽重复必报错"）写入前发射；产物行为不变（错误照写——
    /// 与现状 last-wins 同构）。既有门 F23/F17/F66（Convert/ConvertBack×Ignore/Format）
    /// 在组装层不动（清单⑥）。
    /// </summary>
    /// <param name="dest">dest 锚（单段有效——调用方已过 F55/F06 门；无效静默跳过不重复报）</param>
    /// <param name="srcSegments">来源槽写入值（Map/From only；其余 null = 保留既有槽）</param>
    private static void WriteFluentDeclaration(
        FluentSnapshot fs,
        LambdaParseResult dest,
        FluentDeclarationKind kind,
        List<PropertyPathSegment>? srcSegments,
        INamedTypeSymbol? maskType,
        string? formatString,
        int? direction,
        bool overrideIgnore,
        Microsoft.CodeAnalysis.Location location,
        List<DiagnosticInfo> diagnostics)
    {
        if (dest == null || !HasValidSegmentAnchor(dest)) return; // G1 段锚无效（空段/多段/索引段）——段开已诊断；索引段补判 = 写入侧缺口收口
        if (kind == FluentDeclarationKind.Map && (srcSegments == null || srcSegments.Count == 0)) return; // Map 须 src（旧 AddPathDeclaration 同门）
        var name = dest.Segments[0].Name;
        fs.Declarations.TryGetValue(name, out var existing);

        if (existing != null)
        {
            // F70（⑤ 同维度重复）：同槽重复写入——同段被编译期结构强制（来源唯一/类型分层），
            // 可达形态为跨段与旧面重复（预置判定：跨段同槽重复必报错）。产物行为不变（照写 last-wins）
            var sameDimensionDuplicate = kind switch
            {
                FluentDeclarationKind.Map => existing.SrcSegments != null,
                FluentDeclarationKind.Ignore => existing.Kind == FluentDeclarationKind.Ignore,
                FluentDeclarationKind.Mask => existing.MaskType != null,
                FluentDeclarationKind.Format => existing.FormatString != null,
                FluentDeclarationKind.FromJson => existing.FromJsonDeclared,
                FluentDeclarationKind.MapTree => existing.MapTreeDeclared,
                _ => false,
            };
            if (sameDimensionDuplicate)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.SameDimensionDuplicateRule,
                    Args = new object[] { name, DescribeSlotDimension(kind), existing.Kind.ToString(), kind.ToString() },
                    Location = location,
                });
            }

            // F59 真冲突（清单①-④；组装层既有门 F23/F17/F66 不在此重复）
            var trueConflict =
                // ① Ignore × 映射分支一切操作（结构规则——Ignore 独唱）
                (kind == FluentDeclarationKind.Ignore
                 && (existing.SrcSegments != null || existing.MaskType != null || existing.FormatString != null
                     || existing.FromJsonDeclared || existing.MapTreeDeclared))
                || (existing.Kind == FluentDeclarationKind.Ignore && kind != FluentDeclarationKind.Ignore)
                // ② 脱敏/格式化 × 树递归
                || (kind == FluentDeclarationKind.MapTree
                    && (existing.MaskType != null || existing.FormatString != null))
                || ((kind == FluentDeclarationKind.Mask || kind == FluentDeclarationKind.Format)
                    && existing.MapTreeDeclared)
                // ③ 脱敏 × JSON 展开
                || (kind == FluentDeclarationKind.FromJson && existing.MaskType != null)
                || (kind == FluentDeclarationKind.Mask && existing.FromJsonDeclared)
                // ④ 格式化 × JSON 展开
                || (kind == FluentDeclarationKind.FromJson && existing.FormatString != null)
                || (kind == FluentDeclarationKind.Format && existing.FromJsonDeclared);
            if (trueConflict)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.SameAnchorCrossKindOverrideRule,
                    Args = new object[] { name, existing.Kind.ToString(), kind.ToString() },
                    Location = location,
                });
            }
        }

        fs.Declarations[name] = new FluentPropertyDeclaration(
            kind,
            kind == FluentDeclarationKind.Map ? srcSegments : existing?.SrcSegments,
            kind == FluentDeclarationKind.Mask ? maskType : existing?.MaskType,
            location,
            direction: kind == FluentDeclarationKind.Ignore ? direction : existing?.Direction,
            overrideIgnore: (existing?.OverrideIgnore ?? false) || overrideIgnore,
            formatString: kind == FluentDeclarationKind.Format ? formatString : existing?.FormatString,
            fromJsonDeclared: kind == FluentDeclarationKind.FromJson || (existing?.FromJsonDeclared ?? false),
            mapTreeDeclared: kind == FluentDeclarationKind.MapTree || (existing?.MapTreeDeclared ?? false));
    }

    /// <summary>F70 消息 {1} 槽位维度名（语言中立 token——DescribeStatementKind 先例同口径）</summary>
    private static string DescribeSlotDimension(FluentDeclarationKind kind) => kind switch
    {
        FluentDeclarationKind.Map => "source",
        FluentDeclarationKind.Ignore => "ignore",
        _ => "transform",
    };

    /// <summary>
    /// **段锚有效性**单点判定
    /// <c>IsValidPath ∧ Segments.Count == 1 ∧ 无索引段（IndexAfter == null）</c>。
    /// <para>贯通三处消费端（诊断发射与有效性早退**解耦**——"段开已诊断不重复报"只约束诊断
    /// 发射、不豁免早退）：① F69 两守卫首部（<see cref="CheckMemberOmittedSource"/> /
    /// <see cref="CheckBlockOmittedSource"/>——空段 <c>Segments[0]</c> 直索引崩溃面 + 多段/索引段
    /// F55/F06×F69 双报同源消除）；② 主链写入门（<see cref="WriteFluentDeclaration"/>）；
    /// ③ 块内写入门（<see cref="RegisterBlockAnchoredDeclaration"/>）——无效锚声明不写入
    /// （含单段常量索引，F06 拒绝语义贯穿写入侧， 甲案）。</para>
    /// <para>字段段（<c>Segments[0].Property == null</c>）不在本判定内（既有静默跳过口径维持——
    /// 备案 X1）。</para>
    /// </summary>
    private static bool HasValidSegmentAnchor(LambdaParseResult? dest)
        => dest is { IsValidPath: true, Segments.Count: 1 } && dest.Segments[0].IndexAfter == null;

    /// <summary>
    /// F69（省略来源无同名）：新形态变换写入（Mask/Format/FromJson/MapTree——来源可省 =
    /// 同名默认）时来源槽空且源类型无同名可读成员 → 报错（旧面该场景声明静默消失——auto-match
    /// 不命中即作废；新面显式化防静默失效）。查找口径镜像消费端 BuildMaskMapping/
    /// BuildFormatMapping 等（FindProperty 同名直查——变换分支不涉扁平化）；同链同属性去重。
    /// 豁免面：Ignore（忽略无须源）/ NoAutoInclude（导航收集跳过与源无关）/ MapWhen（锚点落空
    /// 归物化期 F43——双报面分工， 登记）；来源槽已声明（段内 From / 跨段存续）不查。
    /// </summary>
    private static void CheckMemberOmittedSource(
        FluentSnapshot fs,
        INamedTypeSymbol sourceType,
        LambdaParseResult memberDest,
        Microsoft.CodeAnalysis.Location location,
        List<DiagnosticInfo> diagnostics,
        HashSet<string> reported)
    {
        if (!HasValidSegmentAnchor(memberDest)) return; // G1 段锚无效（空段/多段/索引段）——段开已诊断，不重复报
        if (memberDest.Segments[0].Property == null) return; // 字段段
        var name = memberDest.Segments[0].Name;
        if (fs.Declarations.TryGetValue(name, out var declared) && declared.SrcSegments is { Count: > 0 }) return; // 来源槽已声明
        if (MappingExtractor.FindProperty(sourceType, name)?.GetMethod != null) return; // 同名可达——消费端同口径
        if (!reported.Add(name)) return; // 同链同属性去重
        diagnostics.Add(new DiagnosticInfo
        {
            Descriptor = DiagnosticDescriptorsFluent.OmittedSourceNoSameNameRule,
            Args = new object[] { name, sourceType.ToDisplayString() },
            Location = location,
        });
    }

    /// <summary>
    /// F69 块内变体：省略来源的块内变换写入——显式绑定已在（同 (子源, dest) 的路径承载型
    /// ComposeDeclaration——段内 From / 跨段存续）不查；子源无同名可读成员（FindProperty——
    /// 组装层组内按名解析同口径，:370）→ 报错（防声明静默消失）。同块同属性去重。
    /// </summary>
    private static void CheckBlockOmittedSource(
        FluentSnapshot fs,
        INamedTypeSymbol subSource,
        LambdaParseResult blockMemberDest,
        Microsoft.CodeAnalysis.Location location,
        List<DiagnosticInfo> diagnostics,
        HashSet<string> reported)
    {
        if (!HasValidSegmentAnchor(blockMemberDest)) return; // G1 段锚无效（空段/多段/索引段）——段开已诊断，不重复报
        if (blockMemberDest.Segments[0].Property == null) return; // 字段段
        var name = blockMemberDest.Segments[0].Name;
        var hasBinding = fs.ComposeDeclarations.Any(cd =>
            SymbolEqualityComparer.Default.Equals(cd.SubSourceType, subSource)
            && string.Equals(cd.TargetPropertyName, name, StringComparison.Ordinal)
            && cd.SourcePath is { Count: > 0 });
        if (hasBinding) return; // 显式绑定已声明
        if (MappingExtractor.FindProperty(subSource, name)?.GetMethod != null) return; // 子源同名可达
        if (!reported.Add(name)) return; // 同块同属性去重
        diagnostics.Add(new DiagnosticInfo
        {
            Descriptor = DiagnosticDescriptorsFluent.OmittedSourceNoSameNameRule,
            Args = new object[] { name, subSource.ToDisplayString() },
            Location = location,
        });
    }

    /// <summary>
    /// 乙路线（符号级）：invocation 绑定方法包含类型是否为 MemberHead——FromSource 新旧
    /// 形态分流（块 Action lambda 与速记 Func lambda 单参形态语法不可分；MemberHead.FromSource
    /// 仅段头可达——N8 编译期拦截，符号分流为零歧义判定）。
    /// </summary>
    private static bool IsBoundOnMemberHead(InvocationExpressionSyntax inv, SemanticModel sm, CancellationToken ct)
    {
        return sm.GetSymbolInfo(inv, ct).Symbol is IMethodSymbol { ContainingType.Name: "MemberHead" };
    }

    /// <summary>
    /// 新形态头声明来源槽写入——.From&lt;TSrcProp&gt;(src[, overrideIgnore])（旧 Map(dest, src)
    /// 签名平移：dest 出 Member 段锚；src 支持嵌套路径与索引段——G7 Map-src 承载面原样）。
    /// src 无效静默跳过（LambdaParser 已报/ / ——旧 AddPathDeclaration 同门）；
    /// 豁免门位置实参位 args[1]。
    /// </summary>
    private static void ProcessMemberFrom(
        FluentSnapshot fs,
        InvocationChainNode node,
        LambdaParseResult memberDest,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var args = node.Invocation.ArgumentList.Arguments;
        if (args.Count < 1) return; // 实参不足——编译器已报
        var src = ParseLambdaArg(args[0].Expression, sm, diagnostics, ct);
        if (src == null || !src.IsValidPath) return;
        var fromOverrideIgnore = ParseOverrideIgnoreArg(node.Invocation, sm, 1, diagnostics);
        WriteFluentDeclaration(fs, memberDest, FluentDeclarationKind.Map, src.Segments.ToList(),
            null, null, null, fromOverrideIgnore, node.Invocation.GetLocation(), diagnostics);
    }

    /// <summary>
    /// 新形态变换槽脱敏写入——.Mask&lt;TMask&gt;([overrideIgnore])（TProp 由 Member 推断省参；
    /// ParseMaskTypeArgument 取首泛型实参与旧面同位）；F69 省略来源检查 + 槽位合并。
    /// </summary>
    private static void ProcessMemberMask(
        FluentSnapshot fs,
        InvocationChainNode node,
        LambdaParseResult memberDest,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        HashSet<string> f69Reported)
    {
        var maskType = ParseMaskTypeArgument(node.Invocation, sm, ct);
        if (maskType == null) return; // TMask 不可解析——编译器已报（旧 ProcessChainMask 同门）
        var maskOverrideIgnore = ParseOverrideIgnoreArg(node.Invocation, sm, 0, diagnostics);
        CheckMemberOmittedSource(fs, fs.SourceType, memberDest, node.Invocation.GetLocation(), diagnostics, f69Reported);
        WriteFluentDeclaration(fs, memberDest, FluentDeclarationKind.Mask, null, maskType, null,
            null, maskOverrideIgnore, node.Invocation.GetLocation(), diagnostics);
    }

    /// <summary>
    /// 新形态变换槽格式化写入——.Format(format[, overrideIgnore])（格式串前移 args[0]）；
    /// F64/F65 物化期发射照旧、F66（Convert×Format）组装门不动；F69 + 槽位合并。
    /// </summary>
    private static void ProcessMemberFormat(
        FluentSnapshot fs,
        InvocationChainNode node,
        LambdaParseResult memberDest,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        HashSet<string> f69Reported)
    {
        var formatString = ParseFormatStringArgument(node.Invocation, sm, ct, 0);
        if (formatString == null) return; // 格式串缺失/非常量——编译器已报
        var formatOverrideIgnore = ParseOverrideIgnoreArg(node.Invocation, sm, 1, diagnostics);
        CheckMemberOmittedSource(fs, fs.SourceType, memberDest, node.Invocation.GetLocation(), diagnostics, f69Reported);
        WriteFluentDeclaration(fs, memberDest, FluentDeclarationKind.Format, null, null, formatString,
            null, formatOverrideIgnore, node.Invocation.GetLocation(), diagnostics);
    }

    /// <summary>
    /// FromSource 速记形态提取（原名 ExtractComposeShorthand，20260905- 更名；
    /// a）
    /// 1 参泛型 TSubSource 显式解析 + 双 lambda object 占位（属性实际类型经 LambdaParser 既有
    /// SemanticModel 符号解析恢复——段符号即真实类型，值类型装箱为 bound 层转换、语法层无节点，
    /// 禁"剥 UnaryExpression"字面，`_004` I-2）→ ComposeFluentDeclaration（Shorthand）。
    /// dest 须单段属性路径（锚点，物化 TargetProperty）；src 支持嵌套纯属性路径（物化 SourcePath 归一）。
    /// 解析失败静默跳过。
    /// </summary>
    private static void ExtractFromSourceShorthand(
        FluentSnapshot fs,
        InvocationExpressionSyntax inv,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        LambdaParseResult? memberDest = null)
    {
        var subSource = ParseFromSourceTypeArg(inv, sm, ct);
        if (subSource == null) return;

        // 新形态头速记（.Member(x).FromSource<TSub>(src)）——dest 出
        // Member 段锚（已验，F55/F06 门免重复）、src 前移 args[0]；旧形态（dest, src 双 lambda）
        // 原样 ParseTwoLambdaArgs。object 占位恢复机制两形态共用（段符号即真实类型）
        LambdaParseResult? dest;
        LambdaParseResult? src;
        if (memberDest != null)
        {
            dest = memberDest;
            var shArgs = inv.ArgumentList.Arguments;
            src = shArgs.Count >= 1
                ? ParseLambdaArg(shArgs[0].Expression, sm, diagnostics, ct)
                : null;
        }
        else
        {
            (dest, src) = ParseTwoLambdaArgs(inv, sm, diagnostics, ct);
        }
        // 站点清单②（FromSource 速记）：dest 多段 → F55（原静默丢弃）；
        // 单段带索引 → F06（铺点同款，原静默收窄）。
        // 连带修正（通道）：新形态（memberDest 段锚）本站点不重复发射
        // F55/F06——本方法注释「dest 出 Member 段锚（已验，F55/F06 门免重复）」既定口径，消除双报。
        if (memberDest == null && dest != null && dest.IsValidPath && dest.Segments.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.MultiSegmentDestNotSupportedRule,
                Args = new object[] { DescribePath(dest) },
                Location = inv.GetLocation(),
            });
            return;
        }
        if (memberDest == null && dest != null && dest.IsValidPath && dest.Segments.Any(s => s.IndexAfter != null))
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                Args = new object[] { DescribePath(dest) },
                Location = inv.GetLocation(),
            });
            return;
        }
        if (dest == null || !dest.IsValidPath || dest.Segments.Count != 1) return;
        var destProp = dest.Segments[0].Property;
        if (destProp == null) return; // 字段段——锚点模型仅承载 IPropertySymbol（同 AddPathDeclaration 口径）
        if (src == null || !src.IsValidPath || src.Segments.Count == 0) return;

        // 源路径逐段符号归一（单段=单元素列表；多段=逐段符号；字段段物化期按 B4 边界，提取期仅属性段）
        var sourcePath = src.Segments.Where(s => s.Property != null).Select(s => s.Property!).ToList();
        if (sourcePath.Count == 0) return;

        fs.ComposeDeclarations.Add(new ComposeFluentDeclaration(
            subSource,
            destProp.Name,
            destProp,
            sourcePath,
            inv.GetLocation(),
            ComposeDeclarationForm.Shorthand));
    }

    /// <summary>
    /// FromSource 分组块下钻提取（原名 ExtractComposeBlock，20260905- 更名；
    /// b）
    /// 块上下文入栈（{SubSourceType, 块参数符号, 块位置}）→ 遍历 configure lambda 体的
    /// ExpressionStatement 逐语句：语句表达式 = Invocation → ChainCallParser 反向展开 →
    /// 语句链首须为块参数标识符（同名解析到块参数符号，防局部变量冒充，Q8）→ 后续方法名分派
    /// 复用第一遍/第二遍同一套 case（Map/Ignore/Mask/MapTree/FromJson 第一遍；Convert 第二遍；
    /// ConvertBack/NoAutoInclude/AfterMapping → F30/F31/F32 块内禁止诊断）→ 块上下文出栈。
    /// 块内嵌套 FromSource（c.FromSource）→ flatten 语义（等价主链独立 FromSource 声明，递归下钻，Q7）。
    /// </summary>
    private static void ExtractFromSourceBlock(
        FluentSnapshot fs,
        InvocationExpressionSyntax inv,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var subSource = ParseFromSourceTypeArg(inv, sm, ct);
        if (subSource == null) return;

        var configure = inv.ArgumentList.Arguments[0].Expression as LambdaExpressionSyntax;
        // 仅接受块体 lambda（c => { ... }）——Action<FluentTypeConfig<TSubSource,TTarget>> 在 C# 中
        // 只能由块体 lambda 实现：FluentTypeConfig 各终端方法均返回 this（非 void），表达式体
        // （c => c.Map(...)）绑定为 Func 而非 Action，编译器已报 CS1061/CS1503。方法组引用
        // （c => SomeMethod） 已知边界：编译器可绑定但无语句体可下钻 → / 静默。
        if (configure == null || configure.Body is not BlockSyntax block) return;
        // 块参数符号锚定（lambda 参数 Identifier——防局部变量冒充，Q8 默认规则）
        string? blockParamName = null;
        if (configure is SimpleLambdaExpressionSyntax sl) blockParamName = sl.Parameter.Identifier.Text;
        else if (configure is ParenthesizedLambdaExpressionSyntax pl && pl.ParameterList.Parameters.Count == 1)
            blockParamName = pl.ParameterList.Parameters[0].Identifier.Text;

        var blockLocation = inv.GetLocation();
        // 块内嵌套 FromSource flatten 语义：嵌套块独立下钻，声明携带各自 SubSourceType
        foreach (var stmt in block.Statements)
        {
            ct.ThrowIfCancellationRequested();
            if (stmt is not ExpressionStatementSyntax ess
                || ess.Expression is not InvocationExpressionSyntax stmtInv)
                continue;
            ProcessComposeBlockStatement(fs, stmtInv, subSource, blockParamName, blockLocation, sm, diagnostics, ct);
        }
    }

    /// <summary>
    /// 块内单锚点声明注册辅助（重复实现收敛于此：
    /// ProcessComposeBlockStatement 块内 Ignore/MapTree/Mask/FromJson 四分支，差异仅 BlockKind 与
    /// MaskType；Mask 分支的 maskType 空门与 dest 单段门同为静默跳过口径，合入同一判定无诊断差异）。
    /// 主链对应 case 不收敛（分类：体量不贡献风险 + 与块内形态异构度高—— 批内闭合）。
    /// </summary>
    /// <param name="fs">解析期单链快照（ComposeDeclarations 注册目标）</param>
    /// <param name="inv">块内语句 invocation 节点（Location 承载）</param>
    /// <param name="dest">dest lambda 解析结果（单段属性路径锚点——无效/字段段静默丢弃）</param>
    /// <param name="subSource">块级子源类型（FromSource&lt;TSubSource&gt; 泛型实参）</param>
    /// <param name="kind">块内方法种类（Ignore/MapTree/Mask/FromJson）</param>
    /// <param name="diagnostics">提取期诊断累积表（多段/索引门 F55/F06 发射目标）</param>
    /// <param name="maskType">脱敏实现类型符号（Mask only；其余恒 null）</param>
    /// <param name="destDiagnosedAtMemberSite">true = dest 出自块内 Member 段锚（新形态）——F55/F06 已在
    /// 块内 Member 站点发射，此处不再重复（连带修正， 通道：
    /// 与 注释「dest 出段锚已验、F55/F06 门免重复」既定口径对齐，消除双报）</param>
    private static void RegisterBlockAnchoredDeclaration(
        FluentSnapshot fs,
        InvocationExpressionSyntax inv,
        LambdaParseResult? dest,
        INamedTypeSymbol subSource,
        FluentDeclarationKind kind,
        List<DiagnosticInfo> diagnostics,
        INamedTypeSymbol? maskType = null,
        string? formatString = null,
        bool destDiagnosedAtMemberSite = false)
    {
        // 站点清单②（FromSource 块内锚点声明）：dest 多段 → F55（原静默）；
        // 单段带索引 → F06（铺点同款）。
        // G1（③ 写入侧）：诊断发射与有效性早退**解耦**
        // destDiagnosedAtMemberSite 只豁免"重复报告"、不豁免 return（原结构 destDiagnosed=true 时
        // 连早退一起跳过 = 无效锚（单段常量索引）F06 已报而声明照写的写入侧缺口根因）；
        // 末段统一走 HasValidSegmentAnchor 单点判定（与主链写入门同口径）。
        if (dest != null && dest.IsValidPath && dest.Segments.Count > 1)
        {
            if (!destDiagnosedAtMemberSite)
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.MultiSegmentDestNotSupportedRule,
                    Args = new object[] { DescribePath(dest) },
                    Location = inv.GetLocation(),
                });
            return;
        }
        if (dest != null && dest.IsValidPath && dest.Segments.Any(s => s.IndexAfter != null))
        {
            if (!destDiagnosedAtMemberSite)
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                    Args = new object[] { DescribePath(dest) },
                    Location = inv.GetLocation(),
                });
            return;
        }
        if (dest == null || !HasValidSegmentAnchor(dest)) return; // G1 单点判定（空段/多段/索引段一律不写入；null 防御口径原样保留）
        if (kind == FluentDeclarationKind.Mask && maskType == null) return; // MaskType 门（与单段门同静默口径）
        var destProp = dest.Segments[0].Property;
        if (destProp == null) return; // 字段段——静默跳过（锚点模型仅承载 IPropertySymbol）
        fs.ComposeDeclarations.Add(new ComposeFluentDeclaration(
            subSource, destProp.Name, destProp, null, inv.GetLocation(),
            ComposeDeclarationForm.Block, kind, maskType, formatString: formatString));
    }

    /// <summary>
    /// 处理分组块内单条语句（链式调用）
    /// ChainCallParser 反向展开 → 链首须为块参数标识符（防局部变量冒充，Q8）→ 方法名分派复用主链 case。
    /// </summary>
    private static void ProcessComposeBlockStatement(
        FluentSnapshot fs,
        InvocationExpressionSyntax stmtInv,
        INamedTypeSymbol subSource,
        string? blockParamName,
        Microsoft.CodeAnalysis.Location blockLocation,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        // 反向展开语句链 → 语义顺序 [最内层(.Map), ...最外层(.Ignore)]；块内语句接收者为块参数（无链首 For）
        var ordered = ChainCallParser.ReverseToSemanticOrder(ChainCallParser.UnwrapChain(stmtInv));
        if (ordered.Count == 0) return;

        // 语句链首（ordered[0]，最内层 invocation）的 receiver 须为块参数标识符——
        // 同名解析到块参数符号（防局部变量冒充，Q8 默认规则）
        if (ordered[0].Invocation.Expression is not MemberAccessExpressionSyntax headMae
            || headMae.Expression is not IdentifierNameSyntax rootId
            || blockParamName == null
            || rootId.Identifier.Text != blockParamName)
            return; // 链首非块参数（局部变量冒充 / 方法组引用）→ 静默

        // 块内字段段状态机（块内字段语法随主链换代——同一套 Member
        // 消费复用）。段不可跨语句（语句链首须为块参数标识符；Member 段头后段内方法返回
        // MemberConfig——跨语句续段 = 段外 FluentTypeConfig 上调段内签名，编译期拦截），状态为
        // 方法局部；段边界 = 下一 Member / 语句链尾（裸段 F68 同主链）。
        LambdaParseResult? blockMemberDest = null;
        InvocationExpressionSyntax? blockMemberInvocation = null;
        var blockHasContent = false;
        var blockF69Reported = new HashSet<string>(StringComparer.Ordinal);

        // 块内方法分派：复用主链两遍 case 语义（Map/Ignore/Mask/MapTree/FromJson + Convert + F30-F32 禁止）；
        // Member/From/NoAutoMatch 新 case + 新旧单参形态分派（新形态段锚 dest / 旧形态 dest 内携）
        foreach (var node in ordered)
        {
            ct.ThrowIfCancellationRequested();
            switch (node.MethodName)
            {
                case "Member":
                {
                    // 块内字段段开段——上一段先收口（裸段 F68），dest 门同主链 Member case
                    if (blockMemberInvocation != null && blockMemberDest != null && !blockHasContent)
                    {
                        diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.BareMemberNoFollowUpRule,
                            Args = new object[] { DescribePath(blockMemberDest) },
                            Location = blockMemberInvocation.GetLocation(),
                        });
                    }
                    var blockParsed = ParseSingleLambdaArg(node.Invocation, sm, diagnostics, ct);
                    if (blockParsed != null && blockParsed.IsValidPath && blockParsed.Segments.Count > 1)
                    {
                        diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.MultiSegmentDestNotSupportedRule,
                            Args = new object[] { DescribePath(blockParsed) },
                            Location = node.Invocation.GetLocation(),
                        });
                    }
                    else if (blockParsed != null && blockParsed.IsValidPath
                             && blockParsed.Segments.Any(s => s.IndexAfter != null))
                    {
                        diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                            Args = new object[] { DescribePath(blockParsed) },
                            Location = node.Invocation.GetLocation(),
                        });
                    }
                    blockMemberDest = blockParsed;
                    blockMemberInvocation = node.Invocation;
                    blockHasContent = false;
                    break;
                }
                case "From":
                {
                    // 块内头声明来源槽——dest 出段锚，src 前移 args[0]（镜像 ProcessBlockMap
                    // 产物形态：ComposeDeclaration(Block, Map, srcPath)——组装层组内合并消费）
                    if (blockMemberDest == null) break;
                    blockHasContent = true;
                    var bFromArgs = node.Invocation.ArgumentList.Arguments;
                    if (bFromArgs.Count < 1) break;
                    if (!blockMemberDest.IsValidPath || blockMemberDest.Segments.Count != 1) break;
                    var bFromDestProp = blockMemberDest.Segments[0].Property;
                    if (bFromDestProp == null) break; // 字段段
                    var bFromSrc = ParseLambdaArg(bFromArgs[0].Expression, sm, diagnostics, ct);
                    if (bFromSrc == null || !bFromSrc.IsValidPath) break;
                    var bFromPath = bFromSrc.Segments.Where(s => s.Property != null).Select(s => s.Property!).ToList();
                    if (bFromPath.Count == 0) break;
                    fs.ComposeDeclarations.Add(new ComposeFluentDeclaration(
                        subSource, bFromDestProp.Name, bFromDestProp, bFromPath,
                        node.Invocation.GetLocation(), ComposeDeclarationForm.Block, FluentDeclarationKind.Map));
                    break;
                }
                case "Map":
                    // 旧块内 Map(dest, src) 形态：块配置类型 = FluentTypeConfig<TSubSource, TTarget>，
                    // 该类无 Map 重载（公共面无入口）——编译器已报，生成器不叠加诊断
                    break;
                case "Ignore":
                {
                    // 四分支收敛至 RegisterBlockAnchoredDeclaration。
                    // 并存期形态分派：args[0] lambda → 旧形态；否则新形态 .Ignore()/.Ignore(Direction)
                    //（Direction 实参不承载——ComposeFluentDeclaration 无该字段，新旧形态块内一致，
                    // 登记）
                    var bIgArgs = node.Invocation.ArgumentList.Arguments;
                    if (bIgArgs.Count >= 1 && bIgArgs[0].Expression is LambdaExpressionSyntax)
                    {
                        if (blockMemberDest != null) blockHasContent = true;
                        var dest = ParseSingleLambdaArg(node.Invocation, sm, diagnostics, ct);
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, dest, subSource, FluentDeclarationKind.Ignore, diagnostics);
                    }
                    else if (blockMemberDest != null)
                    {
                        blockHasContent = true;
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, blockMemberDest, subSource, FluentDeclarationKind.Ignore, diagnostics, destDiagnosedAtMemberSite: true);
                    }
                    break;
                }
                case "MapTree":
                {
                    var bMtArgs = node.Invocation.ArgumentList.Arguments;
                    if (bMtArgs.Count >= 1 && bMtArgs[0].Expression is LambdaExpressionSyntax)
                    {
                        if (blockMemberDest != null) blockHasContent = true;
                        var dest = ParseSingleLambdaArg(node.Invocation, sm, diagnostics, ct);
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, dest, subSource, FluentDeclarationKind.MapTree, diagnostics);
                    }
                    else if (blockMemberDest != null && bMtArgs.Count == 0)
                    {
                        // 新形态：变换槽树递归（省略来源 → 块内 F69）
                        blockHasContent = true;
                        CheckBlockOmittedSource(fs, subSource, blockMemberDest,
                            node.Invocation.GetLocation(), diagnostics, blockF69Reported);
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, blockMemberDest, subSource, FluentDeclarationKind.MapTree, diagnostics, destDiagnosedAtMemberSite: true);
                    }
                    break;
                }
                case "Mask":
                {
                    var bMkArgs = node.Invocation.ArgumentList.Arguments;
                    if (bMkArgs.Count >= 1 && bMkArgs[0].Expression is LambdaExpressionSyntax)
                    {
                        if (blockMemberDest != null) blockHasContent = true;
                        var dest = ParseSingleLambdaArg(node.Invocation, sm, diagnostics, ct);
                        var maskType = ParseMaskTypeArgument(node.Invocation, sm, ct);
                        // MaskType 符号态持有（D-1 就绪后物化消费；D-1 未就绪则物化降级不消费）
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, dest, subSource, FluentDeclarationKind.Mask, diagnostics, maskType);
                    }
                    else if (blockMemberDest != null)
                    {
                        // 新形态：变换槽脱敏（省略来源 → 块内 F69）；豁免门实参不承载
                        //（ComposeFluentDeclaration 无该字段——新旧形态块内一致， 登记）
                        blockHasContent = true;
                        var bMaskType = ParseMaskTypeArgument(node.Invocation, sm, ct);
                        CheckBlockOmittedSource(fs, subSource, blockMemberDest,
                            node.Invocation.GetLocation(), diagnostics, blockF69Reported);
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, blockMemberDest, subSource, FluentDeclarationKind.Mask, diagnostics, bMaskType, destDiagnosedAtMemberSite: true);
                    }
                    break;
                }
                case "Format":
                {
                    // 块内格式化声明（镜像块内 Mask 站点形态
                    // 格式串常量解析 + RegisterBlockAnchoredDeclaration 单点注册）。
                    // 并存期形态分派：args[0] lambda → 旧形态；否则新形态（格式串前移 args[0]）
                    var bFmArgs = node.Invocation.ArgumentList.Arguments;
                    if (bFmArgs.Count >= 1 && bFmArgs[0].Expression is LambdaExpressionSyntax)
                    {
                        if (blockMemberDest != null) blockHasContent = true;
                        var dest = ParseSingleLambdaArg(node.Invocation, sm, diagnostics, ct);
                        var formatString = ParseFormatStringArgument(node.Invocation, sm, ct);
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, dest, subSource, FluentDeclarationKind.Format, diagnostics, formatString: formatString);
                    }
                    else if (blockMemberDest != null)
                    {
                        blockHasContent = true;
                        var bFormatString = ParseFormatStringArgument(node.Invocation, sm, ct, 0);
                        CheckBlockOmittedSource(fs, subSource, blockMemberDest,
                            node.Invocation.GetLocation(), diagnostics, blockF69Reported);
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, blockMemberDest, subSource, FluentDeclarationKind.Format, diagnostics, formatString: bFormatString, destDiagnosedAtMemberSite: true);
                    }
                    break;
                }
                case "FromJson":
                {
                    var bFjArgs = node.Invocation.ArgumentList.Arguments;
                    if (bFjArgs.Count >= 1 && bFjArgs[0].Expression is LambdaExpressionSyntax)
                    {
                        if (blockMemberDest != null) blockHasContent = true;
                        var dest = ParseSingleLambdaArg(node.Invocation, sm, diagnostics, ct);
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, dest, subSource, FluentDeclarationKind.FromJson, diagnostics);
                    }
                    else if (blockMemberDest != null && bFjArgs.Count == 0)
                    {
                        // 新形态：变换槽 JSON 展开（省略来源 → 块内 F69）
                        blockHasContent = true;
                        CheckBlockOmittedSource(fs, subSource, blockMemberDest,
                            node.Invocation.GetLocation(), diagnostics, blockF69Reported);
                        RegisterBlockAnchoredDeclaration(fs, node.Invocation, blockMemberDest, subSource, FluentDeclarationKind.FromJson, diagnostics, destDiagnosedAtMemberSite: true);
                    }
                    break;
                }
                case "Convert":
                {
                    // G2（②）：块内分派残留清扫——同主链
                    // ProcessChainConvert（旧形态 (destλ, converter) 无编译来源；ProcessBlockConvert
                    // 旧形态支路随退删除，解析失败兜底链不变）
                    var bcArgs = node.Invocation.ArgumentList.Arguments;
                    if (blockMemberDest != null && bcArgs.Count is 1 or 2)
                    {
                        // 新形态：段锚 dest + converter 前移 args[0]（块内七步内核换向复用）
                        blockHasContent = true;
                        var fwdDecl = ResolveConvertMethodForBlock(node.Invocation, blockMemberDest, fs, subSource, sm, diagnostics, ct,
                            memberConverterExpr: bcArgs[0].Expression,
                            memberOverrideIgnore: ParseOverrideIgnoreArg(node.Invocation, sm, 1, diagnostics));
                        if (blockMemberDest.IsValidPath && blockMemberDest.Segments.Count == 1 && fwdDecl != null)
                        {
                            var destProp = blockMemberDest.Segments[0].Property!;
                            fs.ComposeDeclarations.Add(new ComposeFluentDeclaration(
                                subSource, destProp.Name, destProp, null, node.Invocation.GetLocation(),
                                ComposeDeclarationForm.Block, blockKind: null,
                                converter: new ComposeConverterRef(fwdDecl)));
                        }
                    }
                    break;
                }
                case "ConvertBack":
                    AddBlockForbiddenDiagnostic(node, subSource, diagnostics,
                        DiagnosticDescriptorsFluent.ComposeBlockConvertBackForbiddenRule);
                    break;
                case "NoAutoInclude":
                    AddBlockForbiddenDiagnostic(node, subSource, diagnostics,
                        DiagnosticDescriptorsFluent.ComposeBlockNoAutoIncludeForbiddenRule);
                    break;
                case "NoAutoMatch":
                    // 对级改名承接面——NoAutoMatch 为原对级 NoAutoInclude() 更名（禁自动匹配），
                    // 块内属 F31 禁止族同族（原对级 NoAutoInclude 块内既禁；块内置位将误写主对
                    // fs.NoAutoInclude——作用域错置， 登记）
                    AddBlockForbiddenDiagnostic(node, subSource, diagnostics,
                        DiagnosticDescriptorsFluent.ComposeBlockNoAutoIncludeForbiddenRule);
                    break;
                case "AfterMapping":
                    AddBlockForbiddenDiagnostic(node, subSource, diagnostics,
                        DiagnosticDescriptorsFluent.ComposeBlockAfterMappingForbiddenRule);
                    break;
                case "BeforeMapping":
                    // 块内前置钩子禁止（F38，镜像 F32）——运行时 Compose 管线
                    // 无钩子注入口，声明永不执行；Error 防用户误以为生效（fail-fast）
                    AddBlockForbiddenDiagnostic(node, subSource, diagnostics,
                        DiagnosticDescriptorsFluent.ComposeBlockBeforeMappingForbiddenRule);
                    break;
                case "MapWhen":
                {
                    // 块内 MapWhen 允许——锚定子源属性（正向属性谓词
                    // 有落点，跨源字段缺值高发场景）；发射站点 = Update_{Sub}_To_{Target} 逐属性赋值层。
                    // 延迟解析（第三遍）——块内 Map src 的 ComposeDeclarations 累积后统一可见。
                    // 新形态（谓词单参）：段锚经 MemberDest 直携（第三遍 F71 类型参门）
                    var bMwArgs = node.Invocation.ArgumentList.Arguments;
                    if (bMwArgs.Count == 1 && blockMemberDest != null)
                    {
                        blockHasContent = true;
                        fs.PendingWhenInvocations.Add(new PendingWhenInvocation(
                            node.Invocation, subSource, isBlock: true, blockSubSource: subSource, memberDest: blockMemberDest));
                    }
                    else
                    {
                        if (blockMemberDest != null) blockHasContent = true;
                        fs.PendingWhenInvocations.Add(new PendingWhenInvocation(
                            node.Invocation, subSource, isBlock: true, blockSubSource: subSource));
                    }
                    break;
                }
                case "FromSource":
                {
                    // 并存期形态分派：单 lambda + 绑定于 MemberHead → 新形态头速记（段锚 dest，
                    // 主链 ExtractFromSourceShorthand 复用——ComposeDeclaration(Shorthand) 同产物）；
                    // 其余走原分派（嵌套速记/嵌套块——Q7 flatten 语义）
                    var bFsArgs = node.Invocation.ArgumentList.Arguments;
                    if (bFsArgs.Count == 1 && bFsArgs[0].Expression is LambdaExpressionSyntax
                        && IsBoundOnMemberHead(node.Invocation, sm, ct))
                    {
                        blockHasContent = true;
                        ExtractFromSourceShorthand(fs, node.Invocation, sm, diagnostics, ct, blockMemberDest);
                    }
                    else
                    {
                        if (blockMemberDest != null) blockHasContent = true;
                        ProcessBlockFromSource(fs, node, subSource, sm, diagnostics, ct);
                    }
                    break;
                }
                default:
                    // 块内未知方法（object 继承成员等可编译形态）
                    // 原静默跳过 → F60（块内仅跳过该语句节点——与主链"丢整链"语义差异见描述）
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.UnknownChainTerminalRule,
                        Args = new object[] { node.MethodName! },
                        Location = node.Invocation.GetLocation(),
                    });
                    break;
            }
        }

        // 语句尾段收口——裸 Member 段 F68（块内段边界 = 语句链尾）
        if (blockMemberInvocation != null && blockMemberDest != null && !blockHasContent)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.BareMemberNoFollowUpRule,
                Args = new object[] { DescribePath(blockMemberDest) },
                Location = blockMemberInvocation.GetLocation(),
            });
        }
    }

    /// <summary>
    /// ProcessComposeBlockStatement case "FromSource" 体位移拆分
    /// 求值顺序与诊断发射顺序逐字保持；缩进保持 switch 内原样（行级等价证明）。
    /// </summary>
    private static void ProcessBlockFromSource(
        FluentSnapshot fs,
        InvocationChainNode node,
        INamedTypeSymbol subSource,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
                    // 块内嵌套 FromSource：flatten 语义——等价主链独立 FromSource 声明，递归下钻。
                    // 嵌套块继承外层块位置锚定；速记/块两形态各自递归
                    var nestedArgs = node.Invocation.ArgumentList.Arguments;
                    if (nestedArgs.Count == 2 && nestedArgs[0].Expression is LambdaExpressionSyntax
                        && nestedArgs[1].Expression is LambdaExpressionSyntax)
                    {
                        ExtractFromSourceShorthand(fs, node.Invocation, sm, diagnostics, ct);
                    }
                    else if (nestedArgs.Count == 1 && nestedArgs[0].Expression is LambdaExpressionSyntax)
                    {
                        ExtractFromSourceBlock(fs, node.Invocation, sm, diagnostics, ct);
                    }
    }

    /// <summary>
    /// 块内四禁止族诊断收敛——ConvertBack/NoAutoInclude/
    /// AfterMapping/BeforeMapping 同构 diagnostics.Add 模板参数化（描述符入参；
    /// 逐 case 单发——求值与发射顺序不变）。
    /// </summary>
    private static void AddBlockForbiddenDiagnostic(
        InvocationChainNode node,
        INamedTypeSymbol subSource,
        List<DiagnosticInfo> diagnostics,
        DiagnosticDescriptor rule)
    {
        diagnostics.Add(new DiagnosticInfo
        {
            Descriptor = rule,
            Args = new object[] { subSource.ToDisplayString() },
            Location = node.Invocation.GetLocation(),
        });
    }

    /// <summary>
    /// 块内 Convert 解析（ResolveConvertMethod 上下文参数化
    /// 形态判定锚点类级 TSource → 块级 TSubSource；源侧解析基准换子源类型）。
    /// </summary>
    private static FluentForwardDeclaration? ResolveConvertMethodForBlock(
        InvocationExpressionSyntax inv,
        LambdaParseResult? dest,
        FluentSnapshot fs,
        INamedTypeSymbol subSource,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        ExpressionSyntax? memberConverterExpr = null,
        bool memberOverrideIgnore = false)
    {
        // 复用主链七步内核，源基准换子源类型：以 subSource 替代 fs.SourceType 走同名解析/形态匹配。
        // 新形态参数直通（memberConverterExpr/memberOverrideIgnore——块内外同核）
        var blockFs = new FluentSnapshot(subSource, fs.TargetType, fs.ForLocation);
        // 块内 Map 先行声明（两遍保证链序无关）——块内声明携带在 ComposeDeclarations，
        // ResolveConvertMethod 读 fs.Declarations 的 src 优先语义对块内不直接适用，
        // 块内 Convert 按名独立解析（子源侧 FindProperty/扁平化），与主链 T1 按名口径对齐
        return ResolveConvertMethod(inv, dest, blockFs, sm, diagnostics, ct,
            memberConverterExpr, memberOverrideIgnore);
    }

    /// <summary>
    /// MapCtorParam 绑定声明提取
    /// args[0] = 形参名（常量字符串）、args[1] = 源 lambda（形态 1）或源属性名字符串（形态 2）。
    /// 形态 1 经 LambdaParser 解析单段纯属性（TSource 锚定——按 lambda 参数符号解析，与 Map src
    /// 同内核）；非纯路径/字段段沿用既有诊断或静默丢弃（Map src 同口径，B12 不新占码位）。
    /// 形态 2 提取期单点解析：拼错/无 getter → F51（CtorParamBindingSourceNotFoundRule）。
    /// </summary>
    private static void ExtractCtorParamBinding(
        FluentSnapshot fs, InvocationExpressionSyntax inv, SemanticModel sm,
        List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count < 2) return; // 实参不足——编译器已报

        var paramNameVal = sm.GetConstantValue(args[0].Expression, ct);
        if (!paramNameVal.HasValue || paramNameVal.Value is not string paramName || paramName.Length == 0)
            return; // 形参名非常量/空——静默跳过

        IPropertySymbol? srcProp;
        if (args[SourceArgIndex].Expression is LambdaExpressionSyntax lambda)
        {
            // 形态 1：源 lambda（TSource 锚定单段纯属性路径）
            var parsed = ParseLambdaArg(lambda, sm, diagnostics, ct);
            // 站点清单①（MapCtorParam）：多段 → F55（原静默丢弃
            // FluentCtorParamTests Row3b 契约随本批转正）；单段带索引 → F06（原静默收窄）；
            // 字段段 → F55（:1197 门——F55 复用为"锚点声明被跳过"口径）
            if (parsed != null && parsed.IsValidPath && parsed.Segments.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.MultiSegmentDestNotSupportedRule,
                    Args = new object[] { DescribePath(parsed) },
                    Location = inv.GetLocation(),
                });
                return;
            }
            if (parsed == null || !parsed.IsValidPath || parsed.Segments.Count != 1)
                return; // 非纯路径：静默丢弃（Map src 同口径）
            if (parsed.Segments.Any(s => s.IndexAfter != null))
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                    Args = new object[] { DescribePath(parsed) },
                    Location = inv.GetLocation(),
                });
                return;
            }
            var segProp = parsed.Segments[0].Property;
            if (segProp == null)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.MultiSegmentDestNotSupportedRule,
                    Args = new object[] { DescribePath(parsed) },
                    Location = inv.GetLocation(),
                });
                return;
            }
            srcProp = segProp;
        }
        else
        {
            // 形态 2：源属性名字符串——提取期单点解析，拼错/无公共 getter → F51
            var nameVal = sm.GetConstantValue(args[SourceArgIndex].Expression, ct);
            if (!nameVal.HasValue || nameVal.Value is not string srcName || srcName.Length == 0)
                return; // 非常量串——编译器已报类型错误
            srcProp = MappingExtractor.FindProperty(fs.SourceType, srcName);
            if (srcProp == null || srcProp.GetMethod == null)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.CtorParamBindingSourceNotFoundRule,
                    Args = new object[] { paramName, srcName, fs.SourceType.ToDisplayString() },
                    Location = inv.GetLocation(),
                });
                return;
            }
        }

        fs.CtorParamBindings.Add(new FluentCtorParamBinding(paramName, srcProp, inv.GetLocation()));
    }

    /// <summary>
    /// 取 invocation 的泛型实参列表（For&lt;S,T&gt; 2 参 / FromSource&lt;TSub&gt; 1 参通用——
    /// 由 ParseForTypeArgs 2 参基座按 1 参通用化，N6）
    /// </summary>
    private static System.Collections.Generic.List<TypeSyntax> GetGenericArguments(
        InvocationExpressionSyntax inv, out bool hasGenericName)
    {
        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax mae when mae.Name is GenericNameSyntax maeGns:
                hasGenericName = true;
                return maeGns.TypeArgumentList.Arguments.ToList();
            case GenericNameSyntax gns:
                hasGenericName = true;
                return gns.TypeArgumentList.Arguments.ToList();
            default:
                hasGenericName = false;
                return new System.Collections.Generic.List<TypeSyntax>();
        }
    }

    /// <summary>
    /// Lifecycle（Before/AfterMapping）方法组引用解析共享内核（
    /// 原 2 处重复收敛于此：<see cref="ResolveAfterMapping"/>/<see cref="ResolveBeforeMapping"/>
    /// 同构方法体；F14↔F36/F12↔F35 诊断码差异经参数化承载）——
    /// B-1b 可见性契约 + S-1b 方法组边界：lambda/闭包/捕获变量/局部函数 → NotMethodGroup（F14/F36）；
    /// private 等跨类不可访问 → Inaccessible（F12/F35）。
    /// </summary>
    /// <param name="inv">BeforeMapping/AfterMapping invocation 节点</param>
    /// <param name="sm">语义模型</param>
    /// <param name="diagnostics">诊断收集</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="notMethodGroupRule">F14（After）/ F36（Before）</param>
    /// <param name="inaccessibleRule">F12（After）/ F35（Before）</param>
    /// <returns>方法引用 "{ContainingType.ToDisplayString()}.{Name}"；解析失败 null（诊断已发）</returns>
    private static string? ResolveLifecycleMethodGroup(
        InvocationExpressionSyntax inv, SemanticModel sm, List<DiagnosticInfo> diagnostics, CancellationToken ct,
        DiagnosticDescriptor notMethodGroupRule,
        DiagnosticDescriptor inaccessibleRule)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count != 1) return null; // 实参数错误——编译器已报（Action 单参签名）

        var expr = args[0].Expression;
        if (expr is LambdaExpressionSyntax)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = notMethodGroupRule,
                Location = expr.GetLocation(),
            });
            return null;
        }

        var symInfo = sm.GetSymbolInfo(expr, ct);
        if (symInfo.Symbol is not IMethodSymbol ms
            || ms.MethodKind == MethodKind.LambdaMethod
            || ms.MethodKind == MethodKind.LocalFunction)
        {
            // lambda/闭包/捕获变量/局部函数——无静态可寻址入口点
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = notMethodGroupRule,
                Location = expr.GetLocation(),
            });
            return null;
        }

        if (ms.ContainingType == null) return null;

        // 可见性契约：生成代码位于 internal static class GeneratedMapper（同程序集独立类型）——
        // private/protected/private protected 跨类不可访问（public/internal/protected internal 可见）。
        // 补 Accessibility.ProtectedAndInternal 判定，与 F12/F35/F40 门
        // 及特性通道白名单口径对齐——原注释称"本 Roslyn 版
        // 无该枚举值"，实为失实表述（F40 门同文件在用同一枚举值）。
        if (ms.DeclaredAccessibility is Accessibility.Private
            or Accessibility.Protected
            or Accessibility.ProtectedAndInternal)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = inaccessibleRule,
                Args = new object[] { ms.ContainingType.ToDisplayString() + "." + ms.Name },
                Location = expr.GetLocation(),
            });
            return null;
        }

        // 参数签名匹配由编译器保证（Action<TSource,TTarget> 方法组转换）
        return ms.ContainingType.ToDisplayString() + "." + ms.Name;
    }

    /// <summary>
    /// 难点④：解析 AfterMapping 方法组引用（B-1b 可见性契约 + S-1b 方法组边界）——
    /// lambda/闭包/捕获变量/局部函数 → F14；private 等跨类不可访问 → F12。
    /// <para>原方法体与 <see cref="ResolveBeforeMapping"/> 同构，
    /// 收敛至 <see cref="ResolveLifecycleMethodGroup"/> 共享内核（F14/F12 诊断码参数化）。</para>
    /// </summary>
    /// <returns>方法引用 "{ContainingType.ToDisplayString()}.{Name}"；解析失败 null（诊断已发）</returns>
    private static string? ResolveAfterMapping(
        InvocationExpressionSyntax inv, SemanticModel sm, List<DiagnosticInfo> diagnostics, CancellationToken ct)
        => ResolveLifecycleMethodGroup(inv, sm, diagnostics, ct,
            DiagnosticDescriptorsFluent.AfterMappingNotMethodGroupRule,
            DiagnosticDescriptorsFluent.AfterMappingInaccessibleRule);

    /// <summary>
    /// 解析 BeforeMapping 方法组引用（镜像 <see cref="ResolveAfterMapping"/>
    /// B-1b 可见性契约 + S-1b 方法组边界）—— lambda/闭包/捕获变量/局部函数 → F36；
    /// private 等跨类不可访问 → F35。
    /// <para>F37（构造器/一体化发射形态目标）不在本方法判定——发射形态只有生成层可达，
    /// 且双侧通道（Fluent/特性）共用一码须通道中性发射点；提取期不做先验预判（Q5 _Update 豁免
    /// 裁决要求 ref 存续进生成层）。</para>
    /// <para>原方法体与 <see cref="ResolveAfterMapping"/> 同构，
    /// 收敛至 <see cref="ResolveLifecycleMethodGroup"/> 共享内核（F36/F35 诊断码参数化）。</para>
    /// </summary>
    /// <returns>方法引用 "{ContainingType.ToDisplayString()}.{Name}"；解析失败 null（诊断已发）</returns>
    private static string? ResolveBeforeMapping(
        InvocationExpressionSyntax inv, SemanticModel sm, List<DiagnosticInfo> diagnostics, CancellationToken ct)
        => ResolveLifecycleMethodGroup(inv, sm, diagnostics, ct,
            DiagnosticDescriptorsFluent.BeforeMappingNotMethodGroupRule,
            DiagnosticDescriptorsFluent.BeforeMappingInaccessibleRule);

    /// <summary>
    /// converter 实参形态分路共享内核（重复实现收敛于此：
    /// <see cref="ResolveConvertBackMethod"/>/
    /// <see cref="ResolveConvertMethod"/> 各自的「步骤 2：converter 实参形态分路」区块）。
    /// <para>两形态（F20/F26 收窄口径原样承载）：① lambda 实参 →
    /// InlineTranscriber 转录放行（捕获/私有访问 F41、语句体/白名单外节点 F42 保留禁令仍拒）
    /// + lambda 绑定符号直取；② 方法组 → 静态门（F20/F26）+ 可见性门（F19/F25）。
    /// 诊断码差异经 <paramref name="notMethodGroupRule"/>/<paramref name="inaccessibleRule"/> 参数化；
    /// Convert 特有的方法组候选回退经 <paramref name="allowMethodGroupCandidates"/> 开关承载
    /// （ConvertBack 族泛参均在返回位/类级固定——主符号直取，无此现象）。</para>
    /// </summary>
    /// <param name="converterExpr">converter 实参表达式（args[ConverterArgIndex]——B2-26 具名位）</param>
    /// <param name="sm">语义模型</param>
    /// <param name="diagnostics">诊断收集（F41/F42/F19/F25/F20/F26 族）</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="allowMethodGroupCandidates">true = Convert（方法组候选回放，B2-01 多候选防崩）；false = ConvertBack 族（主符号直取）</param>
    /// <param name="notMethodGroupRule">F20（ConvertBack 族）/ F26（Convert）</param>
    /// <param name="inaccessibleRule">F19（ConvertBack 族）/ F25（Convert）</param>
    /// <param name="ms">产出：converter 方法符号（lambda 绑定符号或方法组）</param>
    /// <param name="transcription">产出：lambda 转录产物（方法组形态为 null）</param>
    /// <returns>分路成功 true；失败 false（诊断已发或 / 静默——调用方 return null）</returns>
    private static bool TryResolveConverterArgument(
        ExpressionSyntax converterExpr,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        bool allowMethodGroupCandidates,
        DiagnosticDescriptor notMethodGroupRule,
        DiagnosticDescriptor inaccessibleRule,
        out IMethodSymbol? ms,
        out TranscriptionResult? transcription)
    {
        ms = null;
        transcription = null;
        if (converterExpr is LambdaExpressionSyntax lambdaExpr)
        {
            transcription = InlineTranscriber.TryTranscribe(lambdaExpr, sm, diagnostics, ct);
            if (transcription == null) return false; // F41/F42 已发（精确 span）
            // lambda 绑定符号（形参类型 = 重载择优后委托签名、返回类型 = 表达式体推断）——
            // GetSymbolInfo 非 GetDeclaredSymbol（后者对作为实参的 lambda 恒 null，
            // ResolveMapWhenInline 同款实证）；绑定失败 → 编译器已报
            ms = sm.GetSymbolInfo(lambdaExpr, ct).Symbol as IMethodSymbol;
            return ms != null;
        }

        var symInfo = sm.GetSymbolInfo(converterExpr, ct);
        if (allowMethodGroupCandidates)
        {
            // 方法组候选回退（Convert 特有）：TSrcProp 位于委托入参位（Func<TSrcProp, TTargetProp>），
            // 依赖 C# 10 方法组自然类型推断——参数级绑定报 OverloadResolutionFailure 但 CandidateSymbols
            // 恰含唯一方法。
            //（ConvertBack 委托未定泛参均在返回位/类级固定——无此现象，主符号直取）
            // 多候选防崩——含错编译上生成器仍运行，候选 ≥2 时
            // SingleOrDefault 抛 InvalidOperationException（CS8785 崩溃呈现）。改 FirstOrDefault 容多候选
            // + 计数门显式拒绝（F26 + return false，该声明不生成代码；单候选行为与现状一致）。
            var convertCandidates = symInfo.CandidateSymbols.OfType<IMethodSymbol>().ToList();
            ms = symInfo.Symbol as IMethodSymbol ?? convertCandidates.FirstOrDefault();
            if (symInfo.Symbol == null && convertCandidates.Count >= 2)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = notMethodGroupRule,
                    Location = converterExpr.GetLocation(),
                });
                return false;
            }
        }
        else
        {
            ms = symInfo.Symbol as IMethodSymbol;
        }
        if (ms == null
            || ms.MethodKind == MethodKind.LambdaMethod
            || ms.MethodKind == MethodKind.LocalFunction
            || !ms.IsStatic)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = notMethodGroupRule,
                Location = converterExpr.GetLocation(),
            });
            return false;
        }

        // 可见性门（F19 镜像 F12 / F25 镜像 F19）：生成代码位于 internal static class GeneratedMapper
        //（同程序集独立类型）——private/protected/private protected 跨类不可访问
        //（protected internal 放行； 补 ProtectedAndInternal 判定，
        //  与 F12/F19/F25/F40 门口径对齐）。
        //（B 形态无用户方法符号——可见性由转录引擎 IsAccessibleFromGeneratedCode 逐符号把守）
        if (ms.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = inaccessibleRule,
                Args = new object[] { ms.ContainingType.ToDisplayString() + "." + ms.Name },
                Location = converterExpr.GetLocation(),
            });
            return false;
        }
        return true;
    }


    /// <summary>
    /// 解析 ConvertBack 声明（方法组 + 形态匹配 + 实体侧解析 + get-only 门）
    /// 七步流程（v1.3 提取期单点版，仿 <see cref="ResolveAfterMapping"/> 方法组解析先例）：
    /// <list type="number">
    /// <item>实参门：args.Count != 2 → 静默（编译器已报）</item>
    /// <item>converter 形态分路（F20 收窄）：无捕获表达式 lambda → 转录引擎
    /// 放行（F41/F42 保留禁令仍拒）；委托变量/实例方法绑定 → F20（镜像 F14）+ 可见性门 F19</item>
    /// <item>可见性门：private/protected → F19（镜像 F12）</item>
    /// <item>实体侧解析：dest 锚点属性名在 TSource FindProperty（OrdinalIgnoreCase）未命中 → F18
    /// ——提取期单点发射，产物 EntityProperty 符号态持有（物化期零重复解析）</item>
    /// <item>形态匹配：方法组签名 vs (TField=锚点属性类型, TSource=DTO 类型（TTarget 泛型参）,
    /// TTarget=实体侧同名属性类型)——复用 ResolveConverterMethod 直入重载；
    /// 无一命中 → F21（与特性侧 NCMAP037 同口径；Nullable 守卫变体由共享核心编码）</item>
    /// <item>get-only 锚点门（置于末位——错误优先）：锚点属性无 public setter → F22（Warning，
    /// H-07 写门镜像——声明将被物化遍历静默吞掉，提前告知；v1.4 消息含逃生口指引）</item>
    /// <item>产物：FluentBackDeclaration { Method, Shape, Location, EntityProperty }</item>
    /// </list>
    /// </summary>
    /// <param name="inv">ConvertBack invocation 节点</param>
    /// <param name="dest">dest lambda 解析结果（单段属性路径锚点）</param>
    /// <param name="fs">解析期单链快照（SourceType/TargetType 符号源）</param>
    /// <param name="sm">语义模型</param>
    /// <param name="diagnostics">诊断收集（F18/F19/F20/F21/F22）</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="overrideIgnore">： 豁免门（overrideIgnore 实参——物化期置位 HasOverrideIgnore）</param>
    /// <returns>反向声明（符号态）；解析失败 null（诊断已发）</returns>
    private static FluentBackDeclaration? ResolveConvertBackMethod(
        InvocationExpressionSyntax inv,
        LambdaParseResult? dest,
        FluentSnapshot fs,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        bool overrideIgnore = false,
        ExpressionSyntax? memberConverterExpr = null)
    {
        var args = inv.ArgumentList.Arguments;
        // converter 实参参数化——新形态（.Member(x).ConvertBack(...)，
        // converter 前移 args[0]、无 dest）经 memberConverterExpr 直入（实参门由 ProcessChainConvertBack
        // 形态分派把守）；旧形态两参（基础）或三参（豁免门）原样——实参数错误编译器已报
        ExpressionSyntax converterExpr;
        if (memberConverterExpr != null)
        {
            converterExpr = memberConverterExpr;
        }
        else
        {
            if (args.Count is < 2 or > 3) return null;
            converterExpr = args[ConverterArgIndex].Expression;
        }

        // dest 锚点门：须解析为单段有效属性路径（多段/字段段/超限沿用 F02/F04~F07——LambdaParser 已发）
        if (dest == null || !dest.IsValidPath || dest.Segments.Count != 1) return null;
        // 索引段承载面仅 Map-src——转换器/写回点锚点索引无语义，F06 拒绝
        if (dest.Segments.Any(s => s.IndexAfter != null))
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                Args = new object[] { inv.ToString() },
                Location = inv.GetLocation(),
            });
            return null;
        }
        var anchorProp = dest.Segments[0].Property;
        if (anchorProp == null) return null; // 字段段——静默跳过（锚点模型仅承载 IPropertySymbol）

        // 步骤 2：converter 实参形态分路（——原三段重复区块收敛至
        // TryResolveConverterArgument 共享内核；本段 F20/F19 诊断码参数化，主符号直取无候选回退）。
        // expr 出参数化 converterExpr（新形态直入 / 旧形态 args[1]——两路同核）
        var expr = converterExpr;
        if (!TryResolveConverterArgument(expr, sm, diagnostics, ct,
                allowMethodGroupCandidates: false,
                DiagnosticDescriptorsFluent.ConvertBackNotMethodGroupRule,
                DiagnosticDescriptorsFluent.ConvertBackInaccessibleRule,
                out var ms, out var transcription))
        {
            return null;
        }

        // 步骤 4：实体侧解析（提取期单点；物化期只消费 backDecl.EntityProperty）。
        // 解析序：直接同名 → 自动扁平化叶（场景 4 写回链——auto-flatten 无直接
        // 同名实体属性，F18 不得误杀；扁平化叶即反向写回点，物化经正向多段 pm 附着承载）
        // → lambda 体写回目标（r1 新增，仅前两步双未命中时启用——见 TryResolveLambdaBackTarget）
        // → F18（差异登记： 步骤 4 仅列同名解析， 场景 4 写回链
        // 依赖扁平化解析——执行期实证矛盾，按解析序镜像修正）
        var entityProp = MappingExtractor.FindProperty(fs.SourceType, anchorProp.Name);
        // 写回目标是否由 lambda 体判定得出（异名写回）——步骤 5 字段基准据此调整
        var backTargetFromLambda = false;
        if (entityProp == null)
        {
            var flatPath = MappingExtractor.TryFindFlattenedPath(fs.SourceType, anchorProp.Name);
            if (flatPath is { Count: > 1 })
            {
                entityProp = flatPath[flatPath.Count - 1]; // 扁平化叶（嵌套）——ReverseOnly 不适配（直接成员门）
            }
            else if (expr is LambdaExpressionSyntax backLambda
                     && TryResolveLambdaBackTarget(backLambda, sm, ct, out var lambdaTarget))
            {
                // 异名写回（"名字不同要显式写"）：同名/扁平化解析都落空时，反向转换器 lambda
                // 的体本身就是写回意图的显式声明——(dto) => dto.AmountText（成员访问）或
                // (dto, text) => Parse(text)（T3：第二形参由引擎按正向来源字段取值）。
                // 解析规则刻意收窄，其余形态仍落 F18，绝不猜测。
                entityProp = lambdaTarget;   // T3 形态为 null（实体字段名不可知，交由 T3 模板取值）
                backTargetFromLambda = true;
            }
            else
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.ConvertBackReverseTargetNotFoundRule,
                    // 导航文案：两侧异名时的可执行出口（lambda 形态），避免"按提示声明后仍撞 F18"。
                    // 注：源生成器运行在宿主编译的程序集身份里，ErrorMessages 的双语分支不可依赖——
                    // 文案就地给出（与 ErrorMessages.ConvertBackReverseTargetHint 同口径，勿分叉）。
                    Args = new object[] { anchorProp.Name, fs.SourceType.ToDisplayString(), anchorProp.Name,
                        anchorProp.ContainingType.ToDisplayString(),
                        $"；若 '{fs.SourceType.ToDisplayString()}' 上该属性另有名字，请改用 lambda 形态 .ConvertBack<TEntityProp>((dto) => dto.你的属性)，写回目标将由 lambda 体解析（T3 两参形态用 (dto, 字段值) => …, 写回点即正向来源字段），而不再按名查找" },
                    Location = inv.GetLocation(),
                });
                return null;
            }
        }

        // 步骤 5：形态匹配——换向基准与特性侧 DTO 侧规范位置一致（实体侧换向基准三元组口径，
        // 见 MappingExtractor.MaterializePropertyMapping）：TField=锚点属性类型、TSource=DTO 类型（TTarget 泛型参）、
        // TTarget=实体侧同名属性类型；Nullable 守卫变体由共享核心编码（抽取产物）。
        // B 形态：lambda 绑定符号走同一共享核心（形态按 Convert 同规则判 T0/T1/T2/T3）
        if (ms is null) return null; // 契约收口：TryResolveConverterArgument 返回 true 必非空（编译器不可见）

        // 异名 lambda 写回专用基准（r1）：正向 .From(s => s.Amount).Format("N2") 的锚点类型是 string，
        // 而写回目标是 decimal——按锚点类型匹配会把"整源形态"误判为"T1 单值形态"（F21）。
        // 经 lambda 判定的异名写回一律把字段基准置 null（R6-b 口径：不做字段类型交叉校验，
        // 方法组两模板仍须整源入参），目标类型基准取 lambda 绑定返回类型（= 锚点类型，恒成立）。
        // 同名/扁平化解析路径的基准保持锚点类型，零变化。
        // 收口（r2）：lambda 判定成功但 entityProp 为 null 只可能是 T3 形态，此时恒走 ms.ReturnType 分支
        // ——显式写 `entityProp!` 仅用于对齐可空流分析（实体写回点由物化期按正向来源字段回填）。
        var fieldTypeBasis = backTargetFromLambda ? null : anchorProp.Type;
        var targetTypeBasis = backTargetFromLambda ? ms.ReturnType : entityProp!.Type;
        if (!MappingExtractor.ResolveConverterMethod(ms, fieldTypeBasis, fs.TargetType, targetTypeBasis,
                out var shape, out _))
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.ConvertBackShapeMismatchRule,
                // B 形态无方法组引用——F21 消息载体换 lambda 源文本（F40 同款口径）
                Args = new object[] { transcription != null ? expr.ToString() : ms.ContainingType.ToDisplayString() + "." + ms.Name,
                    anchorProp.Type.ToDisplayString(), fs.TargetType.ToDisplayString(),
                    targetTypeBasis.ToDisplayString() },
                Location = expr.GetLocation(),
            });
            return null;
        }
        // 异名 lambda 写回（T3 形态）：实体写回目标不可知（entityProp 为 null），
        // 由物化期按正向来源字段（From(s => s.Amount) 的 s.Amount）承载——见
        // MappingSnapshotBuilder.AttachBackDeclaration 的 backTargetName 取值。

        // 步骤 6：get-only 锚点门（置于末位——错误优先；H-07 写门镜像 MappingSnapshotBuilder.cs:84-86：
        // 物化遍历将吞掉无 public setter 的锚点声明——F22 提前告知而非改门，方向 A 特有边界）
        if (anchorProp.SetMethod == null || anchorProp.SetMethod.DeclaredAccessibility != Accessibility.Public)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.ConvertBackAnchorNotWritableRule,
                Args = new object[] { anchorProp.Name, anchorProp.ContainingType.ToDisplayString() },
                Location = inv.GetLocation(),
            });
            return null;
        }

        // 步骤 7：产物（EntityProperty 符号态持有——与 MaskType 同生命周期先例；
        // OverrideIgnore 字段——提取期收下、物化期置位 HasOverrideIgnore）。
        // 异名写回 T3 形态（entityProp 为 null）：写回落点由物化期取「正向 pm 的来源字段」承载
        // ——那里才有 From(s => s.Amount) 的 s.Amount 符号。
        // B 形态：转录产物 + lambda 绑定形参类型组承载（物化期计算生成方法名注入三元组）
        if (transcription != null)
        {
            return new FluentBackDeclaration(shape, inv.GetLocation(), entityProp,
                transcription.Body,
                ms.Parameters.Select(p => p.Type.ToDisplayString()).ToArray(),
                transcription.ParamNames, overrideIgnore);
        }
        return new FluentBackDeclaration(ms, shape, inv.GetLocation(), entityProp, overrideIgnore);
    }

    /// <summary>
    /// ConvertBack 异名写回目标解析（lambda 形态）——"两侧属性名不同"场景的显式声明出口。
    /// <para><b>动机</b>：ConvertBack 的实体侧写回目标原本只按<b>同名</b>（→ 扁平化叶）解析，
    /// 无同名即 NCMAP_F18。这对"正向格式化/重命名目标"（实体 <c>Amount</c> → DTO
    /// <c>AmountText</c>）形成死结：反向转换器明明持有整条 DTO 记录，却因为实体属性不叫同一个
    /// 名字而被拒绝——而 F67 又提示"需要往返请声明 .ConvertBack"。本方法让反向转换器 lambda
    /// 以<b>已经写出的表达式</b>指明写回目标：</para>
    /// <list type="bullet">
    /// <item>T2 整源形态 <c>(dto) =&gt; dto.AmountText</c>——表达式体为形参上的直接成员访问，
    /// 该成员即写回目标</item>
    /// <item>T3 组合形态 <c>(dto, text) =&gt; Parse(dto.AmountText)</c>——表达式体为
    /// 单实参调用，该实参是形参上的直接成员访问（或其本身），该成员即写回目标；
    /// 调用本身仍作为反向转换器发射</item>
    /// </list>
    /// <para><b>不猜测原则</b>（白名单式匹配，越界一律落回 F18）：形参个数 1~2、表达式体必为
    /// "单实参调用"或"成员访问"、成员访问的接收者必须是<b>形参自身</b>（排除 <c>this.X</c>、
    /// 静态辅助类成员、<c>dto.A.B</c> 链式嵌套）、成员必须是该形参类型上的属性、且必须
    /// public 可写（写回只需 setter——write-only 计算/存储属性合法）。多目标写回
    /// （对象初始化器）与复合表达式（读 A 写 B 无法从体推断）不支持，语义上也不应猜测。</para>
    /// <para><b>T3 形态（两参）</b>：第二个形参是字段值形参，实体侧写回目标由该字段决定，
    /// 但<b>实体字段名</b>无从得知。此时只校验"体中出现了 T2 形态（整源形参上的成员访问）"——
    /// 出现即视为异名写回成立，返回 true 且 <c>entityProp</c> 为 null（调用方据此把字段基准
    /// 置 null，按 R6-b 不做字段类型交叉校验，最终由整源模板 T3 命中）。</para>
    /// <para><b>优先级</b>：本解析仅在"同名 → 扁平化叶"双未命中时启用——既有解析结果零变化。</para>
    /// </summary>
    /// <param name="lambda">反向转换器 lambda（已过转录门）</param>
    /// <param name="sm">语义模型</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="entityProp">产出：写回目标实体属性（T3 形态成功时为 null）</param>
    /// <returns>true = 已从 lambda 体判定写回目标</returns>
    private static bool TryResolveLambdaBackTarget(
        LambdaExpressionSyntax lambda,
        SemanticModel sm,
        CancellationToken ct,
        out IPropertySymbol? entityProp)
    {
        entityProp = null;
        // 语句体 lambda 无可读的"单表达式目标"——不猜测
        if (lambda.Body is not ExpressionSyntax body) return false;

        // 形参名集合（T2 单参 / T3 双参）；无参无整源入参 → 无从解析
        var paramNames = new HashSet<string>(StringComparer.Ordinal);
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                paramNames.Add(simple.Parameter.Identifier.ValueText);
                break;
            case ParenthesizedLambdaExpressionSyntax paren:
                foreach (var p in paren.ParameterList.Parameters)
                    paramNames.Add(p.Identifier.ValueText);
                break;
            default:
                return false;
        }
        if (paramNames.Count is < 1 or > 2) return false;

        // 形态① 裸成员访问 / 形态② 单实参调用的实参
        ExpressionSyntax candidate = body;
        if (candidate is InvocationExpressionSyntax invocation
            && invocation.ArgumentList.Arguments.Count == 1)
        {
            candidate = invocation.ArgumentList.Arguments[0].Expression;
        }

        switch (candidate)
        {
            case MemberAccessExpressionSyntax access:
                // 接收者必须是形参自身（排除 this.X / 静态类型成员 / 链式 dto.A.B）
                if (sm.GetSymbolInfo(access.Expression, ct).Symbol is not IParameterSymbol owner
                    || !paramNames.Contains(owner.Name))
                {
                    return false;
                }
                if (sm.GetSymbolInfo(access, ct).Symbol is not IPropertySymbol prop) return false;
                // 成员须声明在形参类型自身上（归属歧义时不猜测）
                if (prop.ContainingType == null
                    || !SymbolEqualityComparer.Default.Equals(prop.ContainingType, owner.Type))
                {
                    return false;
                }
                // 写回只要求 setter 可达：public setter 即可（get-only 拒绝；write-only 放行）
                if (prop.SetMethod?.DeclaredAccessibility != Accessibility.Public) return false;
                entityProp = prop;
                return true;

            case IdentifierNameSyntax fieldParam:
                // T3 形态：第二形参（字段值形参）被直接使用——实体侧写回目标由该字段决定，
                // 但实体字段名无从得知（引擎自行按正向来源字段取）。此处只做"确为形参"的门，
                // 具体目标交由 T3 整源模板发射时按正向来源写入。
                if (!paramNames.Contains(fieldParam.Identifier.ValueText)) return false;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// （更名 ConvertUsing→Convert）：
    /// 解析 Convert 声明（方法组 + 形态匹配 + 源侧解析 + get-only 门）——
    /// 七步流程（提取期单点版，仿 <see cref="ResolveConvertBackMethod"/> 反向七步换向复用）：
    /// <list type="number">
    /// <item>实参门：args.Count 2 或 3（overrideIgnore）→ 否则静默（编译器已报）</item>
    /// <item>converter 形态分路（F26 收窄）：无捕获表达式 lambda → 转录引擎
    /// 放行（F41/F42 保留禁令仍拒）；委托变量/实例方法绑定 → F26（镜像 F20）+ 可见性门 F25</item>
    /// <item>可见性门：private/protected → F25（镜像 F19）</item>
    /// <item>源侧解析（正向）：同名 FindProperty → 自动扁平化 TryFindFlattenedPath →
    /// [MapProperty] 反向查找 TryFindSourceByReverseAttribute——与特性侧 [Convert] 消费端
    /// 解析序一致（MappingExtractor.cs:359-378）；全路径不可达 → F24（附着模式无 pm 锚点，fail-fast）；
    /// 产物 SourceProperty/SourcePath 符号态持有（物化期零重复解析）</item>
    /// <item>形态匹配（正向基准）：TField=源侧属性类型、TSource=源实体类型（fs.SourceType）、
    /// TTarget=dest 锚点属性类型——复用 ResolveConverterMethod 直入重载；
    /// 无一命中 → F27（与特性侧 NCMAP037 同口径；Nullable 守卫变体由共享核心编码）</item>
    /// <item>get-only 锚点门（置于末位——错误优先）：锚点属性无 public setter → F28（Warning，
    /// H-07 写门镜像——声明将被物化遍历静默吞掉，提前告知）</item>
    /// <item>产物：FluentForwardDeclaration { Method, Shape, Location, SourceProperty, SourcePath, OverrideIgnore }</item>
    /// </list>
    /// </summary>
    /// <param name="inv">Convert invocation 节点</param>
    /// <param name="dest">dest lambda 解析结果（单段属性路径锚点）</param>
    /// <param name="fs">解析期单链快照（SourceType/TargetType 符号源）</param>
    /// <param name="sm">语义模型</param>
    /// <param name="diagnostics">诊断收集（F24/F25/F26/F27/F28）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>正向声明（符号态）；解析失败 null（诊断已发）</returns>
    private static FluentForwardDeclaration? ResolveConvertMethod(
        InvocationExpressionSyntax inv,
        LambdaParseResult? dest,
        FluentSnapshot fs,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct,
        ExpressionSyntax? memberConverterExpr = null,
        bool memberOverrideIgnore = false)
    {
        var args = inv.ArgumentList.Arguments;
        // converter 实参/豁免门参数化——新形态（.Member(x).Convert(...)，
        // converter 前移 args[0]、无 dest）经 memberConverterExpr 直入（实参门由调用方 ProcessChainConvert
        // 形态分派把守）；旧形态（dest+converter[, oi]）原样：两参或三参——实参数错误编译器已报
        ExpressionSyntax converterExpr;
        bool overrideIgnore;
        if (memberConverterExpr != null)
        {
            converterExpr = memberConverterExpr;
            overrideIgnore = memberOverrideIgnore;
        }
        else
        {
            if (args.Count is < 2 or > 3) return null;
            converterExpr = args[ConverterArgIndex].Expression;
            overrideIgnore = ParseOverrideIgnoreArg(inv, sm, 2, diagnostics);
        }

        // dest 锚点门：须解析为单段有效属性路径（多段/字段段/超限沿用 F02/F04~F07——LambdaParser 已发）
        if (dest == null || !dest.IsValidPath || dest.Segments.Count != 1) return null;
        // 索引段承载面仅 Map-src——转换器/写回点锚点索引无语义，F06 拒绝
        if (dest.Segments.Any(s => s.IndexAfter != null))
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                Args = new object[] { inv.ToString() },
                Location = inv.GetLocation(),
            });
            return null;
        }
        var anchorProp = dest.Segments[0].Property;
        if (anchorProp == null) return null; // 字段段——静默跳过（锚点模型仅承载 IPropertySymbol）

        // 步骤 2：converter 实参形态分路（——原三段重复区块收敛至
        // TryResolveConverterArgument 共享内核；本段 F26/F25 诊断码参数化，Convert 特有方法组
        // 候选回退（B2-01 多候选防崩）经开关承载——TSrcProp 位于委托入参位，参数级绑定可报
        // OverloadResolutionFailure 而 CandidateSymbols 恰含唯一方法）。
        // expr 出参数化 converterExpr（新形态直入 / 旧形态 args[1]——两路同核）
        var expr = converterExpr;
        if (!TryResolveConverterArgument(expr, sm, diagnostics, ct,
                allowMethodGroupCandidates: true,
                DiagnosticDescriptorsFluent.ConvertNotMethodGroupRule,
                DiagnosticDescriptorsFluent.ConvertInaccessibleRule,
                out var ms, out var transcription))
        {
            return null;
        }

        // 步骤 4：源侧解析（正向——与特性侧 [Convert] 消费端一致，解析序换向适配）：
        // ① 显式 Map/Convert 声明的 src 路径优先（特性侧 [MapProperty].FromField 优先语义的 Fluent
        //    等价物——两遍链解析保证链序无关）
        // ② 直接同名 FindProperty → ③ 自动扁平化 TryFindFlattenedPath（特性侧扁平化分支承载转换器消费）
        //    → ④ [MapProperty] 反向查找 TryFindSourceByReverseAttribute（特性侧 B1 改动点 2）
        // 全路径不可达 → F24（fail-fast；特性侧无源走 R6-b default 调用为通道边界差异——差异登记）
        var sourceProp = default(IPropertySymbol?);
        List<IPropertySymbol>? sourcePath = null;
        if (fs.Declarations.TryGetValue(anchorProp.Name, out var srcDecl)
            && srcDecl.SrcSegments is { Count: > 0 } srcSegs
            && srcSegs.All(s => s.Property != null))
        {
            sourcePath = srcSegs.Select(s => s.Property!).ToList();
            sourceProp = sourcePath[sourcePath.Count - 1];
        }
        if (sourceProp == null || sourceProp.GetMethod == null)
        {
            sourceProp = MappingExtractor.FindProperty(fs.SourceType, anchorProp.Name);
            if (sourceProp != null && sourceProp.GetMethod != null)
            {
                sourcePath = new List<IPropertySymbol> { sourceProp };
            }
            else
            {
                var flatPath = MappingExtractor.TryFindFlattenedPath(fs.SourceType, anchorProp.Name);
                if (flatPath is { Count: > 1 })
                {
                    sourceProp = flatPath[flatPath.Count - 1]; // 扁平化叶（嵌套）
                    sourcePath = flatPath;
                }
                else
                {
                    var reverseProp = MappingExtractor.TryFindSourceByReverseAttribute(fs.SourceType, anchorProp, out _);
                    if (reverseProp?.GetMethod != null)
                    {
                        sourceProp = reverseProp;
                        sourcePath = new List<IPropertySymbol> { reverseProp };
                    }
                    else
                    {
                        sourceProp = null;
                        diagnostics.Add(new DiagnosticInfo
                        {
                            Descriptor = DiagnosticDescriptorsFluent.ConvertSourceNotFoundRule,
                            Args = new object[] { anchorProp.Name, fs.SourceType.ToDisplayString(), anchorProp.Name },
                            Location = inv.GetLocation(),
                        });
                        return null;
                    }
                }
            }
        }
        sourcePath ??= new List<IPropertySymbol> { sourceProp! };

        // 步骤 5：形态匹配——正向基准与特性侧 [Convert] 消费端一致（MappingExtractor.cs:618/:634）：
        // TField=源侧属性类型、TSource=源实体类型（fs.SourceType）、TTarget=dest 锚点属性类型；
        // Nullable 守卫变体由共享核心编码（直入重载统一产出）。
        // B 形态：lambda 绑定符号走同一共享核心（R5 门天然通过——lambda 无泛参/可选参/params/ref；
        // 形态按 Convert 同规则判 T0/T1/T2/T3， "形态判定延伸"）
        if (ms is null) return null; // 契约收口：TryResolveConverterArgument 返回 true 必非空（编译器不可见）
        if (!MappingExtractor.ResolveConverterMethod(ms, sourceProp!.Type, fs.SourceType, anchorProp.Type,
                out var shape, out _))
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.ConvertShapeMismatchRule,
                // B 形态无方法组引用——F27 消息载体换 lambda 源文本（F40 同款口径）
                Args = new object[] { transcription != null ? expr.ToString() : ms.ContainingType.ToDisplayString() + "." + ms.Name,
                    sourceProp.Type.ToDisplayString(), fs.SourceType.ToDisplayString(), anchorProp.Type.ToDisplayString() },
                Location = expr.GetLocation(),
            });
            return null;
        }

        // 步骤 6：get-only 锚点门（置于末位——错误优先；H-07 写门镜像 MappingSnapshotBuilder：
        // 物化遍历将吞掉无 public setter 的锚点声明——F28 提前告知而非改门，与 F22 同构）
        if (anchorProp.SetMethod == null || anchorProp.SetMethod.DeclaredAccessibility != Accessibility.Public)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.ConvertAnchorNotWritableRule,
                Args = new object[] { anchorProp.Name, anchorProp.ContainingType.ToDisplayString() },
                Location = inv.GetLocation(),
            });
            return null;
        }

        // 步骤 7：产物（SourceProperty/SourcePath 符号态持有——物化期附着与 ForwardOnly 兜底消费）。
        // B 形态：转录产物 + lambda 绑定形参类型组承载（物化期计算生成方法名注入三元组）
        if (transcription != null)
        {
            return new FluentForwardDeclaration(shape, inv.GetLocation(), sourceProp, sourcePath,
                transcription.Body,
                ms.Parameters.Select(p => p.Type.ToDisplayString()).ToArray(),
                transcription.ParamNames, overrideIgnore);
        }
        return new FluentForwardDeclaration(ms, shape, inv.GetLocation(), sourceProp, sourcePath, overrideIgnore);
    }

    /// <summary>
    /// 解析 MapWhen 条件守卫声明。
    /// <para>步骤：① dest 单段锚点门（F02/F04~F07/F06 沿用 LambdaParser）→ ② 谓词形态判定：
    /// 内联 lambda → B 形态（InlineTranscriber 转录，F41/F42 精确 span）；方法组 → A 形态
    /// （符号解析 + 静态门 + 可见性门 + 签名门 → F40）→ ③ 入参形态判定（T1 字段值/T2 整源——
    /// 单参 == 源实体类型判整源，否则判字段值；自引用双匹配判 T1，Q8 矩阵）→
    /// ④ 源侧类型校验（T1 显式 Map src → 同名 → 扁平化叶；源→谓词入参须隐式转换，违者 F40）→
    /// ⑤ 产物 FluentWhenDeclaration（锚点落空/重复声明/主链 Compose 检测归物化期 Build 消费：
    /// F43/F44/F45）。</para>
    /// <para>块内声明：sourceTypeForShape=子源类型，锚定子源属性——发射落点为
    /// Update_{Sub}_To_{Target} 赋值层（主链锚定 Compose 覆盖属性 → F45，物化期判定）。</para>
    /// </summary>
    private static void ResolveMapWhen(
        PendingWhenInvocation pending,
        FluentSnapshot fs,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var inv = pending.Invocation;
        var args = inv.ArgumentList.Arguments;

        // T-003新形态分路：谓词单参（.Member(x).MapWhen(...) 混合载）
        // 锚点出 PendingWhenInvocation.MemberDest（Member 开段已过 F55/F06 门，免重解析免重复
        // 诊断）；旧形态双参（dest + 谓词）原样。
        var memberForm = pending.MemberDest != null;
        LambdaParseResult? dest;
        if (memberForm)
        {
            if (args.Count != 1) return; // 新形态单参（谓词）——实参数错误编译器已报
            dest = pending.MemberDest;
        }
        else
        {
            if (args.Count != 2) return; // 实参数错误——编译器已报（MapWhen 双参签名）

            // ① dest 锚点门（单段有效属性路径；索引段无语义 F06 拒绝——Convert 族同口径）
            dest = ParseLambdaArg(args[0].Expression, sm, diagnostics, ct);
            // 站点清单③（MapWhen 多段锚）：dest 多段 → F55（原静默；
            // 索引 F06（下）与重复 F44 已在位不触）
            if (dest != null && dest.IsValidPath && dest.Segments.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.MultiSegmentDestNotSupportedRule,
                    Args = new object[] { DescribePath(dest) },
                    Location = inv.GetLocation(),
                });
                return;
            }
        }
        if (dest == null || !dest.IsValidPath || dest.Segments.Count != 1) return;
        if (dest.Segments.Any(s => s.IndexAfter != null))
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.FluentIndexerRule,
                Args = new object[] { inv.ToString() },
                Location = inv.GetLocation(),
            });
            return;
        }
        var anchorProp = dest.Segments[0].Property;
        if (anchorProp == null) return; // 字段段——静默跳过（锚点模型仅承载 IPropertySymbol）

        // F71（新形态类型参门）：MapWhen<TInput> 显式类型参合法域 ∈
        // {TProp（=锚点属性类型，字段值形态显式写法）, TSource（整源形态）}；域外拒收——
        // 防误写类型参的谓词被静默按字段值形态错绑（非泛型 T1 无类型参免检；旧形态不适用——
        // DetermineConditionShape 既有判定 + F40 源侧转换门已覆盖）
        if (memberForm)
        {
            var whenTypeArgs = GetGenericArguments(inv, out var whenHasGeneric);
            if (whenHasGeneric && whenTypeArgs.Count == 1)
            {
                var tInput = sm.GetSymbolInfo(whenTypeArgs[0], ct).Symbol as ITypeSymbol;
                if (tInput != null
                    && !SymbolEqualityComparer.Default.Equals(tInput, anchorProp.Type)
                    && !SymbolEqualityComparer.Default.Equals(tInput, pending.SourceTypeForShape))
                {
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptorsFluent.MapWhenTypeArgMismatchRule,
                        Args = new object[] { anchorProp.Name, tInput.ToDisplayString(), pending.SourceTypeForShape.ToDisplayString() },
                        Location = inv.GetLocation(),
                    });
                    return;
                }
            }
        }

        // ② 谓词形态判定 + ③ 入参形态判定 + ④ 源侧类型校验
        // 谓词实参位自适应——新形态 args[0] / 旧形态 args[PredicateArgIndex]
        FluentWhenDeclaration? whenDecl;
        var expr = args[memberForm ? 0 : PredicateArgIndex].Expression;
        if (expr is LambdaExpressionSyntax lambda)
        {
            whenDecl = ResolveMapWhenInline(inv, lambda, anchorProp, pending.SourceTypeForShape, fs, pending.IsBlock, sm, diagnostics, ct);
        }
        else
        {
            whenDecl = ResolveMapWhenMethodGroup(inv, expr, anchorProp, pending.SourceTypeForShape, fs, pending.IsBlock, sm, diagnostics, ct);
        }

        if (whenDecl == null) return;

        // ⑤ 存储（F44 提取期门：同锚点重复声明——主链按属性名 / 块按 (子源, 属性名)）
        if (pending.IsBlock)
        {
            var dup = fs.BlockWhenDeclarations.Any(b =>
                SymbolEqualityComparer.Default.Equals(b.SubSourceType, pending.BlockSubSource)
                && string.Equals(b.TargetPropertyName, anchorProp.Name, StringComparison.Ordinal));
            if (dup)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.MapWhenDuplicateRule,
                    Args = new object[] { anchorProp.Name, pending.BlockSubSource!.ToDisplayString() },
                    Location = inv.GetLocation(),
                });
                return;
            }
            fs.BlockWhenDeclarations.Add(new BlockWhenDeclaration(pending.BlockSubSource!, anchorProp.Name, whenDecl));
        }
        else
        {
            if (fs.WhenDeclarations.ContainsKey(anchorProp.Name))
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptorsFluent.MapWhenDuplicateRule,
                    Args = new object[] { anchorProp.Name, fs.TargetType.ToDisplayString() },
                    Location = inv.GetLocation(),
                });
                return;
            }
            fs.WhenDeclarations[anchorProp.Name] = whenDecl;
        }
    }

    /// <summary>
    /// B 形态解析——内联 lambda 经 InlineTranscriber 转录
    /// （F41/F42 精确 span 由转录引擎发射）+ lambda 形参符号取入参类型（T1/T2 形态判定）+
    /// 源侧类型校验（T1 隐式转换门 → F40）。
    /// </summary>
    private static FluentWhenDeclaration? ResolveMapWhenInline(
        InvocationExpressionSyntax inv,
        LambdaExpressionSyntax lambda,
        IPropertySymbol anchorProp,
        INamedTypeSymbol sourceTypeForShape,
        FluentSnapshot fs,
        bool isBlock,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var transcription = InlineTranscriber.TryTranscribe(lambda, sm, diagnostics, ct);
        if (transcription == null) return null; // F41/F42 已发（精确 span）

        // lambda 形参符号 → 实际绑定类型（T1 = dest 属性类型经重载推断；T2 = TSource）。
        // GetSymbolInfo（非 GetDeclaredSymbol——后者对作为实参的 lambda 恒 null，实测）：
        // lambda 经重载择优绑定后符号即其 IMethodSymbol（形参类型 = 绑定后委托签名）
        var lambdaSymbol = sm.GetSymbolInfo(lambda, ct).Symbol as IMethodSymbol;
        var paramType = lambdaSymbol?.Parameters.FirstOrDefault()?.Type;
        if (paramType == null) return null;

        var shape = DetermineConditionShape(paramType, sourceTypeForShape, anchorProp.Type);
        if (!ValidateWhenSourceConversion(inv, paramType, anchorProp, sourceTypeForShape, fs, isBlock, sm, diagnostics))
            return null;

        return new FluentWhenDeclaration(
            anchorProp.Name, null, transcription.Body, transcription.ParamName, shape, inv.GetLocation());
    }

    /// <summary>
    /// A 形态解析——命名静态方法组（符号解析 + 静态门 + 可见性门 +
    /// 签名门 → F40）+ 入参形态判定 + 源侧类型校验。
    /// </summary>
    private static FluentWhenDeclaration? ResolveMapWhenMethodGroup(
        InvocationExpressionSyntax inv,
        ExpressionSyntax expr,
        IPropertySymbol anchorProp,
        INamedTypeSymbol sourceTypeForShape,
        FluentSnapshot fs,
        bool isBlock,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var symInfo = sm.GetSymbolInfo(expr, ct);
        // 多候选防崩（与 Convert 路径同型修复）——FirstOrDefault 容多候选
        // + 计数门显式拒绝（F40 {1}=方法组候选不唯一成因 + return null；单候选行为与现状一致）。
        var whenCandidates = symInfo.CandidateSymbols.OfType<IMethodSymbol>().ToList();
        var ms = symInfo.Symbol as IMethodSymbol ?? whenCandidates.FirstOrDefault();
        if (symInfo.Symbol == null && whenCandidates.Count >= 2)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.MapWhenMethodContractRule,
                Args = new object[] { expr.ToString(), "方法组候选不唯一（重载歧义）" },
                Location = expr.GetLocation(),
            });
            return null;
        }

        // 方法组契约门：不可解析（含委托变量/实例绑定）/ lambda/局部函数/非静态
        if (ms == null
            || ms.MethodKind == MethodKind.LambdaMethod
            || ms.MethodKind == MethodKind.LocalFunction
            || !ms.IsStatic)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.MapWhenMethodContractRule,
                Args = new object[] { expr.ToString(), "谓词必须为可静态寻址的 public/internal static 方法组" },
                Location = expr.GetLocation(),
            });
            return null;
        }

        // 可见性门：生成代码位于 internal static class GeneratedMapper（同程序集）
        // private/protected 跨类不可访问
        if (ms.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.MapWhenMethodContractRule,
                Args = new object[] { ms.ContainingType.ToDisplayString() + "." + ms.Name, "谓词方法可见性不足（private/protected 跨类不可访问）" },
                Location = expr.GetLocation(),
            });
            return null;
        }

        // 签名门：单参 + 返回 bool
        if (ms.Parameters.Length != 1 || ms.ReturnType.SpecialType != SpecialType.System_Boolean
            || ms.Parameters[0].RefKind != Microsoft.CodeAnalysis.RefKind.None)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.MapWhenMethodContractRule,
                Args = new object[] { ms.ContainingType.ToDisplayString() + "." + ms.Name, "谓词签名须为 static bool M(T)（单参、返回 bool、无 ref/out/in）" },
                Location = expr.GetLocation(),
            });
            return null;
        }

        var paramType = ms.Parameters[0].Type;
        var shape = DetermineConditionShape(paramType, sourceTypeForShape, anchorProp.Type);
        if (!ValidateWhenSourceConversion(inv, paramType, anchorProp, sourceTypeForShape, fs, isBlock, sm, diagnostics))
            return null;

        return new FluentWhenDeclaration(
            anchorProp.Name, ms.ContainingType.ToDisplayString() + "::" + ms.Name, null, null, shape, inv.GetLocation());
    }

    /// <summary>
    /// 谓词入参形态判定（矩阵）
    /// 单参 == 源实体类型判整源，否则判字段值；双匹配（自引用：参数类型 == 源实体类型
    /// == dest 属性类型）判 T1（betterness 择优——dest 更具体绑定，字段值即源对象语义等价）。
    /// 判定规则与 Convert 族"单参 == 源实体类型判整源，否则判字段值"同构。
    /// </summary>
    private static ConditionShape DetermineConditionShape(ITypeSymbol paramType, INamedTypeSymbol sourceType, ITypeSymbol anchorType)
    {
        var paramIsSource = SymbolEqualityComparer.Default.Equals(paramType, sourceType);
        var paramIsAnchor = SymbolEqualityComparer.Default.Equals(paramType, anchorType);
        return paramIsSource && !paramIsAnchor ? ConditionShape.WholeSource : ConditionShape.FieldValue;
    }

    /// <summary>
    /// T1 源侧类型校验——谓词入参（= dest 属性类型经重载绑定）与
    /// 实际传入的源字段值（发射端 src.X）须存在隐式转换（违者 F40——防生成代码 CS1503）。
    /// T2（整源）恒满足（实参即 src，类型精确匹配）。
    /// </summary>
    private static bool ValidateWhenSourceConversion(
        InvocationExpressionSyntax inv,
        ITypeSymbol paramType,
        IPropertySymbol anchorProp,
        INamedTypeSymbol sourceTypeForShape,
        FluentSnapshot fs,
        bool isBlock,
        SemanticModel sm,
        List<DiagnosticInfo> diagnostics)
    {
        if (SymbolEqualityComparer.Default.Equals(paramType, sourceTypeForShape))
            return true; // T2 整源（或自引用 T1——实参即 src，恒满足）

        var sourceType = TryResolveWhenSourceType(fs, anchorProp.Name, sourceTypeForShape, isBlock);
        if (sourceType == null)
            return true; // 源侧不可解析——锚点落空归物化期 F43（此处不重复报）

        var conversion = sm.Compilation.ClassifyCommonConversion(sourceType, paramType);
        if (!conversion.IsImplicit)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptorsFluent.MapWhenMethodContractRule,
                Args = new object[] { anchorProp.Name, $"源字段类型 {sourceType.ToDisplayString()} 与谓词入参类型 {paramType.ToDisplayString()} 无隐式转换（生成调用点将无法编译）" },
                Location = inv.GetLocation(),
            });
            return false;
        }
        return true;
    }

    /// <summary>
    /// 解析 MapWhen 锚点属性的源侧类型
    /// 主链：显式 Map src 叶 → 同名 FindProperty → 自动扁平化叶；
    /// 块内：块内 Map src 叶（ComposeDeclarations）→ 子源同名 FindProperty → 扁平化叶。
    /// </summary>
    private static ITypeSymbol? TryResolveWhenSourceType(
        FluentSnapshot fs, string anchorName, INamedTypeSymbol sourceTypeForShape, bool isBlock)
    {
        if (isBlock)
        {
            var cd = fs.ComposeDeclarations.FirstOrDefault(c =>
                SymbolEqualityComparer.Default.Equals(c.SubSourceType, sourceTypeForShape)
                && string.Equals(c.TargetPropertyName, anchorName, StringComparison.Ordinal)
                && c.SourcePath is { Count: > 0 });
            if (cd != null)
                return cd.SourcePath![cd.SourcePath.Count - 1].Type;
        }
        else if (fs.Declarations.TryGetValue(anchorName, out var mapDecl)
                 && mapDecl.SrcSegments is { Count: > 0 })
        {
            var leaf = mapDecl.SrcSegments[mapDecl.SrcSegments.Count - 1];
            return leaf.Property?.Type ?? leaf.Field?.Type;
        }

        var sourceProp = MappingExtractor.FindProperty(sourceTypeForShape, anchorName);
        if (sourceProp?.GetMethod != null) return sourceProp.Type;

        var flat = MappingExtractor.TryFindFlattenedPath(sourceTypeForShape, anchorName);
        if (flat is { Count: > 1 }) return flat[flat.Count - 1].Type;
        return null;
    }

    /// <summary>出口值化：符号态诊断 → 纯值 DiagnosticSnapshot 列表（Location → LocationSnapshot）。
    /// internal 化：SubclassSyntheticPipeline（特性侧合成管道）复用。</summary>
    internal static IReadOnlyList<DiagnosticSnapshot> ToDiagnostics(List<DiagnosticInfo> diagnostics)
    {
        return diagnostics.Select(MappingExtractor.MaterializeDiagnostic).ToArray();
    }
}

