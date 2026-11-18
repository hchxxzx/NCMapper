using FreeSql;
using FreeSql.DataAnnotations;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using Xunit;

namespace NCMapper.IntegrationTests;

/// <summary>
/// FreeSql AsDto Single 族三态契约集成测试 — 0 条 / 1 条 / ≥2 条 × 同步与异步 × 两条腿。
/// <para>两条腿：投影腿（FreeSqlProjectionBuilder.Build，默认形态）与降级腿
/// （NeedsDegradationWithTrace 命中 → ApplyFreeSqlIncludes + Take(2) + AssertSingle + Mapper.Map）。
/// 降级腿以 DTO 含集合导航属性触发，并以导航集合被填充自证该腿确实被走到。</para>
/// <para>异常口径：两侧同口径——FreeSql 侧与 EFCore 侧均抛 InvalidOperationException ＋ 本库资源消息
/// （含目标类型名），异常消息文本逐字相等。</para>
/// </summary>
[Collection("FreeSqlGlobalState")]
public class FreeSqlSingleContractTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly string _dbPath;

    public FreeSqlSingleContractTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ncmapper_single_{Guid.NewGuid():N}.db");
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={_dbPath}")
            .UseAutoSyncStructure(true)
            .Build();

        _fsql.Insert(new List<FsSingleUser>
        {
            new() { Id = 1, Name = "Alice", Age = 30 },
            new() { Id = 2, Name = "Bob", Age = 25 },
        }).ExecuteAffrows();

        _fsql.Insert(new List<FsSingleOrder>
        {
            new() { Id = 10, UserId = 1, OrderNo = "ORD-10" },
            new() { Id = 11, UserId = 1, OrderNo = "ORD-11" },
        }).ExecuteAffrows();
    }

    public void Dispose()
    {
        _fsql.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    // ===== 投影腿：三态 × 同步/异步 =====

    [Fact]
    public void Single_投影腿_单条_返回映射值()
    {
        var dto = _fsql.Select<FsSingleUser>().Where(u => u.Id == 1).AsDto().Single<FsSingleUserDto>();

        Assert.NotNull(dto);
        Assert.Equal("Alice", dto!.Name);
        Assert.Equal(30, dto.Age);
    }

    [Fact]
    public void Single_投影腿_零条_抛SequenceNoElements()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _fsql.Select<FsSingleUser>().Where(u => u.Id < 0).AsDto().Single<FsSingleUserDto>());

        Assert.Equal(ErrorMessages.SequenceNoElements(nameof(FsSingleUserDto)), ex.Message);
    }

    [Fact]
    public void Single_投影腿_多条_抛SequenceMultipleElements()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _fsql.Select<FsSingleUser>().AsDto().Single<FsSingleUserDto>());

        Assert.Equal(ErrorMessages.SequenceMultipleElements(nameof(FsSingleUserDto)), ex.Message);
    }

    [Fact]
    public async Task SingleAsync_投影腿_单条_返回映射值()
    {
        var dto = await _fsql.Select<FsSingleUser>().Where(u => u.Id == 2)
            .AsDto().SingleAsync<FsSingleUserDto>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal("Bob", dto!.Name);
        Assert.Equal(25, dto.Age);
    }

    [Fact]
    public async Task SingleAsync_投影腿_零条_抛SequenceNoElements()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fsql.Select<FsSingleUser>().Where(u => u.Id < 0)
                .AsDto().SingleAsync<FsSingleUserDto>(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ErrorMessages.SequenceNoElements(nameof(FsSingleUserDto)), ex.Message);
    }

    [Fact]
    public async Task SingleAsync_投影腿_多条_抛SequenceMultipleElements()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fsql.Select<FsSingleUser>()
                .AsDto().SingleAsync<FsSingleUserDto>(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ErrorMessages.SequenceMultipleElements(nameof(FsSingleUserDto)), ex.Message);
    }

    // ===== 降级腿：三态 × 同步/异步（集合导航触发 NeedsDegradation）=====

    [Fact]
    public void Single_降级腿_单条_返回映射值且导航已加载()
    {
        var dto = _fsql.Select<FsSingleUser>().Where(u => u.Id == 1)
            .AsDto().Single<FsSingleUserWithOrdersDto>();

        Assert.NotNull(dto);
        Assert.Equal("Alice", dto!.Name);
        // 导航集合被填充 = 降级腿（ApplyFreeSqlIncludes）确实被走到；投影腿无法承载集合导航
        Assert.Equal(2, dto.Orders!.Count);
        Assert.Equal(new[] { "ORD-10", "ORD-11" }, dto.Orders.Select(o => o.OrderNo).OrderBy(x => x));
    }

    [Fact]
    public void Single_降级腿_零条_抛SequenceNoElements()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _fsql.Select<FsSingleUser>().Where(u => u.Id < 0)
                .AsDto().Single<FsSingleUserWithOrdersDto>());

        Assert.Equal(ErrorMessages.SequenceNoElements(nameof(FsSingleUserWithOrdersDto)), ex.Message);
    }

    [Fact]
    public void Single_降级腿_多条_抛SequenceMultipleElements()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _fsql.Select<FsSingleUser>().AsDto().Single<FsSingleUserWithOrdersDto>());

        Assert.Equal(ErrorMessages.SequenceMultipleElements(nameof(FsSingleUserWithOrdersDto)), ex.Message);
    }

    [Fact]
    public async Task SingleAsync_降级腿_零条_抛SequenceNoElements()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fsql.Select<FsSingleUser>().Where(u => u.Id < 0)
                .AsDto().SingleAsync<FsSingleUserWithOrdersDto>(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ErrorMessages.SequenceNoElements(nameof(FsSingleUserWithOrdersDto)), ex.Message);
    }

    [Fact]
    public async Task SingleAsync_降级腿_单条_返回映射值且导航已加载()
    {
        var dto = await _fsql.Select<FsSingleUser>().Where(u => u.Id == 1)
            .AsDto().SingleAsync<FsSingleUserWithOrdersDto>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal("Alice", dto!.Name);
        Assert.Equal(2, dto.Orders!.Count);
    }

    [Fact]
    public async Task SingleAsync_降级腿_多条_抛SequenceMultipleElements()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fsql.Select<FsSingleUser>()
                .AsDto().SingleAsync<FsSingleUserWithOrdersDto>(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ErrorMessages.SequenceMultipleElements(nameof(FsSingleUserWithOrdersDto)), ex.Message);
    }

    // ===== 测试模型 =====

    public class FsSingleUser
    {
        [Column(IsIdentity = false, IsPrimary = true)]
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }

        [Navigate(nameof(FsSingleOrder.UserId))]
        public List<FsSingleOrder> Orders { get; set; } = new();
    }

    public class FsSingleOrder
    {
        [Column(IsIdentity = false, IsPrimary = true)]
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OrderNo { get; set; } = "";
    }

    [MapFrom(typeof(FsSingleUser))]
    public class FsSingleUserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    /// <summary>含集合导航的 DTO — 触发 FreeSql 降级判定（集合导航不可投影翻译）。</summary>
    [MapFrom(typeof(FsSingleUser))]
    public class FsSingleUserWithOrdersDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public List<FsSingleOrderDto>? Orders { get; set; }
    }

    [MapFrom(typeof(FsSingleOrder))]
    public class FsSingleOrderDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";
    }
}
