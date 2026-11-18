using Riok.Mapperly.Abstractions;
using NCMapper.Benchmarks.Models;
using AM = global::AutoMapper;

namespace NCMapper.Benchmarks;

/// <summary>
/// Mapperly 映射器 — 编译时生成映射代码，零运行时开销
/// </summary>
[Mapper]
public partial class MapperlyMapper
{
    // 扁平对象
    public partial FlatDto FlatEntityToDto(FlatEntity entity);
    public partial FlatEntity FlatDtoToEntity(FlatDto dto);

    // 嵌套对象
    public partial InnerDto InnerEntityToDto(InnerEntity entity);
    public partial NestedDto NestedEntityToDto(NestedEntity entity);

    // 集合
    public partial List<FlatDto> FlatEntityListToDtoList(List<FlatEntity> entities);

    // 枚举→字符串
    public partial EnumToStringDto EnumEntityToDto(EnumEntity entity);

    // record
    public partial RecordDto RecordEntityToDto(RecordEntity entity);

    // NameDiff 10 属性
    [MapProperty(nameof(NameDiffBigEntity.Id), nameof(NameDiffBigDto.Key))]
    [MapProperty(nameof(NameDiffBigEntity.Name), nameof(NameDiffBigDto.FullName))]
    [MapProperty(nameof(NameDiffBigEntity.Age), nameof(NameDiffBigDto.UserAge))]
    [MapProperty(nameof(NameDiffBigEntity.Created), nameof(NameDiffBigDto.CreatedAt))]
    [MapProperty(nameof(NameDiffBigEntity.Amount), nameof(NameDiffBigDto.TotalAmount))]
    [MapProperty(nameof(NameDiffBigEntity.IsActive), nameof(NameDiffBigDto.Enabled))]
    [MapProperty(nameof(NameDiffBigEntity.Score), nameof(NameDiffBigDto.Grade))]
    [MapProperty(nameof(NameDiffBigEntity.UserId), nameof(NameDiffBigDto.AccountId))]
    [MapProperty(nameof(NameDiffBigEntity.Guid), nameof(NameDiffBigDto.Uid))]
    [MapProperty(nameof(NameDiffBigEntity.Status), nameof(NameDiffBigDto.OrderState))]
    public partial NameDiffBigDto NameDiffBigEntityToDto(NameDiffBigEntity entity);

    // AutoFlatten 10 属性
    public partial FlattenDto FlattenParentEntityToDto(FlattenParentEntity entity);

    // 可空
    public partial NullableDto NullableEntityToDto(NullableEntity entity);
}

/// <summary>
/// Mapperly 10 属性异名映射器
/// </summary>
[Mapper]
public partial class NameDiffMapperlyBigMapper
{
    [MapProperty(nameof(NameDiffBigEntity.Id), nameof(NameDiffBigDto.Key))]
    [MapProperty(nameof(NameDiffBigEntity.Name), nameof(NameDiffBigDto.FullName))]
    [MapProperty(nameof(NameDiffBigEntity.Age), nameof(NameDiffBigDto.UserAge))]
    [MapProperty(nameof(NameDiffBigEntity.Created), nameof(NameDiffBigDto.CreatedAt))]
    [MapProperty(nameof(NameDiffBigEntity.Amount), nameof(NameDiffBigDto.TotalAmount))]
    [MapProperty(nameof(NameDiffBigEntity.IsActive), nameof(NameDiffBigDto.Enabled))]
    [MapProperty(nameof(NameDiffBigEntity.Score), nameof(NameDiffBigDto.Grade))]
    [MapProperty(nameof(NameDiffBigEntity.UserId), nameof(NameDiffBigDto.AccountId))]
    [MapProperty(nameof(NameDiffBigEntity.Guid), nameof(NameDiffBigDto.Uid))]
    [MapProperty(nameof(NameDiffBigEntity.Status), nameof(NameDiffBigDto.OrderState))]
    public partial NameDiffBigDto NameDiffBigEntityToDto(NameDiffBigEntity entity);
}

/// <summary>
/// Mapperly 异名字段映射器
/// </summary>
[Mapper]
public partial class NameDiffMapperlyMapper
{
    [MapProperty(nameof(NameDiffEntity.Name), nameof(NameDiffDto.FullName))]
    [MapProperty(nameof(NameDiffEntity.Age), nameof(NameDiffDto.UserAge))]
    [MapProperty(nameof(NameDiffEntity.Id), nameof(NameDiffDto.AccountId))]
    [MapProperty(nameof(NameDiffEntity.Amount), nameof(NameDiffDto.TotalAmount))]
    public partial NameDiffDto NameDiffEntityToDto(NameDiffEntity entity);
}

/// <summary>
/// AutoMapper 映射配置
/// </summary>
public class BenchmarkProfile : AM.Profile
{
    public BenchmarkProfile()
    {
        CreateMap<FlatEntity, FlatDto>();
        CreateMap<FlatDto, FlatEntity>();
        CreateMap<NameDiffEntity, NameDiffDto>()
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.Name))
            .ForMember(d => d.UserAge, o => o.MapFrom(s => s.Age))
            .ForMember(d => d.AccountId, o => o.MapFrom(s => (long)s.Id))
            .ForMember(d => d.TotalAmount, o => o.MapFrom(s => s.Amount));
        CreateMap<NestedEntity, NestedDto>();
        CreateMap<InnerEntity, InnerDto>();
        CreateMap<EnumEntity, EnumToStringDto>();
        CreateMap<RecordEntity, RecordDto>();
        CreateMap<NullableEntity, NullableDto>();
        CreateMap<FlattenParentEntity, FlattenDto>();
    }
}

/// <summary>
/// AutoMapper 映射器实例
/// </summary>
public static class AutoMapperHelper
{
    public static readonly AM.IMapper Mapper = CreateMapper(c => c.AddProfile<BenchmarkProfile>());

    private static AM.IMapper CreateMapper(Action<AM.MapperConfigurationExpression> configure)
    {
        var expr = new AM.MapperConfigurationExpression();
        configure(expr);
        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        return new AM.MapperConfiguration(expr, loggerFactory).CreateMapper();
    }
}
