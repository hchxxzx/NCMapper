using NCMapper.Attributes;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// JSON 序列化映射测试 — [MapProperty(IsFromJson = true)] 将 JSON 字符串反序列化为对象或集合
    /// </summary>
    public class JsonMappingTests
    {
        /// <summary>
        /// 源实体将配置和列表存储为 JSON 字符串，
        /// DTO 通过 IsFromJson=true 将其反序列化为强类型对象和 List&lt;string&gt;
        /// </summary>
        [Fact]
        public void IsFromJson_Deserializes_Object_And_List()
        {
            var entity = new JsonFieldEntityBuilder()
                .WithId(1)
                .WithConfigJson("{\"Theme\":\"Dark\",\"FontSize\":14}")
                .WithTags("csharp", "dotnet", "mapper")
                .Build();

            var dto = Mapper.Map<JsonFieldEntity, JsonToObjectDto>(entity)!;

            // 同名字段，直接赋值
            Assert.Equal(1, dto.Id);

            // [MapProperty(IsFromJson=true)] → JSON 反序列化为 AppConfig
            Assert.NotNull(dto.Config);
            Assert.Equal("Dark", dto.Config!.Theme);
            Assert.Equal(14, dto.Config.FontSize);

            // [MapProperty(IsFromJson=true)] → JSON 反序列化为 List<string>
            Assert.NotNull(dto.Tags);
            Assert.Equal(3, dto.Tags!.Count);
            Assert.Equal("csharp", dto.Tags[0]);
            Assert.Equal("dotnet", dto.Tags[1]);
            Assert.Equal("mapper", dto.Tags[2]);
        }

        // ================================================================
        // T13: JsonMapping 扩充测试
        // ================================================================

        /// <summary>
        /// 嵌套 JSON 对象数组 — JSON 字符串含多个 AppConfig 对象，
        /// 反序列化为 List&lt;AppConfig&gt;，验证每个元素的属性
        /// </summary>
        [Fact]
        public void IsFromJson_NestedObjectArray_Deserializes()
        {
            var entity = new JsonFieldEntity
            {
                Id = 1,
                NestedObjectsJson = "[{\"Theme\":\"Light\",\"FontSize\":12},{\"Theme\":\"Dark\",\"FontSize\":16}]"
            };

            var dto = Mapper.Map<JsonFieldEntity, JsonNestedDto>(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.NotNull(dto.NestedConfigs);
            Assert.Equal(2, dto.NestedConfigs!.Count);
            Assert.Equal("Light", dto.NestedConfigs[0].Theme);
            Assert.Equal(12, dto.NestedConfigs[0].FontSize);
            Assert.Equal("Dark", dto.NestedConfigs[1].Theme);
            Assert.Equal(16, dto.NestedConfigs[1].FontSize);
        }

        /// <summary>
        /// JSON Dictionary — JSON 字符串为键值对象，
        /// 反序列化为 Dictionary&lt;string, int&gt;
        /// </summary>
        [Fact]
        public void IsFromJson_DictionaryJson_Deserializes()
        {
            var entity = new JsonFieldEntity
            {
                Id = 2,
                MetadataJson = "{\"csharp\":100,\"python\":85,\"java\":90}"
            };

            var dto = Mapper.Map<JsonFieldEntity, JsonNestedDto>(entity)!;

            Assert.Equal(2, dto.Id);
            Assert.NotNull(dto.Metadata);
            Assert.Equal(3, dto.Metadata!.Count);
            Assert.Equal(100, dto.Metadata["csharp"]);
            Assert.Equal(85, dto.Metadata["python"]);
            Assert.Equal(90, dto.Metadata["java"]);
        }

        /// <summary>
        /// 空 JSON 集合 — 空数组和空对象应正确反序列化为空集合
        /// </summary>
        [Fact]
        public void IsFromJson_EmptyCollections_DeserializesToEmpty()
        {
            var entity = new JsonFieldEntity
            {
                Id = 3,
                EmptyJsonArray = "[]",
                MetadataJson = "{}"
            };

            var dto = Mapper.Map<JsonFieldEntity, JsonEmptyDto>(entity)!;

            Assert.Equal(3, dto.Id);
            // 空数组 → 空 List
            Assert.NotNull(dto.EmptyTags);
            Assert.Empty(dto.EmptyTags!);
            // 空对象 → 空 Dictionary
            Assert.NotNull(dto.EmptyMetadata);
            Assert.Empty(dto.EmptyMetadata!);
        }

        /// <summary>
        /// 畸形 JSON — 非法 JSON 字符串应抛出异常
        /// </summary>
        [Fact]
        public void IsFromJson_MalformedJson_ThrowsException()
        {
            var entity = new JsonFieldEntity
            {
                Id = 4,
                ConfigJson = "not valid json{{{",
                TagsJson = "[\"valid\"]"
            };

            Assert.ThrowsAny<Exception>(() =>
                Mapper.Map<JsonFieldEntity, JsonToObjectDto>(entity));
        }

        // ================================================================
        // 值类型 IsFromJson 测试
        // ================================================================

        /// <summary>
        /// 值类型目标 + IsFromJson → 属性不被映射，保持 default
        /// </summary>
        [Fact]
        public void IsFromJson_ValueTypeTarget_Skipped()
        {
            var entity = new JsonValueTypeEntity
            {
                Id = 1,
                IntJson = "42",
                DateTimeJson = "\"2026-07-28T10:30:00\"",
                GuidJson = "\"d9e8f7a6-1234-5678-9abc-def012345678\""
            };

            var dto = Mapper.Map<JsonValueTypeEntity, JsonValueTypeDto>(entity)!;

            // Id 同名直接映射
            Assert.Equal(1, dto.Id);
            // 值类型属性被跳过，保持默认值
            Assert.Equal(0, dto.IntValue);
            Assert.Equal(default(DateTime), dto.DateTimeValue);
            Assert.Equal(default(Guid), dto.GuidValue);
        }

        /// <summary>
        /// 反向：对象源 → string 目标（IsFromJson=true 触发 ObjectConvertToJson）
        /// </summary>
        [Fact]
        public void IsFromJson_ObjectSource_SerializesToJson()
        {
            var source = new ObjectToJsonSource
            {
                Id = 1,
                Config = new AppConfig { Theme = "Dark", FontSize = 14 }
            };

            var dto = Mapper.Map<ObjectToJsonSource, ObjectToJsonDto>(source)!;

            Assert.Equal(1, dto.Id);
            Assert.Contains("Dark", dto.ConfigJson);
            Assert.Contains("14", dto.ConfigJson);
        }

        /// <summary>
        /// 值类型源 + IsFromJson → 跳过（值类型不是"对象"）
        /// </summary>
        [Fact]
        public void IsFromJson_ValueTypeSource_Skipped()
        {
            var source = new ValueTypeToJsonSource
            {
                Id = 1,
                Score = 99,
                Timestamp = new DateTime(2026, 7, 28, 8, 0, 0)
            };

            var dto = Mapper.Map<ValueTypeToJsonSource, ValueTypeToJsonDto>(source)!;

            Assert.Equal(1, dto.Id);
            // 值类型源不是对象，IsFromJson 跳过，string 目标保持初始值
            Assert.Equal("", dto.ScoreJson);
            Assert.Equal("", dto.TimestampJson);
        }
    }

    // ================================================================
    // 值类型 IsFromJson 测试模型
    // ================================================================

    public class JsonValueTypeEntity
    {
        public int Id { get; set; }
        public string IntJson { get; set; } = "0";
        public string DateTimeJson { get; set; } = "\"2000-01-01T00:00:00\"";
        public string GuidJson { get; set; } = "\"00000000-0000-0000-0000-000000000000\"";
    }

    [MapFrom(typeof(JsonValueTypeEntity))]
    public class JsonValueTypeDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(JsonValueTypeEntity), nameof(JsonValueTypeEntity.IntJson), IsFromJson = true)]
        public int IntValue { get; set; }

        [MapProperty(typeof(JsonValueTypeEntity), nameof(JsonValueTypeEntity.DateTimeJson), IsFromJson = true)]
        public DateTime DateTimeValue { get; set; }

        [MapProperty(typeof(JsonValueTypeEntity), nameof(JsonValueTypeEntity.GuidJson), IsFromJson = true)]
        public Guid GuidValue { get; set; }
    }

    /// <summary>对象源 → string 目标（正向序列化）</summary>
    public class ObjectToJsonSource
    {
        public int Id { get; set; }
        public AppConfig? Config { get; set; }
    }

    [MapFrom(typeof(ObjectToJsonSource))]
    public class ObjectToJsonDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(ObjectToJsonSource), nameof(ObjectToJsonSource.Config), IsFromJson = true)]
        public string ConfigJson { get; set; } = "";
    }

    /// <summary>值类型源 → string 目标（应被跳过）</summary>
    public class ValueTypeToJsonSource
    {
        public int Id { get; set; }
        public int Score { get; set; }
        public DateTime Timestamp { get; set; }
    }

    [MapFrom(typeof(ValueTypeToJsonSource))]
    public class ValueTypeToJsonDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(ValueTypeToJsonSource), nameof(ValueTypeToJsonSource.Score), IsFromJson = true)]
        public string ScoreJson { get; set; } = "";

        [MapProperty(typeof(ValueTypeToJsonSource), nameof(ValueTypeToJsonSource.Timestamp), IsFromJson = true)]
        public string TimestampJson { get; set; } = "";
    }
}
