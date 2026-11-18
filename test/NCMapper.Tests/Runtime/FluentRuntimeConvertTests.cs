// P1#1/#4（001 清单缺口 1/4 清零——004 重锚翻译）：
// Fluent Convert/ConvertBack 整机端到端（此前整机 0 覆盖；特性侧参照 ConverterStaticMethodRuntimeTests/
// ConvertBackUsingRuntimeTests 形态面）——四签名形态（T0/T1/T2/T3）+ 正例 + 异常语义 + 反向写回 + 组合叠加。
// 隔离范式（经验 #71/#60）：[Collection("GlobalConfigState")]；[MapperConfig] fixture 编译进测试程序集
// → Mapper.Map 真运行时断言。
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class FluentRuntimeConvertTests
{
    // ==== T1 形态（字段值单参——最常见） ====
    public class FrcT1Entity { public int Count { get; set; } = 7; }
    public class FrcT1Dto { public string Count { get; set; } = ""; }

    [MapperConfig]
    internal class FrcT1Mapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrcT1Entity, FrcT1Dto>()
                  .Member(d => d.Count).From(s => s.Count).Convert<int>(v => "#" + v);
    }

    [Fact]
    [Trait("Capability", "A-10")]
    public void Convert_T1_Lambda_EndToEnd()
    {
        var dto = Mapper.Map<FrcT1Entity, FrcT1Dto>(new FrcT1Entity { Count = 42 });
        Assert.Equal("#42", dto.Count);
    }

    // ==== T0 形态（无参常量） ====
    public class FrcT0Entity { public string Kind { get; set; } = ""; }
    public class FrcT0Dto { public string Kind { get; set; } = ""; }

    [MapperConfig]
    internal class FrcT0Mapper
    {
        public static string Const() => "constant";

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrcT0Entity, FrcT0Dto>()
                  .Member(d => d.Kind).From(s => s.Kind).Convert(Const);
    }

    [Fact]
    [Trait("Capability", "A-10")]
    public void Convert_T0_MethodGroup_EndToEnd()
    {
        var dto = Mapper.Map<FrcT0Entity, FrcT0Dto>(new FrcT0Entity());
        Assert.Equal("constant", dto.Kind);
    }

    // ==== T2 形态（整源） ====
    public class FrcT2Entity { public string First { get; set; } = "a"; public string Last { get; set; } = "b"; public string Full { get; set; } = ""; }
    public class FrcT2Dto { public string Full { get; set; } = ""; }

    [MapperConfig]
    internal class FrcT2Mapper
    {
        public static string Join(FrcT2Entity s) => s.First + "-" + s.Last;

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrcT2Entity, FrcT2Dto>()
                  .Member(d => d.Full).Convert(Join);
    }

    [Fact]
    [Trait("Capability", "A-10")]
    public void Convert_T2_WholeSource_EndToEnd()
    {
        var dto = Mapper.Map<FrcT2Entity, FrcT2Dto>(new FrcT2Entity { First = "x", Last = "y" });
        Assert.Equal("x-y", dto.Full);
    }

    // ==== T3 形态（整源+字段值双参） ====
    public class FrcT3Entity { public string Tag { get; set; } = "t"; public int N { get; set; } = 3; }
    public class FrcT3Dto { public string Tag { get; set; } = ""; }

    [MapperConfig]
    internal class FrcT3Mapper
    {
        public static string Render(FrcT3Entity s, string tag) => $"{s.N}:{tag}";

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrcT3Entity, FrcT3Dto>()
                  .Member(d => d.Tag).Convert((FrcT3Entity s, string tag) => string.Concat(s.N.ToString(), ":", tag));
    }

    [Fact]
    [Trait("Capability", "A-10")]
    public void Convert_T3_Combined_EndToEnd()
    {
        var dto = Mapper.Map<FrcT3Entity, FrcT3Dto>(new FrcT3Entity { N = 9 });
        Assert.Equal("9:t", dto.Tag);
    }

    // ==== 转换器异常透传（异常语义——不吞不裹） ====
    public class FrcExEntity { public int V { get; set; } = 1; }
    public class FrcExDto { public string V { get; set; } = ""; }

    [MapperConfig]
    internal class FrcExMapper
    {
        public static string Boom(int v) => throw new InvalidOperationException("converter-boom");

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrcExEntity, FrcExDto>()
                  .Member(d => d.V).From(s => s.V).Convert<int>(Boom);
    }

    [Fact]
    [Trait("Capability", "A-10")]
    public void Convert_ConverterException_Propagates()
    {
        Assert.Throws<InvalidOperationException>(
            () => Mapper.Map<FrcExEntity, FrcExDto>(new FrcExEntity()));
    }

    // ==== ConvertBack T1 反向（Dto→Entity 实跑） ====
    public class FrcBEntity { public System.DateTime Stamp { get; set; } }
    public class FrcBDto { public string Stamp { get; set; } = "2024-06-01"; }

    [MapperConfig]
    internal class FrcBMapper
    {
        public static System.DateTime Parse(string v) => System.DateTime.Parse(v);

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrcBEntity, FrcBDto>()
                  .Member(d => d.Stamp).ConvertBack(System.DateTime.Parse);
    }

    [Fact]
    [Trait("Capability", "A-09")]
    public void ConvertBack_T1_Reverse_EndToEnd()
    {
        var entity = Mapper.Map<FrcBDto, FrcBEntity>(new FrcBDto { Stamp = "2024-06-01" });
        Assert.Equal(new System.DateTime(2024, 6, 1), entity.Stamp);
    }

    // ==== ConvertBack 反向写回（Map(src, dst) 更新形态） ====
    [Fact]
    [Trait("Capability", "A-09")]
    public void ConvertBack_UpdateForm_WritesBack()
    {
        var dst = new FrcBEntity { Stamp = new System.DateTime(2020, 1, 1) };
        Mapper.Map<FrcBDto, FrcBEntity>(new FrcBDto { Stamp = "2025-01-02" }, dst);
        Assert.Equal(new System.DateTime(2025, 1, 2), dst.Stamp);
    }

    // ==== Convert + Mask 组合实跑（先转换再脱敏——组合管线值断言） ====
    public class FrcCmEntity { public string Phone { get; set; } = "13812345678"; }
    public class FrcCmDto { public string Phone { get; set; } = ""; }

    [MapperConfig]
    internal class FrcCmMapper
    {
        public static string Normalize(string v) => "+86" + v;

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FrcCmEntity, FrcCmDto>()
                  .Member(d => d.Phone).From(s => s.Phone).Convert<string>(Normalize).Mask<MaskPhone>();
    }

    [Fact]
    [Trait("Capability", "A-03")]
    public void Convert_ThenMask_Pipeline_EndToEnd()
    {
        // 先转换（+86 前缀）再脱敏——组合管线值序断言
        var dto = Mapper.Map<FrcCmEntity, FrcCmDto>(new FrcCmEntity { Phone = "13812345678" });
        Assert.NotEqual("13812345678", dto.Phone);
        Assert.StartsWith("+86", dto.Phone);
    }
}
