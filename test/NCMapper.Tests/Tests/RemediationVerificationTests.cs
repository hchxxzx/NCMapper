using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 审计整改验证测试 — 覆盖（运行时 Mask fail-fast）、（代理对）、
/// （MapTracer [MASKED]）、（Clear 缓存）等修复项
/// </summary>
[Collection("DeepCopyGlobalState")]
public class RemediationVerificationTests
{
    // =====，运行时 Mask Emit（原 fail-fast 已改为正确脱敏）=====

    /// <summary>
    /// 验证运行时回退路径对标注 [Mask] 的 string 属性正确脱敏（不再抛异常）
    /// </summary>
    [Fact]
    public void RuntimePath_MaskOnString_CorrectlyMasks()
    {
        var result = Mapper.Map<InternalMaskSource, InternalMaskDto>(new InternalMaskSource { Phone = "13812345678" });
        Assert.NotNull(result);
        Assert.Equal("138****5678", result!.Phone);
    }

    /// <summary>
    /// 验证 MaskProvider.Override 在运行时路径生效
    /// </summary>
    [Fact]
    public void RuntimePath_MaskOverride_Works()
    {
        try
        {
            MaskProvider.Override<MaskPhone>(new JapanPhoneMask());
            // 注意：不可调 Mapper.ClearCache()，因其内部调 MaskProvider.Clear() 会清除 Override
            // 运行时委托每次调用都走 MaskProvider.Resolve(Type)，Override 无需清缓存即生效
            var result = Mapper.Map<InternalMaskSource, InternalMaskDto>(new InternalMaskSource { Phone = "09012345678" });
            Assert.NotNull(result);
            Assert.Equal("090-****-5678", result!.Phone);
        }
        finally
        {
            MaskProvider.Clear();
        }
    }

    /// <summary>
    /// 验证 MaskProvider.Clear 后恢复内置规则
    /// </summary>
    [Fact]
    public void RuntimePath_MaskClear_RestoresBuiltIn()
    {
        MaskProvider.Override<MaskPhone>(new JapanPhoneMask());
        MaskProvider.Clear();
        var result = Mapper.Map<InternalMaskSource, InternalMaskDto>(new InternalMaskSource { Phone = "13812345678" });
        Assert.NotNull(result);
        Assert.Equal("138****5678", result!.Phone);
    }

    /// <summary>
    /// 验证无 [Mask] 的运行时映射不受影响（不抛异常）
    /// </summary>
    [Fact]
    public void RuntimePath_NoMask_MapsNormally()
    {
        var result = Mapper.Map<InternalNoMaskSource, InternalNoMaskDto>(
            new InternalNoMaskSource { Name = "test", Value = 42 });
        Assert.NotNull(result);
        Assert.Equal("test", result!.Name);
        Assert.Equal(42, result.Value);
    }

    // =====，MaskChineseName 代理对测试 =====

    [Fact]
    public void MaskChineseName_SurrogatePair_CjkExtB()
    {
        var result = new MaskChineseName().Mask("𠀀字三");
        Assert.Equal("𠀀*三", result);
    }

    [Fact]
    public void MaskChineseName_SurrogatePair_TwoChar()
    {
        var result = new MaskChineseName().Mask("𠀀明");
        Assert.Equal("𠀀*", result);
    }

    // =====，MapTracer ShowValues =====

    [Fact]
    public void MapTracer_ShowValues_Option_Works()
    {
        var options = new MapTraceOptions { Enabled = true, ShowValues = true };
        Assert.True(options.Enabled);
        Assert.True(options.ShowValues);
    }

    // =====，MaskProvider.Clear 幂等性 =====

    [Fact]
    public void MaskProvider_Clear_Idempotent()
    {
        MaskProvider.Override<MaskPhone>(new MaskPhone());
        MaskProvider.Clear();
        var resolved = MaskProvider.Resolve<MaskPhone>();
        Assert.NotNull(resolved);
        MaskProvider.Clear(); // 二次 Clear 幂等性
    }

    // ===== 内部测试模型（private class，源生成器因 NCMAP011 跳过非 public 类型）=====

    private class InternalMaskSource
    {
        public string Phone { get; set; } = "";
    }

    [MapFrom(typeof(InternalMaskSource))] // T5 复核：Plan 直调输入的承载侧合法形态

    private class InternalMaskDto
    {
        [Mask(typeof(MaskPhone))]
        public string Phone { get; set; } = "";
    }

    private class InternalNoMaskSource
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    private class InternalNoMaskDto
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    /// <summary>
    /// 日本电话脱敏规则（测试 Override 用）
    /// </summary>
    private class JapanPhoneMask : IMask
    {
        public string Mask(string? value)
            => value is { Length: >= 7 } ? $"{value[..3]}-****-{value[^4..]}" : value ?? "";
    }
}
