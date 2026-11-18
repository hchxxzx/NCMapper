namespace NCMapper.Attributes;

/// <summary>
/// 树形结构配置特性 — 标注在 DTO 类上，指定构建树形结构时使用的 Id、ParentId 和 Children 属性名
/// <para>未标注时默认使用 "Id"、"ParentId" 和 "Children"</para>
/// <para>树契约：Children 自引用集合属性必须存在（精确名查找，
/// 消除多自引用集合的非确定性）；缺失或类型不符抛 MapConfigurationException</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MapTreeAttribute : Attribute
{
    /// <summary>
    /// 获取主键属性名
    /// </summary>
    public string IdProperty { get; }

    /// <summary>
    /// 获取父键属性名
    /// </summary>
    public string ParentIdProperty { get; }

    /// <summary>
    /// 获取子节点集合属性名
    /// <para>DTO 须有该名称的可写自引用集合属性（如 List&lt;T&gt; Children），缺失或类型不符抛异常</para>
    /// </summary>
    public string ChildrenProperty { get; }

    /// <summary>
    /// 初始化树形结构配置
    /// </summary>
    /// <param name="idProperty">主键属性名，默认 "Id"</param>
    /// <param name="parentIdProperty">父键属性名，默认 "ParentId"</param>
    /// <param name="childrenProperty">子节点集合属性名，默认 "Children"</param>
    public MapTreeAttribute(string idProperty = "Id", string parentIdProperty = "ParentId",
        string childrenProperty = "Children")
    {
        IdProperty = idProperty;
        ParentIdProperty = parentIdProperty;
        ChildrenProperty = childrenProperty;
    }
}
