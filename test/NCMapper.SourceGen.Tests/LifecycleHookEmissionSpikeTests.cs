using System.Linq;
using NCMapper.Generated;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// spike：BeforeMapping 发射层一次性验证（验收 #2/#3/#4 +
/// Q6 降级形态；正式矩阵归）。文本层（TestHelper.GenerateMapper）+ 运行时层（自生成代码直调）。
/// <list type="bullet">
/// <item>验收 #2：Core 分支③（分支① Q6/N-1 降级形态）与 List 内联两分支——Before 调用于首条赋值前</item>
/// <item>验收 #3：_Update 形态 Before 见 dst 旧值（运行时直调）</item>
/// <item>验收 #4：F37——record/构造器一体化目标双侧诊断（分支①含 init-only + 构造路径两形态）</item>
/// </list>
/// </summary>
public class LifecycleHookEmissionSpikeTests
{
    /// <summary>文本层基座：无 init-only + 声明钩子（特性通道）→ Core 降级分支③"new + 逐语句赋值"</summary>
    private const string PlainPairSource = """
        using NCMapper.Attributes;
        namespace LhePlainNs
        {
            public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }

            [MapFrom(typeof(Src))]
            [BeforeMapping(nameof(Before))]
            [AfterMapping(nameof(After))]
            public class Dto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public static void Before(Src s, Dto d) { }
                public static void After(Src s, Dto d) { }
            }
        }
        """;

    // ================================================================
    // 验收 #2：Core 降级分支③ + List 内联——Before 于首条赋值前
    // ================================================================

    /// <summary>
    /// Core 方法：分支①（初始化器形态）声明钩子 → Q6/N-1 降级"new + 逐语句赋值"——
    /// Before 行位于构造后、首条 dst 赋值前；After 行位于赋值后（F4 回归）
    /// </summary>
    [Fact]
    public void Core_Q6Degrade_BeforeEmitted_BeforeFirstAssignment()
    {
        var result = TestHelper.GenerateMapper(PlainPairSource);
        var code = result.GeneratedCode;

        // 先验锚：Core 方法确实生成
        Assert.Contains("Map_Src_To_Dto(LhePlainNs.Src src)", code);

        // 降级形态锚：分支③形态（new + 逐语句赋值）替代对象初始化器
        Assert.Contains("var dst = new LhePlainNs.Dto();", code);

        // 位置断言（Core 方法内）：Before < 首条赋值 < After
        var coreStart = code.IndexOf("Map_Src_To_Dto(LhePlainNs.Src src)");
        var beforeIdx = code.IndexOf("LhePlainNs.Dto.Before(src, dst);", coreStart);
        var assignIdx = code.IndexOf("dst.Id = src.Id;", coreStart);
        var afterIdx = code.IndexOf("LhePlainNs.Dto.After(src, dst);", coreStart);
        Assert.True(beforeIdx >= 0, "Before 调用行未发射");
        Assert.True(assignIdx > beforeIdx, "Before 未位于首条赋值前（发射位置错误 D-i）");
        Assert.True(afterIdx > assignIdx, "After 应位于赋值之后（F4 既有行为回归）");
    }

    /// <summary>
    /// List 内联（分支① 降级 → 无参构造形态）：MapList 方法体内联元素——Before 行位于首条赋值前
    /// </summary>
    [Fact]
    public void MapList_Inline_Q6Degrade_BeforeBeforeAssignment()
    {
        var result = TestHelper.GenerateMapper(PlainPairSource);
        var code = result.GeneratedCode;

        var listStart = code.IndexOf("MapList_Src_To_Dto(");
        Assert.True(listStart >= 0, "MapList 方法未生成");
        var beforeIdx = code.IndexOf("LhePlainNs.Dto.Before(src, dst);", listStart);
        var assignIdx = code.IndexOf("dst.Id = src.Id;", listStart);
        Assert.True(beforeIdx >= 0, "List 内联 Before 调用行未发射");
        Assert.True(assignIdx > beforeIdx, "List 内联 Before 未位于首条赋值前");
    }

