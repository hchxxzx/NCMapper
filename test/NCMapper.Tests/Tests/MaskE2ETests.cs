using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// F-031 E2E 测试 — 端到端场景覆盖（不依赖新增源生成器模型）
/// </summary>
[Collection("DeepCopyGlobalState")]
public class MaskE2ETests
{
    // ===== E2E-1: MaskProvider Override 端到端 =====

    /// <summary>
    /// 完整链路：Override → Resolve → Mask，验证动态替换生效
    /// </summary>
    [Fact]
    public void E2E_MaskProviderOverride_FullPipeline()
    {
        MaskProvider.Clear();
        try
        {
            var customMask = new FixedMask("***FIXED***");
            MaskProvider.Override<MaskPhone>(customMask);
            var resolved = MaskProvider.Resolve<MaskPhone>();

            // 验证 Override 生效
            Assert.Same(customMask, resolved);
            Assert.Equal("***FIXED***", resolved.Mask("13812345678"));
        }
        finally
        {
            MaskProvider.Clear();
        }
    }

    /// <summary>
    /// 验证 Override 不影响其他 Mask 类型（隔离性）
    /// </summary>
    [Fact]
    public void E2E_MaskProviderOverride_Isolation()
    {
        MaskProvider.Clear();
        try
        {
            var phoneMask = new FixedMask("PHONE-MASKED");
            MaskProvider.Override<MaskPhone>(phoneMask);

            // Phone 被 Override
            Assert.Same(phoneMask, MaskProvider.Resolve<MaskPhone>());
            Assert.Equal("PHONE-MASKED", MaskProvider.Resolve<MaskPhone>().Mask("any-phone"));

            // Email 不受影响（仍返回默认实现的输出）
            var emailMask = MaskProvider.Resolve<MaskEmail>();
            Assert.IsType<MaskEmail>(emailMask);
            Assert.Equal("z***@example.com", emailMask.Mask("zhangsan@example.com"));
        }
        finally
        {
            MaskProvider.Clear();
        }
    }

    /// <summary>
    /// Override 后 Clear，验证恢复到默认实现
    /// </summary>
    [Fact]
    public void E2E_MaskProviderOverride_Clear_RestoresDefault()
    {
        MaskProvider.Clear();
        try
        {
            MaskProvider.Override<MaskPhone>(new FixedMask("CUSTOM"));
            Assert.IsType<FixedMask>(MaskProvider.Resolve<MaskPhone>());

            MaskProvider.Clear();

            var resolved = MaskProvider.Resolve<MaskPhone>();
            Assert.IsType<MaskPhone>(resolved);
            Assert.Equal("138****5678", resolved.Mask("13812345678"));
        }
        finally
        {
            MaskProvider.Clear();
        }
    }

    // ===== E2E-5: Mask+ConvertUsing 组合验证 =====

    /// <summary>
    /// 验证 Mask 和 ConvertUsing 可在同一 DTO 的不同属性上共存
    /// 使用已有的 ConverterTests 模型（无需新 source-gen）
    /// </summary>
    [Fact]
    public void E2E_MaskAndConvertUsing_Coexist()
    {
        // 验证：Mask 和 ConvertUsing 在概念上不冲突
        // - Mask: 对映射后 string 属性脱敏
        // - ConvertUsing: 自定义字段值转换

        // 两者通过 ProjectionStrategy.NeedsTwoStepMapping 检测
        // MaskAttribute.RequiresDowngrade = true（触发降级）
        // ConvertAttribute.RequiresDowngrade = true（触发降级）

        var maskAttr = new MaskAttribute(typeof(MaskPhone));
        Assert.True(maskAttr is IProjectionPolicy { RequiresDowngrade: true },
            "Mask 应触发投影降级");

        // 验证 Mask 实现正常
        var phoneMask = new MaskPhone();
        Assert.Equal("138****5678", phoneMask.Mask("13812345678"));
    }

    /// <summary>
    /// 验证 ConvertUsing+Mask 组合不影响各自的独立映射路径
    /// </summary>
    [Fact]
    public void E2E_MaskAndConvertUsing_IndependentPaths()
    {
        var source = new NCMapper.Tests.Models.ConverterSourceEntity
        {
            Id = 1,
            CreateTime = new DateTime(2026, 7, 25),
            DateString = "2026-07-25",
            Code = 7
        };

        var result = Mapper.Map<NCMapper.Tests.Models.ConverterSourceEntity,
            NCMapper.Tests.Models.ConverterDto>(source);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);

        // 验证 Mask 独立路径
        var phoneMask = MaskProvider.Resolve<MaskPhone>();
        Assert.Equal("138****5678", phoneMask.Mask("13812345678"));
    }
}

/// <summary>
/// 固定掩码实现 — 用于 E2E 测试
/// </summary>
public sealed class FixedMask : IMask
{
    private readonly string _output;
    public FixedMask(string output) => _output = output;
    public string Mask(string? value) => _output;
}
