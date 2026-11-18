using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>
/// 解析期单条 ConvertBack 反向声明（符号态， 模型）。
/// <para><see cref="EntityProperty"/>为提取期 FindProperty 产物（物化期零重复解析），
/// 与 MaskType 符号态持有同生命周期先例（MappingSnapshotBuilder.cs:152-156）。</para>
/// <para>IEquatable 值化投影（定案提取层——编制会话建议③：沿 PropertyMapping 既有
/// SymbolEqualityComparer 符号比较模式）：Method → "{FQN}.{Name}" 字符串 + Shape 枚举 +
/// EntityProperty 符号比较；Location 不参与（值层剥离惯例，与 DiagnosticSnapshot Location
/// 剥离同口径）。</para>
/// </summary>
internal sealed class FluentBackDeclaration : IEquatable<FluentBackDeclaration>
{
    /// <summary>转换器方法符号（A 形态方法组解析产物——符号态，物化后丢弃；B 形态（内联转录）null）</summary>
    public IMethodSymbol? Method { get; }

    /// <summary>4+2 模板匹配形态（含 Nullable 守卫变体编码）</summary>
    public ConverterShape Shape { get; }

    /// <summary>声明位置（诊断定位）</summary>
    public Microsoft.CodeAnalysis.Location Location { get; }

    /// <summary>实体侧反向写回点属性（提取期 FindProperty 产物；解析失败即弃声明——进入物化的
    /// 声明该项已解析，但类型可空，物化侧 BuildReverseOnlyEntry null 防御门保留——
    /// ③：原"恒非 null"不变量表述与防御口径矛盾，按实态订正）</summary>
    public IPropertySymbol? EntityProperty { get; }

    /// <summary>： 豁免门——overrideIgnore 实参（物化期置位 HasOverrideIgnore）</summary>
    public bool OverrideIgnore { get; }

    /// <summary>：B 形态转录体（符号级限定名重写后的表达式体；A 形态 null）</summary>
    public string? TranscribedBody { get; }

    /// <summary>：B 形态形参类型组（lambda 绑定后形参类型显示串；A 形态 null）</summary>
    public string[]? TranscribedParamTypes { get; }

    /// <summary>：B 形态形参名组（A 形态 null）</summary>
    public string[]? TranscribedParamNames { get; }

    /// <summary>A 形态构造（方法组——既有路径零变化）</summary>
    public FluentBackDeclaration(
        IMethodSymbol method,
        ConverterShape shape,
        Microsoft.CodeAnalysis.Location location,
        IPropertySymbol? entityProperty,
        bool overrideIgnore = false)
    {
        Method = method;
        Shape = shape;
        Location = location;
        EntityProperty = entityProperty;
        OverrideIgnore = overrideIgnore;
    }

    /// <summary>
    /// B 形态构造（收窄：无捕获表达式 lambda 经 InlineTranscriber 转录，
    /// Method 置 null；物化期计算生成方法名注入 pm.BackConverter* 三元组 + 方法体承载字段）。
    /// </summary>
    public FluentBackDeclaration(
        ConverterShape shape,
        Microsoft.CodeAnalysis.Location location,
        IPropertySymbol? entityProperty,
        string transcribedBody,
        string[] transcribedParamTypes,
        string[] transcribedParamNames,
        bool overrideIgnore = false)
    {
        Method = null;
        Shape = shape;
        Location = location;
        EntityProperty = entityProperty;
        OverrideIgnore = overrideIgnore;
        TranscribedBody = transcribedBody;
        TranscribedParamTypes = transcribedParamTypes;
        TranscribedParamNames = transcribedParamNames;
    }

    /// <summary>方法引用值化投影 "{ContainingType.ToDisplayString()}.{Name}"（A 形态；B 形态 null）</summary>
    internal string? MethodRef => Method != null ? Method.ContainingType.ToDisplayString() + "." + Method.Name : null;

