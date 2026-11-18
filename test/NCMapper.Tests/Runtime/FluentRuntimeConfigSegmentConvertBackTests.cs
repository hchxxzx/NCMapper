// -1a：MemberConfig 配置段 ConvertBack 四形态的整机端运行时消费断言（必选交付）。
//
// 为什么必须落整机端：生成端断言只能证明「产物含该调用」，不能证明「运行时真消费该转换器」——
// 而反向写回正是本册 8 签名中风险最高的一组（4/8 全是 ConvertBack）。本文件同时稳住的
// N2 判定（落点跨 SourceGen.Tests 与 NCMapper.Tests 两项目 → 兜底 C3 → 抬升① → G4 编排依据）。
//
// 隔离范式沿用 FluentRuntimeConvertTests：既有先例（经验 #71/#60）——[Collection("GlobalConfigState")]
// + [MapperConfig] fixture 编译进测试程序集 → Mapper.Map 真运行时断言。
//
// fixture 必要条件（执行期探针实证，见探针方法学）：
//   ① 每字段单独成配置（同字段重复声明触发 F70）；
//   ② ConvertBack 的 dest 字段必须在 Ent 上有同名属性（否则 F18 反向写回点不存在）；
//   ③ 转换器用静态方法组（同 FluentConvertBackTests 已验证形态；lambda 形态须精确标注否则 F21/F27）。
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class FluentRuntimeConfigSegmentConvertBackTests
{
    // ==== T0 形态（无参常量）====
    public class FcsT0Entity { public string Kind { get; set; } = ""; }
    public class FcsT0Dto { public string Kind { get; set; } = ""; }

    [MapperConfig]
    internal class FcsT0Mapper
    {
        public static string Fixed() => "T0-const";

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FcsT0Entity, FcsT0Dto>()
                  .Member(d => d.Kind).From(s => s.Kind).ConvertBack(Fixed);
    }

    [Fact]
    public void ConvertBack_T0_ConfigSegment_ConsumedAtRuntime()
    {
        var dto = new FcsT0Dto { Kind = "anything" };
        var entity = Mapper.Map<FcsT0Dto, FcsT0Entity>(dto);

        Assert.Equal("T0-const", entity.Kind);
    }

    // ==== T1 形态（字段值单参）====
    public class FcsT1Entity { public string Tag { get; set; } = ""; }
    public class FcsT1Dto { public string Tag { get; set; } = ""; }

    [MapperConfig]
    internal class FcsT1Mapper
    {
        public static string Convert(string fieldValue) => "T1:" + fieldValue;

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FcsT1Entity, FcsT1Dto>()
                  .Member(d => d.Tag).From(s => s.Tag).ConvertBack(Convert);
    }

    [Fact]
    public void ConvertBack_T1_ConfigSegment_ConsumedAtRuntime()
    {
        var dto = new FcsT1Dto { Tag = "abc" };
        var entity = Mapper.Map<FcsT1Dto, FcsT1Entity>(dto);

        Assert.Equal("T1:abc", entity.Tag);
    }

    // ==== T2 形态（整源 DTO）====
    public class FcsT2Entity { public string Tag { get; set; } = ""; }
    public class FcsT2Dto { public string Tag { get; set; } = ""; public string Note { get; set; } = ""; }

    [MapperConfig]
    internal class FcsT2Mapper
    {
        public static string Build(FcsT2Dto dto) => "T2:" + dto.Note;

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FcsT2Entity, FcsT2Dto>()
                  .Member(d => d.Tag).From(s => s.Tag).ConvertBack(Build);
    }

    [Fact]
    public void ConvertBack_T2_ConfigSegment_ConsumedAtRuntime()
    {
        var dto = new FcsT2Dto { Tag = "x", Note = "n" };
        var entity = Mapper.Map<FcsT2Dto, FcsT2Entity>(dto);

        Assert.Equal("T2:n", entity.Tag);
    }

    // ==== T3 形态（DTO + 字段值双入参）====
    public class FcsT3Entity { public string Tag { get; set; } = ""; }
    public class FcsT3Dto { public string Tag { get; set; } = ""; }

    [MapperConfig]
    internal class FcsT3Mapper
    {
        public static string Build(FcsT3Dto dto, string tag) => "T3:" + tag + ":" + dto.Tag.Length;

        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FcsT3Entity, FcsT3Dto>()
                  .Member(d => d.Tag).From(s => s.Tag).ConvertBack(Build);
    }

    [Fact]
    public void ConvertBack_T3_ConfigSegment_ConsumedAtRuntime()
    {
        var dto = new FcsT3Dto { Tag = "abcd" };
        var entity = Mapper.Map<FcsT3Dto, FcsT3Entity>(dto);

        Assert.Equal("T3:abcd:4", entity.Tag);
    }

    // ==== Update 形态（Map(src, dst) 两参——反向写回进既有实体；重审计 A-8 乙案补齐） ====

    [Fact]
    public void ConvertBack_T0_UpdateForm_WritesBack()
    {
        var dst = new FcsT0Entity { Kind = "old" };
        Mapper.Map<FcsT0Dto, FcsT0Entity>(new FcsT0Dto { Kind = "anything" }, dst);

        Assert.Equal("T0-const", dst.Kind);
    }

    [Fact]
    public void ConvertBack_T1_UpdateForm_WritesBack()
    {
        var dst = new FcsT1Entity { Tag = "old" };
        Mapper.Map<FcsT1Dto, FcsT1Entity>(new FcsT1Dto { Tag = "abc" }, dst);

        Assert.Equal("T1:abc", dst.Tag);
    }

    [Fact]
    public void ConvertBack_T2_UpdateForm_WritesBack()
    {
        var dst = new FcsT2Entity { Tag = "old" };
        Mapper.Map<FcsT2Dto, FcsT2Entity>(new FcsT2Dto { Tag = "x", Note = "n" }, dst);

        Assert.Equal("T2:n", dst.Tag);
    }

    [Fact]
    public void ConvertBack_T3_UpdateForm_WritesBack()
    {
        var dst = new FcsT3Entity { Tag = "old" };
        Mapper.Map<FcsT3Dto, FcsT3Entity>(new FcsT3Dto { Tag = "abcd" }, dst);

        Assert.Equal("T3:abcd:4", dst.Tag);
    }
}