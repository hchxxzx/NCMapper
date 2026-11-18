using System.Collections.Generic;
using System.Linq;

namespace NCMapper.Shared;

/// <summary>
/// 属性名候选解析共享算法 — 核心库运行时路径与 SourceGen 编译期路径物理同源的纯字符串算法
/// （方案 v1.2 共享函数契约；v1.1 E2 物理同源决议）。
/// <para>本文件同时被 <c>NCMapper</c>（net8/9/10）与 <c>NCMapper.SourceGen</c>（netstandard2.0）编译，
/// 因此不得引用任何核心库运行时类型；命名策略以 int 承载（0-3 对应 NamingStrategy 枚举值），
/// 属性枚举与字典构建为三端适配层职责，本类只做归一化、变体生成、查表决策与全局忽略判定。</para>
/// </summary>
internal static class NameCandidateResolver
{
    /// <summary>
    /// 命名策略归一化 — 将属性名归一化为比较键。
    /// <para>策略值（对应 NamingStrategy 枚举）：0=Default（原名）、1=CaseSensitive（原名）、
    /// 2=SnakeCase / 3=UpperSnakeCase（去下划线 + 大写，双向同形即匹配）。
    /// Default/CaseSensitive 返回原名，大小写比较语义由调用方字典比较器承担
    /// （Default → OrdinalIgnoreCase；CaseSensitive → Ordinal）。</para>
    /// </summary>
    /// <param name="name">属性名</param>
    /// <param name="strategy">命名策略枚举值（0-3）</param>
    /// <returns>归一化后的比较键</returns>
    internal static string Normalize(string name, int strategy)
    {
        return strategy switch
        {
            2 or 3 => name.Replace("_", "").ToUpperInvariant(),
            _ => name,
        };
    }

