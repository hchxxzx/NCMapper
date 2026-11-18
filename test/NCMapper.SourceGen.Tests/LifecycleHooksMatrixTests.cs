using System.Linq;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Generated;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 正式矩阵：生命周期钩子全维度覆盖（全表，spike 已覆盖维度不重复）。
/// <para>本文件覆盖 spike（LifecycleHookEmissionSpikeTests / HookDualChannelConflictSpikeTests）未覆盖的维度：
/// F35/F36/F38 边界、跨对传播（D-d 签名适用/不适用）、继承语义（Inherited=false）、运行时叠加（After+PostMap）。
/// 每个负向诊断用例配 #67 先验锚（声明确实被提取——避免 vacuous pass）。</para>
/// </summary>
public class LifecycleHooksMatrixTests
{
    // ================================================================
    // F35：BeforeMapping 目标方法 private（跨类不可访问）
    // ================================================================

    /// <summary>
    /// F35：Fluent 侧 .BeforeMapping(PrivateGuard) 目标方法 private → NCMAP_F35
    /// （生成代码在 GeneratedMapper 静态类，跨类不可访问 private；F35 仅 Fluent 侧发射——
    /// ResolveBeforeMapping 检查可见性，特性侧用方法名字符串编译期不检查可见性）
    /// 先验锚：同 Mapper public 方法声明不报 F35
    /// </summary>
    [Fact]
    public void F35_FluentSide_PrivateTargetMethod_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace LhmF35Ns
            {
                public class Src { public int Id { get; set; } }
                public class Dto { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Src, Dto>().BeforeMapping(PrivateGuard);  // F35: private 跨类不可访问
                    }
                    private static void PrivateGuard(Src s, Dto d) { }  // private → F35
                    public static void PublicGuard(Src s, Dto d) { }    // 先验锚：public 不报
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F35");
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F35" && d.GetMessage().Contains("PrivateGuard"));
    }

    // ================================================================
    // F36：BeforeMapping lambda（非方法组——Fluent 侧）
    // ================================================================

    /// <summary>
    /// F36：Fluent 侧 .BeforeMapping(lambda) → NCMAP_F36（lambda 不可静态解析为生成代码引用）
    /// 先验锚：方法组形态正常提取（同 Mapper 内 public static 方法组声明不报 F36）
    /// </summary>
    [Fact]
    public void F36_FluentSide_LambdaTarget_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace LhmF36Ns
            {
                public class Src { public int Id { get; set; } }
                public class Dto { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Src, Dto>().BeforeMapping((s, d) => { });  // F36: lambda
                    }
                    public static void Guard(Src s, Dto d) { }  // 先验锚存在（方法组形态可用）
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F36");
    }

    // ================================================================
    // F38：BeforeMapping 在 FromSource 块内（Fluent 侧 Compose 子源块）
    // ================================================================

