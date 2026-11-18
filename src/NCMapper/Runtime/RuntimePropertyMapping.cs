// 快照运行时元数据承载类型
// 运行时属性映射对——PropertyMappingSnapshot 消费子集的运行时形态。
using NCMapper.Attributes;
using System;

namespace NCMapper.Runtime;

/// <summary>
/// 运行时属性映射对 —— 单个目标属性的源属性映射关系运行时形态
/// </summary>
public sealed class RuntimePropertyMapping
{
    /// <summary>目标属性信号槽</summary>
    public RuntimeTargetPropertySlot Target { get; init; } = null!;

    /// <summary>源属性名（null = 转换器无匹配源场景）</summary>
    public string? SourceName { get; init; }

    /// <summary>源属性访问路径段名（扁平化/点路径时多于 1 段）</summary>
    public string[] SourcePathNames { get; init; } = Array.Empty<string>();

    /// <summary>源叶属性类型显示名（null=无源）</summary>
    public string? SourcePropertyTypeName { get; init; }

    /// <summary>源叶属性类型引用（public 可达时非 null）</summary>
    public Type? SourcePropertyTypeRef { get; init; }

    /// <summary>路径是否含索引段（判定输入：FromField 含 '['）</summary>
    public bool HasIndex { get; init; }

    /// <summary>是否为扁平化属性（多段路径）</summary>
    public bool IsFlattened { get; init; }

    /// <summary>是否为反向写回属性（ReverseWritePath 非 null）</summary>
    public bool IsReverseWriteBack { get; init; }

    /// <summary>映射对级转换器承载（[Convert]/Fluent Convert 注入；false=无）</summary>
    public bool HasConverter { get; init; }

    /// <summary>映射对级转换器锚定类型全名（null=无）</summary>
    public string? ConverterDeclaringType { get; init; }

    /// <summary>映射对级转换器方法名（null=无）</summary>
    public string? ConverterMethodName { get; init; }

    /// <summary>映射对级脱敏承载（[Mask]/Fluent Mask；false=无）</summary>
    public bool HasMask { get; init; }

    /// <summary>映射对级脱敏类型显示名（null=无）</summary>
    public string? MaskTypeName { get; init; }

    /// <summary>
    /// 映射对级格式化承载（[MapFormat]/Fluent Format 的格式串文本；null=无）
    /// <para>
    /// 唯一真实消费方为降级判定（DegradationDetectorBase.ClassifyUntranslatableDeclaration 读"Format"分类）；
    /// emit 端条件发射——null 时不落产物行（F1 字节门）。</para>
    /// </summary>
    public string? FormatString { get; init; }

    /// <summary>JSON 反序列化形态（映射对级 IsFromJson）</summary>
    public bool IsFromJson { get; init; }

    /// <summary>属性级 NoAutoInclude（属性级 [MapNoAutoInclude] / Fluent .NoAutoInclude(dest) 承载）</summary>
    public bool IsPropertyNoAutoInclude { get; init; }

    /// <summary>
    /// 源路径索引段（补 <see cref="HasIndex"/> 布尔之不足）
    /// <para>与 <see cref="SourcePathNames"/> 等长逐段配对：每元素 = 该段常量索引值，<c>-1</c> = 非索引段占位；
    /// null = 无索引信息。双模型承载：SourceGen 快照域为 <c>int?</c> 元素级语义（null=非索引段），
    /// Runtime 域为 <c>int[]?</c> + <c>-1</c> 占位（发射腿 <c>i ?? -1</c> 转换，SnapshotMetadataEmitter IntArray 行）。</para>
    /// <para>消费面：投影工厂（ProjectionExpressionFactory.BuildProjectionValueExpression 经
    /// BuildSegmentsWithIndexes）按契约消费——段索引回填段名（"Items" + 0 → "Items[0]"）交点路径
    /// 索引段解析（起为按契约消费；此前仅段名 join，索引信息被丢弃致绑定静默跳过）。</para>
    /// <para>不一致手写形态处置：手工 Register 给出的数组若与段数不等长 / 含负值，消费端逐段守卫
    /// 保守回退原段名（越界与负值段不重建）；<see cref="HasIndex"/> 为快照布尔与发射端条件门
    /// （纯无索引 pair 不落产物行），非运行时重建判据。</para>
    /// </summary>
    public int[]? SourceIndexes { get; init; }

    /// <summary>
    /// 条件守卫方法引用—— "全限定类型::方法名"文本
    /// （A 形态=用户类型限定名——运行时按名解析静态方法；B 形态=生成类型限定名——
    /// 运行时经 NonPublic 反射解析；null = 无条件）。
    /// <para>消费锚点：PropertyMatcher 快照读取入口（义务②——B9）→ 运行时回退赋值链守卫
    /// （生成/运行时双路径一致，S8/P-d）；A/B 双载体解析规则。</para>
    /// <para>emit 端条件发射——null 时不落产物行（字节不变门）。</para>
    /// </summary>
    public string? ConditionMethodRef { get; init; }

    /// <summary>
    /// 条件守卫谓词入参形态底层值（0=None 无条件 / 1=FieldValue T1 字段值 / 2=WholeSource T2 整源——
    /// 与快照 ConditionShape 同口径；int 承载镜像 RuntimeTargetPropertySlot.MapIgnoreDirection 先例）——
    /// 运行时回退实参选择依据。emit 端条件发射——0 时不落产物行（字节不变门）。
    /// </summary>
    public int ConditionShape { get; init; }
}
