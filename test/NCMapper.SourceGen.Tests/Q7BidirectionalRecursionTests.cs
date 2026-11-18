using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// Q7 复现测试——双向导航（TodoList↔TodoItem）生成代码递归定位
/// <para>CleanArchitecture 实证：映射 TodoList→TodoListDto 时 StackOverflow（4776 次）。
/// 本测试 dump 生成方法体，定位递归链根因（单编译单元 + 跨程序集两种形态）。</para>
/// </summary>
public class Q7BidirectionalRecursionTests
{
    /// <summary>
    /// 最小复现（单编译单元）：实体↔项双向导航，DTO 侧 Items 为接口集合
    /// <para>对照组：单单元形态（所有类型同一编译）下生成代码形态——确认递归是否仅在跨程序集出现。</para>
    /// </summary>
    [Fact]
    public void Dump_BidirectionalNavigation_SingleAssembly()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper.Attributes;
            public class Q7Entity
            {
                public int Id { get; set; }
                public string Title { get; set; }
                public List<Q7Item> Items { get; set; } = new();
            }
            public class Q7Item
            {
                public int Id { get; set; }
                public string Title { get; set; }
                public Q7Entity List { get; set; }
            }
            [MapFrom(typeof(Q7Entity))]
            public class Q7Dto
            {
                public int Id { get; set; }
                public string Title { get; set; }
                public IReadOnlyCollection<Q7ItemDto> Items { get; set; }
            }
            [MapFrom(typeof(Q7Item))]
            public class Q7ItemDto
            {
                public int Id { get; set; }
                public string Title { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);
        var path = Path.Combine(Path.GetTempPath(), "q7-single.txt");
        Dump(result, path);

        var body = result.GetMethod("Map_Q7Entity_To_Q7Dto")?.Body ?? "";
        // 单单元形态：Items 走注册对委托（MapList_Q7Item_To_Q7ItemDto），不应出现自递归
        Assert.Contains("MapList_Q7Item_To_Q7ItemDto", body);
    }

    /// <summary>
    /// 跨程序集复现（CleanArch 真实结构）：Domain 实体（LibA）+ Application DTO（LibB，[MapFrom] 指向 LibA 类型）
    /// <para>CleanArch 实证 StackOverflow 的形态——生成器跑 LibB 编译时，实体类型为外部程序集引用。</para>
    /// </summary>
    [Fact]
    public void Dump_BidirectionalNavigation_CrossAssembly()
    {
        // LibA：Domain 实体（TodoList/TodoItem/Colour 枚举/BaseEntity 基类）
        var libASource = """
            using System.Collections.Generic;
            namespace CleanArchitecture.Domain.Entities;
            public abstract class BaseEntity { public int Id { get; set; } }
            public enum Colour { White = 0, Red = 1, Grey = 2 }
            public class TodoList : BaseEntity
            {
                public string? Title { get; set; }
                public Colour Colour { get; set; } = Colour.Grey;
                public IList<TodoItem> Items { get; private set; } = new List<TodoItem>();
            }
            public class TodoItem : BaseEntity
            {
                public string? Title { get; set; }
                public TodoList List { get; set; } = null!;
                public int Priority { get; set; }
            }
            """;

        // LibB：Application DTO（[MapFrom] 指向 LibA 类型）
        var libBSource = """
            using NCMapper.Attributes;
            using CleanArchitecture.Domain.Entities;
            namespace CleanArchitecture.Application.TodoLists.Queries.GetTodos;
            [MapFrom(typeof(TodoList))]
            public class TodoListDto
            {
                public TodoListDto() { Items = new System.Collections.Generic.List<TodoItemDto>(); }
                [MapIgnore(Direction.ToDto)]
                public int Id { get; init; }
                public string? Title { get; init; }
                public string? Colour { get; init; }
                public System.Collections.Generic.IReadOnlyCollection<TodoItemDto> Items { get; init; }
            }
            [MapFrom(typeof(TodoItem))]
            public class TodoItemDto
            {
                [MapIgnore(Direction.ToDto)]
                public int Id { get; init; }
                public int ListId { get; init; }
                public string? Title { get; init; }
                public bool Done { get; init; }
                public int Priority { get; init; }
                public string? Note { get; init; }
            }
            """;

        // 编译 LibA → 引用集
        var references = SystemReferences();
        references.Add(CompileLib(libASource, "CleanArchitecture.Domain", references));

        // 编译 LibB（引用 LibA）+ 跑生成器 → dump 生成代码
        var libBTree = CSharpSyntaxTree.ParseText(libBSource, cancellationToken: TestContext.Current.CancellationToken);
        var libBCompilation = CSharpCompilation.Create(
            "CleanArchitecture.Application",
            new[] { libBTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new NCMapperSourceGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            libBCompilation, out var outputCompilation, out _, TestContext.Current.CancellationToken);

        var runResult = driver.GetRunResult();
        var generatedText = string.Join("\n\n",
            runResult.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

        var path = Path.Combine(Path.GetTempPath(), "q7-cross.txt");
        System.IO.File.WriteAllText(path, generatedText, System.Text.Encoding.UTF8);

        // 根因断言：修复前 MapList 的 init-only 运行时回退生成 `Mapper.Map<TodoList, TodoListDto>`（自递归链起点），
        // 修复后核心映射完整（对象初始化器）→ 无回退调用。精确匹配运行时回退（非 _WithAction/MapList 正常委托调用）
        var runtimeFallbackSignature =
            "Mapper.Map<CleanArchitecture.Domain.Entities.TodoList, CleanArchitecture.Application.TodoLists.Queries.GetTodos.TodoListDto>";
        Assert.False(generatedText.Contains(runtimeFallbackSignature, StringComparison.Ordinal),
            $"生成代码含运行时回退自递归调用（Q7 复现确认）——详见 {path}");
    }

    /// <summary>
    /// 系统程序集引用集
    /// </summary>
    /// <returns>MetadataReference 列表</returns>
    private static List<MetadataReference> SystemReferences()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();
    }

    /// <summary>
    /// 编译单文件程序集并返回其元数据引用
    /// </summary>
    /// <param name="source">C# 源码</param>
    /// <param name="assemblyName">程序集名</param>
    /// <param name="references">引用集</param>
    /// <returns>编译产物 MetadataReference</returns>
    private static MetadataReference CompileLib(string source, string assemblyName, IEnumerable<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success,
            "LibA 编译失败：" + string.Join("\n", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    /// <summary>
    /// dump 生成代码到临时文件
    /// </summary>
    /// <param name="result">生成结果</param>
    /// <param name="path">输出路径</param>
    private static void Dump(MapperGenerationResult result, string path)
    {
        using var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        sw.WriteLine("=== 全部生成方法 ===");
        foreach (var (name, methods) in result.Methods.OrderBy(kv => kv.Key))
        {
            sw.WriteLine($"\n### {name}");
            foreach (var m in methods)
            {
                sw.WriteLine(m.Body ?? "(null body)");
            }
        }
        sw.WriteLine("\n=== 诊断 ===");
        foreach (var d in result.Diagnostics)
        {
            sw.WriteLine($"  {d.Id}: {d.GetMessage()}");
        }
    }
}
