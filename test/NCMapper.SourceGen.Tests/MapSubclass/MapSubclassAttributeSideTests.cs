// 测试矩阵，特性侧（[MapSubclass]——O-1 方案 1，落地面）。
// O-1 Spike Q1~Q4 复核：[MapFrom] 锚定提取 / 合成管道 / Inherited=false 无传播
//（Q1 判据复核）/ F46 继承校验（B10 先验锚——合法登记正例先行）/ F49 孤儿锚定缺失（Q2 实证复核）。
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.MapSubclass;

public class MapSubclassAttributeSideTests
{
    private static MappingSnapshot? ExtractMapFromCore(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapFrom")))!;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        var mapFromAttr = typeSymbol!.GetAttributes().First(a =>
            a.AttributeClass?.ToDisplayString() == AttributeNames.MapFrom);
        var sourceType = (INamedTypeSymbol?)mapFromAttr.ConstructorArguments[0].Value;
        return MappingExtractor.ExtractMappingCore(typeSymbol, sourceType!, typeDecl,
            System.Threading.CancellationToken.None);
    }

    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        public class MsAAnimal { public string Name { get; set; } = ""; }
        public class MsADog : MsAAnimal { public string Breed { get; set; } = ""; }
        public class MsACat : MsAAnimal { public int Lives { get; set; } }
        public class MsAAnimalDto { public string Name { get; set; } = ""; }
        public class MsADogDto : MsAAnimalDto { public string Breed { get; set; } = ""; }
        public class MsACatDto : MsAAnimalDto { public int Lives { get; set; } }

        [MapFrom(typeof(MsAAnimal))]
        [MapSubclass(typeof(MsADog), typeof(MsADogDto))]
        [MapSubclass(typeof(MsACat), typeof(MsACatDto))]
        public class MsAAnimalDto2 { public string Name { get; set; } = ""; }
        """;

    /// <summary>Q2 复核：[MapFrom] 锚点类单入口回溯全部 [MapSubclass]（多实例不折叠）。</summary>
    [Fact]
    public void MapFromAnchor_CollectsAllRegistrations()
    {
        var source = AttrSource.Replace("MsAAnimalDto2", "MsAAnimalDto");
        var snapshot = ExtractMapFromCore(source);
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.SubclassPairs.Count);
        Assert.Contains(snapshot.SubclassPairs, sp => sp.SourceType.FullName == "MsADog");
        Assert.Contains(snapshot.SubclassPairs, sp => sp.SourceType.FullName == "MsACat");
        Assert.Empty(snapshot.Diagnostics.Where(d => d.DescriptorId.StartsWith("NCMAP_F4")));
    }

    /// <summary>Q1 复核：Inherited=false——派生 DTO 不携带父类登记（符号面 GetAttributes 天然零传播）。</summary>
    [Fact]
    public void InheritedFalse_DerivedDtoCarriesNoRegistration()
    {
        var source = AttrSource.Replace("MsAAnimalDto2", "MsAAnimalDto");
        var compilation = TestHelper.BuildCompilation(source);
        var dogDto = compilation.GetSymbolsWithName("MsADogDto").OfType<INamedTypeSymbol>().First();
        // Roslyn 符号面：派生类 GetAttributes() 零 MapSubclass（编译期天然零传播——Spike Q1 实测）
        Assert.DoesNotContain(dogDto.GetAttributes(),
            a => a.AttributeClass?.ToDisplayString() == AttributeNames.MapSubclass);
    }

    /// <summary>双角色（Q4 复核）：DogDto 自身 [MapFrom] 独立对 + 被基类 [MapSubclass] 引用——互不污染。</summary>
    [Fact]
    public void DualRole_IndependentDeclarations()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MsAAnimal { public string Name { get; set; } = ""; }
            public class MsADog : MsAAnimal { public string Breed { get; set; } = ""; }
            public class MsADogExtra { public string Extra { get; set; } = ""; }
            public class MsAAnimalDto { public string Name { get; set; } = ""; }

            [MapFrom(typeof(MsAAnimal))]
            [MapSubclass(typeof(MsADog), typeof(MsADogDto))]
            public class MsAAnimalDtoX { public string Name { get; set; } = ""; }

            [MapFrom(typeof(MsADogExtra))]
            public class MsADogDto : MsAAnimalDtoX { public string Breed { get; set; } = ""; }
            """;
        // 基类 DTO（MsAAnimalDtoX）提取：SubclassPairs 含 Dog 对（引用角色正常）
        var snapshot = ExtractMapFromCore(source.Replace("MsAAnimalDtoX", "MsAAnimalDtoB"));
        // 修正：直接提取基类 DTO（MapFrom(MsAAnimal) 锚点）
        var compilation = TestHelper.BuildCompilation(source);
        var baseDto = compilation.GetSymbolsWithName("MsAAnimalDtoX").OfType<INamedTypeSymbol>().First();
        var mapFromAttr = baseDto.GetAttributes().First(a =>
            a.AttributeClass?.ToDisplayString() == AttributeNames.MapFrom);
        var sourceType = (INamedTypeSymbol)mapFromAttr.ConstructorArguments[0].Value;
        var baseSnapshot = MappingExtractor.ExtractMappingCore(baseDto, sourceType,
            (TypeDeclarationSyntax)baseDto.DeclaringSyntaxReferences[0].GetSyntax(),
            System.Threading.CancellationToken.None);
        Assert.Single(baseSnapshot!.SubclassPairs);
        Assert.Equal("MsADog", baseSnapshot.SubclassPairs[0].SourceType.FullName);
        // DogDto 自身独立对（[MapFrom(DogExtra)]）正常提取（SubclassPairs 空——无自身登记）
        var dogDtoSymbol = compilation.GetSymbolsWithName("MsADogDto").OfType<INamedTypeSymbol>().First();
        var dogFromAttr = dogDtoSymbol.GetAttributes().First(a =>
            a.AttributeClass?.ToDisplayString() == AttributeNames.MapFrom);
        var dogSnapshot = MappingExtractor.ExtractMappingCore(dogDtoSymbol,
            (INamedTypeSymbol)dogFromAttr.ConstructorArguments[0].Value,
            (TypeDeclarationSyntax)dogDtoSymbol.DeclaringSyntaxReferences[0].GetSyntax(),
            System.Threading.CancellationToken.None);
        Assert.Empty(dogSnapshot!.SubclassPairs);
        Assert.NotNull(snapshot); // 消除未用变量（上方防御提取保留）
    }

    /// <summary>F46（B10 先验锚——合法登记正例先行证明提取链工作）：继承不满足 → F46 + 登记丢弃。</summary>
    [Fact]
    public void InheritanceViolation_F46_WithPriorAnchor()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MsAAnimal { public string Name { get; set; } = ""; }
            public class MsADog : MsAAnimal { public string Breed { get; set; } = ""; }
            public class MsAUnrelated { public string X { get; set; } = ""; }

            [MapFrom(typeof(MsAAnimal))]
            [MapSubclass(typeof(MsADog), typeof(MsADogDto))]
            [MapSubclass(typeof(MsAUnrelated), typeof(MsAUnrelatedDto))]
            public class MsAAnimalDto { public string Name { get; set; } = ""; }
            public class MsADogDto : MsAAnimalDto { public string Breed { get; set; } = ""; }
            public class MsAUnrelatedDto { public string X { get; set; } = ""; }
            """;
        var compilation = TestHelper.BuildCompilation(source);
        var baseDto = compilation.GetSymbolsWithName("MsAAnimalDto").OfType<INamedTypeSymbol>().First();
        var mapFromAttr = baseDto.GetAttributes().First(a =>
            a.AttributeClass?.ToDisplayString() == AttributeNames.MapFrom);
        var sourceType = (INamedTypeSymbol)mapFromAttr.ConstructorArguments[0].Value;
        var snapshot = MappingExtractor.ExtractMappingCore(baseDto, sourceType,
            (TypeDeclarationSyntax)baseDto.DeclaringSyntaxReferences[0].GetSyntax(),
            System.Threading.CancellationToken.None);
        // 先验锚：合法 Dog 登记存活（提取链工作证明——防 fixture 编译失败 vacuous pass，#67）
        Assert.Single(snapshot!.SubclassPairs);
        Assert.Equal("MsADog", snapshot.SubclassPairs[0].SourceType.FullName);
        // 负例：非派生源（MsAUnrelated 非 MsAAnimal 派生）→ F46 + 丢弃
        Assert.Contains(snapshot.Diagnostics, d => d.DescriptorId == "NCMAP_F46");
    }

    /// <summary>F49（Q2 复核）：孤儿 [MapSubclass]（无 [MapFrom] 锚定）→ 合成管道 F49 + 零合成。</summary>
    [Fact]
    public void OrphanAnnotation_F49()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MsAAnimal { public string Name { get; set; } = ""; }
            public class MsADog : MsAAnimal { public string Breed { get; set; } = ""; }
            public class MsAOrphanDto { public string Name { get; set; } = ""; }
            public class MsADogDto : MsAOrphanDto { public string Breed { get; set; } = ""; }

            [MapSubclass(typeof(MsADog), typeof(MsADogDto))]
            public class MsAOrphanDtoX { public string Name { get; set; } = ""; }
            """;
        // 合成管道 transform 直调（GeneratorAttributeSyntaxContext 不可直构——经完整驱动跑）
        var result = TestHelper.GenerateMapper(source);
        // 先验锚：生成器驱动正常运行（无 F02 级崩溃——提取链工作）
        Assert.NotNull(result);
        // F49：孤儿登记锚定缺失（编译期诊断）
        Assert.Contains(result.Diagnostics, d => d.Descriptor.Id == "NCMAP_F49");
        // 零合成：孤儿不产子类对方法（DogDto 无独立声明 → 不生成 Map_MsADog_To_MsADogDto）
        Assert.DoesNotContain("Map_MsADog_To_MsADogDto", result.GeneratedCode);
    }

    /// <summary>特性侧合成：[MapFrom] 锚定 + 未声明子类对 → 生成产物含子类对全族方法（管线级）。</summary>
    [Fact]
    public void AttributeSideSynthesis_GeneratesSubclassPair()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MsAAnimal { public string Name { get; set; } = ""; }
            public class MsADog : MsAAnimal { public string Breed { get; set; } = ""; }

            [MapFrom(typeof(MsAAnimal))]
            [MapSubclass(typeof(MsADog), typeof(MsADogDto))]
            public class MsAAnimalDto { public string Name { get; set; } = ""; }
            public class MsADogDto : MsAAnimalDto { public string Breed { get; set; } = ""; }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // 基类对分派链 + 合成子类对方法（特性侧经 SubclassSyntheticPipeline → Arbiter → mappings）
        Assert.Contains("Map_MsAAnimal_To_MsAAnimalDto", result.GeneratedCode);
        Assert.Contains("if (src is MsADog sub_0) return Map_MsADog_To_MsADogDto(sub_0);", result.GeneratedCode);
        Assert.Contains("public static MsADogDto? Map_MsADog_To_MsADogDto(MsADog src)", result.GeneratedCode);
    }

    /// <summary>F47 特性侧：duplicate-pair / base-self 同报（与 Fluent 侧同码族）。</summary>
    [Fact]
    public void AttributeSide_F47DuplicateAndSelf()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MsAAnimal { public string Name { get; set; } = ""; }
            public class MsADog : MsAAnimal { public string Breed { get; set; } = ""; }

            [MapFrom(typeof(MsAAnimal))]
            [MapSubclass(typeof(MsADog), typeof(MsADogDto))]
            [MapSubclass(typeof(MsADog), typeof(MsADogDto))]
            [MapSubclass(typeof(MsAAnimal), typeof(MsAAnimalDto))]
            public class MsAAnimalDto { public string Name { get; set; } = ""; }
            public class MsADogDto : MsAAnimalDto { public string Breed { get; set; } = ""; }
            """;
        var compilation = TestHelper.BuildCompilation(source);
        var baseDto = compilation.GetSymbolsWithName("MsAAnimalDto").OfType<INamedTypeSymbol>().First();
        var mapFromAttr = baseDto.GetAttributes().First(a =>
            a.AttributeClass?.ToDisplayString() == AttributeNames.MapFrom);
        var sourceType = (INamedTypeSymbol)mapFromAttr.ConstructorArguments[0].Value;
        var snapshot = MappingExtractor.ExtractMappingCore(baseDto, sourceType,
            (TypeDeclarationSyntax)baseDto.DeclaringSyntaxReferences[0].GetSyntax(),
            System.Threading.CancellationToken.None);
        // 首条 Dog 保留 + duplicate F47 + base-self F47（两条各一）
        Assert.Single(snapshot!.SubclassPairs);
        var f47s = snapshot.Diagnostics.Where(d => d.DescriptorId == "NCMAP_F47").ToList();
        Assert.Equal(2, f47s.Count);
    }
}
