using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// T3-4: JsonDeepCopyProvider 独立测试 — POCO/嵌套/集合/异常/特殊类型
/// </summary>
public class JsonDeepCopyProviderTests
{
    private class SimplePoco
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class NestedPoco
    {
        public int Id { get; set; }
        public SimplePoco? Child { get; set; }
    }

    private class WithCollection
    {
        public int Id { get; set; }
        public List<SimplePoco> Items { get; set; } = new();
    }

    private class WithSpecialTypes
    {
        public DateTime Timestamp { get; set; }
        public Guid UniqueId { get; set; }
        public DayOfWeek Day { get; set; }
    }

    private class Circular
    {
        public int Id { get; set; }
        public Circular? Self { get; set; }
    }

    private readonly JsonDeepCopyProvider _provider = JsonDeepCopyProvider.Instance;

    [Fact]
    public void DeepCopy_SimplePoco_Independent()
    {
        var src = new SimplePoco { Id = 1, Name = "original" };
        var copy = _provider.DeepCopy(src);

        Assert.NotNull(copy);
        Assert.NotSame(src, copy);
        Assert.Equal(1, copy!.Id);
        Assert.Equal("original", copy.Name);

        copy.Name = "modified";
        Assert.Equal("original", src.Name); // 独立性
    }

    [Fact]
    public void DeepCopy_Null_ReturnsNull()
    {
        Assert.Null(_provider.DeepCopy<SimplePoco>(null!));
    }

    [Fact]
    public void DeepCopy_NestedObject_DeepIndependent()
    {
        var src = new NestedPoco { Id = 1, Child = new SimplePoco { Id = 2, Name = "child" } };
        var copy = _provider.DeepCopy(src);

        Assert.NotNull(copy);
        Assert.NotSame(src.Child, copy!.Child);
        Assert.Equal("child", copy.Child!.Name);

        copy.Child.Name = "changed";
        Assert.Equal("child", src.Child!.Name);
    }

    [Fact]
    public void DeepCopy_Collection_Independent()
    {
        var src = new WithCollection
        {
            Id = 1,
            Items = new List<SimplePoco> { new() { Id = 10, Name = "a" }, new() { Id = 20, Name = "b" } }
        };
        var copy = _provider.DeepCopy(src);

        Assert.NotNull(copy);
        Assert.Equal(2, copy!.Items.Count);
        Assert.NotSame(src.Items[0], copy.Items[0]);

        copy.Items[0].Name = "x";
        Assert.Equal("a", src.Items[0].Name);
    }

    [Fact]
    public void DeepCopy_SpecialTypes_Preserved()
    {
        var src = new WithSpecialTypes
        {
            Timestamp = new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc),
            UniqueId = Guid.NewGuid(),
            Day = DayOfWeek.Wednesday
        };
        var copy = _provider.DeepCopy(src);

        Assert.NotNull(copy);
        Assert.Equal(src.Timestamp, copy!.Timestamp);
        Assert.Equal(src.UniqueId, copy.UniqueId);
        Assert.Equal(src.Day, copy.Day);
    }

    [Fact]
    public void DeepCopy_CircularReference_ThrowsMapException()
    {
        var src = new Circular { Id = 1 };
        src.Self = src; // 循环引用

        // JSON 序列化不支持循环引用 → 抛 MapException（包装 JsonException）
        var ex = Assert.Throws<MapException>(() => _provider.DeepCopy(src));

        // C14 补测：消息含类型名（异常契约可诊断）
        Assert.Contains(nameof(Circular), ex.Message);
    }

    /// <summary>
    /// C14 补测：同一循环引用对象在 Fast 路径 SUCCESS（对照 Json 路径抛 MapException）
    /// <para>FastDeepCopyProvider 通过 CycleCache 打破循环引用图（A↔A 自引用），
    /// 与 Json 路径（序列化不支持循环）形成对照——异常契约差异显式化。</para>
    /// </summary>
    [Fact]
    public void DeepCopy_CircularReference_FastPath_Succeeds_Control()
    {
        var src = new Circular { Id = 1 };
        src.Self = src; // 循环引用

        var copy = new FastDeepCopyProvider().DeepCopy(src);

        Assert.NotNull(copy);
        Assert.Equal(1, copy!.Id);
        // 循环引用打破：拷贝的 Self 指向自身（独立于源）
        Assert.Same(copy, copy.Self);
        Assert.NotSame(src, copy);
    }

    [Fact]
    public void DeepCopy_String_ThrowsArgument()
    {
        // string 无无参构造器 → 基类走 record 路径 → GetUninitializedObject(string) 抛 ArgumentException
        // JsonDeepCopyProvider.CreateInstance 中的 string 特判在此路径下不可达（已知限制）
        Assert.Throws<ArgumentException>(() => _provider.DeepCopy("hello"));
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        Assert.Same(JsonDeepCopyProvider.Instance, JsonDeepCopyProvider.Instance);
    }

    [Fact]
    public void DeepCopyObject_NonGeneric_Works()
    {
        object src = new SimplePoco { Id = 99, Name = "obj" };
        var copy = _provider.DeepCopyObject(src);

        Assert.NotNull(copy);
        Assert.NotSame(src, copy);
        Assert.Equal(99, ((SimplePoco)copy!).Id);
    }
}
