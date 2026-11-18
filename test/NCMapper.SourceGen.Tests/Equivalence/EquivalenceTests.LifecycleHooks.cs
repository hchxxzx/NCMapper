using NCMapper;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// A-19 AfterMapping + A-20 BeforeMapping — 双钩子双通道等价对。
/// <para>A-19：特性 [AfterMapping(typeof(Host), nameof(Host.Apply))] ↔ Fluent .AfterMapping(Host.Apply)
///。</para>
/// <para>A-20：特性 [BeforeMapping(typeof(Host), nameof(Host.Guard))] ↔ Fluent .BeforeMapping(Host.Guard)
/// （→ A-20 迁移，第五批能力转类——落地，双钩子双通道等价化收口）。</para>
/// <para>等价要点：双通道钩子方法引用同一宿主（L1 BeforeMappingMethodRef/AfterMappingMethodRef
/// 双字段均参与比较——Batch-2 迁移后同构）；L2 生成方法体含同一钩子调用行。</para>
/// </summary>
public class EquivalenceTestsLifecycleHooks : EquivalenceFixtureBase
{
    // ── A-19 AfterMapping 等价对 ──────────────────────────────────────────

    // Attribute channel source: [MapFrom] DTO + 双参 [AfterMapping] 指向共享宿主
    private const string AttrSource = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A19
        {
            public class A19Source { public string Name { get; set; } = ""; public int Age { get; set; } }

            public static class A19HookHost
            {
                public static void Apply(A19Source src, A19TargetDto dst) { }
            }

            [MapFrom(typeof(A19Source))]
            [AfterMapping(typeof(A19HookHost), nameof(A19HookHost.Apply))]
            public class A19TargetDto
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }
        }
        """;

    // Fluent channel source: [MapperConfig] + .AfterMapping(方法组) 指向同一共享宿主
    private const string FluentSource = """
        using NCMapper;
        namespace EqPairs.A19
        {
            public class A19Source { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class A19TargetDto { public string Name { get; set; } = ""; public int Age { get; set; } }

            public static class A19HookHost
            {
                public static void Apply(A19Source src, A19TargetDto dst) { }
            }

            [MapperConfig]
            public class A19Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A19Source, A19TargetDto>()
                       .AfterMapping(A19HookHost.Apply);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A19Source_To_A19TargetDto";

    [Fact]
    [EqPair("A-19")]
    [Trait("EqPair", "A-19")]
    public void A19_AfterMapping_AttributeChannel_Matches_FluentChannel()
    {
        // L1：AfterMappingMethodRef 双通道同值（"EqPairs.A19.A19HookHost.Apply"——迁移后参与比较）；
        // L2：生成方法体含同一 After 调用行
        AssertEquivalent(AttrSource, FluentSource, "A-19", MapMethodName);
    }

    // ── A-20 BeforeMapping 等价对──────────────────

    // Attribute channel source: [MapFrom] DTO + 双参 [BeforeMapping] 指向共享宿主
    private const string AttrSourceBefore = """
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A20
        {
            public class A20Source { public string Name { get; set; } = ""; public int Age { get; set; } }

            public static class A20HookHost
            {
                public static void Guard(A20Source src, A20TargetDto dst) { }
            }

            [MapFrom(typeof(A20Source))]
            [BeforeMapping(typeof(A20HookHost), nameof(A20HookHost.Guard))]
            public class A20TargetDto
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }
        }
        """;

    // Fluent channel source: [MapperConfig] + .BeforeMapping(方法组) 指向同一共享宿主
    private const string FluentSourceBefore = """
        using NCMapper;
        namespace EqPairs.A20
        {
            public class A20Source { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class A20TargetDto { public string Name { get; set; } = ""; public int Age { get; set; } }

            public static class A20HookHost
            {
                public static void Guard(A20Source src, A20TargetDto dst) { }
            }

            [MapperConfig]
            public class A20Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A20Source, A20TargetDto>()
                       .BeforeMapping(A20HookHost.Guard);
                }
            }
        }
        """;

    private const string MapMethodNameBefore = "Map_A20Source_To_A20TargetDto";

    [Fact]
    [EqPair("A-20")]
    [Trait("EqPair", "A-20")]
    public void A20_BeforeMapping_AttributeChannel_Matches_FluentChannel()
    {
        // L1：BeforeMappingMethodRef 双通道同值（"EqPairs.A20.A20HookHost.Guard"——显式 Compared 登记）；
        // L2：生成方法体含同一 Before 调用行（Q6 降级形态：双通道均降级分支③ new+逐语句赋值，首条赋值前发射）
        AssertEquivalent(AttrSourceBefore, FluentSourceBefore, "A-20", MapMethodNameBefore);
    }
}
