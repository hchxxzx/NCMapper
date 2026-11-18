// （G7）：A-16 集合索引路径等价对——
// 特性侧：[MapProperty(typeof(S), "Items[0].Title")] vs Fluent 侧：.Member(d => d.FirstTitle).From(s => s.Items[0].Title)。
// 关闭红名单「FromField 索引路径」行（C→A 迁移）。
// 等价口径 = 正向方法（SourcePath+SourceIndexes 同承载 → ElementAtOrDefault 生成语义共享内核）。
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

public class EquivalenceTestsIndexSegment : EquivalenceFixtureBase
{
    private const string A16Attr = """
        using System.Collections.Generic;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A16
        {
            public class A16ItemE { public string Title { get; set; } = ""; }
            public class A16Entity
            {
                public int Id { get; set; }
                public List<A16ItemE> Items { get; set; } = new();
            }

            [MapFrom(typeof(A16Entity))]
            public class A16Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(A16Entity), "Items[0].Title")]
                public string FirstTitle { get; set; } = "";
            }
        }
        """;

    private const string A16Fluent = """
        using System.Collections.Generic;
        using NCMapper;
        namespace EqPairs.A16
        {
            public class A16ItemE { public string Title { get; set; } = ""; }
            public class A16Entity
            {
                public int Id { get; set; }
                public List<A16ItemE> Items { get; set; } = new();
            }
            public class A16Dto
            {
                public int Id { get; set; }
                public string FirstTitle { get; set; } = "";
            }

            [MapperConfig]
            public class A16Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A16Entity, A16Dto>()
                       .Member(d => d.FirstTitle).From(s => s.Items[0].Title);
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A16Entity_To_A16Dto";

    [Fact]
    [EqPair("A-16")]
    [Trait("EqPair", "A-16")]
    public void A16_IndexSegment_AttributeMatchesFluent()
    {
        AssertEquivalent(A16Attr, A16Fluent, "A-16", MapMethodName);
    }
}
