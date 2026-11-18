using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 全局配置读取器测试— MSBuild 属性读取、[assembly: MapDefaults] 读取、三层合并、非法值降级
/// <para>对应任务验收标准 3/4。</para>
/// </summary>
public class GlobalConfigReaderTests
{
    /// <summary>
    /// 验证：MSBuild 11 属性全量解析正确（枚举名 + 分号分隔清单）
    /// </summary>
    [Fact]
    public void ReadFromMSBuild_AllProperties_Parsed()
    {
        var options = new MapBuilderGlobalOptions(new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "SnakeCase",
            ["build_property.NCMapperPrefixes"] = "Get,Set",
            ["build_property.NCMapperPostfixes"] = "Async",
            ["build_property.NCMapperIgnoreTypes"] = "System.Byte[],System.String",
            ["build_property.NCMapperIgnoreNames"] = "RowVersion,CreatedAt",
            ["build_property.NCMapperIgnoreNamePrefixes"] = "Audit",
            ["build_property.NCMapperIgnoreNameSuffixes"] = "Flag",
            ["build_property.NCMapperIgnoreTypesToEntity"] = "System.Byte[]",
            ["build_property.NCMapperIgnoreNamesToEntity"] = "InternalState",
            ["build_property.NCMapperIgnoreNamePrefixesToEntity"] = "Sys",
            ["build_property.NCMapperIgnoreNameSuffixesToEntity"] = "Meta",
        });

        var snapshot = GlobalConfigReader.ReadFromMSBuild(options);

