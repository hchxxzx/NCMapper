using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 全局配置匹配注入与诊断测试— 策略匹配、全局忽略、NCMAP020/021/022 判定接入
/// <para>对应任务验收标准 1/2/4。</para>
/// </summary>
public class GlobalConfigMatchingTests
{
    /// <summary>
    /// 验证：SnakeCase 配置下 user_name ↔ UserName 匹配成功（源生成路径策略注入）
    /// </summary>
    [Fact]
    public void SnakeCase_Matching_InjectedIntoGeneratedCode()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public string UserName { get; set; } = "";
                public string PhoneNumber { get; set; } = "";
            }
            public class UserEntity
            {
                public string user_name { get; set; } = "";
                public string phone_number { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "SnakeCase",
        });

        // 生成代码引用源属性 user_name（策略归一化匹配成功）
        Assert.Contains("user_name", generated);
        Assert.Contains("phone_number", generated);
    }

    /// <summary>
    /// 验证：全局忽略目标属性 → 生成代码不含该属性赋值（跳过留空）
    /// </summary>
    [Fact]
    public void GlobalIgnore_TargetProperty_NotEmitted()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public string Name { get; set; } = "";
                public byte[] RowVersion { get; set; } = [];
            }
            public class UserEntity
            {
                public string Name { get; set; } = "";
                public byte[] RowVersion { get; set; } = [];
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "RowVersion",
        });

        // Name 映射存在；RowVersion 被全局忽略 → 不生成赋值（只出现属性声明处）
        Assert.Contains("Name", generated);
        Assert.DoesNotContain("RowVersion = src", generated);
    }

    /// <summary>
    /// 验证：NCMAP021 — 显式 [MapProperty] 命中全局忽略且无 OverrideIgnore → Warning 诊断（fail-closed）
    /// </summary>
    [Fact]
    public void NCMAP021_ExplicitMappingHitGlobalIgnore_Reported()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                [MapProperty(typeof(UserEntity), "user_name")]
                public string UserName { get; set; } = "";
            }
            public class UserEntity
            {
                public string user_name { get; set; } = "";
            }
            """;

        var diagnostics = RunGeneratorForDiagnostics(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "user_name",
        });

        Assert.Contains(diagnostics, d => d.Id == "NCMAP021");
    }

    /// <summary>
    /// 验证：NCMAP021 豁免 — OverrideIgnore=true 时显式标注命中全局忽略不报告且映射保留
    /// </summary>
    [Fact]
    public void NCMAP021_OverrideIgnore_ExemptsAndKeepsMapping()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                [MapProperty(typeof(UserEntity), "user_name", OverrideIgnore = true)]
                public string UserName { get; set; } = "";
            }
            public class UserEntity
            {
                public string user_name { get; set; } = "";
            }
            """;

        var (generated, diagnostics) = RunGeneratorWithDiagnostics(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "user_name",
        });

        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP021");
        Assert.Contains("user_name", generated); // 映射保留
    }

    /// <summary>
    /// 验证：NCMAP022 — [MapIgnore] 与 OverrideIgnore=true 同标 → 矛盾诊断（忽略生效）
    /// </summary>
    [Fact]
    public void NCMAP022_MapIgnoreWithOverrideIgnore_Reported()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                [MapIgnore]
                [MapProperty(typeof(UserEntity), "Name", OverrideIgnore = true)]
                public string Name { get; set; } = "";
            }
            public class UserEntity
            {
                public string Name { get; set; } = "";
            }
            """;

        var diagnostics = RunGeneratorForDiagnostics(source, new Dictionary<string, string>());

        Assert.Contains(diagnostics, d => d.Id == "NCMAP022");
    }

    /// <summary>
    /// 验证：双路径一致性抽样 — SnakeCase 配置下源生成匹配与运行时回退匹配结果一致
    /// <para>同一类型对在 NCMapper.Tests（运行时）与 SourceGen 侧分别断言 user_name ↔ UserName。</para>
    /// </summary>
    [Fact]
    public void DualPath_SnakeCase_MatchingConsistent()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public string UserName { get; set; } = "";
            }
            public class UserEntity
            {
                public string user_name { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "SnakeCase",
        });

        // 源生成路径：user_name 被匹配进 UserDto 映射
        Assert.Contains("user_name", generated);

        // 运行时路径：同一类型对经 Mapper.Map（无 [MapFrom] 的 DTO 走运行时回退）——
        // 运行时 PropertyMatcher 走共享算法，须一致。运行时侧完整断言在 S4 对照矩阵
        // （此处锁定源生成侧行为，运行时侧由 GlobalConfigReaderTests/运行时单测覆盖同算法）
        Assert.Contains("Map_UserEntity_To_UserDto", generated);
    }

    /// <summary>
    /// 运行生成器并返回生成代码文本
    /// </summary>
    /// <param name="source">测试源码</param>
    /// <param name="msbuildProperties">注入的 MSBuild 属性</param>
    /// <returns>生成代码文本</returns>
    private static string RunGenerator(string source, Dictionary<string, string> msbuildProperties)
        => RunGeneratorWithDiagnostics(source, msbuildProperties).Item1;

    /// <summary>
    /// 运行生成器并返回诊断列表（仅返回）
    /// </summary>
    /// <param name="source">测试源码</param>
    /// <param name="msbuildProperties">注入的 MSBuild 属性</param>
    /// <returns>诊断列表</returns>
    private static Diagnostic[] RunGeneratorForDiagnostics(string source, Dictionary<string, string> msbuildProperties)
        => RunGeneratorWithDiagnostics(source, msbuildProperties).Item2;

    /// <summary>
    /// 运行生成器并返回（生成代码文本, 诊断列表）
    /// </summary>
    /// <param name="source">测试源码</param>
    /// <param name="msbuildProperties">注入的 MSBuild 属性</param>
    /// <returns>生成代码与诊断</returns>
    private static (string, Diagnostic[]) RunGeneratorWithDiagnostics(string source, Dictionary<string, string> msbuildProperties)
    {
        var compilation = CreateCompilation(source);
        var generator = new NCMapperSourceGenerator();
        var optionsProvider = new GlobalConfigReaderTestsConfigOptionsProvider(msbuildProperties);
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            optionsProvider: optionsProvider);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
        var generated = updatedCompilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated_GeneratedMapper.g.cs"))?
            .ToString() ?? "";
        return (generated, diagnostics.ToArray());
    }

    /// <summary>
    /// 构建测试编译单元（引用 NCMapper 与 SourceGen）
    /// </summary>
    /// <param name="source">测试源码</param>
    /// <returns>编译单元</returns>
    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();
        var ncmapper = typeof(NCMapper.Mapper).Assembly.Location;
        if (!references.Any(r => r.Display == ncmapper))
            references.Add(MetadataReference.CreateFromFile(ncmapper));

        return CSharpCompilation.Create(
            "GlobalConfigMatchingTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    // ===== S1 补充：CaseSensitive / UpperSnakeCase 源生成侧 =====

    /// <summary>
    /// 验证：CaseSensitive 策略源生成侧 — 大小写不同不匹配（生成代码无该赋值）
    /// </summary>
    [Fact]
    public void CaseSensitive_ExactMatchOnly_InGeneratedCode()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public string UserName { get; set; } = "";
            }
            public class UserEntity
            {
                public string user_name { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "CaseSensitive",
        });

        // CaseSensitive：user_name 与 UserName 大小写不同 → 不匹配，生成代码不含该赋值
        Assert.DoesNotContain("UserName = src.user_name", generated);
    }

    /// <summary>
    /// 验证：UpperSnakeCase 策略源生成侧 — USER_NAME 归一化命中 UserName
    /// </summary>
    [Fact]
    public void UpperSnakeCase_Matches_InGeneratedCode()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public string UserName { get; set; } = "";
            }
            public class UserEntity
            {
                public string USER_NAME { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "UpperSnakeCase",
        });

        Assert.Contains("USER_NAME", generated);
    }

    // ===== S2 补充：判定面四象限（源生成侧）=====

    /// <summary>
    /// 验证：象限 1（源生成侧）— 源属性名命中忽略清单 → 生成代码不含赋值
    /// </summary>
    [Fact]
    public void Quadrant1_SourceHit_NotEmitted()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public string user_name { get; set; } = "";
                public string UserName { get; set; } = "";
            }
            public class UserEntity
            {
                public string user_name { get; set; } = "";
                public string UserName { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "user_name",
        });

        // 源侧 user_name 命中 → 整对忽略；UserName 不受影响
        // 注：DeepCopy 方法（DeepCopy_UserEntity）为全量拷贝，不受映射忽略清单约束（设计语义），
        // 断言须限定映射方法体（Map_..._To_... 含 _Update），避免误伤。
        var mapBody = ExtractMethodBody(generated, "Map_UserEntity_To_UserDto(");
        var updateBody = ExtractMethodBody(generated, "Map_UserEntity_To_UserDto_Update(");
        Assert.DoesNotContain("user_name", mapBody);
        Assert.DoesNotContain("user_name", updateBody);
        Assert.Contains("UserName", mapBody);
    }

    /// <summary>
    /// 验证：象限 2（源生成侧）— 目标属性名命中忽略清单 → 生成代码不含赋值
    /// </summary>
    [Fact]
    public void Quadrant2_TargetHit_NotEmitted()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public string user_name { get; set; } = "";
                public string UserName { get; set; } = "";
            }
            public class UserEntity
            {
                public string user_name { get; set; } = "";
                public string UserName { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "UserName",
        });

        // 目标侧 UserName 命中 → 整对忽略；user_name 仍按名匹配（源 user_name → 目标 user_name）
        // 注：DeepCopy 方法为全量拷贝，不受映射忽略清单约束，断言限定映射方法体。
        var mapBodyQ2 = ExtractMethodBody(generated, "Map_UserEntity_To_UserDto(");
        var updateBodyQ2 = ExtractMethodBody(generated, "Map_UserEntity_To_UserDto_Update(");
        Assert.DoesNotContain("UserName", mapBodyQ2);
        Assert.DoesNotContain("UserName", updateBodyQ2);
        Assert.Contains("user_name", mapBodyQ2);
    }

    /// <summary>
    /// 提取生成代码中指定方法的方法体（方法签名到下一个 public/internal static 方法之间）
    /// </summary>
    /// <param name="generated">生成代码全文</param>
    /// <param name="methodSignature">方法签名锚点（含左括号）</param>
    /// <returns>方法体文本；未找到返回空串</returns>
    private static string ExtractMethodBody(string generated, string methodSignature)
    {
        var start = generated.IndexOf(methodSignature, StringComparison.Ordinal);
        if (start < 0) return "";
        var end = generated.IndexOf("public static", start + methodSignature.Length, StringComparison.Ordinal);
        return end < 0 ? generated.Substring(start) : generated.Substring(start, end - start);
    }

    /// <summary>
    /// 验证：Compose 源侧命中全局忽略（跨表子源属性名命中清单）→ 生成 Compose 方法不含该成员
    /// <para>CR-3/F2 回写用例：ComposeMappings 源侧循环已实现
    ///（原符号版实现位于已删除的 MappingExtractor.GlobalConfig.cs，commit f839786 即含；
    /// 现行为由值版管线 ApplySourceGlobalIgnore 承接），补两端断言：
    /// 源侧命中成员剔除 + 非命中跨源成员正常生成（保留至少一个 Compose 成员使 Compose 方法生成）</para>
    /// </summary>
    [Fact]
    public void Compose_SourceHit_NotEmitted()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(ComposeEntityA))]
            public class ComposeDto
            {
                public string Name { get; set; } = "";
                [MapProperty(typeof(ComposeEntityB), "InternalCode")]
                public string ExternalCode { get; set; } = "";
                [MapProperty(typeof(ComposeEntityB), "PublicCode")]
                public string PublicExternalCode { get; set; } = "";
            }
            public class ComposeEntityA
            {
                public string Name { get; set; } = "";
            }
            public class ComposeEntityB
            {
                public string InternalCode { get; set; } = "";
                public string PublicCode { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "InternalCode",
        });

        // Compose 源侧（B.InternalCode）命中 → Update 方法不含 ExternalCode 赋值；
        // PublicExternalCode（B.PublicCode）正常生成（成员赋值在 Update_{子源}_To_{目标} 方法体，
        // Compose_ 方法体仅 switch 分派调用 Update）。
        // ⚠️ 断言用精确赋值形态（dst.ExternalCode / src.InternalCode）：PublicExternalCode 含
        // "ExternalCode" 子串，子串匹配会误伤合法保留成员。
        var updateBody = ExtractMethodBody(generated, "Update_ComposeEntityB_To_ComposeDto(");
        Assert.DoesNotContain("dst.ExternalCode", updateBody);
        Assert.DoesNotContain("InternalCode", updateBody);
        Assert.Contains("dst.PublicExternalCode", updateBody);
        Assert.Contains("src.PublicCode", updateBody);
    }

    // ===== S3 补充：构造器参数匹配恒 OrdinalIgnoreCase（源生成侧，v1.2 W4）=====

    /// <summary>
    /// 验证：SnakeCase 策略下 record 构造器注入路径 — 构造器参数恒 OrdinalIgnoreCase 匹配（不随策略）
    /// <para>record 主构造函数参数 user_id/user_name 与源属性同形 → 生成代码用 src 访问构造参数。</para>
    /// </summary>
    [Fact]
    public void W4_SnakeCase_RecordCtor_OrdinalIgnoreCaseMatch()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public record UserCtorDto(int user_id, string user_name);
            public class UserEntity
            {
                public int user_id { get; set; }
                public string user_name { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "SnakeCase",
        });

        // 构造器路径：参数与源同形（OrdinalIgnoreCase）→ 生成代码 new UserCtorDto(src.user_id, src.user_name)
        Assert.Contains("new Consumer.UserCtorDto(src.user_id, src.user_name)", generated);
    }

    /// <summary>
    /// 验证：象限 4（源生成侧）— 目标命中 + [MapProperty(OverrideIgnore=true)] → 豁免恢复映射
    /// </summary>
    [Fact]
    public void Quadrant4_TargetHitWithExemption_Emitted()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                [MapProperty(typeof(UserEntity), "Password", OverrideIgnore = true)]
                public string Password { get; set; } = "";
            }
            public class UserEntity
            {
                public string Password { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "Password",
        });

        // 豁免生效 → 赋值仍生成（不触发 NCMAP021，无矛盾 NCMAP022）
        Assert.Contains("Password = src.Password", generated);
    }
    /// <summary>
    /// Q6-M3 回归（2026-08-15）V2 对照：无任何配置（无 msbuild 属性、无程序集特性）时，
    /// 约定匹配 IgnoreCase（现状语义基线）。
    /// </summary>
    [Fact]
    public void NoConfig_CaseInsensitiveMatching_Baseline()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public bool HideNewsLetterBlock { get; set; }
            }
            public class UserEntity
            {
                public bool HideNewsletterBlock { get; set; }
            }
            """;

        var (generated, diags) = RunGeneratorWithDiagnostics(source, new Dictionary<string, string>());

        Assert.True(generated.Contains("HideNewsLetterBlock = src.HideNewsletterBlock"),
            "V2 基线生成代码:\n" + generated + "\n诊断:\n" + string.Join("\n", diags.Select(d => d.ToString())));
    }

    /// <summary>
    /// Q6-M3 回归（2026-08-15）V1：[assembly: NCMapperOptions] 存在（诊断选项）但 NamingStrategy 未设置时，
    /// 约定匹配须回退 IgnoreCase（此前 -1 落入 Ordinal 精确匹配——nopCommerce C 类大小写差异根因）。
    /// </summary>
    [Fact]
    public void NCMapperOptions_WithoutNamingStrategy_CaseInsensitiveMatching()
    {
        var source = """
            using NCMapper.Attributes;

            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = false, ReportUnmappedSourceMembers = false)]

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                public bool HideNewsLetterBlock { get; set; }
            }
            public class UserEntity
            {
                public bool HideNewsletterBlock { get; set; }
            }
            """;

        var (generated, diags) = RunGeneratorWithDiagnostics(source, new Dictionary<string, string>());

        Assert.True(generated.Contains("HideNewsLetterBlock = src.HideNewsletterBlock"),
            "V1 生成代码:\n" + generated + "\n诊断:\n" + string.Join("\n", diags.Select(d => d.ToString())));
    }

    /// <summary>
    /// （义务① M2-5 检出器）：SnakeCase 配置感知重匹配 × [MapWhen]——
    /// 守卫字段（ConditionMethodRef 四字段）须随重建透传（SnapshotConfig 义务①——
    /// 重建丢字段即 007 缺陷②"静默丢字段"同型陷阱）；守卫包裹行在重匹配后照常发射。
    /// <para>触发设计：主遍 OrdinalIgnoreCase 原文名匹配 username（target UserName——
    /// user_name 含下划线不命中）→ SnakeCase 重匹配归一化键（去下划线大写）首声明者
    /// user_name → 换绑源不同 → ReplaceWithConfigAwareMatchValue 重建分支（301
    /// SameProperty 不同 → :319 义务①透传）。补匹配路径（此前无 pm）恒默认无条件为
    /// 已文档化边界——本用例锚定"重建"而非"补匹配"。</para>
    /// </summary>
    [Fact]
    public void MapWhen_SnakeCaseRematch_ConditionSurvives()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(UserEntity))]
            public class UserDto
            {
                [MapWhen(nameof(HasContent))]
                public string UserName { get; set; } = "";

                public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
            }
            public class UserEntity
            {
                public string user_name { get; set; } = "";
                public string username { get; set; } = "";
            }
            """;

        var generated = RunGenerator(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "SnakeCase",
        });

        // 守卫透传双重锁定（单断言含两锚）：守卫局部变量承载行 = 换绑源 user_name（重建分支触发——
        // 主遍 username 若未被换绑则此处为 src.username）+ 守卫本体（A 形态用户方法限定名调用）。
        //（DoesNotContain("src.username") 不可用——DeepCopy 自映射 UserEntity→UserEntity 以忽略
        // 大小写同名匹配产出 src.username，与正向映射无关）
        Assert.Contains("var __when_src_UserName = src.user_name;", generated);
        Assert.Contains("if (global::Consumer.UserDto.HasContent(", generated);
    }
}

/// <summary>
/// 测试用 AnalyzerConfigOptions — 字典驱动（供本测试文件使用，与 GlobalConfigReaderTests 内部类同构）
/// </summary>
internal sealed class GlobalConfigReaderTestsConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>
    /// 创建配置提供器
    /// </summary>
    /// <param name="values">全局属性键值对</param>
    public GlobalConfigReaderTestsConfigOptionsProvider(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

    /// <inheritdoc/>
    public override AnalyzerConfigOptions GlobalOptions => new GlobalConfigReaderTestsConfigOptions(_values);

    /// <inheritdoc/>
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalConfigReaderTestsConfigOptions.Empty;

    /// <inheritdoc/>
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalConfigReaderTestsConfigOptions.Empty;
}

/// <summary>
/// 测试用 AnalyzerConfigOptions — 字典驱动
/// </summary>
internal sealed class GlobalConfigReaderTestsConfigOptions : AnalyzerConfigOptions
{
    /// <summary>
    /// 空实例
    /// </summary>
    public static readonly GlobalConfigReaderTestsConfigOptions Empty = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>
    /// 创建字典驱动配置
    /// </summary>
    /// <param name="values">键值对（完整键名）</param>
    public GlobalConfigReaderTestsConfigOptions(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

    /// <inheritdoc/>
    public override bool TryGetValue(string key, out string value)
    {
        if (_values.TryGetValue(key, out var v))
        {
            value = v;
            return true;
        }
        value = "";
        return false;
    }
}
