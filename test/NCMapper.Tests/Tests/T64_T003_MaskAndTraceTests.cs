// ============================================================================
// T64 / B5-5 Batch-3 — 掩码与追踪红测
//（A3-10 Override 生命周期 / A3-11 降级掩码形态 / A3-12 Fluent 掩码通道 / A3-19 代理对截断 /
//  A3-23 强制分析模式行为锁定）
//
// 裁决（用户 2026-09-19）：Q1=①（Override 移出 Full 清理层）、Q2=②（非规范输入给降级
// 掩码形态）、Q3=①（维持"强制分析模式"+文档明示——行为锁定免红测）。
//
// 缺陷承载（审计 20260917-001 -B5-5）：
//   A3-10  MaskProvider 以 CacheLayer.Full 登记 Clear（17/:81-85）——Mapper.ClearCache
//          连带清空用户 Override<> 注册，PII 静默回退内置脱敏；
//   A3-11  MaskIp non-IPv4 / MaskEmail 无@ / 短串各族 Mask 明文返回（Masks.cs:198-204/
//          :138-144/短串口径各门）——脱敏通道静默失效（审计原文"短串各 Mask 原值返回"）；
//   A3-12  ShowValues 仅按 [Mask] 特性脱敏（MapTracer.cs:277）——Fluent .Mask<>() 通道
//          （RuntimePropertyMapping.HasMask）明文泄露；
//   A3-19  FormatValue char 级截断（MapTracer.cs:317）切孤立代理项；
//   A3-23  Trace() 硬编码 Enabled=true（117）忽略 options.Enabled——Q3=① 维持 + 文档。
// ============================================================================

using System.IO;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Tests;

[Collection("GlobalConfigState")]
public class T64_T003_MaskAndTraceTests
{
    public T64_T003_MaskAndTraceTests()
    {
        Mapper.ClearCache();
    }

    // ============================== A3-10 ==============================

    /// <summary>
    /// A3-10 红测（Q1=①）：用户 Override 注册后调用 Mapper.ClearCache()——现状经
    /// CacheRegistry（mask-provider, Full 层）连带清空 _overrides，Resolve 回退内置
    /// 实现（红：PII 静默回退）；修复后 Override 注册移出 Full 清理层（独立生命周期），
    /// ClearCache 只清运行时实例缓存。
    /// </summary>
    [Fact]
    public void A3_10_ClearCache后_Override掩码注册仍生效()
    {
        MaskProvider.Override<MaskPhone>(new A3_10_CustomPhoneMask());
        try
        {
            Mapper.ClearCache();

            var resolved = MaskProvider.Resolve(typeof(MaskPhone));
            Assert.IsType<A3_10_CustomPhoneMask>(resolved);
        }
        finally
        {
            MaskProvider.Clear();
        }
    }

    /// <summary>
    /// A3-10 反幻觉锚点：显式 MaskProvider.Clear()（用户主动调用）仍全清——
    /// 独立生命周期的显式出口保持既有语义。
    /// </summary>
    [Fact]
    public void A3_10_显式MaskProviderClear_仍全清Override()
    {
        MaskProvider.Override<MaskPhone>(new A3_10_CustomPhoneMask());
        MaskProvider.Clear();

        Assert.IsNotType<A3_10_CustomPhoneMask>(MaskProvider.Resolve(typeof(MaskPhone)));
    }

    // ============================== A3-11 ==============================

    /// <summary>
    /// A3-11 红测（Q2=②）：MaskIp 对 non-IPv4 输入（IPv6/段数≠4）现状明文返回
    ///（红：断言非明文失败）；修复后降级掩码形态（fail-safe）。
    /// </summary>
    [Theory]
    [InlineData("2001:db8::1")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("not.an.ip")]
    public void A3_11_MaskIp_非IPv4输入_非明文返回(string input)
    {
        var masked = new MaskIp().Mask(input);
        Assert.NotEqual(input, masked);
        Assert.Contains('*', masked);
    }

