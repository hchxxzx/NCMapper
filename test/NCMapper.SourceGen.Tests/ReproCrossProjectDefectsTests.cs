using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 跨项目真实场景缺陷复现测试（Q6 取证，评审取证先行）
/// <para>目的：用编译断言实测 CS0266（类型不兼容同名属性）/ CS0122（不可访问成员）/ CS8625（NRT 裸 null）
/// 在当前源码下是否真实存在、以何种形态存在——先取证再修复，不凭走读臆断。</para>
/// <para>注意：本文件为"取证测试"，断言代表期望的正确行为（AutoMapper 语义对齐）；
/// 若当前生成器行为不符，测试失败即证实缺陷存在。</para>
/// </summary>
public class ReproCrossProjectDefectsTests
{
    /// <summary>
    /// 编译并运行生成器，返回更新后的编译单元与生成代码
    /// </summary>
    /// <param name="source">测试源码</param>
    /// <param name="nullable">是否启用 NRT（#nullable enable + NullableContextOptions.Enable）</param>
    /// <param name="treatWarningsAsErrors">是否将警告升级为错误（CS8625 在 NRT 下是 warning 级）</param>
    /// <returns>更新后编译单元与生成代码文本</returns>
    private static (Compilation Updated, string GeneratedCode) RunGenerator(
        string source, bool nullable = false, bool treatWarningsAsErrors = false)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();
        var ncmapper = typeof(NCMapper.Mapper).Assembly.Location;
        if (!references.Any(r => r.Display == ncmapper))
            references.Add(MetadataReference.CreateFromFile(ncmapper));

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        if (treatWarningsAsErrors)
            options = options.WithGeneralDiagnosticOption(ReportDiagnostic.Error);