    /// <inheritdoc />
    public bool Equals(FluentBackDeclaration? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(MethodRef, other.MethodRef, StringComparison.Ordinal)
               && Shape == other.Shape
               && SymbolEqualityComparer.Default.Equals(EntityProperty, other.EntityProperty)
               && OverrideIgnore == other.OverrideIgnore
               && string.Equals(TranscribedBody, other.TranscribedBody, StringComparison.Ordinal)
               && (TranscribedParamTypes ?? Array.Empty<string>()).SequenceEqual(
                   other.TranscribedParamTypes ?? Array.Empty<string>(), StringComparer.Ordinal)
               && (TranscribedParamNames ?? Array.Empty<string>()).SequenceEqual(
                   other.TranscribedParamNames ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FluentBackDeclaration);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (MethodRef != null ? StringComparer.Ordinal.GetHashCode(MethodRef) : 0);
            hash = hash * 31 + Shape.GetHashCode();
            hash = hash * 31 + (EntityProperty != null ? SymbolEqualityComparer.Default.GetHashCode(EntityProperty) : 0);
            hash = hash * 31 + OverrideIgnore.GetHashCode();
            hash = hash * 31 + (TranscribedBody != null ? StringComparer.Ordinal.GetHashCode(TranscribedBody) : 0);
            foreach (var t in TranscribedParamTypes ?? Array.Empty<string>()) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(t);
            foreach (var n in TranscribedParamNames ?? Array.Empty<string>()) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(n);
            return hash;
        }
    }
}
/// <summary>
/// 解析期单条 Convert 正向声明（符号态， 模型；
/// 随终端方法更名 ConvertUsing→Convert）。
/// <para><see cref="SourceProperty"/>/<see cref="SourcePath"/> 为提取期解析产物（步骤 4：同名/扁平化/
/// 反向查找——B-1 提取期单点定案），物化期附着与 ForwardOnly 兜底消费（零重复解析），
/// 与 MaskType 符号态持有同生命周期先例（MappingSnapshotBuilder.cs）。</para>
/// <para>IEquatable 值化投影（定案提取层——沿 PropertyMapping 既有 SymbolEqualityComparer
/// 符号比较模式）：Method → "{FQN}.{Name}" 字符串 + Shape 枚举 + SourceProperty 符号比较 +
/// SourcePath 符号序列 + OverrideIgnore 值；Location 不参与（值层剥离惯例）。</para>
/// </summary>
internal sealed class FluentForwardDeclaration : IEquatable<FluentForwardDeclaration>
{
    /// <summary>转换器方法符号（A 形态方法组解析产物——符号态，物化后丢弃；B 形态（内联转录）null）</summary>
    public IMethodSymbol? Method { get; }

    /// <summary>4+2 模板匹配形态（含 Nullable 守卫变体编码）</summary>
    public ConverterShape Shape { get; }

    /// <summary>声明位置（诊断定位）</summary>
    public Microsoft.CodeAnalysis.Location Location { get; }

    /// <summary>源侧属性（提取期解析产物；不变量：进入物化的声明恒非 null——F24 门保证）</summary>
    public IPropertySymbol SourceProperty { get; }

    /// <summary>源侧访问路径（同名单段 / 扁平化多段 / 反向查找单段——物化期 ForwardOnly 建 pm 用）</summary>
    public List<IPropertySymbol> SourcePath { get; }

    /// <summary>Q2 豁免门——overrideIgnore 实参（物化期置位 Target.HasOverrideIgnore）</summary>
    public bool OverrideIgnore { get; }

    /// <summary>：B 形态转录体（符号级限定名重写后的表达式体；A 形态 null）</summary>
    public string? TranscribedBody { get; }

    /// <summary>：B 形态形参类型组（lambda 绑定后形参类型显示串；A 形态 null）</summary>
    public string[]? TranscribedParamTypes { get; }

