using System;
namespace NCMapper.Attributes;

/// <summary>
/// 自动映射字段部分(仅对属性有效，只能使用一次)
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public class MapPropertyAttribute : System.Attribute, IProjectionPolicy
{
    /// <inheritdoc/>
    public bool RequiresDowngrade => IsFromJson;
    /// <summary>
    /// 字段映射的源类型（Entity 类型）
    /// </summary>
    public Type FromSource { get; private set; }

    /// <summary>
    /// 源字段名称
    /// </summary>
    public string FromField { get; private set; }

    /// <summary>
    /// 是否从JSON转换而来，默认为否
    /// </summary>
    public bool IsFromJson { get; set; }

    /// <summary>
    /// 命中全局忽略清单时显式豁免（唯一豁免门）；默认 false = 服从全局忽略
    /// <para>豁免作用于映射对整体（非单侧属性）：目标属性带本特性且 OverrideIgnore=true 时，
    /// 该映射对整体跳过全局忽略判定（源属性名命中忽略清单也参与匹配）。</para>
    /// <para>与属性级 [MapIgnore] 同标即矛盾，源生成器报 NCMAP022 诊断（[MapIgnore] 不参与豁免）。</para>
    /// </summary>
    public bool OverrideIgnore { get; set; }

    /// <summary>
    /// 自动映射字段部分(仅对属性有效，只能使用一次)
    /// </summary>
    /// <param name="fromSource">字段映射的源类型（Entity 类型）</param>
    /// <param name="fromField">源字段名称</param>
    /// <exception cref="ArgumentNullException">fromSource 或 fromField 为 null</exception>
    /// <exception cref="ArgumentException">fromField 为空字符串</exception>
    public MapPropertyAttribute(Type fromSource, string fromField)
    {
        this.FromSource = fromSource ?? throw new ArgumentNullException(nameof(fromSource));
        this.FromField = fromField ?? throw new ArgumentNullException(nameof(fromField));
        if (string.IsNullOrEmpty(fromField))
            throw new ArgumentException(ErrorMessages.MapPropertyFromFieldEmpty(nameof(fromField)), nameof(fromField));
    }
}
