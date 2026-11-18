using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Shared;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper;

internal static partial class ExpressionBuilder
{
    /// <summary>
    /// 构建 [Convert] 自定义转换器值表达式 — 供单标赋值与双标组合复用
    /// <para>返回 global 静态调用 M(字段值)/M(源对象)/M(源对象, 字段值)/M()（不含赋值包装），
    /// 双标场景（[Convert] + [Mask] 共存）由调用方将返回值作为 [Mask] 的输入，实现"先转换再脱敏"。</para>
    /// </summary>
    /// <param name="sourceItem">源属性</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="converterAttr">[Convert] 特性</param>
    /// <param name="sourceProperty">源属性表达式（同源/扁平化=主源属性；跨表=子源末段访问表达式）</param>
    /// <param name="sourceParameter">转换器 source 参数表达式（同源/扁平化=主源参数 ParameterExpression；
    /// 跨表=子源导航表达式）——TSource 基准按本表达式实际类型</param>
    [RequiresUnreferencedCode("BuildConverterValue resolves the converter static method via reflection (ResolveStaticMethod). Some methods may be trimmed.")]
    internal static Expression? BuildConverterValue(PropertyInfo sourceItem, PropertyInfo targetItem,
        ConvertAttribute converterAttr, Expression sourceProperty, Expression sourceParameter)
        => BuildStaticConverterValue(
            converterAttr.DeclaringType
                // 单参形态锚定类型：标注该特性的属性所属类型——特性查询顺序与调用方一致（targetItem 优先，对侧消费兜底）
                ?? (targetItem.IsDefined(typeof(ConvertAttribute)) ? targetItem.DeclaringType! : sourceItem.DeclaringType!),
            converterAttr.MethodName, sourceProperty, sourceParameter, targetItem.PropertyType, targetItem.Name, isReverse: false);

    /// <summary>
    /// 构建 [ConvertBack] 反向自定义转换器值表达式
    /// <para>仅反向（DTO→Entity）路径消费。基准换向（基准类型表）：TSource 基准 = DTO 类型
    /// （sourceParameter 实际类型）、TField 基准 = DTO 属性类型、TTarget = 实体属性类型。</para>
    /// </summary>
    /// <param name="sourceItem">源属性（反向映射中 = DTO 属性）</param>
    /// <param name="targetItem">目标属性（反向映射中 = 实体属性）</param>
    /// <param name="converterAttr">[ConvertBack] 特性</param>
    /// <param name="sourceProperty">DTO 属性表达式</param>
    /// <param name="sourceParameter">反向源参数表达式（DTO 类型 ParameterExpression）</param>
    [RequiresUnreferencedCode("BuildBackConverterValue resolves the converter static method via reflection (ResolveStaticMethod). Some methods may be trimmed.")]
    internal static Expression? BuildBackConverterValue(PropertyInfo sourceItem, PropertyInfo targetItem,
        ConvertBackAttribute converterAttr, Expression sourceProperty, Expression sourceParameter)
        => BuildStaticConverterValue(
            converterAttr.DeclaringType
                // 单参形态锚定类型：反向规范位置 = DTO 属性（sourceItem）优先，实体叶属性兜底（与调用方查询顺序一致）
                ?? (sourceItem.IsDefined(typeof(ConvertBackAttribute)) ? sourceItem.DeclaringType! : targetItem.DeclaringType!),
            converterAttr.MethodName, sourceProperty, sourceParameter, targetItem.PropertyType, targetItem.Name, isReverse: true);

