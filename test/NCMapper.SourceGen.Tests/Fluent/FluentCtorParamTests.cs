using System.Globalization;
using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// MapCtorParam 显式构造器形参绑定测试矩阵（
/// 16 行场景的生成产物/诊断面承载——复刻型模板，DiagnosticFluentTests 先例）。
/// <para>语义点对应，字节门行（未用者零变化）由
/// generated-baseline 门禁 + 既有等价对 fixture 承载（G3 权威口径），运行时回退回归行
/// 由 NCMapper.Tests.MapCtorParamRuntimeTests 承载，record 等价边界行由 EquivalenceTests.
/// MapCtorParam.cs承载——本文件注释行号矩阵逐行登记。</para>
/// </summary>
public class FluentCtorParamTests
{
    // ── 矩阵行 1：形参无属性锚点改绑（形态 1 源 lambda）——绑定优先 ──

    /// <summary>形参 nick 不对应任何目标属性（无 .Map 可锚定）→ 构造形参收到源属性值。</summary>
    [Fact]
    public void Row1_Shape1_NoPropertyAnchor_BindsSourceValue()
    {
        var source = """
            using NCMapper;
            public class FcpR1Entity { public string NickName { get; set; } = ""; }
            public class FcpR1Dto
            {
                public string Nick { get; }
                public FcpR1Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR1Entity, FcpR1Dto>()
                       .MapCtorParam("nick", s => s.NickName);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var body = result.Methods["Map_FcpR1Entity_To_FcpR1Dto"][0].Body;
        // 机制生效断言（#17）：构造实参 = 绑定源属性直访
        Assert.Contains("new FcpR1Dto(src.NickName)", body);
    }

    // ── 矩阵行 2：形态 2（源属性名字符串）──

    /// <summary>形态 2 字符串源属性名 → 与形态 1 同等绑定生效。</summary>
    [Fact]
    public void Row2_Shape2_StringOverload_BindsSourceValue()
    {
        var source = """
            using NCMapper;
            public class FcpR2Entity { public string NickName { get; set; } = ""; }
            public class FcpR2Dto
            {
                public string Nick { get; }
                public FcpR2Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR2Entity, FcpR2Dto>()
                       .MapCtorParam("nick", "NickName");
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var body = result.Methods["Map_FcpR2Entity_To_FcpR2Dto"][0].Body;
        Assert.Contains("new FcpR2Dto(src.NickName)", body);
    }

    // ── 矩阵行 3：形态 1 非纯路径（沿用既有诊断机制——B12 不新占码位）──

    /// <summary>方法调用 lambda → LambdaParser 既有 F07 error + 绑定声明丢弃（无绑定实参）。</summary>
    [Fact]
    public void Row3a_Shape1_MethodCall_RejectedByExistingF07()
    {
        var source = """
            using NCMapper;
            public class FcpR3Entity { public string NickName { get; set; } = ""; }
            public class FcpR3Dto
            {
                public string Nick { get; }
                public FcpR3Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR3Entity, FcpR3Dto>()
                       .MapCtorParam("nick", s => s.NickName.Trim());
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f07 = result.Diagnostics.Where(d => d.Id == "NCMAP_F07").ToList();
        Assert.Single(f07); // 沿用既有诊断机制——MapCtorParam 不新占码位（B12）
        var body = result.Methods["Map_FcpR3Entity_To_FcpR3Dto"][0].Body;
        Assert.DoesNotContain("src.NickName", body); // 声明丢弃——落 default 路径
    }

    /// <summary>多段链 lambda：Map src 合法路径但 MapCtorParam 仅单段——F55 拒绝
    ///（契约转正：原静默丢弃 → 显式 F55；绑定仍不生效）。</summary>
    [Fact]
    public void Row3b_Shape1_MultiSegment_RejectedByF55()
    {
        var source = """
            using NCMapper;
            public class FcpR3bInner { public string Value { get; set; } = ""; }
            public class FcpR3bEntity { public FcpR3bInner Nested { get; set; } = new(); }
            public class FcpR3bDto
            {
                public string Nick { get; }
                public FcpR3bDto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR3bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR3bEntity, FcpR3bDto>()
                       .MapCtorParam("nick", s => s.Nested.Value);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // B5-4，多段为 Map 合法路径——MapCtorParam 单段门原静默丢弃 → 现 F55（Warning）
        var f55 = result.Diagnostics.Where(d => d.Id == "NCMAP_F55").ToList();
        Assert.Single(f55);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f55[0].Severity);
        Assert.Contains("Nested.Value", f55[0].GetMessage());
        var body = result.Methods["Map_FcpR3bEntity_To_FcpR3bDto"][0].Body;
        Assert.DoesNotContain("src.Nested.Value", body);
    }

    // ── 矩阵行 4：绑定形参名不存在于任何公共构造器参数集（→ F52）──

    /// <summary>F52 正例：形参名不存在 → Error + 消息含可用参数集。</summary>
    [Fact]
    public void Row4_F52_ParamNotFound_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcpR4Entity { public string NickName { get; set; } = ""; }
            public class FcpR4Dto
            {
                public string Nick { get; }
                public FcpR4Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR4Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR4Entity, FcpR4Dto>()
                       .MapCtorParam("nickname", s => s.NickName);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f52 = result.Diagnostics.Where(d => d.Id == "NCMAP_F52").ToList();
        Assert.Single(f52);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f52[0].Severity);
        Assert.Contains("nick", f52[0].GetMessage()); // 提示可用参数集（定稿）
        // 报错条目不建表——绑定未生效
        var body = result.Methods["Map_FcpR4Entity_To_FcpR4Dto"][0].Body;
        Assert.DoesNotContain("src.NickName", body);
    }

