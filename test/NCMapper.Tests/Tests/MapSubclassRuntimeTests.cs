// 测试矩阵，运行时分派行为（生成注册实跑——分派矩阵格）。
// isinst 可赋值匹配：基类实例/各子类/未登记更深派生（Puppy→最近登记祖先层 DogDto）/
// 未登记第三方子类（Bird→基类兜底）/重叠登记最派生优先；四族运行时（Map/Update 双实例/
// MapList 元素级/MapListInto 双路径）；反向不分派（O-2）。
// 隔离纪律（#60/#76）：[Collection("GlobalConfigState")] + 层1 委托幂等重注册
//（与 module initializer 同一委托——注册→使用窗口压至语句级）。
using System.Collections.Generic;
using System.Linq;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

// ── fixture（MsR 前缀全局唯一——#76 仅追加型零碰撞）──
public class MsRAnimal { public string Name { get; set; } = ""; }
public class MsRDog : MsRAnimal { public string Breed { get; set; } = ""; }
public class MsRPuppy : MsRDog { public string Toy { get; set; } = ""; }
public class MsRCat : MsRAnimal { public int Lives { get; set; } }
public class MsRBird : MsRAnimal { public string Wing { get; set; } = ""; }
public class MsRAnimalDto { public string Name { get; set; } = ""; }
public class MsRDogDto : MsRAnimalDto { public string Breed { get; set; } = ""; }
public class MsRPuppyDto : MsRDogDto { public string Toy { get; set; } = ""; }
public class MsRCatDto : MsRAnimalDto { public int Lives { get; set; } }

[MapperConfig]
public class MsRMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<MsRAnimal, MsRAnimalDto>()
           .MapSubclass<MsRDog, MsRDogDto>()
           .MapSubclass<MsRCat, MsRCatDto>();
    }
}

// 重叠登记场景（最派生优先仲裁运行时）：Dog + Puppy 同登 → Puppy 实例路由 PuppyDto
public class MsROAnimal { public string Name { get; set; } = ""; }
public class MsRODog : MsROAnimal { public string Breed { get; set; } = ""; }
public class MsROPuppy : MsRODog { public string Toy { get; set; } = ""; }
public class MsROAnimalDto { public string Name { get; set; } = ""; }
public class MsRODogDto : MsROAnimalDto { public string Breed { get; set; } = ""; }
public class MsROPuppyDto : MsRODogDto { public string Toy { get; set; } = ""; }

[MapperConfig]
public class MsROMapper
{
    public static void Configure(FluentMappingConfig cfg)
    {
        cfg.For<MsROAnimal, MsROAnimalDto>()
           .MapSubclass<MsRODog, MsRODogDto>()
           .MapSubclass<MsROPuppy, MsROPuppyDto>();
    }
}

/// <summary>
/// MapSubclass 运行时分派矩阵——生成代码经 module initializer 注册（层1 委托），
/// Mapper.Map/MapList/Update 走生成分派链（零反射 isinst）。
/// </summary>
[Collection("GlobalConfigState")]
public class MapSubclassRuntimeTests
{
    /// <summary>层1 注册自足（#60）：与生成端 module initializer 同一委托幂等重注册。</summary>
    private static void EnsureRegistered()
    {
        Mapper.RegisterGeneratedCore<MsRAnimal, MsRAnimalDto>(NCMapper.Generated.GeneratedMapper.Map_MsRAnimal_To_MsRAnimalDto);
        Mapper.RegisterGeneratedCore<MsRDog, MsRDogDto>(NCMapper.Generated.GeneratedMapper.Map_MsRDog_To_MsRDogDto);
        Mapper.RegisterGeneratedCore<MsRCat, MsRCatDto>(NCMapper.Generated.GeneratedMapper.Map_MsRCat_To_MsRCatDto);
        Mapper.RegisterGeneratedCore<MsROAnimal, MsROAnimalDto>(NCMapper.Generated.GeneratedMapper.Map_MsROAnimal_To_MsROAnimalDto);
        Mapper.RegisterGeneratedCore<MsRODog, MsRODogDto>(NCMapper.Generated.GeneratedMapper.Map_MsRODog_To_MsRODogDto);
        Mapper.RegisterGeneratedCore<MsROPuppy, MsROPuppyDto>(NCMapper.Generated.GeneratedMapper.Map_MsROPuppy_To_MsROPuppyDto);
    }

    // ── 分派矩阵（B-2 isinst 可赋值匹配）──────────────────────