    /// <summary>
    /// 方法引用形态值表达式组装— ResolveStaticMethod 解析 + 按模板组装实参（含 Nullable 守卫/提升）
    /// </summary>
    /// <param name="anchorType">锚定类型（双参=特性 DeclaringType；单参=标注属性所属类型，调用方已解析）</param>
    /// <param name="methodName">方法名</param>
    /// <param name="sourceProperty">字段值表达式（基准 = 其实际类型：索引路径=元素类型、跨表=子源末段、反向=DTO 属性）</param>
    /// <param name="sourceParameter">源对象表达式（基准 = 其实际类型：主源/子源/DTO）</param>
    /// <param name="targetType">目标属性类型</param>
    /// <param name="propertyName">属性名（错误消息）</param>
    /// <param name="isReverse">是否反向（错误消息方向上下文）</param>
    [RequiresUnreferencedCode("BuildStaticConverterValue resolves the converter static method via reflection (ResolveStaticMethod). Some methods may be trimmed.")]
    private static Expression BuildStaticConverterValue(Type anchorType, string methodName,
        Expression sourceProperty, Expression sourceParameter, Type targetType, string propertyName, bool isReverse)
    {
        var (method, template) = ResolveStaticMethod(anchorType, methodName, sourceProperty.Type,
            sourceParameter.Type, targetType, propertyName, isReverse);
        return AssembleTemplateCall(method, template, sourceProperty, sourceParameter, targetType);
    }

    /// <summary>
    /// 运行时静态转换方法解析器（反射侧实现）
    /// <para>R2 锚定类型（开放泛型拒绝）→ R3 DeclaredOnly+Static+可访问性白名单 → R4 名称 Ordinal →
    /// R5 形态排除 → R6 四模板+Q3 优先序+相等匹配（Nullable 解包，不做 assignable 放宽）。
    /// 与源生成端 MappingExtractor.ResolveConverterMethod 逐条同规则（零分叉， 矩阵锁定）。</para>
    /// </summary>
    /// <param name="anchorType">锚定类型</param>
    /// <param name="methodName">方法名</param>
    /// <param name="sourceFieldType">源字段类型基准（相等匹配）；null = 无匹配源属性路径（R6-b：仅形态匹配）</param>
    /// <param name="sourceEntityType">源实体类型基准（反向 = DTO 类型，004 定稿）</param>
    /// <param name="targetType">目标属性类型基准</param>
    /// <param name="propertyName">属性名（错误消息）</param>
    /// <param name="isReverse">是否反向（错误消息方向上下文）</param>
    /// <returns>命中的方法与模板；失败抛 <see cref="MapException"/>（与 NCMAP035/036/037 同源文案）</returns>
    [RequiresUnreferencedCode("ResolveStaticMethod scans anchor type static methods via reflection (GetMethods). Some methods may be trimmed.")]
    internal static (MethodInfo Method, ConverterTemplate Template) ResolveStaticMethod(
        Type anchorType, string methodName, Type? sourceFieldType, Type sourceEntityType, Type targetType,
        string propertyName, bool isReverse)
    {
        // R2 细则 4：开放泛型锚定类型——方法参数含类型参数必然失配（反射口径：typeof(X<>) 为开放泛型定义）
        if (anchorType.IsGenericType && anchorType.ContainsGenericParameters)
            throw new MapException(ErrorMessages.ConverterMethodSignatureMismatch(propertyName, anchorType.Name, methodName,
                targetType.Name, "anchor type is an open generic type; use the two-argument form with a closed generic type (e.g. typeof(X<int>))",
                isReverse));

        // R3/锚定类型自身声明的静态方法（DeclaredOnly 不含基类继承；partial 天然合并），名称 Ordinal 精确匹配
        var sameName = new List<MethodInfo>();
        foreach (var m in anchorType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (string.Equals(m.Name, methodName, StringComparison.Ordinal))
                sameName.Add(m);
        }

        // NCMAP035 判据①：锚定类型上无该名称静态方法（名称不存在 / 仅有实例方法）
        if (sameName.Count == 0)
            throw new MapException(ErrorMessages.ConverterMethodInaccessible(propertyName, anchorType.Name, methodName, isReverse));

        // R3 可访问性白名单：public / internal（同程序集）/ protected internal（同程序集腿）；
        // protected / private protected / private 拒绝。显式判定（与生成端口径一致，IVT 不参与）
        var accessible = new List<MethodInfo>();
        foreach (var m in sameName)
        {
            if (m.IsPublic || ((m.IsAssembly || m.IsFamilyOrAssembly) && m.DeclaringType!.Assembly == anchorType.Assembly))
                accessible.Add(m);
        }

        // NCMAP035 判据②：存在同名静态方法但可见性不足
        if (accessible.Count == 0)
            throw new MapException(ErrorMessages.ConverterMethodInaccessible(propertyName, anchorType.Name, methodName, isReverse));

        // R5 形态排除（静默；失败时 NCMAP037 详情逐一登记）+ R6 模板匹配
        var matches = new List<MethodInfo>();
        var matchedTemplates = new List<ConverterTemplate>();
        var exclusionNotes = new List<string>();
        foreach (var candidate in accessible)
        {
            var ps = candidate.GetParameters();
            var signature = $"{candidate.Name}({string.Join(", ", ps.Select(p => p.ParameterType.Name))})";

            if (candidate.IsGenericMethod)
            {
                exclusionNotes.Add($"{signature}: generic method");
                continue;
            }
            if (ps.Any(p => p.HasDefaultValue))
            {
                exclusionNotes.Add($"{signature}: optional parameter");
                continue;
            }
            if (ps.Length > 0 && ps[ps.Length - 1].GetCustomAttribute<ParamArrayAttribute>() != null)
            {
                exclusionNotes.Add($"{signature}: params array");
                continue;
            }
            if (ps.Any(p => p.ParameterType.IsByRef))
            {
                exclusionNotes.Add($"{signature}: ref/out/in parameter");
                continue;
            }

            if (TryMatchConverterTemplate(candidate, sourceFieldType, sourceEntityType, targetType, out var template))
            {
                matches.Add(candidate);
                matchedTemplates.Add(template);
            }
            else
            {
                exclusionNotes.Add($"{signature}: signature mismatch (return '{candidate.ReturnType.Name}')");
            }
        }

        if (matches.Count == 0)
            throw new MapException(ErrorMessages.ConverterMethodSignatureMismatch(propertyName, anchorType.Name, methodName,
                targetType.Name, string.Join("; ", exclusionNotes), isReverse));

        if (matches.Count > 1)
            throw new MapException(ErrorMessages.ConverterMethodAmbiguous(propertyName, anchorType.Name, methodName, isReverse));

        return (matches[0], matchedTemplates[0]);
    }

