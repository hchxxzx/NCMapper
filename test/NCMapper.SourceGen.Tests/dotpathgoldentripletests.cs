using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 点路径 Golden Triple 正式套件 — 源生成引擎列 + Compose(SourceGen) 引擎列
/// <para>矩阵定位：7 场景 × 4 引擎（源生成 / 运行时 / 投影 / Compose-SourceGen）防漂移锚。
/// 本类 = 源生成列 + Compose-SourceGen 列（生成端形态断言；"值相等"由运行时/投影列以真实执行实证）；
/// 运行时列见 NCMapper.Tests.DotPathGoldenTripleTests；投影列（真实 provider，禁止对投影表达式直接 Compile 断言）
/// 见 NCMapper.IntegrationTests.DotPathGoldenTripleTests。</para>
/// <para>场景覆盖：S1 合法两段/三段路径生成 null 安全链（src.A?.B）；S2 缺失段 NCMAP001/NCMAP005 带失败段名 + 所在类型；
/// S3 中间节点值类型/string NCMAP001/NCMAP005；S4 叶节点命中全局忽略（IgnoreNames 注入）→ 映射移除 + NCMAP021；
/// S5 中间节点 null 由 S1 null 安全链生成形态锁定（C7）；S6 点路径 × [Mask]/[Convert] 消费生效
/// （H6：生成脱敏/转换调用，不再输出 NCMAP030；Compose 列标注：Compose 生成端本就不消费 Mask，锁定不输出 NCMAP030）；
/// S7 仅运行时（见运行时列）。</para>
/// <para>T2 诊断用例（NCMAP007/NCMAP009 双向/NCMAP013 反向措辞）见 DotPathDiagnosticsTests。</para>
/// </summary>
public class DotPathGoldenTripleTests
{
    // ==================== S1：合法两段/三段路径（生成形态 = 值正确性编译期保障） ====================

