using NCMapper;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// 场景 104 — 生命周期钩子 BeforeMapping/AfterMapping（how-to/fluent/hooks.md「BeforeMapping/AfterMapping」节）：
/// 双侧（Fluent 链式 + 类级特性）+ 执行相位 Before→赋值→After + 跨对签名过滤（D-d）。
/// </summary>
public static class FluentHooksScenarios
{
    /// <summary>
    /// Fluent 侧新建映射——双钩子连写（Before 写派生字段 / After 计算派生字段）。
    /// </summary>
    /// <param name="src">源订单实体</param>
    /// <returns>映射后的 DTO（含钩子派生字段）</returns>
    public static HookOrderDto FluentNew(HookOrderEntity src)
        => Mapper.Map<HookOrderEntity, HookOrderDto>(src)!;

    /// <summary>
    /// Fluent 侧 _Update 形态——复用既有实例（Before 见到 dst 旧值）。
    /// </summary>
    /// <param name="src">源订单实体</param>
    /// <param name="existing">既有 DTO（含旧值）</param>
    public static void FluentUpdate(HookOrderEntity src, HookOrderDto existing)
        => Mapper.Map<HookOrderEntity, HookOrderDto>(src, existing);

    /// <summary>
    /// 特性侧映射——[BeforeMapping]/[AfterMapping] 类级特性（双参宿主形态）。
    /// </summary>
    /// <param name="src">源订单实体</param>
    /// <returns>映射后的 DTO（含钩子派生字段）</returns>
    public static AttrHookDto AttributeSide(HookOrderEntity src)
        => Mapper.Map<HookOrderEntity, AttrHookDto>(src)!;

    /// <summary>
    /// 跨对签名过滤（D-d）——HookNoteSource→AttrHookDto 纯 Fluent 对：
    /// 特性侧钩子签名 (HookOrderEntity, AttrHookDto) 与本源不匹配 → 不发射、不发诊断。
    /// </summary>
    /// <param name="src">跨源实体</param>
    /// <returns>映射后的 DTO（钩子派生字段保持默认）</returns>
    public static AttrHookDto CrossPairFilter(HookNoteSource src)
        => Mapper.Map<HookNoteSource, AttrHookDto>(src)!;

    /// <summary>
    /// 场景调度 — 顺序执行全部子场景并输出。
    /// </summary>
    #region FluentHooks_Run
    public static void Run()
    {
        // 1. Fluent side new instance: Before -> assign -> After phases + derived fields + mapped properties kept as-is
        HooksLog.Reset();
        var order = new HookOrderEntity { Id = 1, Title = " hello world ", Qty = 3, UnitPrice = 500.0 };
        var dto = FluentNew(order);
        OutputHelper.Dump("104-fluent-hooks", order, dto);
        if (dto.NormalizedTitle != "HELLO WORLD")
            OutputHelper.Error($"104-fluent-hooks: NormalizedTitle BeforeMapping pre-processing should be HELLO WORLD (actual: {dto.NormalizedTitle})");
        if (dto.Total != "1500.00")
            OutputHelper.Error($"104-fluent-hooks: Total AfterMapping computation should be 1500.00 (actual: {dto.Total})");
        if (dto.Title != " hello world ")
            OutputHelper.Error($"104-fluent-hooks: Title should be mapped as-is (hooks only write derived fields, actual: {dto.Title})");
        if (HooksLog.Steps.Count != 2 || HooksLog.Steps[0] != "Before" || HooksLog.Steps[1] != "After")
            OutputHelper.Error($"104-fluent-hooks: execution phases should be Before→After (actual: {string.Join("→", HooksLog.Steps)})");
        if (HooksLog.BeforeSeenTitle != "")
            OutputHelper.Error($"104-fluent-hooks: in new-instance mode dst.Title should be the initializer empty string at Before time (actual: \"{HooksLog.BeforeSeenTitle}\")");

        // 2. Fluent side _Update: Before sees dst old value (update preserves existing values)
        HooksLog.Reset();
        var existing = new HookOrderDto { Id = 9, Title = "old-title", NormalizedTitle = "STALE", Total = "0.00" };
        var order2 = new HookOrderEntity { Id = 2, Title = "  second  ", Qty = 2, UnitPrice = 250.0 };
        FluentUpdate(order2, existing);
        OutputHelper.Dump("104-fluent-hooks-update", order2, existing);
        if (HooksLog.BeforeSeenTitle != "old-title")
            OutputHelper.Error($"104-fluent-hooks-update: _Update mode Before should see dst old value old-title (actual: \"{HooksLog.BeforeSeenTitle}\")");
        if (existing.NormalizedTitle != "SECOND")
            OutputHelper.Error($"104-fluent-hooks-update: NormalizedTitle should be recomputed to SECOND (actual: {existing.NormalizedTitle})");
        if (existing.Total != "500.00")
            OutputHelper.Error($"104-fluent-hooks-update: Total should be recomputed to 500.00 (actual: {existing.Total})");

        // 3. Attribute side: class-level [BeforeMapping]/[AfterMapping] (two-parameter host form) -- output isomorphic to the Fluent side
        HooksLog.Reset();
        var attrDto = AttributeSide(order);
        OutputHelper.Dump("104-attr-hooks", order, attrDto);
        if (attrDto.NormalizedTitle != "HELLO WORLD" || attrDto.Total != "1500.00")
            OutputHelper.Error($"104-attr-hooks: attribute-side hook output mismatch (NormalizedTitle={attrDto.NormalizedTitle}, Total={attrDto.Total})");
        if (HooksLog.Steps.Count != 2 || HooksLog.Steps[0] != "Before" || HooksLog.Steps[1] != "After")
            OutputHelper.Error($"104-attr-hooks: execution phases should be Before→After (actual: {string.Join("→", HooksLog.Steps)})");

        // 4. Cross-pair signature filtering (D-d): pairs with mismatched signatures emit no hooks and raise no diagnostics
        HooksLog.Reset();
        var crossDto = CrossPairFilter(new HookNoteSource { Note = "note-x" });
        OutputHelper.Dump("104-cross-pair-filter", "HookNoteSource→AttrHookDto (hook signature mismatch)", crossDto);
        if (HooksLog.Steps.Count != 0)
            OutputHelper.Error($"104-cross-pair-filter: signature-mismatched pair should not emit hooks (actual: {string.Join("→", HooksLog.Steps)})");
        if (crossDto.NormalizedTitle != "" || crossDto.Total != "")
            OutputHelper.Error($"104-cross-pair-filter: derived fields should stay at defaults (NormalizedTitle={crossDto.NormalizedTitle}, Total={crossDto.Total})");
        if (crossDto.Title != "note-x")
            OutputHelper.Error($"104-cross-pair-filter: cross-pair Map(Title←Note) failed (actual: {crossDto.Title})");
    }
    #endregion
}
