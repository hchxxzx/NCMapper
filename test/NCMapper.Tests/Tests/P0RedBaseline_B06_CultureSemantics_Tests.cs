// ============================================================================
// Red_B06 — Convert.ChangeType 无 culture：de-DE 下 "1.5"→15 静默错值（Blocker，
//
// 缺陷（006 第 6 项）：TypeConvertHelper.ConvertValue（36）Convert.ChangeType(source,
// underlyingTarget) 无 culture —— 实测 de-DE 下 "1.5"→decimal 静默得 15（小数点被当作
// 分组分隔符丢弃）。
//
// 修复后期望（断言口径，006 / 行为变更登记契约 / 文化矩阵）：
//   ConvertValue 的 IConvertible 主路径改用 InvariantCulture —— "1.5"→decimal 在
//  de-DE/en-US/ja-JP 一致得 1.5m；"1,5" 按千位分隔符语义恒得 15（执行期修正，
//  006 "解析失败抛 MapException" 预期系 Invariant 语义误判——技术异议 Q2，
//   依赖 de-DE "1,5"→1.5 本地小数解析的调用方须自行预转换，见 CHANGELOG L-03）；
//   DateTime ISO/美式格式跨文化一致。端到端：Mapper.Map 字符串→数值/日期属性同口径。
//   三 TFM（net8.0/net9.0/net10.0）随测试工程多目标全覆盖（ICU 行为差异排查）。
//
// 当前基线（e59f1c9）落红形态（留档）：
//   de-DE 下 "1.5"→15（静默错值）、"1,5"→1.5（本地文化解析成功，与迁移后语义相反）、
//   美式日期 "08/28/2026" 按本地文化解析失败 → 兜底抛 MapException → 断言失败 = 预期落红。
// 断言形态平移自审计实测程序 VerifyReview V2/V2b（artifacts/tmp_review_20260827）。
// ============================================================================

using System.Globalization;
using Xunit;

namespace NCMapper.Tests.Tests;

public class Red_B06_CultureSemantics
{
    [Fact]
    public void 跨文化字符串转数值_恒InvariantCulture_一五()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

                // 修复后期望：任何 CurrentCulture 下恒 1.5m（InvariantCulture 单点修复）；
                // 当前形态（留档）：de-DE 下静默得 15（小数点按分组分隔符丢弃）→ 落红
                Assert.Equal(1.5m, TypeConvertHelper.ConvertValue<string, decimal>("1.5"));

                // 端到端（V2b 平移）：属性映射同口径
                var dto = Mapper.Map<B06AmountSrc, B06AmountDto>(new B06AmountSrc { Amount = "1.5" })!;
                Assert.Equal(1.5m, dto.Amount);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 逗号小数字符串_不再按本地文化解析_Invariant口径失败()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // 修复后实测（执行期，006 契约该点语义修正——技术异议 Q2）：
            // InvariantCulture 的 NumberStyles 默认允许千位分隔符，"1,5" 为合法千位写法
            // → 解析成功得 15（与 "1,000"→1000 同构），并非解析失败；de-DE 下原本地
            // 小数语义 1.5 → 现按 Invariant 千位语义 15（值变化而非异常）。依赖本地小数
            // 解析的调用方须自行预转换（迁移指引见 CHANGELOG L-03 修正措辞）
            Assert.Equal(15m, TypeConvertHelper.ConvertValue<string, decimal>("1,5"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 美式日期字符串_跨文化解析_一致成功()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

                // 修复后期望：InvariantCulture（美式 M/d/yyyy 通用格式）口径下跨文化一致解析成功；
                // 当前形态（留档实测）：de-DE 下按本地文化（dd.MM.yyyy）解析 "08/28/2026" 失败
                // → 转换路径兜底抛 MapException → 落红（ISO "2026-08-28" 恰为跨文化兼容格式，
                // 无法暴露分叉，故改用美式格式作区分器）
                Assert.Equal(new DateTime(2026, 8, 28), TypeConvertHelper.ConvertValue<string, DateTime>("08/28/2026"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---------- 模型类型（VerifyReview V2b 形态平移，单文件自含） ----------

    private sealed class B06AmountSrc
    {
        public string Amount { get; set; } = "";
    }

    private sealed class B06AmountDto
    {
        public decimal Amount { get; set; }
    }
}
