using System;
using System.Collections.Generic;

namespace NCMapper.Shared;

// ================================================================
// 类型兼容判据核心
// "可映射/可转换"判据链的符号无关单点实现（步骤 0/1-10 纯函数 + ImplicitConversions 单点表），
// 收敛原四份镜像（TypeCompatView / TypeHelpers / TypeRefHelpers / MappingExtractor 诊断私有副本）。
// 本文件零依赖（仅 System/System.Collections.Generic），经 csproj Compile Include Link 同时编译进
// NCMapper（net8.0+）与 NCMapper.SourceGen（netstandard2.0）——接缝先例：PathResolver.cs /
// assignmentplan.cs / DeepCopyGraphRules.cs。record struct 依赖的 IsExternalInit polyfill
// 在 SourceGen 侧由 Models.Snapshots.cs 提供。
//
// 判据裁决全部在本核心（C5 分离约束：adapter 只做"模型 → TypeShape"映射，不做裁决）；
// X-1/X-2 分叉收敛语义（符号版权威）与 Error 兜底语义（C02 回归根因）为设计硬约束。
// ================================================================

/// <summary>
/// 类型形状类别——含 Error/Unknown 态。
/// <para>Kind 仅承载步骤 0（Error → true）与步骤 4（Class 嵌套类）所需判别信息；
/// 字典/集合形态由 <see cref="TypeShape.IsDictionaryShape"/>/<see cref="TypeShape.ImplementsIEnumerable"/>
/// 标志承载（字典 class 如 Hashtable 必须保持 Kind=Class 以保留步骤 4 既有行为）。</para>
/// </summary>
internal enum TypeShapeKind
{
    /// <summary>adapter 无法解析的形态（保守：判据 false，步骤 10——与 Error 兜底显式区分）</summary>
    Unknown,

    /// <summary>编译错误类型（#nullable disabled 下可空引用解析产物 / TypeRef null 输入）→ 判据 true 不跳过（步骤 0，C02 回归根因，C6 硬约束）</summary>
    Error,

    /// <summary>数值/bool/char 等基元值类型</summary>
    Primitive,

    /// <summary>枚举类型</summary>
    Enum,

    /// <summary>Nullable&lt;T&gt; 值类型包装（底层信息见 TypeShape 底层视图字段）</summary>
    Nullable,

    /// <summary>string（注意：Roslyn TypeKind 下 string 为 Class，adapter 须先于 Class 判定）</summary>
    String,

    /// <summary>普通引用类（步骤 4 嵌套类判定主体；委托/接口不在此列）</summary>
    Class,

    /// <summary>接口类型</summary>
    Interface,

    /// <summary>泛型形态（描述性——泛型判别实际由 TypeShape.IsGenericType 承载）</summary>
    Generic,

    /// <summary>数组形态（含数组在内的集合可枚举性由 ImplementsIEnumerable 承载）</summary>
    Collection,

    /// <summary>字典形态（描述性——字典判别实际由 IsDictionaryShape 承载）</summary>
    Dictionary,

    /// <summary>委托类型（Type 版收敛裁决：委托不再归入 Class，对齐符号版 TypeKind.Delegate）</summary>
    Delegate,

    /// <summary>其余形态（struct 等非基元值类型）</summary>
    Other
}

/// <summary>
/// 特殊类型 id——Roslyn SpecialType / System.Type 的符号无关等价物（核心零依赖约束 C7），
/// 供 ImplicitConversions 单点表与 string/DateTime/Guid/数值族判定消费。
/// <para>Guid 在 Roslyn SpecialType 中为 None，由 adapter 按模型原生口径识别后映射为 <see cref="Guid"/>。</para>
/// </summary>
internal enum TypeCompatSpecialTypeId
{
    /// <summary>非特殊类型（自定义类型/枚举/Nullable 包装等）</summary>
    None = 0,

    /// <summary>bool</summary>
    Boolean = 1,

    /// <summary>char</summary>
    Char = 2,

    /// <summary>sbyte</summary>
    SByte = 3,

    /// <summary>byte</summary>
    Byte = 4,

    /// <summary>short</summary>
    Int16 = 5,

    /// <summary>ushort</summary>
    UInt16 = 6,

    /// <summary>int</summary>
    Int32 = 7,

    /// <summary>uint</summary>
    UInt32 = 8,

    /// <summary>long</summary>
    Int64 = 9,

    /// <summary>ulong</summary>
    UInt64 = 10,

    /// <summary>decimal</summary>
    Decimal = 11,

    /// <summary>float</summary>
    Single = 12,

    /// <summary>double</summary>
    Double = 13,

