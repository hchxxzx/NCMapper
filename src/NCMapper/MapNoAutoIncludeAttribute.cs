namespace NCMapper.Attributes;

/// <summary>
/// 排除自动 Include — 标注的属性在降级路径自动 Include 分析时被跳过
/// <para>用于排除不需要自动加载的导航属性（如大集合、延迟加载场景）</para>
/// <para>用法：在 DTO 属性上标注 [MapNoAutoInclude]，该属性的跨表导航依赖不纳入自动 Include 树
///（T5 契约：属性级标注在其所在类标 [MapFrom]（或经 Fluent 声明为映射目标）
/// 时生效；所在类无锚时标注不消费——无锚不消费口径）</para>
/// <para><b>与对级白名单的辨析</b>（收口互指）：本特性是**属性级**标注，
/// 只影响被标注属性的自动 Include 分析，不禁该对的同名自动匹配；**对级**"禁用自动匹配"由
/// <see cref="MapNoAutoMatchAttribute"/>（特性通道）或 Fluent <c>For&lt;S,T&gt;().NoAutoMatch()</c>
/// 承载（等价对 A-25），两者粒度不同、可并存，不构成等价。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public class MapNoAutoIncludeAttribute : Attribute
{
}
