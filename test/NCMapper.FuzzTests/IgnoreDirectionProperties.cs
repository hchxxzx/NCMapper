using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// MapIgnore 方向控制属性测试 — 验证 Direction.ToDto / Direction.ToEntity 单向忽略
/// <para>[MapIgnore(Direction.ToDto)] — 仅 Entity→DTO 时忽略，DTO→Entity 时正常映射</para>
/// <para>[MapIgnore(Direction.ToEntity)] — 仅 DTO→Entity 时忽略，Entity→DTO 时正常映射</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class IgnoreDirectionProperties
{
    // ================================================================
    // 正向映射 Entity → DTO
    // ================================================================

    /// <summary>
    /// 正向映射时 ToDto 忽略字段保持默认 — WriteOnly 标注 Direction.ToDto，Entity→DTO 时不映射
    /// </summary>
    [Property]
    public bool Map_ToDto_WriteOnly_Not_Mapped(int id, string writeOnly, string readOnly)
    {
        var entity = new FuzzDirectionEntity { Id = id, WriteOnly = writeOnly, ReadOnly = readOnly };
        var dto = Mapper.Map<FuzzDirectionEntity, FuzzDirectionDto>(entity)!;
        return dto.WriteOnly == "DTO_DEFAULT";
    }

    /// <summary>
    /// 正向映射时 ToEntity 忽略字段正常映射 — ReadOnly 标注 Direction.ToEntity，Entity→DTO 时正常映射
    /// </summary>
    [Property]
    public bool Map_ToDto_ReadOnly_Mapped(int id, string writeOnly, string readOnly)
    {
        var entity = new FuzzDirectionEntity { Id = id, WriteOnly = writeOnly, ReadOnly = readOnly };
        var dto = Mapper.Map<FuzzDirectionEntity, FuzzDirectionDto>(entity)!;
        return dto.ReadOnly == readOnly;
    }

    /// <summary>
    /// 正向映射时 Id 不受方向控制影响 — Id 正常映射
    /// </summary>
    [Property]
    public bool Map_ToDto_Id_Preserved(int id, string writeOnly, string readOnly)
    {
        var entity = new FuzzDirectionEntity { Id = id, WriteOnly = writeOnly, ReadOnly = readOnly };
        var dto = Mapper.Map<FuzzDirectionEntity, FuzzDirectionDto>(entity)!;
        return dto.Id == id;
    }

    // ================================================================
    // 反向映射 DTO → Entity
    // ================================================================

    /// <summary>
    /// 反向映射时 ToDto 忽略字段正常映射 — WriteOnly 标注 Direction.ToDto，DTO→Entity 时正常映射
    /// </summary>
    [Property]
    public bool Map_ToEntity_WriteOnly_Mapped(int id, string dtoWriteOnly, string dtoReadOnly)
    {
        var dto = new FuzzDirectionDto { Id = id, WriteOnly = dtoWriteOnly, ReadOnly = dtoReadOnly };
        var entity = Mapper.Map<FuzzDirectionDto, FuzzDirectionEntity>(dto)!;
        return entity.WriteOnly == dtoWriteOnly;
    }

    /// <summary>
    /// 反向映射时 ToEntity 忽略字段保持默认 — ReadOnly 标注 Direction.ToEntity，DTO→Entity 时不映射
    /// </summary>
    [Property]
    public bool Map_ToEntity_ReadOnly_Not_Mapped(int id, string dtoWriteOnly, string dtoReadOnly)
    {
        var dto = new FuzzDirectionDto { Id = id, WriteOnly = dtoWriteOnly, ReadOnly = dtoReadOnly };
        var entity = Mapper.Map<FuzzDirectionDto, FuzzDirectionEntity>(dto)!;
        return entity.ReadOnly == "";
    }

    /// <summary>
    /// 反向映射时 Id 不受方向控制影响 — Id 正常映射
    /// </summary>
    [Property]
    public bool Map_ToEntity_Id_Preserved(int id, string dtoWriteOnly, string dtoReadOnly)
    {
        var dto = new FuzzDirectionDto { Id = id, WriteOnly = dtoWriteOnly, ReadOnly = dtoReadOnly };
        var entity = Mapper.Map<FuzzDirectionDto, FuzzDirectionEntity>(dto)!;
        return entity.Id == id;
    }
}
