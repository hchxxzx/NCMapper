using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 内置 IMask 脱敏实现单元测试 — 覆盖正常/null/空串/短串边界
/// <para>类实现 IDisposable：统一在类级清理 MaskProvider 全局注册（C12 隔离三要件统一，
/// 替代各测试方法内的 Clear() + try/finally 清理形态）。</para>
/// </summary>
public class MasksTests : IDisposable
{
    /// <summary>
    /// 类级清理 — 统一清除 MaskProvider 全局注册（C12 治理：方法内清理改类级）
    /// <para>xunit 每个测试方法新建类实例并在结束后调用 Dispose，
    /// 保证全局 MaskProvider 注册表无跨测试残留。</para>
    /// </summary>
    public void Dispose() => MaskProvider.Clear();

    // ===== MaskPhone =====

    [Fact]
    public void MaskPhone_Normal()
        => Assert.Equal("138****5678", new MaskPhone().Mask("13812345678"));

    [Fact]
    public void MaskPhone_Null()
        => Assert.Equal("", new MaskPhone().Mask(null));

    [Fact]
    public void MaskPhone_Empty()
        => Assert.Equal("", new MaskPhone().Mask(""));

    [Fact]
    public void MaskPhone_Short()
        => Assert.Equal("1****", new MaskPhone().Mask("123456")); // A3-11（Q2=②）：短串降级掩码

    /// <summary>
    /// 阈值下界（M-24）：7 位恰等于 keepFront(3)+keepBack(4)，保留段覆盖全串 →
    /// 原实现输出 "123****4567"（4 星纯装饰、原文全量泄漏），修复后走降级掩码 fail-safe。
    /// </summary>
    [Fact]
    public void MaskPhone_ExactlyMinLength()
        => Assert.Equal("1****", new MaskPhone().Mask("1234567")); // M-24：≤7 位降级掩码

    /// <summary>
    /// 阈值上界（M-24）：8 位为可真实遮蔽的最小长度（中间 1 位被 4 星替换）。
    /// </summary>
    [Fact]
    public void MaskPhone_JustAboveMinLength()
        => Assert.Equal("123****5678", new MaskPhone().Mask("12345678"));

    // ===== MaskIdCard =====

    [Fact]
    public void MaskIdCard_18Digits()
        => Assert.Equal("1101**********1234", new MaskIdCard().Mask("110101199001011234"));

    [Fact]
    public void MaskIdCard_15Digits()
        => Assert.Equal("1101*******1012", new MaskIdCard().Mask("110101900101012"));

    [Fact]
    public void MaskIdCard_Null()
        => Assert.Equal("", new MaskIdCard().Mask(null));

    [Fact]
    public void MaskIdCard_Empty()
        => Assert.Equal("", new MaskIdCard().Mask(""));

    [Fact]
    public void MaskIdCard_Short()
        => Assert.Equal("1****", new MaskIdCard().Mask("123456789")); // A3-11（Q2=②）：短串降级掩码

    // ===== MaskBankCard =====

    [Fact]
    public void MaskBankCard_Normal()
        => Assert.Equal("6222****1234", new MaskBankCard().Mask("6222021234561234"));

    [Fact]
    public void MaskBankCard_Null()
        => Assert.Equal("", new MaskBankCard().Mask(null));

    [Fact]
    public void MaskBankCard_Empty()
        => Assert.Equal("", new MaskBankCard().Mask(""));

    [Fact]
    public void MaskBankCard_Short()
        => Assert.Equal("1****", new MaskBankCard().Mask("123456789")); // A3-11（Q2=②）：短串降级掩码

    [Fact]
    public void MaskBankCard_ExactlyMinLength()
        => Assert.Equal("1234****7890", new MaskBankCard().Mask("1234567890"));

    // ===== MaskEmail =====

    [Fact]
    public void MaskEmail_Normal()
        => Assert.Equal("z***@example.com", new MaskEmail().Mask("zhangsan@example.com"));

    [Fact]
    public void MaskEmail_Null()
        => Assert.Equal("", new MaskEmail().Mask(null));

    [Fact]
    public void MaskEmail_Empty()
        => Assert.Equal("", new MaskEmail().Mask(""));

    [Fact]
    public void MaskEmail_NoAt()
        => Assert.Equal("n****", new MaskEmail().Mask("noemail")); // A3-11（Q2=②）：无@降级掩码

