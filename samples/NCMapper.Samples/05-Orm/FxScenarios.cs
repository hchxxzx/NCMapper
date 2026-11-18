using Microsoft.EntityFrameworkCore;
using NCMapper.EFCore;
using NCMapper.FreeSql;

namespace NCMapper.Samples.Orm;

/// <summary>
/// Runtime×ORM 演示场景集 — Fluent DTO + EFCore/FreeSql AsDto 双态：
/// 纯可翻译声明走投影（SQL 侧列裁剪）；Mask 声明一律强制降级（内存映射 + 脱敏不落明文）。
/// </summary>
public static class FxScenarios
{
    /// <summary>
    /// EFCore 纯可翻译 Fluent DTO 投影：AsDto 正常返回（SQL 侧列裁剪）。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>投影后的 Fluent DTO 列表</returns>
    #region OrmProjection_FluentDualMode
    public static List<FxWikiDto> ECoreFluentProjection(DbContext db)
        => db.Set<FxWikiEntity>()
            .AsDto()
            .ToList<FxWikiDto>();
    #endregion


    /// <summary>
    /// EFCore 含 Mask 声明的 Fluent DTO：一律强制降级（内存映射承载，不抛异常、脱敏不落明文）。
    /// </summary>
    /// <param name="db">EF Core DbContext</param>
    /// <returns>降级路径产物（脱敏后）</returns>
    #region OrmProjection_FluentDualMode_ECoreFluentMaskDowngrade
    public static List<FxMaskDto> ECoreFluentMaskDowngrade(DbContext db)
        => db.Set<FxMaskEntity>()
            .AsDto()
            .ToList<FxMaskDto>();
    #endregion


    /// <summary>
    /// FreeSql 纯可翻译 Fluent DTO 投影。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>投影后的 Fluent DTO 列表</returns>
    #region OrmProjection_FluentDualMode_FreeSqlFluentProjection
    public static List<FxWikiDto> FreeSqlFluentProjection(IFreeSql fsql)
        => fsql.Select<FxWikiEntity>()
            .AsDto()
            .ToList<FxWikiDto>();
    #endregion


    /// <summary>
    /// FreeSql 含 Mask 声明的 Fluent DTO：一律强制降级（内存映射 + 脱敏不落明文）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    /// <returns>降级路径产物（脱敏后）</returns>
    #region OrmProjection_FluentDualMode_FreeSqlFluentMaskDowngrade
    public static List<FxMaskDto> FreeSqlFluentMaskDowngrade(IFreeSql fsql)
        => fsql.Select<FxMaskEntity>()
            .AsDto()
            .ToList<FxMaskDto>();
    #endregion
}
