using System.Text;
using Microsoft.CodeAnalysis;
using NCMapper.Shared;

namespace NCMapper.SourceGen;

// ================================================================
// ITypeSymbol 模型 → TypeShape adapter（③）。
// 符号版权威侧——TypeHelpers.HasMappingPath 门面（签名保留）与诊断链消费。
// 判据裁决全部在 TypeCompatibilityCore（C5 分离约束）；本类只做"模型 → TypeShape"映射。
//
// 行为口径：
// - 步骤 0b：剥离 Nullable<T> 包装的引用类型（#nullable disabled 下可空引用解析为值类型
//   Nullable<T>，原 TypeHelpers.UnwrapNullableOfReferenceType 语义迁入）；
// - 步骤 0 源：TypeKind.Error → TypeShapeKind.Error（C02 回归根因，解包后判定）；
// - 身份键：装配体敏感的结构键（SymbolEqualityComparer.Default 语义的字符串承载）。
// ================================================================

/// <summary>
/// Roslyn <see cref="SpecialType"/> → <see cref="TypeCompatSpecialTypeId"/> 共享映射
/// （Symbol/TypeRef 双 adapter 与 TypeHelpers/TypeRefHelpers 门面消费）。
/// <para>Guid 在 Roslyn SpecialType 中为 None，由各 adapter 按模型原生口径单独识别。</para>
/// </summary>
internal static class SpecialTypeCompatMap
{
    /// <summary>SpecialType → 核心 id 映射（Guid 不在此列——各 adapter 自行识别）</summary>
    internal static TypeCompatSpecialTypeId Map(SpecialType specialType) => specialType switch
    {
        SpecialType.System_Boolean => TypeCompatSpecialTypeId.Boolean,
        SpecialType.System_Char => TypeCompatSpecialTypeId.Char,
        SpecialType.System_SByte => TypeCompatSpecialTypeId.SByte,
        SpecialType.System_Byte => TypeCompatSpecialTypeId.Byte,
        SpecialType.System_Int16 => TypeCompatSpecialTypeId.Int16,
        SpecialType.System_UInt16 => TypeCompatSpecialTypeId.UInt16,
        SpecialType.System_Int32 => TypeCompatSpecialTypeId.Int32,
        SpecialType.System_UInt32 => TypeCompatSpecialTypeId.UInt32,
        SpecialType.System_Int64 => TypeCompatSpecialTypeId.Int64,
        SpecialType.System_UInt64 => TypeCompatSpecialTypeId.UInt64,
        SpecialType.System_Decimal => TypeCompatSpecialTypeId.Decimal,
        SpecialType.System_Single => TypeCompatSpecialTypeId.Single,
        SpecialType.System_Double => TypeCompatSpecialTypeId.Double,
        SpecialType.System_String => TypeCompatSpecialTypeId.String,
        SpecialType.System_DateTime => TypeCompatSpecialTypeId.DateTime,
        _ => TypeCompatSpecialTypeId.None,
    };
}

/// <summary>
/// ITypeSymbol（Roslyn 符号模型）→ <see cref="TypeShape"/> 适配器。
/// <para>身份键为装配体敏感的结构键：键相等 ? 符号相等（SymbolEqualityComparer.Default 语义，
/// 不产生步骤 1 假阳性）；符号相等 ? 键相等（可空注解不进键——与比较器忽略 Nullability 一致）。</para>
/// </summary>
internal static class SymbolCompatAdapter
{
    /// <summary>
    /// 解析 ITypeSymbol → TypeShape（步骤 0b 剥离后视图；Error 态经 TypeKind.Error 承载）。
    /// </summary>
    /// <param name="type">类型符号</param>
    /// <returns>类型形状描述符</returns>
    internal static TypeShape Resolve(ITypeSymbol type)
    {
        // 0b：剥离 Nullable<T> 包装的引用类型（可空引用在 #nullable disabled 上下文下
        // 被解析为值类型 Nullable<T>；剥离后按引用类型判定）
        type = UnwrapNullableOfReferenceType(type);
        var underlying = TypeHelpers.GetUnderlyingType(type);

        return new TypeShape
        {
            Kind = ResolveKind(type),
            SpecialTypeId = GetSpecialTypeId(type),
            IdentityKey = BuildIdentityKey(type),
            IsValueType = type.IsValueType,
            IsEnum = type.TypeKind == TypeKind.Enum,
            IsNullableValueType = TypeHelpers.IsNullableValueType(type),
            IsGenericType = type is INamedTypeSymbol named && named.IsGenericType,
            ImplementsIEnumerable = TypeHelpers.ImplementsIEnumerable(type),
            IsDictionaryShape = TypeHelpers.IsDictionaryType(type),
            HasDictionaryKeyValue = TypeHelpers.TryGetDictionaryKeyValueTypes(type, out _, out _),
            HasCollectionElement = TypeHelpers.GetGenericEnumerableDataType(type) != null,
            UnderlyingSpecialTypeId = GetSpecialTypeId(underlying),
            UnderlyingIsEnum = underlying.TypeKind == TypeKind.Enum,
            UnderlyingIsString = underlying.SpecialType == SpecialType.System_String,
            UnderlyingIsValueType = underlying.IsValueType,
        };
    }

