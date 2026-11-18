using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using NCMapper.Attributes;
using static System.Linq.Expressions.Expression;
namespace NCMapper;
/// <summary>
/// 表达式树构建 — 薄转发层，实际逻辑在 ExpressionBuilder 中
/// </summary>
internal partial class MapperBase<TSource, TTarget> where TTarget : class
{
    /// <summary>
    /// 获取委托 — 核心表达式树构建，编译为 Action&lt;TSource, TTarget&gt;
    /// </summary>
    /// <returns>编译后的映射委托</returns>
    [RequiresDynamicCode("GetMapAction builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT. Use the source generator to ensure AOT compatibility.")]
    [RequiresUnreferencedCode("GetMapAction scans type members and attributes via reflection. Some members may be trimmed. Use the source generator to ensure AOT compatibility.")]
    private static Action<TSource, TTarget> GetMapAction()
    {
        var sourceType = typeof(TSource);
        var targetType = typeof(TTarget);
        var sourceParameter = Parameter(sourceType, "p");
        var targetParameter = Parameter(targetType, "t");

        // 获取赋值表达式列表（转发到 ExpressionBuilder）
        var expressions = GetMapBody(sourceParameter, targetParameter);

        // 当 source != null 时执行赋值
        var testSource = NotEqual(sourceParameter, Constant(null, sourceType));
        var ifTrueSource = Block(expressions);
        var conditionSource = IfThen(testSource, ifTrueSource);

        // 当 target != null 时执行上述逻辑
        var testTarget = NotEqual(targetParameter, Constant(null, targetType));
        var conditionTarget = IfThen(testTarget, conditionSource);

        var lambda = Lambda<Action<TSource, TTarget>>(conditionTarget, sourceParameter, targetParameter);
        return lambda.Compile();
    }


    /// <summary>
    /// 构建属性赋值表达式列表 — 转发到 ExpressionBuilder.BuildMapBody
    /// <para>[Mask] 脱敏双腿生效：特性腿（[Mask] 特性）优先分派；特性腿未命中时注册表腿按
    /// 注册表明细的脱敏类型名解析 Type 施加（解析域 = 类型对两侧程序集）。两腿互斥，
    /// 同一属性不双重施加</para>
    /// </summary>
    /// <param name="sourceParameter">源对象参数表达式</param>
    /// <param name="targetParameter">目标对象参数表达式</param>
    /// <returns>赋值表达式列表</returns>
    [RequiresDynamicCode("GetMapBody builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("GetMapBody scans type members and attributes via reflection. Some members may be trimmed.")]
    private static List<Expression> GetMapBody(ParameterExpression sourceParameter, ParameterExpression targetParameter)
    {
        return ExpressionBuilder.BuildMapBody(typeof(TSource), typeof(TTarget), sourceParameter, targetParameter);
    }
}