    /// <summary>
    /// R6 四模板匹配 + Q3 优先序 + 相等匹配（Nullable 解包后完全相等，不做 assignable 放宽）
    /// <para>与源生成端 MappingExtractor.TryMatchConverterTemplate 逐条镜像（T2→T3→T1→T0 优先序）。</para>
    /// </summary>
    private static bool TryMatchConverterTemplate(MethodInfo candidate, Type? sourceFieldType,
        Type sourceEntityType, Type targetType, out ConverterTemplate template)
    {
        template = ConverterTemplate.None;

        // 返回类型口径：解包后 == 目标属性类型；void 拒绝；提升只允许「非空→可空」
        if (!ConverterReturnTypeMatches(candidate.ReturnType, targetType))
            return false;

        var ps = candidate.GetParameters();

        // static TTarget M(TSource) —— Q3 优先序：单参同时==源实体与源字段类型时判多字段
        if (ps.Length == 1 && TypeEqualsUnwrapped(ps[0].ParameterType, sourceEntityType))
        {
            template = ConverterTemplate.T2MultiField;
            return true;
        }

        // static TTarget M(TSource, TField)；R6-b（fieldType=null）不做字段类型交叉校验
        if (ps.Length == 2
            && TypeEqualsUnwrapped(ps[0].ParameterType, sourceEntityType)
            && (sourceFieldType == null || TypeEqualsUnwrapped(ps[1].ParameterType, sourceFieldType)))
        {
            template = ConverterTemplate.T3Combined;
            return true;
        }

        // static TTarget M(TField)；R6-b（fieldType=null）任意单参形态命中（字段值实参一律 default）
        if (ps.Length == 1 && (sourceFieldType == null || TypeEqualsUnwrapped(ps[0].ParameterType, sourceFieldType)))
        {
            template = ConverterTemplate.T1SingleValue;
            return true;
        }

        // static TTarget M()
        if (ps.Length == 0)
        {
            template = ConverterTemplate.T0Constant;
            return true;
        }

        return false; // 参数 ≥3 一律不匹配（NCMAP037）
    }