    /// <summary>
    /// 场景 1（两段）：Address.City → 生成 src.Address?.City（null 安全链）
    /// </summary>
    [Fact]
    public void S1_TwoSegmentPath_GeneratesNullSafeChain()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity { public GtSgAddress Address { get; set; } }
            public class GtSgAddress { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntity))]
            public class GtSgDtoTwo
            {
                [MapProperty(typeof(GtSgEntity), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_GtSgEntity_To_GtSgDtoTwo", "City = src.Address?.City");
    }

    /// <summary>
    /// 场景 1（三段）：Address.City.Name → 生成多级 null 安全链（S5 中间节点 null 的编译期保障，C7）
    /// </summary>
    [Fact]
    public void S1_ThreeSegmentPath_GeneratesNullSafeChain()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity3 { public GtSgAddress3 Address { get; set; } }
            public class GtSgAddress3 { public GtSgCity3 City { get; set; } }
            public class GtSgCity3 { public string Name { get; set; } }
            [MapFrom(typeof(GtSgEntity3))]
            public class GtSgDtoThree
            {
                [MapProperty(typeof(GtSgEntity3), "Address.City.Name")]
                public string CityName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_GtSgEntity3_To_GtSgDtoThree", "src.Address?.City?.Name");
    }

    // ==================== S2：缺失段 ====================

    /// <summary>
    /// 场景 2（源生成列）：Address.Missing → NCMAP001 带失败段名 + 失败段所在类型
    /// </summary>
    [Fact]
    public void S2_MissingSegment_NCMAP001_WithSegmentNameAndType()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity2 { public GtSgAddress2 Address { get; set; } }
            public class GtSgAddress2 { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntity2))]
            public class GtSgDtoMissing
            {
                [MapProperty(typeof(GtSgEntity2), "Address.Missing")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP001"), "应输出 NCMAP001（拆段失败）");
        var message = result.DiagnosticsByDescriptorId["NCMAP001"][0].GetMessage();
        Assert.Contains("Missing", message);  // 失败段名
        Assert.Contains("GtSgAddress2", message); // 失败段所在类型
    }

    /// <summary>
    /// 场景 2（Compose-SourceGen 列）：跨表点路径缺失段 → NCMAP005 带失败段名 + 所在类型
    /// </summary>
    [Fact]
    public void S2_ComposeMissingSegment_NCMAP005_WithSegmentName()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgOrd2 { public int Id { get; set; } }
            public class GtSgCst2 { public GtSgAdr2 Address { get; set; } }
            public class GtSgAdr2 { public string City { get; set; } }
            [MapFrom(typeof(GtSgOrd2))]
            public class GtSgOrdDtoMissing
            {
                [MapProperty(typeof(GtSgCst2), "Address.Missing")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP005"), "应输出 NCMAP005（Compose 跨表拆段失败）");
        var message = result.DiagnosticsByDescriptorId["NCMAP005"][0].GetMessage();
        Assert.Contains("Missing", message); // 失败段名
        Assert.Contains("GtSgAdr2", message); // 失败段所在类型
    }

    // ==================== S3：中间节点值类型/string 中断 ====================

    /// <summary>
    /// 场景 3（源生成列，值类型）：Address.Zip.X → NCMAP001 带中断段名
    /// </summary>
    [Fact]
    public void S3_ValueTypeIntermediate_NCMAP001_WithSegmentName()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity4 { public GtSgAddress4 Address { get; set; } }
            public class GtSgAddress4 { public int Zip { get; set; } }
            [MapFrom(typeof(GtSgEntity4))]
            public class GtSgDtoValueType
            {
                [MapProperty(typeof(GtSgEntity4), "Address.Zip.X")]
                public int X { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP001"), "应输出 NCMAP001（中间节点值类型中断）");
        Assert.Contains("Zip", result.DiagnosticsByDescriptorId["NCMAP001"][0].GetMessage());
    }

    /// <summary>
    /// 场景 3（源生成列，string）：Address.Label.Sub（string 不可导航）→ NCMAP001 带中断段名
    /// </summary>
    [Fact]
    public void S3_StringIntermediate_NCMAP001_WithSegmentName()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity5 { public GtSgAddress5 Address { get; set; } }
            public class GtSgAddress5 { public string Label { get; set; } }
            [MapFrom(typeof(GtSgEntity5))]
            public class GtSgDtoString
            {
                [MapProperty(typeof(GtSgEntity5), "Address.Label.Sub")]
                public string Sub { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP001"), "应输出 NCMAP001（string 中间节点中断）");
        Assert.Contains("Label", result.DiagnosticsByDescriptorId["NCMAP001"][0].GetMessage());
    }

    /// <summary>
    /// 场景 3（Compose-SourceGen 列）：跨表点路径值类型中断 → NCMAP005 带中断段名
    /// </summary>
    [Fact]
    public void S3_ComposeValueTypeIntermediate_NCMAP005_WithSegmentName()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgOrd3 { public int Id { get; set; } }
            public class GtSgCst3 { public GtSgAdr3 Address { get; set; } }
            public class GtSgAdr3 { public int Zip { get; set; } }
            [MapFrom(typeof(GtSgOrd3))]
            public class GtSgOrdDtoValueType
            {
                [MapProperty(typeof(GtSgCst3), "Address.Zip.X")]
                public int X { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP005"), "应输出 NCMAP005（Compose 拆段值类型中断）");
        Assert.Contains("Zip", result.DiagnosticsByDescriptorId["NCMAP005"][0].GetMessage());
    }

    // ==================== S4：叶节点命中全局忽略（源生成列；Compose 列不适用，标注） ====================

    /// <summary>
    /// 场景 4（源生成列）：IgnoreNames=["Name","Address"] → 叶节点 Name 命中 → 映射移除（生成代码无赋值）+ NCMAP021（显式标注 fail-closed）；
    /// 中间节点 Address 命中不判定（粒度=叶节点）→ Address.City 映射保留
    /// </summary>
    [Fact]
    public void S4_LeafGlobalIgnore_Skipped_IntermediateNotJudged()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity6 { public GtSgAddress6 Address { get; set; } }
            public class GtSgAddress6 { public string Name { get; set; } public string City { get; set; } }
            [MapFrom(typeof(GtSgEntity6))]
            public class GtSgDtoIgnore
            {
                [MapProperty(typeof(GtSgEntity6), "Address.Name")]
                public string Name { get; set; }
                [MapProperty(typeof(GtSgEntity6), "Address.City")]
                public string City { get; set; }
            }
            """;

        var (generated, diagnostics) = RunGeneratorWithDiagnostics(source, new Dictionary<string, string>
        {
            ["build_property.NCMapperIgnoreNames"] = "Name,Address",
        });

        // 叶节点命中 → 映射移除：生成代码不含该点路径赋值（仅属性声明处）
        Assert.DoesNotContain("src.Address?.Name", generated);
        // 中间节点 Address 命中不判定 → Address.City 保留
        Assert.Contains("src.Address?.City", generated);
        // 显式标注命中全局忽略 → NCMAP021（fail-closed）
        Assert.Contains(diagnostics, d => d.Id == "NCMAP021");
    }

    // ==================== S5：中间节点 null（S1 三段 null 安全链已锁定，C7） ====================
    // 说明：源生成列由 S1_ThreeSegmentPath_GeneratesNullSafeChain 的 src.Address?.City?.Name 形态覆盖；
    // 值语义（null → default 不抛）由运行时列 S5_* 与投影列 S5_*（真实 provider）实证。

    // ==================== S6：目标带 [Mask]/[Convert] — 消费生效（H6）====================

    /// <summary>
    /// 场景 6（源生成列）：点路径 × [Mask] → 生成脱敏调用（__mask_* .Mask(...)），不再输出 NCMAP030
    /// </summary>
    [Fact]
    public void S6_Mask_Consumed_NoNCMAP030_WithMaskedAssignment()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity7 { public GtSgAddress7 Address { get; set; } }
            public class GtSgAddress7 { public string City { get; set; } }
            public class GtSgPhoneMask : NCMapper.IMask
            {
                public string Mask(string? value) => value ?? "";
            }
            [MapFrom(typeof(GtSgEntity7))]
            public class GtSgDtoMask
            {
                [MapProperty(typeof(GtSgEntity7), "Address.City")]
                [Mask(typeof(GtSgPhoneMask))]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP030"), "点路径 + [Mask] 消费生效后不应输出 NCMAP030");
        result.Should().HaveMethodBodyContaining("Map_GtSgEntity7_To_GtSgDtoMask",
            "City = __mask_GtSgDtoMask_City.Mask(src.Address?.City)");
    }

    /// <summary>
    /// 场景 6（源生成列）：点路径 × [Convert] → 生成静态方法调用（global::FQN.M(字段访问)），不再输出 NCMAP030
    /// </summary>
    [Fact]
    public void S6_ConvertUsing_Consumed_NoNCMAP030_WithConverterCall()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity8 { public GtSgAddress8 Address { get; set; } }
            public class GtSgAddress8 { public string City { get; set; } }
            public static class GtSgConv
            {
                public static string Convert(string fieldValue) => "CONV:" + fieldValue;
            }
            [MapFrom(typeof(GtSgEntity8))]
            public class GtSgDtoConv
            {
                [MapProperty(typeof(GtSgEntity8), "Address.City")]
                [Convert(typeof(GtSgConv), nameof(GtSgConv.Convert))]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP030"), "点路径 + [Convert] 消费生效后不应输出 NCMAP030");
        result.Should().HaveMethodBodyContaining("Map_GtSgEntity8_To_GtSgDtoConv",
            "City = global::GtSgConv.Convert(src.Address?.City)");
    }

    /// <summary>
    /// 场景 6（源生成列，推导扁平化）：CityName（无显式 [MapProperty]）+ [Mask] → 推导扁平化路径消费脱敏
    /// </summary>
    [Fact]
    public void S6_FlattenMask_Consumed_WithMaskedAssignment()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntity9 { public GtSgAddress9 Address { get; set; } }
            public class GtSgAddress9 { public GtSgCity9 City { get; set; } }
            public class GtSgCity9 { public string Name { get; set; } }
            public class GtSgPhoneMask2 : NCMapper.IMask
            {
                public string Mask(string? value) => value ?? "";
            }
            [MapFrom(typeof(GtSgEntity9))]
            public class GtSgDtoFlattenMask
            {
                [Mask(typeof(GtSgPhoneMask2))]
                public string AddressCityName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_GtSgEntity9_To_GtSgDtoFlattenMask",
            "AddressCityName = __mask_GtSgDtoFlattenMask_AddressCityName.Mask(src.Address?.City?.Name)");
    }

    /// <summary>
    /// 场景 6（Compose-SourceGen 列标注）：Compose 生成端本就不消费 Mask → 不输出 NCMAP030
    /// <para>锁定"Compose 生成端不消费"事实（N4：SourceGen Compose 列分引擎标注）。</para>
    /// </summary>
    [Fact]
    public void S6_ComposeMask_NoNCMAP030_NotConsumed()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgOrd4 { public int Id { get; set; } }
            public class GtSgCst4 { public GtSgAdr4 Address { get; set; } }
            public class GtSgAdr4 { public string City { get; set; } }
            public class GtSgPhoneMask2 : NCMapper.IMask
            {
                public string Mask(string? value) => value ?? "";
            }
            [MapFrom(typeof(GtSgOrd4))]
            public class GtSgOrdDtoMask
            {
                [MapProperty(typeof(GtSgCst4), "Address.City")]
                [Mask(typeof(GtSgPhoneMask2))]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 生成端本就不消费 Mask：无 NCMAP030 诊断（普通列消费用例由 S6_Mask_Consumed_NoNCMAP030_WithMaskedAssignment 锁定）
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP030"), "Compose 生成端不消费 Mask，不应输出 NCMAP030");
    }

    // ==================== Compose-SourceGen 列 S1：合法两段路径（生成形态） ====================

    /// <summary>
    /// Compose 列场景 1：跨表点路径 Address.City → 更新方法生成 src.Address?.City（多段 null 安全访问，C7）
    /// </summary>
    [Fact]
    public void ComposeS1_TwoSegmentPath_GeneratesMultiSegmentAccess()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgOrd5 { public int Id { get; set; } }
            public class GtSgCst5 { public GtSgAdr5 Address { get; set; } }
            public class GtSgAdr5 { public string City { get; set; } }
            [MapFrom(typeof(GtSgOrd5))]
            public class GtSgOrdDtoCompose
            {
                [MapProperty(typeof(GtSgCst5), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // Compose 部分更新方法：多段访问 src.Address?.City（C7 null 安全）
        result.Should().HaveMethod("Update_GtSgCst5_To_GtSgOrdDtoCompose");
        result.Should().HaveMethodBodyContaining("Update_GtSgCst5_To_GtSgOrdDtoCompose", "src.Address?.City");
    }

    // ==================== Compose 列 S-T2：跨表字段 [Convert] 消费（B 组）====================

    /// <summary>
    /// B-1/B-3：跨表字段 [Convert]（T1 模板，TField=末段属性）→ Compose 更新方法消费静态方法
    /// <para>生成形态 `global::CcConv1.Convert(src.Raw)`（T1 单值模板，输入=末段访问），
    /// TSource 基准=子源类型（CcCst1）校验通过 → 无 NCMAP008/035/036/037</para>
    /// </summary>
    [Fact]
    public void ComposeConvertUsing_TSourceSubSource_Consumed_StaticCall()
    {
        var source = """
            using NCMapper.Attributes;
            public class CcOrd1 { public int Id { get; set; } }
            public class CcCst1 { public string Raw { get; set; } }
            public static class CcConv1
            {
                public static string Convert(string fieldValue) => "CONV:" + fieldValue;
            }
            [MapFrom(typeof(CcOrd1))]
            public class CcOrdDto1
            {
                [MapProperty(typeof(CcCst1), "Raw")]
                [Convert(typeof(CcConv1), nameof(CcConv1.Convert))]
                public string Raw { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // B-3：生成端消费静态方法（T1：输入=末段访问 src.Raw）
        result.Should().HaveMethod("Update_CcCst1_To_CcOrdDto1");
        result.Should().HaveMethodBodyContaining("Update_CcCst1_To_CcOrdDto1", "global::CcConv1.Convert(src.Raw)");
        // B-1：TSource=子源（CcCst1）校验通过 → 不报转换器诊断
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP008"),
            "TSource=子源类型应校验通过，不报 NCMAP008");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP035"),
            "T1 模板命中，不应报 NCMAP035");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            "T1 模板命中，不应报 NCMAP037");
    }

    /// <summary>
    /// B-1 反例：跨表字段 [Convert]（T3 模板 TSource=主源，非子源）→ NCMAP037（错配）
    /// <para>方法解析 TSource 基准=子源类型（CcCst2），Convert 首参=主源（CcOrd2）不匹配 → 四模板无命中 → NCMAP037
    /// （原 NCMAP008 接口形态 TSource 错配场景的语义迁移）</para>
    /// </summary>
    [Fact]
    public void ComposeConvertUsing_TSourceMainSource_Mismatches_NCMAP037()
    {
        var source = """
            using NCMapper.Attributes;
            public class CcOrd2 { public int Id { get; set; } }
            public class CcCst2 { public string Raw { get; set; } }
            public static class CcConv2
            {
                public static string Convert(CcOrd2 source, string fieldValue) => "CONV:" + fieldValue;
            }
            [MapFrom(typeof(CcOrd2))]
            public class CcOrdDto2
            {
                [MapProperty(typeof(CcCst2), "Raw")]
                [Convert(typeof(CcConv2), nameof(CcConv2.Convert))]
                public string Raw { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // B-1 反例：T3 首参=主源（CcOrd2）≠ 子源基准（CcCst2）→ NCMAP037
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP037"),
            "TSource=主源（非子源）应报 NCMAP037");
    }

    // ==================== 场景 7（仅运行时列） ====================
    // 说明：缓存键 (Type, PropertyInfo) 互不污染为运行时特性，见 NCMapper.Tests.DotPathGoldenTripleTests.S7_*。

    // ==================== 工具 ====================

    // ==================== S12：反向路径写回 ====================

    /// <summary>
    /// S12a 显式点路径反向写回基础：反向映射生成中间节点 null 创建 + 叶赋值（折叠形态）
    /// </summary>
    [Fact]
    public void S12a_ReverseWriteBack_ExplicitDotPath_GeneratesCreateChain()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityA { public GtSgAddressA Address { get; set; } }
            public class GtSgAddressA { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntityA))]
            public class GtSgDtoA
            {
                [MapProperty(typeof(GtSgEntityA), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 反向 Map_GtSgDtoA_To_GtSgEntityA：源值 null 守卫 + 中间节点创建 + 叶赋值
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoA_To_GtSgEntityA", "if (src.City != null)");
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoA_To_GtSgEntityA", "if (dst.Address == null) dst.Address = new ");
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoA_To_GtSgEntityA", "dst.Address.City = src.City;");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"), "点路径反向写回，不应输出 NCMAP013");
    }

    /// <summary>
    /// S12b 中间节点 null → 创建（三段链）：逐段 null 创建守卫生成（每段独立）
    /// </summary>
    [Fact]
    public void S12b_ReverseWriteBack_ThreeSegment_CreatesEachIntermediate()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityB { public GtSgAddressB Address { get; set; } }
            public class GtSgAddressB { public GtSgCityB City { get; set; } }
            public class GtSgCityB { public string Name { get; set; } }
            [MapFrom(typeof(GtSgEntityB))]
            public class GtSgDtoB
            {
                [MapProperty(typeof(GtSgEntityB), "Address.City.Name")]
                public string CityName { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_GtSgDtoB_To_GtSgEntityB", "if (dst.Address == null) dst.Address = new ");
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoB_To_GtSgEntityB", "if (dst.Address.City == null) dst.Address.City = new ");
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoB_To_GtSgEntityB", "dst.Address.City.Name = src.CityName;");
    }

    /// <summary>
    /// S12c 中间节点不可实例化（抽象类）：NCMAP031 Error 编译阻断，不生成非法代码
    /// </summary>
    [Fact]
    public void S12c_ReverseWriteBack_AbstractIntermediate_NCMAP031_Error()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityC { public GtSgAddressBaseC Address { get; set; } }
            public abstract class GtSgAddressBaseC { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntityC))]
            public class GtSgDtoC
            {
                [MapProperty(typeof(GtSgEntityC), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP031"), "应输出 NCMAP031（中间节点抽象类不可实例化）");
        var body = result.GetMethod("Map_GtSgDtoC_To_GtSgEntityC")?.Body ?? "";
        Assert.DoesNotContain("new GtSgAddressBaseC", body);
    }

    /// <summary>
    /// S12d 推导扁平化反向写回（与显式同进退，P1 同契约）：AddressCity → Address.City
    /// </summary>
    [Fact]
    public void S12d_ReverseWriteBack_DerivedFlatten_GeneratesCreateChain()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityD { public GtSgAddressD Address { get; set; } }
            public class GtSgAddressD { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntityD))]
            public class GtSgDtoD
            {
                public string AddressCity { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_GtSgDtoD_To_GtSgEntityD", "if (dst.Address == null) dst.Address = new ");
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoD_To_GtSgEntityD", "dst.Address.City = src.AddressCity;");
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"), "推导扁平化反向写回，不应输出 NCMAP013");
    }

    /// <summary>
    /// S12e [MapIgnore(ToEntity)] 拦截反向写回：DTO 属性方向忽略生效（源驱动循环显式接线，O-5）
    /// </summary>
    [Fact]
    public void S12e_ReverseWriteBack_MapIgnoreToEntity_BlocksWriteBack()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityE { public GtSgAddressE Address { get; set; } }
            public class GtSgAddressE { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntityE))]
            public class GtSgDtoE
            {
                [MapProperty(typeof(GtSgEntityE), "Address.City")]
                [MapIgnore(Direction.ToEntity)]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        var body = result.GetMethod("Map_GtSgDtoE_To_GtSgEntityE")?.Body ?? "";
        Assert.DoesNotContain("dst.Address.City", body);
    }

    /// <summary>
    /// S12f [Convert] 反向不消费 — A-1/A-2：
    /// 写回路径不携带/不消费转换器（生成端 NCMAP033 登记），按类型转换路径写入 + null=清空形态
    /// </summary>
    [Fact]
    public void S12f_ReverseWriteBack_ConvertUsing_NotConsumed()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityF { public GtSgAddressF Address { get; set; } }
            public class GtSgAddressF { public string City { get; set; } }
            public static class GtSgConvF
            {
                public static string Convert(string fieldValue) => "CONV:" + fieldValue;
            }
            [MapFrom(typeof(GtSgEntityF))]
            public class GtSgDtoF
            {
                [MapProperty(typeof(GtSgEntityF), "Address.City")]
                [Convert(typeof(GtSgConvF), nameof(GtSgConvF.Convert))]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // NCMAP033 登记（写回路径 [Convert] 不消费）
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP033"),
            "反向写回路径标 [Convert] 应报 NCMAP033");
        var body = result.GetMethod("Map_GtSgDtoF_To_GtSgEntityF")?.Body ?? "";
        // 反向方法不生成转换器调用 / 临时变量
        Assert.DoesNotContain(".Convert(", body);
        Assert.DoesNotContain("GtSgConvF.Convert", body);
        Assert.DoesNotContain("_wb_City", body);
        // 类型转换路径 + null=清空形态（源可空）：写分支 null 守卫 + 创建中间节点 + 清叶
        Assert.Contains("if (src.City != null)", body);
        Assert.Contains("if (dst.Address == null) dst.Address = new ", body);
        Assert.Contains("dst.Address.City = src.City;", body);
        Assert.Contains("else if (dst.Address != null)", body);
        Assert.Contains("dst.Address.City = null;", body);
    }

    /// <summary>
    /// S12f 迁移验证（Q3）：[Convert] 改标 [ConvertBack] 后
    /// 反向写回消费静态方法 + N-1 语义保持
    /// <para>S12f 原场景（[Convert] 写回）在起反向一律不消费；迁移方式 = 改标 [ConvertBack]。
    /// 本用例验证迁移后：① 不报 NCMAP033（互斥登记生效）；② 反向写回消费静态方法
    /// `global::GtSgConvM.Convert(src.City)`（T1 单值模板）；
    /// ③ N-1 保持：源 null 走清空分支（else if 清叶）不调用转换器（转换器调用仅存于 if 源非 null 写分支）。</para>
    /// </summary>
    [Fact]
    public void S12f_Migrated_ToConvertBackUsing_N1Preserved()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityM { public GtSgAddressM Address { get; set; } }
            public class GtSgAddressM { public string City { get; set; } }
            public static class GtSgConvM
            {
                public static string Convert(string fieldValue) => "CONV:" + fieldValue;
            }
            [MapFrom(typeof(GtSgEntityM))]
            public class GtSgDtoM
            {
                [MapProperty(typeof(GtSgEntityM), "Address.City")]
                [ConvertBack(typeof(GtSgConvM), nameof(GtSgConvM.Convert))]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // ① 互斥：标 [ConvertBack] 不报 NCMAP033
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP033"),
            "改标 [ConvertBack] 后不应报 NCMAP033");
        var body = result.GetMethod("Map_GtSgDtoM_To_GtSgEntityM")?.Body ?? "";
        // ② 写回消费静态方法（T1 单值模板，输入=DTO 叶属性）
        Assert.Contains("dst.Address.City = global::GtSgConvM.Convert(src.City)", body);
        // ③ N-1 保持：源 null 守卫 + 清空分支（转换器调用仅存在于非 null 写分支）
        Assert.Contains("if (src.City != null)", body);
        Assert.Contains("else if (dst.Address != null)", body);
        Assert.Contains("dst.Address.City = null;", body);
    }

    /// <summary>
    /// S12i 源值 null → 整体跳过（O-1）：生成 if (src.Prop != null) 守卫，不创建中间节点不写叶
    /// </summary>
    [Fact]
    public void S12i_ReverseWriteBack_NullSourceValue_SkippedEntirely()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityI { public GtSgAddressI Address { get; set; } }
            public class GtSgAddressI { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntityI))]
            public class GtSgDtoI
            {
                [MapProperty(typeof(GtSgEntityI), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 引用类型源值 → null 守卫包裹整个写回（创建 + 赋值都在守卫内）
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoI_To_GtSgEntityI", "if (src.City != null)");
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoI_To_GtSgEntityI", "if (dst.Address == null) dst.Address = new ");
    }

    /// <summary>
    /// S12l 中间段 init-only setter：跳过 + NCMAP013（构造后无法再赋新实例，改动点 2）
    /// </summary>
    [Fact]
    public void S12l_ReverseWriteBack_InitOnlyIntermediate_SkippedWithNCMAP013()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityL { public GtSgAddressL Address { get; init; } }
            public class GtSgAddressL { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntityL))]
            public class GtSgDtoL
            {
                [MapProperty(typeof(GtSgEntityL), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP013"), "中间段 init-only → 跳过并输出 NCMAP013");
        var message = result.DiagnosticsByDescriptorId["NCMAP013"][0].GetMessage();
        Assert.Contains("中间节点 Address", message);
        var body = result.GetMethod("Map_GtSgDtoL_To_GtSgEntityL")?.Body ?? "";
        Assert.DoesNotContain("dst.Address.City", body);
    }

    /// <summary>
    /// S12a-U 反向 Update 形态：已有 dst 实例的路径写回同样生成（编辑表单提交主场景）
    /// </summary>
    [Fact]
    public void S12aU_ReverseWriteBack_UpdateMethod_GeneratesCreateChain()
    {
        var source = """
            using NCMapper.Attributes;
            public class GtSgEntityU { public GtSgAddressU Address { get; set; } }
            public class GtSgAddressU { public string City { get; set; } }
            [MapFrom(typeof(GtSgEntityU))]
            public class GtSgDtoU
            {
                [MapProperty(typeof(GtSgEntityU), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_GtSgDtoU_To_GtSgEntityU_Update", "if (dst.Address == null) dst.Address = new ");
        result.Should().HaveMethodBodyContaining("Map_GtSgDtoU_To_GtSgEntityU_Update", "dst.Address.City = src.City;");
    }

    /// <summary>
    /// 运行生成器（msbuild 属性注入版）并返回（生成代码文本, 诊断列表）
    /// </summary>
    private static (string, Diagnostic[]) RunGeneratorWithDiagnostics(string source, Dictionary<string, string> msbuildProperties)
    {
        var compilation = CreateCompilation(source);
        var generator = new NCMapperSourceGenerator();
        var optionsProvider = new GlobalConfigReaderTestsConfigOptionsProvider(msbuildProperties);
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            optionsProvider: optionsProvider);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
        var generated = updatedCompilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("NCMapper_Generated_GeneratedMapper.g.cs"))?
            .ToString() ?? "";
        return (generated, diagnostics.ToArray());
    }

    /// <summary>
    /// 构建测试编译单元（引用 NCMapper 与 SourceGen）
    /// </summary>
    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();
        var ncmapper = typeof(NCMapper.Mapper).Assembly.Location;
        if (!references.Any(r => r.Display == ncmapper))
            references.Add(MetadataReference.CreateFromFile(ncmapper));

        return CSharpCompilation.Create(
            "DotPathGoldenTripleTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
