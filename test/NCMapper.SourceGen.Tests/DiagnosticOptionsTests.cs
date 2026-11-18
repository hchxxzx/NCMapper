using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// NCMapperOptions 双开关测试
/// <para>覆盖：默认行为（NCMAP001 不报/NCMAP007 报）、4 组合策略矩阵、类级&gt;程序集级&gt;默认优先级、
/// 部分标注（按属性取特性默认值）、[MapProperty] 显式错误保真（不受开关影响）。</para>
/// </summary>
public class DiagnosticOptionsTests
{
    /// <summary>
    /// 编译源码并获取 NCMapper 源生成器诊断
    /// </summary>
    /// <param name="source">测试源码</param>
    /// <returns>编译单元与诊断列表</returns>
    private static async Task<(Compilation, ImmutableArray<Diagnostic>)> CompileAndGetDiagnostics(string source)
    {
        // 引用 NCMapper 程序集
        var ncmapperAssembly = typeof(NCMapper.Mapper).Assembly.Location;

        // 收集所有必要的引用（与 DiagnosticTests 同款：核心运行时程序集齐全，防 CS0012）
        var refAssemblies = new[]
        {
            typeof(object).Assembly.Location,
            ncmapperAssembly,
            typeof(System.Runtime.CompilerServices.MethodImplAttribute).Assembly.Location,
        };

        // 添加运行时目录中的核心程序集
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var coreAssemblies = new[]
        {
            System.IO.Path.Combine(runtimeDir, "System.Runtime.dll"),
            System.IO.Path.Combine(runtimeDir, "System.Collections.dll"),
            System.IO.Path.Combine(runtimeDir, "System.Linq.dll"),
            System.IO.Path.Combine(runtimeDir, "System.Linq.Expressions.dll"),
        };

        var allRefs = refAssemblies.Concat(coreAssemblies)
            .Where(System.IO.File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "DiagnosticOptionsTests",
            new[] { CSharpSyntaxTree.ParseText(source) },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new NCMapperSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        return (updatedCompilation, diagnostics);
    }

    private const string TargetMissingSource = """
        using NCMapper.Attributes;

        public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

        [MapFrom(typeof(SourceEntity))]
        public class TargetDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string UnmappedProp { get; set; } = "";
        }
        """;

    private const string SourceUnmapped = """
        using NCMapper.Attributes;

        public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; public string ExtraProp { get; set; } = ""; }

        [MapFrom(typeof(SourceEntity))]
        public class TargetDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }
        """;

    /// <summary>
    /// 默认行为（无标注）：NCMAP001（目标无源）不报
    /// </summary>
    [Fact]
    public async Task Default_NoAttribute_NCMAP001_NotReported()
    {
        var (_, diagnostics) = await CompileAndGetDiagnostics(TargetMissingSource);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP001");
    }

    /// <summary>
    /// 默认行为（无标注）：NCMAP007（源未映射）报
    /// </summary>
    [Fact]
    public async Task Default_NoAttribute_NCMAP007_Reported()
    {
        var (_, diagnostics) = await CompileAndGetDiagnostics(SourceUnmapped);
        Assert.Contains(diagnostics, d => d.Id == "NCMAP007");
    }

    /// <summary>
    /// None 语义（assembly false,false）：NCMAP001/NCMAP007 均不报
    /// </summary>
    [Fact]
    public async Task Assembly_None_BothSuppressed()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = false, ReportUnmappedSourceMembers = false)]

            public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; public string ExtraProp { get; set; } = ""; }

            [MapFrom(typeof(SourceEntity))]
            public class TargetDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string UnmappedProp { get; set; } = "";
            }
            """;
        var (_, diagnostics) = await CompileAndGetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP001");
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP007");
    }

    /// <summary>
    /// Both 语义（assembly true,true）：NCMAP001/NCMAP007 均报
    /// </summary>
    [Fact]
    public async Task Assembly_Both_AllReported()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = true, ReportUnmappedSourceMembers = true)]

            public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; public string ExtraProp { get; set; } = ""; }

            [MapFrom(typeof(SourceEntity))]
            public class TargetDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string UnmappedProp { get; set; } = "";
            }
            """;
        var (_, diagnostics) = await CompileAndGetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "NCMAP001");
        Assert.Contains(diagnostics, d => d.Id == "NCMAP007");
    }

    /// <summary>
    /// Target 变体（true,false）：NCMAP001 报、NCMAP007 不报
    /// </summary>
    [Fact]
    public async Task Assembly_TargetOnly_NCMAP001Reported()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = true, ReportUnmappedSourceMembers = false)]

            public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; public string ExtraProp { get; set; } = ""; }

            [MapFrom(typeof(SourceEntity))]
            public class TargetDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string UnmappedProp { get; set; } = "";
            }
            """;
        var (_, diagnostics) = await CompileAndGetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "NCMAP001");
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP007");
    }

    /// <summary>
    /// Source 变体（false,true，即默认）：NCMAP001 不报、NCMAP007 报
    /// </summary>
    [Fact]
    public async Task Assembly_SourceOnly_NCMAP007Reported()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = false, ReportUnmappedSourceMembers = true)]

            public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; public string ExtraProp { get; set; } = ""; }

            [MapFrom(typeof(SourceEntity))]
            public class TargetDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string UnmappedProp { get; set; } = "";
            }
            """;
        var (_, diagnostics) = await CompileAndGetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP001");
        Assert.Contains(diagnostics, d => d.Id == "NCMAP007");
    }

    /// <summary>
    /// 优先级：类级覆盖程序集级（assembly true,true + 类级 ReportUnmappedSourceMembers=false → NCMAP007 不报）
    /// </summary>
    [Fact]
    public async Task ClassLevel_Overrides_Assembly()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = true, ReportUnmappedSourceMembers = true)]

            public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; public string ExtraProp { get; set; } = ""; }

            [NCMapperOptions(ReportUnmappedSourceMembers = false)]
            [MapFrom(typeof(SourceEntity))]
            public class TargetDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string UnmappedProp { get; set; } = "";
            }
            """;
        var (_, diagnostics) = await CompileAndGetDiagnostics(source);
        // 类级覆盖：NCMAP007 不报；目标侧未显式设置 → 特性默认 false → NCMAP001 不报（类级整体覆盖语义）
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP007");
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP001");
    }

    /// <summary>
    /// 部分标注：仅设 ReportUnmappedSourceMembers=false，目标侧沿用特性默认 false → 均不报
    /// </summary>
    [Fact]
    public async Task PartialAttribute_UsesPerPropertyDefaults()
    {
        var source = """
            using NCMapper.Attributes;

            public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; public string ExtraProp { get; set; } = ""; }

            [NCMapperOptions(ReportUnmappedSourceMembers = false)]
            [MapFrom(typeof(SourceEntity))]
            public class TargetDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string UnmappedProp { get; set; } = "";
            }
            """;
        var (_, diagnostics) = await CompileAndGetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP001");
        Assert.DoesNotContain(diagnostics, d => d.Id == "NCMAP007");
    }

    /// <summary>
    /// 保真：[MapProperty] 显式 FromField 错误在 None 语义（false,false）下仍报 NCMAP001
    /// </summary>
    [Fact]
    public async Task MapPropertyExplicitError_StillWarns_WhenSuppressed()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = false, ReportUnmappedSourceMembers = false)]

            public class SourceEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

            [MapFrom(typeof(SourceEntity))]
            public class TargetDto
            {
                [MapProperty(typeof(SourceEntity), "NonExistentField")]
                public string SomeProp { get; set; } = "";
            }
            """;
        var (_, diagnostics) = await CompileAndGetDiagnostics(source);
        // fieldAttr != null（[MapProperty] 显式）→ 始终报（保真，不受开关影响）
        Assert.Contains(diagnostics, d => d.Id == "NCMAP001");
    }
}
