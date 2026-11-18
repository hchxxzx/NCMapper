using NCMapper;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 配置段（<c>MemberConfig</c>）8 个零调用签名的生成端覆盖。
/// <para><b>缺口成因</b>：链式API 中 <c>Member(...)</c> 返回 <c>MemberHead</c>，仅当链上已有
/// <c>From(...)</c> 时才进入 <c>MemberConfig</c>；存量测试几乎全用「裸 <c>Member</c> 首操作」写法，
/// 配置段那一半从未被行使。</para>
///
/// <para><b>与既有覆盖的分工</b>：A-24 fixture（<c>EquivalenceTests.MemberCombination</c>）已覆盖本类
/// <c>Mask</c>/<c>Format</c>/<c>FromJson</c>/<c>Convert(T1)</c>/<c>MapWhen(T1)</c> 五法 +
/// <c>FluentRuntimeConvertTests</c> 已覆盖 <c>Convert(T0)</c> + <c>FluentMemberFoolProofTests</c>
/// 已覆盖 <c>MapWhen&lt;TInput&gt;</c>；本文件补齐的 8 个签名<b>与其零重叠</b>。</para>
///
/// <para><b>fixture 必要条件（执行期探针实证，勿删）</b>：① 每个字段单独成配置——同一 <c>Member</c>
/// 字段重复声明触发 <c>F70</c>；② <c>ConvertBack</c> 的 dest 字段<b>必须在实体侧有同名属性</b>，
/// 否则触发 <c>F18</c>（反向写回点不存在）；③ lambda 形态须按
/// <c>T0=Func&lt;TEntityProp&gt;</c> / <c>T1=Func&lt;TProp,TEntityProp&gt;</c> /
/// <c>T2=Func&lt;TTarget,TEntityProp&gt;</c> / <c>T3=Func&lt;TTarget,TProp,TEntityProp&gt;</c>
/// 精确标注，否则触发 <c>F21</c>。</para>
///
/// <para><b>行为同构性</b>执行期探针对照（探索项 2）——同参数 T0 在配置段与
/// <c>MemberHead</c> 两侧产物逐字节一致，故本文件断言结构直接平移既有 MemberHead 侧同款。</para>
/// </summary>
public class FluentMemberConfigSegmentCoverageTests
{
    // =================================================================
    // ① MemberConfig.ConvertBack —— T0 无参常量
    // =================================================================

