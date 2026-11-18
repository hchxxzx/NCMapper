// P5②（Check 5 L3 桶 承载——004 #17 构造器形参整机端到端）：
// Fluent MapCtorParam 生成通道整机锚（L2 形态矩阵 = SourceGen.Tests/Fluent/FluentCtorParamTests；
// 运行时回退腿 = MapCtorParamRuntimeTests——该文件与生成注册隔离，故生成通道锚另址）。
// 隔离范式（经验 #71/#60）：[Collection("GlobalConfigState")]；[MapperConfig] fixture 编译进测试程序集
// → Mapper.Map 真运行时断言（构造形参收到源属性值）。
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class FluentRuntimeCtorParamTests
{
    public class FcpGenEntity { public string NickName { get; set; } = "gen"; }

    public class FcpGenDto
    {
        public string Nick { get; }
        public FcpGenDto(string nick) { Nick = nick; }
    }

    [MapperConfig]
    internal class FcpGenMapper
    {
        public static void Configure(FluentMappingConfig cfg)
            => cfg.For<FcpGenEntity, FcpGenDto>()
                  .MapCtorParam("nick", s => s.NickName);
    }

    /// <summary>构造器形参（Fluent 独有）：形参无属性锚点改绑——构造形参收到源属性值（整机端到端）。</summary>
    [Fact]
    [Trait("Capability", "B-04")]
    public void Generated_MapCtorParam_EndToEnd_FlowsSourceValue()
    {
        var dst = Mapper.Map<FcpGenEntity, FcpGenDto>(new FcpGenEntity());
        Assert.NotNull(dst);
        Assert.Equal("gen", dst!.Nick);
    }
}
