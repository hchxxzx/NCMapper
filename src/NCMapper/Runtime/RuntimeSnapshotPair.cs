// 快照运行时元数据承载类型
// 发现机制候选 a：泛型键控静态类（注册面，仿 Mapper.GeneratedCache）——JIT 键控零查找。
using System;

namespace NCMapper.Runtime;

/// <summary>
/// 泛型键控静态类 —— 编译期已知 (源类型, 目标类型) 对的运行时元数据注册面
/// </summary>
/// <typeparam name="TSource">源类型（Entity）</typeparam>
/// <typeparam name="TTarget">目标类型（DTO）</typeparam>
/// <remarks>
/// <para>仿 <c>Mapper.GeneratedCache&lt;TSource,TTarget&gt;</c> 泛型键控先例——JIT 键控零查找。</para>
/// <para>注册由生成代码 [ModuleInitializer] 执行（与 <c>RuntimeSnapshotStore.Register</c> 双写、
/// 同一元数据实例）；仅适合编译期已知 (S,T) 的调用点，运行时仅持有 <see cref="Type"/> 实例
/// （ORM 嵌套/集合元素判定）时经 <see cref="RuntimeSnapshotStore"/> 查询。</para>
/// </remarks>
public static class RuntimeSnapshotPair<TSource, TTarget>
    where TSource : class
    where TTarget : class
{
    /// <summary>该 (源, 目标) 对的运行时元数据（volatile——与 GeneratedCache 注册形态同构，多线程注册后立即可见）</summary>
    public static volatile RuntimeMapperMetadata? Metadata;
}
