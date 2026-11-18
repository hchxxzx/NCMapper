using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NCMapper.Shared;

namespace NCMapper;

/// <summary>
/// 投影按名查找解析器 — EFCore/FreeSql 投影构建器的单段按名查找统一出口（收拢）。
/// <para>与内存路径（PropertyMatcher.ResolveByName）共用共享算法 NameCandidateResolver：
/// 默认配置（空剥缀清单 ∧ NamingStrategy.Default）→ 现状 OrdinalIgnoreCase FirstOrDefault（零变更）；
/// 配置非默认 → 归一化字典查表（策略/前后缀，双路径一致）。</para>
/// <para><b>收拢口径</b>（职责边界）：仅收拢"单段按名查找"；
/// 扁平化探测（TryBuildFlattenedExpression）与点路径（TryBuildDotPathExpression）的段匹配
/// 为现状逻辑兜底（查表序 ④），不随命名策略，保持现状。</para>
/// <para>每次调用构建归一化字典（不缓存）：投影表达式树构建为一次性成本
/// （EFCore/FreeSql 查询计划缓存），非热循环路径；延伸记录。</para>
/// </summary>
internal static class ProjectionNameResolver
{
    /// <summary>
    /// 单段按名查找 — 在属性列表中按配置匹配源属性
    /// </summary>
    /// <param name="sourceProps">源属性列表</param>
    /// <param name="name">目标名称（源字段名）</param>
    /// <returns>匹配的源属性；未命中返回 null</returns>
    [RequiresUnreferencedCode("Projection name lookup triggers global config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    internal static PropertyInfo? Resolve(List<PropertyInfo> sourceProps, string name)
    {
        var config = GlobalMapConfig.GetEffective();
        if (config.Prefixes.Length == 0 && config.Postfixes.Length == 0
            && config.NamingStrategy == NamingStrategy.Default)
        {
            // 零变更默认路径：与现状逐字节一致
            return sourceProps.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        var names = sourceProps.Select(p => p.Name).ToList();
        var dict = NameCandidateResolver.BuildNormalizedSourceKeys(
            names,
            config.Prefixes,
            config.Postfixes,
            (int)config.NamingStrategy,
            config.NamingStrategy == NamingStrategy.Default
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        if (NameCandidateResolver.TryResolveCandidate(dict, name, (int)config.NamingStrategy, out var index))
            return sourceProps[index];
        return null;
    }
}
