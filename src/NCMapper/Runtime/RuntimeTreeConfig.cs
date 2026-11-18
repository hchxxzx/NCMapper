// 树构建配置运行时承载
// Fluent .Tree() 终端与 [MapTree] 特性共用的运行时三元组形态。
using System;

namespace NCMapper.Runtime;

/// <summary>
/// 树构建配置运行时承载 —— 树挂载位判定与树构建的三元组（Id/ParentId/Children 属性名）
/// </summary>
/// <remarks>
/// <para>默认三元组与 [MapTree] 特性侧无参默认一致（三处一致断言随 落地）：
/// <see cref="Attributes.MapTreeAttribute"/> 无参默认 "Id"/"ParentId"/"Children"。</para>
/// <para>opt-in 语义：Fluent DTO 未调用 .Tree() 时 <see cref="RuntimeMapperMetadata.TreeConfig"/>=null，
/// 消费端树挂载位判定恒 false——不得对 null 取默认三元组（防"未声明却恰有同名 Children 自引用集合属性"的双挂载缺口）。</para>
/// </remarks>
public sealed class RuntimeTreeConfig
{
    /// <summary>主键属性名（默认 "Id"）</summary>
    public string IdProperty { get; init; } = "Id";

    /// <summary>父键属性名（默认 "ParentId"）</summary>
    public string ParentIdProperty { get; init; } = "ParentId";

    /// <summary>子节点集合属性名（默认 "Children"）</summary>
    public string ChildrenProperty { get; init; } = "Children";
}
