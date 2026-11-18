using System.Text;
using Microsoft.CodeAnalysis;
namespace NCMapper.SourceGen;

/// <summary>
/// 深拷贝可达属性图的三值分类结果（源生成侧）— 方案 F 条件分发依据
/// <para>与运行时侧 <c>NCMapper.DeepCopyGraphShape</c> 语义一致；源生成器程序集不引用主库，
/// 故各自定义。两侧边规则口径由 统一，一致性由 T6 语料测试约束。
/// 可靠性不变式（宁可多检测，不可漏检测）：<see cref="Acyclic"/> 类型的运行时对象图
/// 不可能出现深拷贝遍历路径上的环。</para>
/// </summary>
internal enum DeepCopyGraphShape : byte
{
    /// <summary>
    /// 无环 — 所有可达引用边均可解析（具体命名类型/数组/Nullable/集合元素/字典 K-V），且图上无环。
    /// </summary>
    Acyclic,

    /// <summary>
    /// 有环 — 可达图中存在被证明的环。
    /// </summary>
    Cyclic,

    /// <summary>
    /// 未知 — 存在不可解析边：object/dynamic/接口类型/开放泛型/泛型参数/
    /// 可被重写的 virtual 引用属性（v1.1 F1）。保守归完整检测。
    /// </summary>
    Unknown,
}

/// <summary>
/// 三值分类过程中记录的不可解析边（供 NCMAP016 逐边报告）
/// <para>三元组：属性所在类型名 / 属性名 / 属性类型显示名 + 属性位置（诊断定位）。</para>
/// </summary>
internal readonly struct UnknownEdge
{
    /// <summary>
    /// 初始化不可解析边记录
    /// </summary>
    /// <param name="ownerType">属性所在类型显示名</param>
    /// <param name="propertyName">属性名（容器节点级不可解析为占位文案）</param>
    /// <param name="propertyTypeDisplay">属性类型显示名</param>
    /// <param name="location">属性位置（null 时诊断落类型位置）</param>
    public UnknownEdge(string ownerType, string propertyName, string propertyTypeDisplay, Location? location)
    {
        OwnerType = ownerType;
        PropertyName = propertyName;
        PropertyTypeDisplay = propertyTypeDisplay;
        Location = location;
    }

    /// <summary>
    /// 属性所在类型显示名
    /// </summary>
    public string OwnerType { get; }

    /// <summary>
    /// 属性名
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// 属性类型显示名
    /// </summary>
    public string PropertyTypeDisplay { get; }

    /// <summary>
    /// 属性位置（诊断定位用，可为 null）
    /// </summary>
    public Location? Location { get; }
}

/// <summary>
/// 代码生成器（深拷贝生成）— record 路径与无参构造路径、数组/集合属性深拷贝、类型与集合辅助
/// </summary>
internal static partial class CodeGenerator
{
    #region 深拷贝生成