    /// <summary>
    /// A3-11 红测（Q2=②）：MaskEmail 对无 @ / @ 起始输入现状明文返回（红）；
    /// 修复后降级掩码形态。
    /// </summary>
    [Theory]
    [InlineData("noemail")]
    [InlineData("@example.com")]
    public void A3_11_MaskEmail_无At输入_非明文返回(string input)
    {
        var masked = new MaskEmail().Mask(input);
        Assert.NotEqual(input, masked);
        Assert.Contains('*', masked);
    }

    /// <summary>
    /// A3-11 红测（Q2=②）：短串各族 Mask 现状原值返回（红——审计原文"短串各 Mask
    /// 原值返回"）；修复后降级掩码形态（首文本元素+****；单元素全遮）。
    /// </summary>
    [Fact]
    public void A3_11_短串输入_各族Mask非明文返回()
    {
        Assert.NotEqual("123456", new MaskPhone().Mask("123456"));
        Assert.NotEqual("123456789", new MaskIdCard().Mask("123456789"));
        Assert.NotEqual("123456789", new MaskBankCard().Mask("123456789"));
        Assert.NotEqual("北京市朝阳", new MaskAddress().Mask("北京市朝阳"));
        Assert.NotEqual("京A12", new MaskLicensePlate().Mask("京A12"));
        Assert.NotEqual("123456", new MaskLandline().Mask("123456"));
        Assert.NotEqual("010-1234", new MaskLandline().Mask("010-1234"));
        Assert.NotEqual("张", new MaskChineseName().Mask("张"));
    }

    /// <summary>
    /// A3-11 反幻觉锚点：null/空串行为不变（返回 ""，非泄漏面）；正常形态脱敏不变。
    /// </summary>
    [Fact]
    public void A3_11_空输入与正常形态_既有行为不变()
    {
        Assert.Equal("", new MaskPhone().Mask(null));
        Assert.Equal("", new MaskPhone().Mask(""));
        Assert.Equal("138****5678", new MaskPhone().Mask("13812345678"));
        Assert.Equal("192.168.*.*", new MaskIp().Mask("192.168.1.100"));
        Assert.Equal("z***@example.com", new MaskEmail().Mask("zhangsan@example.com"));
    }

    // ============================== A3-12 ==============================

