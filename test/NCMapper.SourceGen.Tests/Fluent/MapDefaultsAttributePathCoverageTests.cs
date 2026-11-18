using NCMapper;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 特性侧 <c>[assembly: MapDefaults(...)]</c> 7 项零覆盖属性的赋值路径覆盖。
/// <para><b>缺口成因</b>：这 7 项在 <c>CapabilitySurfaceRegistry.Rows</c> 被登记为 NA 类「X1 不修项」，
/// 但实读源码消费面为 4~15 个文件（<c>GlobalMapConfig.cs:352</c> 特性读取→
/// <c>ProjectionNameResolver.cs:30</c> 投影名解析等）——<b>功能是活的，「不修」不等于「不测」</b>。
/// 将纠正其登记表定性。</para>
///
/// <para><b>与 MSBuild 通道的区隔</b>：<c>GlobalConfigReaderTests.ReadFromMSBuild_AllProperties_Parsed</c>
/// 覆盖的是 <b>MSBuild 独立读取实现</b>（<c>build_property.*</c>），与特性侧属性赋值是<b>两条独立路径</b>，
/// 不可互相替代——本文件用 <c>Attr_</c> 前缀显式区隔。</para>
///
/// <para><b>断言结构</b>：正反两态——属性赋值后行为确实改变（正）+ 未赋值时行为不变（反）；
/// 带 <c>ToEntity</c> 后缀的 3 项额外断言<b>正反两向</b>（正向仍映射 + 反向不填），防「误伤正向」。
/// 方向断言一律锚定<b>提取后的单方法体</b>（<c>ForwardBody</c>/<c>ReverseBody</c> 口径——反写回腿同名片段会污染全文检索）。</para>
/// </summary>
public class MapDefaultsAttributePathCoverageTests
{
    // =================================================================
    // 断言辅助（沿用 MapDefaultsFluentInteractionTests 口径）
    // =================================================================

    private static string ForwardBody(MapperGenerationResult result, string src, string dst, string tc)
    {
        var m = result.GetMethod($"Map_{src}_To_{dst}");
        Assert.True(m != null, $"[{tc}] 未找到正向方法 Map_{src}_To_{dst}");
        return m!.Body;
    }

    /// <summary>反向方法体（Map_{Dto}_To_{Ent}，参数序 src=Dto 类型名 / dst=Ent 类型名）——
    /// 重审计 B-4b：ToEntity 三属性「反向不填」断言的提取口径（与 ForwardBody 同款防全文检索污染）。</summary>
    private static string ReverseBody(MapperGenerationResult result, string src, string dst, string tc)
    {
        var m = result.GetMethod($"Map_{src}_To_{dst}");
        Assert.True(m != null, $"[{tc}] 未找到反向方法 Map_{src}_To_{dst}");
        return m!.Body;
    }

    private static void AssertForwardContains(MapperGenerationResult r, string src, string dst, string frag, string tc)
        => Assert.Contains(frag, ForwardBody(r, src, dst, tc));

    private static void AssertForwardExcludes(MapperGenerationResult r, string src, string dst, string frag, string tc)
        => Assert.True(!ForwardBody(r, src, dst, tc).Contains(frag, StringComparison.Ordinal),
            $"[{tc}] 正向方法体不应包含「{frag}」");

    private static void AssertReverseContains(MapperGenerationResult r, string src, string dst, string frag, string tc)
        => Assert.Contains(frag, ReverseBody(r, src, dst, tc));

    private static void AssertReverseExcludes(MapperGenerationResult r, string src, string dst, string frag, string tc)
        => Assert.True(!ReverseBody(r, src, dst, tc).Contains(frag, StringComparison.Ordinal),
            $"[{tc}] 反向方法体不应包含「{frag}」（仅反向忽略语义被违反）");