    /// <summary>
    /// ITypeSymbol → <see cref="TypeCompatSpecialTypeId"/> 映射
    /// （TypeHelpers.IsImplicitConvertible 门面消费）。
    /// </summary>
    /// <param name="type">类型符号</param>
    /// <returns>特殊类型 id；非特殊类型返回 None</returns>
    internal static TypeCompatSpecialTypeId GetSpecialTypeId(ITypeSymbol type)
        // Guid 识别口径镜像 NCMapper.Shared.TypeCompatibilityCore.IsConvertibleValueType（同口径现行落点）：ToDisplayString 全名比对
        => type.ToDisplayString() == "System.Guid"
            ? TypeCompatSpecialTypeId.Guid
            : SpecialTypeCompatMap.Map(type.SpecialType);

    /// <summary>
    /// 形态解析（Kind 判定序：string 先于 Class——Roslyn 对 string 的 TypeKind 亦为 Class；
    /// 字典/集合形态由标志承载，字典 class 保持 Kind=Class 以保留步骤 4 既有行为）。
    /// </summary>
    private static TypeShapeKind ResolveKind(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Error) return TypeShapeKind.Error;
        if (type.SpecialType == SpecialType.System_String) return TypeShapeKind.String;
        if (type.TypeKind == TypeKind.Enum) return TypeShapeKind.Enum;
        if (type.TypeKind == TypeKind.Delegate) return TypeShapeKind.Delegate;
        if (type.TypeKind == TypeKind.Array) return TypeShapeKind.Collection;
        if (TypeHelpers.IsNullableValueType(type)) return TypeShapeKind.Nullable;
        if (type.TypeKind == TypeKind.Interface) return TypeShapeKind.Interface;
        if (type.TypeKind == TypeKind.Class) return TypeShapeKind.Class;
        if (GetSpecialTypeId(type) != TypeCompatSpecialTypeId.None) return TypeShapeKind.Primitive;
        return TypeShapeKind.Other;
    }

    /// <summary>
    /// 剥离 Nullable&lt;T&gt; 包装的引用类型（原 TypeHelpers 私有方法迁入；C02 回归根因）。
    /// </summary>
    private static ITypeSymbol UnwrapNullableOfReferenceType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1
            && !named.TypeArguments[0].IsValueType)
        {
            return named.TypeArguments[0];
        }
        return type;
    }

    /// <summary>
    /// 装配体敏感的结构身份键——SymbolEqualityComparer.Default 相等语义的字符串承载：
    /// 装配体名 + 命名空间路径 + 嵌套类型路径 + 名称/arity + 泛型实参递归键。
    /// <para>可空注解不参与键构成（与比较器忽略 Nullability 一致）。</para>
    /// </summary>
    private static string BuildIdentityKey(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return BuildIdentityKey(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";

            case IPointerTypeSymbol pointer:
                return BuildIdentityKey(pointer.PointedAtType) + "*";

            case INamedTypeSymbol named:
            {
                var sb = new StringBuilder();
                sb.Append(named.ContainingAssembly?.Name ?? "?").Append(':');
                if (named.ContainingNamespace is { IsGlobalNamespace: false } ns)
                    sb.Append(ns.ToDisplayString()).Append('.');
                AppendNestingPath(named.ContainingType, sb);
                sb.Append(named.Name).Append('`').Append(named.Arity);
                if (named.Arity > 0)
                {
                    sb.Append('[');
                    var args = named.TypeArguments;
                    for (var i = 0; i < args.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(BuildIdentityKey(args[i]));
                    }
                    sb.Append(']');
                }
                return sb.ToString();
            }

            case ITypeParameterSymbol typeParam:
                return "tp:" + (int)typeParam.TypeParameterKind + ':'
                       + (typeParam.DeclaringType?.ToDisplayString()
                          ?? typeParam.DeclaringMethod?.ToDisplayString()
                          ?? "?")
                       + ':' + typeParam.Ordinal;

            default:
                // dynamic 等兜底：形态种类 + 显示名（Error 已由步骤 0 兜底，不依赖此键）
                return type.Kind.ToString() + ':' + type.ToDisplayString();
        }
    }

    /// <summary>嵌套类型路径（外→内逐层，arity 区分同名重载嵌套类型）</summary>
    private static void AppendNestingPath(INamedTypeSymbol? containingType, StringBuilder sb)
    {
        if (containingType == null) return;
        AppendNestingPath(containingType.ContainingType, sb);
        sb.Append(containingType.Name).Append('`').Append(containingType.Arity).Append('+');
    }
}