    /// <summary>string</summary>
    String = 14,

    /// <summary>DateTime</summary>
    DateTime = 15,

    /// <summary>Guid（Roslyn SpecialType 无此项，adapter 按名映射）</summary>
    Guid = 16
}

/// <summary>
/// 轻量类型形状描述符——覆盖判据链步骤 0/1-10 所需信息的最小属性集（M2 机理）。
/// <para>自身视图字段（Kind/SpecialTypeId/IsValueType 等）与底层视图字段（Underlying*，Nullable&lt;T&gt;
/// 解包后；非可空时与自身一致）分离承载，避免描述符自嵌套。步骤 0b（可空引用剥离）由 adapter
/// 在解析时完成——本描述符即解包后视图。</para>
/// </summary>
internal readonly record struct TypeShape
{
    /// <summary>形态类别（Error/Unknown 兜底显式区分）</summary>
    public TypeShapeKind Kind { get; init; }

    /// <summary>自身特殊类型 id（数值/string/DateTime/Guid；None = 非特殊）</summary>
    public TypeCompatSpecialTypeId SpecialTypeId { get; init; }

    /// <summary>
    /// 步骤 1 等类型判定键——模型原生相等语义的字符串承载（Type 版 = AssemblyQualifiedName；
    /// 符号版 = 装配体敏感的结构键；TypeRef 版 = EqualityKey）。null = 不可判等（如 Unknown 形态）。
    /// </summary>
    public string? IdentityKey { get; init; }

    /// <summary>自身是否值类型</summary>
    public bool IsValueType { get; init; }

    /// <summary>自身是否枚举</summary>
    public bool IsEnum { get; init; }

    /// <summary>自身是否 Nullable&lt;T&gt; 值类型包装</summary>
    public bool IsNullableValueType { get; init; }

    /// <summary>自身是否泛型类型（步骤 4 嵌套类排除）</summary>
    public bool IsGenericType { get; init; }

    /// <summary>自身是否实现 IEnumerable（含数组）</summary>
    public bool ImplementsIEnumerable { get; init; }

    /// <summary>自身是否字典形态（IDictionary/IReadOnlyDictionary 系）</summary>
    public bool IsDictionaryShape { get; init; }

    /// <summary>字典 K/V 泛型参数可提取（字典形状成立但 K/V 提取失败 → 步骤 6 false，对齐符号版）</summary>
    public bool HasDictionaryKeyValue { get; init; }

    /// <summary>集合元素类型可提取（步骤 5：非泛型枚举如 ArrayList 为 false）</summary>
    public bool HasCollectionElement { get; init; }

    /// <summary>底层特殊类型 id（Nullable&lt;T&gt; 解包后；非可空时与 SpecialTypeId 一致）</summary>
    public TypeCompatSpecialTypeId UnderlyingSpecialTypeId { get; init; }

    /// <summary>底层是否枚举（步骤 3 枚举路径）</summary>
    public bool UnderlyingIsEnum { get; init; }

    /// <summary>底层是否 string（步骤 3/9）</summary>
    public bool UnderlyingIsString { get; init; }

    /// <summary>底层是否值类型（步骤 9 IConvertible 白名单）</summary>
    public bool UnderlyingIsValueType { get; init; }
}

/// <summary>
/// 类型兼容判据核心——纯函数，无模型依赖。
/// <para>判据链步骤 0/1-10 为三模型（Type/ITypeSymbol/TypeRef）共享单点：
/// 步骤 0 Error 兜底（→ true 不跳过，C02 回归根因，C6 硬约束）与 Unknown 保守
/// （→ false，步骤 10）显式区分；X-1（float/double→enum 排除）与 X-2（string 白名单）
/// 按符号版权威收敛（预期行为变更，D1.3 清单锁定）。</para>
/// </summary>
internal static class TypeCompatibilityCore
{
    /// <summary>
    /// 转换表单点（C3 硬约束）——C# 语言规范 隐式数值转换的唯一物理副本。
    /// <para>内容为 三表全量比对裁决集。
    /// 原 T-1（TypeConverter）/T-2（TypeHelpers）/T-3（TypeRefHelpers）三份镜像已删除，
    /// 各门面改调本表。</para>
    /// </summary>
    internal static readonly IReadOnlyDictionary<TypeCompatSpecialTypeId, HashSet<TypeCompatSpecialTypeId>> ImplicitConversions = CreateImplicitConversions();

