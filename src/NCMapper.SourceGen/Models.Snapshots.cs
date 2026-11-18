using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

// netstandard2.0 兼容：init 访问器需要 IsExternalInit（值模型不可变性的编译期保证）
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace NCMapper.SourceGen
{
    // ================================================================
    // 值模型层— 增量管线缓存值纯值化
    // 进入管线缓存的对象零 ISymbol/SyntaxNode/Location/Compilation 引用；
    // 相等性全部为值语义（跨 Compilation 比较退化为字符串/数值比较）。
    // 符号期解析在 MappingExtractor 内部完成，出口 Materialize 为快照。
    // ================================================================

    /// <summary>
    /// 类型引用值对象 — INamedTypeSymbol/ITypeSymbol 的纯值快照。
    /// <para>FullName 为 ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) 结果（全局唯一键）；
    /// 相等性以 FullName 为准（跨 Compilation 稳定，测试 #8 覆盖边缘形态）。</para>
    /// </summary>
    internal sealed class TypeRef : IEquatable<TypeRef>
    {
        /// <summary>完全限定显示名（含可空注解，等价符号版 ToDisplayString()；发射用
        /// 测试 #2 逐字节零差异约束：类型显示必须与符号版一致，含 `?`）</summary>
        public string FullName { get; init; } = "";

        /// <summary>相等性键（去可空注解——符号版 SymbolEqualityComparer.Default 忽略 Nullability 语义；
        /// Equals/GetHashCode 基于此键，与发射显示解耦）</summary>
        public string EqualityKey { get; init; } = "";

        /// <summary>物化时符号的可空注解（引用类型发射 default!/null 字面量判定用；物化入口统一
        /// 去顶层注解后此处记录原始值—— NullLiteralFor 对齐符号版）</summary>
        public NullableAnnotation NullableAnnotation { get; init; }

        /// <summary>
        /// 最小限定名（MinimallyQualifiedFormat，int→"int" C# 关键字形式；报告/备注用）
        /// </summary>
        public string MinimallyQualifiedName { get; init; } = "";

        /// <summary>
        /// 类型所在程序集名（RegisterGeneratedConfig 程序集清单用）
        /// </summary>
        public string AssemblyName { get; init; } = "";

        /// <summary>
        /// 去可空注解的完全限定显示名（Mapper.Map 泛型参数等 class 约束场景用）
        /// </summary>
        public string NonNullableFullName { get; init; } = "";

        /// <summary>
        /// 简单名（不含命名空间/泛型 arity）
        /// </summary>
        public string SimpleName { get; init; } = "";

        /// <summary>
        /// 命名空间显示名（顶层类型；嵌套类型取最外层命名空间）
        /// </summary>
        public string Namespace { get; init; } = "";

        /// <summary>
        /// MetadataName（含 arity，如 "List`1"；供输出期反查符号）
        /// </summary>
        public string MetadataName { get; init; } = "";

        /// <summary>
        /// 类型种类
        /// </summary>
        public TypeKind Kind { get; init; }

        /// <summary>
        /// 可访问性
        /// </summary>
        public Accessibility Accessibility { get; init; }

        /// <summary>
        /// 特殊类型（int/string/object 等）
        /// </summary>
        public SpecialType SpecialType { get; init; }

        /// <summary>
        /// 是否为值类型
        /// </summary>
        public bool IsValueType { get; init; }

        /// <summary>
        /// 是否为引用类型
        /// </summary>
        public bool IsReferenceType { get; init; }

        /// <summary>
        /// 是否为泛型类型（构造或未构造）
        /// </summary>
        public bool IsGenericType { get; init; }

        /// <summary>
        /// 是否为 Nullable&lt;T&gt; 包装
        /// </summary>
        public bool IsNullableValueType { get; init; }

        /// <summary>
        /// Nullable&lt;T&gt; 的底层类型 T（非可空时为 null）
        /// </summary>
        public TypeRef? UnderlyingType { get; init; }

        /// <summary>
        /// 是否为数组
        /// </summary>
        public bool IsArray { get; init; }

        /// <summary>
        /// 数组元素类型（非数组时为 null）
        /// </summary>
        public TypeRef? ElementType { get; init; }

        /// <summary>
        /// 是否实现 IEnumerable（含数组/List/字典等；输出期集合判断用）
        /// </summary>
        public bool ImplementsIEnumerable { get; init; }

        /// <summary>
        /// 是否为字典类型（实现 IDictionary&lt;,&gt; 或 IReadOnlyDictionary&lt;,&gt;）
        /// </summary>
        public bool IsDictionary { get; init; }

        /// <summary>
        /// IEnumerable&lt;T&gt; 的元素类型 T（非泛型枚举/数组时为 null）
        /// </summary>
        public TypeRef? EnumerableElementType { get; init; }

        /// <summary>
        /// 字典 Key 类型（非字典时为 null）
        /// </summary>
        public TypeRef? DictionaryKeyType { get; init; }

        /// <summary>
        /// 字典 Value 类型（非字典时为 null）
        /// </summary>
        public TypeRef? DictionaryValueType { get; init; }

        /// <summary>
        /// 泛型实参（含嵌套实参，如 List&lt;Dictionary&lt;K,V&gt;&gt; 递归展开）
        /// </summary>
        public IReadOnlyList<TypeRef> TypeArguments { get; init; } = Array.Empty<TypeRef>();

        /// <summary>
        /// 枚举成员（Name, Value 字符串；非枚举时为 null）
        /// </summary>
        public IReadOnlyList<EnumMemberRef>? EnumMembers { get; init; }

        /// <summary>
        /// 公共实例构造器形状（record/无无参构造器类型用）
        /// </summary>
        public IReadOnlyList<ConstructorRef>? Constructors { get; init; }

        /// <summary>
        /// 是否存在公共无参构造器
        /// </summary>
        public bool HasPublicParameterlessCtor { get; init; }

        /// <summary>
        /// 是否为 sealed
        /// </summary>
        public bool IsSealed { get; init; }

        /// <summary>
        /// 是否为 abstract
        /// </summary>
        public bool IsAbstract { get; init; }

        /// <summary>
        /// 是否为委托
        /// </summary>
        public bool IsDelegate { get; init; }

        /// <summary>
        /// 该类型标注的 [MapFrom(typeof(X))] 源类型集合（IsMapFromRegistered 输出期判定用）
        /// </summary>
        public IReadOnlyList<TypeRef> MapFromSources { get; init; } = Array.Empty<TypeRef>();

        /// <summary>
        /// 基类链全名（直接基类逐级到 object 终止；B1-06 compose 子源继承拓扑排序消费——
        /// 仅 INamedTypeSymbol 主物化分支填充，其余分支 null；单键相等设计不参与 EqualityKey：
        /// 同类型基类链恒同源，属发射显示派生字段——X2 排除集登记同 commit）
        /// </summary>
        public IReadOnlyList<string>? BaseTypeChainFullNames { get; init; }

        public bool Equals(TypeRef? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(EqualityKey, other.EqualityKey, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as TypeRef);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(EqualityKey);

        public override string ToString() => FullName;
    }

    /// <summary>
    /// 枚举成员快照（名称 + 底层值 InvariantCulture 字符串）
    /// </summary>
    internal readonly struct EnumMemberRef
    {
        /// <summary>
        /// 创建枚举成员快照
        /// </summary>
        /// <param name="name">成员名</param>
        /// <param name="value">底层值字符串</param>
        public EnumMemberRef(string name, string value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>
        /// 成员名
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 底层值字符串（InvariantCulture，跨文化稳定）
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 元组解构（兼容 foreach (var (name, value) in enumValues) 模式）
        /// </summary>
        /// <param name="name">解构输出的枚举成员名</param>
        /// <param name="value">解构输出的枚举成员值</param>
        public void Deconstruct(out string name, out string value)
        {
            name = Name;
            value = Value;
        }
    }

    /// <summary>
    /// 构造器参数快照
    /// </summary>
    internal sealed class ConstructorParameterRef : IEquatable<ConstructorParameterRef>
    {
        /// <summary>
        /// 参数名
        /// </summary>
        public string Name { get; init; } = "";

        /// <summary>
        /// 参数类型引用
        /// </summary>
        public TypeRef Type { get; init; } = null!;

        /// <summary>
        /// 是否有显式默认值（Optional 参数）
        /// </summary>
        public bool HasExplicitDefaultValue { get; init; }

        /// <summary>
        /// 显式默认值（原始对象，输出期按 Type.SpecialType 格式化）
        /// </summary>
        public object? ExplicitDefaultValue { get; init; }

        /// <summary>
        /// MapCtorParam 显式绑定源属性名
        /// 仅映射对目标类型顶层物化传入绑定表时按形参名填充（ByRef/指针形参跳过）；
        /// null = 未绑定（未用 MapCtorParam 的对快照结构与既有逐字段等价——B9 字节门前提）。
        /// Equals/GetHashCode 纳入本字段。
        /// </summary>
        public string? SourcePropertyName { get; init; }

        public bool Equals(ConstructorParameterRef? other)
        {
            if (other is null) return false;
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && Equals(Type, other.Type)
                   && HasExplicitDefaultValue == other.HasExplicitDefaultValue
                   && Equals(ExplicitDefaultValue, other.ExplicitDefaultValue)
                   && string.Equals(SourcePropertyName, other.SourcePropertyName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as ConstructorParameterRef);
        // GetHashCode 与 Equals 字段集对齐——标志位与默认值两项
        // 入哈希（原仅 Name/Type/SourcePropertyName 三项），同族于 字段守恒口径
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name) * 31
            + (Type?.GetHashCode() ?? 0) * 31
            + (SourcePropertyName is null ? 0 : StringComparer.Ordinal.GetHashCode(SourcePropertyName))
            + HasExplicitDefaultValue.GetHashCode()
            + (ExplicitDefaultValue?.GetHashCode() ?? 0);
    }

    /// <summary>
    /// 构造器快照（record/无无参构造器类型用）
    /// </summary>
    internal sealed class ConstructorRef : IEquatable<ConstructorRef>
    {
        /// <summary>
        /// 构造器参数快照列表
        /// </summary>
        public IReadOnlyList<ConstructorParameterRef> Parameters { get; init; } = Array.Empty<ConstructorParameterRef>();

        /// <summary>
        /// 是否为公共构造器
        /// </summary>
        public bool IsPublic { get; init; }

        public bool Equals(ConstructorRef? other)
        {
            if (other is null) return false;
            if (IsPublic != other.IsPublic) return false;
            if (Parameters.Count != other.Parameters.Count) return false;
            for (int i = 0; i < Parameters.Count; i++)
                if (!Parameters[i].Equals(other.Parameters[i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as ConstructorRef);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsPublic ? 17 : 19;
                foreach (var p in Parameters) hash = hash * 31 + p.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 属性引用值对象 — IPropertySymbol 的纯值快照（含特性参数物化）。
    /// </summary>
    internal sealed class PropertyRef : IEquatable<PropertyRef>
    {
        /// <summary>
        /// 属性名
        /// </summary>
        public string Name { get; init; } = "";

        /// <summary>
        /// 属性类型引用
        /// </summary>
        public TypeRef Type { get; init; } = null!;

        /// <summary>
        /// 是否为静态属性
        /// </summary>
        public bool IsStatic { get; init; }

        /// <summary>
        /// 是否为索引器
        /// </summary>
        public bool IsIndexer { get; init; }

        /// <summary>
        /// 属性声明的可访问性
        /// </summary>
        public Accessibility DeclaredAccessibility { get; init; }

        /// <summary>
        /// getter 可访问性（无 getter 时为 NotApplicable）
        /// </summary>
        public Accessibility GetMethodAccessibility { get; init; }

        /// <summary>
        /// setter 可访问性（无 setter 时为 NotApplicable）
        /// </summary>
        public Accessibility SetMethodAccessibility { get; init; }

        /// <summary>
        /// 是否为 init-only setter
        /// </summary>
        public bool IsInitOnly { get; init; }

        /// <summary>
        /// 是否为编译器隐式声明（record 的 EqualityContract 等）
        /// </summary>
        public bool IsImplicitlyDeclared { get; init; }

        /// <summary>
        /// 容器类型（属性所在类/接口；可为 null）
        /// </summary>
        public TypeRef? ContainingType { get; init; }

        /// <summary>
        /// 属性声明位置（诊断定位用；可为 null）
        /// </summary>
        public LocationSnapshot? Location { get; init; }

        // ---- 特性快照（物化规则）----

        /// <summary>
        /// 是否标注 [MapIgnore]
        /// </summary>
        public bool HasMapIgnore { get; init; }

        /// <summary>
        /// [MapIgnore] 的 Direction 底层值（0=Both,1=ToDto,2=ToEntity；未标注时为 0 但 HasMapIgnore=false）
        /// </summary>
        public int MapIgnoreDirection { get; init; }

        /// <summary>
        /// 是否标注 [MapProperty]（含 FromField/FromSource/IsFromJson 任意形态）
        /// </summary>
        public bool HasMapProperty { get; init; }

        /// <summary>
        /// 是否标注 [MapProperty(IsFromJson=true)]（JSON 反序列化形态）
        /// </summary>
        public bool IsFromJson { get; init; }

        /// <summary>
        /// 显式特性是否声明 OverrideIgnore=true（[MapProperty]/[Convert]/[Mask]/[ConvertBack]）
        /// </summary>
        public bool HasOverrideIgnore { get; init; }

        /// <summary>
        /// 是否标注 [Convert]
        /// </summary>
        public bool HasConvertUsing { get; init; }

        /// <summary>
        /// [Convert] 静态方法引用形态 — 锚定类型 FQN
        /// <para>null = 未标注</para>
        /// </summary>
        public string? ConverterDeclaringType { get; init; }

        /// <summary>
        /// [Convert] 静态方法名（与 <see cref="ConverterDeclaringType"/> 成对）
        /// <para>null = 未标注</para>
        /// </summary>
        public string? ConverterMethodName { get; init; }

        /// <summary>
        /// [Convert] 静态方法签名模板（T0-T3 四模板；None = 未命中/未标注）
        /// </summary>
        public ConverterShape ConverterShape { get; init; }

        /// <summary>
        /// 是否标注 [ConvertBack]（反向 DTO→Entity 转换器）
        /// </summary>
        public bool HasConvertBackUsing { get; init; }

        /// <summary>
        /// [ConvertBack] 静态方法引用形态 — 锚定类型 FQN（T-004a；
        /// "正反向三字段模型"——同一属性可同时标 [Convert]+[ConvertBack]，故反向独立三元组）
        /// <para>null = 未标注</para>
        /// </summary>
        public string? ConverterBackDeclaringType { get; init; }

        /// <summary>
        /// [ConvertBack] 静态方法名（与 <see cref="ConverterBackDeclaringType"/> 成对）
        /// <para>null = 未标注</para>
        /// </summary>
        public string? ConverterBackMethodName { get; init; }

        /// <summary>
        /// [ConvertBack] 静态方法签名模板（T0-T3 四模板；None = 未命中/未标注）
        /// </summary>
        public ConverterShape ConverterBackShape { get; init; }

        /// <summary>
        /// B 形态反向转换器转录体（Fluent ConvertBack/ConvertBackSource
        /// 内联 lambda 转录产物；A 形态/未声明 null）——ReversePlanner 复制到反向 pm 的
        /// ConverterMethod* 三字段，发射端 GenerateConverterMethods 物化为私有静态方法。
        /// </summary>
        public string? ConverterBackMethodBody { get; init; }

        /// <summary>：B 形态反向转换器形参类型组（A 形态 null）</summary>
        public string[]? ConverterBackMethodParamTypes { get; init; }

        /// <summary>：B 形态反向转换器形参名组（A 形态 null）</summary>
        public string[]? ConverterBackMethodParamNames { get; init; }

        /// <summary>
        /// 是否标注 [Mask]
        /// </summary>
        public bool HasMask { get; init; }

        /// <summary>
        /// 是否标注 [MapFormat]（接入面 g——NCMAP021 触发条件
        /// HasExplicitAttributeValue 的表征载体；[MapFormat] 无独立快照类型字段，
        /// 格式串文本经 pm 级 FormatString 承载）
        /// </summary>
        public bool HasMapFormat { get; init; }

        /// <summary>
        /// [Mask(typeof(X))] 的脱敏类型（未标注为 null）
        /// </summary>
        public TypeRef? MaskType { get; init; }

        /// <summary>
        /// [MapProperty] 的 FromField 参数（未标注为 null）
        /// </summary>
        public string? MapPropertyFromField { get; init; }

        /// <summary>
        /// [MapProperty] 的 FromSource 参数（未指定时为 null；显式声明含与主源相同者——
        /// 同源显式声明亦非 null， 注释校准项，产物实证）
        /// </summary>
        public TypeRef? MapPropertyFromSource { get; init; }

        /// <summary>
        /// 是否标注 [MapNoAutoInclude]
        /// </summary>
        public bool HasMapNoAutoInclude { get; init; }

        /// <summary>
        /// Fluent 显式声明标记——pm 产物来自 Fluent 声明面
        /// （Map/Convert/Mask/MapTree/FromJson）时置位；全局配置替换性重匹配
        /// （ReplaceWithConfigAwareMatchValue）据此豁免——显式声明为符号级路径，
        /// 不参与 NamingStrategy 名称规范化重匹配。
        /// <para>additive：特性通道恒默认 false；SameProperty（名+所属类型）不含本字段——
        /// 与 AllTargetProperties 独立物化的身份判定不受影响。</para>
        /// </summary>
        public bool HasFluentExplicit { get; init; }

        public bool Equals(PropertyRef? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && Equals(Type, other.Type)
                   && IsStatic == other.IsStatic
                   && IsIndexer == other.IsIndexer
                   && DeclaredAccessibility == other.DeclaredAccessibility
                   && GetMethodAccessibility == other.GetMethodAccessibility
                   && SetMethodAccessibility == other.SetMethodAccessibility
                   && IsInitOnly == other.IsInitOnly
                   && IsImplicitlyDeclared == other.IsImplicitlyDeclared
                   && Equals(ContainingType, other.ContainingType)
                   && Equals(Location, other.Location)
                   && HasMapIgnore == other.HasMapIgnore
                   && MapIgnoreDirection == other.MapIgnoreDirection
                   && HasMapProperty == other.HasMapProperty
                   && IsFromJson == other.IsFromJson
                   && HasOverrideIgnore == other.HasOverrideIgnore
                   && HasConvertUsing == other.HasConvertUsing
                   && HasConvertBackUsing == other.HasConvertBackUsing
                   && string.Equals(ConverterDeclaringType, other.ConverterDeclaringType, StringComparison.Ordinal)
                   && string.Equals(ConverterMethodName, other.ConverterMethodName, StringComparison.Ordinal)
                   && ConverterShape == other.ConverterShape
                   && string.Equals(ConverterBackDeclaringType, other.ConverterBackDeclaringType, StringComparison.Ordinal)
                   && string.Equals(ConverterBackMethodName, other.ConverterBackMethodName, StringComparison.Ordinal)
                   && ConverterBackShape == other.ConverterBackShape
                   // B 形态反向转换器三字段镜像
                   && string.Equals(ConverterBackMethodBody, other.ConverterBackMethodBody, StringComparison.Ordinal)
                   && (ConverterBackMethodParamTypes ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterBackMethodParamTypes ?? Array.Empty<string>(), StringComparer.Ordinal)
                   && (ConverterBackMethodParamNames ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterBackMethodParamNames ?? Array.Empty<string>(), StringComparer.Ordinal)
                   && HasMask == other.HasMask
                   && HasMapFormat == other.HasMapFormat
                   && Equals(MaskType, other.MaskType)
                   && string.Equals(MapPropertyFromField, other.MapPropertyFromField, StringComparison.Ordinal)
                   && Equals(MapPropertyFromSource, other.MapPropertyFromSource)
                   && HasMapNoAutoInclude == other.HasMapNoAutoInclude
                   && HasFluentExplicit == other.HasFluentExplicit;
        }

        public override bool Equals(object? obj) => Equals(obj as PropertyRef);

        /// <summary>
        /// 身份判定："同一属性"（Name + 所属类型）— 匹配/去重语义专用。
        /// <para>与值相等性（<see cref="Equals(PropertyRef?)"/>）解耦：值相等性参与增量缓存正确性，
        /// 含反向方法引用三元组等上下文相关字段——同一属性在不同物化上下文
        /// （pm.Target 携带 reverseConverterTarget 解析反向三元组 / AllTargetProperties 独立物化不携带）
        /// 下值不等，用作匹配键会把已映射属性误判为未匹配（T-004b，
        /// 实测全局配置重建场景 RegisterDate 补匹配重复）。</para>
        /// </summary>
        public bool SameProperty(PropertyRef? other)
            => other != null
               && string.Equals(Name, other.Name, StringComparison.Ordinal)
               && (ReferenceEquals(ContainingType, other.ContainingType)
                   || Equals(ContainingType, other.ContainingType));

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Name);
                hash = hash * 31 + (Type?.GetHashCode() ?? 0);
                hash = hash * 31 + IsInitOnly.GetHashCode();
                hash = hash * 31 + (int)SetMethodAccessibility;
                hash = hash * 31 + HasMapIgnore.GetHashCode();
                hash = hash * 31 + HasConvertUsing.GetHashCode();
                hash = hash * 31 + HasMask.GetHashCode();
                hash = hash * 31 + HasMapFormat.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 单对属性映射快照 — PropertyMapping 的纯值同构体。
    /// </summary>
    internal sealed class PropertyMappingSnapshot : IEquatable<PropertyMappingSnapshot>
    {
        /// <summary>
        /// 目标属性（DTO 侧）
        /// </summary>
        public PropertyRef Target { get; init; } = null!;

        /// <summary>
        /// 源属性；null = [Convert] 无匹配源属性场景
        /// </summary>
        public PropertyRef? Source { get; init; }

        /// <summary>
        /// 源属性访问路径（扁平化时 >1；非扁平化时 [Source] 或空）
        /// </summary>
        public IReadOnlyList<PropertyRef> SourcePath { get; init; } = Array.Empty<PropertyRef>();

        /// <summary>
        /// 路径段索引元数据— 与 <see cref="SourcePath"/> 等长并行列表：
        /// null = 纯属性段；非 null = 该属性段后跟 <c>[n]</c> 集合索引访问；参与相等性/哈希
        /// </summary>
        public IReadOnlyList<int?> SourceIndexes { get; init; } = Array.Empty<int?>();

        /// <summary>
        /// 路径是否含索引段
        /// </summary>
        public bool HasIndex => SourceIndexes.Count > 0 && SourceIndexes.Any(i => i != null);

        /// <summary>
        /// 反向写回目标链— 非 null 表示该属性在反向映射中执行路径写回：
        /// DTO 扁平属性（Source）→ 实体嵌套路径赋值（Target = 链末段叶属性）。
        /// <para>与正向 SourcePath 同形：链含叶段（[A, B, C] 中 C = Target 叶）；中间段在物化期已校验
        /// setter 可写（public 可重复赋值）且可实例化（class 非 abstract + 公共无参构造器，NCMAP031）。
        /// 参与相等性/哈希（增量缓存正确性，H19）。</para>
        /// </summary>
        public IReadOnlyList<PropertyRef>? ReverseWritePath { get; init; }

        /// <summary>
        /// 是否为反向写回属性（ReverseWritePath 非 null）
        /// </summary>
        public bool IsReverseWriteBack => ReverseWritePath is { Count: > 0 };

        /// <summary>
        /// 是否为扁平化属性（SourcePath 多段形态）
        /// </summary>
        public bool IsFlattened { get; init; }

        /// <summary>
        /// 路径来源— 参与相等性/哈希（H19 增量缓存正确性）
        /// </summary>
        public PathOrigin PathOrigin { get; init; }

        /// <summary>
        /// [Convert]/[ConvertBack] 静态方法引用形态 — 锚定类型 FQN（/// 三字段； 复用承载语义：
        /// 正向快照 = [Convert] 方法引用，反向快照 = [ConvertBack] 方法引用；null=无）
        /// </summary>
        public string? ConverterDeclaringType { get; init; }

        /// <summary>
        /// 静态方法名（与 <see cref="ConverterDeclaringType"/> 成对；null=无）
        /// </summary>
        public string? ConverterMethodName { get; init; }

        /// <summary>
        /// 静态方法签名模板（T0-T3 四模板；None = 未命中/无）
        /// </summary>
        public ConverterShape ConverterShape { get; init; }

        /// <summary>
        /// 脱敏类型（[Mask(typeof(X))]；null=无）
        /// </summary>
        public TypeRef? MaskType { get; init; }

        /// <summary>
        /// 格式化串（[MapFormat("fmt")] / Fluent .Format；
        /// 快照四处之主链值态；null=无）
        /// <para>与 <see cref="MaskType"/> 同区承载；发射端条件发射（null 不落产物行——字节门）</para>
        /// </summary>
        public string? FormatString { get; init; }

        /// <summary>
        /// 是否标注 [MapProperty(IsFromJson=true)]（JSON 反序列化形态）
        /// </summary>
        public bool IsFromJson { get; init; }

        /// <summary>
        /// 条件守卫方法引用 — "全限定类型::方法名"文本
        /// （A 形态=用户类型限定名；B 形态=生成类型限定名；null = 无条件）。
        /// <para>双侧同承载（P-e）；发射端实参渲染依据+ 运行时回退反射解析依据（A/B 双规则）。</para>
        /// </summary>
        public string? ConditionMethodRef { get; init; }

        /// <summary>：B 形态转录产物 — 重写后的表达式体文本（A 形态 null）</summary>
        public string? ConditionMethodBody { get; init; }

        /// <summary>：B 形态生成方法形参名（A 形态 null）</summary>
        public string? ConditionMethodParamName { get; init; }

        /// <summary>：谓词入参形态（T1 字段值 / T2 整源；None = 无条件）</summary>
        public ConditionShape ConditionShape { get; init; }

        /// <summary>
        /// B 形态转换器转录体 — 重写后的表达式体文本
        /// （InlineTranscriber 产物；A 形态（方法组）/无转换器 null）。
        /// <para>ConverterDeclaringType=生成类型限定名 + ConverterMethodName=生成方法名 +
        /// 本三字段成组——发射端 GenerateConverterMethods 物化为 GeneratedMapper 私有静态方法。</para>
        /// </summary>
        public string? ConverterMethodBody { get; init; }

        /// <summary>：B 形态转换器形参类型组（T0 空组/T1·T2 单/T3 双；A 形态 null）</summary>
        public string[]? ConverterMethodParamTypes { get; init; }

        /// <summary>：B 形态转换器形参名组（A 形态 null）</summary>
        public string[]? ConverterMethodParamNames { get; init; }

        public bool Equals(PropertyMappingSnapshot? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Target, other.Target)
                   && Equals(Source, other.Source)
                   && SourcePath.SequenceEqual(other.SourcePath)
                   && SourceIndexes.SequenceEqual(other.SourceIndexes)
                   && (ReverseWritePath == null
                       ? other.ReverseWritePath == null
                       : other.ReverseWritePath != null && ReverseWritePath.SequenceEqual(other.ReverseWritePath))
                   && IsFlattened == other.IsFlattened
                   && PathOrigin == other.PathOrigin
                   && string.Equals(ConverterDeclaringType, other.ConverterDeclaringType, StringComparison.Ordinal)
                   && string.Equals(ConverterMethodName, other.ConverterMethodName, StringComparison.Ordinal)
                   && ConverterShape == other.ConverterShape
                   && Equals(MaskType, other.MaskType)
                   // FormatString 镜像（增量缓存正确性——改格式串必须触发失效）
                   && string.Equals(FormatString, other.FormatString, StringComparison.Ordinal)
                   && IsFromJson == other.IsFromJson
                   // 条件守卫四字段镜像（HasMask 三件套同款
                   // 增量缓存正确性：改谓词（方法组/内联体）必须触发失效）
                   && string.Equals(ConditionMethodRef, other.ConditionMethodRef, StringComparison.Ordinal)
                   && string.Equals(ConditionMethodBody, other.ConditionMethodBody, StringComparison.Ordinal)
                   && string.Equals(ConditionMethodParamName, other.ConditionMethodParamName, StringComparison.Ordinal)
                   && ConditionShape == other.ConditionShape
                   // B 形态转换器三字段镜像（增量缓存正确性同款
                   // 改 Convert/ConvertBack 内联 lambda 体必须触发失效）
                   && string.Equals(ConverterMethodBody, other.ConverterMethodBody, StringComparison.Ordinal)
                   && (ConverterMethodParamTypes ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterMethodParamTypes ?? Array.Empty<string>(), StringComparer.Ordinal)
                   && (ConverterMethodParamNames ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterMethodParamNames ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as PropertyMappingSnapshot);
        public override int GetHashCode()
        {
            // + （执行期形态二修——lazy 缓存哈希，与 MappingSnapshot
            // 同款）：字段集零变化、深哈希每对象仅算一次（pm 对象被 MappingSnapshot 深哈希内层与
            // prep.InitOnlyExprPlans 反复消费——缓存后内层重复物化消除）。消费域 = 单线程编译器管线。
            if (_cachedHashCode is int cached) return cached;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Target?.GetHashCode() ?? 0);
                hash = hash * 31 + (Source?.GetHashCode() ?? 0);
                foreach (var p in SourcePath) hash = hash * 31 + p.GetHashCode();
                foreach (var idx in SourceIndexes) hash = hash * 31 + idx.GetHashCode();
                if (ReverseWritePath != null)
                    foreach (var p in ReverseWritePath) hash = hash * 31 + p.GetHashCode();
                else
                    hash = hash * 31 + 17;
                hash = hash * 31 + IsFlattened.GetHashCode();
                hash = hash * 31 + PathOrigin.GetHashCode();
                hash = hash * 31 + (ConverterDeclaringType != null ? StringComparer.Ordinal.GetHashCode(ConverterDeclaringType) : 0);
                hash = hash * 31 + (ConverterMethodName != null ? StringComparer.Ordinal.GetHashCode(ConverterMethodName) : 0);
                hash = hash * 31 + ConverterShape.GetHashCode();
                hash = hash * 31 + (MaskType?.GetHashCode() ?? 0);
                hash = hash * 31 + (FormatString != null ? StringComparer.Ordinal.GetHashCode(FormatString) : 0);
                hash = hash * 31 + IsFromJson.GetHashCode();
                // 条件守卫四字段镜像（与 Equals 字段集对齐）
                hash = hash * 31 + (ConditionMethodRef != null ? StringComparer.Ordinal.GetHashCode(ConditionMethodRef) : 0);
                hash = hash * 31 + (ConditionMethodBody != null ? StringComparer.Ordinal.GetHashCode(ConditionMethodBody) : 0);
                hash = hash * 31 + (ConditionMethodParamName != null ? StringComparer.Ordinal.GetHashCode(ConditionMethodParamName) : 0);
                hash = hash * 31 + ConditionShape.GetHashCode();
                // B 形态转换器三字段补齐（与 Equals 字段集对齐；
                // 集合以 foreach 聚合参与——null 与空组 Equals 视角相等，哈希同跳过，002-P1-1 契约口径）
                hash = hash * 31 + (ConverterMethodBody != null ? StringComparer.Ordinal.GetHashCode(ConverterMethodBody) : 0);
                if (ConverterMethodParamTypes != null)
                    foreach (var t in ConverterMethodParamTypes) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(t);
                if (ConverterMethodParamNames != null)
                    foreach (var n in ConverterMethodParamNames) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(n);
                _cachedHashCode = hash;
                return hash;
            }
        }
        private int? _cachedHashCode;
    }

    /// <summary>
    /// 多态子类型对登记快照
    /// 基类对 (S,T) 名下的一对子类映射登记（`.MapSubclass&lt;TSrcDerived, TTargetDerived&gt;()`）。
    /// <para>承载层级：FluentSnapshot.SubclassDeclarations（符号态）→ MappingInfo.SubclassPairs（符号态）
    /// → 本类型（值化 TypeRef 二元组，进增量缓存）。去重判定键 = (SourceType, TargetType) FQN 二元组
    /// （物化期查表，完全同对重复 → F47—— 诊断接线）。</para>
    /// <para>分派语义（发射端 消费）：isinst 可赋值匹配 + 最派生优先仲裁；
    /// TTargetDerived == TTarget（目标收敛）为合法用例。</para>
    /// </summary>
    internal sealed class SubclassPairSnapshot : IEquatable<SubclassPairSnapshot>
    {
        /// <summary>子类源类型（TSrcDerived : TSource——分派链 isinst 匹配类型）</summary>
        public TypeRef SourceType { get; init; } = null!;

        /// <summary>子类目标类型（TTargetDerived : TTarget——路由目标子类对的 Target）</summary>
        public TypeRef TargetType { get; init; } = null!;

        /// <summary>子类源简单名（符号 Name 口径——与 MappingSnapshot.SourceClassName 同源，
        /// 分派链引用的子类方法名拼接口径，泛型 arity 一致性由同源保证）</summary>
        public string SourceClassName { get; init; } = "";

        /// <summary>子类目标简单名（同 <see cref="SourceClassName"/> 口径）</summary>
        public string TargetClassName { get; init; } = "";

        /// <inheritdoc />
        public bool Equals(SubclassPairSnapshot? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(SourceType, other.SourceType)
                   && Equals(TargetType, other.TargetType)
                   && string.Equals(SourceClassName, other.SourceClassName, StringComparison.Ordinal)
                   && string.Equals(TargetClassName, other.TargetClassName, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as SubclassPairSnapshot);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (SourceType?.GetHashCode() ?? 0);
                hash = hash * 31 + (TargetType?.GetHashCode() ?? 0);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(SourceClassName);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(TargetClassName);
                return hash;
            }
        }
    }

    /// <summary>
    /// Compose 跨表映射快照 — ComposePropertyMapping 的纯值同构体。
    /// </summary>
    internal sealed class ComposeMappingSnapshot : IEquatable<ComposeMappingSnapshot>
    {
        /// <summary>
        /// Compose 子源类型（附加源）
        /// </summary>
        public TypeRef SubSource { get; init; } = null!;

        /// <summary>
        /// 子源映射的源属性
        /// </summary>
        public PropertyRef Source { get; init; } = null!;

        /// <summary>
        /// 源属性访问路径— 显式点路径时为多段；参与相等性/哈希（门控表第 15 点）
        /// </summary>
        public IReadOnlyList<PropertyRef> SourcePath { get; init; } = Array.Empty<PropertyRef>();

        /// <summary>
        /// 路径段索引元数据— 与 <see cref="SourcePath"/> 等长并行列表
        /// </summary>
        public IReadOnlyList<int?> SourceIndexes { get; init; } = Array.Empty<int?>();

        /// <summary>
        /// 路径是否含索引段
        /// </summary>
        public bool HasIndex => SourceIndexes.Count > 0 && SourceIndexes.Any(i => i != null);

        /// <summary>
        /// 子源映射的目标属性（DTO 侧）
        /// </summary>
        public PropertyRef Target { get; init; } = null!;

        /// <summary>
        /// [Convert] 静态方法引用形态 — 锚定类型 FQN（三字段；
        /// 跨表字段消费转换器）
        /// <para>null = 无转换器</para>
        /// </summary>
        public string? ConverterDeclaringType { get; init; }

        /// <summary>
        /// [Convert] 静态方法名（与 <see cref="ConverterDeclaringType"/> 成对）
        /// <para>null = 无转换器</para>
        /// </summary>
        public string? ConverterMethodName { get; init; }

        /// <summary>
        /// [Convert] 静态方法签名模板（T0-T3 四模板；None = 未命中/无转换器）
        /// </summary>
        public ConverterShape ConverterShape { get; init; }

        /// <summary>
        /// 脱敏类型（[Mask(typeof(X))]；已校验镜像通道：
        /// 提取端三段式校验通过的值；null=无或校验失败）
        /// <para>与 <see cref="PropertyMappingSnapshot.MaskType"/> 同构</para>
        /// </summary>
        public TypeRef? MaskType { get; init; }

        /// <summary>
        /// 格式化串（[MapFormat]；快照四处之 Compose 值态，已校验镜像通道；
        /// null=无或校验失败）
        /// <para>与 <see cref="PropertyMappingSnapshot.FormatString"/> 同构（Compose 子映射格式化承载）</para>
        /// </summary>
        public string? FormatString { get; init; }

        /// <summary>
        /// 是否标注 [MapProperty(IsFromJson=true)]（JSON 反序列化形态；
        /// 已校验镜像通道：值类型守卫通过）
        /// </summary>
        public bool IsFromJson { get; init; }

        /// <summary>
        /// FromSource 块内 MapWhen 条件守卫方法引用
        /// （"全限定类型::方法名"；null = 无条件）——发射落点 = Update_{Sub}_To_{Target} 赋值层。
        /// </summary>
        public string? ConditionMethodRef { get; init; }

        /// <summary>：B 形态转录产物（A 形态 null）</summary>
        public string? ConditionMethodBody { get; init; }

        /// <summary>：B 形态生成方法形参名（A 形态 null）</summary>
        public string? ConditionMethodParamName { get; init; }

        /// <summary>：谓词入参形态（T1 子源字段值 / T2 子源对象；None = 无条件）</summary>
        public ConditionShape ConditionShape { get; init; }

        /// <summary>
        /// B 形态转换器转录体（块内 Convert 内联 lambda 转录产物；
        /// A 形态/无转换器 null）——发射端 GenerateConverterMethods 消费。
        /// </summary>
        public string? ConverterMethodBody { get; init; }

        /// <summary>：B 形态转换器形参类型组（A 形态 null）</summary>
        public string[]? ConverterMethodParamTypes { get; init; }

        /// <summary>：B 形态转换器形参名组（A 形态 null）</summary>
        public string[]? ConverterMethodParamNames { get; init; }

        public bool Equals(ComposeMappingSnapshot? other)
        {
            if (other is null) return false;
            return Equals(SubSource, other.SubSource)
                   && Equals(Source, other.Source)
                   && SourcePath.SequenceEqual(other.SourcePath)
                   && SourceIndexes.SequenceEqual(other.SourceIndexes)
                   && Equals(Target, other.Target)
                   && string.Equals(ConverterDeclaringType, other.ConverterDeclaringType, StringComparison.Ordinal)
                   && string.Equals(ConverterMethodName, other.ConverterMethodName, StringComparison.Ordinal)
                   && ConverterShape == other.ConverterShape
                   // Mask/Json 新字段镜像（与 PropertyMappingSnapshot 口径一致）
                   && Equals(MaskType, other.MaskType)
                   // FormatString 镜像（与 PropertyMappingSnapshot 口径一致）
                   && string.Equals(FormatString, other.FormatString, StringComparison.Ordinal)
                   && IsFromJson == other.IsFromJson
                   // 条件守卫四字段镜像
                   && string.Equals(ConditionMethodRef, other.ConditionMethodRef, StringComparison.Ordinal)
                   && string.Equals(ConditionMethodBody, other.ConditionMethodBody, StringComparison.Ordinal)
                   && string.Equals(ConditionMethodParamName, other.ConditionMethodParamName, StringComparison.Ordinal)
                   && ConditionShape == other.ConditionShape
                   // B 形态转换器三字段镜像（增量缓存正确性同款）
                   && string.Equals(ConverterMethodBody, other.ConverterMethodBody, StringComparison.Ordinal)
                   && (ConverterMethodParamTypes ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterMethodParamTypes ?? Array.Empty<string>(), StringComparer.Ordinal)
                   && (ConverterMethodParamNames ?? Array.Empty<string>()).SequenceEqual(
                       other.ConverterMethodParamNames ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as ComposeMappingSnapshot);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (SubSource?.GetHashCode() ?? 0);
                hash = hash * 31 + (Source?.GetHashCode() ?? 0);
                foreach (var p in SourcePath) hash = hash * 31 + p.GetHashCode();
                foreach (var idx in SourceIndexes) hash = hash * 31 + idx.GetHashCode();
                hash = hash * 31 + (Target?.GetHashCode() ?? 0);
                hash = hash * 31 + (ConverterDeclaringType != null ? StringComparer.Ordinal.GetHashCode(ConverterDeclaringType) : 0);
                hash = hash * 31 + (ConverterMethodName != null ? StringComparer.Ordinal.GetHashCode(ConverterMethodName) : 0);
                hash = hash * 31 + ConverterShape.GetHashCode();
                // Mask/Json 新字段镜像（与 Equals 字段集对齐）
                hash = hash * 31 + (MaskType?.GetHashCode() ?? 0);
                hash = hash * 31 + (FormatString != null ? StringComparer.Ordinal.GetHashCode(FormatString) : 0);
                hash = hash * 31 + IsFromJson.GetHashCode();
                // 条件守卫四字段镜像（与 Equals 字段集对齐）
                hash = hash * 31 + (ConditionMethodRef != null ? StringComparer.Ordinal.GetHashCode(ConditionMethodRef) : 0);
                hash = hash * 31 + (ConditionMethodBody != null ? StringComparer.Ordinal.GetHashCode(ConditionMethodBody) : 0);
                hash = hash * 31 + (ConditionMethodParamName != null ? StringComparer.Ordinal.GetHashCode(ConditionMethodParamName) : 0);
                hash = hash * 31 + ConditionShape.GetHashCode();
                // B 形态转换器三字段补齐（与 Equals 字段集对齐；
                // 集合以 foreach 聚合参与——null 与空组 Equals 视角相等，哈希同跳过，002-P1-1 契约口径）
                hash = hash * 31 + (ConverterMethodBody != null ? StringComparer.Ordinal.GetHashCode(ConverterMethodBody) : 0);
                if (ConverterMethodParamTypes != null)
                    foreach (var t in ConverterMethodParamTypes) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(t);
                if (ConverterMethodParamNames != null)
                    foreach (var n in ConverterMethodParamNames) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(n);
                return hash;
            }
        }
    }

    /// <summary>
    /// 诊断位置快照 — (FilePath, TextSpan, LineSpan)，输出期重建 external Location（S3，：
    /// 加 LineSpan 后重建不再依赖 Compilation 树匹配——解除输出级 Combine 的前置）。
    /// </summary>
    internal readonly struct LocationSnapshot : IEquatable<LocationSnapshot>
    {
        /// <summary>
        /// 创建诊断位置快照
        /// </summary>
        /// <param name="filePath">源文件路径</param>
        /// <param name="span">文本跨度</param>
        /// <param name="lineSpan">行列跨度</param>
        public LocationSnapshot(string filePath, TextSpan span, LinePositionSpan lineSpan)
        {
            FilePath = filePath;
            Span = span;
            LineSpan = lineSpan;
        }

        /// <summary>
        /// 源文件路径
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// 文本跨度
        /// </summary>
        public TextSpan Span { get; }

        /// <summary>
        /// 行列跨度
        /// </summary>
        public LinePositionSpan LineSpan { get; }

        public bool Equals(LocationSnapshot other)
            => string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
               && Span == other.Span
               && LineSpan == other.LineSpan;

        public override bool Equals(object? obj) => obj is LocationSnapshot other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(FilePath) * 31 + Span.GetHashCode();
    }

    /// <summary>
    /// 诊断快照 — Descriptor 以 Id 标识（发射期回查 static Descriptor），Args 全部转字符串。
    /// </summary>
    internal sealed class DiagnosticSnapshot : IEquatable<DiagnosticSnapshot>
    {
        /// <summary>
        /// 诊断 Id（如 "NCMAP001"）
        /// </summary>
        public string DescriptorId { get; init; } = "";

        /// <summary>
        /// 诊断参数（全部字符串化）
        /// </summary>
        public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

        /// <summary>
        /// 诊断位置（可为 null → Location.None）
        /// </summary>
        public LocationSnapshot? Location { get; init; }

        /// <summary>
        /// 显式语义标记（NCMAP001：[MapProperty] 显式 FromField 错误始终报，不受选项抑制——B-6）
        /// </summary>
        public bool IsExplicit { get; init; }

        public bool Equals(DiagnosticSnapshot? other)
        {
            if (other is null) return false;
            return string.Equals(DescriptorId, other.DescriptorId, StringComparison.Ordinal)
                   && Args.SequenceEqual(other.Args)
                   && Equals(Location, other.Location)
                   && IsExplicit == other.IsExplicit;
        }

        public override bool Equals(object? obj) => Equals(obj as DiagnosticSnapshot);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(DescriptorId);
                foreach (var a in Args) hash = hash * 31 + (a?.GetHashCode() ?? 0);
                hash = hash * 31 + (Location?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    // ================================================================
    // DeepCopy 类型图节点快照— 值域分类器输入
    // 物化期按可达图闭包展开（EqualityKey 键控缓存），输出期零反查符号
    // ================================================================

    /// <summary>
    /// DeepCopy 属性边快照（分类器属性遍历 + 生成路径属性消费的输入）
    /// </summary>
    internal sealed class DeepCopyPropertyShape : IEquatable<DeepCopyPropertyShape>
    {
        /// <summary>
        /// 属性名
        /// </summary>
        public string Name { get; init; } = "";

        /// <summary>
        /// 属性类型引用
        /// </summary>
        public TypeRef Type { get; init; } = null!;

        /// <summary>
        /// 公开可读（getter 为 public；分类器属性图口径）
        /// </summary>
        public bool GetterIsPublic { get; init; }

        /// <summary>
        /// F1 判定输入：getter virtual
        /// </summary>
        public bool GetterIsVirtual { get; init; }

        /// <summary>
        /// F1 判定输入：getter sealed（隐式接口实现 CLR virtual+final → Roslyn IsVirtual=true 且 IsSealed=true）
        /// </summary>
        public bool GetterIsSealed { get; init; }

        /// <summary>
        /// F1 判定输入：容器类型 sealed
        /// </summary>
        public bool ContainingTypeIsSealed { get; init; }

        /// <summary>
        /// 是否有 setter（无参路径可写性过滤）
        /// </summary>
        public bool HasSetter { get; init; }

        /// <summary>
        /// setter 是否 public
        /// </summary>
        public bool SetterIsPublic { get; init; }

        /// <summary>
        /// setter 是否为 init-only（构造后只读）
        /// </summary>
        public bool SetterIsInitOnly { get; init; }

        /// <summary>
        /// 是否标注 [MapIgnore]（无参路径跳过）
        /// </summary>
        public bool HasMapIgnore { get; init; }

        /// <summary>
        /// 属性声明位置（NCMAP016 未知边定位）
        /// </summary>
        public LocationSnapshot? Location { get; init; }

        public bool Equals(DeepCopyPropertyShape? other)
        {
            if (other is null) return false;
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && Equals(Type, other.Type)
                   && GetterIsPublic == other.GetterIsPublic
                   && GetterIsVirtual == other.GetterIsVirtual
                   && GetterIsSealed == other.GetterIsSealed
                   && ContainingTypeIsSealed == other.ContainingTypeIsSealed
                   && HasSetter == other.HasSetter
                   && SetterIsPublic == other.SetterIsPublic
                   && SetterIsInitOnly == other.SetterIsInitOnly
                   && HasMapIgnore == other.HasMapIgnore
                   && Equals(Location, other.Location);
        }

        public override bool Equals(object? obj) => Equals(obj as DeepCopyPropertyShape);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Name);
                hash = hash * 31 + (Type?.GetHashCode() ?? 0);
                hash = hash * 31 + GetterIsVirtual.GetHashCode();
                hash = hash * 31 + GetterIsSealed.GetHashCode();
                hash = hash * 31 + SetterIsInitOnly.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// DeepCopy 类型图节点快照（值域分类器输入；S3）
    /// </summary>
    internal sealed class DeepCopyNodeShape : IEquatable<DeepCopyNodeShape>
    {
        /// <summary>
        /// 节点类型引用
        /// </summary>
        public TypeRef Type { get; init; } = null!;

        /// <summary>
        /// 类型声明位置（NCMAP012/NCMAP016 诊断定位）
        /// </summary>
        public LocationSnapshot? Location { get; init; }

        /// <summary>
        /// 是否存在公共无参构造器
        /// </summary>
        public bool HasPublicParameterlessCtor { get; init; }

        /// <summary>
        /// 公共构造器快照列表
        /// </summary>
        public IReadOnlyList<ConstructorRef> PublicConstructors { get; init; } = Array.Empty<ConstructorRef>();

        /// <summary>
        /// 公开实例可读非索引器属性（含继承链，最派生去重；分类器属性图）
        /// </summary>
        public IReadOnlyList<DeepCopyPropertyShape> Properties { get; init; } = Array.Empty<DeepCopyPropertyShape>();

        // ---- 叶子/容器判定输入（物化期计算，镜像 DeepCopyGraphRules）----
        /// <summary>
        /// 是否为叶子类型（值类型/string/Delegate 或命中叶子基类清单）
        /// </summary>
        public bool IsLeaf { get; init; }

        /// <summary>
        /// 是否为集合容器（数组或 IEnumerable 且非 string）
        /// </summary>
        public bool IsCollectionLike { get; init; }

        // ---- 容器边（CollectContainerEdges 值域输入）----
        /// <summary>
        /// 是否为字典容器
        /// </summary>
        public bool IsDictionary { get; init; }

        /// <summary>
        /// 字典键类型（IsDictionary 时有效）
        /// </summary>
        public TypeRef? KeyType { get; init; }

        /// <summary>
        /// 字典值类型（IsDictionary 时有效）
        /// </summary>
        public TypeRef? ValueType { get; init; }

        /// <summary>
        /// 集合元素类型（泛型 IEnumerable&lt;T&gt; 时有效）
        /// </summary>
        public TypeRef? ElementType { get; init; }

        /// <summary>
        /// 是否为非泛型 IEnumerable
        /// </summary>
        public bool IsNonGenericEnumerable { get; init; }

        // ---- 边目标解包（CollectEdgeTargets 值域输入）----
        /// <summary>
        /// 是否为数组
        /// </summary>
        public bool IsArray { get; init; }

        /// <summary>
        /// 数组元素类型（IsArray 时有效）
        /// </summary>
        public TypeRef? ArrayElementType { get; init; }

        /// <summary>
        /// 是否为可空值类型（Nullable&lt;T&gt;）
        /// </summary>
        public bool IsNullableValueType { get; init; }

        /// <summary>
        /// 底层类型（Nullable&lt;T&gt; → T）
        /// </summary>
        public TypeRef? UnderlyingType { get; init; }

        /// <summary>
        /// 是否为接口
        /// </summary>
        public bool IsInterface { get; init; }

        /// <summary>
        /// 是否为泛型类型参数
        /// </summary>
        public bool IsTypeParameter { get; init; }

        /// <summary>
        /// 是否为指针类型
        /// </summary>
        public bool IsPointer { get; init; }

        /// <summary>
        /// 是否为值类型
        /// </summary>
        public bool IsValueType { get; init; }

        /// <summary>
        /// 是否为 string
        /// </summary>
        public bool IsString { get; init; }

        /// <summary>
        /// 是否为委托类型
        /// </summary>
        public bool IsDelegate { get; init; }

        public bool Equals(DeepCopyNodeShape? other)
        {
            if (other is null) return false;
            return Equals(Type, other.Type)
                   && Equals(Location, other.Location)
                   && HasPublicParameterlessCtor == other.HasPublicParameterlessCtor
                   && PublicConstructors.SequenceEqual(other.PublicConstructors)
                   && Properties.SequenceEqual(other.Properties)
                   && IsLeaf == other.IsLeaf
                   && IsCollectionLike == other.IsCollectionLike
                   && IsDictionary == other.IsDictionary
                   && Equals(KeyType, other.KeyType)
                   && Equals(ValueType, other.ValueType)
                   && Equals(ElementType, other.ElementType)
                   && IsNonGenericEnumerable == other.IsNonGenericEnumerable
                   && IsArray == other.IsArray
                   && Equals(ArrayElementType, other.ArrayElementType)
                   && IsNullableValueType == other.IsNullableValueType
                   && Equals(UnderlyingType, other.UnderlyingType)
                   && IsInterface == other.IsInterface
                   && IsTypeParameter == other.IsTypeParameter
                   && IsPointer == other.IsPointer
                   && IsValueType == other.IsValueType
                   && IsString == other.IsString
                   && IsDelegate == other.IsDelegate;
        }

        public override bool Equals(object? obj) => Equals(obj as DeepCopyNodeShape);
        // 与 Equals 字段集对齐（22 项，原仅 Type 单项）
        // 无 ISymbol 字段（TypeRef/LocationSnapshot/值/集合族）；集合以 foreach 聚合参与，
        // null 与空组 Equals 视角相等，哈希同跳过（002-P1-1 契约口径）
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Type?.GetHashCode() ?? 0);
                hash = hash * 31 + (Location?.GetHashCode() ?? 0);
                hash = hash * 31 + HasPublicParameterlessCtor.GetHashCode();
                if (PublicConstructors != null)
                    foreach (var c in PublicConstructors) hash = hash * 31 + c.GetHashCode();
                if (Properties != null)
                    foreach (var p in Properties) hash = hash * 31 + p.GetHashCode();
                hash = hash * 31 + IsLeaf.GetHashCode();
                hash = hash * 31 + IsCollectionLike.GetHashCode();
                hash = hash * 31 + IsDictionary.GetHashCode();
                hash = hash * 31 + (KeyType?.GetHashCode() ?? 0);
                hash = hash * 31 + (ValueType?.GetHashCode() ?? 0);
                hash = hash * 31 + (ElementType?.GetHashCode() ?? 0);
                hash = hash * 31 + IsNonGenericEnumerable.GetHashCode();
                hash = hash * 31 + IsArray.GetHashCode();
                hash = hash * 31 + (ArrayElementType?.GetHashCode() ?? 0);
                hash = hash * 31 + IsNullableValueType.GetHashCode();
                hash = hash * 31 + (UnderlyingType?.GetHashCode() ?? 0);
                hash = hash * 31 + IsInterface.GetHashCode();
                hash = hash * 31 + IsTypeParameter.GetHashCode();
                hash = hash * 31 + IsPointer.GetHashCode();
                hash = hash * 31 + IsValueType.GetHashCode();
                hash = hash * 31 + IsString.GetHashCode();
                hash = hash * 31 + IsDelegate.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// Unknown 分类时不可解析边快照（NCMAP016 逐边报告）
    /// </summary>
    internal sealed class UnknownEdgeSnapshot
    {
        /// <summary>
        /// 所属类型全名
        /// </summary>
        public string OwnerType { get; init; } = "";

        /// <summary>
        /// 属性名
        /// </summary>
        public string PropertyName { get; init; } = "";

        /// <summary>
        /// 属性类型显示名
        /// </summary>
        public string PropertyTypeDisplay { get; init; } = "";

        /// <summary>
        /// 属性声明位置（诊断定位）
        /// </summary>
        public LocationSnapshot? Location { get; init; }
    }

    /// <summary>
    /// 树构建配置三元组快照
    /// <c>.Tree()</c> 终端声明的值形态（record 值相等，参与 MappingSnapshot 相等性）。
    /// </summary>
    /// <summary>
    /// 顶层映射快照 — MappingInfo 的纯值同构体（不含 TargetSyntax/MethodSuffix：
    /// 位置改为 TargetIdentifierLocation；后缀为输出期纯函数派生，不进缓存）。
    /// </summary>
    internal sealed record FluentTreeConfigTriple(string IdProperty, string ParentIdProperty, string ChildrenProperty);

    internal sealed class MappingSnapshot : IEquatable<MappingSnapshot>
    {
        /// <summary>
        /// 源类型引用
        /// </summary>
        public TypeRef SourceType { get; init; } = null!;

        /// <summary>
        /// 目标类型引用
        /// </summary>
        public TypeRef TargetType { get; init; } = null!;

        /// <summary>
        /// 源类型类名（生成方法命名用）
        /// </summary>
        public string SourceClassName { get; init; } = "";

        /// <summary>
        /// 目标类型类名（生成方法命名用）
        /// </summary>
        public string TargetClassName { get; init; } = "";

        /// <summary>
        /// NCMAP011：non-public 类型跳过代码生成
        /// </summary>
        public bool SkipCodeGeneration { get; init; }

        // ---- DiagnosticOptions（两布尔展开，值语义）----
        /// <summary>
        /// 未映射目标成员报告开关（值语义展开）
        /// </summary>
        public bool ReportUnmappedTargetMembers { get; init; }

        /// <summary>
        /// 未映射源成员报告开关（值语义展开）
        /// </summary>
        public bool ReportUnmappedSourceMembers { get; init; }

        /// <summary>
        /// 目标类型是否标注类级 [NCMapperOptions]（程序集级管线注入仅在无类级标注时覆盖）
        /// </summary>
        public bool HasClassLevelDiagnosticOptions { get; init; }

        /// <summary>
        /// 属性映射快照列表（正向映射主体）
        /// </summary>
        public IReadOnlyList<PropertyMappingSnapshot> PropertyMappings { get; init; } = Array.Empty<PropertyMappingSnapshot>();

        /// <summary>
        /// 所有可匹配目标属性候选（配置感知补匹配载体）
        /// </summary>
        public IReadOnlyList<PropertyRef> AllTargetProperties { get; init; } = Array.Empty<PropertyRef>();

        /// <summary>
        /// 源类型全部 public 可读属性（配置感知补匹配的源侧候选；值版 FindProperty 载体）
        /// </summary>
        public IReadOnlyList<PropertyRef> SourceProperties { get; init; } = Array.Empty<PropertyRef>();

        /// <summary>DeepCopy 类型图节点闭包（本 mapping 的 SourceType/TargetType 根的
        /// 可达图节点集合，物化期展开；输出期值域分类器输入——零反查）</summary>
        public IReadOnlyList<DeepCopyNodeShape> DeepCopyNodes { get; init; } = Array.Empty<DeepCopyNodeShape>();

        /// <summary>
        /// Compose 跨表映射快照列表（Compose 场景非空）
        /// </summary>
        public IReadOnlyList<ComposeMappingSnapshot> ComposeMappings { get; init; } = Array.Empty<ComposeMappingSnapshot>();

        /// <summary>
        /// 反向无源转换器映射快照列表— 实体属性标 [ConvertBack] 且
        /// 覆盖排除集外的条目（Target=实体属性、Source=null、三元组=反向转换器）
        /// <para>消费端：ReversePlanner.CreateReverseMapping（追加于反向主 foreach 之后）；
        /// 参与相等性/哈希（增量缓存正确性——与 ComposeMappings 同口径）。</para>
        /// </summary>
        public IReadOnlyList<PropertyMappingSnapshot> ReverseOnlyConvertBacks { get; init; } = Array.Empty<PropertyMappingSnapshot>();

        /// <summary>
        /// 映射诊断快照列表
        /// </summary>
        public IReadOnlyList<DiagnosticSnapshot> Diagnostics { get; init; } = Array.Empty<DiagnosticSnapshot>();

        /// <summary>
        /// 目标类型标识符位置（替代 TargetSyntax.Identifier.GetLocation()）
        /// </summary>
        public LocationSnapshot? TargetIdentifierLocation { get; init; }

        /// <summary>
        /// Fluent 通道 For&lt;S,T&gt;() 调用点位置（T2，NCMAP025 定位分通道
        /// 互指诊断的 Fluent 侧定位指向 For invocation 而非 [MapperConfig] 类声明标识符）。
        /// LocationSnapshot 纯值物化（S3 同款——输出值化增量缓存安全前提，F4 探针锁定）；
        /// [MapFrom] 特性通道恒 null（定位维持 TargetIdentifierLocation）。
        /// </summary>
        public LocationSnapshot? ForInvocationLocation { get; init; }

        /// <summary>
        /// 对级 NoAutoInclude（additive ≤2 字段之一）
        /// 禁用该 (S,T) 对的自动匹配/自动包含（含自动扁平化），仅映射显式声明的属性；
        /// 与特性版属性级 <see cref="PropertyRef.HasMapNoAutoInclude"/> 双承载并行（互不迁移）。
        /// <para>[MapFrom] 通道默认 false；标注 [MapNoAutoMatch] 时 true（
        /// 提取链填充 + ReversePlanner 反向快照透传带位）；消费端按
        /// <c>EqualityComparer&lt;bool&gt;.Default.Equals(field, default)</c> 判定
        /// （v1.2 自查③硬约束——值类型默认值漂移防御）。</para>
        /// </summary>
        public bool NoAutoInclude { get; init; }

        /// <summary>
        /// Fluent 通道 AfterMapping 后置回调方法引用（additive ≤2 字段之一）
        /// 形态 "{ContainingType.ToDisplayString()}.{MethodName}"；发射端在共享核心方法
        /// <c>Map_Src_To_Tgt</c> 末尾发射调用（6 正向形态继承；反向 4 形态不发射）。
        /// <para>[MapFrom] 通道产物恒默认 null；消费端必须 <c>string.IsNullOrEmpty</c> 短路
        /// （v1.2 自查③——默认值时生成代码与现状逐字节一致， F4 字节不变保证）。</para>
        /// </summary>
        public string? AfterMappingMethodRef { get; init; }

        /// <summary>
        /// 生命周期前置回调方法引用
        /// Fluent 通道 <c>.BeforeMapping(...)</c> 与特性通道 <c>[BeforeMapping]</c>双侧填充
        /// 同一承载字段；形态 "{ContainingType.ToDisplayString()}.{MethodName}"；发射端在
        /// Core/Update/MapListInto 各站点首条属性赋值前发射调用（构造器一体化目标不发射——F37 族）。
        /// <para>未声明恒默认 null；消费端必须 <c>string.IsNullOrEmpty</c> 短路
        ///（G3 字节不变保证——无钩子声明的映射对生成代码逐字节不变）。</para>
        /// </summary>
        public string? BeforeMappingMethodRef { get; init; }

        /// <summary>
        /// Fluent 通道正向方向标记（消费补齐，additive）
        /// For&lt;S,T&gt; 声明 S=Entity、T=DTO，正向映射写入 DTO 侧。特性通道方向由目标 [MapFrom]
        /// 推导（IsToDtoMapping 读 MapFromSources），Fluent 声明无该特性——本标记补足方向语义
        ///（生成端 ShouldSkipForDirectionPair / ShouldIgnoreInDirectionValue 方向过滤正确判定）。
        /// <para>[MapFrom] 通道与反向派生快照（ReversePlanner 全新构造）恒默认 false；
        /// false 时 IsToDtoMapping 回落 [MapFrom] 推导——特性通道生成代码逐字节不变。</para>
        /// </summary>
        public bool IsFluentForward { get; init; }

        /// <summary>
        /// Fluent 通道树构建配置—— <c>.Tree()</c> 终端三元组；
        /// null = 未调用（opt-in）。[MapFrom] 通道产物恒默认 null——发射端条件行（null 不落产物行），
        /// 既有产物逐字节不变（F1 字节门）。
        /// </summary>
        public FluentTreeConfigTriple? TreeConfig { get; init; }

        /// <summary>
        /// Fluent 通道属性级 NoAutoInclude 目标属性名集
        /// <c>.NoAutoInclude(dest)</c> 声明；空 = 无声明。对级承载（收集器消费面为全部可写属性，
        /// 与 pm 存在性无关——pm 缺失场景语义完备）。[MapFrom] 通道恒空——发射端条件行（空不落产物行）。
        /// </summary>
        public IReadOnlyList<string> PropertyNoAutoIncludeTargets { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Fluent 通道含 Compose 多源声明——降级判定输入（FromSource 属性
        /// 无 pm 明细，运行时判不可翻译的唯一可见信号）。[MapFrom] 通道恒 false——发射端条件行
        /// （false 不落产物行，F1 字节门）。
        /// </summary>
        public bool HasComposeMappings { get; init; }

        /// <summary>
        /// 多态子类型对登记清单——基类对名下的子类映射
        /// 登记集合（`.MapSubclass&lt;TSrcDerived, TTargetDerived&gt;()` / 特性侧 [MapSubclass] 同承载）。
        /// <para>空 = 无登记（发射端零分派产物，G3 字节门）； 四族分派发射与 运行时承载
        /// （HasSubclassDispatch 降级判定输入）消费。默认值中性（空集合）——[MapFrom] 通道未声明时
        /// 产物逐字节不变。</para>
        /// </summary>
        public IReadOnlyList<SubclassPairSnapshot> SubclassPairs { get; init; } = Array.Empty<SubclassPairSnapshot>();

        public bool Equals(MappingSnapshot? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(SourceType, other.SourceType)
                   && Equals(TargetType, other.TargetType)
                   && string.Equals(SourceClassName, other.SourceClassName, StringComparison.Ordinal)
                   && string.Equals(TargetClassName, other.TargetClassName, StringComparison.Ordinal)
                   && SkipCodeGeneration == other.SkipCodeGeneration
                   && ReportUnmappedTargetMembers == other.ReportUnmappedTargetMembers
                   && ReportUnmappedSourceMembers == other.ReportUnmappedSourceMembers
                   && HasClassLevelDiagnosticOptions == other.HasClassLevelDiagnosticOptions
                   && PropertyMappings.SequenceEqual(other.PropertyMappings)
                   && AllTargetProperties.SequenceEqual(other.AllTargetProperties)
                   && SourceProperties.SequenceEqual(other.SourceProperties)
                   && DeepCopyNodes.SequenceEqual(other.DeepCopyNodes)
                   && ComposeMappings.SequenceEqual(other.ComposeMappings)
                   && ReverseOnlyConvertBacks.SequenceEqual(other.ReverseOnlyConvertBacks)
                   && Diagnostics.SequenceEqual(other.Diagnostics)
                   && Equals(TargetIdentifierLocation, other.TargetIdentifierLocation)
                   // ForInvocationLocation 参与相等性（增量缓存正确性
                   // 编辑 For 调用点位置 → 值变化 → 重算；纯值物化 S3 同款）
                   && Equals(ForInvocationLocation, other.ForInvocationLocation)
                   // Fluent additive 两字段参与相等性（增量缓存正确性）
                   && NoAutoInclude == other.NoAutoInclude
                   && string.Equals(AfterMappingMethodRef, other.AfterMappingMethodRef, StringComparison.Ordinal)
                   // 前置钩子引用参与相等性（增量缓存正确性）
                   && string.Equals(BeforeMappingMethodRef, other.BeforeMappingMethodRef, StringComparison.Ordinal)
                   // Fluent 正向方向标记参与相等性（增量缓存正确性）
                   && IsFluentForward == other.IsFluentForward
                   // Fluent 树配置/属性级 NoAutoInclude 参与相等性（增量缓存正确性；
                   // FluentTreeConfigTriple 为 record 值相等——null 语义经 ?. 直比）
                   && TreeConfig == other.TreeConfig
                   && PropertyNoAutoIncludeTargets.SequenceEqual(other.PropertyNoAutoIncludeTargets, StringComparer.Ordinal)
                   && HasComposeMappings == other.HasComposeMappings
                   // 多态子类型对参与相等性（增量缓存正确性）
                   && SubclassPairs.SequenceEqual(other.SubclassPairs);
        }

        public override bool Equals(object? obj) => Equals(obj as MappingSnapshot);
        public override int GetHashCode()
        {
            // B3-13+B4-11（执行期形态二修）：**lazy 缓存哈希**
            // 首选的"子集浅化"形态被既有守护测试否决（EqualityConservationTests.TwinMutation_GetHashCode_
            // Changes_ForHashableScalars / SnapshotConservationTests.EqualsAndGetHashCode_FieldCoverage_
            // MatchesRegisteredExpectations 守护"hash 字段集 ≡ Equals 字段集对齐"契约，浅化触发红——
            // B2 禁改测试）。本形态：字段集零变化（守护契约不触）、新增 internal 缓存字段（明示许可）、
            // 深哈希 O(整图) 每对象仅计算一次（三站点字典每操作重哈希 → 对象级一次）。
            // 消费域 = 单线程编译器管线（RegisterSourceOutput 回调内；快照对象不跨 run 复用——
            // 增量缓存为值化序列化），int? 缓存读写无并发竞争面。
            if (_cachedHashCode is int cached) return cached;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (SourceType?.GetHashCode() ?? 0);
                hash = hash * 31 + (TargetType?.GetHashCode() ?? 0);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(SourceClassName);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(TargetClassName);
                hash = hash * 31 + SkipCodeGeneration.GetHashCode();
                hash = hash * 31 + ReportUnmappedTargetMembers.GetHashCode();
                hash = hash * 31 + ReportUnmappedSourceMembers.GetHashCode();
                hash = hash * 31 + HasClassLevelDiagnosticOptions.GetHashCode();
                foreach (var p in PropertyMappings) hash = hash * 31 + p.GetHashCode();
                foreach (var p in AllTargetProperties) hash = hash * 31 + p.GetHashCode();
                foreach (var p in SourceProperties) hash = hash * 31 + p.GetHashCode();
                foreach (var n in DeepCopyNodes) hash = hash * 31 + n.GetHashCode();
                foreach (var c in ComposeMappings) hash = hash * 31 + c.GetHashCode();
                foreach (var r in ReverseOnlyConvertBacks) hash = hash * 31 + r.GetHashCode();
                foreach (var d in Diagnostics) hash = hash * 31 + d.GetHashCode();
                hash = hash * 31 + (TargetIdentifierLocation?.GetHashCode() ?? 0);
                // ForInvocationLocation 参与哈希（与 Equals 字段集对齐）
                hash = hash * 31 + (ForInvocationLocation?.GetHashCode() ?? 0);
                // Fluent additive 两字段参与哈希（与 Equals 字段集对齐）
                hash = hash * 31 + NoAutoInclude.GetHashCode();
                hash = hash * 31 + (AfterMappingMethodRef != null ? StringComparer.Ordinal.GetHashCode(AfterMappingMethodRef) : 0);
                // 前置钩子引用参与哈希（与 Equals 字段集对齐）
                hash = hash * 31 + (BeforeMappingMethodRef != null ? StringComparer.Ordinal.GetHashCode(BeforeMappingMethodRef) : 0);
                // Fluent 树配置/属性级 NoAutoInclude 参与哈希（与 Equals 字段集对齐）
                hash = hash * 31 + (TreeConfig?.GetHashCode() ?? 0);
                foreach (var t in PropertyNoAutoIncludeTargets) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(t);
                hash = hash * 31 + HasComposeMappings.GetHashCode();
                // Fluent 正向方向标记参与哈希（与 Equals 字段集对齐）
                hash = hash * 31 + IsFluentForward.GetHashCode();
                // 多态子类型对参与哈希（与 Equals 字段集对齐）
                foreach (var sp in SubclassPairs) hash = hash * 31 + sp.GetHashCode();
                _cachedHashCode = hash;
                return hash;
            }
        }

        /// <summary>
        /// IsSelfRecursiveMapping 判定缓存（三调用点
        /// Core/Update/MapList 对同 mapping 重复全属性遍历，首算复用； 新增 internal
        /// 缓存字段明示许可；消费域 = 单线程编译器管线，快照对象不跨 run 复用）
        /// </summary>
        internal bool? CachedIsSelfRecursive;

        private int? _cachedHashCode;
    }
}