    /// <summary>：B 形态形参名组（A 形态 null）</summary>
    public string[]? TranscribedParamNames { get; }

    /// <summary>A 形态构造（方法组——既有路径零变化）</summary>
    public FluentForwardDeclaration(
        IMethodSymbol method,
        ConverterShape shape,
        Microsoft.CodeAnalysis.Location location,
        IPropertySymbol sourceProperty,
        List<IPropertySymbol> sourcePath,
        bool overrideIgnore)
    {
        Method = method;
        Shape = shape;
        Location = location;
        SourceProperty = sourceProperty;
        SourcePath = sourcePath;
        OverrideIgnore = overrideIgnore;
    }

    /// <summary>
    /// B 形态构造（收窄：无捕获表达式 lambda 经 InlineTranscriber 转录，
    /// Method 置 null；物化期计算生成方法名注入 pm.Converter* 三元组 + 方法体承载字段）。
    /// </summary>
    public FluentForwardDeclaration(
        ConverterShape shape,
        Microsoft.CodeAnalysis.Location location,
        IPropertySymbol sourceProperty,
        List<IPropertySymbol> sourcePath,
        string transcribedBody,
        string[] transcribedParamTypes,
        string[] transcribedParamNames,
        bool overrideIgnore)
    {
        Method = null;
        Shape = shape;
        Location = location;
        SourceProperty = sourceProperty;
        SourcePath = sourcePath;
        OverrideIgnore = overrideIgnore;
        TranscribedBody = transcribedBody;
        TranscribedParamTypes = transcribedParamTypes;
        TranscribedParamNames = transcribedParamNames;
    }

    /// <summary>方法引用值化投影 "{ContainingType.ToDisplayString()}.{Name}"（A 形态；B 形态 null）</summary>
    internal string? MethodRef => Method != null ? Method.ContainingType.ToDisplayString() + "." + Method.Name : null;

    /// <inheritdoc />
    public bool Equals(FluentForwardDeclaration? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(MethodRef, other.MethodRef, StringComparison.Ordinal)
               && Shape == other.Shape
               && SymbolEqualityComparer.Default.Equals(SourceProperty, other.SourceProperty)
               && SourcePath.SequenceEqual(other.SourcePath, SymbolEqualityComparer.Default)
               && OverrideIgnore == other.OverrideIgnore
               && string.Equals(TranscribedBody, other.TranscribedBody, StringComparison.Ordinal)
               && (TranscribedParamTypes ?? Array.Empty<string>()).SequenceEqual(
                   other.TranscribedParamTypes ?? Array.Empty<string>(), StringComparer.Ordinal)
               && (TranscribedParamNames ?? Array.Empty<string>()).SequenceEqual(
                   other.TranscribedParamNames ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FluentForwardDeclaration);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (MethodRef != null ? StringComparer.Ordinal.GetHashCode(MethodRef) : 0);
            hash = hash * 31 + Shape.GetHashCode();
            hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(SourceProperty);
            foreach (var p in SourcePath) hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(p);
            hash = hash * 31 + OverrideIgnore.GetHashCode();
            hash = hash * 31 + (TranscribedBody != null ? StringComparer.Ordinal.GetHashCode(TranscribedBody) : 0);
            foreach (var t in TranscribedParamTypes ?? Array.Empty<string>()) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(t);
            foreach (var n in TranscribedParamNames ?? Array.Empty<string>()) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(n);
            return hash;
        }
    }
}
/// <summary>
/// Fluent 通道单 (S,T) 对声明（值模型——transform 出口产物之一）
/// </summary>
internal sealed class FluentDeclaration : IEquatable<FluentDeclaration>
{
    /// <summary>完整物化快照</summary>
    public MappingSnapshot Snapshot { get; }

    /// <summary>For invocation 位置（F11 仲裁 Fluent 侧报错位置）</summary>
    public LocationSnapshot? ForLocation { get; }

