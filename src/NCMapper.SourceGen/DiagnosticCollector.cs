using System.Collections.Generic;

namespace NCMapper.SourceGen;

/// <summary>
/// per-run 诊断收集器 — 生成期追加诊断的临时载体
/// 缓存快照在 RegisterSourceOutput 阶段保持只读；追加诊断按 mapping 分组，
/// 由 EmitDiagnostics 与 transform 期初始诊断合并输出。
/// key 由 MappingInfo 改为 MappingSnapshot（管线缓存值值化）。
/// </summary>
internal sealed class DiagnosticCollector
{
    /// <summary>
    /// per-mapping 诊断载体（值类型重构）
    /// Items 保发射序，Seen 承担去重（DiagnosticSnapshot.GetHashCode 契约已核成立：
    /// 哈希 3 字段 ⊆ Equals 4 字段，Models.Snapshots.cs:1058-1068——IsExplicit 不入哈希为
    /// "子集哈希"合法形态，与 形态同族）。
    /// </summary>
    private sealed class MappingEntry
    {
        public readonly List<DiagnosticSnapshot> Items = new();
        public readonly HashSet<DiagnosticSnapshot> Seen = new();
    }

    /// <summary>
    /// 按映射快照分组的诊断列表（key 仅作标识，不写入）
    /// </summary>
    private readonly Dictionary<MappingSnapshot, MappingEntry> _byMapping = new();

    /// <summary>
    /// 追加一条诊断
    /// </summary>
    /// <param name="mapping">映射快照</param>
    /// <param name="diag">诊断快照</param>
    public void Add(MappingSnapshot mapping, DiagnosticSnapshot diag)
    {
        if (!_byMapping.TryGetValue(mapping, out var entry))
            _byMapping[mapping] = entry = new MappingEntry();
        entry.Items.Add(diag);
    }

    /// <summary>
    /// 批量追加诊断
    /// </summary>
    /// <param name="mapping">映射快照</param>
    /// <param name="diags">诊断快照序列</param>
    public void AddRange(MappingSnapshot mapping, IEnumerable<DiagnosticSnapshot> diags)
    {
        foreach (var diag in diags)
            Add(mapping, diag);
    }

    /// <summary>
    /// 追加诊断（按 (mapping, 诊断值) 去重）：
    /// GeneratePropertyAssignments 被多路径复用（更新路径/构造器回退路径均 skipInitOnly: true），
    /// 同一 (mapping, 属性) 只发射一次；DiagnosticSnapshot.Equals 为值语义（描述符 + Args 序列 + 位置）。
    /// <para>原线性 existing.Equals(diag) 全列表扫描 O(n²)
    /// → HashSet.Add O(1) 均摊（Seen 按 mapping 分组承载，跨 mapping 不去重——与原 per-list
    /// 语义逐位一致）；Items 追加序 = 原发射序，GetAppended 消费序零变化。</para>
    /// </summary>
    /// <param name="mapping">映射快照</param>
    /// <param name="diag">诊断快照</param>
    public void AddDistinct(MappingSnapshot mapping, DiagnosticSnapshot diag)
    {
        if (!_byMapping.TryGetValue(mapping, out var entry))
            _byMapping[mapping] = entry = new MappingEntry();
        if (!entry.Seen.Add(diag)) return;
        entry.Items.Add(diag);
    }

    /// <summary>
    /// 供 EmitDiagnostics 消费：按 mapping 取出追加诊断（不存在返回空列表）
    /// </summary>
    /// <param name="mapping">映射快照</param>
    /// <returns>该映射的追加诊断列表（不存在时为空列表）</returns>
    public IReadOnlyList<DiagnosticSnapshot> GetAppended(MappingSnapshot mapping)
        => _byMapping.TryGetValue(mapping, out var entry) ? entry.Items : System.Array.Empty<DiagnosticSnapshot>();
}