    /// <summary>
    /// A3-12 红测：Fluent .Mask&lt;&gt;() 通道（注册表 RuntimePropertyMapping.HasMask 承载）
    /// 的属性在 ShowValues 下应输出 [MASKED]——现状仅按 [Mask] 特性判定（红：明文泄露）；
    /// 修复后 ShowValues 并入注册表 HasMask 判定。
    /// </summary>
    [Fact]
    public void A3_12_ShowValues_FluentMask注册表通道_输出MASKED()
    {
        RuntimeSnapshotStore.Clear();
        try
        {
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(A3_12_Src),
                TargetType = typeof(A3_12_Dto),
                SourceTypeFqn = typeof(A3_12_Src).FullName!,
                TargetTypeFqn = typeof(A3_12_Dto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "Phone", TypeName = "String" },
                        SourceName = "Phone",
                        HasMask = true,
                        MaskTypeName = "NCMapper.MaskPhone",
                    },
                },
            });

            var sw = new StringWriter();
            MapTracer.WriteTrace<A3_12_Src, A3_12_Dto>(
                new A3_12_Src { Phone = "13812345678" }, null,
                new MapTraceOptions { Enabled = true, ShowValues = true, Output = sw });

            Assert.Contains("[MASKED]", sw.ToString());
            Assert.DoesNotContain("13812345678", sw.ToString());
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }

    /// <summary>
    /// A3-12 反幻觉锚点：[Mask] 特性通道既有脱敏语义不变（含 inherit 语义订正后仍命中）。
    /// </summary>
    [Fact]
    public void A3_12_ShowValues_Mask特性通道_既有脱敏不变()
    {
        var sw = new StringWriter();
        MapTracer.WriteTrace<A3_12_AttrSrc, A3_12_AttrDto>(
            new A3_12_AttrSrc { Phone = "13812345678" }, null,
            new MapTraceOptions { Enabled = true, ShowValues = true, Output = sw });

        Assert.Contains("[MASKED]", sw.ToString());
        Assert.DoesNotContain("13812345678", sw.ToString());
    }

    // ============================== A3-19 ==============================

    /// <summary>
    /// A3-19 红测：值截断文本元素级——49 ASCII + emoji（50 元素/51 char）现状 char 级
    /// s[..50] 切出孤立高代理（红：emoji 被切坏，Contains 失败）；修复后 50 元素 ≤ 50
    /// 不截断，emoji 完整输出。
    /// </summary>
    [Fact]
    public void A3_19_值截断_文本元素级_不切孤立代理项()
    {
        var value = new string('a', 49) + "😀"; // 51 chars / 50 文本元素
        var sw = new StringWriter();
        MapTracer.WriteTrace<A3_19_Src, A3_19_Dto>(
            new A3_19_Src { V = value }, null,
            new MapTraceOptions { Enabled = true, ShowValues = true, Output = sw });

        Assert.Contains("😀", sw.ToString());
    }

    /// <summary>
    /// A3-19 反幻觉锚点：超长值仍截断（元素级）——51 ASCII + emoji（52 元素）截断为
    /// 50 元素 + "..."，emoji 不出现在截断结果（元素级截断语义）。
    /// </summary>
    [Fact]
    public void A3_19_超长值_仍按元素级截断()
    {
        var value = new string('a', 51) + "😀"; // 53 chars / 52 文本元素
        var sw = new StringWriter();
        MapTracer.WriteTrace<A3_19_Src, A3_19_Dto>(
            new A3_19_Src { V = value }, null,
            new MapTraceOptions { Enabled = true, ShowValues = true, Output = sw });

        var output = sw.ToString();
        Assert.Contains("...", output);
        Assert.DoesNotContain("😀", output);
        Assert.Contains(new string('a', 50), output);
    }

    // ============================== A3-23 ==============================

    /// <summary>
    /// A3-23 行为锁定（Q3=①，免红测）：Trace() 维持"强制分析模式"——无视
    /// options.Enabled 硬输出报告（返回 string 设计绑定强制语义）。
    /// </summary>
    [Fact]
    public void A3_23_Trace强制分析模式_忽略optionsEnabled_行为锁定()
    {
        var report = MapTracer.Trace<A3_23_Src, A3_23_Dto>(
            new A3_23_Src { Name = "n" },
            new MapTraceOptions { Enabled = false });

        Assert.NotEmpty(report);
        Assert.Contains("A3_23_Src", report);
        Assert.Contains("A3_23_Dto", report);
    }

    // ============================== 模型 ==============================

    private sealed class A3_10_CustomPhoneMask : IMask
    {
        public string Mask(string? value) => "[CUSTOM-MASKED]";
    }

    private sealed class A3_12_Src { public string Phone { get; set; } = ""; }
    private sealed class A3_12_Dto { public string Phone { get; set; } = ""; }

    private sealed class A3_12_AttrSrc { public string Phone { get; set; } = ""; }
    [MapFrom(typeof(A3_12_AttrSrc))] // T5 复核：Plan 直调输入的承载侧合法形态
    private sealed class A3_12_AttrDto
    {
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    private sealed class A3_19_Src { public string V { get; set; } = ""; }
    private sealed class A3_19_Dto { public string V { get; set; } = ""; }

    private sealed class A3_23_Src { public string Name { get; set; } = ""; }
    private sealed class A3_23_Dto { public string Name { get; set; } = ""; }
}