    public FluentDeclaration(MappingSnapshot snapshot, LocationSnapshot? forLocation)
    {
        Snapshot = snapshot;
        ForLocation = forLocation;
    }

    /// <inheritdoc />
    public bool Equals(FluentDeclaration? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Snapshot.Equals(other.Snapshot)
               && Equals(ForLocation, other.ForLocation);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FluentDeclaration);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            return (Snapshot.GetHashCode() * 397) ^ (ForLocation?.GetHashCode() ?? 0);
        }
    }
}
/// <summary>
/// Fluent 通道单 [MapperConfig] 类提取结果（值模型——transform 出口，进增量管线缓存）
/// </summary>
internal sealed class FluentExtractionResult : IEquatable<FluentExtractionResult>
{
    /// <summary>[MapperConfig] 类简单名（NCMAP_F11 消息互指——Mapper 类名参数）</summary>
    public string MapperClassName { get; }

    /// <summary>(S,T) 对声明列表（含完整物化快照 + For 位置）</summary>
    public IReadOnlyList<FluentDeclaration> Declarations { get; }

    /// <summary>
    /// MapSubclass 合成子类对列表（平行流——不进 Declarations 的
    /// 冲突仲裁面：合成对与显式声明同对不触发 F11/F16，由 DualDriverArbiter 三查去重
    /// [MapFrom 显式 / Fluent 显式 / 已加入合成集] 后入 mappings）。ForLocation = 基类对
    /// For 位置（合成对无自身声明位置，诊断定位回登记基类对）。
    /// </summary>
    public IReadOnlyList<FluentDeclaration> SyntheticDeclarations { get; }

    /// <summary>通道诊断（F02/F04~F08/F12~F15——DiagnosticSnapshot 值形态）</summary>
    public IReadOnlyList<DiagnosticSnapshot> Diagnostics { get; }

    public FluentExtractionResult(string mapperClassName, IReadOnlyList<FluentDeclaration> declarations, IReadOnlyList<DiagnosticSnapshot> diagnostics,
        IReadOnlyList<FluentDeclaration>? syntheticDeclarations = null)
    {
        MapperClassName = mapperClassName;
        Declarations = declarations;
        SyntheticDeclarations = syntheticDeclarations ?? Array.Empty<FluentDeclaration>();
        Diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public bool Equals(FluentExtractionResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(MapperClassName, other.MapperClassName, System.StringComparison.Ordinal)
               && Declarations.SequenceEqual(other.Declarations)
               && SyntheticDeclarations.SequenceEqual(other.SyntheticDeclarations)
               && Diagnostics.SequenceEqual(other.Diagnostics);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FluentExtractionResult);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (MapperClassName?.GetHashCode() ?? 0);
            foreach (var d in Declarations) hash = hash * 31 + d.GetHashCode();
            foreach (var d in SyntheticDeclarations) hash = hash * 31 + d.GetHashCode();
            foreach (var d in Diagnostics) hash = hash * 31 + d.GetHashCode();
            return hash;
        }
    }
}
/// <summary>
/// 块内 Convert 转换器引用（复用主链 <see cref="FluentForwardDeclaration"/> 七步解析产物，源基准换子源类型）。
/// <para>值化投影：MethodRef 字符串 + Shape 枚举 + SourceProperty/SourcePath 符号比较 + OverrideIgnore 值；
/// 持有 Decl 引用供物化期取 IMethodSymbol（符号态，物化后丢弃，不进增量缓存）。</para>
/// </summary>
internal sealed class ComposeConverterRef : IEquatable<ComposeConverterRef>
{
    /// <summary>主链正向声明解析产物（符号态——物化期取 Method 生成转换器调用）</summary>
    public FluentForwardDeclaration Decl { get; }

    public ComposeConverterRef(FluentForwardDeclaration decl)
    {
        Decl = decl;
    }

