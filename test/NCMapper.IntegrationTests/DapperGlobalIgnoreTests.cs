using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Dapper;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// Dapper AsDto 全局配置继承测试 — 全局忽略清单经 Dapper 管道（SQL 查询 → MapList 内存映射）生效
/// <para>对应任务 Dapper AsDto 路径继承全局配置（走 MapList 管道）。</para>
/// <para>全局配置冻结为进程级状态：本用例注册忽略清单后必须在 finally 重置（测试隔离；
/// 生产场景禁止 ClearCache 修改冻结配置）。</para>
/// </summary>
[Collection("DapperGlobalState")]
public class DapperGlobalIgnoreTests : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>
    /// 建立内存 SQLite 连接并建表插数；重置全局冻结状态（防御跨测试类残留）
    /// </summary>
    public DapperGlobalIgnoreTests()
    {
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests(); // 迁移注册表隔离
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE DgiUser (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Phone TEXT NOT NULL
            )
            """);
        _connection.Execute("""
            INSERT INTO DgiUser (Id, Name, Phone) VALUES (1, 'Alice', '13812345678')
            """);
    }

    /// <summary>
    /// 释放连接
    /// </summary>
    public void Dispose()
    {
        _connection.Dispose();
        Mapper.ClearCache();
        GlobalMapConfig.ResetForTests(); // 注册表隔离
    }

    /// <summary>
    /// Dapper 查询实体
    /// </summary>
    public class DgiUser
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// Dapper 查询目标 DTO
    /// </summary>
    public class DgiUserDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Phone { get; set; } = "";
    }

    /// <summary>
    /// 验证：全局忽略清单（Phone）经 Dapper AsDto → MapList 管道生效，忽略列留空
    /// </summary>
    [Fact]
    public void AsDto_GlobalIgnore_Inherited()
    {
        Mapper.RegisterGeneratedConfig(new GeneratedMapConfig { IgnoreNames = ["Phone"] }, "DapperS3");

        var result = _connection.AsDto<DgiUser>()
            .Query<DgiUserDto>("SELECT * FROM DgiUser")
            .ToList();

        var dto = Assert.Single(result);
        Assert.Equal("Alice", dto.Name);
        // 全局忽略 → Phone 留空（Dapper 走 MapList 管道，忽略判定与内存路径一致）
        Assert.Equal("", dto.Phone);
    }
}
