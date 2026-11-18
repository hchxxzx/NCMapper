// （B5-4 批次三）：符号解析防御红测——
//   B2-13（F62）：Fluent lambda 深链（17 段 > 上限 16）解析层拒绝；16 段边界守护（合法零诊断）；
//   B4-09（F63）：密集注册图（10_001 对链）单起点环检测访问数 > NodeBudget 10_000
//                → 该起点降级"环检测未决"（不发射 NCMAP029、不标记降级）+ advisory；
//   B3-02：符号版 GetFullName 泛型归一（List<int> → "…List`1"，与特性通道 GlobalConfigReader
//          同源口径，任务书 :203 已落地）——靶点订正：符号版消费点全死路径
//         （ProcessTargetProperty 恒传 config=null），compose 端到端拦截面在值版管线，
//  属 B3-05（任务书 :212 引用"-B3-02 落地形态直接归一（零改动）"）。
// 修复前形态：F62/F63 断言必红（诊断码不存在/不触发）；B3p05（原 B3-02 靶点）为
// 前置登记红测——预期红，B3-05 落地时转绿。
using System.Linq;
using System.Text;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class SymbolResolutionDefenseTests
{
    // ======================= B2-13：F62 深度门（17 段拒绝 / 16 段边界）=======================

    /// <summary>B2-13：17 段源链（> 上限 16）→ F62（Warning），主链失败丢整链
    ///（F60 主链丢弃同语义：U→D 对不注册，City/Name 均随整链丢弃）；16 段以内合法。
    /// 第二对正常映射 V→DV 守护提取管线存活。</summary>
    [Fact]
    public void F62_DeepSourceLambda_Rejected()
    {
        var source = """
            using NCMapper;
            namespace B5T5F62Deep
            {
                public class L16 { public string L17 { get; set; } = ""; }
                public class L15 { public L16 L16 { get; set; } = new(); }
                public class L14 { public L15 L15 { get; set; } = new(); }
                public class L13 { public L14 L14 { get; set; } = new(); }
                public class L12 { public L13 L13 { get; set; } = new(); }
                public class L11 { public L12 L12 { get; set; } = new(); }
                public class L10 { public L11 L11 { get; set; } = new(); }
                public class L9 { public L10 L10 { get; set; } = new(); }
                public class L8 { public L9 L9 { get; set; } = new(); }
                public class L7 { public L8 L8 { get; set; } = new(); }
                public class L6 { public L7 L7 { get; set; } = new(); }
                public class L5 { public L6 L6 { get; set; } = new(); }
                public class L4 { public L5 L5 { get; set; } = new(); }
                public class L3 { public L4 L4 { get; set; } = new(); }
                public class L2 { public L3 L3 { get; set; } = new(); }
                public class L1 { public L2 L2 { get; set; } = new(); }
                public class U
                {
                    public string Name { get; set; } = "";
                    public L1 L1 { get; set; } = new();
                }
                public class D { public string Name { get; set; } = ""; public string City { get; set; } = ""; }
                public class V { public string Name { get; set; } = ""; }
                public class DV { public string Name { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.L1.L2.L3.L4.L5.L6.L7.L8.L9.L10.L11.L12.L13.L14.L15.L16.L17);
                        cfg.For<U, D>().Member(d => d.Name).From(s => s.Name);
                        cfg.For<V, DV>().Member(d => d.Name).From(s => s.Name);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var f62 = result.Diagnostics.Where(d => d.Id == "NCMAP_F62").ToList();
        Assert.Single(f62);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f62[0].Severity);
        Assert.Contains("16", f62[0].GetMessage());
        // F62 触发后主链失败丢整链（F60 主链丢弃同语义）：U→D 对不注册（City/Name 均随整链丢弃）
        Assert.DoesNotContain("Map_U_To_D(", result.GeneratedCode);
        // 第二对正常映射守护管线存活（V→DV 照常注册）
        Assert.True(result.Methods.ContainsKey("Map_V_To_DV"), "正常对 V→DV 应照常注册（管线存活锚）");
        AssertPriorExtraction(result);
    }

    /// <summary>B2-13 边界守护：16 段源链（== 上限）合法——零 F62、深链映射照常生成（校准证据：既有合法面在上限内）。</summary>
    [Fact]
    public void F62_SixteenLevelChain_LegalBoundary()
    {
        var source = """
            using NCMapper;
            namespace B5T5F62Bound
            {
                public class L15 { public string L16 { get; set; } = ""; }
                public class L14 { public L15 L15 { get; set; } = new(); }
                public class L13 { public L14 L14 { get; set; } = new(); }
                public class L12 { public L13 L13 { get; set; } = new(); }
                public class L11 { public L12 L12 { get; set; } = new(); }
                public class L10 { public L11 L11 { get; set; } = new(); }
                public class L9 { public L10 L10 { get; set; } = new(); }
                public class L8 { public L9 L9 { get; set; } = new(); }
                public class L7 { public L8 L8 { get; set; } = new(); }
                public class L6 { public L7 L7 { get; set; } = new(); }
                public class L5 { public L6 L6 { get; set; } = new(); }
                public class L4 { public L5 L5 { get; set; } = new(); }
                public class L3 { public L4 L4 { get; set; } = new(); }
                public class L2 { public L3 L3 { get; set; } = new(); }
                public class L1 { public L2 L2 { get; set; } = new(); }
                public class U
                {
                    public string Name { get; set; } = "";
                    public L1 L1 { get; set; } = new();
                }
                public class D { public string Name { get; set; } = ""; public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.City).From(s => s.L1.L2.L3.L4.L5.L6.L7.L8.L9.L10.L11.L12.L13.L14.L15.L16);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F62");
        // 16 段链照常承载（合法上限内零行为变化——上限校准证据）
        var body = result.Methods["Map_U_To_D"][0].Body;
        Assert.Contains("L16", body);
        AssertPriorExtraction(result);
    }

    // ======================= B4-09：F63 环检测预算（密集注册图）=======================

    /// <summary>B4-09：星型注册图（中心对 10_001 条嵌套边）——起点 0 的环检测访问数超
    /// NodeBudget(10_000) → 该起点降级"未决" + F63（Warning）恰一条；叶对起点在预算内完成（零额外 F63）。
    ///（预修复另实测：10_001 深链形态 DfsDetect 递归 10_001 层直接 StackOverflow 进程崩溃 0xC0000005
    /// ——修复采迭代 DFS 一并消解，深链回归锁见 Commit B ChainGraph_DeepChain_IterativeSafe。）</summary>
    [Fact]
    public void F63_DenseStarGraph_BudgetExceeded_StartUndetermined()
    {
        const int LeafCount = 10_001; // 中心对可达 10_002 节点 > 预算 10_000（最小触发规模）
        var source = BuildStarFixture(LeafCount);
        var result = TestHelper.GenerateMapper(source);

        var f63 = result.Diagnostics.Where(d => d.Id == "NCMAP_F63").ToList();
        Assert.Single(f63);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, f63[0].Severity);
        Assert.Contains("10000", f63[0].GetMessage());
        // 先验锚：规模图正常生成（提取/物化管线跑通）
        AssertPriorExtraction(result);
    }

    /// <summary>B4-09 边界守护：小规模星型图（预算内）零 F63——正常环检测路径不受预算/记忆化影响。</summary>
    [Fact]
    public void F63_SmallGraph_NoBudgetDiagnostic()
    {
        var source = BuildStarFixture(50);
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F63");
        AssertPriorExtraction(result);
    }

    /// <summary>B4-09 深链回归锁（迭代安全，Roslyn 水位内）：300 对链邻接（0→1→…→299）。
    /// Commit B 复测订正：类属性深链 ≥1_000 对实测在 <b>Roslyn 编译器侧</b>爆栈
    ///（TypeRefFromSymbol → get_InstanceConstructors → BinderFactory↔AddNonTypeMembers
    /// 交锁递归，属性符号构建链式触发——与 DfsDetect 实现无关，任何环检测实现均先于
    /// 环检测在编译器侧崩溃；Commit A 消息"DfsDetect 递归 10_001 层爆栈"归因据实订正）。
    /// 本锁落位 Roslyn 水位内深链（300 ≈ 1/3 安全余量），锁深链形态端到端安全：
    /// 301 节点 &lt; NodeBudget 10_000 → 零 F63（预算内完成环检测），迭代 DFS 显式栈
    ///（堆分配）承载 300 深度不占调用栈。超 Roslyn 水位的深链由 DfsDetect 迭代化
    /// 纵深防御（测试宿主外场景不可证，登记后验）。</summary>
    [Fact]
    public void ChainGraph_DeepChain_IterativeSafe()
    {
        const int PairCount = 300; // Roslyn 符号构建安全水位（1_000 实测编译器侧爆栈）
        var source = BuildChainFixture(PairCount);
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F63");
        AssertPriorExtraction(result);
    }

    /// <summary>B4-09 记忆化回归锁（指数爆炸场景——审计登记原始缺陷形态）：深度 29
    /// 菱形平行边图——E_k 双属性同指 E_{k+1}，起点 0 到尾路径数 2^29 ≈ 5.4 亿；
    /// 递归版无记忆化按路径全量重复探索（记忆化回归时本测试挂死分钟级以上），
    /// 跨起点 visited 记忆化按节点线性（2×30=60 节点）毫秒完成。无环 → 零 F63 /
    /// 零 NCMAP029（守护：预算与记忆化不改变合法图结论）。</summary>
    [Fact]
    public void ChainGraph_DiamondGraph_MemoizationSafe()
    {
        const int Depth = 29; // 2^29 条路径（递归版回归时挂死）；节点 60 << 预算 10_000
        var source = BuildDiamondFixture(Depth);
        var result = TestHelper.GenerateMapper(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F63");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP029");
        AssertPriorExtraction(result);
    }

    // ======================= B3-05（前置登记）：符号版 GetFullName 泛型归一（compose 防护）=======================

    /// <summary>B3-05 前置登记红测（原 B3-02 靶点订正）：[MapDefaults(IgnoreTypes=typeof(List&lt;int&gt;))]
    /// × compose 子源泛型属性——归一口径命中 → compose 映射被防护丢弃。B3-02 修复面 =
    /// 符号版 GetFullName 归一（已按任务书 :203 落地），但符号版消费点全死路径
    ///（ProcessTargetProperty 恒传 config=null），compose 端到端拦截面在值版管线
    ///（ApplySource/TargetGlobalIgnoreValue → ShouldIgnoreGlobalValue 按 TypeRef 显示形态判定）
    /// → 属 B3-05（任务书 :212）。本用例期预期红（登记），B3-05 落地时转绿。</summary>
    [Fact]
    public void B3p05_GenericFullName_MetadataNameUnified_ComposeGuardBlocks()
    {
        var source = """
            using NCMapper.Attributes;
            using System.Collections.Generic;

            [assembly: MapDefaults(IgnoreTypes = new[] { typeof(List<int>) })]

            namespace B5T5B3p02
            {
                public class Sub { public List<int> Items { get; set; } = new(); }
                public class U
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    public Sub Sub { get; set; } = new();
                }
                [MapFrom(typeof(U))]
                public class D
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                    [MapProperty(typeof(Sub), "Items")]
                    public List<int> Items { get; set; } = new();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // B3-05 面（值版管线统一）未落地前：本对（U→D，经 Sub.Items）照常发射 dst.Items = src.Items
        //（符号版显示形态不命中 → 防护漏防）→ 本断言预期红（前置登记），B3-05 转绿
        Assert.DoesNotContain("dst.Items = src.Items", result.GeneratedCode);
        Assert.Contains("dst.Id = src.Id", result.GeneratedCode);
        AssertPriorExtraction(result);
    }

    // ======================= X5：F62/F63 双语资源三键齐备（#82 前置拦截形态）=======================

    /// <summary> 验收：F62/F63 的 neutral 资源键可读 + zh-CN 卫星三键成对存在（string 键形态）。</summary>
    [Theory]
    [InlineData("NCMAP_F62")]
    [InlineData("NCMAP_F63")]
    public void NewDefenseDiagnostics_BilingualResources_Present(string ruleId)
    {
        var neutral = Resources.DiagnosticResources.ResourceManager.GetString($"{ruleId}_Title");
        Assert.False(string.IsNullOrWhiteSpace(neutral));
        Assert.False(string.IsNullOrWhiteSpace(
            Resources.DiagnosticResources.ResourceManager.GetString($"{ruleId}_Message")));
        Assert.False(string.IsNullOrWhiteSpace(
            Resources.DiagnosticResources.ResourceManager.GetString($"{ruleId}_Description")));

        var zhPath = System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "NCMapper.SourceGen", "Resources", "DiagnosticResources.zh-CN.resx");
        Assert.True(System.IO.File.Exists(zhPath), $"zh-CN resx 不存在: {zhPath}");
        var zhContent = System.IO.File.ReadAllText(zhPath);
        Assert.Contains($"name=\"{ruleId}_Title\"", zhContent);
        Assert.Contains($"name=\"{ruleId}_Message\"", zhContent);
        Assert.Contains($"name=\"{ruleId}_Description\"", zhContent);
    }

    // ======================= 基建 =======================

    private static void AssertPriorExtraction(MapperGenerationResult result)
    {
        Assert.False(
            string.IsNullOrWhiteSpace(result.GeneratedCode),
            "先验锚失败：本次 fixture 未产出任何生成代码（提取管线未跑通，负向断言无判别力）");
    }

    /// <summary>星型注册图 fixture：中心对 E0→D0 持 leafCount 条嵌套边（P_k : E_k / D_k），
    /// 叶对 E_k→D_k 各持 int Tail（无出边）——起点 0 可达 1+leafCount 节点。</summary>
    private static string BuildStarFixture(int leafCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using NCMapper.Attributes;");
        sb.AppendLine("public class E0 {");
        for (int k = 1; k <= leafCount; k++)
            sb.AppendLine($"    public E{k} P{k} {{ get; set; }}");
        sb.AppendLine("}");
        sb.AppendLine("[MapFrom(typeof(E0))] public class D0 {");
        for (int k = 1; k <= leafCount; k++)
            sb.AppendLine($"    public D{k} P{k} {{ get; set; }}");
        sb.AppendLine("}");
        for (int k = 1; k <= leafCount; k++)
            sb.AppendLine($"public class E{k} {{ public int Tail {{ get; set; }} }}");
        for (int k = 1; k <= leafCount; k++)
            sb.AppendLine($"[MapFrom(typeof(E{k}))] public class D{k} {{ public int Tail {{ get; set; }} }}");
        return sb.ToString();
    }

    /// <summary>深链注册图 fixture：链对 E_k→D_k（k=0..pairCount-1），E_k.Next : E_{k+1} /
    /// D_k.Next : D_{k+1} 构成邻接链 0→1→…→pairCount-1。末对持 int Tail（无出边）。
    /// 注意：pairCount 上限受 Roslyn 符号构建递归深度约束（10_001 实测编译器侧爆栈）。</summary>
    private static string BuildChainFixture(int pairCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using NCMapper.Attributes;");
        for (int k = 0; k < pairCount - 1; k++)
            sb.AppendLine($"public class E{k} {{ public E{k + 1} Next {{ get; set; }} }}");
        sb.AppendLine($"public class E{pairCount - 1} {{ public int Tail {{ get; set; }} }}");
        for (int k = 0; k < pairCount - 1; k++)
            sb.AppendLine($"[MapFrom(typeof(E{k}))] public class D{k} {{ public D{k + 1} Next {{ get; set; }} }}");
        sb.AppendLine($"[MapFrom(typeof(E{pairCount - 1}))] public class D{pairCount - 1} {{ public int Tail {{ get; set; }} }}");
        return sb.ToString();
    }

    /// <summary>菱形重复路径图 fixture：E_k.P / E_k.Q 双属性同指 E_{k+1}（平行嵌套边
    /// × depth 层 → 起点到尾路径数 2^depth）——递归版无记忆化按路径探索指数爆炸，
    /// 记忆化迭代版按节点线性（2×(depth+1)）。无回边（非环）。</summary>
    private static string BuildDiamondFixture(int depth)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using NCMapper.Attributes;");
        for (int k = 0; k < depth; k++)
            sb.AppendLine($"public class E{k} {{ public E{k + 1} P {{ get; set; }} public E{k + 1} Q {{ get; set; }} }}");
        sb.AppendLine($"public class E{depth} {{ public int Tail {{ get; set; }} }}");
        for (int k = 0; k < depth; k++)
            sb.AppendLine($"[MapFrom(typeof(E{k}))] public class D{k} {{ public D{k + 1} P {{ get; set; }} public D{k + 1} Q {{ get; set; }} }}");
        sb.AppendLine($"[MapFrom(typeof(E{depth}))] public class D{depth} {{ public int Tail {{ get; set; }} }}");
        return sb.ToString();
    }
}