    /// <summary>方法引用值化投影 "{ContainingType.ToDisplayString()}.{Name}"（A 形态；B 形态 null）</summary>
    internal string? MethodRef => Decl.MethodRef;

    /// <summary>4+2 模板匹配形态（含 Nullable 守卫变体编码）</summary>
    internal ConverterShape Shape => Decl.Shape;

    /// <inheritdoc />
    public bool Equals(ComposeConverterRef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(MethodRef, other.MethodRef, StringComparison.Ordinal)
               && Shape == other.Shape
               && SymbolEqualityComparer.Default.Equals(Decl.SourceProperty, other.Decl.SourceProperty)
               && Decl.SourcePath.SequenceEqual(other.Decl.SourcePath, SymbolEqualityComparer.Default)
               && Decl.OverrideIgnore == other.Decl.OverrideIgnore
               && string.Equals(Decl.TranscribedBody, other.Decl.TranscribedBody, StringComparison.Ordinal)
               // 内联转录形参组镜像（对齐同文件
               // FluentForwardDeclaration.Equals 先例——不加入则「改 Convert 内联 lambda
               // 形参类型/名组」的增量失效漏判）
               && (Decl.TranscribedParamTypes ?? Array.Empty<string>()).SequenceEqual(
                   other.Decl.TranscribedParamTypes ?? Array.Empty<string>(), StringComparer.Ordinal)
               && (Decl.TranscribedParamNames ?? Array.Empty<string>()).SequenceEqual(
                   other.Decl.TranscribedParamNames ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ComposeConverterRef);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (MethodRef != null ? StringComparer.Ordinal.GetHashCode(MethodRef) : 0);
            hash = hash * 31 + Shape.GetHashCode();
            hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(Decl.SourceProperty);
            foreach (var p in Decl.SourcePath) hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(p);
            hash = hash * 31 + Decl.OverrideIgnore.GetHashCode();
            hash = hash * 31 + (Decl.TranscribedBody != null ? StringComparer.Ordinal.GetHashCode(Decl.TranscribedBody) : 0);
            // 内联转录形参组镜像（与 Equals 字段集对齐）
            foreach (var t in Decl.TranscribedParamTypes ?? Array.Empty<string>()) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(t);
            foreach (var n in Decl.TranscribedParamNames ?? Array.Empty<string>()) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(n);
            return hash;
        }
    }
}
/// <summary>
/// 解析期单条 FromSource 补充源绑定声明（符号态，选型 X）。
/// <para><see cref="SubSourceType"/>/<see cref="TargetProperty"/>/<see cref="SourcePath"/> 为
/// 提取期符号解析产物（速记经 LambdaParser 段符号；块经块内方法名分派），物化期填
/// <c>MappingSnapshot.ComposeMappings</c>（零映射引擎改动全量复用，G3 字节门），
/// 与 MaskType 符号态持有同生命周期先例（MappingSnapshotBuilder.cs）。</para>
/// <para><see cref="Form"/> 区分速记/分组块；<see cref="BlockKind"/> 仅在 Block 形态承载
/// 块内方法种类（Map/Ignore/MapTree/Mask/FromJson——Convert 经 <see cref="Converter"/> 区分，
/// 不入此枚举以防破坏物化 switch）。<see cref="MaskType"/>仅在块内 Mask 承载（D-1 就绪后
/// 物化消费；未就绪降级不消费）。<see cref="Converter"/> 仅在块内 Convert 承载。</para>
/// <para>IEquatable 值化投影：SubSourceType/TargetProperty/SourcePath 符号比较
/// + Form/BlockKind/MaskType 值比较 + Converter 引用值比较；Location 不参与（值层剥离惯例）。</para>
/// </summary>
internal sealed class ComposeFluentDeclaration : IEquatable<ComposeFluentDeclaration>
{
    /// <summary>子源类型符号（FromSource&lt;TSubSource&gt; 泛型实参）</summary>
    public INamedTypeSymbol SubSourceType { get; }

