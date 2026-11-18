// 测试矩阵，四族分派生成产物（四族分派格）。
// Core isinst 链（最派生优先）/ WithAction 经 Core 零改动 / _Update 双实例匹配 /
// MapList 四路径元素级（数组/ICollection/IEnumerable/带 Action 重载——重校发现的第 4 点）/
// MapListInto 双路径双实例匹配（003 两处同插）/ 深度卫语序（分派先于 ++s_depth）/
// 目标收窄对照（显式声明照字面生成）/ 无声明零分派行（G3 文本级前置）。
using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.MapSubclass;

public class MapSubclassDispatchGenerationTests
{
    private const string PolySource = """
        using NCMapper;
        public class MsGAnimal { public string Name { get; set; } = ""; }
        public class MsGDog : MsGAnimal { public string Breed { get; set; } = ""; }
        public class MsGCat : MsGAnimal { public int Lives { get; set; } }
        public class MsGAnimalDto { public string Name { get; set; } = ""; }
        public class MsGDogDto : MsGAnimalDto { public string Breed { get; set; } = ""; }
        public class MsGCatDto : MsGAnimalDto { public int Lives { get; set; } }

        [MapperConfig]
        public class MsGMapper
        {
            public static void Configure(FluentMappingConfig cfg)
            {
                cfg.For<MsGAnimal, MsGAnimalDto>()
                   .MapSubclass<MsGDog, MsGDogDto>()
                   .MapSubclass<MsGCat, MsGCatDto>();
            }
        }
        """;

    /// <summary>族 2 Map：基类 Core 体首部 isinst 分派链（isinst 可赋值匹配 + 子类方法引用）。</summary>
    [Fact]
    public void CoreMethod_ContainsIsinstDispatchChain()
    {
        var result = TestHelper.GenerateMapper(PolySource);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        // 分派链行（Dog + Cat 两条——isinst 模式变量 sub_0/sub_1）
        Assert.Contains("if (src is MsGDog sub_0) return Map_MsGDog_To_MsGDogDto(sub_0);", result.GeneratedCode);
        Assert.Contains("if (src is MsGCat sub_1) return Map_MsGCat_To_MsGCatDto(sub_1);", result.GeneratedCode);
        // 子类对方法生成（合成对全族：Core + Update + List + ListInto + 注册）
        Assert.Contains("public static MsGDogDto? Map_MsGDog_To_MsGDogDto(MsGDog src)", result.GeneratedCode);
        Assert.Contains("public static void Map_MsGDog_To_MsGDogDto_Update(MsGDog src, MsGDogDto dst", result.GeneratedCode);
    }

    /// <summary>族 2 边界：兜底不重命名（既有基类体顺序落入，无 _base 方法——002）。</summary>
    [Fact]
    public void CoreMethod_FallbackKeepsOriginalBody_NoBaseRename()
    {
        var result = TestHelper.GenerateMapper(PolySource);
        Assert.DoesNotContain("Map_MsGAnimal_To_MsGAnimalDto_base", result.GeneratedCode);
        // 基类兜底体保留（基类属性赋值仍生成）
        Assert.Contains("Map_MsGAnimal_To_MsGAnimalDto(MsGAnimal src)", result.GeneratedCode);
    }

    /// <summary>族 3 _Update：双实例匹配（src isinst && dst isinst → 子类 Update + return；action 逆变透传）。</summary>
    [Fact]
    public void UpdateMethod_DualInstanceDispatch()
    {
        var result = TestHelper.GenerateMapper(PolySource);
        Assert.Contains("if (src is MsGDog sub_0 && dst is MsGDogDto subDst_0)", result.GeneratedCode);
        Assert.Contains("Map_MsGDog_To_MsGDogDto_Update(sub_0, subDst_0, action);", result.GeneratedCode);
        // Cat 条目同构（链上第二实例匹配）
        Assert.Contains("if (src is MsGCat sub_1 && dst is MsGCatDto subDst_1)", result.GeneratedCode);
    }

    /// <summary>族 4 MapList：三路径 + 带 Action 重载（4 消费点）元素级分派全覆盖。</summary>
    [Fact]
    public void MapListMethod_FourPathsElementDispatch()
    {
        var result = TestHelper.GenerateMapper(PolySource);
        // 无 Action 三路径 + 带 Action 重载共用元素级分派（EmitMapListElementDispatch）——
        // 每条路径命中即 list.Add(子类 Core 产物)；生成文本按调用点次数断言（4 处 × 2 子类对 = 8 行）
        var dispatchAddDog = "list.Add(Map_MsGDog_To_MsGDogDto(sub_0));";
        var count = CountOccurrences(result.GeneratedCode, dispatchAddDog);
        Assert.True(count >= 3, $"MapList 三路径 + Action 重载 Dog 元素分派行应 ≥3 处，实测 {count}");
        // 带 Action 重载形态（先映射 → action → Add）
        Assert.Contains("var subDst_0 = Map_MsGDog_To_MsGDogDto(sub_0);", result.GeneratedCode);
        Assert.Contains("action(src, subDst_0);", result.GeneratedCode);
    }

    /// <summary>族 5 MapListInto：双路径（数组快速路径 + 通用索引循环）双实例匹配同插——003。</summary>
    [Fact]
    public void MapListIntoMethod_BothPathsDualInstanceDispatch()
    {
        var result = TestHelper.GenerateMapper(PolySource);
        var dispatchCall = "Map_MsGDog_To_MsGDogDto_Update(sub_0, subDst_0, action);";
        // 双路径同插 → 该调用形态出现 ≥2 次（数组路径 + 通用索引循环）
        var count = CountOccurrences(result.GeneratedCode, dispatchCall);
        Assert.True(count >= 2, $"MapListInto 双路径 Dog 双实例分派调用应 ≥2 处，实测 {count}");
        Assert.Contains("if (src is MsGDog sub_0 && dst is MsGDogDto subDst_0)", result.GeneratedCode);
    }

