using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 生成列红测（ConvertBack 物化注入契约——前必红，后转绿）。
/// <para>断言模板经特性侧 [ConvertBack] 既有产物校准（ConvertBackUsingGenerationTests
/// 既有绿测断言值——同一发射端零改动复用的实测口径：反向方法名 Map_{Dto}_To_{Entity}（类简单名）、
/// T1 内联调用 `Tag = global::{FQN}.Convert(src.Tag)`、ReverseOnly 整源 `X = global::Conv.M(src)`）。</para>
/// <para>双层断言：①快照层（ExtractCore 直调——PropertyMappings 三元组值 + ReverseOnly 条目）；
/// ②文本层（GenerateMapper——反向方法体转换器调用）。</para>
/// </summary>
public class FluentConvertBackTests
{
    /// <summary>
    /// 直调 FluentExtractor.ExtractCore 取完整物化快照（等价机制同款入口——绕过 ForAttributeWithMetadataName 管线）
    /// </summary>
    private static MappingSnapshot? ExtractSnapshot(string source)
    {
        var result = ExtractResult(source);
        return result?.Declarations.FirstOrDefault()?.Snapshot;
    }

    /// <summary>直调 FluentExtractor.ExtractCore 取提取结果（增量缓存相等性断言用）</summary>
    private static FluentExtractionResult? ExtractResult(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapperConfig")))!;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        return FluentExtractor.ExtractCore(compilation, typeSymbol!, typeDecl, CancellationToken.None);
    }

    /// <summary>定位快照中目标属性名对应的正向 pm</summary>
    private static PropertyMappingSnapshot PmOf(MappingSnapshot snapshot, string targetPropName)
        => snapshot.PropertyMappings.First(pm => pm.Target.Name == targetPropName);

    // ── T0~T3 四形态 × 匹配对（自动匹配命中）──────────────────────────

