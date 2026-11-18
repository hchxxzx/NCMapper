using System;

namespace NCMapper.Attributes;

/// <summary>
/// 标注属性在映射时按格式串输出文本 — 源须实现 <see cref="IFormattable"/>（DateTime/DateTimeOffset/
/// TimeSpan/Guid/数值/枚举，含 <see cref="Nullable{T}"/> 包装），目标须为 string。
/// <para>用法：[MapFormat("yyyy-MM-dd")]（日期）/ [MapFormat("N2")]（数值）/ [MapFormat("D")]（枚举整数形式）。</para>
/// <para>文化口径：恒传 null provider（= IFormattable.ToString(format, null) 的"当前文化"语义）——
/// 与未标注属性恒 InvariantCulture 的转换路径输出分叉为有意设计；需要跨文化一致的固定输出请改用
/// [Convert] 自控文化。</para>
/// <para>三类窄格式域提示：Guid 格式串仅认 N/D/B/P/X；TimeSpan 自定义格式串的冒号与句点须转义
/// （如 hh\:mm）；枚举格式串为 G/D/X/F 等枚举专用形式（拿日期格式串套枚举会在运行时抛
/// FormatException——格式串语义为运行时，编译期只校验非空）。</para>
/// <para>反向不支持：本特性仅正向（Entity→DTO）消费；反向派生命中且未标 [ConvertBack] 时，
/// 源生成器报 NCMAP046（Warning），运行时引擎抛 <see cref="NCMapper.MapException"/> 同口径文本。</para>
/// <para>路径说明（无锚不消费口径，与 [Mask] 同）：[MapFormat] 标注在其所在类标
/// [MapFrom]（或经 Fluent 声明为映射目标）时生效；所在类无锚时标注不消费，
/// 无锚属性级标注由 NCMAP019 编译期拦截。</para>
/// <para>投影路径：实现 IProjectionPolicy（RequiresDowngrade=true），触发 AsDto 自动降级为二步映射。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class MapFormatAttribute : Attribute, IProjectionPolicy
{
    /// <summary>
    /// 格式串（.NET 标准或自定义格式串；语义为运行时，编译期只校验非空）
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// 格式化为 CLR 路径，不可翻译为 SQL，需触发投影降级
    /// </summary>
    public bool RequiresDowngrade => true;

    /// <summary>
    /// 命中全局忽略清单时显式豁免（唯一豁免门）；默认 false = 服从全局忽略
    /// <para>豁免作用于映射对整体（非单侧属性）：目标属性带本特性且 OverrideIgnore=true 时，
    /// 该映射对整体跳过全局忽略判定（源属性名命中忽略清单也参与匹配）。</para>
    /// <para>与属性级 [MapIgnore] 同标即矛盾，源生成器报 NCMAP022 诊断（[MapIgnore] 不参与豁免）。</para>
    /// </summary>
    public bool OverrideIgnore { get; set; }

    /// <summary>
    /// 创建指定格式串的特性实例
    /// </summary>
    /// <param name="format">格式串，空/空白抛 <see cref="ArgumentException"/></param>
    public MapFormatAttribute(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            throw new ArgumentException(ErrorMessages.MapFormatEmpty(), nameof(format));
        Format = format;
    }
}
