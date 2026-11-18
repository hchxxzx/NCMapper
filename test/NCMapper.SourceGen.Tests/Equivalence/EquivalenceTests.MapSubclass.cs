using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-22 MapSubclass — 多态子类型对登记双通道等价对（第七批能力登记）。
/// <para>特性 [MapSubclass(typeof(A22Dog), typeof(A22DogDto))] ↔ Fluent .MapSubclass&lt;A22Dog, A22DogDto&gt;()
///（双侧同承载 P-f：MappingSnapshot.SubclassPairs 双通道同值参与 L1 比较——顶层"反射全集 − Stripped"
/// 免手列口径自动 Compared；四族分派发射后 L2 含同一 isinst 分派链）。</para>
/// <para>等价要点：L1 SubclassPairs 单元素（A22Dog/A22DogDto FQN 二元组 + ClassName 同源）；
/// L2 基类 Core 方法体含同一分派行（<c>if (src is A22Dog sub_0) return Map_A22Dog_To_A22DogDto(sub_0);</c>）。</para>
/// </summary>
public class EquivalenceTestsMapSubclass : EquivalenceFixtureBase
{
    // Attribute channel source: [MapFrom] 锚定 + [MapSubclass] type-level 多实例（锚定类 = 基类 DTO 自身）
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A22
        {
            public class A22Animal { public string Name { get; set; } = ""; }
            public class A22Dog : A22Animal { public string Breed { get; set; } = ""; }

            [MapFrom(typeof(A22Animal))]
            [MapSubclass(typeof(A22Dog), typeof(A22DogDto))]
            public class A22AnimalDto { public string Name { get; set; } = ""; }
            public class A22DogDto : A22AnimalDto { public string Breed { get; set; } = ""; }
        }
        """;

    // Fluent channel source: [MapperConfig] + .MapSubclass<TSrcDerived, TTargetDerived>()
    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A22
        {
            public class A22Animal { public string Name { get; set; } = ""; }
            public class A22Dog : A22Animal { public string Breed { get; set; } = ""; }
            public class A22AnimalDto { public string Name { get; set; } = ""; }
            public class A22DogDto : A22AnimalDto { public string Breed { get; set; } = ""; }

            [MapperConfig]
            public class A22Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A22Animal, A22AnimalDto>()
                       .MapSubclass<A22Dog, A22DogDto>();
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A22Animal_To_A22AnimalDto";

    [Fact]
    [EqPair("A-22")]
    [Trait("EqPair", "A-22")]
    public void A22_MapSubclass_AttributeChannel_Matches_FluentChannel()
    {
        // L1：SubclassPairs 双通道同值（(A22Dog, A22DogDto) FQN 二元组——顶层自动 Compared）；
        // L2：发射后基类 Core 含同一 isinst 分派行
        AssertEquivalent(AttrSource, FluentSource, "A-22", MapMethodName);
    }
}
