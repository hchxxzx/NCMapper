using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper.Dapper;

namespace NCMapper.Benchmarks;

// ================================================================
// Dapper 双轨执行路径对比基准
// ----------------------------------------------------------------
// 轨道 A（委托）：字符串版公共入口 → s_queryFunc 等 Lazy 委托
//   （Expression.Compile 编译产物，强类型调 Dapper 分离参数重载）
// 轨道 B（直调）：CommandDefinition 公共入口 → Dapper 扩展方法直调
//   （DapperDtoSelector.cs QuerySource(CommandDefinition) 路径）
//
// 方法论：
//   多遍运行取中位数（本基准跨进程跑 3 遍）+ DriftBench 无关场景
//   （纯内存映射，无 DB）作环境漂移对照——双轨差异 < 漂移波动时判噪声。
//
// 命名纪律：[Benchmark] 方法名刻意避开 "Run" 且类名后缀不匹配
//   baseline 竞争形态（Program.cs baseline 过滤：GetMethod("Run") + 后缀
//   IsSelfComp/IsRivalComp），确保不被档 1 全量（run_all.sh）误吸收
//   （约束：基准改动不污染对比产物）。
//
// 运行方式（精确类名，Program.cs :1220 路径）：
//   dotnet run -c Release --framework net10.0 -- DapperDualTrackQueryBench
//   （每遍一个进程；跑 3 遍取中位数，附 DriftBench 漂移对照）
// ================================================================

/// <summary>
/// 双轨对比 ①：列表查询（Query 同步）— 委托 vs 直调 + 原生 Dapper 对照
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class DapperDualTrackQueryBench
{
    private SqliteConnection _connection = null!;
    private const string Sql = "SELECT * FROM UserEntity ORDER BY Id";

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE UserEntity (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Age INTEGER NOT NULL,
                Email TEXT NOT NULL,
                Phone TEXT NOT NULL
            )
            """);
        for (int i = 1; i <= 100; i++)
        {
            _connection.Execute(
                "INSERT INTO UserEntity (Id, Name, Age, Email, Phone) VALUES (@Id, @Name, @Age, @Email, @Phone)",
                new { Id = i, Name = $"User{i}", Age = 20 + i % 50, Email = $"user{i}@test.com", Phone = $"138{i:00000000}" });
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    /// <summary>轨道 A：字符串版 Query — s_queryFunc 委托编译路径（基准基线）</summary>
    [Benchmark(Baseline = true)]
    public List<DapperUserDto> Query_String_Delegate()
        => _connection.AsDto<DapperUserEntity>().Query<DapperUserDto>(Sql).ToList();

    /// <summary>轨道 B：CommandDefinition Query — 直调路径（每次构造 CommandDefinition，等价真实用法）</summary>
    [Benchmark]
    public List<DapperUserDto> Query_CommandDef_Direct()
        => _connection.AsDto<DapperUserEntity>().Query<DapperUserDto>(new CommandDefinition(Sql)).ToList();

    /// <summary>原生对照：Dapper 原生 Query（无 AsDto 映射管道）— 分离查询开销与映射开销</summary>
    [Benchmark]
    public List<DapperUserEntity> Dapper_Native_Query()
        => _connection.Query<DapperUserEntity>(Sql).ToList();
}

/// <summary>
/// 双轨对比 ②：单行查询（QueryFirst / QueryFirstOrDefault 同步）— 委托 vs 直调
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class DapperDualTrackSingleBench
{
    private SqliteConnection _connection = null!;
    private const string Sql = "SELECT * FROM UserEntity WHERE Id = 42";

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE UserEntity (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Age INTEGER NOT NULL,
                Email TEXT NOT NULL,
                Phone TEXT NOT NULL
            )
            """);
        for (int i = 1; i <= 100; i++)
        {
            _connection.Execute(
                "INSERT INTO UserEntity (Id, Name, Age, Email, Phone) VALUES (@Id, @Name, @Age, @Email, @Phone)",
                new { Id = i, Name = $"User{i}", Age = 20 + i % 50, Email = $"user{i}@test.com", Phone = $"138{i:00000000}" });
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    /// <summary>轨道 A：字符串版 QueryFirst — s_queryFirstFunc 委托编译路径（基准基线）</summary>
    [Benchmark(Baseline = true)]
    public DapperUserDto QueryFirst_String_Delegate()
        => _connection.AsDto<DapperUserEntity>().QueryFirst<DapperUserDto>(Sql)!;

    /// <summary>轨道 B：CommandDefinition QueryFirst — 直调路径</summary>
    [Benchmark]
    public DapperUserDto QueryFirst_CommandDef_Direct()
        => _connection.AsDto<DapperUserEntity>().QueryFirst<DapperUserDto>(new CommandDefinition(Sql))!;

    /// <summary>轨道 A：字符串版 QueryFirstOrDefault — s_queryFirstOrDefaultFunc 委托路径</summary>
    [Benchmark]
    public DapperUserDto? QueryFirstOrDefault_String_Delegate()
        => _connection.AsDto<DapperUserEntity>().QueryFirstOrDefault<DapperUserDto>(Sql);

    /// <summary>轨道 B：CommandDefinition QueryFirstOrDefault — 直调路径</summary>
    [Benchmark]
    public DapperUserDto? QueryFirstOrDefault_CommandDef_Direct()
        => _connection.AsDto<DapperUserEntity>().QueryFirstOrDefault<DapperUserDto>(new CommandDefinition(Sql));
}

