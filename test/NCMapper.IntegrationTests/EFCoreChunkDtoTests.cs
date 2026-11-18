using Xunit;
using Microsoft.EntityFrameworkCore;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// EFCore ToChunkDto / ToChunkDtoAsync 集成测试
    /// <para>覆盖：投影路径 / 降级路径 / 空表 / 与 ToList 一致性 / CancellationToken 取消</para>
    /// </summary>
    public class EFCoreChunkDtoTests : IDisposable
    {
        private readonly TestDbContext _db;

        /// <summary>
        /// 初始化 SQLite 内存数据库并植入测试数据
        /// </summary>
        public EFCoreChunkDtoTests()
        {
            var connStr = $"DataSource=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new TestDbContext(connStr);
            _db.SeedData();
        }

        /// <summary>
        /// 释放 DbContext
        /// </summary>
        public void Dispose() => _db.Dispose();

        // ================================================================
        // 同步 ToChunkDto — 投影路径
        // ================================================================

        /// <summary>
        /// 投影路径分块正确 — UserDto 无 ConvertUsing，走投影路径
        /// <para>验证：3 条数据 + 每批 2 条 = 2 次回调 + 首批 2 条正确值</para>
        /// </summary>
        [Fact]
        public void T002_投影路径_分块正确()
        {
            var all = new List<UserDto>();
            int callbackCount = 0;

            _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToChunkDto<UserDto>(2, chunk =>
                {
                    callbackCount++;
                    all.AddRange(chunk);
                });

            Assert.Equal(2, callbackCount); // ceil(3/2) = 2
            Assert.Equal(3, all.Count);
            Assert.Equal("Alice", all[0].Name);
            Assert.Equal(30, all[0].Age);
            Assert.Equal("alice@test.com", all[0].Email);
            Assert.Equal("Bob", all[1].Name);
            Assert.Equal("Charlie", all[2].Name);
        }

        /// <summary>
        /// 投影路径 size=1 — 每批只返回 1 条
        /// </summary>
        [Fact]
        public void T002_投影路径_Size为1()
        {
            var all = new List<UserDto>();
            int callbackCount = 0;

            _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToChunkDto<UserDto>(1, chunk =>
                {
                    callbackCount++;
                    Assert.Single(chunk);
                    all.AddRange(chunk);
                });

            Assert.Equal(3, callbackCount);
            Assert.Equal(3, all.Count);
        }

        /// <summary>
        /// 投影路径空表 — 0 次回调
        /// </summary>
        [Fact]
        public void T002_投影路径_空表零次回调()
        {
            int callbackCount = 0;

            _db.Users
                .Where(u => u.Id == 999)
                .AsDto()
                .ToChunkDto<UserDto>(2, _ => callbackCount++);

            Assert.Equal(0, callbackCount);
        }

        /// <summary>
        /// 投影路径与 ToList 一致性 — 逐条逐字段断言
        /// </summary>
        [Fact]
        public void T002_投影路径_与ToList一致性()
        {
            var chunkResults = new List<UserDto>();

            _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToChunkDto<UserDto>(2, chunk => chunkResults.AddRange(chunk));

            var toListResults = _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToList<UserDto>();

            Assert.Equal(toListResults.Count, chunkResults.Count);

            for (int i = 0; i < toListResults.Count; i++)
            {
                Assert.Equal(toListResults[i].Id, chunkResults[i].Id);
                Assert.Equal(toListResults[i].Name, chunkResults[i].Name);
                Assert.Equal(toListResults[i].Age, chunkResults[i].Age);
                Assert.Equal(toListResults[i].Email, chunkResults[i].Email);
                Assert.Equal(toListResults[i].IsActive, chunkResults[i].IsActive);
                Assert.Equal(toListResults[i].Salary, chunkResults[i].Salary);
            }
        }

        // ================================================================
        // 同步 ToChunkDto — 降级路径
        // ================================================================

        /// <summary>
        /// 降级路径分块正确 — 使用含 ConvertUsing 的 DTO 触发降级
        /// <para>Validation: ConvertUsing 生效（"[Conv]"前缀）+ 首批 2 条</para>
        /// </summary>
        [Fact]
        public void T002_降级路径_分块与ConvertUsing正确()
        {
            var all = new List<UserConvDto>();
            int callbackCount = 0;

            _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToChunkDto<UserConvDto>(2, chunk =>
                {
                    callbackCount++;
                    all.AddRange(chunk);
                });

            Assert.Equal(2, callbackCount);
            Assert.Equal(3, all.Count);
            // ConvertUsing 生效 — "[Conv]" 前缀
            Assert.Equal("[Conv]Alice", all[0].Name);
            Assert.Equal("[Conv]Bob", all[1].Name);
            Assert.Equal("[Conv]Charlie", all[2].Name);
            Assert.Equal(30, all[0].Age);
            Assert.Equal(25, all[1].Age);
        }

        /// <summary>
        /// 降级路径空表 — 0 次回调
        /// </summary>
        [Fact]
        public void T002_降级路径_空表零次回调()
        {
            int callbackCount = 0;

            _db.Users
                .Where(u => u.Id == 999)
                .AsDto()
                .ToChunkDto<UserConvDto>(2, _ => callbackCount++);

            Assert.Equal(0, callbackCount);
        }

        // ================================================================
        // 异步 ToChunkDtoAsync — 投影路径
        // ================================================================

        /// <summary>
        /// 投影路径异步分块正确
        /// </summary>
        [Fact]
        public async Task T003_投影路径_异步分块正确()
        {
            var all = new List<UserDto>();
            int callbackCount = 0;

            await _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToChunkDtoAsync<UserDto>(2, chunk =>
                {
                    callbackCount++;
                    all.AddRange(chunk);
                    return Task.CompletedTask;
                }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, callbackCount);
            Assert.Equal(3, all.Count);
            Assert.Equal("Alice", all[0].Name);
            Assert.Equal("Bob", all[1].Name);
            Assert.Equal("Charlie", all[2].Name);
        }

        /// <summary>
        /// 投影路径异步空表 — 0 次回调
        /// </summary>
        [Fact]
        public async Task T003_投影路径_异步空表零次回调()
        {
            int callbackCount = 0;

            await _db.Users
                .Where(u => u.Id == 999)
                .AsDto()
                .ToChunkDtoAsync<UserDto>(2, _ =>
                {
                    callbackCount++;
                    return Task.CompletedTask;
                }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, callbackCount);
        }

        /// <summary>
        /// 投影路径异步与 ToListAsync 一致性
        /// </summary>
        [Fact]
        public async Task T003_投影路径_异步与ToListAsync一致性()
        {
            var chunkResults = new List<UserDto>();

            await _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToChunkDtoAsync<UserDto>(2, chunk =>
                {
                    chunkResults.AddRange(chunk);
                    return Task.CompletedTask;
                }, cancellationToken: TestContext.Current.CancellationToken);

            var toListResults = await _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToListAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(toListResults.Count, chunkResults.Count);

            for (int i = 0; i < toListResults.Count; i++)
            {
                Assert.Equal(toListResults[i].Id, chunkResults[i].Id);
                Assert.Equal(toListResults[i].Name, chunkResults[i].Name);
                Assert.Equal(toListResults[i].Age, chunkResults[i].Age);
                Assert.Equal(toListResults[i].Email, chunkResults[i].Email);
            }
        }

        // ================================================================
        // 异步 ToChunkDtoAsync — 降级路径
        // ================================================================

        /// <summary>
        /// 降级路径异步分块正确 — ConvertUsing 生效
        /// </summary>
        [Fact]
        public async Task T003_降级路径_异步分块与ConvertUsing正确()
        {
            var all = new List<UserConvDto>();

            await _db.Users
                .OrderBy(u => u.Id)
                .AsDto()
                .ToChunkDtoAsync<UserConvDto>(2, chunk =>
                {
                    all.AddRange(chunk);
                    return Task.CompletedTask;
                }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, all.Count);
            Assert.Equal("[Conv]Alice", all[0].Name);
            Assert.Equal("[Conv]Bob", all[1].Name);
            Assert.Equal("[Conv]Charlie", all[2].Name);
        }

        // ================================================================
        // CancellationToken 取消
        // ================================================================

        /// <summary>
        /// CancellationToken 取消后不再触发回调
        /// <para>使用已取消的 token，验证 OperationCanceledException 被抛出</para>
        /// </summary>
        [Fact]
        public async Task T003_CancellationToken取消_不再触发回调()
        {
            int callbackCount = 0;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _db.Users
                    .OrderBy(u => u.Id)
                    .AsDto()
                    .ToChunkDtoAsync<UserDto>(2, _ =>
                    {
                        callbackCount++;
                        return Task.CompletedTask;
                    }, cts.Token);
            });

            Assert.Equal(0, callbackCount);
        }

        /// <summary>
        /// 参数校验 — size &lt; 1 抛 ArgumentOutOfRangeException（同步）
        /// </summary>
        [Fact]
        public void T002_参数校验_Size小于1抛异常()
        {
            var selector = _db.Users.AsDto();

            var ex0 = Assert.Throws<ArgumentOutOfRangeException>(
                () => selector.ToChunkDto<UserDto>(0, _ => { }));
            Assert.Equal("size", ex0.ParamName);

            var exNeg = Assert.Throws<ArgumentOutOfRangeException>(
                () => selector.ToChunkDto<UserDto>(-1, _ => { }));
            Assert.Equal("size", exNeg.ParamName);
        }

        /// <summary>
        /// 参数校验 — size &lt; 1 抛 ArgumentOutOfRangeException（异步）
        /// </summary>
        [Fact]
        public async Task T003_参数校验_Size小于1抛异常()
        {
            var selector = _db.Users.AsDto();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => selector.ToChunkDtoAsync<UserDto>(0, _ => Task.CompletedTask, cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    // ================================================================
    // 降级测试用 DTO（ConvertUsing 触发 NeedsTwoStepMapping）
    // ================================================================

    /// <summary>
    /// 含 ConvertUsing 的 DTO — 触发 EfCoreDegradationDetector.NeedsDegradation
    /// </summary>
    [MapFrom(typeof(UserEntity))]
    public class UserConvDto
    {
        /// <summary>
        /// 主键标识
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 名称 — ConvertUsing 触发降级，在值前加 "[Conv]" 前缀
        /// </summary>
        [Convert(typeof(NoOpStringConverter), nameof(NoOpStringConverter.Convert))]
        public string Name { get; set; } = "";
        /// <summary>
        /// 年龄
        /// </summary>
        public int Age { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        public string Email { get; set; } = "";
    }

    /// <summary>
    /// 无操作字符串转换器（静态方法引用形态，T1 模板 M(TField)）— 在值前加 "[Conv]" 前缀，用于验证 ConvertUsing 是否生效
    /// </summary>
    public static class NoOpStringConverter
    {
        /// <summary>
        /// 转换 — 添加 "[Conv]" 前缀
        /// </summary>
        /// <param name="fieldValue">源字段值</param>
        /// <returns>带前缀的字符串</returns>
        public static string Convert(string fieldValue) => $"[Conv]{fieldValue}";
    }
}
