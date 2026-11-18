using Xunit;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NCMapper.Attributes;
using NCMapper.EFCore;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// T17: CancellationToken 取消场景测试 —
    /// 验证异步方法在取消令牌触发时的行为
    /// </summary>
    public class CancellationTokenTests : IDisposable
    {
        private readonly SqliteConnection _connection;

        public CancellationTokenTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _connection.Execute(@"
                CREATE TABLE CancelUsers (Id INTEGER PRIMARY KEY, Name TEXT, Age INTEGER);
                INSERT INTO CancelUsers VALUES (1, 'Alice', 30);
                INSERT INTO CancelUsers VALUES (2, 'Bob', 25);
            ");
        }

        public void Dispose() => _connection.Dispose();

        // ================================================================
        // T17-01: Dapper QueryAsync 取消（通过 CommandDefinition）
        // ================================================================

        /// <summary>
        /// T17-01: Dapper QueryAsync 在 CancellationToken 已取消时应抛出 OperationCanceledException
        /// </summary>
        [Fact]
        public async Task Dapper_QueryAsync_CancelledToken_ThrowsOperationCanceled()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel(); // 提前取消

            // 使用 CommandDefinition 传入已取消的 token
            var cmd = new CommandDefinition(
                "SELECT Id, Name, Age FROM CancelUsers",
                cancellationToken: cts.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _connection.QueryAsync<CancelUserDto>(cmd);
            });
        }

        // ================================================================
        // T17-02: Dapper QueryAsync 正常完成（未取消）
        // ================================================================

        /// <summary>
        /// T17-02: Dapper QueryAsync 在 CancellationToken 未取消时正常完成
        /// </summary>
        [Fact]
        public async Task Dapper_QueryAsync_ActiveToken_CompletesNormally()
        {
            var cts = new CancellationTokenSource();

            var cmd = new CommandDefinition(
                "SELECT Id, Name, Age FROM CancelUsers ORDER BY Id",
                cancellationToken: cts.Token);

            var dtos = (await _connection.QueryAsync<CancelUserDto>(cmd)).ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("Alice", dtos[0].Name);
            Assert.Equal("Bob", dtos[1].Name);
        }

        // ================================================================
        // T17-03: EF Core 异步查询取消
        // ================================================================

        /// <summary>
        /// T17-03: EF Core ToListAsync 在 CancellationToken 已取消时应抛出 OperationCanceledException
        /// </summary>
        [Fact]
        public async Task EFCore_ToListAsync_CancelledToken_ThrowsOperationCanceled()
        {
            var connStr = $"DataSource=file:canceltest{Guid.NewGuid():N}?mode=memory&cache=shared";
            using var db = new TestDbContext(connStr);
            db.SeedData();

            var cts = new CancellationTokenSource();
            cts.Cancel(); // 提前取消

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                var result = await db.Users
                    .Select(EfCoreProjectionBuilder.Build<Models.UserEntity, Models.UserDto>())
                    .ToListAsync(cts.Token);
            });
        }

        // ================================================================
        // T17-04: EF Core 异步查询正常完成
        // ================================================================

        /// <summary>
        /// T17-04: EF Core ToListAsync 在 CancellationToken 未取消时正常完成
        /// </summary>
        [Fact]
        public async Task EFCore_ToListAsync_ActiveToken_CompletesNormally()
        {
            var connStr = $"DataSource=file:canceltest2{Guid.NewGuid():N}?mode=memory&cache=shared";
            using var db = new TestDbContext(connStr);
            db.SeedData();

            var cts = new CancellationTokenSource();

            var result = await db.Users
                .Select(EfCoreProjectionBuilder.Build<Models.UserEntity, Models.UserDto>())
                .OrderBy(d => d.Id)
                .ToListAsync(cts.Token);

            Assert.Equal(3, result.Count);
            Assert.Equal("Alice", result[0].Name);
        }

        // ================================================================
        // 测试 DTO
        // ================================================================

        /// <summary>
        /// 取消测试用 DTO
        /// </summary>
        public class CancelUserDto
        {
            /// <summary>用户 ID</summary>
            public int Id { get; set; }
            /// <summary>用户名</summary>
            public string Name { get; set; } = "";
            /// <summary>年龄</summary>
            public int Age { get; set; }
        }
    }
}
