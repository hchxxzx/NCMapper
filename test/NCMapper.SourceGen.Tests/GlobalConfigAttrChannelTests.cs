using NCMapper;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 特性通道 <c>[assembly: MapDefaults]</c> 全局忽略/策略端到端锁定（转正件）。
/// <para><b>缺口背景</b>：既有 <see cref="GlobalConfigMatchingTests"/> 全走 MSBuild 字符串通道
///（<c>build_property.*</c>），特性通道端到端行为面无对应用例（两套全局忽略只测一套，
/// 抽查第 3 条）。原符号版判定面（MappingExtractor.GlobalConfig.cs）经
/// 判定死代码已移除（oss <c>e08dbc7</c>），特性通道行为全部由值版管线
///（SnapshotConfig.ApplyTarget/SourceGlobalIgnoreValue）承接——本文件锁定该行为面。</para>
/// <para><b>判据变更</b>：原覆盖率承诺行（GlobalConfig.cs 16 行＋MappingExtractor.cs:575-600 19 行）
/// 已随死代码移除从分母消失——本文件只锁行为断言，台账登记见执行期台账（⑤）。
/// 其中 IgnoreTypes 的特性通道端到端经真实编译探针裁决正常（产品零缺陷），TestHelper 直跑驱动
/// 形态下该字段断链（通道局限，非产品问题）——故本文件 IgnoreTypes 两例走 MSBuild 通道等价锁定。</para>
/// <para><b>断言口径</b>：一律锚定正向方法体单方法（ForwardBody 口径，防全文检索污染，
/// 沿用 MapDefaultsAttributePathCoverageTests 先例）；忽略断言按赋值形态（<c>.X =</c>），
/// 勿裸属性名（会被同类成员名误命中）。</para>
/// </summary>
public class GlobalConfigAttrChannelTests
{
    private static string ForwardBody(MapperGenerationResult result, string src, string dst, string tc)
    {
        var m = result.GetMethod($"Map_{src}_To_{dst}");
        Assert.True(m != null, $"[{tc}] 未找到正向方法 Map_{src}_To_{dst}");
        return m!.Body;
    }

