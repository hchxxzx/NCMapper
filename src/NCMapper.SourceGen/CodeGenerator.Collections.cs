using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.Shared;

namespace NCMapper.SourceGen;

internal static partial class CodeGenerator
{
    /// <summary>
    /// 生成嵌套类属性映射代码
    /// </summary>
    private static void GenerateNestedClassMapping(StringBuilder sb, PropertyMappingSnapshot pm, string indent,
        Dictionary<TypeRef, string> typeNameMap, string srcAccess, string srcPropType, string tgtPropType,
        Dictionary<string, string>? suffixMap = null)
    {
        var nestedSrcType = pm.Source!.Type;
        bool isNullable = nestedSrcType.IsReferenceType || TypeRefHelpers.IsNullableValueType(nestedSrcType);
        // 嵌套调用名改对级后缀查表（嵌套对 = (pm.Source.Type →
        // pm.Target.Type)，SuffixKey 同键）——缺陷态：typeNameMap first-wins 后缀 ≠ 元素对对级后缀
        // → 调用名与注册名断裂（CS0103）；miss（非注册对，走运行时回退分支不消费方法名）回落
        // SimpleName 与现状一致。suffixMap null 兜底 = 现状 miss 行为
        var pairSuffix = GetPairSuffix(pm.Source!.Type!, pm.Target.Type!, suffixMap);
        var nestedSrcName = pm.Source!.Type!.SimpleName + pairSuffix;
        var nestedTgtName = pm.Target.Type!.SimpleName + pairSuffix;
        var nestedMethodName = $"Map_{nestedSrcName}_To_{nestedTgtName}";

        // 互调环降级——环上注册对不生成委托（属性保持默认值；
        // NCMAP029 诊断已在 Prepare 阶段输出）
        if (NestedCycleDetector.IsDegraded(nestedSrcType, pm.Target.Type))
        {
            return;
        }

        if (IsMapFromRegistered(nestedSrcType, pm.Target.Type))
        {
            if (isNullable)
            {
                sb.AppendLine($"{indent}if ({srcAccess} != null)");
                sb.AppendLine($"{indent}    dst.{SourceGenEscape.Identifier(pm.Target.Name)} = GeneratedMapper.{nestedMethodName}({srcAccess})!;");
            }
            else
            {
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = GeneratedMapper.{nestedMethodName}({srcAccess})!;");
            }
        }
        else
        {
            // 运行时回退：Mapper.Map 的 TSource/TTarget 带 class 约束（Mapper.cs:417），
            // 可空引用类型属性（如 InnerDto?）的 ToDisplayString 带 ? 注解会违反约束（CS8634），
            // 须去掉外层可空注解；null 透传由 Mapper.Map 入口 if (source == null) return null 处理。
            var srcMapType = pm.Source!.Type.NonNullableFullName;
            var tgtMapType = pm.Target.Type.NonNullableFullName;
            if (isNullable)
            {
                sb.AppendLine($"{indent}if ({srcAccess} != null)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    // Fallback path: nested mapping runtime fallback (nested type not [MapFrom]-registered). May fail under NativeAOT; see docs 05-Compose");
                sb.AppendLine($"{indent}#pragma warning disable IL2026, IL3050");
                sb.AppendLine($"{indent}    dst.{SourceGenEscape.Identifier(pm.Target.Name)} = NCMapper.Mapper.Map<{srcMapType}, {tgtMapType}>({srcAccess})!;");
                sb.AppendLine($"{indent}#pragma warning restore IL2026, IL3050");
                sb.AppendLine($"{indent}}}");
            }
            else
            {
                sb.AppendLine($"{indent}// Fallback path: nested mapping runtime fallback (nested type not [MapFrom]-registered). May fail under NativeAOT; see docs 05-Compose");
                sb.AppendLine($"{indent}#pragma warning disable IL2026, IL3050");
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = NCMapper.Mapper.Map<{srcMapType}, {tgtMapType}>({srcAccess})!;");
                sb.AppendLine($"{indent}#pragma warning restore IL2026, IL3050");
            }
        }
    }

