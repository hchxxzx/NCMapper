using System.ComponentModel;

namespace NCMapper;

/// <summary>
/// 源生成器输出的全局映射配置快照 — 纯基元常量载体，AOT 安全（零反射）。
/// <para>公开仅因生成代码需以其为参数类型（先例：<see cref="DeepCopyContext"/>，
/// 生成代码位于消费者程序集，无法访问核心库 internal 成员）。</para>
/// <para><b>【仅供生成代码调用】</b>手工注册绕开源生成器违反"单一事实来源"原则，
/// 运行时将触发 <see cref="MapConfigurationException"/> 冲突检测（多程序集配置不一致时）。</para>
/// <para><see cref="NamingStrategy"/> 以 int 承载（避免生成代码依赖枚举符号），
/// 冻结时校验取值 0-3，越界抛 <see cref="MapConfigurationException"/>；
/// Type 以全名（FullName）字符串承载（精确匹配语义）。</para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedMapConfig
{
    /// <summary>
    /// 命名策略枚举值（int 承载；0-3 对应 <see cref="NamingStrategy"/> 的
    /// Default / CaseSensitive / SnakeCase / UpperSnakeCase）
    /// </summary>
    // <GENERATED:gen-props>
    public int NamingStrategy { get; set; }

    /// <summary>
    /// 源属性名前缀剥离清单（仅源侧），≤8 项
    /// </summary>
    public string[] Prefixes { get; set; } = [];

    /// <summary>
    /// 源属性名后缀剥离清单（仅源侧），≤8 项
    /// </summary>
    public string[] Postfixes { get; set; } = [];

    /// <summary>
    /// 双向忽略类型全名清单（Type.FullName，精确匹配；开放泛型不匹配）
    /// </summary>
    public string[] IgnoreTypeFullNames { get; set; } = [];

    /// <summary>
    /// 双向忽略属性名清单（精确命中即忽略）
    /// </summary>
    public string[] IgnoreNames { get; set; } = [];

    /// <summary>
    /// 双向忽略属性名前缀清单（前缀命中即忽略）
    /// </summary>
    public string[] IgnoreNamePrefixes { get; set; } = [];

    /// <summary>
    /// 双向忽略属性名后缀清单（后缀命中即忽略）
    /// </summary>
    public string[] IgnoreNameSuffixes { get; set; } = [];

    /// <summary>
    /// ToEntity 方向额外忽略类型全名清单（Type.FullName，精确匹配）
    /// </summary>
    public string[] IgnoreTypeFullNamesToEntity { get; set; } = [];

    /// <summary>
    /// ToEntity 方向额外忽略属性名清单（精确命中即忽略）
    /// </summary>
    public string[] IgnoreNamesToEntity { get; set; } = [];

    /// <summary>
    /// ToEntity 方向额外忽略属性名前缀清单（前缀命中即忽略）
    /// </summary>
    public string[] IgnoreNamePrefixesToEntity { get; set; } = [];

    /// <summary>
    /// ToEntity 方向额外忽略属性名后缀清单（后缀命中即忽略）
    /// </summary>
    public string[] IgnoreNameSuffixesToEntity { get; set; } = [];
    // </GENERATED:gen-props>
}
