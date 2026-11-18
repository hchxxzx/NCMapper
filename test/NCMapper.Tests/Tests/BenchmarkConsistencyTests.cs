using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// T3-8: Benchmark vs ManualMapper 一致性断言 — 验证 NCMapper.Map 输出 == 手写映射输出
/// </summary>
public class BenchmarkConsistencyTests
{
    // 模拟 benchmark 中的典型实体/DTO
    private class BenchOrder
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
        public int Status { get; set; }
        public string? Note { get; set; }
    }

    [NCMapper.Attributes.MapFrom(typeof(BenchOrder))]
    private class BenchOrderDto
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
        public int Status { get; set; }
        public string? Note { get; set; }
    }

    [Fact]
    public void Map_FlatEntity_MatchesManual()
    {
        var src = new BenchOrder
        {
            Id = 42,
            CustomerName = "Alice",
            Amount = 199.99m,
            CreatedAt = new DateTime(2026, 7, 28),
            Status = 3,
            Note = "rush"
        };

        // NCMapper 映射
        var dto = Mapper.Map<BenchOrder, BenchOrderDto>(src)!;

        // 手写映射
        var manual = new BenchOrderDto
        {
            Id = src.Id,
            CustomerName = src.CustomerName,
            Amount = src.Amount,
            CreatedAt = src.CreatedAt,
            Status = src.Status,
            Note = src.Note
        };

        Assert.Equal(manual.Id, dto.Id);
        Assert.Equal(manual.CustomerName, dto.CustomerName);
        Assert.Equal(manual.Amount, dto.Amount);
        Assert.Equal(manual.CreatedAt, dto.CreatedAt);
        Assert.Equal(manual.Status, dto.Status);
        Assert.Equal(manual.Note, dto.Note);
    }

    [Fact]
    public void Map_NullSource_ReturnsNull()
    {
        var result = Mapper.Map<BenchOrder, BenchOrderDto>(null!);
        Assert.Null(result);
    }

    [Fact]
    public void MapList_MatchesManualLoop()
    {
        var sources = Enumerable.Range(1, 50).Select(i => new BenchOrder
        {
            Id = i,
            CustomerName = $"Customer{i}",
            Amount = i * 10.5m,
            CreatedAt = DateTime.UnixEpoch.AddDays(i),
            Status = i % 5,
            Note = i % 2 == 0 ? null : $"note{i}"
        }).ToList();

        var dtos = Mapper.MapList<BenchOrder, BenchOrderDto>(sources)!;

        // 手写循环映射
        Assert.Equal(50, dtos.Count);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(sources[i].Id, dtos[i].Id);
            Assert.Equal(sources[i].CustomerName, dtos[i].CustomerName);
            Assert.Equal(sources[i].Amount, dtos[i].Amount);
            Assert.Equal(sources[i].Note, dtos[i].Note);
        }
    }
}
