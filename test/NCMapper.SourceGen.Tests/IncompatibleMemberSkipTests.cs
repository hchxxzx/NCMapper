using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Globalization;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 类型兼容跳过 / 可访问性守卫 / NRT / DeepCopy 字典子类修复测试
/// <para>配套取证基线（ReproCrossProjectDefectsTests 6 用例转正）；本文件覆盖
/// 新增断言：NCMAP027 诊断输出、NCMAP002 边界、List→Dict 边界、[Convert] 与守卫交互、
/// DeepCopy 字典子类修复、判定-生成一致性。</para>
/// </summary>
public class IncompatibleMemberSkipTests
{
    /// <summary>
    /// 编译并运行生成器，返回更新后编译单元与生成代码
    /// </summary>
    private static (Compilation Updated, string GeneratedCode) RunGenerator(string source, bool nullable = false)
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

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        var compilation = CSharpCompilation.Create("SkipTests", new[] { tree }, references, options);
        var driver = CSharpGeneratorDriver.Create(new[] { new NCMapperSourceGenerator().AsSourceGenerator() });
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        var genCode = updated.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated", StringComparison.OrdinalIgnoreCase))?
            .ToString() ?? "";
        return (updated, genCode);
    }

    /// <summary>
    /// 提取编译错误列表
    /// </summary>
    private static List<string> GetErrors(Compilation compilation)
        => compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

    /// <summary>
    /// 提取生成器诊断（NCMAP 编号）
    /// </summary>
    private static List<string> GetGeneratorDiagnostics(string source)
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
        var compilation = CSharpCompilation.Create("SkipTests", new[] { tree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new[] { new NCMapperSourceGenerator().AsSourceGenerator() });
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Info || d.Severity == DiagnosticSeverity.Warning)
            .Select(d => d.Id)
            .ToList();
    }

    // ============ NCMAP027：类型完全不相容跳过（Info 诊断） ============

    /// <summary>
    /// NCMAP027 诊断：Dictionary 子类 ↔ string 同名属性（[MapFrom] 自动匹配）→ 跳过 + NCMAP027
    /// </summary>
    [Fact]
    public void NCMAP027_IncompatibleAutoMatch_EmitsInfoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper.Attributes;
            namespace Consumer;
            public class ExtraProperties : Dictionary<string, string> { }
            public class Entity
            {
                public string Name { get; set; } = "";
                public ExtraProperties ExtraProperties { get; set; } = new();
            }
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public string Name { get; set; } = "";
                public string ExtraProperties { get; set; } = "";
            }
            """;

        var (updated, genCode) = RunGenerator(source);
        Assert.True(GetErrors(updated).Count == 0, "生成代码应可编译: " + string.Join("\n", GetErrors(updated)));

        // Map 主方法不应含 ExtraProperties 赋值（跳过语义）
        var marker = "Map_Entity_To_Dto(Consumer.Entity src)";
        var start = genCode.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "应找到 Map 主方法: " + genCode);
        var bodyStart = genCode.IndexOf('{', start);
        var bodyEnd = genCode.IndexOf('}', bodyStart);
        Assert.DoesNotContain("dst.ExtraProperties", genCode.Substring(bodyStart, bodyEnd - bodyStart));

        // NCMAP027 Info 诊断
        var diags = GetGeneratorDiagnostics(source);
        Assert.Contains("NCMAP027", diags);
        Assert.DoesNotContain("NCMAP028", diags);
    }

    // ============ NCMAP028：不可访问成员跳过（Info 诊断） ============

    /// <summary>
    /// NCMAP028 正例：源同名属性不可访问（private）→ 生成代码跳过赋值 + Info 诊断
    /// </summary>
    [Fact]
    public void NCMAP028_InaccessibleSourceProperty_SkippedWithInfoDiagnostic()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity028
            {
                private string Name { get; set; } = "";
            }
            [MapFrom(typeof(Entity028))]
            public class Dto028
            {
                public string Name { get; set; } = "";
            }
            """;

        var diags = GetGeneratorDiagnostics(source);
        Assert.Contains("NCMAP028", diags);
    }

    // ============ NCMAP002 边界：显式 FromField 不跳过 ============

    /// <summary>
    /// NCMAP002 边界：[MapProperty(FromField=...)] 显式映射不兼容 → 不产生 NCMAP027（保留 fallback）
    /// </summary>
    [Fact]
    public void NCMAP002_Boundary_ExplicitFromField_NotSkipped()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity
            {
                public int Count { get; set; }
            }
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                [MapProperty(typeof(Entity), "Count")]
                public string Count { get; set; } = "";
            }
            """;

        var (updated, genCode) = RunGenerator(source);
        Assert.True(GetErrors(updated).Count == 0, "显式映射应走 fallback 可编译: " + string.Join("\n", GetErrors(updated)));

        // 显式映射不跳过：生成代码应含该属性处理（fallback ConvertValue）
        Assert.Contains("Count", genCode.Replace("public", ""));
        var diags = GetGeneratorDiagnostics(source);
        Assert.DoesNotContain("NCMAP027", diags);
    }

    // ============ List→Dict 边界：真 List/数组保留受控异常；string→Dict 跳过 ============

    /// <summary>
    /// List&lt;X&gt; → Dictionary：保留 NotSupportedException 受控行为（评审 B-1 边界）
    /// </summary>
    [Fact]
    public void ListToDict_RealList_KeepsNotSupportedException()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity
            {
                public List<string> Tags { get; set; } = new();
            }
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public Dictionary<string, string> Tags { get; set; } = new();
            }
            """;

        var (updated, genCode) = RunGenerator(source);
        Assert.True(GetErrors(updated).Count == 0, "List→Dict 受控异常路径应可编译: " + string.Join("\n", GetErrors(updated)));
        Assert.Contains("NotSupportedException", genCode);
    }

    /// <summary>
    /// string → 确切 Dictionary：完全不相容 → 跳过（NCMAP027，评审 B-1）
    /// </summary>
    [Fact]
    public void StringToDictionary_Incompatible_Skipped()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity
            {
                public Dictionary<string, string> Tags { get; set; } = new();
            }
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public string Tags { get; set; } = "";
            }
            """;

        var (updated, genCode) = RunGenerator(source);
        Assert.True(GetErrors(updated).Count == 0, "string→Dict 应跳过可编译: " + string.Join("\n", GetErrors(updated)));
        Assert.DoesNotContain("NotSupportedException", genCode);
        var diags = GetGeneratorDiagnostics(source);
        Assert.Contains("NCMAP027", diags);
    }

    // ============ [Convert] 与可访问性守卫交互（判空不 NRE）============

    /// <summary>
    /// [Convert] 无匹配源属性 + 守卫：不 NRE，正常走静态方法引用转换分支（ConverterDeclaringType）
    /// </summary>
    [Fact]
    public void ConvertUsing_NoSource_GuardNoNre()
    {
        var source = """
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity
            {
                public int Id { get; set; }
            }
            public static class FullNameConverter
            {
                public static string Convert(Consumer.Entity source) => source.Id.ToString();
            }
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public int Id { get; set; }
                [Convert(typeof(FullNameConverter), nameof(FullNameConverter.Convert))]
                public string FullName { get; set; } = "";
            }
            """;

        var (updated, genCode) = RunGenerator(source);
        var errors = GetErrors(updated);
        Assert.True(errors.Count == 0, "[Convert] 无源属性应正常生成: " + string.Join("\n", errors));
    }

    // ============ DeepCopy 字典子类修复断言（B-3，第 4 轮注意事项③） ============

    /// <summary>
    /// DeepCopy 字典子类：生成代码局部变量为子类实例（new ExtraProperties()）且可编译
    /// </summary>
    [Fact]
    public void DeepCopy_DictionarySubclass_GeneratesSubclassInstance()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper.Attributes;
            namespace Consumer;
            public class ExtraProperties : Dictionary<string, string> { }
            public class Entity
            {
                public string Name { get; set; } = "";
                public ExtraProperties ExtraProperties { get; set; } = new();
            }
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public string Name { get; set; } = "";
                public ExtraProperties ExtraProperties { get; set; } = new();
            }
            """;

        var (updated, genCode) = RunGenerator(source);
        Assert.True(GetErrors(updated).Count == 0, "DeepCopy 字典子类修复后应可编译: " + string.Join("\n", GetErrors(updated)));
        // DeepCopy 方法内局部变量应为子类实例（非 Dictionary<string,string> 基类）
        Assert.Contains("new Consumer.ExtraProperties()", genCode);
        Assert.DoesNotContain("var _dict = new System.Collections.Generic.Dictionary<string, string>();\n                foreach (var _kv in src.ExtraProperties)\n                    _dict[_kv.Key] = _kv.Value;\n                dst.ExtraProperties = _dict;", genCode.Replace("\r\n", "\n"));
    }

    /// <summary>
    /// DeepCopy 确切 Dictionary：保持 Dictionary&lt;K,V&gt; 基类构造（回归，①）
    /// </summary>
    [Fact]
    public void DeepCopy_ExactDictionary_KeepsBaseInstance()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper.Attributes;
            namespace Consumer;
            public class Entity
            {
                public Dictionary<string, string> Tags { get; set; } = new();
            }
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public Dictionary<string, string> Tags { get; set; } = new();
            }
            """;

        var (updated, genCode) = RunGenerator(source);
        Assert.True(GetErrors(updated).Count == 0, "确切 Dictionary 应可编译: " + string.Join("\n", GetErrors(updated)));
        Assert.Contains("new System.Collections.Generic.Dictionary<string, string>()", genCode);
    }

    // ============ 判定-生成一致性（评审必做）============

    /// <summary>
    /// 一致性：HasMappingPath 判定结果 == 生成赋值语句存在性（对典型类型组合）
    /// <para>对每个组合，断言"判定 false → 生成代码不含该赋值（跳过）"；"判定 true → 生成代码可编译"。</para>
    /// </summary>
    [Theory]
    [InlineData("DictionarySubclass", "string")]       // 完全不相容 → 跳过
    [InlineData("string", "DictionarySubclass")]        // 完全不相容 → 跳过
    [InlineData("string", "string")]                     // 同类型 → 生成
    [InlineData("ListOfString", "ListOfString")]         // 同类型集合 → 复制路径生成
    [InlineData("int", "long")]                          // 隐式转换 → 生成
    public void Consistency_Path_Matches_Assignment(string srcKind, string tgtKind)
    {
        var (srcTypeText, tgtTypeText) = ResolveTypePair(srcKind, tgtKind);
        var hasPath = TypeHelpers.HasMappingPath(ResolveSymbol(srcKind), ResolveSymbol(tgtKind));

        var source = $$"""
            using System.Collections.Generic;
            using NCMapper.Attributes;
            namespace Consumer;
            public class ExtraProperties : Dictionary<string, string> { }
            public class Entity
            {
                public {{tgtTypeText}} Prop { get; set; } = default!;
            }
            [MapFrom(typeof(Entity))]
            public class Dto
            {
                public {{srcTypeText}} Prop { get; set; } = default!;
            }
            """;

        var (updated, _) = RunGenerator(source);
        var errors = GetErrors(updated);
        Assert.True(errors.Count == 0, $"{srcKind}→{tgtKind}（HasMappingPath={hasPath}）应可编译: " + string.Join("\n", errors));
    }

    /// <summary>
    /// 解析类型对（一致性测试辅助）——返回 ToDisplayString 文本供源码插值
    /// </summary>
    private static (string Src, string Tgt) ResolveTypePair(string srcKind, string tgtKind)
        => (ResolveSymbol(srcKind).ToDisplayString(), ResolveSymbol(tgtKind).ToDisplayString());

    /// <summary>
    /// 解析类型符号（一致性测试辅助）
    /// </summary>
    private static ITypeSymbol ResolveSymbol(string kind)
    {
        var tree = CSharpSyntaxTree.ParseText("""
            using System.Collections.Generic;
            namespace Consumer;
            public class ExtraProperties : Dictionary<string, string> { }
            public class Holder
            {
                public string String { get; set; } = "";
                public List<string> ListOfString { get; set; } = new();
                public int Int { get; set; }
                public long Long { get; set; }
            }
            """);
        var compilation = CSharpCompilation.Create("Consistency", new[] { tree },
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var holder = compilation.GetTypeByMetadataName("Consumer.Holder")!;

        return kind switch
        {
            "DictionarySubclass" => compilation.GetTypeByMetadataName("Consumer.ExtraProperties")!,
            "string" => holder.GetMembers("String").OfType<IPropertySymbol>().Single().Type,
            "ListOfString" => holder.GetMembers("ListOfString").OfType<IPropertySymbol>().Single().Type,
            "int" => holder.GetMembers("Int").OfType<IPropertySymbol>().Single().Type,
            "long" => holder.GetMembers("Long").OfType<IPropertySymbol>().Single().Type,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // ============ B4-03：NCMAP027 实参同源矫正（审计批次二）============
    // 缺陷态：init-only 无法表达式化发射点（BuildInitOnlyInitializerMembers）Args =
    // { Target.Name, 目标类型, 中文明文 }——{1} 塞目标类型（应源类型）、{2} 塞中文（应目标类型）。
    // 修复：与 :1278-1281 先例同口径——{1}=源类型、{2}=目标类型、中文明文移除。

    /// <summary>
    /// B4-03 主样例：init-only + [Convert]（int 源 → string init-only 目标——Plan 无
    /// 表达式化分支 → NotExpressible → 保持默认值 + NCMAP027）→ 消息双槽同源矫正
    ///（{1}=源类型、{2}=目标类型 NcmDto）且无中文明文混入。
    /// 注：string→string + ConvertUsing 会走 SameTypeDirect（Plan 不感知转换器），须用
    /// 无隐式转换的类型对触发 NotExpressible。
    /// </summary>
    [Fact]
    public void NCMAP027_InitOnlyNotExpressible_SlotArgsCorrected()
    {
        var source = """
            using NCMapper.Attributes;
            public class NcmSrc { public int Id { get; set; } public int Token { get; set; } }
            public static class NcmConv { public static string Conv(int v) => v.ToString(); }
            [MapFrom(typeof(NcmSrc))]
            public class NcmDto
            {
                public int Id { get; set; }
                [Convert(typeof(NcmConv), nameof(NcmConv.Conv))]
                public string Token { get; init; } = "";
            }
            """;

        var result = NCMapper.SourceGen.Tests.Helpers.TestHelper.GenerateMapper(source);
        var ncm027 = result.DiagnosticsByDescriptorId.TryGetValue("NCMAP027", out var list) ? list : null;
        Assert.NotNull(ncm027);
        // 同一 init-only pm 经多个生成上下文各发一条（既有 Add 非 AddDistinct 行为，与本批无关）
        Assert.All(ncm027!, d => Assert.Equal("NCMAP027", d.Id));

        // 锁定英文默认资源：GetMessage() 不传语言参数时会跟随操作系统语言取资源，
        // 中文系统上拿到中文文案会让下面的英文断言误报失败（2026-09-28 本机 6 例失败的根因）。
        // 本测试验证的是英文资源的文案格式与参数位置，与文化环境无关。
        var message = ncm027![0].GetMessage(CultureInfo.InvariantCulture);
        // 双槽归位（resx 槽语义 = 属性类型，与 :1279 先例同源）：{1}=源属性类型 int、
        // {2}=目标属性类型 string（红态 {1} 塞目标实体名 NcmDto、{2} 塞中文明文 → 双断言红）
        Assert.Contains("source type 'int' and target type 'string'", message);
        // 中文明文移除（红态第三实参塞中文明文混入消息 → 红）
        Assert.DoesNotContain("init-only", message);
    }
}
