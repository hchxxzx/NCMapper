// 快照运行时元数据承载类型
// 运行时目标属性信号槽——PropertyRef 消费子集的运行时形态（D2 判定输入重建）。
// 设计约束：纯 public init 属性，字段仅 Type + string + bool + int（零 SourceGen 符号引用）；
// MapFromAttribute? 反射缓存字段消除"注册表命中但无特性实例"语义缺口。
using System;

namespace NCMapper.Runtime;

/// <summary>
/// 运行时目标属性信号槽 —— 目标类型属性级映射声明信号的运行时形态
/// </summary>
/// <remarks>
/// <para>对应源生成器快照 <c>PropertyRef</c> 的编译期信号（特性/Fluent 声明采集）；
/// 运行时全局忽略配置（MapOptions/全局清单）本就运行时可达，不入发射范围。</para>
/// <para>承载 <c>[MapIgnore(Both)]</c> 差集重建规则：Both 忽略属性在发射清单中零痕迹，
/// 消费端经「运行时属性全集 - AllTargetSlots - 可写性门」差集重建。</para>
/// </remarks>
public sealed class RuntimeTargetPropertySlot
{
    /// <summary>目标属性名</summary>
    public string Name { get; init; } = "";

    /// <summary>目标属性类型显示名（含可空注解；<see cref="TypeRef"/> 为 null 时兜底比对依据）</summary>
    public string TypeName { get; init; } = "";

    /// <summary>目标属性类型引用（类型 public 可达时非 null；不可达时 null——<see cref="TypeName"/> 兜底）</summary>
    public Type? TypeRef { get; init; }

    /// <summary>所属 DTO 类型原本是否标注 [MapFrom] 特性（发射时回填；Fluent DTO 此字段=false）</summary>
    public bool HasMapFromAttribute { get; init; }

    /// <summary>
    /// [MapFrom] 特性实例反射缓存（发射时物化回填；Fluent DTO 此字段=null）——
    /// 消费端统一走 <c>slot.MapFromAttribute ?? type.GetCustomAttribute&lt;MapFromAttribute&gt;()</c> 单回退路径
    /// </summary>
    public Attributes.MapFromAttribute? MapFromAttribute { get; init; }

    /// <summary>是否声明忽略（[MapIgnore] 或 Fluent Ignore 声明物化）</summary>
    public bool HasMapIgnore { get; init; }

    /// <summary>忽略方向（0=Both,1=ToDto,2=ToEntity；与快照 MapIgnoreDirection 同口径）</summary>
    public int MapIgnoreDirection { get; init; }

    /// <summary>忽略豁免（显式 OverrideIgnore 特性或 Fluent 四终端 overrideIgnore 物化叠加）</summary>
    public bool HasOverrideIgnore { get; init; }

    /// <summary>是否显式声明映射属性（[MapProperty] 或 Fluent Map/Convert 显式声明）</summary>
    public bool HasMapProperty { get; init; }

    /// <summary>[MapProperty] FromField（点路径/索引路径串；null=未声明）</summary>
    public string? FromField { get; init; }

    /// <summary>[MapProperty(FromSource=typeof(X))] 跨表源类型显示名（null=未声明）</summary>
    public string? FromSourceTypeName { get; init; }

    /// <summary>是否声明脱敏（[Mask] 或 Fluent Mask 声明）</summary>
    public bool HasMask { get; init; }

    /// <summary>脱敏类型显示名（null=未声明）</summary>
    public string? MaskTypeName { get; init; }

    /// <summary>是否声明转换器（[Convert] 特性采集面）</summary>
    public bool HasConverter { get; init; }

    /// <summary>转换器锚定类型全名（null=未声明）</summary>
    public string? ConverterDeclaringType { get; init; }

    /// <summary>转换器静态方法名（null=未声明）</summary>
    public string? ConverterMethodName { get; init; }

    /// <summary>是否声明反向转换器（[ConvertBack] 特性采集面）</summary>
    public bool HasConvertBack { get; init; }

    /// <summary>反向转换器锚定类型全名（null=未声明）</summary>
    public string? ConverterBackDeclaringType { get; init; }

    /// <summary>反向转换器静态方法名（null=未声明）</summary>
    public string? ConverterBackMethodName { get; init; }

    /// <summary>是否 JSON 反序列化形态（[MapProperty(IsFromJson=true)] / Fluent FromJson 声明）</summary>
    public bool IsFromJson { get; init; }
}
