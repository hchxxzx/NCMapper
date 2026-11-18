using NCMapper.Attributes;
using NCMapper.Shared;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;
using static NCMapper.ExpressionBuilder;

namespace NCMapper.Internal;

/// <summary>
/// 集合赋值策略 — AssignmentKind.Collection 执行器（④ 策略化深化）。
/// <para>两分支对齐原 RenderPlan :406-410：同类型集合走浅拷贝/复制路径
/// （BuildSameTypeCollectionAssignment），异型走元素转换（BuildCrossTypeAssignment 集合分支）。</para>
/// <para>两方法（BuildSameTypeCollectionAssignment:1411 / BuildCrossTypeAssignment:1374）方法体由
/// ExpressionBuilder 整体迁入（逐行不动——经 using static 引用留守的 GetClassExpression/
/// GetDictionaryExpression/GetListExpression/ConvertListExpression，迁移体零改动）。</para>
/// </summary>
internal sealed class CollectionStrategy : IAssignmentStrategy
{
    /// <summary>
    /// 该策略处理的赋值类别 — <see cref="AssignmentKind.Collection"/>。
    /// </summary>
    public AssignmentKind Kind => AssignmentKind.Collection;

    /// <summary>
    /// 渲染集合赋值表达式（同型浅拷贝 / 异型字典转换 / 异型列表元素转换）。
    /// </summary>
    /// <param name="session">构建会话（不可变上下文）</param>
    /// <param name="sourceItem">源属性（Collection 必非空——主循环 sourceItem 非空不变式）</param>
    /// <param name="targetItem">目标属性</param>
    /// <param name="attr">[MapProperty] 特性（Collection 分支不消费）</param>
    /// <param name="plan">赋值计划</param>
    /// <returns>赋值表达式</returns>
    [RequiresDynamicCode("BuildExpression builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildExpression scans type members and attributes via reflection. Some members may be trimmed.")]
    public Expression? BuildExpression(in BuildSession session, PropertyInfo? sourceItem, PropertyInfo targetItem,
        MapPropertyAttribute? attr, AssignmentPlan plan)
    {
        // 两行前奏上提共享 helper（原 Property(SourceParameter/TargetParameter) 两行，语义逐字不变）
        var (sourceProperty, targetProperty) = AssignmentStrategyResolver.BuildPropertyPair(session, sourceItem, targetItem);
        // 同型集合走浅拷贝/复制路径；异型集合走元素转换
        if (sourceItem!.PropertyType == targetItem.PropertyType)
        {
            // 同型字典统一走 ConvertListExpression
            // Dictionary<Item,Item> 等复合键字典原误入 GetListExpression 元素递归路径
            //（GetGenericArguments()[0] 取 TKey 当元素类型 → Expression.Call(IEnumerable<TSource>)
            // 参数不匹配 → 构建期裸抛 ArgumentException）；ConvertListExpression 内
            // IsGenericDictionary 分派 CopyDictionary/CopyConcurrentDictionary/CopyDictionaryByKeyValue
            //（K/V 深拷贝 + DeepCopyContext 循环引用检测），值类型键值对现状同径（行为不变）。
            if (CollectionTypeDetector.IsDictionary(sourceItem.PropertyType))
            {
                var dictExpression = ConvertListExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
                return Assign(targetProperty, dictExpression);
            }
            return BuildSameTypeCollectionAssignment(sourceItem, targetItem, sourceProperty, targetProperty);
        }
        return BuildCrossTypeAssignment(sourceItem, targetItem, sourceProperty, targetProperty);
    }

    /// <summary>
    /// 构建异类型（class/集合）赋值表达式 — 从 BuildMapBody 拆分
    /// <para>由 ExpressionBuilder 迁入（方法体逐行不动）；
    /// internal static——过渡期 RenderPlan（BuildAssignmentForProperty）限定调用。</para>
    /// </summary>
    [RequiresDynamicCode("BuildCrossTypeAssignment builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildCrossTypeAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression? BuildCrossTypeAssignment(PropertyInfo sourceItem, PropertyInfo targetItem,
        Expression sourceProperty, Expression targetProperty)
    {
        //数据类型不一致的：判断都是(非泛型、非数组)class
        if (sourceItem.PropertyType.IsClass && targetItem.PropertyType.IsClass
            && !sourceItem.PropertyType.IsArray && !targetItem.PropertyType.IsArray
            && !sourceItem.PropertyType.IsGenericType && !targetItem.PropertyType.IsGenericType)
        {
            var expression = GetClassExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
            return Assign(targetProperty, expression);
        }

        //集合数组类型的转换
        if (typeof(IEnumerable).IsAssignableFrom(sourceItem.PropertyType) && typeof(IEnumerable).IsAssignableFrom(targetItem.PropertyType))
        {
            // 判断是否为字典类型 — 统一使用 CollectionTypeDetector
            if (CollectionTypeDetector.IsDictionary(sourceItem.PropertyType))
            {
                var expression = GetDictionaryExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
                return Assign(targetProperty, expression);
            }
            else
            {
                var expression = GetListExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
                return Assign(targetProperty, expression);
            }
        }

        // 不匹配任何异类型分支 → 回退到主流程后续判断（返回 null 让调用方继续）
        return null;
    }

    /// <summary>
    /// 构建同类型集合赋值表达式 — 从 BuildMapBody 拆分
    /// <para>由 ExpressionBuilder 迁入（方法体逐行不动）；
    /// internal static——过渡期 RenderPlan（BuildAssignmentForProperty）限定调用。</para>
    /// </summary>
    [RequiresDynamicCode("BuildSameTypeCollectionAssignment builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("BuildSameTypeCollectionAssignment scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static Expression? BuildSameTypeCollectionAssignment(PropertyInfo sourceItem, PropertyInfo targetItem,
        Expression sourceProperty, Expression targetProperty)
    {
        // 判断元素类型是否为简单类型（值类型/枚举/string），是则走浅拷贝路径
        var args = sourceItem.PropertyType.GetGenericArguments();
        bool isSimpleElement;
        if (sourceItem.PropertyType.IsArray)
        {
            var elemType = sourceItem.PropertyType.GetElementType();
            isSimpleElement = elemType != null && (elemType.IsValueType || elemType.IsEnum || elemType == typeof(string));
        }
        else
        {
            isSimpleElement = args.Any(x => x.IsEnum || x.IsValueType || x == typeof(string));
        }
        if (isSimpleElement)
        {
            var expression = ConvertListExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
            return Assign(targetProperty, expression);
        }
        else
        {
            var expression = GetListExpression(sourceProperty, sourceItem.PropertyType, targetItem.PropertyType);
            return Assign(targetProperty, expression);
        }
    }
}