        Assert.Equal(2, snapshot.NamingStrategy);
        Assert.Equal(["Get", "Set"], snapshot.Prefixes!);
        Assert.Equal(["Async"], snapshot.Postfixes!);
        Assert.Equal(["System.Byte[]", "System.String"], snapshot.IgnoreTypeFullNames!);
        Assert.Equal(["RowVersion", "CreatedAt"], snapshot.IgnoreNames!);
        Assert.Equal(["Audit"], snapshot.IgnoreNamePrefixes!);
        Assert.Equal(["Flag"], snapshot.IgnoreNameSuffixes!);
        Assert.Equal(["System.Byte[]"], snapshot.IgnoreTypeFullNamesToEntity!);
        Assert.Equal(["InternalState"], snapshot.IgnoreNamesToEntity!);
        Assert.Equal(["Sys"], snapshot.IgnoreNamePrefixesToEntity!);
        Assert.Equal(["Meta"], snapshot.IgnoreNameSuffixesToEntity!);
        Assert.Empty(snapshot.Diagnostics);
    }

    /// <summary>
    /// 验证：非法枚举名 → 该项未设置 + NCMAP020 诊断（不阻断）
    /// </summary>
    [Fact]
    public void ReadFromMSBuild_InvalidEnum_UnsetWithDiagnostic()
    {
        var options = new MapBuilderGlobalOptions(new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "KebabCase",
        });

        var snapshot = GlobalConfigReader.ReadFromMSBuild(options);

        Assert.Equal(-1, snapshot.NamingStrategy);
        var diag = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(DiagnosticDescriptors.InvalidGlobalConfigValueRule, diag.Descriptor);
    }

    /// <summary>
    /// 验证：清单超 8 项 → 该项未设置 + NCMAP020 诊断
    /// </summary>
    [Fact]
    public void ReadFromMSBuild_ListOver8Items_UnsetWithDiagnostic()
    {
        var options = new MapBuilderGlobalOptions(new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "A,B,C,D,E,F,G,H,I",
        });

        var snapshot = GlobalConfigReader.ReadFromMSBuild(options);

        Assert.Null(snapshot.IgnoreNames);
        Assert.Single(snapshot.Diagnostics);
    }

    /// <summary>
    /// 验证：Q3 边界 — 类型全名含泛型 arity 反引号 → 该项降级未设置 + NCMAP020（MVP 仅承诺封闭非泛型全名）
    /// </summary>
    [Fact]
    public void ReadFromMSBuild_GenericTypeFullName_UnsetWithDiagnostic()
    {
        var options = new MapBuilderGlobalOptions(new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreTypes"] = "System.Collections.Generic.List`1",
        });

        var snapshot = GlobalConfigReader.ReadFromMSBuild(options);

        Assert.Null(snapshot.IgnoreTypeFullNames);
        Assert.Single(snapshot.Diagnostics);
    }

    /// <summary>
    /// 验证：未设置任何属性 → 快照为空（IsEmpty），不产生诊断
    /// </summary>
    [Fact]
    public void ReadFromMSBuild_NoProperties_EmptySnapshot()
    {
        var snapshot = GlobalConfigReader.ReadFromMSBuild(MapBuilderGlobalOptions.Empty);

        Assert.True(snapshot.IsEmpty);
        Assert.Empty(snapshot.Diagnostics);
    }

    /// <summary>
    /// 验证：[assembly: MapDefaults] 特性读取 — 枚举与清单正确解析，Type 转为全名
    /// </summary>
    [Fact]
    public void ReadFromAssemblyAttribute_AttributePresent_Parsed()
    {
        var source = """
            using NCMapper.Attributes;
            using NCMapper;

            [assembly: MapDefaults(
                NamingStrategy = NamingStrategy.UpperSnakeCase,
                IgnoreNames = new[] { "RowVersion", "CreatedAt" },
                IgnoreTypes = new[] { typeof(byte[]) })]

            namespace Consumer;
            public class Marker { }
            """;

        var compilation = CreateCompilation(source);
        var snapshot = GlobalConfigReader.ReadFromAssemblyAttribute(compilation, TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot!.NamingStrategy);
        Assert.Equal(["RowVersion", "CreatedAt"], snapshot.IgnoreNames!);
        Assert.Equal(["System.Byte[]"], snapshot.IgnoreTypeFullNames!);
        Assert.Empty(snapshot.Diagnostics);
    }

    /// <summary>
    /// 验证：未标注 [assembly: MapDefaults] → 返回 null
    /// </summary>
    [Fact]
    public void ReadFromAssemblyAttribute_NoAttribute_ReturnsNull()
    {
        var compilation = CreateCompilation("namespace Consumer; public class Marker { }");
        var snapshot = GlobalConfigReader.ReadFromAssemblyAttribute(compilation, TestContext.Current.CancellationToken);
        Assert.Null(snapshot);
    }

    /// <summary>
    /// 验证：三层合并 — 特性优先（非空覆盖），MSBuild 次之，逐项继承下层
    /// </summary>
    [Fact]
    public void Merge_ThreeLayerPrecedence_NonEmptyOverrides()
    {
        var attribute = new GlobalConfigSnapshot
        {
            NamingStrategy = 2,
            IgnoreNames = ["A"],
            Prefixes = ["Get"],
        };
        var msbuild = new GlobalConfigSnapshot
        {
            NamingStrategy = 3,
            IgnoreNames = ["B"],
            Postfixes = ["Async"],
        };

        var merged = GlobalConfigReader.Merge(attribute, msbuild);

        // 特性项优先
        Assert.Equal(2, merged.NamingStrategy);
        Assert.Equal(["A"], merged.IgnoreNames!);
        Assert.Equal(["Get"], merged.Prefixes!);
        // 特性未设置项继承 MSBuild
        Assert.Equal(["Async"], merged.Postfixes!);
    }

    /// <summary>
    /// 验证：合并时空数组视为"已设置"（覆盖下层），null 视为"未设置"（继承下层）
    /// </summary>
    [Fact]
    public void Merge_EmptyArrayOverrides_NullInherits()
    {
        var attribute = new GlobalConfigSnapshot { IgnoreNames = [] };
        var msbuild = new GlobalConfigSnapshot { IgnoreNames = ["B"] };

        var merged = GlobalConfigReader.Merge(attribute, msbuild);

        // 空数组非 null → 已设置 → 覆盖 MSBuild 值（用户显式清空）
        Assert.Equal([], merged.IgnoreNames!);
    }

    /// <summary>
    /// 验证：Merge(null, msbuild) → msbuild；Merge(attribute, null) → attribute
    /// </summary>
    [Fact]
    public void Merge_SingleLayer_ReturnsThatLayer()
    {
        var msbuild = new GlobalConfigSnapshot { NamingStrategy = 1 };
        var fromMsbuild = GlobalConfigReader.Merge(null, msbuild);
        Assert.Equal(1, fromMsbuild.NamingStrategy);

        var attribute = new GlobalConfigSnapshot { NamingStrategy = 2 };
        var fromAttribute = GlobalConfigReader.Merge(attribute, null);
        Assert.Equal(2, fromAttribute.NamingStrategy);
    }

    /// <summary>
    /// 验证：端到端 — MSBuild 属性注入后生成代码含 RegisterGeneratedConfig 快照（D1 PoC 的生成侧可观测载体）
    /// </summary>
    [Fact]
    public void EndToEnd_MSBuildProperty_SnapshotEmitted()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public string Name { get; set; } = "";
            }
            public class Entity
            {
                public string Name { get; set; } = "";
                public byte[] RowVersion { get; set; } = [];
            }
            """;

        var compilation = CreateCompilation(source);
        var generator = new NCMapperSourceGenerator();
        var optionsProvider = new MapBuilderConfigOptionsProvider(new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "RowVersion",
            ["build_property.NCMapperNamingStrategy"] = "SnakeCase",
        });
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            optionsProvider: optionsProvider);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics, TestContext.Current.CancellationToken);
        var generated = updatedCompilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated_GeneratedMapper.g.cs"))?
            .ToString() ?? "";

        // 快照注册行（程序集名取自映射类型所在程序集）
        Assert.Contains("NCMapper.Mapper.RegisterGeneratedConfig(new NCMapper.GeneratedMapConfig", generated);
        Assert.Contains("NamingStrategy = 2,", generated);
        Assert.Contains("IgnoreNames = new string[] { \"RowVersion\" },", generated);
        Assert.Contains("EndToEndMSBuildPropertySnapshotEmitted", generated); // 程序集名常量
        Assert.DoesNotContain("NCMAP020", diagnostics.ToString());
    }

    /// <summary>
    /// 验证：非法 MSBuild 值经管线输出 NCMAP020 诊断（不阻断生成）
    /// </summary>
    [Fact]
    public void EndToEnd_InvalidMSBuildValue_NCMAP020Diagnostic()
    {
        var source = """
            using NCMapper.Attributes;

            namespace Consumer;
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public string Name { get; set; } = "";
            }
            public class Entity
            {
                public string Name { get; set; } = "";
            }
            """;

        var compilation = CreateCompilation(source);
        var generator = new NCMapperSourceGenerator();
        var optionsProvider = new MapBuilderConfigOptionsProvider(new Dictionary<string, string>
        {
            ["build_property.NCMapperNamingStrategy"] = "KebabCase",
        });
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            optionsProvider: optionsProvider);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics, TestContext.Current.CancellationToken);

        Assert.Contains(diagnostics, d => d.Id == "NCMAP020");
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
        // 显式补充 NCMapper（MapDefaultsAttribute/MapFromAttribute 所在程序集）
        var ncmapper = typeof(NCMapper.Mapper).Assembly.Location;
        if (!references.Any(r => r.Display == ncmapper))
            references.Add(MetadataReference.CreateFromFile(ncmapper));

        return CSharpCompilation.Create(
            "EndToEndMSBuildPropertySnapshotEmitted",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// 测试用 AnalyzerConfigOptions — 字典驱动
    /// </summary>
    private sealed class MapBuilderGlobalOptions : AnalyzerConfigOptions
    {
        /// <summary>
        /// 空实例
        /// </summary>
        public static readonly MapBuilderGlobalOptions Empty = new(new Dictionary<string, string>());

        private readonly IReadOnlyDictionary<string, string> _values;

        /// <summary>
        /// 创建字典驱动的配置选项
        /// </summary>
        /// <param name="values">键值对（完整键名，含 build_property. 前缀）</param>
        public MapBuilderGlobalOptions(IReadOnlyDictionary<string, string> values)
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

    /// <summary>
    /// 测试用 AnalyzerConfigOptionsProvider — 注入 11 个全局配置属性
    /// </summary>
    private sealed class MapBuilderConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        /// <summary>
        /// 创建配置提供器
        /// </summary>
        /// <param name="values">全局属性键值对</param>
        public MapBuilderConfigOptionsProvider(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GlobalOptions => new MapBuilderGlobalOptions(_values);

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => MapBuilderGlobalOptions.Empty;

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => MapBuilderGlobalOptions.Empty;
    }
}
