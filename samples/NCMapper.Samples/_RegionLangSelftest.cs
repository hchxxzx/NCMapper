// _RegionLangSelftest.cs — R-7 常驻自证文件（不删除、不入 DFM 引用、不参与中文注释归零范围）
// 用途：check-region-lang.sh 的正向对照——下方 region 必须被报 [CJK_IN_COMMENT]。
// 门禁若对本文件出绿，说明判据有洞（分组键/豁免粒度/字符串剥离失效），属门禁缺陷而非本文件可修。
// 本文件不注册 ScenarioRegistry、无任何场景调用、零运行时行为；region 外的中文注释不受门禁约束（门禁只查 region 内）。
// 形态说明：成员 XML doc 置于 region 外（R-1 合规），region 内仅植入注释泄漏——信号纯净：
// 该 region 只应命中 CJK_IN_COMMENT 违规，不应命中 DOC_IN_REGION warn。
namespace NCMapper.Samples;

/// <summary>
/// 门禁自证宿主类 — 为 check-region-lang.sh 提供常驻正向对照，无业务语义。
/// </summary>
public static class RegionLangSelftest
{
    /// <summary>
    /// 植入探针 — 方法体内含故意保留的中文行内注释（整行 + 行尾两形态各一）。
    /// </summary>
    #region Selftest_ChineseLeak
    public static void Probe()
    {
        // 故意植入的中文注释：本行是门禁正向对照载体，禁止英文化
        _ = 1; // 行尾形态的植入泄漏，同样必须被检出
    }
    #endregion
}