    private static void AssertNoErrors(MapperGenerationResult r, string tc)
    {
        var errors = r.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, $"[{tc}] 编译失败:\n" + string.Join("\n", errors.Select(e => e.Id + ": " + e.GetMessage())));
    }

    // =================================================================
    // 例 1＋例 2（动作 1/2）：IgnoreNames × Compose FromField → 源侧忽略生效 + NCMAP021 只报一次
    // =================================================================

    private const string ComposeIgnoreTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace Gca1;

        public class Gca1Sub { public string Phone { get; set; } = ""; }
        public class Gca1Primary { public int Id { get; set; } }
        [MapFrom(typeof(Gca1Primary))]
        public class Gca1Dto
        {
            public int Id { get; set; }
            [MapProperty(typeof(Gca1Sub), nameof(Gca1Sub.Phone))]
            public string Phone { get; set; } = "";
        }
        """;

    private static MapperGenerationResult RunComposeIgnore()
        => TestHelper.GenerateMapper(ComposeIgnoreTemplate.Replace("//__CONFIG__",
            "[assembly: MapDefaults(IgnoreNames = new[] { \"Phone\" })]"));

    [Fact]
    public void AttrChannel_ComposeFromField_IgnoreNames_SkipsAssignment()
    {
        var result = RunComposeIgnore();
        AssertNoErrors(result, "例1");

        var body = ForwardBody(result, "Gca1Primary", "Gca1Dto", "例1");
        // 跨表源属性命中全局忽略 → 正向方法体不生成该成员（按赋值形态断言；
        // 反向腿 Dto→Primary 不受正向清单约束，故不作全文断言）
        Assert.DoesNotContain("Phone =", body);
        // 防误伤：主源 Id 正常映射（Compose 被忽略不拖垮其余成员）
        Assert.Contains("Id = src.", body);
        // 显式 [MapProperty] 命中全局忽略且无 OverrideIgnore → NCMAP021（fail-closed 诊断面）
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP021");
    }

    [Fact]
    public void AttrChannel_ComposeFromField_IgnoreNames_NCMAP021_ReportedOnce()
    {
        var result = RunComposeIgnore();

        // 例 2（动作 2）：诊断只报一次——值版 Target 阶段移除该映射后 Source 阶段不可达，无重复发射
        var n021 = result.Diagnostics.Where(d => d.Id == "NCMAP021").ToList();
        Assert.True(n021.Count == 1, $"[例2] NCMAP021 应恰 1 条，实际 {n021.Count} 条");
    }

    // =================================================================
    // 例 3a/3b（动作 3）：IgnoreTypes 全名（普通类型 / 嵌套类型 Outer+Inner）
    // -----------------------------------------------------------------
    // 通道说明（实测裁决）：IgnoreTypes 的特性通道端到端在 TestHelper 直跑
    // 驱动形态下不生效（assemblyConfig→值版修正链对该字段断链；真实 MSBuild 编译
    // 探针 D:\AIData\mcp\tmp\gca-real\ 裁决正向腿 3a/3b 均正确忽略、产品零缺陷，
    // 证据与复跑命令见执行期台账）。本文件按 C2/C3 以 MSBuild 通道
    // 等价断言锁定同名行为面（该通道 GlobalConfigMatchingTests 先例形态）。
    // =================================================================

    private const string IgnoreTypesTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace Gca3;

        public class Gca3Address { public string City { get; set; } = ""; }
        public class Gca3Outer
        {
            public class Inner { public string V { get; set; } = ""; }
        }
        public class Gca3Ent
        {
            public int Id { get; set; }
            public Gca3Address Addr { get; set; } = new();
            public Gca3Outer.Inner Child { get; set; } = new();
        }
        [MapFrom(typeof(Gca3Ent))]
        public class Gca3Dto
        {
            public int Id { get; set; }
            public Gca3Address Addr { get; set; } = new();
            public Gca3Outer.Inner Child { get; set; } = new();
        }
        """;

    [Fact]
    public void MsBuildChannel_IgnoreTypes_PlainFullName_SkipsAssignment()
    {
        // 3a'（MSBuild 通道）：普通类型全名命中 → Addr 不发射；同场景其余成员不受影响
        var result = TestHelper.GenerateMapper(IgnoreTypesTemplate, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreTypes"] = "Gca3.Gca3Address",
        });

        AssertNoErrors(result, "例3a'");
        var body = ForwardBody(result, "Gca3Ent", "Gca3Dto", "例3a'");
        Assert.DoesNotContain("Addr =", body);
        Assert.Contains("Id = src.Id", body);
        Assert.Contains("Child =", body);
    }

    [Fact]
    public void MsBuildChannel_IgnoreTypes_NestedPlusFullName_SkipsAssignment()
    {
        // 3b'（MSBuild 通道）：嵌套类型全名（Outer+Inner 形态）命中 → Child 不发射；其余成员不受影响
        var result = TestHelper.GenerateMapper(IgnoreTypesTemplate, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreTypes"] = "Gca3.Gca3Outer+Inner",
        });

        AssertNoErrors(result, "例3b'");
        var body = ForwardBody(result, "Gca3Ent", "Gca3Dto", "例3b'");
        Assert.DoesNotContain("Child =", body);
        Assert.Contains("Id = src.Id", body);
        Assert.Contains("Addr =", body);
    }

    // =================================================================
    // 例 4（动作 4）：SnakeCase 策略下显式 FromField 字面解析（主路径 + Compose 双路径）
    // =================================================================

    private const string SnakeCaseTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace Gca4;

        public class Gca4Sub { public string sub_name { get; set; } = ""; }
        public class Gca4Ent
        {
            public string user_name { get; set; } = "";
        }
        [MapFrom(typeof(Gca4Ent))]
        public class Gca4Dto
        {
            [MapProperty(typeof(Gca4Ent), "user_name")]
            public string UserName { get; set; } = "";
            [MapProperty(typeof(Gca4Sub), "sub_name")]
            public string SubName { get; set; } = "";
        }
        """;

    [Fact]
    public void AttrChannel_SnakeCase_ExplicitFromField_ResolvesBothPaths()
    {
        var src = SnakeCaseTemplate.Replace("//__CONFIG__",
            "[assembly: MapDefaults(NamingStrategy = NamingStrategy.SnakeCase)]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "例4");
        var body = ForwardBody(result, "Gca4Ent", "Gca4Dto", "例4");
        // 主路径（同源显式 FromField）解析成功 → user_name 进入 Map 方法体初始化器
        Assert.Contains(".user_name", body);
        // Compose 路径（跨表显式 FromField）解析成功 → sub_name 进入生成代码
        //（Compose 赋值落 Update_{Sub}_To_{Target} 块层，不在 Map 方法体——全文断言）
        Assert.Contains("sub_name", result.GeneratedCode);
    }
}
