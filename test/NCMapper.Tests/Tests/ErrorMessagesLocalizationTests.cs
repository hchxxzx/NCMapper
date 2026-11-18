using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace NCMapper.Tests.Tests;

/// <summary>
/// ErrorMessages 双语完整性 — 本地化守护（验收 B10：en-US 文化必须验证）
/// <para>① 反射遍历全部公开消息方法：zh-CN 下均含中文、en-US 下均不含中文（纯英文口径）；
/// ② UseEnglish 边界（zh-CN/en-US/en-GB/ja-JP 四文化）；
/// ③ zh 逐字对照抽样（原硬编码文本逐字不变）；④ EN 关键短语抽样（含前既有 Compose 原文）。</para>
/// </summary>
public class ErrorMessagesLocalizationTests
{
    private static readonly Regex CjkRegex = new("[\u4e00-\u9fff]", RegexOptions.Compiled);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCurrent = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUi = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            var culture = new CultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCurrent;
            CultureInfo.CurrentUICulture = _originalUi;
        }
    }

    /// <summary>反射调用单个消息方法（参数按类型给最小占位值：string→"T"、int→1、bool→false、enum→首值；
    /// ReverseWriteBack* 的失败分类参数取反向专用合法值 ReverseWriteBackSegmentNotNavigable——首值 InvalidIndexSyntax 仅正向路径合法）</summary>
    private static string Invoke(MethodInfo method)
    {
        var args = method.GetParameters()
            .Select(p =>
            {
                var t = p.ParameterType;
                if (t == typeof(string)) return (object)"T";
                if (t == typeof(int)) return 1;
                if (t == typeof(bool)) return (object)false; // TreeChildrenPropertyInvalid.isReadOnly
                if (t.IsEnum)
                {
                    if (t == typeof(NCMapper.DotPathFailureKind) && method.Name.StartsWith("ReverseWriteBack", StringComparison.Ordinal))
                        return NCMapper.DotPathFailureKind.ReverseWriteBackSegmentNotNavigable;
                    return Enum.GetValues(t).GetValue(0)!;
                }
                throw new NotSupportedException($"意外的参数类型 {t}（方法 {method.Name}）");
            })
            .ToArray();
        return (string)method.Invoke(null, args)!;
    }

    private static MethodInfo[] PublicMessageMethods()
        => typeof(ErrorMessages)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void AllMethods_ZhCn_AllMessagesContainChinese()
    {
        var methods = PublicMessageMethods();
        Assert.True(methods.Length >= 62, $"ErrorMessages 公开方法数 {methods.Length} 低于预期下限 62（26 既有 + 新增 34 + Q8 1 方法 + TreeChildrenPropertyInvalid 1 方法）");

        using var _ = new CultureScope("zh-CN");
        foreach (var m in methods)
        {
            var msg = Invoke(m);
            Assert.Matches(CjkRegex, msg);
        }
    }

    [Fact]
    public void AllMethods_EnUs_AllMessagesAreNonChinese()
    {
        var methods = PublicMessageMethods();
        Assert.True(methods.Length >= 62, $"ErrorMessages 公开方法数 {methods.Length} 低于预期下限 62（26 既有 + 新增 34 + Q8 1 方法 + TreeChildrenPropertyInvalid 1 方法）");

        using var _ = new CultureScope("en-US");
        foreach (var m in methods)
        {
            var msg = Invoke(m);
            Assert.DoesNotMatch(CjkRegex, msg);
        }
    }

    [Fact]
    public void UseEnglish_Boundary_FourCultures()
    {
        // zh-CN → 中文；en-US → 英文；en-GB → 英文（UseEnglish 按 "en" 前缀）；ja-JP → 中文（非 en 前缀回退 zh 口径）
        using (var _ = new CultureScope("zh-CN"))
            Assert.Matches(CjkRegex, ErrorMessages.PageNumberMustBePositive());
        using (var _ = new CultureScope("en-US"))
            Assert.DoesNotMatch(CjkRegex, ErrorMessages.PageNumberMustBePositive());
        using (var _ = new CultureScope("en-GB"))
            Assert.DoesNotMatch(CjkRegex, ErrorMessages.PageNumberMustBePositive());
        using (var _ = new CultureScope("ja-JP"))
            Assert.Matches(CjkRegex, ErrorMessages.PageNumberMustBePositive());
    }

    [Fact]
    public void ZhVerbatim_SampledMethods_MatchOriginalHardcodedText()
    {
        // zh 逐字不变（原硬编码字面量对照，迁移锚点原文）
        using var _ = new CultureScope("zh-CN");
        Assert.Equal("页码必须 >= 1", ErrorMessages.PageNumberMustBePositive());
        Assert.Equal("每页条数必须 >= 1", ErrorMessages.PageSizeMustBePositive());
        Assert.Equal("每批条数必须 >= 1", ErrorMessages.BatchSizeMustBePositive());
    }

    [Fact]
    public void EnUs_SampledMethods_MatchExpectedPhrases()
    {
        // EN 关键短语（含前既有方法 ComposeSourceNotFound 的原文——EN 分支保留既有英文，逐字对照）
        using var _ = new CultureScope("en-US");
        Assert.Equal("Page number must be >= 1", ErrorMessages.PageNumberMustBePositive());
        Assert.Equal("Page size must be >= 1", ErrorMessages.PageSizeMustBePositive());
        Assert.Equal("Batch size must be >= 1", ErrorMessages.BatchSizeMustBePositive());
        Assert.Equal(
            "Compose<TTarget> could not find a source object matching the primary source type ComposeEntity. The primary source type declared by [MapFrom(typeof(ComposeEntity))] must be passed as one of the arguments.",
            ErrorMessages.ComposeSourceNotFound("ComposeEntity"));
    }
}