    // ── 矩阵行 5：绑定形参名存在于部分候选构造器（选器加权）──

    /// <summary>绑定名仅存在于有参候选 → 命中构造器被选中（自动匹配散属性照常赋值）。</summary>
    [Fact]
    public void Row5_SelectorWeighting_PartialCandidates_Selected()
    {
        var source = """
            using NCMapper;
            public class FcpR5Entity { public string NickName { get; set; } = ""; }
            public class FcpR5Dto
            {
                public string Nick { get; set; } = "";
                public FcpR5Dto() { }
                public FcpR5Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR5Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR5Entity, FcpR5Dto>()
                       .MapCtorParam("nick", s => s.NickName);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var body = result.Methods["Map_FcpR5Entity_To_FcpR5Dto"][0].Body;
        // 选器加权：命中绑定形参名的有参候选被选中（未用绑定时"参数最多优先"同样选它——
        // 加权可见性由 Row10b 反例承载；本行锁定加权不破坏选中结果）
        Assert.Contains("new FcpR5Dto(src.NickName)", body);
    }

    // ── 矩阵行 6：绑定源属性不存在（形态 2 拼错 → F51）──

    /// <summary>F51 正例：形态 2 源属性名拼错 → Error（提取期单点解析拦截）。</summary>
    [Fact]
    public void Row6_F51_SourceNotFound_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcpR6Entity { public string NickName { get; set; } = ""; }
            public class FcpR6Dto
            {
                public string Nick { get; }
                public FcpR6Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR6Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR6Entity, FcpR6Dto>()
                       .MapCtorParam("nick", "NickNmae");
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f51 = result.Diagnostics.Where(d => d.Id == "NCMAP_F51").ToList();
        Assert.Single(f51);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f51[0].Severity);
        Assert.Contains("NickNmae", f51[0].GetMessage());
    }

    // ── 矩阵行 7：同一形参重复绑定（→ F53 duplicate）──

    /// <summary>F53 正例①：两次 MapCtorParam 同形参 → Error（duplicate），首条生效。</summary>
    [Fact]
    public void Row7_F53_DuplicateBinding_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcpR7Entity { public string NickName { get; set; } = ""; public string Nick2 { get; set; } = ""; }
            public class FcpR7Dto
            {
                public string Nick { get; }
                public FcpR7Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR7Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR7Entity, FcpR7Dto>()
                       .MapCtorParam("nick", s => s.NickName)
                       .MapCtorParam("nick", s => s.Nick2);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f53 = result.Diagnostics.Where(d => d.Id == "NCMAP_F53").ToList();
        Assert.Single(f53);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f53[0].Severity);
        Assert.Contains("duplicate", f53[0].GetMessage());
        // 首条生效（后条报错不建表）——实参来自首条绑定源
        var body = result.Methods["Map_FcpR7Entity_To_FcpR7Dto"][0].Body;
        Assert.Contains("new FcpR7Dto(src.NickName)", body);
        Assert.DoesNotContain("src.Nick2", body);
    }

    /// <summary>F51/F53 邻界反例：源属性存在且单条绑定 → 不报 F51/F53。</summary>
    [Fact]
    public void RowOk_CtorParamSingleValidBinding_NoF51F53()
    {
        var source = """
            using NCMapper;
            public class FcpOkEntity { public string NickName { get; set; } = ""; }
            public class FcpOkDto
            {
                public string Nick { get; }
                public FcpOkDto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpOkMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpOkEntity, FcpOkDto>()
                       .MapCtorParam("nick", "NickName");
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F51");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F53");
    }

