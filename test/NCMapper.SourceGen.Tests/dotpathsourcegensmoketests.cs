using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 显式点路径源生成器冒烟测试（Batch-1）— A2-1~A2-5 + A1-3（PathOrigin/Compose SourcePath 相等性）
/// <para>覆盖：生成形态 src.A?.B / 失败 NCMAP001 带失败段名 / NCMAP009 显式重复判定与推导豁免 /
/// Compose 生成端多段访问 / Mask·ConvertUsing 可观测诊断（NCMAP030）</para>
/// </summary>
public class DotPathSourceGenSmokeTests
{
    // ==================== A2-1：点路径生成 src.A?.B 形态 ====================

    [Fact]
    public void SameSourceDotPath_GeneratesNullSafeChain()
    {
        var source = """
            using NCMapper.Attributes;
            public class DpEntity { public DpAddress Address { get; set; } }
            public class DpAddress { public string City { get; set; } }
            [MapFrom(typeof(DpEntity))]
            public class DpDto
            {
                [MapProperty(typeof(DpEntity), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("Map_DpEntity_To_DpDto", "City = src.Address?.City");
    }

    // ==================== A2-2：失败 NCMAP001 带失败段名 + 所在类型 ====================

    [Fact]
    public void DotPath_FailedSegment_NCMAP001_WithSegmentNameAndType()
    {
        var source = """
            using NCMapper.Attributes;
            public class DpEntity2 { public DpAddress Address { get; set; } }
            public class DpAddress { public string City { get; set; } }
            [MapFrom(typeof(DpEntity2))]
            public class DpDto2
            {
                [MapProperty(typeof(DpEntity2), "Address.Missing")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP001"), "应输出 NCMAP001（拆段失败）");
        var message = result.DiagnosticsByDescriptorId["NCMAP001"][0].GetMessage();
        Assert.Contains("Missing", message);  // 失败段名
        Assert.Contains("DpAddress", message); // 失败段所在类型
    }

    [Fact]
    public void DotPath_IntermediateValueType_NCMAP001_WithSegmentName()
    {
        var source = """
            using NCMapper.Attributes;
            public class DpEntity3 { public DpAddress Address { get; set; } }
            public class DpAddress { public int Zip { get; set; } }
            [MapFrom(typeof(DpEntity3))]
            public class DpDto3
            {
                [MapProperty(typeof(DpEntity3), "Address.Zip.X")]
                public int X { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP001"), "应输出 NCMAP001（中间节点值类型中断）");
        Assert.Contains("Zip", result.DiagnosticsByDescriptorId["NCMAP001"][0].GetMessage());
    }

    // ==================== A2-3：NCMAP009 显式重复判定 / 推导豁免 ====================

    [Fact]
    public void DotPath_ExplicitDuplicate_EmitsNCMAP009()
    {
        var source = """
            using NCMapper.Attributes;
            public class DpEntity4 { public DpAddress Address { get; set; } }
            public class DpAddress { public string City { get; set; } }
            [MapFrom(typeof(DpEntity4))]
            public class DpDto4
            {
                [MapProperty(typeof(DpEntity4), "Address.City")]
                public string City { get; set; }
                [MapProperty(typeof(DpEntity4), "Address.City")]
                public string City2 { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 显式点路径（PathOrigin=ExplicitDotPath）参与重复检测
        Assert.True(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP009"), "两个显式点路径指向同一叶应报 NCMAP009");
    }

    [Fact]
    public void DerivedFlatten_ExemptFromNCMAP009()
    {
        var source = """
            using NCMapper.Attributes;
            public class DpEntity5 { public DpAddress Address { get; set; } }
            public class DpAddress { public string City { get; set; } }
            [MapFrom(typeof(DpEntity5))]
            public class DpDto5
            {
                public string AddressCity { get; set; }
                public string AddressCity2 { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 推导扁平化（PathOrigin=DerivedFlatten）豁免——自动行为重复无意义
        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP009"), "推导扁平化重复不应报 NCMAP009");
    }

    // ==================== A2-4：Compose 生成端多段访问（Compose 四点） ====================

    [Fact]
    public void ComposeDotPath_GeneratesMultiSegmentAccess()
    {
        var source = """
            using NCMapper.Attributes;
            public class Ord { public int Id { get; set; } }
            public class Cst { public Adr Address { get; set; } }
            public class Adr { public string City { get; set; } }
            [MapFrom(typeof(Ord))]
            public class OrdDto
            {
                [MapProperty(typeof(Cst), "Address.City")]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // Compose 部分更新方法：多段访问 src.A?.B（C7 null 安全）
        result.Should().HaveMethod("Update_Cst_To_OrdDto");
        result.Should().HaveMethodBodyContaining("Update_Cst_To_OrdDto", "src.Address?.City");
    }

    // ==================== A2-5：Mask/ConvertUsing 消费（H6，替代 NCMAP030 可观测断言）====================

    [Fact]
    public void DotPath_Mask_Consumed_NoNCMAP030()
    {
        var source = """
            using NCMapper.Attributes;
            public class DpEntity6 { public DpAddress Address { get; set; } }
            public class DpAddress { public string City { get; set; } }
            public class PhoneMask : NCMapper.IMask
            {
                public string Mask(string? value) => value ?? "";
            }
            [MapFrom(typeof(DpEntity6))]
            public class DpDto6
            {
                [MapProperty(typeof(DpEntity6), "Address.City")]
                [Mask(typeof(PhoneMask))]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP030"), "点路径 + [Mask] 消费生效后不应输出 NCMAP030");
        // 生成脱敏调用（消费形态与直接属性分支一致）
        result.Should().HaveMethodBodyContaining("Map_DpEntity6_To_DpDto6",
            "City = __mask_DpDto6_City.Mask(src.Address?.City)");
    }

    [Fact]
    public void DotPath_ConvertUsing_Consumed_NoNCMAP030()
    {
        var source = """
            using NCMapper.Attributes;
            public class DpEntity7 { public DpAddress Address { get; set; } }
            public class DpAddress { public string City { get; set; } }
            public static class StrConverter
            {
                public static string Convert(string fieldValue) => fieldValue;
            }
            [MapFrom(typeof(DpEntity7))]
            public class DpDto7
            {
                [MapProperty(typeof(DpEntity7), "Address.City")]
                [Convert(typeof(StrConverter), nameof(StrConverter.Convert))]
                public string City { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        Assert.False(result.DiagnosticsByDescriptorId.ContainsKey("NCMAP030"), "点路径 + [Convert] 消费生效后不应输出 NCMAP030");
        // 生成转换器调用（消费形态与直接属性分支一致：内联静态调用，点路径访问作为字段实参）
        result.Should().HaveMethodBodyContaining("Map_DpEntity7_To_DpDto7",
            "City = global::StrConverter.Convert(src.Address?.City)");
    }

    // ==================== A1-3：PathOrigin 入快照相等性（H19） ====================

    [Fact]
    public void PathOrigin_ParticipatesInSnapshotEquality()
    {
        var type = new TypeRef { FullName = "T", EqualityKey = "T", MinimallyQualifiedName = "T" };
        var prop = new PropertyRef { Name = "X", Type = type };

        var derived = new PropertyMappingSnapshot
        {
            Target = prop,
            Source = prop,
            SourcePath = new[] { prop },
            IsFlattened = true,
            PathOrigin = PathOrigin.DerivedFlatten
        };
        var explicitPath = new PropertyMappingSnapshot
        {
            Target = prop,
            Source = prop,
            SourcePath = new[] { prop },
            IsFlattened = true,
            PathOrigin = PathOrigin.ExplicitDotPath
        };
        var derived2 = new PropertyMappingSnapshot
        {
            Target = prop,
            Source = prop,
            SourcePath = new[] { prop },
            IsFlattened = true,
            PathOrigin = PathOrigin.DerivedFlatten
        };

        // P1-2：PathOrigin 不同但 SourceProperty/TargetProperty/SourcePath 相同 → 判不等
        Assert.NotEqual(derived, explicitPath);
        Assert.NotEqual(derived.GetHashCode(), explicitPath.GetHashCode());
        Assert.Equal(derived, derived2);
    }

    /// <summary>门控表第 15 点：Compose 快照 SourcePath 入相等性（增量管道防误命中）</summary>
    [Fact]
    public void ComposeSnapshot_SourcePath_ParticipatesInEquality()
    {
        var type = new TypeRef { FullName = "T", EqualityKey = "T", MinimallyQualifiedName = "T" };
        var prop = new PropertyRef { Name = "X", Type = type };

        var withPath = new ComposeMappingSnapshot
        {
            SubSource = type,
            Source = prop,
            Target = prop,
            SourcePath = new[] { prop }
        };
        var withoutPath = new ComposeMappingSnapshot
        {
            SubSource = type,
            Source = prop,
            Target = prop,
            SourcePath = Array.Empty<PropertyRef>()
        };

        Assert.NotEqual(withPath, withoutPath);
        Assert.NotEqual(withPath.GetHashCode(), withoutPath.GetHashCode());
    }
}
