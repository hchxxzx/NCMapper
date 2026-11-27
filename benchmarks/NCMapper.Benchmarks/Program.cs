using System.Diagnostics;
using System.Runtime.Versioning;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using NCMapper.Benchmarks.Models;
using NCMapper.Generated;
using AM = global::AutoMapper;
using Mapster;
using System.Text.Json;
using MemoryPack;
using MessagePack;

namespace NCMapper.Benchmarks;

// ================================================================
// 共享测试数据
// ================================================================

public static class BenchmarkData
{
    public static readonly FlatEntity FlatEntity = new()
    {
        Id = 1, Name = "Test", Age = 30, Salary = 5000m,
        CreatedAt = new DateTime(2024, 1, 1), IsActive = true, Score = 99.5,
        UserId = 123456, Guid = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffffffffff"),
        Status = OrderStatus.Shipped
    };

    public static readonly FlatDto FlatDto = new()
    {
        Id = 1, Name = "Test", Age = 30, Salary = 5000m,
        CreatedAt = new DateTime(2024, 1, 1), IsActive = true, Score = 99.5,
        UserId = 123456, Guid = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffffffffff"),
        Status = OrderStatus.Shipped
    };

    public static readonly NameDiffEntity NameDiffEntity = new();

    public static readonly NestedEntity NestedEntity = new()
    {
        Id = 1, Name = "Parent",
        Child = new InnerEntity { City = "Beijing", Street = "Chang'an Ave", ZipCode = 100000 }
    };

    public static readonly List<FlatEntity> FlatEntities100
        = Enumerable.Range(1, 100).Select(i => new FlatEntity
        {
            Id = i, Name = $"N_{i}", Age = i % 100, Salary = i * 100m,
            CreatedAt = new DateTime(2024, 1, 1), IsActive = true, Score = i * 1.5,
            UserId = i * 1000L, Guid = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffffffffff"),
            Status = OrderStatus.Processing
        }).ToList();

    public static readonly List<FlatEntityRuntime> FlatEntities100Runtime
        = Enumerable.Range(1, 100).Select(i => new FlatEntityRuntime
        {
            Id = i, Name = $"N_{i}", Age = i % 100, Salary = i * 100m,
            CreatedAt = new DateTime(2024, 1, 1), IsActive = true, Score = i * 1.5,
            UserId = i * 1000L, Guid = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffffffffff"),
            Status = OrderStatus.Processing
        }).ToList();

    public static readonly MapperlyMapper Mapperly = new();
    public static readonly NameDiffMapperlyMapper NameDiffMapperly = new();
    public static readonly NameDiffMapperlyBigMapper NameDiffMapperlyBig = new();

    // 新场景数据
    public static readonly EnumEntity EnumEntityData = new() { Id = 1, Status = OrderStatus.Delivered, Name = "Enum" };
    public static readonly RecordEntity RecordEntityData = new(42, "Record", 25, 999.9m, new DateTime(2024, 6, 1));
    public static readonly RecordBigEntity RecordBigEntityData = new(42, "Record", 25, 999.9m, new DateTime(2024, 6, 1), true, 95.5, 1234567890L, Guid.Empty, OrderStatus.Shipped);
    public static readonly NameDiffBigEntity NameDiffBigEntityData = new();
    public static readonly NullableEntity NullableEntityData = new() { Id = 1, Score = 85, UpdatedAt = new DateTime(2024, 6, 1), Name = "Nullable" };
    public static readonly FlattenParentEntity FlattenEntityData = new() { Id = 1, Child = new FlattenChildEntity { Name = "FlatChild", Level = 5, City = "C", Street = "S", ZipCode = 1, IsActive = true, Score = 99.5, UserId = 999, Guid = Guid.Empty, Status = OrderStatus.Shipped } };
    public static readonly DeepCopyEntity DeepCopyEntityData = new() { Id = 1, Name = "DeepCopy", Child = new InnerEntity { City = "C", Street = "S", ZipCode = 1 }, Tags = new List<int> { 1, 2, 3 } };
    public static readonly DeepCopyMapFromDto DeepCopyMapFromEntityData = new() { Id = 1, Name = "DeepCopy", Child = new DeepCopyChildEntity { City = "C", Street = "S", ZipCode = 1 }, Tags = new List<int> { 1, 2, 3 } };

    // 无 [MapFrom] 对照组数据 — 走表达式树路径
    public static readonly FlatEntityRuntime FlatEntityRuntime = new()
    {
        Id = 1, Name = "Test", Age = 30, Salary = 5000m,
        CreatedAt = new DateTime(2024, 1, 1), IsActive = true, Score = 99.5,
        UserId = 123456, Guid = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffffffffff"),
        Status = OrderStatus.Shipped
    };
    public static readonly NestedEntityRuntime NestedEntityRuntime = new()
    {
        Id = 1, Name = "Parent",
        Child = new InnerEntity { City = "Beijing", Street = "Chang'an Ave", ZipCode = 100000 }
    };
    public static readonly RecordEntityRuntime RecordEntityRuntimeData = new(42, "Record", 25, 999.9m, new DateTime(2024, 6, 1));
    public static readonly NullableEntityRuntime NullableEntityRuntimeData = new() { Id = 1, Score = 85, UpdatedAt = new DateTime(2024, 6, 1), Name = "Nullable" };

    // 补充无 [MapFrom] Runtime 对照数据
    public static readonly NameDiffEntityRuntime NameDiffEntityRuntime = new();
    public static readonly FlatDtoRuntimeReverse FlatDtoRuntimeReverse = new();
    public static readonly ActionEntityRuntime ActionEntityRuntime = new();
    public static readonly EnumEntityRuntime EnumEntityRuntime = new() { Id = 1, Status = OrderStatus.Delivered, Name = "Enum" };
    public static readonly FlattenParentEntityRuntime FlattenEntityRuntime = new() { Id = 1, Child = new FlattenChildEntity { Name = "FlatChild", Level = 5, City = "C", Street = "S", ZipCode = 1, IsActive = true, Score = 99.5, UserId = 999, Guid = Guid.Empty, Status = OrderStatus.Shipped } };

    // Array/Dictionary/Compose 新场景数据
    /* 数组映射数据 — TSource[] → List<TTarget>（通过 MapList）。
       复用 FlatEntities100 转为数组，避免额外分配。 */
    public static readonly FlatEntity[] FlatEntityArray
        = FlatEntities100.ToArray();

    /* 字典深拷贝数据 — Dictionary<string, T> 的 DeepCopy。
       10 个键值对，T 为 DeepCopyEntity（含嵌套引用 + 集合）。 */
    public static readonly Dictionary<string, DeepCopyEntity> DictDeepCopyData
        = Enumerable.Range(1, 10).ToDictionary(
            i => $"key_{i}",
            i => new DeepCopyEntity { Id = i, Name = $"Dict_{i}", Child = new InnerEntity { City = "C", Street = "S", ZipCode = i }, Tags = new List<int> { i, i * 2 } });

    /* 3 源合成数据 — 订单 + 客户 + 产品，通过 Compose 合成 DTO。 */
    public static readonly ComposeOrderSource ComposeOrderData = new();
    public static readonly ComposeCustomerSource ComposeCustomerData = new();
    public static readonly ComposeProductSource ComposeProductData = new();
}

// ================================================================
// Job 配置常量
// ================================================================

internal static class BenchConfig
{
    public const int LaunchCount = 5;
    public const int WarmupCount = 3;
    public const int IterationCount = 15;
}

// ================================================================
// 手写映射 — 理论最优基准
// ================================================================

internal static class ManualMapper
{
    public static FlatDto FlatEntityToDto(FlatEntity s) => new()
    {
        Id = s.Id, Name = s.Name, Age = s.Age, Salary = s.Salary,
        CreatedAt = s.CreatedAt, IsActive = s.IsActive, Score = s.Score,
        UserId = s.UserId, Guid = s.Guid, Status = s.Status
    };

    public static FlatEntity FlatDtoToEntity(FlatDto s) => new()
    {
        Id = s.Id, Name = s.Name, Age = s.Age, Salary = s.Salary,
        CreatedAt = s.CreatedAt, IsActive = s.IsActive, Score = s.Score,
        UserId = s.UserId, Guid = s.Guid, Status = s.Status
    };