    /// <summary>零编译错误 + 零 F 系诊断。</summary>
    private static void AssertNoErrors(MapperGenerationResult r, string tc)
    {
        var errors = r.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, $"[{tc}] 编译失败:\n" + string.Join("\n", errors.Select(e => e.Id + ": " + e.GetMessage())));
        var f = r.Diagnostics.Where(d => d.Id.StartsWith("NCMAP_F")).Select(d => d.Id).Distinct().ToList();
        Assert.True(f.Count == 0, $"[{tc}] 触发 F 系诊断: " + string.Join(", ", f));
    }

    // =================================================================
    // ① Prefixes —— 源属性名前缀剥离
    // =================================================================

    private const string PrefixesTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdPref;

        public class MdPrefEnt { public string OldName { get; set; } = ""; }
        [MapFrom(typeof(MdPrefEnt))]
        public class MdPrefDto { public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_Prefixes_StrippedPrefix_Matches()
    {
        var src = PrefixesTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(Prefixes = new[] { \"Old\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "Prefixes=Old");
        // 正态：OldName 经前缀剥离后与 Name 自动匹配
        AssertForwardContains(result, "MdPrefEnt", "MdPrefDto", "Name", "Prefixes=Old");
    }

    [Fact]
    public void AttrPath_Prefixes_Unset_NoStripping()
    {
        var src = PrefixesTemplate.Replace("//__CONFIG__", "");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "Prefixes 未设");
        // 反态：未赋值时行为不变（Name 不映射 OldName）
        AssertForwardExcludes(result, "MdPrefEnt", "MdPrefDto", "OldName", "Prefixes 未设");
    }

    // =================================================================
    // ② Postfixes —— 源属性名后缀剥离
    // =================================================================

    private const string PostfixesTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdPost;

        public class MdPostEnt { public string NameLegacy { get; set; } = ""; }
        [MapFrom(typeof(MdPostEnt))]
        public class MdPostDto { public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_Postfixes_StrippedSuffix_Matches()
    {
        var src = PostfixesTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(Postfixes = new[] { \"Legacy\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "Postfixes=Legacy");
        AssertForwardContains(result, "MdPostEnt", "MdPostDto", "Name", "Postfixes=Legacy");
    }

    [Fact]
    public void AttrPath_Postfixes_Unset_NoStripping()
    {
        var src = PostfixesTemplate.Replace("//__CONFIG__", "");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "Postfixes 未设");
        AssertForwardExcludes(result, "MdPostEnt", "MdPostDto", "NameLegacy", "Postfixes 未设");
    }

    // =================================================================
    // ③ IgnoreNamePrefixes —— 忽略名前缀（正向不映射）
    // =================================================================

    private const string IgnoreNamePrefixesTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdIgnPre;

        public class MdIgnPreEnt { public string AuditTrail { get; set; } = ""; public string Name { get; set; } = ""; }
        [MapFrom(typeof(MdIgnPreEnt))]
        public class MdIgnPreDto { public string AuditTrail { get; set; } = ""; public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreNamePrefixes_Suppressed()
    {
        var src = IgnoreNamePrefixesTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreNamePrefixes = new[] { \"Audit\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNamePrefixes=Audit");
        // 正态：AuditTrail 不参与映射
        AssertForwardExcludes(result, "MdIgnPreEnt", "MdIgnPreDto", "AuditTrail", "IgnoreNamePrefixes=Audit");
    }

    [Fact]
    public void AttrPath_IgnoreNamePrefixes_Unset_Maps()
    {
        var src = IgnoreNamePrefixesTemplate.Replace("//__CONFIG__", "");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNamePrefixes 未设");
        // 反态：未赋值时 AuditTrail 正常映射
        AssertForwardContains(result, "MdIgnPreEnt", "MdIgnPreDto", "AuditTrail", "IgnoreNamePrefixes 未设");
    }

    // =================================================================
    // ④ IgnoreNameSuffixes —— 忽略名后缀（正向不映射）
    // =================================================================

    private const string IgnoreNameSuffixesTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdIgnSuf;

        public class MdIgnSufEnt { public string PayloadRaw { get; set; } = ""; public string Name { get; set; } = ""; }
        [MapFrom(typeof(MdIgnSufEnt))]
        public class MdIgnSufDto { public string PayloadRaw { get; set; } = ""; public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreNameSuffixes_Suppressed()
    {
        var src = IgnoreNameSuffixesTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreNameSuffixes = new[] { \"Raw\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNameSuffixes=Raw");
        AssertForwardExcludes(result, "MdIgnSufEnt", "MdIgnSufDto", "PayloadRaw", "IgnoreNameSuffixes=Raw");
    }

    [Fact]
    public void AttrPath_IgnoreNameSuffixes_Unset_Maps()
    {
        // 反态（重审计 B-4a 补齐——原缺未赋值对照，7 属性中唯一单向）：未赋值时 PayloadRaw 正常映射
        var src = IgnoreNameSuffixesTemplate.Replace("//__CONFIG__", "");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNameSuffixes 未设");
        AssertForwardContains(result, "MdIgnSufEnt", "MdIgnSufDto", "PayloadRaw", "IgnoreNameSuffixes 未设");
    }

    // =================================================================
    // ⑤ IgnoreTypesToEntity —— 仅反向忽略类型（正反两向）
    // =================================================================

    private const string IgnoreTypesToEntityTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdIgnType;

        public class MdIgnTypeEnt { public string Name { get; set; } = ""; public int Count { get; set; } }
        [MapFrom(typeof(MdIgnTypeEnt))]
        public class MdIgnTypeDto { public string Name { get; set; } = ""; public string Count { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreTypesToEntity_ForwardStillMaps()
    {
        // 反向忽略：正向必须照常映射（防「误伤正向」）。类型对选 int→string（有可行转换，跨型由生成代码内建承接）
        var src = IgnoreTypesToEntityTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreTypesToEntity = new[] { typeof(int) })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreTypesToEntity=int");
        AssertForwardContains(result, "MdIgnTypeEnt", "MdIgnTypeDto", "Count", "IgnoreTypesToEntity 正向");
        // 反向不填（B-4b）：仅反向忽略——反向方法体不得写回 int 型实体属性
        AssertReverseExcludes(result, "MdIgnTypeDto", "MdIgnTypeEnt", "src.Count", "IgnoreTypesToEntity 反向");
    }

    [Fact]
    public void AttrPath_IgnoreTypesToEntity_Unset_Normal()
    {
        var src = IgnoreTypesToEntityTemplate.Replace("//__CONFIG__", "");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreTypesToEntity 未设");
        AssertForwardContains(result, "MdIgnTypeEnt", "MdIgnTypeDto", "Count", "IgnoreTypesToEntity 未设");
        // 反态对照（判别力）：未赋值时反向照常写回——去掉上方赋值的变异将使 Exclude 断言转红
        AssertReverseContains(result, "MdIgnTypeDto", "MdIgnTypeEnt", "src.Count", "IgnoreTypesToEntity 未设反向");
    }

    // =================================================================
    // ⑥ IgnoreNamePrefixesToEntity —— 仅反向忽略名前缀（正反两向）
    // =================================================================

    private const string IgnoreNamePrefixesToEntityTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdPreToEnt;

        public class MdPreToEntEnt { public string SysStamp { get; set; } = ""; public string Name { get; set; } = ""; }
        [MapFrom(typeof(MdPreToEntEnt))]
        public class MdPreToEntDto { public string SysStamp { get; set; } = ""; public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreNamePrefixesToEntity_ForwardStillMaps()
    {
        var src = IgnoreNamePrefixesToEntityTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreNamePrefixesToEntity = new[] { \"Sys\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNamePrefixesToEntity=Sys");
        AssertForwardContains(result, "MdPreToEntEnt", "MdPreToEntDto", "SysStamp", "IgnoreNamePrefixesToEntity 正向");
        // 反向不填（B-4b）：仅反向忽略——反向方法体不得写回 SysStamp
        AssertReverseExcludes(result, "MdPreToEntDto", "MdPreToEntEnt", "SysStamp", "IgnoreNamePrefixesToEntity 反向");
    }

    [Fact]
    public void AttrPath_IgnoreNamePrefixesToEntity_Unset_Normal()
    {
        var src = IgnoreNamePrefixesToEntityTemplate.Replace("//__CONFIG__", "");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNamePrefixesToEntity 未设");
        AssertForwardContains(result, "MdPreToEntEnt", "MdPreToEntDto", "SysStamp", "IgnoreNamePrefixesToEntity 未设");
        // 反态对照（判别力）：未赋值时反向照常写回
        AssertReverseContains(result, "MdPreToEntDto", "MdPreToEntEnt", "SysStamp", "IgnoreNamePrefixesToEntity 未设反向");
    }

    // =================================================================
    // ⑦ IgnoreNameSuffixesToEntity —— 仅反向忽略名后缀（正反两向）
    // =================================================================

    private const string IgnoreNameSuffixesToEntityTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdSufToEnt;

        public class MdSufToEntEnt { public string NameStamp { get; set; } = ""; public string Name { get; set; } = ""; }
        [MapFrom(typeof(MdSufToEntEnt))]
        public class MdSufToEntDto { public string NameStamp { get; set; } = ""; public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreNameSuffixesToEntity_ForwardStillMaps()
    {
        var src = IgnoreNameSuffixesToEntityTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreNameSuffixesToEntity = new[] { \"Stamp\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNameSuffixesToEntity=Stamp");
        AssertForwardContains(result, "MdSufToEntEnt", "MdSufToEntDto", "NameStamp", "IgnoreNameSuffixesToEntity 正向");
        // 反向不填（B-4b）：仅反向忽略——反向方法体不得写回 NameStamp
        AssertReverseExcludes(result, "MdSufToEntDto", "MdSufToEntEnt", "NameStamp", "IgnoreNameSuffixesToEntity 反向");
    }

    [Fact]
    public void AttrPath_IgnoreNameSuffixesToEntity_Unset_Normal()
    {
        var src = IgnoreNameSuffixesToEntityTemplate.Replace("//__CONFIG__", "");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNameSuffixesToEntity 未设");
        AssertForwardContains(result, "MdSufToEntEnt", "MdSufToEntDto", "NameStamp", "IgnoreNameSuffixesToEntity 未设");
        // 反态对照（判别力）：未赋值时反向照常写回
        AssertReverseContains(result, "MdSufToEntDto", "MdSufToEntEnt", "NameStamp", "IgnoreNameSuffixesToEntity 未设反向");
    }

    // =================================================================
    // ⑧ 语义边界扩展（重审计 A-8 乙案补齐：多值并存 / 前后缀非子串 / 空数组——
    //    语义边界与容量维度，G10 用例数补齐的一部分）
    // =================================================================

    private const string PrefixesMultiTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdPrefMulti;

        public class MdPrefMultiEnt { public string OldName { get; set; } = ""; public string LegacyCode { get; set; } = ""; }
        [MapFrom(typeof(MdPrefMultiEnt))]
        public class MdPrefMultiDto { public string Name { get; set; } = ""; public string Code { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_Prefixes_MultiplePrefixes_BothStripped()
    {
        // 多前缀并存：["Old","Legacy"] 同时生效
        var src = PrefixesMultiTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(Prefixes = new[] { \"Old\", \"Legacy\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "Prefixes 多前缀");
        AssertForwardContains(result, "MdPrefMultiEnt", "MdPrefMultiDto", "OldName", "Prefixes 多前缀");
        AssertForwardContains(result, "MdPrefMultiEnt", "MdPrefMultiDto", "LegacyCode", "Prefixes 多前缀");
    }

    [Fact]
    public void AttrPath_Prefixes_EmptyArray_NoStripping()
    {
        // 空数组边界：行为同未设（不产生剥离匹配）
        var src = PrefixesMultiTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(Prefixes = new string[] { })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "Prefixes 空数组");
        AssertForwardExcludes(result, "MdPrefMultiEnt", "MdPrefMultiDto", "OldName", "Prefixes 空数组");
    }

    private const string PostfixesMultiTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdPostMulti;

        public class MdPostMultiEnt { public string NameLegacy { get; set; } = ""; public string CodeLegacy { get; set; } = ""; }
        [MapFrom(typeof(MdPostMultiEnt))]
        public class MdPostMultiDto { public string Name { get; set; } = ""; public string Code { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_Postfixes_MultipleSuffixes_BothStripped()
    {
        var src = PostfixesMultiTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(Postfixes = new[] { \"Legacy\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "Postfixes 多值");
        AssertForwardContains(result, "MdPostMultiEnt", "MdPostMultiDto", "NameLegacy", "Postfixes 多值");
        AssertForwardContains(result, "MdPostMultiEnt", "MdPostMultiDto", "CodeLegacy", "Postfixes 多值");
    }

    private const string IgnoreNamePrefixesMultiTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdIgnPreMulti;

        public class MdIgnPreMultiEnt { public string AuditTrail { get; set; } = ""; public string TmpData { get; set; } = ""; public string Name { get; set; } = ""; }
        [MapFrom(typeof(MdIgnPreMultiEnt))]
        public class MdIgnPreMultiDto { public string AuditTrail { get; set; } = ""; public string TmpData { get; set; } = ""; public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreNamePrefixes_MultiplePrefixes_BothSuppressed()
    {
        var src = IgnoreNamePrefixesMultiTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreNamePrefixes = new[] { \"Audit\", \"Tmp\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNamePrefixes 多前缀");
        AssertForwardExcludes(result, "MdIgnPreMultiEnt", "MdIgnPreMultiDto", "AuditTrail", "IgnoreNamePrefixes 多前缀");
        AssertForwardExcludes(result, "MdIgnPreMultiEnt", "MdIgnPreMultiDto", "TmpData", "IgnoreNamePrefixes 多前缀");
        AssertForwardContains(result, "MdIgnPreMultiEnt", "MdIgnPreMultiDto", "Name", "IgnoreNamePrefixes 多前缀");
    }

    [Fact]
    public void AttrPath_IgnoreNamePrefixes_PrefixNotSubstring()
    {
        // 前缀语义边界：「Audit」忽略 AuditTrail，不忽略 XAuditTrail（非子串匹配）。
        // 只断 Contains——"XAuditTrail" 文本包含 "AuditTrail" 子串，excludes 会被同名片段污染（头注口径）；
        // 抑制面本身由 AttrPath_IgnoreNamePrefixes_Suppressed 承载，本例承载「不过度抑制」。
        var src = IgnoreNamePrefixesMultiTemplate.Replace(
            "public string TmpData { get; set; } = \"\";",
            "public string XAuditTrail { get; set; } = \"\";")
            .Replace("[assembly: MapDefaults(IgnoreNamePrefixes = new[] { \"Audit\", \"Tmp\" })]",
                     "[assembly: MapDefaults(IgnoreNamePrefixes = new[] { \"Audit\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNamePrefixes 前缀边界");
        AssertForwardContains(result, "MdIgnPreMultiEnt", "MdIgnPreMultiDto", "XAuditTrail", "IgnoreNamePrefixes 前缀边界");
    }

    private const string IgnoreNameSuffixesBoundaryTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdIgnSufBnd;

        public class MdIgnSufBndEnt { public string PayloadRaw { get; set; } = ""; public string PayloadRawX { get; set; } = ""; public string Name { get; set; } = ""; }
        [MapFrom(typeof(MdIgnSufBndEnt))]
        public class MdIgnSufBndDto { public string PayloadRaw { get; set; } = ""; public string PayloadRawX { get; set; } = ""; public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreNameSuffixes_SuffixNotSubstring()
    {
        // 后缀语义边界：「Raw」忽略 PayloadRaw，不忽略 PayloadRawX（非子串匹配）。
        // 只断 Contains——"PayloadRawX" 文本包含 "PayloadRaw" 子串，excludes 会被同名片段污染；
        // 抑制面本身由 AttrPath_IgnoreNameSuffixes_Suppressed 承载，本例承载「不过度抑制」。
        var src = IgnoreNameSuffixesBoundaryTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreNameSuffixes = new[] { \"Raw\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNameSuffixes 后缀边界");
        AssertForwardContains(result, "MdIgnSufBndEnt", "MdIgnSufBndDto", "PayloadRawX", "IgnoreNameSuffixes 后缀边界");
    }

    private const string IgnoreTypesMultiTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdIgnTypeMulti;

        public class MdIgnTypeMultiEnt { public string Name { get; set; } = ""; public int Count { get; set; } public long Qty { get; set; } }
        [MapFrom(typeof(MdIgnTypeMultiEnt))]
        public class MdIgnTypeMultiDto { public string Name { get; set; } = ""; public string Count { get; set; } = ""; public string Qty { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreTypesToEntity_MultipleTypes_BothSuppressed()
    {
        // 多类型并存：[typeof(int), typeof(long)] 反向均忽略，正向照常
        var src = IgnoreTypesMultiTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreTypesToEntity = new[] { typeof(int), typeof(long) })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreTypesToEntity 多类型");
        AssertForwardContains(result, "MdIgnTypeMultiEnt", "MdIgnTypeMultiDto", "Count", "IgnoreTypesToEntity 多类型");
        AssertReverseExcludes(result, "MdIgnTypeMultiDto", "MdIgnTypeMultiEnt", "src.Count", "IgnoreTypesToEntity 多类型反向");
        AssertReverseExcludes(result, "MdIgnTypeMultiDto", "MdIgnTypeMultiEnt", "src.Qty", "IgnoreTypesToEntity 多类型反向");
    }

    private const string IgnoreNamePrefixesToEntityMultiTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdPreToEntMulti;

        public class MdPreToEntMultiEnt { public string SysStamp { get; set; } = ""; public string TmpMark { get; set; } = ""; public string Name { get; set; } = ""; }
        [MapFrom(typeof(MdPreToEntMultiEnt))]
        public class MdPreToEntMultiDto { public string SysStamp { get; set; } = ""; public string TmpMark { get; set; } = ""; public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreNamePrefixesToEntity_MultiplePrefixes_BothSuppressedReverse()
    {
        var src = IgnoreNamePrefixesToEntityMultiTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreNamePrefixesToEntity = new[] { \"Sys\", \"Tmp\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNamePrefixesToEntity 多前缀");
        AssertForwardContains(result, "MdPreToEntMultiEnt", "MdPreToEntMultiDto", "SysStamp", "IgnoreNamePrefixesToEntity 多前缀正向");
        AssertReverseExcludes(result, "MdPreToEntMultiDto", "MdPreToEntMultiEnt", "SysStamp", "IgnoreNamePrefixesToEntity 多前缀反向");
        AssertReverseExcludes(result, "MdPreToEntMultiDto", "MdPreToEntMultiEnt", "TmpMark", "IgnoreNamePrefixesToEntity 多前缀反向");
    }

    private const string IgnoreNameSuffixesToEntityMultiTemplate = """
        using NCMapper;
        using NCMapper.Attributes;
        //__CONFIG__
        namespace MdSufToEntMulti;

        public class MdSufToEntMultiEnt { public string NameStamp { get; set; } = ""; public string NameTag { get; set; } = ""; public string Name { get; set; } = ""; }
        [MapFrom(typeof(MdSufToEntMultiEnt))]
        public class MdSufToEntMultiDto { public string NameStamp { get; set; } = ""; public string NameTag { get; set; } = ""; public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void AttrPath_IgnoreNameSuffixesToEntity_MultipleSuffixes_BothSuppressedReverse()
    {
        var src = IgnoreNameSuffixesToEntityMultiTemplate.Replace("//__CONFIG__", "[assembly: MapDefaults(IgnoreNameSuffixesToEntity = new[] { \"Stamp\", \"Tag\" })]");
        var result = TestHelper.GenerateMapper(src);

        AssertNoErrors(result, "IgnoreNameSuffixesToEntity 多后缀");
        AssertForwardContains(result, "MdSufToEntMultiEnt", "MdSufToEntMultiDto", "NameStamp", "IgnoreNameSuffixesToEntity 多后缀正向");
        AssertReverseExcludes(result, "MdSufToEntMultiDto", "MdSufToEntMultiEnt", "NameStamp", "IgnoreNameSuffixesToEntity 多后缀反向");
        AssertReverseExcludes(result, "MdSufToEntMultiDto", "MdSufToEntMultiEnt", "NameTag", "IgnoreNameSuffixesToEntity 多后缀反向");
    }
}