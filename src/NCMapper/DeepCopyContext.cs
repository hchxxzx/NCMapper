using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace NCMapper;

/// <summary>
/// 深拷贝上下文 — 承载递归调用间共享的循环引用检测缓存和递归深度计数
/// <para>方向 I 优化：替代 [ThreadStatic] 字段，改为方法参数传递，避免 net9/10 ThreadStatic
/// ABI 从 <c>GETSHARED_*THREADSTATIC_BASE_NOCTOR_OPTIMIZED</c> 改为
/// <c>GETDYNAMIC_*THREADSTATIC_BASE_NOCTOR_OPTIMIZED2</c> 导致的性能回退。</para>
/// <para>Expression 路径每次递归 6-8 次 ThreadStatic 访问（s_cycleCache + s_depth），
/// net9/10 ABI 变化后每次访问成本上升，累积导致 ~427ns/ops 性能差距。</para>
/// <para>顶层入口（Mapper.DeepCopy / MapperBase.Map 回退路径）经 ThreadStatic ??= 复用同一 ctx
/// （仅首次分配，~32B），递归调用经 ctx 参数传递；顶层退出（ctx.Depth 归 0）时 Clear CycleCache
/// （保留桶容量）。Provider 独立入口（DeepCopyProviderBase/FastDeepCopyProvider）为每调用新建 ctx，
/// 退出后随 GC 回收。</para>
/// <para><b>Fast 路径环形缓冲</b>：<see cref="FastDeepCopyProvider"/> 路径专用 N 槽（N=8）
/// 环形缓冲，用于检测环路径节点数 ≤ 8 的循环引用。三字段（<see cref="FastSources"/>/
/// <see cref="FastTargets"/>/<see cref="FastSlot"/>）为 internal，仅同程序集 Provider 访问；
/// 采用 F2 退出回滚策略（退化为祖先链栈），无循环 DAG 图行为与现状完全一致。</para>
/// <para><b>可见性说明</b>：本类型为 public 是因为源生成器生成的代码会被发射到用户程序集中，
/// 需要引用 <see cref="DeepCopyContext"/> 类型作为方法参数。本类不作为通用公开 API，
/// 普通用户不应直接构造或访问本类，应通过 <see cref="Mapper.DeepCopy{T}(T)"/> 入口使用。</para>
/// </summary>
public sealed class DeepCopyContext
{
    /// <summary>
    /// 循环引用检测缓存 — 源对象→拷贝对象映射（强引用，顶层退出后随 ctx 一起 GC）
    /// <para>Provider 路径（Expression）和 Generated 路径共用此字段。</para>
    /// <para>使用 <see cref="ReferenceEqualityComparer"/> 比较键（引用语义），与原
    /// ConditionalWeakTable 行为一致。</para>
    /// <para>顶层调用时懒初始化（Depth==0），递归调用复用，无需显式 Clear
    /// （ctx 丢弃后 Dictionary 自动 GC）。</para>
    /// </summary>
    /// <summary>
    /// 循环引用缓存 — 【仅供生成代码 / DeepCopyProvider 内部使用，勿在外部读写】
    /// <para>H-07⑦ 文档化：公开仅因生成代码与 Provider 位于不同程序集需访问；语义见 <see cref="DeepCopyContext"/> 说明。</para>
    /// </summary>
    public Dictionary<object, object>? CycleCache;

    /// <summary>
    /// 当前递归深度 — 0 表示顶层调用
    /// <para>替代 <c>DeepCopyProviderBase.s_depth</c> / <c>FastDeepCopyProvider.s_fastDepth</c> /
    /// <c>Mapper.s_deepCopyDepth</c> 三个 ThreadStatic 计数器。</para>
    /// </summary>
    /// <summary>
    /// 递归深度计数 — 【仅供生成代码 / DeepCopyProvider 内部使用，勿在外部读写】
    /// <para>H-07⑦ 文档化：顶层为 0，递归递增；最外层（归 0）负责清理 CycleCache。</para>
    /// </summary>
    public int Depth;

    // ===== Fast 路径专用环形缓冲（internal，仅同程序集 FastDeepCopyProvider 访问） =====

    /// <summary>
    /// Fast 路径专用：N 槽环形缓冲的源对象数组（懒初始化，顶层 Depth==0 时重置）
    /// <para>F2 退出回滚策略下退化为祖先链栈，仅用于循环检测，无循环场景保持 null 零分配。</para>
    /// </summary>
    internal object?[]? FastSources;

    /// <summary>
    /// Fast 路径专用：N 槽环形缓冲的目标对象数组（懒初始化，顶层 Depth==0 时重置）
    /// <para>与 <see cref="FastSources"/> 一一对应，循环命中时返回对应槽位的占位实例。</para>
    /// </summary>
    internal object?[]? FastTargets;

    /// <summary>
    /// Fast 路径专用：环形缓冲当前写入位置（自增取模，顶层重置为 0）
    /// <para>位运算取模 <c>FastSlot &amp; (FastRingSize - 1)</c> 要求 FastRingSize 为 2 的幂。</para>
    /// </summary>
    internal int FastSlot;

    /// <summary>
    /// Fast 路径环形缓冲槽位数（覆盖环路径节点数 ≤ 8 的循环引用）
    /// <para>必须为 2 的幂以支持位运算取模；调大可覆盖更长环路径，但增加每节点扫描开销。</para>
    /// </summary>
    internal const int FastRingSize = 8;
}
