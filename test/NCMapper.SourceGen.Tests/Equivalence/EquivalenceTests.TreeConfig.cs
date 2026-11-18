// （G2）：A-17 树构建配置等价对——
// 特性侧：[MapTree("Key", "ParentKey", "Nodes")] vs Fluent 侧：.Tree("Key", "ParentKey", "Nodes")。
// 关闭红名单「[MapTree] 树配置」行（C→A 迁移，红名单第 4 行）。
// 等价口径 = L1 快照（映射面：同名自动匹配，TreeConfig 为通道性 Stripped 不参与比较）
// + L2 方法体（树配置不进映射产物）+ 运行时承载行为断言（TreeBuilder/IsTreeChildrenSlot
// 注册表消费——NCMapper.Tests TreeConfigConsumptionTests）；J-4 默认三元组三处一致断言同见。
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

public class EquivalenceTestsTreeConfig : EquivalenceFixtureBase
{
    private const string A17Attr = """
        using System.Collections.Generic;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A17
        {
            public class A17Entity
            {
                public int Key { get; set; }
                public int? ParentKey { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom(typeof(A17Entity))]
            [MapTree("Key", "ParentKey", "Nodes")]
            public class A17Dto
            {
                public int Key { get; set; }
                public int? ParentKey { get; set; }
                public string Name { get; set; } = "";
                public List<A17Dto> Nodes { get; set; } = new();
            }
        }
        """;

    private const string A17Fluent = """
        using System.Collections.Generic;
        using NCMapper;
        namespace EqPairs.A17
        {
            public class A17Entity
            {
                public int Key { get; set; }
                public int? ParentKey { get; set; }
                public string Name { get; set; } = "";
            }
            public class A17Dto
            {
                public int Key { get; set; }
                public int? ParentKey { get; set; }
                public string Name { get; set; } = "";
                public List<A17Dto> Nodes { get; set; } = new();
            }

            [MapperConfig]
            public class A17Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A17Entity, A17Dto>()
                       .Tree("Key", "ParentKey", "Nodes");
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A17Entity_To_A17Dto";

    [Fact]
    [EqPair("A-17")]
    [Trait("EqPair", "A-17")]
    public void A17_TreeConfig_AttributeMatchesFluent()
    {
        AssertEquivalent(A17Attr, A17Fluent, "A-17", MapMethodName);
    }
}
