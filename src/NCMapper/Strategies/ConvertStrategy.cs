using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper.Internal;

/// <summary>
/// 类型转换策略 — AssignmentKind.Convert 执行器（④ 策略化深化， 试点）。
/// <para>两分支对齐原 RenderPlan :399-404：IsNullableConditional=true 走可空条件转换
/// （BuildNullableConditionalAssignment），其余走 ConvertValue 转换路径
/// （BuildTypeConversionAssignment）。</para>
/// <para>三方法（BuildNullableConditionalAssignment / BuildTypeConversionAssignment /
/// BuildEnumToStringSwitch）方法体由 ExpressionBuilder 整体迁入（
/// 选项 1 方法体迁移，逐行不动——仅迁移宿主类变化，git diff 逐行核验）。</para>
/// </summary>
internal sealed class ConvertStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Convert"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Convert;

    /// <summary>
    /// 渲染类型转换赋值表达式（可空条件 / 隐式转换 / 枚举→string / 运行时转换四形态）。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（Convert 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（Convert 分支不消费）</param>
    /// <param name="plan">赋值计划（IsNullableConditional 标记决定分支）</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // 可空→非可空值类型走条件转换；其余走 ConvertValue 转换路径
        // IsConvertedFromConvertUsing=true 时走 BuildTypeConversionAssignment（与现状 ConvertValue 等价）
        if (plan.IsNullableConditional)
            return BuildNullableConditionalAssignment(sourceItem!, targetItem, sourceProperty, targetProperty);
        return BuildTypeConversionAssignment(sourceItem!, targetItem, sourceProperty, targetProperty);
    }

    /// <summary>
    /// 可空→非可空值类型条件转换赋值（计划器 Kind=Convert + IsNullableConditional 的执行器）—
    /// T1 形态语义：追加值类型限定，排除 string 等引用类型目标
    /// <para>由 ExpressionBuilder 迁入（方法体逐行不动）；
    /// internal static——过渡期 RenderPlan（BuildAssignmentForProperty）限定调用，
    /// RenderPlan 删除后由本类 BuildExpression 独占消费。</para>
    /// <para>A1-04孪生点收敛：条件表达式构建上提
    /// <see cref="BuildNullableConditionalValue"/> 共享单点（与 MapperBase.MemberInit 快路径
    /// 预检 + Conditional 构建双点位同源），本方法仅保留 Assign 包装；
    /// 形态以本类位为准（初判 MapperBase 位经 G2 门禁证伪——本位快照测试形态锁定，
    /// 改判与等价性论证见 BuildNullableConditionalValue 注释）。</para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Expression.Property(Expression, string) resolves the property via reflection. Some members may be trimmed.")]
    internal static Expression? BuildNullableConditionalAssignment(PropertyInfo sourceItem, PropertyInfo targetItem,
        Expression sourceProperty, Expression targetProperty)
    {
        var conditionItem = BuildNullableConditionalValue(sourceItem, targetItem, sourceProperty);
        return Assign(targetProperty, conditionItem);
    }

    /// <summary>
    /// 可空→非可空值类型条件转换条件表达式共享构建（A1-04 单点）——
    /// 可转换性预检（MapConfigurationException）+ HasValue 相等条件 + 整体 Convert。
    /// 原 MapperBase.MemberInit 与本类
    /// BuildNullableConditionalAssignment 两处孪生实现收敛于此（消费方：两处）。
    /// <para>形态改判以本类（ConvertStrategy）位为准（G2 门禁证伪初判，偏差登记）——本位有
    /// StrategyPilotEquivalenceTests.ConvertStrategy_NullableConditional_MatchesSnapshot 形态锁定
    /// 快照（Equal(HasValue, true) 条件 + 整体 Convert + Default(target) 假分支），MapperBase 位
    /// 无形态锁定测试；两形态语义逐点等价（bool==true 恒等消解；真分支内 src.Value 非空故
    /// Convert(整源) 与 Convert(.Value) 同值；提升转换与 underlying 转换对非空值同值；
    /// 假分支 Default(target) 与 Default(U) 经外层 Convert 后同值），MapperBase 快路径树变化
    /// 经全量测试等价验证（等价性证据 = 全量测试，见 artifacts/t62 等价性登记）。</para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Expression.Property(Expression, string) resolves the property via reflection. Some members may be trimmed.")]
    internal static Expression BuildNullableConditionalValue(PropertyInfo sourceItem, PropertyInfo targetItem,
        Expression sourceProperty)
    {
        // 可转换性预检——无 coercion operator 的可空对
        //（DateTime?→long 等）原在 Expression.Convert 处构建期裸抛 InvalidOperationException，
        // 统一为 MapConfigurationException（含源/目标类型上下文）。预检用"同型探测 Convert"
        // 判定（与真实 Convert 走同一类型校验路径，零误拦：枚举对/显式数值对等现状可转换
        // 形态全部放行——int?→long / E?→E / long?→int 等既有成功行为不变；探测形态与
        // 本位实际节点一致，取 nullable 全型→target 整体探测）。
        try
        {
            _ = Expression.Convert(Expression.Default(sourceItem.PropertyType), targetItem.PropertyType);
        }
        catch (InvalidOperationException)
        {
            throw new MapConfigurationException(
                ErrorMessages.NullableConversionNotSupported(sourceItem.PropertyType.Name, targetItem.PropertyType.Name),
                sourceItem.PropertyType, targetItem.PropertyType);
        }

        var hasValue = Equal(Property(sourceProperty, "HasValue"), Constant(true));
        return Condition(hasValue, Convert(sourceProperty, targetItem.PropertyType), Default(targetItem.PropertyType));
    }

    /// <summary>
    /// 构建类型转换赋值表达式 — 从 BuildMapBody 拆分
    /// <para>隐式转换优先（编译为一条 IL 指令）</para>
    /// <para>枚举→字符串：用 Expression.Switch 生成整数跳转返回字符串字面量，对齐源生成器性能</para>
    /// <para>其他非隐式转换走 TypeConvertHelper 运行时转换</para>
    /// </summary>
    /// <param name="sourceItem">源属性</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="sourceProperty">源属性表达式</param>
    /// <param name="targetProperty">目标属性表达式</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildTypeConversionAssignment builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildTypeConversionAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression BuildTypeConversionAssignment(PropertyInfo sourceItem, PropertyInfo targetItem,
        Expression sourceProperty, Expression targetProperty)
    {
        // 优化：基元类型隐式转换（int→long, int→double 等）直接用 Expression.Convert
        if (TypeConverter.IsImplicitConvertible(sourceItem.PropertyType, targetItem.PropertyType))
        {
            return Assign(targetProperty, Convert(sourceProperty, targetItem.PropertyType));
        }

        // 优化：枚举→字符串 — 生成 Expression.Switch 整数跳转返回字符串字面量（对齐源生成器路径）
        var srcUnderlying = Nullable.GetUnderlyingType(sourceItem.PropertyType) ?? sourceItem.PropertyType;
        var tgtUnderlying = Nullable.GetUnderlyingType(targetItem.PropertyType) ?? targetItem.PropertyType;

        if (srcUnderlying.IsEnum && tgtUnderlying == typeof(string))
        {
            return Assign(targetProperty, BuildEnumToStringSwitch(sourceProperty, srcUnderlying));
        }

        // 非隐式转换（string→int 等）走 TypeConvertHelper 运行时转换
        var helperMethod = ReflectionHelper.GetRequiredMethod(typeof(TypeConvertHelper), nameof(TypeConvertHelper.ConvertValue))
            .MakeGenericMethod(sourceItem.PropertyType, targetItem.PropertyType);
        var convertExpression = Call(null, helperMethod, sourceProperty);
        return Assign(targetProperty, convertExpression);
    }

    /// <summary>
    /// 构建枚举→字符串的 Switch 表达式 — 编译为整数比较 + 跳转返回字符串字面量
    /// <para>与源生成器生成的 switch 表达式等价，消除 Convert.ChangeType + 装箱开销</para>
    /// </summary>
    /// <param name="sourceProperty">枚举源属性表达式</param>
    /// <param name="enumType">枚举类型（已去除 Nullable）</param>
    /// <returns>返回字符串的 Switch 表达式</returns>
    [RequiresDynamicCode("BuildEnumToStringSwitch builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildEnumToStringSwitch builds expressions via Expression.Property(string) and reflection over enum types. Some members may be trimmed.")]
    internal static Expression BuildEnumToStringSwitch(Expression sourceProperty, Type enumType)
    {
        var enumNames = Enum.GetNames(enumType);
        var enumValues = Enum.GetValues(enumType);

        // 别名枚举值冲突检测（与源生成器同条件同行为）
        // 冲突时退化为 ToString() 调用：Expression.Switch 对同值不同名成员虽可编译，
        // 但行为为静默歧义映射（B→"A"，取第一个成员名）；退化后输出 = Enum.ToString()（同样取第一个名字），
        // 与源生成器退化路径输出一致，且消除歧义映射的静默性（行为由 ToString 契约定义）。
        var distinctValues = new HashSet<object>();
        var hasValueCollision = false;
        for (var i = 0; i < enumValues.Length; i++)
        {
            if (!distinctValues.Add(Enum.ToObject(enumType, enumValues.GetValue(i)!))) { hasValueCollision = true; break; }
        }

        var cases = new List<SwitchCase>();

        // T1 落点：Nullable<enum> 输入（可空→string 转换路径）时，
        // Expression.Switch 要求 case 值与 switch 值类型一致——case 值须提升为 Nullable<enum>。
        var isNullableInput = sourceProperty.Type.IsGenericType
            && sourceProperty.Type.GetGenericTypeDefinition() == typeof(Nullable<>);

        // 默认分支：调用 ToString() 作为兜底（理论上不会触发）
        var toStringMethod = typeof(Enum).GetMethod(nameof(Enum.ToString), Type.EmptyTypes)!;
        Expression defaultBody;
        if (isNullableInput)
        {
            // Nullable 输入：不能直接 Call(Enum.ToString)（Nullable 不是 Enum 实例）——
            // 无值 → ""（与生成侧 Nullable<enum>→string 的 "" 语义一致）；
            // 有值 → Value.ToString()（枚举名称；未定义数值 → 数字字符串）
            defaultBody = Condition(
                Property(sourceProperty, "HasValue"),
                Call(Property(sourceProperty, "Value"), toStringMethod),
                Constant("", typeof(string)));
        }
        else
        {
            defaultBody = Call(sourceProperty, toStringMethod);
        }

        if (hasValueCollision)
        {
            // 别名枚举退化：直接返回 ToString()（与源生成器退化路径输出一致）
            return defaultBody;
        }

        var caseValueType = isNullableInput ? sourceProperty.Type : enumType;

        for (var i = 0; i < enumValues.Length; i++)
        {
            var value = Enum.ToObject(enumType, enumValues.GetValue(i)!);
            var name = enumNames[i];
            var testValue = isNullableInput
                ? (Expression)Convert(Constant(value, enumType), caseValueType)
                : Constant(value, enumType);
            cases.Add(SwitchCase(
                Constant(name, typeof(string)),
                testValue
            ));
        }

        return Switch(sourceProperty, defaultBody, cases.ToArray());
    }
}
