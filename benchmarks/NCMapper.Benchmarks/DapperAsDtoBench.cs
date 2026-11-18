using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper.Attributes;
using NCMapper.Dapper;

namespace NCMapper.Benchmarks;

/// <summary>
/// Dapper AsDto 查询映射基准 — AsDto vs 原生 Query + 手动 Mapper.Map
/// <para>测量场景：平铺映射 / [Mask] 脱敏 / [ConvertUsing] 转换器</para>
/// <para>对照组：Dapper 原生 Query + Mapper.MapList 手动调用</para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class DapperAsDto_Query_Bench
{
    private SqliteConnection _connection = null!;

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

    /// <summary>
    /// AsDto 路径：统一 QuerySource + MapListCached（含委托缓存管道）
    /// </summary>
    [Benchmark(Baseline = true)]
    public List<DapperUserDto> AsDto_Query()
        => _connection.AsDto<DapperUserEntity>().Query<DapperUserDto>(
            "SELECT * FROM UserEntity ORDER BY Id").ToList();

    /// <summary>
    /// 对照组：Dapper 原生 Query + Mapper.MapList 手动调用
    /// </summary>
    [Benchmark]
    public List<DapperUserDto> Native_Query_MapList()
    {
        var entities = _connection.Query<DapperUserEntity>(
            "SELECT * FROM UserEntity ORDER BY Id");
        return Mapper.MapList<DapperUserEntity, DapperUserDto>(entities)!.ToList();
    }

    /// <summary>
    /// AsDto 路径 [Mask] 脱敏：手机号脱敏映射
    /// </summary>
    [Benchmark]
    public List<DapperUserMaskDto> AsDto_Mask()
        => _connection.AsDto<DapperUserEntity>().Query<DapperUserMaskDto>(
            "SELECT * FROM UserEntity ORDER BY Id").ToList();

    /// <summary>
    /// 对照组：[Mask] 脱敏 — 手动 MapList
    /// </summary>
    [Benchmark]
    public List<DapperUserMaskDto> Native_Mask()
    {
        var entities = _connection.Query<DapperUserEntity>(
            "SELECT * FROM UserEntity ORDER BY Id");
        return Mapper.MapList<DapperUserEntity, DapperUserMaskDto>(entities)!.ToList();
    }

    /// <summary>
    /// AsDto 路径 [ConvertUsing] 转换器：拼接 Name(Age)
    /// </summary>
    [Benchmark]
    public List<DapperUserConvertDto> AsDto_ConvertUsing()
        => _connection.AsDto<DapperUserEntity>().Query<DapperUserConvertDto>(
            "SELECT * FROM UserEntity ORDER BY Id").ToList();

    /// <summary>
    /// 对照组：[ConvertUsing] 转换器 — 手动 MapList
    /// </summary>
    [Benchmark]
    public List<DapperUserConvertDto> Native_ConvertUsing()
    {
        var entities = _connection.Query<DapperUserEntity>(
            "SELECT * FROM UserEntity ORDER BY Id");
        return Mapper.MapList<DapperUserEntity, DapperUserConvertDto>(entities)!.ToList();
    }
}

// ─── 基准模型 ────────────────────────────────────────────────────────────

public class DapperUserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
}

[MapFrom(typeof(DapperUserEntity))]
public class DapperUserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

[MapFrom(typeof(DapperUserEntity))]
public class DapperUserMaskDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    [Mask(typeof(MaskPhone))]
    public string Phone { get; set; } = "";
}

[MapFrom(typeof(DapperUserEntity))]
public class DapperUserConvertDto
{
    public int Id { get; set; }
    [Convert(typeof(DapperSummaryConverter), nameof(DapperSummaryConverter.Convert))]
    public string Summary { get; set; } = "";
}

public static class DapperSummaryConverter
{
    public static string Convert(DapperUserEntity source)
        => $"{source.Name}({source.Age})";
}