        var compilation = CSharpCompilation.Create("Repro", new[] { tree }, references, options);
        var driver = CSharpGeneratorDriver.Create(new[] { new NCMapperSourceGenerator().AsSourceGenerator() });
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        var genCode = updated.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated", StringComparison.OrdinalIgnoreCase))?
            .ToString() ?? "";
        return (updated, genCode);
    }

    /// <summary>
    /// 提取编译错误列表（用于断言输出）
    /// </summary>
    private static List<string> GetErrorTexts(Compilation compilation)
        => compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

    // ============ CS0266：同名属性类型不兼容（lin-cms ExtraProperties 同构） ============

    /// <summary>
    /// 复现 1：Dictionary 子类 ↔ string 同名属性（lin-cms ExtraProperties 同构）
    /// <para>期望（AutoMapper 语义）：生成代码可编译 + 不生成该属性赋值（跳过）。</para>
    /// </summary>
    [Fact]
    public void Repro1_DictionarySubclass_Vs_String_SameName()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper.Attributes;

            namespace Consumer;

            public class ExtraProperties : Dictionary<string, string>
            {
            }

            public class LinUserIdentity
            {
                public string IdentityType { get; set; } = "";
                public ExtraProperties ExtraProperties { get; set; } = new();
            }

            [MapFrom(typeof(LinUserIdentity))]
            public class UserIdentityDto
            {
                public string IdentityType { get; set; } = "";
                public string ExtraProperties { get; set; } = "";
            }
            """;

        var (updated, genCode) = RunGenerator(source);

        // 断言 1：生成代码可编译（无 error）——若失败，CS0266 在当前源码下属实
        var errors = GetErrorTexts(updated);
        Assert.True(errors.Count == 0,
            "生成代码不应有编译错误（CS0266 取证）: \n" + string.Join("\n", errors));

        // 断言 2：Map_ 主方法体内不含该属性赋值（跳过语义）——若失败，语义分叉（未跳过）
        // 限定主方法体，DeepCopy_/Update_ 方法天然含合法赋值（DeepCopy 修复后亦合法），
        // 全文本断言会产生误导性失败
        var marker = "Map_LinUserIdentity_To_UserIdentityDto(Consumer.LinUserIdentity src)";
        var methodStart = genCode.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "应找到 Map 主方法签名: \n" + genCode);
        var bodyStart = genCode.IndexOf('{', methodStart);
        var bodyEnd = genCode.IndexOf('}', bodyStart);
        var mapBody = genCode.Substring(bodyStart, bodyEnd - bodyStart);
        Assert.False(mapBody.Contains("dst.ExtraProperties", StringComparison.Ordinal),
            "类型不兼容同名属性应在 Map 主方法中被跳过（对齐 AutoMapper），生成代码不应含赋值: \n" + mapBody);
    }

    /// <summary>
    /// 复现 1b：同一场景在 NRT + TreatWarningsAsErrors 下的编译形态（裸 null 是否触发 CS8625）
    /// <para>若生成 throw 表达式（`src == null ? null : throw`），裸 null 赋不可空引用类型 → CS8625。</para>
    /// </summary>
    [Fact]
    public void Repro1b_DictionarySubclass_Vs_String_Nrt()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using NCMapper.Attributes;

            namespace Consumer;

            public class ExtraProperties : Dictionary<string, string>
            {
            }

            public class LinUserIdentity
            {
                public string IdentityType { get; set; } = "";
                public ExtraProperties ExtraProperties { get; set; } = new();
            }

            [MapFrom(typeof(LinUserIdentity))]
            public class UserIdentityDto
            {
                public string IdentityType { get; set; } = "";
                public string ExtraProperties { get; set; } = "";
            }
            """;

        var (updated, genCode) = RunGenerator(source, nullable: true, treatWarningsAsErrors: true);
        var errors = GetErrorTexts(updated);
        Assert.True(errors.Count == 0,
            "NRT + TreatWarningsAsErrors 下生成代码不应有编译错误（CS0266/CS8625 取证）: \n"
            + string.Join("\n", errors) + "\n生成代码: \n" + genCode);
    }

    // ============ CS0122：生成代码访问不可访问成员（CleanArchitecture internals 同构） ============

    /// <summary>
    /// 复现 2：源类型 internal 成员 + 跨程序集（DTO 在 Consumer，实体在 LibA）
    /// <para>期望：生成代码可编译（仅访问 public）——若生成器访问 src.InternalNote，跨程序集 → CS0122。</para>
    /// </summary>
    [Fact]
    public void Repro2_InternalMember_CrossAssembly()
    {
        // 程序集 A：实体含 internal 成员
        var libSource = """
            namespace LibA;

            public class OrderEntity
            {
                public int Id { get; set; }
                internal string InternalNote { get; set; } = "";
            }
            """;
        var libTree = CSharpSyntaxTree.ParseText(libSource, cancellationToken: TestContext.Current.CancellationToken);
        var baseRefs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();
        var ncmapper = typeof(NCMapper.Mapper).Assembly.Location;
        if (!baseRefs.Any(r => r.Display == ncmapper))
            baseRefs.Add(MetadataReference.CreateFromFile(ncmapper));
        var libComp = CSharpCompilation.Create("LibA", new[] { libTree }, baseRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 程序集 B：DTO 引用 LibA
        var appSource = """
            using NCMapper.Attributes;
            using LibA;

            namespace Consumer;

            [MapFrom(typeof(OrderEntity))]
            public class OrderDto
            {
                public int Id { get; set; }
                public string InternalNote { get; set; } = "";
            }
            """;
        var appTree = CSharpSyntaxTree.ParseText(appSource, cancellationToken: TestContext.Current.CancellationToken);
        var appRefs = baseRefs.ToList();
        appRefs.Add(libComp.ToMetadataReference());
        var appComp = CSharpCompilation.Create("Consumer", new[] { appTree }, appRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new[] { new NCMapperSourceGenerator().AsSourceGenerator() });
        driver.RunGeneratorsAndUpdateCompilation(appComp, out var updated, out _, TestContext.Current.CancellationToken);

        var errors = updated.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0,
            "跨程序集 internal 成员不应产生编译错误（CS0122 取证）: \n" + string.Join("\n", errors));
    }

    /// <summary>
    /// 复现 2b：同程序集 internal setter 目标属性
    /// <para>目标 setter 非 public 场景（机制 A 已标注 [MapIgnore]，此处验证生成侧不直接访问 internal setter）。</para>
    /// </summary>
    [Fact]
    public void Repro2b_InternalSetter_Target()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;

            public class OrderEntity
            {
                public int Id { get; set; }
                public string Note { get; set; } = "";
            }

            [MapFrom(typeof(OrderEntity))]
            public class OrderDto
            {
                public int Id { get; set; }
                public string Note { get; internal set; } = "";
            }
            """;

        var (updated, genCode) = RunGenerator(source);
        var errors = GetErrorTexts(updated);
        Assert.True(errors.Count == 0,
            "internal setter 目标属性不应产生编译错误: \n" + string.Join("\n", errors)
            + "\n生成代码: \n" + genCode);
    }

    // ============ CS8625：NRT 裸 null 赋不可空引用类型（CleanArchitecture 同构） ============

    /// <summary>
    /// 复现 3：List→Dictionary 不兼容路径在 NRT + TreatWarningsAsErrors 下（裸 null）
    /// <para>生成 `src.Tags == null ? null : throw ...`：裸 null 赋 Dictionary（不可空引用类型）→ CS8625。</para>
    /// </summary>
    [Fact]
    public void Repro3_ListToDict_NullableEnable_NullLiteral()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using NCMapper.Attributes;

            namespace Consumer;

            public class TodoEntity
            {
                public List<string> Tags { get; set; } = new();
            }

            [MapFrom(typeof(TodoEntity))]
            public class TodoDto
            {
                public Dictionary<string, string> Tags { get; set; } = new();
            }
            """;

        var (updated, genCode) = RunGenerator(source, nullable: true, treatWarningsAsErrors: true);
        var errors = GetErrorTexts(updated);
        Assert.True(errors.Count == 0,
            "NRT + TreatWarningsAsErrors 下生成代码不应有编译错误（CS8625 取证）: \n"
            + string.Join("\n", errors) + "\n生成代码: \n" + genCode);
    }

    /// <summary>
    /// 复现 3b：集合元素裸 null（MapList 生成 `list.Add(null!)`）在 NRT 下的形态
    /// </summary>
    [Fact]
    public void Repro3b_MapList_NullableEnable()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using NCMapper.Attributes;

            namespace Consumer;

            public class ItemEntity
            {
                public string? Name { get; set; }
            }

            [MapFrom(typeof(ItemEntity))]
            public class ItemDto
            {
                public string? Name { get; set; }
            }

            public class HolderEntity
            {
                public List<ItemEntity> Items { get; set; } = new();
            }

            [MapFrom(typeof(HolderEntity))]
            public class HolderDto
            {
                public List<ItemDto> Items { get; set; } = new();
            }
            """;

        var (updated, genCode) = RunGenerator(source, nullable: true, treatWarningsAsErrors: true);
        var errors = GetErrorTexts(updated);
        Assert.True(errors.Count == 0,
            "NRT 下 MapList 集合映射不应有编译错误: \n" + string.Join("\n", errors)
            + "\n生成代码: \n" + genCode);
    }
}
