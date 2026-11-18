namespace NCMapper;

/// <summary>
/// 冻结配置的只读视图（不可变）— 用于调试、测试断言、日志。
/// <para>构造时对清单做 defensive copy，IReadOnlyList 底层数组不外泄
/// （接口包装不防改，必须拷贝；方案 v1.2 I4）。</para>
/// </summary>
public sealed record GlobalConfigView
{
    /// <summary>
    /// 命名策略
    /// </summary>
    // <GENERATED:view-props>
    public NamingStrategy NamingStrategy { get; }

    /// <summary>
    /// 源属性名前缀剥离清单（仅源侧）
    /// </summary>
    public IReadOnlyList<string> Prefixes { get; }

    /// <summary>
    /// 源属性名后缀剥离清单（仅源侧）
    /// </summary>
    public IReadOnlyList<string> Postfixes { get; }

    /// <summary>
    /// 双向忽略类型全名清单（精确匹配）
    /// </summary>
    public IReadOnlyList<string> IgnoreTypeFullNames { get; }

    /// <summary>
    /// 双向忽略属性名清单（精确匹配）
    /// </summary>
    public IReadOnlyList<string> IgnoreNames { get; }

    /// <summary>
    /// 双向忽略属性名前缀清单
    /// </summary>
    public IReadOnlyList<string> IgnoreNamePrefixes { get; }

    /// <summary>
    /// 双向忽略属性名后缀清单
    /// </summary>
    public IReadOnlyList<string> IgnoreNameSuffixes { get; }

    /// <summary>
    /// ToEntity 方向额外忽略类型全名清单（精确匹配）
    /// </summary>
    public IReadOnlyList<string> IgnoreTypeFullNamesToEntity { get; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名清单（精确匹配）
    /// </summary>
    public IReadOnlyList<string> IgnoreNamesToEntity { get; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名前缀清单
    /// </summary>
    public IReadOnlyList<string> IgnoreNamePrefixesToEntity { get; }

    /// <summary>
    /// ToEntity 方向额外忽略属性名后缀清单
    /// </summary>
    public IReadOnlyList<string> IgnoreNameSuffixesToEntity { get; }
    // </GENERATED:view-props>

    /// <summary>
    /// 创建只读视图（internal 构造 — 由 <see cref="GlobalMapConfig"/> 冻结后产出）
    /// </summary>
    /// <param name="namingStrategy">命名策略</param>
    /// <param name="prefixes">源属性名前缀剥离清单</param>
    /// <param name="postfixes">源属性名后缀剥离清单</param>
    /// <param name="ignoreTypeFullNames">双向忽略类型全名清单</param>
    /// <param name="ignoreNames">双向忽略属性名清单</param>
    /// <param name="ignoreNamePrefixes">双向忽略属性名前缀清单</param>
    /// <param name="ignoreNameSuffixes">双向忽略属性名后缀清单</param>
    /// <param name="ignoreTypeFullNamesToEntity">ToEntity 方向额外忽略类型全名清单</param>
    /// <param name="ignoreNamesToEntity">ToEntity 方向额外忽略属性名清单</param>
    /// <param name="ignoreNamePrefixesToEntity">ToEntity 方向额外忽略属性名前缀清单</param>
    /// <param name="ignoreNameSuffixesToEntity">ToEntity 方向额外忽略属性名后缀清单</param>
    internal GlobalConfigView(
        // <GENERATED:view-ctor-params>
        NamingStrategy namingStrategy,
        IReadOnlyList<string>? prefixes,
        IReadOnlyList<string>? postfixes,
        IReadOnlyList<string>? ignoreTypeFullNames,
        IReadOnlyList<string>? ignoreNames,
        IReadOnlyList<string>? ignoreNamePrefixes,
        IReadOnlyList<string>? ignoreNameSuffixes,
        IReadOnlyList<string>? ignoreTypeFullNamesToEntity,
        IReadOnlyList<string>? ignoreNamesToEntity,
        IReadOnlyList<string>? ignoreNamePrefixesToEntity,
        IReadOnlyList<string>? ignoreNameSuffixesToEntity)
        // </GENERATED:view-ctor-params>
    {
        // <GENERATED:view-ctor-assign>
        NamingStrategy = namingStrategy;
        Prefixes = Copy(prefixes);
        Postfixes = Copy(postfixes);
        IgnoreTypeFullNames = Copy(ignoreTypeFullNames);
        IgnoreNames = Copy(ignoreNames);
        IgnoreNamePrefixes = Copy(ignoreNamePrefixes);
        IgnoreNameSuffixes = Copy(ignoreNameSuffixes);
        IgnoreTypeFullNamesToEntity = Copy(ignoreTypeFullNamesToEntity);
        IgnoreNamesToEntity = Copy(ignoreNamesToEntity);
        IgnoreNamePrefixesToEntity = Copy(ignoreNamePrefixesToEntity);
        IgnoreNameSuffixesToEntity = Copy(ignoreNameSuffixesToEntity);
        // </GENERATED:view-ctor-assign>
    }

    /// <summary>
    /// 清单防御性拷贝 — 空/null 输入返回空数组（底层数组不外泄）
    /// </summary>
    /// <param name="items">源清单</param>
    /// <returns>拷贝后的新数组</returns>
    private static string[] Copy(IReadOnlyList<string>? items)
        => items == null || items.Count == 0 ? Array.Empty<string>() : items.ToArray();
}
