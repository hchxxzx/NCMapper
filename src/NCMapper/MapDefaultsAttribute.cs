namespace NCMapper.Attributes;

/// <summary>
/// 程序集级全局映射默认配置 — 对该程序集中所有映射生效。
/// <para>优先级：属性级显式标注 &gt; 本特性 &gt; MSBuild 属性 &gt; 内建默认。</para>
/// <para>配置在首次映射使用时冻结，运行中不可修改（冻结规则，单一事实来源 = 编译期配置）。</para>
/// <para>方向语义：ToEntity 方向忽略 ⟺（双向清单命中）∨（ToEntity 清单命中）；
/// ToDto 方向仅双向清单生效（ToDto 额外清单为设计裁决项，见）。</para>
/// <para>本特性也可由 <see cref="Mapper"/> 在无源生成代码的程序集通过反射读取（回退通道）。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class MapDefaultsAttribute : Attribute
{
    /// <summary>
    /// 命名策略（默认 <see cref="NamingStrategy.Default"/> = 现状行为）
    /// </summary>
    // <GENERATED:attr-props>
    public NamingStrategy NamingStrategy { get; set; } = NamingStrategy.Default;

    /// <summary>
    /// 源属性名前缀剥离清单（如 "Get"），≤8 项；仅作用于源侧
    /// </summary>
    public string[]? Prefixes { get; set; }

    /// <summary>
    /// 源属性名后缀剥离清单，≤8 项；仅作用于源侧
    /// </summary>
    public string[]? Postfixes { get; set; }

    /// <summary>
    /// 双向忽略：属性类型精确命中即忽略（开放泛型不匹配任何封闭构造类型）
    /// </summary>
    public Type[]? IgnoreTypes { get; set; }

    /// <summary>
    /// 双向忽略：属性名精确命中即忽略
    /// </summary>
    public string[]? IgnoreNames { get; set; }

    /// <summary>
    /// 双向忽略：属性名前缀命中即忽略
    /// </summary>
    public string[]? IgnoreNamePrefixes { get; set; }

    /// <summary>
    /// 双向忽略：属性名后缀命中即忽略
    /// </summary>
    public string[]? IgnoreNameSuffixes { get; set; }

    /// <summary>
    /// ToEntity 方向额外忽略：属性类型精确命中（仅实体侧生效）
    /// </summary>
    public Type[]? IgnoreTypesToEntity { get; set; }

    /// <summary>
    /// ToEntity 方向额外忽略：属性名精确命中
    /// </summary>
    public string[]? IgnoreNamesToEntity { get; set; }

    /// <summary>
    /// ToEntity 方向额外忽略：属性名前缀命中
    /// </summary>
    public string[]? IgnoreNamePrefixesToEntity { get; set; }

    /// <summary>
    /// ToEntity 方向额外忽略：属性名后缀命中
    /// </summary>
    public string[]? IgnoreNameSuffixesToEntity { get; set; }
    // </GENERATED:attr-props>
}
