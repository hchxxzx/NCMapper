using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;

namespace NCMapper.Samples.Infrastructure;

/// <summary>
/// 管道场景集 — PostMap 后置钩子、Enable/Clear 控制。
/// </summary>
public static class PipelineScenarios
{
    /// <summary>
    /// PostMap 追加审计后缀到 Email。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>经 PostMap 修改 Email 的 DTO</returns>
    #region Pipeline_PostMapAudit
    public static UserDto PostMapAudit(UserEntity src)
    {
        Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((source, target) =>
        {
            target.Email = $"{target.Email} (by system)";
        });

        var result = Mapper.Map<UserEntity, UserDto>(src)!;

        Mapper.Pipeline.Clear();
        return result;
    }
    #endregion

    /// <summary>
    /// PostMap 审计追踪：映射后追加审计标签到 Name。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>经管道处理的 DTO</returns>
    public static UserDto PostMapAuditTrail(UserEntity src)
    {
        Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((source, target) =>
        {
            target.Name = $"{target.Name} [Audited]";
        });

        var result = Mapper.Map<UserEntity, UserDto>(src)!;

        Mapper.Pipeline.Clear();
        return result;
    }

    /// <summary>
    /// 多层 PostMap 洋葱模型：注册 2 个 PostMap，验证执行顺序（先注册后执行）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>经双层 PostMap 处理的 DTO</returns>
    #region Infra_Pipeline_PostMapOrder
    public static UserDto PostMapOnionModel(UserEntity src)
    {
        // Inner layer: registered first, executed last (closest to the mapping core)
        Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((source, target) =>
        {
            target.Name = $"{target.Name} [Inner]";
        });
        // Outer layer: registered last, executed first (wraps the inner one)
        Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((source, target) =>
        {
            target.Name = $"{target.Name} [Outer]";
        });

        var result = Mapper.Map<UserEntity, UserDto>(src)!;

        Mapper.Pipeline.Clear();
        return result;
    }
    #endregion

    /// <summary>清空所有已注册 PostMap：Clear 一次全清，后续映射零钩子执行（管道短路标志同步复位）。</summary>
    #region Infra_Pipeline_ClearAll
    public static UserDto ClearAllHooks(UserEntity src)
    {
        Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((s, t) => t.Name = $"{t.Name} [hooked]");
        // Drop every registered PostMap in one call
        Mapper.Pipeline.Clear();
        // PostMap no longer runs after Clear
        return Mapper.Map<UserEntity, UserDto>(src)!;
    }
    #endregion

    /// <summary>横切关注点注册形态：应用启动时注册一次、全程生效，不要事后 Clear。</summary>
    #region Infra_Pipeline_CrossCutOnce
    public static void RegisterCrossCuttingOnce()
    {
        // Cross-cutting concern: register once at startup, effective for the whole process lifetime
        Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((s, t) =>
            t.Name = $"{t.Name} @ {DateTime.UtcNow:O}");
    }
    #endregion

    /// <summary>
    /// Map + Action 回调 + PostMap 组合：Action 在 PostMap 之前执行。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>经组合管道处理的 DTO</returns>
    public static UserDto PostMapWithAction(UserEntity src)
    {
        Mapper.Pipeline.AddPostMap<UserEntity, UserDto>((source, target) =>
        {
            target.Email = $"{target.Email} [PostMap]";
        });

        // Action 在 PostMap 之前执行
        var result = Mapper.Map<UserEntity, UserDto>(src, (s, t) => t.Name = $"{t.Name} [Action]")!;

        Mapper.Pipeline.Clear();
        return result;
    }

    // ===== 场景注册入口=====
    public static void Run()
    {
    var src = new UserEntity { Id = 1, Name = "Pipeline-Test", Age = 28, Email = "pipeline@test.com" };

    var dto = PipelineScenarios.PostMapAudit(src);
    OutputHelper.Dump("601-postmap-audit", src.Email, dto.Email);
    // 期望值：PostMap 映射后改写语义——Email 追加审计后缀
    if (dto.Email != "pipeline@test.com (by system)")
        OutputHelper.Error($"601-postmap-audit: PostMap should append audit suffix to Email (actual: {dto.Email})");

    var src2 = new UserEntity { Id = 2, Name = "Audit-Trail", Age = 30, Email = "audit@test.com" };
    var dto2 = PipelineScenarios.PostMapAuditTrail(src2);
    OutputHelper.Dump("601-postmap-audit-trail", src2.Name, dto2.Name);
    // 期望值：PostMap 追加语义——Name 追加 [Audited] 标记
    if (dto2.Name != "Audit-Trail [Audited]")
        OutputHelper.Error($"601-postmap-audit-trail: PostMap should append [Audited] to Name (actual: {dto2.Name})");

    var src3 = new UserEntity { Id = 3, Name = "Onion", Age = 30, Email = "onion@test.com" };
    var dto3 = PipelineScenarios.PostMapOnionModel(src3);
    OutputHelper.Dump("601-postmap-onion", src3.Name, dto3.Name);
    // 期望值：PostMap 按注册逆序执行（洋葱模型）——后注册的 Outer 先执行，标记在前
    if (dto3.Name != "Onion [Outer] [Inner]")
        OutputHelper.Error($"601-postmap-onion: onion order should be Outer-then-Inner (registered reverse order) (actual: {dto3.Name})");

    var src4 = new UserEntity { Id = 4, Name = "Combo", Age = 30, Email = "combo@test.com" };
    var dto4 = PipelineScenarios.PostMapWithAction(src4);
    OutputHelper.Dump("601-postmap-action", src4.Name + "/" + src4.Email, $"{dto4.Name} / {dto4.Email}");
    // 期望值：Action 与 PostMap 相位序——Action 在映射期、PostMap 在映射后，互不覆盖
    if (dto4.Name != "Combo [Action]" || dto4.Email != "combo@test.com [PostMap]")
        OutputHelper.Error($"601-postmap-action: Action (mapping-time) and PostMap (post-mapping) should both apply (actual: {dto4.Name} / {dto4.Email})");
    }

}
