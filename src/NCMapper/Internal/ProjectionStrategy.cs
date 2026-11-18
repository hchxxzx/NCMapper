// : ORM 投影内核统一 —— 投影策略（两库差异点收敛为显式声明）
using System;

namespace NCMapper.Internal
{
    /// <summary>
    /// 投影构建策略 —— 两库差异点收敛为显式声明（编译期防漏，避免统一内核中错配）
    /// <para>EFCore: MaxNestingDepth=8, WarningDepth=5, EnableCollectionProjection=true</para>
    /// <para>FreeSql: MaxNestingDepth=3, WarningDepth=2, EnableCollectionProjection=false</para>
    /// <para>作为 s_cache 缓存键的一部分，保证两库同 (Source,Target) 互不串用</para>
    /// </summary>
    internal sealed record ProjectionStrategy(
        int MaxNestingDepth,
        int WarningDepth,
        bool EnableCollectionProjection,
        string TracePrefix);
}
