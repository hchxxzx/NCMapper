namespace NCMapper.Attributes;
/// <summary>
/// 忽略方向 — 控制 [MapIgnore] 在哪个映射方向生效
/// </summary>
public enum Direction
{
    /// <summary>
    /// 双向忽略
    /// </summary>
    Both,
    /// <summary>
    /// 仅 DTO 方向忽略
    /// </summary>
    ToDto,
    /// <summary>
    /// 仅实体方向忽略
    /// </summary>
    ToEntity
}

/// <summary>
/// 映射忽略特性 — 标注的属性在指定方向上不参与映射
/// <para>支持按方向控制忽略行为</para>
/// <para>用法：[MapIgnore] 双向忽略；[MapIgnore(Direction.ToDto)] 仅读取时忽略</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class MapIgnoreAttribute : Attribute, IProjectionPolicy
{
    /// <inheritdoc/>
    public bool RequiresDowngrade => false;
    /// <summary>
    /// 忽略方向（默认 Both）
    /// </summary>
    public Direction Direction { get; set; } = Direction.Both;

    /// <summary>
    /// 创建双向忽略的特性实例（默认方向）
    /// </summary>
    public MapIgnoreAttribute() { }

    /// <summary>
    /// 创建指定方向的忽略特性
    /// </summary>
    /// <param name="direction">忽略方向</param>
    public MapIgnoreAttribute(Direction direction)
    {
        Direction = direction;
    }
}
