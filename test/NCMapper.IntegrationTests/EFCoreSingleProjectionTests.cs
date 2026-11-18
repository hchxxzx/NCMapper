// 集成测试：EFCoreDtoSelector.Single/Async 唯一记录映射
using Microsoft.EntityFrameworkCore;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.EFCore;
using NCMapper.IntegrationTests.Models;
using Xunit;

namespace NCMapper.IntegrationTests
{
    /// <summary>
    /// EF Core Single/Async 集成测试
    /// </summary>
    public class EFCoreSingleProjectionTests : IDisposable
    {
        private readonly TestDbContext _db;

        public EFCoreSingleProjectionTests()
        {
            var connStr = $"DataSource=file:test{Guid.NewGuid():N}?mode=memory&cache=shared";
            _db = new TestDbContext(connStr);
            _db.SeedData();
        }

        public void Dispose() => _db.Dispose();

        /// <summary>
        /// AsDto.Single 正常单条映射
        /// </summary>
        [Fact]
        public void Single_正常单条映射正确()
        {
            var dto = _db.Users
                .Where(u => u.Id == 1)
                .AsDto()
                .Single<UserDto>();

            Assert.NotNull(dto);
            Assert.Equal("Alice", dto.Name);
            Assert.Equal(30, dto.Age);
        }

        /// <summary>
        /// AsDto.Single 0 条记录抛 InvalidOperationException（消息 = 本库资源消息，与 FreeSql 侧同口径）
        /// </summary>
        [Fact]
        public void Single_零条记录_抛InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => _db.Users.Where(u => u.Id < 0).AsDto().Single<UserDto>());

            Assert.Equal(ErrorMessages.SequenceNoElements(nameof(UserDto)), ex.Message);
        }

        /// <summary>
        /// AsDto.Single 多条记录抛 InvalidOperationException（消息 = 本库资源消息，与 FreeSql 侧同口径）
        /// </summary>
        [Fact]
        public void Single_多条记录_抛InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => _db.Users.AsDto().Single<UserDto>());

            Assert.Equal(ErrorMessages.SequenceMultipleElements(nameof(UserDto)), ex.Message);
        }

        /// <summary>
        /// AsDto.SingleAsync 异步正常
        /// </summary>
        [Fact]
        public async Task SingleAsync_正常单条映射正确()
        {
            var dto = await _db.Users
                .Where(u => u.Id == 2)
                .AsDto()
                .SingleAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(dto);
            Assert.Equal("Bob", dto.Name);
            Assert.Equal(25, dto.Age);
        }

        /// <summary>
        /// AsDto.SingleAsync 异步 0 条抛异常（消息 = 本库资源消息，与 FreeSql 侧同口径）
        /// </summary>
        [Fact]
        public async Task SingleAsync_零条记录_抛InvalidOperationException()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _db.Users.Where(u => u.Id < 0).AsDto().SingleAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ErrorMessages.SequenceNoElements(nameof(UserDto)), ex.Message);
        }

        /// <summary>
        /// S-05b：AsDto().SingleAsync 异步多条抛 InvalidOperationException（消息 = 本库资源消息，与 FreeSql 侧同口径）
        /// </summary>
        [Fact]
        public async Task SingleAsync_多条记录_抛InvalidOperationException()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _db.Users.AsDto().SingleAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ErrorMessages.SequenceMultipleElements(nameof(UserDto)), ex.Message);
        }

        /// <summary>
        /// AsDto.Single 跨表映射 — OrderDto 含 CustomerName
        /// </summary>
        [Fact]
        public void Single_跨表映射正确()
        {
            var dto = _db.Orders
                .Where(o => o.Id == 1)
                .AsDto()
                .Single<OrderFlatDto>();

            Assert.NotNull(dto);
            Assert.Equal("ORD-001", dto.OrderNo);
            Assert.Equal("ZhangSan", dto.CustomerName);
        }
    }
}
