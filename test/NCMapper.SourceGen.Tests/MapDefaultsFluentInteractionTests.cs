using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 全局 MapDefaults × Fluent 通道交互缺陷族复现矩阵（gate）。
/// <para>矩阵口径：6 缺陷 ×（有/无 MapDefaults 环境）×（Fluent/特性通道对照）双维固化；
/// 缺陷用例以正确语义（23 号页/裁决定案 ①②③=是）为断言，当前代码态下应红；
/// 对照用例为既有正确行为锚点，当前代码态下应绿。</para>
/// <para><b>落盘机制</b>缺陷用例以 <c>[Fact(Skip=" 修复后启用")]</c>
/// 持久化（保住 CI 可复跑又不使 Batch-1 后全量常红）；各修复任务「摘 Skip + 转绿」计入该缺陷验收。
/// 落盘时已先行无 Skip 实跑核验红绿（红/绿实测记录见调查报告）。</para>
/// <para><b>环境注入约定</b>：<c>//__CONFIG__</c> 占位行位于 using 指令之后（C# 程序集特性须置于
/// using 之后——CS1529），缺陷组经 Replace 注入 [assembly: MapDefaults(...)]，控制组保留注释行（无全局配置）。</para>
/// <para>根因锚点（调查同源）：
/// ①③=MappingExtractor.SnapshotConfig.cs ReplaceWithConfigAwareMatchValue（显式声明无豁免标记/源侧复检无豁免门）；
/// ②=替换性重匹配与方向物化的交互（实证形态见各用例注释）；
/// ④=ApplyGlobalConfig 对 Fluent 自动匹配面的清单应用现状（精确命中已抑制/族名不命中——探针语义勘误）；
/// ⑤=运行时 PropertyMatcher.GetPropertyList isToDto 推断（NCMapper.Tests 运行时矩阵文件承载）；
/// ⑥=属性级 NoAutoInclude 导航 dest 的 NCMAP002 连带与发射缺失。</para>
/// </summary>
public class MapDefaultsFluentInteractionTests
{
    /// <summary>SnakeCase 全局配置注入（缺陷组环境——占位行替换）。</summary>
    private const string SnakeCaseAttr = "[assembly: MapDefaults(NamingStrategy = NamingStrategy.SnakeCase)]";

    /// <summary>SnakeCase+IgnoreNames 全局配置注入（缺陷③④环境）。</summary>
    private const string SnakeCaseIgnoreAttr = "[assembly: MapDefaults(NamingStrategy = NamingStrategy.SnakeCase, IgnoreNames = new[] { \"RowVersion\" })]";

    private static void AssertGenerated(MapperGenerationResult result, string expectedFragment, string testCase)
    {
        Assert.True(result.GeneratedCode.Contains(expectedFragment),
            $"[{testCase}] 生成代码缺少「{expectedFragment}」\n=== 生成代码 ===\n{result.GeneratedCode}");
    }

    private static void AssertNotGenerated(MapperGenerationResult result, string fragment, string testCase)
    {
        Assert.False(result.GeneratedCode.Contains(fragment),
            $"[{testCase}] 生成代码不应包含「{fragment}」\n=== 生成代码 ===\n{result.GeneratedCode}");
    }

    /// <summary>正向方法体（Map_{S}_To_{T}）域断言——生成文件同时含正/反向方法，反向写回
    /// （dst.X = src.X）在全文检索时污染同名片段断言（落盘实测勘误），方向断言一律锚定正向方法体。</summary>
    private static string ForwardBody(MapperGenerationResult result, string sourceTypeName, string targetTypeName, string testCase)
    {
        var method = result.GetMethod($"Map_{sourceTypeName}_To_{targetTypeName}");
        Assert.True(method != null, $"[{testCase}] 未找到正向方法 Map_{sourceTypeName}_To_{targetTypeName}\n=== 生成代码 ===\n{result.GeneratedCode}");
        return method!.Body;
    }

