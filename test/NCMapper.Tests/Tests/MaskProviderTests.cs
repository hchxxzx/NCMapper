using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// MaskProvider 静态注册表单元测试 — 验证 Override/Resolve/Clear 行为
/// </summary>
public class MaskProviderTests
{
    [Fact]
    public void Resolve_WithoutOverride_ReturnsDefaultInstance()
    {
        MaskProvider.Clear();
        var mask = MaskProvider.Resolve<MaskPhone>();
        Assert.IsType<MaskPhone>(mask);
        Assert.Equal("138****5678", mask.Mask("13812345678"));
    }

    [Fact]
    public void Override_ThenResolve_ReturnsReplacement()
    {
        MaskProvider.Clear();
        var custom = new ReverseMask();
        MaskProvider.Override<MaskPhone>(custom);

        var resolved = MaskProvider.Resolve<MaskPhone>();
        Assert.Same(custom, resolved);
        Assert.Equal("87654321831", resolved.Mask("13812345678"));

        MaskProvider.Clear();
    }

    [Fact]
    public void Override_OneType_DoesNotAffectOther()
    {
        MaskProvider.Clear();
        MaskProvider.Override<MaskPhone>(new ReverseMask());

        var email = MaskProvider.Resolve<MaskEmail>();
        Assert.IsType<MaskEmail>(email);

        MaskProvider.Clear();
    }

    [Fact]
    public void Clear_ResetsAllOverrides()
    {
        MaskProvider.Override<MaskPhone>(new ReverseMask());
        MaskProvider.Override<MaskEmail>(new ReverseMask());

        MaskProvider.Clear();

        Assert.IsType<MaskPhone>(MaskProvider.Resolve<MaskPhone>());
        Assert.IsType<MaskEmail>(MaskProvider.Resolve<MaskEmail>());
    }

    [Fact]
    public void Override_NullReplacement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MaskProvider.Override<MaskPhone>(null!));
    }

    [Fact]
    public void Override_LastWins()
    {
        MaskProvider.Clear();
        var first = new ReverseMask();
        var second = new UpperMask();

        MaskProvider.Override<MaskPhone>(first);
        MaskProvider.Override<MaskPhone>(second);

        Assert.Same(second, MaskProvider.Resolve<MaskPhone>());
        MaskProvider.Clear();
    }

    // ===== 测试用自定义 Mask =====

    private class ReverseMask : IMask
    {
        public string Mask(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var chars = value.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
    }

    private class UpperMask : IMask
    {
        public string Mask(string? value) => value?.ToUpperInvariant() ?? "";
    }
}