    /// <summary>深度卫语序（003）：自引用多态树——分派链在基类 Core ++s_depth 之前（防双重计数）。</summary>
    [Fact]
    public void DispatchPrecedes_DepthGuard_InSelfRecursiveCore()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            public class MsTNode { public string Name { get; set; } = ""; public List<MsTNode>? Children { get; set; } }
            public class MsTDogNode : MsTNode { public string Breed { get; set; } = ""; }
            public class MsTNodeDto { public string Name { get; set; } = ""; public List<MsTNodeDto>? Children { get; set; } }
            public class MsTDogNodeDto : MsTNodeDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class MsTMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsTNode, MsTNodeDto>()
                       .MapSubclass<MsTDogNode, MsTDogNodeDto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var coreStart = result.GeneratedCode.IndexOf("public static MsTNodeDto? Map_MsTNode_To_MsTNodeDto(MsTNode src)");
        Assert.True(coreStart >= 0, "基类 Core 方法未生成");
        var dispatchIdx = result.GeneratedCode.IndexOf("if (src is MsTDogNode sub_0)", coreStart);
        var depthIdx = result.GeneratedCode.IndexOf("++s_depth_Map_MsTNode_To_MsTNodeDto", coreStart);
        Assert.True(dispatchIdx >= 0, "分派链未生成");
        Assert.True(depthIdx >= 0, "自引用深度卫未生成");
        // 语序断言：分派先于深度卫（同方法体内）
        Assert.True(dispatchIdx < depthIdx, "分派链必须在 ++s_depth 深度卫之前（003 防基类+子类双重计数）");
    }

    /// <summary>目标收窄对（003）：TTargetDerived 更派生（MapSubclass&lt;Dog, PuppyDto&gt）——显式声明照字面生成。</summary>
    [Fact]
    public void TargetNarrowing_LiteralGeneration()
    {
        var source = """
            using NCMapper;
            public class MsNAnimal { public string Name { get; set; } = ""; }
            public class MsNDog : MsNAnimal { public string Breed { get; set; } = ""; }
            public class MsNAnimalDto { public string Name { get; set; } = ""; }
            public class MsNDogDto : MsNAnimalDto { public string Breed { get; set; } = ""; }
            public class MsNPuppyDto : MsNDogDto { public string Toy { get; set; } = ""; }

            [MapperConfig]
            public class MsNMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsNAnimal, MsNAnimalDto>()
                       .MapSubclass<MsNDog, MsNPuppyDto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        // 照字面生成：(Dog, PuppyDto) 子类对（Dog 面 + 基类共有属性 → PuppyDto），不纠偏到 DogDto
        Assert.Contains("Map_MsNDog_To_MsNPuppyDto", result.GeneratedCode);
        Assert.Contains("if (src is MsNDog sub_0) return Map_MsNDog_To_MsNPuppyDto(sub_0);", result.GeneratedCode);
    }

    /// <summary>G3 前置（文本级）：无 MapSubclass 声明的对——生成产物零分派行（无 sub_ 模式变量）。</summary>
    [Fact]
    public void NoRegistration_ZeroDispatchLines()
    {
        var source = """
            using NCMapper;
            public class MsZAnimal { public string Name { get; set; } = ""; }
            public class MsZDog : MsZAnimal { public string Breed { get; set; } = ""; }
            public class MsZAnimalDto { public string Name { get; set; } = ""; }
            public class MsZDogDto : MsZAnimalDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class MsZMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsZAnimal, MsZAnimalDto>()
                       .Member(d => d.Name).From(s => s.Name);
                    cfg.For<MsZDog, MsZDogDto>()
                       .Member(d => d.Breed).From(s => s.Breed);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        // 零分派行（isinst 模式变量 sub_N / subDst_N 不出现——字节门文本级前置；正式字节门走 Linux 全量比对）
        Assert.DoesNotContain(" sub_0)", result.GeneratedCode);
        Assert.DoesNotContain(" subDst_0)", result.GeneratedCode);
    }

    /// <summary>嵌套引用（族 6）：嵌套属性赋值经基类对方法调用形态 → 分派自然覆盖（零专门嵌套分派行）。</summary>
    [Fact]
    public void NestedReference_RoutesThroughBaseCore()
    {
        var source = """
            using NCMapper;
            public class MsQOwner { public MsQAnimal Pet { get; set; } = new(); }
            public class MsQAnimal { public string Name { get; set; } = ""; }
            public class MsQDog : MsQAnimal { public string Breed { get; set; } = ""; }
            public class MsQOwnerDto { public MsQAnimalDto Pet { get; set; } = new(); }
            public class MsQAnimalDto { public string Name { get; set; } = ""; }
            public class MsQDogDto : MsQAnimalDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class MsQMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsQOwner, MsQOwnerDto>()
                       .Member(d => d.Pet).From(s => s.Pet);
                    cfg.For<MsQAnimal, MsQAnimalDto>()
                       .MapSubclass<MsQDog, MsQDogDto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        // 嵌套赋值经 GeneratedMapper.Map_MsQAnimal_To_MsQAnimalDto（基类 Core——分派链在 Core 内）
        Assert.Contains("GeneratedMapper.Map_MsQAnimal_To_MsQAnimalDto", result.GeneratedCode);
        Assert.Contains("if (src is MsQDog sub_0) return Map_MsQDog_To_MsQDogDto(sub_0);", result.GeneratedCode);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