    public static NameDiffDto NameDiffEntityToDto(NameDiffEntity s) => new()
    {
        Id = s.Id, FullName = s.Name, UserAge = s.Age,
        AccountId = s.Id, Created = s.Created, TotalAmount = s.Amount
    };

    public static NestedDto NestedEntityToDto(NestedEntity s) => new()
    {
        Id = s.Id, Name = s.Name,
        Child = s.Child == null ? null : new InnerDto { City = s.Child.City, Street = s.Child.Street, ZipCode = s.Child.ZipCode }
    };

    public static List<FlatDto> FlatEntityListToDtoList(List<FlatEntity> src)
    {
        var list = new List<FlatDto>(src.Count);
        foreach (var s in src)
        {
            list.Add(new FlatDto
            {
                Id = s.Id, Name = s.Name, Age = s.Age, Salary = s.Salary,
                CreatedAt = s.CreatedAt, IsActive = s.IsActive, Score = s.Score,
                UserId = s.UserId, Guid = s.Guid, Status = s.Status
            });
        }
        return list;
    }

    public static EnumToStringDto EnumEntityToDto(EnumEntity s) => new()
    {
        Id = s.Id, Status = s.Status.ToString(), Name = s.Name
    };

    public static RecordDto RecordEntityToDto(RecordEntity s) => new(s.Id, s.Name, s.Age, s.Salary, s.CreatedAt);

    public static RecordBigDto RecordBigEntityToDto(RecordBigEntity s) => new(s.Id, s.Name, s.Age, s.Salary, s.CreatedAt, s.IsActive, s.Score, s.UserId, s.Guid, s.Status);

    public static NullableDto NullableEntityToDto(NullableEntity s) => new()
    {
        Id = s.Id, Score = s.Score ?? 0, UpdatedAt = s.UpdatedAt ?? default, Name = s.Name
    };

    public static FlattenDto FlattenParentEntityToDto(FlattenParentEntity s) => new()
    {
        Id = s.Id, ChildName = s.Child?.Name ?? "", ChildLevel = s.Child?.Level ?? 0,
        ChildCity = s.Child?.City ?? "", ChildStreet = s.Child?.Street ?? "", ChildZipCode = s.Child?.ZipCode ?? 0,
        ChildIsActive = s.Child?.IsActive ?? false, ChildScore = s.Child?.Score ?? 0,
        ChildUserId = s.Child?.UserId ?? 0, ChildGuid = s.Child?.Guid ?? Guid.Empty, ChildStatus = s.Child?.Status ?? default
    };

    public static NameDiffBigDto NameDiffBigEntityToDto(NameDiffBigEntity s) => new()
    {
        Key = s.Id, FullName = s.Name, UserAge = s.Age, CreatedAt = s.Created, TotalAmount = s.Amount,
        Enabled = s.IsActive, Grade = s.Score, AccountId = s.UserId, Uid = s.Guid, OrderState = s.Status
    };

    public static DeepCopyEntity DeepCopy(DeepCopyEntity s) => new()
    {
        Id = s.Id, Name = s.Name,
        Child = s.Child == null ? null : new InnerEntity { City = s.Child.City, Street = s.Child.Street, ZipCode = s.Child.ZipCode },
        Tags = new List<int>(s.Tags)
    };

    public static DeepCopyMapFromDto DeepCopyMapFrom(DeepCopyMapFromDto s) => new()
    {
        Id = s.Id, Name = s.Name,
        Child = s.Child == null ? null : new DeepCopyChildEntity { City = s.Child.City, Street = s.Child.Street, ZipCode = s.Child.ZipCode },
        Tags = new List<int>(s.Tags)
    };

    public static void FlatEntityToUpdateDto(FlatEntity s, FlatDto d)
    {
        d.Id = s.Id; d.Name = s.Name; d.Age = s.Age; d.Salary = s.Salary;
        d.CreatedAt = s.CreatedAt; d.IsActive = s.IsActive; d.Score = s.Score;
        d.UserId = s.UserId; d.Guid = s.Guid; d.Status = s.Status;
    }
}

// ================================================================
// 1. Flat — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Flat_NCMapper { [Benchmark] public FlatDto Run() => NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Flat_Mapperly { [Benchmark] public FlatDto Run() => BenchmarkData.Mapperly.FlatEntityToDto(BenchmarkData.FlatEntity); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Flat_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<FlatEntity, FlatDto>.NewConfig().Compile(); [Benchmark] public FlatDto Run() => BenchmarkData.FlatEntity.Adapt<FlatDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Flat_AutoMapper { [Benchmark] public FlatDto Run() => AutoMapperHelper.Mapper.Map<FlatDto>(BenchmarkData.FlatEntity); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Flat_Manual { [Benchmark] public FlatDto Run() => ManualMapper.FlatEntityToDto(BenchmarkData.FlatEntity); }

// ================================================================
// 2. NameDiff — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiff_NCMapper { [Benchmark] public NameDiffDto Run() => NCMapper.Mapper.Map<NameDiffEntity, NameDiffDto>(BenchmarkData.NameDiffEntity)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiff_Mapperly { [Benchmark] public NameDiffDto Run() => BenchmarkData.NameDiffMapperly.NameDiffEntityToDto(BenchmarkData.NameDiffEntity); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiff_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<NameDiffEntity, NameDiffDto>.NewConfig().Compile(); [Benchmark] public NameDiffDto Run() => BenchmarkData.NameDiffEntity.Adapt<NameDiffDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiff_AutoMapper { [Benchmark] public NameDiffDto Run() => AutoMapperHelper.Mapper.Map<NameDiffDto>(BenchmarkData.NameDiffEntity); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiff_Manual { [Benchmark] public NameDiffDto Run() => ManualMapper.NameDiffEntityToDto(BenchmarkData.NameDiffEntity); }

// ================================================================
// 3. Nested — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_NCMapper { [Benchmark] public NestedDto Run() => NCMapper.Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity)!; }

