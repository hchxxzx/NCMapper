using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen
{
    /// <summary>
    /// 值版类型判定（物化字段矩阵）— TypeHelpers 的 TypeRef 重载。
    /// <para>与符号版判定逻辑逐条同构（同源规则），输入为 Materialize 物化的 TypeRef 字段，
    /// 供 CodeGenerator 输出期消费（零符号依赖、零 Compilation 反查）。</para>
    /// </summary>
    internal static class TypeRefHelpers
    {


        /// <summary>等类型判定（EqualityKey 值相等——去可空注解，与 SymbolEqualityComparer.Default
        /// 忽略 Nullability 语义同构；FullName 仅发射用）</summary>
        /// <param name="a">左侧类型引用</param>
        /// <param name="b">右侧类型引用</param>
        /// <returns>两者 EqualityKey 值相等时为 true</returns>
        public static bool IsSameType(TypeRef? a, TypeRef? b)
        {
            if (a == null || b == null) return false;
            return string.Equals(a.EqualityKey, b.EqualityKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// 是否字符串类型
        /// </summary>
        /// <param name="t">待判定类型引用</param>
        /// <returns>类型为 System.String 时为 true</returns>
        public static bool IsStringType(TypeRef? t)
            => t != null && t.SpecialType == SpecialType.System_String;

        /// <summary>
        /// 是否实现 IEnumerable（值版）
        /// </summary>
        /// <param name="t">待判定类型引用</param>
        /// <returns>类型实现 IEnumerable 时为 true</returns>
        public static bool ImplementsIEnumerable(TypeRef? t)
            => t != null && t.ImplementsIEnumerable;

        /// <summary>
        /// 是否为字典类型（值版）
        /// </summary>
        /// <param name="t">待判定类型引用</param>
        /// <returns>类型为字典（IDictionary 形态）时为 true</returns>
        public static bool IsDictionaryType(TypeRef? t)
            => t != null && t.IsDictionary;

        /// <summary>
        /// 获取泛型 IEnumerable&lt;T&gt; 元素类型（值版；非泛型枚举/数组返回 null）
        /// </summary>
        /// <param name="t">待提取元素类型的类型引用</param>
        /// <returns>元素类型；非泛型枚举或数组时为 null</returns>
        public static TypeRef? GetGenericEnumerableDataType(TypeRef? t)
            => t?.EnumerableElementType;

        /// <summary>
        /// 枚举成员（值版；非枚举返回空列表）
        /// </summary>
        /// <param name="t">待提取枚举成员的类型引用</param>
        /// <returns>枚举成员快照列表；非枚举类型时为空列表</returns>
        public static IReadOnlyList<EnumMemberRef> GetEnumValues(TypeRef? t)
            => t?.EnumMembers ?? Array.Empty<EnumMemberRef>();

        /// <summary>
        /// 是否值类型
        /// </summary>
        /// <param name="t">待判定类型引用</param>
        /// <returns>类型为值类型时为 true</returns>
        public static bool IsValueType(TypeRef? t)
            => t != null && t.IsValueType;

        /// <summary>
        /// 是否可空值类型（Nullable&lt;T&gt;）
        /// </summary>
        /// <param name="t">待判定类型引用</param>
        /// <returns>类型为 Nullable&lt;T&gt; 时为 true</returns>
        public static bool IsNullableValueType(TypeRef? t)
            => t != null && t.IsNullableValueType;

        /// <summary>
        /// 获取底层类型（Nullable&lt;T&gt; → T；否则原类型）
        /// </summary>
        /// <param name="t">待提取底层类型的类型引用</param>
        /// <returns>Nullable&lt;T&gt; 的底层类型；非可空类型时返回原类型</returns>
        public static TypeRef? GetUnderlyingType(TypeRef? t)
            => t?.UnderlyingType ?? t;

        /// <summary>
        /// 隐式数值转换（与 TypeHelpers.IsImplicitConvertible 同源：ImplicitConversions 表）
        /// </summary>
        /// <param name="source">源类型引用</param>
        /// <param name="target">目标类型引用</param>
        /// <returns>源可隐式转换到目标时为 true</returns>
        public static bool IsImplicitConvertible(TypeRef? source, TypeRef? target)
        {
            if (source == null || target == null) return false;
            if (!source.IsValueType || !target.IsValueType) return false;
            if (source.Kind == TypeKind.Enum || target.Kind == TypeKind.Enum) return false;
            if (IsSameType(source, target)) return true;

            // 转换表单点——TypeCompatibilityCore.ImplicitConversions
            //（原 T-3 表删除，与 T-1/T-2 三表全量比对等价，D1.2）
            return NCMapper.Shared.TypeCompatibilityCore.ImplicitConversions
                       .TryGetValue(TypeRefCompatAdapter.GetSpecialTypeId(source), out var targets)
                   && targets.Contains(TypeRefCompatAdapter.GetSpecialTypeId(target));
        }

        /// <summary>
        /// 源 Nullable&lt;T&gt; → 目标非可空值类型（与 TypeHelpers.IsNullableToNonNullable 同源）
        /// </summary>
        /// <param name="source">源类型引用</param>
        /// <param name="target">目标类型引用</param>
        /// <returns>源为可空值类型且目标为非可空值类型时为 true</returns>
        public static bool IsNullableToNonNullable(TypeRef? source, TypeRef? target)
        {
            if (source == null || target == null) return false;
            if (!source.IsNullableValueType) return false;
            if (!target.IsValueType) return false;
            if (target.IsNullableValueType) return false;
            return true;
        }

        /// <summary>
        /// 源非可空值类型 → 目标 Nullable&lt;T&gt;（与 TypeHelpers.IsNonNullableToNullable 同源）
        /// </summary>
        /// <param name="source">源类型引用</param>
        /// <param name="target">目标类型引用</param>
        /// <returns>源为非可空值类型且目标为可空值类型时为 true</returns>
        public static bool IsNonNullableToNullable(TypeRef? source, TypeRef? target)
        {
            if (source == null || target == null) return false;
            if (source.IsNullableValueType) return false;
            if (!source.IsValueType) return false;
            if (!target.IsValueType) return false;
            if (!target.IsNullableValueType) return false;
            return true;
        }

        /// <summary>
        /// 源/目标均为 Nullable&lt;T&gt;（与 TypeHelpers.IsNullableToNullable 同源）
        /// </summary>
        /// <param name="source">源类型引用</param>
        /// <param name="target">目标类型引用</param>
        /// <returns>源与目标均为可空值类型时为 true</returns>
        public static bool IsNullableToNullable(TypeRef? source, TypeRef? target)
        {
            if (source == null || target == null) return false;
            return source.IsNullableValueType && target.IsNullableValueType;
        }

        /// <summary>
        /// 嵌套 class 属性判定（与 TypeHelpers.IsNestedClassProperty 同源）
        /// </summary>
        /// <param name="source">源类型引用</param>
        /// <param name="target">目标类型引用</param>
        /// <returns>两类型为不同非泛型 class 且均非 string 时为 true</returns>
        public static bool IsNestedClassProperty(TypeRef? source, TypeRef? target)
        {
            if (source == null || target == null) return false;
            if (source.Kind != TypeKind.Class || target.Kind != TypeKind.Class) return false;
            if (source.SpecialType == SpecialType.System_String || target.SpecialType == SpecialType.System_String) return false;
            if (source.IsGenericType || target.IsGenericType) return false;
            return !IsSameType(source, target);
        }

        /// <summary>
        /// 获取集合元素类型对（与 TypeHelpers.TryGetCollectionElementTypes 同源）
        /// </summary>
        /// <param name="source">源集合类型引用</param>
        /// <param name="target">目标集合类型引用</param>
        /// <param name="elemSrc">输出的源元素类型</param>
        /// <param name="elemTgt">输出的目标元素类型</param>
        /// <returns>成功提取元素类型对时为 true</returns>
        public static bool TryGetCollectionElementTypes(TypeRef? source, TypeRef? target,
            out TypeRef? elemSrc, out TypeRef? elemTgt)
        {
            elemSrc = null;
            elemTgt = null;

            if (source == null || target == null) return false;
            if (IsStringType(source) || IsStringType(target)) return false;
            if (source.IsDictionary || target.IsDictionary) return false;
            if (!source.ImplementsIEnumerable || !target.ImplementsIEnumerable) return false;

            // 数组对
            if (source.IsArray && target.IsArray)
            {
                elemSrc = source.ElementType;
                elemTgt = target.ElementType;
                return elemSrc != null && elemTgt != null;
            }

            // 泛型 IEnumerable<T>
            var srcElem = source.EnumerableElementType;
            var tgtElem = target.EnumerableElementType;
            if (srcElem != null && tgtElem != null)
            {
                elemSrc = srcElem;
                elemTgt = tgtElem;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 字典 K/V 提取（与 TypeHelpers.TryGetDictionaryKeyValueTypes 同源）
        /// </summary>
        /// <param name="type">待提取的字典类型引用</param>
        /// <param name="keyType">输出的键类型</param>
        /// <param name="valueType">输出的值类型</param>
        /// <returns>成功提取键值类型时为 true</returns>
        public static bool TryGetDictionaryKeyValueTypes(TypeRef? type,
            out TypeRef? keyType, out TypeRef? valueType)
        {
            keyType = null;
            valueType = null;
            if (type == null || !type.IsDictionary) return false;
            keyType = type.DictionaryKeyType;
            valueType = type.DictionaryValueType;
            return keyType != null && valueType != null;
        }

        /// <summary>
        /// 是否有映射路径（与 TypeHelpers.HasMappingPath 同源，规则序镜像）
        /// </summary>
        /// <param name="source">源类型引用</param>
        /// <param name="target">目标类型引用</param>
        /// <returns>存在可映射路径（跳过判定不成立）时为 true</returns>
        public static bool HasMappingPath(TypeRef? source, TypeRef? target)
        {
        // ③：判据链收敛至 TypeCompatibilityCore 单点（/ 向符号版权威
        // 收敛——预期行为变更见D1.3 清单：float/double→enum、string→非白名单值类型
        // 生成侧判定由 true 变 false，新增跳过 + NCMAP027）。本门面保留签名，CodeGenerator
        // 三处消费点（:638/:1157/:1688）零改动。步骤 0b 剥离与 null/Error 兜底由 adapter 承载。
        return NCMapper.Shared.TypeCompatibilityCore.CanConvert(
            TypeRefCompatAdapter.Resolve(source),
            TypeRefCompatAdapter.Resolve(target));
        }





        // ② 枢纽深化：TypeRefHelpers.PlanSymbol 已删除——PlanSymbolAdapter 直接调用
        // public 形态判定方法（IsNestedClassProperty/IsCollectionShape/IsNullableToNonNullable/
        // IsNonNullableToNullable/IsSameType），消除 SymbolAssignmentKind 双宇宙。

        /// <summary>
        /// 集合形态判定（与 TypeHelpers.IsCollectionShapeSymbol 同源）
        /// ② 枢纽深化：改为 public 供 PlanSymbolAdapter 直接调用
        /// </summary>
        public static bool IsCollectionShape(TypeRef? source, TypeRef? target)
        {
            if (source == null || target == null) return false;
            if (IsStringType(source) || IsStringType(target)) return false;
            return source.ImplementsIEnumerable && target.ImplementsIEnumerable;
        }

        /// <summary>目标 null 字面量（镜像 TypeHelpers.NullLiteralFor：可空值类型→null；
        /// 引用类型按 NRT 注解 Annotated→null / NotAnnotated→default!；非可空值类型→default(T)）</summary>
        /// <param name="target">目标类型引用</param>
        /// <returns>按目标类型形态生成的 null 字面量表达式</returns>
        public static string NullLiteralFor(TypeRef? target)
        {
            if (target == null) return "null";
            // 可空值类型（Nullable<T>）→ null 合法
            if (target.IsNullableValueType)
                return "null";
            // 引用类型（class/interface/delegate/array/dynamic/type parameter）：按 NRT 注解
            if (target.Kind is TypeKind.Class or TypeKind.Interface
                or TypeKind.Delegate or TypeKind.Array or TypeKind.Dynamic or TypeKind.TypeParameter)
            {
                return target.NullableAnnotation == NullableAnnotation.Annotated
                    ? "null"
                    : "default!";
            }
            // 非可空值类型 → default
            return $"default({target.FullName})";
        }

        /// <summary>
        /// 生成代码可访问性判定（与 TypeHelpers.IsAccessibleFromGeneratedCode 同源：仅 public）
        /// </summary>
        /// <param name="accessibility">符号可访问性</param>
        /// <returns>可访问性为 public 时为 true</returns>
        public static bool IsAccessibleFromGeneratedCode(Accessibility accessibility)
            => accessibility == Accessibility.Public;

        /// <summary>
        /// 字典创建表达式（与 TypeHelpers.TryGetDictionaryCreationExpression 同源，值版）
        /// </summary>
        /// <param name="propType">字典属性类型引用</param>
        /// <param name="keyTypeName">键类型显示名</param>
        /// <param name="valueTypeName">值类型显示名</param>
        /// <returns>字典创建表达式字符串；无可用构造路径时为 null</returns>
        public static string? TryGetDictionaryCreationExpression(TypeRef? propType, string keyTypeName, string valueTypeName)
        {
            if (propType == null) return null;
            var isSystemCollections = propType.Namespace.StartsWith("System.Collections", StringComparison.Ordinal);
            var isInterfaceDict = propType.Kind == TypeKind.Interface
                && (propType.SimpleName == "IDictionary" || propType.SimpleName == "IReadOnlyDictionary")
                && isSystemCollections;
            var isExactDictionary = propType.SimpleName == "Dictionary" && isSystemCollections;
            var hasPublicParameterlessCtor = propType.HasPublicParameterlessCtor;

            if (isInterfaceDict || isExactDictionary)
                return $"new System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}>()";
            if (hasPublicParameterlessCtor)
                // P4 连带（CS8628 修复，DeepCopy 同型缺陷第二发射点）：
                // 可空字典属性（如 ConcurrentDictionary<K,V>?）的 FullName 携带 "?" 注解，
                // new 表达式禁可空注解（new X?() 非法语法）→ NonNullableFullName。
                return $"new {propType.NonNullableFullName}()";
            return null;
        }


    }
}
