using Dapper;
using Microsoft.Data.Sqlite;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Dapper;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// Dapper AsDto 终端重载族集成测试 — CommandDefinition 重载族（五同步 + 五异步状态机）、
/// QuerySource(CommandDefinition) 与 QueryUnbufferedAsync 流式腿。
/// <para>每法与对应字符串重载逐值对照（非仅"不抛"）；取消令牌两态各有断言。</para>
/// </summary>
[Collection("DapperGlobalState")]
public class DapperAsyncTerminalTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DapperAsyncTerminalTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE AtUser (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Age INTEGER NOT NULL
            )
            """);
        _connection.Execute("""
            INSERT INTO AtUser (Id, Name, Age) VALUES
            (1, 'Alice',   25),
            (2, 'Bob',     30),
            (3, 'Charlie', 35)
            """);
    }

    public void Dispose() => _connection.Dispose();

    private const string AllSql = "SELECT * FROM AtUser ORDER BY Id";
    private const string NoneSql = "SELECT * FROM AtUser WHERE Id > 100";

    private static CommandDefinition Cmd(string sql) =>
        new(sql, null, null, null, null, CommandFlags.Buffered, TestContext.Current.CancellationToken);

    // ===== 五同步重载 =====

    [Fact]
    public void Query_CommandDefinition_MatchesStringOverload()
    {
        var viaCommand = _connection.AsDto<AtUser>().Query<AtUserDto>(Cmd(AllSql)).ToList();
        var viaString = _connection.AsDto<AtUser>().Query<AtUserDto>(AllSql).ToList();

        Assert.Equal(3, viaCommand.Count);
        Assert.Equal(viaString.Select(d => d.Id), viaCommand.Select(d => d.Id));
        Assert.Equal(viaString.Select(d => d.Name), viaCommand.Select(d => d.Name));
        Assert.Equal(viaString.Select(d => d.Age), viaCommand.Select(d => d.Age));
    }

    [Fact]
    public void Query_CommandDefinition_WithParam_FiltersCorrectly()
    {
        var cmd = new CommandDefinition(
            "SELECT * FROM AtUser WHERE Age > @age ORDER BY Id", new { age = 28 },
            null, null, null, CommandFlags.Buffered, TestContext.Current.CancellationToken);

        var result = _connection.AsDto<AtUser>().Query<AtUserDto>(cmd).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Bob", result[0].Name);
        Assert.Equal("Charlie", result[1].Name);
    }

    [Fact]
    public void QuerySource_CommandDefinition_OverloadMaterializesBufferedResult()
    {
        // QuerySource(CommandDefinition) 由 Query(command) 消费：缓冲语义下同一结果可重复枚举且值稳定
        var first = _connection.AsDto<AtUser>().Query<AtUserDto>(Cmd(AllSql)).ToList();
        var again = _connection.AsDto<AtUser>().Query<AtUserDto>(Cmd(AllSql)).ToList();

        Assert.Equal(3, first.Count);
        Assert.Equal(first.Select(d => d.Id), again.Select(d => d.Id));
        Assert.Equal(new[] { 1, 2, 3 }, first.Select(d => d.Id));
    }

    [Fact]
    public void QueryFirst_CommandDefinition_MatchesStringOverload()
    {
        var viaCommand = _connection.AsDto<AtUser>().QueryFirst<AtUserDto>(Cmd(AllSql));
        var viaString = _connection.AsDto<AtUser>().QueryFirst<AtUserDto>(AllSql);

        Assert.Equal(viaString.Id, viaCommand.Id);
        Assert.Equal(viaString.Name, viaCommand.Name);
        Assert.Equal(1, viaCommand.Id);
    }

    [Fact]
    public void QueryFirst_CommandDefinition_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => _connection.AsDto<AtUser>().QueryFirst<AtUserDto>(Cmd(NoneSql)));
    }

    [Fact]
    public void QueryFirstOrDefault_CommandDefinition_MatchesStringOverload()
    {
        var viaCommand = _connection.AsDto<AtUser>().QueryFirstOrDefault<AtUserDto>(Cmd(AllSql));
        var viaString = _connection.AsDto<AtUser>().QueryFirstOrDefault<AtUserDto>(AllSql);

        Assert.NotNull(viaCommand);
        Assert.Equal(viaString!.Name, viaCommand!.Name);
        Assert.Equal(1, viaCommand.Id);
    }

    [Fact]
    public void QueryFirstOrDefault_CommandDefinition_Empty_ReturnsNull()
    {
        var dto = _connection.AsDto<AtUser>().QueryFirstOrDefault<AtUserDto>(Cmd(NoneSql));

        Assert.Null(dto);
    }

    [Fact]
    public void QuerySingle_CommandDefinition_MatchesStringOverload()
    {
        var cmd = new CommandDefinition(
            "SELECT * FROM AtUser WHERE Id = @id", new { id = 2 },
            null, null, null, CommandFlags.Buffered, TestContext.Current.CancellationToken);

        var viaCommand = _connection.AsDto<AtUser>().QuerySingle<AtUserDto>(cmd);
        var viaString = _connection.AsDto<AtUser>().QuerySingle<AtUserDto>(
            "SELECT * FROM AtUser WHERE Id = @id", new { id = 2 });

        Assert.Equal(viaString!.Id, viaCommand!.Id);
        Assert.Equal("Bob", viaCommand.Name);
        Assert.Equal(30, viaCommand.Age);
    }

    [Fact]
    public void QuerySingle_CommandDefinition_MultipleRows_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => _connection.AsDto<AtUser>().QuerySingle<AtUserDto>(Cmd(AllSql)));
    }

    [Fact]
    public void QuerySingleOrDefault_CommandDefinition_MatchesStringOverload()
    {
        var cmd = new CommandDefinition(
            "SELECT * FROM AtUser WHERE Id = @id", new { id = 3 },
            null, null, null, CommandFlags.Buffered, TestContext.Current.CancellationToken);

        var viaCommand = _connection.AsDto<AtUser>().QuerySingleOrDefault<AtUserDto>(cmd);
        var viaString = _connection.AsDto<AtUser>().QuerySingleOrDefault<AtUserDto>(
            "SELECT * FROM AtUser WHERE Id = @id", new { id = 3 });

        Assert.Equal(viaString!.Name, viaCommand!.Name);
        Assert.Equal("Charlie", viaCommand.Name);
    }

    [Fact]
    public void QuerySingleOrDefault_CommandDefinition_Empty_ReturnsNull()
    {
        var dto = _connection.AsDto<AtUser>().QuerySingleOrDefault<AtUserDto>(Cmd(NoneSql));

        Assert.Null(dto);
    }

    // ===== 五异步状态机（CommandDefinition 重载）=====

    [Fact]
    public async Task QueryAsync_CommandDefinition_MatchesStringOverload()
    {
        var viaCommand = (await _connection.AsDto<AtUser>().QueryAsync<AtUserDto>(Cmd(AllSql))).ToList();
        var viaString = (await _connection.AsDto<AtUser>().QueryAsync<AtUserDto>(
            AllSql, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(3, viaCommand.Count);
        Assert.Equal(viaString.Select(d => d.Id), viaCommand.Select(d => d.Id));
        Assert.Equal(viaString.Select(d => d.Name), viaCommand.Select(d => d.Name));
    }

    [Fact]
    public async Task QueryFirstAsync_CommandDefinition_MatchesStringOverload()
    {
        var viaCommand = await _connection.AsDto<AtUser>().QueryFirstAsync<AtUserDto>(Cmd(AllSql));
        var viaString = await _connection.AsDto<AtUser>().QueryFirstAsync<AtUserDto>(
            AllSql, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(viaString.Id, viaCommand.Id);
        Assert.Equal("Alice", viaCommand.Name);
    }

    [Fact]
    public async Task QueryFirstAsync_CommandDefinition_Empty_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _connection.AsDto<AtUser>().QueryFirstAsync<AtUserDto>(Cmd(NoneSql)));
    }

    [Fact]
    public async Task QueryFirstOrDefaultAsync_CommandDefinition_MatchesStringOverload()
    {
        var viaCommand = await _connection.AsDto<AtUser>().QueryFirstOrDefaultAsync<AtUserDto>(Cmd(AllSql));
        var viaString = await _connection.AsDto<AtUser>().QueryFirstOrDefaultAsync<AtUserDto>(
            AllSql, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(viaCommand);
        Assert.Equal(viaString!.Name, viaCommand!.Name);
    }

    [Fact]
    public async Task QueryFirstOrDefaultAsync_CommandDefinition_Empty_ReturnsNull()
    {
        var dto = await _connection.AsDto<AtUser>().QueryFirstOrDefaultAsync<AtUserDto>(Cmd(NoneSql));

        Assert.Null(dto);
    }

    [Fact]
    public async Task QuerySingleAsync_CommandDefinition_MatchesStringOverload()
    {
        var cmd = new CommandDefinition(
            "SELECT * FROM AtUser WHERE Id = @id", new { id = 2 },
            null, null, null, CommandFlags.Buffered, TestContext.Current.CancellationToken);

        var viaCommand = await _connection.AsDto<AtUser>().QuerySingleAsync<AtUserDto>(cmd);
        var viaString = await _connection.AsDto<AtUser>().QuerySingleAsync<AtUserDto>(
            "SELECT * FROM AtUser WHERE Id = @id", new { id = 2 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(viaString!.Name, viaCommand!.Name);
        Assert.Equal(30, viaCommand.Age);
    }

    [Fact]
    public async Task QuerySingleAsync_CommandDefinition_MultipleRows_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _connection.AsDto<AtUser>().QuerySingleAsync<AtUserDto>(Cmd(AllSql)));
    }

    [Fact]
    public async Task QuerySingleOrDefaultAsync_CommandDefinition_MatchesStringOverload()
    {
        var cmd = new CommandDefinition(
            "SELECT * FROM AtUser WHERE Id = @id", new { id = 1 },
            null, null, null, CommandFlags.Buffered, TestContext.Current.CancellationToken);

        var viaCommand = await _connection.AsDto<AtUser>().QuerySingleOrDefaultAsync<AtUserDto>(cmd);
        var viaString = await _connection.AsDto<AtUser>().QuerySingleOrDefaultAsync<AtUserDto>(
            "SELECT * FROM AtUser WHERE Id = @id", new { id = 1 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(viaString!.Name, viaCommand!.Name);
    }

    [Fact]
    public async Task QuerySingleOrDefaultAsync_CommandDefinition_Empty_ReturnsNull()
    {
        var dto = await _connection.AsDto<AtUser>().QuerySingleOrDefaultAsync<AtUserDto>(Cmd(NoneSql));

        Assert.Null(dto);
    }

    // ===== 取消令牌两态（CommandDefinition 重载族）=====

    [Fact]
    public async Task QueryAsync_CommandDefinition_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cmd = new CommandDefinition(AllSql, null, null, null, null, CommandFlags.Buffered, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _connection.AsDto<AtUser>().QueryAsync<AtUserDto>(cmd));
    }

    [Fact]
    public async Task QueryAsync_CommandDefinition_ActiveToken_CompletesNormally()
    {
        using var cts = new CancellationTokenSource();
        var cmd = new CommandDefinition(AllSql, null, null, null, null, CommandFlags.Buffered, cts.Token);

        var result = (await _connection.AsDto<AtUser>().QueryAsync<AtUserDto>(cmd)).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal("Alice", result[0].Name);
    }

    // ===== QueryUnbufferedAsync 流式腿 =====

    [Fact]
    public async Task QueryUnbufferedAsync_StreamsInQueryOrder()
    {
        var seen = new List<string?>();
        await foreach (var dto in _connection.AsDto<AtUser>().QueryUnbufferedAsync<AtUserDto>(
            AllSql, cancellationToken: TestContext.Current.CancellationToken))
        {
            seen.Add(dto.Name);
        }

        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, seen);
    }

    [Fact]
    public async Task QueryUnbufferedAsync_EmptyResult_YieldsNothing()
    {
        var count = 0;
        await foreach (var dto in _connection.AsDto<AtUser>().QueryUnbufferedAsync<AtUserDto>(
            NoneSql, cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task QueryUnbufferedAsync_BreakMidEnumeration_ReleasesReader()
    {
        await foreach (var dto in _connection.AsDto<AtUser>().QueryUnbufferedAsync<AtUserDto>(
            AllSql, cancellationToken: TestContext.Current.CancellationToken))
        {
            if (dto.Id == 2) break; // 中途退出 → 异步枚举 dispose 释放 reader
        }

        // reader 已释放：同一连接可继续查询（未释放时 SQLite 会报 reader 未关闭）
        var after = (await _connection.AsDto<AtUser>().QueryAsync<AtUserDto>(
            AllSql, cancellationToken: TestContext.Current.CancellationToken)).ToList();
        Assert.Equal(3, after.Count);
    }

    [Fact]
    public async Task QueryUnbufferedAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var dto in _connection.AsDto<AtUser>().QueryUnbufferedAsync<AtUserDto>(
                AllSql, cancellationToken: cts.Token))
            {
                _ = dto;
            }
        });
    }

    // ===== 测试模型 =====

    public class AtUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [MapFrom(typeof(AtUser))]
    public class AtUserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }
}
