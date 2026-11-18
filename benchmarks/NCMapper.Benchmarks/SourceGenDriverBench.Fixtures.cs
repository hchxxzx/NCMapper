using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen;

namespace NCMapper.Benchmarks;

// 扩展夹具载体（复用既有单对输入，不另设夹具）。
// 以 partial 扩展 SourceGenDriverBench：8 个嵌套基准类按"类.方法"命名落地
// （BDN 显示名 SourceGenDriverBench.MapListShape 等）；既有单对输入与本文件零交叉（保持纵向可比，Q-6 口径）。
// 纪律约束：夹具 source 均为内存字符串，不作为项目源码参与编译 → generated-baseline 字节门零影响。
public partial class SourceGenDriverBench
{
    /// <summary>编译 harness 构建（与既有 Setup 同形态：AppDomain 引用集 + NCMapper 程序集补挂）。</summary>
    private static (CSharpCompilation Compilation, ISourceGenerator Generator) BuildHarness(string source, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var ncmapper = typeof(NCMapper.Attributes.MapFromAttribute).Assembly.Location;
        if (!references.Any(r => r.Display == ncmapper))
        {
            references.Add(MetadataReference.CreateFromFile(ncmapper));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return (compilation, new NCMapperSourceGenerator().AsSourceGenerator());
    }

    /// <summary>
    /// 夹具基类：GlobalSetup 探针守门（字节 &gt; 0 + 生成文本内容锚全命中——防"有产物但目标路径未触发"的假基准），
    /// Benchmark = 单次 driver 运行（返回生成文本 UTF-8 字节数），与既有 RunGeneratorDriver 同形态。
    /// 特性声明于本基类（嵌套类不继承外层类特性）；各夹具继承同一 job 口径。
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 15)]
    public abstract class SourceGenFixtureBase
    {
        protected abstract string SourceText { get; }
        /// <summary>生成文本内容锚：全部命中才算夹具有效（目标路径真实执行）。</summary>
        protected abstract string[] ProbeMarkers { get; }

        private CSharpCompilation _comp = null!;
        private ISourceGenerator _gen = null!;
        private int _probeBytes;

        /// <summary>生成文本规模（UTF-8 字节数，GlobalSetup 探针结果）— 前后对比的"文本规模应零变化"锚。</summary>
        public int GeneratedBytes => _probeBytes;

        [GlobalSetup]
        public void Setup()
        {
            (_comp, _gen) = BuildHarness(SourceText, $"SourceGenDriverBench_{GetType().Name}");

            var probe = CSharpGeneratorDriver.Create(_gen)
                .RunGenerators(_comp)
                .GetRunResult();
            _probeBytes = probe.GeneratedTrees.Sum(t => Encoding.UTF8.GetByteCount(t.ToString()));
            var allText = string.Join("\n", probe.GeneratedTrees.Select(t => t.ToString()));

            if (_probeBytes == 0)
                throw new InvalidOperationException(
                    $"{GetType().Name}: the generator produced no output -- the benchmark would measure nothing.");
            foreach (var marker in ProbeMarkers)
            {
                if (!allText.Contains(marker, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"{GetType().Name}: probe marker '{marker}' missing from generated text -- target path not exercised.");
            }

            Console.WriteLine($"[{GetType().Name}] probe OK: {_probeBytes} bytes, markers [{string.Join(", ", ProbeMarkers)}]");
        }

        [Benchmark]
        public int RunGeneratorDriver()
        {
            var runResult = CSharpGeneratorDriver.Create(_gen)
                .RunGenerators(_comp)
                .GetRunResult();

            var bytes = 0;
            foreach (var generatedTree in runResult.GeneratedTrees)
            {
                bytes += Encoding.UTF8.GetByteCount(generatedTree.ToString());
            }
            return bytes;
        }
    }

    // ================================================================
    // MapList 元素赋值体发射（Collections.cs GenerateInlineElement / TryBuildSimpleAssignments）
    // ================================================================

    public class MapListShape : SourceGenFixtureBase
    {
        protected override string[] ProbeMarkers { get; } = ["MlsOrderDto", "MapList_"];

        protected override string SourceText => """
            using System.Collections.Generic;
            using NCMapper.Attributes;

            namespace BenchInput;

            public class MlsItemEntity { public int SkuId { get; set; } public string Title { get; set; } = ""; public decimal Price { get; set; } }
            public class MlsTagEntity { public int TagKey { get; set; } public string Name { get; set; } = ""; public bool IsPrimary { get; set; } }
            public class MlsNoteEntity { public int NoteId { get; set; } public string Body { get; set; } = ""; public string Author { get; set; } = ""; }

            [MapFrom(typeof(MlsItemEntity))]
            public class MlsItemDto { public int SkuId { get; set; } public string Title { get; set; } = ""; public decimal Price { get; set; } }
            [MapFrom(typeof(MlsTagEntity))]
            public class MlsTagDto { public int TagKey { get; set; } public string Name { get; set; } = ""; public bool IsPrimary { get; set; } }
            [MapFrom(typeof(MlsNoteEntity))]
            public class MlsNoteDto { public int NoteId { get; set; } public string Body { get; set; } = ""; public string Author { get; set; } = ""; }

            public class MlsOrderEntity
            {
                public int Id { get; set; }
                public List<MlsItemEntity> Items { get; set; } = new();
                public List<MlsTagEntity> Tags { get; set; } = new();
                public IReadOnlyList<MlsNoteEntity> Notes { get; set; } = new();
            }

            [MapFrom(typeof(MlsOrderEntity))]
            public class MlsOrderDto
            {
                public int Id { get; set; }
                public List<MlsItemDto> Items { get; set; } = new();
                public List<MlsTagDto> Tags { get; set; } = new();
                public IReadOnlyList<MlsNoteDto> Notes { get; set; } = new();
            }
            """;
    }

    // ================================================================
    // DetectMutualPairs 互指对检测 O(n²)（≥3 互指对才可观测）
    // 互指对零产物（快照跳过生成）→ 单向对承担守门字节；检测循环 = 7² 次交叉判定。
    // ================================================================

    public class MutualPairs3 : SourceGenFixtureBase
    {
        protected override string[] ProbeMarkers { get; } = ["MpOneWayDto"];

        protected override string SourceText => """
            using NCMapper.Attributes;

            namespace BenchInput;

            [MapFrom(typeof(MpB1Entity))]
            public class MpA1Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
            [MapFrom(typeof(MpA1Dto))]
            public class MpB1Entity { public int Id { get; set; } public string Name { get; set; } = ""; }

            [MapFrom(typeof(MpB2Entity))]
            public class MpA2Dto { public int Id { get; set; } public string Code { get; set; } = ""; }
            [MapFrom(typeof(MpA2Dto))]
            public class MpB2Entity { public int Id { get; set; } public string Code { get; set; } = ""; }

            [MapFrom(typeof(MpB3Entity))]
            public class MpA3Dto { public int Id { get; set; } public string Title { get; set; } = ""; }
            [MapFrom(typeof(MpA3Dto))]
            public class MpB3Entity { public int Id { get; set; } public string Title { get; set; } = ""; }

            [MapFrom(typeof(MpB3Entity))]
            public class MpB3Dto { public int Id { get; set; } public string Title { get; set; } = ""; }

            [MapFrom(typeof(MpOneWayEntity))]
            public class MpOneWayDto { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class MpOneWayEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            """;
    }

    // ================================================================
    // 特性判定热路径（ToDisplayString()==AttributeNames.X，44 站点）
    // 6 对 × 20 属性，5 类特性轮转（Mask/MapIgnore/MapProperty/ConvertUsing/ConvertBackUsing/MapWhen）
    // ================================================================

    public class WideProperties : SourceGenFixtureBase
    {
        protected override string[] ProbeMarkers { get; } = ["WpD0", "__mask_"];

        protected override string SourceText
        {
            get
            {
                var sb = new StringBuilder("""
                    using NCMapper.Attributes;

                    namespace BenchInput;

                    public class WpMask : NCMapper.IMask
                    {
                        public string Mask(string? value) => "***";
                    }
                    public static class WpConverters
                    {
                        public static int CvtInt(int v) => v;
                    }
                    """);
                for (var p = 0; p < 6; p++)
                {
                    sb.Append($$"""

                        public class WpE{{p}}
                        {
                        """);
                    for (var i = 0; i < 20; i++)
                    {
                        var type = i < 4 ? "string" : "int";
                        sb.Append($"\n    public {type} P{i:00} {{ get; set; }}{(i < 4 ? " = \"\";" : "")}");
                    }
                    sb.Append("\n}\n");
                    sb.Append($$"""

                        [MapFrom(typeof(WpE{{p}}))]
                        public class WpD{{p}}
                        {
                            public static bool Guard(int v) => v > 0;

                            [MapIgnore] public string P00 { get; set; } = "";
                            [Mask(typeof(WpMask))] public string P01 { get; set; } = "";
                            [Mask(typeof(WpMask))] public string P02 { get; set; } = "";
                            [MapIgnore] public string P03 { get; set; } = "";
                            [Convert(typeof(WpConverters), nameof(WpConverters.CvtInt))] public int P04 { get; set; }
                            [Convert(typeof(WpConverters), nameof(WpConverters.CvtInt))] public int P05 { get; set; }
                            [MapWhen(nameof(Guard))] public int P06 { get; set; }
                            [MapWhen(nameof(Guard))] public int P07 { get; set; }
                            [MapProperty(typeof(WpE{{p}}), nameof(WpE{{p}}.P08))] public int P08 { get; set; }
                            [MapProperty(typeof(WpE{{p}}), nameof(WpE{{p}}.P09))] public int P09 { get; set; }
                            [ConvertBack(typeof(WpConverters), nameof(WpConverters.CvtInt))] public int P10 { get; set; }
                            [ConvertBack(typeof(WpConverters), nameof(WpConverters.CvtInt))] public int P11 { get; set; }
                            [Mask(typeof(WpMask))] public string P12 { get; set; } = "";
                            [MapIgnore(Direction.ToEntity)] public int P13 { get; set; }
                            [Convert(typeof(WpConverters), nameof(WpConverters.CvtInt))] public int P14 { get; set; }
                            [MapWhen(nameof(Guard))] public int P15 { get; set; }
                            [MapIgnore(Direction.ToDto)] public int P16 { get; set; }
                            [ConvertBack(typeof(WpConverters), nameof(WpConverters.CvtInt))] public int P17 { get; set; }
                            [MapProperty(typeof(WpE{{p}}), nameof(WpE{{p}}.P18))] public int P18 { get; set; }
                            [Mask(typeof(WpMask))] public string P19 { get; set; } = "";
                        }
                        """);
                }
                return sb.ToString();
            }
        }
    }

    // ================================================================
    // 诊断收集规模面
    // 8 对 ×（NCMAP001×2 + NCMAP007×2 + init-only NCMAP032（AddDistinct 去重）+ ConvertBackUsing 反向臂）
    // ================================================================

    public class DiagScale : SourceGenFixtureBase
    {
        protected override string[] ProbeMarkers { get; } = ["DsD0"];

        protected override string SourceText
        {
            get
            {
                var sb = new StringBuilder("""
                    using NCMapper.Attributes;

                    namespace BenchInput;

                    public static class DsBack
                    {
                        public static string Back(string v) => v ?? "";
                    }
                    """);
                for (var p = 0; p < 8; p++)
                {
                    sb.Append($$"""

                        public class DsE{{p}}
                        {
                            public int Id { get; set; }
                            public string Name { get; set; } = "";
                            public int ExtraA { get; set; }
                            public int ExtraB { get; set; }
                        }
                        [MapFrom(typeof(DsE{{p}}))]
                        [NCMapperOptions(ReportUnmappedTargetMembers = true)]
                        public class DsD{{p}}
                        {
                            public int Id { get; set; }
                            public string Name { get; set; } = "";
                            public int MissingX { get; set; }
                            public int MissingY { get; set; }
                            public int InitOnlyA { get; init; }
                            public int InitOnlyB { get; init; }
                            [ConvertBack(typeof(DsBack), nameof(DsBack.Back))]
                            public string Name2 { get; set; } = "";
                        }
                        """);
                }
                return sb.ToString();
            }
        }
    }

    // ================================================================
    // (类型,名)→属性 重复全类型扫描（FindProperty 族 + MappingSnapshotBuilder 族）
    // 3 个 MapperConfig × 24 声明长链（.Member(dest).From(src) ×12 + .Member(dest).ConvertBack(conv) ×6 + .Member(dest).Ignore() ×6）
    // ================================================================

    public class RepeatedDeclarations3 : SourceGenFixtureBase
    {
        protected override string[] ProbeMarkers { get; } = ["RdT0"];

        protected override string SourceText
        {
            get
            {
                var sb = new StringBuilder("""
                    using NCMapper;
                    using NCMapper.Attributes;

                    namespace BenchInput;

                    public static class RdConv
                    {
                        public static int Round(int v) => v;
                    }
                    """);
                for (var g = 0; g < 3; g++)
                {
                    sb.Append($$"""

                        public class RdS{{g}}
                        {
                        """);
                    for (var i = 0; i < 15; i++) sb.Append($"\n    public int A{i:00} {{ get; set; }}");
                    sb.Append("\n}\n");
                    sb.Append($$"""

                        public class RdT{{g}}
                        {
                        """);
                    for (var i = 0; i < 15; i++) sb.Append($"\n    public int A{i:00} {{ get; set; }}");
                    sb.Append("\n}\n");
                    sb.Append($$"""

                        [MapperConfig]
                        public class RdConfig{{g}}
                        {
                            public static void Configure(FluentMappingConfig cfg)
                            {
                                cfg.For<RdS{{g}}, RdT{{g}}>()
                        """);
                    for (var i = 0; i < 12; i++)
                        sb.Append($"\n                   .Member(d => d.A{i:00}).From(s => s.A{i:00})");
                    for (var i = 12; i < 15; i++)
                        sb.Append($"\n                   .Member(d => d.A{i:00}).ConvertBack(RdConv.Round)");
                    for (var i = 12; i < 15; i++)
                        sb.Append($"\n                   .Member(d => d.A{i:00}).Ignore()");
                    sb.Append("\n                   ;\n            }\n        }\n");
                }
                return sb.ToString();
            }
        }
    }

    // ================================================================
    // 自递归映射对（IsSelfRecursiveMapping 判定 + MapList 元素内联发射）
    // 直接自引用（Self）+ 集合自引用（Children）双形态 → 3 调用点全覆盖
    // ================================================================

    public class SelfRecursiveInline : SourceGenFixtureBase
    {
        protected override string[] ProbeMarkers { get; } = ["SrNodeDto", "MapList_"];

        protected override string SourceText => """
            using System.Collections.Generic;
            using NCMapper.Attributes;

            namespace BenchInput;

            public class SrNode
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public int Depth { get; set; }
                public SrNode? Parent { get; set; }
                public SrNode? Self { get; set; }
                public List<SrNode> Children { get; set; } = new();
            }

            [MapFrom(typeof(SrNode))]
            public class SrNodeDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public int Depth { get; set; }
                public SrNodeDto? Parent { get; set; }
                public SrNodeDto? Self { get; set; }
                public List<SrNodeDto> Children { get; set; } = new();
            }
            """;
    }

    // ================================================================
    // 深拷贝图分类（BuildDeepCopyGraphs 三段 + TypeRefDeepCopyClassifier）
    // 3 根共享 SharedAddress 子图（单值边 + 集合边）→ nodeMap EqualityKey 合并 + 每根 DFS
    // ================================================================

    public class DeepCopyGraphShared : SourceGenFixtureBase
    {
        protected override string[] ProbeMarkers { get; } = ["DcRoot1Dto", "DcSharedAddressDto"];

        protected override string SourceText
        {
            get
            {
                var sb = new StringBuilder("""
                    using System.Collections.Generic;
                    using NCMapper.Attributes;

                    namespace BenchInput;

                    public class DcSharedAddress
                    {
                        public string City { get; set; } = "";
                        public string Street { get; set; } = "";
                        public string Zip { get; set; } = "";
                    }
                    [MapFrom(typeof(DcSharedAddress))]
                    public class DcSharedAddressDto
                    {
                        public string City { get; set; } = "";
                        public string Street { get; set; } = "";
                        public string Zip { get; set; } = "";
                    }
                    """);
                for (var r = 1; r <= 3; r++)
                {
                    sb.Append($$"""

                        public class DcRoot{{r}}
                        {
                            public int Id { get; set; }
                            public DcSharedAddress? Address { get; set; }
                            public List<DcSharedAddress> History { get; set; } = new();
                        }
                        [MapFrom(typeof(DcRoot{{r}}))]
                        public class DcRoot{{r}}Dto
                        {
                            public int Id { get; set; }
                            public DcSharedAddressDto? Address { get; set; }
                            public List<DcSharedAddressDto> History { get; set; } = new();
                        }
                        """);
                }
                return sb.ToString();
            }
        }
    }

    // ================================================================
    // 子类声明物化 + Fluent 链（UnwrapChain）+ TypeRefFromSymbol
    // 端到端子类分派发射走 Fluent .MapSubclass 终端（MapSubclassDispatchGenerationTests PolySource
    // 已证形态——特性通道不产端到端分派文本，首轮实测 FsDogDto 缺席被探针拦截）；
    // 另配独立 .Map 长链对补 FindProperty/UnwrapChain 声明面。
    // ================================================================

    public class FluentChainSubclass : SourceGenFixtureBase
    {
        protected override string[] ProbeMarkers { get; } = ["Map_FsDog_To_FsDogDto", "Map_FsCat_To_FsCatDto", "FsOtherDto"];

        protected override string SourceText => """
            using NCMapper;
            using NCMapper.Attributes;

            namespace BenchInput;

            public class FsAnimal { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class FsDog : FsAnimal { public string Breed { get; set; } = ""; public bool Trained { get; set; } }
            public class FsCat : FsAnimal { public int Lives { get; set; } public bool Indoor { get; set; } }

            public class FsAnimalDto { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class FsDogDto : FsAnimalDto { public string Breed { get; set; } = ""; public bool Trained { get; set; } }
            public class FsCatDto : FsAnimalDto { public int Lives { get; set; } public bool Indoor { get; set; } }

            [MapperConfig]
            public class FsAnimalFluentMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FsAnimal, FsAnimalDto>()
                       .MapSubclass<FsDog, FsDogDto>()
                       .MapSubclass<FsCat, FsCatDto>();
                }
            }

            public class FsOther { public int A { get; set; } public int B { get; set; } public int C { get; set; } public int D { get; set; } }
            public class FsOtherDto { public int A { get; set; } public int B { get; set; } public int C { get; set; } public int D { get; set; } }

            [MapperConfig]
            public class FsOtherFluentMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<FsOther, FsOtherDto>()
                       .Member(d => d.A).From(s => s.A)
                       .Member(d => d.B).From(s => s.B)
                       .Member(d => d.C).From(s => s.C)
                       .Member(d => d.D).From(s => s.D);
                }
            }
            """;
    }
}
