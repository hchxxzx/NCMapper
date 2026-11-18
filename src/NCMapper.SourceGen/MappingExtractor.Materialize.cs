using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen
{
    /// <summary>
    /// 物化层— transform 出口一次性把符号模型
    /// 转换为纯值快照。物化发生在 transform 内部（符号可用，合法短暂持有），
    /// 出口之后管线缓存与下游只见值对象。
    /// </summary>
    internal static partial class MappingExtractor
    {
        /// <summary>
        /// 将符号版 MappingInfo 物化为纯值 MappingSnapshot（transform 出口）
        /// </summary>
        /// <param name="info">符号版映射信息</param>
        /// <returns>物化后的纯值映射快照</returns>
        public static MappingSnapshot Materialize(MappingInfo info)
        {
            return new MappingSnapshot
            {
                SourceType = TypeRefFromSymbol(info.SourceType),
                // 目标类型顶层物化携带 MapCtorParam 绑定表
                // 仅此调用点传入（嵌套/递归物化不传），绑定只对映射对目标类型本体构造器形状生效
                TargetType = TypeRefFromSymbol(info.TargetType, 0, info.CtorParamBindings),
                SourceClassName = info.SourceClassName,
                TargetClassName = info.TargetClassName,
                SkipCodeGeneration = info.SkipCodeGeneration,
                ReportUnmappedTargetMembers = info.DiagnosticOptions.ReportUnmappedTargetMembers,
                ReportUnmappedSourceMembers = info.DiagnosticOptions.ReportUnmappedSourceMembers,
                HasClassLevelDiagnosticOptions = DiagnosticOptionsResolver.HasClassLevelOptions(info.TargetType),
                PropertyMappings = info.PropertyMappings.Select(MaterializePropertyMapping).ToArray(),
                AllTargetProperties = info.AllTargetProperties.Select(p => PropertyRefFromSymbol(p)).ToArray(),
                SourceProperties = GetTypeAndBaseTypes(info.SourceType)
                    .SelectMany(t => t.GetMembers().OfType<IPropertySymbol>())
                    .Where(p => !p.IsStatic && !p.IsIndexer && p.GetMethod != null
                        && p.GetMethod.DeclaredAccessibility == Accessibility.Public)
                    .Select(p => PropertyRefFromSymbol(p))
                    .ToArray(),
                // DeepCopy 类型图闭包物化——本 mapping 的源/目标根的可达图节点
                //（EqualityKey 防环 + 物化期完整展开，输出期值域分类器零反查）
                // 闭包物化携带 MapCtorParam 绑定表
                // 目标类型节点（含根）的 PublicConstructors 同步填充 SourcePropertyName；TypeRef.Equals
                // 仅 EqualityKey 不含 Constructors，绑定变更检出经 DeepCopyNodeShape.Equals→
                // PublicConstructors.SequenceEqual→ConstructorParameterRef.Equals
                DeepCopyNodes = MaterializeDeepCopyClosure(info.SourceType, info.TargetType, info.CtorParamBindings),
                ComposeMappings = info.ComposeMappings.Select(MaterializeComposeMapping).ToArray(),
                // 反向无源转换器映射物化（条目复用 pm 物化路径：
                // Target=实体属性（reverseConverterTarget=null 不解析反向三元组——三元组由 pm 级
                // 三字段承载，发射端 GenerateSinglePropertyAssignment 三字段分支消费）、Source=null）
                ReverseOnlyConvertBacks = info.ReverseOnlyConvertBacks.Select(MaterializePropertyMapping).ToArray(),
                Diagnostics = info.Diagnostics.Select(MaterializeDiagnostic).ToArray(),
                TargetIdentifierLocation = info.TargetSyntax != null
                    ? SnapshotLocation(info.TargetSyntax.Identifier.GetLocation())
                    : null,
                // ForInvocationLocation 物化（NCMAP025 定位分通道
                // Fluent 侧指向 For invocation；[MapFrom] 通道恒默认 null——增量缓存字节不变前提）
                ForInvocationLocation = info.ForLocation != null
                    ? SnapshotLocationInternal(info.ForLocation)
                    : null,
                // Fluent additive 两字段物化（[MapFrom] 通道默认值
                // NoAutoInclude 默认 false、标注 [MapNoAutoMatch] 时 true（
                // 注释口径订正⑥，提取链第四类级读取点填充）/ AfterMappingMethodRef=null；
                // 未标注场景取默认值——字节不变保证前提）
                NoAutoInclude = info.NoAutoInclude,
                AfterMappingMethodRef = info.AfterMappingMethodRef,
                // 特性侧前置钩子物化（[MapFrom]+[BeforeMapping] 对填充；
                // 无声明对恒 null——与 AfterMappingMethodRef 同字段族，字节不变保证前提）
                BeforeMappingMethodRef = info.BeforeMappingMethodRef,
                // Fluent 树配置/属性级 NoAutoInclude 物化（[MapFrom] 通道
                // 恒默认 null/空——发射端条件行，字节不变保证前提）
                TreeConfig = info.TreeConfig is { } tc
                    ? new FluentTreeConfigTriple(tc.IdProperty, tc.ParentIdProperty, tc.ChildrenProperty)
                    : null,
                PropertyNoAutoIncludeTargets = info.PropertyNoAutoIncludeTargets.ToArray(),
                HasComposeMappings = info.HasComposeMappings,
                // （消费补齐）：Fluent 正向方向标记物化
                //（[MapFrom] 通道恒默认 false——IsToDtoMapping 回落 [MapFrom] 推导，字节不变）
                IsFluentForward = info.IsFluentForward,
                // 多态子类型对物化（值化 TypeRef 二元组——符号态
                // SubclassPairMapping 经 TypeRefFromSymbol 值化；[MapFrom] 通道恒默认空集合，
                // 发射端零分派产物——字节不变保证前提）
                SubclassPairs = info.SubclassPairs
                    .Select(sp => new SubclassPairSnapshot
                    {
                        SourceType = TypeRefFromSymbol(sp.SourceType),
                        TargetType = TypeRefFromSymbol(sp.TargetType),
                        // ClassName 与 MappingSnapshot.SourceClassName 同源（符号 Name）——
                        // 分派链方法名拼接口径一致
                        SourceClassName = sp.SourceType.Name,
                        TargetClassName = sp.TargetType.Name,
                    })
                    .ToArray(),
            };
        }

        /// <summary>
        /// 物化单对属性映射
        /// </summary>
        private static PropertyMappingSnapshot MaterializePropertyMapping(PropertyMapping pm)
        {
            return new PropertyMappingSnapshot
            {
                // 采集侧：Target 携带反向方法引用上下文（[ConvertBack] 三元组解析，
                // TTarget 基准=正向匹配的源属性；无源属性→null→不解析，反向写回本就跳过该场景）；
                // Fluent ConvertBack 三元组经 additive 可选参注入
                //（pm.BackConverter*——符号态 PropertyMapping 三字段；[MapFrom] 通道恒默认 null，
                // 行为与既有逐字节不变——字节不变硬门）
                // Fluent overrideIgnore/方向性忽略经 additive 可选参注入
                //（pm.FluentOverrideIgnore/FluentIgnoreDirection——四终端豁免门 + F-2；默认值时
                // 与既有逐字节不变）
                Target = PropertyRefFromSymbol(pm.TargetProperty, pm.SourceProperty,
                    fluentBackDeclaringType: pm.BackConverterDeclaringType,
                    fluentBackMethodName: pm.BackConverterMethodName,
                    fluentBackShape: pm.BackConverterShape,
                    fluentOverrideIgnore: pm.FluentOverrideIgnore,
                    fluentIgnoreDirection: pm.FluentIgnoreDirection,
                    // 显式声明标记透传（全局配置重匹配豁免）
                    fluentExplicit: pm.HasFluentExplicit,
                    // B 形态反向转换器方法体三字段透传（PropertyRef 承载）
                    fluentBackMethodBody: pm.BackConverterMethodBody,
                    fluentBackMethodParamTypes: pm.BackConverterMethodParamTypes,
                    fluentBackMethodParamNames: pm.BackConverterMethodParamNames),
                // /：实体侧（Source）属性标
                // [ConvertBack] 时以对侧 DTO 属性（TargetProperty）为换向基准物化反向三元组
                // （TField=DTO 属性类型、TSource=DTO 类型、TTarget=实体属性类型——与运行时
                // BuildBackConverterValue 的 source 参数即 DTO 对象语义一致）；直接匹配对与写回链
                // （多段路径）恒传（Count==1 时与原条件式等价）——写回链实体叶三元组由 ReversePlanner
                // 兜底分支消费（X2 补齐；运行时镜像：ExpressionBuilder.BuildReversePathWriteBackFromPath
                // sourceProp ?? leaf 叶兜底查询序）
                // T4 源腿退役：源侧 PropertyRef 不再采集 [MapIgnore] 特性/
                // IgnoreSource 方向注入（collectMapIgnore: false）——源类无锚形态为 019 Error
                // 拦截对象，输入不可再构造，按置顶"违规现状即施工面"出清（④ v1.2）
                Source = pm.SourceProperty != null
                    ? PropertyRefFromSymbol(pm.SourceProperty,
                        reverseConverterField: pm.TargetProperty,
                        collectMapIgnore: false)
                    : null,
                SourcePath = pm.SourcePath.Select(p => PropertyRefFromSymbol(p)).ToArray(),
                // 索引元数据物化传播（与 SourcePath 等长）
                SourceIndexes = pm.SourceIndexes.ToArray(),
                IsFlattened = pm.IsFlattened,
                PathOrigin = pm.PathOrigin,
                // 采集侧：方法引用三字段物化（符号采集期 ApplyConverterMethodReference 填充）
                ConverterDeclaringType = pm.ConverterDeclaringType,
                ConverterMethodName = pm.ConverterMethodName,
                ConverterShape = pm.ConverterShape,
                MaskType = pm.MaskType != null ? TypeRefFromSymbol(pm.MaskType) : null,
                // 格式化串物化（符号态 PropertyMapping.FormatString → 值态快照；
                // 发射端条件发射——null 不落产物行）
                FormatString = pm.FormatString,
                IsFromJson = pm.IsFromJson,
                // 条件守卫四字段物化（承载联动面——Fluent/特性双侧同字段，
                // 符号态 PropertyMapping → 值态 PropertyMappingSnapshot；SourceIgnoreDirection 同款通道）
                ConditionMethodRef = pm.ConditionMethodRef,
                ConditionMethodBody = pm.ConditionMethodBody,
                ConditionMethodParamName = pm.ConditionMethodParamName,
                ConditionShape = pm.ConditionShape,
                // B 形态转换器方法体三字段物化（生成三元组配套
                // 发射端 GenerateConverterMethods 消费；ReverseOnly 条目同经本方法物化自动覆盖）
                ConverterMethodBody = pm.ConverterMethodBody,
                ConverterMethodParamTypes = pm.ConverterMethodParamTypes,
                ConverterMethodParamNames = pm.ConverterMethodParamNames,
            };
        }

        /// <summary>
        /// 物化 Compose 映射
        /// </summary>
        private static ComposeMappingSnapshot MaterializeComposeMapping(ComposePropertyMapping cm)
        {
            return new ComposeMappingSnapshot
            {
                SubSource = TypeRefFromSymbol(cm.SubSourceType),
                Source = PropertyRefFromSymbol(cm.SourceProperty),
                SourcePath = cm.SourcePath.Select(p => PropertyRefFromSymbol(p)).ToArray(),
                // 索引元数据物化传播
                SourceIndexes = cm.SourceIndexes.ToArray(),
                Target = PropertyRefFromSymbol(cm.TargetProperty),
                // 采集侧：方法引用三字段物化
                ConverterDeclaringType = cm.ConverterDeclaringType,
                ConverterMethodName = cm.ConverterMethodName,
                ConverterShape = cm.ConverterShape,
                // 已校验镜像通道物化（与主源 pm 物化 同构）
                MaskType = cm.MaskType != null ? TypeRefFromSymbol(cm.MaskType) : null,
                // Compose 格式化串物化——物化管线铁律（:12-13"出口之后
                // 下游只见值对象"）：符号态 ComposePropertyMapping.FormatString 过出口即丢，
                // 漏物化 = Compose 场景格式化静默失效且无诊断
                FormatString = cm.FormatString,
                IsFromJson = cm.IsFromJson,
                // 块内 MapWhen 条件守卫四字段物化
                // （发射落点 = Update_{Sub}_To_{Target} 赋值层）
                ConditionMethodRef = cm.ConditionMethodRef,
                ConditionMethodBody = cm.ConditionMethodBody,
                ConditionMethodParamName = cm.ConditionMethodParamName,
                ConditionShape = cm.ConditionShape,
                // B 形态转换器方法体三字段物化（块内 Convert 内联转录）
                ConverterMethodBody = cm.ConverterMethodBody,
                ConverterMethodParamTypes = cm.ConverterMethodParamTypes,
                ConverterMethodParamNames = cm.ConverterMethodParamNames,
            };
        }

        /// <summary>
        /// 物化诊断（Descriptor → Id；Args 全部字符串化；Location → LocationSnapshot）
        /// <para>：internal 化供 Fluent 通道（FluentExtractor 出口值化）复用（逻辑零变化）</para>
        /// </summary>
        internal static DiagnosticSnapshot MaterializeDiagnostic(DiagnosticInfo diag)
        {
            return new DiagnosticSnapshot
            {
                DescriptorId = diag.Descriptor?.Id ?? "",
                Args = (diag.Args ?? Array.Empty<object>())
                    .Select(StringifyDiagnosticArg)
                    .ToArray(),
                Location = diag.Location != null ? SnapshotLocation(diag.Location) : null,
                IsExplicit = diag.IsExplicit,
            };
        }

        /// <summary>
        /// 诊断参数字符串化（符号 → ToDisplayString；其他 → InvariantCulture）
        /// </summary>
        private static string StringifyDiagnosticArg(object arg)
        {
            switch (arg)
            {
                case ISymbol symbol:
                    return symbol.ToDisplayString();
                case null:
                    return "";
                default:
                    return Convert.ToString(arg, CultureInfo.InvariantCulture) ?? "";
            }
        }

        /// <summary>Location → LocationSnapshot（无 SourceTree 返回 null；LineSpan 物化供 external 重建）。
        /// internal 入口（NCMAP019 输出值化用，S3）</summary>
        /// <param name="location">Roslyn 位置对象</param>
        /// <returns>物化的位置快照；无 SourceTree 时为 null</returns>
        internal static LocationSnapshot? SnapshotLocationInternal(Location location) => SnapshotLocation(location);

        /// <summary>
        /// Location → LocationSnapshot（无 SourceTree 返回 null；LineSpan 物化供 external 重建）
        /// </summary>
        private static LocationSnapshot? SnapshotLocation(Location location)
        {
            if (location?.SourceTree == null) return null;
            return new LocationSnapshot(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
        }

        // ================================================================
        // TypeRef 物化（物化字段矩阵）
        // ================================================================

        /// <summary>
        /// 完整 MetadataName（含嵌套链与泛型 arity：Outer`1+Inner`2；GetTypeByMetadataName 反查用）
        /// </summary>
        private static string GetMetadataNameWithNesting(INamedTypeSymbol type)
        {
            var sb = new System.Text.StringBuilder();
            var cur = type;
            while (cur != null)
            {
                if (sb.Length > 0) sb.Insert(0, '+');
                sb.Insert(0, cur.MetadataName);
                cur = cur.ContainingType;
            }
            return sb.ToString();
        }

        /// <summary>
        /// ITypeSymbol → TypeRef（递归物化；数组/可空/泛型实参/枚举成员/构造器）
        /// </summary>
        /// <param name="type">符号版类型</param>
        /// <returns>物化后的类型引用快照</returns>
        public static TypeRef TypeRefFromSymbol(ITypeSymbol type)
        {
            // 顶层入口 memo——盘点前置结论：
            // ① TypeRef 产物深不可变（init-only 全字段，无 mutable 状态）✓；
            // ② 键 = 原始 ITypeSymbol 实例（CWT 引用键控——同 compilation 同符号恒同实例；
            // annotated 与 plain 为不同实例，FullName 注解差异天然分键）；CWT 条目随符号
            // GC 回收不跨 compilation（AttrSymbolCache 同款形态）；
            // ③ **可缓存面收窄为 depth=0 顶层入口**——产物依赖 depth 截断点（同 symbol 不同
            // depth 调用产物不同：MaxTypeRefDepth 浅快照锚定于调用 depth），仅缓存 depth=0
            // 完整物化（截断点恒定 → 产物确定）；递归内层不缓存（避免截断点漂移改变
            // DeepCopyNodes 物化）；
            // ④ ctorParamBindings 非空形态不入缓存（绑定产物依赖绑定字典）。
            // 原 TryGetValue+Add 非原子——CWT.Add 对已存在键抛 ArgumentException，
            // 与 MappingExtractor.cs:335"ConcurrentDictionary 承载 transform 期可能的并行回调"
            // 同口径下并发首访可触发（CS8785）；GetValue 单次原子查填，工厂幂等（纯函数产物确定），
            // 与 AttrSymbolCache.DisplayName（attributenames.cs:113）同款形态。
            return _typeRefCache.GetValue(type, static t => TypeRefFromSymbol(t, 0));
        }

        /// <summary>
        /// 顶层入口缓存— netstandard2.0 CWT 无 AddOrUpdate/TryAdd；改用 GetValue 单次
        /// 原子查填（工厂幂等，并发首访结果一致，与 AttrSymbolCache 同款）；
        /// 自引用深度截断由 MaxTypeRefDepth 维持（depth=0 固定）
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ITypeSymbol, TypeRef> _typeRefCache
            = new System.Runtime.CompilerServices.ConditionalWeakTable<ITypeSymbol, TypeRef>();

        /// <summary>
        /// 递归物化最大深度（防 record 自引用/深泛型图栈溢出；超限返回浅快照）
        /// </summary>
        private const int MaxTypeRefDepth = 8;

        private static TypeRef TypeRefFromSymbol(ITypeSymbol type, int depth, IReadOnlyDictionary<string, string>? ctorParamBindings = null)
        {
            // 可空注解归一化：引用类型统一去顶层 `?`（与符号版 SymbolEqualityComparer.Default 忽略
            // Nullability 的相等语义一致—— 值版等性键必须与符号版同构，否则
            // HasMappingPath/IsSameType 等判定在 string vs string? 场景误判导致映射整条丢失）。
            // 值类型（int?）保留（Nullable<T> 包装分支物化，不影响）。
            var originalNullable = type.NullableAnnotation;
            // 发射显示名（含可空注解，零差异约束）
            var annotatedDisplay = type.ToDisplayString();
            if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
                type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

            var nullable = type.NullableAnnotation;

            // 深度超限 → 浅快照（仅标识字段，不递归展开构造器/泛型实参；DeepCopy 反查不受影响——
            // 反查仅需 MetadataName/Namespace）
            if (depth > MaxTypeRefDepth)
            {
                return new TypeRef
                {
                    FullName = annotatedDisplay,
                    NonNullableFullName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(),
                EqualityKey = type.ToDisplayString(),
                    NullableAnnotation = originalNullable,
                    MinimallyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    SimpleName = type.Name,
                    Namespace = type.ContainingNamespace != null && !type.ContainingNamespace.IsGlobalNamespace
                        ? type.ContainingNamespace.ToDisplayString()
                        : "",
                    MetadataName = (type as INamedTypeSymbol)?.MetadataName ?? type.Name,
                    Kind = type.TypeKind,
                    Accessibility = type.DeclaredAccessibility,
                    SpecialType = type.SpecialType,
                    IsValueType = type.IsValueType,
                    IsReferenceType = type.IsReferenceType,
                    IsGenericType = type is INamedTypeSymbol n2 && n2.IsGenericType,
                    IsArray = type is IArrayTypeSymbol,
                    TypeArguments = Array.Empty<TypeRef>(),
                };
            }

            // Nullable<T> 值类型包装：物化底层类型 + 标记
            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
            {
                var underlying = TypeRefFromSymbol(named.TypeArguments[0], depth + 1);
                return new TypeRef
                {
                    FullName = annotatedDisplay,
                    EqualityKey = type.ToDisplayString(),
                    NullableAnnotation = NullableAnnotation.NotAnnotated,
                    NonNullableFullName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(),
                    SimpleName = named.Name,
                    Namespace = named.ContainingNamespace?.ToDisplayString() ?? "",
                    MetadataName = named.MetadataName,
                    Kind = TypeKind.Struct,
                    Accessibility = named.DeclaredAccessibility,
                    SpecialType = named.SpecialType,
                    IsValueType = true,
                    IsReferenceType = false,
                    IsGenericType = true,
                    IsNullableValueType = true,
                    UnderlyingType = underlying,
                    IsArray = false,
                    ImplementsIEnumerable = false,
                    IsDictionary = false,
                    TypeArguments = new[] { underlying },
                    IsSealed = true,
                    IsAbstract = false,
                    IsDelegate = false,
                };
            }

            // 数组
            if (type is IArrayTypeSymbol array)
            {
                var element = TypeRefFromSymbol(array.ElementType, depth + 1);
                return new TypeRef
                {
                    FullName = annotatedDisplay,
                    EqualityKey = type.ToDisplayString(),
                    NonNullableFullName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(),
                    SimpleName = element.SimpleName + "[]",
                    Namespace = element.Namespace,
                    MetadataName = element.MetadataName + "[]",
                    Kind = TypeKind.Array,
                    Accessibility = Accessibility.Public,
                    SpecialType = SpecialType.None,
                    IsValueType = false,
                    IsReferenceType = true,
                    IsGenericType = false,
                    IsArray = true,
                    ElementType = element,
                    ImplementsIEnumerable = true,
                    IsDictionary = false,
                    EnumerableElementType = element,
                    TypeArguments = Array.Empty<TypeRef>(),
                    HasPublicParameterlessCtor = false,
                    IsSealed = true,
                };
            }

            // 指针/函数指针（NCMapper 场景极罕见，按最简形态物化，不递归）
            if (type is IPointerTypeSymbol pointer)
            {
                var element = TypeRefFromSymbol(pointer.PointedAtType, depth + 1);
                return new TypeRef
                {
                    FullName = annotatedDisplay,
                    EqualityKey = type.ToDisplayString(),
                    NonNullableFullName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(),
                    SimpleName = element.SimpleName + "*",
                    Namespace = element.Namespace,
                    MetadataName = element.MetadataName + "*",
                    Kind = TypeKind.Pointer,
                    Accessibility = Accessibility.Public,
                    SpecialType = SpecialType.None,
                    IsValueType = true,
                    IsReferenceType = false,
                    IsArray = false,
                    TypeArguments = Array.Empty<TypeRef>(),
                };
            }

            if (type is not INamedTypeSymbol namedType)
            {
                // 动态（object 别名）等兜底
                return new TypeRef
                {
                    FullName = annotatedDisplay,
                    NonNullableFullName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(),
                EqualityKey = type.ToDisplayString(),
                    NullableAnnotation = originalNullable,
                    MinimallyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    SimpleName = type.Name,
                    Namespace = type.ContainingNamespace?.ToDisplayString() ?? "",
                    MetadataName = type.Name,
                    Kind = type.TypeKind,
                    Accessibility = type.DeclaredAccessibility,
                    SpecialType = type.SpecialType,
                    IsValueType = type.IsValueType,
                    IsReferenceType = type.IsReferenceType,
                    TypeArguments = Array.Empty<TypeRef>(),
                };
            }

            // 泛型实参（递归物化）
            var typeArgs = namedType.TypeArguments
                .Select(t => TypeRefFromSymbol(t, depth + 1))
                .ToArray();

            // 枚举成员（仅枚举类型）
            IReadOnlyList<EnumMemberRef>? enumMembers = null;
            if (namedType.TypeKind == TypeKind.Enum)
            {
                enumMembers = namedType.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(f => f.ConstantValue != null)
                    .Select(f => new EnumMemberRef(
                        f.Name,
                        Convert.ToString(f.ConstantValue, CultureInfo.InvariantCulture) ?? f.Name))
                    .ToArray();
            }

            // 构造器形状（公共构造器；含参数名/类型/默认值）
            IReadOnlyList<ConstructorRef>? constructors = null;
            var publicCtors = namedType.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                .ToArray();
            if (publicCtors.Length > 0)
            {
                constructors = publicCtors
                    .Select(c => new ConstructorRef
                    {
                        IsPublic = true,
                        Parameters = c.Parameters.Select(p => new ConstructorParameterRef
                        {
                            Name = p.Name,
                            Type = TypeRefFromSymbol(p.Type, depth + 1),
                            HasExplicitDefaultValue = p.HasExplicitDefaultValue,
                            // 无默认值的参数访问 ExplicitDefaultValue 抛 InvalidOperationException（Roslyn 行为），
                            // 仅 HasExplicitDefaultValue 时读取（实测）
                            ExplicitDefaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue : null,
                            // MapCtorParam 显式绑定承载
                            // 仅目标类型顶层物化传入绑定表（ctorParamBindings 非空），按形参名（OrdinalIgnoreCase，
                            // 表构建即忽略大小写）匹配填充；ByRef/指针形参不可绑定跳过填充；
                            // 递归/嵌套物化不传表（默认 null）恒 null——未用 MapCtorParam 的对快照
                            // 与既有逐字段等价（B9 字节门前提）
                            SourcePropertyName = ctorParamBindings != null
                                && p.RefKind == RefKind.None
                                && p.Type.TypeKind != TypeKind.Pointer
                                && p.Type.TypeKind != TypeKind.FunctionPointer
                                && ctorParamBindings.TryGetValue(p.Name, out var boundSrcName) ? boundSrcName : null,
                        }).ToArray(),
                    })
                    .ToArray();
            }

            // 基类链全名（直接基类逐级到 object 终止）
            // compose 子源继承拓扑排序消费；接口不入链；深度护栏 64（继承链物理有限，防御值）；
            // object 自身（BaseType null）链为空。单键相等设计不参与 EqualityKey（X2 排除集同 commit 登记）。
            IReadOnlyList<string>? baseTypeChain = null;
            if (namedType.BaseType is INamedTypeSymbol baseSym)
            {
                var chain = new List<string>();
                var cur = baseSym;
                var guard = 0;
                while (cur != null && guard++ < 64)
                {
                    chain.Add(cur.ToDisplayString());
                    cur = cur.BaseType;
                }
                baseTypeChain = chain;
            }

            return new TypeRef
            {
                FullName = annotatedDisplay,
                    NonNullableFullName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(),
                EqualityKey = type.ToDisplayString(),
                NullableAnnotation = originalNullable,
                MinimallyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                AssemblyName = namedType.ContainingAssembly?.Name ?? "",
                SimpleName = namedType.Name,
                Namespace = (namedType.ContainingNamespace != null && !namedType.ContainingNamespace.IsGlobalNamespace)
                    ? namedType.ContainingNamespace.ToDisplayString()
                    : "",
                MetadataName = GetMetadataNameWithNesting(namedType),
                Kind = namedType.TypeKind,
                Accessibility = namedType.DeclaredAccessibility,
                SpecialType = namedType.SpecialType,
                IsValueType = namedType.IsValueType,
                IsReferenceType = namedType.IsReferenceType,
                IsGenericType = namedType.IsGenericType,
                IsArray = false,
                ImplementsIEnumerable = TypeHelpers.ImplementsIEnumerable(namedType),
                IsDictionary = TypeHelpers.IsDictionaryType(namedType),
                EnumerableElementType = TryGetEnumerableElementType(namedType, depth + 1),
                DictionaryKeyType = TryGetDictionaryKeyType(namedType, depth + 1),
                DictionaryValueType = TryGetDictionaryValueType(namedType, depth + 1),
                TypeArguments = typeArgs,
                EnumMembers = enumMembers,
                Constructors = constructors,
                HasPublicParameterlessCtor = namedType.InstanceConstructors
                    .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public),
                IsSealed = namedType.IsSealed,
                IsAbstract = namedType.IsAbstract,
                IsDelegate = namedType.TypeKind == TypeKind.Delegate,
                MapFromSources = namedType.GetAttributes()
                    .Where(a => AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapFrom)
                    .SelectMany(a => a.ConstructorArguments)
                    .Where(c => c.Value is INamedTypeSymbol)
                    .Select(c => TypeRefFromSymbol((INamedTypeSymbol)c.Value!, depth + 1))
                    .ToArray(),
                BaseTypeChainFullNames = baseTypeChain,
            };
        }

        /// <summary>
        /// 提取泛型 IEnumerable&lt;T&gt; 的元素类型（仅物化直连，不递归）
        /// </summary>
        private static TypeRef? TryGetEnumerableElementType(INamedTypeSymbol type, int depth)
        {
            var element = TypeHelpers.GetGenericEnumerableDataType(type);
            return element != null ? TypeRefFromSymbol(element, depth + 1) : null;
        }

        /// <summary>
        /// 提取字典 Key 类型
        /// </summary>
        private static TypeRef? TryGetDictionaryKeyType(INamedTypeSymbol type, int depth)
        {
            if (TypeHelpers.TryGetDictionaryKeyValueTypes(type, out var keyType, out _))
                return TypeRefFromSymbol(keyType, depth + 1);
            return null;
        }

        /// <summary>
        /// 提取字典 Value 类型
        /// </summary>
        private static TypeRef? TryGetDictionaryValueType(INamedTypeSymbol type, int depth)
        {
            if (TypeHelpers.TryGetDictionaryKeyValueTypes(type, out _, out var valueType))
                return TypeRefFromSymbol(valueType, depth + 1);
            return null;
        }

        // ================================================================
        // PropertyRef 物化（含特性参数）
        // ================================================================

        /// <summary>
        /// IPropertySymbol → PropertyRef（含特性快照）
        /// </summary>
        /// <param name="prop">符号版属性</param>
        /// <param name="reverseConverterTarget">采集侧：反向 [ConvertBack] 方法引用解析的
        /// TTarget 基准（=正向匹配的源属性）；null=无映射上下文（独立枚举/源属性物化），反向三元组不解析。
        /// 诊断不在物化期产出（符号采集期同基准已解析并登记，此处仅取物化结果）</param>
        /// <param name="reverseConverterField"> 实体侧匹配对兜底：prop 为实体属性（正向 pm 的
        /// SourceProperty）时传入对侧 DTO 属性——TField=该属性类型、TSource=其所属 DTO 类型、
        /// TTarget=prop.Type（实体属性类型，基准换向）；null=非实体侧兜底场景（DTO 侧经
        /// reverseConverterTarget 解析，独立枚举不解析）。诊断不在物化期产出（实体侧提取块单点产出）</param>
        /// <param name="fluentBackDeclaringType">：Fluent ConvertBack 声明的
        /// 反向方法引用 FQN（符号态 PropertyMapping.BackConverter* 物化传参；null = 无 Fluent 声明——
        /// additive 默认值，行为与既有逐字节不变）</param>
        /// <param name="fluentBackMethodName">Fluent ConvertBack 反向方法名（与 declaringType 成对）</param>
        /// <param name="fluentBackShape">Fluent ConvertBack 反向签名模板（含守卫变体）</param>
        /// <param name="fluentOverrideIgnore">：Fluent 四终端 overrideIgnore
        /// 豁免门注入（符号态 PropertyMapping.FluentOverrideIgnore 物化传参；false = additive 默认值，
        /// 行为与既有逐字节不变——HasOverrideIgnore 与特性采集面布尔叠加）</param>
        /// <param name="fluentIgnoreDirection">Fluent 方向性忽略
        /// 注入（符号态 PropertyMapping.FluentIgnoreDirection 物化传参；null = 无方向声明——
        /// additive 默认值，行为与既有逐字节不变；1/2 时 HasMapIgnore=true + MapIgnoreDirection 取值，
        /// 与 [MapIgnore(Direction)] 特性采集同口径）</param>
        /// <param name="fluentExplicit">：Fluent 显式声明标记注入
        /// （符号态 PropertyMapping.HasFluentExplicit 物化传参；false = additive 默认值——
        /// 特性通道逐字节不变；置位时全局配置替换性重匹配豁免）</param>
        /// <returns>物化后的属性引用快照</returns>
        public static PropertyRef PropertyRefFromSymbol(IPropertySymbol prop, IPropertySymbol? reverseConverterTarget = null,
            IPropertySymbol? reverseConverterField = null,
            string? fluentBackDeclaringType = null,
            string? fluentBackMethodName = null,
            ConverterShape fluentBackShape = ConverterShape.None,
            bool fluentOverrideIgnore = false,
            int? fluentIgnoreDirection = null,
            bool collectMapIgnore = true,
            bool fluentExplicit = false,
            string? fluentBackMethodBody = null,
            string[]? fluentBackMethodParamTypes = null,
            string[]? fluentBackMethodParamNames = null)
        {
            var attributes = prop.GetAttributes();
            // T4 源腿退役：collectMapIgnore=false 时源侧不再采集 [MapIgnore]
            //（目标腿 [MapIgnore] 特性采集 + .Ignore 方向注入不受影响——A-11 承载侧合法形态）
            var mapIgnore = collectMapIgnore
                ? attributes.FirstOrDefault(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapIgnore)
                : null;

            // MapIgnore.Direction 底层值（0=Both,1=ToDto,2=ToEntity；默认 0）
            int mapIgnoreDirection = 0;
            if (mapIgnore != null)
            {
                if (mapIgnore.ConstructorArguments.Length > 0 && mapIgnore.ConstructorArguments[0].Value is int ctorVal)
                    mapIgnoreDirection = ctorVal;
                foreach (var arg in mapIgnore.NamedArguments)
                {
                    if (arg.Key == "Direction" && arg.Value.Value is int namedVal)
                        mapIgnoreDirection = namedVal;
                }
            }

            // [ConvertBack] 纳入 OverrideIgnore 采集面
            // （与 [Convert] 对称的反向豁免门；命名参数名检测统一）
            // （接入面 f）：[MapFormat] 纳入 OverrideIgnore 采集面
            // 生成路径豁免门标志收不到（与运行时 e 构成两侧同一义务）
            var hasOverrideIgnore = attributes.Any(a =>
                (AttrSymbolCache.DisplayName(a.AttributeClass) is
                    AttributeNames.MapProperty or AttributeNames.Convert
                    or AttributeNames.Mask or AttributeNames.ConvertBack
                    or AttributeNames.MapFormat)
                && a.NamedArguments.Any(kv => kv.Key == "OverrideIgnore" && kv.Value.Value is true));

            // [Convert] 方法引用形态（正向三元组经 pm 级物化承载，采集期此处仅识别标注存在性）
            var convertAttr = attributes.FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Convert);

            // [ConvertBack] 反向转换器（C-1c；采集模式与 [Convert] 同构）
            string? converterBackDeclaringType = null;
            string? converterBackMethodName = null;
            var converterBackShape = ConverterShape.None;
            var backConvAttr = attributes.FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.ConvertBack);
            if (backConvAttr != null && backConvAttr.ConstructorArguments.Length >= 1)
            {
                // 反向方法引用形态三元组（基准按标注位置换向——反向转换器两实参恒来自 DTO 侧）：
                // DTO 侧（规范位置）：TField=prop.Type、TSource=prop.ContainingType（DTO 类型）、
                //   TTarget=reverseConverterTarget.Type（实体属性类型），与符号采集期校验基准一致；
                // 实体侧（匹配对兜底）：TField=reverseConverterField.Type（DTO 属性类型）、
                //   TSource=reverseConverterField.ContainingType（DTO 类型）、TTarget=prop.Type（实体属性类型）
                // 诊断丢弃——采集期已产出（DTO 侧提取块 / 实体侧 提取块，单点产出防重复发射）
                if (TryGetMethodReference(backConvAttr, prop, out var backMrAnchor, out var backMrMethod)
                    && (reverseConverterTarget != null || reverseConverterField != null))
                {
                    var tField = reverseConverterField != null ? reverseConverterField.Type : prop.Type;
                    var tSource = reverseConverterField != null ? reverseConverterField.ContainingType! : prop.ContainingType!;
                    var tTarget = reverseConverterField != null ? prop.Type : reverseConverterTarget!.Type;
                    if (ResolveConverterMethod(backMrAnchor, backMrMethod, tField, tSource,
                        tTarget, prop.Name, null, new List<DiagnosticInfo>(),
                        out var backMethod, out var backShape))
                    {
                        converterBackDeclaringType = backMethod!.ContainingType.ToDisplayString();
                        converterBackMethodName = backMethod.Name;
                        converterBackShape = backShape;
                    }
                }
            }

            // Fluent ConvertBack 注入（additive——默认值时行为逐字节不变）：
            // 特性派生三元组为空 && Fluent 声明非空 → 采用 Fluent 值（提取期 ResolveConvertBackMethod
            // 已完成解析与 F18/F19/F21 校验——物化期零重复解析；同 (S,T) 对双通道声明被 F11 仲裁
            // 抑制，双源并存不可达，特性优先为纯防御序）
            // B 形态方法体三字段随三元组同门注入（生成类型限定名 +
            // 生成方法名 + 转录体——ReversePlanner 复制到反向 pm 的 ConverterMethod* 消费）
            string? converterBackMethodBody = null;
            string[]? converterBackMethodParamTypes = null;
            string[]? converterBackMethodParamNames = null;
            if (fluentBackDeclaringType != null && converterBackDeclaringType == null)
            {
                converterBackDeclaringType = fluentBackDeclaringType;
                converterBackMethodName = fluentBackMethodName;
                converterBackShape = fluentBackShape;
                converterBackMethodBody = fluentBackMethodBody;
                converterBackMethodParamTypes = fluentBackMethodParamTypes;
                converterBackMethodParamNames = fluentBackMethodParamNames;
            }

            // [Mask(typeof(X))] 脱敏类型
            TypeRef? maskType = null;
            var maskAttr = attributes.FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.Mask);
            if (maskAttr != null && maskAttr.ConstructorArguments.Length >= 1
                && maskAttr.ConstructorArguments[0].Value is INamedTypeSymbol maskSym)
            {
                maskType = TypeRefFromSymbol(maskSym);
            }

            // [MapProperty] FromField/FromSource 参数（补匹配用）
            string? fromField = null;
            TypeRef? fromSource = null;
            var mapPropAttr = attributes.FirstOrDefault(a =>
                AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapProperty);
            if (mapPropAttr != null)
            {
                if (mapPropAttr.ConstructorArguments.Length >= 2)
                {
                    if (mapPropAttr.ConstructorArguments[0].Value is INamedTypeSymbol fromSrc)
                        fromSource = TypeRefFromSymbol(fromSrc);
                    if (mapPropAttr.ConstructorArguments[1].Value is string fromFld)
                        fromField = fromFld;
                }
            }

            return new PropertyRef
            {
                Name = prop.Name,
                Type = TypeRefFromSymbol(prop.Type),
                IsStatic = prop.IsStatic,
                IsIndexer = prop.IsIndexer,
                DeclaredAccessibility = prop.DeclaredAccessibility,
                GetMethodAccessibility = prop.GetMethod?.DeclaredAccessibility ?? Accessibility.NotApplicable,
                SetMethodAccessibility = prop.SetMethod?.DeclaredAccessibility ?? Accessibility.NotApplicable,
                IsInitOnly = prop.SetMethod?.IsInitOnly == true,
                IsImplicitlyDeclared = prop.IsImplicitlyDeclared,
                ContainingType = prop.ContainingType != null ? TypeRefFromSymbol(prop.ContainingType) : null,
                Location = prop.Locations.FirstOrDefault() is { } loc ? SnapshotLocation(loc) : null,
                // （搭车）：Fluent 方向性忽略叠加（additive——默认 null 时
                // 与特性采集逐字节不变；1/2 时镜像 [MapIgnore(Direction)] 值化，正反向方向过滤自动消费）
                HasMapIgnore = mapIgnore != null || fluentIgnoreDirection != null,
                MapIgnoreDirection = fluentIgnoreDirection ?? mapIgnoreDirection,
                HasMapProperty = mapPropAttr != null,
                IsFromJson = mapPropAttr != null && mapPropAttr.NamedArguments.Any(kv => kv.Key == "IsFromJson" && kv.Value.Value is true),
                // Fluent 四终端 overrideIgnore 豁免门叠加（additive——默认 false
                // 时与特性采集逐字节不变；正反向双链全局忽略过滤豁免消费）
                HasOverrideIgnore = hasOverrideIgnore || fluentOverrideIgnore,
                HasConvertUsing = convertAttr != null,
                // Fluent 声明注入亦置位（ReversePlanner/IsExplicitlyMapped/HasExplicitAttributeValue
                // 前向消费点零改动读取——additive 行为变化与特性侧语义一致）
                HasConvertBackUsing = backConvAttr != null || fluentBackDeclaringType != null,
                // 采集侧：反向方法引用三元组（ReversePlanner 反向快照构造时携带）
                ConverterBackDeclaringType = converterBackDeclaringType,
                ConverterBackMethodName = converterBackMethodName,
                ConverterBackShape = converterBackShape,
                // B 形态方法体三字段（A 形态/特性通道恒 null——additive）
                ConverterBackMethodBody = converterBackMethodBody,
                ConverterBackMethodParamTypes = converterBackMethodParamTypes,
                ConverterBackMethodParamNames = converterBackMethodParamNames,
                HasMask = maskAttr != null,
                // （接入面 g）：[MapFormat] 存在性标志（NCMAP021 触发载体）
                HasMapFormat = attributes.Any(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapFormat),
                MaskType = maskType,
                MapPropertyFromField = fromField,
                MapPropertyFromSource = fromSource,
                HasMapNoAutoInclude = attributes.Any(a =>
                    AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapNoAutoInclude),
                // Fluent 显式声明标记（additive——默认 false 与既有逐字节不变）
                HasFluentExplicit = fluentExplicit,
            };
        }

        // ================================================================
        // 值版反向映射（输出期对快照翻转，零符号依赖）
        // CreateReverseMapping 已逐行迁至 ReversePlanner
        // （C1 前移——消除 CodeGenerator 输出期对 transform 期类的依赖）
        // ================================================================

        /// <summary>
        /// 值版 [MapIgnore] 方向判定（与符号版 ShouldIgnoreInDirection 同源）
        /// </summary>
        /// <param name="prop">属性引用快照</param>
        /// <param name="toDto">是否 DTO 方向</param>
        /// <returns>命中忽略方向时为 true</returns>
        internal static bool ShouldIgnoreInDirectionValue(PropertyRef prop, bool toDto)
        {
            if (!prop.HasMapIgnore) return false;
            return prop.MapIgnoreDirection switch
            {
                0 => true,          // Both
                1 => toDto,         // ToDto
                2 => !toDto,        // ToEntity
                _ => true           // 未知值默认跳过
            };
        }

        // ================================================================
        // DeepCopy 节点闭包物化
        // ================================================================

        /// <summary>
        /// 物化 DeepCopy 类型图闭包（源/目标根的并集节点；进行中集合防环）
        /// <para>：可选携带 MapCtorParam 绑定表（bindingTarget=目标类型）
        /// 图内等于目标类型的节点（含目标根）PublicConstructors 填充 SourcePropertyName，
        /// 与 TypeRefFromSymbol 顶层物化双点同步。</para>
        /// </summary>
        private static IReadOnlyList<DeepCopyNodeShape> MaterializeDeepCopyClosure(
            INamedTypeSymbol source, INamedTypeSymbol target,
            IReadOnlyDictionary<string, string>? ctorParamBindings = null)
        {
            var nodes = new List<DeepCopyNodeShape>();
            var inProgress = new HashSet<string>(StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);
            BuildDeepCopyNodeRecursive(source, nodes, inProgress, done, target, ctorParamBindings);
            BuildDeepCopyNodeRecursive(target, nodes, inProgress, done, target, ctorParamBindings);
            return nodes;
        }

        /// <summary>
        /// 递归物化单个 DeepCopy 节点（EqualityKey 防环：进行中/已完成均跳过；符号驱动递归）
        /// </summary>
        private static void BuildDeepCopyNodeRecursive(
            ITypeSymbol type, List<DeepCopyNodeShape> nodes, HashSet<string> inProgress, HashSet<string> done,
            INamedTypeSymbol? bindingTarget = null, IReadOnlyDictionary<string, string>? ctorParamBindings = null)
        {
            // 入口解包（镜像 CollectEdgeTargets：数组 → 元素、Nullable<T> → T；指针/类型参数不可命名 → 无边）
            if (type is IArrayTypeSymbol arrType)
            {
                BuildDeepCopyNodeRecursive(arrType.ElementType, nodes, inProgress, done, bindingTarget, ctorParamBindings);
                return;
            }
            if (TypeHelpers.IsNullableValueType(type))
            {
                BuildDeepCopyNodeRecursive(TypeHelpers.GetUnderlyingType(type), nodes, inProgress, done, bindingTarget, ctorParamBindings);
                return;
            }
            if (type is not INamedTypeSymbol named)
                return;

            var typeRef = TypeRefFromSymbol(named);
            var key = typeRef.EqualityKey;
            if (inProgress.Contains(key) || done.Contains(key)) return;

            inProgress.Add(key);

            // 叶子/容器判定输入（镜像 DeepCopyGraphRules）
            var isAnyLeafBaseType = false;
            for (var t = named; t != null; t = t.BaseType)
            {
                var ns = t.ContainingNamespace?.ToDisplayString();
                if (NCMapper.Shared.DeepCopyGraphRules.IsLeafBaseType(ns ?? "", t.Name))
                {
                    isAnyLeafBaseType = true;
                    break;
                }
            }
            var isLeaf = NCMapper.Shared.DeepCopyGraphRules.IsLeafType(
                isValueType: named.IsValueType,
                isString: named.SpecialType == SpecialType.System_String,
                isDelegate: named.TypeKind == TypeKind.Delegate,
                isAnyLeafBaseType: isAnyLeafBaseType);
            var isCollectionLike = NCMapper.Shared.DeepCopyGraphRules.IsCollectionLike(
                isArray: named.TypeKind == TypeKind.Array,
                implementsIEnumerable: TypeHelpers.ImplementsIEnumerable(named),
                isString: named.SpecialType == SpecialType.System_String);

            // 属性图（含继承链，最派生去重；与符号版 GetPublicInstanceProperties 同口径）；
            // 非叶子非容器时递归展开属性类型（与分类器可达集一致）
            var props = new List<DeepCopyPropertyShape>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            for (var current = named; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is not IPropertySymbol prop) continue;
                    if (prop.IsStatic) continue;
                    if (prop.Parameters.Length != 0) continue;   // 索引器不计边
                    var getter = prop.GetMethod;
                    if (getter == null || getter.DeclaredAccessibility != Accessibility.Public) continue;
                    if (!seenNames.Add(prop.Name)) continue;
                    props.Add(new DeepCopyPropertyShape
                    {
                        Name = prop.Name,
                        Type = TypeRefFromSymbol(prop.Type),
                        GetterIsPublic = true,
                        GetterIsVirtual = getter.IsVirtual,
                        GetterIsSealed = getter.IsSealed,
                        ContainingTypeIsSealed = current.IsSealed,
                        HasSetter = prop.SetMethod != null,
                        SetterIsPublic = prop.SetMethod?.DeclaredAccessibility == Accessibility.Public,
                        SetterIsInitOnly = prop.SetMethod?.IsInitOnly == true,
                        HasMapIgnore = prop.GetAttributes().Any(a =>
                            AttrSymbolCache.DisplayName(a.AttributeClass) == AttributeNames.MapIgnore),
                        Location = prop.Locations.FirstOrDefault() is { } loc ? SnapshotLocation(loc) : null,
                    });
                    if (!isLeaf && !isCollectionLike)
                        BuildDeepCopyNodeRecursive(prop.Type, nodes, inProgress, done, bindingTarget, ctorParamBindings);
                }
            }

            // 容器边（镜像 CollectContainerEdges 输入；符号保留供递归）
            TypeRef? keyType = null;
            TypeRef? valueType = null;
            TypeRef? elementType = null;
            INamedTypeSymbol? keySym = null;
            INamedTypeSymbol? valueSym = null;
            INamedTypeSymbol? elemSym = null;
            var isDictionary = TypeHelpers.IsDictionaryType(named);
            if (isDictionary && TypeHelpers.TryGetDictionaryKeyValueTypes(named, out var kt, out var vt))
            {
                keyType = TypeRefFromSymbol(kt);
                valueType = TypeRefFromSymbol(vt);
                keySym = kt as INamedTypeSymbol;
                valueSym = vt as INamedTypeSymbol;
            }
            if (!isDictionary)
            {
                var elem = TypeHelpers.GetGenericEnumerableDataType(named);
                if (elem is INamedTypeSymbol elemNamed)
                {
                    elementType = TypeRefFromSymbol(elemNamed);
                    elemSym = elemNamed;
                }
            }

            // 边目标形态（镜像 CollectEdgeTargets 解包输入）
            var isNullableValueType = TypeHelpers.IsNullableValueType(named);
            var isArray = named.TypeKind == TypeKind.Array;
            var isNonGenericEnumerable = !isDictionary && elementType == null
                && TypeHelpers.ImplementsIEnumerable(named)
                && named.TypeKind != TypeKind.Array;

            nodes.Add(new DeepCopyNodeShape
            {
                Type = typeRef,
                Location = named.Locations.FirstOrDefault() is { } tloc ? SnapshotLocation(tloc) : null,
                HasPublicParameterlessCtor = named.InstanceConstructors.Any(c =>
                    c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public),
                PublicConstructors = named.InstanceConstructors
                    .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                    .OrderByDescending(c => c.Parameters.Length)
                    .Select(c => new ConstructorRef
                    {
                        IsPublic = true,
                        Parameters = c.Parameters.Select(p => new ConstructorParameterRef
                        {
                            Name = p.Name,
                            Type = TypeRefFromSymbol(p.Type),
                            HasExplicitDefaultValue = p.HasExplicitDefaultValue,
                            ExplicitDefaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue : null,
                            // MapCtorParam 绑定承载（双点之二）
                            // 仅节点类型 == 映射对目标类型时按绑定表填充（ByRef/指针跳过）；
                            // 深拷贝发射不消费本字段（CodeGenerator.DeepCopy 仅识别拷贝构造器形状），
                            // 产物零变化；参与 DeepCopyNodeShape.Equals 等价性（绑定变更检出防线）
                            SourcePropertyName = bindingTarget != null && ctorParamBindings != null
                                && SymbolEqualityComparer.Default.Equals(named, bindingTarget)
                                && p.RefKind == RefKind.None
                                && p.Type.TypeKind != TypeKind.Pointer
                                && p.Type.TypeKind != TypeKind.FunctionPointer
                                && ctorParamBindings.TryGetValue(p.Name, out var boundSrcName) ? boundSrcName : null,
                        }).ToArray(),
                    })
                    .ToArray(),
                Properties = props,
                IsLeaf = isLeaf,
                IsCollectionLike = isCollectionLike,
                IsDictionary = isDictionary,
                KeyType = keyType,
                ValueType = valueType,
                ElementType = elementType,
                IsNonGenericEnumerable = isNonGenericEnumerable,
                IsArray = isArray,
                ArrayElementType = isArray && named is IArrayTypeSymbol arr ? TypeRefFromSymbol(arr.ElementType) : null,
                IsNullableValueType = isNullableValueType,
                UnderlyingType = isNullableValueType && named is INamedTypeSymbol namedN && namedN.TypeArguments.Length == 1
                    ? TypeRefFromSymbol(namedN.TypeArguments[0])
                    : null,
                IsInterface = named.TypeKind == TypeKind.Interface,
                IsTypeParameter = named.TypeKind == TypeKind.TypeParameter,
                IsPointer = named.TypeKind == TypeKind.Pointer,
                IsValueType = named.IsValueType,
                IsString = named.SpecialType == SpecialType.System_String,
                IsDelegate = named.TypeKind == TypeKind.Delegate,
            });

            done.Add(key);
            inProgress.Remove(key);

            // 容器边递归展开（分类器按元素/K-V 建边）
            // 三边递归转发 bindingTarget/ctorParamBindings
            //（对齐属性边 先例——容器元素=目标类型自引用场景，元素节点 ctor 绑定经
            // 容器边透传；缺陷态 = 不转发 → 元素节点绑定丢失，DeepCopy ctor 实参缺源）
            if (elemSym != null)
                BuildDeepCopyNodeRecursive(elemSym, nodes, inProgress, done, bindingTarget, ctorParamBindings);
            if (keySym != null)
                BuildDeepCopyNodeRecursive(keySym, nodes, inProgress, done, bindingTarget, ctorParamBindings);
            if (valueSym != null)
                BuildDeepCopyNodeRecursive(valueSym, nodes, inProgress, done, bindingTarget, ctorParamBindings);
        }
    }
}
