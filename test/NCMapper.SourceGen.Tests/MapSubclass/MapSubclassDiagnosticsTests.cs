// 测试矩阵，诊断矩阵收口（F48 落空 / F50 Info 静态 advisory）+
// resx 三键双语断言（neutral + zh-CN——#35 降级判读口径，G8 验收）。
// F47/F46/F49 见 MapSubclassDeclarationTests / MapSubclassAttributeSideTests（B10 先验锚在案）。
using System.Globalization;
using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.MapSubclass;

public class MapSubclassDiagnosticsTests
{
    /// <summary>F48 落空（B10 先验锚）：合法登记 + 落空登记（目标无可映射公共属性）并存——合法者存活、落空者报 F48。</summary>
    [Fact]
    public void EmptyTargetSurface_F48_WithPriorAnchor()
    {
        var source = """
            using NCMapper;
            public class MsEAnimal { public string Name { get; set; } = ""; }
            public class MsEDog : MsEAnimal { public string Breed { get; set; } = ""; }
            public class MsECat : MsEAnimal { public int Lives { get; set; } }
            public class MsEAnimalDto { public string Name { get; set; } = ""; }
            public class MsEDogDto : MsEAnimalDto { public string Breed { get; set; } = ""; }
            // 落空目标：无任何公共可写属性（合成快照零 PropertyMappings）
            public class MsEEmptyDto { private int _hidden; }

            [MapperConfig]
            public class MsEMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsEAnimal, MsEAnimalDto>()
                       .MapSubclass<MsEDog, MsEDogDto>()
                       .MapSubclass<MsECat, MsEEmptyDto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 先验锚：合法 Dog 登记产物在案（提取链工作证明——防 vacuous pass，#67）
        Assert.Contains("Map_MsEDog_To_MsEDogDto", result.GeneratedCode);
        // F48：落空登记（合成快照零可映射属性）fail-fast 上报
        Assert.Contains(result.Diagnostics, d => d.Descriptor.Id == "NCMAP_F48");
        // 落空对不生成（generatableKeys 防线——防 CS0103 引用断裂）
        Assert.DoesNotContain("Map_MsECat_To_MsEEmptyDto", result.GeneratedCode);
    }

    /// <summary>F48 邻界反例：目标有可映射公共属性（非空表面）→ 登记存活且不报 F48。</summary>
    [Fact]
    public void NonEmptyTargetSurface_NoF48()
    {
        var source = """
            using NCMapper;
            public class MsHAnimal { public string Name { get; set; } = ""; }
            public class MsHDog : MsHAnimal { public string Breed { get; set; } = ""; }
            public class MsHAnimalDto { public string Name { get; set; } = ""; }
            public class MsHDogDto : MsHAnimalDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class MsHMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsHAnimal, MsHAnimalDto>()
                       .MapSubclass<MsHDog, MsHDogDto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 先验锚：子类对生成（防 vacuous pass）
        Assert.Contains("Map_MsHDog_To_MsHDogDto", result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Descriptor.Id == "NCMAP_F48");
    }

    /// <summary>F50 Info 静态 advisory（②反向不对称 + ③双实例匹配——编译期，每对一次）。</summary>
    [Fact]
    public void RegistrationEmits_F50Advisory()
    {
        var source = """
            using NCMapper;
            public class MsFAnimal { public string Name { get; set; } = ""; }
            public class MsFDog : MsFAnimal { public string Breed { get; set; } = ""; }
            public class MsFAnimalDto { public string Name { get; set; } = ""; }
            public class MsFDogDto : MsFAnimalDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class MsFMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsFAnimal, MsFAnimalDto>()
                       .MapSubclass<MsFDog, MsFDogDto>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // F50：Info 级静态 advisory（正向分派 + 反向自动生成 → reverse-asymmetry + dual-instance-fallback）
        var f50 = result.Diagnostics.Where(d => d.Descriptor.Id == "NCMAP_F50").ToList();
        Assert.Single(f50); // 每对一次
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, f50[0].Severity);
        Assert.Contains("reverse-asymmetry", f50[0].GetMessage());
        Assert.Contains("dual-instance-fallback", f50[0].GetMessage());
    }

    /// <summary>F50 对照：无登记对零 F50（G3 语义——诊断面同发射条件）。</summary>
    [Fact]
    public void NoRegistration_ZeroF50()
    {
        var source = """
            using NCMapper;
            public class MsGAnimal { public string Name { get; set; } = ""; }
            public class MsGAnimalDto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MsGMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MsGAnimal, MsGAnimalDto>()
                       .Member(d => d.Name).From(s => s.Name);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Descriptor.Id == "NCMAP_F50");
    }

    /// <summary>resx 三键双语（G8 验收）：F46~F50 的 Title/Message/Description 在 neutral 与 zh-CN 双侧齐备（#35）。</summary>
    [Theory]
    [InlineData("NCMAP_F46")]
    [InlineData("NCMAP_F47")]
    [InlineData("NCMAP_F48")]
    [InlineData("NCMAP_F49")]
    [InlineData("NCMAP_F50")]
    public void ResxKeys_NeutralAndZhCN_BothPresent(string id)
    {
        // 手写强类型资源访问器（resx 源生成关闭——Designer 提交入库，Q3）：
        // ResourceManager 按 culture 回退链读取（InvariantCulture → neutral；zh-CN → 卫星）
        var manager = NCMapper.SourceGen.Resources.DiagnosticResources.ResourceManager;

        foreach (var suffix in new[] { "Title", "Message", "Description" })
        {
            var key = $"{id}_{suffix}";
            var neutral = manager.GetString(key, CultureInfo.InvariantCulture);
            Assert.False(string.IsNullOrEmpty(neutral), $"neutral 缺键 {key}");
            var zh = manager.GetString(key, new CultureInfo("zh-CN"));
            Assert.False(string.IsNullOrEmpty(zh), $"zh-CN 缺键 {key}");
            // 双语非同文（zh 侧中文承载——防键复制粘贴同文）
            Assert.NotEqual(neutral, zh);
        }
    }
}
