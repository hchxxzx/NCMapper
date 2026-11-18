// 快照运行时元数据承载类型
// 统一查询视图（建议 1）—— TryGetMappingMeta 的返回值，
// 封装"注册表命中/反射回退"统一形态，消费端读字段不分支。
namespace NCMapper.Runtime;

/// <summary>
/// 统一查询视图 —— 映射 DTO 判定输入的统一消费形态
/// </summary>
/// <remarks>
/// <para>由 <see cref="RuntimeSnapshotStore.TryGetMappingMeta"/> 返回，
/// 消费端读字段不分支：<see cref="IsDto"/> 等价重建 <c>IsDefined(MapFromAttribute)</c>、
/// <see cref="PrimarySource"/> 等价重建 <c>GetCustomAttribute&lt;MapFromAttribute&gt;().FromSource</c>。</para>
/// <para><see cref="MapFromAttribute"/> 在注册表命中（[MapFrom] 通道）时为发射时物化的特性实例、
/// Fluent 通道为 null；反射回退时为真实反射实例——消费端无需区分来源。</para>
/// </remarks>
public sealed record MappingMetaView
{
    /// <summary>是否为映射 DTO（注册表命中或反射 [MapFrom] 命中）</summary>
    public bool IsDto { get; init; }

    /// <summary>主源类型（注册表主源或反射 FromSource；非 DTO 时 null）</summary>
    public System.Type? PrimarySource { get; init; }

    /// <summary>[MapFrom] 特性实例（注册表命中时为发射物化实例或 null（Fluent）；反射回退时为真实实例）</summary>
    public Attributes.MapFromAttribute? MapFromAttribute { get; init; }

    /// <summary>注册表是否命中（on 热路径 true；off 反射回退 false）</summary>
    public bool IsRegistered { get; init; }

    /// <summary>命中的元数据条目（注册表命中时非 null；反射回退时 null——消费端按需读属性明细）</summary>
    public RuntimeMapperMetadata? Metadata { get; init; }
}