    private static void AssertForwardGenerated(MapperGenerationResult result, string sourceTypeName, string targetTypeName, string fragment, string testCase)
        => Assert.Contains(fragment, ForwardBody(result, sourceTypeName, targetTypeName, testCase));

    private static void AssertForwardNotGenerated(MapperGenerationResult result, string sourceTypeName, string targetTypeName, string fragment, string testCase)
    {
        var body = ForwardBody(result, sourceTypeName, targetTypeName, testCase);
        Assert.True(!body.Contains(fragment),
            $"[{testCase}] 正向方法体不应包含「{fragment}」\n=== 正向方法体 ===\n{body}");
    }

    // ── 缺陷①：全局 NamingStrategy 下 Fluent 单段异名 Map 静默丢弃 ──────────────

    private const string F1Common = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace Mfi1;

        public class Mfi1Sub { public string Name { get; set; } = ""; }
        public class Mfi1Entity
        {
            public int Id { get; set; }
            public Mfi1Sub Customer { get; set; } = new();
        }
        public class Mfi1Dto
        {
            public int OrderId { get; set; }
            public string CustomerName { get; set; } = "";
        }

        [MapperConfig]
        public class Mfi1Mapper
        {
            public static void Configure(FluentMappingConfig cfg)
            {
                cfg.For<Mfi1Entity, Mfi1Dto>()
                   .Member(d => d.OrderId).From(s => s.Id)
                   .Member(d => d.CustomerName).From(s => s.Customer.Name);
            }
        }
        """;

    /// <summary>对照（绿锚点）：无 MapDefaults 环境，单段异名 Map 正常生成（004 隔离探针实证形态）。</summary>
    [Fact]
    public void F1_Control_NoConfig_ExplicitRenameMap_Generated()
    {
        var result = TestHelper.GenerateMapper(F1Common);
        AssertForwardGenerated(result, "Mfi1Entity", "Mfi1Dto", "OrderId = src.Id", "F1_Control");
    }

    /// <summary>缺陷①（红·落盘实测）：SnakeCase 环境下同一单段异名 Map 声明被静默丢弃（零诊断）。
    /// 正确语义（裁决①=是）：显式声明为符号级路径，不参与名称策略规范化匹配。</summary>
    [Fact]
    public void F1_Defect_SnakeCase_ExplicitRenameMap_Generated()
    {
        var result = TestHelper.GenerateMapper(F1Common.Replace("//__CONFIG__", SnakeCaseAttr));
        AssertForwardGenerated(result, "Mfi1Entity", "Mfi1Dto", "OrderId = src.Id", "F1_Defect");
    }

    /// <summary>对照（绿锚点）：SnakeCase 环境多段嵌套异名路径不受策略影响（ReplaceWithConfigAwareMatchValue
    /// 的 SourcePath.Count>1 豁免判据——缺陷① 的边界证据）。发射形态含引用型中间段空条件运算（src.Customer?.Name）。</summary>
    [Fact]
    public void F1_Control_SnakeCase_MultiSegmentMap_Kept()
    {
        var result = TestHelper.GenerateMapper(F1Common.Replace("//__CONFIG__", SnakeCaseAttr));
        AssertForwardGenerated(result, "Mfi1Entity", "Mfi1Dto", "CustomerName = src.Customer", "F1_MultiSegment");
    }

    /// <summary>对照（绿锚点）：SnakeCase 环境下显式同名 Map 存活（证明丢弃与名称归一化相关，
    /// 并非显式 Map 一律丢弃——修复设计的判据边界）。</summary>
    [Fact]
    public void F1_Control_SnakeCase_SameNameMap_Kept()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Mfi1b;

            [assembly: MapDefaults(NamingStrategy = NamingStrategy.SnakeCase)]

            public class Mfi1bEntity { public int Id { get; set; } }
            public class Mfi1bDto { public int Id { get; set; } }

            [MapperConfig]
            public class Mfi1bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Mfi1bEntity, Mfi1bDto>().Member(d => d.Id).From(s => s.Id);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        AssertForwardGenerated(result, "Mfi1bEntity", "Mfi1bDto", "Id = src.Id", "F1_SameName");
    }

    /// <summary>对照（绿锚点，特性通道）：SnakeCase 环境 [MapProperty] 显式 FromField 异名不受策略干扰
    /// （G3 恒字面——裁决① 的特性侧同构依据）。</summary>
    [Fact]
    public void A1_Control_SnakeCase_AttributeFromField_Kept()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace MfiA1;

            [assembly: MapDefaults(NamingStrategy = NamingStrategy.SnakeCase)]

            public class MfiA1Entity { public int Id { get; set; } }
            [MapFrom(typeof(MfiA1Entity))]
            public class MfiA1Dto
            {
                [MapProperty(typeof(MfiA1Entity), "Id")]
                public int OrderId { get; set; }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        AssertForwardGenerated(result, "MfiA1Entity", "MfiA1Dto", "OrderId = src.Id", "A1_Control");
    }

    // ── 缺陷②：Fluent Ignore(dest, Direction) 方向畸变 ─────────────────────────

    private const string F2Common = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace Mfi2;

        public class Mfi2Entity
        {
            public int Id { get; set; }
            public string Tag { get; set; } = "";
            public string AuditNote { get; set; } = "";
        }
        public class Mfi2Dto
        {
            public int Id { get; set; }
            public string Tag { get; set; } = "";
            public string AuditNote { get; set; } = "";
        }

        [MapperConfig]
        public class Mfi2Mapper
        {
            public static void Configure(FluentMappingConfig cfg)
            {
                cfg.For<Mfi2Entity, Mfi2Dto>()
                   .Member(d => d.Tag).Ignore(Direction.ToDto)
                   .Member(d => d.AuditNote).Ignore(Direction.ToEntity);
            }
        }
        """;

