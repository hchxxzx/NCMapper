using System.Text;
using Microsoft.CodeAnalysis;
using NCMapper.Shared;
namespace NCMapper.SourceGen;

/// <summary>
/// 代码生成器（映射方法生成）— Core/Action/Update/MapList 方法、属性赋值、构造器匹配、内联元素、类型转换与源访问辅助
/// </summary>
internal static partial class CodeGenerator
{
    #region 映射方法生成

    /// <summary>
    /// 生成 Core 映射方法（无 Action 参数，热路径，方法体短可被 JIT 内联）
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表，用于嵌套映射引用</param>
    /// <param name="ctx">可选生成上下文（传递全局配置等）</param>
    /// <param name="methodSuffix">方法名后缀（如 "_v2"）</param>
    public static void GenerateCoreMapMethod(StringBuilder sb, MappingSnapshot mapping, Dictionary<TypeRef, string> typeNameMap, GenerationContext? ctx = null, string methodSuffix = "", List<CodeGenerator.SubclassDispatchEntry>? subclassDispatch = null)
    {
        var sourceName = mapping.SourceType.FullName;
        var targetName = mapping.TargetType.FullName;
        var methodName = GetMethodNameWithSuffix(mapping, "Map", methodSuffix);

        // 自引用递归 Map 方法深度保护（与 MapList 同构，见 IsSelfRecursiveMapping 注释）
        var isSelfRecursive = IsSelfRecursiveMapping(mapping);
        if (isSelfRecursive)
        {
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// {methodName} 递归深度计数（自引用结构映射，深度保护）");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        [System.ThreadStatic]");
            sb.AppendLine($"        internal static int s_depth_{methodName};");
            sb.AppendLine();
        }

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 源生成器核心映射：{sourceName} → {targetName}（无 Action，热路径）");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");

        sb.AppendLine($"        public static {targetName}? {methodName}({sourceName} src)");
        sb.AppendLine($"        {{");

        // MapSubclass 分派链（族 2——Map 族）
        // 体首部、深度卫**之前**（分派先于 ++s_depth，防基类+子类双重计数提前触发
        // MaxRecursionDepth；分派 return 路径不经过 try/finally——深度字段未 ++ 无需归还）。
        // isinst 可赋值匹配（未登记更深派生命中最近登记祖先层）；链序 = 物化期最派生优先排序；
        // 未命中顺序落入既有基类兜底体（不重命名不新增 _base 方法）。
        if (subclassDispatch != null)
        {
            for (var i = 0; i < subclassDispatch.Count; i++)
            {
                var d = subclassDispatch[i];
                sb.AppendLine($"            if (src is {d.MatchTypeFullName} sub_{i}) return {d.CoreMethodName}(sub_{i});");
            }
        }

        if (isSelfRecursive)
        {
            var depthField = $"s_depth_{methodName}";
            sb.AppendLine($"            if (++{depthField} > NCMapper.Mapper.MaxRecursionDepth)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                --{depthField};");
            sb.AppendLine($"                throw new NCMapper.MapException(NCMapper.ErrorMessages.RecursionDepthExceeded(NCMapper.Mapper.MaxRecursionDepth, typeof({sourceName}).Name, typeof({targetName}).Name), typeof({sourceName}), typeof({targetName}));");
            sb.AppendLine($"            }}");
            sb.AppendLine($"            try");
            sb.AppendLine($"            {{");
        }

        sb.AppendLine($"            if (src is null) return null;");

        bool hasParameterlessCtor = GetConstructors(mapping.TargetType)
            .Any(c => c.Parameters.Count == 0 && c.IsPublic);

        // 绑定非空 → 构造器路径（命中构造器升权选中， /
        // 无参构造器不再短路选器，验收行 2"部分候选"场景；内联门 同步）；
        // 绑定空时谓词与现状逐字节等价
        bool useCtorPath = !hasParameterlessCtor || HasCtorParamBindings(mapping.TargetType);

        if (useCtorPath)
        {
            // ctx 传递（advisory 三站点之一——helper 内去重）
            if (GenerateConstructorMapping(sb, mapping, targetName, ctx: ctx))
            {
                // 无公共构造器 → 已生成 throw（MapConfigurationException），方法体到此结束：
                // 跳过属性赋值与 return dst；自递归场景仍需闭合 try/finally（throw 触发 finally 归还深度）
                if (isSelfRecursive)
                {
                    sb.AppendLine($"            }}");
                    sb.AppendLine($"            finally");
                    sb.AppendLine($"            {{");
                    sb.AppendLine($"                --s_depth_{methodName};");
                    sb.AppendLine($"            }}");
                }
                sb.AppendLine($"        }}");
                return;
            }
            // 构造器参数未覆盖的属性仍需赋值（跳过 init-only 和已通过构造器赋值的属性）
            // 构造器一体化形态（实参即首组赋值，构造与赋值不可分）无 Before 插入点 → F37（:86 赋值照旧，钩子不发射）
            ReportBeforeMappingUninsertableIfNeeded(ctx, mapping);
            GeneratePropertyAssignments(sb, mapping, "            ", typeNameMap, skipInitOnly: true, ctx, methodSuffix);
        }
        else
        {
            GeneratePropertyMapping(sb, mapping, targetName, typeNameMap, ctx, methodSuffix);
        }

        AppendAfterMappingCall(sb, mapping, "            ");

        sb.AppendLine($"            return dst;");
        if (isSelfRecursive)
        {
            // 自引用递归 Map：先闭合 try，再 finally 归还深度计数（异常路径也归还，避免残留），最后闭合方法
            sb.AppendLine($"            }}");
            sb.AppendLine($"            finally");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                --s_depth_{methodName};");
            sb.AppendLine($"            }}");
        }
        sb.AppendLine($"        }}");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成 Action 包装方法（复用 Core 方法，仅在有 action 时使用）
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="methodSuffix">方法名后缀（如 "_v2"）</param>
    public static void GenerateActionMapMethod(StringBuilder sb, MappingSnapshot mapping, string methodSuffix = "")
    {
        var sourceName = mapping.SourceType.FullName;
        var targetName = mapping.TargetType.FullName;
        var coreMethodName = GetMethodNameWithSuffix(mapping, "Map", methodSuffix);
        var actionMethodName = $"{coreMethodName}_WithAction";

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 源生成器映射：{sourceName} → {targetName}（带 Action 回调）");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");

        sb.AppendLine($"        public static {targetName}? {actionMethodName}({sourceName} src, System.Action<{sourceName}, {targetName}>? action)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            var dst = {coreMethodName}(src);");
        sb.AppendLine($"            if (dst != null) action?.Invoke(src, dst);");
        sb.AppendLine($"            return dst;");
        sb.AppendLine($"        }}");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成 Update 更新方法（更新已有目标对象，带 Action 回调）
    /// <para>与 Core 方法的属性赋值逻辑一致（值类型直接赋值、引用类型递归映射），
    /// 但不创建新对象——直接赋值到传入的 dst 参数。</para>
    /// <para>跳过 init-only 属性（已有对象无法后续赋值），不涉及构造器参数匹配（dst 已存在）。</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表，用于嵌套映射引用</param>
    /// <param name="ctx">可选生成上下文（传递全局配置等）</param>
    /// <param name="methodSuffix">方法名后缀（如 "_v2"）</param>
    public static void GenerateUpdateMapMethod(StringBuilder sb, MappingSnapshot mapping, Dictionary<TypeRef, string> typeNameMap, GenerationContext? ctx = null, string methodSuffix = "", List<SubclassDispatchEntry>? subclassDispatch = null)
    {
        var sourceName = mapping.SourceType.FullName;
        var targetName = mapping.TargetType.FullName;
        var coreMethodName = GetMethodNameWithSuffix(mapping, "Map", methodSuffix);
        var updateMethodName = $"{coreMethodName}_Update";

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 源生成器更新映射：{sourceName} → {targetName}（更新已有目标对象，带 Action 回调）");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");

        sb.AppendLine($"        public static void {updateMethodName}({sourceName} src, {targetName} dst, System.Action<{sourceName}, {targetName}>? action)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            if (src is null || dst is null) return;");

