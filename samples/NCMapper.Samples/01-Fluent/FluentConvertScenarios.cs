using System.Globalization;
using NCMapper;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Fluent;

/// <summary>
/// Fluent 转换场景集 — Convert/ConvertBack 四形态、同锚点配对、方向性忽略、
/// 全局忽略豁免门、FromJson 与反向转换（T 侧锚 ConvertBack + action 写回）演示。
/// </summary>
public static class FluentConvertScenarios
{
    /// <summary>
    /// 场景 1 — Convert 四形态（T0 常量/T1 字段值/T2 整源/T3 组合）正向映射。
    /// </summary>
    /// <param name="src">源订单实体</param>
    /// <returns>四形态转换产物 DTO</returns>
    public static CvConvDto ConvertFourForms(CvConvEntity src)
        => Mapper.Map<CvConvEntity, CvConvDto>(src)!;

    /// <summary>
    /// 场景 2 — ConvertBack 四形态反向写回（DTO → Entity，同型属性对）。
    /// </summary>
    /// <param name="dto">源 DTO</param>
    /// <returns>四形态反向写回产物实体</returns>
    public static CvPairEntity ConvertBackFourForms(CvPairDto dto)
        => Mapper.Map<CvPairDto, CvPairEntity>(dto)!;

    /// <summary>
    /// 场景 3 — Ignore 方向重载：ToDto 仅正向忽略 / ToEntity 仅反向忽略（反向更新形态验证跳过）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="existing">反向写回的已有实体（验证 AuditNote 保持）</param>
    /// <returns>正向映射 DTO</returns>
    public static CvDirectionDto IgnoreDirection(CvDirectionEntity src, CvDirectionEntity existing)
    {
        var dto = Mapper.Map<CvDirectionEntity, CvDirectionDto>(src)!;
        Mapper.Map(dto, existing);
        return dto;
    }

    /// <summary>
    /// 场景 4 — OverrideIgnore 豁免门：全局 IgnoreNames（RowVersion）命中仍生成映射；
    /// 对照 RowVersionPlain 无豁免被全局忽略抑制。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>映射 DTO（RowVersion 已豁免写入、RowVersionPlain 保持默认）</returns>
    public static CvDirectionDto OverrideIgnore(CvDirectionEntity src)
        => Mapper.Map<CvDirectionEntity, CvDirectionDto>(src)!;

    /// <summary>
    /// 场景 5 — FromJson：源侧同名 string 经 JsonConverter 反序列化为复杂类型。
    /// </summary>
    /// <param name="src">源实体（Payload 为 JSON 字符串）</param>
    /// <returns>Payload 反序列化后的 DTO</returns>
    public static CvSideDto FromJsonExpand(CvSideEntity src)
        => Mapper.Map<CvSideEntity, CvSideDto>(src)!;

    /// <summary>
    /// 场景 6 — 方向性忽略（T 侧锚 .Member(dest).Ignore()）：Both 双向拦截（正向不映射+反向写回跳过）、
    /// ToDto 仅正向忽略（反向更新形态验证照常写回）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="existing">反向写回的已有实体（验证 SecretCode 保持）</param>
    /// <returns>正向映射 DTO</returns>
    public static CvSideDto DirectionalIgnoreProbe(CvSideEntity src, CvSideEntity existing)
    {
        var dto = Mapper.Map<CvSideEntity, CvSideDto>(src)!;
        Mapper.Map(dto, existing);
        return dto;
    }

    /// <summary>
    /// 场景 7 — ConvertBack 反向解析（T 侧锚：entity.CreatedAt ← Parse(dto.CreatedAt)）+
    /// SyncStamp 时点性写回经 action 重载调用点级显式化（e.SyncStamp ← BuildSyncStamp(dto)）。
    /// </summary>
    /// <param name="dto">源 DTO</param>
    /// <returns>反向写回产物实体</returns>
    public static CvSideEntity ConvertBackProbe(CvSideDto dto)
    {
        var entity = new CvSideEntity();
        Mapper.Map(dto, entity, (d, e) => e.SyncStamp = CvConverters.BuildSyncStamp(d));
        return entity;
    }