    [Fact]
    public void MaskEmail_AtStart()
        => Assert.Equal("@****", new MaskEmail().Mask("@example.com")); // A3-11（Q2=②）：@起始降级掩码

    [Fact]
    public void MaskEmail_SingleCharBeforeAt()
        => Assert.Equal("a***@b.com", new MaskEmail().Mask("a@b.com"));

    // ===== MaskChineseName =====

    [Fact]
    public void MaskChineseName_ThreeChars()
        => Assert.Equal("张*丰", new MaskChineseName().Mask("张三丰"));

    [Fact]
    public void MaskChineseName_TwoChars()
        => Assert.Equal("张*", new MaskChineseName().Mask("张三"));

    [Fact]
    public void MaskChineseName_FourChars()
        => Assert.Equal("欧**明", new MaskChineseName().Mask("欧阳修明"));

    [Fact]
    public void MaskChineseName_SingleChar()
        => Assert.Equal("****", new MaskChineseName().Mask("张")); // A3-11（Q2=②）：单字全遮（原明文返回属泄漏）

    [Fact]
    public void MaskChineseName_Null()
        => Assert.Equal("", new MaskChineseName().Mask(null));

    [Fact]
    public void MaskChineseName_Empty()
        => Assert.Equal("", new MaskChineseName().Mask(""));

    /// <summary>
    /// 验证含 CJK Ext B+ 生僻字（U+20000+，代理对占 2 char）的姓名脱敏不切断代理对
    /// </summary>
    [Fact]
    public void MaskChineseName_SurrogatePair_CjkExtB()
    {
        // "𠀀" (U+20000) = 代理对 \uD840\uDC00，2 个 char 但 1 个文本元素
        // 三字名 "𠀀字三" → 应返回 "𠀀*三"（保留首尾文本元素，中间替换为 *）
        // 长度判断用 StringInfo.LengthInTextElements = 3
        var result = new MaskChineseName().Mask("𠀀字三");
        Assert.Equal("𠀀*三", result);
    }

    [Fact]
    public void MaskChineseName_SurrogatePair_TwoChar()
    {
        // 两字名含代理对首字: "𠀀明" → "𠀀*"
        var result = new MaskChineseName().Mask("𠀀明");
        Assert.Equal("𠀀*", result);
    }

    // ===== MaskAddress =====

    [Fact]
    public void MaskAddress_Normal()
        => Assert.Equal("北京市朝阳区****", new MaskAddress().Mask("北京市朝阳区建国路88号"));

    [Fact]
    public void MaskAddress_Null()
        => Assert.Equal("", new MaskAddress().Mask(null));

    [Fact]
    public void MaskAddress_Empty()
        => Assert.Equal("", new MaskAddress().Mask(""));

    [Fact]
    public void MaskAddress_Short()
        => Assert.Equal("北****", new MaskAddress().Mask("北京市朝阳")); // A3-11（Q2=②）：短串降级掩码

    [Fact]
    public void MaskAddress_ExactlySix()
        => Assert.Equal("北****", new MaskAddress().Mask("北京市朝阳区")); // A3-11（Q2=②）：≤6 降级掩码

    // ===== MaskIp =====

    [Fact]
    public void MaskIp_Normal()
        => Assert.Equal("192.168.*.*", new MaskIp().Mask("192.168.1.100"));

    [Fact]
    public void MaskIp_Null()
        => Assert.Equal("", new MaskIp().Mask(null));

    [Fact]
    public void MaskIp_Empty()
        => Assert.Equal("", new MaskIp().Mask(""));

    [Fact]
    public void MaskIp_InvalidFormat()
        => Assert.Equal("n****", new MaskIp().Mask("not.an.ip")); // A3-11（Q2=②）：非IPv4降级掩码

    [Fact]
    public void MaskIp_TwoSegments()
        => Assert.Equal("1****", new MaskIp().Mask("192.168")); // A3-11（Q2=②）：段数≠4降级掩码

    // ===== MaskLicensePlate =====

    [Fact]
    public void MaskLicensePlate_Normal7()
        => Assert.Equal("京A***45", new MaskLicensePlate().Mask("京A12345"));

    [Fact]
    public void MaskLicensePlate_NewEnergy8()
        => Assert.Equal("京A****45", new MaskLicensePlate().Mask("京AD12345"));

    [Fact]
    public void MaskLicensePlate_Null()
        => Assert.Equal("", new MaskLicensePlate().Mask(null));

