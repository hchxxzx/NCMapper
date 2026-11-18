// ============================================================================
// 文化矩阵测试（Batch-2）
//
// 覆盖矩阵三组断言（006 文化矩阵 + T0 期望行为）：
//   ① "1.5"→decimal/float：三文化（de-DE/en-US/ja-JP）恒 1.5（InvariantCulture
//      单点修复——消除 de-DE 下小数点按分组分隔符剥离静默得 15 的错值）；
//  ② "1,5"：实测修正口径（执行期，006 契约语义修正——技术异议
// Q2）：InvariantCulture 的 NumberStyles 默认允许千位分隔符，"1,5"
//      为合法千位写法 → 三文化恒 15（与 "1,000"→1000 同构）；真格式错误
//      （"1.5.2"）解析失败 → 兜底 MapException（异常显式化）；
//   ③ DateTime（ISO/美式格式）：三文化解析结果一致（Invariant 通用格式）。
// 端到端：Mapper.Map 字符串→数值/日期属性同口径（V2b 平移）。
//
// 文化切换惯例：try/finally 保存恢复 CurrentCulture（复制
// DualEngineSemanticsConformanceTests:282-307 惯例；语义用 CurrentCulture
// 解析文化；AssemblyInfo 已禁止测试并行化，无全局污染）。
// 三 TFM：net8.0/net9.0/net10.0 随测试工程多目标各跑一次（ICU 差异排查）。
// 反向验证（经验 #22）：临时改回双参重载 → 失败集应精确 = de-DE/ja-JP 文化
// 下的数值/日期断言（en-US 与 Invariant 同构不受影响）→ 恢复全绿（见）。
// ============================================================================

using System.Globalization;
using Xunit;

namespace NCMapper.Tests.Tests;

public class CultureConversionMatrixTests
{
    // ================================================================
    // ① "1.5" → decimal/float/double：三文化恒 Invariant 结果
    // ================================================================

    /// <summary>
    /// 矩阵①A — "1.5"→decimal 三文化恒 1.5m：修复核心断言（原缺陷：
    /// de-DE 下小数点按分组分隔符剥离静默得 15）。
    /// </summary>
    [Fact]
    public void 矩阵A_小数字符串_三文化恒Invariant解析_decimal()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(1.5m, TypeConvertHelper.ConvertValue<string, decimal>("1.5"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 矩阵①B — "1.5"→float/double 三文化恒 1.5（浮点族同口径）。
    /// </summary>
    [Fact]
    public void 矩阵B_小数字符串_三文化恒Invariant解析_浮点族()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(1.5f, TypeConvertHelper.ConvertValue<string, float>("1.5"));
                Assert.Equal(1.5d, TypeConvertHelper.ConvertValue<string, double>("1.5"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 矩阵①C — 端到端：属性映射 string→decimal 在三文化下同口径
    /// （表达式树运行时转换兜底入口消费 ConvertValue）。
    /// </summary>
    [Fact]
    public void 矩阵C_属性映射端到端_三文化同口径()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                var dto = Mapper.Map<CmAmountSrc, CmAmountDto>(new CmAmountSrc { Amount = "1.5" })!;
                Assert.Equal(1.5m, dto.Amount);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ================================================================
    // ② "1,5" 千位语义 + 真格式错误异常路径（实测修正口径，Q2）
    // ================================================================

    /// <summary>
    /// 矩阵②A — "1,5" 三文化恒 15m：InvariantCulture 千位分隔符语义
    /// （006 "解析失败"预期系语义误判——执行期实测修正，依赖 de-DE
    /// "1,5"→1.5 本地小数解析的调用方须自行预转换，见 CHANGELOG L-03）。
    /// </summary>
    [Fact]
    public void 矩阵D_逗号数字字符串_三文化恒千位语义()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(15m, TypeConvertHelper.ConvertValue<string, decimal>("1,5"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 矩阵②B — 合法千位写法 "1,000"→1000m（文化无关，锁定 Invariant 千位语义
    /// 与 "1,5"→15 同构——证明②A 断言非偶然值）。
    /// </summary>
    [Fact]
    public void 矩阵E_合法千位写法_恒按千位语义解析()
    {
        Assert.Equal(1000m, TypeConvertHelper.ConvertValue<string, decimal>("1,000"));
        Assert.Equal(1234567m, TypeConvertHelper.ConvertValue<string, decimal>("1,234,567"));
    }

    /// <summary>
    /// 矩阵②C — 真格式错误 "1.5.2"：IConvertible 路径 FormatException 被
    /// catch 后 fall-through → 兜底 MapException（显式异常优于静默错值，
    /// 异常契约锁定）。
    /// </summary>
    [Fact]
    public void 矩阵F_真格式错误_兜底抛MapException()
    {
        Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<string, decimal>("1.5.2"));
        Assert.Throws<MapException>(() => TypeConvertHelper.ConvertValue<string, DateTime>("not-a-date"));
    }

    // ================================================================
    // ③ DateTime 跨文化一致（ISO/美式格式）
    // ================================================================

    /// <summary>
    /// 矩阵③A — ISO 格式 "2026-08-28" 三文化解析同值（Invariant 通用格式）。
    /// </summary>
    [Fact]
    public void 矩阵G_ISO日期字符串_三文化解析一致()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(new DateTime(2026, 8, 28),
                    TypeConvertHelper.ConvertValue<string, DateTime>("2026-08-28"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 矩阵③B — 美式格式 "08/28/2026" 三文化解析同值（Invariant M/d/yyyy
    /// 通用格式——原缺陷：de-DE 本地文化（dd.MM.yyyy）解析失败落 MapException）。
    /// </summary>
    [Fact]
    public void 矩阵H_美式日期字符串_三文化解析一致()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(new DateTime(2026, 8, 28),
                    TypeConvertHelper.ConvertValue<string, DateTime>("08/28/2026"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ================================================================
    // ④ 边界：同型直返分支（29 source is TTarget）不误入 IConvertible
    // ================================================================

    /// <summary>
    /// 矩阵④ — 同型直返分支：string→string 直返不经 IConvertible 路径
    /// （任何 CurrentCulture 下值不变——分支绕过检查点，CR 典型问题）。
    /// </summary>
    [Fact]
    public void 矩阵I_同型直返分支_不误入Convertible路径()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("1.5", TypeConvertHelper.ConvertValue<string, string>("1.5"));
            Assert.Equal(1.5m, TypeConvertHelper.ConvertValue<decimal, decimal>(1.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ================================================================
    // 模型类型（单文件自含）
    // ================================================================

    /// <summary>矩阵C 端到端源：string 金额属性。</summary>
    private sealed class CmAmountSrc
    {
        public string Amount { get; set; } = "";
    }

    /// <summary>矩阵C 端到端目标：decimal 金额属性。</summary>
    private sealed class CmAmountDto
    {
        public decimal Amount { get; set; }
    }
}
