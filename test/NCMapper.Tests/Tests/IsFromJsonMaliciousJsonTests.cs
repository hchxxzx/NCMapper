using System.Text.Json;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// IsFromJson 生成侧恶意 JSON 测试（C2 补测，P0）
/// <para>报告 §五 C2 断言形态：异常类型=JsonException 传播 + 消息含关键字。
/// 裁决回流（Q3，A-001-F2 裁决 D7）：异常契约 = 包装为 <see cref="MapException"/>
/// （InnerException 为 <see cref="JsonException"/> + 消息含关键字）。
/// 三样本：畸形 JSON / 超深 JSON（>64 层）/ 超大 JSON。</para>
/// <para><b>Skip 隔离</b>：目标语义（MapException 包装）待落地
/// （A-001-F2 异常包装修复），当前生产代码生成侧直抛 JsonException（未包装），
/// 测试按目标语义编写 → 当前红 → 按任务书 Q3 允许 Skip 隔离；落地后解除 Skip。</para>
/// </summary>
public class IsFromJsonMaliciousJsonTests
{
    /// <summary>
    /// 恶意 JSON 目标类型（Config 为 IsFromJson 反序列化目标）
 /// private → public——NCMapper 属性匹配契约要求属性类型
 /// IsPublic/IsNestedPublic（PropertyMatcher L47-48），private 嵌套类导致 Config 属性被过滤（D6 实证）
    /// </summary>
    public class MalJsonConfig
    {
        /// <summary>
        /// 主题名称
        /// </summary>
        public string Theme { get; set; } = "";
    }

    /// <summary>
    /// 被测源实体 — ConfigJson 承载恶意 JSON 字符串
    /// </summary>
    public class MalJsonEntity
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 配置 JSON 字符串（恶意样本注入点）
        /// </summary>
        public string ConfigJson { get; set; } = "";
    }

    /// <summary>
    /// 被测目标 DTO — Config 经 IsFromJson 反序列化
 /// [MapFrom] 标于 DTO（规范用法）——D6/D7 实证：标 Entity 上时
 /// 源生成器 Entity→Dto 方向 IsFromJson 丢失（D7 结论），运行时引擎正常；规范用法见
    /// README（[MapFrom(typeof(Entity))] 标 DTO 上，单标注双向生成）
    /// </summary>
    [NCMapper.Attributes.MapFrom(typeof(MalJsonEntity))]
    public class MalJsonDto
    {
        /// <summary>
        /// 标识字段
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 配置对象（IsFromJson 反序列化）
        /// </summary>
        [NCMapper.Attributes.MapProperty(typeof(MalJsonEntity), nameof(MalJsonEntity.ConfigJson), IsFromJson = true)]
        public MalJsonConfig? Config { get; set; }
    }

    /// <summary>
 /// 畸形 JSON — 大括号不闭合 → MapException 包装 JsonException，消息含关键字（目标语义）
    /// </summary>
 [Fact] // 解除 Skip——D7 异常包装落地（JsonConverter.cs catch JsonException→MapException）
    public void IsFromJson_MalformedJson_ThrowsMapExceptionWrappingJson()
    {
        var entity = new MalJsonEntity { Id = 1, ConfigJson = "{ \"Theme\": \"dark\" " };

        var ex = Assert.Throws<MapException>(() => Mapper.Map<MalJsonEntity, MalJsonDto>(entity));

 // 异常契约：InnerException 为 JsonException + 消息含关键字（D7 裁决口径）
        Assert.IsType<JsonException>(ex.InnerException);
        Assert.Contains("Json", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 超深 JSON — 嵌套深度超 STJ 默认 64 → MapException 包装 JsonException
    /// </summary>
 [Fact] // 解除 Skip——同 MalformedJson（D7 落地）
    public void IsFromJson_DeepJson_ThrowsMapExceptionWrappingJson()
    {
        // 100 层嵌套数组（超 STJ 默认 MaxDepth=64）
        var deepJson = new string('[', 100) + string.Join(',', Enumerable.Repeat("0", 100)) + new string(']', 100);
        var entity = new MalJsonEntity { Id = 2, ConfigJson = deepJson };

        var ex = Assert.Throws<MapException>(() => Mapper.Map<MalJsonEntity, MalJsonDto>(entity));

        Assert.IsType<JsonException>(ex.InnerException);
        Assert.Contains("Json", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
 /// 超大 JSON — 100KB 合法 payload：STJ 无长度限制，应正常解析零差异（③ 实测；
 /// 按复审 P2-复审-1 修正：原"长度超限抛异常"假设与 STJ 行为不符，改正常解析断言）
    /// </summary>
    [Fact]
    public void IsFromJson_HugeJson_ValidJson_ParsesNormally()
    {
        // 超大 payload：100KB 字符串（合法 JSON，STJ 不抛）
        var hugeJson = "{\"Theme\":\"" + new string('x', 100_000) + "\"}";
        var entity = new MalJsonEntity { Id = 3, ConfigJson = hugeJson };

 // 语义：合法 JSON 走正常路径，不抛异常（STJ 无长度上限，与③ 一致）
        var dto = Mapper.Map<MalJsonEntity, MalJsonDto>(entity);

        Assert.NotNull(dto);
        Assert.Equal(100_000, dto.Config?.Theme?.Length);
    }
}
