using NCMapper;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// 场景 105 — 条件守卫 MapWhen（how-to/fluent/terminals.md「MapWhen」节）：
/// 命名方法组 / 内联转录 / 整源 / 同锚点组合 + 跳过值语义（新建 default、_Update 保留既有值）+ 特性侧 [MapWhen]。
/// </summary>
public static class MapWhenScenarios
{
    /// <summary>
    /// Fluent 侧映射（四守卫形态同链）。
    /// </summary>
    /// <param name="src">源条目实体</param>
    /// <returns>映射后的 DTO（守卫真才映射）</returns>
    public static MwItemDto FluentSide(MwItemEntity src)
        => Mapper.Map<MwItemEntity, MwItemDto>(src)!;

    /// <summary>
    /// Fluent 侧 _Update 形态——条件假 → 保留 dst 既有值。
    /// </summary>
    /// <param name="src">源条目实体</param>
    /// <param name="existing">既有 DTO（含旧值）</param>
    public static void FluentUpdate(MwItemEntity src, MwItemDto existing)
        => Mapper.Map<MwItemEntity, MwItemDto>(src, existing);

    /// <summary>
    /// 特性侧映射——[MapWhen(nameof(HasContent))] 属性级守卫。
    /// </summary>
    /// <param name="src">源条目实体</param>
    /// <returns>映射后的 DTO（Name 受属性级守卫）</returns>
    public static MwAttrItemDto AttributeSide(MwItemEntity src)
        => Mapper.Map<MwItemEntity, MwAttrItemDto>(src)!;

    /// <summary>
    /// 场景调度 — 顺序执行全部子场景并输出。
    /// </summary>
    #region MapWhen_Run
    public static void Run()
    {
        // 1. Fluent all-true: all four guards hit
        var item = new MwItemEntity { Id = 1, Name = "alpha", Score = 95, Kind = "A", BoundedSource = 7 };
        var dto = FluentSide(item);
        OutputHelper.Dump("105-mapwhen-all-true", item, dto);
        if (dto.Name != "alpha" || dto.Score != 95 || dto.KindTag != "A" || dto.BoundedScore != 7)
            OutputHelper.Error($"105-mapwhen-all-true: all-true scenario should map everything (Name={dto.Name}, Score={dto.Score}, KindTag={dto.KindTag}, BoundedScore={dto.BoundedScore})");

        // 2. Fluent all-false (new instance): false condition -- keep default / initializer values
        var item2 = new MwItemEntity { Id = 2, Name = "", Score = 10, Kind = "B", BoundedSource = 0 };
        var dto2 = FluentSide(item2);
        OutputHelper.Dump("105-mapwhen-all-false", item2, dto2);
        if (dto2.Name != "" || dto2.Score != 0 || dto2.KindTag != "" || dto2.BoundedScore != 0)
            OutputHelper.Error($"105-mapwhen-all-false: false condition should keep defaults (Name=\"{dto2.Name}\", Score={dto2.Score}, KindTag=\"{dto2.KindTag}\", BoundedScore={dto2.BoundedScore})");
        if (dto2.Id != 2)
            OutputHelper.Error($"105-mapwhen-all-false: unguarded property Id should map as usual (actual: {dto2.Id})");

        // 3. Fluent _Update: false condition -- keep dst existing values; true condition -- update
        var existing = new MwItemDto { Id = 3, Name = "keep-me", Score = 77, KindTag = "keep-tag", BoundedScore = 55 };
        var item3 = new MwItemEntity { Id = 4, Name = "", Score = 95, Kind = "B", BoundedSource = 3 };
        FluentUpdate(item3, existing);
        OutputHelper.Dump("105-mapwhen-update-keep", item3, existing);
        if (existing.Name != "keep-me")
            OutputHelper.Error($"105-mapwhen-update-keep: Name should keep existing value keep-me when condition is false (actual: {existing.Name})");
        if (existing.KindTag != "keep-tag")
            OutputHelper.Error($"105-mapwhen-update-keep: KindTag should keep existing value keep-tag when condition is false (actual: {existing.KindTag})");
        if (existing.Score != 95 || existing.BoundedScore != 3)
            OutputHelper.Error($"105-mapwhen-update-keep: true-condition properties should update (Score={existing.Score}, BoundedScore={existing.BoundedScore})");

        // 4. Attribute side [MapWhen]: property-level guard (predicate method declared on the DTO itself)
        var attrDto = AttributeSide(item);
        OutputHelper.Dump("105-mapwhen-attr", item, attrDto);
        if (attrDto.Name != "alpha")
            OutputHelper.Error($"105-mapwhen-attr: true predicate should map Name (actual: {attrDto.Name})");
        var attrDto2 = AttributeSide(item2);
        OutputHelper.Dump("105-mapwhen-attr-false", item2, attrDto2);
        if (attrDto2.Name != "")
            OutputHelper.Error($"105-mapwhen-attr-false: false predicate should keep default (actual: \"{attrDto2.Name}\")");
    }
    #endregion
}
