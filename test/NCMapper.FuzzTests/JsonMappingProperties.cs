using System.Text.Json;
using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// IsFromJson 属性测试 — [MapProperty(IsFromJson = true)] 将 JSON 字符串反序列化为对象和列表
/// <para>覆盖路径：JSON 反序列化为对象、JSON 反序列化为列表、往返一致性</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// </summary>
public class JsonMappingProperties
{
    // ================================================================
    // JSON → 对象 — 反序列化为 FuzzJsonConfig
    // ================================================================

    /// <summary>
    /// IsFromJson 反序列化对象 — JSON 字符串正确反序列化为 FuzzJsonConfig 对象
    /// </summary>
    [Property]
    public bool Map_IsFromJson_Deserializes_Object(string theme, int fontSize)
    {
        var config = new FuzzJsonConfig { Theme = theme, FontSize = fontSize };
        var json = JsonSerializer.Serialize(config);
        var entity = new FuzzJsonEntity { Id = 42, ConfigJson = json };

        var dto = Mapper.Map<FuzzJsonEntity, FuzzJsonDto>(entity)!;

        return dto.Id == 42
            && dto.Config is not null
            && dto.Config.Theme == theme
            && dto.Config.FontSize == fontSize;
    }

    // ================================================================
    // JSON → 列表 — 反序列化为 List<string>
    // ================================================================

    /// <summary>
    /// IsFromJson 反序列化列表 — JSON 字符串正确反序列化为 List&lt;string&gt;
    /// </summary>
    [Property]
    public bool Map_IsFromJson_Deserializes_List(string[] tags)
    {
        var json = JsonSerializer.Serialize(tags);
        var entity = new FuzzJsonEntity { Id = 1, TagsJson = json };

        var dto = Mapper.Map<FuzzJsonEntity, FuzzJsonDto>(entity)!;

        return dto.Tags is not null
            && dto.Tags!.SequenceEqual(tags);
    }

    // ================================================================
    // 往返一致性 — 序列化→反序列化后值一致
    // ================================================================

    /// <summary>
    /// IsFromJson 往返一致 — 对象序列化为 JSON 后反序列化，字段值一致
    /// </summary>
    [Property]
    public bool Map_IsFromJson_RoundTrip_Object(string theme, int fontSize, int id)
    {
        var config = new FuzzJsonConfig { Theme = theme, FontSize = fontSize };
        var json = JsonSerializer.Serialize(config);
        var entity = new FuzzJsonEntity { Id = id, ConfigJson = json };

        var dto = Mapper.Map<FuzzJsonEntity, FuzzJsonDto>(entity)!;

        return dto.Id == id
            && dto.Config is not null
            && dto.Config.Theme == config.Theme
            && dto.Config.FontSize == config.FontSize;
    }

    /// <summary>
    /// IsFromJson 往返一致 — 列表序列化为 JSON 后反序列化，元素一致
    /// </summary>
    [Property]
    public bool Map_IsFromJson_RoundTrip_List(string[] tags, int id)
    {
        var json = JsonSerializer.Serialize(tags);
        var entity = new FuzzJsonEntity { Id = id, TagsJson = json };

        var dto = Mapper.Map<FuzzJsonEntity, FuzzJsonDto>(entity)!;

        return dto.Id == id
            && dto.Tags is not null
            && dto.Tags!.Count == tags.Length
            && dto.Tags.SequenceEqual(tags);
    }

    // ================================================================
    // null / 空 JSON 守卫
    // ================================================================

    /// <summary>
    /// IsFromJson 空数组反序列化为空列表 — JSON "[]" 映射为空 List&lt;string&gt;
    /// </summary>
    [Property]
    public bool Map_IsFromJson_Empty_Array_Returns_Empty_List(int id)
    {
        var entity = new FuzzJsonEntity { Id = id, TagsJson = "[]" };

        var dto = Mapper.Map<FuzzJsonEntity, FuzzJsonDto>(entity)!;

        return dto.Tags is not null && dto.Tags.Count == 0;
    }
}
