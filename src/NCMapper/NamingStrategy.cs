namespace NCMapper;

/// <summary>
/// 属性名匹配策略（全局映射配置）
/// <para>作用于"单段属性名的字符级比较"；与扁平化（OrderItemName → Order.Item.Name）正交叠加，
/// 四种策略下扁平化均保持生效（Default 即现状行为）。</para>
/// <para>本枚举后续新增值（如 kebab-case）不视为破坏性变更，
/// 消费者 switch 表达式须带 default 分支。</para>
/// </summary>
public enum NamingStrategy
{
    /// <summary>
    /// 现状行为：忽略大小写同名匹配 + 自动扁平化（零变更默认）
    /// </summary>
    Default = 0,

    /// <summary>
    /// 大小写敏感精确匹配 + 自动扁平化（关闭忽略大小写）
    /// </summary>
    CaseSensitive = 1,

    /// <summary>
    /// snake_case ↔ PascalCase 双向归一化匹配（user_name ↔ UserName）
    /// </summary>
    SnakeCase = 2,

    /// <summary>
    /// UPPER_SNAKE_CASE ↔ PascalCase 双向归一化匹配（USER_NAME ↔ UserName）
    /// </summary>
    UpperSnakeCase = 3,
}
