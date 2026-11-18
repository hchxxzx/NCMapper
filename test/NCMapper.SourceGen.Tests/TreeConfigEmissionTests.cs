// （G2+D1）：发射面断言——
//   ① J-4 第三处：Fluent .Tree() 无参默认三元组与 [MapTree] 无参/RuntimeTreeConfig init 默认一致
//     （生成产物文本断言——运行时两处断言见 test/NCMapper.Tests/Runtime/TreeConfigConsumptionTests.cs）；
//   ② 条件行字节门：无树配置/无属性级声明 → 产物零新增行（F1 字节门在基线 diff 中另行核验）；
//   ③ 属性级 NoAutoInclude(dest) → PropertyNoAutoIncludeTargets 发射承载。
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class TreeConfigEmissionTests
{
    private const string FluentTreeDefault = """
        using System.Collections.Generic;
        using NCMapper;
        namespace Tce
        {
            public class TceEntity { public int Id { get; set; } }
            public class TceDto { public int Id { get; set; } public List<TceDto> Children { get; set; } = new(); }

            [NCMapper.MapperConfig]
            public class TceDefaultMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<TceEntity, TceDto>().Tree();
                }
            }
        }
        """;

    private const string FluentNoTree = """
        using System.Collections.Generic;
        using NCMapper;
        namespace Tce
        {
            public class PlainEntity { public int Id { get; set; } }
            public class PlainDto { public int Id { get; set; } }

            [NCMapper.MapperConfig]
            public class TcePlainMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<PlainEntity, PlainDto>();
                }
            }
        }
        """;

    /// <summary>J-4 第三处：.Tree() 无参 → 产物发射 RuntimeTreeConfig 默认 "Id"/"ParentId"/"Children"
    /// （与 MapTreeAttribute 无参默认、RuntimeTreeConfig init 默认三处一致——运行时两处断言见
    /// TreeConfigConsumptionTests.J4_DefaultTriplet_RuntimePlacesConsistent）。</summary>
    [Fact]
    public void Tree_DefaultInvocation_EmitsConsistentTriplet()
    {
        var result = TestHelper.GenerateMapper(FluentTreeDefault);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // 发射断言走 on 态注册段（GenerateMapper 默认 off 态——注册段零发射）
        var code = TestHelper.GenerateRuntimeSnapshotCode(FluentTreeDefault);
        Assert.False(string.IsNullOrEmpty(code), "on 态下 RuntimeSnapshot 注册段应有输出");
        Assert.Contains("TreeConfig = new global::NCMapper.Runtime.RuntimeTreeConfig", code);
        Assert.Contains("IdProperty = \"Id\"", code);
        Assert.Contains("ParentIdProperty = \"ParentId\"", code);
        Assert.Contains("ChildrenProperty = \"Children\"", code);
    }

    /// <summary>条件行字节门：未调 .Tree() → 产物不含 TreeConfig 行（F1 字节门——既有产物零新增）。</summary>
    [Fact]
    public void NoTreeDeclaration_EmitsNoTreeConfigLine()
    {
        var result = TestHelper.GenerateMapper(FluentNoTree);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var code = TestHelper.GenerateRuntimeSnapshotCode(FluentNoTree);
        Assert.DoesNotContain("TreeConfig =", code);
        Assert.DoesNotContain("PropertyNoAutoIncludeTargets =", code);
    }

    /// <summary>D1 发射承载：.Member(dest).NoAutoInclude() → PropertyNoAutoIncludeTargets 数组发射属性名。</summary>
    [Fact]
    public void PropertyNoAutoInclude_EmitsTargetsArray()
    {
        const string source = """
            using System.Collections.Generic;
            using NCMapper;
            namespace Tce
            {
                public class NaEntity { public int Id { get; set; } }
                public class NaDto { public int Id { get; set; } public List<string> AuditLogs { get; set; } = new(); }

                [NCMapper.MapperConfig]
                public class TceNaMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<NaEntity, NaDto>().Member(d => d.AuditLogs).NoAutoInclude();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var code = TestHelper.GenerateRuntimeSnapshotCode(source);
        Assert.False(string.IsNullOrEmpty(code), "on 态下 RuntimeSnapshot 注册段应有输出");
        Assert.Contains("PropertyNoAutoIncludeTargets = new string[] { \"AuditLogs\" }", code);
    }
}