        // MapSubclass 双实例匹配分派（族 3——_Update 族）
        // null 卫之后、Before 之前。dst 固定型约束（002 B-1 深层语义）：dst 为调用方已存在实例，
        // 无法"变为"子类实例——src isinst && dst isinst 同时成立才路由子类 Update（全匹配排除
        // 错写路径）；action 经委托逆变透传（Action<S,T> 可赋 Action<子类对>）；
        // dst 运行时非 TTargetDerived → 回落既有基类更新体（残留静默面由 F50③ Info 可见）。
        if (subclassDispatch != null)
        {
            for (var i = 0; i < subclassDispatch.Count; i++)
            {
                var d = subclassDispatch[i];
                sb.AppendLine($"            if (src is {d.MatchTypeFullName} sub_{i} && dst is {d.TargetTypeFullName} subDst_{i})");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                {d.UpdateMethodName}(sub_{i}, subDst_{i}, action);");
                sb.AppendLine($"                return;");
                sb.AppendLine($"            }}");
            }
        }
        /* 复用 GeneratePropertyAssignments（与 Core 方法属性赋值逻辑一致）：
         * skipInitOnly=true 跳过 init-only 属性（已有对象无法后续赋值）。
         * dst 为参数，GeneratePropertyAssignments 生成的 dst.Prop = value 直接赋值到传入对象。 */
        // 赋值前发射 Before——dst 旧值可见（_Update 语义锚，验收 #3）
        AppendBeforeMappingCall(sb, mapping, "            ");
        GeneratePropertyAssignments(sb, mapping, "            ", typeNameMap, skipInitOnly: true, ctx, methodSuffix);
        AppendAfterMappingCall(sb, mapping, "            ");
        sb.AppendLine($"            action?.Invoke(src, dst);");
        sb.AppendLine($"        }}");
        sb.AppendLine();
    }

    #endregion 映射方法生成

    #region 映射方法生成（续）

    /// <summary>
    /// 生成 MapList 方法 — 内联赋值循环
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表，用于嵌套映射引用</param>
    /// <param name="ctx">可选生成上下文（传递全局配置等）</param>
    /// <param name="methodSuffix">方法名后缀（如 "_v2"）</param>
    public static void GenerateMapListMethod(StringBuilder sb, MappingSnapshot mapping, Dictionary<TypeRef, string> typeNameMap, GenerationContext? ctx = null, string methodSuffix = "", List<SubclassDispatchEntry>? subclassDispatch = null)
    {
        var sourceName = mapping.SourceType.FullName;
        var targetName = mapping.TargetType.FullName;
        var methodName = GetMethodNameWithSuffix(mapping, "MapList", methodSuffix);

        // 范围扩大：P0 自引用递归 MapList 深度保护
        // 集合属性元素对 == mapping 自身（树形 DTO，如 Category.Children: List<Category>→List<CategoryDto>）
        // 时，生成 MapList 方法会对自引用集合直接递归且无深度保护（运行时 MapperBase 有 s_depthIn/s_depthOut
        // 双计数器保护，生成侧没有）→ 成环数据栈溢出崩溃（实测 Category 测试进程 Stack overflow）。
        // 为递归 MapList 方法生成 ThreadStatic 深度字段 + 入口检查 + try-finally（仅递归方法生成，
        // 常规 MapList 零开销；深度语义与运行时 MaxRecursionDepth 对齐）
        var isSelfRecursive = IsSelfRecursiveMapping(mapping);

        if (isSelfRecursive)
        {
            var depthField = $"s_depth_{methodName}";
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// {methodName} 递归深度计数（自引用集合树形映射，深度保护）");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        [System.ThreadStatic]");
            sb.AppendLine($"        internal static int {depthField};");
            sb.AppendLine();
        }

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 源生成器列表映射：{sourceName} → List&lt;{targetName}&gt;（内联赋值循环 + 预分配容量）");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"sources\">源列表</param>");
        sb.AppendLine($"        /// <returns>映射后的目标列表；sources 为 null 时实际返回 null（null 透传）。签名声明非空仅为满足 Mapper.RegisterGeneratedList 委托契约，运行时 null 语义与简单赋值路径一致</returns>");
        sb.AppendLine($"        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");

        sb.AppendLine($"        public static System.Collections.Generic.List<{targetName}> {methodName}(System.Collections.Generic.IEnumerable<{sourceName}> sources)");
        sb.AppendLine($"        {{");

        if (isSelfRecursive)
        {
            var depthField = $"s_depth_{methodName}";
            // 入口深度检查（与运行时 MapperBase.Map 的 s_depthIn/s_depthOut 语义对齐：
            // 超过 MaxRecursionDepth 视为循环引用，抛 MapException 防栈溢出）
            sb.AppendLine($"            if (++{depthField} > NCMapper.Mapper.MaxRecursionDepth)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                --{depthField};");
            sb.AppendLine($"                throw new NCMapper.MapException(NCMapper.ErrorMessages.RecursionDepthExceeded(NCMapper.Mapper.MaxRecursionDepth, typeof({sourceName}).Name, typeof({targetName}).Name), typeof({sourceName}), typeof({targetName}));");
            sb.AppendLine($"            }}");
            sb.AppendLine($"            try");
            sb.AppendLine($"            {{");
        }

        sb.AppendLine($"            // 签名非空仅为满足 Mapper.RegisterGeneratedList 委托契约（Func<List<T>, List<T>>），null 透传与简单赋值路径语义一致");
        sb.AppendLine($"            if (sources is null) return null!;");

        // 数组快速路径 — null 元素保留为 null（保持索引一致性 + 精确预分配）
        sb.AppendLine($"            if (sources is {sourceName}[] arr)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                var list = new System.Collections.Generic.List<{targetName}>(arr.Length);");
        sb.AppendLine($"                for (int i = 0; i < arr.Length; i++)");
        sb.AppendLine($"                {{");
        sb.AppendLine($"                    var src = arr[i];");
        sb.AppendLine($"                    if (src is null)");
        sb.AppendLine($"                    {{");
        sb.AppendLine($"                        list.Add(null!);");
        sb.AppendLine($"                        continue;");
        sb.AppendLine($"                    }}");
        // MapSubclass 元素级分派（族 4 路径①——元素新建目标，
        // dst 固定型不适用，仅源侧 isinst；等价逐元素走 Core 分派语义）
        EmitMapListElementDispatch(sb, subclassDispatch, "                    ", withAction: false);
        GenerateInlineElement(sb, mapping, "                    ", typeNameMap, ctx, methodSuffix);
        sb.AppendLine($"                    list.Add(dst);");
        sb.AppendLine($"                }}");
        sb.AppendLine($"                return list;");
        sb.AppendLine($"            }}");

        // ICollection 路径 — null 元素保留为 null
        sb.AppendLine($"            if (sources is System.Collections.Generic.ICollection<{sourceName}> coll)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                var list = new System.Collections.Generic.List<{targetName}>(coll.Count);");
        sb.AppendLine($"                foreach (var src in coll)");
        sb.AppendLine($"                {{");
        sb.AppendLine($"                    if (src is null)");
        sb.AppendLine($"                    {{");
        sb.AppendLine($"                        list.Add(null!);");
        sb.AppendLine($"                        continue;");
        sb.AppendLine($"                    }}");
        // 元素级分派（族 4 路径②）
        EmitMapListElementDispatch(sb, subclassDispatch, "                    ", withAction: false);
        GenerateInlineElement(sb, mapping, "                    ", typeNameMap, ctx, methodSuffix);
        sb.AppendLine($"                    list.Add(dst);");
        sb.AppendLine($"                }}");
        sb.AppendLine($"                return list;");
        sb.AppendLine($"            }}");

        // IEnumerable 路径 — null 元素保留为 null
        sb.AppendLine($"            {{");
        sb.AppendLine($"                var list = new System.Collections.Generic.List<{targetName}>();");
        sb.AppendLine($"                foreach (var src in sources)");
        sb.AppendLine($"                {{");
        sb.AppendLine($"                    if (src is null)");
        sb.AppendLine($"                    {{");
        sb.AppendLine($"                        list.Add(null!);");
        sb.AppendLine($"                        continue;");
        sb.AppendLine($"                    }}");
        // 元素级分派（族 4 路径③）
        EmitMapListElementDispatch(sb, subclassDispatch, "                    ", withAction: false);
        GenerateInlineElement(sb, mapping, "                    ", typeNameMap, ctx, methodSuffix);
        sb.AppendLine($"                    list.Add(dst);");
        sb.AppendLine($"                }}");
        sb.AppendLine($"                return list;");
        sb.AppendLine($"            }}");
        if (isSelfRecursive)
        {
            // 自引用递归 MapList：finally 归还深度计数（异常路径也归还，避免残留）
            sb.AppendLine($"            }}");
            sb.AppendLine($"            finally");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                --s_depth_{methodName};");
            sb.AppendLine($"            }}");
        }
        sb.AppendLine($"        }}");
        sb.AppendLine();

        // 带 Action 的 MapList 重载 — 每个元素映射后执行回调
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 源生成器列表映射（带 Action 回调）：{sourceName} → List&lt;{targetName}&gt;");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"sources\">源列表</param>");
        sb.AppendLine($"        /// <param name=\"action\">每个元素映射后的回调</param>");
        sb.AppendLine($"        /// <returns>映射后的目标列表；sources 为 null 时实际返回 null（null 透传）。签名声明非空仅为满足 Mapper.RegisterGeneratedList 委托契约，运行时 null 语义与简单赋值路径一致</returns>");

        sb.AppendLine($"        public static System.Collections.Generic.List<{targetName}> {methodName}(System.Collections.Generic.IEnumerable<{sourceName}> sources, System.Action<{sourceName}, {targetName}>? action)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            // 签名非空仅为满足 Mapper.RegisterGeneratedList 委托契约（Func<List<T>, List<T>>），null 透传与简单赋值路径语义一致");
        sb.AppendLine($"            if (sources is null) return null!;");
        sb.AppendLine($"            if (action is null) return {methodName}(sources);");
        sb.AppendLine($"            var list = new System.Collections.Generic.List<{targetName}>();");
        sb.AppendLine($"            foreach (var src in sources)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                if (src is null)");
        sb.AppendLine($"                {{");
        sb.AppendLine($"                    list.Add(null!);");
        sb.AppendLine($"                    continue;");
        sb.AppendLine($"                }}");
        // 元素级分派（族 4 路径④——带 Action 重载；action 语义同构：先映射后回调）
        EmitMapListElementDispatch(sb, subclassDispatch, "                ", withAction: true);
        GenerateInlineElement(sb, mapping, "                ", typeNameMap, ctx, methodSuffix);
        sb.AppendLine($"                action(src, dst);");
        sb.AppendLine($"                list.Add(dst);");
        sb.AppendLine($"            }}");
        sb.AppendLine($"            return list;");
        sb.AppendLine($"        }}");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成 MapListInto 方法 — 批量就地映射
    /// <para>与 GenerateMapListMethod 的差异：入参 IList&lt;TSource&gt; + TTarget[] 目标数组，void 返回；
    /// null 源元素 → continue（C4 槽位保持原值）；destination[i]==null → no-op；
    /// 元素赋值复用 GeneratePropertyAssignments（skipInitOnly: true，与 Update 方法同构——对已有 dst 赋值，
    /// 嵌套引用属性每轮新建子对象 C7）；入参处理两分支（数组快速路径 + 通用索引循环），
    /// 不复刻三分支形态（IList 继承 ICollection，I-1 修订）；自引用递归深度保护同构照搬（v1.3 拍板）</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表，用于嵌套映射引用</param>
    /// <param name="ctx">可选生成上下文（传递全局配置等）</param>
    /// <param name="methodSuffix">方法名后缀（如 "_v2"）</param>
    public static void GenerateMapListIntoMethod(StringBuilder sb, MappingSnapshot mapping, Dictionary<TypeRef, string> typeNameMap, GenerationContext? ctx = null, string methodSuffix = "", List<SubclassDispatchEntry>? subclassDispatch = null)
    {
        var sourceName = mapping.SourceType.FullName;
        var targetName = mapping.TargetType.FullName;
        var methodName = GetMethodNameWithSuffix(mapping, "MapListInto", methodSuffix);

        // 自引用递归深度保护 — 与 GenerateMapListMethod（:194-211）同构照搬
        // （v1.3 拍板）：集合属性元素对 == mapping 自身（树形 DTO）时生成独立 ThreadStatic 深度字段 +
        // 入口检查 + try-finally（仅递归方法生成，常规映射零开销；深度语义与运行时 MaxRecursionDepth 对齐）
        var isSelfRecursive = IsSelfRecursiveMapping(mapping);

        if (isSelfRecursive)
        {
            var depthField = $"s_depth_{methodName}";
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// {methodName} 递归深度计数（自引用集合树形映射，深度保护）");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        [System.ThreadStatic]");
            sb.AppendLine($"        internal static int {depthField};");
            sb.AppendLine();
        }

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 源生成器批量就地映射：{sourceName} → {targetName}[]（内联赋值循环，仅覆盖映射字段；复用场景顶层零分配）");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"sources\">源集合</param>");
        sb.AppendLine($"        /// <param name=\"destination\">预分配目标数组（调用方负责初始化每个元素；null 槽位 no-op 跳过，C5）</param>");
        sb.AppendLine($"        /// <param name=\"action\">每个元素映射后的回调，可为 null</param>");
        sb.AppendLine($"        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static void {methodName}(System.Collections.Generic.IList<{sourceName}> sources, {targetName}[] destination, System.Action<{sourceName}, {targetName}>? action)");
        sb.AppendLine($"        {{");

        if (isSelfRecursive)
        {
            var depthField = $"s_depth_{methodName}";
            // 入口深度检查（与 MapList 版一致：超过 MaxRecursionDepth 视为循环引用，抛 MapException 防栈溢出）
            sb.AppendLine($"            if (++{depthField} > NCMapper.Mapper.MaxRecursionDepth)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                --{depthField};");
            sb.AppendLine($"                throw new NCMapper.MapException(NCMapper.ErrorMessages.RecursionDepthExceeded(NCMapper.Mapper.MaxRecursionDepth, typeof({sourceName}).Name, typeof({targetName}).Name), typeof({sourceName}), typeof({targetName}));");
            sb.AppendLine($"            }}");
            sb.AppendLine($"            try");
            sb.AppendLine($"            {{");
        }

        // 防御性 null 守卫（公共入口 Mapper.MapListInto 已做 C1/C2 校验；此层保护生成方法被直接调用）
        sb.AppendLine($"            if (sources is null || destination is null) return;");

        // 数组快速路径 — null 源元素 → continue（槽位保持原值）；destination[i]==null → no-op
        sb.AppendLine($"            if (sources is {sourceName}[] srcArr)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                for (int i = 0; i < srcArr.Length; i++)");
        sb.AppendLine($"                {{");
        sb.AppendLine($"                    var src = srcArr[i];");
        sb.AppendLine($"                    if (src is null) continue;");
        sb.AppendLine($"                    var dst = destination[i];");
        sb.AppendLine($"                    if (dst is null) continue;");
        // MapSubclass 双实例匹配分派（族 5——MapListInto
        // 路径①数组快速路径；双路径必须同插，缺一即该形态静默不分派）。复用子类
        // Update 发射（dst 已存在，与 _Update 同构——dst 固定型双实例匹配；action 逆变透传）。
        EmitListIntoElementDispatch(sb, subclassDispatch, "                    ");
        // 元素赋值 — 复用 GeneratePropertyAssignments（对已有 dst 赋值，与 Update 方法同构；
        // init-only 属性跳过（已有对象无法后续赋值），诊断经 AddDistinct 与 Update 路径去重）
        // 元素赋值前发射 Before（数组快速路径插入点）
        AppendBeforeMappingCall(sb, mapping, "                    ");
        GeneratePropertyAssignments(sb, mapping, "                    ", typeNameMap, skipInitOnly: true, ctx, methodSuffix);
        AppendAfterMappingCall(sb, mapping, "                    ");
        sb.AppendLine($"                    action?.Invoke(src, dst);");
        sb.AppendLine($"                }}");
        sb.AppendLine($"                return;");
        sb.AppendLine($"            }}");

        // 通用索引循环（IList<T>继承 ICollection<T>有 Count；I-1 修订：无需复刻三分支形态）
        sb.AppendLine($"            for (int i = 0; i < sources.Count; i++)");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                var src = sources[i];");
        sb.AppendLine($"                if (src is null) continue;");
        sb.AppendLine($"                var dst = destination[i];");
        sb.AppendLine($"                if (dst is null) continue;");
        // 双实例匹配分派（族 5 路径②通用索引循环——与路径①同构同插）
        EmitListIntoElementDispatch(sb, subclassDispatch, "                ");
        // 元素赋值前发射 Before（通用索引路径插入点）
        AppendBeforeMappingCall(sb, mapping, "                ");
        GeneratePropertyAssignments(sb, mapping, "                ", typeNameMap, skipInitOnly: true, ctx, methodSuffix);
        // ListInto 通用索引路径携带 AfterMapping
        AppendAfterMappingCall(sb, mapping, "                ");
        sb.AppendLine($"                action?.Invoke(src, dst);");
        sb.AppendLine($"            }}");

        if (isSelfRecursive)
        {
            // 自引用递归：finally 归还深度计数（异常路径也归还，避免残留）
            sb.AppendLine($"            }}");
            sb.AppendLine($"            finally");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                --s_depth_{methodName};");
            sb.AppendLine($"            }}");
        }
        sb.AppendLine($"        }}");
        sb.AppendLine();
    }

    /// <summary>
    /// MapList 元素级分派发射（族 4——四个元素路径共用：
    /// 数组/ICollection/IEnumerable/带 Action 重载）。null 元素跳过后、既有内联赋值前插入——
    /// 命中即 list.Add(子类 Core 产物) + continue；未命中顺序落入既有内联基类赋值（兜底）。
    /// 元素新建目标 → dst 固定型不适用（仅源侧 isinst，与逐元素走 Core 分派语义等价）。
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="subclassDispatch">分派链（null/空 = 无登记零发射）</param>
    /// <param name="indent">当前缩进</param>
    /// <param name="withAction">带 Action 重载形态（先映射 → action(src, dst) → Add，语义同构）</param>
    private static void EmitMapListElementDispatch(
        StringBuilder sb, List<SubclassDispatchEntry>? subclassDispatch, string indent, bool withAction)
    {
        if (subclassDispatch == null) return;
        for (var i = 0; i < subclassDispatch.Count; i++)
        {
            var d = subclassDispatch[i];
            if (withAction)
            {
                sb.AppendLine($"{indent}if (src is {d.MatchTypeFullName} sub_{i})");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    var subDst_{i} = {d.CoreMethodName}(sub_{i});");
                sb.AppendLine($"{indent}    action(src, subDst_{i});");
                sb.AppendLine($"{indent}    list.Add(subDst_{i});");
                sb.AppendLine($"{indent}    continue;");
                sb.AppendLine($"{indent}}}");
            }
            else
            {
                sb.AppendLine($"{indent}if (src is {d.MatchTypeFullName} sub_{i})");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    list.Add({d.CoreMethodName}(sub_{i}));");
                sb.AppendLine($"{indent}    continue;");
                sb.AppendLine($"{indent}}}");
            }
        }
    }

    /// <summary>
    /// MapListInto 元素级双实例匹配分派发射（族 5
    /// 数组快速路径与通用索引循环两路径同构共用）。dst null 检查后、Before 之前插入——
    /// src isinst &amp;&amp; dst isinst 全匹配 → 复用子类 Update（action 逆变透传）+ continue；
    /// 不全匹配 → 既有就地基类赋值（dst 固定型残留面，F50③ Info 可见）。
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="subclassDispatch">分派链（null/空 = 无登记零发射）</param>
    /// <param name="indent">当前缩进</param>
    private static void EmitListIntoElementDispatch(
        StringBuilder sb, List<SubclassDispatchEntry>? subclassDispatch, string indent)
    {
        if (subclassDispatch == null) return;
        for (var i = 0; i < subclassDispatch.Count; i++)
        {
            var d = subclassDispatch[i];
            sb.AppendLine($"{indent}if (src is {d.MatchTypeFullName} sub_{i} && dst is {d.TargetTypeFullName} subDst_{i})");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    {d.UpdateMethodName}(sub_{i}, subDst_{i}, action);");
            sb.AppendLine($"{indent}    continue;");
            sb.AppendLine($"{indent}}}");
        }
    }

    /// <summary>
    /// 构造器参数匹配公共逻辑 — 遍历构造器参数，匹配源属性并生成类型转换代码
    /// </summary>
    /// <param name="mapping">映射信息</param>
    /// <param name="ctor">目标类型构造器</param>
    /// <returns>构造器参数表达式列表</returns>
    private static List<string> BuildConstructorArgs(MappingSnapshot mapping, ConstructorRef ctor)
    {
        var ctorArgs = new List<string>();
        foreach (var param in ctor.Parameters)
        {
            var matched = false;
            // 显式绑定优先——绑定表命中替代同名比对（谓词变化）；
            // SourcePropertyName null（未用 MapCtorParam）恒 false——产物与现状逐字节等价
            if (TryBuildBoundConstructorArg(mapping, param, out var boundArg))
            {
                ctorArgs.Add(boundArg);
                continue; // 绑定已产实参——跳过同名匹配与 default 回落
            }
            foreach (var pm in mapping.PropertyMappings)
            {
                // 反向写回属性由赋值路径生成（写链+null 创建），不参与构造器参数匹配
                if (pm.IsReverseWriteBack) continue;

                if (System.StringComparer.OrdinalIgnoreCase.Equals(pm.Target.Name, param.Name))
                {
                    // [MapIgnore] 方向控制 — 忽略的属性不匹配构造器参数，走默认值路径
                    if (ShouldSkipForDirectionPair(pm, IsToDtoMapping(mapping))) break;

                    var paramType = param.Type.FullName;
                    // [Convert] 无源属性（SourceProperty == null）时，
                    // 参数类型不依赖源属性——方法引用形态渲染 R6-b 静态调用（T1 M(default)/T2 M(src)/
                    // T3 M(src, default)/T0 M()），接口形态（退役期）直接 default(参数类型)，
                    // 避免 NRE 被 per-mapping try/catch 吞掉报 NCMAP099 导致整个映射族丢失。
                    if (pm.Source == null)
                    {
                        // 守卫三元（条件假 → 构造器参数显式 default）；T1 无源已由
                        // 物化期 F43 门拦截不附着守卫，此处仅 T2（整源）可包裹
                        ctorArgs.Add(ApplyWhenToConstructorArg(pm, pm.ConverterDeclaringType != null
                            ? BuildStaticConverterCall(pm.ConverterDeclaringType, pm.ConverterMethodName!,
                                pm.ConverterShape, null, "src")
                            : $"default({paramType})", param.Type));
                        matched = true;
                        break;
                    }
                    var srcAccess = BuildSourceAccess(pm);
                    var srcPropType = pm.Source!.Type.FullName;

                    // 守卫三元（条件假 → 构造器参数显式 default；
                    // 表达式列表上下文无法 if 化/局部变量承载，三元形态—— 风险点②）
                    if (srcPropType == paramType)
                        ctorArgs.Add(ApplyWhenToConstructorArg(pm, $"{srcAccess}", param.Type));
                    else if (TypeRefHelpers.IsImplicitConvertible(pm.Source!.Type, param.Type))
                        ctorArgs.Add(ApplyWhenToConstructorArg(pm, $"({paramType}){srcAccess}", param.Type));
                    else
                        ctorArgs.Add(ApplyWhenToConstructorArg(pm, $"NCMapper.TypeConvertHelper.ConvertValue<{srcPropType}, {paramType}>({srcAccess})!", param.Type));

                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                if (param.HasExplicitDefaultValue)
                    ctorArgs.Add(FormatExplicitDefault(param.ExplicitDefaultValue, param.Type));
                else if (param.Type.IsReferenceType)
                    ctorArgs.Add("null");
                else
                    ctorArgs.Add($"default({param.Type.FullName})");
            }
        }
        return ctorArgs;
    }

    private static bool GenerateConstructorMapping(StringBuilder sb, MappingSnapshot mapping, string targetName, IReadOnlyList<string>? initOnlyMembers = null, string methodSuffix = "", GenerationContext? ctx = null)
    {
        // 选器单点（加权 + advisory——helper 内 AddDistinct 去重）；
        // 绑定集合为空时与既有 Where(IsPublic).OrderByDescending(Count).FirstOrDefault() 等价
        var ctor = SelectTargetConstructor(mapping, ctx);

        if (ctor == null)
        {
            // 无公共构造器 → 生成期直接抛 MapConfigurationException（方案甲，返回 true 表示已 throw）
            // 不可回退公共门面 Mapper.Map：该类型对的生成方法已注册为 GeneratedCache.CoreFunc，
            // 门面命中自身 → 无限互递归 → StackOverflow（崩溃根因）；
            // 行为对齐运行时 MapperBase 路径（抛 MapConfigurationException），双路径同调
            // ErrorMessages.NoPublicConstructor（ErrorMessages 已 public，生成代码可引用）
            sb.AppendLine($"            throw new NCMapper.MapConfigurationException(");
            sb.AppendLine($"                NCMapper.ErrorMessages.NoPublicConstructor(typeof({targetName}).Name),");
            sb.AppendLine($"                typeof({mapping.SourceType.FullName}), typeof({targetName}));");
            return true;
        }
        if (ctor.Parameters.Count == 0)
        {
            // 有无参公共构造器 → 直接构造（原回退 Mapper.Map 会命中自身注册项递归；
            // new T() 语义等价且无递归风险；后续属性赋值路径照常生成）
            // Q7无参构造 + init-only 成员 → 对象初始化器补充赋值
            sb.AppendLine($"            var dst = new {targetName}{BuildInitializerSuffix(initOnlyMembers)};");
            return false;
        }

        var ctorArgs = BuildConstructorArgs(mapping, ctor);
        // Q7构造器参数 + init-only 成员对象初始化器（C# 允许 new T(args) { Prop = expr }）
        sb.Append($"            var dst = new {targetName}(");
        sb.Append(string.Join(", ", ctorArgs));
        sb.AppendLine($"){BuildInitializerSuffix(initOnlyMembers)};");
        return false;
    }

    /// <summary>
    /// 生成属性映射代码（逐属性赋值，被 GeneratePropertyMappingSnapshot 和 GenerateInlineElement 共用）
    /// </summary>
    private static void GeneratePropertyMapping(StringBuilder sb, MappingSnapshot mapping, string targetName, Dictionary<TypeRef, string> typeNameMap, GenerationContext? ctx = null, string methodSuffix = "")
    {
        // 声明 BeforeMapping 的对在分支①（对象初始化器——构造与赋值不可分）无 Before 插入点
        // 无 init-only 属性 → 降级分支③"new + 逐语句赋值"形态（首条赋值前发射）；含 init-only 属性 → F37（原发射保留，钩子不发射）；未声明恒分支①（G3 逐字节不变）
        bool beforeHook = !string.IsNullOrEmpty(mapping.BeforeMappingMethodRef);
        if (TryBuildSimpleAssignments(mapping, out var assignments, methodSuffix) && assignments.Count > 0 && !(beforeHook && !HasInitOnlyTargetProperty(mapping)))
        {
            ReportBeforeMappingUninsertableIfNeeded(ctx, mapping);
            // object initializer 语法同时支持公共 setter 和 init-only setter
            sb.AppendLine($"            var dst = new {targetName}");
            sb.AppendLine($"            {{");
            for (int i = 0; i < assignments.Count; i++)
            {
                var trailing = i < assignments.Count - 1 ? "," : "";
                sb.AppendLine($"                {assignments[i]}{trailing}");
            }
            sb.AppendLine($"            }};");
        }
        else if (HasInitOnlyTargetProperty(mapping))
        {
            // 存在 init-only 属性且简单赋值路径失败（含复杂映射）→ 构造器参数匹配 + 对象初始化器
            // init-only 属性只能通过 object initializer 或构造器参数赋值。
            // Q7现状"构造器未覆盖的 init-only 属性保持默认值"
            // 对全 init-only 目标（如 CleanArchitecture TodoListDto：所有属性 init + 无参构造）产出空方法体
            // （仅 new + return，无属性赋值）→ MapList 走 init-only 运行时回退 Mapper.Map →
            // 双向导航（TodoItem.List/TodoList.Items）反射映射无循环防护 → StackOverflow。
            // 构造器路径追加对象初始化器（object initializer 支持 init setter），
            // 可表达式化的 init-only 属性（简单/嵌套注册/集合注册/转换）在初始化器中赋值；
            // 无法表达式化的 init-only 属性（复杂多语句分支）保持默认值 + Info 诊断。
            var initMembers = BuildInitOnlyInitializerMembers(mapping, typeNameMap, ctx);
            GenerateConstructorMapping(sb, mapping, targetName, initMembers, ctx: ctx);
            // 分支②插入点——构造后、非 init-only 属性赋值前发射 Before
            AppendBeforeMappingCall(sb, mapping, "            ");
            // 非 init-only 属性仍可通过 dst.Prop = value 赋值
            GeneratePropertyAssignments(sb, mapping, "            ", typeNameMap, skipInitOnly: true, ctx, methodSuffix);
        }
        else
        {
            sb.AppendLine($"            var dst = new {targetName}();");
            // 分支③插入点——首条属性赋值前发射 Before（分支①降级对同经此处）
            AppendBeforeMappingCall(sb, mapping, "            ");
            // 第三站点补传 methodSuffix——冲突后缀场景掩码字段
            // 引用/声明名一致（与 :719/:108/:215 等既有传参站点同口径）
            GeneratePropertyAssignments(sb, mapping, "            ", typeNameMap, ctx: ctx, methodSuffix: methodSuffix);
        }
    }

    /// <summary>
    /// 构建 init-only 属性的对象初始化器成员
    /// <para>对 init-only 目标属性生成 "Prop = expr" 成员，供 GenerateConstructorMapping 的对象初始化器使用。
    /// 过滤与 GeneratePropertyAssignments 一致（[MapIgnore] 方向/可访问性/类型完全不相容跳过）；
    /// 无法表达式化的属性（复杂多语句分支：枚举 switch/字典/Json/Mask/Convert）跳过并登记 Info 诊断。</para>
    /// </summary>
    /// <param name="mapping">映射信息</param>
    /// <param name="typeNameMap">类型到"类名+后缀"映射表</param>
    /// <param name="ctx">生成上下文（诊断收集；可为 null）</param>
    /// <returns>对象初始化器成员列表（"Prop = expr" 形式）</returns>
    private static List<string> BuildInitOnlyInitializerMembers(MappingSnapshot mapping, Dictionary<TypeRef, string> typeNameMap, GenerationContext? ctx)
    {
        var members = new List<string>();
        foreach (var pm in mapping.PropertyMappings)
        {
            // 仅 init-only 目标属性
            if (pm.Target.IsInitOnly != true) continue;

            // [MapIgnore] 方向控制（与 GeneratePropertyAssignments 同判）
            if (ShouldSkipForDirectionPair(pm, IsToDtoMapping(mapping))) continue;

            // 可访问性守卫（CS0122 防护，NCMAP028——与标准路径同判）
            var (accessible, _) = IsAccessiblePair(pm);
            if (!accessible) continue;

            // 类型完全不相容跳过（NCMAP027——与标准路径同判；显式映射保留；：有效源类型）
            var effectiveSrcType = GetEffectiveSourceType(pm);
            if (effectiveSrcType != null
                && !IsExplicitlyMapped(pm)
                && !TypeRefHelpers.HasMappingPath(effectiveSrcType, pm.Target.Type))
            {
                continue;
            }

            // W3_1 （C2a 决策前移）：决策已入准备段 Plan（发射期零判定），此处查表 + 纯渲染
            var expr = RenderInitOnlyAssignmentExpr(pm, ResolveInitOnlyExprPlan(pm, ctx, typeNameMap));
            if (expr == null)
            {
                // 无法表达式化的 init-only 属性 → 保持默认值 + Info 诊断（记录差异，不静默）
                if (ctx != null)
                {
                    // 实参同源矫正——resx NCMAP027 第三/四槽语义为
                    // {1}=源类型、{2}=目标类型（消息体 "source type '{1}' and target type '{2}'"）。
                    // 原 Args 第二槽塞目标类型、第三槽塞中文明文 → 渲染 source/target 同值双槽 + 中文混入。
                    // 与既有口径一致（effective 源类型优先，null 回落源根类型）。
                    ctx.Diagnostics.Add(mapping, new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.SkippedIncompatibleMemberRule.Id,
                        Args = new[]
                        {
                            pm.Target.Name,
                            GetEffectiveSourceType(pm)?.FullName ?? mapping.SourceType.FullName,
                            pm.Target.Type.FullName,
                        },
                        Location = pm.Target.Location,
                    });
                }
                continue;
            }

            // 守卫三元（条件假 → init-only 成员显式 default；
            // 对象初始化器成员为赋值上下文表达式，三元形态）
            // 初始化器成员名 @ 转义（对象初始化器目标属性名引用）
            members.Add($"{SourceGenEscape.Identifier(pm.Target.Name)} = {ApplyWhenToInitOnlyExpr(pm, expr)}");
        }
        return members;
    }

    /// <summary>
    /// 查询 init-only 表达式化计划（W3_1 C2a 决策前移）— 准备段 Plan 表查表（正常路径）；
    /// miss 兜底现场决策（防御不变量：Plan 覆盖面 = 正向 + 反向全部 init-only pm，理论不可达；
    /// ctx 未挂载/表未含该 pm 时兜底，保证任意调用上下文行为等价）。
    /// </summary>
    /// <param name="pm">属性映射信息</param>
    /// <param name="ctx">生成期上下文（Preparation 通道；可为 null）</param>
    /// <param name="typeNameMap">类型到"类名+后缀"映射表（兜底决策用）</param>
    /// <returns>表达式化路径计划</returns>
    private static InitOnlyExprPlan ResolveInitOnlyExprPlan(PropertyMappingSnapshot pm, GenerationContext? ctx, Dictionary<TypeRef, string> typeNameMap)
    {
        var plans = ctx?.Preparation?.InitOnlyExprPlans;
        if (plans != null && plans.TryGetValue(pm, out var plan))
            return plan;
        // 兜底现场决策路径同传对级后缀表（ctx 通道）；B1-13-兜底路径 mapping 键无源
        // （null → 枚举冲突诊断不发射，理论不可达契约——注释声明）
        return GenerationPreparation.PlanInitOnlyExpr(pm, typeNameMap, ctx?.Preparation?.MethodSuffixMap, ctx, null);
    }

    /// <summary>
    /// 渲染 init-only 属性映射表达式（W3_1 C2a 决策前移）— 按 Plan.Kind 纯模板渲染，
    /// 零判定；模板与原 BuildInitOnlyAssignmentExpr 各分支逐字符一致（判定链已前移
    /// GenerationPreparation.PlanInitOnlyExpr，行为等价由逐字节基线锁定）。
    /// <para>覆盖可单表达式化的映射分支（同类型/隐式转换/Nullable 同底层/嵌套注册/嵌套回退/集合注册/枚举）；
    /// 复杂多语句分支（字典/Json/Mask/Convert/未注册集合内联）Plan 为 NotExpressible 返回 null 降级。</para>
    /// </summary>
    /// <param name="pm">属性映射信息</param>
    /// <param name="plan">表达式化路径计划（准备段产物或兜底决策）</param>
    /// <returns>映射表达式字符串；无法表达式化返回 null</returns>
    private static string? RenderInitOnlyAssignmentExpr(PropertyMappingSnapshot pm, InitOnlyExprPlan plan)
    {
        if (plan.Kind == InitOnlyExprKind.NotExpressible) return null;
        var srcAccess = BuildSourceAccess(pm);
        switch (plan.Kind)
        {
            case InitOnlyExprKind.SameTypeDirect:
            case InitOnlyExprKind.NullableSameUnderlying:
                return srcAccess;

            case InitOnlyExprKind.ImplicitCast:
                return $"({pm.Target.Type.FullName}){srcAccess}";

            case InitOnlyExprKind.NestedRegistered:
                return $"GeneratedMapper.{plan.RegisteredMethodName}({srcAccess})!";

            case InitOnlyExprKind.NestedRuntimeFallback:
                return $"{srcAccess} == null ? null : NCMapper.Mapper.Map<{plan.RuntimeMapTypeParams}>({srcAccess})";

            case InitOnlyExprKind.CollectionRegistered:
                return $"GeneratedMapper.{plan.RegisteredMethodName}({srcAccess})";

            case InitOnlyExprKind.Enum:
                return plan.EnumExpr;

            case InitOnlyExprKind.Format:
                // 直接表达式化（src.Prop?.ToString("fmt", null)
                // 纯单表达式——探针 1/2 实证形态；可空 ?. 编译器自动生成 Condition 等价结构）
                var effFmtSrc = GetEffectiveSourceType(pm)!;
                return BuildFormattedAssignment(
                    srcAccess, pm.FormatString!, IsNullableFormattedAccess(effFmtSrc), IsEnumFormattedSource(effFmtSrc));

            default:
                return null;
        }
    }

    /// <summary>
    /// 尝试构建枚举转换表达式（init-only 目标专用）
    /// <para>判定改指共享内核 ClassifyEnumConversion
    ///（CodeGenerator.Conversion.cs——分支选择/退化检测①②/可空性/冲突集合单点）；
    /// 表达式构造形态（单行 join）留本薄壳。与语句版同判定同表达式语义——5 分支表达式化：
    /// enum→string switch / 别名冲突 ToString 退避 / string→enum switch+throw / 大小写冲突精确匹配 / enum→enum 强转；
    /// 退避分支生成表达式与语句版逐字一致（映射结果语义相同，验收 S8 用例矩阵 + 生成文本对比断言）。
    /// 退化诊断 NCMAP023/024 表达式版预存不发射——移 B5-4 预登记。
    /// internal 化供 GenerationPreparation 决策期枚举表达式预计算调用（逻辑逐行不动）。</para>
    /// </summary>
    /// <param name="pm">属性映射信息</param>
    /// <param name="srcAccess">源属性访问表达式</param>
    /// <param name="ctx">生成期上下文（B1-13-R1——枚举冲突退化诊断发射；null = 兜底路径契约不发射）</param>
    /// <param name="mapping">所属映射快照（诊断键；null 不发射——Compose 等无 mapping 上下文路径）</param>
    /// <returns>枚举转换表达式字符串；非枚举转换场景返回 null</returns>
    internal static string? TryBuildEnumConversionExpr(PropertyMappingSnapshot pm, string srcAccess, GenerationContext? ctx = null, MappingSnapshot? mapping = null)
    {
        // B1-13 判定内核单点（发射形态留本薄壳：单行表达式 join）
        var facts = ClassifyEnumConversion(pm.Source!.Type, pm.Target.Type);
        if (facts == null)
            return null;

        // B1-13-枚举冲突退化诊断（表达式路径）——镜像语句版
        // Conversion.cs 形态（Args=[EnumTypeStr]，Location=目标属性）；ctx == null =
        // 兜底路径契约（理论不可达，不发射——ResolveInitOnlyExprPlan 兜底无 mapping 键源）；
        // 双报防护 X6 互斥论证：诊断决策期单点发射（发射端按 Plan.Kind 纯模板渲染零判定，
        // 语句版枚举分支不再调本函数）→ 同 pm 恒单次
        if ((facts.HasNameCollision || facts.HasValueCollision) && ctx != null && mapping != null)
        {
            if (facts.HasNameCollision)
            {
                ctx.Diagnostics.Add(mapping, new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.EnumNameCollisionDegradedRule.Id,
                    Args = new[] { facts.EnumTypeStr },
                    Location = pm.Target.Location,
                });
            }
            if (facts.HasValueCollision)
            {
                ctx.Diagnostics.Add(mapping, new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.EnumValueCollisionDegradedRule.Id,
                    Args = new[] { facts.EnumTypeStr },
                    Location = pm.Target.Location,
                });
            }
        }

        // enum → string
        if (facts.Kind == EnumConversionKind.EnumToString)
        {
            // 别名冲突（数值重复成员）退避：与语句版一致 ToString() 调用
            if (facts.HasValueCollision)
            {
                return $"{srcAccess}.ToString()";
            }

            var arms = new List<string>();
            foreach (var (name, _) in facts.EnumValues)
            {
                arms.Add($"{facts.EnumTypeStr}.{name} => \"{name}\"");
            }
            // 可空输入：switch 表达式 null 输入命中 _ 兜底 → ToString() 结果与语句版一致
            arms.Add(facts.SrcIsNullable
                ? $"_ => {srcAccess}?.ToString() ?? \"\""
                : $"_ => {srcAccess}.ToString()");
            return $"{srcAccess} switch {{ {string.Join(", ", arms)} }}";
        }

        // string → enum
        if (facts.Kind == EnumConversionKind.StringToEnum)
        {
            // 非法值统一抛 MapException（主体文本内核单点——B1-13；表达式壳直接使用，无缩进无尾逗号）
            var throwBranch = BuildEnumThrowBranchCore(facts.EnumTypeStr);

            if (facts.HasNameCollision)
            {
                // 退化路径：精确名称匹配（保留大小写敏感现状）+ null 分支 + default 抛 MapException
                //（空串不再落 default——A1-08 契约：落 throw 分支，与运行时同口径）
                var arms = new List<string> { $"null => {facts.EmptyDefaultLiteral}" };
                foreach (var (name, _) in facts.EnumValues)
                {
                    arms.Add($"\"{name}\" => {facts.EnumTypeStr}.{name}");
                }
                arms.Add(throwBranch);
                return $"{srcAccess} switch {{ {string.Join(", ", arms)} }}";
            }

            // 正常路径：全小写标签 + 数字串标签 + null 分支（null → default 与运行时 ConvertValue(null) 一致；
            // 空串不再独立落位——落 _ 分支抛 MapException，对齐运行时 A1-08 空串抛口径）
            var normalArms = new List<string> { $"null => {facts.EmptyDefaultLiteral}" };
            foreach (var (name, value) in facts.EnumValues)
            {
                normalArms.Add($"\"{name.ToLowerInvariant()}\" => {facts.EnumTypeStr}.{name}");
                // 数字串标签：数值互异时逐成员发射；数值重复时数字串维度已退化（仅保留精确名称标签）
                if (!facts.HasValueCollision)
                {
                    normalArms.Add($"\"{value}\" => {facts.EnumTypeStr}.{name}");
                }
            }
            normalArms.Add(throwBranch);
            return $"{srcAccess}?.ToLowerInvariant() switch {{ {string.Join(", ", normalArms)} }}";
        }

        // enum → enum（不同枚举类型，按值强转；主体文本内核单点——B1-13）
        return BuildEnumCastCore(facts.TgtUnderlying, srcAccess);
    }

    /// <summary>
    /// 检测映射是否存在自引用递归（对象嵌套或集合元素对 == mapping 自身）
    /// <para>范围扩大生成 Map/MapList 对自引用结构
    /// （树形 DTO 如 Category.Children / SelfRefNode.Next）直接递归且无深度保护——
    /// 运行时 MapperBase 有 s_depthIn/s_depthOut 双计数器保护，生成侧没有 → 成环数据栈溢出崩溃
    /// （实测 Category MapList / SelfRefNode Map 测试进程 Stack overflow）。递归方法生成
    /// ThreadStatic 深度字段 + 入口检查 + try-finally（仅递归方法生成，常规映射零开销）。</para>
    /// </summary>
    /// <param name="mapping">映射信息</param>
    /// <returns>存在自引用递归返回 true</returns>
    private static bool IsSelfRecursiveMapping(MappingSnapshot mapping)
    {
        // 判定结果随 mapping 缓存（lazy 字段）——三调用点
        // Core/Update/MapList（:28/:234/:397）对同一 mapping 重复全属性遍历，首算复用；
        // 正反方向共用（判定纯函数：同 mapping 同结果）；消费域 = 单线程编译器管线
        if (mapping.CachedIsSelfRecursive is bool cached) return cached;
        var result = mapping.PropertyMappings.Any(pm =>
            pm.Source != null && pm.Target != null
            && ((TypeRefHelpers.TryGetCollectionElementTypes(pm.Source!.Type, pm.Target.Type, out var elemSrcSym, out var elemTgtSym)
                    && TypeRefHelpers.IsSameType(elemSrcSym, mapping.SourceType)
                    && TypeRefHelpers.IsSameType(elemTgtSym, mapping.TargetType))
                || (TypeRefHelpers.IsSameType(pm.Source!.Type, mapping.SourceType)
                    && TypeRefHelpers.IsSameType(pm.Target.Type, mapping.TargetType))));
        mapping.CachedIsSelfRecursive = result;
        return result;
    }

    /// <summary>
    /// 判断映射方向是否为 ToDto（Entity→DTO）— 依据目标类型是否标注 [MapFrom]
    /// <para> 范围扩大：生成侧 [MapIgnore] 方向判定由硬编码 toDto:true
    /// 改为按目标 [MapFrom] 推导（与运行时 PropertyMatcher.GetPropertyList 的
    /// isToDto = targetType.IsDefined([MapFrom]) 对齐）——修复反向（DTO→Entity）时源侧
    /// [MapIgnore(ToDto)] 被错误跳过（实测 SgDirection_ToEntity 分叉）。
    /// Fluent 无 [MapFrom]——IsFluentForward 标记补足
    ///（[MapFrom] 通道恒 false，additive 字节不变）</para>
    /// </summary>
    private static bool IsToDtoMapping(MappingSnapshot mapping)
        => mapping.TargetType.MapFromSources.Count > 0 || mapping.IsFluentForward;

    /// <summary>
    /// 检查属性对（源/目标）是否在指定方向应被跳过 — [MapIgnore] 方向控制
    /// <para> 范围扩大：生成侧源侧 [MapIgnore] 补齐
    /// 既有实现只查目标属性（ShouldSkipForDirection(pm.Target)），源属性上的
    /// [MapIgnore]（Both/ToDto）在运行时 PropertyIgnoreChecker 双侧判定生效、生成侧不处理
    /// （实测 MapIgnore_On_Source_Side 分叉）。双侧判定与运行时 GetPropertyList 源侧过滤对齐；
    /// [Convert] 无源属性场景（SourceProperty=null）判空跳过源侧。</para>
    /// </summary>
    /// <param name="pm">属性映射信息</param>
    /// <param name="toDto">是否 ToDto 方向（由 IsToDtoMapping 按目标 [MapFrom] 推导）</param>
    /// <returns>源或目标属性在当前方向应被忽略返回 true</returns>
    private static bool ShouldSkipForDirectionPair(PropertyMappingSnapshot pm, bool toDto)
    {
        // T4 源腿退役：双腿 OR 退化单腿（源侧 PropertyRef 不再承载
        // HasMapIgnore/MapIgnoreDirection—— ④ v1.2；目标腿单查语义不变）
        return ShouldSkipForDirection(pm.Target, toDto);
    }

    /// <summary>
    /// 生成属性映射代码（逐属性赋值，被 GeneratePropertyMappingSnapshot 和 GenerateInlineElement 共用）
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="indent">缩进字符串</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表，用于嵌套映射引用</param>
    /// <param name="skipInitOnly">是否跳过 init-only 属性（构造器路径回退时为 true，避免生成无法编译的 dst.Prop = value）</param>
    private static void GeneratePropertyAssignments(StringBuilder sb, MappingSnapshot mapping, string indent, Dictionary<TypeRef, string> typeNameMap, bool skipInitOnly = false, GenerationContext? ctx = null, string methodSuffix = "")
    {
        foreach (var pm in mapping.PropertyMappings)
        {
            // init-only 属性无法用 dst.Prop = value 赋值，构造器路径回退时跳过
            if (skipInitOnly && pm.Target.IsInitOnly == true)
            {
                // G1（NCMAP032）：更新期 init-only 跳过零诊断补齐；
                // 多路径复用（更新路径/构造器回退路径均 skipInitOnly: true）按 (mapping, 属性) 去重
                if (ctx != null)
                {
                    ctx.Diagnostics.AddDistinct(mapping, new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.InitOnlyUpdateSkipRule.Id,
                        Args = new[] { pm.Target.Name, mapping.SourceType.FullName, mapping.TargetType.FullName },
                        Location = pm.Target.Location,
                    });
                }
                continue;
            }

            // [MapIgnore] 方向控制 — 正向(ToDto)跳过 Direction.ToDto/Both 的属性（双侧判定， 源侧补齐）
            if (ShouldSkipForDirectionPair(pm, IsToDtoMapping(mapping))) continue;

            // 可访问性守卫（CS0122 防护，NCMAP028）
            // 生成代码仅访问 public：源/目标属性及其容器类型任一非 public → 跳过 + Info 诊断。
            // 判空：pm.Source 为 null（[Convert] 无匹配源属性）跳过源侧检查
            var (accessible, offender) = IsAccessiblePair(pm);
            if (!accessible)
            {
                // 追加诊断改 per-run collector（缓存 MappingSnapshot 只读）；ctx 为 null 时静默（兼容无上下文调用）
                if (ctx != null)
                {
                    ctx.Diagnostics.Add(mapping, new DiagnosticSnapshot
                    {
                        DescriptorId = DiagnosticDescriptors.InaccessibleMemberRule.Id,
                        Args = new[]
                        {
                            pm.Target.Name,
                            offender is PropertyRef offProp ? offProp.Name
                                : offender is TypeRef offType ? offType.SimpleName
                                : pm.Target.Name,
                            offender is PropertyRef offProp2 ? offProp2.DeclaredAccessibility.ToString()
                                : offender is TypeRef offType2 ? offType2.Accessibility.ToString()
                                : "?",
                        },
                        Location = pm.Target.Location,
                    });
                }
                continue;
            }

            // T2/枚举退化诊断收集至 mapping.Diagnostics（EmitDiagnostics 统一 Report）
            // 反向路径写回属性（DTO 扁平 → 实体嵌套路径）走专用生成分支
            //（条件守卫不适用——反向写回 pm 由 ReversePlanner 派生，条件四字段不携带即剥离，D3）
            if (pm.IsReverseWriteBack)
            {
                GenerateReversePathWriteBack(sb, pm, indent, typeNameMap, mapping, ctx);
                continue;
            }
            // 赋值层守卫包裹（双包裹点之一）；
            // 无守卫直通（GenerateSinglePropertyAssignment 原样调用，字节不变门）
            EmitGuardedSinglePropertyAssignment(sb, pm, indent, typeNameMap, methodSuffix, mapping, ctx);
        }
    }

    /// <summary>
    /// 可访问性守卫判定
    /// <para>源/目标属性及其容器类型任一 DeclaredAccessibility != Public 返回 false。
    /// pm.Source 为 null（[Convert] 无匹配源属性）跳过源侧检查（
    /// 该场景由方法引用三字段受控分支处理）。</para>
    /// </summary>
    /// <param name="pm">属性映射信息</param>
    /// <returns>可访问返回 true；不可访问时返回 (false, 不可访问的成员符号)</returns>
    private static (bool Ok, object? Offender) IsAccessiblePair(PropertyMappingSnapshot pm)
    {
        if (pm.Target != null)
        {
            if (!TypeRefHelpers.IsAccessibleFromGeneratedCode(pm.Target.DeclaredAccessibility))
                return (false, pm.Target);
            if (pm.Target.ContainingType != null
                && !TypeRefHelpers.IsAccessibleFromGeneratedCode(pm.Target.ContainingType.Accessibility))
                return (false, pm.Target);
        }

        if (pm.Source != null)
        {
            if (!TypeRefHelpers.IsAccessibleFromGeneratedCode(pm.Source.DeclaredAccessibility))
                return (false, pm.Source);
            if (pm.Source.ContainingType != null
                && !TypeRefHelpers.IsAccessibleFromGeneratedCode(pm.Source.ContainingType.Accessibility))
                return (false, pm.Source);
        }

        return (true, null);
    }

    /// <summary>
    /// 判定属性是否为显式映射（边界）
    /// <para>[MapProperty]（FromField/FromSource/IsFromJson）或 [Convert]/[ConvertBack] 标注的属性属显式映射，
    /// 代表开发者明确意图——不参与"类型完全不相容自动跳过"（保留既有 NCMAP002 fallback / IsFromJson /
    /// 转换器路径语义），仅 [MapFrom] 同名自动匹配参与跳过。
    /// [ConvertBack] 与 [Convert] 同族：特性标在 DTO 属性上，正向快照中 DTO=Target、反向快照中 DTO=Source，双向均甄别。</para>
    /// </summary>
    /// <param name="pm">属性映射信息</param>
    /// <returns>源或目标属性带 [MapProperty]/[Convert]/[ConvertBack] 标注返回 true</returns>
    private static bool IsExplicitlyMapped(PropertyMappingSnapshot pm)
    {
        if (pm.Target != null && (pm.Target.HasMapProperty || pm.Target.HasConvertUsing || pm.Target.HasConvertBackUsing))
            return true;

        if (pm.Source != null && (pm.Source.HasMapProperty || pm.Source.HasConvertUsing || pm.Source.HasConvertBackUsing))
            return true;

        return false;
    }

    /// <summary>
    /// 为单个属性生成赋值代码 — 按类型分发到不同转换路径
    /// <para><b>Convert 优先原则</b>：[Convert] 自定义转换器优先于所有其他转换路径，
    /// 必须最先判断。原因：[Convert] 无匹配源属性场景下 <c>pm.Source</c> 为 null!，
    /// 后续 <c>pm.Source!.Type</c> 访问会抛 NullReferenceException。
    /// 前置此分支后，走到后续代码时 SourceProperty 必非 null。</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="pm">属性映射信息</param>
    /// <param name="indent">缩进字符串</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表</param>
    /// <param name="methodSuffix">方法名后缀（同名类型冲突消歧）</param>
    /// <param name="diagnostics">诊断收集列表（T2/F-01 枚举退化诊断；无 mapping 上下文路径传 null）</param>
    /// <summary>
    /// 脱敏赋值模板单点：收敛 4 处 [Mask] 包装赋值重复
    /// （Convert+Mask / 纯 Mask / 同类型包装 / 隐式转换包装），生成 "Prop = maskField.Mask(expr)" 片段；
    /// 语句形态（sb 路径）由调用处补 "dst." 前缀与 ";" 后缀。
    /// </summary>
    private static string BuildMaskedAssignment(string propName, string maskFieldName, string maskedExpr)
        => $"{SourceGenEscape.Identifier(propName)} = {maskFieldName}.Mask({maskedExpr})";

    /// <summary>
    /// 格式化赋值模板单点（照
    /// BuildMaskedAssignment 单点收敛先例）：生成 "{access}{?.|.}ToString({Literal(format)}, null)"。
    /// <para>nullableAccess=true 时以 "?." 空条件调用发射（null 源 → 目标 null；C# 编译器自动
    /// 生成 Condition(HasValue, …) 等价结构——探针 1 实证形态）；枚举源（isEnumSource=true）发
    /// 单参 ToString(fmt)——.NET 10 起 Enum.ToString(String, IFormatProvider) 标记 [Obsolete]
    /// （provider 参数本就不用于枚举），双参形态在 warnaserror 工程触发 CS0618（CI 实证），
    /// 单参/双参行为等价（枚举格式 G/D/X 与 provider 无关——探针 2 实证三 TFM 一致）；
    /// 非枚举源 provider 恒 null（= 当前文化，与未标注属性恒 Invariant 的转换路径分叉为有意设计）。</para>
    /// </summary>
    private static string BuildFormattedAssignment(string access, string format, bool nullableAccess, bool isEnumSource = false)
        => $"{access}{(nullableAccess ? "?." : ".")}ToString({SourceGenEscape.Literal(format)}{(isEnumSource ? "" : ", null")})";

    /// <summary>
    /// 格式化源是否枚举（含 Nullable&lt;枚举&gt; 解包判定）——枚举源发单参 ToString(fmt)
    /// （规避 .NET 10 Enum.ToString(String, IFormatProvider) 废弃注记的 CS0618）。
    /// </summary>
    private static bool IsEnumFormattedSource(TypeRef effectiveSrcType)
    {
        var t = TypeRefHelpers.IsNullableValueType(effectiveSrcType)
            ? TypeRefHelpers.GetUnderlyingType(effectiveSrcType)
            : effectiveSrcType;
        return t != null && t.Kind == TypeKind.Enum;
    }

    /// <summary>
    /// 格式化源访问是否需要空条件调用（可空感知）——值类型可空源（Nullable&lt;T&gt;）与引用类型
    /// 可空源以 "?." 空条件调用发射；非可空值类型直调。
    /// </summary>
    private static bool IsNullableFormattedAccess(TypeRef effectiveSrcType)
        => !effectiveSrcType.IsValueType || TypeRefHelpers.IsNullableValueType(effectiveSrcType);

    // BuildStaticConverterCall（静态转换方法调用渲染）已移 CodeGenerator.ConverterCall.cs
    // （发射端 partial 拆分，守卫主文件行数承诺线 ≤2460）

    private static void GenerateSinglePropertyAssignment(StringBuilder sb, PropertyMappingSnapshot pm, string indent, Dictionary<TypeRef, string> typeNameMap, string methodSuffix = "", MappingSnapshot? mapping = null, GenerationContext? ctx = null, string? srcAccessOverride = null)
    {
        // [Convert] 自定义转换器 — 优先于所有其他转换路径
        // 必须最先判断：pm.Source 可能为 null!（[Convert] 无匹配源属性场景，
        // 如 UserDetailDto.FullName），此时不能访问 pm.Source!.Type
        if (pm.ConverterDeclaringType != null)
        {
            // 方法引用三字段 → 内联 global::FQN.M(args)（不再生成 __conv 单例字段）；
            // R6-b 无匹配源属性 → 字段实参 default 字面量
            // srcAccessOverride（MapWhen T1 局部变量承载）非 null 时替换源访问
            var srcAccess = srcAccessOverride ?? (pm.Source != null ? BuildSourceAccess(pm) : null);
            var convertExpr = BuildStaticConverterCall(pm.ConverterDeclaringType, pm.ConverterMethodName!, pm.ConverterShape, srcAccess, "src");

            // [Convert] + [Mask] 共存：先转换再脱敏
            if (pm.MaskType != null)
            {
                var className = pm.Target.ContainingType?.SimpleName ?? "Anon";
                var maskFieldName = BuildMaskFieldName(className, methodSuffix, pm.Target.Name);
                sb.AppendLine($"{indent}dst.{BuildMaskedAssignment(pm.Target.Name, maskFieldName, convertExpr)};");
            }
            else
            {
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {convertExpr};");
            }
            return;
        }

        // 接口形态无匹配源属性（桥接期，T-001b 退役）：三字段为空、静态调用无从发射，跳过
        // （方法引用形态无源已由上方三字段分支 R6-b 覆盖；有源接口形态落入下方类型转换路径）
        if (pm.Source == null!)
            return;

        // [Mask] / [MapFormat] / 双标 — 非 Convert 路径
        // IMask.Mask(string?) → string，目标属性必为 string 类型，源属性访问直接传入
        // 入口由 pm.MaskType 扩为 (MaskType || FormatString)
        // 否则纯 [MapFormat]（无 [Mask]）时整块跳过、Format 判定落空（镜像运行时入口扩展）
        if (pm.MaskType != null || pm.FormatString != null)
        {
            var className = pm.Target.ContainingType?.SimpleName ?? "Anon";

            // FormatThenMask 双标（先格式化再脱敏）——须在下方"源非 string 落常规路径"守卫之前：
            // 双标下源本非 string（如 DateTime 源），该守卫不适用（镜像 PlanCore FormatThenMask
            // 在源类型守卫之前的位置语义）；Mask(string?) 输入 = 格式化结果（string），无需源 string 守卫。
            // 无效源（编译期 044 已拦的理论路径）→ fall through 常规转换链（口径——双标整体不消费）
            if (pm.MaskType != null && pm.FormatString != null)
            {
                var effectiveFmtSrc = GetEffectiveSourceType(pm) ?? pm.Source!.Type;
                if (pm.Target.Type.SpecialType == SpecialType.System_String
                    && GenerationPreparation.IsFormattableTypeRef(effectiveFmtSrc))
                {
                    var maskFieldName = BuildMaskFieldName(className, methodSuffix, pm.Target.Name);
                    var srcAccess = srcAccessOverride ?? BuildSourceAccess(pm);
                    var formatted = BuildFormattedAssignment(srcAccess, pm.FormatString!, IsNullableFormattedAccess(effectiveFmtSrc), IsEnumFormattedSource(effectiveFmtSrc));
                    sb.AppendLine($"{indent}dst.{BuildMaskedAssignment(pm.Target.Name, maskFieldName, formatted)};");
                    return;
                }
            }
            else if (pm.MaskType != null)
            {
                // （生成侧守卫）：有效源类型非 string 时不发射 Mask 裸调用
                //（Mask(string?) 对 int 实参 → 生成代码 CS1503 编译失败），fall through 类型转换链——
                // 运行时镜像：AssignmentPlanner.PlanCore 源类型守卫（assignmentplanner.cs:124-139，
                // 双引擎锚点纪律
                var effectiveMaskSrc = GetEffectiveSourceType(pm) ?? pm.Source!.Type;
                if (effectiveMaskSrc.SpecialType == SpecialType.System_String)
                {
                    var maskFieldName = BuildMaskFieldName(className, methodSuffix, pm.Target.Name);
                    // srcAccessOverride（MapWhen T1 局部变量承载）优先
                    var srcAccess = srcAccessOverride ?? BuildSourceAccess(pm);
                    sb.AppendLine($"{indent}dst.{BuildMaskedAssignment(pm.Target.Name, maskFieldName, srcAccess)};");
                    return;
                }
            }
            else
            {
                // Format 单标——源/目标守卫（编译期 NCMAP043/044 已拦，
                // 此处为纯运行时/防御路径的 fall-through 口径：无效源不发射格式化，落常规转换链）
                var effectiveFmtSrc = GetEffectiveSourceType(pm) ?? pm.Source!.Type;
                if (pm.Target.Type.SpecialType == SpecialType.System_String
                    && GenerationPreparation.IsFormattableTypeRef(effectiveFmtSrc))
                {
                    var srcAccess = srcAccessOverride ?? BuildSourceAccess(pm);
                    var formatted = BuildFormattedAssignment(srcAccess, pm.FormatString!, IsNullableFormattedAccess(effectiveFmtSrc), IsEnumFormattedSource(effectiveFmtSrc));
                    sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {formatted};");
                    return;
                }
            }
        }

        // 走到这里 SourceProperty 必非 null（仅 [Convert] 无源属性路径会让 SourceProperty=null!）
        // srcPropType 用有效源类型（末段索引=元素类型，与 srcAccess2 表达式类型一致）
        var srcPropType = (GetEffectiveSourceType(pm) ?? pm.Source!.Type).FullName;
        var tgtPropType = pm.Target.Type.FullName;
        // srcAccessOverride（MapWhen T1 局部变量承载）优先——后续全部分支共用
        var srcAccess2 = srcAccessOverride ?? BuildSourceAccess(pm);

        // [MapProperty(IsFromJson = true)] — JSON 序列化/反序列化
        if (pm.IsFromJson)
        {
            var srcIsString = pm.Source!.Type.SpecialType == SpecialType.System_String;
            var tgtIsString = pm.Target.Type.SpecialType == SpecialType.System_String;

            if (srcIsString && !tgtIsString)
            {
                // string → 对象：反序列化
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = global::NCMapper.JsonConverter.JsonConvertToObject<{tgtPropType}>({srcAccess2});");
            }
            else if (!srcIsString && tgtIsString)
            {
                // 对象 → string：序列化（范围扩大：null 守卫与运行时
                // GetJsonByObjectExpression 的 Condition null guard 对齐——null 源 → null 而非 ""，
                // 实测 Map_ObjectToJson_NullSource 分叉）
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess2} == null ? null : global::NCMapper.JsonConverter.ObjectConvertToJson({srcAccess2});");
            }
            else
            {
                // string → string：直接赋值
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess2};");
            }
            return;
        }

        // 类型完全不相容 → 跳过（对齐 AutoMapper"不兼容成员不映射"语义，NCMAP027）
        // 插入点：IsFromJson 分支之后（JSON 映射已提前 return，不误杀）、多段路径分支之前
        // 源类型取末端：SourcePath.Count > 1 时用路径末端属性类型，否则 SourceProperty.Type（改键；
        // 末段集合索引时取集合元素类型——GetEffectiveSourceType）
        // （避免扁平化根类型被误判；仅 [MapFrom] 自动匹配跳过，
        //  显式 [MapProperty(FromField/IsFromJson)/Convert] 映射保留既有语义——IsExplicitlyMapped）
        var effectiveSrcType = GetEffectiveSourceType(pm)!;
        if (!IsExplicitlyMapped(pm) && !TypeRefHelpers.HasMappingPath(effectiveSrcType, pm.Target.Type))
        {
            // 追加诊断改 per-run collector（缓存 MappingSnapshot 只读）；mapping/ctx 为 null 时静默（兼容无上下文调用）
            if (mapping != null && ctx != null)
            {
                ctx.Diagnostics.Add(mapping, new DiagnosticSnapshot
                {
                    DescriptorId = DiagnosticDescriptors.SkippedIncompatibleMemberRule.Id,
                    Args = new[] { pm.Target.Name, effectiveSrcType.FullName, pm.Target.Type.FullName },
                    Location = pm.Target.Location,
                });
            }
            return;
        }

        // 多段路径属性（⑤ 保留分支——推导扁平化/显式点路径共用； 改键 SourcePath.Count > 1；
        // 路径解析已单点化， M6-⑤ 不前移）
        // 注：单段集合索引（"B[0]"，Count==1）不走本分支，落入下方 Plan 分派（有效源类型=元素类型）
        if (pm.SourcePath.Count > 1)
        {
            GenerateFlattenedAssignment(sb, pm, indent, srcAccess2, tgtPropType);
            return;
        }

        // ⑥⑦⑨⑩⑪⑫⑬⑭ 八分支判定前移
        // PlanSymbolAdapter.PlanForAssignment（判定序对齐分发链：同型→隐式→可空三态→Nested→集合→字典），
        // 发射端按 Kind + 承载字段分派纯渲染（RenderAssignmentByPlan）——决策与发射分离。
        // 返回 null = 保留分支形态（⑧死分支 / ⑮List→Dict / ⑯枚举 / ⑰fallback，下方原样保留）。
        // 类型判据统一用有效源类型（判定侧输入一致）
        var plan = PlanSymbolAdapter.PlanForAssignment(effectiveSrcType, pm.Target.Type, pm.Source!.Type);
        if (plan != null)
        {
            RenderAssignmentByPlan(sb, pm, indent, typeNameMap, effectiveSrcType, srcAccess2, srcPropType, tgtPropType, plan.Value, ctx);
            return;
        }

        // 以下为保留分支（⑧⑮⑯⑰ 不前移，发射端原样）

        // ⑧ 死分支（⑤ 首分支全捕获 SourcePath.Count>1，本条件恒 false）
        // 原样保留不前移（W3_1 C2b 标记； 登记后续清理）
        if (pm.SourcePath.Count > 1 && TypeRefHelpers.IsNullableValueType(effectiveSrcType)
                 && !TypeRefHelpers.IsNullableValueType(pm.Target.Type))
        {
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess2} ?? default({tgtPropType});");
        }
        else if (!TypeRefHelpers.IsDictionaryType(effectiveSrcType) && TypeRefHelpers.IsDictionaryType(pm.Target.Type)
            && TypeRefHelpers.ImplementsIEnumerable(effectiveSrcType)
            && !TypeRefHelpers.IsStringType(effectiveSrcType))
        {
            // T5/：List/数组 → Dictionary 不兼容路径（与运行时 GetListExpression 同构）
            // 运行时抛 NotSupportedException（ErrorMessages.UnsupportedListToDictionaryConversion），生成侧对齐——
            // 修复仅同步运行时，生成侧落入 ConvertValue fallback 抛 MapException（异常类型分叉）。
            // 源 null → 赋 null 不抛（与运行时 Condition 分支一致）；：
            // 改调 ErrorMessages.UnsupportedListToDictionaryConversion 与运行时双路径同源
            // （ErrorMessages 已 public，生成代码可引用；typeof().Name 运行时计算类型名保持同构）
            // typeof 不可用于 NRT 注解类型（CS8639）——类型名须去 ? 注解（WithNullableAnnotation.NotAnnotated）
            // 追加 !IsStringType——string（实现 IEnumerable<char>）与任何非 string
            // 目标均属"完全不相容"→ 由分发链前置 NCMAP027 跳过（string→Dict 不落入本受控异常分支）；
            // null 侧改 NullLiteralFor（NRT 下裸 null 赋不可空引用类型触发 CS8601/CS8625）
            var t5SrcType = pm.Source!.Type.NonNullableFullName;
            var t5TgtType = pm.Target.Type.NonNullableFullName;
            var nullLiteral = TypeRefHelpers.NullLiteralFor(pm.Target.Type);
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess2} == null ? {nullLiteral} : throw new System.NotSupportedException(NCMapper.ErrorMessages.UnsupportedListToDictionaryConversion(typeof({t5SrcType}).Name, typeof({t5TgtType}).Name));");
        }
        else if (TryGenerateEnumConversion(sb, pm.Source!, pm.Target, srcAccess2, indent, mapping, ctx))
        {
            // 枚举/可空转换已在 TryGenerateEnumConversion 内生成赋值代码（有副作用的条件分支，空体为链占位，
            // -4 评估：不可删/不可去 else 化——去 else 化会破坏 if/else-if 链，
            // 前置分支命中时仍会误执行 Try/fallback 导致双重生成，登记为有意设计保留）
        }
        else
        {
            sb.AppendLine($"{indent}// fallback to runtime: TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess2})");
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = NCMapper.TypeConvertHelper.ConvertValue<{srcPropType}, {tgtPropType}>({srcAccess2})!;");
        }
    }

    /// <summary>
    /// 按赋值计划渲染— GenerateSinglePropertyAssignment
    /// 分发链 ⑥⑦⑨⑩⑪⑫⑬⑭ 八分支的发射端纯渲染（判定已前移 PlanSymbolAdapter.PlanForAssignment）。
    /// <para>按 plan.Kind + 承载字段分派：⑥ Direct 直赋 / ⑦ Convert cast / ⑨⑩⑪ Convert + NullableCompatibility
    /// 三态（子分支按底层类型渲染期判定，与改造前逐行等价——子分支表达式对应 / I-3 精确形态）/
    /// ⑫ Nested / ⑬⑭ Collection（⑭ IsDictionaryShape 承载区分；元素/键值类型渲染期提取—— 后注）。</para>
    /// <para>同类型集合/字典复制路径说明（继承自改造前 ⑥ 注释）：运行时对同类型集合复制（新实例，
    /// CollectionCopier CopyList/CopyArray/CopyHashSet），生成侧直接赋值共享引用分叉（实测
    /// Array_Of_Int/HashSet_Of_String/Map_SameType_List NotSame 失败）——同类型集合/字典由判定侧
    /// 落入 ⑬⑭ 走复制路径（新实例）；string 实现 IEnumerable（char 枚举）但属标量，判定侧已显式排除。</para>
    /// </summary>
    private static void RenderAssignmentByPlan(StringBuilder sb, PropertyMappingSnapshot pm, string indent,
        Dictionary<TypeRef, string> typeNameMap, TypeRef effectiveSrcType, string srcAccess, string srcPropType,
        string tgtPropType, AssignmentPlan plan, GenerationContext? ctx)
    {
        // ⑥ 同类型直接赋值
        if (plan.Kind == AssignmentKind.Direct)
        {
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess};");
            return;
        }

        // ⑦ 隐式数值转换（int→long 等）
        if (plan.Kind == AssignmentKind.Convert && plan.NullableCompatibility == NullableKind.None)
        {
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = ({tgtPropType}){srcAccess};");
            return;
        }

        // ⑨⑩⑪ 可空组合转换（NullableCompatibility 三态承载，子分支渲染期判定）
        if (plan.Kind == AssignmentKind.Convert)
        {
            RenderNullableConversion(sb, pm, indent, effectiveSrcType, srcAccess, srcPropType, tgtPropType, plan.NullableCompatibility);
            return;
        }

        // ⑫ 嵌套类映射（渲染内部注册对子判定/环降级查询保持现状；
        // plan.IsRegisteredMapFromPair 承载为 TG-P2 映射锁定与 渲染器族抽取预留）
        if (plan.Kind == AssignmentKind.Nested)
        {
            // 嵌套调用名对级后缀表经 ctx 通道透传
            GenerateNestedClassMapping(sb, pm, indent, typeNameMap, srcAccess, srcPropType, tgtPropType,
                ctx?.Preparation?.MethodSuffixMap);
            return;
        }

        // ⑭ 双字典形态（复用 Collection Kind + IsDictionaryShape 承载——M6-②；
        // 键值类型渲染期提取，plan 判定时已确认双字典两侧提取成功，此处必然成功）
        if (plan.Kind == AssignmentKind.Collection && plan.IsDictionaryShape)
        {
            TypeRefHelpers.TryGetDictionaryKeyValueTypes(effectiveSrcType, out var srcKey, out var srcVal);
            TypeRefHelpers.TryGetDictionaryKeyValueTypes(pm.Target.Type, out var tgtKey, out var tgtVal);
            GenerateDictionaryMapping(sb, pm, srcAccess, indent, srcKey!, srcVal!, tgtKey!, tgtVal!, typeNameMap, ctx);
            return;
        }

        // ⑬ 集合形态（元素类型渲染期提取—— 后注；判定时已成功，此处必然成功）
        if (plan.Kind == AssignmentKind.Collection)
        {
            TypeRefHelpers.TryGetCollectionElementTypes(effectiveSrcType, pm.Target.Type, out var elemSrc, out var elemTgt);
            GenerateCollectionMapping(sb, pm, indent, typeNameMap, srcAccess, elemSrc!, elemTgt!, ctx);
            return;
        }
    }

    // RenderNullableConversion（⑨⑩⑪ 可空组合转换纯渲染）已移 CodeGenerator.WhenGuard.cs
    //（预算补偿外移——主文件承诺线 ≤2460，MapWhen 守卫发射占用后以既有渲染函数外移平衡）

    /// <summary>
    /// 从可空值类型显示名提取底层类型名 — 用于 NonNullableToNullable 转换的类型转换
    /// <para>例："int?" → "int"，"long?" → "long"，"System.Nullable&lt;int&gt;" → "int"</para>
    /// </summary>
    /// <param name="nullableTypeName">可空值类型的 ToDisplayString() 结果</param>
    /// <returns>底层类型名；无法提取时返回原字符串</returns>
    private static string GetUnderlyingNullableTypeName(string nullableTypeName)
    {
        // "int?" 格式 → 去掉末尾 "?"
        if (nullableTypeName.EndsWith("?"))
            return nullableTypeName.Substring(0, nullableTypeName.Length - 1);
        // "System.Nullable<int>" 格式 → 提取尖括号内类型
        var openIdx = nullableTypeName.IndexOf('<');
        var closeIdx = nullableTypeName.LastIndexOf('>');
        if (openIdx >= 0 && closeIdx > openIdx)
            return nullableTypeName.Substring(openIdx + 1, closeIdx - openIdx - 1);
        return nullableTypeName;
    }

    /// <summary>
    /// 生成扁平化属性赋值代码
    /// <para>：索引段三元表达式结果非提升（default 推断元素类型）；
    /// 纯属性链 ?. 对值类型成员提升为可空（int?）——表达式可空性判定：
    /// 索引段 = 元素类型可空性；纯链 = 值类型即可空（原 IsValueType 判据语义）。</para>
    /// </summary>
    private static void GenerateFlattenedAssignment(StringBuilder sb, PropertyMappingSnapshot pm, string indent, string srcAccess, string tgtPropType)
    {
        var srcType = GetEffectiveSourceType(pm)!;
        var srcIsNullableVT = TypeRefHelpers.IsNullableValueType(srcType);
        // 纯属性链：?. 对值类型提升为可空（int → int?）；索引段：三元结果 = 元素类型（可空性 = 元素可空性）
        var exprIsNullable = pm.HasIndex ? srcIsNullableVT : srcType.IsValueType;
        if (exprIsNullable && !TypeRefHelpers.IsNullableValueType(pm.Target.Type))
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess} ?? default({tgtPropType});");
        else
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess};");
    }

    /// <summary>
    /// 生成反向路径写回赋值代码— DTO 扁平属性 → 实体嵌套路径（折叠）
    /// <para>语义：
    /// ① 带 [ConvertBack]（pm.ConverterDeclaringType 非 null，反向方法引用三字段，
    /// ReversePlanner 从 PropertyRef.ConverterBack* 三元组携带）→ 内联静态方法调用
    /// （global::FQN.M(args)，四模板 + 守卫变体，与直接属性路径同源）；
    /// ② 无 [ConvertBack] → 一律不消费 [Convert]（转换器泛型绑定正向方向，A-1 写回快照已不携带，
    /// NCMAP033 登记），统一走类型转换路径（BuildLeafValueExpr）；
    /// 源可空 → null=清空形态（源 null：链存在则清叶、链不存在 no-op 不创建中间节点且不调转换器；源非 null：写链）；
    /// 中间节点 null → new 创建（物化期已校验 setter public + 类型 public + 可实例化，B10）；
    /// 已有中间节点 → 复用（不覆盖既有导航对象，仅创建 null 段）；
    /// 集合索引路径已登记跳过，ReverseWritePath 恒为纯属性链。</para>
    /// </summary>
    private static void GenerateReversePathWriteBack(StringBuilder sb, PropertyMappingSnapshot pm, string indent,
        Dictionary<TypeRef, string> typeNameMap, MappingSnapshot? mapping = null, GenerationContext? ctx = null)
    {
        var path = pm.ReverseWritePath!;
        var srcAccess = $"src.{SourceGenEscape.Identifier(pm.Source!.Name)}";
        var srcType = pm.Source.Type;
        var leaf = path[path.Count - 1];
        var leafType = leaf.Type.FullName;

        // 带 [ConvertBack]（读反向方法引用三字段，
        // ReversePlanner 携带）→ 内联静态方法调用（四模板 + 守卫变体：T1 M(值)/T2 M(dto)/T3 M(dto, 值)）。
        // 转换器调用表达式仅在源非 null 写分支求值（null=清空形态 / 源值 null 守卫包裹），
        // 源 null 走清空分支不调转换器（N-1 语义保持）。
        // 无 [ConvertBack] → 一律不消费 [Convert]（物化端 已不携带），
        // 统一走类型转换路径（BuildLeafValueExpr）。
        string valueExpr;
        if (pm.ConverterDeclaringType != null)
        {
            valueExpr = BuildStaticConverterCall(pm.ConverterDeclaringType, pm.ConverterMethodName!,
                pm.ConverterShape, srcAccess, "src");
        }
        else
        {
            valueExpr = BuildLeafValueExpr(pm, srcAccess);
        }

        // 源可空 → null=清空形态。
        // 源 null：链存在则清叶（可空叶 null / 非可空值叶 default(T)），清空路径逐段不创建中间节点；
        // 源非 null：写链（中间段 null → new 创建，现状语义）。
        var srcNullable = srcType.IsReferenceType || TypeRefHelpers.IsNullableValueType(srcType);
        if (srcNullable)
        {
            var leafNullable = leaf.Type.IsReferenceType || TypeRefHelpers.IsNullableValueType(leaf.Type);
            var clearValue = leafNullable ? "null" : $"default({leaf.Type.FullName})";

            sb.AppendLine($"{indent}if ({srcAccess} != null)");
            sb.AppendLine($"{indent}{{");
            var writeIndent = indent + "    ";

            // 写分支：中间节点 null 创建守卫（每段不含叶）；非 null 中间节点复用（不覆盖既有导航对象）
            var writeChain = "dst";
            for (var i = 0; i < path.Count - 1; i++)
            {
                var seg = path[i];
                var segAccess = $"{writeChain}.{seg.Name}";
                sb.AppendLine($"{writeIndent}if ({segAccess} == null) {segAccess} = new {seg.Type.NonNullableFullName}();");
                writeChain = segAccess;
            }

            var writeLeafAccess = $"{writeChain}.{leaf.Name}";
            sb.AppendLine($"{writeIndent}{writeLeafAccess} = {valueExpr};");
            sb.AppendLine($"{indent}}}");

            // 清空分支：链存在（中间段全非 null）→ 清叶；链不存在 → no-op（不创建中间节点）。
            // 无 else 兜底——ReverseWritePath 三个产点（ReversePlanner
            // 写回快照构造）均位于 SourcePath.Count > 1 门内（ReversePlanner.cs "点路径/扁平化反向写回"
            // 分支），恒有中间段，chainExistsParts 恒非空；原防御 else 分支恒死已删。
            var chainExistsParts = new List<string>();
            var checkChain = "dst";
            for (var i = 0; i < path.Count - 1; i++)
            {
                checkChain = $"{checkChain}.{path[i].Name}";
                chainExistsParts.Add($"{checkChain} != null");
            }
            sb.AppendLine($"{indent}else if ({string.Join(" && ", chainExistsParts)})");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{writeIndent}{writeLeafAccess} = {clearValue};");
            sb.AppendLine($"{indent}}}");
            return;
        }

        // 中间节点 null 创建守卫（每段不含叶）：if (dst.A == null) dst.A = new A();
        // 非 null 中间节点复用（不覆盖既有导航对象）；段类型物化期已校验可实例化。
        // 原第二处 if (srcNullable) null 守卫恒死已删——上方首个
        // if (srcNullable) 块以无条件 return 收尾，此后仅 srcNullable == false 可达；
        // bodyIndent/guardWritten 随死分支一并重整（恒为 indent/false，等价论证见）。
        var chain = "dst";
        for (var i = 0; i < path.Count - 1; i++)
        {
            var seg = path[i];
            var segAccess = $"{chain}.{seg.Name}";
            sb.AppendLine($"{indent}if ({segAccess} == null) {segAccess} = new {seg.Type.NonNullableFullName}();");
            chain = segAccess;
        }

        // 叶赋值：链式目标 + 值表达式（类型转换路径内联）
        var leafAccess = $"{chain}.{leaf.Name}";
        sb.AppendLine($"{indent}{leafAccess} = {valueExpr};");
    }

    /// <summary>
    /// 构建反向写回叶值表达式— 源（DTO 属性）类型 → 叶类型转换
    /// <para>分支语义与 GenerateSinglePropertyAssignment 同源（同类型直赋 / 隐式转换 cast /
    /// Nullable 组合三元 / 兜底 TypeConvertHelper.ConvertValue），仅返回表达式不生成赋值语句。</para>
    /// </summary>
    private static string BuildLeafValueExpr(PropertyMappingSnapshot pm, string srcAccess)
    {
        var srcType = pm.Source!.Type;
        var tgtType = pm.Target.Type;
        var srcTypeName = srcType.FullName;
        var tgtTypeName = tgtType.FullName;

        if (TypeRefHelpers.IsSameType(srcType, tgtType))
            return srcAccess;
        if (TypeRefHelpers.IsImplicitConvertible(srcType, tgtType))
            return $"({tgtTypeName}){srcAccess}";
        if (TypeRefHelpers.IsNullableToNullable(srcType, tgtType))
        {
            var uSrc = TypeRefHelpers.GetUnderlyingType(srcType);
            var uTgt = TypeRefHelpers.GetUnderlyingType(tgtType);
            if (TypeRefHelpers.IsSameType(uSrc, uTgt))
                return srcAccess;
            if (TypeRefHelpers.IsImplicitConvertible(uSrc, uTgt))
                return $"{srcAccess}.HasValue ? ({tgtTypeName}){srcAccess}.Value : null";
        }
        else if (TypeRefHelpers.IsNullableToNonNullable(srcType, tgtType))
        {
            var uSrc = TypeRefHelpers.GetUnderlyingType(srcType);
            if (TypeRefHelpers.IsSameType(uSrc, tgtType))
                return $"{srcAccess}.HasValue ? {srcAccess}.Value : default";
            if (TypeRefHelpers.IsImplicitConvertible(uSrc, tgtType))
                return $"{srcAccess}.HasValue ? ({tgtTypeName}){srcAccess}.Value : default";
        }
        else if (TypeRefHelpers.IsNonNullableToNullable(srcType, tgtType))
        {
            var uTgt = TypeRefHelpers.GetUnderlyingType(tgtType)!;
            if (TypeRefHelpers.IsSameType(srcType, uTgt) || TypeRefHelpers.IsImplicitConvertible(srcType, uTgt))
                return $"new {tgtTypeName}(({uTgt.FullName}){srcAccess})";
        }
        return $"NCMapper.TypeConvertHelper.ConvertValue<{srcTypeName}, {tgtTypeName}>({srcAccess})!";
    }

    #endregion 映射方法生成（续）
}