    /// <summary>基类实例 → 基类兜底（AnimalDto，基类属性）。</summary>
    [Fact]
    public void Map_BaseInstance_BaseFallback()
    {
        EnsureRegistered();
        var dto = Mapper.Map<MsRAnimal, MsRAnimalDto>(new MsRAnimal { Name = "Generic" });
        Assert.IsType<MsRAnimalDto>(dto);
        Assert.Equal("Generic", dto!.Name);
    }

    /// <summary>登记子类 → 路由子类对（DogDto + 专有字段 Breed）。</summary>
    [Fact]
    [Trait("Capability", "A-22")] // P5④ 回补——Fluent MapSubclass 多态分派整机锚
    public void Map_RegisteredDog_RoutesToDogDto()
    {
        EnsureRegistered();
        var dto = Mapper.Map<MsRAnimal, MsRAnimalDto>(new MsRDog { Name = "Rex", Breed = "Lab" });
        var dogDto = Assert.IsType<MsRDogDto>(dto);
        Assert.Equal("Rex", dogDto.Name);
        Assert.Equal("Lab", dogDto.Breed);
    }

    /// <summary>未登记更深派生（Puppy : Dog，未登记）→ isinst 可赋值匹配命中最近登记祖先层 DogDto（非兜底）。</summary>
    [Fact]
    public void Map_UnregisteredDeeperPuppy_MatchesNearestAncestorDog()
    {
        EnsureRegistered();
        var dto = Mapper.Map<MsRAnimal, MsRAnimalDto>(new MsRPuppy { Name = "Bit", Breed = "Corgi", Toy = "Ball" });
        var dogDto = Assert.IsType<MsRDogDto>(dto);
        Assert.Equal("Bit", dogDto.Name);
        Assert.Equal("Corgi", dogDto.Breed); // 经 Dog 对（isinst 可赋值——Breed 保留）
    }

    /// <summary>未登记第三方子类（Bird，无登记祖先）→ 基类兜底（专有字段丢失——语义边界）。</summary>
    [Fact]
    public void Map_UnregisteredThirdParty_FallsBackToBase()
    {
        EnsureRegistered();
        var dto = Mapper.Map<MsRAnimal, MsRAnimalDto>(new MsRBird { Name = "Tweety", Wing = "Long" });
        Assert.IsType<MsRAnimalDto>(dto); // 基类兜底（非 DogDto/CatDto）
        Assert.Equal("Tweety", dto!.Name);
    }

    /// <summary>重叠登记最派生优先（仲裁）：Dog + Puppy 同登 → Puppy 实例路由 PuppyDto（最派生）。</summary>
    [Fact]
    public void Map_OverlappingRegistration_MostDerivedWins()
    {
        EnsureRegistered();
        var dto = Mapper.Map<MsROAnimal, MsROAnimalDto>(new MsROPuppy { Name = "Pip", Breed = "Pom", Toy = "Rope" });
        var puppyDto = Assert.IsType<MsROPuppyDto>(dto);
        Assert.Equal("Rope", puppyDto.Toy);
        // Dog 实例（非 Puppy）仍路由 DogDto
        var dogDto = Mapper.Map<MsROAnimal, MsROAnimalDto>(new MsRODog { Name = "Rex", Breed = "Lab" });
        Assert.IsType<MsRODogDto>(dogDto);
    }

    // ── 四族运行时 ──────────────────────────────────────────────

    /// <summary>MapList 元素级分派：混合集合各元素按运行时类型路由（多态集合主场景）。</summary>
    [Fact]
    public void MapList_MixedCollection_ElementLevelDispatch()
    {
        EnsureRegistered();
        List<MsRAnimal> animals = new()
        {
            new MsRAnimal { Name = "A" },
            new MsRDog { Name = "B", Breed = "Boxer" },
            new MsRCat { Name = "C", Lives = 9 },
            new MsRPuppy { Name = "D", Breed = "Husky", Toy = "Bone" },
        };
        var dtos = Mapper.MapList<MsRAnimal, MsRAnimalDto>(animals);
        Assert.Equal(4, dtos!.Count);
        Assert.IsType<MsRAnimalDto>(dtos[0]);
        var dog = Assert.IsType<MsRDogDto>(dtos[1]);
        Assert.Equal("Boxer", dog.Breed);
        var cat = Assert.IsType<MsRCatDto>(dtos[2]);
        Assert.Equal(9, cat.Lives);
        var puppy = Assert.IsType<MsRDogDto>(dtos[3]); // 未登记更深 → 最近祖先层
        Assert.Equal("Husky", puppy.Breed);
    }