    // 原「为单个类型生成 DeepCopy 方法」无宿主文档块删除（T6.1）——该签名的宿主方法已并入 GenerateDeepCopyForType（其摘要见下）
    /// <summary>
    /// 生成 DeepCopy 方法（S3 值版入口）— 分类与节点在输出期由值域分类器/物化节点提供，
    /// 输出期零反查符号。
    /// <para>W3_1 （C4 前移）：className/node/shape/unknownEdges 四参现来自 prep.DeepCopyGraphs
    /// （准备段构造——键收集 + nodeMap 合并 + Classify 循环前移至 GenerationPreparation）；
    /// 本方法纯渲染，签名与逻辑逐行不动。</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="typeRef">类型快照</param>
    /// <param name="className">类名+后缀（方法名 DeepCopy_{className}）</param>
    /// <param name="spc">源生产上下文（NCMAP016/NCMAP012 诊断输出）</param>
    /// <param name="node">DeepCopy 类型图节点快照</param>
    /// <param name="shape">三值分类结果（值域分类器输出）</param>
    /// <param name="unknownEdges">Unknown 时不可解析边清单（NCMAP016 逐边报告）</param>
    /// <returns>三值分类结果（模块初始化器 acyclic 字面量输入）</returns>
    private static DeepCopyGraphShape GenerateDeepCopyForType(StringBuilder sb, TypeRef typeRef, string className, SourceProductionContext spc, DeepCopyNodeShape node, DeepCopyGraphShape shape, IReadOnlyList<UnknownEdgeSnapshot> unknownEdges)
    {
        var typeName = typeRef.FullName;
        var methodName = $"DeepCopy_{className}";

        // NCMAP016（Info）：Unknown 类型逐不可解析边报告（类型/属性/属性类型三元组）
        if (shape == DeepCopyGraphShape.Unknown)
        {
            foreach (var edge in unknownEdges)
            {
                var location = edge.Location != null
                    ? RebuildDeepCopyLocation(edge.Location.Value) ?? Location.None
                    : node.Location != null ? RebuildDeepCopyLocation(node.Location.Value) ?? Location.None
                    : Location.None;
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DeepCopyGraphUnknownRule,
                    location,
                    edge.OwnerType, edge.PropertyName, edge.PropertyTypeDisplay));
            }
        }

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 源生成器深拷贝：{typeName} → {typeName}（编译时生成，~100ns）");
        sb.AppendLine($"        /// </summary>");

        sb.AppendLine($"        public static {typeName} {methodName}({typeName} src, NCMapper.DeepCopyContext ctx)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            // 签名非空仅为满足 Mapper.RegisterGeneratedDeepCopy 委托契约（Func<T, DeepCopyContext, T>），null 透传与运行时 DeepCopy 入口语义一致");
        sb.AppendLine($"            if (src is null) return null!;");

        bool hasParameterlessCtor = node.HasPublicParameterlessCtor;

        // 拆分为 record 路径和 parameterless ctor 路径两个独立方法
        if (!hasParameterlessCtor)
        {
            // 循环引用检测 — record 构造器路径无法在 new T(...) 前预登记 dst，
            // 循环引用图会导致无限递归。检测到循环时回退到运行时 DeepCopyRuntimeOnly。
            // Unknown 同 Cyclic 回退
            // record 的 dst 在 new T(...) 内部才创建，不可解析边（object/接口/virtual 重写）
            // 背后的环在构造器路径中无法被 CycleCache 防御，静态无法证明安全即回退运行时。
            if (shape != DeepCopyGraphShape.Acyclic)
            {
                sb.AppendLine($"            // Fallback path: circular reference safety mechanism (record ctor path cannot pre-register dst, cycle would recurse infinitely). May fail under NativeAOT; see docs 06-Deep-Copy");
                sb.AppendLine($"#pragma warning disable IL2026, IL3050");
                sb.AppendLine($"            return NCMapper.Mapper.DeepCopyRuntimeOnly<{typeName}>(src, ctx)!;");
                sb.AppendLine($"#pragma warning restore IL2026, IL3050");
                sb.AppendLine($"        }}");
                sb.AppendLine();
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RecordCircularReferenceRule,
                    node.Location != null ? RebuildDeepCopyLocation(node.Location.Value) ?? Location.None
                    : Location.None,
                    typeName));
                return shape;
            }

            if (!GenerateDeepCopyRecordPath(sb, node, typeName))
            {
                // 无合适构造器 → 已在 GenerateDeepCopyRecordPath 中输出 return + 方法结尾
                return shape;
            }
        }
        else
        {
            GenerateDeepCopyParameterlessPath(sb, node, typeName, shape);
        }

        sb.AppendLine($"            return dst;");
        sb.AppendLine($"        }}");
        sb.AppendLine();
        return shape;
    }

    /// <summary>
    /// 生成 record 深拷贝路径（无无参构造器）— 从 GenerateDeepCopyForType 拆分
    /// <para>通过构造器参数匹配生成 new T(arg1, arg2, ...)，复杂类型参数递归 DeepCopy</para>
    /// <para>构造后追加构参外可写属性补填（镜像运行时
    /// CompileFiller 修复——构参外 set 属性不再静默丢失）；构参已匹配属性与 init-only
    /// 构参外属性跳过（后者受 C# 构造后赋值限制 CS8852，与
    /// <see cref="GenerateDeepCopyParameterlessPath"/>的 P1-SHOULD-8 口径一致）。</para>
    /// <para><b>已知限制（循环引用图）</b>：dst 对象在构造器调用 <c>new T(...)</c> 内部创建，
    /// 无法像 <see cref="GenerateDeepCopyParameterlessPath"/> 那样在构造前调用
    /// <c>Mapper.RegisterDeepCopyTarget(src, dst)</c> 预登记 dst。因此当源图存在循环引用
    /// （自引用 A.Self = A、双向引用 A↔B）且走本源生成路径时，构造器参数中递归的
    /// <c>Mapper.DeepCopy(src.Prop)</c> 无法在 ThreadStatic 字典中命中已登记 dst，
    /// 仍会无限递归直至栈溢出。</para>
    /// <para><b>规避方案</b>：record 类型 + 循环引用图的组合请走运行时路径 —
    /// 调用 <c>DeepCopyProviderBase.Current.DeepCopy(source)</c>，或通过
    /// <c>Mapper.RegisterGeneratedDeepCopy&lt;T&gt;(null)</c> 注销源生成委托使
    /// <see cref="Mapper.DeepCopy{T}"/> 回退到运行时 Provider（使用 ConditionalWeakTable，
    /// 支持所有类型含 record 的循环引用检测）。</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="type">目标类型</param>
    /// <param name="typeName">目标类型全名字符串</param>
    /// <returns>true 表示成功生成构造代码；false 表示无合适构造器，已输出运行时回退代码</returns>
    private static bool GenerateDeepCopyRecordPath(StringBuilder sb, DeepCopyNodeShape node, string typeName)
    {
        // record 等无无参构造器 → 用构造器参数
        // 注意：dst 在构造器调用 new T(...) 内部创建，无法在构造前预登记到循环引用检测表，
        // 故本路径不支持循环引用图（自引用 / A↔B）。如需处理循环引用，请走运行时
        // DeepCopyProviderBase.Current.DeepCopy 路径（基于 ConditionalWeakTable）。
        var ctor = node.PublicConstructors.FirstOrDefault();

        if (ctor == null || ctor.Parameters.Count == 0)
        {
            // 无法编译 → 回退到运行时（使用 DeepCopyRuntimeOnly 避免重入 GeneratedDeepCopyCache 导致无限递归）
            sb.AppendLine($"            // Fallback path: compile failure fallback to runtime deep copy (avoids re-entry into GeneratedDeepCopyCache infinite recursion). May fail under NativeAOT; see docs 06-Deep-Copy");
            sb.AppendLine($"#pragma warning disable IL2026, IL3050");
            sb.AppendLine($"            return NCMapper.Mapper.DeepCopyRuntimeOnly<{typeName}>(src, ctx);");
            sb.AppendLine($"#pragma warning restore IL2026, IL3050");
            sb.AppendLine($"        }}");
            sb.AppendLine();
            return false;
        }

        var args = new List<string>();
        // 属性图沿继承链遍历（物化期已完成最派生去重）——与分类器同口径
        var props = node.Properties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var param in ctor.Parameters)
        {
            if (props.TryGetValue(param.Name, out var prop))
            {
                var propType = prop.Type;
                if (IsSimpleTypeValue(propType))
                {
                    args.Add($"src.{SourceGenEscape.Identifier(prop.Name)}");
                }
                else
                {
                    // null 分支按目标可空性生成（NRT 安全，防 CS8625——CleanArchitecture
                    // TodoList.Colour 不可空引用实测触发；不可空引用 → default!，可空引用 → null）。
                    // 差异登记：构造器参数为表达式列表，无法 if 化——若参数类型不可空且源属性可能 null，
                    // 三元 null 状态不传播仍会 CS8625（用户类型设计问题：不可空参数不应收 null；当前测试源无此场景）
                    args.Add($"src.{SourceGenEscape.Identifier(prop.Name)} is not null ? NCMapper.Mapper.DeepCopy(src.{SourceGenEscape.Identifier(prop.Name)}, ctx)! : {TypeRefHelpers.NullLiteralFor(propType)}");
                }
            }
            else
            {
                args.Add($"default({param.Type.FullName})");
            }
        }
        sb.AppendLine($"            var dst = new {typeName}({string.Join(", ", args)});");

        // 构参外可写属性补填
        // 镜像运行时修复（DeepCopyCompiler.CompileFiller 去无参构造短路 + ProviderBase 占位
        // 分支补调 FillProperties）。构参已匹配的属性由构造器设置（跳过，同值重赋无意义）；
        // init-only 构参外属性无法在构造后赋值（C# CS8852），跟随
        // GenerateDeepCopyParameterlessPath 的 P1-SHOULD-8 既有口径跳过。
        // 本路径仅在 Acyclic shape 下生成（非 Acyclic 已回退 DeepCopyRuntimeOnly），
        // 构参外属性边已被分类图覆盖，递归 DeepCopy 无环安全。
        var ctorCoveredProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var param in ctor.Parameters)
        {
            if (props.TryGetValue(param.Name, out var matched))
                ctorCoveredProps.Add(matched.Name);
        }

        var externalProps = node.Properties
            .Where(p => p.HasSetter
                && p.SetterIsPublic
                && !p.SetterIsInitOnly
                && !ctorCoveredProps.Contains(p.Name));

        foreach (var prop in externalProps)
        {
            // 跳过 [MapIgnore]
            if (prop.HasMapIgnore)
                continue;

            var propType = prop.Type;
            if (IsSimpleTypeValue(propType))
            {
                // 值类型/string/枚举 → 直接赋值
                sb.AppendLine($"            dst.{SourceGenEscape.Identifier(prop.Name)} = src.{SourceGenEscape.Identifier(prop.Name)};");
            }
            else if (propType.IsArray)
            {
                GenerateDeepCopyArrayProperty(sb, prop, propType);
            }
            else if (propType.IsDictionary
                && propType.DictionaryKeyType != null && propType.DictionaryValueType != null)
            {
                GenerateDeepCopyDictionaryProperty(sb, prop, propType, propType.DictionaryKeyType, propType.DictionaryValueType);
            }
            else if (propType.EnumerableElementType != null)
            {
                GenerateDeepCopyCollectionProperty(sb, prop, propType, propType.EnumerableElementType);
            }
            else
            {
                // 引用类型 → 递归 DeepCopy（复用同一 ctx 保持循环引用检测口径一致）
                sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(prop.Name)} is not null)");
                sb.AppendLine($"                dst.{SourceGenEscape.Identifier(prop.Name)} = NCMapper.Mapper.DeepCopy(src.{SourceGenEscape.Identifier(prop.Name)}, ctx)!;");
            }
        }
        return true;
    }

    /// <summary>
    /// 生成无参构造器深拷贝路径 — 从 GenerateDeepCopyForType 拆分
    /// <para>new T() + 逐属性赋值（简单类型直接赋值、数组/集合/引用类型递归 DeepCopy）</para>
    /// <para>Acyclic 变体省略<c>RegisterDeepCopyTarget</c>
    /// 编译期已证明类型图无环，层 1 <c>IsAcyclic</c> 分支跳过 CycleCache，登记无消费方；
    /// Cyclic/Unknown 变体保留登记（CycleCache 完整检测路径依赖它打破循环）。</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="type">目标类型</param>
    /// <param name="typeName">目标类型全名字符串</param>
    /// <param name="shape">三值分类结果（决定是否省略 CycleCache 登记行）</param>
    private static void GenerateDeepCopyParameterlessPath(StringBuilder sb, DeepCopyNodeShape node, string typeName, DeepCopyGraphShape shape)
    {
        sb.AppendLine($"            var dst = new {typeName}();");

        // T1 补丁：dst 创建后立即登记到 Mapper.DeepCopy 的循环引用检测表
        // 这样递归 Mapper.DeepCopy(src.Prop, ctx) 调用（src.Prop 循环回 src 时）
        // 能 TryGetValue 命中已登记的 dst，返回共享引用，打破循环。
        // 原版登记在 Mapper.DeepCopy 入口的 genDeepCopy 返回后，对真正循环图无效。
        // 方向 I：传递 ctx 参数，登记到 ctx.CycleCache 替代 [ThreadStatic] s_deepCopyInProgress
        // 仅 Cyclic/Unknown 变体输出登记行；Acyclic 图无环可打破，层 1 亦跳过 CycleCache
        if (shape != DeepCopyGraphShape.Acyclic)
        {
            sb.AppendLine($"            NCMapper.Mapper.RegisterDeepCopyTarget(src, dst, ctx);");
        }

        // 逐属性赋值
        // 跳过 init-only 属性（无法用 dst.Prop = value 赋值）；
        // init-only 属性的深拷贝需通过构造器参数路径，此处仅处理可写属性
        // 属性图沿继承链遍历（物化期已完成最派生去重）；setter 条件在此额外过滤
        var props = node.Properties
            .Where(p => p.HasSetter
                && p.SetterIsPublic
                && !p.SetterIsInitOnly);

        foreach (var prop in props)
        {
            // 跳过 [MapIgnore]
            if (prop.HasMapIgnore)
                continue;

            var propType = prop.Type;
            if (IsSimpleTypeValue(propType))
            {
                // 值类型/string/枚举 → 直接赋值
                sb.AppendLine($"            dst.{SourceGenEscape.Identifier(prop.Name)} = src.{SourceGenEscape.Identifier(prop.Name)};");
            }
            else if (propType.IsArray)
            {
                GenerateDeepCopyArrayProperty(sb, prop, propType);
            }
            else if (propType.IsDictionary
                && propType.DictionaryKeyType != null && propType.DictionaryValueType != null)
            {
                // 字典属性须在单值集合判定之前
                // 字典不被视为单值集合，否则字典属性落入引用类型递归分支（运行时回退，AOT 不可用）。
                GenerateDeepCopyDictionaryProperty(sb, prop, propType, propType.DictionaryKeyType, propType.DictionaryValueType);
            }
            else if (propType.EnumerableElementType != null)
            {
                GenerateDeepCopyCollectionProperty(sb, prop, propType, propType.EnumerableElementType);
            }
            else
            {
                // 引用类型 → 递归 DeepCopy
                // 方向 I：传递 ctx 参数，使递归调用复用同一 ctx 保持循环引用检测有效
                // DeepCopy<T> 返回 T?（Mapper.cs:909）——三元表达式 null 状态
                // 不传播（Roslyn 保守行为），`cond ? expr! : default!` 赋不可空目标仍 CS8625（CleanArchitecture
                // TodoList.Colour 实测）。改 if 语句：src 非 null 才赋值（src null → 目标保持默认值，语义更安全）
                sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(prop.Name)} is not null)");
                sb.AppendLine($"                dst.{SourceGenEscape.Identifier(prop.Name)} = NCMapper.Mapper.DeepCopy(src.{SourceGenEscape.Identifier(prop.Name)}, ctx)!;");
            }
        }
    }

    /// <summary>
    /// 生成数组属性的深拷贝代码 — 从 GenerateDeepCopyParameterlessPath 拆分
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="prop">属性符号</param>
    /// <param name="propType">属性类型</param>
    private static void GenerateDeepCopyArrayProperty(StringBuilder sb, DeepCopyPropertyShape prop, TypeRef propType)
    {
        // 数组 → new + CopyTo 或逐元素 DeepCopy
        var elemType = propType.ElementType!;
        if (IsSimpleTypeValue(elemType))
        {
            sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(prop.Name)} is not null) dst.{SourceGenEscape.Identifier(prop.Name)} = ({propType.FullName})src.{SourceGenEscape.Identifier(prop.Name)}.Clone();");
        }
        else
        {
            sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(prop.Name)} is not null) {{");
            sb.AppendLine($"                var _arr = new {elemType.FullName}[src.{SourceGenEscape.Identifier(prop.Name)}.Length];");
            sb.AppendLine($"                for (int _i = 0; _i < src.{SourceGenEscape.Identifier(prop.Name)}.Length; _i++)");
            sb.AppendLine($"                {{");
            sb.AppendLine($"                    if (src.{SourceGenEscape.Identifier(prop.Name)}[_i] is not null)");
            sb.AppendLine($"                        _arr[_i] = NCMapper.Mapper.DeepCopy(src.{SourceGenEscape.Identifier(prop.Name)}[_i], ctx)!;");
            sb.AppendLine($"                }}");
            sb.AppendLine($"                dst.{SourceGenEscape.Identifier(prop.Name)} = _arr;");
            sb.AppendLine($"            }}");
        }
    }

    /// <summary>
    /// 生成集合属性的深拷贝代码 — 从 GenerateDeepCopyParameterlessPath 拆分
    /// <para>Queue/Stack/ConcurrentStack/SortedSet 按类型生成特定填充代码（Enqueue/Push/Add），
    /// 避免 List 中间路径的不必要分配；其他单值集合保持 List 中间路径</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="prop">属性符号</param>
    /// <param name="propType">属性类型</param>
    /// <param name="elemType">元素类型</param>
    private static void GenerateDeepCopyCollectionProperty(StringBuilder sb, DeepCopyPropertyShape prop, TypeRef propType, TypeRef elemType)
    {
        var elemTypeStr = elemType.FullName;
        var typeName = propType.SimpleName;
        // CS8628去掉外层可空注解，避免 new Queue<int>?() 非法语法
        // （Queue<int>? → Queue<int>；元素注解不受影响）
        var fullTypeStr = propType.NonNullableFullName;
        var propName = prop.Name;

        // Queue<T>→ new Queue<T>() + Enqueue
        if (typeName == "Queue")
        {
            sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(propName)} is not null) {{");
            sb.AppendLine($"                var _coll = new {fullTypeStr}();");
            if (IsSimpleTypeValue(elemType))
            {
                sb.AppendLine($"                foreach (var _item in src.{SourceGenEscape.Identifier(propName)}) _coll.Enqueue(_item);");
            }
            else
            {
                sb.AppendLine($"                foreach (var _item in src.{SourceGenEscape.Identifier(propName)})");
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    if (_item is not null)");
                sb.AppendLine($"                        _coll.Enqueue(NCMapper.Mapper.DeepCopy(_item, ctx)!);");
                sb.AppendLine($"                }}");
            }
            sb.AppendLine($"                dst.{SourceGenEscape.Identifier(propName)} = _coll;");
            sb.AppendLine($"            }}");
            return;
        }

        // Stack<T>/ConcurrentStack<T>→ ToArray() + Reverse + Push（保持 LIFO 顺序）
        // Stack<T>.GetEnumerator() 从栈顶到栈底遍历，直接 Push 会反转顺序
        // 因此先 ToArray()（栈顶→栈底）再 Reverse（栈底→栈顶），Push 后新栈与原栈一致
        // P4 连带：ConcurrentStack 同入本分支——SimpleName "ConcurrentStack"
        // 原落 List 中间路径直接构造（ctor 按枚举序 Push → 顺序反转），与运行时
        // CollectionCopier.CopyConcurrentStack（CopyTo→Reverse→Push 保序）通道分歧（Fuzz 实证）。
        // ConcurrentStack<T> 同具 ToArray()（栈顶→栈底）与 Push，发射形态与 Stack 一致。
        if (typeName == "Stack" || typeName == "ConcurrentStack")
        {
            sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(propName)} is not null) {{");
            sb.AppendLine($"                var _arr = src.{SourceGenEscape.Identifier(propName)}.ToArray();");
            sb.AppendLine($"                System.Array.Reverse(_arr);");
            sb.AppendLine($"                var _coll = new {fullTypeStr}();");
            if (IsSimpleTypeValue(elemType))
            {
                sb.AppendLine($"                foreach (var _item in _arr) _coll.Push(_item);");
            }
            else
            {
                sb.AppendLine($"                foreach (var _item in _arr)");
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    if (_item is not null)");
                sb.AppendLine($"                        _coll.Push(NCMapper.Mapper.DeepCopy(_item, ctx)!);");
                sb.AppendLine($"                }}");
            }
            sb.AppendLine($"                dst.{SourceGenEscape.Identifier(propName)} = _coll;");
            sb.AppendLine($"            }}");
            return;
        }

        // SortedSet<T>→ new SortedSet<T>() + Add
        if (typeName == "SortedSet")
        {
            sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(propName)} is not null) {{");
            sb.AppendLine($"                var _coll = new {fullTypeStr}();");
            if (IsSimpleTypeValue(elemType))
            {
                sb.AppendLine($"                foreach (var _item in src.{SourceGenEscape.Identifier(propName)}) _coll.Add(_item);");
            }
            else
            {
                sb.AppendLine($"                foreach (var _item in src.{SourceGenEscape.Identifier(propName)})");
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    if (_item is not null)");
                sb.AppendLine($"                        _coll.Add(NCMapper.Mapper.DeepCopy(_item, ctx)!);");
                sb.AppendLine($"                }}");
            }
            sb.AppendLine($"                dst.{SourceGenEscape.Identifier(propName)} = _coll;");
            sb.AppendLine($"            }}");
            return;
        }

        // 其他单值集合 → 先填 List<T>，再用构造器创建目标集合（支持 HashSet 等全部类型）
        sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(propName)} is not null) {{");
        sb.AppendLine($"                var _tmpList = new System.Collections.Generic.List<{elemTypeStr}>();");
        if (IsSimpleTypeValue(elemType))
        {
            sb.AppendLine($"                foreach (var _item in src.{SourceGenEscape.Identifier(propName)}) _tmpList.Add(_item);");
        }
        else
        {
            sb.AppendLine($"                foreach (var _item in src.{SourceGenEscape.Identifier(propName)})");
            sb.AppendLine($"                {{");
            sb.AppendLine($"                    if (_item is not null)");
            sb.AppendLine($"                        _tmpList.Add(NCMapper.Mapper.DeepCopy(_item, ctx)!);");
            sb.AppendLine($"                }}");
        }
        sb.AppendLine($"                dst.{SourceGenEscape.Identifier(propName)} = {GetCollectionCreationExpression(propType, elemTypeStr, "_tmpList")};");
        sb.AppendLine($"            }}");
    }

    /// <summary>
    /// 生成字典属性的深拷贝代码 — P1-5
    /// <para>Dictionary&lt;K,V&gt; 及 IDictionary&lt;K,V&gt;/IReadOnlyDictionary&lt;K,V&gt; 接口 →
    /// new Dictionary + 逐 KeyValuePair 深拷贝。镜像运行时 CollectionCopier.CopyDictionary 语义：
    /// key/value 简单类型（值类型/string/枚举）原样复用，引用类型递归 DeepCopy；
    /// key 非 null（Dictionary 不允许 null key）无需判空。
    /// 仅处理确切 Dictionary 与字典接口——其他字典实现（ConcurrentDictionary/SortedDictionary 等）
    /// 不具备可静态生成的构造路径，保持既有行为（差异登记 X）。</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="prop">属性符号</param>
    /// <param name="propType">属性类型（字典类型）</param>
    /// <param name="keyType">字典 Key 类型</param>
    /// <param name="valueType">字典 Value 类型</param>
    private static void GenerateDeepCopyDictionaryProperty(StringBuilder sb, DeepCopyPropertyShape prop, TypeRef propType, TypeRef keyType, TypeRef valueType)
    {
        var keyStr = keyType.NonNullableFullName;
        var valueStr = valueType.NonNullableFullName;
        var valueSimple = IsSimpleTypeValue(valueType);
        var keyExpr = IsSimpleTypeValue(keyType)
            ? "_kv.Key"
            : "NCMapper.Mapper.DeepCopy(_kv.Key, ctx)!";

        // （lin-cms ExtraProperties 场景 CS0266 错误列 39 = _dict）：
        // 局部变量类型对齐属性声明类型——原实现用基类 Dictionary<K,V> 局部变量赋给字典子类属性 → 不可隐式转换。
        // 三分层：
        // ① 确切 Dictionary<K,V> / 字典接口（IDictionary/IReadOnlyDictionary）→ new Dictionary<K,V>()（保持现状，
        //    与运行时 ResolveDictionaryCopyMethod 的 CopyDictionary 同构）
        // ② 字典子类可无参构造（lin-cms ExtraProperties 满足）→ new {propType}() 子类实例填充（与运行时
        //    CopyDictionaryByKeyValue 按子类反射构造同构——Q9 已确认无双端问题）
        // ③ 字典子类不可无参构造 → 差异登记：降级浅拷贝引用赋值 + 注释（不静默产出编译错误）
        // P4 连带（CS8628 修复，Queue 路径 同模式）：
        // 子类构造分支的 dictTypeName 剥外层可空注解——可空字典属性（如 ConcurrentDictionary<K,V>?）
        // 的 FullName 携带 "?" 注解，new 表达式禁可空注解（new X?() 非法语法）→ NonNullableFullName。
        var dictTypeName = propType.NonNullableFullName;
        var isSystemCollections = propType.Namespace.StartsWith(
            "System.Collections", StringComparison.Ordinal);
        var isInterfaceDict = propType.Kind == TypeKind.Interface
            && (propType.SimpleName == "IDictionary" || propType.SimpleName == "IReadOnlyDictionary")
            && isSystemCollections;
        var isExactDictionary = propType.SimpleName == "Dictionary"
            && isSystemCollections;
        var hasPublicParameterlessCtor = propType.HasPublicParameterlessCtor;

        if (isInterfaceDict || isExactDictionary || hasPublicParameterlessCtor)
        {
            var ctorExpr = (isInterfaceDict || isExactDictionary)
                ? $"new System.Collections.Generic.Dictionary<{keyStr}, {valueStr}>()"
                : $"new {dictTypeName}()";
            sb.AppendLine($"            if (src.{SourceGenEscape.Identifier(prop.Name)} is not null) {{");
            sb.AppendLine($"                var _dict = {ctorExpr};");
            sb.AppendLine($"                foreach (var _kv in src.{SourceGenEscape.Identifier(prop.Name)})");
            if (valueSimple)
            {
                sb.AppendLine($"                    _dict[{keyExpr}] = _kv.Value;");
            }
            else
            {
                // DeepCopy<T> 返回 T?——三元 null 状态不传播（CS8625），
                // 改 if 形式：值非 null 才 DeepCopy（null 保持目标默认）
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    if (_kv.Value is not null)");
                sb.AppendLine($"                        _dict[{keyExpr}] = NCMapper.Mapper.DeepCopy(_kv.Value, ctx)!;");
                sb.AppendLine($"                }}");
            }
            sb.AppendLine($"                dst.{SourceGenEscape.Identifier(prop.Name)} = _dict;");
            sb.AppendLine($"            }}");
        }
        else
        {
            // ③ 差异登记：字典子类无公共无参构造器，无法按子类实例深拷贝——降级浅拷贝引用赋值
            // （类型匹配可编译，避免 CS0266；语义为浅拷贝，与深拷贝语义差异在此显式登记）
            sb.AppendLine($"            // 差异登记：字典子类 {dictTypeName} 无公共无参构造器，" +
                "无法按子类实例深拷贝——降级为浅拷贝引用赋值（类型匹配，避免 CS0266）");
            sb.AppendLine($"            dst.{SourceGenEscape.Identifier(prop.Name)} = src.{SourceGenEscape.Identifier(prop.Name)};");
        }
    }

    #endregion 深拷贝生成

    #region 类型与集合辅助

    // 原「三值分类节点预算」无宿主文档块删除（T6.1）——当前载体 TypeRefDeepCopyClassifier.NodeBudget

    // 原「解包属性类型，收集需要递归分类的引用类型边目标」无宿主文档块删除（T6.1）——当前载体 TypeRefDeepCopyClassifier.CollectEdgeTargets

    // 原「容器类型建边」无宿主文档块删除（T6.1）——当前载体 TypeRefDeepCopyClassifier.CollectContainerEdges

    // 原「判断类型是否为叶子」无宿主文档块删除（T6.1）——叶子判定现由 DeepCopyNodeShape.IsLeaf 快照字段承载

    // 原「判断类型是否为容器」无宿主文档块删除（T6.1）——容器判定现由 DeepCopyNodeShape.IsCollectionLike 快照字段承载

    /// <summary>
    /// 判断是否为简单类型（值类型/string/枚举/Nullable&lt;T&gt;）— 值版
    /// </summary>
    private static bool IsSimpleTypeValue(TypeRef type)
    {
        if (type.Kind == TypeKind.Enum) return true;
        if (type.IsValueType) return true;
        if (type.SpecialType == SpecialType.System_String) return true;
        return false;
    }

    /// <summary>
    /// 获取集合创建表达式 — 从 List 构造目标集合类型
    /// <para>支持所有有 IEnumerable&lt;T&gt; 构造器的集合类型（Queue/Stack/HashSet/ConcurrentQueue 等）</para>
    /// </summary>
    /// <param name="type">目标集合类型符号</param>
    /// <param name="elemTypeStr">元素类型全名字符串</param>
    /// <param name="listVarName">已填充的 List 变量名</param>
    /// <returns>从 List 构造目标集合的 C# 表达式</returns>
    private static string GetCollectionCreationExpression(TypeRef type, string elemTypeStr, string listVarName)
    {
        var typeName = type.SimpleName;
        // CS8628去掉外层可空注解，避免 new LinkedList<int>?(_tmpList) 非法语法
        var fullTypeStr = type.NonNullableFullName;

        // List<T>/IList<T>/IEnumerable<T> 等接口 → 直接用 List 赋值
        if (typeName is "List" or "IList" or "ICollection"
            or "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection")
            return listVarName;

        // HashSet<T>/ISet<T>/IReadOnlySet<T> → new HashSet<T>(list)
        if (typeName is "HashSet" or "ISet" or "IReadOnlySet")
            return $"new System.Collections.Generic.HashSet<{elemTypeStr}>({listVarName})";

        // 其他具体集合类型 → new FullType<T>(list)（依赖 IEnumerable<T> 或 IList<T> 构造器）
        return $"new {fullTypeStr}({listVarName})";
    }

    /// <summary>
    /// 重建 DeepCopy 诊断位置（external location，不依赖 Compilation）
    /// </summary>
    private static Location? RebuildDeepCopyLocation(LocationSnapshot snapshot)
        => Location.Create(snapshot.FilePath, snapshot.Span, snapshot.LineSpan);

    #endregion 类型与集合辅助
}