    private const string ConvertBackT0Source = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Stamp { get; set; } = ""; }
            public class Dto { public string Stamp { get; set; } = ""; }
            public static class Conv { public static string Fixed() => "fixed"; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Stamp).From(s => s.Stamp).ConvertBack<string>(Conv.Fixed);
            }
        }
        """;

    [Fact]
    public void ConvertBack_T0_Constant_ConfigSegmentEmitsReverseCall()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT0Source);

        AssertNoErrors(result);
        Assert.Contains("Conv.Fixed()", result.GeneratedCode);
        Assert.Contains("Map_Dto_To_Ent", result.GeneratedCode);
    }

    // =================================================================
    // ② MemberConfig.ConvertBack —— T1 单值（Func<TProp, TEntityProp>）
    // =================================================================

    private const string ConvertBackT1Source = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Tag { get; set; } = ""; }
            public class Dto { public string Tag { get; set; } = ""; }
            public static class Conv { public static string Convert(string fieldValue) => "back:" + fieldValue; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Tag).From(s => s.Tag).ConvertBack(Conv.Convert);
            }
        }
        """;

    [Fact]
    public void ConvertBack_T1_SingleValue_ConfigSegmentEmitsReverseCall()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT1Source);

        AssertNoErrors(result);
        Assert.Contains("Conv.Convert", result.GeneratedCode);
        Assert.Contains("Map_Dto_To_Ent", result.GeneratedCode);
    }

    // =================================================================
    // ③ MemberConfig.ConvertBack —— T2 整源（Func<TTarget, TEntityProp>）
    // =================================================================

    private const string ConvertBackT2Source = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Tag { get; set; } = ""; }
            public class Dto { public string Tag { get; set; } = ""; public string Note { get; set; } = ""; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Tag).From(s => s.Tag).ConvertBack<string>(dto => "T2:" + dto.Note);
            }
        }
        """;

    [Fact]
    public void ConvertBack_T2_WholeTarget_ConfigSegmentEmitsReverseCall()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT2Source);

        AssertNoErrors(result);
        // 锚 lambda 形参引用而非字符串字面量（重审计 A-4 残留加固：字面量受常量折叠影响可假红）
        Assert.Contains("dto.Note", result.GeneratedCode);
        Assert.Contains("Map_Dto_To_Ent", result.GeneratedCode);
    }

    // =================================================================
    // ④ MemberConfig.ConvertBack —— T3 双入参（Func<TTarget, TProp, TEntityProp>）
    // =================================================================

    private const string ConvertBackT3Source = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Stamp { get; set; } = ""; }
            public class Dto { public string Stamp { get; set; } = ""; public string Note { get; set; } = ""; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Stamp).From(s => s.Stamp).ConvertBack<string>((dto, tag) => "T3:" + tag);
            }
        }
        """;

    [Fact]
    public void ConvertBack_T3_TwoInputs_ConfigSegmentEmitsReverseCall()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT3Source);

        AssertNoErrors(result);
        // 锚 lambda 形参引用而非字符串字面量（重审计 A-4 残留加固：字面量受常量折叠影响可假红）
        Assert.Contains("tag", result.GeneratedCode);
        Assert.Contains("Map_Dto_To_Ent", result.GeneratedCode);
    }

    // =================================================================
    // ⑤ MemberConfig.Convert —— T2 整源（Func<TSource, TProp>）
    // =================================================================

    private const string ConvertT2Source = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public int First { get; set; } public int Last { get; set; } public string Full { get; set; } = ""; }
            public class Dto { public string Full { get; set; } = ""; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Full).From(s => s.Full).Convert(e => e.First + "-" + e.Last);
            }
        }
        """;

    [Fact]
    public void Convert_T2_WholeSource_ConfigSegmentEmitsCall()
    {
        var result = TestHelper.GenerateMapper(ConvertT2Source);

        AssertNoErrors(result);
        Assert.Contains("First", result.GeneratedCode);
        Assert.Contains("Last", result.GeneratedCode);
    }

    // =================================================================
    // ⑥ MemberConfig.Convert —— T3 双入参（Func<TSource, TSrcProp, TProp>）
    // =================================================================

    private const string ConvertT3Source = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public int Count { get; set; } public string Suffix { get; set; } = ""; }
            public class Dto { public string CountText { get; set; } = ""; }
            public static class Conv { public static string Build(Ent e, string srcProp) => "T3:" + e.Suffix + srcProp; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.CountText).From(s => s.Suffix)
                          .Convert(Conv.Build);
            }
        }
        """;

    [Fact]
    public void Convert_T3_TwoInputs_ConfigSegmentEmitsCall()
    {
        var result = TestHelper.GenerateMapper(ConvertT3Source);

        AssertNoErrors(result);
        // 断言消费转换器方法本身（字符串字面量会被生成器折叠，锚方法名更稳）
        Assert.Contains("Conv.Build", result.GeneratedCode);
    }

    // =================================================================
    // ⑦ MemberConfig.MapTree —— 递归映射声明
    // =================================================================

    private const string MapTreeSource = """
        using System.Collections.Generic;
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Name { get; set; } = ""; public List<Ent> Children { get; set; } = new(); }
            public class Dto { public string Name { get; set; } = ""; public List<Dto> Children { get; set; } = new(); }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Children).From(s => s.Children).MapTree();
            }
        }
        """;

    [Fact]
    public void MapTree_ConfigSegment_EmitsRecursion()
    {
        var result = TestHelper.GenerateMapper(MapTreeSource);

        AssertNoErrors(result);
        Assert.Contains("Children", result.GeneratedCode);
        // 递归形态：对 Children 自身再走一次映射
        Assert.Contains("Map_Ent_To_Dto", result.GeneratedCode);
    }

    [Fact]
    public void MapTree_CoexistsWithFieldLevelDeclaration()
    {
        var result = TestHelper.GenerateMapper(MapTreeSource);

        AssertNoErrors(result);
        // 字段级声明（From 在前）与MapTree（配置段）在同一链内共存，无诊断
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    // =================================================================
    // ⑧ MemberConfig.NoAutoInclude —— 字段级「查询不加载」
    // =================================================================

    private const string NoAutoIncludeSource = """
        using System.Collections.Generic;
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Name { get; set; } = ""; public List<string> Audit { get; set; } = new(); }
            public class Dto { public string Name { get; set; } = ""; public List<string> Audit { get; set; } = new(); }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Audit).From(s => s.Audit).NoAutoInclude();
            }
        }
        """;

    /// <summary>反态对照（与 NoAutoIncludeSource 同形，仅缺 NoAutoInclude()）——承载判别力（去掉赋值须转红）。</summary>
    private const string NoAutoIncludeControlSource = """
        using System.Collections.Generic;
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Name { get; set; } = ""; public List<string> Audit { get; set; } = new(); }
            public class Dto { public string Name { get; set; } = ""; public List<string> Audit { get; set; } = new(); }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Audit).From(s => s.Audit);
            }
        }
        """;

    [Fact]
    public void NoAutoInclude_ConfigSegment_EmitsWithoutLoading()
    {
        var result = TestHelper.GenerateMapper(NoAutoIncludeSource);

        AssertNoErrors(result);
        // 主映射产物零 F 诊断（效果断言在下方快照注册段用例——属性级 NoAutoInclude
        // 的承载在 SnapshotMetadataEmitter 条件行，主映射产物不体现）
    }

    /// <summary>
    /// 效果断言（重审计 B-5）：配置段 <c>NoAutoInclude()</c> 的真实语义 = 该属性加入
    /// <c>PropertyNoAutoIncludeTargets</c>（导航收集跳过目标），经 <c>SnapshotMetadataEmitter</c>
    /// 条件行落入快照注册段——<see cref="TestHelper.GenerateRuntimeSnapshotCode"/> 开发射开关后可观测。
    /// </summary>
    [Fact]
    public void NoAutoInclude_ConfigSegment_PropertyTargets_RegisteredInSnapshot()
    {
        // 正态：显式 NoAutoInclude 后，快照元数据登记 Audit 为导航收集跳过目标
        var with = TestHelper.GenerateRuntimeSnapshotCode(NoAutoIncludeSource);
        Assert.Contains("PropertyNoAutoIncludeTargets = new string[] { \"Audit\" }", with);

        // 反态（判别力护栏）：同形配置未声明 NoAutoInclude 则无该登记行——本断言在
        // 去掉 .NoAutoInclude() 的变异下转红（防假测试第 3 项承载）
        var ctrl = TestHelper.GenerateRuntimeSnapshotCode(NoAutoIncludeControlSource);
        Assert.DoesNotContain("PropertyNoAutoIncludeTargets", ctrl);
    }

    [Fact]
    public void NoAutoInclude_ConfigSegment_NoDuplicateWithPairLevel()
    {
        // 与对级 .Ignore().NoAutoInclude() 的 AfterIgnore 形态并存不冲突
        var result = TestHelper.GenerateMapper(NoAutoIncludeSource);

        AssertNoErrors(result);
        Assert.Contains("Audit", result.GeneratedCode);
    }

    // =================================================================
    // ⑨ 列表/更新形态发射扩展（重审计 A-8 乙案补齐：既有用例断言主映射发射，
    //    本节补 MapList 注册与 Update 形态对转换器的真实消费——G10 用例数补齐的一部分）
    // =================================================================

    [Fact]
    public void ConvertBack_T0_ListMapperRegistered()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT0Source);

        AssertNoErrors(result);
        Assert.Contains("MapList_Dto_To_Ent", result.GeneratedCode);
    }

    [Fact]
    public void ConvertBack_T1_ListMapperRegistered()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT1Source);

        AssertNoErrors(result);
        Assert.Contains("MapList_Dto_To_Ent", result.GeneratedCode);
    }

    [Fact]
    public void ConvertBack_T2_ListMapperRegistered()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT2Source);

        AssertNoErrors(result);
        Assert.Contains("MapList_Dto_To_Ent", result.GeneratedCode);
    }

    [Fact]
    public void ConvertBack_T3_ListMapperRegistered()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT3Source);

        AssertNoErrors(result);
        Assert.Contains("MapList_Dto_To_Ent", result.GeneratedCode);
    }

    [Fact]
    public void ConvertBack_T0_UpdateFormConsumesConverter()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT0Source);

        AssertNoErrors(result);
        var update = result.GetMethod("Map_Dto_To_Ent_Update");
        Assert.True(update != null, "未找到反向 Update 方法");
        Assert.Contains("Conv.Fixed()", update!.Body);
    }

    [Fact]
    public void ConvertBack_T1_UpdateFormConsumesConverter()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT1Source);

        AssertNoErrors(result);
        var update = result.GetMethod("Map_Dto_To_Ent_Update");
        Assert.True(update != null, "未找到反向 Update 方法");
        Assert.Contains("Conv.Convert", update!.Body);
    }

    [Fact]
    public void ConvertBack_T2_UpdateFormConsumesConverter()
    {
        // 方法组形态（lambda 转换器在 Update 方法体以合成方法承载、不以源文本内联——方法组逐字可见）
        var result = TestHelper.GenerateMapper(ConvertBackT2MethodGroupSource);

        AssertNoErrors(result);
        var update = result.GetMethod("Map_Dto_To_Ent_Update");
        Assert.True(update != null, "未找到反向 Update 方法");
        Assert.Contains("Conv2G.Build", update!.Body);
    }

    private const string ConvertBackT2MethodGroupSource = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Tag { get; set; } = ""; }
            public class Dto { public string Tag { get; set; } = ""; public string Note { get; set; } = ""; }
            public static class Conv2G { public static string Build(Dto dto) => "G2:" + dto.Note; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Tag).From(s => s.Tag).ConvertBack(Conv2G.Build);
            }
        }
        """;

    [Fact]
    public void ConvertBack_T3_UpdateFormConsumesConverter()
    {
        // 方法组形态（同 CB_T2——lambda 在 Update 体以合成方法承载）
        var result = TestHelper.GenerateMapper(ConvertBackT3MethodGroupSource);

        AssertNoErrors(result);
        var update = result.GetMethod("Map_Dto_To_Ent_Update");
        Assert.True(update != null, "未找到反向 Update 方法");
        Assert.Contains("Conv3G.Build", update!.Body);
    }

    private const string ConvertBackT3MethodGroupSource = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Tag { get; set; } = ""; }
            public class Dto { public string Tag { get; set; } = ""; }
            public static class Conv3G { public static string Build(Dto dto, string tag) => "G3:" + tag; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Tag).From(s => s.Tag).ConvertBack(Conv3G.Build);
            }
        }
        """;

    [Fact]
    public void Convert_T2_ListMapperRegistered()
    {
        var result = TestHelper.GenerateMapper(ConvertT2Source);

        AssertNoErrors(result);
        Assert.Contains("MapList_Ent_To_Dto", result.GeneratedCode);
    }

    [Fact]
    public void Convert_T3_ListMapperRegistered()
    {
        var result = TestHelper.GenerateMapper(ConvertT3Source);

        AssertNoErrors(result);
        Assert.Contains("MapList_Ent_To_Dto", result.GeneratedCode);
    }

    [Fact]
    public void Convert_T2_UpdateFormConsumesConverter()
    {
        // 方法组形态（同 CB_T2 口径）
        var result = TestHelper.GenerateMapper(ConvertT2MethodGroupSource);

        AssertNoErrors(result);
        var update = result.GetMethod("Map_Ent_To_Dto_Update");
        Assert.True(update != null, "未找到正向 Update 方法");
        Assert.Contains("ConvT2G.Build", update!.Body);
    }

    private const string ConvertT2MethodGroupSource = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string First { get; set; } = ""; public string Last { get; set; } = ""; public string Full { get; set; } = ""; }
            public class Dto { public string Full { get; set; } = ""; }
            public static class ConvT2G { public static string Build(Ent e) => e.First + "-" + e.Last; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Full).From(s => s.Full).Convert(ConvT2G.Build);
            }
        }
        """;

    [Fact]
    public void Convert_T3_UpdateFormConsumesConverter()
    {
        var result = TestHelper.GenerateMapper(ConvertT3Source);

        AssertNoErrors(result);
        var update = result.GetMethod("Map_Ent_To_Dto_Update");
        Assert.True(update != null, "未找到正向 Update 方法");
        Assert.Contains("Conv.Build", update!.Body);
    }

    // =================================================================
    // ⑩ 多段叠加 / lambda 形态 / 双字段独立配置（重审计 A-8 乙案补齐——
    //    探索项 3「允许多段」结论的固化 + 探针方法学 F21 校准面固化）
    // =================================================================

    private const string ConvertBackT1LambdaSource = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Tag { get; set; } = ""; }
            public class Dto { public string Tag { get; set; } = ""; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Tag).From(s => s.Tag).ConvertBack<string>((string v) => "L1:" + v);
            }
        }
        """;

    [Fact]
    public void ConvertBack_T1_LambdaForm_ConfigSegmentEmitsReverseCall()
    {
        var result = TestHelper.GenerateMapper(ConvertBackT1LambdaSource);

        AssertNoErrors(result);
        Assert.Contains("L1:", result.GeneratedCode);
        Assert.Contains("Map_Dto_To_Ent", result.GeneratedCode);
    }

    private const string ConvertT2WithMaskSource = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string First { get; set; } = ""; public string Last { get; set; } = ""; public string Full { get; set; } = ""; }
            public class Dto { public string Full { get; set; } = ""; }
            public class SegMask : IMask
            {
                public string Mask(string? value) => (value ?? "") + "#M";
            }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Full).From(s => s.Full).Convert(e => e.First + "-" + e.Last).Mask<SegMask>();
            }
        }
        """;

    [Fact]
    public void Convert_T2_ThenMask_MultiSegmentChain()
    {
        var result = TestHelper.GenerateMapper(ConvertT2WithMaskSource);

        AssertNoErrors(result);
        // 掩码类字面量留在源码——生成产物以掩码类型引用为锚
        Assert.Contains("SegMask", result.GeneratedCode);
    }

    private const string ConvertT3WithMaskSource = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string First { get; set; } = ""; public string Last { get; set; } = ""; }
            public class Dto { public string Full { get; set; } = ""; }
            public class SegMask3 : IMask
            {
                public string Mask(string? value) => (value ?? "") + "#M3";
            }
            public static class Conv3 { public static string Build(Ent e, string f) => f + "|" + e.Last; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Full).From(s => s.First).Convert(Conv3.Build).Mask<SegMask3>();
            }
        }
        """;

    [Fact]
    public void Convert_T3_ThenMask_MultiSegmentChain()
    {
        var result = TestHelper.GenerateMapper(ConvertT3WithMaskSource);

        AssertNoErrors(result);
        Assert.Contains("Conv3.Build", result.GeneratedCode);
        // 掩码类字面量留在源码——生成产物以掩码类型引用为锚
        Assert.Contains("SegMask3", result.GeneratedCode);
    }

    private const string ConvertBackT0WithFormatSource = """
        using NCMapper;
        using System;
        namespace FcSeg
        {
            public class Ent { public DateTime Stamp { get; set; } = new(2024, 6, 1, 12, 0, 0); }
            public class Dto { public string Stamp { get; set; } = ""; }
            public static class ConvF { public static DateTime Parse(string v) => DateTime.Parse(v); }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Stamp).From(s => s.Stamp).ConvertBack<DateTime>(ConvF.Parse).Format("yyyy-MM-dd");
            }
        }
        """;

    [Fact]
    public void ConvertBack_T1_WithFormat_MultiSegmentChain()
    {
        // ConvertBack(T1：Func<TProp,TEntityProp> = string→DateTime) + Format（正向格式化）同链多段
        var result = TestHelper.GenerateMapper(ConvertBackT0WithFormatSource);

        AssertNoErrors(result);
        Assert.Contains("ConvF.Parse", result.GeneratedCode);
        Assert.Contains("yyyy-MM-dd", result.GeneratedCode);
    }

    private const string ConvertBackTwoFieldsSource = """
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Tag { get; set; } = ""; public string Note { get; set; } = ""; }
            public class Dto { public string Tag { get; set; } = ""; public string Note { get; set; } = ""; }
            public static class ConvA { public static string Conv(string v) => "A:" + v; }
            public static class ConvB { public static string Back(string v) => "B:" + v; }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .Member(d => d.Tag).From(s => s.Tag).ConvertBack(ConvA.Conv)
                          .Member(d => d.Note).From(s => s.Note).ConvertBack(ConvB.Back);
            }
        }
        """;

    [Fact]
    public void ConvertBack_TwoFields_IndependentConfigs()
    {
        var result = TestHelper.GenerateMapper(ConvertBackTwoFieldsSource);

        AssertNoErrors(result);
        Assert.Contains("ConvA.Conv", result.GeneratedCode);
        Assert.Contains("ConvB.Back", result.GeneratedCode);
    }

    private const string NoAutoMatchWithSegmentSource = """
        using System.Collections.Generic;
        using NCMapper;
        namespace FcSeg
        {
            public class Ent { public string Name { get; set; } = ""; public List<string> Audit { get; set; } = new(); }
            public class Dto { public string Name { get; set; } = ""; public List<string> Audit { get; set; } = new(); }
            [MapperConfig]
            public class Cfg
            {
                public static void Configure(FluentMappingConfig cfg)
                    => cfg.For<Ent, Dto>()
                          .NoAutoMatch()
                          .Member(d => d.Name).From(s => s.Name)
                          .Member(d => d.Audit).From(s => s.Audit).NoAutoInclude();
            }
        }
        """;

    [Fact]
    public void NoAutoInclude_PairLevelNoAutoMatch_Coexists()
    {
        // 对级 NoAutoMatch（禁自动匹配）+ 显式 From 声明 + 字段级 NoAutoInclude 并存——零诊断（-1 计划第三例）
        var result = TestHelper.GenerateMapper(NoAutoMatchWithSegmentSource);

        AssertNoErrors(result);
        Assert.Contains("Map_Ent_To_Dto", result.GeneratedCode);
    }

    // =================================================================
    // 断言辅助
    // =================================================================

    /// <summary>零编译错误 + 零 F 系诊断（配置段 8 签名的共同前提）。</summary>
    private static void AssertNoErrors(MapperGenerationResult result)
    {
        var errors = result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "配置段用例编译失败:\n" + string.Join("\n", errors.Select(e => e.Id + ": " + e.GetMessage())));

        var fCodes = result.Diagnostics.Where(d => d.Id.StartsWith("NCMAP_F")).Select(d => d.Id).Distinct().ToList();
        Assert.True(fCodes.Count == 0, "触发 F 系诊断: " + string.Join(", ", fCodes));
    }
}