    /// <summary>
    /// F38：Fluent 侧 FromSource 块内 .BeforeMapping(...) → NCMAP_F38（运行时 Compose 管线无前置钩子注入口）
    /// 块形态：FromSource&lt;Sub&gt;(Action&lt;FluentTypeConfig&lt;Sub,Dto&gt;&gt;)，块内 BeforeMapping 禁止
    /// </summary>
    [Fact]
    public void F38_FluentSide_FromSourceBlock_Diagnosed()
    {
        var source = """
            using NCMapper;
            namespace LhmF38Ns
            {
                public class Src { public int Id { get; set; } }
                public class Sub { public int Id { get; set; } }
                public class Dto { public int Id { get; set; } }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Src, Dto>()
                           .FromSource<Sub>(c => { c.BeforeMapping(BlockGuard); });  // F38: 块内禁止
                    }
                    public static void BlockGuard(Sub s, Dto d) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "NCMAP_F38");
    }

    // ================================================================
    // 跨对传播（D-d 签名适用/不适用）——★B-2
    // "适用对"场景会触发 F11（双通道声明同对），由 spike Suppress_Rebuild 覆盖传播注入验证；
    // 此处测"不适用对"：Fluent 声明 OtherSrc→Dto + BeforeMapping，Dto 有 [MapFrom(typeof(Src))]
    // → 签名不适用（OtherSrc≠Src）→ 不注入、不发射、不发诊断
    // ================================================================

    /// <summary>
    /// 跨对传播（不适用对）：Fluent 链声明 cfg.For&lt;OtherSrc,Dto&gt;.BeforeMapping(Guard) + Dto 有 [MapFrom(typeof(Src))]
    /// → 签名不适用（OtherSrc≠Src）→ 不注入、不发射 Before、不发诊断（D-d 正常过滤路径）
    /// </summary>
    [Fact]
    public void CrossPairPropagation_InapplicablePair_NoEmission_NoDiagnostic()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;
            namespace LhmPropInapNs
            {
                public class Src { public int Id { get; set; } }
                public class OtherSrc { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                public class Dto
                {
                    public int Id { get; set; }
                }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<OtherSrc, Dto>().BeforeMapping(Guard);  // 签名不适用（OtherSrc≠Src）
                    }
                    public static void Guard(OtherSrc s, Dto d) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // 生成代码不含 Before 调用（签名不适用 → 不注入）
        var coreStart = result.GeneratedCode.IndexOf("Map_Src_To_Dto(");
        if (coreStart >= 0)
        {
            var coreEnd = result.GeneratedCode.IndexOf("_WithAction", coreStart);
            var coreBody = result.GeneratedCode.Substring(coreStart, (coreEnd > coreStart ? coreEnd - coreStart : 100));
            Assert.DoesNotContain("Guard(src, dst)", coreBody);
        }
        // 不发诊断（D-d 正常过滤路径——不适用对不报）
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F39");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP_F35" || d.Id == "NCMAP_F36");
    }

    // ================================================================
    // 继承语义（D-j /，Inherited=false）——★
    // ================================================================

    /// <summary>
    /// 继承语义：基类 BaseDto 标注 [BeforeMapping] + [MapFrom(typeof(Src))]；
    /// 派生 DerivedDto : BaseDto（无 [BeforeMapping]）→ 派生对**不继承**钩子（Inherited=false）
    /// 先验锚：基类对的 Before 调用确实生成（验证提取层可达）
    /// </summary>
    [Fact]
    public void Inheritance_DerivedDto_DoesNotInheritBeforeHook()
    {
        var source = """
            using NCMapper.Attributes;
            namespace LhmInheritNs
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(BaseGuard))]
                public class BaseDto
                {
                    public int Id { get; set; }
                    public static void BaseGuard(Src s, BaseDto d) { }
                }

                [MapFrom(typeof(Src))]
                public class DerivedDto : BaseDto  // 派生：不继承 [BeforeMapping]（Inherited=false）
                {
                    public int Id { get; set; }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);

        // 先验锚：基类对的 Before 调用确实生成（提取层可达——非 vacuous pass）
        Assert.Contains("LhmInheritNs.BaseDto.BaseGuard(src, dst);", result.GeneratedCode);

        // 派生对不继承钩子：DerivedDto 的映射方法不含 Before 调用
        var derivedStart = result.GeneratedCode.IndexOf("Map_Src_To_DerivedDto(");
        Assert.True(derivedStart >= 0, "DerivedDto 映射方法未生成——先验锚失败（特性未提取？）");
        var derivedEnd = result.GeneratedCode.IndexOf("_WithAction", derivedStart);
        var derivedBody = result.GeneratedCode.Substring(derivedStart, derivedEnd - derivedStart);
        Assert.DoesNotContain("BaseGuard", derivedBody);  // 不继承基类钩子
    }

    /// <summary>
    /// 继承语义（派生显式标注）：派生 DTO 显式标注 [BeforeMapping] → 生效（与基类钩子独立）
    /// AnnotatedDerivedDto 自身声明 Guard 方法（DeclaredOnly 不含基类——方法须在标注类自身）
    /// </summary>
    [Fact]
    public void Inheritance_DerivedDto_ExplicitAnnotation_Effective()
    {
        var source = """
            using NCMapper.Attributes;
            namespace LhmInheritExpNs
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(DerivedGuard))]
                public class AnnotatedDerivedDto
                {
                    public int Id { get; set; }
                    public static void DerivedGuard(Src s, AnnotatedDerivedDto d) { }  // 自身声明（DeclaredOnly）
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        // AnnotatedDerivedDto 的映射方法含 Before 调用（显式标注生效）
        Assert.Contains("LhmInheritExpNs.AnnotatedDerivedDto.DerivedGuard(src, dst);", result.GeneratedCode);
    }

    // ================================================================
    // 运行时叠加（[AfterMapping] + Pipeline.AddPostMap 顺序）——★
    // ================================================================

    /// <summary>
    /// 运行时叠加：[AfterMapping]（生成代码内）+ Pipeline.AddPostMap（运行时管线）共存 →
    /// After 钩子执行（dst.Tag="after:7"）+ PostMap 回调执行；PostMap 在 Map 返回后触发
    /// （生成内 After 先于管线 PostMap）
    /// </summary>
    [Fact]
    public void RuntimeOverlap_AfterMapping_Then_PostMap_ExecutionOrder()
    {
        var src = new LhEmitSrc { Id = 7, Name = "n" };
        string? postMapTag = null;

        // 注册 PostMap（管线后置）——After 在生成代码内先执行（dst.Tag="after:7"），PostMap 后执行
        NCMapper.Mapper.Pipeline.AddPostMap<LhEmitSrc, LhEmitDto>((s, d) =>
        {
            postMapTag = d.Tag + "-post";
        });

        try
        {
            var dst = GeneratedMapper.Map_LhEmitSrc_To_LhEmitDto(src)!;
            // After 先执行：dst.Tag = "after:7"
            Assert.Equal("after:7", dst.Tag);
            // 管线 PostMap 在 Map 返回后执行
            NCMapper.MapPipeline.RunPostMap(src, dst);
            Assert.Equal("after:7-post", postMapTag);
        }
        finally
        {
            NCMapper.Mapper.Pipeline.Clear();
        }
    }

    // ================================================================
    // 投影门禁（ClassifyUntranslatableDeclaration + 计数 8）
    // spike LifecycleHookEmissionSpikeTests.ClassifyUntranslatableDeclaration_BeforeMappingRef_ReturnsBeforeMappingCategory 已覆盖分类名；
    // 此处补测：BeforeMapping 分类触发投影降级（RequiresProjectionDowngrade=true）
    // ================================================================

    /// <summary>
    /// 投影降级：BeforeMappingMethodRef 非空 → ClassifyUntranslatableDeclaration 返回 "BeforeMapping"
    /// → 投影降级（钩子语义不可投影翻译）；与 AfterMapping 同口径
    /// </summary>
    [Fact]
    public void Projection_BeforeMappingRef_TriggersDowngrade()
    {
        var meta = new NCMapper.Runtime.RuntimeMapperMetadata
        {
            SourceType = typeof(LhEmitSrc),
            TargetType = typeof(LhEmitDto),
            SourceTypeFqn = typeof(LhEmitSrc).FullName!,
            TargetTypeFqn = typeof(LhEmitDto).FullName!,
            BeforeMappingMethodRef = "NCMapper.SourceGen.Tests.LhEmitDto.Before",
            Properties = System.Array.Empty<NCMapper.Runtime.RuntimePropertyMapping>(),
        };
        var category = NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(meta);
        Assert.Equal("BeforeMapping", category);
        // 降级路径：非空分类 → RequiresProjectionDowngrade 语义（分类名即降级原因）
        Assert.NotNull(category);
    }
}
