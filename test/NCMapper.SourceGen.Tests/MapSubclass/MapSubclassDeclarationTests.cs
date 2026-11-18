// 测试矩阵，声明面（Fluent 提取 → 中间模型 → 快照承载 → 物化 → 合成/去重）。
// 等价对 fixture 前置（A-22 声明面）+ FQN 二元组去重查表（002）+
// 最派生优先排序（仲裁）+ 目标收敛合法（I-2）。
// fixture 前缀 MsD*（全局唯一——#76 仅追加型零碰撞纪律）。
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.MapSubclass;

public class MapSubclassDeclarationTests
{
    private static FluentExtractionResult? ExtractResult(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapperConfig")))!;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        return FluentExtractor.ExtractCore(compilation, typeSymbol!, typeDecl, System.Threading.CancellationToken.None);
    }

    private const string PolySource = """
        using NCMapper;
        public class MsDAnimal { public string Name { get; set; } = ""; }
        public class MsDDog : MsDAnimal { public string Breed { get; set; } = ""; }
        public class MsDCat : MsDAnimal { public int Lives { get; set; } }
        public class MsDAnimalDto { public string Name { get; set; } = ""; }
        public class MsDDogDto : MsDAnimalDto { public string Breed { get; set; } = ""; }
        public class MsDCatDto : MsDAnimalDto { public int Lives { get; set; } }

        [MapperConfig]
        public class MsDMapper
        {
            public static void Configure(FluentMappingConfig cfg)
            {
                cfg.For<MsDAnimal, MsDAnimalDto>()
                   .MapSubclass<MsDDog, MsDDogDto>()
                   .MapSubclass<MsDCat, MsDCatDto>();
            }
        }
        """;

    /// <summary>提取 → 物化：基类对快照 SubclassPairs 两元组（FQN 二元组承载）。</summary>
    [Fact]
    public void FluentRegistration_MaterializesSubclassPairs()
    {
        var result = ExtractResult(PolySource);
        Assert.NotNull(result);
        var snapshot = result!.Declarations.First(d => d.Snapshot.TargetType.SimpleName == "MsDAnimalDto").Snapshot;
        Assert.Equal(2, snapshot.SubclassPairs.Count);
        Assert.Contains(snapshot.SubclassPairs, sp => sp.SourceType.FullName == "MsDDog" && sp.TargetType.FullName == "MsDDogDto");
        Assert.Contains(snapshot.SubclassPairs, sp => sp.SourceType.FullName == "MsDCat" && sp.TargetType.FullName == "MsDCatDto");
        // ClassName 承载（与主生成方法名口径同源——分派链引用）
        Assert.Contains(snapshot.SubclassPairs, sp => sp.SourceClassName == "MsDDog" && sp.TargetClassName == "MsDDogDto");
    }

    /// <summary>合成：登记未独立声明的子类对 → SyntheticDeclarations 平行流（纯自动匹配）。</summary>
    [Fact]
    public void UnregisteredPair_Synthesized()
    {
        var result = ExtractResult(PolySource);
        Assert.NotNull(result);
        // 两个子类对均未独立声明 → 各产一个合成条目（含子类专有属性 Breed/Lives）
        Assert.Equal(2, result!.SyntheticDeclarations.Count);
        var dogSynth = result.SyntheticDeclarations
            .First(d => d.Snapshot.TargetType.SimpleName == "MsDDogDto").Snapshot;
        Assert.Contains(dogSynth.PropertyMappings, pm => pm.Target.Name == "Breed");
    }

    /// <summary>显式声明优先：同 Configure 内显式 For&lt;Dog,DogDto&gt; 链 → 该对不进合成流（Arbiter 消歧前置）。</summary>
    [Fact]
    public void ExplicitPair_ExcludedFromSynthetics()
    {
        var source = """
            using NCMapper;
            public class MsDAnimal { public string Name { get; set; } = ""; }
            public class MsDDog : MsDAnimal { public string Breed { get; set; } = ""; }
            public class MsDAnimalDto { public string Name { get; set; } = ""; }
            public class MsDDogDto : MsDAnimalDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class MsDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsDAnimal, MsDAnimalDto>()
                       .MapSubclass<MsDDog, MsDDogDto>();
                    cfg.For<MsDDog, MsDDogDto>()
                       .Member(d => d.Breed).From(s => s.Breed);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        // 显式链存在（Declarations 含 Dog 对）→ 合成流不含 Dog 对（显式优先——declaredKeys 本地去重）
        Assert.DoesNotContain(result!.SyntheticDeclarations, d => d.Snapshot.TargetType.SimpleName == "MsDDogDto");
        // 基类对 SubclassPairs 仍在（分派面不受显式声明影响）
        var basePair = result.Declarations.First(d => d.Snapshot.TargetType.SimpleName == "MsDAnimalDto").Snapshot;
        Assert.Single(basePair.SubclassPairs);
    }

    /// <summary>F47 邻界反例：互不重复的子类对登记（不同 TSource/TSourceDerived）→ 不报 F47。</summary>
    [Fact]
    public void DistinctSubclassPairs_NoF47()
    {
        var result = ExtractResult(PolySource);
        Assert.NotNull(result);
        var basePair = result!.Declarations.First(d => d.Snapshot.TargetType.SimpleName == "MsDAnimalDto").Snapshot;

        Assert.Equal(2, basePair.SubclassPairs.Count);
        Assert.DoesNotContain(basePair.Diagnostics, d => d.DescriptorId == "NCMAP_F47");
    }

    /// <summary>F47-① duplicate-pair：完全同对重复登记 → 首条保留 + F47 诊断（部分重叠合法不报）。</summary>
    [Fact]
    public void DuplicatePair_F47KeepsFirst()
    {
        var source = """
            using NCMapper;
            public class MsDAnimal { public string Name { get; set; } = ""; }
            public class MsDDog : MsDAnimal { public string Breed { get; set; } = ""; }
            public class MsDAnimalDto { public string Name { get; set; } = ""; }
            public class MsDDogDto : MsDAnimalDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class MsDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsDAnimal, MsDAnimalDto>()
                       .MapSubclass<MsDDog, MsDDogDto>()
                       .MapSubclass<MsDDog, MsDDogDto>();
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        var basePair = result!.Declarations.First().Snapshot;
        Assert.Single(basePair.SubclassPairs); // 首条保留
        // F47 经基类对快照诊断流（物化期 Build 注入 info.Diagnostics → MaterializeDiagnostic）
        Assert.Contains(basePair.Diagnostics, d => d.DescriptorId == "NCMAP_F47");
    }