/// <summary>
/// 嵌套互调环降级对缓存（A2；W3_1）
/// <para>环检测决策逻辑（依赖图构建 + DFS 检测长度 ≥2 互调环 + 降级标记，边=嵌套注册对
/// IsMapFromRegistered 口径与生成分发链 1:1——S9）已前移 GenerationPreparation.PlanCycleDetection
/// （发射前准备段， C3 完整面—— 补迁）；本类收敛为降级对缓存：
/// per-run per-thread 表（Reset 重建 + TryMarkDegraded 写 + IsDegraded 读）。</para>
/// <para>降级语义（决策侧承载，不变）：环上全部注册对降级（目标属性保持默认值 + NCMAP029 Info
/// 诊断）——确定性，同输入同输出；直接自引用（长度 1）由 IsSelfRecursiveMapping 深度保护
/// 维持（P0，不降级——B1）；集合元素边不纳入图（与 Q7 既有降级不叠加）。</para>
/// </summary>
internal static class NestedCycleDetector
{
    /// <summary>
    /// 降级注册对集合（源类型 → 目标类型）— per-run per-thread 缓存（Reset 重置；生成阶段只读）
    /// <para>[ThreadStatic] 线程隔离修复并发竞态——原静态字段在
    /// 并发编译（多目标并行/VBCSCompiler 共享进程）与并行测试（xUnit 线程池）下，另一 run 的
    /// Prepare 重置与本 run 的 IsDegraded 读写重叠（非并发字典损坏 → InvalidOperationException/
    /// 降级集意外丢失）。C3 前移拉长"Prepare → 发射期读"窗口后竞态显形；线程隔离后同线程内
    /// Prepare→读序不变（行为等价），跨线程各自独立表（语义不变——每 run 全量重建）。</para>
    /// </summary>
    [ThreadStatic]
    private static Dictionary<TypeRef, TypeRef>? _degradedPairs;

