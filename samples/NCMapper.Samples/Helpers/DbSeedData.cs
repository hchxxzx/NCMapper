using FreeSql;
using Microsoft.Data.Sqlite;

namespace NCMapper.Samples.Helpers;

/// <summary>
/// 数据库种子数据与连接辅助类 — 所有数据库场景的统一骨架。
/// </summary>
public static class DbSeedData
{
    /// <summary>
    /// 创建内存模式 FreeSql 实例（自动建表）。
    /// </summary>
    /// <returns>配置好的 FreeSql 实例（调用方需负责释放）</returns>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .UseNoneCommandParameter(true)
            .Build();
    }

    /// <summary>
    /// 创建并打开内存模式 SQLite 连接（供 Dapper / EF Core 使用）。
    /// </summary>
    /// <returns>已打开的 SQLite 连接（调用方需负责释放）</returns>
    public static SqliteConnection CreateInMemoryConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// 向 FreeSql 播种 UserEntity 种子数据（3 条：Alice / Bob / Charlie）。
    /// </summary>
    /// <param name="fsql">FreeSql 实例</param>
    public static void SeedUsers(IFreeSql fsql)
    {
        fsql.Insert(new Models.Entities.UserEntity
        {
            Id = 1,
            Name = "Alice",
            Age = 25,
            Email = "alice@example.com",
            Status = Models.Enums.UserStatus.Active,
            CreatedAt = new DateTime(2025, 1, 15),
        }).ExecuteAffrows();

        fsql.Insert(new Models.Entities.UserEntity
        {
            Id = 2,
            Name = "Bob",
            Age = 30,
            Email = "bob@example.com",
            Status = Models.Enums.UserStatus.Inactive,
            CreatedAt = new DateTime(2025, 3, 20),
        }).ExecuteAffrows();

        fsql.Insert(new Models.Entities.UserEntity
        {
            Id = 3,
            Name = "Charlie",
            Age = 35,
            Email = "charlie@example.com",
            Status = Models.Enums.UserStatus.Active,
            CreatedAt = new DateTime(2025, 6, 1),
        }).ExecuteAffrows();
    }
}