// ================================================================
// 扩展方法直调（零委托，JIT 可内联）
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Flat_Ext { [Benchmark] public FlatDto Run() => BenchmarkData.FlatEntity.MapToFlatDto()!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiff_Ext { [Benchmark] public NameDiffDto Run() => BenchmarkData.NameDiffEntity.MapToNameDiffDto()!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Ext { [Benchmark] public NestedDto Run() => BenchmarkData.NestedEntity.MapToNestedDto()!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Record_Ext { [Benchmark] public RecordDto Run() => BenchmarkData.RecordEntityData.MapToRecordDto()!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class AutoFlatten_Ext { [Benchmark] public FlattenDto Run() => BenchmarkData.FlattenEntityData.MapToFlattenDto()!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirED_Ext { [Benchmark] public FlatDto Run() => BenchmarkData.FlatEntity.MapToFlatDto()!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class EnumString_Ext { [Benchmark] public EnumToStringDto Run() => BenchmarkData.EnumEntityData.MapToEnumToStringDto()!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nullable_Ext { [Benchmark] public NullableDto Run() => BenchmarkData.NullableEntityData.MapToNullableDto()!; }

// ================================================================
// Nested 逐层开销分析 — 精确定位委托/入口/action 开销
// ================================================================

// 层次 0：直接调源生成器 Core 方法（static call，JIT 可内联，理论最优）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Layer0_DirectCore { [Benchmark] public NestedDto Run() => NCMapper.Generated.GeneratedMapper.Map_NestedEntity_To_NestedDto(BenchmarkData.NestedEntity)!; }

// 层次 1：通过局部委托调用（delegate.Invoke，不可内联）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Layer1_Delegate
{
    static readonly Func<NestedEntity, NestedDto?> _func = NCMapper.Generated.GeneratedMapper.Map_NestedEntity_To_NestedDto;
    [Benchmark] public NestedDto Run() => _func(BenchmarkData.NestedEntity)!;
}

// 层次 2：通过 Mapper.Map 入口（泛型静态字段委托 + 入口分支）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Layer2_MapperMap { [Benchmark] public NestedDto Run() => NCMapper.Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity)!; }

// 层次 3：通过 Mapper.Map 入口 + 显式传 null action（走 FuncWithAction 委托 + action?.Invoke）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Layer3_MapperMapAction { [Benchmark] public NestedDto Run() => NCMapper.Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity, null)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Mapperly { [Benchmark] public NestedDto Run() => BenchmarkData.Mapperly.NestedEntityToDto(BenchmarkData.NestedEntity); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<NestedEntity, NestedDto>.NewConfig().Compile(); [Benchmark] public NestedDto Run() => BenchmarkData.NestedEntity.Adapt<NestedDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_AutoMapper { [Benchmark] public NestedDto Run() => AutoMapperHelper.Mapper.Map<NestedDto>(BenchmarkData.NestedEntity); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Manual { [Benchmark] public NestedDto Run() => ManualMapper.NestedEntityToDto(BenchmarkData.NestedEntity); }

// ================================================================
// 4. Collection — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Collection_NCMapper { [Benchmark] public List<FlatDto> Run() => NCMapper.Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Collection_Mapperly { [Benchmark] public List<FlatDto> Run() => BenchmarkData.Mapperly.FlatEntityListToDtoList(BenchmarkData.FlatEntities100); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Collection_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<List<FlatEntity>, List<FlatDto>>.NewConfig().Compile(); [Benchmark] public List<FlatDto> Run() => BenchmarkData.FlatEntities100.Adapt<List<FlatDto>>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Collection_AutoMapper { [Benchmark] public List<FlatDto> Run() => AutoMapperHelper.Mapper.Map<List<FlatDto>>(BenchmarkData.FlatEntities100); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Collection_Manual { [Benchmark] public List<FlatDto> Run() => ManualMapper.FlatEntityListToDtoList(BenchmarkData.FlatEntities100); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Collection_Ext { [Benchmark] public List<FlatDto> Run() => BenchmarkData.FlatEntities100.MapToFlatDtoList()!; }

// 批量就地映射：与 Collection_NCMapper 对比"每调用分配" vs "复用零分配"
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class CollectionInto_NCMapper
{
    private List<FlatEntity> _src = null!;
    private FlatDto[] _dst = null!;

    /// <summary>
    /// 预分配并初始化 destination（一次性成本，不计入测量；复用调用顶层零分配）。
    /// 不得在 Run 内 new——否则退化为"新建+填充"反模式，与 MapList 无差异。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _src = BenchmarkData.FlatEntities100;
        _dst = new FlatDto[_src.Count];
        for (int i = 0; i < _dst.Length; i++)
        {
            _dst[i] = new FlatDto();
        }
    }

    /// <summary>
    /// 复用填充调用（destination 复用；预期 Allocation = 0 B，与 Collection_NCMapper 每次全量分配形成对比）
    /// </summary>
    [Benchmark]
    public void Run() => NCMapper.Mapper.MapListInto<FlatEntity, FlatDto>(_src, _dst);
}

/// <summary>
/// 批量就地映射 Runtime 路径（2026-08-30 新增）：与 CollectionInto_NCMapper 对比
/// 生成器 vs 运行时（无 [MapFrom] 类型 → GeneratedCache 未命中 → MapperBase 表达式树回退）。
/// 复用预分配目标，调用顶层零分配；GlobalSetup 一次性成本不计入测量。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class CollectionInto_Runtime
{
    private List<FlatEntityRuntime> _src = null!;
    private FlatDtoRuntime[] _dst = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = BenchmarkData.FlatEntities100Runtime;
        _dst = new FlatDtoRuntime[_src.Count];
        for (int i = 0; i < _dst.Length; i++)
        {
            _dst[i] = new FlatDtoRuntime();
        }
    }

    /// <summary>
    /// 复用填充调用（Runtime 表达式树回退路径；预期 Allocation = 0 B）
    /// </summary>
    [Benchmark]
    public void Run() => NCMapper.Mapper.MapListInto<FlatEntityRuntime, FlatDtoRuntime>(_src, _dst);
}

// ================================================================
// 5. Bidirectional E→D — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirED_NCMapper { [Benchmark] public FlatDto Run() => NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirED_Mapperly { [Benchmark] public FlatDto Run() => BenchmarkData.Mapperly.FlatEntityToDto(BenchmarkData.FlatEntity); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirED_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<FlatEntity, FlatDto>.NewConfig().Compile(); [Benchmark] public FlatDto Run() => BenchmarkData.FlatEntity.Adapt<FlatDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirED_AutoMapper { [Benchmark] public FlatDto Run() => AutoMapperHelper.Mapper.Map<FlatDto>(BenchmarkData.FlatEntity); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirED_Manual { [Benchmark] public FlatDto Run() => ManualMapper.FlatEntityToDto(BenchmarkData.FlatEntity); }

// ================================================================
// 6. Bidirectional D→E — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirDE_Ext { [Benchmark] public FlatEntity Run() => BenchmarkData.FlatDto.MapToFlatEntity()!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirDE_NCMapper { [Benchmark] public FlatEntity Run() => NCMapper.Mapper.Map<FlatDto, FlatEntity>(BenchmarkData.FlatDto)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirDE_Mapperly { [Benchmark] public FlatEntity Run() => BenchmarkData.Mapperly.FlatDtoToEntity(BenchmarkData.FlatDto); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirDE_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<FlatDto, FlatEntity>.NewConfig().Compile(); [Benchmark] public FlatEntity Run() => BenchmarkData.FlatDto.Adapt<FlatEntity>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirDE_AutoMapper { [Benchmark] public FlatEntity Run() => AutoMapperHelper.Mapper.Map<FlatEntity>(BenchmarkData.FlatDto); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirDE_Manual { [Benchmark] public FlatEntity Run() => ManualMapper.FlatDtoToEntity(BenchmarkData.FlatDto); }

// ================================================================
// 7. Action — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Action_NCMapper { [Benchmark] public FlatDto Run() => NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity, (s, d) => d.Name += "!")!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Action_Mapperly { [Benchmark] public FlatDto Run() { var d = BenchmarkData.Mapperly.FlatEntityToDto(BenchmarkData.FlatEntity); d.Name += "!"; return d; } }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Action_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<FlatEntity, FlatDto>.NewConfig().Compile(); [Benchmark] public FlatDto Run() { var d = BenchmarkData.FlatEntity.Adapt<FlatDto>(); d.Name += "!"; return d; } }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Action_AutoMapper { [Benchmark] public FlatDto Run() { var d = AutoMapperHelper.Mapper.Map<FlatDto>(BenchmarkData.FlatEntity); d.Name += "!"; return d; } }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Action_Manual { [Benchmark] public FlatDto Run() { var d = ManualMapper.FlatEntityToDto(BenchmarkData.FlatEntity); d.Name += "!"; return d; } }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Action_Ext { [Benchmark] public FlatDto Run() => BenchmarkData.FlatEntity.MapToFlatDto((s, d) => d.Name += "!")!; }

// ================================================================
// 8. EnumToString — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class EnumString_NCMapper { [Benchmark] public EnumToStringDto Run() => NCMapper.Mapper.Map<EnumEntity, EnumToStringDto>(BenchmarkData.EnumEntityData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class EnumString_Mapperly { [Benchmark] public EnumToStringDto Run() => BenchmarkData.Mapperly.EnumEntityToDto(BenchmarkData.EnumEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class EnumString_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<EnumEntity, EnumToStringDto>.NewConfig().Compile(); [Benchmark] public EnumToStringDto Run() => BenchmarkData.EnumEntityData.Adapt<EnumToStringDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class EnumString_AutoMapper { [Benchmark] public EnumToStringDto Run() => AutoMapperHelper.Mapper.Map<EnumToStringDto>(BenchmarkData.EnumEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class EnumString_Manual { [Benchmark] public EnumToStringDto Run() => ManualMapper.EnumEntityToDto(BenchmarkData.EnumEntityData); }

// ================================================================
// 9. Record — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Record_NCMapper { [Benchmark] public RecordDto Run() => NCMapper.Mapper.Map<RecordEntity, RecordDto>(BenchmarkData.RecordEntityData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Record_Mapperly { [Benchmark] public RecordDto Run() => BenchmarkData.Mapperly.RecordEntityToDto(BenchmarkData.RecordEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Record_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<RecordEntity, RecordDto>.NewConfig().Compile(); [Benchmark] public RecordDto Run() => BenchmarkData.RecordEntityData.Adapt<RecordDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Record_AutoMapper { [Benchmark] public RecordDto Run() => AutoMapperHelper.Mapper.Map<RecordDto>(BenchmarkData.RecordEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Record_Manual { [Benchmark] public RecordDto Run() => ManualMapper.RecordEntityToDto(BenchmarkData.RecordEntityData); }

// Record 10 参数 — 验证 JIT 内联策略
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class RecordBig_NCMapper { [Benchmark] public RecordBigDto Run() => NCMapper.Mapper.Map<RecordBigEntity, RecordBigDto>(BenchmarkData.RecordBigEntityData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class RecordBig_Manual { [Benchmark] public RecordBigDto Run() => ManualMapper.RecordBigEntityToDto(BenchmarkData.RecordBigEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class RecordBig_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<RecordBigEntity, RecordBigDto>.NewConfig().Compile(); [Benchmark] public RecordBigDto Run() => BenchmarkData.RecordBigEntityData.Adapt<RecordBigDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class RecordBig_AutoMapper { [Benchmark] public RecordBigDto Run() => AutoMapperHelper.Mapper.Map<RecordBigDto>(BenchmarkData.RecordBigEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class RecordBig_Ext { [Benchmark] public RecordBigDto Run() => BenchmarkData.RecordBigEntityData.MapToRecordBigDto()!; }

// NameDiff 10 属性 — 验证方法体长度对 JIT 内联的影响
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiffBig_NCMapper { [Benchmark] public NameDiffBigDto Run() => NCMapper.Mapper.Map<NameDiffBigEntity, NameDiffBigDto>(BenchmarkData.NameDiffBigEntityData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiffBig_Mapperly { [Benchmark] public NameDiffBigDto Run() => BenchmarkData.NameDiffMapperlyBig.NameDiffBigEntityToDto(BenchmarkData.NameDiffBigEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiffBig_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<NameDiffBigEntity, NameDiffBigDto>.NewConfig().Compile(); [Benchmark] public NameDiffBigDto Run() => BenchmarkData.NameDiffBigEntityData.Adapt<NameDiffBigDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiffBig_AutoMapper { [Benchmark] public NameDiffBigDto Run() => AutoMapperHelper.Mapper.Map<NameDiffBigDto>(BenchmarkData.NameDiffBigEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiffBig_Manual { [Benchmark] public NameDiffBigDto Run() => ManualMapper.NameDiffBigEntityToDto(BenchmarkData.NameDiffBigEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiffBig_Ext { [Benchmark] public NameDiffBigDto Run() => BenchmarkData.NameDiffBigEntityData.MapToNameDiffBigDto()!; }

// AutoFlatten 10 属性 — 验证方法体长度对 JIT 内联的影响
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class AutoFlattenBig_NCMapper { [Benchmark] public FlattenDto Run() => NCMapper.Mapper.Map<FlattenParentEntity, FlattenDto>(BenchmarkData.FlattenEntityData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class AutoFlattenBig_Mapperly { [Benchmark] public FlattenDto Run() => BenchmarkData.Mapperly.FlattenParentEntityToDto(BenchmarkData.FlattenEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class AutoFlattenBig_Manual { [Benchmark] public FlattenDto Run() => ManualMapper.FlattenParentEntityToDto(BenchmarkData.FlattenEntityData); }

// ================================================================
// 10. Nullable — 5 方式
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nullable_NCMapper { [Benchmark] public NullableDto Run() => NCMapper.Mapper.Map<NullableEntity, NullableDto>(BenchmarkData.NullableEntityData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nullable_Mapperly { [Benchmark] public NullableDto Run() => BenchmarkData.Mapperly.NullableEntityToDto(BenchmarkData.NullableEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nullable_Mapster { [GlobalSetup] public void Setup() => TypeAdapterConfig<NullableEntity, NullableDto>.NewConfig().Compile(); [Benchmark] public NullableDto Run() => BenchmarkData.NullableEntityData.Adapt<NullableDto>(); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nullable_AutoMapper { [Benchmark] public NullableDto Run() => AutoMapperHelper.Mapper.Map<NullableDto>(BenchmarkData.NullableEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nullable_Manual { [Benchmark] public NullableDto Run() => ManualMapper.NullableEntityToDto(BenchmarkData.NullableEntityData); }

// ================================================================
// 11. AutoFlatten — 3 方式（NCMapper + Mapperly + Manual）
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class AutoFlatten_NCMapper { [Benchmark] public FlattenDto Run() => NCMapper.Mapper.Map<FlattenParentEntity, FlattenDto>(BenchmarkData.FlattenEntityData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class AutoFlatten_Mapperly { [Benchmark] public FlattenDto Run() => BenchmarkData.Mapperly.FlattenParentEntityToDto(BenchmarkData.FlattenEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class AutoFlatten_Manual { [Benchmark] public FlattenDto Run() => ManualMapper.FlattenParentEntityToDto(BenchmarkData.FlattenEntityData); }

// ================================================================
// 12. DeepCopy — 6 方式（P0 Runtime + P1 Generated + Manual + Json + MemoryPack + MessagePack）
// ================================================================

// P0: 无 [MapFrom] → FastDeepCopyProvider（~350ns）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DeepCopy_Runtime { [Benchmark] public DeepCopyEntity Run() => NCMapper.Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData)!; }

// P1: 有 [MapFrom] → 源生成器 DeepCopy（~100ns）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DeepCopy_Generated { [Benchmark] public DeepCopyMapFromDto Run() => NCMapper.Mapper.DeepCopy(BenchmarkData.DeepCopyMapFromEntityData)!; }

// P2: ExpressionTreeDeepCopyProvider — 表达式树路径，支持循环引用检测
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DeepCopy_Expression
{
    [GlobalSetup]
    public void Setup() => DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();

    [Benchmark]
    public DeepCopyEntity Run() => NCMapper.Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData)!;
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DeepCopy_Manual { [Benchmark] public DeepCopyEntity Run() => ManualMapper.DeepCopy(BenchmarkData.DeepCopyEntityData); }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DeepCopy_Json { [Benchmark] public DeepCopyEntity Run() => JsonSerializer.Deserialize<DeepCopyEntity>(JsonSerializer.Serialize(BenchmarkData.DeepCopyEntityData))!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DeepCopy_MemoryPack { [Benchmark] public DeepCopyEntity Run() => MemoryPackSerializer.Deserialize<DeepCopyEntity>(MemoryPackSerializer.Serialize(BenchmarkData.DeepCopyEntityData))!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DeepCopy_MessagePack { [Benchmark] public DeepCopyEntity Run() => MessagePackSerializer.Deserialize<DeepCopyEntity>(MessagePackSerializer.Serialize(BenchmarkData.DeepCopyEntityData)); }

// ================================================================
// 13. Array / Dictionary / Compose — 新场景
// ================================================================

// 数组映射 — TSource[] → List<TTarget>（通过 MapList）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class ArrayMapping_NCMapper { [Benchmark] public List<FlatDto> Run() => NCMapper.Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntityArray)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class ArrayMapping_Manual { [Benchmark] public List<FlatDto> Run() => ManualMapper.FlatEntityListToDtoList(BenchmarkData.FlatEntityArray.ToList()); }

// 字典深拷贝 — Dictionary<string, T> 的 DeepCopy
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DictionaryMapping_NCMapper { [Benchmark] public Dictionary<string, DeepCopyEntity> Run() => NCMapper.Mapper.DeepCopy(BenchmarkData.DictDeepCopyData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class DictionaryMapping_Json { [Benchmark] public Dictionary<string, DeepCopyEntity> Run() => JsonSerializer.Deserialize<Dictionary<string, DeepCopyEntity>>(JsonSerializer.Serialize(BenchmarkData.DictDeepCopyData))!; }

// 3 源合成 — 订单 + 客户 + 产品 → ComposeMultiSourceDto（通过 Compose）
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class ComposeMultiSource_NCMapper { [Benchmark] public ComposeMultiSourceDto Run() => NCMapper.Mapper.Compose<ComposeMultiSourceDto>(BenchmarkData.ComposeOrderData, BenchmarkData.ComposeCustomerData, BenchmarkData.ComposeProductData)!; }


// ================================================================
// 13.5. 更新已有对象 — Map(source, target, action) 源生成器 vs 运行时 vs 手写 vs 竞品
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Update_NCMapper
{
    private FlatDto _target = new();
    [Benchmark] public FlatDto Run() { NCMapper.Mapper.Map(BenchmarkData.FlatEntity, _target); return _target; }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Update_Ext
{
    private FlatDto _target = new();
    [Benchmark] public FlatDto Run() { BenchmarkData.FlatEntity.MapToFlatDto(_target); return _target; }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Update_Manual
{
    private FlatDto _target = new();
    [Benchmark] public FlatDto Run() { ManualMapper.FlatEntityToUpdateDto(BenchmarkData.FlatEntity, _target); return _target; }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Update_Mapster
{
    private FlatDto _target = new();
    [GlobalSetup] public void Setup() => TypeAdapterConfig<FlatEntity, FlatDto>.NewConfig().Compile();
    [Benchmark] public FlatDto Run() { BenchmarkData.FlatEntity.Adapt(_target); return _target; }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Update_AutoMapper
{
    private FlatDto _target = new();
    [Benchmark] public FlatDto Run() { AutoMapperHelper.Mapper.Map(BenchmarkData.FlatEntity, _target); return _target; }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Update_Runtime
{
    private FlatDtoRuntime _target = new();
    [Benchmark] public FlatDtoRuntime Run() { NCMapper.Mapper.Map(BenchmarkData.FlatEntityRuntime, _target); return _target; }
}



// ================================================================
// 14. 无 [MapFrom] 对照 — 表达式树（MapperBase）路径
// 与同名场景结构一致，仅去掉 [MapFrom]，走运行时表达式树编译
// ================================================================

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Flat_Runtime { [Benchmark] public FlatDtoRuntime Run() => NCMapper.Mapper.Map<FlatEntityRuntime, FlatDtoRuntime>(BenchmarkData.FlatEntityRuntime)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nested_Runtime { [Benchmark] public NestedDtoRuntime Run() => NCMapper.Mapper.Map<NestedEntityRuntime, NestedDtoRuntime>(BenchmarkData.NestedEntityRuntime)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Record_Runtime { [Benchmark] public RecordDtoRuntime Run() => NCMapper.Mapper.Map<RecordEntityRuntime, RecordDtoRuntime>(BenchmarkData.RecordEntityRuntimeData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Nullable_Runtime { [Benchmark] public NullableDtoRuntime Run() => NCMapper.Mapper.Map<NullableEntityRuntime, NullableDtoRuntime>(BenchmarkData.NullableEntityRuntimeData)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class NameDiff_Runtime { [Benchmark] public NameDiffDtoRuntime Run() => NCMapper.Mapper.Map<NameDiffEntityRuntime, NameDiffDtoRuntime>(BenchmarkData.NameDiffEntityRuntime)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirED_Runtime { [Benchmark] public FlatDtoRuntime Run() => NCMapper.Mapper.Map<FlatEntityRuntime, FlatDtoRuntime>(BenchmarkData.FlatEntityRuntime)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class BidirDE_Runtime { [Benchmark] public FlatEntityRuntimeReverse Run() => NCMapper.Mapper.Map<FlatDtoRuntimeReverse, FlatEntityRuntimeReverse>(BenchmarkData.FlatDtoRuntimeReverse)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Action_Runtime { [Benchmark] public ActionDtoRuntime Run() => NCMapper.Mapper.Map<ActionEntityRuntime, ActionDtoRuntime>(BenchmarkData.ActionEntityRuntime, (s, d) => d.Name += "!")!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class EnumString_Runtime { [Benchmark] public EnumToStringDtoRuntime Run() => NCMapper.Mapper.Map<EnumEntityRuntime, EnumToStringDtoRuntime>(BenchmarkData.EnumEntityRuntime)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class AutoFlatten_Runtime { [Benchmark] public FlattenDtoRuntime Run() => NCMapper.Mapper.Map<FlattenParentEntityRuntime, FlattenDtoRuntime>(BenchmarkData.FlattenEntityRuntime)!; }

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchConfig.LaunchCount, warmupCount: BenchConfig.WarmupCount, iterationCount: BenchConfig.IterationCount)]
public class Collection_Runtime { [Benchmark] public List<FlatDtoRuntime> Run() => NCMapper.Mapper.MapList<FlatEntityRuntime, FlatDtoRuntime>(BenchmarkData.FlatEntities100Runtime)!; }

// ================================================================
// 快速验证 — 1×3×5，每个场景三种路径全覆盖
// ================================================================

internal static class QuickConfig
{
    public const int LaunchCount = 1;
    public const int WarmupCount = 3;
    public const int IterationCount = 5;
}

// --- Flat ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Flat_SG_Direct { [Benchmark] public FlatDto Run() => BenchmarkData.FlatEntity.MapToFlatDto()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Flat_SG_Map { [Benchmark] public FlatDto Run() => NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Flat_RT { [Benchmark] public FlatDtoRuntime Run() => NCMapper.Mapper.Map<FlatEntityRuntime, FlatDtoRuntime>(BenchmarkData.FlatEntityRuntime)!; }

// --- Nested ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Nested_SG_Direct { [Benchmark] public NestedDto Run() => BenchmarkData.NestedEntity.MapToNestedDto()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Nested_SG_Map { [Benchmark] public NestedDto Run() => NCMapper.Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Nested_RT { [Benchmark] public NestedDtoRuntime Run() => NCMapper.Mapper.Map<NestedEntityRuntime, NestedDtoRuntime>(BenchmarkData.NestedEntityRuntime)!; }

// --- Record ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Record_SG_Direct { [Benchmark] public RecordDto Run() => BenchmarkData.RecordEntityData.MapToRecordDto()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Record_SG_Map { [Benchmark] public RecordDto Run() => NCMapper.Mapper.Map<RecordEntity, RecordDto>(BenchmarkData.RecordEntityData)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Record_RT { [Benchmark] public RecordDtoRuntime Run() => NCMapper.Mapper.Map<RecordEntityRuntime, RecordDtoRuntime>(BenchmarkData.RecordEntityRuntimeData)!; }

// --- Nullable ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Nullable_SG_Direct { [Benchmark] public NullableDto Run() => BenchmarkData.NullableEntityData.MapToNullableDto()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Nullable_SG_Map { [Benchmark] public NullableDto Run() => NCMapper.Mapper.Map<NullableEntity, NullableDto>(BenchmarkData.NullableEntityData)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Nullable_RT { [Benchmark] public NullableDtoRuntime Run() => NCMapper.Mapper.Map<NullableEntityRuntime, NullableDtoRuntime>(BenchmarkData.NullableEntityRuntimeData)!; }

// --- NameDiff ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_NameDiff_SG_Direct { [Benchmark] public NameDiffDto Run() => BenchmarkData.NameDiffEntity.MapToNameDiffDto()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_NameDiff_SG_Map { [Benchmark] public NameDiffDto Run() => NCMapper.Mapper.Map<NameDiffEntity, NameDiffDto>(BenchmarkData.NameDiffEntity)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_NameDiff_RT { [Benchmark] public NameDiffDtoRuntime Run() => NCMapper.Mapper.Map<NameDiffEntityRuntime, NameDiffDtoRuntime>(BenchmarkData.NameDiffEntityRuntime)!; }

// --- BidirED ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_BidirED_SG_Direct { [Benchmark] public FlatDto Run() => BenchmarkData.FlatEntity.MapToFlatDto()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_BidirED_SG_Map { [Benchmark] public FlatDto Run() => NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_BidirED_RT { [Benchmark] public FlatDtoRuntime Run() => NCMapper.Mapper.Map<FlatEntityRuntime, FlatDtoRuntime>(BenchmarkData.FlatEntityRuntime)!; }

// --- Action ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Action_SG_Direct { [Benchmark] public FlatDto Run() => BenchmarkData.FlatEntity.MapToFlatDto((s, d) => d.Name += "!")!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Action_SG_Map { [Benchmark] public FlatDto Run() => NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity, (s, d) => d.Name += "!")!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Action_RT { [Benchmark] public ActionDtoRuntime Run() => NCMapper.Mapper.Map<ActionEntityRuntime, ActionDtoRuntime>(BenchmarkData.ActionEntityRuntime, (s, d) => d.Name += "!")!; }

// --- EnumString ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_EnumString_SG_Direct { [Benchmark] public EnumToStringDto Run() => BenchmarkData.EnumEntityData.MapToEnumToStringDto()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_EnumString_SG_Map { [Benchmark] public EnumToStringDto Run() => NCMapper.Mapper.Map<EnumEntity, EnumToStringDto>(BenchmarkData.EnumEntityData)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_EnumString_RT { [Benchmark] public EnumToStringDtoRuntime Run() => NCMapper.Mapper.Map<EnumEntityRuntime, EnumToStringDtoRuntime>(BenchmarkData.EnumEntityRuntime)!; }

// --- AutoFlatten ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_AutoFlatten_SG_Direct { [Benchmark] public FlattenDto Run() => BenchmarkData.FlattenEntityData.MapToFlattenDto()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_AutoFlatten_SG_Map { [Benchmark] public FlattenDto Run() => NCMapper.Mapper.Map<FlattenParentEntity, FlattenDto>(BenchmarkData.FlattenEntityData)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_AutoFlatten_RT { [Benchmark] public FlattenDtoRuntime Run() => NCMapper.Mapper.Map<FlattenParentEntityRuntime, FlattenDtoRuntime>(BenchmarkData.FlattenEntityRuntime)!; }

// --- Collection ---
[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Collection_SG_Direct { [Benchmark] public List<FlatDto> Run() => BenchmarkData.FlatEntities100.MapToFlatDtoList()!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Collection_SG_Map { [Benchmark] public List<FlatDto> Run() => NCMapper.Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100)!; }

[MemoryDiagnoser]
[DisassemblyDiagnoser(printSource: true, maxDepth: 3)]
[SimpleJob(launchCount: QuickConfig.LaunchCount, warmupCount: QuickConfig.WarmupCount, iterationCount: QuickConfig.IterationCount)]
public class Quick_Collection_RT { [Benchmark] public List<FlatDtoRuntime> Run() => NCMapper.Mapper.MapList<FlatEntityRuntime, FlatDtoRuntime>(BenchmarkData.FlatEntities100Runtime)!; }

// ================================================================
// Entry Point
// ================================================================

/// <summary>
/// 基准程序入口 — Windows 上锁定 P-core（0xFFFF，i7-12700K 逻辑核 0-15）；
/// 非 Windows（如 Linux 服务器）不锁定亲和性，交由外部 taskset/numactl 控制。
/// </summary>
[SupportedOSPlatform("windows")]
class Program
{
    /// <summary>
    /// 包装 BenchmarkRunner.Run，检测 BDN 自动生成项目构建失败（瞬态故障）并自动重试。
    /// 判定条件：所有 report 的 ResultStatistics 为 null（即 0 个 benchmark 被执行）。
    /// </summary>
    static void RunWithRetry(Type type, int maxRetries = 2, int delaySeconds = 5)
    {
        for (int attempt = 1; ; attempt++)
        {
            var summary = BenchmarkRunner.Run(type);

            bool buildFailed = summary.Reports.Length > 0
                && summary.Reports.All(r => r.ResultStatistics == null);

            if (!buildFailed || attempt > maxRetries)
                return;

            Console.WriteLine($"  WARNING: {type.Name} build failed (attempt {attempt}/{maxRetries + 1}), retrying in {delaySeconds}s...");
            Thread.Sleep(delaySeconds * 1000);
        }
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 编译期基准骨架 — 独立入口，刻意置于映射预热之前：
        // 下方预热针对运行时映射路径（自研 args 分发，不解析 BDN 的 --filter），
        // 而源生成器 driver 基准不含运行时映射，无需预热，也免去 Windows 亲和性锁定的隐式依赖。
        if (args.Length > 0 && args[0] == "sourcegen-driver")
        {
            Console.WriteLine("=== Running SourceGen driver benchmark (compile-time skeleton) ===");
            RunWithRetry(typeof(SourceGenDriverBench));
            return;
        }

        // 扩展夹具入口：与 sourcegen-driver 同为编译期基准，
        // 刻意置于映射预热之前（源生成器 driver 基准不含运行时映射，无需预热）。
        if (args.Length > 0 && args[0] == "sourcegen-fixtures")
        {
            Console.WriteLine("=== Running SourceGen driver fixture benchmarks ===");
            Type[] fixtures =
            [
                typeof(SourceGenDriverBench.MapListShape),
                typeof(SourceGenDriverBench.MutualPairs3),
                typeof(SourceGenDriverBench.WideProperties),
                typeof(SourceGenDriverBench.DiagScale),
                typeof(SourceGenDriverBench.RepeatedDeclarations3),
                typeof(SourceGenDriverBench.SelfRecursiveInline),
                typeof(SourceGenDriverBench.DeepCopyGraphShared),
                typeof(SourceGenDriverBench.FluentChainSubclass),
            ];
            foreach (var fixture in fixtures)
            {
                RunWithRetry(fixture);
            }
            return;
        }

        // per-iteration 冷路径基准入口（SourceGenDriverColdBench 9 夹具）。
        // 进程级隔离形态（LaunchCount=IterationCount=15, WarmupCount=0）：每迭代一进程，
        // GlobalSetup 新建 compilation（symbol 全新 → SourceGen 符号键控 CWT 全 miss = 冷路径）。
        // 平行夹具：零触既有热夹具；冷源文本经反射从热夹具收割。
        if (args.Length > 0 && args[0] == "sourcegen-cold")
        {
            Console.WriteLine("=== Running SourceGen driver COLD benchmarks (per-iteration new compilation) ===");
            // args[1] 可选：夹具名过滤（如 MapListShapeCold），便于探索期单夹具运行
            var coldFilter = args.Length > 1 ? args[1] : null;
            Type[] coldFixtures =
            [
                typeof(SourceGenDriverColdBench),
                typeof(MapListShapeCold),
                typeof(MutualPairs3Cold),
                typeof(WidePropertiesCold),
                typeof(DiagScaleCold),
                typeof(RepeatedDeclarations3Cold),
                typeof(SelfRecursiveInlineCold),
                typeof(DeepCopyGraphSharedCold),
                typeof(FluentChainSubclassCold),
            ];
            foreach (var fixture in coldFixtures)
            {
                if (coldFilter != null && !fixture.Name.Contains(coldFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                RunWithRetry(fixture);
            }
            return;
        }

        // CPU 亲和性锁定（仅 Windows）: 仅 P-core (i7-12700K: 逻辑核 0-15 = 8P×2HT)
        // 避免 E-core 迁移导致基准抖动, BDN 子进程继承父进程亲和性。
        // Linux 上不锁定：0xFFFF 是 Windows 混合架构（P/E core）的专用掩码，
        // 在 Linux 上会被解释为"仅前 16 个逻辑核"，对核数不同的服务器属错误约束
        // （如 32 核机型只用到 NUMA node0 的半数核），且会与 NUMA 拓扑冲突导致跨节点访存。
        // Linux 侧需要限核时应在外部用 taskset/numactl 显式指定，不写死在程序里。
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Process.GetCurrentProcess().ProcessorAffinity = new IntPtr(0xFFFF);
                Console.WriteLine("CPU affinity locked to P-core (0xFFFF, logical 0-15)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: CPU affinity lock failed: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"CPU affinity lock skipped (non-Windows; {Environment.ProcessorCount} logical cores available)");
        }

        // 预热（进程级：JIT + 静态缓存 + CPU 热稳定，约 10 秒，用户裁决 2026-08-13）
        // 覆盖 self 与 rivals 全部标准场景；self/rivals 各自进程启动时自动执行，
        // 冷却后启动的竞品进程同样获得一致预热，消除"低温高频"测量偏差。
        Console.WriteLine("Warming up (~10s)...");
        var warmupSw = System.Diagnostics.Stopwatch.StartNew();
        long warmupIters = 0;
        while (warmupSw.Elapsed < TimeSpan.FromSeconds(10))
        {
            // NCMapper 标准场景（self：源生成主路径 + 双向 + DeepCopy 各实现）
            NCMapper.Mapper.Map<FlatEntity, FlatDto>(BenchmarkData.FlatEntity);
            NCMapper.Mapper.Map<NameDiffEntity, NameDiffDto>(BenchmarkData.NameDiffEntity);
            NCMapper.Mapper.Map<NestedEntity, NestedDto>(BenchmarkData.NestedEntity);
            NCMapper.Mapper.Map<RecordEntity, RecordDto>(BenchmarkData.RecordEntityData);
            NCMapper.Mapper.Map<NullableEntity, NullableDto>(BenchmarkData.NullableEntityData);
            NCMapper.Mapper.Map<EnumEntity, EnumToStringDto>(BenchmarkData.EnumEntityData);
            NCMapper.Mapper.Map<FlattenParentEntity, FlattenDto>(BenchmarkData.FlattenEntityData);
            NCMapper.Mapper.MapList<FlatEntity, FlatDto>(BenchmarkData.FlatEntities100);
            NCMapper.Mapper.Map<FlatDto, FlatEntity>(BenchmarkData.FlatDto);          // BidirDE
            NCMapper.Mapper.DeepCopy(BenchmarkData.DeepCopyEntityData);              // Runtime/Expression
            NCMapper.Mapper.DeepCopy(BenchmarkData.DeepCopyMapFromEntityData);       // Generated
            // 表达式树/Runtime 路径（无 [MapFrom]）
            NCMapper.Mapper.Map<FlatEntityRuntime, FlatDtoRuntime>(BenchmarkData.FlatEntityRuntime);
            NCMapper.Mapper.Map<NestedEntityRuntime, NestedDtoRuntime>(BenchmarkData.NestedEntityRuntime);
            NCMapper.Mapper.Map<RecordEntityRuntime, RecordDtoRuntime>(BenchmarkData.RecordEntityRuntimeData);
            NCMapper.Mapper.Map<NullableEntityRuntime, NullableDtoRuntime>(BenchmarkData.NullableEntityRuntimeData);
            NCMapper.Mapper.Map<NameDiffEntityRuntime, NameDiffDtoRuntime>(BenchmarkData.NameDiffEntityRuntime);
            NCMapper.Mapper.Map<FlatDtoRuntimeReverse, FlatEntityRuntimeReverse>(BenchmarkData.FlatDtoRuntimeReverse);
            NCMapper.Mapper.Map<ActionEntityRuntime, ActionDtoRuntime>(BenchmarkData.ActionEntityRuntime, (s, d) => d.Name += "!");
            NCMapper.Mapper.Map<EnumEntityRuntime, EnumToStringDtoRuntime>(BenchmarkData.EnumEntityRuntime);
            NCMapper.Mapper.Map<FlattenParentEntityRuntime, FlattenDtoRuntime>(BenchmarkData.FlattenEntityRuntime);
            NCMapper.Mapper.MapList<FlatEntityRuntime, FlatDtoRuntime>(BenchmarkData.FlatEntities100Runtime);
            // 竞品（rivals 场景）
            BenchmarkData.Mapperly.FlatEntityToDto(BenchmarkData.FlatEntity);
            BenchmarkData.FlatEntity.Adapt<FlatDto>();
            AutoMapperHelper.Mapper.Map<FlatDto>(BenchmarkData.FlatEntity);
            ManualMapper.FlatEntityToDto(BenchmarkData.FlatEntity);
            warmupIters++;
        }
        warmupSw.Stop();
        Console.WriteLine($"Warmup done ({warmupIters} iterations, {warmupSw.Elapsed.TotalSeconds:F1}s).");

        // MemoryPack / MessagePack 预热（各自内部缓存，只需一次）
        MemoryPackSerializer.Serialize(BenchmarkData.DeepCopyEntityData);
        MessagePackSerializer.Serialize(BenchmarkData.DeepCopyEntityData);

        // 源生成器状态检查
        Console.WriteLine();
        Console.WriteLine("=== Source Generator Status ===");
        Console.WriteLine($"  FlatEntity→FlatDto:         {Mapper.HasGenerated<FlatEntity, FlatDto>()}");
        Console.WriteLine($"  NameDiffEntity→NameDiffDto: {Mapper.HasGenerated<NameDiffEntity, NameDiffDto>()}");
        Console.WriteLine($"  NestedEntity→NestedDto:     {Mapper.HasGenerated<NestedEntity, NestedDto>()}");
        Console.WriteLine($"  FlatDto→FlatEntity:         {Mapper.HasGenerated<FlatDto, FlatEntity>()}");
        Console.WriteLine($"  EnumEntity→EnumToStringDto: {Mapper.HasGenerated<EnumEntity, EnumToStringDto>()}");
        Console.WriteLine($"  RecordEntity→RecordDto:     {Mapper.HasGenerated<RecordEntity, RecordDto>()}");
        Console.WriteLine($"  NullableEntity→NullableDto: {Mapper.HasGenerated<NullableEntity, NullableDto>()}");
        Console.WriteLine($"  FlattenParent→FlattenDto:   {Mapper.HasGenerated<FlattenParentEntity, FlattenDto>()}");
        Console.WriteLine($"  DeepCopyMapFrom (Generated): {Mapper.HasGeneratedDeepCopy<DeepCopyMapFromDto>()}");
        Console.WriteLine($"  DeepCopy (Runtime):          {Mapper.HasGeneratedDeepCopy<DeepCopyEntity>()}");
        Console.WriteLine("================================");
        Console.WriteLine();

        // Defender 预扫描模式（bench_lib.sh 的 __prescan__ 调用）：
        //   仅完成进程启动与程序集加载（含上方 warmup 迭代与 MemoryPack/MessagePack 序列化预热），
        //   随即退出，使 Windows Defender 实时扫描集中在非计时阶段，
        //   正式 benchmark 读取同一批 DLL 时命中扫描缓存，消除计时干扰。
        if (args.Length > 0 && args[0] == "__prescan__")
        {
            return;
        }

        // GC 诊断模式: dotnet run -c Release -- gcdiag [iterations] [batchSize] [path]
        //   path: all | runtime | generated | expression
        //   用于定位 DeepCopy 各路径在不同框架下的 GC 压力差异 (诊断 net9 Expression 回退根因)
        if (args.Length > 0 && args[0].Equals("gcdiag", StringComparison.OrdinalIgnoreCase))
        {
            DeepCopyGcDiag.Run(args.Length > 1 ? args[1..] : Array.Empty<string>());
            return;
        }

        if (args.Length > 0 && args[0] == "ncmapper")
        {
            Console.WriteLine("=== Running NCMapper-only benchmarks ===");
            Console.WriteLine();
            var ncmapperTypes = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.Name.EndsWith("_NCMapper") && t.GetMethod("Run") != null)
                .Where(t => !t.Name.Contains("Big"))   // 用户裁决 2026-08-13：Big 后缀场景全部去除
                .OrderBy(t => t.Name)
                .ToArray();
            foreach (var type in ncmapperTypes)
            {
                Console.WriteLine($"  Running {type.Name}...");
                try { RunWithRetry(type); }
                catch (Exception ex) { Console.WriteLine($"  ERROR: {ex.Message}"); }
            }
            Console.WriteLine();
            Console.WriteLine("=== NCMapper benchmarks complete ===");
            return;
        }

        if (args.Length > 0 && args[0] == "ncmapper_all")
        {
            Console.WriteLine("=== Running NCMapper all-path benchmarks (NCMapper + Ext + Runtime) ===");
            Console.WriteLine();
            var allNcmapperTypes = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.GetMethod("Run") != null
                    && (t.Name.EndsWith("_NCMapper") || t.Name.EndsWith("_Ext") || t.Name.EndsWith("_Runtime")
                        || t.Name.EndsWith("_Expression") || t.Name.EndsWith("_Generated"))
                    && !t.Name.Contains("Quick")
                    && !t.Name.Contains("Big"))   // 用户裁决 2026-08-13：Big 后缀场景全部去除
                .OrderBy(t => t.Name)
                .ToArray();
            Console.WriteLine($"  Total: {allNcmapperTypes.Length} benchmark classes");
            Console.WriteLine();
            foreach (var type in allNcmapperTypes)
            {
                Console.WriteLine($"  Running {type.Name}...");
                try { RunWithRetry(type); }
                catch (Exception ex) { Console.WriteLine($"  ERROR: {ex.Message}"); }
            }
            Console.WriteLine();
        Console.WriteLine("=== NCMapper all-path benchmarks complete ===");
        return;
    }

    // 性能回归基线模式 (2026-07-07 定型, 2026-07-08 新增 baseline_rivals)
    //   baseline       = 全 7 变体同场 (自己组件 Ext/NCMapper/Runtime + 竞品 Mapperly/Manual/Mapster/AutoMapper + DeepCopy 全变体)
    //                    单进程一次跑完 -> PGO 跨全部变体饱和, 作为竞品同场快照 + 7 变体数据
    //   baseline_self  = 仅自己组件 3 变体 (Ext/NCMapper/Runtime + DeepCopy 的 Generated/Manual/Runtime)
    //                    单进程独占跑完 -> PGO 仅跨 3 变体饱和, 作为今后回归基线 (层1)
    //   baseline_rivals = 仅竞品变体 (标准场景 AutoMapper/Manual/Mapperly/Mapster + DeepCopy 的 Json/MemoryPack/MessagePack/Manual)
    //                    单进程冷启动独占跑完 -> PGO 仅跨竞品变体饱和, 与 baseline_self 同协议(冷启动/全局 SimpleJob), 用于"竞品差距"对比
    //   baseline_ext_mapperly = 同段裁决模式 (2026-08-30, 裁决 net10 的 Ext vs Mapperly 跨段污染问题):
    //                    仅 Ext + Mapperly + Manual (标准场景) + DeepCopy_Manual (锚点), 同段同刻测量,
    //                    消除 self 段与 rivals 段的跨段环境差异; 不含 AutoMapper (与裁决无关, 且已证段偏)
    // 各模式分进程运行, 确保"回归运行"与"基线自己组件部分"PGO 饱和广度一致, 消除伪差。
    // 排除规则: Quick_* / *_Layer* / *Big* / ArrayMapping* / DictionaryMapping* / ComposeMultiSource*
    //   （*Big* 为用户裁决 2026-08-13：AutoFlattenBig/NameDiffBig/RecordBig 场景全部去除）
    //   （Update* 2026-08-19 起纳入档1：全量跑包含 Update 场景对比，baseline_self 含
    //     Update_Ext/NCMapper/Runtime 3 变体，baseline_rivals 含 Update_Manual/Mapster/AutoMapper 3 变体）
    if (args.Length > 0 && (args[0] == "baseline" || args[0] == "baseline_self" || args[0] == "baseline_rivals" || args[0] == "baseline_ext_mapperly"))
    {
        string mode = args[0];
        bool selfOnly = mode == "baseline_self";
        bool rivalsOnly = mode == "baseline_rivals";
        bool extMapperly = mode == "baseline_ext_mapperly";
        Console.WriteLine($"=== Running baseline{(extMapperly ? " (ext-vs-mapperly same-segment, 2026-08-30 裁决)" : selfOnly ? " (self-only, 3-variant)" : rivalsOnly ? " (rivals-only, competitor variants)" : " (full, 7-variant)")} ===");
        Console.WriteLine();

        string SuffixOf(string name) => name.Contains("_") ? name.Substring(name.LastIndexOf('_') + 1) : name;
        bool IsSelfComp(string name, string comp) =>
            name.StartsWith("DeepCopy_")
                ? (comp == "Generated" || comp == "Expression" || comp == "Manual" || comp == "Runtime")
                : (comp == "NCMapper" || comp == "Ext" || comp == "Runtime");
        bool IsFullComp(string name, string comp) =>
            name.StartsWith("DeepCopy_")
                ? (comp == "Generated" || comp == "Expression" || comp == "Manual" || comp == "Runtime" || comp == "Json" || comp == "MemoryPack" || comp == "MessagePack")
                : (comp == "NCMapper" || comp == "Ext" || comp == "Runtime" || comp == "Mapperly" || comp == "Manual" || comp == "Mapster" || comp == "AutoMapper");
        bool IsRivalComp(string name, string comp) =>
            name.StartsWith("DeepCopy_")
                ? (comp == "Json" || comp == "MemoryPack" || comp == "MessagePack" || comp == "Manual")
                : (comp == "AutoMapper" || comp == "Manual" || comp == "Mapperly" || comp == "Mapster");
        // 同段裁决：Ext + Mapperly + Manual（标准场景）+ DeepCopy_Manual（锚点）；剔除 AutoMapper 与其余 DeepCopy 变体
        bool IsExtMapperlyComp(string name, string comp) =>
            name.StartsWith("DeepCopy_")
                ? (comp == "Manual")
                : (comp == "Ext" || comp == "Mapperly" || comp == "Manual");

        var baselineTypes = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetMethod("Run") != null)
            .Where(t => !t.Name.Contains("Quick") && !t.Name.Contains("_Layer"))
            .Where(t => !t.Name.Contains("Big"))   // 用户裁决 2026-08-13：Big 后缀场景全部去除
            .Where(t => !t.Name.StartsWith("ArrayMapping") && !t.Name.StartsWith("DictionaryMapping") && !t.Name.StartsWith("ComposeMultiSource"))
            .Where(t => { string comp = SuffixOf(t.Name); return extMapperly ? IsExtMapperlyComp(t.Name, comp) : selfOnly ? IsSelfComp(t.Name, comp) : rivalsOnly ? IsRivalComp(t.Name, comp) : IsFullComp(t.Name, comp); })
            .OrderBy(t => t.Name)
            .ToArray();

        Console.WriteLine($"  Total: {baselineTypes.Length} benchmark classes");
        Console.WriteLine();
        foreach (var type in baselineTypes)
        {
            Console.WriteLine($"  Running {type.Name}...");
            try { RunWithRetry(type); }
            catch (Exception ex) { Console.WriteLine($"  ERROR: {ex.Message}"); }
        }
        Console.WriteLine();
        Console.WriteLine("=== baseline complete ===");
        return;
    }

    if (args.Length > 0 && args[0] != "all")
        {
            // 先尝试精确类名匹配（如 Flat_NCMapper、Nested_Mapperly 等）
            var type = System.Reflection.Assembly.GetExecutingAssembly().GetType($"NCMapper.Benchmarks.{args[0]}");
            if (type != null)
            {
                RunWithRetry(type);
                return;
            }

            // 再尝试前缀匹配（场景名如 Quick、Flat、Nested、Runtime 等）
            var prefix = args[0];
            var types = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.Name.StartsWith(prefix + "_") && t.GetMethod("Run") != null)
                .Where(t => !t.Name.Contains("_Layer"))
                .OrderBy(t => t.Name)
                .ToArray();

            if (types.Length > 0)
            {
                Console.WriteLine($"=== Running {prefix} benchmarks ({types.Length} classes) ===");
                Console.WriteLine();
                foreach (var t in types)
                {
                    Console.WriteLine($"  Running {t.Name}...");
                    try { RunWithRetry(t); }
                    catch (Exception ex) { Console.WriteLine($"  ERROR: {ex.Message}"); }
                }
                Console.WriteLine();
                Console.WriteLine($"=== {prefix} benchmarks complete ===");
                return;
            }

            Console.WriteLine($"Class not found: {args[0]}");
            return;
        }

        Console.WriteLine("=== Running all benchmarks ===");
        Console.WriteLine();

        // 按场景分组运行
        var groups = new[]
        {
            "Flat", "NameDiff", "Nested", "Collection",
            "BidirED", "BidirDE", "Action",
            "EnumString", "Record", "Nullable", "AutoFlatten", "DeepCopy",
            "ArrayMapping", "DictionaryMapping", "ComposeMultiSource",
            "Update",
            "Runtime", "Quick"
        };

        foreach (var group in groups)
        {
            Console.WriteLine($"--- {group} ---");
            var types = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.Name.StartsWith(group + "_") && t.GetMethod("Run") != null)
                .Where(t => !t.Name.Contains("_Layer"))
                .OrderBy(t => t.Name)
                .ToArray();

            foreach (var type in types)
            {
                Console.WriteLine($"  Running {type.Name}...");
                try { RunWithRetry(type); }
                catch (Exception ex) { Console.WriteLine($"  ERROR: {ex.Message}"); }
            }
            Console.WriteLine();
        }

        Console.WriteLine("=== All benchmarks complete ===");
    }
}