    private static IReadOnlyDictionary<TypeCompatSpecialTypeId, HashSet<TypeCompatSpecialTypeId>> CreateImplicitConversions()
    {
        var table = new Dictionary<TypeCompatSpecialTypeId, HashSet<TypeCompatSpecialTypeId>>();
        void Add(TypeCompatSpecialTypeId from, params TypeCompatSpecialTypeId[] targets)
            => table[from] = new HashSet<TypeCompatSpecialTypeId>(targets);

        Add(TypeCompatSpecialTypeId.Byte,
            TypeCompatSpecialTypeId.Int16, TypeCompatSpecialTypeId.UInt16,
            TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.UInt32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.SByte,
            TypeCompatSpecialTypeId.Int16, TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.Int64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.Int16,
            TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.Int64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.UInt16,
            TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.UInt32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.Int32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.Single,
            TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.UInt32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.Int64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.Char,
            TypeCompatSpecialTypeId.UInt16, TypeCompatSpecialTypeId.Int32, TypeCompatSpecialTypeId.UInt32,
            TypeCompatSpecialTypeId.Int64, TypeCompatSpecialTypeId.UInt64,
            TypeCompatSpecialTypeId.Single, TypeCompatSpecialTypeId.Double, TypeCompatSpecialTypeId.Decimal);
        Add(TypeCompatSpecialTypeId.Single,
            TypeCompatSpecialTypeId.Double);

        return table;
    }

    /// <summary>
    /// 判据链纯函数（步骤 0/1-10）——三模型共享的"存在映射/转换路径"单一判定点。
    /// <para>步骤序与原 TypeCompatView.CanConvert / TypeHelpers.HasMappingPath / TypeRefHelpers.HasMappingPath
    /// 逐条对应（三版并集，分叉按符号版权威收敛）。步骤 0b（可空引用剥离）由 adapter 完成后进入本函数。</para>
    /// </summary>
    /// <param name="source">源类型形状（adapter 解析产物）</param>
    /// <param name="target">目标类型形状（adapter 解析产物）</param>
    /// <returns>存在映射/转换路径时为 true；完全不相容（调用方跳过 + NCMAP027）时为 false</returns>
    internal static bool CanConvert(TypeShape source, TypeShape target)
    {
        // 0. Error 兜底（C02 回归根因，C6 硬约束）：error 类型（含 TypeRef null 输入）不参与跳过判定，
        //    保持 fallback 行为。与 Unknown（adapter 无法解析 → 保守 false，落步骤 10）显式区分。
        if (source.Kind == TypeShapeKind.Error || target.Kind == TypeShapeKind.Error)
            return true;

        // 0b. 可空引用剥离已由 adapter 完成（TypeShape 即解包后视图）。

        // 1. 等类型（同类型直接赋值；同类型字典/集合走复制路径，均有转换路径）——
        //    等价语义由 adapter 的 IdentityKey 承载（模型原生相等语义）。
        if (IsSameIdentity(source, target))
            return true;

        // 2. 隐式数值转换（int→long 等，ImplicitConversions 单点表）
        if (IsImplicitNumericConvertible(source, target))
            return true;

        // 3. 枚举路径（enum?enum / enum?string，底层类型判定）
        if (IsEnumPath(source, target))
            return true;

        // 4. 嵌套类映射（不同 class，非 string，非泛型，非委托——Type 版委托收敛见 D1.3 #5）
        if (IsNestedClassShape(source, target))
            return true;

        // 5. 集合?集合（元素级映射；排除字典/string；元素类型须可提取）
        if (IsCollectionShapePair(source, target))
            return true;

        // 6. 字典?字典（K/V 复制；字典形状成立但 K/V 提取失败 → false，I-4 对齐符号版）
        if (IsDictionaryShapePair(source, target))
            return true;

        // 7. 真 List/数组 → Dictionary 受控异常路径（收紧：排除 string
        //    string→Dictionary 不满足此谓词，落步骤 10 跳过）
        if (IsListToDictionaryShape(source, target))
            return true;

        // 8. 值类型可空性组合（Nullable<T>?U / U?Nullable<T> / Nullable<T>?Nullable<U>）
        if (IsNullableCompatible(source, target))
            return true;

        // 9. IConvertible 交叉转换（符号版权威语义，对齐运行时 TypeConvertHelper.ConvertValue 能力面）：
        //    string?IConvertible 值类型白名单（X-2 收敛）、enum?数值、数值?enum（X-1 收敛：float/double 排除）、
        //    数值族?数值族（含显式窄化）。仅"运行时也无法转换"的组合落步骤 10 跳过 + NCMAP027。
        if (IsIConvertibleCrossConvertible(source, target))
            return true;

        // 10. 其余 → 完全不相容（调用方跳过 + NCMAP027）；Unknown 形态保守 false 亦落此处
        return false;
    }

