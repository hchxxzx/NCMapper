using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NCMapper.Shared;

namespace NCMapper;

/// <summary>
/// 全局忽略判定（运行时适配层）— 读取冻结的全局映射配置，调共享纯字符串判定函数
/// <see cref="NameCandidateResolver.ShouldIgnoreGlobal"/> 执行忽略清单命中判定。
/// <para>与 SourceGen 编译期路径共用同一判定函数（v1.2 判定面双路径对齐），杜绝分歧。</para>
/// </summary>
internal static class GlobalIgnoreChecker
{
    /// <summary>
    /// 判断属性是否命中全局忽略清单（fail-closed：命中即忽略）。
    /// <para>判定式（方案 v1.2）：双向命中 ∨（ToEntity 方向 ∧ *ToEntity 清单命中）；
    /// 类型精确匹配；名称类清单 OrdinalIgnoreCase。</para>
    /// </summary>
    /// <param name="prop">属性信息</param>
    /// <param name="isToDto">是否为 ToDto 方向（false = ToEntity 方向，启用 *ToEntity 清单）</param>
    /// <returns>命中全局忽略清单返回 true</returns>
    [RequiresUnreferencedCode("Global ignore check triggers config freeze, which reflects over [assembly: MapDefaults] when no generated snapshot exists.")]
    public static bool ShouldIgnore(PropertyInfo prop, bool isToDto)
    {
        var config = GlobalMapConfig.GetEffective();
        return NameCandidateResolver.ShouldIgnoreGlobal(
            prop.Name,
            prop.PropertyType.FullName,
            isToDto,
            config.IgnoreTypeFullNames,
            config.IgnoreNames,
            config.IgnoreNamePrefixes,
            config.IgnoreNameSuffixes,
            config.IgnoreTypeFullNamesToEntity,
            config.IgnoreNamesToEntity,
            config.IgnoreNamePrefixesToEntity,
            config.IgnoreNameSuffixesToEntity);
    }
}
