using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 互指对死字段清理断言测试（W4Q-1）
/// <para>样本自建带 [Convert]+[Mask] 标注（复审 P1-1/P1-2：不可复用 QuadrantSemanticsGenerationTests
/// 的无标注样本 X32G/X31G，否则断言恒真/必然失败）；断言区分力由 W4Q-0 基线快照保证
/// （互指样本修复前生成物含 __conv_*/__mask_* 死字段，见 artifacts/task-20260809-003/w4q0-baseline-mutual-annotated.txt）。</para>
/// </summary>
public class MutualDeadFieldGenerationTests
{
    /// <summary>
    /// 带 [Convert]+[Mask] 标注的互指对样本（双 true FromSource，方法引用形态）
    /// <para>A 标 [MapFrom(B)]（源=B，目标=A），B 标 [MapFrom(A)]（源=A，目标=B）→ 互指。
    /// 转换方法 MfgConvAB.Convert（T1 单值模板）：参数 int = B.Phone 类型，返回 string = A.Phone 类型。</para>
    /// </summary>
    private const string MutualAnnotatedSource = """
        using NCMapper;
        using NCMapper.Attributes;

        [MapFrom(typeof(MfgMutualB))]
        public class MfgMutualA
        {
            public int Id { get; set; }
            [Convert(typeof(MfgConvAB), nameof(MfgConvAB.Convert))]
            [Mask(typeof(MaskPhone))]
            public string Phone { get; set; } = "";
        }

        [MapFrom(typeof(MfgMutualA))]
        public class MfgMutualB
        {
            public int Id { get; set; }
            public int Phone { get; set; }
        }

        public static class MfgConvAB
        {
            public static string Convert(int fieldValue) => fieldValue.ToString();
        }
        """;

    /// <summary>
    /// 带同样标注的非互指样本（对照）——修复前后字段均应保持存在且被引用
    /// </summary>
    private const string NonMutualAnnotatedSource = """
        using NCMapper;
        using NCMapper.Attributes;

        [MapFrom(typeof(MfgEntity))]
        public class MfgDto
        {
            public int Id { get; set; }
            [Convert(typeof(MfgPhoneConverter), nameof(MfgPhoneConverter.Convert))]
            [Mask(typeof(MaskPhone))]
            public string Phone { get; set; } = "";
        }

        public class MfgEntity
        {
            public int Id { get; set; }
            public int Phone { get; set; }
        }

        public static class MfgPhoneConverter
        {
            public static string Convert(int fieldValue) => fieldValue.ToString();
        }
        """;

    /// <summary>
    /// 互指对（带转换器/脱敏器标注）不生成 __conv_*/__mask_* 死字段
    /// <para>修复前（W4Q-0 基线）：生成 __conv_MfgMutualA_Phone/__mask_MfgMutualA_Phone 且无任何方法体引用（死字段）；
    /// 修复后（本任务）：两方法对互指对跳过字段生成 → 断言不包含。
    /// （起转换器无单例字段，__conv_ 部分由形态天然满足；__mask_ 仍是区分力所在。）</para>
    /// </summary>
    [Fact]
    public void MutualPair_WithConvertAndMask_DoesNotEmitDeadFields()
    {
        var result = TestHelper.GenerateMapper(MutualAnnotatedSource);

        // 修复后：互指对不生成任何 converter/mask 静态字段
        Assert.DoesNotContain("__conv_", result.GeneratedCode);
        Assert.DoesNotContain("__mask_", result.GeneratedCode);

        // 反向语义确认：NCMAP025 互指诊断仍在（互指判定未受影响）
        var diagnostics = result.Diagnostics;
        Assert.Contains(diagnostics, d => d.Id == "NCMAP025");
    }

    /// <summary>
    /// 非互指对（带同样标注）__mask 字段保持存在且字段名不变；转换器以内联静态调用消费
    /// <para>修复前后均生成 __mask_MfgDto_Phone 且被方法体引用（非死字段，不可误删）；
    /// 转换器消费形态为内联静态调用 global::MfgPhoneConverter.Convert(src.Phone)
    /// （起不再生成 __conv_* 单例字段，字段名恒定性语义由"静态调用存在"承载）。</para>
    /// </summary>
    [Fact]
    public void NonMutualPair_WithConvertAndMask_KeepsFields()
    {
        var result = TestHelper.GenerateMapper(NonMutualAnnotatedSource);

        // __mask 字段声明保持存在
        Assert.Contains("private static readonly global::NCMapper.IMask __mask_MfgDto_Phone", result.GeneratedCode);

        // 转换器内联静态调用保持存在（非互指对的转换器消费不被互指对清理波及）
        Assert.Contains("global::MfgPhoneConverter.Convert(src.Phone)", result.GeneratedCode);

        // 赋值引用保持存在（__mask 字段非死字段，被方法体消费；先转换再脱敏）
        Assert.Contains("__mask_MfgDto_Phone.Mask(", result.GeneratedCode);
    }

    /// <summary>
    /// 互指对与非互指对共存场景：仅互指对字段被跳过，非互指对字段不受影响
    /// <para>防回归：一次编译单元内同时出现互指对与非互指对，跳过度量不能波及非互指对。</para>
    /// </summary>
    [Fact]
    public void MixedCompilation_MutualSkipped_NonMutualKept()
    {
        var source = """
            using NCMapper;
            using NCMapper.Attributes;

            [MapFrom(typeof(MfgMutualB))]
            public class MfgMutualA
            {
                public int Id { get; set; }
                [Convert(typeof(MfgConvAB), nameof(MfgConvAB.Convert))]
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
            }

            [MapFrom(typeof(MfgMutualA))]
            public class MfgMutualB
            {
                public int Id { get; set; }
                public int Phone { get; set; }
            }

            [MapFrom(typeof(MfgEntity))]
            public class MfgDto
            {
                public int Id { get; set; }
                [Convert(typeof(MfgPhoneConverter), nameof(MfgPhoneConverter.Convert))]
                [Mask(typeof(MaskPhone))]
                public string Phone { get; set; } = "";
            }

            public class MfgEntity
            {
                public int Id { get; set; }
                public int Phone { get; set; }
            }

            public static class MfgConvAB
            {
                public static string Convert(int fieldValue) => fieldValue.ToString();
            }

            public static class MfgPhoneConverter
            {
                public static string Convert(int fieldValue) => fieldValue.ToString();
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 互指对：无死字段（__conv_ 部分由形态天然满足）
        Assert.DoesNotContain("__conv_MfgMutualA_Phone", result.GeneratedCode);
        Assert.DoesNotContain("__mask_MfgMutualA_Phone", result.GeneratedCode);
        // 非互指对：__mask 字段保持 + 转换器内联静态调用保持
        Assert.Contains("__mask_MfgDto_Phone", result.GeneratedCode);
        Assert.Contains("global::MfgPhoneConverter.Convert(src.Phone)", result.GeneratedCode);
    }
}
