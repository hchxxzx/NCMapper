// （B5-4）验收③——缓存击穿行为测试（对照 B4-04 批次
// IncrementalCtorParamTests 四红测形态）：Fluent 钩子 / 方向声明变更后，同 driver
// 第二跑产物反映新输入（增量再生成触发）。
//   覆盖字段族：MappingInfo.AfterMappingMethodRef / BeforeMappingMethodRef（R-10——
//  null→值与值→值两型变更）；IgnoreSource 方向用例随终端退役移除。
//   定性背景：R-10/B4-06 缺口位于符号族（Models.cs——通道 transform 内部消费、物化后丢弃）；
//   两通道 transform 出口均已值化（MappingSnapshot），快照侧对应字段（AfterMappingMethodRef/
//   BeforeMappingMethodRef/NoAutoInclude/IsFluentForward、PropertyRef.MapIgnoreDirection）已在
//   EqualsCover（SnapshotConservationTests :299-314/:234-246）——本类由此定位为"现状回归防线"
//   （锁定值化出口对钩子/方向编辑的检出，防未来快照重构静默破坏该隐式检出通道——B4-04 批次
//   IncrementalCtorParamTests 同款翻转先例）；符号族缺口的先证红测由 EqualityConservationTests
//   孪生断言（MappingInfo/DiagnosticInfo/PropertyMapping 三注册面）承载。防线有效性由变异演练
//   证明（执行期）：临时移除 MappingSnapshot.Equals 的 AfterMappingMethodRef 腿 → 钩子两用例
//   必红 → 还原复绿；方向腿变异点 = PropertyRef.Equals 的 MapIgnoreDirection。
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class IncrementalHookDirectionTests
{
    private const string MainFileName = "NCMapper_Generated_GeneratedMapper.g.cs";
    private const string DetailFileName = "NCMapper_RuntimeSnapshotMetadata.g.cs";

    /// <summary>同 CSharpGeneratorDriver 实例两跑（B4-04 IncrementalCtorParamTests.RunTwoPass 同款）——
    /// 增量管线 transform 缓存比较发生处；双生成器 + 运行时快照发射开关（方向用例消费明细段）。</summary>
    private static (string main1, string detail1, string main2, string detail2) RunTwoPass(
        string source1, string source2)
    {
        var compilation1 = CSharpCompilation.Create(
            "IhdTwoPass",
            new[] { CSharpSyntaxTree.ParseText(source1, cancellationToken: TestContext.Current.CancellationToken) },
            DiagnosticAndCacheTests.BuildMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generators = ImmutableArray.Create<ISourceGenerator>(
            new NCMapperSourceGenerator().AsSourceGenerator(),
            new RuntimeSnapshotGenerator().AsSourceGenerator());
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators,
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: new EmitRuntimeSnapshotOptions());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation1, out var updated1, out _, TestContext.Current.CancellationToken);
        var main1 = GetGenerated(updated1, compilation1, MainFileName);
        var detail1 = GetGenerated(updated1, compilation1, DetailFileName);

        var compilation2 = CSharpCompilation.Create(
            "IhdTwoPass",
            new[] { CSharpSyntaxTree.ParseText(source2, cancellationToken: TestContext.Current.CancellationToken) },
            DiagnosticAndCacheTests.BuildMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation2, out var updated2, out _, TestContext.Current.CancellationToken);
        var main2 = GetGenerated(updated2, compilation2, MainFileName);
        var detail2 = GetGenerated(updated2, compilation2, DetailFileName);
        return (main1, detail1, main2, detail2);
    }

    private static string GetGenerated(Compilation updated, Compilation original, string fileName) =>
        updated.SyntaxTrees
            .Skip(original.SyntaxTrees.Count())
            .FirstOrDefault(t => t.FilePath.Contains(fileName))
            ?.ToString() ?? "";

    /// <summary>提取生成方法体（两趟增量比对辅助——ExtractMethod）。</summary>
    private static string ExtractMethod(string text, string methodName)
    {
        var idx = text.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"生成方法 {methodName} 不存在");
        var bodyStart = text.IndexOf('{', idx);
        var depth = 0;
        for (var i = bodyStart; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(idx, i - idx + 1);
            }
        }
        return text.Substring(idx);
    }

    // ------------------------------------------------------------------
    // 用例①：AfterMapping 钩子值变更（值→值——AfterMappingMethodRef 参与快照相等）
    // ------------------------------------------------------------------

    private const string HookBase = """
        using NCMapper;
        namespace Ihd1
        {
            public class E { public string N { get; set; } = ""; }
            public class D { public string N { get; set; } = ""; }
            public static class Helper
            {
                public static void PostA(E s, D d) { }
                public static void PostB(E s, D d) { }
            }

            [MapperConfig]
            public static class M
            {
                public static void Configure(FluentMappingConfig cfg) =>
                    cfg.For<E, D>().Member(d => d.N).From(s => s.N).HOOK_SITE;
            }
        }
        """;

    /// <summary>用例③专用模板（配置尾占位形态）——run1 = 纯显式声明（无钩子）、run2 = 追加钩子；
    /// 原实现复用 HookBase + 旧形态 Map 字符串注入（T2.2 字符串面漏行——断代清扫 D-9 补改写）。</summary>
    private const string HookAddBase = """
        using NCMapper;
        namespace Ihd1
        {
            public class E { public string N { get; set; } = ""; }
            public class D { public string N { get; set; } = ""; }
            public static class Helper
            {
                public static void PostA(E s, D d) { }
            }

            [MapperConfig]
            public static class M
            {
                public static void Configure(FluentMappingConfig cfg) =>
                    cfg.For<E, D>().Member(d => d.N).From(s => s.N)CONFIG_TAIL;
            }
        }
        """;

    [Fact]
    public void AfterMappingChange_Run2ProductReflectsNewHook()
    {
        var (main1, _, main2, _) = RunTwoPass(
            HookBase.Replace("HOOK_SITE", "AfterMapping(Helper.PostA)"),
            HookBase.Replace("HOOK_SITE", "AfterMapping(Helper.PostB)"));
        // 先验锚：Run1 产物含 PostA（防 vacuous pass）
        Assert.Contains("Ihd1.Helper.PostA(src, dst);", main1);
        // 回归防线：Run2 反映新钩子（缓存陈旧时仍为 PostA → 红）
        Assert.Contains("Ihd1.Helper.PostB(src, dst);", main2);
        Assert.DoesNotContain("Ihd1.Helper.PostA(src, dst);", main2);
    }

    /// <summary>用例②：BeforeMapping 钩子值变更（值→值——BeforeMappingMethodRef 参与快照相等）。</summary>
    [Fact]
    public void BeforeMappingChange_Run2ProductReflectsNewHook()
    {
        var (main1, _, main2, _) = RunTwoPass(
            HookBase.Replace("HOOK_SITE", "BeforeMapping(Helper.PostA)"),
            HookBase.Replace("HOOK_SITE", "BeforeMapping(Helper.PostB)"));
        Assert.Contains("Ihd1.Helper.PostA(src, dst);", main1);
        Assert.Contains("Ihd1.Helper.PostB(src, dst);", main2);
        Assert.DoesNotContain("Ihd1.Helper.PostA(src, dst);", main2);
    }

    /// <summary>用例③：AfterMapping 钩子新增（null→值——未声明→声明，AfterMappingMethodRef
    /// 由 null 变值；缓存陈旧时 Run2 沿用 Run1 无钩子产物 → 红）。</summary>
    [Fact]
    public void AfterMappingAdded_Run2ProductReflectsNewHook()
    {
        var (main1, _, main2, _) = RunTwoPass(
            HookAddBase.Replace("CONFIG_TAIL", ""),
            HookAddBase.Replace("CONFIG_TAIL", ".AfterMapping(Helper.PostA)"));
        // 先验锚：Run1 无钩子调用
        Assert.DoesNotContain("Helper.PostA(src, dst);", main1);
        Assert.Contains("Map_E_To_D(", main1);
        // 回归防线：Run2 产物含新钩子调用
        Assert.Contains("Ihd1.Helper.PostA(src, dst);", main2);
    }

    /// <summary>运行时快照发射开关（SourceIndexesCarryingTests.EmitSwitchOptionsProvider 同款形态）。</summary>
    private sealed class EmitRuntimeSnapshotOptions : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Opts();
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Opts();
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Opts();
        private sealed class Opts : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.NCMapperEmitRuntimeSnapshot") { value = "true"; return true; }
                value = string.Empty;
                return false;
            }
        }
    }
}