    [Fact]
    public void MaskLicensePlate_Empty()
        => Assert.Equal("", new MaskLicensePlate().Mask(""));

    [Fact]
    public void MaskLicensePlate_Short()
        => Assert.Equal("京****", new MaskLicensePlate().Mask("京A12")); // A3-11（Q2=②）：短串降级掩码

    // ===== MaskLandline =====

    [Fact]
    public void MaskLandline_WithDash()
        => Assert.Equal("010-****5678", new MaskLandline().Mask("010-12345678"));

    [Fact]
    public void MaskLandline_WithoutDash()
        => Assert.Equal("010****5678", new MaskLandline().Mask("01012345678"));

    [Fact]
    public void MaskLandline_Null()
        => Assert.Equal("", new MaskLandline().Mask(null));

    [Fact]
    public void MaskLandline_Empty()
        => Assert.Equal("", new MaskLandline().Mask(""));

    [Fact]
    public void MaskLandline_ShortLocalPart()
        => Assert.Equal("0****", new MaskLandline().Mask("010-1234")); // A3-11（Q2=②）：localPart≤4降级掩码

    [Fact]
    public void MaskLandline_ShortNoDash()
        => Assert.Equal("1****", new MaskLandline().Mask("123456")); // A3-11（Q2=②）：短串降级掩码

    // ===== MaskAttribute IProjectionPolicy =====

    [Fact]
    public void MaskAttribute_Implements_IProjectionPolicy()
    {
        var attr = new Attributes.MaskAttribute(typeof(MaskPhone));
        Assert.True(attr is IProjectionPolicy { RequiresDowngrade: true });
    }

    [Fact]
    public void MaskAttribute_Stores_MaskType()
    {
        var attr = new Attributes.MaskAttribute(typeof(MaskEmail));
        Assert.Equal(typeof(MaskEmail), attr.MaskType);
    }

    [Fact]
    public void MaskAttribute_NullType_Throws()
        => Assert.Throws<ArgumentNullException>(() => new Attributes.MaskAttribute(null!));

    // ===== MaskProvider 并发安全测试 =====

    /// <summary>
    /// 多线程并发 Override 不同 TMask 类型，无异常无损坏（C28 补测：结果守恒断言替代常量断言）
    /// <para>全局状态清理由类级 Dispose 统一承担（C12），方法内不再 try/finally Clear。
    /// 原常量断言 Assert.True(true)（TC-004-1 发现 F4）已替换为结果守恒断言：
    /// 并发 Override 后各类型 Resolve 均返回有效实例（ConcurrentDictionary 结构不被并发破坏）。</para>
    /// </summary>
    [Fact]
    public async Task MaskProvider_ConcurrentOverride_DifferentTypes()
    {
        var tasks = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                if (idx % 2 == 0)
                    MaskProvider.Override<MaskPhone>(new MaskPhone());
                else
                    MaskProvider.Override<MaskEmail>(new MaskEmail());
            }, TestContext.Current.CancellationToken));
        }
        await Task.WhenAll(tasks.ToArray());

        // 结果守恒断言（C28）：并发后各类型 Resolve 均返回有效实例，无损坏
        var phone = MaskProvider.Resolve<MaskPhone>();
        var email = MaskProvider.Resolve<MaskEmail>();
        Assert.NotNull(phone);
        Assert.NotNull(email);
        Assert.IsType<MaskPhone>(phone);
        Assert.IsType<MaskEmail>(email);
    }

    /// <summary>
    /// 多线程并发 Override 相同 TMask 类型，最终值正确
    /// <para>全局状态清理由类级 Dispose 统一承担（C12），方法内不再 try/finally Clear。</para>
    /// </summary>
    [Fact]
    public async Task MaskProvider_ConcurrentOverride_SameType()
    {
        var barrier = new Barrier(4);
        var tasks = new Task[4];
        var replacements = new IMask[]
        {
            new MaskPhone(),
            new MaskPhone(),
            new MaskPhone(),
            new MaskPhone()
        };

        for (var i = 0; i < 4; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                MaskProvider.Override<MaskPhone>(replacements[idx]);
            }, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);
        // 所有线程完成后，Resolve 应返回有效的 IMask（不抛异常）
        var resolved = MaskProvider.Resolve<MaskPhone>();
        Assert.NotNull(resolved);
        Assert.IsType<MaskPhone>(resolved);
    }
}
