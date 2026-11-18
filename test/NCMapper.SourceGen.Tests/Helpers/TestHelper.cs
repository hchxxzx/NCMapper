using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NCMapper.SourceGen;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NCMapper.SourceGen.Tests.Helpers;

/// <summary>
/// 测试辅助类 — 构建 Compilation、运行源生成器、返回封装后的生成结果
/// </summary>
public static class TestHelper
{
    /// <summary>
    /// 主生成器输出文件名
    /// </summary>
    public const string GeneratedFileName = "NCMapper_Generated_GeneratedMapper.g.cs";

    /// <summary>
    /// 运行 NCMapperSourceGenerator 并返回封装后的生成结果
    /// </summary>
    /// <param name="source">C# 源码字符串（包含 [MapFrom] 标注的 DTO 类）</param>
    /// <returns>封装后的生成器运行结果</returns>
    public static MapperGenerationResult GenerateMapper(string source)
        => GenerateMapper(source, buildProperties: null);

    /// <summary>
    /// 带 MSBuild 属性注入的生成器运行（B3-21 红测面——NCMAP020 触发仅存在于
    /// MSBuild 通道字符串解析（特性通道强类型参数编译期恒合法），经 optionsProvider 注入
    /// build_property.* 键值模拟 csproj CompilerVisibleProperty）
    /// </summary>
    public static MapperGenerationResult GenerateMapper(string source, IReadOnlyDictionary<string, string>? buildProperties)
    {
        var compilation = BuildCompilation(source);
        var generator = new NCMapperSourceGenerator();
        var driver = buildProperties == null
            ? CSharpGeneratorDriver.Create(generator.AsSourceGenerator())
            : CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { generator.AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default,
                optionsProvider: new DictionaryConfigOptionsProvider(buildProperties));
        var runResult = driver.RunGenerators(compilation).GetRunResult();
        // 定位主生成器输出文件
        var generatedTree = runResult.GeneratedTrees
            .SingleOrDefault(t => Path.GetFileName(t.FilePath) == GeneratedFileName);

        var generatedCode = generatedTree?.ToString() ?? string.Empty;
        var syntaxRoot = generatedTree?.GetRoot();

        // 提取所有方法并构建字典（按方法名分组，支持重载方法）
        var methods = ExtractAllMethods(syntaxRoot)
            .Select(x => new GeneratedMethod(x))
            .GroupBy(x => x.Name)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<GeneratedMethod>)x.ToList());

        // 分组诊断信息
        var filteredDiagnostics = runResult.Diagnostics.ToList();
        var groupedDiagnostics = filteredDiagnostics
            .GroupBy(x => x.Descriptor.Id)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<Diagnostic>)x.ToList());

        return new MapperGenerationResult(filteredDiagnostics, groupedDiagnostics, methods, generatedCode);
    }

    /// <summary>B3-21 红测注入：字典版 AnalyzerConfigOptions（build_property.* 键值直供）</summary>
    private sealed class DictionaryConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly IReadOnlyDictionary<string, string> _props;
        public DictionaryConfigOptionsProvider(IReadOnlyDictionary<string, string> props) => _props = props;
        public override AnalyzerConfigOptions GlobalOptions => new DictOptions(_props);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new DictOptions(_props);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new DictOptions(_props);

        private sealed class DictOptions : AnalyzerConfigOptions
        {
            private readonly IReadOnlyDictionary<string, string> _props;
            public DictOptions(IReadOnlyDictionary<string, string> props) => _props = props;
            public override bool TryGetValue(string key, out string value)
                => _props.TryGetValue(key, out value!);
        }
    }

    /// <summary>
    /// 构建 C# Compilation，引用系统程序集和 NCMapper 程序集
    /// </summary>
    /// <param name="source">C# 源码字符串</param>
    /// <returns>编译实例</returns>
    public static CSharpCompilation BuildCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default);

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            "CodeGenTests",
            [syntaxTree],
            options: compilationOptions);

        // 引用系统程序集
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        compilation = compilation.AddReferences(references);

        // 引用 NCMapper 程序集（MapFromAttribute 所在）
        compilation = compilation.AddReferences(
            MetadataReference.CreateFromFile(typeof(NCMapper.Attributes.MapFromAttribute).Assembly.Location));

        // Fluent 通道 lambda 形参为 Expression<Func<...>>——
        // 测试宿主进程未按需加载 System.Linq.Expressions 时 CS0012 导致 lambda 体不绑定
        //（GetSymbolInfo 恒 null → F02 误报、声明被丢）；显式补引用保证 Fluent 源可绑定
        compilation = compilation.AddReferences(
            MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression<>).Assembly.Location));

        return compilation;
    }

    /// <summary>
    /// B-3 注册时机测试辅助：编译源码（跑 NCMapperSourceGenerator + RuntimeSnapshotGenerator 双生成器，
    /// 发射开关可控）→ 输出 compilation（含生成代码）→ Emit 内存字节 → 返回加载后的 Assembly。
    /// <para>用途：构造"独立程序集含 Fluent DTO + [ModuleInitializer] RuntimeSnapshot 注册行"的场景，
    /// 验证 typeof(Dto) 首查是否触发 ModuleInitializer（B-3 注册时机死锁，R9）。</para>
    /// </summary>
    /// <param name="source">C# 源码（含 [MapperConfig]/[MapFrom] 标注）</param>
    /// <param name="emitRuntimeSnapshot">true 时经 GlobalOptions 注入 build_property.NCMapperEmitRuntimeSnapshot=true</param>
    /// <returns>已加载（Load 于 bytes）的内存程序集</returns>
    public static Assembly CompileWithGeneratorsAndLoad(string source, bool emitRuntimeSnapshot)
    {
        var compilation = BuildCompilation(source);
        var generators = ImmutableArray.Create<ISourceGenerator>(
            new NCMapperSourceGenerator().AsSourceGenerator(),
            new RuntimeSnapshotGenerator().AsSourceGenerator());

        AnalyzerConfigOptionsProvider? optionsProvider = null;
        if (emitRuntimeSnapshot)
            optionsProvider = new EmitSwitchOnConfigOptionsProvider();

        var driver = CSharpGeneratorDriver.Create(
            generators,
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: optionsProvider);

        // RunGeneratorsAndUpdateCompilation 返回含生成代码（主生成器 + RuntimeSnapshot 产物）的 output compilation
        var driverResult = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var _);

        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = string.Join("; ",
                emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10));
            throw new InvalidOperationException($"编译含生成器的源码失败: {errors}");
        }

        return Assembly.Load(ms.ToArray());
    }

    /// <summary>
    /// 运行生成器并返回 RuntimeSnapshot 注册段生成代码文本（发射面断言辅助——
    /// <see cref="GenerateMapper"/> 走默认 off 态（注册段零发射）；本方法经 GlobalOptions 注入
    /// NCMapperEmitRuntimeSnapshot=true 后提取 RuntimeSnapshotGenerator 输出，供对级条件行断言。
    /// </summary>
    /// <param name="source">C# 源码（含 [MapperConfig]/[MapFrom] 标注）</param>
    /// <returns>RuntimeSnapshot 注册段生成代码（开关关闭或无注册产物时为空串）</returns>
    public static string GenerateRuntimeSnapshotCode(string source)
    {
        var compilation = BuildCompilation(source);
        var generators = ImmutableArray.Create<ISourceGenerator>(
            new NCMapperSourceGenerator().AsSourceGenerator(),
            new RuntimeSnapshotGenerator().AsSourceGenerator());
        var driver = CSharpGeneratorDriver.Create(
            generators,
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: new EmitSwitchOnConfigOptionsProvider());
        var runResult = driver.RunGenerators(compilation).GetRunResult();
        var tree = runResult.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("RuntimeSnapshot", StringComparison.OrdinalIgnoreCase)
                || t.FilePath.Contains("SnapshotRegistry", StringComparison.OrdinalIgnoreCase));
        return tree?.ToString() ?? string.Empty;
    }

    /// <summary>B-3 发射开关注入：GlobalOptions 恒返回 NCMapperEmitRuntimeSnapshot=true</summary>
    private sealed class EmitSwitchOnConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => s_on;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_on;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_on;
        private static readonly AnalyzerConfigOptions s_on = new OnOptions();
        private sealed class OnOptions : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.NCMapperEmitRuntimeSnapshot")
                {
                    value = "true";
                    return true;
                }
                value = string.Empty;
                return false;
            }
        }
    }

    /// <summary>
    /// 递归提取语法树中的所有方法声明
    /// </summary>
    /// <param name="root">语法树根节点（可为 null）</param>
    /// <returns>方法声明语法节点集合</returns>
    private static IEnumerable<MethodDeclarationSyntax> ExtractAllMethods(SyntaxNode? root)
    {
        if (root == null)
            yield break;

        foreach (var node in root.ChildNodes())
        {
            if (node is NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax)
            {
                foreach (var method in ExtractAllMethods(node))
                    yield return method;
            }

            if (node is not ClassDeclarationSyntax classNode)
                continue;

            foreach (var method in classNode.ChildNodes().OfType<MethodDeclarationSyntax>())
                yield return method;

            foreach (var method in ExtractAllMethods(node))
                yield return method;
        }
    }
}
