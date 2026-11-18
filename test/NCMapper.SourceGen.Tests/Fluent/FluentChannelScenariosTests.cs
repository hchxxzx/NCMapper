using System.Linq;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests.Fluent;

/// <summary>
/// Fluent 通道业务场景测试（spike 00/01/02/03/09 组移植，
/// S-4b 按业务场景命名 + 端到端断言改写：spike 断言中间产物 FluentSpikeSnapshot，
/// 生产化断言完整管线生成代码与诊断）。
/// <para>覆盖：链式扫描（难点①）/ lambda 属性路径（难点②）/ 链序保真（难点③）/
/// 多 (S,T) 对单 Configure（09 组）。</para>
/// </summary>
public class FluentChannelScenariosTests
{
    // ── 00 冒烟组 ──────────────────────────────────────────────────────

    /// <summary>spike 00-1：单 For+Map 端到端——合法映射生成（链路打通）</summary>
    [Fact]
    public void One_For_Map_GeneratesMappedAssignment()
    {
        var source = """
            using NCMapper;
            namespace Sc1
            {
                public class User { public string Name { get; set; } = ""; }
                public class UserDto { public string FullName { get; set; } = ""; }

                [MapperConfig]
                public static class SmokeMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<User, UserDto>().Member(dest => dest.FullName).From(src => src.Name);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 零 Fluent 诊断
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        // 显式异名映射生效
        Assert.Contains("Map_User_To_UserDto(", result.GeneratedCode);
        Assert.Contains("dst.FullName = src.Name;", result.GeneratedCode);
    }

    /// <summary>spike 00-2：空 Configure——零快照零诊断（无声明即无生成）</summary>
    [Fact]
    public void EmptyConfigure_NoGeneration_NoDiagnostics()
    {
        var source = """
            using NCMapper;
            namespace Sc2
            {
                [MapperConfig]
                public static class EmptyMapper
                {
                    public static void Configure(FluentMappingConfig cfg) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 无声明 → 无 F-诊断（F15 仅在"无匹配签名方法"时触发——此例方法签名匹配，体为空）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        // 生成骨架存在但无任何映射方法/注册
        Assert.DoesNotContain("RegisterGeneratedCore<", result.GeneratedCode);
    }

    // ── 01 链式调用组（难点①）─────────────────────────────────────────

    /// <summary>spike 01-1：三层链 Map+Ignore——Ignore 属性不映射、Map 属性映射</summary>
    [Fact]
    public void Chain_MapThenIgnore_IgnoredPropertyNotMapped()
    {
        var source = """
            using NCMapper;
            namespace Sc3
            {
                public class User { public string Name { get; set; } = ""; public string Password { get; set; } = ""; }
                public class UserDto { public string Name { get; set; } = ""; public string Password { get; set; } = ""; }

                [MapperConfig]
                public static class ChainMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<User, UserDto>().Member(dest => dest.Name).From(src => src.Name).Member(dest => dest.Password).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));

        // 方法体级断言（排除 DeepCopy 同名属性赋值干扰；对象初始化器形态——无 dst. 前缀）
        var body = result.Methods["Map_User_To_UserDto"].Single().Body;
        Assert.Contains("Name = src.Name", body);
        // Ignore 语义：不生成 Password 赋值（同名可映射但显式忽略）
        Assert.DoesNotContain("Password = ", body);
    }

    /// <summary>spike 01-2：Map + Mask 混合链——脱敏字段生成（MaskProvider 共享下游）</summary>
    [Fact]
    public void Chain_MapAndMask_MaskedFieldGenerated()
    {
        var source = """
            using NCMapper;
            namespace Sc4
            {
                public class User { public string Name { get; set; } = ""; public string CardNo { get; set; } = ""; }
                public class UserDto { public string Name { get; set; } = ""; public string CardNo { get; set; } = ""; }
                public class CardMask : NCMapper.IMask { public string? Mask(string? value) => "***"; }

                [MapperConfig]
                public static class MixedMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<User, UserDto>()
                           .Member(dest => dest.Name).From(src => src.Name)
                           .Member(dest => dest.CardNo).Mask<CardMask>();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));

        Assert.Contains("dst.Name = src.Name;", result.GeneratedCode);
        // Mask 生成：脱敏器静态字段 + 脱敏赋值（与 [Mask] 通道共享生成路径）
        Assert.Contains("__mask_", result.GeneratedCode);
        Assert.Contains("MaskProvider.Resolve", result.GeneratedCode);
    }

    // ── 02 lambda 解析组（难点②）──────────────────────────────────────

    /// <summary>spike 02-1：单层属性路径（双侧）</summary>
    [Fact]
    public void Lambda_SingleProperty_BothSides_Resolved()
    {
        var source = """
            using NCMapper;
            namespace Sc5
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(dest => dest.N).From(src => src.N);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        Assert.Contains("dst.N = src.N;", result.GeneratedCode);
    }

    /// <summary>spike 02-2：嵌套属性路径（源侧多段 → 扁平化读取链）</summary>
    [Fact]
    public void Lambda_NestedSourcePath_FlattenedReadChain()
    {
        var source = """
            using NCMapper;
            namespace Sc6
            {
                public class Address { public string City { get; set; } = ""; }
                public class U { public Address Address { get; set; } = ""; }
                public class D { public string City { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(dest => dest.City).From(src => src.Address.City);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        // 嵌套源路径：生成读取链（显式点路径语义——src.Address.City）
        Assert.Contains("src.Address", result.GeneratedCode);
        Assert.Contains(".City", result.GeneratedCode);
    }

    /// <summary>spike 02-3：属性链上集合 Count（List&lt;T&gt;.Count 是属性非方法调用）</summary>
    [Fact]
    public void Lambda_ItemsCount_PropertyChain_Resolved()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace Sc7
            {
                public class U { public List<int> Items { get; set; } = new(); }
                public class D { public int Count { get; set; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(dest => dest.Count).From(src => src.Items.Count);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        // Count 是属性（非方法调用）——Items.Count 路径可解析
        Assert.Contains("src.Items", result.GeneratedCode);
        Assert.Contains("Count", result.GeneratedCode);
    }

    /// <summary>spike 02-4：null 抑制（src.Address!.City）——语义等同直接访问</summary>
    [Fact]
    public void Lambda_NullSuppression_Resolved()
    {
        var source = """
            using NCMapper;
            namespace Sc8
            {
                public class Addr { public string City { get; set; } = ""; }
                public class U { public Addr? Address { get; set; } }
                public class D { public string? City { get; set; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(dest => dest.City).From(src => src.Address!.City);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        Assert.Contains("src.Address", result.GeneratedCode);
    }

    // ── 03 链序组（难点③）────────────────────────────────────────────

    /// <summary>spike 03-1：Map→Ignore→Map 混合链序——不重排不丢失（行为断言）</summary>
    [Fact]
    public void Ordering_MapIgnoreMap_AllPreserved()
    {
        var source = """
            using NCMapper;
            namespace Sc9
            {
                public class U { public string A { get; set; } = ""; public string B { get; set; } = ""; public string C { get; set; } = ""; }
                public class D { public string A { get; set; } = ""; public string B { get; set; } = ""; public string C { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.A).From(s => s.A).Member(d => d.B).Ignore().Member(d => d.C).From(s => s.C);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        // Map(A)/Map(C) 映射、Ignore(B) 忽略——不丢失不误映射（方法体级断言排除 DeepCopy 干扰；
        // 对象初始化器形态——无 dst. 前缀）
        var body = result.Methods["Map_U_To_D"].Single().Body;
        Assert.Contains("A = src.A", body);
        Assert.Contains("C = src.C", body);
        Assert.DoesNotContain("B = ", body);
    }

    /// <summary>spike 03-2：同方法多次调用不丢失（2 Map + 2 Ignore）</summary>
    [Fact]
    public void Ordering_SameMethodTwice_NotLost()
    {
        var source = """
            using NCMapper;
            namespace Sc10
            {
                public class U { public string A { get; set; } = ""; public string B { get; set; } = ""; public string C { get; set; } = ""; public string D { get; set; } = ""; }
                public class Dto { public string A { get; set; } = ""; public string B { get; set; } = ""; public string C { get; set; } = ""; public string D { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, Dto>()
                           .Member(d => d.A).From(s => s.A).Member(d => d.B).From(s => s.B)
                           .Member(d => d.C).Ignore().Member(d => d.D).Ignore();
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        var body = result.Methods["Map_U_To_Dto"].Single().Body;
        Assert.Contains("A = src.A", body);
        Assert.Contains("B = src.B", body);
        Assert.DoesNotContain("C = ", body);
        Assert.DoesNotContain("D = ", body);
    }

    // ── 04 跨方法引用组（难点④）────────────────────────────────────────

    /// <summary>spike 04-2：AfterMapping 引用外部类静态方法——生成代码发射跨类调用（可见性契约内）</summary>
    [Fact]
    public void AfterMapping_ExternalClass_ReferenceEmitted()
    {
        var source = """
            using NCMapper;
            namespace Sc16
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }
                public static class Helper { public static void Post(U s, D d) { } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.N).From(s => s.N).AfterMapping(Helper.Post);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        // 跨类方法引用发射（含 F12/F14 检查通过——public static 可见）
        Assert.Contains("Sc16.Helper.Post(src, dst);", result.GeneratedCode);
    }

    // ── 05 泛型参数组（难点⑤）─────────────────────────────────────────

    /// <summary>spike 05-1：类型对解析——生成签名含完全限定类型名（TypeRef.FullName 一致性）</summary>
    [Fact]
    public void GenericArg_SimplePair_FullNameInSignature()
    {
        var source = """
            using NCMapper;
            namespace Sc11
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<U, D>().Member(d => d.N).From(s => s.N);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // TypeRef.FromName 一致性：签名使用完全限定名（与 [MapFrom] 通道同口径）
        Assert.Contains("Map_U_To_D(Sc11.U src)", result.GeneratedCode);
        Assert.Contains("Sc11.D? Map_U_To_D", result.GeneratedCode);
        // 注册行同型
        Assert.Contains("RegisterGeneratedCore<Sc11.U, Sc11.D>", result.GeneratedCode);
    }

    /// <summary>spike 05-2：构造泛型（List&lt;U&gt）类型对——MetadataName 含 arity 标记</summary>
    [Fact]
    public void GenericArg_ConstructedGenericType_Resolved()
    {
        var source = """
            using NCMapper;
            using System.Collections.Generic;
            namespace Sc12
            {
                public class U { public string N { get; set; } = ""; }
                public class D { public int Count { get; set; } }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg) =>
                        cfg.For<List<U>, D>().Member(d => d.Count).From(s => s.Count);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        // 构造泛型解析：方法名取 SimpleName（List`1 → List）
        Assert.Contains("Map_List_To_D(", result.GeneratedCode);
    }

    // ── 09 多类型对组 ─────────────────────────────────────────────────

    /// <summary>spike 09-1：单 Configure 声明 2 个 (S,T) 对——两快照均派生</summary>
    [Fact]
    public void TwoTypePairsInOneConfigure_BothGenerated()
    {
        var source = """
            using NCMapper;
            namespace Sc13
            {
                public class U1 { public string N { get; set; } = ""; }
                public class D1 { public string N { get; set; } = ""; }
                public class U2 { public string N { get; set; } = ""; }
                public class D2 { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U1, D1>().Member(d => d.N).From(s => s.N);
                        cfg.For<U2, D2>().Member(d => d.N).Ignore();
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));

        // 两对各自派生 + 注册（增量粒度=单 (S,T) 链）
        Assert.Contains("Map_U1_To_D1(", result.GeneratedCode);
        Assert.Contains("Map_U2_To_D2(", result.GeneratedCode);
        Assert.Contains("RegisterGeneratedCore<Sc13.U1, Sc13.D1>", result.GeneratedCode);
        Assert.Contains("RegisterGeneratedCore<Sc13.U2, Sc13.D2>", result.GeneratedCode);
        // 第二对 Ignore：无 N 映射
        Assert.Contains("dst.N = src.N;", result.GeneratedCode); // 第一对
    }

    /// <summary>spike 09-2：单 Configure 声明 3 个 (S,T) 对——全部独立派生</summary>
    [Fact]
    public void ThreeTypePairs_AllDistinct()
    {
        var source = """
            using NCMapper;
            namespace Sc14
            {
                public class U1 { public string N { get; set; } = ""; }
                public class D1 { public string N { get; set; } = ""; }
                public class U2 { public string N { get; set; } = ""; }
                public class D2 { public string N { get; set; } = ""; }
                public class U3 { public string N { get; set; } = ""; }
                public class D3 { public string N { get; set; } = ""; }

                [MapperConfig]
                public static class M
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<U1, D1>().Member(d => d.N).From(s => s.N);
                        cfg.For<U2, D2>().Member(d => d.N).From(s => s.N);
                        cfg.For<U3, D3>().Member(d => d.N).From(s => s.N);
                    }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        Assert.Contains("Map_U1_To_D1(", result.GeneratedCode);
        Assert.Contains("Map_U2_To_D2(", result.GeneratedCode);
        Assert.Contains("Map_U3_To_D3(", result.GeneratedCode);
    }

    /// <summary>spike 09-3：跨方法承载多对（类内多 Configure 方法——A1.5 方法名任意）</summary>
    [Fact]
    public void MultipleConfigureMethods_OneClass_AllScanned()
    {
        var source = """
            using NCMapper;
            namespace Sc15
            {
                public class A { public int Id { get; set; } }
                public class ADto { public int Id { get; set; } }
                public class B { public int Id { get; set; } }
                public class BDto { public int Id { get; set; } }

                [MapperConfig]
                public class CatalogMapper
                {
                    public static void ConfigureCategory(FluentMappingConfig cfg) =>
                        cfg.For<A, ADto>().Member(d => d.Id).From(s => s.Id);

                    public static void ConfigureTag(FluentMappingConfig cfg) =>
                        cfg.For<B, BDto>().Member(d => d.Id).From(s => s.Id);
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("NCMAP_F"));
        // 方法名任意（ConfigureCategory/ConfigureTag 均命中签名约定）
        Assert.Contains("Map_A_To_ADto(", result.GeneratedCode);
        Assert.Contains("Map_B_To_BDto(", result.GeneratedCode);
    }
}
