// （D1）：A-18 属性级 NoAutoInclude 等价对——
// 特性侧：属性标 [MapNoAutoInclude] vs Fluent 侧：.Member(d => d.AuditLogs).NoAutoInclude()。
// 关闭红名单「属性级 [MapNoAutoInclude]」行（C→A 迁移，红名单第 5 行）。
// 等价口径 = L1 快照（映射面：同名自动匹配，PropertyNoAutoIncludeTargets 为通道性 Stripped
// 不参与比较）+ L2 方法体 + 运行时承载行为断言（NavigationTreeCollector 注册表消费——
// NCMapper.Tests TreeConfigConsumptionTests.PropertyNoAutoInclude_RegistryRead_SkipsNavigation /
// PropertyNoAutoInclude_ReflectionFallback_Unchanged）；对级 NoAutoMatch()〔原 NoAutoInclude 改名〕仍为 Fluent 独有。
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

public class EquivalenceTestsPropertyNoAutoInclude : EquivalenceFixtureBase
{
    private const string A18Attr = """
        using System.Collections.Generic;
        using NCMapper;
        using NCMapper.Attributes;
        namespace EqPairs.A18
        {
            public class A18LogE { public string Action { get; set; } = ""; }
            public class A18Entity
            {
                public int Id { get; set; }
                public List<A18LogE> AuditLogs { get; set; } = new();
            }

            [MapFrom(typeof(A18Entity))]
            public class A18Dto
            {
                public int Id { get; set; }
                [MapNoAutoInclude]
                public List<string> AuditLogs { get; set; } = new();
            }
        }
        """;

    private const string A18Fluent = """
        using System.Collections.Generic;
        using NCMapper;
        namespace EqPairs.A18
        {
            public class A18LogE { public string Action { get; set; } = ""; }
            public class A18Entity
            {
                public int Id { get; set; }
                public List<A18LogE> AuditLogs { get; set; } = new();
            }
            public class A18Dto
            {
                public int Id { get; set; }
                public List<string> AuditLogs { get; set; } = new();
            }

            [MapperConfig]
            public class A18Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<A18Entity, A18Dto>()
                       .Member(d => d.AuditLogs).NoAutoInclude();
                }
            }
        }
        """;

    private const string MapMethodName = "Map_A18Entity_To_A18Dto";

    [Fact]
    [EqPair("A-18")]
    [Trait("EqPair", "A-18")]
    public void A18_PropertyNoAutoInclude_AttributeMatchesFluent()
    {
        AssertEquivalent(A18Attr, A18Fluent, "A-18", MapMethodName);
    }
}
