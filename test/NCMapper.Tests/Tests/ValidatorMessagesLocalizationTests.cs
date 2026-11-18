using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using NCMapper.Tests.Models;

namespace NCMapper.Tests.Tests;

/// <summary>
/// 验证器消息双语化测试矩阵
/// <para>七维度：文化×报表面 / 文化×ValidateOrThrow / 明细项×文化 /
/// PrintReport×文化 / 生成侧×两侧同文（前缀口径——两侧明细形态本就不同：生成器 ASCII "-"&gt;、
/// 运行时 Unicode "→"+尾词）/ 生成侧基线（既有 RepresentativeSnapshot 快照机制承载，不在本文件）/
/// 输出语言守卫（en-US 结构框架文本无 CJK，降级原因正文除外）。</para>
/// <para>文化切换复用 ErrorMessagesLocalizationTests 的 CultureScope 模式（保存/恢复
/// CurrentCulture + CurrentUICulture，防并行用例串扰）。zh 断言锁运行时链路现状逐字形态
/// （防回归基线）；en 断言锁双语化后目标形态（实现前运行即红——红测先证红）。</para>
/// </summary>
public class ValidatorMessagesLocalizationTests
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

    // ==== fixture（不加 [MapFrom]——不进生成器验证委托，零生成基线影响）====

    private sealed class UnmappedSrc
    {
        public string Name { get; set; } = "";
    }

    private sealed class UnmappedDto
    {
        public string Name { get; set; } = "";
        public int Missing { get; set; }
    }

    /// <summary>类型不兼容 fixture：List&lt;string&gt; → int 无任何转换路径（运行时 TypeCompatView 判不兼容）。
    /// 注意：生成器 NCMAP002 的不兼容判定与运行时不同源（如 decimal→int 生成器报不兼容、
    /// 运行时验证器判兼容——PrecisionEntity→CrossNumericDto 即此形态），故运行时侧用例用本 fixture。</summary>
    private sealed class MismatchSrc
    {
        public List<string> Items { get; set; } = new();
    }

    private sealed class MismatchDto
    {
        public int Items { get; set; }
    }

    /// <summary>运行时验证：UnmappedSrc → UnmappedDto（Missing 无源 → Warn 1 未映射）</summary>
    private static MapValidationResult RuntimeUnmapped()
        => MapValidator.ValidateMapping(typeof(UnmappedSrc), typeof(UnmappedDto));

    /// <summary>运行时验证：MismatchSrc → MismatchDto（Items 类型不兼容 → Error）</summary>
    private static MapValidationResult RuntimeMismatch()
        => MapValidator.ValidateMapping(typeof(MismatchSrc), typeof(MismatchDto));

    /// <summary>手工构造 Error 级降级结果行（PrintReport 快照断言输入；reason 模拟真实中文正文形态）</summary>
    private static MapValidationResult ManualDowngradeError()
        => new()
        {
            SourceType = typeof(UnmappedSrc),
            TargetType = typeof(UnmappedDto),
            Level = MapValidationLevel.Error,
            MappedCount = 0,
            TotalCount = 1,
            TypeMismatchProperties = new List<string> { "Q" },
            Message = "Q",
            RequiresProjectionDowngrade = true,
            ProjectionDowngradeReason = "不可翻译声明（Mask）",
        };

    // ==== A. zh 防回归基线（运行时链路现状逐字；实现后必须仍绿——F2 底线）====

    [Fact]
    public void ToString_Runtime_Zh_AnchoredLiterally()
    {
        using var _ = new CultureScope("zh-CN");
        var result = RuntimeUnmapped();
        Assert.Equal(MapValidationLevel.Warn, result.Level);
        Assert.Equal("⚠️ UnmappedSrc → UnmappedDto: 1/2 属性已映射，1 个未映射：Missing (源字段 Missing 不存在)", result.ToString());
    }

    [Fact]
    public void TypeMismatch_Detail_Zh_Anchored()
    {
        using var _ = new CultureScope("zh-CN");
        var result = RuntimeMismatch();
        Assert.Equal(MapValidationLevel.Error, result.Level);
        Assert.Equal("Items: List`1 → Int32 不兼容", Assert.Single(result.TypeMismatchProperties));
        Assert.Equal("，1 个类型不兼容：Items: List`1 → Int32 不兼容", result.Message);
    }

    [Fact]
    public void PrintReport_Zh_AnchoredSnapshot()
    {
        using var _ = new CultureScope("zh-CN");
        var sw = new StringWriter();
        MapValidator.PrintReport(new List<MapValidationResult> { ManualDowngradeError() }, sw, showOk: true);
        var lines = sw.ToString().Replace("\r\n", "\n").Split('\n');
        Assert.Equal("========== NCMapper 映射验证报告 ==========", lines[0]);
        Assert.Equal("总计 1 个映射对：✅ 0 OK / ⚠️ 0 警告 / ❌ 1 错误 / ⤵ 降级 1 对", lines[1]);
        Assert.Equal("", lines[2]);
        Assert.Equal("  ❌ UnmappedSrc → UnmappedDto: 0/1 属性已映射Q ⤵ ORM投影降级", lines[3]);
        Assert.Equal("    ⤵ 降级原因: 不可翻译声明（Mask）", lines[4]);
        Assert.Equal("==========================================", lines[5]);
    }

    [Fact]
    public void ValidateOrThrow_Zh_OuterChinese()
    {
        EnsureGeneratedValidateRegistered();
        using var _ = new CultureScope("zh-CN");
        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.ValidateOrThrow());
        // 外层消息（ErrorMessages.MappingValidationFailed zh 分支）随文化，本任务不改动
        Assert.Contains("映射验证失败，发现", ex.Message);
        Assert.Contains("PrecisionEntity→CrossNumericDto: ", ex.Message);
    }

    // ==== B. en 目标形态（实现前运行即红——红测先证红）====

    [Fact]
    public void ToString_En_EnglishForm()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal("⚠️ UnmappedSrc → UnmappedDto: 1/2 properties mapped, 1 unmapped: Missing (source field Missing not found)", RuntimeUnmapped().ToString());
    }

    [Fact]
    public void TypeMismatch_Detail_En()
    {
        using var _ = new CultureScope("en-US");
        var result = RuntimeMismatch();
        Assert.Equal("Items: List`1 → Int32 incompatible", Assert.Single(result.TypeMismatchProperties));
        Assert.Equal(", 1 incompatible type(s): Items: List`1 → Int32 incompatible", result.Message);
    }

    [Fact]
    public void PrintReport_En_StructureEnglish()
    {
        using var _ = new CultureScope("en-US");
        var sw = new StringWriter();
        MapValidator.PrintReport(new List<MapValidationResult> { ManualDowngradeError() }, sw, showOk: true);
        var lines = sw.ToString().Replace("\r\n", "\n").Split('\n');
        Assert.Equal("========== NCMapper Mapping Validation Report ==========", lines[0]);
        Assert.Equal("Total 1 mapping pair(s): ✅ 0 OK / ⚠️ 0 warning(s) / ❌ 1 error(s) / ⤵ downgraded 1 pair(s)", lines[1]);
        Assert.Equal("  ❌ UnmappedSrc → UnmappedDto: 0/1 properties mappedQ ⤵ ORM projection downgraded", lines[3]);
        Assert.Equal("    ⤵ Downgrade reason: 不可翻译声明（Mask）", lines[4]);
        Assert.Equal("==========================================", lines[5]);
    }

    [Fact]
    public void ValidateOrThrow_En_NoDanglingPunctuation()
    {
        EnsureGeneratedValidateRegistered();
        using var _ = new CultureScope("en-US");
        var ex = Assert.Throws<MapConfigurationException>(() => Mapper.ValidateOrThrow());
        Assert.Contains("Mapping validation failed", ex.Message);
        // TrimStart 剥离契约（字符集 {'，', ',', ' '}）：类型对 ": " 后紧跟明细正文，无悬空标点
        Assert.Contains("PrecisionEntity→CrossNumericDto: 1 incompatible type(s):", ex.Message);
        Assert.DoesNotContain(": ,", ex.Message);
        Assert.DoesNotContain(": ，", ex.Message);
    }

    // ==== C. 生成侧（Mapper.Validate() 层1 = 生成对结果；固定英文不随文化——R3 混排形态①）====

    /// <summary>重建本程序集的真实生成验证注册——既有测试可能调 Mapper.ClearGenerated() 清空
    /// s_generatedValidateFuncs，使 Mapper.Validate() 回退运行时反射路径（生成对行消失）。
    /// 注册的还是生成器真产物（GeneratedMapper.ValidateGenerated），非模拟，断言语义不变。</summary>
    private static void EnsureGeneratedValidateRegistered()
        => Mapper.RegisterGeneratedValidate(NCMapper.Generated.GeneratedMapper.ValidateGenerated);

    [Fact]
    public void Generated_Message_En_FixedEnglishForm()
    {
        EnsureGeneratedValidateRegistered();
        using var _ = new CultureScope("en-US");
        var row = Mapper.Validate().FirstOrDefault(r =>
            r.SourceType == typeof(PrecisionEntity) && r.TargetType == typeof(CrossNumericDto));
        Assert.NotNull(row);
        // 生成器明细为 ASCII "->"（CodeGenerator.Validate.cs NCMAP002 拼法），与运行时 Unicode "→" 不同形态
        Assert.Equal(", 1 incompatible type(s): PreciseDecimal: Int32 -> Decimal", row!.Message);
    }

    [Fact]
    public void Generated_Message_Zh_AlsoEnglish()
    {
        EnsureGeneratedValidateRegistered();
        using var _ = new CultureScope("zh-CN");
        var row = Mapper.Validate().FirstOrDefault(r =>
            r.SourceType == typeof(PrecisionEntity) && r.TargetType == typeof(CrossNumericDto));
        Assert.NotNull(row);
        Assert.StartsWith(", 1 incompatible type(s): ", row!.Message);
    }

    /// <summary>两侧同文（前缀口径）：en-US 下生成对与运行时反射对的 Message
    /// 前缀形态一致（", 1 incompatible type(s): "）。两侧明细形态不同（生成器 ASCII "-&gt;" 纯名、
    /// 运行时 Unicode "→"+尾词），且运行时与生成器的不兼容判定不同源（decimal→int 生成器报、
    /// 运行时判兼容）——故两侧各用自己通道的真实不兼容对，分别断言前缀。</summary>
    [Fact]
    public void Generated_MatchesRuntime_Prefix_InEn()
    {
        EnsureGeneratedValidateRegistered();
        using var _ = new CultureScope("en-US");
        var gen = Mapper.Validate().FirstOrDefault(r =>
            r.SourceType == typeof(PrecisionEntity) && r.TargetType == typeof(CrossNumericDto));
        var runtime = RuntimeMismatch();
        Assert.NotNull(gen);
        Assert.StartsWith(", 1 incompatible type(s): ", gen!.Message);
        Assert.StartsWith(", 1 incompatible type(s): ", runtime.Message);
    }

    // ==== D. 输出语言守卫（en-US 结构框架文本无 CJK；降级原因正文除外——缩界非目标）====

    [Fact]
    public void PrintReport_En_NoCjk_InStructure()
    {
        using var _ = new CultureScope("en-US");
        // 真实反射路径：运行时验证测试程序集全量 [MapFrom] 对 + 手工降级行（覆盖降级原因行豁免形态）
        var results = MapValidator.Validate(typeof(ValidatorMessagesLocalizationTests).Assembly);
        results.Add(ManualDowngradeError());
        var sw = new StringWriter();
        MapValidator.PrintReport(results, sw, showOk: true);
        var structureLines = sw.ToString()
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(l => !l.Contains("⤵ Downgrade reason:"))
            .ToArray();
        Assert.All(structureLines, l => Assert.False(CjkRegex.IsMatch(l), $"en 输出结构行含 CJK：{l}"));
    }
}