    /// <summary>步骤 1：等类型判定（IdentityKey 均非空且 Ordinal 相等）</summary>
    private static bool IsSameIdentity(TypeShape source, TypeShape target)
        => source.IdentityKey != null && target.IdentityKey != null
           && string.Equals(source.IdentityKey, target.IdentityKey, StringComparison.Ordinal);

    /// <summary>
    /// 步骤 2：隐式数值转换（ImplicitConversions 单点表查询）。
    /// <para>门面级 IsImplicitConvertible 的"同类型 → true"分支已由步骤 1 承载（键相等即返回）。</para>
    /// </summary>
    internal static bool IsImplicitNumericConvertible(TypeShape source, TypeShape target)
    {
        if (!source.IsValueType || !target.IsValueType) return false;
        if (source.IsEnum || target.IsEnum) return false;
        return ImplicitConversions.TryGetValue(source.SpecialTypeId, out var targets)
               && targets.Contains(target.SpecialTypeId);
    }

    /// <summary>步骤 3：枚举路径（底层视图判定：enum?enum / enum?string，双向）</summary>
    internal static bool IsEnumPath(TypeShape source, TypeShape target)
    {
        if (source.UnderlyingIsEnum && (target.UnderlyingIsEnum || target.UnderlyingIsString))
            return true;
        if (target.UnderlyingIsEnum && (source.UnderlyingIsEnum || source.UnderlyingIsString))
            return true;
        return false;
    }

    /// <summary>
    /// 步骤 4：嵌套类映射（双方 Class、非泛型；string/委托/接口/结构均不满足）。
    /// <para>步骤 1 已排除同类型，此处无须重复不等判定。</para>
    /// </summary>
    internal static bool IsNestedClassShape(TypeShape source, TypeShape target)
        => source.Kind == TypeShapeKind.Class && target.Kind == TypeShapeKind.Class
           && !source.IsGenericType && !target.IsGenericType;

    /// <summary>步骤 5：集合?集合（双方非 string/字典、可枚举且元素类型可提取）</summary>
    internal static bool IsCollectionShapePair(TypeShape source, TypeShape target)
        => !IsStringShape(source) && !IsStringShape(target)
           && !source.IsDictionaryShape && !target.IsDictionaryShape
           && source.ImplementsIEnumerable && target.ImplementsIEnumerable
           && source.HasCollectionElement && target.HasCollectionElement;

    /// <summary>步骤 6：字典?字典（双方字典形态且 K/V 可提取——提取失败 → false）</summary>
    internal static bool IsDictionaryShapePair(TypeShape source, TypeShape target)
        => source.IsDictionaryShape && target.IsDictionaryShape
           && source.HasDictionaryKeyValue && target.HasDictionaryKeyValue;

    /// <summary>步骤 7：真 List/数组 → Dictionary 受控路径（源非 string、目标字典、源可枚举）</summary>
    internal static bool IsListToDictionaryShape(TypeShape source, TypeShape target)
        => !IsStringShape(source) && target.IsDictionaryShape && source.ImplementsIEnumerable;

    /// <summary>步骤 8：值类型可空性组合（N?N / N?非N值 / 非N值?N 三态）
    /// <para>三态均限定——underlying 相同
    /// （T?↔T/T?，如 DateTime?→DateTime）或双方数值族（含 char/bool，如 int?→long?）。
    /// 原对任意 Nullable 组合判 true（如 DateTime?→TimeSpan?），Expression.Convert 无
    /// 该转换运算符运行时必抛——验证结果误导（判可映射实不可映射）。enum 组合不受影响
    /// （步骤 3 底层视图已承接：enum?↔enum / enum?→string）。</para></summary>
    internal static bool IsNullableCompatible(TypeShape source, TypeShape target)
    {
        // 三态守卫（原结构保持）：至少一侧可空 + 双方值类型
        var nullableInvolved = source.IsNullableValueType || target.IsNullableValueType;
        if (!nullableInvolved || !source.IsValueType || !target.IsValueType)
            return false;

        // 同 underlying（SpecialTypeId 非 None 且相等）——T?↔T/T? 恒可转换
        var s = source.UnderlyingSpecialTypeId;
        var t = target.UnderlyingSpecialTypeId;
        if (s != TypeCompatSpecialTypeId.None && s == t)
            return true;

        // 数值族互转（Expression.Convert 支持含显式窄化）
        return IsNumericSpecialId(s) && IsNumericSpecialId(t);
    }

