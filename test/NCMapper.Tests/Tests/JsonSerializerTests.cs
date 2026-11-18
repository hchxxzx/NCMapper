using System.Text.Json;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// T3-3: JsonSerializerBase/SystemTextJsonSerializer 边界测试
/// </summary>
public class JsonSerializerTests
{
    private class Payload
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void Serialize_Null_ReturnsEmpty()
    {
        var serializer = new SystemTextJsonSerializer();
        Assert.Equal("", serializer.Serialize(null));
    }

    [Fact]
    public void Serialize_POCO_ReturnsJson()
    {
        var serializer = new SystemTextJsonSerializer();
        var json = serializer.Serialize(new Payload { Id = 1, Name = "test" });
        Assert.Contains("\"Id\":1", json.Replace(" ", ""));
        Assert.Contains("\"Name\":\"test\"", json.Replace(" ", ""));
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsNull()
    {
        var serializer = new SystemTextJsonSerializer();
        Assert.Null(serializer.Deserialize("", typeof(Payload)));
        Assert.Null(serializer.Deserialize("   ", typeof(Payload)));
    }

    [Fact]
    public void Deserialize_CaseInsensitive()
    {
        var serializer = new SystemTextJsonSerializer();
        var result = (Payload?)serializer.Deserialize("""{"id":42,"name":"hello"}""", typeof(Payload));
        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("hello", result.Name);
    }

    [Fact]
    public void Deserialize_MaxDepthExceeded_Throws()
    {
        var serializer = new SystemTextJsonSerializer();
        // 构造超过 64 层嵌套的 JSON
        var deep = new string('[', 100) + "1" + new string(']', 100);
        Assert.Throws<JsonException>(() => serializer.Deserialize(deep, typeof(object)));
    }

    [Fact]
    public void Current_DefaultIsSystemTextJson()
    {
        Assert.IsType<SystemTextJsonSerializer>(JsonSerializerBase.Current);
    }

    [Fact]
    public void Current_CanReplace_AndRestore()
    {
        var original = JsonSerializerBase.Current;
        try
        {
            var custom = new SystemTextJsonSerializer(new JsonSerializerOptions { WriteIndented = true });
            JsonSerializerBase.Current = custom;
            Assert.Same(custom, JsonSerializerBase.Current);
        }
        finally
        {
            JsonSerializerBase.Current = original;
        }
    }

    [Fact]
    public void CustomOptions_Respected()
    {
        var serializer = new SystemTextJsonSerializer(new JsonSerializerOptions { WriteIndented = true });
        var json = serializer.Serialize(new Payload { Id = 1, Name = "x" });
        Assert.Contains("\n", json); // indented 输出含换行
    }
}