    /// <summary>
    /// 生成集合属性映射代码
    /// </summary>
    private static void GenerateCollectionMapping(StringBuilder sb, PropertyMappingSnapshot pm, string indent,
        Dictionary<TypeRef, string> typeNameMap, string srcAccess,
        TypeRef elemSrc, TypeRef elemTgt, GenerationContext? ctx = null)
    {
        var elemTgtStr = elemTgt.FullName;
        var tmpVar = $"_list_{pm.Target.Name}";
        // 元素对后缀改对级查表（同嵌套站点口径；注册性判定
        // isRegisteredPair 维持 typeNameMap.ContainsKey 存在性语义——明文不扩大改动面）
        var pairSuffix = GetPairSuffix(elemSrc!, elemTgt!, ctx?.Preparation?.MethodSuffixMap);
        var elemSrcName = elemSrc!.SimpleName + pairSuffix;
        var elemTgtName = elemTgt!.SimpleName + pairSuffix;
        var listMethodName = $"MapList_{elemSrcName}_To_{elemTgtName}";
        // T4/：判定元素对是否注册（双方均在 typeNameMap）
        // 注册对 → 现状 MapList 委托调用；未注册对（如基元异类型 int→long）→ 内联逐元素转换，
        // 消除引用不存在 MapList 委托的 CS0117 编译失败（双向 ×10 实证）
        var isRegisteredPair = elemSrc is TypeRef elemSrcNamed && elemTgt is TypeRef elemTgtNamed
            && typeNameMap.ContainsKey(elemSrcNamed) && typeNameMap.ContainsKey(elemTgtNamed);
        if (isRegisteredPair)
        {
            sb.AppendLine($"{indent}var {tmpVar} = GeneratedMapper.{listMethodName}({srcAccess});");
        }
        else
        {
            // 内联逐元素转换（方案 b）：null 守恒（srcAccess 为 null 时赋 null，与 MapList(null)=null 契约一致），
            // 元素转换查 ElementExprPlan 纯渲染（W3_1 C2a 决策前移；隐式转换→强转路径①；ConvertValue/嵌套回退→路径②，AOT 回退点）。
            // 生成文件头无 using 区，System.Linq.Enumerable.Select / List 一律全限定
            var elemPlan = ResolveElementExprPlan(elemSrc, elemTgt, ctx, typeNameMap);
            var elementExpr = RenderElementMappingExpression(elemSrc, elemTgt, "_e", elemPlan);
            // pragma 判定改读 Plan.Kind（原 StartsWith 字符串反推的 Kind 同构替换，渲染输出零变化）
            var isRuntimeFallback = elemPlan.Kind is ElementExprKind.NestedRuntimeFallback or ElementExprKind.RuntimeConvert;
            var inlineStatement = $"{indent}var {tmpVar} = {srcAccess} == null ? null : new System.Collections.Generic.List<{elemTgtStr}>(System.Linq.Enumerable.Select({srcAccess}, _e => {elementExpr}));";
            if (isRuntimeFallback)
            {
                // Fallback path: collection element mapping runtime fallback (element type not [MapFrom]-registered). May fail under NativeAOT; see docs 05-Compose
                // T4 路径②：ConvertValue 委托运行时转换 → 新增 AOT 回退点，pragma 精确抑制 + D-002 备案登记
                sb.AppendLine($"{indent}// Fallback path: collection element mapping runtime fallback (element type not [MapFrom]-registered). May fail under NativeAOT; see docs 05-Compose");
                sb.AppendLine($"{indent}#pragma warning disable IL2026, IL3050");
                sb.AppendLine(inlineStatement);
                sb.AppendLine($"{indent}#pragma warning restore IL2026, IL3050");
            }
            else
            {
                sb.AppendLine(inlineStatement);
            }
        }
        var tgtType = pm.Target.Type;
        // T3/：守卫转化为 null 条件构造（v1.1）
        // 与运行时 GetListExpression 的 Condition(test, mapped, null) 逐分支同构：
        // Update 路径源集合为 null 时统一赋 null（清空），不再保留旧值。
        // 数组/ISet/具体集合分支对 null tmpVar 不得解引用（原守卫直接赋值会 NRE），
        // 故用 null 条件构造（tmp == null ? null : <构造>）；List/接口分支 null 即赋 null。
        if (tgtType.Kind == TypeKind.Interface && tgtType.SimpleName != "IEnumerable")
        {
            if (tgtType.SimpleName == "ISet" || tgtType.SimpleName == "IReadOnlySet")
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {tmpVar} == null ? null : new System.Collections.Generic.HashSet<{elemTgtStr}>({tmpVar});");
            else
                sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {tmpVar};");
        }
        else if (tgtType.IsArray)
        {
            // 数组目标 → ToArray()
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {tmpVar} == null ? null : {tmpVar}.ToArray();");
        }
        else if (tgtType.SimpleName == "List" || tgtType.SimpleName == "IList" || tgtType.SimpleName == "ICollection"
            || tgtType.SimpleName == "IEnumerable" || tgtType.SimpleName == "IReadOnlyList" || tgtType.SimpleName == "IReadOnlyCollection")
        {
            // List<T> 或兼容接口 → 直接赋值（null 即赋 null，与运行时一致）
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {tmpVar};");
        }
        else
        {
            // 具体集合类型（HashSet/Queue/Stack/SortedSet/ObservableCollection 等）
            // → 使用 IEnumerable<T> 构造器创建目标集合
            var fullTypeStr = tgtType.NonNullableFullName;
            // 范围扩大：Stack 复制顺序分叉
            // 运行时 CopyStack（CollectionCopier）保持 Pop 语义（CopyTo 栈顶→栈底 + Array.Reverse + 构造器 Push），
            // 生成侧 new Stack(枚举) 会反转（Stack.GetEnumerator 从栈顶开始，构造器 Push 后新栈顶=枚举末尾=源栈底）
            // → 实测 Stack_Of_Int_Mapped：Expected 777（源栈顶）Actual 999（源栈底）。
            // 修复：Stack 目标构造器参数包 System.Linq.Enumerable.Reverse，保持 Pop 语义与运行时对齐。
            // 注：Queue 无此问题（FIFO 枚举序 = 构造序）。
            var ctorArg = tgtType.SimpleName == "Stack"
                ? $"System.Linq.Enumerable.Reverse({tmpVar})"
                : tmpVar;
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(pm.Target.Name)} = {tmpVar} == null ? null : new {fullTypeStr}({ctorArg});");
        }
    }

    /// <summary>
    /// 检查所有属性是否都是简单赋值（类型相同或可隐式转换，非扁平化值类型）
    /// <para><b>非简单赋值场景</b>：</para>
    /// <list type="bullet">
    /// <item>[Convert] 自定义转换器（可能 SourceProperty=null!，访问 .Type 会 NRE）</item>
    /// <item>扁平化属性且源为值类型（需要 null 合并生成）</item>
    /// <item>类型不同且不可隐式转换（需要类型转换路径）</item>
    /// </list>
    /// </summary>
    internal static bool TryBuildSimpleAssignments(MappingSnapshot mapping, out List<string> assignments, string methodSuffix = "")
    {
        assignments = new List<string>();
        foreach (var pm in mapping.PropertyMappings)
        {
            // [MapIgnore] 方向控制 — 与 GeneratePropertyAssignments 保持一致
            if (ShouldSkipForDirectionPair(pm, IsToDtoMapping(mapping))) continue;

            // 反向写回为多语句分支——不可进简单赋值路径，整体回退标准路径
            if (pm.IsReverseWriteBack)
                return false;

            // [Convert] 转换路径非简单赋值，且 SourceProperty 可能为 null!（访问前必判，否则 NRE）
            if (pm.ConverterDeclaringType != null || pm.Source == null!)
                return false;

            // （等价缺口修复）：JSON 展开为非简单赋值——早退回退标准路径。
            // [MapFrom] 通道 IsFromJson 恒为显式映射（plan 阶段必 return false），早退仅前移判定点——产物字节不变
            if (pm.IsFromJson)
                return false;

            // 条件守卫属性不进简单赋值路径——早退回退标准路径
            //（赋值层 if 包裹守卫单点 EmitGuardedSinglePropertyAssignment；对象初始化器成员
            // 三元化须逐 Add 点包裹，回退标准路径复用单点实现，行为一致性收敛）
            if (pm.ConditionMethodRef is { Length: > 0 })
                return false;

            // 可访问性守卫 + 类型完全不相容跳过（与标准路径一致；简单/标准路径决策由
            // PlanSymbol 单点承载——见下方简单赋值判定；源侧判空已由 IsAccessiblePair 处理）
            var (accessible, _) = IsAccessiblePair(pm);
            if (!accessible)
                continue;

            // 边界：显式 [MapProperty]/[Convert] 映射不参与自动跳过（保留 NCMAP002 fallback）
            // 有效源类型（末段索引=元素类型）
            if (!IsExplicitlyMapped(pm))
            {
                var effectiveSrcType = GetEffectiveSourceType(pm)!;
                if (!TypeRefHelpers.HasMappingPath(effectiveSrcType, pm.Target.Type))
                    continue;
            }

            // srcPropType/值类型判定均用有效源类型（与 srcAccess 表达式类型一致）
            var srcPropType = (GetEffectiveSourceType(pm) ?? pm.Source!.Type).FullName;
            var tgtPropType = pm.Target.Type.FullName;
            var srcAccess = BuildSourceAccess(pm);

            if ((pm.SourcePath.Count > 1 || pm.HasIndex) && GetEffectiveSourceType(pm)!.IsValueType)
                return false;

            // ② 枢纽深化：简单赋值判定由 PlanSymbolAdapter 驱动，
            // 消费 AssignmentPlan（统一 Kind），替代 TypeRefHelpers.PlanSymbol（SymbolAssignmentKind）。
            // 行为边界：
            // - pm.ConverterDeclaringType != null / pm.Source == null → return false 早退行为不变
            //   （方法引用 ConvertUsing 不进简单路径；接口形态已退役、经类型转换路径）
            // - Mask Kind 路径裁决：adapter 对简单路径不产 Mask Kind（hasMask 传 false），
            //   简单路径 :1710/:1725 的 pm.MaskType != null 包装保留不变（现状行为）
            // - hasConvertUsing/hasConvertBackUsing 传 false（:1747 已早退方法引用形态）
            var plan = PlanSymbolAdapter.Plan(
                source: GetEffectiveSourceType(pm)!,
                target: pm.Target.Type,
                hasConvertUsing: false,  // :1671 已早退
                hasMask: false,  // 简单路径 Mask 包装保留不变
                hasConvertBackUsing: false,  // :1671 已早退
                isToDto: IsToDtoMapping(mapping),
                isFromJson: pm.IsFromJson,
                crossTableFromSource: null);  // 简单路径不处理跨表

            if (plan.Kind == AssignmentKind.Direct)
            {
                // [Mask] 脱敏包装：同类型赋值时包装为 __mask_X.Mask(srcAccess)
                if (pm.MaskType != null)
                {
                    var className = pm.Target.ContainingType?.SimpleName ?? "Anon";
                    var maskFieldName = BuildMaskFieldName(className, methodSuffix, pm.Target.Name);
                    assignments.Add(BuildMaskedAssignment(pm.Target.Name, maskFieldName, srcAccess));
                }
                else
                {
                    // 初始化器成员名 @ 转义（对象初始化器目标属性名引用）
                    assignments.Add($"{SourceGenEscape.Identifier(pm.Target.Name)} = {srcAccess}");
                }
            }
            else if (plan.Kind == AssignmentKind.Convert
                     && TypeRefHelpers.IsImplicitConvertible(GetEffectiveSourceType(pm)!, pm.Target.Type))
            {
                // [Mask] 脱敏包装：隐式转换时包装为 __mask_X.Mask((type)srcAccess)
                if (pm.MaskType != null)
                {
                    var className = pm.Target.ContainingType?.SimpleName ?? "Anon";
                    var maskFieldName = BuildMaskFieldName(className, methodSuffix, pm.Target.Name);
                    assignments.Add(BuildMaskedAssignment(pm.Target.Name, maskFieldName, $"({tgtPropType}){srcAccess}"));
                }
                else
                {
                    assignments.Add($"{SourceGenEscape.Identifier(pm.Target.Name)} = ({tgtPropType}){srcAccess}");
                }
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 生成内联元素赋值体
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="indent">缩进字符串</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表，用于嵌套映射引用</param>
    /// <param name="ctx">可选生成上下文（传递全局配置等）</param>
    /// <param name="methodSuffix">方法名后缀（如 "_v2"）</param>
    public static void GenerateInlineElement(StringBuilder sb, MappingSnapshot mapping, string indent, Dictionary<TypeRef, string> typeNameMap, GenerationContext? ctx = null, string methodSuffix = "")
    {
        var targetName = mapping.TargetType.FullName;

        bool hasParameterlessCtor = GetConstructors(mapping.TargetType)
            .Any(c => c.Parameters.Count == 0 && c.IsPublic);

        // 绑定非空 → 构造器路径（与 Core 门同步—— 双点，
        // 防 矩阵分叉；绑定空时各分支谓词与现状逐字节等价，B9）
        bool useCtorPath = !hasParameterlessCtor || HasCtorParamBindings(mapping.TargetType);

        // 声明 BeforeMapping 的对在分支①（初始化器——构造与赋值不可分）无 Before 插入点
        // 无 init-only 属性 → 降级 :1914"new + 逐语句赋值"形态（首条赋值前发射）；含 init-only 属性 → F37（原发射保留，钩子不发射）；
        // 构造器路径（实参即赋值）/fallback（整建回退）无插入点 → F37。未声明钩子恒原形态（G3 逐字节不变）
        bool beforeHook = !string.IsNullOrEmpty(mapping.BeforeMappingMethodRef);
        // MapList 四路径共享的简单赋值预扫描——prep 段
        //（GenerationPreparation.SimpleAssignments）一次计算、发射端查表纯消费；表值 null =
        // 非简单赋值判定；miss（ctx/Preparation 缺席、超集外快照）兜底现场计算——判定序与
        // 语义零改动（PlanSymbolAdapter 同款防御不变量），发射文本逐字节不变。
        var prepSimpleAssignments = ctx?.Preparation?.SimpleAssignments;
        List<string>? assignments;
        if (prepSimpleAssignments != null && prepSimpleAssignments.TryGetValue(mapping, out var preSimple))
            assignments = preSimple; // 命中：null = 非简单赋值（prep 已判定）
        else
            assignments = TryBuildSimpleAssignments(mapping, out var builtSimple, methodSuffix) ? builtSimple : null;
        if (assignments is { Count: > 0 } && !useCtorPath && !(beforeHook && !HasInitOnlyTargetProperty(mapping)))
        {
            ReportBeforeMappingUninsertableIfNeeded(ctx, mapping);
            sb.AppendLine($"{indent}var dst = new {targetName}");
            sb.AppendLine($"{indent}{{");
            for (int i = 0; i < assignments.Count; i++)
            {
                var trailing = i < assignments.Count - 1 ? "," : "";
                sb.AppendLine($"{indent}    {assignments[i]}{trailing}");
            }
            sb.AppendLine($"{indent}}};");
        }
        else if (useCtorPath)
        {
            // record 等无无参构造器类型 → 用构造器参数映射
            // 内联选器单点（加权 + advisory——与 Core 同步，
            // 004 R-2 双点不同步防线）；绑定空时与既有链等价
            var ctor = SelectTargetConstructor(mapping, ctx);

            if (ctor != null && ctor.Parameters.Count > 0)
            {
                var ctorArgs = new List<string>();
                for (var pi = 0; pi < ctor.Parameters.Count; pi++)
                {
                    var param = ctor.Parameters[pi];
                    // 显式绑定优先——与 BuildConstructorArgs 同款
                    // helper（内联独立实现三处之二——004 R-2 内联匹配同步）；未命中走既有同名循环
                    if (TryBuildBoundConstructorArg(mapping, param, out var boundArg))
                    {
                        ctorArgs.Add(boundArg);
                        continue;
                    }
                    foreach (var pm in mapping.PropertyMappings)
                    {
                        if (System.StringComparer.OrdinalIgnoreCase.Equals(pm.Target.Name, param.Name))
                        {
                            // 内联路径补方向跳过检查（与 BuildConstructorArgs 对齐）
                            if (ShouldSkipForDirectionPair(pm, IsToDtoMapping(mapping))) break;

                            var paramTypeFullName = param.Type.FullName;
                            // [Convert] 无源属性守卫——方法引用形态渲染 R6-b 静态调用，
                            // 接口形态（退役期）default(参数类型)（与 BuildConstructorArgs 同口径）
                            if (pm.Source == null)
                            {
                                // 守卫三元；T1 无源已由物化期 F43 门拦截，此处仅 T2 可包裹
                                ctorArgs.Add(ApplyWhenToConstructorArg(pm, pm.ConverterDeclaringType != null
                                    ? BuildStaticConverterCall(pm.ConverterDeclaringType, pm.ConverterMethodName!,
                                        pm.ConverterShape, null, "src")
                                    : $"default({paramTypeFullName})", param.Type));
                                break;
                            }
                            var srcAccess = BuildSourceAccess(pm);
                            var srcPropType = pm.Source!.Type;
                            var paramType = param.Type;

                            // 守卫三元（条件假 → 构造器参数显式 default；
                            // 与 BuildConstructorArgs 同口径，内联场景独立实现）
                            if (TypeRefHelpers.IsSameType(srcPropType, paramType))
                            {
                                ctorArgs.Add(ApplyWhenToConstructorArg(pm, srcAccess, param.Type));
                            }
                            else if (TypeRefHelpers.IsImplicitConvertible(srcPropType, paramType))
                            {
                                ctorArgs.Add(ApplyWhenToConstructorArg(pm, $"({paramType.FullName}){srcAccess}", param.Type));
                            }
                            else
                            {
                                // fallback to runtime: TypeConvertHelper.ConvertValue<{srcPropType}, {paramType}>({srcAccess})
                                ctorArgs.Add(ApplyWhenToConstructorArg(pm, $"NCMapper.TypeConvertHelper.ConvertValue<{srcPropType.FullName}, {paramType.FullName}>({srcAccess})!", param.Type));
                            }
                            break;
                        }
                    }
                    // 当前参数未匹配 → 补 default（仅当该参数位置缺失；等价原 IndexOf(param)+1 判定）
                    if (ctorArgs.Count <= pi)
                        ctorArgs.Add($"default({param.Type.FullName})");
                }
                sb.AppendLine($"{indent}var dst = new {targetName}({string.Join(", ", ctorArgs)});");
                // 构造器一体化（实参即赋值）无 Before 插入点 → F37（钩子不发射）
                ReportBeforeMappingUninsertableIfNeeded(ctx, mapping);
            }
            else
            {
                sb.AppendLine($"{indent}// Fallback path: no usable parameterized constructor, runtime mapping fallback. May fail under NativeAOT; see docs 05-Compose");
                sb.AppendLine($"{indent}#pragma warning disable IL2026, IL3050");
                sb.AppendLine($"{indent}var dst = NCMapper.Mapper.Map<{mapping.SourceType.NonNullableFullName}, {targetName}>(src)!;");
                sb.AppendLine($"{indent}#pragma warning restore IL2026, IL3050");
                // 整建回退（运行时整体接管）无 Before 插入点 → F37（钩子不发射）
                ReportBeforeMappingUninsertableIfNeeded(ctx, mapping);
            }
        }
        else if (hasParameterlessCtor)
        {
            // 存在 init-only 属性且简单赋值失败 → 回退到构造器参数匹配路径
            if (HasInitOnlyTargetProperty(mapping))
            {
                // init-only 属性只能通过 object initializer 或构造器参数赋值；
                // Q7构造器 + 对象初始化器（BuildInitOnlyInitializerMembers 表达式化 init-only 属性）
                var initMembers = BuildInitOnlyInitializerMembers(mapping, typeNameMap, ctx);
                GenerateInlineConstructorMapping(sb, mapping, indent, targetName, initMembers, methodSuffix, ctx);
                // init-only 变体插入点——构造后、非 init-only 赋值前发射 Before
                AppendBeforeMappingCall(sb, mapping, indent);
                GeneratePropertyAssignments(sb, mapping, indent, typeNameMap, skipInitOnly: true, ctx, methodSuffix);
            }
            else
            {
                sb.AppendLine($"{indent}var dst = new {targetName}();");
                // 无参构造插入点——首条赋值前发射 Before（分支①降级对同经此处）
                AppendBeforeMappingCall(sb, mapping, indent);
                GeneratePropertyAssignments(sb, mapping, indent, typeNameMap, ctx: ctx, methodSuffix: methodSuffix);
            }
        }

        // List 族内联元素统一携带 AfterMapping（4 调用点单点收口）
        AppendAfterMappingCall(sb, mapping, indent);
    }

    /// <summary>
    /// 为内联元素生成构造器参数匹配代码 — P1-SHOULD-8 init-only 属性回退路径
    /// <para>逻辑与 <see cref="GenerateConstructorMapping"/> 一致，但使用指定缩进（用于循环体内联场景）</para>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="mapping">映射信息</param>
    /// <param name="indent">缩进字符串</param>
    /// <param name="targetName">目标类型全名字符串</param>
    private static void GenerateInlineConstructorMapping(StringBuilder sb, MappingSnapshot mapping, string indent, string targetName, IReadOnlyList<string>? initOnlyMembers = null, string methodSuffix = "", GenerationContext? ctx = null)
    {
        // 内联选器单点（加权 + advisory 三站点之三）；
        // 调用的 BuildConstructorArgs 自动继承绑定优先匹配；绑定空逐字节等价
        var ctor = SelectTargetConstructor(mapping, ctx);

        if (ctor == null || ctor.Parameters.Count == 0)
        {
            // 无参数化构造器 → 无参构造 + 对象初始化器（修复，：
            // 原回退 Mapper.Map 对双向导航（TodoList↔TodoItem）反射映射无循环防护 → StackOverflow；
            // 对象初始化器支持 init setter，可表达式化属性直接赋值，不再走运行时回退）
            if (ctor != null)
            {
                sb.AppendLine($"{indent}var dst = new {targetName}{BuildInitializerSuffix(initOnlyMembers)};");
                return;
            }
            // 无任何公共构造器 → 回退到运行时（记录差异；不构成循环递归链——无注册委托可命中）
            sb.AppendLine($"{indent}// Fallback path: init-only target without usable constructor, runtime mapping fallback. May fail under NativeAOT; see docs 05-Compose");
            sb.AppendLine($"{indent}#pragma warning disable IL2026, IL3050");
            sb.AppendLine($"{indent}var dst = NCMapper.Mapper.Map<{mapping.SourceType.NonNullableFullName}, {targetName}>(src)!;");
            sb.AppendLine($"{indent}#pragma warning restore IL2026, IL3050");
            return;
        }

        var ctorArgs = BuildConstructorArgs(mapping, ctor);
        // Q7构造器参数 + init-only 成员对象初始化器
        sb.AppendLine($"{indent}var dst = new {targetName}({string.Join(", ", ctorArgs)}){BuildInitializerSuffix(initOnlyMembers)};");
    }

    /// <summary>
    /// 生成字典属性映射代码 — KV 循环映射，支持跨类型 K/V
    /// <para>生成模式：</para>
    /// <code>
    /// if (src.Prop != null)
    /// {
    ///     var _dict_Prop = new Dictionary&lt;TgtKey, TgtVal&gt;();
    ///     foreach (var _kv_Prop in src.Prop)
    ///     {
    ///         _dict_Prop[&lt;mappedKey&gt;] = &lt;mappedValue&gt;;
    ///     }
    ///     dst.Prop = _dict_Prop;
    /// }
    /// </code>
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="pm">属性映射信息</param>
    /// <param name="srcAccess">源属性访问表达式字符串</param>
    /// <param name="indent">缩进字符串</param>
    /// <param name="srcKey">源字典 Key 类型</param>
    /// <param name="srcVal">源字典 Value 类型</param>
    /// <param name="tgtKey">目标字典 Key 类型</param>
    /// <param name="tgtVal">目标字典 Value 类型</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表</param>
    /// <param name="ctx">生成期上下文（ElementExprPlan 查表通道；null 时 miss 兜底现场决策）</param>
    private static void GenerateDictionaryMapping(StringBuilder sb, PropertyMappingSnapshot pm, string srcAccess, string indent,
        TypeRef srcKey, TypeRef srcVal, TypeRef tgtKey, TypeRef tgtVal,
        Dictionary<TypeRef, string> typeNameMap, GenerationContext? ctx = null)
    {
        var tgtKeyStr = tgtKey.FullName;
        var tgtValStr = tgtVal.FullName;
        var propName = pm.Target.Name;
        var dictVar = $"_dict_{propName}";
        var kvVar = $"_kv_{propName}";

        // （映射路径补齐，与 DeepCopy 三分层同构）：局部变量类型对齐属性声明类型
        // 字典子类目标属性（lin-cms ExtraProperties）用基类 Dictionary<K,V> 局部变量赋子类属性 → CS0266。
        // 确切 Dictionary/接口 → Dictionary<K,V>；子类可无参构造 → 子类实例；子类不可无参构造 → 差异登记浅拷贝
        var dictCtorExpr = TypeRefHelpers.TryGetDictionaryCreationExpression(pm.Target.Type, tgtKeyStr, tgtValStr);
        if (dictCtorExpr == null)
        {
            sb.AppendLine($"{indent}// 差异登记：字典子类 {pm.Target.Type.FullName} 无公共无参构造器，" +
                "字典复制降级为浅拷贝引用赋值（类型匹配，避免 CS0266）");
            sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(propName)} = {srcAccess};");
            return;
        }

        // T3/O2 连带：字典路径 Update null 守卫转化为 null 条件构造（连带核查点）
        // 源字典为 null 时统一赋 null（清空），与运行时 GetDictionaryExpression 的 Condition(test, mapped, null)
        // 逐分支同构，不再"跳过赋值保留旧值"（Update 场景分叉消除）
        sb.AppendLine($"{indent}var {dictVar} = {srcAccess} == null ? null : {dictCtorExpr};");
        sb.AppendLine($"{indent}if ({srcAccess} != null)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    foreach (var {kvVar} in {srcAccess})");
        sb.AppendLine($"{indent}    {{");
        // W3_1 C2a 决策前移：K/V 元素表达式查 ElementExprPlan 纯渲染（判定链已前移准备段）
        var keyPlan = ResolveElementExprPlan(srcKey, tgtKey, ctx, typeNameMap);
        var mappedKey = RenderElementMappingExpression(srcKey, tgtKey, $"{kvVar}.Key", keyPlan);
        var valPlan = ResolveElementExprPlan(srcVal, tgtVal, ctx, typeNameMap);
        var mappedVal = RenderElementMappingExpression(srcVal, tgtVal, $"{kvVar}.Value", valPlan);
        // 元素类型未注册 [MapFrom] 时回退到运行时 Mapper.Map——pragma 精确包裹回退语句
        // （pragma 判定改读 Plan.Kind，原 StartsWith("NCMapper.Mapper.Map<") 仅命中 NestedRuntimeFallback，同构替换）
        bool keyIsRuntimeFallback = keyPlan.Kind == ElementExprKind.NestedRuntimeFallback;
        bool valIsRuntimeFallback = valPlan.Kind == ElementExprKind.NestedRuntimeFallback;
        if (keyIsRuntimeFallback || valIsRuntimeFallback)
        {
            sb.AppendLine($"{indent}        // Fallback path: collection element mapping runtime fallback (element type not [MapFrom]-registered). May fail under NativeAOT; see docs 05-Compose");
            sb.AppendLine($"{indent}        #pragma warning disable IL2026, IL3050");
            sb.AppendLine($"{indent}        {dictVar}[{mappedKey}] = {mappedVal};");
            sb.AppendLine($"{indent}        #pragma warning restore IL2026, IL3050");
        }
        else
        {
            sb.AppendLine($"{indent}        {dictVar}[{mappedKey}] = {mappedVal};");
        }
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}dst.{SourceGenEscape.Identifier(propName)} = {dictVar};");
    }

    /// <summary>
    /// 查询元素映射表达式计划（W3_1 C2a 决策前移）— 准备段 ElementExprPlans 查表（正常路径）；
    /// miss 兜底现场决策（防御不变量：Plan 覆盖面 = 正向 + 反向全部无条件提取的元素对/KV 对，
    /// 理论不可达；ctx 未挂载/表未含该类型对时兜底，保证任意调用上下文行为等价）。
    /// </summary>
    /// <param name="srcElemType">源元素类型</param>
    /// <param name="tgtElemType">目标元素类型</param>
    /// <param name="ctx">生成期上下文（Preparation 查表通道；null 时直接兜底）</param>
    /// <param name="typeNameMap">类型到"类名+后缀"的映射表（兜底决策的方法名派生依赖）</param>
    private static ElementExprPlan ResolveElementExprPlan(
        TypeRef srcElemType, TypeRef tgtElemType, GenerationContext? ctx,
        Dictionary<TypeRef, string> typeNameMap)
    {
        var plans = ctx?.Preparation?.ElementExprPlans;
        if (plans != null && plans.TryGetValue((srcElemType, tgtElemType), out var plan))
            return plan;
        // 兜底现场决策路径同传对级后缀表（ctx 通道）
        return GenerationPreparation.PlanElementExpr(srcElemType, tgtElemType, typeNameMap,
            ctx?.Preparation?.MethodSuffixMap);
    }

    /// <summary>
    /// 渲染单个元素的映射表达式字符串（W3_1 C2a 决策前移）— 按 Plan.Kind 纯模板渲染，
    /// 零判定；模板与原 GenerateElementMappingExpression 各分支逐字符一致（判定链已前移
    /// GenerationPreparation.PlanElementExpr，行为等价由逐字节基线锁定）。
    /// <para>处理：同类型直接赋值、隐式转换、可空转换、嵌套对象映射、其他回退 TypeConvertHelper</para>
    /// </summary>
    /// <param name="srcElemType">源元素类型符号（类型串渲染期读取）</param>
    /// <param name="tgtElemType">目标元素类型符号（类型串渲染期读取）</param>
    /// <param name="srcAccess">源元素访问表达式字符串</param>
    /// <param name="plan">元素映射路径计划（决策期产物）</param>
    /// <returns>映射后的表达式字符串</returns>
    private static string RenderElementMappingExpression(
        TypeRef srcElemType, TypeRef tgtElemType, string srcAccess,
        ElementExprPlan plan)
    {
        var srcTypeStr = srcElemType!.FullName;
        var tgtTypeStr = tgtElemType!.FullName;

        switch (plan.Kind)
        {
            // 同类型直接赋值
            case ElementExprKind.SameTypeDirect:
                return srcAccess;

            // 隐式数值转换（int→long 等）
            case ElementExprKind.ImplicitCast:
                return $"({tgtTypeStr}){srcAccess}";

            // 可空→非可空值类型
            case ElementExprKind.NullableToNonNullable:
                return $"{srcAccess}.HasValue ? {srcAccess}.Value : default({tgtTypeStr})";

            // 非可空→可空值类型且底层兼容
            case ElementExprKind.NonNullableToNullableCompatible:
                var underlyingTgt = TypeRefHelpers.GetUnderlyingType(tgtElemType)!;
                return $"new {tgtTypeStr}(({underlyingTgt.FullName}){srcAccess})";

            // 嵌套 class 映射 — 注册对走源生成器方法（方法名决策期派生入 Plan）
            case ElementExprKind.NestedRegistered:
                return $"GeneratedMapper.{plan.RegisteredMethodName}({srcAccess})!";

            // 嵌套未注册 → 运行时 Mapper.Map：
            // Mapper.Map 的 TSource/TTarget 带 class 约束，元素类型可空注解（如 ItemDto?）违反约束（CS8634）
            case ElementExprKind.NestedRuntimeFallback:
                var srcMapType = srcElemType.NonNullableFullName;
                var tgtMapType = tgtElemType.NonNullableFullName;
                return $"NCMapper.Mapper.Map<{srcMapType}, {tgtMapType}>({srcAccess})!";

            // 其他类型回退到 TypeConvertHelper（含非可空→可空底层不兼容回退）
            default:
                return $"NCMapper.TypeConvertHelper.ConvertValue<{srcTypeStr}, {tgtTypeStr}>({srcAccess})!";
        }
    }
}
