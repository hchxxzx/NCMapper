using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// B1 反向匹配生成断言测试（U1-U17 生成侧）
/// <para>双位置矩阵：U1-U8 规范位置（MapFrom 标 DTO）零回归；U9-U17 非规范位置
/// （MapFrom 标 Entity）反向匹配 + IsFromJson 对侧 fallback 功能验证。</para>
/// <para>断言分级：①生成代码存在性 ②方向语义（ObjectConvertToJson / JsonConvertToObject 调用）
/// ③值类型 return（无赋值）④诊断码（NCMAP001）。</para>
/// </summary>
public class ReverseMappingGenerationTests
{
    // ================================================================
    // U1-U8：规范位置（MapFrom 标 DTO）— 零回归显式断言
    // ================================================================

    /// <summary>
    /// U1（规范）：MapFrom 标 DTO + 属性级 [MapProperty(IsFromJson=true)] — 反序列化调用存在（基线回归）
    /// </summary>
    [Fact]
    public void U1_Normal_IsFromJson_DeserializationCall()
    {
        var source = """
            using NCMapper.Attributes;
            public class U1Entity { public int Id { get; set; } public string ConfigJson { get; set; } = ""; }
            public class U1Config { public string Theme { get; set; } = ""; }
            [MapFrom(typeof(U1Entity))]
            public class U1Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(U1Entity), nameof(U1Entity.ConfigJson), IsFromJson = true)]
                public U1Config Config { get; set; } = new();
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethod("Map_U1Entity_To_U1Dto");
        result.Should().HaveMethodBodyContaining("Map_U1Entity_To_U1Dto", "JsonConvertToObject<U1Config>");
        Assert.DoesNotContain("NCMAP001", result.DiagnosticsByDescriptorId.Keys);
    }

    /// <summary>
    /// U2（规范）：同名属性无标注 — 直接赋值（基线回归）
    /// </summary>
    [Fact]
    public void U2_Normal_SameName_DirectAssign()
    {
        var source = """
            using NCMapper.Attributes;
            public class U2Entity { public int Id { get; set; } public string Name { get; set; } = ""; }
            [MapFrom(typeof(U2Entity))]
            public class U2Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_U2Entity_To_U2Dto", "Name = src.Name");
        Assert.DoesNotContain("NCMAP001", result.DiagnosticsByDescriptorId.Keys);
    }

