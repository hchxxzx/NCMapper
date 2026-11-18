using NCMapper.SourceGen;
using Xunit;
using NCMapper.SourceGen.Tests.Helpers;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// [MapFormat] 编译期矩阵——生成产物形态（语句版/表达式版/枚举/
    /// 可空/组合/Compose）与守门诊断（NCMAP043/044/045/046 + 家族接入面 NCMAP019/022）。
    /// 产物形态对照方案书 20260929-003 v1.2 A（BuildFormattedAssignment 单点模板）。
    /// </summary>
    public class MapFormatGenerationTests
    {
        // ================================================================
        // 生成产物形态（A：dst.Prop = src.Prop.ToString("fmt", null)；可空 ?.）
        // ================================================================

        [Fact]
        public void MapFormat_StatementVersion_EmitsToStringCall()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MFEntity { public DateTime CreatedAt { get; set; } }
                [MapFrom(typeof(MFEntity))]
                public class MFDto
                {
                    [MapFormat("yyyy-MM-dd")]
                    public string CreatedAt { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 语句版模板单点：ToString("yyyy-MM-dd", null)（provider 恒 null——当前文化语义）
            result.Should().HaveMethodBodyContaining("Map_MFEntity_To_MFDto",
                "CreatedAt = src.CreatedAt.ToString(\"yyyy-MM-dd\", null)");
        }

        [Fact]
        public void MapFormat_NullableSource_EmitsNullConditionalCall()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MFNEntity { public DateTime? ShippedDate { get; set; } }
                [MapFrom(typeof(MFNEntity))]
                public class MFNDto
                {
                    [MapFormat("yyyy-MM-dd")]
                    public string? ShippedDate { get; set; }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 可空源 ?. 空条件调用（null 源 → 目标 null）
            result.Should().HaveMethodBodyContaining("Map_MFNEntity_To_MFNDto",
                "ShippedDate = src.ShippedDate?.ToString(\"yyyy-MM-dd\", null)");
        }

        [Fact]
        public void MapFormat_EnumSource_EmitsToStringCall()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MFEEntity { public DayOfWeek Status { get; set; } }
                [MapFrom(typeof(MFEEntity))]
                public class MFEDto
                {
                    [MapFormat("D")]
                    public string Status { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 枚举源：单参 ToString("D")——.NET 10 起 Enum.ToString(String, IFormatProvider) 废弃
            //（CS0618，provider 本不用于枚举），warnaserror 工程（CI）强制单参形态（行为等价）
            result.Should().HaveMethodBodyContaining("Map_MFEEntity_To_MFEDto",
                "Status = src.Status.ToString(\"D\")");
        }

        [Fact]
        public void MapFormatThenMask_EmitsMaskWrappingFormatCall()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MFKEntity { public DateTime PaidAt { get; set; } }
                [MapFrom(typeof(MFKEntity))]
                public class MFKDto
                {
                    [MapFormat("yyyy-MM-dd")]
                    [Mask(typeof(MFMaskImpl))]
                    public string PaidAt { get; set; } = "";
                }
                public class MFMaskImpl : NCMapper.IMask
                {
                    public string Mask(string? value) => (value ?? "") + "#";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // FormatThenMask 双标有序：先格式化再脱敏（镜像 ConvertUsingThenMask 包装形态）
            result.Should().HaveMethodBodyContaining("Map_MFKEntity_To_MFKDto",
                "PaidAt = __mask_MFKDto_PaidAt.Mask(src.PaidAt.ToString(\"yyyy-MM-dd\", null))");
        }

        [Fact]
        public void ComposeFormatThenMask_EmitsMaskWrappingFormatCall_DeclaresMaskField()
        {
            // 等价性（缺陷修复 2026-10-23）：Compose 块内 Format+Mask 双标（Fluent FromSource 块）
            // 与主链 FormatThenMask 产出同一形态——掩码静态字段已声明（收集侧漏查 FormatString
            // 时只引用不声明 → CS0103）+ 先 Format 后 Mask 值序。分流注意：FromSource 命中属性
            // 只入 ComposeMappings（掩码收集只能走 cm 收集侧），故本用例是收集谓词的直接验证面
            var source = """
                using NCMapper;
                using System;
                public class CFKEntity { public DateTime PaidAt { get; set; } }
                public class CFKSub { public DateTime MemberSince { get; set; } }
                public class CFKDto { public string JoinedAt { get; set; } = ""; }
                public class CFKMask : NCMapper.IMask
                {
                    public string Mask(string? value) => (value ?? "") + "#";
                }
                [MapperConfig]
                public class CFKMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<CFKEntity, CFKDto>()
                           .FromSource<CFKSub>(c =>
                           {
                               c.Member(d => d.JoinedAt).From(s => s.MemberSince)
                                   .Format("yyyy-MM-dd").Mask<CFKMask>();
                           });
                    }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // ② 掩码字段已声明（Compose 收集侧谓词含 FormatString 分支——缺陷态缺声明仅引用）
            Assert.Contains("private static readonly global::NCMapper.IMask __mask_CFKDto_JoinedAt",
                result.GeneratedCode);
            // ③ 值序正确：Mask 包装 Format 调用（先 Format 后 Mask，与主链 FormatThenMask 同序）
            Assert.Contains("JoinedAt = __mask_CFKDto_JoinedAt.Mask(src.MemberSince.ToString(\"yyyy-MM-dd\", null))",
                result.GeneratedCode);
            Assert.Matches(@"JoinedAt = __mask_CFKDto_JoinedAt\.Mask\([^;]*\.ToString\(""yyyy-MM-dd"", null\)\)",
                result.GeneratedCode);
        }

        [Fact]
        public void MapFormat_InitOnlyTarget_ExpressionVersion()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MFIEntity { public DateTime CreatedAt { get; set; } }
                [MapFrom(typeof(MFIEntity))]
                public class MFIDto
                {
                    [MapFormat("yyyy-MM-dd")]
                    public string CreatedAt { get; init; }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // init-only 目标表达式版（B 直接表达式化——探针 1 实证形态）：
            // 对象初始化器内 ?.ToString（C# 编译器自动生成 Condition(HasValue, …) 等价结构）
            result.Should().HaveMethodBodyContaining("Map_MFIEntity_To_MFIDto",
                "CreatedAt = src.CreatedAt.ToString(\"yyyy-MM-dd\", null)");
        }

        [Fact]
        public void MapFormat_ConditionalEmission_NoRowWhenUnannotated()
        {
            // 条件发射硬约束（证伪 F2）：无 [MapFormat] 的映射对产物零 FormatString 行
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MFCEntity { public DateTime CreatedAt { get; set; } }
                [MapFrom(typeof(MFCEntity))]
                public class MFCDto
                {
                    public string CreatedAt { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.DoesNotContain("FormatString", result.GeneratedCode);
        }

        // ================================================================
        // 守门诊断（守门表——Error 级编译期拦截）
        // ================================================================

        [Fact]
        public void MapFormat_NonStringTarget_EmitsNCMAP043()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MF43Entity { public DateTime CreatedAt { get; set; } }
                [MapFrom(typeof(MF43Entity))]
                public class MF43Dto
                {
                    [MapFormat("yyyy-MM-dd")]
                    public DateTime CreatedAt { get; set; }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP043"),
                $"期望 NCMAP043（目标非 string），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        [Fact]
        public void MapFormat_StringSource_EmitsNCMAP044()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                public class MF44Entity { public string Name { get; set; } = ""; }
                [MapFrom(typeof(MF44Entity))]
                public class MF44Dto
                {
                    [MapFormat("yyyy-MM-dd")]
                    public string Name { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // string 源不支持（未实现 IFormattable——支持域显式排除）
            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP044"),
                $"期望 NCMAP044（源非 IFormattable），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        [Fact]
        public void MapFormat_WithConvert_SameProperty_EmitsNCMAP045()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MF45Entity { public DateTime CreatedAt { get; set; } }
                public static class MF45Converter
                {
                    public static string Convert(DateTime value) => value.ToString("O");
                }
                [MapFrom(typeof(MF45Entity))]
                public class MF45Dto
                {
                    [MapFormat("yyyy-MM-dd")]
                    [Convert(typeof(MF45Converter), nameof(MF45Converter.Convert))]
                    public string CreatedAt { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 同标互斥（组合矩阵——三标情形由本码优先拦截）
            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP045"),
                $"期望 NCMAP045（Format+Convert 同标），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        [Fact]
        public void MapFormat_ReverseDerivation_EmitsNCMAP046()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MF46Entity
                {
                    public DateTime CreatedAt { get; set; }
                }
                [MapFrom(typeof(MF46Entity))]
                public class MF46Dto
                {
                    [MapFormat("yyyy-MM-dd")]
                    public string CreatedAt { get; set; } = "";
                }
                public static class MF46Program
                {
                    public static MF46Entity Run(MF46Dto dto) => NCMapper.Mapper.Map<MF46Dto, MF46Entity>(dto);
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 反向派生命中（未标 [ConvertBack]）→ NCMAP046 警告（反向跳过该属性不赋值——执行后设计
            // 修订 2026-09-29：Error 强制表态撤销，裸 [MapFormat] 合法）
            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP046"),
                $"期望 NCMAP046（反向跳过警告），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        [Fact]
        public void MapFormat_WithConvertBack_SameProperty_NoReverseDiagnostic()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MF47Entity
                {
                    public DateTime CreatedAt { get; set; }
                }
                public static class MF47Parser
                {
                    public static DateTime Parse(string value) => DateTime.Parse(value);
                }
                [MapFrom(typeof(MF47Entity))]
                public class MF47Dto
                {
                    [MapFormat("yyyy-MM-dd")]
                    [ConvertBack(typeof(MF47Parser), nameof(MF47Parser.Parse))]
                    public string CreatedAt { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // 合法组合（方向正交——[ConvertBack] 仅反向消费）：反向照常消费、无警告
            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP046"),
                "标 [ConvertBack] 的 [MapFormat] 属性反向照常消费，不应报 NCMAP046 警告");
        }

        [Fact]
        public void MapFormat_NoAnchor_EmitsNCMAP019()
        {
            // 接入面 b（NCMAP019 十项家族清单——漏改 = 无锚标注静默无效）
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MF48Entity { public DateTime CreatedAt { get; set; } }
                public class MF48NoAnchorDto
                {
                    [MapFormat("yyyy-MM-dd")]
                    public string CreatedAt { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP019"),
                $"期望 NCMAP019（无锚拦截），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        [Fact]
        public void MapFormat_OverrideIgnoreWithMapIgnore_EmitsNCMAP022()
        {
            // 接入面 c（NCMAP022 矛盾谓词——漏改 = 矛盾配置静默漏报）
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MF49Entity { public DateTime CreatedAt { get; set; } }
                [MapFrom(typeof(MF49Entity))]
                public class MF49Dto
                {
                    [MapIgnore]
                    [MapFormat("yyyy-MM-dd", OverrideIgnore = true)]
                    public string CreatedAt { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP022"),
                $"期望 NCMAP022（MapIgnore × OverrideIgnore 矛盾），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        // ================================================================
        // Fluent 通道诊断（F64~F67——触发落点）
        // ================================================================

        [Fact]
        public void FluentFormat_NonStringTarget_EmitsF64()
        {
            var source = """
                using NCMapper;
                using System;
                public class FF64Entity { public DateTime CreatedAt { get; set; } }
                public class FF64Dto { public DateTime CreatedAt { get; set; } }
                [MapperConfig]
                public class FF64Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FF64Entity, FF64Dto>().Member(t => t.CreatedAt).Format("yyyy-MM-dd");
                    }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP_F64"),
                $"期望 NCMAP_F64（dest 非 string），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        [Fact]
        public void FluentFormat_NonFormattableSource_EmitsF65()
        {
            var source = """
                using NCMapper;
                using System;
                public class FF65Entity { public string Name { get; set; } = ""; }
                public class FF65Dto { public string Name { get; set; } = ""; }
                [MapperConfig]
                public class FF65Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FF65Entity, FF65Dto>().Member(t => t.Name).Format("yyyy-MM-dd");
                    }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP_F65"),
                $"期望 NCMAP_F65（源非 IFormattable），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        [Fact]
        public void FluentFormat_WithConvert_SameAnchor_EmitsF66()
        {
            var source = """
                using NCMapper;
                using System;
                public class FF66Entity { public DateTime CreatedAt { get; set; } }
                public class FF66Dto { public string CreatedAt { get; set; } = ""; }
                public static class FF66Converter
                {
                    public static string Convert(System.DateTime value) => value.ToString("O");
                }
                [MapperConfig]
                public class FF66Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FF66Entity, FF66Dto>()
                           .Member(t => t.CreatedAt).Format("yyyy-MM-dd")
                           .Member(t => t.CreatedAt).Convert<DateTime>(FF66Converter.Convert);
                    }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP_F66"),
                $"期望 NCMAP_F66（Format × Convert 同锚点），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        [Fact]
        public void FluentFormat_ReverseDerivation_EmitsF67()
        {
            var source = """
                using NCMapper;
                using System;
                public class FF67Entity { public DateTime CreatedAt { get; set; } }
                public class FF67Dto { public string CreatedAt { get; set; } = ""; }
                [MapperConfig]
                public class FF67Mapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FF67Entity, FF67Dto>().Member(t => t.CreatedAt).Format("yyyy-MM-dd");
                    }
                }
                public static class FF67Program
                {
                    public static FF67Entity Run(FF67Dto dto) => NCMapper.Mapper.Map<FF67Dto, FF67Entity>(dto);
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP_F67"),
                $"期望 NCMAP_F67（Fluent 反向跳过警告），实际: [{string.Join(", ", result.DiagnosticsByDescriptorId.Keys)}]");
        }

        // ================================================================
        // 邻界反例：合法 [MapFormat] 形态不误报（NCMAP043/044/045 同断言面）
        // ================================================================

        [Fact]
        public void MapFormat_ValidForm_StringTargetFormattableSource_NoFormatDiagnostics()
        {
            // 邻界合法形态：目标 string 且源 DateTime（IFormattable）且无同标 [Convert] → 三码均不报
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MFOKEntity { public DateTime CreatedAt { get; set; } }
                [MapFrom(typeof(MFOKEntity))]
                public class MFOKDto
                {
                    [MapFormat("yyyy-MM-dd")]
                    public string CreatedAt { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP043"), "目标为 string，不应报 NCMAP043");
            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP044"), "源为 IFormattable，不应报 NCMAP044");
            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP045"), "无同标 [Convert]，不应报 NCMAP045");
        }

        [Fact]
        public void FluentFormat_ValidForm_NoFormatDiagnostics()
        {
            // 邻界合法形态：Fluent 目标 string + 源 DateTime + 无同锚 .Convert → F64/F65/F66 均不报
            var source = """
                using NCMapper;
                using System;
                public class FFOKEntity { public DateTime CreatedAt { get; set; } }
                public class FFOKDto { public string CreatedAt { get; set; } = ""; }
                [MapperConfig]
                public class FFOKMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FFOKEntity, FFOKDto>().Member(t => t.CreatedAt).Format("yyyy-MM-dd");
                    }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP_F64"), "Fluent 目标为 string，不应报 NCMAP_F64");
            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP_F65"), "Fluent 源为 IFormattable，不应报 NCMAP_F65");
            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP_F66"), "无同锚 .Convert，不应报 NCMAP_F66");
        }

        [Fact]
        public void FluentFormat_WithConvertBack_NoReverseDiagnostic()
        {
            // 邻界合法形态：Fluent .Format 同锚声明 .ConvertBack（反向消费）→ 不报 F67
            var source = """
                using NCMapper;
                using System;
                public class FF67OKEntity { public DateTime CreatedAt { get; set; } }
                public class FF67OKDto { public string CreatedAt { get; set; } = ""; }
                public static class FF67OKConv
                {
                    public static DateTime Parse(string value) => DateTime.Parse(value);
                }
                [MapperConfig]
                public class FF67OKMapper
                {
                    public static void Configure(FluentMappingConfig cfg)
                    {
                        cfg.For<FF67OKEntity, FF67OKDto>().Member(t => t.CreatedAt)
                           .Format("yyyy-MM-dd").ConvertBack(FF67OKConv.Parse);
                    }
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP_F67"), "声明 .ConvertBack 后反向照常消费，不应报 NCMAP_F67");
        }

        // ================================================================
        // Compose 子映射格式化（路径 8）
        // ================================================================

        [Fact]
        public void Compose_SubMapping_MapFormat_EmitsFormattedAssignment()
        {
            var source = """
                using NCMapper;
                using NCMapper.Attributes;
                using System;
                public class MFCOrder { public DateTime CreatedAt { get; set; } }
                public class MFCCustomer { public MFCOrder Order { get; set; } = new(); public string Name { get; set; } = ""; }
                [MapFrom(typeof(MFCCustomer))]
                public class MFCDto
                {
                    [MapProperty(typeof(MFCOrder), nameof(MFCOrder.CreatedAt))]
                    [MapFormat("yyyy-MM-dd")]
                    public string CreatedAt { get; set; } = "";
                    public string Name { get; set; } = "";
                }
                """;

            var result = TestHelper.GenerateMapper(source);

            // Compose 跨表子映射渲染（Update_{Sub}_To_{Target} 块内赋值层）
            Assert.Contains(".ToString(\"yyyy-MM-dd\", null)", result.GeneratedCode);
        }
    }
}
