using Microsoft.CodeAnalysis;
using NCMapper.Shared;

namespace NCMapper.SourceGen;

// ================================================================
// TypeRef（值模型快照）→ TypeShape adapter（③）。
// 生成侧消费（CodeGenerator.Mapping 三处 HasMappingPath 调用点零改动）。
// 判据裁决全部在 TypeCompatibilityCore（C5 分离约束）；本类只做"模型 → TypeShape"映射。
//
// 行为口径（X-1/X-2 收敛承载点）：
// - null 输入 → Error 态（步骤 0 兜底 true，镜像原 HasMappingPath "null 不参与跳过"）；
// - 步骤 0b：剥离 Nullable<T> 包装的引用类型（原 TypeRefHelpers 私有方法迁入）；
// - 字典 K/V 提取性检查（I-4 对齐符号版：字典形状成立但 K/V 提取失败 → 步骤 6 false）；
// - X-1/X-2 分叉随核心化自动向符号版权威收敛（float/double→enum、string→非白名单值类型
//   生成侧判定由 true 变 false——新增跳过 + NCMAP027）。
// ================================================================

/// <summary>
/// TypeRef（Materialize 物化值模型）→ <see cref="TypeShape"/> 适配器。
/// <para>身份键 = TypeRef.EqualityKey（保留值模型既有等性语义——显示名承载、去可空注解；
/// 与符号版结构键的装配体盲差异为既有观察项，登记）。</para>
/// </summary>
internal static class TypeRefCompatAdapter
{
    /// <summary>
    /// 解析 TypeRef → TypeShape（步骤 0b 剥离后视图；null → Error 兜底）。
    /// </summary>
    /// <param name="type">类型引用快照（可为 null——物化缺失场景）</param>
    /// <returns>类型形状描述符</returns>
    internal static TypeShape Resolve(TypeRef? type)
    {
        // 0b：剥离引用类型的 Nullable<T> 包装（仅底层为引用类型时剥离——值类型 int? 保留）
        type = UnwrapNullableOfReferenceType(type);

        // null → Error 兜底（原 HasMappingPath :245：null 不参与跳过判定）
        if (type == null)
            return new TypeShape { Kind = TypeShapeKind.Error };

        var underlying = type.UnderlyingType ?? type;

        return new TypeShape
        {
            Kind = ResolveKind(type),
            SpecialTypeId = GetSpecialTypeId(type),
            IdentityKey = type.EqualityKey,
            IsValueType = type.IsValueType,
            IsEnum = type.Kind == TypeKind.Enum,
            IsNullableValueType = type.IsNullableValueType,
            IsGenericType = type.IsGenericType,
            ImplementsIEnumerable = type.ImplementsIEnumerable,
            IsDictionaryShape = type.IsDictionary,
            // I-4 对齐符号版：字典形状成立但 K/V 提取失败（非泛型字典）→ 步骤 6 false
            HasDictionaryKeyValue = type.IsDictionary
                                    && type.DictionaryKeyType != null
                                    && type.DictionaryValueType != null,
            HasCollectionElement = type.EnumerableElementType != null,
            UnderlyingSpecialTypeId = GetSpecialTypeId(underlying),
            UnderlyingIsEnum = underlying.Kind == TypeKind.Enum,
            UnderlyingIsString = underlying.SpecialType == SpecialType.System_String,
            UnderlyingIsValueType = underlying.IsValueType,
        };
    }

    /// <summary>
    /// TypeRef → <see cref="TypeCompatSpecialTypeId"/> 映射
    /// （TypeRefHelpers.IsImplicitConvertible 门面消费）。
    /// </summary>
    /// <param name="type">类型引用快照</param>
    /// <returns>特殊类型 id；非特殊类型返回 None</returns>
    internal static TypeCompatSpecialTypeId GetSpecialTypeId(TypeRef type)
        // Guid 识别：SimpleName + Namespace（TypeRef 无 ToDisplayString，等价于符号版全名口径）
        => type.SimpleName == "Guid" && type.Namespace == "System"
            ? TypeCompatSpecialTypeId.Guid
            : SpecialTypeCompatMap.Map(type.SpecialType);

    /// <summary>
    /// 形态解析（Kind 判定序：string 先于 Class——物化时 string 的 Kind 亦为 Roslyn TypeKind.Class；
    /// 字典/集合形态由标志承载，字典 class 保持 Kind=Class 以保留步骤 4 既有行为）。
    /// </summary>
    private static TypeShapeKind ResolveKind(TypeRef type)
    {
        if (type.Kind == TypeKind.Error) return TypeShapeKind.Error;
        if (type.SpecialType == SpecialType.System_String) return TypeShapeKind.String;
        if (type.Kind == TypeKind.Enum) return TypeShapeKind.Enum;
        if (type.Kind == TypeKind.Delegate) return TypeShapeKind.Delegate;
        if (type.Kind == TypeKind.Array) return TypeShapeKind.Collection;
        if (type.IsNullableValueType) return TypeShapeKind.Nullable;
        if (type.Kind == TypeKind.Interface) return TypeShapeKind.Interface;
        if (type.Kind == TypeKind.Class) return TypeShapeKind.Class;
        if (GetSpecialTypeId(type) != TypeCompatSpecialTypeId.None) return TypeShapeKind.Primitive;
        return TypeShapeKind.Other;
    }

    /// <summary>
    /// 剥离引用类型的 Nullable&lt;T&gt; 包装（原 TypeRefHelpers 私有方法迁入；
    /// 仅底层为引用类型时剥离——值类型 int? 保留）。
    /// </summary>
    private static TypeRef? UnwrapNullableOfReferenceType(TypeRef? t)
    {
        if (t != null && t.IsNullableValueType && t.UnderlyingType != null && !t.UnderlyingType.IsValueType)
            return t.UnderlyingType;
        return t;
    }
}