    /// <summary>
    /// 返回类型匹配（R6 口径 2）：解包后相等；void 拒绝；提升只允许「非空→可空」
    /// <para>（M 返回 DateTime、目标 DateTime? 允许；返回 DateTime?、目标 DateTime → 不匹配）</para>
    /// </summary>
    private static bool ConverterReturnTypeMatches(Type returnType, Type targetType)
    {
        if (returnType == typeof(void))
            return false;

        var retUnderlying = Nullable.GetUnderlyingType(returnType);
        var tgtUnderlying = Nullable.GetUnderlyingType(targetType);

        // 提升只允许「非空→可空」：返回 Nullable<T> + 目标 T → 不匹配
        if (retUnderlying != null && tgtUnderlying == null)
            return false;

        return (retUnderlying ?? returnType) == (tgtUnderlying ?? targetType);
    }

    /// <summary>
    /// 相等匹配（R6 口径 1）：两侧 Nullable&lt;T&gt; 解包后完全相等（仅解值类型 Nullable，引用类型可空标注不参与）
    /// </summary>
    private static bool TypeEqualsUnwrapped(Type a, Type b)
        => (Nullable.GetUnderlyingType(a) ?? a) == (Nullable.GetUnderlyingType(b) ?? b);

    /// <summary>
    /// 按模板组装静态调用表达式— 含字段值 Nullable 守卫/提升与返回类型提升
    /// <para>R6-c null 语义：字段 Nullable&lt;T&gt; 为 null → 不调用 M、目标保持默认（条件表达式短路）。</para>
    /// </summary>
    /// <param name="method">命中的静态方法</param>
    /// <param name="template">命中模板</param>
    /// <param name="fieldExpr">字段值表达式；null = R6-b 无匹配源属性（字段实参一律 default(方法参数类型)）</param>
    /// <param name="sourceParameter">源对象表达式（T2/T3 实体实参）</param>
    /// <param name="targetType">目标属性类型</param>
    [RequiresUnreferencedCode("AssembleTemplateCall builds field-guarded calls via string-based Expression.Property. Some members may be trimmed.")]
    private static Expression AssembleTemplateCall(MethodInfo method, ConverterTemplate template,
        Expression? fieldExpr, Expression sourceParameter, Type targetType)
    {
        var ps = method.GetParameters();
        Expression call;
        switch (template)
        {
            case ConverterTemplate.T0Constant:
                call = Call(method);
                break;
            case ConverterTemplate.T1SingleValue:
                // R6-b：无匹配源属性（fieldExpr=null）→ 字段实参 default(方法参数类型)
                call = fieldExpr == null
                    ? Call(method, Default(ps[0].ParameterType))
                    : BuildFieldGuardedCall(method, entityArg: null, fieldExpr);
                break;
            case ConverterTemplate.T2MultiField:
                call = Call(method, LiftArgument(sourceParameter, ps[0].ParameterType));
                break;
            case ConverterTemplate.T3Combined:
                call = fieldExpr == null
                    ? Call(method, LiftArgument(sourceParameter, ps[0].ParameterType), Default(ps[1].ParameterType))
                    : BuildFieldGuardedCall(method, LiftArgument(sourceParameter, ps[0].ParameterType), fieldExpr);
                break;
            default:
                call = Call(method);
                break;
        }

        // 返回类型提升（R6 口径 2）：M 返回 T、目标 T? → 显式转换（生成端隐式提升，值等价）
        if (call.Type != targetType && Nullable.GetUnderlyingType(targetType) == call.Type)
            call = Convert(call, targetType);
        return call;
    }

