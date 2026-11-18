using System;
namespace NCMapper.Attributes;

/// <summary>
/// 声明当前类型从哪个源类型映射而来
/// <para>仅对类（class）有效，每个类只能标注一次。</para>
/// <para><b>双向映射</b>：NCMapper 基于属性名匹配，标注在一方即可双向映射。
/// 例如在 UserDto 上标注 [MapFrom(typeof(User))]，则 Map&lt;User, UserDto&gt; 和 Map&lt;UserDto, User&gt; 均可工作，
/// 无需在 User 上再标注 [MapFrom(typeof(UserDto))]。</para>
/// <para><b>模式并存</b>：Fluent 入口通道（[MapperConfig] 类 + Configure 方法）与本特性通道
/// 长期并存——可对不同 (Source, Target) 类型对混合使用两通道；同一类型对被两通道同时声明时
/// 编译期报 NCMAP_F11 互斥禁止（详见 docs/zh-CN/how-to/23-Fluent-Mapping.md）。</para>
/// <para><b>三原则指引</b>：映射配置归属映射目标侧——同一类型对双通道声明
/// → F11 互斥；属性级映射配置标注（[MapProperty]/[Convert]/[Mask]/[MapIgnore]/[MapWhen] 等）
/// 仅在其所在类有锚（标 [MapFrom] 或经 Fluent 声明为映射目标）时消费，无锚属性级标注 →
/// 编译期 NCMAP019（Error）拦截。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public class MapFromAttribute : System.Attribute, IProjectionPolicy
{
    /// <inheritdoc/>
    public bool RequiresDowngrade => false;
    /// <summary>
    /// 映射的源类型（Entity 类型）
    /// </summary>
    public Type FromSource { get; private set; }

    /// <summary>
    /// 声明当前类型从源类型映射而来
    /// </summary>
    /// <param name="fromSource">映射的源类型（Entity 类型）</param>
    /// <exception cref="ArgumentNullException">fromSource 为 null</exception>
    public MapFromAttribute(Type fromSource)
    {
        this.FromSource = fromSource ?? throw new ArgumentNullException(nameof(fromSource));
    }
}