    /// <summary>F47-② base-self：TSrcDerived == TSource（泛型约束允许自身）→ 拦截 + F47（防分派自引用死循环）。</summary>
    [Fact]
    public void BaseSelfRegistration_F47Blocked()
    {
        var source = """
            using NCMapper;
            public class MsDAnimal { public string Name { get; set; } = ""; }
            public class MsDAnimalDto { public string Name { get; set; } = ""; }
            public class MsDDogDto : MsDAnimalDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class MsDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsDAnimal, MsDAnimalDto>()
                       .MapSubclass<MsDAnimal, MsDDogDto>();
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        var basePair = result!.Declarations.First().Snapshot;
        Assert.Empty(basePair.SubclassPairs); // 基类自身登记拦截
        Assert.Contains(basePair.Diagnostics, d => d.DescriptorId == "NCMAP_F47");
    }

    /// <summary>最派生优先排序（仲裁）：登记 Dog 与 Puppy:Dog 同登 → Puppy 先（继承距离降序）。</summary>
    [Fact]
    public void OverlappingRegistration_MostDerivedFirst()
    {
        var source = """
            using NCMapper;
            public class MsDAnimal { public string Name { get; set; } = ""; }
            public class MsDDog : MsDAnimal { public string Breed { get; set; } = ""; }
            public class MsDPuppy : MsDDog { public string Toy { get; set; } = ""; }
            public class MsDAnimalDto { public string Name { get; set; } = ""; }
            public class MsDDogDto : MsDAnimalDto { public string Breed { get; set; } = ""; }
            public class MsDPuppyDto : MsDDogDto { public string Toy { get; set; } = ""; }

            [MapperConfig]
            public class MsDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsDAnimal, MsDAnimalDto>()
                       .MapSubclass<MsDDog, MsDDogDto>()
                       .MapSubclass<MsDPuppy, MsDPuppyDto>();
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        var basePair = result!.Declarations.First().Snapshot;
        Assert.Equal(2, basePair.SubclassPairs.Count);
        // 部分重叠合法（不报 F47）；Puppy（距离 2）在 Dog（距离 1）前
        Assert.Equal("MsDPuppy", basePair.SubclassPairs[0].SourceType.FullName);
        Assert.Equal("MsDDog", basePair.SubclassPairs[1].SourceType.FullName);
    }

    /// <summary>目标收敛（I-2 定案）：TTargetDerived == TTarget（MapSubclass&lt;Dog, AnimalDto&gt）合法。</summary>
    [Fact]
    public void TargetConvergence_LegalPair()
    {
        var source = """
            using NCMapper;
            public class MsDAnimal { public string Name { get; set; } = ""; }
            public class MsDDog : MsDAnimal { public string Breed { get; set; } = ""; }
            public class MsDAnimalDto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MsDMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsDAnimal, MsDAnimalDto>()
                       .MapSubclass<MsDDog, MsDAnimalDto>();
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        var basePair = result!.Declarations.First().Snapshot;
        Assert.Single(basePair.SubclassPairs);
        Assert.Equal("MsDAnimalDto", basePair.SubclassPairs[0].TargetType.FullName);
        Assert.Empty(basePair.Diagnostics); // 合法用例零诊断
    }

    /// <summary>快照相等性：SubclassPairs 参与相等性/哈希（增量缓存正确性——声明增删触发失效）。</summary>
    [Fact]
    public void SubclassPairs_ParticipateInSnapshotEquality()
    {
        var a = ExtractResult(PolySource)!.Declarations.First().Snapshot;
        var b = ExtractResult(PolySource)!.Declarations.First().Snapshot;
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        // 声明面变化（去一登记）→ 不等（缓存失效语义）
        var fewer = ExtractResult(PolySource.Replace(".MapSubclass<MsDCat, MsDCatDto>()", ""))!
            .Declarations.First().Snapshot;
        Assert.NotEqual(a, fewer);
    }
}