    /// <summary>
    /// 字段值实参组装 + null 守卫包裹（口径 3 / -c）
    /// <para>基准 Nullable&lt;T&gt; + 参数 T → 守卫（值 null 不调用 M，短路 default）；基准 T + 参数 Nullable&lt;T&gt; → 显式提升；否则直传。</para>
    /// </summary>
    [RequiresUnreferencedCode("BuildFieldGuardedCall builds HasValue/Value property access via string-based Expression.Property. Some members may be trimmed.")]
    private static Expression BuildFieldGuardedCall(MethodInfo method, Expression? entityArg, Expression fieldExpr)
    {
        var ps = method.GetParameters();
        var fieldParamType = ps[ps.Length - 1].ParameterType;

        if (fieldExpr.Type != fieldParamType)
        {
            // 提升：字段 T → 参数 Nullable<T>
            if (Nullable.GetUnderlyingType(fieldParamType) == fieldExpr.Type)
            {
                var liftedArgs = NewTemplateArgs(ps, entityArg, Convert(fieldExpr, fieldParamType));
                return Call(method, liftedArgs);
            }

            // 守卫：字段 Nullable<T> 为 null 不调用，否则 M(src, field.Value)
            if (Nullable.GetUnderlyingType(fieldExpr.Type) == fieldParamType)
            {
                var hasValue = Property(fieldExpr, nameof(Nullable<int>.HasValue));
                var guardedArgs = NewTemplateArgs(ps, entityArg, Property(fieldExpr, nameof(Nullable<int>.Value)));
                var guardedCall = Call(method, guardedArgs);
                return Condition(hasValue, guardedCall, Default(guardedCall.Type));
            }
        }

        return Call(method, NewTemplateArgs(ps, entityArg, fieldExpr));
    }

    /// <summary>组装模板实参数组（entityArg 供 T3；字段实参置于末位）</summary>
    private static Expression[] NewTemplateArgs(ParameterInfo[] ps, Expression? entityArg, Expression fieldArg)
    {
        var args = new Expression[ps.Length];
        if (ps.Length == 2)
            args[0] = entityArg!;
        args[ps.Length - 1] = fieldArg;
        return args;
    }

    /// <summary>
    /// 实体实参 Nullable 提升（R6 口径 3）：源对象 T → 参数 Nullable&lt;T&gt; 显式转换；相等直传
    /// </summary>
    private static Expression LiftArgument(Expression value, Type parameterType)
        => value.Type == parameterType || Nullable.GetUnderlyingType(parameterType) != value.Type
            ? value
            : Convert(value, parameterType);