    /// <summary>_Update 双实例匹配：src=Dog && dst=DogDto → 子类 Update（Breed 写入）。</summary>
    [Fact]
    public void Update_DualInstanceMatch_RoutesSubclassUpdate()
    {
        EnsureRegistered();
        var src = new MsRDog { Name = "Rex", Breed = "NewBreed" };
        var dst = new MsRDogDto { Name = "Old", Breed = "OldBreed" };
        Mapper.Map<MsRAnimal, MsRAnimalDto>(src, dst);
        Assert.Equal("NewBreed", dst.Breed); // 双实例匹配 → 子类 Update 写入专有字段
    }

    /// <summary>_Update dst 固定型回落（002 B-1）：src=Dog && dst=AnimalDto（运行时非子类目标）→ 基类体（Breed 不写入）。</summary>
    [Fact]
    public void Update_DstRuntimeBase_FallsBackToBaseBody()
    {
        EnsureRegistered();
        var src = new MsRDog { Name = "Rex", Breed = "NewBreed" };
        var dst = new MsRAnimalDto { Name = "Old" };
        Mapper.Map<MsRAnimal, MsRAnimalDto>(src, dst);
        Assert.Equal("Rex", dst.Name); // 基类体仍更新共有属性
        // Breed 不写入（dst 固定型——基类 DTO 无该字段，残留面 F50③ Info 可见）
    }

    /// <summary>MapListInto 双路径（003）：数组入参与 IList 入参均双实例匹配分派。</summary>
    [Fact]
    public void MapListInto_BothPaths_DualInstanceDispatch()
    {
        EnsureRegistered();
        // 路径①数组快速路径（sources is T[]）
        MsRAnimal[] arr = { new MsRDog { Name = "A", Breed = "A1" }, new MsRAnimal { Name = "B" } };
        var dstArr = new MsRAnimalDto[] { new MsRDogDto(), new MsRAnimalDto() };
        Mapper.MapListInto(arr, dstArr, null);
        Assert.Equal("A1", ((MsRDogDto)dstArr[0]!).Breed); // 数组路径双实例匹配 → 子类 Update

        // 路径②通用索引循环（IList<T>）
        List<MsRAnimal> list = new() { new MsRDog { Name = "C", Breed = "C1" } };
        var dstList = new MsRAnimalDto[] { new MsRDogDto() };
        Mapper.MapListInto(list, dstList, null);
        Assert.Equal("C1", ((MsRDogDto)dstList[0]!).Breed); // 通用索引路径同插
    }

    // ── 反向族（O-2：反向基类对不分派）────────────────────────

    /// <summary>反向不分派（002 B-2 裁定）：Map&lt;DogDto, Animal&gt;(dogDto) → 基类字段（Breed 不反向写入——dst 固定型）。</summary>
    [Fact]
    public void Reverse_BasePair_DoesNotDispatch()
    {
        EnsureRegistered();
        var dogDto = new MsRDogDto { Name = "Rex", Breed = "Lab" };
        var entity = Mapper.Map<MsRAnimalDto, MsRAnimal>(dogDto);
        Assert.IsType<MsRAnimal>(entity); // 反向基类对——不路由（无 MsRDog 生成体）
        Assert.Equal("Rex", entity!.Name);
    }

    // ── 深度卫语序（003 运行时格）────────────────────────

    /// <summary>深多态批量不提前触发 MaxRecursionDepth：分派先于基类 Core ++s_depth（生成层
    /// 自引用树 IndexOf 断言见 MapSubclassDispatchGenerationTests.DispatchPrecedes_DepthGuard_InSelfRecursiveCore）——
    /// 运行时以深多态批量实跑不抛 RecursionDepthExceeded（若分派被插入深度卫之后，
    /// 基类+子类双重计数将提前触发）。</summary>
    [Fact]
    public void DeepPolymorphicBatch_NoPrematureRecursionDepthExceeded()
    {
        EnsureRegistered();
        var deep = new List<MsRAnimal>();
        for (var i = 0; i < 30; i++)
        {
            deep.Add(i % 2 == 0
                ? new MsRDog { Name = $"D{i}", Breed = "X" }
                : new MsRAnimal { Name = $"A{i}" });
        }
        var exception = Record.Exception(() => Mapper.MapList<MsRAnimal, MsRAnimalDto>(deep));
        Assert.Null(exception); // 不提前触发（分派先于深度卫——语序正确的运行时投影）
        Assert.Equal(30, Mapper.MapList<MsRAnimal, MsRAnimalDto>(deep)!.Count);
    }
}
