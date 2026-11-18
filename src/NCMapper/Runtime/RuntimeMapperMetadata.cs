// 快照运行时元数据承载类型
// 运行时映射元数据——MappingSnapshot 消费子集（单 (S,T) 对）。
using System;

namespace NCMapper.Runtime;

/// <summary>
/// 运行时映射元数据 —— 单个 (源类型, 目标类型) 映射对的运行时形态
/// </summary>
public sealed class RuntimeMapperMetadata
{
    /// <summary>源类型（Entity）引用（恒可达——non-public 门保证 public）</summary>
    public Type SourceType { get; init; } = null!;

    /// <summary>目标类型（DTO）引用</summary>
    public Type TargetType { get; init; } = null!;

    /// <summary>源类型显示名</summary>
    public string SourceTypeFqn { get; init; } = "";

    /// <summary>目标类型显示名</summary>
    public string TargetTypeFqn { get; init; } = "";

    /// <summary>声明通道</summary>
    public RuntimeMappingChannel Channel { get; init; }

    /// <summary>
    /// 对级不自动包含（禁用该 (S,T) 对的自动匹配/自动包含，含自动扁平化）——
    /// Fluent <c>For&lt;S,T&gt;().NoAutoMatch()</c> 与特性 <c>[MapNoAutoMatch]</c> 双通道填充同一字段
    ///（注释口径订正④：[MapFrom] 通道默认 false、标注 [MapNoAutoMatch] 时 true；
    /// 反向快照经 ReversePlanner 透传带位）。消费入口 <c>RuntimeSnapshotStore.IsNoAutoMatch</c>。
    /// </summary>
    public bool NoAutoInclude { get; init; }

    /// <summary>Fluent 正向方向标记（[MapFrom] 通道恒 false）</summary>
    public bool IsFluentForward { get; init; }

    /// <summary>Fluent AfterMapping 方法引用（"{ContainingType}.{Method}"；null=无）</summary>
    public string? AfterMappingMethodRef { get; init; }

    /// <summary>
    /// Fluent/特性侧 BeforeMapping 方法引用（
    /// "{ContainingType}.{Method}"；null=无）。[BeforeMapping] 特性侧与 Fluent 终端
    /// 双侧填充同一字段（双通道等价）；[MapFrom] 通道无声明对恒 null。
    /// </summary>
    public string? BeforeMappingMethodRef { get; init; }

    /// <summary>
    /// 树构建配置—— null=未启用树语义（opt-in）
    /// <para>Fluent .Tree() 终端物化承载；[MapFrom] 通道经 [MapTree] 特性反射读取（消费端注册表优先+反射回退）。
    /// 消费端（TreeBuilder/IsTreeChildrenSlot/EFCore 树挂载位）对 null 不得取默认三元组。</para>
    /// </summary>
    public RuntimeTreeConfig? TreeConfig { get; init; }

    /// <summary>
    /// 属性级 NoAutoInclude 目标属性名集
    /// Fluent .NoAutoInclude(dest) 声明的属性名；空 = 无声明。
    /// <para><b>对级承载</b>：收集器消费面为全部可写属性，与 <see cref="Properties"/> 存在性无关
    ///（无 pm 属性的声明须生效——pm 级 <see cref="RuntimePropertyMapping.IsPropertyNoAutoInclude"/>
    /// 保留特性侧 [MapNoAutoInclude] 发射承载，两承载 OR 语义）。[MapFrom] 通道恒空。</para>
    /// </summary>
    public string[] PropertyNoAutoIncludeTargets { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 含 Compose 多源声明——Fluent FromSource 声明的属性不进
    /// <see cref="Properties"/>（明细由 <see cref="ComposeMappings"/> 承载），
    /// 降级判定据此判不可翻译。
    /// [MapFrom] 通道恒 false（特性 Compose 走 ComposeProjectionKernel 特性解析，合法投影）。
    /// </summary>
    /// <remarks>
    /// 布尔（声明计数替身）与 <see cref="ComposeMappings"/> 数组（产出计数）可能不齐
    ///（F29 fail-fast 等形态——"声明有、产出无"）——发射以编译期 m.ComposeMappings 为准
    ///（承载面说明；H3 边界归类见）。
    /// </remarks>
    public bool HasComposeMappings { get; init; }

    /// <summary>
    /// Compose 跨表映射明细（双通道统一发射：
    /// 特性侧 [MapProperty(FromSource)] + Fluent FromSource 同载）。
    /// 空 = 无 Compose 声明。发射条件门：无声明不落产物行（F1 字节门精神——纯无 Compose
    /// 声明 pair 产物零变化）。消费端：降级分类五类扫描/ Compose 内核跨表分流/
    /// 投影主链跨表单载按 TargetPropertyName 索引消费。
    /// </summary>
    public RuntimeComposeMapping[] ComposeMappings { get; init; } = Array.Empty<RuntimeComposeMapping>();

    /// <summary>
    /// 含多态子类型分派声明
    /// 基类对带 MapSubclass 登记（Fluent `.MapSubclass&lt;...&gt;()` / 特性侧 [MapSubclass] 同承载）。
    /// <para>消费方：投影降级判定（<c>ClassifyUntranslatableDeclaration</c> → "SubclassDispatch"
    /// 恒降级——isinst 分派链不可投影）+ MapValidator F50②③ 静态 advisory。无声明恒 false。</para>
    /// </summary>
    public bool HasSubclassDispatch { get; init; }

    /// <summary>属性映射对（正向映射主体）</summary>
    public RuntimePropertyMapping[] Properties { get; init; } = Array.Empty<RuntimePropertyMapping>();

    /// <summary>
    /// 全部可匹配目标属性信号槽 —— <c>[MapIgnore(Both)]</c> 差集重建输入
    /// <para>Both 忽略属性在发射清单中零痕迹，消费端经
    /// 「运行时属性全集 - 本清单 - 可写性门」差集重建。</para>
    /// </summary>
    public RuntimeTargetPropertySlot[] AllTargetSlots { get; init; } = Array.Empty<RuntimeTargetPropertySlot>();
}