    /// <summary>
    /// 构建 [Mask] 脱敏赋值表达式 — tgt.Prop = MaskProvider.Resolve(maskType).Mask(src.Prop)
    /// <para>使用 MaskProvider.Resolve(Type) 而非 New(maskType)，保证 Override 机制在运行时路径生效</para>
    /// </summary>
    /// <param name="maskAttr">目标属性上的 [Mask] 特性</param>
    /// <param name="sourceProperty">源属性表达式</param>
    /// <param name="targetProperty">目标属性表达式</param>
    /// <param name="targetItem">目标属性（用于错误消息）</param>
    /// <returns>赋值表达式</returns>
    [RequiresUnreferencedCode("BuildMaskAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression BuildMaskAssignment(MaskAttribute maskAttr, Expression sourceProperty,
        MemberExpression targetProperty, PropertyInfo targetItem)
        => BuildMaskAssignment(maskAttr.MaskType, sourceProperty, targetProperty, targetItem);

    /// <summary>
    /// 构建 [Mask] 脱敏赋值表达式 — Type 承载重载（特性腿经薄转发复用同一实现；
    /// 注册表腿渲染消费 plan 携带的解析就绪 Type 形态）。
    /// </summary>
    [RequiresUnreferencedCode("BuildMaskAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression BuildMaskAssignment(Type maskType, Expression sourceProperty,
        MemberExpression targetProperty, PropertyInfo targetItem)
    {
        var maskCall = BuildMaskValue(maskType, sourceProperty, targetItem);
        return Assign(targetProperty, maskCall);
    }

    /// <summary>
    /// 构建 [Mask] 脱敏值表达式 — 供单标赋值与双标组合复用
    /// <para>返回 MaskProvider.Resolve(maskType).Mask(sourceValue) 调用本身（不含赋值包装），
    /// 双标场景（[Convert] + [Mask] 共存）由调用方传入转换值表达式作为 sourceValue，
    /// 实现"先转换再脱敏"（与源生成器 CodeGenerator.Mapping.cs L538-543 对齐）。</para>
    /// </summary>
    /// <param name="maskAttr">目标属性上的 [Mask] 特性</param>
    /// <param name="sourceValue">待脱敏值表达式（单标=源属性；双标=转换结果）</param>
    /// <param name="targetItem">目标属性（用于错误消息）</param>
    /// <returns>脱敏调用表达式</returns>
    [RequiresUnreferencedCode("BuildMaskValue scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression BuildMaskValue(MaskAttribute maskAttr, Expression sourceValue, PropertyInfo targetItem)
        => BuildMaskValue(maskAttr.MaskType, sourceValue, targetItem);

    /// <summary>
    /// 构建 [Mask] 脱敏值表达式 — Type 承载重载（特性腿经薄转发复用同一实现；
    /// 注册表腿解析出 Type 后直接消费）。
    /// </summary>
    [RequiresUnreferencedCode("BuildMaskValue scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression BuildMaskValue(Type maskType, Expression sourceValue, PropertyInfo targetItem)
    {
        if (!typeof(IMask).IsAssignableFrom(maskType))
            throw new MapException(ErrorMessages.MaskTypeNotImplementIMask(targetItem.Name, maskType.Name));

        // MaskProvider.Resolve(Type maskType) — 非泛型重载
        var resolveMethod = typeof(MaskProvider).GetMethod(nameof(MaskProvider.Resolve), new[] { typeof(Type) })
            ?? throw new MapException(ErrorMessages.MaskResolveMethodNotFound(targetItem.Name));
        var resolveCall = Call(resolveMethod, Constant(maskType, typeof(Type)));

        // IMask.Mask(string?) 方法
        var maskMethod = typeof(IMask).GetMethod(nameof(IMask.Mask))
            ?? throw new MapException(ErrorMessages.MaskMaskMethodNotFound(targetItem.Name));

        // 待脱敏值需要转为 string?（源值可能是 object 或其他类型，但 Mask 分支仅在 target 为 string 时触发）
        Expression valueToMask = sourceValue;
        if (sourceValue.Type != typeof(string))
        {
            // 非 string 源防御——计划器已分流单标场景
            //（源非 string 落类型形态决策），此处兜底双标组合（ConvertUsingThenMask 转换器
            // 输出 TField 非 string）等未预期路径：ToString() 中转替代 Convert(源, string)
            //（无 coercion operator，构建期裸抛 InvalidOperationException）。
            var toStringMethod = typeof(object).GetMethod(nameof(object.ToString), Type.EmptyTypes)!;
            valueToMask = Call(sourceValue, toStringMethod);
        }

        return Call(resolveCall, maskMethod, valueToMask);
    }

    /// <summary>
    /// 构建 [MapFormat] 格式化值表达式 — 供单标赋值与双标组合复用
    /// <para>统一绑定 <see cref="IFormattable.ToString(string, IFormatProvider)"/> 接口双参重载
    /// （防接口声明源无单参重载编译不过，；值类型经 Convert 到接口装箱，枚举装箱后
    /// 接口调用 net10 实测可用）；provider 恒 null（= 当前文化，与未标注属性恒 Invariant 的
    /// 转换路径输出分叉为有意设计）。</para>
    /// <para>可空源（Nullable&lt;T&gt;）以 Condition(HasValue, ToString 调用, null 常量) 形态展开
    /// （null 源 → 目标 null；不走 NullableKind 分发链——Format 判定在 PlanTypeShape 之前短路，
    /// 可空处理在渲染点单点）。</para>
    /// <para>返回格式化调用表达式本身（不含赋值包装），双标场景（FormatThenMask）由调用方传入
    /// 脱敏消费——实现"先格式化再脱敏"。</para>
    /// </summary>
    /// <param name="sourceItem">源属性（用于错误消息）</param>
    /// <param name="targetItem">目标属性（用于错误消息）</param>
    /// <param name="formatAttr">目标属性上的 [MapFormat] 特性</param>
    /// <param name="sourceProperty">源属性表达式</param>
    /// <returns>格式化值表达式（string 类型）</returns>
    [RequiresUnreferencedCode("BuildFormatValue builds HasValue/Value property access via string-based Expression.Property. Some members may be trimmed.")]
    internal static Expression? BuildFormatValue(PropertyInfo sourceItem, PropertyInfo targetItem,
        MapFormatAttribute formatAttr, Expression sourceProperty)
    {
        // IFormattable.ToString(string, IFormatProvider) 接口双参重载（单点反射查找）
        var toStringMethod = typeof(IFormattable).GetMethod(nameof(IFormattable.ToString),
            new[] { typeof(string), typeof(IFormatProvider) })
            ?? throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name, "（IFormattable.ToString(string, IFormatProvider) 未找到——内部不变式违约）"));

        var formatArg = Constant(formatAttr.Format, typeof(string));
        var providerArg = Constant(null, typeof(IFormatProvider));

        // 可空源：Nullable<T> 接口面为空（net10 实测）——解包 Value 后装箱调用，
        // HasValue=false 时短路 null 常量（目标 string? 语义）
        if (Nullable.GetUnderlyingType(sourceProperty.Type) != null)
        {
            var hasValue = Property(sourceProperty, nameof(Nullable<int>.HasValue));
            var valueExpr = Property(sourceProperty, nameof(Nullable<int>.Value));
            var boxedValue = Convert(valueExpr, typeof(IFormattable));
            var guardedCall = Call(boxedValue, toStringMethod, formatArg, providerArg);
            return Condition(hasValue, guardedCall, Constant(null, typeof(string)));
        }

        // 接口声明源（如 public IFormattable X）直调；其余经 Convert 到接口（值类型=装箱，引用类型=castclass）
        var ifaceValue = sourceProperty.Type == typeof(IFormattable)
            ? sourceProperty
            : Convert(sourceProperty, typeof(IFormattable));

        return Call(ifaceValue, toStringMethod, formatArg, providerArg);
    }

    /// <summary>
    /// 构建 [Convert] 无匹配源属性时的转换器赋值表达式 — fieldValue 传 default(TField)
    /// <para>用于属性标注了 [Convert] 但无匹配源属性的场景，转换器通过 source 参数访问实体字段</para>
    /// <para> 方法引用形态：走共用解析器 -b 形态匹配（sourceFieldType=null 不做字段类型交叉校验，
    /// 字段值实参一律 default(方法参数类型)：T0→M()；T1→M(default)；T2→M(src)；T3→M(src, default)），
    /// 消除第三处接口构建复制点。</para>
    /// </summary>
    /// <param name="targetItem">目标属性</param>
    /// <param name="converterAttr">[Convert] 特性</param>
    /// <param name="targetProperty">目标属性表达式</param>
    /// <param name="sourceParameter">整个源对象的参数表达式</param>
    [RequiresUnreferencedCode("BuildConverterAssignmentForNullSource resolves the converter static method via reflection (ResolveStaticMethod). Some methods may be trimmed.")]
    private static Expression? BuildConverterAssignmentForNullSource(PropertyInfo targetItem,
        ConvertAttribute converterAttr, Expression targetProperty,
        ParameterExpression sourceParameter)
    {
        // 单参形态锚定类型：本路径特性仅从 targetItem 查询（:176），锚定即其所属类型
        var anchorType = converterAttr.DeclaringType ?? targetItem.DeclaringType!;
        var (method, template) = ResolveStaticMethod(anchorType, converterAttr.MethodName, null,
            sourceParameter.Type, targetItem.PropertyType, targetItem.Name, isReverse: false);
        var call = AssembleTemplateCall(method, template, fieldExpr: null, sourceParameter, targetItem.PropertyType);
        return Assign(targetProperty, call);
    }

    // BuildBackConverterAssignmentForNullSource 随反向无源 [ConvertBack] 暂挂链出清删除
}