    /// <summary>
    /// 场景 8 — 同锚点 Convert + ConvertBack 配对：正向格式化与反向解析互逆往返。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="roundTripTarget">反向写回目标实体（验证往返还原）</param>
    /// <returns>正向映射 DTO</returns>
    public static CvPairDto ConvertPairRoundTrip(CvPairEntity src, CvPairEntity roundTripTarget)
    {
        var dto = Mapper.Map<CvPairEntity, CvPairDto>(src)!;
        Mapper.Map(dto, roundTripTarget);
        return dto;
    }

    /// <summary>
    /// 场景调度 — 顺序执行全部场景并输出。
    /// </summary>
    public static void Run()
    {
        // 1. Convert 四形态（正向）
        var src = new CvConvEntity
        {
            Id = 1,
            OrderNo = "ORD-CV-001",
            CustomerName = "LiSi",
            AgeBand = 35,
            FullSummary = "ignored-value",
            ModifiedText = new DateTime(2026, 9, 6, 10, 30, 0),
        };
        var dto = ConvertFourForms(src);
        OutputHelper.Dump("102-convert-four-forms", src, dto);
        if (dto.FullSummary != "ORD-CV-001/LiSi" || dto.BatchNo != "BATCH-2026" || dto.AgeBand != "senior" || dto.ModifiedText != "mod@2026-09-06")
            OutputHelper.Error("102-convert-four-forms: four-form output mismatch");

        // 2. ConvertBack 四形态（反向，同型属性对）
        var backDto = new CvPairDto
        {
            Id = 9,
            CreatedText = "2026-09-06",
            ChangeNote = "seed",
            CompositeKey = "seed",
        };
        var backEntity = ConvertBackFourForms(backDto);
        OutputHelper.Dump("102-convertback-four-forms", backDto, backEntity);
        if (backEntity.AuditTag != "AUDIT-001" || backEntity.CreatedText != new DateTime(2026, 9, 6, 0, 0, 0)
            || backEntity.ChangeNote != "note:2026-09-06" || backEntity.CompositeKey != "9:seed")
            OutputHelper.Error("102-convertback-four-forms: reverse write-back output mismatch");

        // 3. Ignore 方向重载（方向语义强断言：
        //    ToDto=仅正向忽略（反向照常派生）/ToEntity=仅反向忽略（正向照常映射））
        var dirSrc = new CvDirectionEntity { Id = 2, Tag = "tag-x", AuditNote = "note-x" };
        var dirExisting = new CvDirectionEntity { Tag = "KEEP-TAG", AuditNote = "KEEP-NOTE" };
        var dirDto = IgnoreDirection(dirSrc, dirExisting);
        OutputHelper.Dump("102-ignore-direction", dirSrc, new { dto = dirDto, backTag = dirExisting.Tag, backAuditNote = dirExisting.AuditNote });
        if (dirDto.Tag != "")
            OutputHelper.Error($"102-ignore-direction: ToDto forward should ignore Tag (actual: {dirDto.Tag})");
        if (dirDto.AuditNote != "note-x")
            OutputHelper.Error($"102-ignore-direction: ToEntity forward should map AuditNote as usual (actual: {dirDto.AuditNote})");
        if (dirExisting.AuditNote != "KEEP-NOTE")
            OutputHelper.Error($"102-ignore-direction: ToEntity reverse should ignore AuditNote (actual: {dirExisting.AuditNote})");
        if (dirExisting.Tag != "")
            OutputHelper.Error($"102-ignore-direction: ToDto reverse should derive Tag as usual (actual: {dirExisting.Tag})");

        // 4. OverrideIgnore 豁免门（强断言——全局 IgnoreNames（RowVersion）命中仍生成映射；
        //    对照 RowVersionPlain：名称非清单精确命中（IgnoreNames 为精确名/前缀/后缀匹配，
        // 不做族名/前缀族推断—— 勘误）→ 正常自动匹配映射）
        var ovSrc = new CvDirectionEntity { Id = 3, RowVersion = "RV-9", RowVersionPlain = "RVP-9" };
        var ovDto = OverrideIgnore(ovSrc);
        OutputHelper.Dump("102-override-ignore", ovSrc, ovDto);
        if (ovDto.RowVersion != "RV-9")
            OutputHelper.Error($"102-override-ignore: exemption gate failed - RowVersion should map as usual (actual: {ovDto.RowVersion})");
        if (ovDto.RowVersionPlain != "RVP-9")
            OutputHelper.Error($"102-override-ignore: RowVersionPlain is not an exact list match and should map normally (actual: {ovDto.RowVersionPlain})");

        // 5. FromJson
        var jsSrc = new CvSideEntity
        {
            Id = 4,
            Payload = "{\"City\":\"Shanghai\",\"Zip\":\"200001\"}",
            SecretCode = "SECRET-1",
            LegacyNote = "LEGACY-1",
            CreatedAt = new DateTime(2026, 9, 6, 9, 0, 0),
        };
        var jsDto = FromJsonExpand(jsSrc);
        OutputHelper.Dump("102-from-json", jsSrc.Payload, jsDto.Payload);
        if (jsDto.Payload.City != "Shanghai" || jsDto.Payload.Zip != "200001")
            OutputHelper.Error("102-from-json: JSON expansion output mismatch");

            // 6. 方向性忽略（T 侧锚 .Member(dest).Ignore() 强断言：
        //    SecretCode 双向=双向拦截（正向不映射+反向写回跳过）；LegacyNote ToDto=仅正向忽略（反向照常派生））
        var sideExisting = new CvSideEntity { SecretCode = "KEEP-SECRET", LegacyNote = "KEEP-LEGACY" };
        var sideDto = DirectionalIgnoreProbe(jsSrc, sideExisting);
        OutputHelper.Dump("102-directional-ignore", jsSrc, new { dto = sideDto, backSecret = sideExisting.SecretCode, backLegacy = sideExisting.LegacyNote });
        if (sideDto.SecretCode != "")
            OutputHelper.Error($"102-directional-ignore: Both directional ignore should block SecretCode forward (actual: {sideDto.SecretCode})");
        if (sideExisting.SecretCode != "KEEP-SECRET")
            OutputHelper.Error($"102-directional-ignore: Both directional ignore should block SecretCode reverse (actual: {sideExisting.SecretCode})");
        if (sideDto.LegacyNote != "")
            OutputHelper.Error($"102-directional-ignore: ToDto forward should ignore LegacyNote (actual: {sideDto.LegacyNote})");
        if (sideExisting.LegacyNote != "")
            OutputHelper.Error($"102-directional-ignore: ToDto reverse should derive LegacyNote as usual (actual: {sideExisting.LegacyNote})");

        // 7. ConvertBack 反向解析 + SyncStamp action 写回
        var cbDto = new CvSideDto { Id = 5, CreatedAt = "2026-09-06T09:00:00" };
        var cbEntity = ConvertBackProbe(cbDto);
        OutputHelper.Dump("102-convertback", cbDto, cbEntity);
        if (cbEntity.CreatedAt != new DateTime(2026, 9, 6, 9, 0, 0) || cbEntity.SyncStamp != "sync@5")
            OutputHelper.Error("102-convertback: reverse write-back mismatch");

        // 8. Convert + ConvertBack 配对往返
        var pairSrc = new CvPairEntity { Id = 6, CreatedText = new DateTime(2026, 3, 15, 0, 0, 0) };
        var pairTarget = new CvPairEntity();
        var pairDto = ConvertPairRoundTrip(pairSrc, pairTarget);
        OutputHelper.Dump("102-convert-pair", pairSrc.CreatedText, new { dtoText = pairDto.CreatedText, roundTrip = pairTarget.CreatedText });
        if (pairDto.CreatedText != "2026-03-15" || pairTarget.CreatedText != new DateTime(2026, 3, 15, 0, 0, 0))
            OutputHelper.Error("102-convert-pair: paired round-trip mismatch");
    }
}
