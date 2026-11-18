using NCMapper.Attributes;

namespace NCMapper.Tests.Models
{
    // ================================================================
    // 生成侧核验模型（006 决策树分支②）
    // 双引擎对照：B03GenRecord（[MapFrom] → 生成侧 DeepCopy，record 路径构参外
    // 属性填充）vs B03RuntimeRecord（无标注 → 运行时 Provider 占位路径）。
    // ================================================================

    /// <summary>
    /// 生成侧核验源 — 任意同名属性形态（生成侧 DeepCopy 的触发载体）
    /// </summary>
    public class B03GenRecordSource
    {
        /// <summary>与 B03GenRecord 构参同名匹配的属性</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 双引擎对照 record DTO — [MapFrom] 标注触发生成侧 DeepCopy（record 构造器
    /// 路径）；构参 Name + 构参外可写属性 Note（生成侧修复前 Note 静默丢失 → 同构核验锚点）
    /// </summary>
    [MapFrom(typeof(B03GenRecordSource))]
    public record B03GenRecord(string Name)
    {
        /// <summary>构参外可写属性 — 生成侧填充与运行时行为一致性对照锚点</summary>
        public string? Note { get; set; }
    }

    /// <summary>
    /// 运行时对照 record — 无 [MapFrom]（不触发生成侧，走 Fast → base 占位路径：
    /// 统一转发 + 占位回拷 + 构参外属性补填）
    /// </summary>
    public record B03RuntimeRecord(string Name)
    {
        /// <summary>构参外可写属性 — 运行时路径对照锚点</summary>
        public string? Note { get; set; }
    }
}
