// （B5-4 批次四）：生成码可编译性红测——
//   B1-23：同名冲突对后缀分配跨构建确定（分配循环消费排序产物；缺陷态 = 原始声明序决定
//          后缀归属 → 两序列化产物后缀互换，nsB 版方法名错拿空后缀）；
//   B1-05：嵌套/元素调用名与注册名一致（对级键；缺陷态 = 同 src 两对同名组冲突时，
//          typeNameMap first-wins 后缀 ≠ 元素对对级后缀 → 发射 Map_User_To_D_2( 而注册
//          名为 Map_User_2_To_D_2 → CS0103）；
//   B1-06：compose 子源继承 case 拓扑序（派生先于基类；缺陷态 = 声明序发射 → 基类 case
//          在前吞掉派生 case → CS8120）；
//   B1-07：关键字属性名 @ 转义（缺陷态 = dst.class/src.event 裸发射 → 生成码编译失败）；
//   B1-08：方向跳过映射的 [Mask] 字段零声明（缺陷态 = 收集侧只查 MaskType 不查方向 →
//          字段声明零引用 → CS0414 面）+ 合法 [Mask] 字段仍声明（防过紧误删正守护）；
//   B1-19：内联转录 ns 根 global:: 限定（缺陷态 = INamespaceSymbol 分支裸 ToDisplayString）。
// 修复前形态：全部断言必红（编译失败/文本形态不符/产物非确定）。
using NCMapper;
using NCMapper.Attributes;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class CodegenCompilabilityDefenseTests
{
    // ======================= B1-23：后缀分配跨构建确定 ========================

    /// <summary>B1-23：同名冲突对（NsA.Pair→NsA.D / NsB.Pair→NsB.D）后缀分配按 FullName
    /// 排序序——nsA 恒空后缀、nsB 恒 _2，与类声明序无关。缺陷态：分配循环按原始声明序
    /// 计数 → 序 B（nsB.Dto 先声明）中 nsB 拿空后缀（归属互换）→ 注册签名断言红。</summary>
    [Fact]
    public void B1p23_SuffixAssignment_CrossBuildDeterministic()
    {
        // 序 A：nsA 对先声明
        var sourceA = """
            using NCMapper.Attributes;
            namespace NsA
            {
                public class Pair { public int Id { get; set; } }
                [MapFrom(typeof(Pair))] public class D { public int Id { get; set; } }
            }
            namespace NsB
            {
                public class Pair { public int Id { get; set; } }
                [MapFrom(typeof(Pair))] public class D { public int Id { get; set; } }
            }
            """;
        // 序 B：nsB 对先声明（仅声明序不同，类型集一致）
        var sourceB = """
            using NCMapper.Attributes;
            namespace NsB
            {
                public class Pair { public int Id { get; set; } }
                [MapFrom(typeof(Pair))] public class D { public int Id { get; set; } }
            }
            namespace NsA
            {
                public class Pair { public int Id { get; set; } }
                [MapFrom(typeof(Pair))] public class D { public int Id { get; set; } }
            }
            """;
        var codeA = TestHelper.GenerateMapper(sourceA).GeneratedCode;
        var codeB = TestHelper.GenerateMapper(sourceB).GeneratedCode;

        // 两序产物中后缀归属一致（排序序）：nsA 版恒 Map_Pair_To_D、nsB 版恒 Map_Pair_2_To_D_2
        //（签名锚 = 方法名 + 参数类型全名，区分归属）
        Assert.Contains("Map_Pair_To_D(NsA.Pair", codeA);
        Assert.Contains("Map_Pair_2_To_D_2(NsB.Pair", codeA);
        Assert.Contains("Map_Pair_To_D(NsA.Pair", codeB);
        Assert.Contains("Map_Pair_2_To_D_2(NsB.Pair", codeB);
    }

    // ======================= B1-05：嵌套调用名与注册名一致（对级键）=======================

    /// <summary>B1-05：同 src 两对（NsA.User→NsA.D 空 / →NsB.D _2，同名组冲突）+ 第三对
    /// 集合嵌套元素对 = 第二对——嵌套调用名须与第二对注册名 Map_User_2_To_D_2 一致。
    /// 缺陷态：typeNameMap[NsA.User] first-wins 取对 1 的 "User"（≠ 对 2 的对级后缀 _2）
    /// → 发射 Map_User_To_D_2( → CS0103（编译断言红）。</summary>
    [Fact]
    public void B1p05_NestedCallName_MatchesRegisteredPairSuffix()
    {
        var source = """
            using NCMapper.Attributes;
            namespace NsA
            {
                public class User { public int Id { get; set; } }
                [MapFrom(typeof(User))] public class D { public int Id { get; set; } }
            }
            namespace NsB
            {
                [MapFrom(typeof(NsA.User))] public class D { public int Id { get; set; } }
            }
            namespace NsC
            {
                public class X { public NsA.User Inner { get; set; } = new(); }
                [MapFrom(typeof(X))] public class Y { public NsB.D Inner { get; set; } = new(); }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 注册名在案（对 2 的 Core 方法）
        Assert.Contains("Map_User_2_To_D_2(", result.GeneratedCode);
        // 缺陷态错名零发射（嵌套调用不得用 first-wins 后缀）
        Assert.DoesNotContain("Map_User_To_D_2(", result.GeneratedCode);
        // 生成码整体可编译（CS0103 面——修复前发射错名 → 编译失败红）
        TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
    }

    // ======================= B1-06：compose case 继承拓扑序 ========================

    /// <summary>B1-06：compose 子源继承（SubChild : SubBase）且声明序基类在前——case 发射
    /// 须派生先于基类（switch pattern subsumption）；缺陷态 = 声明序 → CS8120 编译失败。
    /// 文本断言 Child case 行序 &lt; Base case 行序（拓扑排序证据）。</summary>
    [Fact]
    public void B1p06_ComposeCaseInheritance_DerivedCaseBeforeBase()
    {
        var source = """
            using NCMapper.Attributes;
            namespace B5T4B106
            {
                public class SubBase { public int A { get; set; } }
                public class SubChild : SubBase { public int B { get; set; } }
                public class U
                {
                    public int Id { get; set; }
                    public SubBase Base { get; set; } = new();
                    public SubChild Child { get; set; } = new();
                }
                [MapFrom(typeof(U))]
                public class D
                {
                    public int Id { get; set; }
                    [MapProperty(typeof(SubBase), "A")] public int A { get; set; }
                    [MapProperty(typeof(SubChild), "B")] public int B { get; set; }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var code = result.GeneratedCode;

        // 拓扑序：派生 case（SubChild）先于基类 case（SubBase）
        var childCase = code.IndexOf("case B5T4B106.SubChild ", System.StringComparison.Ordinal);
        var baseCase = code.IndexOf("case B5T4B106.SubBase ", System.StringComparison.Ordinal);
        Assert.True(childCase >= 0, "SubChild case 未发射");
        Assert.True(baseCase >= 0, "SubBase case 未发射");
        Assert.True(childCase < baseCase, $"派生 case（{childCase}）须先于基类 case（{baseCase}）发射");

        // 生成码整体可编译（CS8120 面——修复前基类 case 在前 → 编译失败红）
        TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
    }

    // ======================= B1-07：关键字属性名 @ 转义 ========================

    /// <summary>B1-07：属性名为 C# 关键字（event/class）——发射引用处须 @ 前缀转义；
    /// 缺陷态 = dst.event/src.class 裸发射 → 生成码语法错误编译失败（红）。</summary>
    [Fact]
    public void B1p07_KeywordPropertyNames_EscapedInGeneratedCode()
    {
        var source = """
            using NCMapper.Attributes;
            namespace B5T4B107
            {
                public class U
                {
                    public string @event { get; set; } = "";
                    public string @class { get; set; } = "";
                }
                [MapFrom(typeof(U))]
                public class D
                {
                    public string @event { get; set; } = "";
                    public string @class { get; set; } = "";
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 双端关键字属性 @ 转义（源侧经 BuildSourceAccess、目标侧经赋值发射）
        Assert.Contains("src.@event", result.GeneratedCode);
        Assert.Contains("dst.@class", result.GeneratedCode);
        // 裸关键字形态零发射
        Assert.DoesNotContain("dst.event ", result.GeneratedCode);
        Assert.DoesNotContain("dst.class ", result.GeneratedCode);
        // 生成码整体可编译（关键字语法错误面——修复前编译失败红）
        TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
    }

    // ======================= B1-08：掩码字段收集谓词对齐 ========================

    /// <summary>B1-08 场景 A：[Mask] × [MapIgnore(ToDto)] 方向跳过——引用侧零 Mask 调用，
    /// 收集侧须同样零声明（缺陷态 = 只查 MaskType → __mask_D_Phone 声明零引用 → CS0414 面）。</summary>
    [Fact]
    public void B1p08_MaskField_DirectionSkipped_NoDeclaration()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace B5T4B108
            {
                public class U { public string Phone { get; set; } = ""; }
                [MapFrom(typeof(U))]
                public class D
                {
                    [Mask(typeof(MaskPhone))]
                    [MapIgnore(Direction.ToDto)]
                    public string Phone { get; set; } = "";
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 方向跳过 → 掩码字段零声明（缺陷态：__mask_D_Phone 被声明但零引用 → 红）
        Assert.DoesNotContain("__mask_", result.GeneratedCode);
    }

    /// <summary>B1-08 正守护（R2 防过紧误删）：合法 [Mask]（无方向跳过）字段仍声明仍引用。</summary>
    [Fact]
    public void B1p08_MaskField_LegalStillDeclaredAndReferenced()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace B5T4B108B
            {
                public class U { public string Phone { get; set; } = ""; }
                [MapFrom(typeof(U))]
                public class D
                {
                    [Mask(typeof(MaskPhone))]
                    public string Phone { get; set; } = "";
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("__mask_", result.GeneratedCode);
        Assert.Contains(".Mask(", result.GeneratedCode);
    }

    // ======================= B1-19：内联转录 ns 根 global:: 限定 ========================

    /// <summary>B1-19（直调形态——InternalsVisibleTo）：MapWhen 内联谓词引用命名空间根符号
    ///（B5T9.NsUtil.IsPositive）——INamespaceSymbol 分支须发射 global:: 限定（缺陷态 = 裸
    /// ToDisplayString → 用户上下文同名符号歧义面），且转录放行零 F41/F42。</summary>
    [Fact]
    public void B1p19_InlineTranscribe_NamespaceRoot_GlobalQualified()
    {
        var source = """
            using NCMapper;
            namespace B5T9
            {
                public static class NsUtil { public static bool IsPositive(int v) => v > 0; }
            }
            namespace B5T4B119
            {
                public class U { public int Amount { get; set; } }
                public class D { public int Amount { get; set; } }
                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Amount).MapWhen<U>(s => B5T9.NsUtil.IsPositive(s.Amount));
                    }
                }
            }
            """;
        // 直调转录器（端到端生成码中类型段已被 GlobalQualifiedType 限定，ns 分支差异被吸收——
        // 直调为 ns 分支唯一可观测面）
        var compilation = TestHelper.BuildCompilation(source);
        var tree = compilation.SyntaxTrees[0];
        var sm = compilation.GetSemanticModel(tree);
        var lambda = tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax>()
            .First(l => l.ToString().Contains("B5T9.NsUtil"));
        var diagnostics = new System.Collections.Generic.List<NCMapper.SourceGen.DiagnosticInfo>();
        var result = NCMapper.SourceGen.InlineTranscriber.TryTranscribe(
            lambda, sm, diagnostics, System.Threading.CancellationToken.None);

        Assert.NotNull(result);
        Assert.DoesNotContain(diagnostics, d => d.Descriptor.Id == "NCMAP_F41");
        Assert.DoesNotContain(diagnostics, d => d.Descriptor.Id == "NCMAP_F42");
        // ns 根 global:: 限定（缺陷态：裸 "B5T9" → 红）
        Assert.Contains("global::B5T9.", result!.Body);
    }
}
