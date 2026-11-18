using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 生成端矩阵（更名 ConvertUsing→Convert）：
/// Convert 四形态 + Nullable 守卫 + 附着路径 +
/// 交互矩阵（ConvertBack 共存 / F23 矛盾 / Q3 .Convert 共存 / Q2 四终端豁免 / 方向性忽略 / FromJson）。
/// <para>断言模板经特性侧 [Convert] 既有产物校准（BuildStaticConverterCall 发射端——
/// 同一发射端零改动复用：T1 `X = global::Conv.M(src.X)`、T2 `M(src)` 整源、T3 `M(src, src.X)`、
/// T1Guarded `(src.X).HasValue ? M((src.X).Value) : default` 三元）。</para>
/// <para>双层断言：①快照层（ExtractCore 直调——PropertyMappings 正向三元组值 + HasOverrideIgnore）；
/// ②文本层（GenerateMapper——正向方法体转换器调用）。</para>
/// </summary>
public class FluentConvertTests
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

    // ── T0~T3 四形态（自动匹配/ForwardOnly 路径）──────────────────────

    /// <summary>
    /// T1 单值（最常见形态）：同名源自动匹配（ForwardOnly 路径）→ pm 正向三元组 +
    /// 正向方法体消费转换器（`AgeBand = global::FcuT1Conv.ToBand(src.Age)`——特性侧同构实测口径）。
    /// </summary>
    [Fact]
    public void T1_SameNameSource_InjectsTriple_And_GeneratesConverterCall()
    {
        var source = """
            using NCMapper;
            public class FcuT1Entity { public int Age { get; set; } }
            public class FcuT1Dto { public string Age { get; set; } = ""; }
            public static class FcuT1Conv { public static string ToBand(int fieldValue) => fieldValue < 18 ? "minor" : "adult"; }

            [MapperConfig]
            public class FcuT1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuT1Entity, FcuT1Dto>().Member(d => d.Age).Convert<int>(FcuT1Conv.ToBand);
                }
            }
            """;

        // ① 快照层：正向三元组注入契约（物化注入）
        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Age");
        Assert.Equal("FcuT1Conv", pm.ConverterDeclaringType);
        Assert.Equal("ToBand", pm.ConverterMethodName);
        Assert.Equal(ConverterShape.T1SingleValue, pm.ConverterShape);
        Assert.Equal("Age", pm.Source!.Name); // 源侧同名解析
        Assert.False(pm.Target.HasOverrideIgnore); // overrideIgnore 缺省 false

        // ② 文本层：正向方法体消费转换器（BuildStaticConverterCall 发射端——T1 字段值入参）
        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcuT1Entity_To_FcuT1Dto",
            "Age = global::FcuT1Conv.ToBand(src.Age)");
    }

    /// <summary>
    /// T0 无参常量：`Func&lt;TTargetProp&gt;` 签名——常量写入（形态编码 T0Constant）。
    /// </summary>
    [Fact]
    public void T0_Constant_EncodesConstantShape()
    {
        var source = """
            using NCMapper;
            public class FcuT0Entity { public string Stamp { get; set; } = ""; }
            public class FcuT0Dto { public string Stamp { get; set; } = ""; }
            public static class FcuT0Conv { public static string Stamp() => "fixed"; }

            [MapperConfig]
            public class FcuT0Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuT0Entity, FcuT0Dto>().Member(d => d.Stamp).Convert(FcuT0Conv.Stamp);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Stamp");
        Assert.Equal("FcuT0Conv", pm.ConverterDeclaringType);
        Assert.Equal(ConverterShape.T0Constant, pm.ConverterShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcuT0Entity_To_FcuT0Dto",
            "Stamp = global::FcuT0Conv.Stamp()");
    }

    /// <summary>
    /// T2 整源：`Func&lt;TSrcProp, TTargetProp&gt;` 签名且单参 == 源实体类型——
    /// 断言「形态匹配判 T2 + 生成代码整源入参」（评审 _002 P0-1 口径——不断言重载选择）。
    /// </summary>
    [Fact]
    public void T2_WholeSource_ShapeJudgedMultiField_And_PassesWholeEntity()
    {
        var source = """
            using NCMapper;
            public class FcuT2Entity { public string City { get; set; } = ""; public string Street { get; set; } = ""; public string FullAddress { get; set; } = ""; }
            public class FcuT2Dto { public string FullAddress { get; set; } = ""; }
            public static class FcuT2Conv { public static string Build(FcuT2Entity entity) => entity.City + ", " + entity.Street; }

            [MapperConfig]
            public class FcuT2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuT2Entity, FcuT2Dto>().Member(d => d.FullAddress).Convert(FcuT2Conv.Build);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "FullAddress");
        // 形态判定：单参 == 源实体类型 → T2MultiField（Q3 优先序——特性侧 TryMatchConverterTemplate 同口径）
        Assert.Equal(ConverterShape.T2MultiField, pm.ConverterShape);
        Assert.Equal("FullAddress", pm.Source!.Name);

        var result = TestHelper.GenerateMapper(source);
        // 整源入参：M(src) —— 整个源对象传给转换器
        result.Should().HaveMethodBodyContaining("Map_FcuT2Entity_To_FcuT2Dto",
            "FullAddress = global::FcuT2Conv.Build(src)");
    }

    /// <summary>
    /// T3 组合+字段值：`Func&lt;TSource, TSrcProp, TTargetProp&gt;` 签名——整源 + 字段值双入参。
    /// </summary>
    [Fact]
    public void T3_Combined_PassesSourceAndField()
    {
        var source = """
            using NCMapper;
            public class FcuT3Entity { public string Name { get; set; } = ""; public string Locale { get; set; } = ""; public int Label { get; set; } }
            public class FcuT3Dto { public string Label { get; set; } = ""; }
            public static class FcuT3Conv { public static string Build(FcuT3Entity entity, int fieldValue) => "v" + fieldValue + "@" + entity.Locale; }

            [MapperConfig]
            public class FcuT3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuT3Entity, FcuT3Dto>().Member(d => d.Label).Convert<int>(FcuT3Conv.Build);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Label");
        Assert.Equal(ConverterShape.T3Combined, pm.ConverterShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcuT3Entity_To_FcuT3Dto",
            "Label = global::FcuT3Conv.Build(src, src.Label)");
    }

    /// <summary>
    /// T1Guarded Nullable 守卫：源属性 Nullable&lt;DateTime&gt; + converter 入参 DateTime——
    /// 生成 HasValue 三元（null 不调用、目标保持 default）。
    /// </summary>
    [Fact]
    public void T1Guarded_NullableSource_GeneratesGuardTernary()
    {
        var source = """
            using System;
            using NCMapper;
            public class FcuGEntity { public DateTime? CreatedAt { get; set; } }
            public class FcuGDto { public string CreatedAt { get; set; } = ""; }
            public static class FcuGConv { public static string Format(DateTime fieldValue) => fieldValue.ToString("yyyy-MM-dd"); }

            [MapperConfig]
            public class FcuGMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuGEntity, FcuGDto>().Member(d => d.CreatedAt).Convert<DateTime>(FcuGConv.Format);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "CreatedAt");
        Assert.Equal(ConverterShape.T1Guarded, pm.ConverterShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcuGEntity_To_FcuGDto",
            "(src.CreatedAt).HasValue ? global::FcuGConv.Format((src.CreatedAt).Value) : default");
    }

    // ── 附着路径（显式声明附着 / 扁平化源 / [MapProperty] 反向查找源）──────────

    /// <summary>
    /// 显式来源附着：.Member(d =&gt; d.X).From(s =&gt; s.Y) + .Member(d =&gt; d.X).Convert(M) 同锚点——
    /// pm 用显式源路径，转换器三元组附着其上（附着模式①：显式 Build* 产物附着）。
    /// </summary>
    [Fact]
    public void ExplicitMap_AttachesConverterTriple()
    {
        var source = """
            using NCMapper;
            public class FcuEEntity { public int Age { get; set; } }
            public class FcuEDto { public string AgeBand { get; set; } = ""; }
            public static class FcuEConv { public static string ToBand(int fieldValue) => fieldValue < 18 ? "minor" : "adult"; }

            [MapperConfig]
            public class FcuEMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuEEntity, FcuEDto>()
                       .Member(d => d.AgeBand).From(s => s.Age)
                       .Member(d => d.AgeBand).Convert<int>(FcuEConv.ToBand);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "AgeBand");
        Assert.Equal("Age", pm.Source!.Name); // 显式路径源
        Assert.Equal("FcuEConv", pm.ConverterDeclaringType);
        Assert.Equal(ConverterShape.T1SingleValue, pm.ConverterShape);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcuEEntity_To_FcuEDto",
            "AgeBand = global::FcuEConv.ToBand(src.Age)");
    }

    /// <summary>
    /// 扁平化源：DTO 锚点名经 TryFindFlattenedPath 命中嵌套路径（提取期步骤 4）——
    /// pm.IsFlattened=true + PathOrigin=DerivedFlatten + 转换器消费叶值。
    /// </summary>
    [Fact]
    public void FlattenedSource_AttachesConverterTriple()
    {
        var source = """
            using NCMapper;
            public class FcuFEntity { public FcuFAddr Address { get; set; } = new(); public string City { get; set; } = ""; }
            public class FcuFAddr { public string City { get; set; } = ""; }
            public class FcuFDto { public string AddressCity { get; set; } = ""; }
            public static class FcuFConv { public static string Upper(string fieldValue) => fieldValue.ToUpperInvariant(); }

            [MapperConfig]
            public class FcuFMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuFEntity, FcuFDto>().Member(d => d.AddressCity).Convert<string>(FcuFConv.Upper);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "AddressCity");
        // 扁平化：Address.City 叶为源（提取期解析 + 物化 ForwardOnly 同产物）
        Assert.True(pm.IsFlattened);
        Assert.Equal(2, pm.SourcePath!.Count);
        Assert.Equal("FcuFConv", pm.ConverterDeclaringType);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcuFEntity_To_FcuFDto",
            "AddressCity = global::FcuFConv.Upper(src.Address?.City)");
    }

    /// <summary>
    /// [MapProperty] 反向查找源：同名/扁平化均未命中时经源属性反向标注解析
    ///（提取期步骤 4 第三路——与特性侧 [Convert] 消费端 B1 改动点 2 一致）。
    /// </summary>
    [Fact]
    public void ReverseLookupSource_AttachesConverterTriple()
    {
        var source = """
            using NCMapper;
            public class FcuREntity
            {
                [NCMapper.Attributes.MapProperty(typeof(FcuRDto), nameof(FcuRDto.DisplayName))]
                public string FullName { get; set; } = "";
            }
            public class FcuRDto { public string DisplayName { get; set; } = ""; }
            public static class FcuRConv { public static string TrimName(string fieldValue) => fieldValue.Trim(); }

            [MapperConfig]
            public class FcuRMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuREntity, FcuRDto>().Member(d => d.DisplayName).Convert<string>(FcuRConv.TrimName);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "DisplayName");
        Assert.Equal("FullName", pm.Source!.Name); // 反向查找命中
        Assert.Equal("FcuRConv", pm.ConverterDeclaringType);

        var result = TestHelper.GenerateMapper(source);
        result.Should().HaveMethodBodyContaining("Map_FcuREntity_To_FcuRDto",
            "DisplayName = global::FcuRConv.TrimName(src.FullName)");
    }

    // ── 交互矩阵 ──────────────────────────────────────────────

    /// <summary>
    /// Convert × ConvertBack 同名锚点共存：正向转换 + 反向转换各自独立可选
    ///（镜像特性侧 [Convert]+[ConvertBack] 同属性独立可选结构）。
    /// </summary>
    [Fact]
    public void Convert_And_ConvertBack_SameAnchor_BothEmit()
    {
        var source = """
            using System;
            using NCMapper;
            public class FcuCBEntity { public DateTime Created { get; set; } }
            public class FcuCBDto { public string Created { get; set; } = ""; }
            public static class FcuCBConv
            {
                public static string ToText(DateTime v) => v.ToString("yyyy-MM-dd");
                public static DateTime Parse(string v) => DateTime.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            }

            [MapperConfig]
            public class FcuCBMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuCBEntity, FcuCBDto>()
                       .Member(d => d.Created).Convert<DateTime>(FcuCBConv.ToText)
                       .Member(d => d.Created).ConvertBack(FcuCBConv.Parse);
                }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        // 正向：Convert 消费（字段值入参）
        result.Should().HaveMethodBodyContaining("Map_FcuCBEntity_To_FcuCBDto",
            "Created = global::FcuCBConv.ToText(src.Created)");
        // 反向：ConvertBack 消费（独立反向转换）
        result.Should().HaveMethodBodyContaining("Map_FcuCBDto_To_FcuCBEntity",
            "Created = global::FcuCBConv.Parse(src.Created)");
    }

    /// <summary>
    /// Q3 共存验证（v1.1 增补，强制）：.Map（表达式树）与 .Convert（方法组）同名锚点共存——
    /// 编译通过 + 生成代码无冲突（机制不同：表达式树运行时求值 vs 方法组编译期解析；
    /// 本用例 Map 显式路径优先建 pm，Convert 附着其上——后附着三元组胜出）。
    /// </summary>
    [Fact]
    public void Q3_Map_And_Convert_SameAnchor_Coexist()
    {
        var source = """
            using NCMapper;
            public class FcuQ3Entity { public int Age { get; set; } public int Label { get; set; } }
            public class FcuQ3Dto { public string Label { get; set; } = ""; }
            public static class FcuQ3Conv { public static string ToText(int fieldValue) => "age:" + fieldValue; }

            [MapperConfig]
            public class FcuQ3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuQ3Entity, FcuQ3Dto>()
                       .Member(d => d.Label).From(s => s.Age)
                       .Member(d => d.Label).Convert<int>(FcuQ3Conv.ToText);
                }
            }
            """;

        // 编译通过（无 CS0121 冲突）+ 两声明各自消费
        var result = TestHelper.GenerateMapper(source);
        Assert.True(result.Diagnostics.All(d => d.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
            "Map 与 Convert 同链共存不应产生 Error 诊断");
        // .Map 显式 src 路径（s => s.Age）为 Convert 源侧解析①优先输入——
        // 转换器消费显式源（两遍链解析：Convert 感知先行 Map 声明的 src）
        result.Should().HaveMethodBodyContaining("Map_FcuQ3Entity_To_FcuQ3Dto",
            "Label = global::FcuQ3Conv.ToText(src.Age)");
    }

    /// <summary>
    /// Q2 豁免门（Convert 终端）：overrideIgnore: true → 快照 Target.HasOverrideIgnore 置位
    ///（正反向双链全局忽略过滤豁免——物化叠加位断言；消费面读点 SnapshotConfig/ReversePlanner 既有）。
    /// </summary>
    [Fact]
    public void Q2_Convert_OverrideIgnore_True_SetsFlag()
    {
        var source = """
            using NCMapper;
            public class FcuOEntity { public int Age { get; set; } }
            public class FcuODto { public string Age { get; set; } = ""; }
            public static class FcuOConv { public static string ToBand(int fieldValue) => "band"; }

            [MapperConfig]
            public class FcuOMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuOEntity, FcuODto>().Member(d => d.Age).Convert<int>(FcuOConv.ToBand, overrideIgnore: true);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Age");
        Assert.True(pm.Target.HasOverrideIgnore, "overrideIgnore: true → Target.HasOverrideIgnore 置位");
        Assert.Equal("FcuOConv", pm.ConverterDeclaringType);
    }

    /// <summary>
    /// Q2 豁免门（Map/Mask/ConvertBack 终端，v1.1 四终端全量）：overrideIgnore: true → 置位；
    /// 缺省（false）→ 不置位（照常被全局忽略抑制——对照组）。
    /// </summary>
    [Fact]
    public void Q2_Map_Mask_ConvertBack_OverrideIgnore_Flags()
    {
        var source = """
            using NCMapper;
            public class FcuQ2Entity { public string A { get; set; } = ""; public string B { get; set; } = ""; public string C { get; set; } = ""; }
            public class FcuQ2Dto { public string A { get; set; } = ""; public string B { get; set; } = ""; public string C { get; set; } = ""; }
            public static class FcuQ2Conv { public static string Parse(string v) => v; }

            [MapperConfig]
            public class FcuQ2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuQ2Entity, FcuQ2Dto>()
                       .Member(d => d.A).From(s => s.A, overrideIgnore: true)
                       .Member(d => d.B).From(s => s.B)
                       .Member(d => d.C).Mask<FcuMaskPhone>(overrideIgnore: true);
                }
            }
            public class FcuMaskPhone : NCMapper.IMask { public string Mask(string? value) => value ?? ""; }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pmA = PmOf(snapshot!, "A");
        Assert.True(pmA.Target.HasOverrideIgnore, "Map overrideIgnore: true → 置位");
        var pmB = PmOf(snapshot!, "B");
        Assert.False(pmB.Target.HasOverrideIgnore, "Map 缺省 → 不置位（照常抑制对照组）");
        var pmC = PmOf(snapshot!, "C");
        Assert.True(pmC.Target.HasOverrideIgnore, "Mask overrideIgnore: true → 置位");
    }

    /// <summary>
    /// Q2 豁免门（ConvertBack 终端）：overrideIgnore: true → 匹配对 pm 置位 + ReverseOnly 条目置位（实体侧承载）。
    /// </summary>
    [Fact]
    public void Q2_ConvertBack_OverrideIgnore_Flag_OnMatchedPair()
    {
        var source = """
            using NCMapper;
            public class FcuQ2BEntity { public string Tag { get; set; } = ""; }
            public class FcuQ2BDto { public string Tag { get; set; } = ""; }
            public static class FcuQ2BConv { public static string Convert(string v) => v; }

            [MapperConfig]
            public class FcuQ2BMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuQ2BEntity, FcuQ2BDto>().Member(d => d.Tag).ConvertBack(FcuQ2BConv.Convert, overrideIgnore: true);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Tag");
        Assert.True(pm.Target.HasOverrideIgnore, "ConvertBack overrideIgnore: true → 匹配对 pm.Target 置位");
    }

    /// <summary>
    /// F-2 方向性忽略（ToEntity）：正向照常映射（pm 保留）+ 快照 MapIgnoreDirection=2
    ///（镜像 [MapIgnore(Direction.ToEntity)] 采集——反向 ShouldIgnoreInDirectionValue 命中跳过）。
    /// </summary>
    [Fact]
    public void F2_DirectionalIgnore_ToEntity_KeepsForwardMapping()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class FcuDEntity { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public class FcuDDto { public int Id { get; set; } public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FcuDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuDEntity, FcuDDto>().Member(d => d.Tag).Ignore(Direction.ToEntity);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        // 正向 pm 保留（方向感知——非完全跳过）
        var pm = PmOf(snapshot!, "Tag");
        Assert.True(pm.Target.HasMapIgnore, "方向性忽略 → HasMapIgnore 置位（镜像 [MapIgnore] 采集）");
        Assert.Equal(2, pm.Target.MapIgnoreDirection); // ToEntity

        var result = TestHelper.GenerateMapper(source);
        // 正向照常映射（ToEntity 只抑制反向）
        Assert.Contains("Tag = ", result.Methods["Map_FcuDEntity_To_FcuDDto"][0].Body);
        // 反向不派生（Direction.ToEntity → ShouldIgnoreInDirectionValue 命中 → 无反向 Tag 条目）
        var reverseBody = result.Methods["Map_FcuDDto_To_FcuDEntity"][0].Body;
        Assert.DoesNotContain("Tag = ", reverseBody);
    }

    /// <summary>
    /// F-2 方向性忽略（ToDto）：正向跳过（生成端 ShouldSkipForDirection 命中）+ 反向派生保留。
    /// </summary>
    [Fact]
    public void F2_DirectionalIgnore_ToDto_SkipsForward()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class FcuD2Entity { public string Tag { get; set; } = ""; }
            public class FcuD2Dto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FcuD2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuD2Entity, FcuD2Dto>().Member(d => d.Tag).Ignore(Direction.ToDto);
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Tag");
        Assert.Equal(1, pm.Target.MapIgnoreDirection); // ToDto

        var result = TestHelper.GenerateMapper(source);
        // 正向跳过（ShouldSkipForDirection(toDto=true) 命中）
        Assert.DoesNotContain("Tag = ", result.Methods["Map_FcuD2Entity_To_FcuD2Dto"][0].Body);
        // 反向照常派生
        Assert.Contains("Tag = ", result.Methods["Map_FcuD2Dto_To_FcuD2Entity"][0].Body);
    }

    /// <summary>
    /// F-3 FromJson：源侧 string → 目标复杂类型经 JsonConverter 展开（正向）；
    /// 快照 pm.IsFromJson 置位 + 生成代码 JSON 反序列化调用（镜像特性侧 [MapProperty(IsFromJson=true)]）。
    /// </summary>
    [Fact]
    public void F3_FromJson_SetsFlag_And_GeneratesJsonDeserialization()
    {
        var source = """
            using NCMapper;
            public class FcuJEntity { public string Payload { get; set; } = ""; }
            public class FcuJDto { public FcuJData Payload { get; set; } = new(); }
            public class FcuJData { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class FcuJMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuJEntity, FcuJDto>().Member(d => d.Payload).FromJson();
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        var pm = PmOf(snapshot!, "Payload");
        Assert.True(pm.IsFromJson, "FromJson → pm.IsFromJson 置位");
        Assert.Equal("Payload", pm.Source!.Name);

        var result = TestHelper.GenerateMapper(source);
        // JSON 反序列化发射（特性侧 [MapProperty(IsFromJson)] 同款——JsonConvert 调用形态）
        var fwdBody = result.Methods["Map_FcuJEntity_To_FcuJDto"][0].Body;
        Assert.Contains("Payload", fwdBody);
        Assert.Contains("JsonConverter", fwdBody);
    }

    /// <summary>
    /// F-3 FromJson 类型门（邻界）：值类型参与（int 源）→ 声明静默丢弃（镜像特性侧值类型跳过）。
    /// </summary>
    [Fact]
    public void F3_FromJson_ValueType_Dropped()
    {
        var source = """
            using NCMapper;
            public class FcuJ2Entity { public int Payload { get; set; } }
            public class FcuJ2Dto { public string Payload { get; set; } = ""; }

            [MapperConfig]
            public class FcuJ2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuJ2Entity, FcuJ2Dto>().Member(d => d.Payload).FromJson();
                }
            }
            """;

        var snapshot = ExtractSnapshot(source);
        Assert.NotNull(snapshot);
        Assert.DoesNotContain(snapshot!.PropertyMappings, p => p.Target.Name == "Payload");
    }
}