    /// <summary>
    /// 重置降级对表（每次生成 run 开头由准备段 PlanCycleDetection 调用）
    /// </summary>
    internal static void Reset() => _degradedPairs = new Dictionary<TypeRef, TypeRef>();

    /// <summary>
    /// 尝试标记降级对——不存在时写入并返回 true，已存在返回 false（幂等：已降级对不重复标记/诊断；
    /// 原 DegradeCycle ContainsKey 判断 + 写入合并为 TryAdd 语义，W3_1 决策前移重构）
    /// </summary>
    internal static bool TryMarkDegraded(TypeRef srcType, TypeRef tgtType)
    {
        if (_degradedPairs!.ContainsKey(srcType)) return false;
        _degradedPairs[srcType] = tgtType;
        return true;
    }

    /// <summary>
    /// 查询注册对是否被降级（互调环上，生成委托前调用）
    /// </summary>
    /// <param name="srcType">源类型</param>
    /// <param name="tgtType">目标类型</param>
    /// <returns>命中降级返回 true</returns>
    public static bool IsDegraded(TypeRef srcType, TypeRef tgtType)
    {
        return _degradedPairs != null
            && _degradedPairs.TryGetValue(srcType, out var tgt)
            && TypeRefHelpers.IsSameType(tgt, tgtType);
    }
}
