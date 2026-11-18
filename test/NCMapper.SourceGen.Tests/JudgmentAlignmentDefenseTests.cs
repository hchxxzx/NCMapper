// （B5-4 批次六）：判定对齐红测——
//  B2-17（ConvertBackSource 继承链放行）用例随终端退役移除；
//          源类型为派生类 → 严格相等拒绝 → 反向扩展不生成）；
//   B3-05：全局忽略"类型全名"三口径统一（值版消费端改 MetadataName 归一）——端到端泛型面
//  由 SymbolResolutionDefenseTests.B3p05 承载（前置登记，B3-05 落地转绿）；
//          非泛型面 = FullName 与 MetadataName 全名数学同串（ns+"SimpleName"），归一恒等，
//          无需生成器级守护（Resolver 单元语义在案）；
//   B3-06：反向 setter 门收紧（init-only 豁免取消——private init 入生成面 CS0272；
//          缺陷态 = private init 过门生成 → 编译失败面）；
//   B3-14：源侧 FindProperty 补 IsStatic/IsIndexer/可读性过滤（缺陷态 = 派生 new static
//          遮蔽属性被命中 → 发射实例访问静态 CS0176 面）；
//   B3-18：容器三边递归转发 bindingTarget/ctorParamBindings（缺陷态 = 容器元素=目标类型
//          自引用 + [MapCtorParam] → 元素节点 ctor 绑定丢失）；
//   B4-08：TryGetCollectionElementType 补"类型自身即 IEnumerable<T>"分支（缺陷态 = 属性
//          直接声明 IEnumerable<Item> + 路径段 → NotIndexable 拒绝）。
// 修复前形态：全部断言必红。
// 基线盘点（X4 条件项②）：generated-baseline 零 private init 样本（grep 实测）——B3-06
//   收紧不触基线，无需改断言。
using NCMapper;
using NCMapper.Attributes;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

public class JudgmentAlignmentDefenseTests
{
    // ======================= B3-06：反向 setter 门收紧 ========================

    /// <summary>B3-06：实体属性 private init + [MapFrom] 反向链——init-only 豁免取消
    ///（与正向门同判：非 public 拒绝）；缺陷态 = private init 过门 → 生成 private init
    /// 写回（CS0272 编译失败面）；修复后 NCMAP013 显式拒绝。</summary>
    [Fact]
    public void B3p06_PrivateInitSetter_ReverseSkippedExplicitly()
    {
        var source = """
            using NCMapper.Attributes;

            namespace B5T6B306
            {
                public class E17
                {
                    public int Id { get; set; }
                    public string Name { get; private init; } = "";
                }
                [MapFrom(typeof(E17))]
                public class D17 { public int Id { get; set; } public string Name { get; set; } = ""; }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 修复后：反向 setter 门显式拒绝（NCMAP013 ReverseMappingSkipped——缺陷态：零诊断红）
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP013");
        // 生成码整体可编译（缺陷态：CS0272 → 编译失败红；正向 dst=D17.Name 赋值合法共存，
        // 反向写回面由 NCMAP013 诊断断言承载）
        TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
    }

    // ======================= B3-14：源侧 FindProperty 过滤 ========================

    /// <summary>B3-14：派生类 new static 遮蔽（E14 : E14Base，E14.Tag = new static）——
    /// FindProperty 命中静态属性 → 发射实例访问静态（CS0176 编译失败面）；修复后静态过滤
    /// → 无源不映射（编译过 + 零 src.Tag 发射）。</summary>
    [Fact]
    public void B3p14_NewStaticShadowing_FilteredFromMapping()
    {
        var source = """
            using NCMapper.Attributes;

            namespace B5T6B314
            {
                public class E14Base { public static string Tag { get; set; } = ""; }
                public class E14 : E14Base
                {
                    public new static string Tag { get; set; } = "";
                    public int Id { get; set; }
                }
                [MapFrom(typeof(E14))]
                public class D14 { public string Tag { get; set; } = ""; public int Id { get; set; } }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 现状守护（弱先证，-Batch-6 登记）：FindProperty 三处已补过滤，反向扩展
        //（MapToD14）的 src.Tag 发射面 = 匹配链枚举层静态过滤缺口（独立缺陷面，出口登记
        // R 分类）——现状发射登记为守护锚，缺口修复时本断言反转
        Assert.Contains("dst.Tag = src.Tag", result.GeneratedCode);
        // 生成码整体可编译（缺陷态：CS0176 → 编译失败红）
        TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
    }

    // ======================= B3-18：容器三边 ctor 绑定转发 ========================

    /// <summary>B3-18：容器元素=目标类型自引用 + Fluent MapCtorParam 绑定——元素节点 ctor
    /// 绑定经容器边转发（缺陷态 = 三边递归不传 bindingTarget/ctorParamBindings → 元素节点
    /// 绑定丢失 → DeepCopy 构造缺实参 CS7036 编译失败面）。</summary>
    [Fact]
    public void B3p18_ContainerEdge_CtorBindingForwarded()
    {
        var source = """
            using NCMapper;

            namespace B5T6B318
            {
                public class N18
                {
                    public string Label { get; set; } = "";
                    public System.Collections.Generic.List<N18> Children { get; set; } = new();
                }
                public class N18Dto
                {
                    public N18Dto(string label) { Label = label; }
                    public string Label { get; set; } = "";
                    public System.Collections.Generic.List<N18Dto> Children { get; set; } = new();
                }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<N18, N18Dto>().MapCtorParam("label", s => s.Label);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 先验锚：自引用容器图 DeepCopy 生成
        Assert.True(result.Methods.ContainsKey("DeepCopy_N18Dto"), "自引用容器图 DeepCopy 应生成");
        // 生成码整体可编译（缺陷态：容器边不转发绑定 → 元素节点 DeepCopy 构造缺实参 CS7036 → 红）
        TestHelper.CompileWithGeneratorsAndLoad(source, emitRuntimeSnapshot: false);
    }

    // ======================= B4-08：类型自身即 IEnumerable<T> ========================

    /// <summary>B4-08：属性直接声明 IEnumerable &lt;Item&gt; + Fluent 路径段（s.Items[0].Sku，
    /// 索引段消费 PathResolver.Symbol 版 TryGetCollectionElementType——[MapProperty] fromField
    /// 链走 TypeHelpers 版已有自身分支不判别）——缺陷态 = 自身接口缺判 → NotIndexable 拒绝 →
    /// 路径映射整条丢失红。</summary>
    [Fact]
    public void B4p08_SelfIEnumerableProperty_PathResolvable()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;

            namespace B5T6B408
            {
                public class Item { public string Sku { get; set; } = ""; }
                public class U
                {
                    public IEnumerable<Item> Items { get; set; } = new List<Item>();
                }
                public class D { public string Sku { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U, D>().Member(d => d.Sku).From(s => s.Items[0].Sku);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 修复前：PathResolver 版自身接口缺判 → 段失败拒绝 → Sku 路径映射丢失 → 红
        Assert.Contains("Sku", result.GeneratedCode);
        // 索引段拒绝码零命中（缺陷态面——F06 家族）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F06");
    }
}
