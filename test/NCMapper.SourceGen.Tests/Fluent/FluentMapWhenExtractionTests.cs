// Batch-1测试矩阵：
// 转录引擎矩阵（验收 #3）：参数原样/BCL FQN/跨 ns 枚举 FQN/别名展开/三元算术/?./
//       nameof 字面量化/泛型静态调用 + 负向（捕获 F41/语句体 F42/dynamic F42——#67 先验锚）；
// 提取八组合（验收 #2）：自动匹配/显式 × A 方法组/B 内联 × T1/T2；
// 诊断矩阵（验收 #3/#6 + F45）：F40 四形态/F43 两形态/F44 重复/F45 主链 Compose + 块内正例；
// 特性侧（验收 #1/#2）：T1/T2 提取 + NCMAP041 五负向（解析失败/重载歧义/非静态/可见性/签名）；
// 快照相等性（验收 #4）：ConditionMethodRef 参与相等性 + 哈希一致；
// 运行时快照发射（承载面）：ConditionMethodRef/ConditionShape 条件行 + 无声明零行（字节门）。
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

public class FluentMapWhenExtractionTests
{
    private static FluentExtractionResult? ExtractResult(string source)
    {
        var compilation = TestHelper.BuildCompilation(source);
        var typeDecl = compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("MapperConfig")))!;
        var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        return FluentExtractor.ExtractCore(compilation, typeSymbol!, typeDecl, System.Threading.CancellationToken.None);
    }

    private static MappingSnapshot FirstSnapshot(string source)
    {
        var result = ExtractResult(source);
        Assert.NotNull(result);
        return result!.Declarations.First().Snapshot;
    }

    private static PropertyMappingSnapshot Pm(MappingSnapshot snapshot, string targetName)
        => snapshot.PropertyMappings.First(p => p.Target.Name == targetName);

    // ================================================================
    // 转录引擎矩阵
    // ================================================================

    /// <summary>B 形态基础：参数标识符原样 + BCL 静态调用限定名重写（B10 红线——非参数名必须限定化）。</summary>
    [Fact]
    public void Inline_ParamAndBclCall_FqnRewritten()
    {
        var source = """
            using NCMapper;
            public class MwI1Entity { public string Name { get; set; } = ""; }
            public class MwI1Dto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MwI1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI1Entity, MwI1Dto>()
                       .Member(d => d.Name).MapWhen(v => !string.IsNullOrEmpty(v));
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "Name");
        Assert.Equal(ConditionShape.FieldValue, pm.ConditionShape);
        Assert.Equal("v", pm.ConditionMethodParamName);
        Assert.Equal("!global::System.String.IsNullOrEmpty(v)", pm.ConditionMethodBody);
        Assert.StartsWith("NCMapper.Generated.GeneratedMapper::Map_When_", pm.ConditionMethodRef);
    }

    /// <summary>跨命名空间枚举成员 → global:: FQN 重写（生成作用域零 using 依赖）。</summary>
    [Fact]
    public void Inline_CrossNamespaceEnum_FqnRewritten()
    {
        var source = """
            using NCMapper;
            namespace MwOther { public enum Kind { A, B } }
            public class MwI2Entity { public MwOther.Kind Kind { get; set; } }
            public class MwI2Dto { public MwOther.Kind Kind { get; set; } }

            [MapperConfig]
            public class MwI2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI2Entity, MwI2Dto>()
                       .Member(d => d.Kind).MapWhen(k => k == MwOther.Kind.A);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "Kind");
        Assert.StartsWith("NCMapper.Generated.GeneratedMapper::Map_When_", pm.ConditionMethodRef);
        Assert.Equal("k == global::MwOther.Kind.A", pm.ConditionMethodBody);
    }

    /// <summary>using 别名 → FQN 展开（GetAliasInfo 命中——定稿行为：展开而非拒绝，用户零感知）。</summary>
    [Fact]
    public void Inline_UsingAlias_ExpandedToFqn()
    {
        var source = """
            using NCMapper;
            using K = MwAliasNs.Kind;
            namespace MwAliasNs { public enum Kind { A, B } }
            public class MwI3Entity { public MwAliasNs.Kind Kind { get; set; } }
            public class MwI3Dto { public MwAliasNs.Kind Kind { get; set; } }

            [MapperConfig]
            public class MwI3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI3Entity, MwI3Dto>()
                       .Member(d => d.Kind).MapWhen(k => k == K.A);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        // 别名场景编译可过（K.A 经 GetAliasInfo 解析成功）→ 无 F41/F42 诊断
        Assert.DoesNotContain(result!.Diagnostics, d => d.DescriptorId is "NCMAP_F41" or "NCMAP_F42");
        var pm = Pm(result.Declarations.First().Snapshot, "Kind");
        Assert.Equal("k == global::MwAliasNs.Kind.A", pm.ConditionMethodBody);
    }

    /// <summary>F41 正例（#67 先验锚）：捕获外层局部变量 → F41 + 声明丢弃（其余属性提取不受影响）。</summary>
    [Fact]
    public void Inline_CapturedLocal_F41Rejected()
    {
        var source = """
            using NCMapper;
            public class MwI4Entity { public string Name { get; set; } = ""; public int Age { get; set; } }
            public class MwI4Dto { public string Name { get; set; } = ""; public int Age { get; set; } }

            [MapperConfig]
            public class MwI4Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    var prefix = "x:";
                    cfg.For<MwI4Entity, MwI4Dto>()
                       .Member(d => d.Name).MapWhen(v => v == prefix + "y");
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        // 先验锚：For 对提取成功（链非整体拒绝——F41 仅丢弃该 MapWhen 声明）
        var snapshot = result!.Declarations.First().Snapshot;
        Assert.Equal(2, snapshot.PropertyMappings.Count);
        // F41 精确发射
        var f41 = result.Diagnostics.Where(d => d.DescriptorId == "NCMAP_F41").ToList();
        Assert.Single(f41);
        // 声明丢弃：Name pm 无条件字段
        var pm = Pm(snapshot, "Name");
        Assert.Null(pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.None, pm.ConditionShape);
    }

    /// <summary>F42 正例：语句体 lambda → 拒绝（仅表达式体可转录）。</summary>
    [Fact]
    public void Inline_StatementBody_F42Rejected()
    {
        var source = """
            using NCMapper;
            public class MwI5Entity { public string Name { get; set; } = ""; }
            public class MwI5Dto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MwI5Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI5Entity, MwI5Dto>()
                       .Member(d => d.Name).MapWhen(v =>
                       {
                           return v.Length > 0;
                       });
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        var f42 = result!.Diagnostics.Where(d => d.DescriptorId == "NCMAP_F42").ToList();
        Assert.Single(f42);
        var pm = Pm(result.Declarations.First().Snapshot, "Name");
        Assert.Null(pm.ConditionMethodRef);
    }

    /// <summary>F42 正例：dynamic 绑定成员访问 → 拒绝（B10/dynamic 禁令）。</summary>
    [Fact]
    public void Inline_DynamicAccess_F42Rejected()
    {
        var source = """
            using NCMapper;
            public class MwI6Entity { public object Name { get; set; } = ""; }
            public class MwI6Dto { public object Name { get; set; } = ""; }

            [MapperConfig]
            public class MwI6Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI6Entity, MwI6Dto>()
                       .Member(d => d.Name).MapWhen(v => ((dynamic)v).Length > 0);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        var snapshot = result!.Declarations.First().Snapshot;
        Assert.Single(snapshot.PropertyMappings);
        Assert.Contains(result.Diagnostics, d => d.DescriptorId is "NCMAP_F41" or "NCMAP_F42");
        var pm = Pm(snapshot, "Name");
        Assert.Null(pm.ConditionMethodRef);
    }

    /// <summary>nameof 字面量化 + 三元/算术 + ?. 链 + 泛型静态调用放行（白名单矩阵）。</summary>
    [Fact]
    public void Inline_Nameof_Ternary_NullPropagation_GenericCall_AllPass()
    {
        var source = """
            using System.Linq;
            using NCMapper;
            public class MwI7Entity
            {
                public string Name { get; set; } = "";
                public int Score { get; set; }
                public string? Nick { get; set; }
                public System.Collections.Generic.List<int> Tags { get; set; } = new();
            }
            public class MwI7Dto
            {
                public string Name { get; set; } = "";
                public int Score { get; set; }
                public string? Nick { get; set; }
                public System.Collections.Generic.List<int> Tags { get; set; } = new();
            }
            public static class MwI7Rules
            {
                public static bool HasName(string v) => !string.IsNullOrEmpty(v);
                public static bool ScoreOk(int v) => v > 0;
                public static bool NickOk(string? v) => v?.Length >= 2;
                public static bool TagsOk(System.Collections.Generic.List<int> v) => Enumerable.Count(v) > 0;
            }

            [MapperConfig]
            public class MwI7Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI7Entity, MwI7Dto>()
                       .Member(d => d.Name).MapWhen(v => v != nameof(MwI7Dto))                       // nameof → 字面量
                       .Member(d => d.Score).MapWhen(v => (v * 2 + 1) > 0 ? v > 0 : false)            // 三元+算术
                       .Member(d => d.Nick).MapWhen(v => v?.Length >= 2)                              // ?. 链
                       .Member(d => d.Tags).MapWhen(t => Enumerable.Count(t) > 0);                   // 泛型静态调用
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Diagnostics, d => d.DescriptorId is "NCMAP_F41" or "NCMAP_F42");
        var snapshot = result.Declarations.First().Snapshot;
        foreach (var name in new[] { "Name", "Score", "Nick", "Tags" })
        {
            Assert.True(Pm(snapshot, name).ConditionMethodRef!
                .StartsWith("NCMapper.Generated.GeneratedMapper::Map_When_", System.StringComparison.Ordinal),
                $"属性 {name} 转录失败");
        }
        // nameof → 字面量化（零名称依赖）
        Assert.Equal("v != \"MwI7Dto\"", Pm(snapshot, "Name").ConditionMethodBody);
        // 三元+算术（括号保形）
        Assert.Equal("(v * 2 + 1) > 0 ? v > 0 : false", Pm(snapshot, "Score").ConditionMethodBody);
        // ?. 链（绑定段按接收者类型解析——原样安全）
        Assert.Equal("v?.Length >= 2", Pm(snapshot, "Nick").ConditionMethodBody);
        // 泛型静态调用 → FQN（推断实参由生成作用域重推——参数类型不变结果不变）
        Assert.Equal("global::System.Linq.Enumerable.Count(t) > 0", Pm(snapshot, "Tags").ConditionMethodBody);
    }

    // ================================================================
    // 提取八组合（验收 #2：自动匹配/显式 × A/B × T1/T2）
    // ================================================================

    private const string CombiCommon = """
        using NCMapper;
        public class MwCEntity { public string Name { get; set; } = ""; public int Age { get; set; } public string RealName { get; set; } = ""; }
        public class MwCDto
        {
            public string Name { get; set; } = "";
            public int Age { get; set; }
            public string AliasName { get; set; } = "";
            public static bool HasContent(string v) => !string.IsNullOrEmpty(v);
            public static bool WholeOk(MwCEntity s) => s.Age > 0;
        }
        """;

    /// <summary>A+T1+自动匹配：方法组 → ConditionMethodRef="声明类型::方法名"、Shape=FieldValue。</summary>
    [Fact]
    public void Combo_A_T1_AutoMatch()
    {
        var source = CombiCommon + """
            [MapperConfig]
            public class MwC1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwCEntity, MwCDto>().Member(d => d.Name).MapWhen(MwCDto.HasContent);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "Name");
        Assert.Equal("MwCDto::HasContent", pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.FieldValue, pm.ConditionShape);
    }

    /// <summary>A+T1+显式 Map：异名显式声明锚点 pm 命中（AliasName ← RealName）。</summary>
    [Fact]
    public void Combo_A_T1_ExplicitMap()
    {
        var source = CombiCommon + """
            [MapperConfig]
            public class MwC2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwCEntity, MwCDto>()
                       .Member(d => d.AliasName).From(s => s.RealName)
                       .Member(d => d.AliasName).MapWhen(MwCDto.HasContent);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "AliasName");
        Assert.Equal("MwCDto::HasContent", pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.FieldValue, pm.ConditionShape);
    }

    /// <summary>A+T2+自动匹配：整源谓词（dest object 占位 + Func&lt;TSource,bool&gt）→ Shape=WholeSource。</summary>
    [Fact]
    public void Combo_A_T2_AutoMatch()
    {
        var source = CombiCommon + """
            [MapperConfig]
            public class MwC3Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwCEntity, MwCDto>().Member(d => d.Name).MapWhen<MwCEntity>(MwCDto.WholeOk);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "Name");
        Assert.Equal("MwCDto::WholeOk", pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.WholeSource, pm.ConditionShape);
    }

    /// <summary>A+T2+显式 Map。</summary>
    [Fact]
    public void Combo_A_T2_ExplicitMap()
    {
        var source = CombiCommon + """
            [MapperConfig]
            public class MwC4Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwCEntity, MwCDto>()
                       .Member(d => d.AliasName).From(s => s.RealName)
                       .Member(d => d.AliasName).MapWhen<MwCEntity>(MwCDto.WholeOk);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "AliasName");
        Assert.Equal("MwCDto::WholeOk", pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.WholeSource, pm.ConditionShape);
    }

    /// <summary>B+T1+自动匹配：内联 lambda → 生成方法限定名 + Shape=FieldValue。</summary>
    [Fact]
    public void Combo_B_T1_AutoMatch()
    {
        var source = CombiCommon + """
            [MapperConfig]
            public class MwC5Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwCEntity, MwCDto>().Member(d => d.Name).MapWhen(v => v.Length > 0);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "Name");
        Assert.StartsWith("NCMapper.Generated.GeneratedMapper::Map_When_", pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.FieldValue, pm.ConditionShape);
    }

    /// <summary>B+T1+显式 Map。</summary>
    [Fact]
    public void Combo_B_T1_ExplicitMap()
    {
        var source = CombiCommon + """
            [MapperConfig]
            public class MwC6Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwCEntity, MwCDto>()
                       .Member(d => d.AliasName).From(s => s.RealName)
                       .Member(d => d.AliasName).MapWhen(v => v.Length > 0);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "AliasName");
        Assert.StartsWith("NCMapper.Generated.GeneratedMapper::Map_When_", pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.FieldValue, pm.ConditionShape);
    }

    /// <summary>B+T2+自动匹配：整源内联谓词 → Shape=WholeSource。</summary>
    [Fact]
    public void Combo_B_T2_AutoMatch()
    {
        var source = CombiCommon + """
            [MapperConfig]
            public class MwC7Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwCEntity, MwCDto>().Member(d => d.Name).MapWhen<MwCEntity>(s => s.Age > 0);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "Name");
        Assert.StartsWith("NCMapper.Generated.GeneratedMapper::Map_When_", pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.WholeSource, pm.ConditionShape);
    }

    /// <summary>B+T2+显式 Map。</summary>
    [Fact]
    public void Combo_B_T2_ExplicitMap()
    {
        var source = CombiCommon + """
            [MapperConfig]
            public class MwC8Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwCEntity, MwCDto>()
                       .Member(d => d.AliasName).From(s => s.RealName)
                       .Member(d => d.AliasName).MapWhen<MwCEntity>(s => s.Age > 0);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "AliasName");
        Assert.StartsWith("NCMapper.Generated.GeneratedMapper::Map_When_", pm.ConditionMethodRef);
        Assert.Equal(ConditionShape.WholeSource, pm.ConditionShape);
    }

    /// <summary>Q8 自引用边角：dest 属性类型 == TSource 时谓词判 T1（betterness 择优——字段值即源对象）。</summary>
    [Fact]
    public void Combo_Q8_SelfReferencingAnchor_JudgedT1()
    {
        var source = """
            using NCMapper;
            public class MwQ8Node { public MwQ8Node Child { get; set; } = new(); public string Name { get; set; } = ""; }
            public class MwQ8Dto { public MwQ8Node Child { get; set; } = new(); public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MwQ8Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwQ8Node, MwQ8Dto>()
                       .Member(d => d.Child).MapWhen(c => c != null! && c.Name.Length > 0);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "Child");
        Assert.Equal(ConditionShape.FieldValue, pm.ConditionShape);
        Assert.StartsWith("NCMapper.Generated.GeneratedMapper::Map_When_", pm.ConditionMethodRef);
    }

    // ================================================================
    // 诊断矩阵（验收 #3/#6 + F45 主链 Compose + 块内）
    // ================================================================

    /// <summary>F40：非静态方法组（实例绑定）→ Error + 声明丢弃。</summary>
    [Fact]
    public void F40_InstanceMethodGroup_Rejected()
    {
        var source = """
            using NCMapper;
            public class MwF40aEntity { public string Name { get; set; } = ""; }
            public class MwF40aDto
            {
                public string Name { get; set; } = "";
                public bool Check(string v) => v.Length > 0;   // 非 static
            }

            [MapperConfig]
            public class MwF40aMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwF40aEntity, MwF40aDto>().Member(d => d.Name).MapWhen(new MwF40aDto().Check);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        Assert.Contains(result!.Diagnostics, d => d.DescriptorId == "NCMAP_F40");
        var pm = Pm(result.Declarations.First().Snapshot, "Name");
        Assert.Null(pm.ConditionMethodRef);
    }

    /// <summary>F40：private 可见性 → Error（生成代码位于 GeneratedMapper 跨类不可达）。</summary>
    [Fact]
    public void F40_PrivatePredicate_Rejected()
    {
        var source = """
            using NCMapper;
            public class MwF40bEntity { public string Name { get; set; } = ""; }
            public class MwF40bDto
            {
                public string Name { get; set; } = "";
                private static bool Check(string v) => v.Length > 0;   // private
            }

            [MapperConfig]
            public class MwF40bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwF40bEntity, MwF40bDto>().Member(d => d.Name).MapWhen(MwF40bDto.Check);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        Assert.Contains(result!.Diagnostics, d => d.DescriptorId == "NCMAP_F40");
    }

    /// <summary>F40：签名不符（双参）→ Error。</summary>
    [Fact]
    public void F40_TwoParamSignature_Rejected()
    {
        var source = """
            using NCMapper;
            public class MwF40cEntity { public string Name { get; set; } = ""; }
            public class MwF40cDto
            {
                public string Name { get; set; } = "";
                public static bool Check(string v, int min) => v.Length > min;   // 双参
            }

            [MapperConfig]
            public class MwF40cMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwF40cEntity, MwF40cDto>().Member(d => d.Name).MapWhen(MwF40cDto.Check);
                }
            }
            """;
        // 方法组双参不适配 Func<string,bool>——CS0123 编译层红 + 提取层符号解析兜底
        var compilation = TestHelper.BuildCompilation(source);
        Assert.Contains(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>F40：源字段类型与谓词入参无隐式转换（int → string）→ Error（防生成调用点 CS1503）。</summary>
    [Fact]
    public void F40_SourceTypeMismatch_Rejected()
    {
        var source = """
            using NCMapper;
            public class MwF40dEntity { public int Age { get; set; } }
            public class MwF40dDto
            {
                public int Age { get; set; }
                public static bool Check(string v) => v.Length > 0;   // 入参 string ≠ 源 int
            }

            [MapperConfig]
            public class MwF40dMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwF40dEntity, MwF40dDto>().Member(d => d.Age).MapWhen(MwF40dDto.Check);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        Assert.Contains(result!.Diagnostics, d => d.DescriptorId == "NCMAP_F40");
    }

    /// <summary>F43 形态一：锚点属性无映射（源侧不可达）→ Error。</summary>
    [Fact]
    public void F43_AnchorNoMapping_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class MwF43aEntity { public string Name { get; set; } = ""; }
            public class MwF43aDto
            {
                public string Name { get; set; } = "";
                public string Band { get; set; } = "";   // 源侧无同名
                public static bool HasContent(string v) => v.Length > 0;
            }

            [MapperConfig]
            public class MwF43aMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwF43aEntity, MwF43aDto>().Member(d => d.Band).MapWhen(MwF43aDto.HasContent);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        // F43 为物化期诊断（MappingSnapshotBuilder）——落在快照 Diagnostics 而非提取器诊断列表
        var f43 = result!.Declarations.First().Snapshot.Diagnostics
            .Where(d => d.DescriptorId == "NCMAP_F43").ToList();
        Assert.Single(f43);
        Assert.Contains("Band", string.Join(" ", f43[0].Args));
    }

    /// <summary>F43 形态二：锚点被静态 Ignore 排除 → Error（Ignore 属性不入候选）。</summary>
    [Fact]
    public void F43_AnchorIgnored_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class MwF43bEntity { public string Name { get; set; } = ""; }
            public class MwF43bDto
            {
                public string Name { get; set; } = "";
                public static bool HasContent(string v) => v.Length > 0;
            }

            [MapperConfig]
            public class MwF43bMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwF43bEntity, MwF43bDto>()
                       .Member(d => d.Name).Ignore()
                       .Member(d => d.Name).MapWhen(MwF43bDto.HasContent);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        var f43 = result!.Declarations.First().Snapshot.Diagnostics
            .Where(d => d.DescriptorId == "NCMAP_F43").ToList();
        Assert.Single(f43);
    }

    /// <summary>F44：同属性重复 MapWhen → Error（单条件）。</summary>
    [Fact]
    public void F44_DuplicateWhen_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class MwF44Entity { public string Name { get; set; } = ""; }
            public class MwF44Dto
            {
                public string Name { get; set; } = "";
                public static bool HasContent(string v) => v.Length > 0;
                public static bool IsLong(string v) => v.Length > 10;
            }

            [MapperConfig]
            public class MwF44Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwF44Entity, MwF44Dto>()
                       .Member(d => d.Name).MapWhen(MwF44Dto.HasContent)
                       .Member(d => d.Name).MapWhen(MwF44Dto.IsLong);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        Assert.Contains(result!.Diagnostics, d => d.DescriptorId == "NCMAP_F44");
        // 首条声明保留（LastWrite 不适用——重复即拒第二条）
        var pm = Pm(result.Declarations.First().Snapshot, "Name");
        Assert.Equal("MwF44Dto::HasContent", pm.ConditionMethodRef);
    }

    /// <summary>F45：主链锚点仅由 FromSource 跨源覆盖 → Error（主链无属性赋值展开落点）。</summary>
    [Fact]
    public void F45_MainChainComposeOnlyAnchor_Diagnosed()
    {
        var source = """
            using NCMapper;
            public class MwF45Entity { public int Id { get; set; } }
            public class MwF45Sub { public string Name { get; set; } = ""; }
            public class MwF45Dto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public static bool HasContent(string v) => v.Length > 0;
            }

            [MapperConfig]
            public class MwF45Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwF45Entity, MwF45Dto>()
                       .Member(d => d.Name).FromSource<MwF45Sub>(s => s.Name)
                       .Member(d => d.Name).MapWhen(MwF45Dto.HasContent);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        // F45 为物化期诊断——落在快照 Diagnostics
        Assert.Contains(result!.Declarations.First().Snapshot.Diagnostics, d => d.DescriptorId == "NCMAP_F45");
    }

    /// <summary>
    /// 邻界反例：合法 MapWhen（静态方法组 + 签名适配 + 锚点有映射 + 单条声明）→ 不报 NCMAP041/F40/F43/F44/F45
    /// </summary>
    [Fact]
    public void MapWhen_ValidStaticPredicate_NoDiagnostic()
    {
        var source = """
            using NCMapper;
            public class MwOkEntity { public string Name { get; set; } = ""; }
            public class MwOkDto
            {
                public string Name { get; set; } = "";
                public static bool Check(string v) => v.Length > 0;
            }

            [MapperConfig]
            public class MwOkMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwOkEntity, MwOkDto>().Member(d => d.Name).MapWhen(MwOkDto.Check);
                }
            }
            """;
        var result = ExtractResult(source);
        Assert.NotNull(result);
        var codes = result!.Diagnostics
            .Concat(result.Declarations.SelectMany(d => d.Snapshot.Diagnostics))
            .Select(d => d.DescriptorId)
            .ToList();

        Assert.DoesNotContain("NCMAP041", codes);
        Assert.DoesNotContain("NCMAP_F40", codes);
        Assert.DoesNotContain("NCMAP_F43", codes);
        Assert.DoesNotContain("NCMAP_F44", codes);
        Assert.DoesNotContain("NCMAP_F45", codes);
    }

    /// <summary> 正例：FromSource 块内 MapWhen → ComposePropertyMapping 条件四字段注入（有发射落点，无 F45）。</summary>
    [Fact]
    public void S4_BlockWhen_InjectsComposeMapping()
    {
        var source = """
            using NCMapper;
            public class MwS4Entity { public int Id { get; set; } }
            public class MwS4Sub { public string Name { get; set; } = ""; }
            public class MwS4Dto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public static bool HasContent(string v) => v.Length > 0;
            }

            [MapperConfig]
            public class MwS4Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwS4Entity, MwS4Dto>()
                       .FromSource<MwS4Sub>(block =>
                       {
                           block.Member(d => d.Name).From(s => s.Name);
                           block.Member(d => d.Name).MapWhen(MwS4Dto.HasContent);
                       });
                }
            }
            """;
        var snapshot = FirstSnapshot(source);
        var cm = snapshot.ComposeMappings.Single(c => c.Target.Name == "Name");
        Assert.Equal("MwS4Dto::HasContent", cm.ConditionMethodRef);
        Assert.Equal(ConditionShape.FieldValue, cm.ConditionShape);
    }

    // ================================================================
    // 特性侧
    // ================================================================

    /// <summary>特性侧 T1 自动匹配：[MapFrom] + [MapWhen] → 与 Fluent 同字段（P-e 双侧同承载）。</summary>
    [Fact]
    public void Attribute_T1_AutoMatch_SameFieldAsFluent()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA1Entity { public string Name { get; set; } = ""; }
            [MapFrom(typeof(MwA1Entity))]
            public class MwA1Dto
            {
                [MapWhen(nameof(HasContent))]
                public string Name { get; set; } = "";
                public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var code = TestHelper.GenerateRuntimeSnapshotCode(source);
        Assert.Contains("ConditionMethodRef = \"MwA1Dto::HasContent\"", code);
        Assert.Contains("ConditionShape = 1", code);
    }

    /// <summary>特性侧 T1 显式 [MapProperty]：异名映射属性锚定。</summary>
    [Fact]
    public void Attribute_T1_ExplicitMapProperty()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA2Entity { public string RealName { get; set; } = ""; }
            [MapFrom(typeof(MwA2Entity))]
            public class MwA2Dto
            {
                [MapProperty(typeof(MwA2Entity), nameof(MwA2Entity.RealName))]
                [MapWhen(nameof(HasContent))]
                public string AliasName { get; set; } = "";
                public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var code = TestHelper.GenerateRuntimeSnapshotCode(source);
        Assert.Contains("ConditionMethodRef = \"MwA2Dto::HasContent\"", code);
    }

    /// <summary>特性侧 T2 整源：单参 == 源实体类型 → Shape=WholeSource（ConditionShape = 2）。</summary>
    [Fact]
    public void Attribute_T2_WholeSource()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA3Entity { public string Name { get; set; } = ""; }
            [MapFrom(typeof(MwA3Entity))]
            public class MwA3Dto
            {
                [MapWhen(nameof(WholeOk))]
                public string Name { get; set; } = "";
                public static bool WholeOk(MwA3Entity s) => s.Name.Length > 0;
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var code = TestHelper.GenerateRuntimeSnapshotCode(source);
        Assert.Contains("ConditionMethodRef = \"MwA3Dto::WholeOk\"", code);
        Assert.Contains("ConditionShape = 2", code);
    }

    /// <summary>NCMAP041 形态一：方法不存在。</summary>
    [Fact]
    public void Ncmap041_MethodNotFound()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA4Entity { public string Name { get; set; } = ""; }
            [MapFrom(typeof(MwA4Entity))]
            public class MwA4Dto
            {
                [MapWhen("NoSuchMethod")]
                public string Name { get; set; } = "";
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var d = result.Diagnostics.Single(x => x.Id == "NCMAP041");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity);
        // 先验锚：DTO 本身提取成功（映射方法生成在案——诊断针对 [MapWhen] 解析失败而非整体跳过）
        Assert.Contains("Map_MwA4Entity_To_MwA4Dto", result.GeneratedCode);
    }

    /// <summary>NCMAP041 形态二：非静态。</summary>
    [Fact]
    public void Ncmap041_NotStatic()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA5Entity { public string Name { get; set; } = ""; }
            [MapFrom(typeof(MwA5Entity))]
            public class MwA5Dto
            {
                [MapWhen(nameof(Check))]
                public string Name { get; set; } = "";
                public bool Check(string v) => v.Length > 0;   // 非 static
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, x => x.Id == "NCMAP041");
    }

    /// <summary>NCMAP041 形态三：可见性不足（private）。</summary>
    [Fact]
    public void Ncmap041_PrivateVisibility()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA6Entity { public string Name { get; set; } = ""; }
            [MapFrom(typeof(MwA6Entity))]
            public class MwA6Dto
            {
                [MapWhen(nameof(Check))]
                public string Name { get; set; } = "";
                private static bool Check(string v) => v.Length > 0;   // private
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, x => x.Id == "NCMAP041");
    }

    /// <summary>NCMAP041 形态四：签名不符（返回非 bool）。</summary>
    [Fact]
    public void Ncmap041_SignatureMismatch()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA7Entity { public string Name { get; set; } = ""; }
            [MapFrom(typeof(MwA7Entity))]
            public class MwA7Dto
            {
                [MapWhen(nameof(LengthOf))]
                public string Name { get; set; } = "";
                public static int LengthOf(string v) => v.Length;   // 返回 int
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, x => x.Id == "NCMAP041");
    }

    /// <summary>NCMAP041 形态五：入参类型与源字段/源实体均不匹配（T1/T2 双不适配）。</summary>
    [Fact]
    public void Ncmap041_ParamTypeMismatch()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA8Entity { public int Age { get; set; } }
            [MapFrom(typeof(MwA8Entity))]
            public class MwA8Dto
            {
                [MapWhen(nameof(Check))]
                public int Age { get; set; }
                public static bool Check(string v) => v.Length > 0;   // 入参 string ≠ 源 int/实体
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, x => x.Id == "NCMAP041");
    }

    /// <summary>NCMAP041 形态六：同名方法多重载歧义。</summary>
    [Fact]
    public void Ncmap041_OverloadAmbiguity()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwA9Entity { public string Name { get; set; } = ""; }
            [MapFrom(typeof(MwA9Entity))]
            public class MwA9Dto
            {
                [MapWhen(nameof(Check))]
                public string Name { get; set; } = "";
                public static bool Check(string v) => v.Length > 0;
                public static bool Check(string v, int min) => v.Length > min;
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, x => x.Id == "NCMAP041");
    }

    // ================================================================
    // 快照相等性（验收 #4）
    // ================================================================

    /// <summary>同源两次提取 → 快照相等 + 哈希一致（增量缓存正确性——ConditionMethodRef 参与相等性）。</summary>
    [Fact]
    public void SnapshotEquality_SameCondition_EqualWithHash()
    {
        const string source = """
            using NCMapper;
            public class MwE1Entity { public string Name { get; set; } = ""; }
            public class MwE1Dto { public string Name { get; set; } = ""; public static bool HasContent(string v) => v.Length > 0; }

            [MapperConfig]
            public class MwE1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwE1Entity, MwE1Dto>().Member(d => d.Name).MapWhen(MwE1Dto.HasContent);
                }
            }
            """;
        var s1 = FirstSnapshot(source);
        var s2 = FirstSnapshot(source);
        Assert.True(s1.Equals(s2));
        Assert.Equal(s1.GetHashCode(), s2.GetHashCode());
        Assert.Contains("ConditionMethodRef", typeof(PropertyMappingSnapshot).GetProperties().Select(p => p.Name));
    }

    /// <summary>不同谓词（方法名不同）→ 快照不等（ConditionMethodRef 差异可检）。</summary>
    [Fact]
    public void SnapshotEquality_DifferentCondition_NotEqual()
    {
        const string src1 = """
            using NCMapper;
            public class MwE2Entity { public string Name { get; set; } = ""; }
            public class MwE2Dto
            {
                public string Name { get; set; } = "";
                public static bool HasContent(string v) => v.Length > 0;
            }

            [MapperConfig]
            public class MwE2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwE2Entity, MwE2Dto>().Member(d => d.Name).MapWhen(MwE2Dto.HasContent);
                }
            }
            """;
        const string src2 = """
            using NCMapper;
            public class MwE2Entity { public string Name { get; set; } = ""; }
            public class MwE2Dto
            {
                public string Name { get; set; } = "";
                public static bool IsEmpty(string v) => v.Length == 0;
            }

            [MapperConfig]
            public class MwE2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwE2Entity, MwE2Dto>().Member(d => d.Name).MapWhen(MwE2Dto.IsEmpty);
                }
            }
            """;
        var s1 = FirstSnapshot(src1);
        var s2 = FirstSnapshot(src2);
        Assert.False(s1.Equals(s2));
        // 两快照均有守卫承载（条件字段差异即不等根源）
        Assert.Equal("MwE2Dto::HasContent", Pm(s1, "Name").ConditionMethodRef);
        Assert.Equal("MwE2Dto::IsEmpty", Pm(s2, "Name").ConditionMethodRef);
    }

    // ================================================================
    // 运行时快照发射（承载面 + 字节不变门）
    // ================================================================

    /// <summary>on 态发射：ConditionMethodRef/ConditionShape 条件行在注册段（Fluent B 形态）。</summary>
    [Fact]
    public void RuntimeSnapshot_FluentInlineEmitsConditionRows()
    {
        var source = """
            using NCMapper;
            public class MwR1Entity { public string Name { get; set; } = ""; }
            public class MwR1Dto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MwR1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwR1Entity, MwR1Dto>().Member(d => d.Name).MapWhen(v => v.Length > 0);
                }
            }
            """;
        var code = TestHelper.GenerateRuntimeSnapshotCode(source);
        Assert.False(string.IsNullOrEmpty(code));
        Assert.Contains("ConditionMethodRef = \"NCMapper.Generated.GeneratedMapper::Map_When_", code);
        Assert.Contains("ConditionShape = 1", code);
    }

    /// <summary>字节不变门：无 MapWhen 声明 → 注册段零 ConditionMethodRef 行（既有产物逐字节不变）。</summary>
    [Fact]
    public void RuntimeSnapshot_NoDeclaration_EmptyZeroRows()
    {
        var source = """
            using NCMapper;
            public class MwR2Entity { public string Name { get; set; } = ""; }
            public class MwR2Dto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MwR2Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwR2Entity, MwR2Dto>().Member(d => d.Name).Mask<NCMapper.MaskPhone>();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var code = TestHelper.GenerateRuntimeSnapshotCode(source);
        Assert.DoesNotContain("ConditionMethodRef", code);
        Assert.DoesNotContain("ConditionShape", code);
    }

    // ================================================================
    // 转录矩阵补 + 落点/冲突锁定（2026-09-07）
    // ================================================================

    /// <summary>
    /// 转录矩阵补：成员链（参数根 s.X）+ 算术运算（*）——参数根链原样 +
    /// 算术操作符递归重写（转录维度"成员链/算术"）。
    /// </summary>
    [Fact]
    public void Inline_MemberChainArithmetic_Transcribed()
    {
        var source = """
            using NCMapper;
            public class MwI7Child { public int Score { get; set; } }
            public class MwI7Entity { public MwI7Child Inner { get; set; } = new(); }
            public class MwI7Dto { public MwI7Child Inner { get; set; } = new(); }

            [MapperConfig]
            public class MwI7Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI7Entity, MwI7Dto>()
                       .Member(d => d.Inner).MapWhen(s => s.Score * 2 + 1 > 10);
                }
            }
            """;
        var pm = Pm(FirstSnapshot(source), "Inner");
        Assert.Equal(ConditionShape.FieldValue, pm.ConditionShape);
        Assert.Equal("s", pm.ConditionMethodParamName);
        // 成员链参数根原样（s.Score）+ 算术操作符递归重写（B10 红线：仅参数名原样）
        Assert.Equal("s.Score * 2 + 1 > 10", pm.ConditionMethodBody);
    }

    /// <summary>
    /// 转录矩阵补：私有成员访问 → F41（保留禁令——生成代码同类直呼 private 字段不可达；
    /// #67 先验锚：断言含 F41 诊断存在 + 无 B 形态方法发射）。
    /// </summary>
    [Fact]
    public void Inline_PrivateMemberAccess_F41Rejected()
    {
        var source = """
            using NCMapper;
            public class MwI8Entity { public string Name { get; set; } = ""; }
            public class MwI8Dto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MwI8Mapper
            {
                private static readonly int MinLen = 3;

                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI8Entity, MwI8Dto>()
                       .Member(d => d.Name).MapWhen(v => v.Length > MinLen);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f41 = result.Diagnostics.Where(d => d.Id == "NCMAP_F41").ToList();
        Assert.Single(f41);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, f41[0].Severity);
        // 先验锚：私有访问 span 精确到 MinLen 标识符
        Assert.Contains("MinLen", f41[0].GetMessage());
        // 声明被拒 → 无 B 形态谓词方法发射
        Assert.DoesNotContain("Map_When_", result.GeneratedCode);
    }

    /// <summary>
    /// B 形态方法发射断言（M2-8 检出器）：GeneratedMapper 私有静态谓词方法本体 +
    /// 赋值层守卫调用接线（EmitWhenMethod/EmitGuardedSinglePropertyAssignment 消费闭环）。
    /// </summary>
    [Fact]
    public void Inline_GuardMethodEmitted_InGeneratedCode()
    {
        var source = """
            using NCMapper;
            public class MwI9Entity { public string Name { get; set; } = ""; }
            public class MwI9Dto { public string Name { get; set; } = ""; }

            [MapperConfig]
            public class MwI9Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwI9Entity, MwI9Dto>()
                       .Member(d => d.Name).MapWhen(v => v.Length > 0);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        // ① B 形态谓词方法本体（private static bool Map_When_*(string v) => 转录体）
        Assert.Contains("private static bool Map_When_", result.GeneratedCode);
        Assert.Contains(") => v.Length > 0;", result.GeneratedCode);
        // ② 赋值层守卫接线（同类直呼——global:: 限定形态，RenderWhenCall）
        Assert.Contains("if (global::NCMapper.Generated.GeneratedMapper.Map_When_", result.GeneratedCode);
    }

    /// <summary>
    /// 落点锁定（Q9 闭合验证用例）：.Member(dest).MapTree() × .Member(dest).MapWhen(cond) 同属性——
    /// MapTree 属性级声明物化为普通 pm（BuildMapTreeMapping 同名匹配），守卫照常承载
    /// （树构建配置与三包裹点零交集——Q9 裁定②③的行为锁定）。
    /// </summary>
    [Fact]
    public void MapTree_WhenCoexist_GuardOnNormalPm_Q9Lock()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwT9Node { public int Id { get; set; } public int ParentId { get; set; } public string Label { get; set; } = ""; }
            public class MwT9Dto
            {
                public int Id { get; set; }
                public int ParentId { get; set; }
                public string Label { get; set; } = "";
                public System.Collections.Generic.List<MwT9Dto>? Children { get; set; }

                public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
            }

            [MapperConfig]
            public class MwT9Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwT9Node, MwT9Dto>()
                       .Tree()
                       .Member(d => d.Children).MapTree()
                       .Member(d => d.Label).MapWhen(MwT9Dto.HasContent);
                }
            }
            """;
        var snapshot = FirstSnapshot(source);
        // 树配置就位（Tree() 无参默认三元组——与守卫互不干扰）
        Assert.NotNull(snapshot.TreeConfig);
        // Label 守卫照常承载于普通 pm（MapTree 物化不吞 MapWhen——Q9 锁定）
        var labelPm = snapshot.PropertyMappings.First(p => p.Target.Name == "Label");
        Assert.Equal("MwT9Dto::HasContent", labelPm.ConditionMethodRef);
        Assert.Equal(ConditionShape.FieldValue, labelPm.ConditionShape);
    }

    /// <summary>
    /// 冲突维度（R1 ② 判定表）：[MapWhen] 跨通道适用性——Fluent 对上 DTO 侧
    /// [MapWhen] 特性不生效（Fluent 声明即唯一意图来源，与 [MapIgnore] 跨通道同题
    /// （裁定：特性通道对按 F11 声明者所有）；锁定：pm 无守卫承载。
    /// </summary>
    [Fact]
    public void AttributeMapWhen_OnFluentPair_NotConsumed()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            public class MwX1Entity { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class MwX1Dto
            {
                public int Id { get; set; }

                [MapWhen(nameof(HasContent))]
                public string Name { get; set; } = "";

                public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
            }

            [MapperConfig]
            public class MwX1Mapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwX1Entity, MwX1Dto>().Member(d => d.Id).From(s => s.Id);
                }
            }
            """;
        var snapshot = FirstSnapshot(source);
        // Fluent 通道物化不读 DTO 侧特性声明——Name pm 经自动匹配建立但无守卫承载
        var namePm = snapshot.PropertyMappings.FirstOrDefault(p => p.Target.Name == "Name");
        // 先验锚：自动匹配已建立 Name pm（无守卫断言非"属性不存在"假绿）
        Assert.NotNull(namePm);
        Assert.Null(namePm!.ConditionMethodRef);
        Assert.Equal(ConditionShape.None, namePm.ConditionShape);
    }

    // ================================================================
    // B2-01：MapWhen 路径 SingleOrDefault 多候选防崩（审计批次二）
    // ================================================================

    /// <summary>
    /// B2-01 MapWhen 路径防崩回归锚：多候选 fixture（重载 ≥2 形态）→ 生成器零 CS8785。
    /// <para><b>R1 预案降级登记</b>MapWhen T1 的 condition 形参为闭合委托
    /// Func&lt;TSrcProp,bool&gt;（TSrcProp 由 dest lambda 推断）——方法组转换恒有目标类型，
    /// Roslyn 重载决议总能在候选间择优。三种候选对实测均绑定成功（string/object exact 消歧、
    /// string/泛型 tie-break 消歧、object/IEnumerable&lt;char&gt; 同级隐式引用转换消歧），
    /// CandidateSymbols.Count ≥ 2 结构性不可达 → 多候选 F40 诊断断言不可构造（白盒注入亦不可行
    /// ——SymbolInfo 为 struct 值不可伪造）。防崩门（FirstOrDefault + 计数门）保留为纵深防御，
    /// 与 Convert 路径同型对称（Convert 侧承载完整红绿对照——真多候选 + CS8785 + F26 全链已证）。</para>
    /// </summary>
    [Fact]
    public void MultiOverloadMapWhen_NoCrash_GuardRegression()
    {
        var source = """
            using NCMapper;
            public class MwAmbEntity { public string Name { get; set; } = ""; }
            public class MwAmbDto { public string Name { get; set; } = ""; }
            public static class MwAmbPreds
            {
                public static bool Check(object v) => v != null;
                public static bool Check(System.Collections.Generic.IEnumerable<char> v) => v.Any();
            }

            [MapperConfig]
            public class MwAmbMapper
            {
                public static void Configure(FluentMappingConfig cfg)
                {
                    cfg.For<MwAmbEntity, MwAmbDto>().Member(d => d.Name).MapWhen(MwAmbPreds.Check);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 生成器零崩溃（防崩回归——无论择优结果如何，SingleOrDefault 崩溃形态不得复现）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS8785");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS8786");
    }
}
