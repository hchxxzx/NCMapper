using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper.Shared;

// ================================================================
// Type 模型 → TypeShape adapter（③）。
// 运行时消费（MapValidator 经 TypeCompatView.CanConvert 门面，:418 零改动）。
// 判据裁决全部在 TypeCompatibilityCore（C5 分离约束）；本类只做"模型 → TypeShape"映射。
//
// 行为口径（对齐裁决）：
// - Error 态不可达（运行时无编译错误类型）——步骤 0 经本 adapter 永不触发；
// - 步骤 0b 可空引用剥离为恒等操作（运行时 Nullable 约束 T:struct，无 Nullable<引用类型>）；
// - 委托不再归入 Class（Kind=Delegate，对齐符号版 TypeKind.Delegate）——D1.3 #5；
// - 字典门控补齐 IReadOnlyDictionary<,>（对齐符号版）——D1.3 #6。
// ================================================================

/// <summary>
/// Type（System.Type 反射模型）→ <see cref="TypeShape"/> 适配器。
/// <para>身份键 = AssemblyQualifiedName（与 Type 引用相等语义一致：键相等 ? 类型同一）。
/// 集合/字典形状判定镜像符号版（TypeHelpers.IsDictionaryType/GetGenericEnumerableDataType），
/// 门控差异按符号版权威收敛并登记（D1.3 #6）。</para>
/// </summary>
internal static class TypeCompatAdapter
{
    /// <summary>
    /// 解析 Type → TypeShape（含底层视图与集合/字典形状）。
    /// </summary>
    /// <param name="type">运行时类型（DAM Interfaces 注解随门面传播——形状解析需 GetInterfaces）</param>
    /// <returns>类型形状描述符</returns>
    internal static TypeShape Resolve(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        // 0b：剥离 Nullable<T> 包装的引用类型——运行时恒等（Nullable 约束 T:struct），
        // 语义占位以与符号版步骤 0b 对应。
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return new TypeShape
        {
            Kind = ResolveKind(type),
            SpecialTypeId = GetSpecialTypeId(type),
            IdentityKey = type.AssemblyQualifiedName ?? type.FullName ?? type.ToString(),
            IsValueType = type.IsValueType,
            IsEnum = type.IsEnum,
            IsNullableValueType = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>),
            IsGenericType = type.IsGenericType,
            ImplementsIEnumerable = ImplementsIEnumerable(type),
            IsDictionaryShape = IsDictionaryShape(type),
            HasDictionaryKeyValue = HasDictionaryKeyValue(type),
            HasCollectionElement = GetGenericEnumerableDataType(type) != null,
            UnderlyingSpecialTypeId = GetSpecialTypeId(underlying),
            UnderlyingIsEnum = underlying.IsEnum,
            UnderlyingIsString = underlying == typeof(string),
            UnderlyingIsValueType = underlying.IsValueType,
        };
    }

    /// <summary>
    /// Type → <see cref="TypeCompatSpecialTypeId"/> 映射（TypeConverter.IsImplicitConvertible 门面消费）。
    /// </summary>
    /// <param name="type">运行时类型</param>
    /// <returns>特殊类型 id；非特殊类型返回 None</returns>
    internal static TypeCompatSpecialTypeId GetSpecialTypeId(Type type)
        => type == typeof(string) ? TypeCompatSpecialTypeId.String
         : type == typeof(bool) ? TypeCompatSpecialTypeId.Boolean
         : type == typeof(char) ? TypeCompatSpecialTypeId.Char
         : type == typeof(sbyte) ? TypeCompatSpecialTypeId.SByte
         : type == typeof(byte) ? TypeCompatSpecialTypeId.Byte
         : type == typeof(short) ? TypeCompatSpecialTypeId.Int16
         : type == typeof(ushort) ? TypeCompatSpecialTypeId.UInt16
         : type == typeof(int) ? TypeCompatSpecialTypeId.Int32
         : type == typeof(uint) ? TypeCompatSpecialTypeId.UInt32
         : type == typeof(long) ? TypeCompatSpecialTypeId.Int64
         : type == typeof(ulong) ? TypeCompatSpecialTypeId.UInt64
         : type == typeof(decimal) ? TypeCompatSpecialTypeId.Decimal
         : type == typeof(float) ? TypeCompatSpecialTypeId.Single
         : type == typeof(double) ? TypeCompatSpecialTypeId.Double
         : type == typeof(DateTime) ? TypeCompatSpecialTypeId.DateTime
         : type == typeof(Guid) ? TypeCompatSpecialTypeId.Guid
         : TypeCompatSpecialTypeId.None;

    /// <summary>
    /// 形态解析（Kind 判定序：string 先于 Class——反射 IsClass 对 string/委托均为 true；
    /// 字典/集合形态由标志承载，字典 class 如 Hashtable 保持 Kind=Class 以保留步骤 4 既有行为）。
    /// </summary>
    private static TypeShapeKind ResolveKind(Type type)
    {
        if (type == typeof(string)) return TypeShapeKind.String;
        if (type.IsEnum) return TypeShapeKind.Enum;
        if (typeof(Delegate).IsAssignableFrom(type)) return TypeShapeKind.Delegate;
        if (type.IsArray) return TypeShapeKind.Collection;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) return TypeShapeKind.Nullable;
        if (type.IsInterface) return TypeShapeKind.Interface;
        if (type.IsClass) return TypeShapeKind.Class;
        if (GetSpecialTypeId(type) != TypeCompatSpecialTypeId.None) return TypeShapeKind.Primitive;
        return TypeShapeKind.Other;
    }

    /// <summary>
    /// 实现 IEnumerable（含数组）。
    /// </summary>
    private static bool ImplementsIEnumerable(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
        => type.IsArray || typeof(IEnumerable).IsAssignableFrom(type);

    /// <summary>
    /// 字典形态判定——镜像符号版 IsDictionaryType（TypeHelpers.cs:175-185）：
    /// 自身检查（含非泛型 IDictionary 接口）+ 接口遍历（IDictionary/IReadOnlyDictionary 系 + 命名空间守卫）。
    /// <para>对齐差异（D1.3 #6）：原 Type 版经 CollectionTypeDetector 仅识别泛型 IDictionary&lt;,&gt; 与
    /// 非泛型 IDictionary，漏检 IReadOnlyDictionary&lt;,&gt; 实现方与"自身即非泛型 IDictionary 接口"——
    /// 收敛至符号版权威（List→IReadOnlyDictionary 实现方由 false 变 true）。</para>
    /// <para>可见性 private → internal，作为轻量 bool 入口供
    /// <see cref="CollectionTypeDetector.IsDictionary"/> 口径收敛委托（不做全量 <see cref="Resolve"/>
    /// TypeShape 解析），消除运行时 detector 与验证器双口径分叉。</para>
    /// </summary>
    internal static bool IsDictionaryShape(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        if (IsDictionaryInterfaceShape(type)) return true;

        foreach (var iface in type.GetInterfaces())
        {
            if (IsDictionaryInterfaceShape(iface)) return true;
        }
        return false;
    }

    /// <summary>字典接口形态（含命名空间守卫，防用户自定义同名类型误判）</summary>
    private static bool IsDictionaryInterfaceShape(Type type)
        => (type.Name == "IDictionary`2" || type.Name == "IReadOnlyDictionary`2" || type.Name == "IDictionary")
           && (type.Namespace == "System.Collections.Generic"
               || type.Namespace == "System.Collections"
               || type.Namespace == "System.Collections.ObjectModel");

    /// <summary>
    /// 字典 K/V 泛型参数可提取——原 TypeCompatView.GetDictionaryKeyValueTypes 的可提取性判定
    /// （自身字典接口 / 接口遍历 / 自身泛型字典类 Dictionary/SortedDictionary/SortedList）。
    /// </summary>
    private static bool HasDictionaryKeyValue(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        // 自身即为 IDictionary<,>/IReadOnlyDictionary<,> 接口（GetInterfaces 不含自身）
        if (type.IsGenericType
            && (type.Name == "IDictionary`2" || type.Name == "IReadOnlyDictionary`2"))
            return true;

        // 从接口查找 IDictionary<,>/IReadOnlyDictionary<,>
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType
                && (iface.Name == "IDictionary`2" || iface.Name == "IReadOnlyDictionary`2"))
                return true;
        }

        // 兜底：类型自身即为泛型字典（Dictionary<,>/SortedDictionary<,>/SortedList<,>）
        if (type.IsGenericType
            && (type.Name == "Dictionary`2" || type.Name == "SortedDictionary`2" || type.Name == "SortedList`2"))
            return true;

        return false;
    }

    /// <summary>
    /// 提取泛型可枚举元素类型（数组返回元素类型；非泛型集合返回 null）——
    /// 原 TypeCompatView.GetGenericEnumerableDataType 同构（仅用于元素可提取性判定）。
    /// </summary>
    private static Type? GetGenericEnumerableDataType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        if (type.IsArray) return type.GetElementType();

        // 类型自身即为 IEnumerable<T>（GetInterfaces 不含自身，先查自身——符号版同理）
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        // 从接口查找 IEnumerable<T>（ICollection<T>/IList<T>/List<T> 等均含）
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        }

        return null;
    }
}
