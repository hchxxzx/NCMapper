namespace NCMapper;

/// <summary>
/// 投影策略接口 — 特性实现此接口声明自己在 Projection 路径下是否需要降级为二步映射
/// <para>NCMapper 命名空间内的特性如果未实现此接口，将触发默认降级（安全网）</para>
/// <para>外部特性（DataAnnotations、System.Text.Json 等）不受此接口影响</para>
/// </summary>
public interface IProjectionPolicy
{
    /// <summary>
    /// 该特性在 Projection 路径下是否需要降级为二步映射（先 SQL 查实体再 MapList）
    /// <para>true = 需要降级（如 Convert、Mask 等 CLR 委托路径特性）</para>
    /// <para>false = 不需要降级（如 MapFrom、MapIgnore 等纯元数据特性）</para>
    /// </summary>
    bool RequiresDowngrade { get; }
}
