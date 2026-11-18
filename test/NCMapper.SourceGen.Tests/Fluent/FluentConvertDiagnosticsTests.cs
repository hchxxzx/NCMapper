using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// 诊断矩阵：F23~F28 六码正例 + 各 ≥1 邻界反例（合法声明不误报）+
/// CS0121 双类编译验证（converter 方法组多重载——编译期红，解歧指引见 FluentMappingConfig XML §T1 重载）。
/// </summary>
public class FluentConvertDiagnosticsTests
{
    // ── F23：Ignore + Convert 矛盾 ──────────────────────────────

    /// <summary>F23 正例：同属性 Ignore + Convert → Error + forwardDecl 丢弃（Ignore 照常）。</summary>
    [Fact]
    public void F23_IgnorePlusConvert_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcuF23Entity { public string Tag { get; set; } = ""; }
            public class FcuF23Dto { public string Tag { get; set; } = ""; }
            public static class FcuF23Conv { public static string ToText(string fieldValue) => "v:" + fieldValue; }

            [MapperConfig]
            public class FcuF23Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF23Entity, FcuF23Dto>()
                       .Member(d => d.Tag).Ignore()
                       .Member(d => d.Tag).Convert<string>(FcuF23Conv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f23 = result.Diagnostics.Where(d => d.Id == "NCMAP_F23").ToList();
        Assert.Single(f23);
        Assert.Contains("Tag", f23[0].GetMessage());
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f23[0].Severity);
        // forwardDecl 丢弃 + Ignore 照常：正向不映射 Tag、无转换器调用
        var fwdBody = result.Methods["Map_FcuF23Entity_To_FcuF23Dto"][0].Body;
        Assert.DoesNotContain("Tag = ", fwdBody);
        Assert.DoesNotContain("FcuF23Conv.ToText", result.GeneratedCode);
    }

    /// <summary>F23 邻界：仅 Ignore（无 Convert）→ 不误报。</summary>
    [Fact]
    public void F23_IgnoreOnly_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcuF23bEntity { public string Tag { get; set; } = ""; }
            public class FcuF23bDto { public string Tag { get; set; } = ""; }

            [MapperConfig]
            public class FcuF23bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF23bEntity, FcuF23bDto>().Member(d => d.Tag).Ignore();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F23");
    }

    // ── F24：源侧不可达（同名/扁平化/反向查找全失败）────────────────

    /// <summary>F24 正例：锚点名在源侧全路径不可达 → Error + 声明丢弃。</summary>
    [Fact]
    public void F24_SourceUnreachable_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcuF24Entity { public string Name { get; set; } = ""; }
            public class FcuF24Dto { public string Band { get; set; } = ""; }
            public static class FcuF24Conv { public static string ToText(string fieldValue) => "v:" + fieldValue; }

            [MapperConfig]
            public class FcuF24Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF24Entity, FcuF24Dto>().Member(d => d.Band).Convert<string>(FcuF24Conv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f24 = result.Diagnostics.Where(d => d.Id == "NCMAP_F24").ToList();
        Assert.Single(f24);
        Assert.Contains("FcuF24Entity", f24[0].GetMessage());
        Assert.Contains("Band", f24[0].GetMessage());
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f24[0].Severity);
        // 声明丢弃：无转换器调用
        Assert.DoesNotContain("FcuF24Conv.ToText", result.GeneratedCode);
    }

    /// <summary>F24 邻界：同名源可达（正常声明）→ 不误报 + 转换器调用生成。</summary>
    [Fact]
    public void F24_SameNameReachable_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcuF24bEntity { public string Band { get; set; } = ""; }
            public class FcuF24bDto { public string Band { get; set; } = ""; }
            public static class FcuF24bConv { public static string ToText(string fieldValue) => "v:" + fieldValue; }

            [MapperConfig]
            public class FcuF24bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF24bEntity, FcuF24bDto>().Member(d => d.Band).Convert<string>(FcuF24bConv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F24");
        result.Should().HaveMethodBodyContaining("Map_FcuF24bEntity_To_FcuF24bDto",
            "Band = global::FcuF24bConv.ToText(src.Band)");
    }

    // ── F25：converter 可见性不足 ────────────────────────────────

    /// <summary>F25 正例：private static converter 方法 → Error（镜像 F19 可见性契约——生成代码跨类不可达）。</summary>
    [Fact]
    public void F25_PrivateConverter_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcuF25Entity { public string Band { get; set; } = ""; }
            public class FcuF25Dto { public string Band { get; set; } = ""; }

            [MapperConfig]
            public class FcuF25Mapper
            {
                private static string ToText(string v) => "v:" + v;

                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF25Entity, FcuF25Dto>().Member(d => d.Band).Convert<string>(ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f25 = result.Diagnostics.Where(d => d.Id == "NCMAP_F25").ToList();
        Assert.Single(f25);
        Assert.Contains("ToText", f25[0].GetMessage());
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f25[0].Severity);
        Assert.DoesNotContain("ToText", result.GeneratedCode);
    }

    /// <summary>F25 邻界：public static converter（正常声明）→ 不误报。</summary>
    [Fact]
    public void F25_PublicConverter_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcuF25bEntity { public string Band { get; set; } = ""; }
            public class FcuF25bDto { public string Band { get; set; } = ""; }
            public static class FcuF25bConv { public static string ToText(string v) => "v:" + v; }

            [MapperConfig]
            public class FcuF25bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF25bEntity, FcuF25bDto>().Member(d => d.Band).Convert<string>(FcuF25bConv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F25");
    }

    // ── F26：非静态可寻址方法组（lambda）─────────────────────────
    // F26 收窄（翻转清单）：无捕获表达式 lambda 从
    // "F26 整体拒绝"翻转为"转录放行"（InlineTranscriber → GeneratedMapper 私有静态方法）；
    // 保留禁令全清单（捕获/私有与受保护成员访问/语句体/局部函数/实例方法绑定）仍拒——F41/F42。

    /// <summary>
    /// F26 翻转正例：无捕获表达式 lambda converter → 转录放行（生成 Map_Cv_* 私有静态
    /// 方法 + 调用点接线；原 F26_LambdaConverter_Diagnosed 断言 Error 的用例按契约修订翻转）。
    /// </summary>
    [Fact]
    public void F26_LambdaConverter_Transcribed()
    {
        var source = """
            using System;
            using NCMapper;
            public class FcuF26Entity { public string Band { get; set; } = ""; }
            public class FcuF26Dto { public string Band { get; set; } = ""; }

            [MapperConfig]
            public class FcuF26Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF26Entity, FcuF26Dto>().Member(d => d.Band).Convert((string v) => "v:" + v);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 翻转断言：无 F26（转录放行）+ 无 F41/F42（无捕获可转录）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F26");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F41" || d.Id == "NCMAP_F42");
        // B 形态产物：Map_Cv_* 私有静态方法（形参类型 string + 转录体）+ 生成赋值调用点接线
        //（方法名含 (S,T,prop,body) FNV 哈希尾——断言按前缀 + 调用实参分段）
        Assert.Contains("private static string Map_Cv_FcuF26Dto_Band_", result.GeneratedCode);
        Assert.Contains(") => \"v:\" + v;", result.GeneratedCode);
        Assert.Contains("global::NCMapper.Generated.GeneratedMapper.Map_Cv_FcuF26Dto_Band_", result.GeneratedCode);
        Assert.Contains("(src.Band)", result.GeneratedCode);
    }

    /// <summary>F26 保留禁令：捕获外层局部变量 → F41（仍拒——转录引擎禁令清单）。</summary>
    [Fact]
    public void F26_LambdaCapture_Rejected_F41()
    {
        var source = """
            using System;
            using NCMapper;
            public class FcuF26cEntity { public string Band { get; set; } = ""; }
            public class FcuF26cDto { public string Band { get; set; } = ""; }

            [MapperConfig]
            public class FcuF26cMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    var prefix = "v:";
                    cfg.For<FcuF26cEntity, FcuF26cDto>().Member(d => d.Band).Convert((string v) => prefix + v);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f41 = result.Diagnostics.Where(d => d.Id == "NCMAP_F41").ToList();
        Assert.Single(f41);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f41[0].Severity);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F26");
        Assert.DoesNotContain("Map_Cv_FcuF26cDto_Band_", result.GeneratedCode);
    }

    /// <summary>F26 保留禁令：语句体 lambda → F42（仍拒——转录引擎仅表达式体）。</summary>
    [Fact]
    public void F26_LambdaStatementBody_Rejected_F42()
    {
        var source = """
            using System;
            using NCMapper;
            public class FcuF26dEntity { public string Band { get; set; } = ""; }
            public class FcuF26dDto { public string Band { get; set; } = ""; }

            [MapperConfig]
            public class FcuF26dMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF26dEntity, FcuF26dDto>().Member(d => d.Band).Convert((string v) => { return "v:" + v; });
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f42 = result.Diagnostics.Where(d => d.Id == "NCMAP_F42").ToList();
        Assert.Single(f42);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f42[0].Severity);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F26");
        Assert.DoesNotContain("Map_Cv_FcuF26dDto_Band_", result.GeneratedCode);
    }

    /// <summary>F26 邻界：方法组（正常声明）→ 不误报（方法组候选回退路径）。</summary>
    [Fact]
    public void F26_MethodGroup_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcuF26bEntity { public string Band { get; set; } = ""; }
            public class FcuF26bDto { public string Band { get; set; } = ""; }
            public static class FcuF26bConv { public static string ToText(string v) => "v:" + v; }

            [MapperConfig]
            public class FcuF26bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF26bEntity, FcuF26bDto>().Member(d => d.Band).Convert<string>(FcuF26bConv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F26");
    }

    // ── F27：四形态模板均不匹配 ──────────────────────────────────

    /// <summary>F27 正例：源侧同名可达但签名模板全不匹配（TField=int vs 入参 long）→ Error + 声明丢弃。</summary>
    [Fact]
    public void F27_ShapeMismatch_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcuF27Entity { public int Band { get; set; } }
            public class FcuF27Dto { public string Band { get; set; } = ""; }
            public static class FcuF27Conv { public static string ToText(long fieldValue) => "v:" + fieldValue; }

            [MapperConfig]
            public class FcuF27Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF27Entity, FcuF27Dto>().Member(d => d.Band).Convert<string>(FcuF27Conv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f27 = result.Diagnostics.Where(d => d.Id == "NCMAP_F27").ToList();
        Assert.Single(f27);
        Assert.Contains("FcuF27Conv.ToText", f27[0].GetMessage());
        // ToDisplayString 产 C# 关键字（int/string 非 Int32/String）——断言取引号包裹段保双语 locale 无关
        Assert.Contains("TField='int'", f27[0].GetMessage());          // TField=源属性类型 int
        Assert.Contains("TSource='FcuF27Entity'", f27[0].GetMessage());
        Assert.Contains("'string'", f27[0].GetMessage());             // TTarget=锚点类型 string
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f27[0].Severity);
        Assert.DoesNotContain("FcuF27Conv.ToText", result.GeneratedCode);
    }

    /// <summary>F27 邻界：T1 形态匹配（正常声明）→ 不误报。</summary>
    [Fact]
    public void F27_T1ShapeMatch_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcuF27bEntity { public int Age { get; set; } }
            public class FcuF27bDto { public string Band { get; set; } = ""; }
            public static class FcuF27bConv { public static string ToText(int fieldValue) => "v:" + fieldValue; }

            [MapperConfig]
            public class FcuF27bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF27bEntity, FcuF27bDto>().Member(d => d.Band).From(s => s.Age).Member(d => d.Band).Convert<string>(FcuF27bConv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F27");
        result.Should().HaveMethodBodyContaining("Map_FcuF27bEntity_To_FcuF27bDto",
            "Band = global::FcuF27bConv.ToText(src.Age)");
    }

    // ── F28：锚点无 public setter（Warning，末位门）────────────────

    /// <summary>F28 正例：get-only 锚点 → Warning（物化遍历将吞掉声明——提前告知而非改门）。</summary>
    [Fact]
    public void F28_GetOnlyAnchor_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class FcuF28Entity { public string Band { get; set; } = ""; }
            public class FcuF28Dto { public string Band { get; } = ""; }
            public static class FcuF28Conv { public static string ToText(string fieldValue) => "v:" + fieldValue; }

            [MapperConfig]
            public class FcuF28Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF28Entity, FcuF28Dto>().Member(d => d.Band).Convert<string>(FcuF28Conv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f28 = result.Diagnostics.Where(d => d.Id == "NCMAP_F28").ToList();
        Assert.Single(f28);
        Assert.Contains("Band", f28[0].GetMessage());
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f28[0].Severity);
    }

    /// <summary>F28 邻界：public setter 锚点（正常声明）→ 不误报。</summary>
    [Fact]
    public void F28_SettableAnchor_NoFalsePositive()
    {
        var source = """
            using NCMapper;
            public class FcuF28bEntity { public string Band { get; set; } = ""; }
            public class FcuF28bDto { public string Band { get; set; } = ""; }
            public static class FcuF28bConv { public static string ToText(string fieldValue) => "v:" + fieldValue; }

            [MapperConfig]
            public class FcuF28bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuF28bEntity, FcuF28bDto>().Member(d => d.Band).Convert<string>(FcuF28bConv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F28");
    }

    // ── CS0121：converter 方法组多重载歧义（编译期红——解歧指引）────

    /// <summary>
    /// CS0121 编译验证：converter 方法组双载（M(int)/M(string)）+ 无显式类型参 → CS0121 编译错误
    ///（TestHelper.BuildCompilation 不校验编译诊断——此处断言生成器诊断面：链上 CS0121 导致
    /// invocation 绑定失败 → 方法组不可达 → F26 无诊断零声明（D-2 静默整链跳过——编译器已报）。
    /// 解歧手段：显式委托转换（XML 注释 §T1 重载解歧指引）。
    /// </summary>
    [Fact]
    public void CS0121_MultiOverloadMethodGroup_FallsBackToCompilerError()
    {
        var source = """
            using System;
            using NCMapper;
            public class FcuAmbEntity { public int Age { get; set; } }
            public class FcuAmbDto { public string Band { get; set; } = ""; }
            public static class FcuAmbConv
            {
                public static string ToText(int fieldValue) => "v:" + fieldValue;
                public static string ToText(string fieldValue) => "s:" + fieldValue;
            }

            [MapperConfig]
            public class FcuAmbMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuAmbEntity, FcuAmbDto>().Member(d => d.Band).Convert(FcuAmbConv.ToText);
                }
            }
            """;
        // 生成器视角：CS0121 编译期红（用户侧先见）——多候选场景缺陷态 SingleOrDefault 崩溃
        //（CS8785 静默），修复后（B2-01）改为显式拒绝：F26 落位 + 声明不生成
        //（断言口径回归——原断言"零诊断整链跳过"编码的正是崩溃缺陷形态，批次一 TG-P-4 同款）
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F26");
        Assert.DoesNotContain("FcuAmbConv.ToText", result.GeneratedCode);
    }

    /// <summary>
    /// CS0121 解歧（显式委托转换）：同上多载方法组 + (Func&lt;int,string&gt;) 显式转换 →
    /// 编译通过 + T1 形态正常生成（XML 注释解歧指引的可执行验证）。
    /// </summary>
    [Fact]
    public void CS0121_ResolvedByExplicitDelegateCast()
    {
        var source = """
            using System;
            using NCMapper;
            public class FcuAmb2Entity { public int Age { get; set; } }
            public class FcuAmb2Dto { public string Band { get; set; } = ""; }
            public static class FcuAmb2Conv
            {
                public static string ToText(int fieldValue) => "v:" + fieldValue;
                public static string ToText(string fieldValue) => "s:" + fieldValue;
            }

            [MapperConfig]
            public class FcuAmb2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuAmb2Entity, FcuAmb2Dto>()
                       .Member(d => d.Band).From(s => s.Age)
                       .Member(d => d.Band).Convert((Func<int, string>)FcuAmb2Conv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F26" || d.Id == "NCMAP_F27");
        result.Should().HaveMethodBodyContaining("Map_FcuAmb2Entity_To_FcuAmb2Dto",
            "Band = global::FcuAmb2Conv.ToText(src.Age)");
    }

    // ── B2-01：SingleOrDefault 多候选防崩（审计批次二）──

    /// <summary>
    /// B2-01 Convert 路径：多候选方法组（无显式类型参 → CS0121 编译红）→ 生成器不崩（零 CS8785）
    /// + F26 显式诊断落位 + 该声明零生成码（缺陷态 SingleOrDefault 抛 InvalidOperationException
    /// → 生成器崩溃以 CS8785 呈现 → 红）。
    /// </summary>
    [Fact]
    public void MultiOverloadConvert_NoCrash_F26Emitted_NoCode()
    {
        var source = """
            using NCMapper;
            public class FcuAmb3Entity { public int Age { get; set; } }
            public class FcuAmb3Dto { public string Band { get; set; } = ""; }
            public static class FcuAmb3Conv
            {
                public static string ToText(int fieldValue) => "v:" + fieldValue;
                public static string ToText(string fieldValue) => "s:" + fieldValue;
            }

            [MapperConfig]
            public class FcuAmb3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FcuAmb3Entity, FcuAmb3Dto>().Member(d => d.Band).Convert(FcuAmb3Conv.ToText);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 生成器不崩（缺陷态 CS8785 → 红）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS8785");

        // F26 显式诊断（多候选显式拒绝——新口径；缺陷态零诊断 → 红）
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F26");

        // 该声明不生成代码
        Assert.DoesNotContain("FcuAmb3Conv.ToText", result.GeneratedCode);
    }
}
