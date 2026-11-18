using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 大对象映射测试 — 验证 30+ 字段的类型映射正确性（M-16）
/// <para>覆盖大量属性场景下运行时表达式树编译和映射的正确性</para>
/// </summary>
public class BigStructMappingTests
{
    // 30+ 字段的源类型
    public class BigSource
    {
        public int Field01 { get; set; }
        public int Field02 { get; set; }
        public int Field03 { get; set; }
        public long Field04 { get; set; }
        public long Field05 { get; set; }
        public float Field06 { get; set; }
        public float Field07 { get; set; }
        public double Field08 { get; set; }
        public double Field09 { get; set; }
        public decimal Field10 { get; set; }
        public decimal Field11 { get; set; }
        public string? Field12 { get; set; }
        public string? Field13 { get; set; }
        public string? Field14 { get; set; }
        public bool Field15 { get; set; }
        public bool Field16 { get; set; }
        public bool Field17 { get; set; }
        public DateTime Field18 { get; set; }
        public DateTime Field19 { get; set; }
        public Guid Field20 { get; set; }
        public Guid Field21 { get; set; }
        public byte Field22 { get; set; }
        public short Field23 { get; set; }
        public ushort Field24 { get; set; }
        public uint Field25 { get; set; }
        public ulong Field26 { get; set; }
        public sbyte Field27 { get; set; }
        public char Field28 { get; set; }
        public TimeSpan Field29 { get; set; }
        public DateTimeOffset Field30 { get; set; }
        public int? Field31 { get; set; }
        public long? Field32 { get; set; }
    }

    // 对应的目标类型（同类型字段）
    public class BigTarget
    {
        public int Field01 { get; set; }
        public int Field02 { get; set; }
        public int Field03 { get; set; }
        public long Field04 { get; set; }
        public long Field05 { get; set; }
        public float Field06 { get; set; }
        public float Field07 { get; set; }
        public double Field08 { get; set; }
        public double Field09 { get; set; }
        public decimal Field10 { get; set; }
        public decimal Field11 { get; set; }
        public string? Field12 { get; set; }
        public string? Field13 { get; set; }
        public string? Field14 { get; set; }
        public bool Field15 { get; set; }
        public bool Field16 { get; set; }
        public bool Field17 { get; set; }
        public DateTime Field18 { get; set; }
        public DateTime Field19 { get; set; }
        public Guid Field20 { get; set; }
        public Guid Field21 { get; set; }
        public byte Field22 { get; set; }
        public short Field23 { get; set; }
        public ushort Field24 { get; set; }
        public uint Field25 { get; set; }
        public ulong Field26 { get; set; }
        public sbyte Field27 { get; set; }
        public char Field28 { get; set; }
        public TimeSpan Field29 { get; set; }
        public DateTimeOffset Field30 { get; set; }
        public int? Field31 { get; set; }
        public long? Field32 { get; set; }
    }