    /// <summary>对照（绿锚点）：无 MapDefaults 环境，方向矩阵正确——ToDto 正向不发射、ToEntity 正向发射。</summary>
    [Fact]
    public void F2_Control_NoConfig_DirectionIgnore_ForwardMatrix()
    {
        var result = TestHelper.GenerateMapper(F2Common);
        AssertForwardNotGenerated(result, "Mfi2Entity", "Mfi2Dto", "Tag = src.Tag", "F2_Control_ToDto");
        AssertForwardGenerated(result, "Mfi2Entity", "Mfi2Dto", "AuditNote = src.AuditNote", "F2_Control_ToEntity");
    }

    /// <summary>缺陷②-α（红·落盘实测）：SnakeCase 环境下 Ignore(ToDto) 正向应跳过而实测照发（声明失效→双向自动匹配）。
    /// 正确语义：ToDto=仅正向忽略（反向照常派生）。</summary>
    [Fact]
    public void F2_Defect_SnakeCase_ToDtoIgnore_ForwardSkipped()
    {
        var result = TestHelper.GenerateMapper(F2Common.Replace("//__CONFIG__", SnakeCaseAttr));
        AssertForwardNotGenerated(result, "Mfi2Entity", "Mfi2Dto", "Tag = src.Tag", "F2_Defect_ToDto");
    }

    /// <summary>缺陷②-β（红·落盘实测）：SnakeCase 环境下 Ignore(ToEntity) 正向应保留而实测被剔除（双向剔除——
    /// 正反向方法体均无该赋值）。正确语义：ToEntity=仅反向忽略（正向照常映射）。</summary>
    [Fact]
    public void F2_Defect_SnakeCase_ToEntityIgnore_ForwardKept()
    {
        var result = TestHelper.GenerateMapper(F2Common.Replace("//__CONFIG__", SnakeCaseAttr));
        AssertForwardGenerated(result, "Mfi2Entity", "Mfi2Dto", "AuditNote = src.AuditNote", "F2_Defect_ToEntity");
    }