    /// <summary>
    /// 源属性剥缀变体生成（剥缀仅源侧，v1.2 G2）— 返回 [原名] + [剥缀变体]。
    /// <para>变体规则：前缀组合（剥掉每个命中前缀）、后缀组合（剥掉每个命中后缀）、
    /// 前后缀组合（先剥前缀再剥后缀）；剥缀匹配 OrdinalIgnoreCase，且剥后名非空。
    /// 目标属性名不参与剥缀，仅经策略归一生成查表键。</para>
    /// </summary>
    /// <param name="name">源属性名</param>
    /// <param name="prefixes">前缀剥离清单</param>
    /// <param name="postfixes">后缀剥离清单</param>
    /// <returns>变体名序列（首个为原名）</returns>
    internal static IEnumerable<string> GetStripVariants(string name, string[] prefixes, string[] postfixes)
    {
        yield return name;
        if (prefixes.Length == 0 && postfixes.Length == 0)
            yield break;

        foreach (var prefix in prefixes)
        {
            if (name.Length > prefix.Length
                && name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                yield return name.Substring(prefix.Length);
            }
        }

        foreach (var postfix in postfixes)
        {
            if (name.Length > postfix.Length
                && name.EndsWith(postfix, System.StringComparison.OrdinalIgnoreCase))
            {
                yield return name.Substring(0, name.Length - postfix.Length);
            }
        }

        foreach (var prefix in prefixes)
        {
            if (name.Length <= prefix.Length
                || !name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var stripped = name.Substring(prefix.Length);
            foreach (var postfix in postfixes)
            {
                if (stripped.Length > postfix.Length
                    && stripped.EndsWith(postfix, System.StringComparison.OrdinalIgnoreCase))
                {
                    yield return stripped.Substring(0, stripped.Length - postfix.Length);
                }
            }
        }
    }

    /// <summary>
    /// 构建归一化源字典 — 键 = 源属性所有剥缀变体经策略归一化后的比较键，值 = 源属性序号。
    /// <para>撞键规则：声明序首个保留（先到先得），后续同键变体丢弃。</para>
    /// </summary>
    /// <param name="sourcePropertyNames">源属性名序列（声明序）</param>
    /// <param name="prefixes">前缀剥离清单</param>
    /// <param name="postfixes">后缀剥离清单</param>
    /// <param name="strategy">命名策略枚举值（0-3）</param>
    /// <param name="keyComparer">字典比较器（Default → OrdinalIgnoreCase；其余 → Ordinal）</param>
    /// <returns>归一化键 → 源属性序号 的字典</returns>
    internal static Dictionary<string, int> BuildNormalizedSourceKeys(
        IReadOnlyList<string> sourcePropertyNames,
        string[] prefixes,
        string[] postfixes,
        int strategy,
        IEqualityComparer<string> keyComparer)
    {
        var dict = new Dictionary<string, int>(keyComparer);
        for (var i = 0; i < sourcePropertyNames.Count; i++)
        {
            foreach (var variant in GetStripVariants(sourcePropertyNames[i], prefixes, postfixes))
            {
                var key = Normalize(variant, strategy);
                if (!dict.ContainsKey(key))
                    dict[key] = i;
            }
        }
        return dict;
    }

    /// <summary>
    /// 候选查表决策 — 目标属性名经策略归一化后在归一化源字典中查表。
    /// <para>一次查表即覆盖"精确名命中 / 剥缀变体命中 / 策略归一化命中"三种键来源
    /// （变体与归一化均已在字典构建时入键）；扁平化探测由调用方兜底（现状逻辑）。</para>
    /// </summary>
    /// <param name="normalizedSourceKeys">归一化源字典（<see cref="BuildNormalizedSourceKeys"/> 构建）</param>
    /// <param name="targetName">目标属性名</param>
    /// <param name="strategy">命名策略枚举值（0-3）</param>
    /// <param name="matchedSourceIndex">命中的源属性序号</param>
    /// <returns>命中返回 true</returns>
    internal static bool TryResolveCandidate(
        IReadOnlyDictionary<string, int> normalizedSourceKeys,
        string targetName,
        int strategy,
        out int matchedSourceIndex)
    {
        return normalizedSourceKeys.TryGetValue(Normalize(targetName, strategy), out matchedSourceIndex);
    }

    /// <summary>
    /// 全局忽略判定（纯字符串版）— 双向清单与方向清单的命中判定（方案 v1.2 判定式）。
    /// <para>判定式：双向命中 = 类型全名精确匹配 ∨ 属性名精确命中 ∨ 名前缀命中 ∨ 名后缀命中；
    /// ToEntity 方向额外叠加 *ToEntity 清单（isToDto=false 时生效）。
    /// 类型全名用 Ordinal 精确匹配（精确匹配语义，不考虑可赋值/继承）；
    /// 名称类清单用 OrdinalIgnoreCase（与按名匹配现状语义一致）。</para>
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <param name="typeFullName">属性类型全名（可能为 null）</param>
    /// <param name="isToDto">是否为 ToDto 方向（false = ToEntity 方向，启用 *ToEntity 清单）</param>
    /// <param name="ignoreTypeFullNames">双向忽略类型全名清单</param>
    /// <param name="ignoreNames">双向忽略属性名清单</param>
    /// <param name="ignoreNamePrefixes">双向忽略属性名前缀清单</param>
    /// <param name="ignoreNameSuffixes">双向忽略属性名后缀清单</param>
    /// <param name="ignoreTypeFullNamesToEntity">ToEntity 方向额外忽略类型全名清单</param>
    /// <param name="ignoreNamesToEntity">ToEntity 方向额外忽略属性名清单</param>
    /// <param name="ignoreNamePrefixesToEntity">ToEntity 方向额外忽略属性名前缀清单</param>
    /// <param name="ignoreNameSuffixesToEntity">ToEntity 方向额外忽略属性名后缀清单</param>
    /// <returns>命中忽略清单返回 true</returns>
    internal static bool ShouldIgnoreGlobal(
        string propertyName,
        string? typeFullName,
        bool isToDto,
        string[] ignoreTypeFullNames,
        string[] ignoreNames,
        string[] ignoreNamePrefixes,
        string[] ignoreNameSuffixes,
        string[] ignoreTypeFullNamesToEntity,
        string[] ignoreNamesToEntity,
        string[] ignoreNamePrefixesToEntity,
        string[] ignoreNameSuffixesToEntity)
    {
        if ((typeFullName != null && ignoreTypeFullNames.Contains(typeFullName, System.StringComparer.Ordinal))
            || ignoreNames.Contains(propertyName, System.StringComparer.OrdinalIgnoreCase)
            || ignoreNamePrefixes.Any(p => propertyName.StartsWith(p, System.StringComparison.OrdinalIgnoreCase))
            || ignoreNameSuffixes.Any(s => propertyName.EndsWith(s, System.StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!isToDto
            && ((typeFullName != null && ignoreTypeFullNamesToEntity.Contains(typeFullName, System.StringComparer.Ordinal))
                || ignoreNamesToEntity.Contains(propertyName, System.StringComparer.OrdinalIgnoreCase)
                || ignoreNamePrefixesToEntity.Any(p => propertyName.StartsWith(p, System.StringComparison.OrdinalIgnoreCase))
                || ignoreNameSuffixesToEntity.Any(s => propertyName.EndsWith(s, System.StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        return false;
    }
}