    [Fact]
    public void BigObject_Map_All_Fields_Preserved()
    {
        var source = new BigSource
        {
            Field01 = 1,
            Field02 = 2,
            Field03 = 3,
            Field04 = 4L,
            Field05 = 5L,
            Field06 = 6.6f,
            Field07 = 7.7f,
            Field08 = 8.8,
            Field09 = 9.9,
            Field10 = 10.10m,
            Field11 = 11.11m,
            Field12 = "twelve",
            Field13 = "thirteen",
            Field14 = "fourteen",
            Field15 = true,
            Field16 = false,
            Field17 = true,
            Field18 = new DateTime(2024, 1, 18),
            Field19 = new DateTime(2024, 6, 19),
            Field20 = Guid.NewGuid(),
            Field21 = Guid.NewGuid(),
            Field22 = 22,
            Field23 = 23,
            Field24 = 24,
            Field25 = 25u,
            Field26 = 26ul,
            Field27 = 27,
            Field28 = 'Z',
            Field29 = TimeSpan.FromMinutes(29),
            Field30 = new DateTimeOffset(2024, 1, 30, 0, 0, 0, TimeSpan.Zero),
            Field31 = 31,
            Field32 = 32L
        };

        var target = Mapper.Map<BigSource, BigTarget>(source)!;

        Assert.Equal(source.Field01, target.Field01);
        Assert.Equal(source.Field02, target.Field02);
        Assert.Equal(source.Field03, target.Field03);
        Assert.Equal(source.Field04, target.Field04);
        Assert.Equal(source.Field05, target.Field05);
        Assert.Equal(source.Field06, target.Field06);
        Assert.Equal(source.Field07, target.Field07);
        Assert.Equal(source.Field08, target.Field08);
        Assert.Equal(source.Field09, target.Field09);
        Assert.Equal(source.Field10, target.Field10);
        Assert.Equal(source.Field11, target.Field11);
        Assert.Equal(source.Field12, target.Field12);
        Assert.Equal(source.Field13, target.Field13);
        Assert.Equal(source.Field14, target.Field14);
        Assert.Equal(source.Field15, target.Field15);
        Assert.Equal(source.Field16, target.Field16);
        Assert.Equal(source.Field17, target.Field17);
        Assert.Equal(source.Field18, target.Field18);
        Assert.Equal(source.Field19, target.Field19);
        Assert.Equal(source.Field20, target.Field20);
        Assert.Equal(source.Field21, target.Field21);
        Assert.Equal(source.Field22, target.Field22);
        Assert.Equal(source.Field23, target.Field23);
        Assert.Equal(source.Field24, target.Field24);
        Assert.Equal(source.Field25, target.Field25);
        Assert.Equal(source.Field26, target.Field26);
        Assert.Equal(source.Field27, target.Field27);
        Assert.Equal(source.Field28, target.Field28);
        Assert.Equal(source.Field29, target.Field29);
        Assert.Equal(source.Field30, target.Field30);
        Assert.Equal(source.Field31, target.Field31);
        Assert.Equal(source.Field32, target.Field32);
    }

    [Fact]
    public void BigObject_Map_With_Null_Fields()
    {
        var source = new BigSource
        {
            Field01 = 42,
            Field12 = null,
            Field13 = null,
            Field14 = null,
            Field31 = null,
            Field32 = null
        };

        var target = Mapper.Map<BigSource, BigTarget>(source)!;

        Assert.Equal(42, target.Field01);
        Assert.Null(target.Field12);
        Assert.Null(target.Field13);
        Assert.Null(target.Field14);
        Assert.Null(target.Field31);
        Assert.Null(target.Field32);
    }

    [Fact]
    public void BigObject_Map_Default_Source_Fields()
    {
        var source = new BigSource();
        var target = Mapper.Map<BigSource, BigTarget>(source)!;

        Assert.Equal(0, target.Field01);
        Assert.Equal(0L, target.Field04);
        Assert.Null(target.Field12);
        Assert.False(target.Field15);
        Assert.Equal(default(Guid), target.Field20);
        Assert.Null(target.Field31);
    }

    [Fact]
    public void BigObject_MapList_All_Elements_Mapped()
    {
        var sources = new List<BigSource>
        {
            new() { Field01 = 1, Field12 = "A" },
            new() { Field01 = 2, Field12 = "B" },
            new() { Field01 = 3, Field12 = "C" }
        };

        var targets = Mapper.MapList<BigSource, BigTarget>(sources)!;

        Assert.Equal(3, targets.Count);
        Assert.Equal(1, targets[0].Field01);
        Assert.Equal("A", targets[0].Field12);
        Assert.Equal(2, targets[1].Field01);
        Assert.Equal("B", targets[1].Field12);
        Assert.Equal(3, targets[2].Field01);
        Assert.Equal("C", targets[2].Field12);
    }
}