    /// <summary>
    /// U3（规范）：值类型参与 IsFromJson — return 跳过，无该属性赋值（基线回归）
    /// </summary>
    [Fact]
    public void U3_Normal_ValueTypeIsFromJson_Skipped()
    {
        var source = """
            using NCMapper.Attributes;
            public class U3Entity { public int Val { get; set; } }
            [MapFrom(typeof(U3Entity))]
            public class U3Dto
            {
                public int Val { get; set; }
                [MapProperty(typeof(U3Entity), nameof(U3Entity.Val), IsFromJson = true)]
                public int Val2 { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 值类型参与 → 属性跳过（正向 L408-409 return），方法体不含 Val2 赋值
        result.Should().HaveMethod("Map_U3Entity_To_U3Dto");
        var body = result.GetMethod("Map_U3Entity_To_U3Dto")!.Body;
        Assert.DoesNotContain("Val2", body);
    }

    /// <summary>
    /// U7（规范）：多目标属性指向同一源属性 — NCMAP009 触发（基线回归）
    /// </summary>
    [Fact]
    public void U7_Normal_MultipleTargets_SameSource_NCMAP009()
    {
        var source = """
            using NCMapper.Attributes;
            public class U7Entity { public string Shared { get; set; } = ""; }
            [MapFrom(typeof(U7Entity))]
            public class U7Dto
            {
                [MapProperty(typeof(U7Entity), nameof(U7Entity.Shared))]
                public string A { get; set; } = "";
                [MapProperty(typeof(U7Entity), nameof(U7Entity.Shared))]
                public string B { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.Contains("NCMAP009", result.DiagnosticsByDescriptorId.Keys);
    }

    // ================================================================
    // U9-U17：非规范位置（MapFrom 标 Entity）— B1 功能验证
    // ================================================================

    /// <summary>
    /// U9（非规范·核心触发场景）：MapFrom 标 Entity + Dto.Config [MapProperty(IsFromJson)] —
    /// 反向匹配建立映射，双向 JSON 转换生成（U17 Dto→Entity 序列化断言并入）
    /// </summary>
    [Fact]
    public void U9_NonNormal_ReverseMatch_JsonBothDirections()
    {
        var source = """
            using NCMapper.Attributes;
            public class R9Config { public string Theme { get; set; } = ""; }
            public class R9Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(R9Entity), nameof(R9Entity.ConfigJson), IsFromJson = true)]
                public R9Config Config { get; set; } = new();
            }
            [MapFrom(typeof(R9Dto))]
            public class R9Entity { public int Id { get; set; } public string ConfigJson { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 无 NCMAP001（反向匹配建立映射，原 null 分支被短路）
        Assert.DoesNotContain("NCMAP001", result.DiagnosticsByDescriptorId.Keys);

        // 正向 Dto→Entity：对象→string = 序列化（U17 断言）
        result.Should().HaveMethod("Map_R9Dto_To_R9Entity");
        result.Should().HaveMethodBodyContaining("Map_R9Dto_To_R9Entity", "ObjectConvertToJson");

        // 反向 Entity→Dto：string→对象 = 反序列化
        result.Should().HaveMethod("Map_R9Entity_To_R9Dto");
        result.Should().HaveMethodBodyContaining("Map_R9Entity_To_R9Dto", "JsonConvertToObject<R9Config>");
    }

    /// <summary>
    /// U10（非规范）：同名属性 + 对侧 IsFromJson — 正向匹配成功 + 改动点 3 补读
    /// </summary>
    [Fact]
    public void U10_NonNormal_SameName_OppositeIsFromJson()
    {
        var source = """
            using NCMapper.Attributes;
            public class R10Config { public string Theme { get; set; } = ""; }
            public class R10Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(R10Entity), nameof(R10Entity.ConfigJson), IsFromJson = true)]
                public R10Config ConfigJson { get; set; } = new();
            }
            [MapFrom(typeof(R10Dto))]
            public class R10Entity { public int Id { get; set; } public string ConfigJson { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 同名 ConfigJson 按名匹配成功 → 改动点 3 补读对侧 IsFromJson → 序列化
        result.Should().HaveMethod("Map_R10Dto_To_R10Entity");
        result.Should().HaveMethodBodyContaining("Map_R10Dto_To_R10Entity", "ObjectConvertToJson");
        result.Should().HaveMethodBodyContaining("Map_R10Entity_To_R10Dto", "JsonConvertToObject<R10Config>");
        Assert.DoesNotContain("NCMAP001", result.DiagnosticsByDescriptorId.Keys);
    }