    /// <summary>目标属性名（dest 锚点——物化 TargetProperty 名）</summary>
    public string TargetPropertyName { get; }

    /// <summary>目标属性符号（dest 单段锚点；不变量：进入物化的声明恒非 null）</summary>
    public IPropertySymbol TargetProperty { get; }

    /// <summary>
    /// 源路径逐段符号（速记 src / 块内 Map——单段=单元素，多段=嵌套路径；
    /// 块内 Ignore/MapTree/Mask/FromJson/Convert 为 null——物化按名解析子源侧）。
    /// </summary>
    public List<IPropertySymbol>? SourcePath { get; }

    /// <summary>声明位置（诊断定位——F29 同 dest 矛盾仲裁）</summary>
    public Microsoft.CodeAnalysis.Location Location { get; }

    /// <summary>声明形态（速记 / 分组块）</summary>
    public ComposeDeclarationForm Form { get; }

    /// <summary>块内方法种类（Block 形态 Map/Ignore/MapTree/Mask/FromJson；Convert 为 null 经 Converter 区分；Shorthand 为 null）</summary>
    public FluentDeclarationKind? BlockKind { get; }

    /// <summary>脱敏实现类型符号（块内 Mask only；D-1 就绪后物化消费）</summary>
    public INamedTypeSymbol? MaskType { get; }

    /// <summary>块内 Convert 转换器引用（Block 形态 Convert only）</summary>
    public ComposeConverterRef? Converter { get; }

    /// <summary>格式化串（块内 Format only；物化期校验后置位 cm.FormatString）</summary>
    public string? FormatString { get; }

    public ComposeFluentDeclaration(
        INamedTypeSymbol subSourceType,
        string targetPropertyName,
        IPropertySymbol targetProperty,
        List<IPropertySymbol>? sourcePath,
        Microsoft.CodeAnalysis.Location location,
        ComposeDeclarationForm form,
        FluentDeclarationKind? blockKind = null,
        INamedTypeSymbol? maskType = null,
        ComposeConverterRef? converter = null,
        string? formatString = null)
    {
        SubSourceType = subSourceType;
        TargetPropertyName = targetPropertyName;
        TargetProperty = targetProperty;
        SourcePath = sourcePath;
        Location = location;
        Form = form;
        BlockKind = blockKind;
        MaskType = maskType;
        Converter = converter;
        FormatString = formatString;
    }

    /// <inheritdoc />
    public bool Equals(ComposeFluentDeclaration? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SymbolEqualityComparer.Default.Equals(SubSourceType, other.SubSourceType)
               && string.Equals(TargetPropertyName, other.TargetPropertyName, StringComparison.Ordinal)
               && SymbolEqualityComparer.Default.Equals(TargetProperty, other.TargetProperty)
               && (SourcePath == null) == (other.SourcePath == null)
               && (SourcePath == null || SourcePath.SequenceEqual(other.SourcePath!, SymbolEqualityComparer.Default))
               && Form == other.Form
               && BlockKind == other.BlockKind
               && SymbolEqualityComparer.Default.Equals(MaskType, other.MaskType)
               && Equals(Converter, other.Converter)
               && string.Equals(FormatString, other.FormatString, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ComposeFluentDeclaration);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(SubSourceType);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(TargetPropertyName);
            hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(TargetProperty);
            if (SourcePath != null) foreach (var p in SourcePath) hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(p);
            hash = hash * 31 + Form.GetHashCode();
            hash = hash * 31 + BlockKind.GetHashCode();
            hash = hash * 31 + (MaskType != null ? SymbolEqualityComparer.Default.GetHashCode(MaskType) : 0);
            hash = hash * 31 + (Converter?.GetHashCode() ?? 0);
            hash = hash * 31 + (FormatString != null ? StringComparer.Ordinal.GetHashCode(FormatString) : 0);
            return hash;
        }
    }
}