    // ── 矩阵行 8：.Map 显式条目同形参（子规则 003 R-1 → F53 map-conflict）──

    /// <summary>F53 正例②：record 同形参 .Map + .MapCtorParam → Error（fail-fast 非静默覆盖）。</summary>
    [Fact]
    public void Row8_F53_MapConflict_SameCtorParam_Diagnosed()
    {
        var source = """
            using NCMapper;
            public record FcpR8Entity(string Name, string Full);
            public record FcpR8Dto(string Full);

            [MapperConfig]
            public class FcpR8Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR8Entity, FcpR8Dto>()
                       .Member(d => d.Full).From(s => s.Full)
                       .MapCtorParam("Full", s => s.Full);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f53 = result.Diagnostics.Where(d => d.Id == "NCMAP_F53").ToList();
        Assert.Single(f53);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f53[0].Severity);
        Assert.Contains("map-conflict", f53[0].GetMessage());
        // 矛盾条目不建表——ctor Full 形参回落 .Map 的 pm 匹配（既有同名逻辑，非静默覆盖：error 已可见）
        var body = result.Methods["Map_FcpR8Entity_To_FcpR8Dto"][0].Body;
        Assert.Contains("new FcpR8Dto(src.Full)", body);
    }

    /// <summary>F53 正例②变体：.Map 声明名与绑定形参名仅大小写分叉 → 同判 map-conflict
    ///（形参匹配全局 OrdinalIgnoreCase 口径——评审修正：fs.Declarations 键 Ordinal 不得漏检）。</summary>
    [Fact]
    public void Row8b_F53_MapConflict_CaseDivergence_Diagnosed()
    {
        var source = """
            using NCMapper;
            public record FcpR8bEntity(string Name, string Full);
            public record FcpR8bDto(string Full);

            [MapperConfig]
            public class FcpR8bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR8bEntity, FcpR8bDto>()
                       .Member(d => d.Full).From(s => s.Full)
                       .MapCtorParam("full", s => s.Name);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f53 = result.Diagnostics.Where(d => d.Id == "NCMAP_F53").ToList();
        Assert.Single(f53);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f53[0].Severity);
        Assert.Contains("map-conflict", f53[0].GetMessage());
        // 矛盾条目不建表——绑定源 Name 不入产物
        var body = result.Methods["Map_FcpR8bEntity_To_FcpR8bDto"][0].Body;
        Assert.DoesNotContain("src.Name", body);
    }

    // ── 矩阵行 9：MapList/内联元素级绑定（004 R-2 双点同步）──

    /// <summary>MapList 内联元素构造走绑定实参（内联匹配循环与 Core 同步——三站点之二）。</summary>
    [Fact]
    public void Row9_MapList_InlineBinding_Effective()
    {
        var source = """
            using NCMapper;
            public class FcpR9Entity { public string NickName { get; set; } = ""; }
            public class FcpR9Dto
            {
                public string Nick { get; }
                public FcpR9Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR9Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR9Entity, FcpR9Dto>()
                       .MapCtorParam("nick", s => s.NickName);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var listBody = result.Methods["MapList_FcpR9Entity_To_FcpR9Dto"][0].Body;
        Assert.Contains("new FcpR9Dto(src.NickName)", listBody);
    }

    /// <summary>绑定非空绕过无参构造器（内联门与 Core 门同步——Row5 的 MapList 镜像，防矩阵分叉）。</summary>
    [Fact]
    public void Row9b_MapList_BindingOverridesParameterlessCtor()
    {
        var source = """
            using NCMapper;
            public class FcpR9bEntity { public string NickName { get; set; } = ""; }
            public class FcpR9bDto
            {
                public string Nick { get; set; } = "";
                public FcpR9bDto() { }
                public FcpR9bDto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR9bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR9bEntity, FcpR9bDto>()
                       .MapCtorParam("nick", s => s.NickName);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var listBody = result.Methods["MapList_FcpR9bEntity_To_FcpR9bDto"][0].Body;
        // 内联门同步：绑定非空 → 命中构造器实参形态（非 new T{} 初始化器形态）
        Assert.Contains("new FcpR9bDto(src.NickName)", listBody);
    }

    // ── 矩阵行 10：多等权构造器 advisory（机械口径）+ 绑定消解反例 ──

    /// <summary>F54 正例：两个公共 ctor 并列第一（int/string 形参各一）且绑定空 → Info。</summary>
    [Fact]
    public void Row10a_AmbiguousConstructors_NoBinding_AdvisoryEmitted()
    {
        var source = """
            using NCMapper;
            public class FcpR10Entity { public string Name { get; set; } = ""; public int Code { get; set; } }
            public class FcpR10Dto
            {
                public string Name { get; set; } = "";
                public int Code { get; set; }
                public FcpR10Dto(int a) { Code = a; }
                public FcpR10Dto(string b) { Name = b; }
            }

            [MapperConfig]
            public class FcpR10Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR10Entity, FcpR10Dto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f54 = result.Diagnostics.Where(d => d.Id == "NCMAP_F54").ToList();
        Assert.Single(f54);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, f54[0].Severity);
    }

    /// <summary>F54 反例：绑定非空（显式绑定已表达消解意图）→ 不报 advisory。</summary>
    [Fact]
    public void Row10b_BindingResolvesAmbiguity_NoAdvisory()
    {
        var source = """
            using NCMapper;
            public class FcpR10bEntity { public string Name { get; set; } = ""; }
            public class FcpR10bDto
            {
                public string Name { get; set; } = "";
                public FcpR10bDto(int a) { }
                public FcpR10bDto(string b) { Name = b; }
            }

            [MapperConfig]
            public class FcpR10bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR10bEntity, FcpR10bDto>()
                       .MapCtorParam("b", s => s.Name);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F54");
        var body = result.Methods["Map_FcpR10bEntity_To_FcpR10bDto"][0].Body;
        Assert.Contains("new FcpR10bDto(src.Name)", body);
    }

    // ── 矩阵行 11：绑定 + MapWhen 组合（条件假 → ctor 形参 default）──

    /// <summary>同名 pm（自动匹配 Nick↔Nick）携带 MapWhen → 绑定实参被守卫三元包裹（修饰继承）。</summary>
    [Fact]
    public void Row11_BoundPlusMapWhen_FalseYieldsDefault()
    {
        var source = """
            using NCMapper;
            public class FcpR11Entity { public string NickName { get; set; } = ""; public string Nick { get; set; } = ""; public string Tag { get; set; } = ""; }
            public class FcpR11Dto
            {
                public string Nick { get; set; } = "";
                public FcpR11Dto(string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR11Mapper
            {
                public static bool Gate(string v) => v.Length > 0;

                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR11Entity, FcpR11Dto>()
                       .MapCtorParam("nick", s => s.NickName)
                       .Member(d => d.Nick).MapWhen(FcpR11Mapper.Gate);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var body = result.Methods["Map_FcpR11Entity_To_FcpR11Dto"][0].Body;
        // 命中同名 pm（Target=Nick）——守卫三元包裹绑定源访问（条件假 → default）
        Assert.Contains("? src.NickName : default(string)", body);
    }

    // ── 矩阵行 12：绑定 + Convert 修饰继承──

    /// <summary>同名 pm（自动匹配 Nick↔Nick）携带 Convert → 绑定源值经转换器调用（修饰自动继承）。</summary>
    [Fact]
    public void Row12_BoundPlusConvert_ConverterInherited()
    {
        var source = """
            using NCMapper;
            public class FcpR12Entity { public string NickName { get; set; } = ""; public string Nick { get; set; } = ""; }
            public class FcpR12Dto
            {
                public string Nick { get; set; } = "";
                public FcpR12Dto(string nick) { Nick = nick; }
            }
            public static class FcpR12Conv { public static string ToText(string v) => "v:" + v; }

            [MapperConfig]
            public class FcpR12Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR12Entity, FcpR12Dto>()
                       .MapCtorParam("nick", s => s.NickName)
                       .Member(d => d.Nick).Convert<string>(FcpR12Conv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var body = result.Methods["Map_FcpR12Entity_To_FcpR12Dto"][0].Body;
        // Convert 修饰继承——转换器作用于绑定源值
        Assert.Contains("FcpR12Conv.ToText(src.NickName)", body);
    }

    // ── 矩阵行 14：ByRef/指针形参排除绑定比对──

    /// <summary>ref 形参不参与绑定比对 → 绑定 ref 形参名报 F52（可用集排除 ref）。</summary>
    [Fact]
    public void Row14_ByRefParam_ExcludedFromBinding()
    {
        var source = """
            using NCMapper;
            public class FcpR14Entity { public string NickName { get; set; } = ""; }
            public struct FcpR14Dto
            {
                public string Nick { get; }
                public FcpR14Dto(ref string nick) { Nick = nick; }
            }

            [MapperConfig]
            public class FcpR14Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR14Entity, FcpR14Dto>()
                       .MapCtorParam("nick", s => s.NickName);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f52 = result.Diagnostics.Where(d => d.Id == "NCMAP_F52").ToList();
        Assert.Single(f52); // ref 形参名不在可用集（跳过比对）→ F52
    }

    // ── 矩阵行 15：未用 MapCtorParam 的既有 record/带参对——绑定空分支零变化 ──

    /// <summary>未用绑定的 record 对：构造器路径与既有逐字节同型（B9 快照面——产物锁定断言）。</summary>
    [Fact]
    public void Row15_NoBinding_RecordCtorPath_Unchanged()
    {
        var source = """
            using NCMapper;
            public record FcpR15Entity(string Name);
            public record FcpR15Dto(string Name);

            [MapperConfig]
            public class FcpR15Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpR15Entity, FcpR15Dto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var body = result.Methods["Map_FcpR15Entity_To_FcpR15Dto"][0].Body;
        // 自动同名 pm 命中 ctor 形参（既有路径）——产物形态锁定
        Assert.Contains("new FcpR15Dto(src.Name)", body);
        // 未用绑定 → 绑定族诊断（F51~F54）零发射
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F5"));
    }

    // ── 矩阵行 13/16 由专项文件承载 ──
    // 行 13（record .Map ↔ .MapCtorParam 等价性边界）→ EquivalenceTests.MapCtorParam.cs
    // 行 16（运行时回退回归——动态 Mapper.Map 带参构造对）→ NCMapper.Tests.MapCtorParamRuntimeTests

    // ── B4-02：F54 实参错配矫正（审计批次二）──
    // 缺陷态：Args 仅 1 实参 vs resx 2 占位符（{1}=并列数量）→ GetMessage 回落裸模板
    //（Roslyn FormatException 无异常信号）。修复：Args 补第二实参（tiedCount）。

    /// <summary>
    /// B4-02 主样例：F54 消息格式化正确（含并列数量 2、无裸占位符残留）。
    /// fixture 与 Row10a 同构（新类名）——Row10a 锁 Id/Severity，本样例锁消息渲染。
    /// </summary>
    [Fact]
    public void F54_MessageFormatted_TiedCountPresent_NoRawPlaceholder()
    {
        var source = """
            using NCMapper;
            public class FcpAmbMsgEntity { public string Name { get; set; } = ""; public int Code { get; set; } }
            public class FcpAmbMsgDto
            {
                public string Name { get; set; } = "";
                public int Code { get; set; }
                public FcpAmbMsgDto(int a) { Code = a; }
                public FcpAmbMsgDto(string b) { Name = b; }
            }

            [MapperConfig]
            public class FcpAmbMsgMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcpAmbMsgEntity, FcpAmbMsgDto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f54 = result.Diagnostics.Where(d => d.Id == "NCMAP_F54").ToList();
        Assert.Single(f54);

        // 锁定英文默认资源：GetMessage() 不传语言参数时会跟随操作系统语言取资源，
        // 中文系统上拿到中文文案会让下面的英文断言误报失败（2026-09-28 本机 6 例失败的根因）。
        // 本测试验证的是英文资源的文案格式与参数位置，与文化环境无关。
        var message = f54[0].GetMessage(CultureInfo.InvariantCulture);
        // 第二实参（并列数量 2）已格式化进消息（缺陷态 {1} 裸露模板 → 红）
        Assert.Contains("2 public constructors tie", message);
        Assert.DoesNotContain("{1}", message);
    }
}
