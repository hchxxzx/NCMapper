using NCMapper.Attributes;
using NCMapper.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;
using static NCMapper.ExpressionBuilder;

namespace NCMapper.Internal;

/// <summary>
/// 跨表导航策略 — AssignmentKind.CrossTable 执行器（④ 策略化深化）。
/// <para>[MapProperty] FromSource≠源类型 的跨表导航赋值（原 RenderPlan 分支）：
/// 四参消费 sourceType/attr/srcParam/targetParam——由 session.SourceType/attr 参数/
/// session.SourceParameter/session.TargetParameter 提供。</para>
/// <para>方法体由 ExpressionBuilder 迁入（逐行不动——经 using static 引用留守的
/// BuildConverterValue 共享 helper，迁移体零改动；:1143 特性查询随方法体逐行保留——
/// 迁移纪律优先于缓存化，缓存优化属后续扩展域）。</para>
/// <para>主循环点路径跨表分支（BuildMapBody）调用点改为直调本类同名 internal 方法。</para>
/// </summary>
internal sealed class CrossTableStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.CrossTable"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.CrossTable;

    /// <summary>
    /// 渲染跨表导航赋值表达式（导航解析 + 多级 null 安全链 + 跨表 [Convert] 消费）。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文——SourceType/SourceParameter/TargetParameter 为四参要素之三）</param>
    /// <param name="sourceItem">源属性（CrossTable 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（四参要素之四——FromSource/FromField 消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式；导航/字段未找到时返回 null（附 Trace，对齐 RenderPlan）</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 跨表导航解析：[MapProperty] 的 FromSource 指向非当前源类型
        // AU使用实际源类型 session.SourceType 而非 sourceItem.DeclaringType（继承属性的 DeclaringType 可能是基类）
        // srcParam/targetParam 由 session 直接提供（与 RenderPlan :386/:424 从 MemberExpression 提取等价）
        var crossExpr = BuildCrossTableAssignment(session.SourceType, targetItem, attr!, session.SourceParameter, session.TargetParameter);
        if (crossExpr != null)
            return crossExpr;
        MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：跨表导航解析失败，FromSource={attr!.FromSource.Name}，FromField={attr!.FromField}）");
        return null;
    }

    /// <summary>
    /// 构建跨表导航赋值表达式 — [MapProperty] 的 FromSource 指向非当前源类型时经导航属性取值
    /// <para>由 ExpressionBuilder 迁入（方法体逐行不动）；
    /// internal static——过渡期 RenderPlan（BuildAssignmentForProperty）与主循环点路径
    /// 跨表分支（BuildMapBody）限定调用。</para>
    /// </summary>
    /// <param name="sourceType">当前源对象类型</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（FromSource != sourceType）</param>
    /// <param name="sourceParameter">源对象参数表达式</param>
    /// <param name="targetParameter">目标对象参数表达式</param>
    /// <returns>赋值表达式；导航/字段未找到或类型不兼容时返回 null</returns>
    [RequiresUnreferencedCode("BuildCrossTableAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression? BuildCrossTableAssignment(Type sourceType, PropertyInfo targetItem,
        MapPropertyAttribute attr, ParameterExpression sourceParameter, ParameterExpression targetParameter)
    {
        // AU值类型导航防护 — FromSource 为 struct 时无法构造 null 安全访问（Expression.Constant(null, struct) 会抛 ArgumentException）
        if (!attr.FromSource.IsClass) return null;

        // 在源类型中查找类型为 attr.FromSource 的导航属性（按类型匹配，引用导航优先）
        var navProp = NavigationResolver.ResolveNavigationProperty(
            TypeHelper.GetPublicProperties(sourceType), attr.FromSource, sourceType.Name);
        if (navProp == null) return null;

        // 跨表字段拆段：FromField 含 '.' 时按段逐级解析
        // null 安全链覆盖"导航 + 路径段"两级（中间 null → default 不抛）；
        // 段缺失/中间节点值类型或 string → 返回 null（调用方 Trace 跳过，保持跨表失败行为）
        var segments = attr.FromField.Split('.');
        var navField = TypeHelper.GetPublicProperties(attr.FromSource)
            .FirstOrDefault(p => string.Equals(p.Name, segments[0], StringComparison.OrdinalIgnoreCase));
        if (navField == null) return null;

        var navAccess = Property(sourceParameter, navProp);
        // 各级访问表达式（0 = 导航；1..N = 路径段，含首段）— null 安全链按级从内向外包裹（单段时覆盖 nav）
        var levelAccess = new List<Expression> { navAccess };
        Expression fieldAccess = Property(navAccess, navField);
        levelAccess.Add(fieldAccess);
        var fieldType = navField.PropertyType;
        for (var i = 1; i < segments.Length; i++)
        {
            // 中间节点必须是 class 或 interface（非 string、非值类型，C3）
            if (fieldType.IsValueType || fieldType == typeof(string)) return null;
            var nextProp = TypeHelper.GetPublicProperties(fieldType)
                .FirstOrDefault(p => string.Equals(p.Name, segments[i], StringComparison.OrdinalIgnoreCase));
            if (nextProp == null || !nextProp.CanRead) return null;
            fieldAccess = Property(fieldAccess, nextProp);
            levelAccess.Add(fieldAccess);
            fieldType = nextProp.PropertyType;
        }
        var targetType = targetItem.PropertyType;

        // 跨表字段消费 [Convert]（source=子源导航表达式，输入=末段访问表达式）
        // TSource 校验基准=子源导航类型（BuildConverterValue 内部 IsAssignableFrom）；TField=末段类型（fieldAccess.Type）；
        // 导航 null → null 安全链短路 default(TTarget)（导航 null 裁决，与生成端 BuildComposeSourceAccess 的 ?. 链双端一致）
        var crossConverterAttr = PropertyIgnoreChecker.GetConvertAttribute(targetItem); // 收敛特性查询缓存族
        if (crossConverterAttr != null)
        {
            // 转换器调用 new Conv().Convert(末段访问, 子源导航)；无效转换器 BuildConverterValue 抛 MapException（与正向同源路径一致）
            var convExpr = BuildConverterValue(navField, targetItem, crossConverterAttr, fieldAccess, navAccess);
            if (convExpr != null)
            {
                // 若转换器 TTarget 与目标属性类型不同（多态基类），显式转换保证 Condition 类型一致
                if (convExpr.Type != targetType)
                    convExpr = Convert(convExpr, targetType);
                // 复用现有 null 安全链：导航链任一中间节点 null → default(TTarget)（短路，不触发转换调用）
                Expression convNullSafe = convExpr;
                for (var i = levelAccess.Count - 2; i >= 0; i--)
                {
                    var segAccess = levelAccess[i];
                    var nullCheck = NotEqual(segAccess, Constant(null, segAccess.Type));
                    convNullSafe = Condition(nullCheck, convNullSafe, Default(targetType));
                }
                return Assign(Property(targetParameter, targetItem), convNullSafe);
            }
            return null;
        }

        // 类型兼容性检查 + 转换
        if (fieldType != targetType)
        {
            if (TypeConverter.IsImplicitConvertible(fieldType, targetType))
                fieldAccess = Convert(fieldAccess, targetType);
            else if (TypeConverter.IsNullableType(targetType)
                     && Nullable.GetUnderlyingType(targetType) == fieldType)
                fieldAccess = Convert(fieldAccess, targetType); // T → T? 始终合法
            else
                return null;
        }

        // null 安全链（多级）：seg_i != null ? seg_{i+1}... : default(targetType)（从内向外包裹，与单段行为一致）
        Expression nullSafe = fieldAccess;
        for (var i = levelAccess.Count - 2; i >= 0; i--)
        {
            var segAccess = levelAccess[i];
            var nullCheck = NotEqual(segAccess, Constant(null, segAccess.Type));
            nullSafe = Condition(nullCheck, nullSafe, Default(targetType));
        }
        return Assign(Property(targetParameter, targetItem), nullSafe);
    }
}