    /// <summary>
    /// T1 单值（最常见形态）：自动匹配命中（string→string 同名）→ pm.Target 携带反向三元组 +
    /// 反向方法体消费转换器（`Tag = global::FcbT1Conv.Convert(src.Tag)`——特性侧 CbuR1 同构实测口径）。
    /// </summary>
    [Fact]
    public void T1_MatchedPair_AutoMatch_AttachesTriple_And_GeneratesReverseConverterCall()
    {
        var source = """
            using NCMapper;
            public class FcbT1Entity { public string Tag { get; set; } = ""; }
            public class FcbT1Dto { public string Tag { get; set; } = ""; }
            public static class FcbT1Conv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbT1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbT1Entity, FcbT1Dto>().Member(d => d.Tag).ConvertBack(FcbT1Conv.Convert);
                }
            }
            """;

        // ① 快照层：三元组注入契约（前红——BackDeclarations 未被物化消费）
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Tag");
        Assert.True(pm.Target.HasConvertBackUsing, "锚点 pm.Target.HasConvertBackUsing 应为 true（Fluent 声明注入）");
        Assert.Equal("FcbT1Conv", pm.Target.ConverterBackDeclaringType);
        Assert.Equal("Convert", pm.Target.ConverterBackMethodName);
        Assert.Equal(ConverterShape.T1SingleValue, pm.Target.ConverterBackShape);

        // ② 文本层：反向方法体消费转换器（特性侧 CbuR1 实测口径——src=DTO 对象、T1 单实参）
        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbT1Dto_To_FcbT1Entity",
            "Tag = global::FcbT1Conv.Convert(src.Tag)");
        // 正向不消费（同类型直赋，无转换器调用）
        var fwdBody = result.Methods["Map_FcbT1Entity_To_FcbT1Dto"][0].Body;
        Assert.DoesNotContain(".Convert(", fwdBody);
    }

    /// <summary>
    /// T0 无参常量：`Func&lt;TEntityProp&gt;` 重载——反向写入常量（形态编码 T0Constant）。
    /// </summary>
    [Fact]
    public void T0_MatchedPair_AutoMatch_EncodesConstantShape()
    {
        var source = """
            using NCMapper;
            public class FcbT0Entity { public string Tag { get; set; } = ""; }
            public class FcbT0Dto { public string Tag { get; set; } = ""; }
            public static class FcbT0Conv { public static string Stamp() => "fixed"; }

            [MapperConfig]
            public class FcbT0Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbT0Entity, FcbT0Dto>().Member(d => d.Tag).ConvertBack<string>(FcbT0Conv.Stamp);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Tag");
        Assert.True(pm.Target.HasConvertBackUsing);
        Assert.Equal("FcbT0Conv", pm.Target.ConverterBackDeclaringType);
        Assert.Equal("Stamp", pm.Target.ConverterBackMethodName);
        Assert.Equal(ConverterShape.T0Constant, pm.Target.ConverterBackShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbT0Dto_To_FcbT0Entity",
            "Tag = global::FcbT0Conv.Stamp()");
    }

    /// <summary>
    /// T2 整源：`Func&lt;TTarget, TEntityProp&gt;` 重载——反向整 DTO 对象入参（`FcbT2Conv.Build(src)`）。
    /// </summary>
    [Fact]
    public void T2_MatchedPair_AutoMatch_PassesWholeDto()
    {
        var source = """
            using NCMapper;
            public class FcbT2Entity { public string Tag { get; set; } = ""; }
            public class FcbT2Dto { public string Tag { get; set; } = ""; }
            public static class FcbT2Conv { public static string Build(FcbT2Dto dto) => "T2:" + dto.Tag; }

            [MapperConfig]
            public class FcbT2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbT2Entity, FcbT2Dto>().Member(d => d.Tag).ConvertBack(FcbT2Conv.Build);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Tag");
        Assert.True(pm.Target.HasConvertBackUsing);
        Assert.Equal("FcbT2Conv", pm.Target.ConverterBackDeclaringType);
        Assert.Equal("Build", pm.Target.ConverterBackMethodName);
        Assert.Equal(ConverterShape.T2MultiField, pm.Target.ConverterBackShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbT2Dto_To_FcbT2Entity",
            "Tag = global::FcbT2Conv.Build(src)");
    }

    /// <summary>
    /// T3 组合+字段值：`Func&lt;TTarget, TDtoProp, TEntityProp&gt;` 重载——整 DTO + 字段值双实参
    /// （`FcbT3Conv.Build(src, src.Tag)`——BuildStaticConverterCall T3 模板实测口径）。
    /// </summary>
    [Fact]
    public void T3_MatchedPair_AutoMatch_PassesDtoAndField()
    {
        var source = """
            using NCMapper;
            public class FcbT3Entity { public string Tag { get; set; } = ""; }
            public class FcbT3Dto { public string Tag { get; set; } = ""; }
            public static class FcbT3Conv { public static string Build(FcbT3Dto dto, string tag) => "T3:" + tag; }

            [MapperConfig]
            public class FcbT3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbT3Entity, FcbT3Dto>().Member(d => d.Tag).ConvertBack(FcbT3Conv.Build);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Tag");
        Assert.True(pm.Target.HasConvertBackUsing);
        Assert.Equal("FcbT3Conv", pm.Target.ConverterBackDeclaringType);
        Assert.Equal("Build", pm.Target.ConverterBackMethodName);
        Assert.Equal(ConverterShape.T3Combined, pm.Target.ConverterBackShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbT3Dto_To_FcbT3Entity",
            "Tag = global::FcbT3Conv.Build(src, src.Tag)");
    }

    // ── ReverseOnly（NoAutoInclude + 无正向声明）────────────────────────

    /// <summary>
    /// ReverseOnly 条目（NoAutoInclude 场景）：Tag 无正向声明 → info.ReverseOnlyConvertBacks 条目
    /// （TargetProperty=实体属性、SourceProperty=null、pm 级三元组）+ 反向方法整源调用
    /// （特性侧 CbuNs 同构实测口径：`Tag = global::FcbROConv.Stamp(src)`——src=DTO 对象整传）。
    /// </summary>
    [Fact]
    public void ReverseOnly_NoAutoInclude_CreatesEntry_And_GeneratesDtoWideCall()
    {
        var source = """
            using NCMapper;
            public class FcbROEntity { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public class FcbRODto { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public static class FcbROConv { public static string Stamp(FcbRODto dto) => "ro:" + dto.Id; }

            [MapperConfig]
            public class FcbROMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbROEntity, FcbRODto>()
                       .NoAutoMatch()
                       .Member(d => d.Id).From(s => s.Id)
                       .Member(d => d.Tag).ConvertBack<string>(FcbROConv.Stamp);
                }
            }
            """;

        // ① 快照层：ReverseOnly 条目契约（前红——条目缺失）
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var ro = snapshot!.ReverseOnlyConvertBacks.SingleOrDefault(r => r.Target.Name == "Tag");
        Assert.NotNull(ro);
        Assert.Null(ro.Source); // 反向无源
        Assert.Equal("FcbROConv", ro.ConverterDeclaringType);
        Assert.Equal("Stamp", ro.ConverterMethodName);
        Assert.Equal(ConverterShape.T2MultiField, ro.ConverterShape);

        // ② 文本层：反向方法体整源调用（特性侧 CbuNs 实测口径）
        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbRODto_To_FcbROEntity",
            "Tag = global::FcbROConv.Stamp(src)");
    }

    // ── 独立可选（跨类型）────────────────────────────────────────

    /// <summary>
    /// 独立可选（跨类型）：ConvertBack 单独声明（无任何正向声明、auto-match 命中 DateTime→string）——
    /// 正向内建转换 + 反向自定义解析（「独立可选」维度；A-09 同构语义的 Fluent 原生断言）。
    /// </summary>
    [Fact]
    public void Standalone_CrossType_ForwardBuiltIn_ReverseCustom()
    {
        var source = """
            using System;
            using NCMapper;
            public class FcbStEntity { public DateTime Created { get; set; } }
            public class FcbStDto { public string Created { get; set; } = ""; }
            public static class FcbStConv { public static DateTime Parse(string fieldValue) => DateTime.Parse(fieldValue, System.Globalization.CultureInfo.InvariantCulture); }

            [MapperConfig]
            public class FcbStMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbStEntity, FcbStDto>().Member(d => d.Created).ConvertBack(FcbStConv.Parse);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Created");
        Assert.True(pm.Target.HasConvertBackUsing);
        Assert.Equal(ConverterShape.T1SingleValue, pm.Target.ConverterBackShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbStDto_To_FcbStEntity",
            "Created = global::FcbStConv.Parse(src.Created)");
        // 正向不消费（内建类型转换承接，无转换器调用）
        var fwdBody = result.Methods["Map_FcbStEntity_To_FcbStDto"][0].Body;
        Assert.DoesNotContain("FcbStConv.Parse", fwdBody);
    }

    // ── 写回链（auto-flatten 正向，X2 链反向）× 4 形态 ─────────────

    /// <summary>
    /// 写回链 T1：正向 auto-flatten（DTO.City ← entity.Address.City），反向经 X2 写回链消费转换器
    /// （特性侧 CbuW 同构实测口径：`dst.Address.City = global::Conv.Convert(src.City)` + 源 null 守卫）。
    /// </summary>
    [Fact]
    public void WriteBackChain_T1_ReverseConsumesConverter_WithNullGuard()
    {
        var source = """
            using NCMapper;
            public class FcbW1Address { public string City { get; set; } = ""; }
            public class FcbW1Entity { public FcbW1Address Address { get; set; } = new(); }
            public class FcbW1Dto { public string AddressCity { get; set; } = ""; }
            public static class FcbW1Conv { public static string Convert(string fieldValue) => "city:" + fieldValue; }

            [MapperConfig]
            public class FcbW1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbW1Entity, FcbW1Dto>().Member(d => d.AddressCity).ConvertBack(FcbW1Conv.Convert);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        var reverseBody = result.Methods["Map_FcbW1Dto_To_FcbW1Entity"][0].Body;
        Assert.Contains("dst.Address.City = global::FcbW1Conv.Convert(src.AddressCity)", reverseBody);
        // N-1 源 null 守卫（特性侧 CbuW 实测口径）
        Assert.Contains("if (src.AddressCity != null)", reverseBody);
    }

    /// <summary>写回链 T0：常量形态——反向写回链消费无参转换器。</summary>
    [Fact]
    public void WriteBackChain_T0_ReverseConsumesConstantConverter()
    {
        var source = """
            using NCMapper;
            public class FcbW0Address { public string City { get; set; } = ""; }
            public class FcbW0Entity { public FcbW0Address Address { get; set; } = new(); }
            public class FcbW0Dto { public string AddressCity { get; set; } = ""; }
            public static class FcbW0Conv { public static string Stamp() => "fixed"; }

            [MapperConfig]
            public class FcbW0Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbW0Entity, FcbW0Dto>().Member(d => d.AddressCity).ConvertBack<string>(FcbW0Conv.Stamp);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        var reverseBody = result.Methods["Map_FcbW0Dto_To_FcbW0Entity"][0].Body;
        Assert.Contains("dst.Address.City = global::FcbW0Conv.Stamp()", reverseBody);
    }

    /// <summary>写回链 T2：整源形态——反向写回链消费整 DTO 入参转换器。</summary>
    [Fact]
    public void WriteBackChain_T2_ReverseConsumesWholeDtoConverter()
    {
        var source = """
            using NCMapper;
            public class FcbW2Address { public string City { get; set; } = ""; }
            public class FcbW2Entity { public FcbW2Address Address { get; set; } = new(); }
            public class FcbW2Dto { public string AddressCity { get; set; } = ""; public string Name { get; set; } = ""; }
            public static class FcbW2Conv { public static string Build(FcbW2Dto dto) => "T2:" + dto.Name; }

            [MapperConfig]
            public class FcbW2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbW2Entity, FcbW2Dto>().Member(d => d.AddressCity).ConvertBack(FcbW2Conv.Build);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        var reverseBody = result.Methods["Map_FcbW2Dto_To_FcbW2Entity"][0].Body;
        Assert.Contains("dst.Address.City = global::FcbW2Conv.Build(src)", reverseBody);
    }

    /// <summary>写回链 T3：组合形态——反向写回链消费整 DTO + 字段值双实参转换器。</summary>
    [Fact]
    public void WriteBackChain_T3_ReverseConsumesCombinedConverter()
    {
        var source = """
            using NCMapper;
            public class FcbW3Address { public string City { get; set; } = ""; }
            public class FcbW3Entity { public FcbW3Address Address { get; set; } = new(); }
            public class FcbW3Dto { public string AddressCity { get; set; } = ""; }
            public static class FcbW3Conv { public static string Build(FcbW3Dto dto, string city) => "T3:" + city; }

            [MapperConfig]
            public class FcbW3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbW3Entity, FcbW3Dto>().Member(d => d.AddressCity).ConvertBack(FcbW3Conv.Build);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        var reverseBody = result.Methods["Map_FcbW3Dto_To_FcbW3Entity"][0].Body;
        Assert.Contains("dst.Address.City = global::FcbW3Conv.Build(src, src.AddressCity)", reverseBody);
    }

    // ── ReverseOnly × 其余形态（T0/T1/T3；NoAutoInclude 场景，T2 已于红测批次）──────

    /// <summary>ReverseOnly T0（NoAutoInclude）：无正向声明 → 条目 + 常量调用。</summary>
    [Fact]
    public void ReverseOnly_NoAutoInclude_T0Constant()
    {
        var source = """
            using NCMapper;
            public class FcbR0Entity { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public class FcbR0Dto { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public static class FcbR0Conv { public static string Stamp() => "fixed"; }

            [MapperConfig]
            public class FcbR0Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbR0Entity, FcbR0Dto>()
                       .NoAutoMatch()
                       .Member(d => d.Id).From(s => s.Id)
                       .Member(d => d.Tag).ConvertBack<string>(FcbR0Conv.Stamp);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var ro = snapshot!.ReverseOnlyConvertBacks.SingleOrDefault(r => r.Target.Name == "Tag");
        Assert.NotNull(ro);
        Assert.Equal(ConverterShape.T0Constant, ro.ConverterShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbR0Dto_To_FcbR0Entity",
            "Tag = global::FcbR0Conv.Stamp()");
    }

    /// <summary>
    /// ReverseOnly T1（NoAutoInclude）：无正向声明 → 条目 + 字段值调用。
    /// <para>产物同构口径（差异登记 D-3）：ReverseOnly 条目 SourceProperty=null——T1 字段实参为
    /// default 字面量（与特性侧无源条目同构：`M(default)`，锚点 DTO 属性不充当字段实参——
    /// T1/T3 形态建议配正向声明使用，ReverseOnly 场景首选 T0/T2）。</para>
    /// </summary>
    [Fact]
    public void ReverseOnly_NoAutoInclude_T1SingleValue()
    {
        var source = """
            using NCMapper;
            public class FcbR1Entity { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public class FcbR1Dto { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public static class FcbR1Conv { public static string Convert(string fieldValue) => "v:" + fieldValue; }

            [MapperConfig]
            public class FcbR1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbR1Entity, FcbR1Dto>()
                       .NoAutoMatch()
                       .Member(d => d.Id).From(s => s.Id)
                       .Member(d => d.Tag).ConvertBack<string>(FcbR1Conv.Convert);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var ro = snapshot!.ReverseOnlyConvertBacks.SingleOrDefault(r => r.Target.Name == "Tag");
        Assert.NotNull(ro);
        Assert.Equal(ConverterShape.T1SingleValue, ro.ConverterShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbR1Dto_To_FcbR1Entity",
            "Tag = global::FcbR1Conv.Convert(default)");
    }

    /// <summary>
    /// ReverseOnly T3（NoAutoInclude）：无正向声明 → 条目 + 组合调用（整源实参 + default 字段实参——产物同构口径，D-3）。
    /// </summary>
    [Fact]
    public void ReverseOnly_NoAutoInclude_T3Combined()
    {
        var source = """
            using NCMapper;
            public class FcbR3Entity { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public class FcbR3Dto { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public static class FcbR3Conv { public static string Build(FcbR3Dto dto, string tag) => "T3:" + tag; }

            [MapperConfig]
            public class FcbR3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbR3Entity, FcbR3Dto>()
                       .NoAutoMatch()
                       .Member(d => d.Id).From(s => s.Id)
                       .Member(d => d.Tag).ConvertBack(FcbR3Conv.Build);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var ro = snapshot!.ReverseOnlyConvertBacks.SingleOrDefault(r => r.Target.Name == "Tag");
        Assert.NotNull(ro);
        Assert.Equal(ConverterShape.T3Combined, ro.ConverterShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbR3Dto_To_FcbR3Entity",
            "Tag = global::FcbR3Conv.Build(src, default)");
    }

    // ── ReverseOnly 场景 4（自动匹配未命中 + 实体同名 write-only——构造前提 P2-1）──────

    /// <summary>
    /// ReverseOnly 场景 4 T1：实体同名属性 write-only（getter 不可读 + public setter）→ 自动匹配未命中
    /// （正向 NCMAP001 已发——正交不冗余）→ ReverseOnly 条目 + 反向方法生成调用。
    /// </summary>
    [Fact]
    public void ReverseOnly_WriteOnlyEntity_T1_CreatesEntry()
    {
        var source = """
            using NCMapper;
            public class FcbWoEntity { public int Id { get; set; } public string Tag { set; } }
            public class FcbWoDto { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public static class FcbWoConv { public static string Convert(string fieldValue) => "wo:" + fieldValue; }

            [MapperConfig]
            public class FcbWoMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbWoEntity, FcbWoDto>().Member(d => d.Tag).ConvertBack<string>(FcbWoConv.Convert);
                }
            }
            """;

        // 快照层：ReverseOnly 条目（实体 write-only 属性为 Target）
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var ro = snapshot!.ReverseOnlyConvertBacks.SingleOrDefault(r => r.Target.Name == "Tag");
        Assert.NotNull(ro);
        Assert.Equal("FcbWoConv", ro.ConverterDeclaringType);
        Assert.Equal(ConverterShape.T1SingleValue, ro.ConverterShape);
        // 正向 NCMAP001 同时在场（语义「正向无源」——与 F18「反向目标缺失」正交，双报保留）
        Assert.Contains(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP001");

        // 文本层：反向方法生成且消费转换器（写 write-only 属性合法；产物同构口径——无源条目
        // T1 字段实参 default 字面量，D-3）
        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbWoDto_To_FcbWoEntity",
            "Tag = global::FcbWoConv.Convert(default)");
    }

    /// <summary>ReverseOnly 场景 4 T2：write-only 实体属性 + 整源转换器（T2 整传实参——ReverseOnly 推荐形态）。</summary>
    [Fact]
    public void ReverseOnly_WriteOnlyEntity_T2_CreatesEntry()
    {
        var source = """
            using NCMapper;
            public class FcbWxEntity { public int Id { get; set; } public string Tag { set; } }
            public class FcbWxDto { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public static class FcbWxConv { public static string Stamp(FcbWxDto dto) => "x:" + dto.Id; }

            [MapperConfig]
            public class FcbWxMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbWxEntity, FcbWxDto>().Member(d => d.Tag).ConvertBack<string>(FcbWxConv.Stamp);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var ro = snapshot!.ReverseOnlyConvertBacks.SingleOrDefault(r => r.Target.Name == "Tag");
        Assert.NotNull(ro);
        Assert.Equal(ConverterShape.T2MultiField, ro.ConverterShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbWxDto_To_FcbWxEntity",
            "Tag = global::FcbWxConv.Stamp(src)");
    }

    // ── Nullable 守卫变体（执行期实证：方法组 API 结构性不可达——差异登记 D-1 回流）──────

    /// <summary>
    /// 守卫变体不可达实证（D-1）：锚点 Nullable&lt;T&gt;（int?）+ converter 入参 T（int）——
    /// Func&lt;int?,int&gt; 对 M(int) 无值类型逆变（编译期 CS0123，绑定失败）→ F20（Error 级非静默，
    /// 与编译器诊断并列）。T1Guarded/T3Guarded 仅特性通道可表达（nameof 免委托绑定）。
    /// <para>「Nullable 守卫变体自动识别」对 Fluent 通道证伪——执行阶段触发方案修订，
    /// 登记于差异登记 D-1（方案书附录 B 修订位）。</para>
    /// </summary>
    [Fact]
    public void Guarded_Unreachable_IntAnchorWithNonNullableParam_FallsToF20()
    {
        var source = """
            using NCMapper;
            public class FcbG1Entity { public int Count { get; set; } }
            public class FcbG1Dto { public int? Count { get; set; } }
            public static class FcbG1Conv { public static int Convert(int fieldValue) => fieldValue + 1; }

            [MapperConfig]
            public class FcbG1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbG1Entity, FcbG1Dto>().Member(d => d.Count).ConvertBack(FcbG1Conv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 实证：绑定失败路径落入 F20（非静默），声明丢弃
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F20");
        Assert.DoesNotContain("FcbG1Conv.Convert", result.GeneratedCode);
    }

    /// <summary>
    /// Nullable 直传形态（守卫的 Fluent 等价表达）：锚点 int? + converter 入参 int?（同 Nullable）——
    /// 平直 T1 直传（无守卫三元；null 由转换器自行处理——与特性侧「参数同为 Nullable 时直传」同口径）。
    /// </summary>
    [Fact]
    public void Guarded_NullableParam_PlainT1DirectPass()
    {
        var source = """
            using NCMapper;
            public class FcbG2Entity { public int Count { get; set; } }
            public class FcbG2Dto { public int? Count { get; set; } }
            public static class FcbG2Conv { public static int Convert(int? fieldValue) => (fieldValue ?? -1) + 1; }

            [MapperConfig]
            public class FcbG2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbG2Entity, FcbG2Dto>().Member(d => d.Count).ConvertBack(FcbG2Conv.Convert);
                }
            }
            """;
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Count");
        Assert.Equal(ConverterShape.T1SingleValue, pm.Target.ConverterBackShape); // 平直 T1（非守卫）

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbG2Dto_To_FcbG2Entity",
            "Count = global::FcbG2Conv.Convert(src.Count)");
    }

    // ── 双向配对（Convert+ConvertBack / Map+ConvertBack 同名锚点）──────

    /// <summary>
    /// 双向配对 ①：Map（正向显式跨名）+ ConvertBack 同名锚点——正向 src 路径与反向转换器共存
    /// （正向 Name→Tag 内建转换、反向 Tag→Name 自定义转换）。
    /// </summary>
    [Fact]
    public void DualPair_Convert_Plus_ConvertBack_Coexist()
    {
        var source = """
            using NCMapper;
            public class FcbDP1Entity { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
            public class FcbDP1Dto { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
            public static class FcbDP1Conv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbDP1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbDP1Entity, FcbDP1Dto>()
                       .Member(d => d.Tag).From(s => s.Name)
                       .Member(d => d.Tag).ConvertBack(FcbDP1Conv.Convert);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        // 正向：显式 Map 路径（Tag = src.Name——内建转换，无转换器调用）
        var fwdBody = result.Methods["Map_FcbDP1Entity_To_FcbDP1Dto"][0].Body;
        Assert.Contains("Tag = src.Name", fwdBody);
        Assert.DoesNotContain("FcbDP1Conv.Convert", fwdBody);
        // 反向：转换器消费（Name = Convert(src.Tag)——反向跨名映射经转换器）
        result.Should().HaveMethodBodyContaining("Map_FcbDP1Dto_To_FcbDP1Entity",
            "Name = global::FcbDP1Conv.Convert(src.Tag)");
    }

    /// <summary>双向配对 ②：Map（正向显式跨名）+ ConvertBack 同名锚点。</summary>
    [Fact]
    public void DualPair_Map_Plus_ConvertBack_Coexist()
    {
        var source = """
            using NCMapper;
            public class FcbDP2Entity { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
            public class FcbDP2Dto { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
            public static class FcbDP2Conv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbDP2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbDP2Entity, FcbDP2Dto>()
                       .Member(d => d.Tag).From(s => s.Name)
                       .Member(d => d.Tag).ConvertBack(FcbDP2Conv.Convert);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        var fwdBody = result.Methods["Map_FcbDP2Entity_To_FcbDP2Dto"][0].Body;
        Assert.Contains("Tag = src.Name", fwdBody);
        result.Should().HaveMethodBodyContaining("Map_FcbDP2Dto_To_FcbDP2Entity",
            "Name = global::FcbDP2Conv.Convert(src.Tag)");
    }

    // ── 交互边界（Mask / MapTree / 重复声明）──────────────────────

    /// <summary>
    /// 交互 ①：Mask + ConvertBack 并存——允许；反向消费转换器（脱敏值经转换器写回）。
    /// <para>注：NCMAP034 读 PropertyRef.HasMask（特性派生 attr-snapshot 位——Fluent .Mask() 声明
    /// 不置位，既有通道不对称与 ConvertBack 无关，STRIPPED 口径同源）；此处断言行为层（转换器消费）。</para>
    /// </summary>
    [Fact]
    public void Interaction_Mask_Plus_ConvertBack_Allowed()
    {
        var source = """
            using NCMapper;
            public class FcbIMEntity { public string Phone { get; set; } = ""; }
            public class FcbIMDto { public string Phone { get; set; } = ""; }
            public class FcbIMMask : NCMapper.IMask { public string? Mask(string? value) => "***"; }
            public static class FcbIMConv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbIMMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbIMEntity, FcbIMDto>()
                       .Member(d => d.Phone).Mask<FcbIMMask>()
                       .Member(d => d.Phone).ConvertBack(FcbIMConv.Convert);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        // 反向消费转换器（Mask 与 ConvertBack 无互斥）
        result.Should().HaveMethodBodyContaining("Map_FcbIMDto_To_FcbIMEntity",
            "Phone = global::FcbIMConv.Convert(src.Phone)");
        // 零 F 系诊断（并存合法）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>交互 ②：MapTree + ConvertBack 并存——允许；反向消费转换器（物化层 MapTree 等价 Direct 同名映射）。</summary>
    [Fact]
    public void Interaction_MapTree_Plus_ConvertBack_Allowed()
    {
        var source = """
            using NCMapper;
            public class FcbITEntity { public string Tag { get; set; } = ""; }
            public class FcbITDto { public string Tag { get; set; } = ""; }
            public static class FcbITConv { public static string Build(FcbITDto dto) => "mt:" + dto.Tag; }

            [MapperConfig]
            public class FcbITMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbITEntity, FcbITDto>()
                       .Member(d => d.Tag).MapTree()
                       .Member(d => d.Tag).ConvertBack(FcbITConv.Build);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbITDto_To_FcbITEntity",
            "Tag = global::FcbITConv.Build(src)");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    /// <summary>
    /// 交互 ③：同属性重复 ConvertBack——后写覆盖（builder 惯例，与 Declarations 同款）。
    /// <para>已知与特性侧 NCMAP009 判重不对称（Fluent builder 后写覆盖惯例）——登记项非疏漏，
    /// 勿顺手修齐（编制会话建议④锚定；Q6 遗留归统筹治理）。</para>
    /// </summary>
    [Fact]
    public void Interaction_Duplicate_ConvertBack_LastWriteWins()
    {
        var source = """
            using NCMapper;
            public class FcbDpEntity { public string Tag { get; set; } = ""; }
            public class FcbDpDto { public string Tag { get; set; } = ""; }
            public static class FcbDpFirst { public static string Convert(string fieldValue) => "first:" + fieldValue; }
            public static class FcbDpSecond { public static string Convert(string fieldValue) => "second:" + fieldValue; }

            [MapperConfig]
            public class FcbDpMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbDpEntity, FcbDpDto>()
                       .Member(d => d.Tag).ConvertBack(FcbDpFirst.Convert)
                       .Member(d => d.Tag).ConvertBack(FcbDpSecond.Convert);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Tag");
        Assert.Equal("FcbDpSecond", pm.Target.ConverterBackDeclaringType);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcbDpDto_To_FcbDpEntity",
            "Tag = global::FcbDpSecond.Convert(src.Tag)");
        Assert.DoesNotContain("FcbDpFirst", result.GeneratedCode);
        // 零 F 系诊断（后写覆盖为既定惯例，非错误）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
    }

    // ── 增量缓存（三层相等性——改 converter 方法组 → 提取产物不等）──────

    /// <summary>
    /// 增量缓存 ③（端到端）：改 converter 方法组（同形态不同方法）→ FluentExtractionResult 不等 →
    /// 下游再生成触发（增量管线以提取结果相等性判缓存命中——两层值差经快照值层 PropertyRef.ConverterBack*）。
    /// </summary>
    [Fact]
    public void Incremental_ConverterMethodChange_BreaksExtractionEquality()
    {
        const string sourceA = """
            using NCMapper;
            public class FcbIncEntity { public string Tag { get; set; } = ""; }
            public class FcbIncDto { public string Tag { get; set; } = ""; }
            public static class FcbIncConv1 { public static string Convert(string fieldValue) => "a:" + fieldValue; }
            public static class FcbIncConv2 { public static string Convert(string fieldValue) => "b:" + fieldValue; }

            [MapperConfig]
            public class FcbIncMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbIncEntity, FcbIncDto>().Member(d => d.Tag).ConvertBack(FcbIncConv1.Convert);
                }
            }
            """;
        const string sourceB = """
            using NCMapper;
            public class FcbIncEntity { public string Tag { get; set; } = ""; }
            public class FcbIncDto { public string Tag { get; set; } = ""; }
            public static class FcbIncConv1 { public static string Convert(string fieldValue) => "a:" + fieldValue; }
            public static class FcbIncConv2 { public static string Convert(string fieldValue) => "b:" + fieldValue; }

            [MapperConfig]
            public class FcbIncMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbIncEntity, FcbIncDto>().Member(d => d.Tag).ConvertBack(FcbIncConv2.Convert);
                }
            }
            """;

        var ra = ExtractResult(sourceA);
        var rb = ExtractResult(sourceB);
        Assert.NotNull(ra);
        Assert.NotNull(rb);

        // 提取结果不等（增量缓存失效入口——改 converter 方法组必触发）
        Assert.False(ra!.Equals(rb), "改 converter 方法组后 FluentExtractionResult 应不等（增量缓存失效）");

        // 快照值层：PropertyRef.ConverterBack* 差异（既有 Equals :494-496 承载）
        var pmA = PmOf(ra.Declarations[0].Snapshot, "Tag");
        var pmB = PmOf(rb!.Declarations[0].Snapshot, "Tag");
        Assert.NotEqual(pmA.Target.ConverterBackDeclaringType, pmB.Target.ConverterBackDeclaringType);
        Assert.NotEqual(ra.Declarations[0].Snapshot, rb.Declarations[0].Snapshot);

        // 生成的反向方法体随 converter 变化（下游再生成产物确实不同）
        var genA = TestHelper.GenerateMapper(sourceA);
        var genB = TestHelper.GenerateMapper(sourceB);
        Assert.Contains("FcbIncConv1.Convert", genA.GeneratedCode);
        Assert.Contains("FcbIncConv2.Convert", genB.GeneratedCode);
    }

    /// <summary>
    /// 增量缓存 ①（符号态镜像）：PropertyMapping.Equals 对 BackConverter* 三字段敏感
    /// （同符号、不同反向三元组 → 不等——Q5 层①；变异验证锚点：去掉 Equals 镜像行本用例必红）。
    /// </summary>
    [Fact]
    public void Incremental_PropertyMappingEquals_SensitiveToBackConverterFields()
    {
        var (comp, conv1, conv2, entityProp, dtoProp) = LoadIncrementSymbols();
        var basePm = new PropertyMapping
        {
            TargetProperty = dtoProp,
            SourceProperty = entityProp,
            SourcePath = new List<Microsoft.CodeAnalysis.IPropertySymbol> { entityProp },
        };
        var withBack = new PropertyMapping
        {
            TargetProperty = dtoProp,
            SourceProperty = entityProp,
            SourcePath = new List<Microsoft.CodeAnalysis.IPropertySymbol> { entityProp },
            BackConverterDeclaringType = "FcbIncConv2",
            BackConverterMethodName = "Convert",
            BackConverterShape = ConverterShape.T1SingleValue,
        };

        // 同符号 + 反向三元组差异 → 不等（镜像生效）
        Assert.False(basePm.Equals(withBack), "PropertyMapping.Equals 应对 BackConverter* 三字段敏感（Q5 层①镜像）");

        // 同符号 + 同三元组 → 相等（镜像不引入误判）
        var withBack2 = new PropertyMapping
        {
            TargetProperty = dtoProp,
            SourceProperty = entityProp,
            SourcePath = new List<Microsoft.CodeAnalysis.IPropertySymbol> { entityProp },
            BackConverterDeclaringType = "FcbIncConv2",
            BackConverterMethodName = "Convert",
            BackConverterShape = ConverterShape.T1SingleValue,
        };
        Assert.True(withBack.Equals(withBack2));

        // 符号取用防未用告警（conv1/conv2 由共享装载器取得，供未来扩展断言）
        Assert.True(conv1 is not null || conv2 is not null);
    }

    /// <summary>
    /// 增量缓存 ②（提取层投影）：FluentBackDeclaration 值化投影——Method/Shape/EntityProperty 参与
    /// 相等性、Location 不参与（值层剥离惯例；变异验证锚点：投影成分增删本用例必红）。
    /// </summary>
    [Fact]
    public void Incremental_FluentBackDeclaration_ValueProjection()
    {
        var (comp, conv1, conv2, entityProp, dtoProp) = LoadIncrementSymbols();
        var tree = comp.SyntaxTrees.First();
        var locA = tree.GetLocation(new Microsoft.CodeAnalysis.Text.TextSpan(0, 1));
        var locB = tree.GetLocation(new Microsoft.CodeAnalysis.Text.TextSpan(5, 1));

        var d1 = new FluentBackDeclaration(conv1!, ConverterShape.T1SingleValue, locA, entityProp);
        var d1Clone = new FluentBackDeclaration(conv1!, ConverterShape.T1SingleValue, locB, entityProp); // 不同 Location
        var d2 = new FluentBackDeclaration(conv2!, ConverterShape.T1SingleValue, locA, entityProp);
        var d3 = new FluentBackDeclaration(conv1!, ConverterShape.T0Constant, locA, entityProp);
        var d4 = new FluentBackDeclaration(conv1!, ConverterShape.T1SingleValue, locA, dtoProp); // 不同 EntityProperty

        // Location 不参与（值化投影三成分：MethodRef + Shape + EntityProperty）
        Assert.True(d1.Equals(d1Clone), "Location 不参与 FluentBackDeclaration 相等性（值层剥离惯例）");
        // Method 差异 → 不等
        Assert.False(d1.Equals(d2), "方法引用差异应破坏相等（值化投影 MethodRef）");
        // Shape 差异 → 不等
        Assert.False(d1.Equals(d3), "形态差异应破坏相等（值化投影 Shape）");
        // EntityProperty 差异 → 不等
        Assert.False(d1.Equals(d4), "实体侧属性差异应破坏相等（值化投影 EntityProperty）");
        // 哈希一致性（同值同哈希）
        Assert.Equal(d1.GetHashCode(), d1Clone.GetHashCode());
    }

    // ── 全局忽略联动（P2-3 前向副作用面：NCMAP021 + 反向条目抑制）────────

    /// <summary>
    /// 全局忽略命中锚点属性（IgnoreNames="Tag"）→ ① 正向 NCMAP021 一并发射
    ///（HasConvertBackUsing 注入置位使锚点属性进入 HasExplicitAttributeValue 触发条件——
    /// 方案书 P2-3「代码零改动 + 行为变化（正确）」；与特性侧 [ConvertBack] 同口径）；
    /// ② 反向条目被抑制（ApplyReverseGlobalIgnoreFilter 既有行为对 Fluent 生效——反向方法
    /// 不含转换器调用）。
    /// </summary>
    [Fact]
    public void GlobalIgnore_HitsAnchor_NCMAP021_And_ReverseEntrySuppressed()
    {
        var source = """
            using NCMapper;
            public class FcbGiEntity { public string Tag { get; set; } = ""; }
            public class FcbGiDto { public string Tag { get; set; } = ""; }
            public static class FcbGiConv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbGiMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbGiEntity, FcbGiDto>().Member(d => d.Tag).ConvertBack(FcbGiConv.Convert);
                }
            }
            """;

        var (generated, diagnostics) = RunGeneratorWithConfig(source, new System.Collections.Generic.Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "Tag",
        });

        // ① 正向 NCMAP021（显式声明命中全局忽略——fail-closed 处置指引）
        Assert.Contains(diagnostics, d => d.Id == "NCMAP021" && d.GetMessage().Contains("Tag"));

        // ② 反向条目被抑制：正向过滤（ApplyTargetGlobalIgnoreValue 删除显式 pm + 补匹配
        // ShouldIgnoreGlobalValue 跳过）→ 反向快照无 Tag 条目 → 反向方法整体不生成
        //（唯一 (S,T) 对仅 Tag 属性——过滤后反向空 → CreateReverseMapping 返回 null）；
        // DeepCopy 方法全属性复制不受映射过滤影响（"dst.Tag = src.Tag" 仅存在于 DeepCopy——
        // 与映射忽略正交，不参与断言）
        Assert.DoesNotContain("Tag = global::FcbGiConv.Convert", generated);
        Assert.DoesNotContain("Map_FcbGiDto_To_FcbGiEntity", generated);
    }

    /// <summary>
    /// IsExplicitlyMapped 前向副作用（P2-3 第二消费点——审计修复补充）：类型完全不相容的
    /// 同名属性（自定义类型 P1↔P2 无转换路径）+ ConvertBack 声明 → HasConvertBackUsing 注入
    /// 置位使锚点属性获得「类型完全不相容自动跳过」豁免（IsExplicitlyMapped，
    /// CodeGenerator.Mapping.cs:1030/:1033——与特性侧 [ConvertBack] 语义一致）——
    /// 正向赋值保留（走运行时转换路径），不因不相容被静默跳过。
    /// </summary>
    [Fact]
    public void IsExplicitlyMapped_IncompatibleType_ConvertBackAnchorKeptInForward()
    {
        var source = """
            using NCMapper;
            public class FcbIePayloadA { public string V { get; set; } = ""; }
            public class FcbIePayloadB { public string V { get; set; } = ""; }
            public class FcbIeEntity { public FcbIePayloadA Data { get; set; } = new(); }
            public class FcbIeDto { public FcbIePayloadB Data { get; set; } = new(); }
            public static class FcbIeConv { public static FcbIePayloadA Convert(FcbIePayloadB fieldValue) => new(); }

            [MapperConfig]
            public class FcbIeMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbIeEntity, FcbIeDto>().Member(d => d.Data).ConvertBack(FcbIeConv.Convert);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 正向赋值保留（显式豁免——类型不相容不触发自动跳过；运行时按类型转换路径处理）
        var fwdBody = result.Methods["Map_FcbIeEntity_To_FcbIeDto"][0].Body;
        Assert.Contains("Data = ", fwdBody);
        // NCMAP002（自动路径类型不兼容诊断）在场——诊断照发（B-6 语义），仅生成不跳过
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP002");
    }

    /// <summary>全局忽略未命中（IgnoreNames 其他属性）→ 转换器照常消费（零误伤邻界）。</summary>
    [Fact]
    public void GlobalIgnore_Miss_AnchorUnaffected()
    {
        var source = """
            using NCMapper;
            public class FcbGmEntity { public string Tag { get; set; } = ""; }
            public class FcbGmDto { public string Tag { get; set; } = ""; }
            public static class FcbGmConv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbGmMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbGmEntity, FcbGmDto>().Member(d => d.Tag).ConvertBack(FcbGmConv.Convert);
                }
            }
            """;

        var (generated, diagnostics) = RunGeneratorWithConfig(source, new System.Collections.Generic.Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "SomethingElse",
        });

        Assert.Contains("Tag = global::FcbGmConv.Convert(src.Tag)", generated);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP021");
    }

    /// <summary>带 MSBuild 全局配置运行生成器（GlobalConfigMatchingTests 同款注入模式）</summary>
    private static (string, Microsoft.CodeAnalysis.Diagnostic[]) RunGeneratorWithConfig(
        string source, System.Collections.Generic.Dictionary<string, string> msbuildProperties)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var generator = new NCMapperSourceGenerator();
        var optionsProvider = new GlobalConfigReaderTestsConfigOptionsProvider(msbuildProperties);
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            optionsProvider: optionsProvider);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
        var generated = updatedCompilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated_GeneratedMapper.g.cs"))?
            .ToString() ?? "";
        return (generated, diagnostics.ToArray());
    }

    /// <summary>装载增量断言所需符号（Conv1/Conv2 方法 + 实体/DTO Tag 属性）</summary>
    private static (CSharpCompilation, IMethodSymbol?, IMethodSymbol?, Microsoft.CodeAnalysis.IPropertySymbol, Microsoft.CodeAnalysis.IPropertySymbol) LoadIncrementSymbols()
    {
        const string source = """
            using NCMapper;
            public class FcbIncEntity { public string Tag { get; set; } = ""; }
            public class FcbIncDto { public string Tag { get; set; } = ""; }
            public static class FcbIncConv1 { public static string Convert(string fieldValue) => "a:" + fieldValue; }
            public static class FcbIncConv2 { public static string Convert(string fieldValue) => "b:" + fieldValue; }
            """;
        var comp = (CSharpCompilation)TestHelper.BuildCompilation(source);
        var sm = comp.GetSemanticModel(comp.SyntaxTrees.First());

        IMethodSymbol? MethodOf(string className)
        {
            var cls = comp.SyntaxTrees.First().GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                .First(c => c.Identifier.ValueText == className);
            return sm.GetDeclaredSymbol(cls)!.GetMembers("Convert").OfType<IMethodSymbol>().FirstOrDefault();
        }
        Microsoft.CodeAnalysis.IPropertySymbol PropOf(string className)
        {
            var cls = comp.SyntaxTrees.First().GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                .First(c => c.Identifier.ValueText == className);
            return sm.GetDeclaredSymbol(cls)!.GetMembers("Tag").OfType<Microsoft.CodeAnalysis.IPropertySymbol>().First();
        }

        return (comp, MethodOf("FcbIncConv1"), MethodOf("FcbIncConv2"), PropOf("FcbIncEntity"), PropOf("FcbIncDto"));
    }
}