    // ── 缺陷②-γ 用例族（IgnoreSource 源侧方向矩阵）随终端退役移除
    //（IgnoreSource 退役 + 源侧腿出清——④ v1.2；载体 .Ignore 形态由 A-11 等价对锁定）──

    // ── 缺陷③：Fluent overrideIgnore 豁免门声明被丢弃 + 缺陷④：IgnoreNames × Fluent 自动匹配 ──
    // （③④ 共用 SnakeCase+IgnoreNames 环境；Fluent 载体与特性通道对照分列）

    private const string F3IgnoreEnvCommon = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace Mfi3;

        public class Mfi3Entity
        {
            public int Id { get; set; }
            public string RowVersion { get; set; } = "";
            public string RowVersionPlain { get; set; } = "";
        }
        public class Mfi3Dto
        {
            public int Id { get; set; }
            public string RowVersion { get; set; } = "";
            public string RowVersionPlain { get; set; } = "";
        }

        [MapperConfig]
        public class Mfi3Mapper
        {
            public static void Configure(FluentMappingConfig cfg)
            {
                cfg.For<Mfi3Entity, Mfi3Dto>();
            }
        }
        """;

    private static string WithIgnoreAttr(string body) => body.Replace("namespace Mfi3;", $"{SnakeCaseIgnoreAttr}\nnamespace Mfi3;");

    /// <summary>缺陷④-α 实证（裁决②=是口径锚点）：SnakeCase+IgnoreNames 环境下，
    /// Fluent 链无声明、自动匹配的同名 RowVersion（精确命中清单）应被全局忽略抑制。
    /// 同时是缺陷③ 的"无豁免对照"面。</summary>
    [Fact]
    public void F3_F4_Control_SnakeCaseIgnoreNames_AutoMatchRowVersion_Suppressed()
    {
        var result = TestHelper.GenerateMapper(WithIgnoreAttr(F3IgnoreEnvCommon));
        AssertForwardNotGenerated(result, "Mfi3Entity", "Mfi3Dto", "RowVersion = src.RowVersion", "F3_F4_Control");
    }

    /// <summary>缺陷③（红）：SnakeCase+IgnoreNames 环境下 Map(dest, src, overrideIgnore: true) 豁免门声明
    /// 被丢弃（静默不生成）。正确语义（镜像 [MapProperty(OverrideIgnore=true)]）：全局忽略命中仍生成。</summary>
    [Fact]
    public void F3_Defect_SnakeCaseIgnoreNames_OverrideIgnoreExempt_Generated()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace Mfi3b;

            public class Mfi3bEntity { public int Id { get; set; } public string RowVersion { get; set; } = ""; }
            public class Mfi3bDto { public int Id { get; set; } public string RowVersion { get; set; } = ""; }

            [MapperConfig]
            public class Mfi3bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Mfi3bEntity, Mfi3bDto>()
                       .Member(d => d.RowVersion).From(s => s.RowVersion, overrideIgnore: true);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source.Replace("namespace Mfi3b;", $"{SnakeCaseIgnoreAttr}\nnamespace Mfi3b;"));
        AssertForwardGenerated(result, "Mfi3bEntity", "Mfi3bDto", "RowVersion = src.RowVersion", "F3_Defect");
    }

    /// <summary>缺陷④-β 实证：RowVersionPlain 非清单精确命中名（NameCandidateResolver.ShouldIgnoreGlobal
    /// 为精确名/前缀/后缀匹配）→ 自动匹配照常映射。本用例固化精确匹配语义，并作为 samples 02 组场景 4
    /// 注释（"RowVersionPlain 被全局忽略抑制"系族名误判）的勘误锚点——恢复强断言时按本语义修正注释。</summary>
    [Fact]
    public void F4_Evidence_SnakeCaseIgnoreNames_NonHitName_AutoMapped()
    {
        var result = TestHelper.GenerateMapper(WithIgnoreAttr(F3IgnoreEnvCommon));
        AssertForwardGenerated(result, "Mfi3Entity", "Mfi3Dto", "RowVersionPlain = src.RowVersionPlain", "F4_Evidence");
    }

    /// <summary>对照（绿锚点，特性通道）：IgnoreNames 对 [MapFrom] 通道自动匹配生效（既有正确行为，
    /// 与 GlobalConfigMatchingTests.GlobalIgnore_TargetProperty_NotEmitted 互补——本例经程序集特性注入）。</summary>
    [Fact]
    public void A4_Control_AttributeChannel_IgnoreNames_Suppressed()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace MfiA4;

            public class MfiA4Entity { public int Id { get; set; } public string RowVersion { get; set; } = ""; }
            [MapFrom(typeof(MfiA4Entity))]
            public class MfiA4Dto { public int Id { get; set; } public string RowVersion { get; set; } = ""; }
            """;
        var result = TestHelper.GenerateMapper(source.Replace("namespace MfiA4;", $"{SnakeCaseIgnoreAttr}\nnamespace MfiA4;"));
        AssertForwardNotGenerated(result, "MfiA4Entity", "MfiA4Dto", "RowVersion = src.RowVersion", "A4_Control");
    }

    // ── 缺陷⑥：属性级 NoAutoInclude(dest) 的 dest 为导航类型时连带丢弃 ──────────

    /// <summary>对照（绿锚点）：dest=List&lt;string&gt; 形态 → PropertyNoAutoIncludeTargets 正常发射
    ///（006 既有覆盖形态，D1 发射承载锚点）。</summary>
    [Fact]
    public void F6_Control_ListStringDest_TargetsEmitted()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper;
            namespace Mfi6;

            public class Mfi6aEntity { public int Id { get; set; } }
            public class Mfi6aDto { public int Id { get; set; } public List<string> AuditLogs { get; set; } = new(); }

            [MapperConfig]
            public class Mfi6aMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Mfi6aEntity, Mfi6aDto>().Member(d => d.AuditLogs).NoAutoInclude();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.False(result.Diagnostics.Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
            "F6_Control 编译错误:\n" + string.Join("\n", result.Diagnostics));
        var snapshotCode = TestHelper.GenerateRuntimeSnapshotCode(source);
        Assert.True(snapshotCode.Contains("PropertyNoAutoIncludeTargets") && snapshotCode.Contains("AuditLogs"),
            "F6_Control 快照注册段缺少 PropertyNoAutoIncludeTargets/AuditLogs:\n" + snapshotCode);
    }

    /// <summary>缺陷⑥（缩窄修复后启用，裁决③=是）：dest=导航类型（AuthorDto）+ SnakeCase 全局配置
    /// 环境 → PropertyNoAutoIncludeTargets 照常发射（已补齐 ApplyToSnapshot additive 透传）且
    /// NCMAP002 类型不兼容噪声不再发射（dest 不参与可映射性校验——导航属性为主要使用场景）。</summary>
    [Fact]
    public void F6_Defect_NavigationDest_TargetsEmitted()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper;
            using NCMapper.Attributes;
            namespace Mfi6b;

            [assembly: MapDefaults(NamingStrategy = NamingStrategy.SnakeCase)]

            public class Mfi6bAuthor { public string Name { get; set; } = ""; }
            public class Mfi6bAuthorDto { public string Name { get; set; } = ""; }
            public class Mfi6bEntity
            {
                public int Id { get; set; }
                public Mfi6bAuthor Author { get; set; } = new();
            }
            public class Mfi6bDto
            {
                public int Id { get; set; }
                public Mfi6bAuthorDto Author { get; set; } = new();
            }

            [MapperConfig]
            public class Mfi6bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<Mfi6bEntity, Mfi6bDto>().Member(d => d.Author).NoAutoInclude();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 修复面：NCMAP002 类型不兼容噪声不再发射（dest 不参与可映射性校验）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP002");
        var snapshotCode = TestHelper.GenerateRuntimeSnapshotCode(source);
        Assert.Contains("""PropertyNoAutoIncludeTargets = new string[] { "Author" }""", snapshotCode);
    }
}
