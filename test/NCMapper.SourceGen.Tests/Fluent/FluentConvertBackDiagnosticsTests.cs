using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 诊断矩阵：F17~F22 六码正例 + 各 ≥1 邻界反例（合法声明不误报）+
/// 建议② F21 邻界补强（扩展方法/generic/optional/params/ref 变体实证定性）+ CS0121 双类编译验证
///（TDtoProp==TTarget 病态 + converter 方法组多重载——编译期红，解歧指引见 XML）。
/// </summary>
public class FluentConvertBackDiagnosticsTests
{
    // ── F17：Ignore + ConvertBack 矛盾 ──────────────────────────────

    /// <summary>F17 正例：同属性 Ignore + ConvertBack → Error + backDecl 丢弃（Ignore 照常）。</summary>
    [Fact]
    public void F17_IgnorePlusConvertBack_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbF17Entity { public string Tag { get; set; } = ""; }
            public class FcbF17Dto { public string Tag { get; set; } = ""; }
            public static class FcbF17Conv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF17Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF17Entity, FcbF17Dto>()
                       .Member(d => d.Tag).Ignore()
                       .Member(d => d.Tag).ConvertBack(FcbF17Conv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f17 = result.Diagnostics.Where(d => d.Id == "NCMAP_F17").ToList();
        Assert.Single(f17);
        Assert.Contains("Tag", f17[0].GetMessage());
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f17[0].Severity);
        // backDecl 丢弃 + Ignore 照常：正向不映射 Tag、反向无转换器调用
        var fwdBody = result.Methods["Map_FcbF17Entity_To_FcbF17Dto"][0].Body;
        Assert.DoesNotContain("Tag = ", fwdBody);
        Assert.DoesNotContain("FcbF17Conv.Convert", result.GeneratedCode);
    }

    /// <summary>F17 邻界：仅 Ignore（无 ConvertBack）→ 不误报。</summary>
    [Fact]
    public void F17_IgnoreOnly_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcbF17bEntity { public string Tag { get; set; } = ""; }
            public class FcbF17bDto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FcbF17bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF17bEntity, FcbF17bDto>().Member(d => d.Tag).Ignore();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F17");
    }

    // ── F18：实体侧反向写回点不存在（提取期单点）────────────────────

    /// <summary>F18 正例：实体无同名属性 → Error（提取期）+ 声明丢弃。</summary>
    [Fact]
    public void F18_EntitySideMissing_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbF18Entity { public string Name { get; set; } = ""; }
            public class FcbF18Dto { public string Tag { get; set; } = ""; }
            public static class FcbF18Conv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF18Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF18Entity, FcbF18Dto>().Member(d => d.Tag).ConvertBack(FcbF18Conv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f18 = result.Diagnostics.Where(d => d.Id == "NCMAP_F18").ToList();
        Assert.Single(f18);
        Assert.Contains("FcbF18Entity", f18[0].GetMessage());
        Assert.Contains("Tag", f18[0].GetMessage());
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f18[0].Severity);
        // 声明丢弃：反向无转换器调用
        Assert.DoesNotContain("FcbF18Conv.Convert", result.GeneratedCode);
    }

    /// <summary>F18 邻界：实体有同名属性（正常声明）→ 不误报。</summary>
    [Fact]
    public void F18_EntitySidePresent_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcbF18bEntity { public string Tag { get; set; } = ""; }
            public class FcbF18bDto { public string Tag { get; set; } = ""; }
            public static class FcbF18bConv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF18bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF18bEntity, FcbF18bDto>().Member(d => d.Tag).ConvertBack(FcbF18bConv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F18");
        result.Should().HaveMethodBodyContaining("Map_FcbF18bDto_To_FcbF18bEntity",
            "Tag = global::FcbF18bConv.Convert(src.Tag)");
    }

    // ── F19：converter 可见性不足 ────────────────────────────────

    /// <summary>F19 正例：private static converter → Error（镜像 F12 可见性契约）。</summary>
    [Fact]
    public void F19_PrivateConverter_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbF19Entity { public string Tag { get; set; } = ""; }
            public class FcbF19Dto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FcbF19Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF19Entity, FcbF19Dto>().Member(d => d.Tag).ConvertBack(PrivateConvert);
                }

                private static string PrivateConvert(string fieldValue) => "back:" + fieldValue;
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f19 = result.Diagnostics.Where(d => d.Id == "NCMAP_F19").ToList();
        Assert.Single(f19);
        Assert.Contains("PrivateConvert", f19[0].GetMessage());
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f19[0].Severity);
    }

    /// <summary>F19 邻界：internal static converter → 不误报（生成代码同程序集可见）。</summary>
    [Fact]
    public void F19_InternalConverter_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcbF19bEntity { public string Tag { get; set; } = ""; }
            public class FcbF19bDto { public string Tag { get; set; } = ""; }
            internal static class FcbF19bConv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF19bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF19bEntity, FcbF19bDto>().Member(d => d.Tag).ConvertBack(FcbF19bConv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F19");
        result.Should().HaveMethodBodyContaining("Map_FcbF19bDto_To_FcbF19bEntity",
            "Tag = global::FcbF19bConv.Convert(src.Tag)");
    }

    // ── F20：converter 非静态可寻址方法组 ─────────────────────────
    // F20 收窄（镜像 Convert F26，翻转清单）：
    // 无捕获表达式 lambda 翻转为"转录放行"（Map_CvBack_* 私有静态方法，反向发射消费）；
    // 保留禁令全清单仍拒——F41/F42。

    /// <summary>
    /// F20 翻转正例：无捕获表达式 lambda converter → 转录放行（生成 Map_CvBack_* 私有
    /// 静态方法 + 反向赋值调用点接线；原 F20_LambdaConverter_Diagnosed 断言 Error 的用例翻转）。
    /// </summary>
    [Fact]
    public void F20_LambdaConverter_Transcribed()
    {
        var source = """
            using NCMapper;
            public class FcbF20Entity { public string Tag { get; set; } = ""; }
            public class FcbF20Dto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FcbF20Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF20Entity, FcbF20Dto>().Member(d => d.Tag).ConvertBack(x => "back:" + x);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 翻转断言：无 F20（转录放行）+ 无 F41/F42（无捕获可转录）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F20");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F41" || d.Id == "NCMAP_F42");
        // B 形态产物：Map_CvBack_* 私有静态方法 + 反向赋值调用点接线
        //（方法名含 FNV 哈希尾——断言按前缀分段）
        Assert.Contains("private static string Map_CvBack_FcbF20Dto_Tag_", result.GeneratedCode);
        Assert.Contains(") => \"back:\" + x;", result.GeneratedCode);
        Assert.Contains("global::NCMapper.Generated.GeneratedMapper.Map_CvBack_FcbF20Dto_Tag_", result.GeneratedCode);
    }

    /// <summary>F20 保留禁令：捕获外层局部变量 → F41（仍拒——转录引擎禁令清单）。</summary>
    [Fact]
    public void F20_LambdaCapture_Rejected_F41()
    {
        var source = """
            using NCMapper;
            public class FcbF20cEntity { public string Tag { get; set; } = ""; }
            public class FcbF20cDto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FcbF20cMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    var prefix = "back:";
                    cfg.For<FcbF20cEntity, FcbF20cDto>().Member(d => d.Tag).ConvertBack(x => prefix + x);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f41 = result.Diagnostics.Where(d => d.Id == "NCMAP_F41").ToList();
        Assert.Single(f41);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f41[0].Severity);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F20");
        Assert.DoesNotContain("Map_CvBack_FcbF20cDto_Tag_", result.GeneratedCode);
    }

    /// <summary>F20 邻界：合法方法组 → 不误报。</summary>
    [Fact]
    public void F20_MethodGroup_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcbF20bEntity { public string Tag { get; set; } = ""; }
            public class FcbF20bDto { public string Tag { get; set; } = ""; }
            public static class FcbF20bConv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF20bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF20bEntity, FcbF20bDto>().Member(d => d.Tag).ConvertBack(FcbF20bConv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F20");
    }

    // ── F21：签名不匹配 4+2 模板 ─────────────────────────────────

    /// <summary>
    /// F21 逆变参数边缘实证（差异登记 D-4）：M(object) 对 T1(Func&lt;string,...&gt;) 与
    /// T2(Func&lt;Dto,...&gt;) 均可逆变绑定（string→object / Dto→object 隐式引用转换）→
    /// 编译期 CS0121 重载歧义先于生成期拦截（方案书 P1-2 预期的 F21 逆变形态实际由 CS0121 承接
    /// ——显式类型参不能消解，解歧用 converter 实参显式委托转换）。
    /// </summary>
    [Fact]
    public void F21_ContravariantParamEdge_CSAmbiguity()
    {
        var source = """
            using NCMapper;
            public class FcbF21Entity { public string Tag { get; set; } = ""; }
            public class FcbF21Dto { public string Tag { get; set; } = ""; }
            public static class FcbF21Conv { public static string Convert(object fieldValue) => "back"; }

            [MapperConfig]
            public class FcbF21Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF21Entity, FcbF21Dto>().Member(d => d.Tag).ConvertBack(FcbF21Conv.Convert);
                }
            }
            """;
        var compilation = TestHelper.BuildCompilation(source);
        Assert.Contains(compilation.GetDiagnostics(), d => d.Id == "CS0121");
    }

    /// <summary>F21 邻界：合法 T1 签名 → 不误报。</summary>
    [Fact]
    public void F21_ValidSignature_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcbF21bEntity { public string Tag { get; set; } = ""; }
            public class FcbF21bDto { public string Tag { get; set; } = ""; }
            public static class FcbF21bConv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF21bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF21bEntity, FcbF21bDto>().Member(d => d.Tag).ConvertBack(FcbF21bConv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F21");
    }

    /// <summary>
    /// F21 邻界补强（建议②·泛型方法）：双参泛型方法组经类型推断独占绑定 T3 重载
    /// Func&lt;Dto, string, string&gt;（T1/T2 单参不可达——无 CS0121），生成端 R5 形态排除
    ///（generic method）→ F21 而非静默。
    /// </summary>
    [Fact]
    public void F21_GenericMethodConverter_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbF21gEntity { public string Tag { get; set; } = ""; }
            public class FcbF21gDto { public string Tag { get; set; } = ""; }
            public static class FcbF21gConv { public static string Build<T>(T dto, string tag) => "back"; }

            [MapperConfig]
            public class FcbF21gMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF21gEntity, FcbF21gDto>().Member(d => d.Tag).ConvertBack(FcbF21gConv.Build);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F21");
    }

    /// <summary>F21 邻界补强（建议②·可选参数）：可选参方法组绑定成功 → R5 排除 → F21。</summary>
    [Fact]
    public void F21_OptionalParamConverter_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbF21oEntity { public string Tag { get; set; } = ""; }
            public class FcbF21oDto { public string Tag { get; set; } = ""; }
            public static class FcbF21oConv { public static string Convert(string fieldValue = "") => "back"; }

            [MapperConfig]
            public class FcbF21oMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF21oEntity, FcbF21oDto>().Member(d => d.Tag).ConvertBack(FcbF21oConv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F21");
    }

    /// <summary>F21 邻界补强（建议②·params 数组）：数组锚点 + params 方法组绑定成功 → R5 排除 → F21。</summary>
    [Fact]
    public void F21_ParamsArrayConverter_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbF21pEntity { public string[] Tags { get; set; } = []; }
            public class FcbF21pDto { public string[] Tags { get; set; } = []; }
            public static class FcbF21pConv { public static string Convert(params string[] fields) => "back"; }

            [MapperConfig]
            public class FcbF21pMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF21pEntity, FcbF21pDto>().Member(d => d.Tags).ConvertBack(FcbF21pConv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics, d => d.Id == "NCMAP_F21");
    }

    /// <summary>
    /// F21 邻界补强（建议②·ref 参数）：ref 方法组无法绑定 Func 委托（编译期 CS0123）——
    /// 实证定性：绑定失败路径落入 F20（非静态可寻址——非静默，Error 级；与 F21 形态排除路径分叉登记）。
    /// </summary>
    [Fact]
    public void F21_RefParamConverter_FallsToCompileErrorPath_F20()
    {
        var source = """
            using NCMapper;
            public class FcbF21rEntity { public string Tag { get; set; } = ""; }
            public class FcbF21rDto { public string Tag { get; set; } = ""; }
            public static class FcbF21rConv { public static string Convert(ref string fieldValue) => "back"; }

            [MapperConfig]
            public class FcbF21rMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF21rEntity, FcbF21rDto>().Member(d => d.Tag).ConvertBack(FcbF21rConv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 实证定性：ref 参数无法作方法组转换（编译期红），生成器侧落入 F20（Error 级——非静默）
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F20" || d.Id == "NCMAP_F21");
    }

    /// <summary>
    /// F21 邻界补强（建议②·扩展方法）：扩展方法组以静态形态绑定 Func&lt;string,string&gt;（编译期合法）——
    /// 实证定性：生成端按普通静态方法消费（T1 模板命中，`this` 修饰符不影响静态调用形态）。
    /// </summary>
    [Fact]
    public void F21_ExtensionMethodConverter_WorksAsStaticMethodGroup()
    {
        var source = """
            using NCMapper;
            public class FcbF21eEntity { public string Tag { get; set; } = ""; }
            public class FcbF21eDto { public string Tag { get; set; } = ""; }
            public static class FcbF21eConv { public static string Back(this string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF21eMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF21eEntity, FcbF21eDto>().Member(d => d.Tag).ConvertBack(FcbF21eConv.Back);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 实证定性：可转换、可生成（静态调用形态 global::FcbF21eConv.Back(src.Tag)）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F2"));
        result.Should().HaveMethodBodyContaining("Map_FcbF21eDto_To_FcbF21eEntity",
            "Tag = global::FcbF21eConv.Back(src.Tag)");
    }

    // ── F22：get-only/非 public setter DTO 锚点（Warning）──────────────

    /// <summary>F22 正例：get-only 锚点 → Warning + 声明丢弃（物化遍历 H-07 门将吞掉）。</summary>
    [Fact]
    public void F22_GetOnlyAnchor_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbF22Entity { public string Tag { get; set; } = ""; }
            public class FcbF22Dto { public string Tag { get; } = ""; }
            public static class FcbF22Conv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF22Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF22Entity, FcbF22Dto>().Member(d => d.Tag).ConvertBack(FcbF22Conv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f22 = result.Diagnostics.Where(d => d.Id == "NCMAP_F22").ToList();
        Assert.Single(f22);
        Assert.Contains("Tag", f22[0].GetMessage());
        Assert.Contains("FcbF22Dto", f22[0].GetMessage());
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f22[0].Severity);
        // 声明丢弃：反向无转换器调用（get-only 属性被 H-07 门吞掉）
        Assert.DoesNotContain("FcbF22Conv.Convert", result.GeneratedCode);
    }

    /// <summary>F22 邻界：public setter 锚点 → 不误报。</summary>
    [Fact]
    public void F22_PublicSetterAnchor_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcbF22bEntity { public string Tag { get; set; } = ""; }
            public class FcbF22bDto { public string Tag { get; set; } = ""; }
            public static class FcbF22bConv { public static string Convert(string fieldValue) => "back:" + fieldValue; }

            [MapperConfig]
            public class FcbF22bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbF22bEntity, FcbF22bDto>().Member(d => d.Tag).ConvertBack(FcbF22bConv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F22");
        result.Should().HaveMethodBodyContaining("Map_FcbF22bDto_To_FcbF22bEntity",
            "Tag = global::FcbF22bConv.Convert(src.Tag)");
    }

    // ── CS0121 双类编译验证（编译期红——解歧指引见 XML）────────────

    /// <summary>
    /// CS0121 ①：TDtoProp==TTarget 病态（自引用 DTO 属性）——T1/T2 重载签名合流 → 编译期歧义
    ///（显式类型参不能消解；解歧手段为 converter 实参显式委托转换）。
    /// </summary>
    [Fact]
    public void CS0121_SelfReferencingDto_OverloadAmbiguity()
    {
        var source = """
            using NCMapper;
            public class FcbAmbEntity { public FcbAmbDto Child { get; set; } = new(); }
            public class FcbAmbDto { public FcbAmbDto Child { get; set; } = new(); }
            public static class FcbAmbConv { public static FcbAmbDto Build(FcbAmbDto dto) => dto; }

            [MapperConfig]
            public class FcbAmbMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbAmbEntity, FcbAmbDto>().Member(d => d.Child).ConvertBack(FcbAmbConv.Build);
                }
            }
            """;
        var compilation = TestHelper.BuildCompilation(source);
        Assert.Contains(compilation.GetDiagnostics(), d => d.Id == "CS0121");
    }

    /// <summary>
    /// CS0121 ②：converter 方法组自身多重载（工具类同时含 M(string) 与 M(Dto)）——
    /// T1/T2 候选均可绑定 → 编译期歧义（比 ① 更常见的触发面）。
    /// </summary>
    [Fact]
    public void CS0121_MultiOverloadConverter_OverloadAmbiguity()
    {
        var source = """
            using NCMapper;
            public class FcbAmb2Entity { public string Tag { get; set; } = ""; }
            public class FcbAmb2Dto { public string Tag { get; set; } = ""; }
            public static class FcbAmb2Conv
            {
                public static string Convert(string fieldValue) => "v:" + fieldValue;
                public static string Convert(FcbAmb2Dto dto) => "d:" + dto.Tag;
            }

            [MapperConfig]
            public class FcbAmb2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbAmb2Entity, FcbAmb2Dto>().Member(d => d.Tag).ConvertBack(FcbAmb2Conv.Convert);
                }
            }
            """;
        var compilation = TestHelper.BuildCompilation(source);
        Assert.Contains(compilation.GetDiagnostics(), d => d.Id == "CS0121");
    }

    /// <summary>CS0121 解歧正例：converter 实参显式委托转换消歧（唯一可绑定重载）。</summary>
    [Fact]
    public void CS0121_ResolvedByExplicitDelegateCast()
    {
        var source = """
            using NCMapper;
            public class FcbAmb3Entity { public string Tag { get; set; } = ""; }
            public class FcbAmb3Dto { public string Tag { get; set; } = ""; }
            public static class FcbAmb3Conv
            {
                public static string Convert(string fieldValue) => "v:" + fieldValue;
                public static string Convert(FcbAmb3Dto dto) => "d:" + dto.Tag;
            }

            [MapperConfig]
            public class FcbAmb3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbAmb3Entity, FcbAmb3Dto>().Member(d => d.Tag).ConvertBack((System.Func<string, string>)FcbAmb3Conv.Convert);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 解歧后绑定 T1（显式委托转换指定 Func<string,string>——仅 M(string) 可绑定）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F1") || d.Id.StartsWith("NCMAP_F2"));
        result.Should().HaveMethodBodyContaining("Map_FcbAmb3Dto_To_FcbAmb3Entity",
            "Tag = global::FcbAmb3Conv.Convert(src.Tag)");
    }

    // ── 异名写回（lambda 体解析写回目标，NCMAP_F67 指引的可执行出口）────────

    /// <summary>
    /// 异名写回正例（T3 形态）：实体 <c>Amount</c> → DTO <c>AmountText</c>（正向 Format 后异名），
    /// 反向用 lambda 形态声明——写回目标由 lambda 体（第二形参的源字段值）解析为实体属性。
    /// <para>断言：①无 F18（不再按锚点名找工作回目标）；②反向方法体写入实体属性（T3 模板
    /// <c>M(src, src.AmountText)</c>）；③正向不受影响。</para>
    /// </summary>
    [Fact]
    public void AliasWriteBack_T3Lambda_ResolvesTargetFromBody()
    {
        var source = """
            using NCMapper;
            public class FcbAliasEntity { public decimal Amount { get; set; } }
            public class FcbAliasDto { public string AmountText { get; set; } = ""; }
            public static class FcbAliasConv
            {
                public static decimal Parse(string fieldValue) => decimal.Parse(fieldValue);
            }

            [MapperConfig]
            public class FcbAliasMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbAliasEntity, FcbAliasDto>()
                       .Member(d => d.AmountText).From(s => s.Amount).Format("N2")
                       .ConvertBack<decimal>((dto, text) => FcbAliasConv.Parse(text));
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F18");
        // F67 反转：反向已消费该属性的转换声明
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F67");
        // B 形态：lambda 经转录为生成的私有静态方法 Map_CvBack_*，反向以 (src, src.AmountText) 调用
        result.Should().HaveMethodBodyContaining("Map_FcbAliasDto_To_FcbAliasEntity",
            "Amount = global::NCMapper.Generated.GeneratedMapper.Map_CvBack_FcbAliasDto_Amount_");
        result.Should().HaveMethodBodyContaining("Map_FcbAliasDto_To_FcbAliasEntity",
            "(src, src.AmountText)");
    }

    /// <summary>
    /// 异名写回正例（T2 形态）：<c>(dto) =&gt; dto.AmountText</c>——表达式体本身就是形参上的成员访问，
    /// 该成员即写回目标（整源形态，无字段实参）。
    /// </summary>
    [Fact]
    public void AliasWriteBack_T2MemberAccess_ResolvesTargetFromBody()
    {
        var source = """
            using NCMapper;
            public class FcbAlias2Entity { public string Name { get; set; } = ""; }
            public class FcbAlias2Dto { public string DisplayName { get; set; } = ""; }

            [MapperConfig]
            public class FcbAlias2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbAlias2Entity, FcbAlias2Dto>()
                       .Member(d => d.DisplayName).From(s => s.Name)
                       .ConvertBack<string>((FcbAlias2Dto dto) => dto.DisplayName);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F18");
        // B 形态：lambda 经转录为生成的私有静态方法，反向写回实体属性 Name（取自 lambda 体成员访问）
        result.Should().HaveMethodBodyContaining("Map_FcbAlias2Dto_To_FcbAlias2Entity",
            "Name = global::NCMapper.Generated.GeneratedMapper.Map_CvBack_");
    }

    /// <summary>
    /// 异名写回反例（不猜测）：lambda 体是多字段组合式的复合表达式（读两处再算），
    /// 无法据此确定唯一写回目标 → 仍报 F18。
    /// </summary>
    [Fact]
    public void AliasWriteBack_CompositeExpression_StillDiagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbAlias3Entity { public decimal Amount { get; set; } }
            public class FcbAlias3Dto { public string AmountText { get; set; } = ""; public string FeeText { get; set; } = ""; }
            public static class FcbAlias3Conv { public static decimal Parse(string v) => decimal.Parse(v); }

            [MapperConfig]
            public class FcbAlias3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbAlias3Entity, FcbAlias3Dto>()
                       .Member(d => d.AmountText)
                       .ConvertBack<decimal>((FcbAlias3Dto dto) => FcbAlias3Conv.Parse(dto.AmountText) + FcbAlias3Conv.Parse(dto.FeeText));
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f18 = result.Diagnostics.Where(d => d.Id == "NCMAP_F18").ToList();
        Assert.Single(f18);
    }

    /// <summary>
    /// 异名写回反例（非形参接收者）：成员访问落在静态辅助类的字段上
    /// （<c>(dto) =&gt; Helper.AmountText</c>）——接收者不是形参，不解析为写回目标，仍报 F18。
    /// </summary>
    [Fact]
    public void AliasWriteBack_StaticMemberReceiver_StillDiagnosed()
    {
        var source = """
            using NCMapper;
            public class FcbAlias4Entity { public decimal Amount { get; set; } }
            public class FcbAlias4Dto { public string AmountText { get; set; } = ""; }
            public static class FcbAlias4Helper { public static string AmountText { get; set; } = ""; }

            [MapperConfig]
            public class FcbAlias4Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbAlias4Entity, FcbAlias4Dto>()
                       .Member(d => d.AmountText)
                       .ConvertBack<decimal>((FcbAlias4Dto dto) => FcbAlias4Helper.AmountText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Single(result.Diagnostics.Where(d => d.Id == "NCMAP_F18"));
    }

    /// <summary>
    /// 同名优先（回归）：实体侧存在同名属性时，既有同名解析照旧生效——
    /// lambda 体解析不覆盖、不干扰（既有行为零变化）。
    /// </summary>
    [Fact]
    public void SameName_TakesPrecedenceOverLambdaBody()
    {
        var source = """
            using NCMapper;
            public class FcbAlias5Entity { public string Tag { get; set; } = ""; public string Other { get; set; } = ""; }
            public class FcbAlias5Dto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FcbAlias5Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcbAlias5Entity, FcbAlias5Dto>()
                       .Member(d => d.Tag)
                       .ConvertBack<string>((FcbAlias5Dto dto) => dto.Other);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F18");
        // 同名解析优先：写回实体 Tag（同名），lambda 体里的 Other 不改变写回点
        result.Should().HaveMethodBodyContaining("Map_FcbAlias5Dto_To_FcbAlias5Entity",
            "Tag = global::NCMapper.Generated.GeneratedMapper.Map_CvBack_");
    }
}
