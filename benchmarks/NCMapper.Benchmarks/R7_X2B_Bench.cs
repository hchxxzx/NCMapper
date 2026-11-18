using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using NCMapper;
using NCMapper.Attributes;

namespace NCMapper.Benchmarks;

// ================================================================
// R7 基准量化：X2-B Dto→Entity 序列化写回热路径成本
// ================================================================
// 门禁判定：
//   - Benchmark 1（Dto→Entity + IsFromJson）：改造前为"零成本跳过"（不映射），
//     改造后为"序列化写回"——功能从无到有，量化新增成本绝对量级（ns/op），
//     评估 10 万条记录批量导出场景的绝对吞吐影响，判定接受或需 escape hatch。
//   - Benchmark 2（Dto→Entity 无 IsFromJson）：X2-B 反向匹配查找开销——15% 阈值适用。
//   - Benchmark 3（Entity→Dto 规范方向）：X2-B 不进入（sourceIsDefine 分支），对照组零回归。
// 全部强制运行时路径（RegisterGenerated null），测运行时引擎（X2-B 只改运行时）。

/// <summary>
/// R7 基准：X2-B 运行时 Dto→Entity 反向消费性能量化
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class R7X2BBench
{
    private R7X2BDto _isJsonDto = null!;
    private R7PlainDto _plainDto = null!;
    private R7NormEntity _normEntity = null!;

    /// <summary>
    /// 初始化：强制三个类型对走运行时路径（取消源生成器自动注册）
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        Mapper.RegisterGenerated<R7X2BEntity, R7X2BDto>(null!);
        Mapper.RegisterGeneratedCore<R7X2BEntity, R7X2BDto>(null!);
        Mapper.RegisterGenerated<R7X2BDto, R7X2BEntity>(null!);
        Mapper.RegisterGeneratedCore<R7X2BDto, R7X2BEntity>(null!);

        Mapper.RegisterGenerated<R7PlainEntity, R7PlainDto>(null!);
        Mapper.RegisterGeneratedCore<R7PlainEntity, R7PlainDto>(null!);
        Mapper.RegisterGenerated<R7PlainDto, R7PlainEntity>(null!);
        Mapper.RegisterGeneratedCore<R7PlainDto, R7PlainEntity>(null!);

        Mapper.RegisterGenerated<R7NormEntity, R7NormDto>(null!);
        Mapper.RegisterGeneratedCore<R7NormEntity, R7NormDto>(null!);
        Mapper.RegisterGenerated<R7NormDto, R7NormEntity>(null!);
        Mapper.RegisterGeneratedCore<R7NormDto, R7NormEntity>(null!);

        _isJsonDto = new R7X2BDto { Id = 7, Config = new R7X2BConfig { Theme = "Light", Level = 3 } };
        _plainDto = new R7PlainDto { Id = 7, Name = "Bench" };
        _normEntity = new R7NormEntity { Id = 7, Name = "Bench" };
    }

    /// <summary>
    /// Benchmark 1（R7 核心）：Dto→Entity 带 IsFromJson 标注 — 基线跳过 vs HEAD 序列化写回
    /// </summary>
    [Benchmark]
    public R7X2BEntity? DtoToEntity_IsFromJson() => Mapper.Map<R7X2BDto, R7X2BEntity>(_isJsonDto);

    /// <summary>
    /// Benchmark 2：Dto→Entity 无 IsFromJson — X2-B 反向匹配查找开销（15% 阈值适用）
    /// </summary>
    [Benchmark]
    public R7PlainEntity? DtoToEntity_Plain() => Mapper.Map<R7PlainDto, R7PlainEntity>(_plainDto);

    /// <summary>
    /// Benchmark 3（对照）：Entity→Dto 规范方向 — X2-B 不进入，零回归验证
    /// </summary>
    [Benchmark]
    public R7NormDto? EntityToDto_Normal() => Mapper.Map<R7NormEntity, R7NormDto>(_normEntity);
}

// ===== R7 基准类型（仿 U18/R18 结构）=====

/// <summary>
/// R7 核心场景实体 — MapFrom 标此处（非规范），Dto→Entity 时 targetIsDefine=true 触发 X2-B
/// </summary>
[MapFrom(typeof(R7X2BDto))]
public class R7X2BEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 配置 JSON 字符串（X2-B 序列化写回目标）
    /// </summary>
    public string ConfigJson { get; set; } = "";
}

/// <summary>
/// R7 配置对象（JSON 序列化/反序列化载体）
/// </summary>
public class R7X2BConfig
{
    /// <summary>
    /// 主题
    /// </summary>
    public string Theme { get; set; } = "Light";

    /// <summary>
    /// 级别
    /// </summary>
    public int Level { get; set; } = 1;
}

/// <summary>
/// R7 核心场景 DTO — Config 属性带 IsFromJson 标注（源侧标注）
/// </summary>
public class R7X2BDto
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 配置对象（Dto→Entity 时序列化写回 ConfigJson）
    /// </summary>
    public R7X2BConfig Config { get; set; } = new();
}

/// <summary>
/// R7 普通场景实体 — MapFrom 标此处，Dto→Entity 时 targetIsDefine=true（测 X2-B 查找开销）
/// </summary>
[MapFrom(typeof(R7PlainDto))]
public class R7PlainEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// R7 普通场景 DTO（无 IsFromJson 标注）
/// </summary>
public class R7PlainDto
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// R7 对照场景实体 — MapFrom 标此处（规范），Entity→Dto 时 sourceIsDefine=true（X2-B 不进入）
/// </summary>
[MapFrom(typeof(R7NormDto))]
public class R7NormEntity
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// R7 对照场景 DTO（无标注，规范 Entity→Dto）
/// </summary>
public class R7NormDto
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";
}
