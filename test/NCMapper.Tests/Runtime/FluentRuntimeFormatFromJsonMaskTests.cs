// P1#2/#5（001 清单缺口 2/5/6 清零——004 重锚翻译）：
// Fluent Format / FromJson / Mask 整机端到端（此前整机 0 覆盖；特性侧参照 MapFormatRuntimeTests /
// Masks 族形态面）——Format 值断言（可空源 null→null / 枚举源）/ FromJson 双向实跑 / Mask 值序
// （正常/null/短串自定义/组合）。隔离范式（经验 #71/#60）：[Collection("GlobalConfigState")]。
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class FluentRuntimeFormatFromJsonMaskTests
{
    // ==== Format：DateTime 格式化值断言 ====
    public class FrfFEntity { public System.DateTime Stamp { get; set; } = new(2024, 6, 1); }
    public class FrfFDto { public string Stamp { get; set; } = ""; }

    [MapperConfig]
    internal class FrfFMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrfFEntity, FrfFDto>()
                  .Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd");
    }

    [Fact]
    [Trait("Capability", "A-23")]
    public void Format_EndToEnd_FormattedValue()
    {
        var dto = Mapper.Map<FrfFEntity, FrfFDto>(new FrfFEntity { Stamp = new(2024, 6, 1) });
        Assert.Equal("2024-06-01", dto.Stamp);
    }

    // ==== Format：可空源 null → null（"?. 形态 null 源 → 目标 null"语义） ====
    public class FrfNEntity { public System.DateTime? Stamp { get; set; } }
    public class FrfNDto { public string Stamp { get; set; } = ""; }

    [MapperConfig]
    internal class FrfNMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrfNEntity, FrfNDto>()
                  .Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd");
    }

    [Fact]
    [Trait("Capability", "A-23")]
    public void Format_NullableSource_Null_StaysNull()
    {
        var dto = Mapper.Map<FrfNEntity, FrfNDto>(new FrfNEntity { Stamp = null });
        Assert.Null(dto.Stamp);
    }

    // ==== Format：枚举源单参 ToString ====
    public enum FrfGrade { A, B, C }
    public class FrfEEntity { public FrfGrade Level { get; set; } = FrfGrade.B; }
    public class FrfEDto { public string Level { get; set; } = ""; }

    [MapperConfig]
    internal class FrfEMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrfEEntity, FrfEDto>()
                  .Member(d => d.Level).From(s => s.Level).Format("G");
    }

    [Fact]
    [Trait("Capability", "A-23")]
    public void Format_EnumSource_EndToEnd()
    {
        var dto = Mapper.Map<FrfEEntity, FrfEDto>(new FrfEEntity { Level = FrfGrade.C });
        Assert.Equal("C", dto.Level);
    }

    // ==== FromJson：string → 对象（JSON 反序列化实跑） ====
    public class FrfJPayload { public int Id { get; set; } public string Tag { get; set; } = ""; }
    public class FrfJEntity { public string Blob { get; set; } = ""; }
    public class FrfJDto { public FrfJPayload Blob { get; set; } = new(); }

    [MapperConfig]
    internal class FrfJMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrfJEntity, FrfJDto>()
                  .Member(d => d.Blob).FromJson();
    }

    [Fact]
    [Trait("Capability", "A-12")]
    public void FromJson_Deserializes_EndToEnd()
    {
        var dto = Mapper.Map<FrfJEntity, FrfJDto>(new FrfJEntity { Blob = """{"Id":7,"Tag":"x"}""" });
        Assert.Equal(7, dto.Blob.Id);
        Assert.Equal("x", dto.Blob.Tag);
    }

    // ==== FromJson：对象 → string（序列化实跑） ====
    public class FrfSEntity { public FrfJPayload Blob { get; set; } = new(); }
    public class FrfSDto { public string Blob { get; set; } = ""; }

    [MapperConfig]
    internal class FrfSMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrfSEntity, FrfSDto>()
                  .Member(d => d.Blob).FromJson();
    }

    [Fact]
    [Trait("Capability", "A-12")]
    public void FromJson_Serializes_EndToEnd()
    {
        var dto = Mapper.Map<FrfSEntity, FrfSDto>(new FrfSEntity { Blob = new() { Id = 3, Tag = "y" } });
        Assert.Contains("\"Id\":3", dto.Blob);
        Assert.Contains("\"Tag\":\"y\"", dto.Blob);
    }

    // ==== FromJson：null 源 → null ====
    [Fact]
    [Trait("Capability", "A-12")]
    public void FromJson_NullSource_StaysNull()
    {
        var dto = Mapper.Map<FrfJEntity, FrfJDto>(new FrfJEntity { Blob = null! });
        Assert.Null(dto.Blob);
    }

    // ==== Mask：脱敏值实跑（库内置 MaskPhone） ====
    public class FrfMEntity { public string Phone { get; set; } = ""; }
    public class FrfMDto { public string Phone { get; set; } = ""; }

    [MapperConfig]
    internal class FrfMMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrfMEntity, FrfMDto>()
                  .Member(d => d.Phone).From(s => s.Phone).Mask<MaskPhone>();
    }

    [Fact]
    [Trait("Capability", "A-03")]
    public void Mask_MaskedValue_NotPlaintext()
    {
        var dto = Mapper.Map<FrfMEntity, FrfMDto>(new FrfMEntity { Phone = "13812345678" });
        Assert.NotEqual("13812345678", dto.Phone);
    }

    // ==== Mask：null 透传（IMask.Mask(string?) 契约） ====
    [Fact]
    [Trait("Capability", "A-03")]
    public void Mask_NullInput_DefaultKept()
    {
        var dto = Mapper.Map<FrfMEntity, FrfMDto>(new FrfMEntity { Phone = null! });
        Assert.Equal("", dto.Phone);
    }

    // ==== Mask：自定义短串实现透传（自定义 IMask 生成端管线） ====
    public class FrfShortMask : IMask
    {
        public string Mask(string? value) => value == null ? "" : value.Length <= 2 ? "**" : value[..2] + "**";
    }

    public class FrfSmEntity { public string Code { get; set; } = ""; }
    public class FrfSmDto { public string Code { get; set; } = ""; }

    [MapperConfig]
    internal class FrfSmMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrfSmEntity, FrfSmDto>()
                  .Member(d => d.Code).From(s => s.Code).Mask<FrfShortMask>();
    }

    [Fact]
    [Trait("Capability", "A-03")]
    public void Mask_CustomImplementation_EndToEnd()
    {
        var dto = Mapper.Map<FrfSmEntity, FrfSmDto>(new FrfSmEntity { Code = "ABCDE" });
        Assert.Equal("AB**", dto.Code);
    }

    // ==== Mask：FormatThenMask 双标值序（先格式化再脱敏） ====
    public class FrfFmEntity { public System.DateTime Stamp { get; set; } = new(2024, 6, 1); }
    public class FrfFmDto { public string Stamp { get; set; } = ""; }

    [MapperConfig]
    internal class FrfFmMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrfFmEntity, FrfFmDto>()
                  .Member(d => d.Stamp).From(s => s.Stamp).Format("yyyy-MM-dd").Mask<FrfShortMask>();
    }

    [Fact]
    [Trait("Capability", "A-23")]
    public void Format_ThenMask_ValueOrder_EndToEnd()
    {
        // 格式化（10 字符 "2024-06-01"）后脱敏（前 2 + **）
        var dto = Mapper.Map<FrfFmEntity, FrfFmDto>(new FrfFmEntity());
        Assert.Equal("20**", dto.Stamp);
    }
}