    /// <summary>
    /// Core 分支②（含 init-only + 非简单赋值）：构造器 + 初始化器后、非 init-only 赋值前发射 Before
    /// </summary>
    [Fact]
    public void Core_InitOnlyBranch_BeforeEmitted_AfterConstruction()
    {
        var source = """
            using NCMapper.Attributes;
            using System;
            namespace LheInitNs
            {
                public class Src { public int A { get; set; } public DateTime B { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(Before))]
                public class Dto
                {
                    public int A { get; init; }
                    public string B { get; set; } = "";
                    public static void Before(Src s, Dto d) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var code = result.GeneratedCode;

        var coreStart = code.IndexOf("Map_Src_To_Dto(LheInitNs.Src src)");
        Assert.True(coreStart >= 0, "Core 方法未生成");
        // 分支②锚：init-only 经对象初始化器（含 A）构造
        var ctorIdx = code.IndexOf("var dst = new LheInitNs.Dto", coreStart);
        var beforeIdx = code.IndexOf("LheInitNs.Dto.Before(src, dst);", coreStart);
        var assignIdx = code.IndexOf("dst.B = ", coreStart);
        Assert.True(ctorIdx >= 0, "分支②构造行未发射");
        Assert.True(beforeIdx > ctorIdx, "Before 应位于构造之后");
        Assert.True(assignIdx > beforeIdx, "Before 应位于非 init-only 属性赋值之前");
    }

    /// <summary>
    /// MapListInto（两路径）与 _Update 文本锚：Before 行位于赋值段前（_Update 运行时语义见下）
    /// </summary>
    [Fact]
    public void Update_And_ListInto_BeforeLine_BeforeAssignments()
    {
        var result = TestHelper.GenerateMapper(PlainPairSource);
        var code = result.GeneratedCode;

        // _Update：dst 为参数——Before 直接收（无构造行），位于首条赋值前
        var updStart = code.IndexOf("Map_Src_To_Dto_Update(");
        var updBefore = code.IndexOf("LhePlainNs.Dto.Before(src, dst);", updStart);
        var updAssign = code.IndexOf("dst.Id = src.Id;", updStart);
        Assert.True(updStart >= 0, "_Update 方法未生成");
        Assert.True(updBefore >= 0 && updAssign > updBefore, "_Update Before 应位于首条赋值前");

        // MapListInto：元素级 Before（数组快速路径/通用索引路径共用发射锚）
        var intoStart = code.IndexOf("MapListInto_Src_To_Dto(");
        var intoBefore = code.IndexOf("LhePlainNs.Dto.Before(src, dst);", intoStart);
        Assert.True(intoStart >= 0, "MapListInto 方法未生成");
        Assert.True(intoBefore >= 0, "MapListInto Before 调用行未发射");
    }

    /// <summary>
    /// 验收 #2（否定面）+ G3：无钩子声明的对——Core 保持对象初始化器原形态（不降级）且零 Before 发射
    /// </summary>
    [Fact]
    public void NoHookPair_InitializerFormPreserved_NoBeforeEmission()
    {
        var source = """
            using NCMapper.Attributes;
            namespace LheNoHookNs
            {
                public class Src { public int Id { get; set; } }
                [MapFrom(typeof(Src))]
                public class Dto { public int Id { get; set; } }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        var code = result.GeneratedCode;

        // 先验锚：Core 方法确实生成（窗口 = Core 方法体——其后紧跟 _WithAction）
        var coreStart = code.IndexOf("Map_Src_To_Dto(LheNoHookNs.Src src)");
        Assert.True(coreStart >= 0, "Core 方法未生成");
        var coreEnd = code.IndexOf("_WithAction", coreStart);
        var coreBody = code.Substring(coreStart, coreEnd - coreStart);

        // 分支①原形态保留（N-1：未声明钩子的对维持初始化器——非分支③语句形态）
        Assert.Contains("var dst = new LheNoHookNs.Dto", coreBody);
        Assert.DoesNotContain("new LheNoHookNs.Dto();", coreBody);
        // 零 Before 发射（G3——ref 恒 null 短路，全方法族）
        Assert.DoesNotContain("Before(src, dst)", code);
    }

    // ================================================================
    // 验收 #4：F37——构造器一体化目标双侧诊断
    // ================================================================

    /// <summary>
    /// F37 形态一：分支①含 init-only（构造与赋值不可分且不可降级）→ NCMAP_F37，Core 原发射保留；
    /// _Update/MapListInto 的 Before 发射不属 F37 约束面（dst 既有、插入点合法——Q5 结论）
    /// </summary>
    [Fact]
    public void F37_InitOnlyInitializerForm_Diagnosed()
    {
        var source = """
            using NCMapper.Attributes;
            namespace LheF37aNs
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(Before))]
                public class Dto
                {
                    public int Id { get; init; }
                    public static void Before(Src s, Dto d) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("NCMAP_F37", result.Diagnostics.Select(d => d.Descriptor.Id));

        // Core 窗口（其后紧跟 _WithAction）：原发射形态保留（初始化器）且钩子不发射
        var code = result.GeneratedCode;
        var coreStart = code.IndexOf("Map_Src_To_Dto(LheF37aNs.Src src)");
        Assert.True(coreStart >= 0, "Core 方法未生成");
        var coreEnd = code.IndexOf("_WithAction", coreStart);
        var coreBody = code.Substring(coreStart, coreEnd - coreStart);
        Assert.Contains("var dst = new LheF37aNs.Dto", coreBody);
        Assert.DoesNotContain("new LheF37aNs.Dto();", coreBody);
        Assert.DoesNotContain("Before(src, dst)", coreBody);
    }

    /// <summary>
    /// F37 形态二：record 位置构造器（!hasParameterlessCtor——实参即首组赋值）→ NCMAP_F37
    /// </summary>
    [Fact]
    public void F37_PositionalRecordTarget_Diagnosed()
    {
        var source = """
            using NCMapper.Attributes;
            namespace LheF37bNs
            {
                public class Src { public int Id { get; set; } }

                [MapFrom(typeof(Src))]
                [BeforeMapping(nameof(Before))]
                public record PosDto(int Id)
                {
                    public static void Before(Src s, PosDto d) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("NCMAP_F37", result.Diagnostics.Select(d => d.Descriptor.Id));
    }

    /// <summary>
    /// F37 双侧（Fluent 通道共用码）：.BeforeMapping(方法组) + record 目标 → NCMAP_F37
    /// </summary>
    [Fact]
    public void F37_FluentChannel_RecordTarget_SameDiagnostic()
    {
        var source = """
            using NCMapper;
            namespace LheF37cNs
            {
                public class Src { public int Id { get; set; } }
                public record PosDto(int Id) { }

                [MapperConfig]
                public class Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<Src, PosDto>().BeforeMapping(Guard);
                    }
                    public static void Guard(Src src, PosDto dst) { }
                }
            }
            """;
        var result = TestHelper.GenerateMapper(source);
        Assert.Contains("NCMAP_F37", result.Diagnostics.Select(d => d.Descriptor.Id));
    }

    /// <summary>
    /// F37 邻界反例：普通类目标（无参构造可降级）声明钩子 → 构造与赋值可分，不报 NCMAP_F37
    /// </summary>
    [Fact]
    public void PlainTarget_WithHook_NoF37()
    {
        var result = TestHelper.GenerateMapper(PlainPairSource);

        Assert.DoesNotContain(result.Diagnostics, d => d.Descriptor.Id == "NCMAP_F37");
    }

    // ================================================================
    // 验收 #1：投影降级分类名
    // ================================================================

    /// <summary>
    /// BeforeMappingMethodRef 非空 → ClassifyUntranslatableDeclaration 返回 "BeforeMapping"
    /// （前置钩子不可投影翻译，强制降级；null 时不受影响）
    /// </summary>
    [Fact]
    public void ClassifyUntranslatableDeclaration_BeforeMappingRef_ReturnsBeforeMappingCategory()
    {
        var meta = new NCMapper.Runtime.RuntimeMapperMetadata
        {
            SourceType = typeof(LhEmitSrc),
            TargetType = typeof(LhEmitDto),
            SourceTypeFqn = typeof(LhEmitSrc).FullName!,
            TargetTypeFqn = typeof(LhEmitDto).FullName!,
            Properties = Array.Empty<NCMapper.Runtime.RuntimePropertyMapping>(),
        };

        // 先验锚：无钩子声明 → 纯可翻译（null）
        Assert.Null(NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(meta));

        // Before 声明 → "BeforeMapping" 分类
        meta = new NCMapper.Runtime.RuntimeMapperMetadata
        {
            SourceType = typeof(LhEmitSrc),
            TargetType = typeof(LhEmitDto),
            SourceTypeFqn = typeof(LhEmitSrc).FullName!,
            TargetTypeFqn = typeof(LhEmitDto).FullName!,
            BeforeMappingMethodRef = "NCMapper.SourceGen.Tests.LhEmitDto.Before",
            Properties = Array.Empty<NCMapper.Runtime.RuntimePropertyMapping>(),
        };
        Assert.Equal("BeforeMapping", NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(meta));

        // 判定序锚：AfterMapping 先于 BeforeMapping（同类并存报 After——既有语义不变）
        meta = new NCMapper.Runtime.RuntimeMapperMetadata
        {
            SourceType = typeof(LhEmitSrc),
            TargetType = typeof(LhEmitDto),
            SourceTypeFqn = typeof(LhEmitSrc).FullName!,
            TargetTypeFqn = typeof(LhEmitDto).FullName!,
            BeforeMappingMethodRef = "Ns.X.Before",
            AfterMappingMethodRef = "Ns.X.After",
            Properties = Array.Empty<NCMapper.Runtime.RuntimePropertyMapping>(),
        };
        Assert.Equal("AfterMapping", NCMapper.Internal.DegradationDetectorBase.ClassifyUntranslatableDeclaration(meta));
    }

    // ================================================================
    // 验收 #3：_Update 形态 Before 见 dst 旧值（运行时层——自生成代码直调）
    // ================================================================

    /// <summary>
    /// _Update：Before 钩子观测到的 dst.Id 为赋值前旧值（99），赋值后变 src.Id（7）——
    /// 任务书验收 #3 语义锚（dst 旧值可见）
    /// </summary>
    [Fact]
    public void Update_BeforeHook_SeesOldDstValues()
    {
        var src = new LhEmitSrc { Id = 7, Name = "n" };
        var dst = new LhEmitDto { Id = 99, Name = "old" };
        LhEmitDto.BeforeSawId = null;

        GeneratedMapper.Map_LhEmitSrc_To_LhEmitDto_Update(src, dst, null);

        // Before 于赋值前触发：观测值为旧值 99（非 src.Id=7）
        Assert.Equal(99, LhEmitDto.BeforeSawId);
        // 赋值在 Before 之后执行
        Assert.Equal(7, dst.Id);
        Assert.Equal("n", dst.Name);
    }

    /// <summary>
    /// Core 运行时全链：Before 于首条赋值前触发（新实例观测到默认值）+ After 行为不变（F4）
    /// </summary>
    [Fact]
    public void Core_Runtime_BeforeBeforeAssignments_AfterStillRuns()
    {
        var src = new LhEmitSrc { Id = 7, Name = "n" };
        LhEmitDto.BeforeSawId = null;

        var dst = GeneratedMapper.Map_LhEmitSrc_To_LhEmitDto(src)!;

        // Before 于赋值前触发：新实例默认值 0（若发射在赋值后将观测到 7）
        Assert.Equal(0, LhEmitDto.BeforeSawId);
        Assert.Equal(7, dst.Id);
        Assert.Equal("n", dst.Name);
        // After 既有行为（F4）
        Assert.Equal("after:7", dst.Tag);
    }
}