    /// <summary>
    /// U11（非规范）：无对侧标注 — 反向匹配失败，NCMAP001 诊断（现状不变）
    /// 处置：NCMAP001 默认不报，本用例显式标注 ReportUnmappedTargetMembers=true 保留原断言（反向路径受开关控制验证）
    /// </summary>
    [Fact]
    public void U11_NonNormal_NoOppositeAttribute_NCMAP001()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = true)]
            public class R11Dto { public int Id { get; set; } }
            [MapFrom(typeof(R11Dto))]
            public class R11Entity { public int Id { get; set; } public string ConfigJson { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.Contains("NCMAP001", result.DiagnosticsByDescriptorId.Keys);
        // 方法仍生成（Id 正常映射），但不含 ConfigJson 赋值
        result.Should().HaveMethod("Map_R11Dto_To_R11Entity");
        Assert.DoesNotContain("ConfigJson", result.GetMethod("Map_R11Dto_To_R11Entity")!.Body);
    }

    /// <summary>
    /// U12（非规范）：值类型参与 IsFromJson — return 跳过，无该属性赋值（与 U3 行为一致）
    /// </summary>
    [Fact]
    public void U12_NonNormal_ValueTypeIsFromJson_Skipped()
    {
        var source = """
            using NCMapper.Attributes;
            public class R12Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(R12Entity), nameof(R12Entity.Val), IsFromJson = true)]
                public int Val { get; set; }
            }
            [MapFrom(typeof(R12Dto))]
            public class R12Entity { public int Id { get; set; } public int Val { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethod("Map_R12Dto_To_R12Entity");
        var body = result.GetMethod("Map_R12Dto_To_R12Entity")!.Body;
        Assert.DoesNotContain("Val = src.Val", body);
    }

    /// <summary>
    /// U13（非规范）：FromField 大小写差异 — OrdinalIgnoreCase 命中反向匹配
    /// </summary>
    [Fact]
    public void U13_NonNormal_FromFieldCaseInsensitive()
    {
        var source = """
            using NCMapper.Attributes;
            public class R13Config { public string Theme { get; set; } = ""; }
            public class R13Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(R13Entity), "configjson", IsFromJson = true)]
                public R13Config Config { get; set; } = new();
            }
            [MapFrom(typeof(R13Dto))]
            public class R13Entity { public int Id { get; set; } public string ConfigJson { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // FromField="configjson"（小写）vs 锚点属性 "ConfigJson" → OrdinalIgnoreCase 命中
        result.Should().HaveMethodBodyContaining("Map_R13Dto_To_R13Entity", "ObjectConvertToJson");
        Assert.DoesNotContain("NCMAP001", result.DiagnosticsByDescriptorId.Keys);
    }

    /// <summary>
    /// U14（非规范）：sourceType 基类属性带 [MapProperty] — GetTypeAndBaseTypes 反向匹配命中
    /// </summary>
    [Fact]
    public void U14_NonNormal_BaseTypeAttribute_ReverseMatch()
    {
        var source = """
            using NCMapper.Attributes;
            public class R14Config { public string Theme { get; set; } = ""; }
            public class R14DtoBase
            {
                [MapProperty(typeof(R14Entity), nameof(R14Entity.ConfigJson), IsFromJson = true)]
                public R14Config Config { get; set; } = new();
            }
            public class R14Dto : R14DtoBase { public int Id { get; set; } }
            [MapFrom(typeof(R14Dto))]
            public class R14Entity { public int Id { get; set; } public string ConfigJson { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 基类 R14DtoBase 的属性被 GetTypeAndBaseTypes 遍历命中
        result.Should().HaveMethodBodyContaining("Map_R14Dto_To_R14Entity", "ObjectConvertToJson");
        Assert.DoesNotContain("NCMAP001", result.DiagnosticsByDescriptorId.Keys);
    }

    /// <summary>
    /// U15（非规范）：对侧 [MapIgnore(Direction.Both)] — 反向匹配跳过，NCMAP001（不建立映射）
    /// 处置：NCMAP001 默认不报，本用例显式标注 ReportUnmappedTargetMembers=true 保留原断言
    /// </summary>
    [Fact]
    public void U15_NonNormal_MapIgnoreBoth_SkipsReverseMatch()
    {
        var source = """
            using NCMapper.Attributes;
            [assembly: NCMapperOptions(ReportUnmappedTargetMembers = true)]
            public class R15Config { public string Theme { get; set; } = ""; }
            public class R15Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(R15Entity), nameof(R15Entity.ConfigJson))]
                [MapIgnore(Direction.Both)]
                public R15Config Config { get; set; } = new();
            }
            [MapFrom(typeof(R15Dto))]
            public class R15Entity { public int Id { get; set; } public string ConfigJson { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 双向忽略 → 反向匹配跳过 → ConfigJson 未映射 → NCMAP001
        Assert.Contains("NCMAP001", result.DiagnosticsByDescriptorId.Keys);
        Assert.DoesNotContain("ConfigJson = src.Config", result.GetMethod("Map_R15Dto_To_R15Entity")!.Body);
    }

    /// <summary>
    /// U16（非规范）：两个对侧属性指向同一锚点属性 — 取声明序第一个建立映射，无 NCMAP009
    /// <para>修正说明：锚点遍历机制下第二个对侧属性不被处理，无重复 sourceProp 供 NCMAP009 检测；
    /// 与运行时 GetSourceItemByTargetItem 反向分支（L298-307 break 取第一个）行为一致。</para>
    /// </summary>
    [Fact]
    public void U16_NonNormal_MultipleOpposite_FirstWins_NoNCMAP009()
    {
        var source = """
            using NCMapper.Attributes;
            public class R16Config { public string Theme { get; set; } = ""; }
            public class R16Dto
            {
                public int Id { get; set; }
                [MapProperty(typeof(R16Entity), nameof(R16Entity.ConfigJson))]
                public R16Config ConfigA { get; set; } = new();
                [MapProperty(typeof(R16Entity), nameof(R16Entity.ConfigJson))]
                public R16Config ConfigB { get; set; } = new();
            }
            [MapFrom(typeof(R16Dto))]
            public class R16Entity { public int Id { get; set; } public string ConfigJson { get; set; } = ""; }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 取声明序第一个（ConfigA）建立映射（无 IsFromJson → 普通赋值路径）
        result.Should().HaveMethodBodyContaining("Map_R16Dto_To_R16Entity", "src.ConfigA");
        // 第二个（ConfigB）不被消费，且无 NCMAP009（与运行时行为一致）
        Assert.DoesNotContain("ConfigB", result.GetMethod("Map_R16Dto_To_R16Entity")!.Body);
        Assert.DoesNotContain("NCMAP009", result.DiagnosticsByDescriptorId.Keys);
    }

    // ================================================================
    // X1（W4S-2）：语义点①a 锚点优先 + 对侧 ConvertUsing 消费
    // ================================================================

    /// <summary>
    /// X1-①a-1：锚点带 [Convert] + 对侧同名属性带反向标注 → 走转换器（锚点显式标注优先，X1-1a 入口条件拦截反向匹配）
    /// <para>锚点 DTO.Phone 带 [Convert]（方法引用形态）→ 生成静态转换方法内联调用
    /// global::X1PhoneConverter.Convert(src.Phone)（不再生成 __conv 单例字段）；对侧 Entity.Phone 的
    /// [MapProperty] 反向标注不被消费（①a 优先级：锚点显式标注优先）。</para>
    /// </summary>
    [Fact]
    public void X1_AnchorConvertUsing_Prevails_OverReverseAnnotation()
    {
        var source = """
            using NCMapper.Attributes;
            public class X1Entity { public int Id { get; set; } public string Phone { get; set; } = ""; }
            public static class X1PhoneConverter
            {
                public static string Convert(string fieldValue) => "CONV:" + fieldValue;
            }
            [MapFrom(typeof(X1Entity))]
            public class X1Dto
            {
                public int Id { get; set; }
                [Convert(typeof(X1PhoneConverter), nameof(X1PhoneConverter.Convert))]
                public string Phone { get; set; } = "";
            }
            public class X1EntityPhoneHolder
            {
                [MapProperty(typeof(X1Dto), nameof(X1Dto.Phone))]
                public string Phone { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 锚点带 ConvertUsing → 生成静态转换方法内联调用（T1 单值模板，方法体未用 source）
        Assert.Contains("global::X1PhoneConverter.Convert(src.Phone)", result.GeneratedCode);
        Assert.Contains("X1PhoneConverter", result.GeneratedCode);
    }

    /// <summary>
    /// X1-①a-2：锚点无标注 + 源侧（对侧）带 [Convert] → fallback 消费对侧转换器（X1-1b）
    /// <para>源实体 Entity.Phone 带 [Convert]（对侧，方法引用形态），锚点 DTO.Phone 无标注——
    /// 按名匹配成功 + sourceProp 补读 ConvertUsing（与 B1 改动点 3 补读模式同构），消费对侧静态转换方法。</para>
    /// </summary>
    [Fact]
    public void X1_OppositeConvertUsing_Consumed_WhenAnchorUnannotated()
    {
        var source = """
            using NCMapper.Attributes;
            public class X1bEntity
            {
                public int Id { get; set; }
                [Convert(typeof(X1bConverter), nameof(X1bConverter.Convert))]
                public string Phone { get; set; } = "";
            }
            public static class X1bConverter
            {
                public static string Convert(string fieldValue) => "OPP:" + fieldValue;
            }
            [MapFrom(typeof(X1bEntity))]
            public class X1bDto
            {
                public int Id { get; set; }
                public string Phone { get; set; } = "";
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 源侧（对侧）带 [Convert] 经补读消费 → 静态转换方法调用生成（X1-1b，T1 单值模板）
        Assert.Contains("global::X1bConverter.Convert(src.Phone)", result.GeneratedCode);
    }

    // ================================================================
    // B3-01 单段索引路径反向跳过门（审计批次二）
    // HasIndex 门原在 SourcePath.Count > 1 分支内——单段索引路径（Posts[0]）逃逸到
    // 普通反向分支 → 反向生成 entity.Posts = dto.X（索引语义丢失），类型不匹配时
    // 生成不可编译代码且不发 NCMAP013。修复：HasIndex 门提前到 Count 判定之前。
    // ================================================================

    /// <summary>
    /// B3-01 主样例：FromField="Posts[0]"（单段索引）→ 正向 ElementAtOrDefault 语义保留；
    /// 反向 NCMAP013 登记恰一条（否定面：非索引单段 Id 不发）+ 反向方法体无该属性赋值
    /// （缺陷态：逃逸普通反向分支生成 dst.Posts = ... 语义丢失且零诊断 → 红）。
    /// </summary>
    [Fact]
    public void SingleSegmentIndexPath_ReverseSkipped_NCMAP013Registered()
    {
        var source = """
            using NCMapper.Attributes;
            using System.Collections.Generic;
            public class RvPost { public string Title { get; set; } = ""; }
            public class RvEntity { public int Id { get; set; } public List<RvPost> Posts { get; set; } = new(); }
            [MapFrom(typeof(RvEntity))]
            public class RvDto
            {
                public int Id { get; set; }
                [MapProperty(typeof(RvEntity), "Posts[0]")]
                public RvPost FirstPost { get; set; } = new();
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 正向：索引语义保留（ElementAtOrDefault 生成——单段索引正向承载面不受门提前影响）
        Assert.Contains("ElementAtOrDefault", result.GeneratedCode);

        // 反向：NCMAP013 登记恰一条（缺陷态零登记 → 红；误伤 Id 等非索引单段 → 计数 >1 红）
        var skipped = result.DiagnosticsByDescriptorId.TryGetValue("NCMAP013", out var list) ? list : null;
        Assert.NotNull(skipped);
        Assert.Single(skipped!);

        // 消息第四槽文案（单段场景不再断言"嵌套路径"——嵌套路径不存在）
        Assert.Contains("集合索引路径不适用反向映射", skipped![0].GetMessage());

        // 反向方法体无该属性赋值（缺陷态 dst.Posts = ... → 红）
        var reverseBody = result.Methods["Map_RvDto_To_RvEntity"][0].Body;
        Assert.DoesNotContain("dst.Posts =", reverseBody);
    }
}
