using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// MapCtorParam — 显式构造器形参绑定（Fluent-only 等价能力
/// 第八批能力登记）。特性侧裁决 No-Go（Stripped 备案 X2）——
/// 登记 B 类（ChannelSupport.FluentOnly）而非 A 类双通道。
/// <para>等价形态：record 同名场景 <c>.MapCtorParam("Full", s => s.Full)</c> 直喂形参 ↔
/// 等价 <c>.Member(d =&gt; d.Full).From(s =&gt; s.Full)</c>（同通道终端对，003 R-1 边界澄清——
/// record 名不一致场景既有 .Map 即可解决，MapCtorParam 与其产出等价结果）。
/// L2 文本等价（同通道终端对——不走 AssertEquivalent 的 Attr/Fluent 双源提取）+
/// 机制生效断言（#17：产物确实收到绑定源属性值，防"两侧同错产物假等价"）。</para>
/// </summary>
public class EquivalenceTestsMapCtorParam
{
    // .Map 侧：显式属性映射（pm.Target=Full 命中 record ctor 形参 Full——同名匹配既有路径）
    private const string MapSideSource = """
        using NCMapper;
        namespace EqPairs.B04
        {
            public record B04RecSrc(string Name, string Full);
            public record B04RecDst(string Full);

            [MapperConfig]
            public class B04MapSideMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<B04RecSrc, B04RecDst>()
                       .Member(d => d.Full).From(s => s.Full);
                }
            }
        }
        """;

    // .MapCtorParam 侧：显式 ctor 形参绑定（绑定表命中替代同名比对——谓词变化）
    private const string CtorParamSideSource = """
        using NCMapper;
        namespace EqPairs.B04
        {
            public record B04RecSrc(string Name, string Full);
            public record B04RecDst(string Full);

            [MapperConfig]
            public class B04CtorParamSideMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<B04RecSrc, B04RecDst>()
                       .MapCtorParam("Full", s => s.Full);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_B04RecSrc_To_B04RecDst";

    [Fact]
    [EqPair("B-04")]
    [Trait("EqPair", "B-04")]
    public void B04_MapCtorParam_Matches_Map_SameRecordScenario()
    {
        var mapResult = TestHelper.GenerateMapper(MapSideSource);
        var ctorParamResult = TestHelper.GenerateMapper(CtorParamSideSource);

        var mapBody = mapResult.Methods[MapMethodName][0].Body;
        var ctorParamBody = ctorParamResult.Methods[MapMethodName][0].Body;

        // L2 文本等价：同 record 对两终端产出同型构造实参（机制生效断言——实参即绑定源属性；
        // fixture 带 namespace → 产物目标名为全限定形态）
        Assert.Contains("new EqPairs.B04.B04RecDst(src.Full)", mapBody);
        Assert.Contains("new EqPairs.B04.B04RecDst(src.Full)", ctorParamBody);
        Assert.Equal(mapBody, ctorParamBody);

        // 等价对边界：两侧绑定族诊断零发射（合法声明不误报——反例矩阵由 FluentCtorParamTests 承载）
        Assert.DoesNotContain(mapResult.Diagnostics, d => d.Id.StartsWith("NCMAP_F5"));
        Assert.DoesNotContain(ctorParamResult.Diagnostics, d => d.Id.StartsWith("NCMAP_F5"));
    }
}
