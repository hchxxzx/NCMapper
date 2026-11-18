using System;
using System.Diagnostics.CodeAnalysis;

namespace NCMapper.Runtime;

/// <summary>
/// 运行时锚门 — **两分谓词**单点 helper（T5 双锚判定式 +
/// 特性锚谓词；谓词定案 / 20261010-）。
/// <para><b>谓词两分口径（强制执行）</b>：
/// ① <see cref="HasAnchor"/>（**双锚**：type.IsDefined([MapFrom]) ∨ RuntimeSnapshotStore 注册表命中）
/// = **映射资格判定**（是否映射 DTO / 是否入治理域）——复用 <see cref="RuntimeSnapshotStore.TryGetMappingMeta"/>
/// 「注册表优先 + 反射回退」双腿；禁止用于"是否读取属性级标注"的准入判定。
/// ② <see cref="HasAttributeAnchor"/>（**特性锚**：仅 [MapFrom] 反射腿，不含注册表腿）
/// = **"读特性"准入判定**（属性级映射配置标注能否被运行时消费）—— 定案。</para>
/// <para><b>消费口径（本册后终态）</b>：属性级映射配置标注（[MapProperty]/[Convert]/[ConvertBack]/
/// [Mask]/[MapIgnore]/[MapWhen]/[MapNoAutoInclude]）的消费准入 = **特性锚**：
/// 特性对（有 [MapFrom]）照旧读；**Fluent 对（仅注册表命中）不读**（按快照明细执行——与生成端
/// "不读取属性级特性"同侧，消除 007 实证的"特性压过明细"分叉）；无锚不消费不变；
/// 混合形态（[MapFrom] + Fluent 注册并存）按特性锚读（判定路径改为反射腿）。
/// [MapTree] 不在本清单——类级树构建定义、非映射对配置。
/// 生成端同口径：无锚类属性级标注由 NCMAP019（Error）拦截，双引擎语义对齐；
/// Fluent 声明目标类由生成端 fluentTargetClassNames 豁免（NCMapperSourceGenerator.cs:98-136），与本门注册表腿同源（双锚判定式的"注册表命中"腿）。</para>
/// <para><b>反射腿正/负结果缓存</b>：
/// 两谓词的反射腿统一经 <c>RuntimeSnapshotStore.s_mapFromProbe</c> CWT 弱键缓存
/// （<see cref="HasAnchor"/> 走 <c>TryGetMappingMeta</c> 反射回退腿、<see cref="HasAttributeAnchor"/>
/// 走 <c>RuntimeSnapshotStore.IsMapFromDeclared</c>——**同一缓存、同一反射调用点，零新增反射路径**）
/// ——同一 Type 至多一次 <c>GetCustomAttribute&lt;MapFromAttribute&gt;</c> 反射；命中负缓存
/// （未注册且无 [MapFrom]）时零堆分配零锁，命中注册表腿/正缓存腿时零锁（各构造一次视图）。
/// <b>失效口径</b>：缓存随 <c>Mapper.ClearCache()</c> 全链清理（登记名
/// <c>runtime-snapshot-probe</c>，CWT Clear()）；[MapFrom] 特性运行期不可新增（语言级保证）；
/// 故无热失效场景。</para>
/// </summary>
internal static class RuntimeAnchorGate
{
    /// <summary>
    /// 判定类型是否具备**映射资格锚**（[MapFrom] 特性声明 ∨ 运行时注册表命中）——双锚语义。
    /// <para>用途边界（本册后）：映射资格/入域判定（如投影锚判定、注册合并、资格门）。
    /// **"读特性"准入一律改用 <see cref="HasAttributeAnchor"/>**——本谓词的注册表腿会把
    /// Fluent 对放行到属性级标注消费路径（007 缺陷根因）。</para>
    /// <para>动态类型（ALC 场景）同样适用——TryGetMappingMeta 反射腿直接生效。</para>
    /// <para>不使用 DynamicallyAccessedMembers 注解
    /// 该注解会向全部调用链传播 trimming 分析负担（实测 IL2026/IL3050 各百级），
    /// 与本门 best-effort 语义矛盾（trim 后退化注册表腿，Justification 同下）。</para>
    /// </summary>
    /// <param name="type">待判定类型（属性 DeclaringType / 映射对任一侧）</param>
    /// <returns>true = 有锚（映射资格成立）；false = 无锚（不消费、不入域）</returns>
    [SuppressMessage("Trimming", "IL2026",
        Justification = "Anchor gate is best-effort by design: trimmed [MapFrom] degrades to registry leg; unregistered+trimmed types are not valid mapping DTOs (generator would have blocked them with NCMAP019).")]
    [SuppressMessage("Trimming", "IL3050",
        Justification = "Same best-effort semantics as IL2026 justification.")]
    internal static bool HasAnchor(Type type)
        => RuntimeSnapshotStore.TryGetMappingMeta(type, out _);

    /// <summary>
    /// 判定类型是否具备**特性锚**（仅 <c>[MapFrom]</c> 声明——反射腿，不含 Fluent 注册表腿）——
    /// **"读特性"准入专用**。
    /// <para>与 <see cref="HasAnchor"/>（双锚）职责分离：映射资格判定继续用双锚，属性级标注消费准入
    /// 一律用本谓词。缓存口径 = 复用 <c>RuntimeSnapshotStore.s_mapFromProbe</c> CWT 正/负缓存家族
    /// （零新增反射路径；禁止裸<c>IsDefined</c>/<c>GetCustomAttribute</c>无缓存调用——C-3 口径）。</para>
    /// <para>Fluent 对的净变化 = 首次一次负探测（缓存后 O(1) 命中）；混合形态（[MapFrom] + Fluent
    /// 注册并存）判定结果与旧口径相同、判定路径改为反射腿（第 4 行）。</para>
    /// </summary>
    /// <param name="type">待判定类型（属性 DeclaringType）</param>
    /// <returns>true = 具特性锚（属性级标注可消费）；false = 无特性锚（含 Fluent 对与无锚形态）</returns>
    [SuppressMessage("Trimming", "IL2026",
        Justification = "Attribute-anchor probe is best-effort by design: trimmed [MapFrom] yields false, which matches the 'no anchor → do not consume' contract (generator blocks unanchored annotated DTOs with NCMAP019).")]
    [SuppressMessage("Trimming", "IL3050",
        Justification = "Same best-effort semantics as IL2026 justification.")]
    internal static bool HasAttributeAnchor(Type type)
        => RuntimeSnapshotStore.IsMapFromDeclared(type);
}