    /// <summary>数值族特殊 id 判定（Boolean..Double 连续段：bool/char/sbyte..double——N-11）</summary>
    private static bool IsNumericSpecialId(TypeCompatSpecialTypeId id)
        => id >= TypeCompatSpecialTypeId.Boolean && id <= TypeCompatSpecialTypeId.Double;

    /// <summary>
    /// 步骤 9：IConvertible 交叉转换能力判定——符号版权威语义（X-1/X-2 收敛点）。
    /// <para>规则集（对齐运行时 TypeConvertHelper.ConvertValue 能力面）：
    /// ① string ? IConvertible 值类型白名单（数值/bool/char/DateTime/Guid/enum）——X-2 收敛
    /// （TypeRef 版原放宽为任意值类型，收敛后 string→TimeSpan 等新增跳过 + NCMAP027，D1.3 #2-4）；
    /// ② enum → 数值族；③ 数值 → enum（float/double 排除）——X-1 收敛（D1.3 #1）；
    /// ④ 数值族 ? 数值族（含显式窄化与 bool/char 互转）。DateTime/Guid 仅与 string 互转（白名单）。</para>
    /// </summary>
    internal static bool IsIConvertibleCrossConvertible(TypeShape source, TypeShape target)
    {
        var sEnum = source.UnderlyingIsEnum;
        var tEnum = target.UnderlyingIsEnum;
        var sStr = source.UnderlyingIsString;
        var tStr = target.UnderlyingIsString;

        // 1) string ? IConvertible 值类型白名单（含 enum）
        if (sStr && (tEnum || IsConvertibleValueType(target)))
            return true;
        if (tStr && (sEnum || IsConvertibleValueType(source)))
            return true;

        // 2) enum → 数值族（含 decimal/float/double 底层均可 ChangeType）
        if (sEnum && IsNumericFamily(target))
            return true;

        // 3) 数值 → enum（float/double 排除，对齐 TypeConvertHelper 直转路径语义——X-1 收敛）
        if (tEnum && IsNumericFamily(source)
            && source.UnderlyingSpecialTypeId is not (TypeCompatSpecialTypeId.Single or TypeCompatSpecialTypeId.Double))
            return true;

        // 4) 数值族 ? 数值族（显式窄化与 bool/char 互转）
        if (IsNumericFamily(source) && IsNumericFamily(target))
            return true;

        return false;
    }

    /// <summary>IConvertible 值类型白名单判定（不含 string/enum；含 DateTime/Guid——仅与 string 互转）</summary>
    private static bool IsConvertibleValueType(TypeShape shape)
        => shape.UnderlyingIsValueType
           && shape.UnderlyingSpecialTypeId is
               TypeCompatSpecialTypeId.Boolean or TypeCompatSpecialTypeId.Char
               or TypeCompatSpecialTypeId.SByte or TypeCompatSpecialTypeId.Byte
               or TypeCompatSpecialTypeId.Int16 or TypeCompatSpecialTypeId.UInt16
               or TypeCompatSpecialTypeId.Int32 or TypeCompatSpecialTypeId.UInt32
               or TypeCompatSpecialTypeId.Int64 or TypeCompatSpecialTypeId.UInt64
               or TypeCompatSpecialTypeId.Single or TypeCompatSpecialTypeId.Double
               or TypeCompatSpecialTypeId.Decimal
               or TypeCompatSpecialTypeId.DateTime or TypeCompatSpecialTypeId.Guid;

    /// <summary>数值族判定（底层视图）：SByte..Decimal + Boolean + Char（ChangeType 可互转；DateTime/Guid 不在内）</summary>
    private static bool IsNumericFamily(TypeShape shape)
        => shape.UnderlyingSpecialTypeId is
               TypeCompatSpecialTypeId.SByte or TypeCompatSpecialTypeId.Byte
               or TypeCompatSpecialTypeId.Int16 or TypeCompatSpecialTypeId.UInt16
               or TypeCompatSpecialTypeId.Int32 or TypeCompatSpecialTypeId.UInt32
               or TypeCompatSpecialTypeId.Int64 or TypeCompatSpecialTypeId.UInt64
               or TypeCompatSpecialTypeId.Single or TypeCompatSpecialTypeId.Double
               or TypeCompatSpecialTypeId.Decimal
               or TypeCompatSpecialTypeId.Boolean or TypeCompatSpecialTypeId.Char;

    /// <summary>string 形态判定（Kind=String 或 SpecialTypeId=String——双保险）</summary>
    private static bool IsStringShape(TypeShape shape)
        => shape.Kind == TypeShapeKind.String || shape.SpecialTypeId == TypeCompatSpecialTypeId.String;
}
