using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// MapIgnore 属性忽略属性测试 — 验证标注 [MapIgnore] 的属性不参与映射
/// <para>覆盖路径：正向映射（Entity→DTO）忽略、反向映射（DTO→Entity）忽略、非忽略字段正常映射</para>
/// <para>[MapIgnore] 默认 Direction.Both，双向忽略 — 正反两个方向都不映射该字段</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class IgnoreProperties
{
    // ================================================================
    // 正向映射 Entity → DTO — [MapIgnore] 字段不映射
    // ================================================================

    /// <summary>
    /// 正向映射时被忽略字段保持默认值 — SecretKey 标注 [MapIgnore] 不被 Entity 的值覆盖
    /// </summary>
    [Property]
    public bool Map_MapIgnore_Field_Not_Mapped(int id, string name, string secretKey)
    {
        var entity = new FuzzIgnoreEntity { Id = id, Name = name, SecretKey = secretKey };
        var dto = Mapper.Map<FuzzIgnoreEntity, FuzzIgnoreDto>(entity)!;
        return dto.SecretKey == "DEFAULT";
    }

    /// <summary>
    /// 正向映射时非忽略字段正常映射 — Id 和 Name 不受 [MapIgnore] 影响
    /// </summary>
    [Property]
    public bool Map_MapIgnore_Other_Fields_Preserved(int id, string name, string secretKey)
    {
        var entity = new FuzzIgnoreEntity { Id = id, Name = name, SecretKey = secretKey };
        var dto = Mapper.Map<FuzzIgnoreEntity, FuzzIgnoreDto>(entity)!;
        return dto.Id == id && dto.Name == name;
    }

    // ================================================================
    // 反向映射 DTO → Entity — [MapIgnore] 字段不映射
    // ================================================================

    /// <summary>
    /// 反向映射时被忽略字段不被覆盖 — DTO 的 SecretKey 不映射到 Entity，Entity 保持默认空字符串
    /// </summary>
    [Property]
    public bool MapReverse_MapIgnore_Field_Not_Mapped(int id, string name, string dtoSecretKey)
    {
        var dto = new FuzzIgnoreDto { Id = id, Name = name, SecretKey = dtoSecretKey };
        var entity = Mapper.Map<FuzzIgnoreDto, FuzzIgnoreEntity>(dto)!;
        // Entity 的 SecretKey 应为默认空字符串，因为 [MapIgnore] 双向忽略
        return entity.SecretKey == "";
    }

    /// <summary>
    /// 反向映射时非忽略字段正常映射 — Id 和 Name 从 DTO 正确映射到 Entity
    /// </summary>
    [Property]
    public bool MapReverse_MapIgnore_Other_Fields_Preserved(int id, string name, string dtoSecretKey)
    {
        var dto = new FuzzIgnoreDto { Id = id, Name = name, SecretKey = dtoSecretKey };
        var entity = Mapper.Map<FuzzIgnoreDto, FuzzIgnoreEntity>(dto)!;
        return entity.Id == id && entity.Name == name;
    }

    // ================================================================
    // 双向往返 — 被忽略字段在往返后保持默认
    // ================================================================

    /// <summary>
    /// 双向往返后被忽略字段保持默认 — Entity→DTO→Entity 后非忽略字段一致，SecretKey 保持默认
    /// </summary>
    [Property]
    public bool RoundTrip_MapIgnore_Field_Stays_Default(int id, string name, string secretKey)
    {
        var entity = new FuzzIgnoreEntity { Id = id, Name = name, SecretKey = secretKey };
        var dto = Mapper.Map<FuzzIgnoreEntity, FuzzIgnoreDto>(entity)!;
        var roundTrip = Mapper.Map<FuzzIgnoreDto, FuzzIgnoreEntity>(dto)!;

        // Id 和 Name 往返一致
        // SecretKey 往返后为 Entity 默认空字符串（DTO→Entity 时 [MapIgnore] 不映射）
        return roundTrip.Id == id
            && roundTrip.Name == name
            && roundTrip.SecretKey == "";
    }
}
