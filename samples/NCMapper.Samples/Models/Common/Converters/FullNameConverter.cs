using NCMapper.Samples.Models.Entities;

namespace NCMapper.Samples.Models.Converters;

/// <summary>
/// 全名组合转换器 — 演示 [Convert] 多字段组合场景（T2 模板 M(TSource)）。
/// <para>用法：[Convert(typeof(FullNameConverter), nameof(FullNameConverter.Convert))]</para>
/// <para>注意：DTO 属性无匹配源属性时（如 FullName），T2 模板直接以 source 参数读取源对象所有字段。</para>
/// </summary>
public static class FullNameConverter
{
    /// <summary>
    /// 组合全名。
    /// </summary>
    /// <param name="source">源实体对象（通过 source 读取多个字段实现多字段组合）</param>
    /// <returns>组合后的全名</returns>
    public static string Convert(UserEntity source)
    {
        // 纯计算属性：直接从 source 读取多个字段组合
        return $"{source.Name} ({source.Email})";
    }
}