/// <summary>
/// 双轨对比 ③：异步列表查询（QueryAsync）— 委托（s_queryAsyncFunc，CommandDefinition 委托）vs 直调
/// <para>注：字符串版 QueryAsync 内部先构造 CommandDefinition 再走 s_queryAsyncFunc 委托
/// （DapperDtoSelector.cs QuerySourceAsync），与 CommandDefinition 直调入口构成同构对比。</para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class DapperDualTrackAsyncBench
{
    private SqliteConnection _connection = null!;
    private const string Sql = "SELECT * FROM UserEntity ORDER BY Id";

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE UserEntity (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Age INTEGER NOT NULL,
                Email TEXT NOT NULL,
                Phone TEXT NOT NULL
            )
            """);
        for (int i = 1; i <= 100; i++)
        {
            _connection.Execute(
                "INSERT INTO UserEntity (Id, Name, Age, Email, Phone) VALUES (@Id, @Name, @Age, @Email, @Phone)",
                new { Id = i, Name = $"User{i}", Age = 20 + i % 50, Email = $"user{i}@test.com", Phone = $"138{i:00000000}" });
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    /// <summary>轨道 A：字符串版 QueryAsync — 内部构造 CommandDefinition 后走 s_queryAsyncFunc 委托（基准基线）</summary>
    [Benchmark(Baseline = true)]
    public async Task<List<DapperUserDto>> QueryAsync_String_Delegate()
        => (await _connection.AsDto<DapperUserEntity>().QueryAsync<DapperUserDto>(Sql)).ToList();

    /// <summary>轨道 B：CommandDefinition QueryAsync — 直调路径</summary>
    [Benchmark]
    public async Task<List<DapperUserDto>> QueryAsync_CommandDef_Direct()
        => (await _connection.AsDto<DapperUserEntity>().QueryAsync<DapperUserDto>(new CommandDefinition(Sql))).ToList();
}

/// <summary>
/// 双轨对比 ④：漂移对照（无关场景）— 纯内存映射，无 DB
/// <para>跨遍波动 = 环境漂移基线：双轨差异小于本类同批波动时判为噪声。</para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class DapperDualTrackDriftBench
{
    private readonly DapperUserEntity _entity = new()
    {
        Id = 1, Name = "User1", Age = 21, Email = "user1@test.com", Phone = "13800000001"
    };
    private readonly List<DapperUserEntity> _entities100 = Enumerable.Range(1, 100)
        .Select(i => new DapperUserEntity
        {
            Id = i, Name = $"User{i}", Age = 20 + i % 50, Email = $"user{i}@test.com", Phone = $"138{i:00000000}"
        }).ToList();

    /// <summary>漂移对照 ①：单对象映射（无 DB）</summary>
    [Benchmark(Baseline = true)]
    public DapperUserDto Drift_MapperMap()
        => NCMapper.Mapper.Map<DapperUserEntity, DapperUserDto>(_entity)!;

    /// <summary>漂移对照 ②：100 条列表映射（无 DB）</summary>
    [Benchmark]
    public List<DapperUserDto> Drift_MapList100()
        => NCMapper.Mapper.MapList<DapperUserEntity, DapperUserDto>(_entities100)!.ToList();
}
