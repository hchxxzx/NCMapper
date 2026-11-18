using System.Linq.Expressions;
namespace NCMapper;
/// <summary>
/// 类型转换策略 — 隐式转换判断、可空类型判断、类型兼容性检查
/// <para>从 MapperBase 提取，减少泛型类的 JIT 编译副本</para>
/// </summary>
internal static class TypeConverter
{


    /// <summary>
    /// 判断源类型到目标类型是否可隐式转换（基元类型拓宽转换）
    /// int→long, int→double, float→double 等
    /// </summary>
    /// <param name="sourceType">源类型</param>
    /// <param name="targetType">目标类型</param>
    /// <returns>如果源类型可隐式转换为目标类型则返回true，否则返回false</returns>
    public static bool IsImplicitConvertible(Type sourceType, Type targetType)
    {
        // Nullable 剥壳归一——Nullable<T> 自身 SpecialTypeId
        // 为 None（TypeCompatAdapter），原判定对 int?→long? 等可空对恒 false → Block 渲染落
        // ConvertValue 装箱慢路径、MemberInit 走"值→可空"Convert 快路径，双引擎分叉。
        // 剥壳后按底层基元对判定（int?→long? ≡ int→long），两侧引擎同走 Expression.Convert。
        sourceType = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!sourceType.IsValueType || !targetType.IsValueType) return false;
        if (sourceType.IsEnum || targetType.IsEnum) return false;
        if (sourceType == targetType) return true;

        // 转换表单点——TypeCompatibilityCore.ImplicitConversions
            //（原 T-1 表迁移，与 T-2/T-3 三表全量比对等价，D1.2）
            return NCMapper.Shared.TypeCompatibilityCore.ImplicitConversions
                       .TryGetValue(NCMapper.Shared.TypeCompatAdapter.GetSpecialTypeId(sourceType), out var targets)
                   && targets.Contains(NCMapper.Shared.TypeCompatAdapter.GetSpecialTypeId(targetType));
    }

    /// <summary>
    /// 获取 Nullable&lt;T&gt; 的底层类型 T；非 Nullable 返回原类型
    /// </summary>
    /// <param name="type">要获取底层类型的类型</param>
    /// <returns>Nullable&lt;T&gt;的底层类型 T；如果非Nullable类型则返回原类型</returns>
    public static Type GetUnderlyingType(Type type)
    {
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    /// <summary>
    /// 判断类型是否为 Nullable&lt;T&gt;
    /// </summary>
    /// <param name="type">要判断的类型</param>
    /// <returns>如果是Nullable&lt;T&gt;类型返回true，否则返回false</returns>
    public static bool IsNullableType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }
}
